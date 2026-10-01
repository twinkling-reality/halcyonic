using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class SpeechClipTests
{
    private static float[] Tone(double seconds, int rate, int channels, double hertz, float amplitude)
    {
        var frames = (int)(seconds * rate);
        var samples = new float[frames * channels];
        for (var frame = 0; frame < frames; frame++)
        {
            var value = (float)(amplitude * Math.Sin(2 * Math.PI * hertz * frame / rate));
            for (var channel = 0; channel < channels; channel++) samples[frame * channels + channel] = value;
        }
        return samples;
    }

    private static short[] SamplesOf(byte[] wav)
    {
        var samples = new short[(wav.Length - 44) / 2];
        for (var index = 0; index < samples.Length; index++) samples[index] = BitConverter.ToInt16(wav, 44 + index * 2);
        return samples;
    }

    [Test]
    public void WritesSixteenKilohertzMonoPcmInTheHeaderTheControlPlaneReads()
    {
        var wav = SpeechClip.Encode(Tone(1, 16000, 1, 440, 0.5f), 16000, 1, 16000)!;
        Assert.That(Encoding.ASCII.GetString(wav, 0, 4), Is.EqualTo("RIFF"));
        Assert.That(BitConverter.ToInt32(wav, 4), Is.EqualTo(wav.Length - 8));
        Assert.That(Encoding.ASCII.GetString(wav, 8, 8), Is.EqualTo("WAVEfmt "));
        Assert.That(BitConverter.ToInt16(wav, 20), Is.EqualTo(1), "PCM");
        Assert.That(BitConverter.ToInt16(wav, 22), Is.EqualTo(1), "mono");
        Assert.That(BitConverter.ToInt32(wav, 24), Is.EqualTo(16000));
        Assert.That(BitConverter.ToInt16(wav, 34), Is.EqualTo(16), "bits");
        Assert.That(Encoding.ASCII.GetString(wav, 36, 4), Is.EqualTo("data"));
        Assert.That(BitConverter.ToInt32(wav, 40), Is.EqualTo(32000));
        var samples = SamplesOf(wav);
        Assert.That(samples[4], Is.EqualTo((short)Math.Round(0.5 * Math.Sin(2 * Math.PI * 440 * 4 / 16000) * short.MaxValue)));
    }

    [TestCase(48000, 2)]
    [TestCase(44100, 1)]
    [TestCase(8000, 1)]
    public void ResamplesAndMixesWhatTheMicrophoneGivesKeepingThePitchAndLevel(int rate, int channels)
    {
        var tone = Tone(2, rate, channels, 300, 0.5f);
        var samples = SamplesOf(SpeechClip.Encode(tone, tone.Length, channels, rate)!);
        Assert.That(samples.Length, Is.EqualTo(32000).Within(1));
        var crossings = 0;
        for (var index = 1; index < samples.Length; index++)
        {
            if (samples[index - 1] < 0 && samples[index] >= 0) crossings++;
        }
        Assert.That(crossings, Is.EqualTo(600).Within(2), "300 Hz for two seconds");
        var rms = Math.Sqrt(samples.Average(sample => (double)sample * sample)) / short.MaxValue;
        Assert.That(rms, Is.EqualTo(0.5 / Math.Sqrt(2)).Within(0.02));
    }

    [Test]
    public void LessThanHalfASecondIsNoClipAndPastThirtySecondsIsLeftOut()
    {
        Assert.That(SpeechClip.Encode(new float[7999], 7999, 1, 16000), Is.Null);
        Assert.That(SpeechClip.Encode(new float[8000], 8000, 1, 16000)!.Length, Is.EqualTo(44 + 16000));
        var long_ = new float[31 * 48000];
        var wav = SpeechClip.Encode(long_, long_.Length, 1, 48000)!;
        Assert.That(wav.Length, Is.EqualTo(SpeechClip.MaxBytes));
        Assert.That(SpeechClip.MaxBytes, Is.EqualTo(960044), "the control plane's TRANSCRIPTION_MAX_BYTES");
    }

    [Test]
    public void UsesOnlyTheSamplesCapturedAndClampsWhatIsTooLoud()
    {
        var samples = Enumerable.Repeat(2f, 16000).Concat(Enumerable.Repeat(-2f, 16000)).ToArray();
        var wav = SamplesOf(SpeechClip.Encode(samples, 16000, 1, 16000)!);
        Assert.That(wav.Length, Is.EqualTo(16000));
        Assert.That(wav.All(sample => sample == short.MaxValue), Is.True);
        Assert.That(SamplesOf(SpeechClip.Encode(samples, 32000, 1, 16000)!).Last(), Is.EqualTo(-short.MaxValue));
    }
}

public class TranscriptionTests
{
    private static readonly byte[] Second = SpeechClip.Encode(new float[16000], 16000, 1, 16000)!;

    [Test]
    public async Task PostsTheClipAsAudioWavAndReadsWhatWasHeard()
    {
        var handler = new ControlPlaneApiTests.CannedHandler(
            HttpStatusCode.OK,
            """{"outcome":"heard","text":"Add a contact form.","language":"en","engine":{"name":"whisper.cpp","version":"1.9.4"}}""");
        using var api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", handler);
        var heard = (HeardTranscription)await api.TranscribeAsync(Second);
        Assert.That(heard.Text, Is.EqualTo("Add a contact form."));
        Assert.That(heard.Language, Is.EqualTo("en"));
        Assert.That(heard.Engine.Name, Is.EqualTo("whisper.cpp"));
        var request = handler.Requests.Single();
        Assert.That(request.Method.Method, Is.EqualTo("POST"));
        Assert.That(request.RequestUri, Is.EqualTo(new Uri("http://127.0.0.1:47800/api/transcriptions")));
        Assert.That(request.Content!.Headers.ContentType!.MediaType, Is.EqualTo("audio/wav"));
        Assert.That(await request.Content.ReadAsByteArrayAsync(), Is.EqualTo(Second));
    }

    [Test]
    public async Task NothingHeardIsAnAnswerAndARefusalCarriesItsCode()
    {
        var nothing = new ControlPlaneApiTests.CannedHandler(
            HttpStatusCode.OK, """{"outcome":"nothing_heard","engine":{"name":"whisper.cpp","version":"1.9.4"}}""");
        using (var api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", nothing))
        {
            Assert.That(await api.TranscribeAsync(Second), Is.InstanceOf<NothingHeardTranscription>());
        }
        var busy = new ControlPlaneApiTests.CannedHandler(
            HttpStatusCode.ServiceUnavailable,
            """{"error":{"code":"transcription_busy_on_mac","message":"The Mac is transcribing another clip.","issues":[]}}""");
        using (var api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", busy))
        {
            var refused = Assert.ThrowsAsync<ControlPlaneRequestException>(() => api.TranscribeAsync(Second))!;
            Assert.That(refused.Code, Is.EqualTo("transcription_busy_on_mac"));
            Assert.That(VoiceText.Refusal(refused.Code), Is.EqualTo("Your Mac is hearing another clip. Try again in a moment."));
        }
    }

    [Test]
    public void EveryRefusalTheRouteGivesHasWordsThatOfferTypingOrTryingAgain()
    {
        var codes = new[]
        {
            "transcription_unavailable", "transcription_busy", "transcription_busy_on_mac", "rate_limited",
            "transcription_failed", "invalid_audio", "audio_too_short", "audio_too_long", "payload_too_large",
            "unsupported_media_type", "something_new", null,
        };
        foreach (var code in codes)
        {
            var words = VoiceText.Refusal(code);
            Assert.That(words.ToLowerInvariant(), Does.Contain("typ").Or.Contain("try again"), code ?? "null");
            Assert.That(words, Does.Not.Contain("\u2014"));
        }
        Assert.That(VoiceText.Refusal(null), Is.EqualTo(VoiceText.Unreachable));
    }

    [Test]
    public void NoMicrophoneNeverAsksToAllowOneAndARefusalSaysWhereToAllowIt()
    {
        Assert.That(VoiceText.NoMicrophone, Does.Not.Contain("Allow"));
        Assert.That(VoiceText.MicrophoneRefused, Does.Contain("settings"));
        Assert.That(VoiceText.Shown, Does.Contain(VoiceText.NoMicrophone).And.Contain(VoiceText.MicrophoneRefused));
    }
}

public class LiveTranscriptionTests
{
    private static readonly byte[] Second = SpeechClip.Encode(new float[16000], 16000, 1, 16000)!;
    private string dataDir = null!;

    [SetUp]
    public void SetUp() => dataDir = Directory.CreateTempSubdirectory("halcyonic-voice-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(dataDir, recursive: true);

    [Test]
    public async Task WithoutVoiceSetUpTheMacSaysSo()
    {
        using var controlPlane = await ControlPlaneProcess.StartAsync(dataDir, ControlPlaneProcess.FreePort());
        using var api = new ControlPlaneApi(ControlPlaneApi.BaseUriFor(controlPlane.RealtimeEndpoint), controlPlane.AccessToken);
        var refused = Assert.ThrowsAsync<ControlPlaneRequestException>(() => api.TranscribeAsync(Second))!;
        Assert.That(refused.Code, Is.EqualTo("transcription_unavailable"));
    }

    [Test]
    public async Task AClipBecomesADraftThroughARealControlPlane()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("The stand-in for whisper-cli is a script run through its #! line.");
        var node = Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, "node"))
            .First(File.Exists);
        var whisper = Path.Combine(dataDir, "whisper-cli");
        File.WriteAllText(whisper, $$"""
            #!{{node}}
            if (process.argv[2] === '--version') { process.stdout.write('whisper.cpp version: 1.9.4-dev\n'); process.exit(0); }
            process.stdout.write(' Add a contact form to the home page.\n');
            """);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(whisper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var model = Path.Combine(dataDir, "model.bin");
        File.WriteAllText(model, string.Empty);
        var environment = new Dictionary<string, string>
        {
            ["HALCYONIC_WHISPER_BIN"] = whisper,
            ["HALCYONIC_WHISPER_MODEL"] = model,
            ["HALCYONIC_WHISPER_VAD_MODEL"] = model,
        };
        using var controlPlane = await ControlPlaneProcess.StartAsync(dataDir, ControlPlaneProcess.FreePort(), environment: environment);
        using var api = new ControlPlaneApi(ControlPlaneApi.BaseUriFor(controlPlane.RealtimeEndpoint), controlPlane.AccessToken);
        TranscriptionResponse? answer = null;
        // The control plane warms the engine once at startup and holds the Mac while it does.
        for (var attempt = 0; attempt < 50 && answer == null; attempt++)
        {
            try
            {
                answer = await api.TranscribeAsync(Second);
            }
            catch (ControlPlaneRequestException busy) when (busy.Code == "transcription_busy_on_mac")
            {
                await Task.Delay(100);
            }
        }
        var heard = (HeardTranscription)answer!;
        Assert.That(heard.Text, Is.EqualTo("Add a contact form to the home page."));
        Assert.That(heard.Engine.Version, Is.EqualTo("1.9.4-dev"));
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class GlazeSynthesizerTests
{
    private const int Rate = 48000;

    /// <summary>Every cue for every bot as it plays at 48 kHz, rendered once for these tests.</summary>
    private static readonly Lazy<IReadOnlyList<GlazeClip>> Played = new(() =>
    {
        var clock = Stopwatch.StartNew();
        var clips = GlazeSynthesizer.RenderAll(Rate).ToList();
        TestContext.Progress.WriteLine(
            $"Rendered {clips.Count} clips, {clips.Sum(clip => clip.Samples.Length)} samples at {Rate} Hz, in {clock.ElapsedMilliseconds} ms.");
        return clips;
    });

    /// <summary>
    /// The soundbook page's own figures for each render at 48 kHz, before the room: the length, peak
    /// and energy (the sum of squared samples) of the dry mix, and the energy of its send to the room.
    /// Computed by running the page's synthesis code in Node on 2026-09-29.
    /// </summary>
    private static readonly (SoundCue Cue, int Bot, int Length, double Peak, double Energy, double Send)[] Page =
    {
        (SoundCue.Working, 0, 48000, 0.112294420600, 43.0439447458, 1.72175779073),
        (SoundCue.Working, 1, 48000, 0.102008871734, 42.4158242624, 1.69663297079),
        (SoundCue.Working, 2, 48000, 0.117949165404, 40.0132334104, 1.60052933664),
        (SoundCue.Working, 3, 48000, 0.138072609901, 38.6466068440, 1.54586427522),
        (SoundCue.Working, 4, 48000, 0.124590694904, 38.1865322383, 1.52746129018),
        (SoundCue.Working, 5, 48000, 0.157426998019, 36.9367701752, 1.47747080626),
        (SoundCue.Verifying, 0, 43200, 0.127335175872, 42.4935053340, 1.69974021191),
        (SoundCue.Verifying, 1, 43200, 0.122979931533, 41.2463727998, 1.64985491036),
        (SoundCue.Verifying, 2, 43200, 0.142329305410, 40.7567496261, 1.63026998546),
        (SoundCue.Verifying, 3, 43200, 0.118773162365, 40.8814488163, 1.63525795193),
        (SoundCue.Verifying, 4, 43200, 0.128530055285, 39.2239226899, 1.56895690806),
        (SoundCue.Verifying, 5, 43200, 0.127771124244, 38.3731587121, 1.53492634860),
        (SoundCue.NeedsYou, 0, 76800, 0.336215704679, 317.100313311, 15.3476551543),
        (SoundCue.NeedsYou, 1, 76800, 0.358438044786, 303.548015232, 14.6917239436),
        (SoundCue.NeedsYou, 2, 76800, 0.332976758480, 295.668893076, 14.3103744144),
        (SoundCue.NeedsYou, 3, 76800, 0.328801065683, 281.339057824, 13.6168103950),
        (SoundCue.NeedsYou, 4, 76800, 0.336004763842, 268.389581607, 12.9900557475),
        (SoundCue.NeedsYou, 5, 76800, 0.346810221672, 289.706109091, 14.0217756717),
        (SoundCue.TurnFinished, 0, 72000, 0.222885787487, 133.781166784, 6.47500847105),
        (SoundCue.TurnFinished, 1, 72000, 0.185867562890, 121.674219452, 5.88903222440),
        (SoundCue.TurnFinished, 2, 72000, 0.172247812152, 110.741532611, 5.35989017562),
        (SoundCue.TurnFinished, 3, 72000, 0.185952603817, 108.222933544, 5.23798998392),
        (SoundCue.TurnFinished, 4, 72000, 0.213334470987, 117.039875777, 5.66472998964),
        (SoundCue.TurnFinished, 5, 72000, 0.221496447921, 108.220692996, 5.23788153879),
        (SoundCue.Failed, 0, 48000, 0.406873226166, 185.795892743, 7.43183571004),
        (SoundCue.Failed, 1, 48000, 0.266214877367, 176.573315935, 7.06293263656),
        (SoundCue.Failed, 2, 48000, 0.356272339821, 168.547601888, 6.74190407313),
        (SoundCue.Failed, 3, 48000, 0.289590418339, 161.627103905, 6.46508415746),
        (SoundCue.Failed, 4, 48000, 0.405309945345, 155.267820009, 6.21071280077),
        (SoundCue.Failed, 5, 48000, 0.335527867079, 152.662822777, 6.10651290722),
        (SoundCue.Unknown, 0, 67200, 0.171646028757, 111.459063407, 8.73839057321),
        (SoundCue.Unknown, 1, 67200, 0.164420947433, 131.366642836, 10.2991447999),
        (SoundCue.Unknown, 2, 67200, 0.166451275349, 101.513142222, 7.95863034870),
        (SoundCue.Unknown, 3, 67200, 0.183333024383, 94.6149163165, 7.41780943476),
        (SoundCue.Unknown, 4, 67200, 0.173840582371, 97.7097877797, 7.66044736524),
        (SoundCue.Unknown, 5, 67200, 0.185242474079, 103.462716402, 8.11147696246),
        (SoundCue.Stopped, 0, 24000, 0.217809945345, 81.9693901344, 2.65580824224),
        (SoundCue.Stopped, 1, 24000, 0.197244897485, 79.4842297622, 2.57528904485),
        (SoundCue.Stopped, 2, 24000, 0.204441055655, 76.9284390501, 2.49248142686),
        (SoundCue.Stopped, 3, 24000, 0.235737994313, 75.6804252106, 2.45204577792),
        (SoundCue.Stopped, 4, 24000, 0.197692781687, 74.6414528309, 2.41838307431),
        (SoundCue.Stopped, 5, 24000, 0.206087395549, 73.3047317595, 2.37507331005),
        (SoundCue.LastKnown, 0, 91200, 0.177562922239, 86.5309308463, 26.1756065811),
        (SoundCue.Open, 0, 62400, 0.228252217174, 108.001889329, 1.55513482541),
        (SoundCue.Open, 1, 62400, 0.228795468807, 104.212816986, 1.49957999224),
        (SoundCue.Open, 2, 62400, 0.219589486718, 102.284850118, 1.47182278283),
        (SoundCue.Open, 3, 62400, 0.258797258139, 99.9246754837, 1.43843595373),
        (SoundCue.Open, 4, 62400, 0.238438546658, 97.6049261811, 1.40440753858),
        (SoundCue.Open, 5, 62400, 0.267101705074, 93.7056125297, 1.34893564813),
        (SoundCue.Collapse, 0, 57600, 0.193671792746, 66.7409195449, 0.960455068550),
        (SoundCue.Collapse, 1, 57600, 0.186529785395, 63.8618353438, 0.918985556064),
        (SoundCue.Collapse, 2, 57600, 0.172718688846, 62.9584943391, 0.905899425059),
        (SoundCue.Collapse, 3, 57600, 0.207994267344, 61.0430290964, 0.878350866024),
        (SoundCue.Collapse, 4, 57600, 0.165225446224, 60.6609985120, 0.872795890219),
        (SoundCue.Collapse, 5, 57600, 0.189382746816, 59.2167234641, 0.851390132491),
        (SoundCue.Approve, 0, 57600, 0.257693409920, 105.761100475, 1.05761100554),
        (SoundCue.Approve, 1, 57600, 0.247049778700, 102.351064120, 1.02351064195),
        (SoundCue.Approve, 2, 57600, 0.272319287062, 98.9811910655, 0.989811911115),
        (SoundCue.Approve, 3, 57600, 0.231943368912, 97.3257336341, 0.973257336336),
        (SoundCue.Approve, 4, 57600, 0.252698242664, 96.6194413534, 0.966194413609),
        (SoundCue.Approve, 5, 57600, 0.304304331541, 92.3231784858, 0.923231784800),
        (SoundCue.Deny, 0, 33600, 0.218665719032, 85.8672334545, 0.549550294103),
        (SoundCue.Deny, 1, 33600, 0.244632735848, 83.4810228559, 0.534278546623),
        (SoundCue.Deny, 2, 33600, 0.251097649336, 80.7420163827, 0.516748905393),
        (SoundCue.Deny, 3, 33600, 0.216817870736, 77.3806648657, 0.495236255582),
        (SoundCue.Deny, 4, 33600, 0.206532061100, 75.4875738098, 0.483120472809),
        (SoundCue.Deny, 5, 33600, 0.238140404224, 74.5974561687, 0.477423719482),
        (SoundCue.Instruct, 0, 28800, 0.176377698779, 32.1590276726, 0.205817777214),
        (SoundCue.Instruct, 1, 28800, 0.171710833907, 30.8653017023, 0.197537930971),
        (SoundCue.Instruct, 2, 28800, 0.165351822972, 30.1232922413, 0.192789070536),
        (SoundCue.Instruct, 3, 28800, 0.146796301007, 29.7819908288, 0.190604741155),
        (SoundCue.Instruct, 4, 28800, 0.146898359060, 29.4863534914, 0.188712662390),
        (SoundCue.Instruct, 5, 28800, 0.171587496996, 29.0099081488, 0.185663412101),
        (SoundCue.Interrupt, 0, 24000, 0.275215446949, 91.2806489421, 0.328610336369),
        (SoundCue.Interrupt, 1, 24000, 0.291355997324, 86.3314028450, 0.310793049810),
        (SoundCue.Interrupt, 2, 24000, 0.298363953829, 82.0809844336, 0.295491543665),
        (SoundCue.Interrupt, 3, 24000, 0.262529045343, 79.5890148811, 0.286520453533),
        (SoundCue.Interrupt, 4, 24000, 0.278060406446, 77.0131329277, 0.277247278114),
        (SoundCue.Interrupt, 5, 24000, 0.313692092896, 75.7458120016, 0.272684922930),
    };

    /// <summary>
    /// How bright each cue may be, as the played clip's spectral centroid, in hertz: about a sixth
    /// above the brightest bot's. Calm cues stay low, since sharpness predicts annoyance and pitch
    /// urgency (docs/internal/architecture/XR_CLIENT.md, "Sound").
    /// </summary>
    private static readonly Dictionary<SoundCue, double> Brightest = new()
    {
        [SoundCue.Working] = 520,
        [SoundCue.Verifying] = 560,
        [SoundCue.NeedsYou] = 630,
        [SoundCue.TurnFinished] = 570,
        [SoundCue.Failed] = 380,
        [SoundCue.Unknown] = 520,
        [SoundCue.Stopped] = 520,
        [SoundCue.LastKnown] = 370,
        [SoundCue.Open] = 760,
        [SoundCue.Collapse] = 650,
        [SoundCue.Approve] = 650,
        [SoundCue.Deny] = 480,
        [SoundCue.Instruct] = 570,
        [SoundCue.Interrupt] = 400,
    };

    [Test]
    public void EveryRenderIsThePagesOwnBeforeTheRoom()
    {
        Assert.That(Page.Select(row => (row.Cue, row.Bot)),
            Is.EquivalentTo(GlazeSynthesizer.Cues.SelectMany(cue => Enumerable.Range(0, GlazeSynthesizer.VoicesOf(cue)).Select(bot => (cue, bot)))));
        foreach (var row in Page)
        {
            var voice = GlazeSynthesizer.RenderVoice(row.Cue, row.Bot, Rate);
            var name = row.Cue + " for bot " + row.Bot;
            Assert.That(voice.Dry, Has.Length.EqualTo(row.Length), name);
            Assert.That(voice.Send, Has.Length.EqualTo(row.Length), name);
            Assert.That(voice.Dry.Max(Math.Abs), Is.EqualTo(row.Peak).Within(1e-6).Percent, name + ": peak");
            Assert.That(voice.Dry.Sum(x => (double)x * x), Is.EqualTo(row.Energy).Within(1e-6).Percent, name + ": energy");
            Assert.That(voice.Send.Sum(x => (double)x * x), Is.EqualTo(row.Send).Within(1e-6).Percent, name + ": send");
        }
    }

    [Test]
    public void RendersTheSameSamplesEveryTime()
    {
        var again = GlazeSynthesizer.RenderAll(Rate).ToList();
        Assert.That(again.Select(clip => (clip.Cue, clip.Bot)), Is.EqualTo(Played.Value.Select(clip => (clip.Cue, clip.Bot))));
        for (var i = 0; i < again.Count; i++)
        {
            Assert.That(again[i].Samples, Is.EqualTo(Played.Value[i].Samples), again[i].Cue + " for bot " + again[i].Bot);
        }
        var room = GlazeRoom.Create(Rate);
        foreach (var (cue, bot) in new[] { (SoundCue.NeedsYou, 3), (SoundCue.LastKnown, 0), (SoundCue.Open, 4), (SoundCue.Deny, 1) })
        {
            Assert.That(GlazeSynthesizer.Render(cue, bot, room), Is.EqualTo(Clip(cue, bot).Samples), "rendered alone, " + cue + " for bot " + bot);
        }
    }

    [Test]
    public void EveryCueHasItsOwnRenderPerBotAndTheRoomsCueOne()
    {
        Assert.That(Played.Value, Has.Count.EqualTo(13 * GlazeSynthesizer.Bots + 1));
        Assert.That(GlazeSynthesizer.VoicesOf(SoundCue.LastKnown), Is.EqualTo(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GlazeSynthesizer.RenderVoice(SoundCue.Working, GlazeSynthesizer.Bots, Rate));
        Assert.Throws<ArgumentOutOfRangeException>(() => GlazeSynthesizer.Render(SoundCue.LastKnown, 1, GlazeRoom.Create(Rate)));
        Assert.Throws<ArgumentOutOfRangeException>(() => GlazeSynthesizer.RenderAll(4000).ToList());
    }

    [Test]
    public void NoSampleIsNaNAndNoneReachesTheCeiling()
    {
        foreach (var clip in Played.Value)
        {
            var name = clip.Cue + " for bot " + clip.Bot;
            Assert.That(clip.Samples.All(float.IsFinite), Is.True, name);
            Assert.That(clip.Samples.Max(Math.Abs), Is.LessThanOrEqualTo(GlazeSynthesizer.Ceiling), name);
        }
        Assert.That(GlazeSynthesizer.Ceiling, Is.EqualTo(0.6), "the page's ceiling, 4.4 dB below full scale");
    }

    [Test]
    public void EachCueLastsAsTheSoundbookSaysAndTheRoomRingsOnAfter()
    {
        var seconds = new Dictionary<SoundCue, double>
        {
            [SoundCue.Working] = 1.0,
            [SoundCue.Verifying] = 0.9,
            [SoundCue.NeedsYou] = 1.6,
            [SoundCue.TurnFinished] = 1.5,
            [SoundCue.Failed] = 1.0,
            [SoundCue.Unknown] = 1.4,
            [SoundCue.Stopped] = 0.5,
            [SoundCue.LastKnown] = 1.9,
            [SoundCue.Open] = 1.3,
            [SoundCue.Collapse] = 1.2,
            [SoundCue.Approve] = 1.2,
            [SoundCue.Deny] = 0.7,
            [SoundCue.Instruct] = 0.6,
            [SoundCue.Interrupt] = 0.5,
        };
        Assert.That(seconds.Keys, Is.EquivalentTo(GlazeSynthesizer.Cues));
        foreach (var clip in Played.Value)
        {
            var name = clip.Cue + " for bot " + clip.Bot;
            var duration = seconds[clip.Cue];
            Assert.That(GlazeSynthesizer.DurationOf(clip.Cue), Is.EqualTo(duration), name);
            var voice = GlazeSynthesizer.RenderVoice(clip.Cue, clip.Bot, Rate);
            Assert.That(voice.Dry, Has.Length.EqualTo((int)Math.Ceiling(duration * Rate)), name);
            // Cutting the tail 60 dB down never cuts the dry mix within 40 dB of the peak; near the
            // floor, the room's tail may cancel the dry mix's last faded samples. The room adds at
            // most its own length.
            var peak = clip.Samples.Max(Math.Abs);
            Assert.That(clip.Samples.Length, Is.GreaterThanOrEqualTo(Audible(voice.Dry, peak * 0.01)), name);
            Assert.That(clip.Samples.Length, Is.LessThanOrEqualTo(voice.Dry.Length + (int)(GlazeRoom.Seconds * Rate)), name);
            // It starts and ends in silence, so it never clicks.
            Assert.That(Math.Abs(clip.Samples[0]), Is.LessThan(peak * 1e-6), name + " starts");
            Assert.That(Math.Abs(clip.Samples[^1]), Is.LessThan(peak * 1e-6), name + " ends");
        }
    }

    [Test]
    public void EachCueIsSetToTheLoudnessOfItsImportance()
    {
        var targets = GlazeSynthesizer.Cues.ToDictionary(cue => cue, GlazeSynthesizer.TargetOf);
        Assert.That(targets[SoundCue.NeedsYou], Is.EqualTo(-20), "the loudest: it asks for the person");
        Assert.That(targets.Values.Max(), Is.EqualTo(-20));
        Assert.That(targets[SoundCue.Working], Is.EqualTo(-28), "work starting is among the quietest");
        Assert.That(targets[SoundCue.Instruct], Is.EqualTo(-29));
        foreach (var row in Page)
        {
            var voice = GlazeSynthesizer.RenderVoice(row.Cue, row.Bot, Rate);
            Assert.That(GlazeSynthesizer.Loudness(voice.Dry, Rate), Is.EqualTo(targets[row.Cue]).Within(0.01), row.Cue + " for bot " + row.Bot);
        }
    }

    [Test]
    public void EachCueStaysCalm()
    {
        var centroids = Played.Value.Select(clip => (clip.Cue, Hz: Centroid(clip.Samples, Rate))).ToList();
        foreach (var (cue, hz) in centroids) Assert.That(hz, Is.LessThanOrEqualTo(Brightest[cue]), cue.ToString());
        var mean = centroids.Average(item => item.Hz);
        TestContext.Out.WriteLine($"Spectral centroid: mean {mean:F0} Hz, brightest {centroids.Max(item => item.Hz):F0} Hz.");
        Assert.That(mean, Is.LessThanOrEqualTo(1100), "the soundbook's calm average");
    }

    [Test]
    public void TheRoomIsTheSendConvolvedWithItsResponse()
    {
        var room = GlazeRoom.Create(Rate);
        // Needs you is long enough to take two segments of the room's transform.
        foreach (var (cue, bot) in new[] { (SoundCue.NeedsYou, 3), (SoundCue.Working, 0) })
        {
            var voice = GlazeSynthesizer.RenderVoice(cue, bot, Rate);
            var played = GlazeSynthesizer.Render(cue, bot, room);
            var fade = (int)(0.01 * Rate);
            var random = new Random(7);
            for (var probe = 0; probe < 60; probe++)
            {
                var t = random.Next(0, played.Length - fade);
                double expected = t < voice.Dry.Length ? voice.Dry[t] : 0;
                for (var k = Math.Max(0, t - room.Response.Count + 1); k <= Math.Min(t, voice.Send.Length - 1); k++)
                {
                    expected += (double)voice.Send[k] * room.Response[t - k];
                }
                Assert.That(played[t], Is.EqualTo(expected).Within(1e-6), cue + " at sample " + t);
            }
        }
    }

    [Test]
    public void TheRoomIsThePagesShortRoom()
    {
        var room = GlazeRoom.Create(Rate);
        Assert.That(room.Response, Has.Count.EqualTo((int)Math.Round(GlazeRoom.Seconds * Rate)));
        Assert.That(room.Response.Take((int)(0.012 * Rate)).All(x => x == 0), Is.True, "12 ms of silence before it answers");
        Assert.That(room.Response[576], Is.Not.EqualTo(0f));
        Assert.That(room.Response.Sum(x => (double)x * x), Is.EqualTo(0.81).Within(1e-6), "its energy, as on the page");
        // Samples of the page's own first channel at 48 kHz, from the page's code: its onset, its
        // first early reflection and its tail.
        Assert.That(room.Response[576], Is.EqualTo(-0.0236028787).Within(1e-9));
        Assert.That(room.Response[912], Is.EqualTo(-0.00121787691).Within(1e-9));
        Assert.That(room.Response[30000], Is.EqualTo(0.000127925203).Within(1e-11));
    }

    [Test]
    public void EveryNoteIsInOneKeyAndEachBotHasItsOwn()
    {
        var pentatonic = new[] { 2, 4, 6, 9, 11 };
        Assert.That(GlazeSynthesizer.Homes, Is.EqualTo(new[] { 57, 59, 62, 64, 66, 69 }));
        Assert.That(GlazeSynthesizer.Homes.All(note => pentatonic.Contains(note % 12)), Is.True, "D major pentatonic");
        Assert.That(GlazeSynthesizer.Homes, Is.Ordered.Ascending.And.Unique, "rising from the person's left");
    }

    [Test]
    public void EachBotSoundsFromWhereTheStagesArcStandsIt()
    {
        // The stage's default arc: six slots 12 degrees apart, the outermost 30 degrees to each side.
        for (var slot = 0; slot < GlazeSynthesizer.Bots; slot++)
        {
            var angle = (-30 + 12 * slot) * Math.PI / 180;
            Assert.That(GlazeSynthesizer.Pans[slot], Is.EqualTo(Math.Sin(angle)).Within(0.006), "slot " + slot);
        }
    }

    [Test]
    public void OtherSampleRatesRenderTheSameCues()
    {
        const int rate = 44100;
        var room = GlazeRoom.Create(rate);
        foreach (var cue in new[] { SoundCue.NeedsYou, SoundCue.LastKnown, SoundCue.Unknown })
        {
            var voice = GlazeSynthesizer.RenderVoice(cue, 0, rate);
            Assert.That(voice.Dry, Has.Length.EqualTo((int)Math.Ceiling(GlazeSynthesizer.DurationOf(cue) * rate)), cue.ToString());
            Assert.That(GlazeSynthesizer.Loudness(voice.Dry, rate), Is.EqualTo(GlazeSynthesizer.TargetOf(cue)).Within(0.01), cue.ToString());
            var played = GlazeSynthesizer.Render(cue, 0, room);
            Assert.That(played.All(float.IsFinite) && played.Max(Math.Abs) <= GlazeSynthesizer.Ceiling, Is.True, cue.ToString());
        }
    }

    private static GlazeClip Clip(SoundCue cue, int bot) => Played.Value.Single(clip => clip.Cue == cue && clip.Bot == bot);

    /// <summary>Samples up to the last one above the floor, as the soundbook's check measured audible length.</summary>
    private static int Audible(float[] samples, double floor)
    {
        var last = samples.Length - 1;
        while (last > 0 && Math.Abs(samples[last]) <= floor) last--;
        return last + 1;
    }

    /// <summary>
    /// The energy-weighted spectral centroid over 2048-sample Hann frames on a 512-sample hop, the
    /// measure the soundbook's own check used.
    /// </summary>
    private static double Centroid(float[] samples, int rate)
    {
        const int size = 2048;
        const int hop = 512;
        double weighted = 0;
        double total = 0;
        var re = new double[size];
        var im = new double[size];
        for (var start = 0; start + size <= samples.Length + size / 2; start += hop)
        {
            for (var i = 0; i < size; i++)
            {
                var at = start + i - size / 2;
                re[i] = (at >= 0 && at < samples.Length ? samples[at] : 0) * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size));
                im[i] = 0;
            }
            Fourier(re, im);
            for (var bin = 1; bin <= size / 2; bin++)
            {
                var power = re[bin] * re[bin] + im[bin] * im[bin];
                weighted += power * bin * rate / size;
                total += power;
            }
        }
        return weighted / total;
    }

    private static void Fourier(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (var length = 2; length <= n; length <<= 1)
        {
            for (var start = 0; start < n; start += length)
            {
                for (var k = 0; k < length / 2; k++)
                {
                    var angle = -2 * Math.PI * k / length;
                    double wr = Math.Cos(angle), wi = Math.Sin(angle);
                    int u = start + k, v = u + length / 2;
                    double tr = re[v] * wr - im[v] * wi, ti = re[v] * wi + im[v] * wr;
                    re[v] = re[u] - tr;
                    im[v] = im[u] - ti;
                    re[u] += tr;
                    im[u] += ti;
                }
            }
        }
    }
}

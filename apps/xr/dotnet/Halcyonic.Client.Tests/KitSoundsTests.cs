using System;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The room's ten sounds (the art direction's handoff, 2026-10-30, section 4): one key, the handoff's notes, onsets and
/// levels, bells that ring at their notes with their partial, nothing longer than a second and nothing looping.
/// </summary>
public class KitSoundsTests
{
    private const int Rate = 48000;

    private static readonly KitSound[] All = Enum.GetValues(typeof(KitSound)).Cast<KitSound>().ToArray();

    [Test]
    public void EveryNoteIsInOneKey()
    {
        var notes = All.SelectMany(sound => KitSounds.PartsOf(sound)).SelectMany(part => new[] { part.Note, part.ToNote }).Where(note => note != null).ToList();
        Assert.That(notes, Is.Not.Empty);
        foreach (var note in notes)
            Assert.That(KitSounds.Key, Does.Contain(KitSounds.NameOf(note!)), $"{note} is in A major pentatonic");
        Assert.That(KitSounds.Frequency("A4"), Is.EqualTo(440).Within(1e-9));
        Assert.That(KitSounds.Frequency("C#6"), Is.EqualTo(1108.73).Within(0.01));
    }

    [Test]
    public void TheCuesAreTheHandoffs()
    {
        // Notes, onsets and levels as the handoff lists them.
        string Shape(KitSound sound) => string.Join(" ", KitSounds.PartsOf(sound).Select(part => $"{part.Note ?? part.Voice.ToString()}@{part.OnsetMs}:{part.DurationMs}/{part.LevelDb}"));
        Assert.That(Shape(KitSound.Question), Is.EqualTo("C#6@0:200/-22 E6@70:220/-22 A6@140:320/-23"));
        Assert.That(Shape(KitSound.Moment), Is.EqualTo("E6@0:220/-24 A6@60:260/-25"));
        Assert.That(Shape(KitSound.Sent), Is.EqualTo("A6@0:120/-28 E6@70:200/-29"));
        Assert.That(Shape(KitSound.Press), Is.EqualTo("Noise@0:12/-34 A6@12:60/-30"));
        Assert.That(Shape(KitSound.Failed), Is.EqualTo("E5@0:300/-22 B4@120:300/-22"));
        Assert.That(KitSounds.PartsOf(KitSound.Passed).Where(part => part.Note != null).Select(part => part.Note), Is.EqualTo(new[] { "A5", "C#6", "E6", "A6" }), "the hero rises through the key");
        Assert.That(KitSounds.PartsOf(KitSound.Passed).Where(part => part.Note != null).All(part => part.Partial == 0.22), Is.True, "the hero is bright");
        Assert.That(All.Where(sound => sound != KitSound.Passed).SelectMany(sound => KitSounds.PartsOf(sound)).Where(part => part.Voice == KitSoundVoice.Bell).All(part => part.Partial == 0.12), Is.True);
        Assert.That(All.Max(sound => KitSounds.PartsOf(sound).Max(part => part.LevelDb)), Is.EqualTo(-18), "the hero is the loudest, at -18 dB");
    }

    [Test]
    public void NothingLoopsOrOutstaysASecond()
    {
        foreach (var sound in All)
        {
            var samples = KitSounds.Render(sound, Rate);
            Assert.That(samples.Length, Is.EqualTo((int)Math.Ceiling(KitSounds.DurationMs(sound) / 1000 * Rate)), $"{sound} is as long as itself");
            Assert.That(KitSounds.DurationMs(sound), Is.LessThanOrEqualTo(1000), $"{sound} under a second");
            Assert.That(samples.All(sample => !float.IsNaN(sample) && Math.Abs(sample) < 0.5f), Is.True, $"{sound} is finite and quiet");
            Assert.That(samples.Skip(samples.Length - 48).All(sample => Math.Abs(sample) < 1e-3), Is.True, $"{sound} ends in silence");
        }
    }

    [Test]
    public void EachBellRingsAtItsNoteWithItsPartial()
    {
        // The Moment's first bell alone, before the second starts at 60 ms: its pitch by zero crossings, its partial by the DFT.
        var samples = KitSounds.Render(KitSound.Moment, Rate).Take(Rate * 55 / 1000).Skip(Rate * 8 / 1000).Select(sample => (double)sample).ToArray();
        var crossings = Enumerable.Range(1, samples.Length - 1).Count(i => samples[i - 1] < 0 && samples[i] >= 0);
        var measured = crossings / (samples.Length / (double)Rate);
        Assert.That(measured, Is.EqualTo(KitSounds.Frequency("E6")).Within(KitSounds.Frequency("E6") * 0.01), "E6 within 1 percent");
        double Magnitude(double frequency)
        {
            double re = 0, im = 0;
            for (var i = 0; i < samples.Length; i++)
            {
                re += samples[i] * Math.Cos(2 * Math.PI * frequency * i / Rate);
                im -= samples[i] * Math.Sin(2 * Math.PI * frequency * i / Rate);
            }
            return Math.Sqrt(re * re + im * im);
        }
        var partial = Magnitude(KitSounds.Frequency("E6") * KitSounds.PartialRatio);
        Assert.That(partial, Is.GreaterThan(5 * Magnitude(KitSounds.Frequency("E6") * 2)), "the 3.01 partial rings, the octave does not");
        Assert.That(partial, Is.LessThan(Magnitude(KitSounds.Frequency("E6"))), "under its fundamental");
    }

    [Test]
    public void APressVariesTheTickOnly()
    {
        Assert.That(KitSounds.Render(KitSound.Press, Rate, 0.5), Is.Not.EqualTo(KitSounds.Render(KitSound.Press, Rate, -0.5)), "presses differ a little");
        Assert.That(KitSounds.Render(KitSound.Question, Rate, 0.5), Is.EqualTo(KitSounds.Render(KitSound.Question, Rate, -0.5)), "a question is always the same");
        Assert.That(KitSounds.Render(KitSound.Question, Rate), Is.EqualTo(KitSounds.Render(KitSound.Question, Rate)), "rendered the same each time");
    }
}

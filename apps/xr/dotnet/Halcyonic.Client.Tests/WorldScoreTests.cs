using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>A person's world as it is heard (ADR 0049): measured, since a test cannot listen.</summary>
public class WorldScoreTests
{
    private const int Rate = 48000;

    private static readonly Lazy<Dictionary<WorldSound, float[]>> Rendered = new(() =>
    {
        var clock = Stopwatch.StartNew();
        var all = Enum.GetValues(typeof(WorldSound)).Cast<WorldSound>().ToDictionary(sound => sound, sound => WorldScore.Render(sound, Rate));
        TestContext.Progress.WriteLine($"Rendered the world's sounds at {Rate} Hz in {clock.ElapsedMilliseconds} ms.");
        return all;
    });

    private static float[] Mono(float[] stereo)
    {
        var mono = new float[stereo.Length / 2];
        for (var i = 0; i < mono.Length; i++) mono[i] = (stereo[2 * i] + stereo[2 * i + 1]) * 0.5f;
        return mono;
    }

    /// <summary>Where the energy sits, in Hz: the power-weighted mean frequency over the whole sound.</summary>
    private static double Centroid(float[] mono)
    {
        // A zero-crossing rate is a cheap stand-in for brightness on these smooth signals.
        var crossings = 0;
        for (var i = 1; i < mono.Length; i++) if ((mono[i - 1] < 0) != (mono[i] < 0)) crossings++;
        return crossings * (double)Rate / (2 * mono.Length);
    }

    [Test]
    public void Each_sound_is_stereo_the_length_it_says_and_never_past_the_ceiling()
    {
        foreach (var (sound, samples) in Rendered.Value)
        {
            var seconds = sound == WorldSound.PlaceLit ? WorldScore.ChimeSeconds : WorldScore.LoopSeconds;
            Assert.That(samples.Length, Is.EqualTo((int)Math.Round(seconds * Rate) * 2), sound.ToString());
            Assert.That(samples.All(float.IsFinite), Is.True, sound.ToString());
            Assert.That(samples.Max(Math.Abs), Is.LessThanOrEqualTo(WorldScore.Ceiling + 1e-6), sound.ToString());
            var left = Enumerable.Range(0, samples.Length / 2).Select(i => samples[2 * i]).ToArray();
            var right = Enumerable.Range(0, samples.Length / 2).Select(i => samples[2 * i + 1]).ToArray();
            Assert.That(left.SequenceEqual(right), Is.False, sound + " is stereo, not one channel twice");
        }
    }

    [Test]
    public void Each_sound_is_at_its_loudness_unless_the_ceiling_held_it_down()
    {
        foreach (var (sound, samples) in Rendered.Value)
        {
            var loudness = GlazeSynthesizer.Loudness(Mono(samples), Rate);
            Assert.That(loudness, Is.LessThanOrEqualTo(WorldScore.TargetOf(sound) + 0.5), sound.ToString());
            Assert.That(loudness, Is.GreaterThan(WorldScore.TargetOf(sound) - 6), sound + " is not held far under its target");
        }
    }

    [Test]
    public void A_bed_runs_from_its_end_into_its_beginning_without_a_step()
    {
        foreach (var sound in new[] { WorldSound.Dusk, WorldSound.Night })
        {
            var samples = Rendered.Value[sound];
            // The jump across the seam is no larger than the largest step anywhere inside the loop.
            var largest = 0.0;
            for (var i = 2; i < samples.Length; i++) largest = Math.Max(largest, Math.Abs(samples[i] - samples[i - 2]));
            var seamLeft = Math.Abs(samples[0] - samples[samples.Length - 2]);
            var seamRight = Math.Abs(samples[1] - samples[samples.Length - 1]);
            Assert.That(Math.Max(seamLeft, seamRight), Is.LessThanOrEqualTo(largest), sound.ToString());
            // And the loop is never silent at its start, where the folded tail lands.
            var head = Mono(samples).Take(Rate / 2).Select(Math.Abs).Max();
            Assert.That(head, Is.GreaterThan(1e-4), sound + " starts sounding");
        }
    }

    [Test]
    public void Night_is_darker_and_quieter_than_dusk()
    {
        var dusk = Mono(Rendered.Value[WorldSound.Dusk]);
        var night = Mono(Rendered.Value[WorldSound.Night]);
        Assert.That(Centroid(night), Is.LessThan(Centroid(dusk)));
        Assert.That(GlazeSynthesizer.Loudness(night, Rate), Is.LessThan(GlazeSynthesizer.Loudness(dusk, Rate)));
    }

    [Test]
    public void The_place_chime_sounds_at_once_and_has_died_away_by_its_end()
    {
        var chime = Mono(Rendered.Value[WorldSound.PlaceLit]);
        var first = chime.Take(Rate / 10).Select(Math.Abs).Max();
        var last = chime.Skip(chime.Length - Rate / 10).Select(Math.Abs).Max();
        Assert.That(first, Is.GreaterThan(0.05));
        Assert.That(last, Is.LessThan(first / 100));
    }

    [Test]
    public void The_same_rate_gives_the_same_samples()
    {
        var again = WorldScore.Render(WorldSound.PlaceLit, Rate);
        Assert.That(again.SequenceEqual(Rendered.Value[WorldSound.PlaceLit]), Is.True);
    }
}

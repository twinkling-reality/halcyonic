#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The room's ten sounds made in the engine (<see cref="KitSoundPlayer"/> from <see cref="KitSounds"/>): every clip mono, as
    /// long as its sound and no longer, holding exactly the core's samples; the Moment's first bell at E6 within 1 percent in
    /// the clip as Unity holds it; and a source set fully spatial, without Doppler, never looping. No device plays here: what
    /// the person hears on the headset is the headset's to judge.
    /// </summary>
    public static partial class KitRender
    {
        private static List<string> SoundCheck()
        {
            var failures = new List<string>();
            void Require(bool condition, string why) { if (!condition) failures.Add("sound: " + why); }
            const int rate = 48000;
            foreach (KitSound sound in Enum.GetValues(typeof(KitSound)))
            {
                foreach (var variant in new[] { 0, 2 })
                {
                    var clip = KitSoundPlayer.MakeClip(sound, variant, rate);
                    try
                    {
                        var expected = KitSounds.Render(sound, rate, variant == 0 ? -1 : 1);
                        Require(clip.channels == 1 && clip.frequency == rate, sound + " is not mono at " + rate + " Hz");
                        Require(clip.samples == expected.Length, sound + " is " + clip.samples + " samples, not its " + expected.Length);
                        var data = new float[clip.samples];
                        clip.GetData(data, 0);
                        var worst = data.Zip(expected, (a, b) => Math.Abs(a - b)).DefaultIfEmpty(0f).Max();
                        Require(worst < 1e-5f, sound + " is not the core's samples (" + worst + " apart)");
                        Require(data.Skip(data.Length - 48).All(sample => Math.Abs(sample) < 1e-3f), sound + " does not end in silence");
                        if (sound == KitSound.Moment && variant == 0)
                        {
                            var bell = data.Take(rate * 55 / 1000).Skip(rate * 8 / 1000).ToArray();
                            var crossings = Enumerable.Range(1, bell.Length - 1).Count(i => bell[i - 1] < 0 && bell[i] >= 0);
                            var measured = crossings / (bell.Length / (double)rate);
                            var e6 = KitSounds.Frequency("E6");
                            Require(Math.Abs(measured - e6) <= e6 * 0.01, "the Moment's first bell rings at " + measured.ToString("0.0") + " Hz, not E6's " + e6.ToString("0.0"));
                        }
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(clip);
                    }
                }
            }
            // A moment's cue by its tone: each named, and no other tone sounds (AUDIT's A3C-S1).
            Require(KitMomentChoreography.CueOf(SpatialKit.Attention) == KitSound.Question && KitMomentChoreography.CueOf(SpatialKit.Passed) == KitSound.Passed
                && KitMomentChoreography.CueOf(SpatialKit.Failure) == KitSound.Failed, "a moment's tone does not sound its own cue");
            Require(KitMomentChoreography.CueOf(SpatialKit.Accent) == null && KitMomentChoreography.CueOf(null) == null, "a tone with no cue of its own sounds one");
            var host = new GameObject("Room sound check");
            try
            {
                var source = host.AddComponent<AudioSource>();
                KitSoundPlayer.Configure(source);
                Require(!source.loop && !source.playOnAwake, "a source loops or plays on its own");
                Require(Mathf.Approximately(source.spatialBlend, 1f) && source.dopplerLevel == 0f, "a source is not fully spatial without Doppler");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
            Debug.Log("Halcyonic: spatial kit: sound: " + Enum.GetValues(typeof(KitSound)).Length + " cues made in the engine, " + failures.Count + " failures.");
            return failures;
        }
    }
}

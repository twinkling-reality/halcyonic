#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>
    /// Plays a character's own sounds (<see cref="CharacterSounds"/>: its arrival, its voice on its own pair of notes, the
    /// hop at the computer's own passed check, its fold) from where its figure stands: clips made once from the client
    /// core's numbers, no audio assets, on fully spatial sources set as the room's are (no Doppler, full level within 2.5 m and
    /// quietest from 25 m, Meta XR Audio's spatializer where it is loaded) that never loop, heard only while the app has the
    /// person's attention. None in the editor's batch renders.
    /// </summary>
    public sealed class CharacterSoundPlayer : MonoBehaviour
    {
        private const int Sources = 4;
        private const string Spatializer = "Meta XR Audio";

        private static CharacterSoundPlayer? shared;
        private readonly Dictionary<(CharacterSound Sound, KitFigureFeature Feature), AudioClip> clips = new Dictionary<(CharacterSound, KitFigureFeature), AudioClip>();
        private readonly List<AudioSource> sources = new List<AudioSource>();
        private int next;

        /// <summary>Plays each of <paramref name="sounds"/> at <paramref name="at"/>, with the voice of <paramref name="feature"/>.</summary>
        public static void Play(IReadOnlyList<CharacterSound> sounds, KitFigureFeature feature, Vector3 at)
        {
            if (!Application.isPlaying || !Application.isFocused || FocusGuard.InputSuspended) return;
            if (shared == null)
            {
                var go = new GameObject("Character sounds");
                DontDestroyOnLoad(go);
                shared = go.AddComponent<CharacterSoundPlayer>();
            }
            foreach (var sound in sounds) shared.PlayOne(sound, feature, at);
        }

        /// <summary>
        /// A cue's samples, mono, at <paramref name="rate"/> Hz, at its level and as long as it is (nothing loops): sines from
        /// one pitch to another, or a noise band sweeping, each under the room's envelope.
        /// </summary>
        public static float[] Render(CharacterSound sound, KitFigureFeature feature, int rate)
        {
            var tones = CharacterSounds.Tones(sound, feature);
            var samples = new float[(int)Math.Ceiling(tones.Max(tone => tone.StartMs + tone.DurationMs) / 1000 * rate)];
            var level = Math.Pow(10, CharacterSounds.LevelDb(sound) / 20);
            var random = new System.Random(1 + (int)sound);
            foreach (var tone in tones)
            {
                var start = (int)(tone.StartMs / 1000 * rate);
                var length = (int)(tone.DurationMs / 1000 * rate);
                double phase = 0, low = 0, band = 0;
                for (var i = 0; i < length && start + i < samples.Length; i++)
                {
                    var through = i / (double)length;
                    var frequency = tone.FromHz * Math.Pow(tone.ToHz / tone.FromHz, through);
                    double value;
                    if (tone.Noise)
                    {
                        // A state-variable band-pass, its centre sweeping with the tone.
                        var f = 2 * Math.Sin(Math.PI * Math.Min(frequency, rate * 0.2) / rate);
                        low += f * band;
                        var high = random.NextDouble() * 2 - 1 - low - 0.5 * band;
                        band += f * high;
                        value = band;
                    }
                    else
                    {
                        phase += 2 * Math.PI * frequency / rate;
                        value = Math.Sin(phase);
                    }
                    samples[start + i] += (float)(value * KitSounds.Envelope(i * 1000.0 / rate, tone.DurationMs) * level);
                }
            }
            return samples;
        }

        private void PlayOne(CharacterSound sound, KitFigureFeature feature, Vector3 at)
        {
            if (!clips.TryGetValue((sound, feature), out var clip))
            {
                var rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
                var samples = Render(sound, feature, rate);
                clip = AudioClip.Create("Character " + sound + " " + feature, samples.Length, 1, rate, false);
                clip.SetData(samples, 0);
                clips[(sound, feature)] = clip;
            }
            var source = Source();
            source.transform.position = at;
            source.clip = clip;
            source.Play();
        }

        private AudioSource Source()
        {
            if (sources.Count < Sources)
            {
                var go = new GameObject("Character sound " + sources.Count);
                go.transform.SetParent(transform, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 1f;
                source.dopplerLevel = 0f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = 2.5f;
                source.maxDistance = 25f;
                source.spatialize = AudioSettings.GetSpatializerPluginName() == Spatializer;
                sources.Add(source);
                return source;
            }
            var reused = sources[next];
            next = (next + 1) % Sources;
            return reused;
        }

        private void OnDestroy()
        {
            foreach (var clip in clips.Values) Destroy(clip);
            if (shared == this) shared = null;
        }
    }
}

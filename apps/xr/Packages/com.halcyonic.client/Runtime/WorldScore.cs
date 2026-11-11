#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>Which of a world's sounds.</summary>
    public enum WorldSound
    {
        /// <summary>The bed at dusk: warm pads moving through four chords, a few bells, air.</summary>
        Dusk,

        /// <summary>The bed at night: lower, slower, darker, the bells rarer.</summary>
        Night,

        /// <summary>A project's place lighting: one soft rising chime.</summary>
        PlaceLit,
    }

    /// <summary>
    /// A person's world as it is heard (ADR 0049): its music composed in code, never a file. Two beds that loop
    /// without a seam, one for dusk and one for night, which the headset crossfades as its look does, and the
    /// chime a project's place makes when it lights. Everything is rendered once at start, deterministically:
    /// the same sample rate gives the same samples. Stereo, interleaved, left first.
    /// </summary>
    /// <remarks>
    /// The beds are pads (each chord tone a few slightly detuned partials with slow swells), sparse bells on the
    /// chord's tones, and filtered noise for air, through one hall (a feedback delay network). A loop is rendered
    /// with its tail and the tail is folded back onto its start, so its end runs into its beginning. Levels are
    /// set by the same loudness measure the stage's cues use (<see cref="GlazeSynthesizer.Loudness"/>).
    /// </remarks>
    public static class WorldScore
    {
        /// <summary>How long one loop of a bed is, in seconds: four chords of eight seconds, twice.</summary>
        public const double LoopSeconds = 64;

        /// <summary>How long the place's chime is, in seconds, its tail included.</summary>
        public const double ChimeSeconds = 4;

        /// <summary>Loudness each sound is set to, in LUFS: a bed sits well under the stage's cues.</summary>
        public static double TargetOf(WorldSound sound) => sound switch
        {
            WorldSound.Dusk => -27,
            WorldSound.Night => -30,
            _ => -22,
        };

        /// <summary>No sample is ever louder than this.</summary>
        public const double Ceiling = 0.5;

        // Four chords, as MIDI notes, from the bass up: D major 9, B minor 9, G major 7 sharp 11, A add 9.
        private static readonly int[][] Chords =
        {
            new[] { 38, 50, 54, 57, 61, 64 },
            new[] { 35, 50, 54, 57, 61, 62 },
            new[] { 31, 50, 54, 57, 59, 61 },
            new[] { 33, 52, 57, 59, 61, 64 },
        };

        /// <summary>Renders one sound, stereo interleaved, at the sample rate given.</summary>
        public static float[] Render(WorldSound sound, int sampleRate)
        {
            GlazeSynthesizer.CheckRate(sampleRate);
            var stereo = sound == WorldSound.PlaceLit ? Chime(sampleRate) : Bed(sound == WorldSound.Night, sampleRate);
            Level(stereo, sampleRate, TargetOf(sound));
            return stereo;
        }

        private static float[] Bed(bool night, int rate)
        {
            var frames = (int)Math.Round(LoopSeconds * rate);
            var tail = (int)Math.Round(8.0 * rate);
            var left = new double[frames + tail];
            var right = new double[frames + tail];
            var random = new Random(night ? 41 : 17);
            var chordSeconds = LoopSeconds / 8;
            var shift = night ? -5 : 0; // night sits a fourth lower
            for (var c = 0; c < 8; c++)
            {
                var chord = Chords[c % 4];
                var start = c * chordSeconds;
                for (var n = 0; n < chord.Length; n++)
                {
                    var note = chord[n] + shift;
                    var pan = n == 0 ? 0 : (n % 2 == 0 ? -0.35 : 0.35) * (0.6 + 0.4 * n / chord.Length);
                    var level = n == 0 ? 0.55 : 0.32;
                    // Each pad note swells in over two seconds and out over three, overlapping the next chord.
                    Pad(left, right, rate, Mtof(note), start, chordSeconds + 3, level, pan, night, random);
                }
                // Bells: a few of the chord's upper tones, two octaves up, at uneven times.
                var bells = night ? 1 : 3;
                for (var b = 0; b < bells; b++)
                {
                    var tone = chord[1 + random.Next(chord.Length - 1)] + shift + 12;
                    var at = start + 0.5 + random.NextDouble() * (chordSeconds - 1);
                    Bell(left, right, rate, Mtof(tone), at, night ? 0.09 : 0.12, (random.NextDouble() - 0.5) * 1.2);
                }
            }
            Air(left, right, rate, night, random);
            Hall(left, right, rate, night ? 0.32 : 0.26, night ? 5.5 : 4.2);
            // Fold the tail onto the start, so the loop's end runs into its beginning.
            for (var i = 0; i < tail; i++)
            {
                left[i] += left[frames + i];
                right[i] += right[frames + i];
            }
            return Interleave(left, right, frames);
        }

        private static float[] Chime(int rate)
        {
            var frames = (int)Math.Round(ChimeSeconds * rate);
            var left = new double[frames];
            var right = new double[frames];
            // A rising fifth and ninth over the home key, close together: something lit, not an alarm.
            Bell(left, right, rate, Mtof(74), 0.0, 0.32, -0.25);
            Bell(left, right, rate, Mtof(81), 0.12, 0.26, 0.15);
            Bell(left, right, rate, Mtof(88), 0.26, 0.18, 0.3);
            Hall(left, right, rate, 0.3, 2.5);
            Fade(left, right, rate, 0.6);
            return Interleave(left, right, frames);
        }

        private static void Pad(double[] left, double[] right, int rate, double frequency, double start, double seconds, double level, double pan, bool night, Random random)
        {
            var from = (int)(start * rate);
            var count = (int)(seconds * rate);
            // Three partials per voice, each slightly detuned, with a soft high fall-off: warmer at night.
            var partials = new[] { (Ratio: 1.0, Gain: 1.0), (Ratio: 2.0, Gain: night ? 0.18 : 0.3), (Ratio: 3.0, Gain: night ? 0.05 : 0.12) };
            var detune = new[] { -0.0025, 0.0, 0.0031 };
            var phases = new double[partials.Length * detune.Length];
            for (var p = 0; p < phases.Length; p++) phases[p] = random.NextDouble() * Math.PI * 2;
            var gainLeft = Math.Sqrt(0.5 * (1 - pan));
            var gainRight = Math.Sqrt(0.5 * (1 + pan));
            var swell = 2.0 * rate;
            var release = 3.0 * rate;
            for (var i = 0; i < count && from + i < left.Length; i++)
            {
                var envelope = i < swell ? Smooth(i / swell) : 1.0;
                if (i > count - release) envelope *= Smooth((count - i) / release);
                // A slow breath in the level, different per note.
                envelope *= 0.85 + 0.15 * Math.Sin(2 * Math.PI * (0.07 + frequency % 0.05) * i / rate + phases[0]);
                var t = (double)i / rate;
                var sum = 0.0;
                for (var p = 0; p < partials.Length; p++)
                {
                    for (var d = 0; d < detune.Length; d++)
                    {
                        sum += partials[p].Gain * Math.Sin(2 * Math.PI * frequency * partials[p].Ratio * (1 + detune[d]) * t + phases[p * detune.Length + d]);
                    }
                }
                var value = sum / 9.0 * level * envelope;
                left[from + i] += value * gainLeft;
                right[from + i] += value * gainRight;
            }
        }

        private static void Bell(double[] left, double[] right, int rate, double frequency, double at, double level, double pan)
        {
            var from = (int)(at * rate);
            var count = (int)(3.5 * rate);
            var gainLeft = Math.Sqrt(0.5 * (1 - pan));
            var gainRight = Math.Sqrt(0.5 * (1 + pan));
            // A glass-like bell: a fundamental and two inharmonic partials, each fading at its own rate.
            var partials = new[] { (Ratio: 1.0, Gain: 1.0, Decay: 1.6), (Ratio: 2.76, Gain: 0.35, Decay: 0.7), (Ratio: 5.4, Gain: 0.12, Decay: 0.35) };
            for (var i = 0; i < count && from + i < left.Length; i++)
            {
                var t = (double)i / rate;
                var attack = Math.Min(1.0, t / 0.004);
                var sum = 0.0;
                foreach (var p in partials) sum += p.Gain * Math.Exp(-t / p.Decay) * Math.Sin(2 * Math.PI * frequency * p.Ratio * t);
                var value = sum * attack * level;
                left[from + i] += value * gainLeft;
                right[from + i] += value * gainRight;
            }
        }

        /// <summary>Air: noise through a slow, wandering low-pass, quieter and darker at night.</summary>
        private static void Air(double[] left, double[] right, int rate, bool night, Random random)
        {
            var stateLeft = 0.0;
            var stateRight = 0.0;
            var level = night ? 0.012 : 0.02;
            for (var i = 0; i < left.Length; i++)
            {
                var t = (double)i / rate;
                var cutoff = (night ? 300 : 650) * (1 + 0.5 * Math.Sin(2 * Math.PI * t / 23.0));
                var a = 1 - Math.Exp(-2 * Math.PI * cutoff / rate);
                stateLeft += a * ((random.NextDouble() * 2 - 1) - stateLeft);
                stateRight += a * ((random.NextDouble() * 2 - 1) - stateRight);
                var gust = 0.6 + 0.4 * Math.Sin(2 * Math.PI * t / 31.0 + 1.3);
                left[i] += stateLeft * level * gust;
                right[i] += stateRight * level * gust;
            }
        }

        /// <summary>A hall: four feedback delay lines mixed by a Householder matrix, damped above a few kHz.</summary>
        private static void Hall(double[] left, double[] right, int rate, double wet, double seconds)
        {
            var lengths = new[] { 0.0297, 0.0371, 0.0411, 0.0437 };
            var lines = new double[4][];
            var positions = new int[4];
            var gains = new double[4];
            var damp = new double[4];
            for (var k = 0; k < 4; k++)
            {
                var length = Math.Max(1, (int)(lengths[k] * rate * 1.9));
                lines[k] = new double[length];
                gains[k] = Math.Pow(10, -3.0 * length / rate / seconds);
            }
            var lowpass = 1 - Math.Exp(-2 * Math.PI * 4500.0 / rate);
            var outs = new double[4];
            for (var i = 0; i < left.Length; i++)
            {
                for (var k = 0; k < 4; k++) outs[k] = lines[k][positions[k]];
                var mix = (outs[0] + outs[1] + outs[2] + outs[3]) * 0.5;
                var input = (left[i] + right[i]) * 0.5;
                for (var k = 0; k < 4; k++)
                {
                    var feedback = (outs[k] - mix) * gains[k];
                    damp[k] += lowpass * (feedback - damp[k]);
                    lines[k][positions[k]] = damp[k] + input * (k % 2 == 0 ? 1 : -1) * 0.5;
                    positions[k] = (positions[k] + 1) % lines[k].Length;
                }
                left[i] = left[i] * (1 - wet) + (outs[0] + outs[2]) * wet;
                right[i] = right[i] * (1 - wet) + (outs[1] + outs[3]) * wet;
            }
        }

        private static void Fade(double[] left, double[] right, int rate, double seconds)
        {
            var count = Math.Min(left.Length, (int)(seconds * rate));
            for (var i = 0; i < count; i++)
            {
                var g = Smooth((double)i / count);
                left[left.Length - 1 - i] *= g;
                right[right.Length - 1 - i] *= g;
            }
        }

        /// <summary>Sets a sound to its loudness, then keeps every sample under the ceiling.</summary>
        private static void Level(float[] stereo, int rate, double target)
        {
            var mono = new float[stereo.Length / 2];
            for (var i = 0; i < mono.Length; i++) mono[i] = (stereo[2 * i] + stereo[2 * i + 1]) * 0.5f;
            var loudness = GlazeSynthesizer.Loudness(mono, rate);
            var gain = Math.Pow(10, (target - loudness) / 20);
            var peak = 0.0;
            for (var i = 0; i < stereo.Length; i++) peak = Math.Max(peak, Math.Abs(stereo[i] * gain));
            if (peak > Ceiling) gain *= Ceiling / peak;
            for (var i = 0; i < stereo.Length; i++) stereo[i] = (float)(stereo[i] * gain);
        }

        private static float[] Interleave(double[] left, double[] right, int frames)
        {
            var stereo = new float[frames * 2];
            for (var i = 0; i < frames; i++)
            {
                stereo[2 * i] = (float)left[i];
                stereo[2 * i + 1] = (float)right[i];
            }
            return stereo;
        }

        private static double Smooth(double x)
        {
            x = Math.Max(0, Math.Min(1, x));
            return x * x * (3 - 2 * x);
        }

        private static double Mtof(double note) => 440 * Math.Pow(2, (note - 69) / 12);
    }
}

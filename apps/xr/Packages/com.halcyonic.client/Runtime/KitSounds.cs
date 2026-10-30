#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>The room's ten sounds (the art direction's handoff, 2026-10-30, section 4), named by when they play.</summary>
    public enum KitSound
    {
        /// <summary>Pointing at a tile.</summary>
        Tick,

        /// <summary>Every answer and tile, on release.</summary>
        Press,

        /// <summary>Run unlocking; the computer back.</summary>
        Moment,

        /// <summary>A question arrives.</summary>
        Question,

        /// <summary>An answer is sent.</summary>
        Sent,

        /// <summary>Read Command; the bar unfolding; a tile picked.</summary>
        Open,

        /// <summary>Fold; the bar folding.</summary>
        Fold,

        /// <summary>A check of the person's computer passes: the one hero sound.</summary>
        Passed,

        /// <summary>A failure arrives.</summary>
        Failed,

        /// <summary>The person's computer becomes unreachable.</summary>
        Lost,
    }

    /// <summary>What one layer of a sound is.</summary>
    public enum KitSoundVoice
    {
        /// <summary>A sine with a partial at 3.01 times its frequency, a 6 ms attack and an exponential decay.</summary>
        Bell,

        /// <summary>Noise through a band-pass at a centre frequency.</summary>
        Noise,

        /// <summary>Noise through a band-pass whose centre sweeps from one frequency to another.</summary>
        Sweep,

        /// <summary>A triangle wave through a low-pass.</summary>
        Triangle,

        /// <summary>A sine gliding from one pitch to another.</summary>
        Glide,
    }

    /// <summary>One layer of a sound: its voice, its notes or band, when it starts, how long it lasts and how loud it is.</summary>
    public sealed class KitSoundPart
    {
        public KitSoundPart(KitSoundVoice voice, double onsetMs, double durationMs, double levelDb, string? note = null, string? toNote = null,
            double low = 0, double high = 0, double q = 1, double partial = 0.12, double vary = 0)
        {
            Voice = voice;
            OnsetMs = onsetMs;
            DurationMs = durationMs;
            LevelDb = levelDb;
            Note = note;
            ToNote = toNote;
            Low = low;
            High = high;
            Q = q;
            Partial = partial;
            Vary = vary;
        }

        public KitSoundVoice Voice { get; }

        public double OnsetMs { get; }

        public double DurationMs { get; }

        /// <summary>Relative to full scale, as the prototype's Web Audio levels are (its master gain set by ear on the headset).</summary>
        public double LevelDb { get; }

        /// <summary>A pitched layer's note, as "A6"; a glide's start.</summary>
        public string? Note { get; }

        /// <summary>A glide's end.</summary>
        public string? ToNote { get; }

        /// <summary>A noise layer's band: its centre (Low) or a sweep's start and end (Low, High), in Hz; a triangle's low-pass in Low.</summary>
        public double Low { get; }

        public double High { get; }

        public double Q { get; }

        /// <summary>A bell's partial at 3.01 times its frequency: 12 percent, 22 for the hero.</summary>
        public double Partial { get; }

        /// <summary>How far the pitch or band may vary from press to press, as a share (0.04 is 4 percent).</summary>
        public double Vary { get; }

        public double Amplitude => Math.Pow(10, LevelDb / 20);
    }

    /// <summary>
    /// The room's sounds as data and as samples (the handoff's section 4): one key, A major pentatonic, every bell a sine with a
    /// partial at 3.01 times its frequency; each placed at its element by the player; nothing loops. No audio assets: the
    /// samples are made here from these numbers. Every sound has its visual twin; a sound never carries a state alone.
    /// </summary>
    public static class KitSounds
    {
        /// <summary>The key's notes: A major pentatonic.</summary>
        public static IReadOnlyList<string> Key { get; } = new[] { "A", "B", "C#", "E", "F#" };

        public const double AttackMs = 6;

        /// <summary>The partial's ratio to its bell's frequency.</summary>
        public const double PartialRatio = 3.01;

        private static readonly IReadOnlyDictionary<KitSound, KitSoundPart[]> Parts = new Dictionary<KitSound, KitSoundPart[]>
        {
            [KitSound.Tick] = new[] { new KitSoundPart(KitSoundVoice.Noise, 0, 18, -38, low: 3200, q: 3, vary: 0.04) },
            [KitSound.Press] = new[]
            {
                new KitSoundPart(KitSoundVoice.Noise, 0, 12, -34, low: 2400, q: 3),
                new KitSoundPart(KitSoundVoice.Bell, 12, 60, -30, "A6", vary: 0.02),
            },
            [KitSound.Moment] = new[]
            {
                new KitSoundPart(KitSoundVoice.Bell, 0, 220, -24, "E6"),
                new KitSoundPart(KitSoundVoice.Bell, 60, 260, -25, "A6"),
            },
            [KitSound.Question] = new[]
            {
                new KitSoundPart(KitSoundVoice.Bell, 0, 200, -22, "C#6"),
                new KitSoundPart(KitSoundVoice.Bell, 70, 220, -22, "E6"),
                new KitSoundPart(KitSoundVoice.Bell, 140, 320, -23, "A6"),
            },
            [KitSound.Sent] = new[]
            {
                new KitSoundPart(KitSoundVoice.Bell, 0, 120, -28, "A6"),
                new KitSoundPart(KitSoundVoice.Bell, 70, 200, -29, "E6"),
            },
            [KitSound.Open] = new[]
            {
                new KitSoundPart(KitSoundVoice.Sweep, 0, 280, -30, low: 400, high: 2400, q: 2),
                new KitSoundPart(KitSoundVoice.Bell, 0, 300, -34, "E5"),
            },
            [KitSound.Fold] = new[] { new KitSoundPart(KitSoundVoice.Sweep, 0, 220, -31, low: 2400, high: 400, q: 2) },
            // The hero: four rising bells 60 ms apart, from 700 to 460 ms, bright; air above them from 200 ms. The air's level is
            // DESIGN's (the handoff gives the bells' -18 to -21 dB only).
            [KitSound.Passed] = new[]
            {
                new KitSoundPart(KitSoundVoice.Bell, 0, 700, -18, "A5", partial: 0.22),
                new KitSoundPart(KitSoundVoice.Bell, 60, 620, -19, "C#6", partial: 0.22),
                new KitSoundPart(KitSoundVoice.Bell, 120, 540, -20, "E6", partial: 0.22),
                new KitSoundPart(KitSoundVoice.Bell, 180, 460, -21, "A6", partial: 0.22),
                new KitSoundPart(KitSoundVoice.Sweep, 200, 500, -32, low: 6000, high: 9000, q: 1),
            },
            [KitSound.Failed] = new[]
            {
                new KitSoundPart(KitSoundVoice.Triangle, 0, 300, -22, "E5", low: 1400),
                new KitSoundPart(KitSoundVoice.Triangle, 120, 300, -22, "B4", low: 1400),
            },
            [KitSound.Lost] = new[] { new KitSoundPart(KitSoundVoice.Glide, 0, 500, -26, "A3", "E3") },
        };

        public static IReadOnlyList<KitSoundPart> PartsOf(KitSound sound) => Parts[sound];

        /// <summary>From its start to the end of its last layer.</summary>
        public static double DurationMs(KitSound sound) => Parts[sound].Max(part => part.OnsetMs + part.DurationMs);

        /// <summary>A note's frequency, equal-tempered from A4 at 440 Hz: "C#6" is 1108.73 Hz.</summary>
        public static double Frequency(string note)
        {
            var octave = note[note.Length - 1] - '0';
            var name = note.Substring(0, note.Length - 1);
            var semitone = name switch
            {
                "C" => -9, "C#" => -8, "D" => -7, "D#" => -6, "E" => -5, "F" => -4, "F#" => -3, "G" => -2, "G#" => -1, "A" => 0, "A#" => 1, "B" => 2,
                _ => throw new ArgumentException("Not a note: " + note, nameof(note)),
            };
            return 440 * Math.Pow(2, (semitone + 12 * (octave - 4)) / 12.0);
        }

        /// <summary>A note's name without its octave: "C#6" is "C#".</summary>
        public static string NameOf(string note) => note.Substring(0, note.Length - 1);

        /// <summary>
        /// The sound's samples, mono, at <paramref name="rate"/> Hz, as long as the sound and no longer (nothing loops);
        /// <paramref name="variation"/> from -1 to 1 shifts a varying layer by that share of its own range, so presses differ.
        /// </summary>
        public static float[] Render(KitSound sound, int rate, double variation = 0)
        {
            var samples = new float[(int)Math.Ceiling(DurationMs(sound) / 1000 * rate)];
            var seed = 1 + (int)sound * 7919;
            foreach (var part in Parts[sound])
            {
                var shift = 1 + part.Vary * Math.Max(-1, Math.Min(1, variation));
                var start = (int)(part.OnsetMs / 1000 * rate);
                var length = (int)(part.DurationMs / 1000 * rate);
                var random = new Random(seed++);
                var filter = new Biquad();
                double phase = 0, partialPhase = 0;
                for (var i = 0; i < length && start + i < samples.Length; i++)
                {
                    var t = i / (double)rate;
                    var through = i / (double)length;
                    var envelope = Envelope(t * 1000, part.DurationMs);
                    double value;
                    switch (part.Voice)
                    {
                        case KitSoundVoice.Bell:
                        {
                            var frequency = Frequency(part.Note!) * shift;
                            phase += 2 * Math.PI * frequency / rate;
                            partialPhase += 2 * Math.PI * frequency * PartialRatio / rate;
                            value = (Math.Sin(phase) + part.Partial * Math.Sin(partialPhase)) / (1 + part.Partial);
                            break;
                        }
                        case KitSoundVoice.Glide:
                        {
                            var frequency = Frequency(part.Note!) * Math.Pow(Frequency(part.ToNote!) / Frequency(part.Note!), through);
                            phase += 2 * Math.PI * frequency / rate;
                            value = Math.Sin(phase);
                            break;
                        }
                        case KitSoundVoice.Triangle:
                        {
                            phase += Frequency(part.Note!) / rate;
                            var saw = phase - Math.Floor(phase);
                            if (i == 0) filter.LowPass(part.Low, 0.707, rate);
                            value = filter.Process(4 * Math.Abs(saw - 0.5) - 1);
                            break;
                        }
                        default:
                        {
                            var centre = part.Voice == KitSoundVoice.Sweep
                                ? part.Low * Math.Pow(part.High / part.Low, through)
                                : part.Low * shift;
                            if (i == 0 || part.Voice == KitSoundVoice.Sweep) filter.BandPass(centre, part.Q, rate);
                            value = filter.Process(random.NextDouble() * 2 - 1) * 2;
                            break;
                        }
                    }
                    samples[start + i] += (float)(value * envelope * part.Amplitude);
                }
            }
            return samples;
        }

        /// <summary>A 6 ms attack, then an exponential decay to -60 dB by the layer's end.</summary>
        public static double Envelope(double ms, double durationMs)
        {
            if (ms < 0 || ms >= durationMs) return 0;
            if (ms < AttackMs) return ms / AttackMs;
            return Math.Exp(-6.9 * (ms - AttackMs) / Math.Max(1, durationMs - AttackMs));
        }

        /// <summary>A second-order filter (the RBJ cookbook's), for the noise bands and the triangle's low-pass.</summary>
        private sealed class Biquad
        {
            private double b0, b1, b2, a1, a2, x1, x2, y1, y2;

            public void BandPass(double centre, double q, int rate)
            {
                var w = 2 * Math.PI * Math.Min(centre, rate * 0.45) / rate;
                var alpha = Math.Sin(w) / (2 * q);
                var a0 = 1 + alpha;
                (b0, b1, b2, a1, a2) = (alpha / a0, 0, -alpha / a0, -2 * Math.Cos(w) / a0, (1 - alpha) / a0);
            }

            public void LowPass(double cutoff, double q, int rate)
            {
                var w = 2 * Math.PI * Math.Min(cutoff, rate * 0.45) / rate;
                var alpha = Math.Sin(w) / (2 * q);
                var cos = Math.Cos(w);
                var a0 = 1 + alpha;
                (b0, b1, b2, a1, a2) = ((1 - cos) / 2 / a0, (1 - cos) / a0, (1 - cos) / 2 / a0, -2 * cos / a0, (1 - alpha) / a0);
            }

            public double Process(double x)
            {
                var y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                (x2, x1, y2, y1) = (x1, x, y1, y);
                return y;
            }
        }
    }
}

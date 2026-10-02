#nullable enable
using System;
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>
    /// The stage's sound in the Glaze direction, which the owner chose on 2026-09-29: each character is
    /// a small glazed ceramic object that events strike softly with a felt mallet, and silence means
    /// all is well. Every note comes from one D major pentatonic scale, and each of the six bots keeps
    /// its own home note (docs/internal/architecture/XR_CLIENT.md, "Sound").
    /// </summary>
    /// <remarks>
    /// A line by line port of the synthesis on the soundbook page the owner listened to: plain
    /// arithmetic with a seeded generator, the page's pitches, envelopes, loudness targets and room,
    /// so a cue renders to the same samples every time on a given platform. The page's layers each had
    /// a pan; in the headset a cue sounds from a place in the room instead (its character, the
    /// workspace or the whole stage), so layers mix to one mono channel and only their send to the
    /// room is kept. Nothing here makes a sound or uses an engine.
    /// </remarks>
    public static class GlazeSynthesizer
    {
        /// <summary>How many bots have a note of their own.</summary>
        public const int Bots = 6;

        /// <summary>The highest sample a cue reaches before the room, as on the page.</summary>
        public const double Ceiling = 0.6;

        private const double Tau = Math.PI * 2;

        /// <summary>
        /// Each bot's home note as a MIDI note number, from the person's left: A3, B3, D4, E4, F sharp 4
        /// and A4.
        /// </summary>
        public static IReadOnlyList<int> Homes { get; } = new[] { 57, 59, 62, 64, 66, 69 };

        /// <summary>D4, the key's own note and the middle of the bots': a control's cues, which belong to no bot, sound on it.</summary>
        private const int Tonic = 62;

        /// <summary>
        /// Where the page placed each bot, from the left: the sine of its angle on the stage's arc,
        /// whose outermost characters stand 30 degrees to either side of the person. In the headset each
        /// bot sounds from where its character stands, which the stage's default arc puts at these
        /// angles.
        /// </summary>
        public static IReadOnlyList<double> Pans { get; } = new[] { -0.5, -0.31, -0.1, 0.1, 0.31, 0.5 };

        /// <summary>Every cue, in the soundbook's order.</summary>
        public static IReadOnlyList<SoundCue> Cues { get; } = (SoundCue[])Enum.GetValues(typeof(SoundCue));

        /// <summary>The six inharmonic modes of a struck glazed object: frequency ratio, level and decay.</summary>
        private static readonly double[,] Modes =
        {
            { 1, 1, 1 },
            { 2.006, 0.3, 0.62 },
            { 2.954, 0.22, 0.38 },
            { 4.11, 0.11, 0.22 },
            { 5.57, 0.05, 0.13 },
            { 7.23, 0.025, 0.08 },
        };

        /// <summary>D major pentatonic from MIDI note 12 to 120, so every note agrees with every other.</summary>
        private static readonly int[] Pentatonic = BuildScale(new[] { 2, 4, 6, 9, 11 });

        /// <summary>How long a cue is before the room, in seconds.</summary>
        public static double DurationOf(SoundCue cue) => SpecOf(cue, 0).Duration;

        /// <summary>The loudness a cue is set to, matching its importance, in LUFS as <see cref="Loudness"/> measures it.</summary>
        public static double TargetOf(SoundCue cue) => SpecOf(cue, 0).Target;

        /// <summary>
        /// How many renders a cue has: one per bot, or one for a cue no bot's note decides: the stage's,
        /// which plays every bot's note, and a control's, on the key's note.
        /// </summary>
        public static int VoicesOf(SoundCue cue) => cue == SoundCue.LastKnown || cue == SoundCue.Touch || cue == SoundCue.NotNow ? 1 : Bots;

        /// <summary>
        /// Renders one cue for one bot as the page renders it: each layer in turn, all of them scaled
        /// together to the cue's loudness target (never above <see cref="Ceiling"/>), with a short fade
        /// at the very end. The dry mix, and what each layer sends to the room, before the room.
        /// </summary>
        public static GlazeVoice RenderVoice(SoundCue cue, int bot, int sampleRate)
        {
            CheckRate(sampleRate);
            if (bot < 0 || bot >= Bots) throw new ArgumentOutOfRangeException(nameof(bot), bot, "Bots are numbered 0 to 5.");
            var spec = SpecOf(cue, bot);
            var n = (int)Math.Ceiling(spec.Duration * sampleRate);
            var mix = new float[n];
            var layers = new float[spec.Layers.Length][];
            for (var j = 0; j < layers.Length; j++)
            {
                var layer = spec.Layers[j];
                var buffer = new float[n];
                layer.Draw(buffer, sampleRate);
                if (layer.Muffle > 0)
                {
                    // Heard through a wall.
                    Filter(buffer, sampleRate, FilterType.Lowpass, layer.Muffle, 0.6);
                    Filter(buffer, sampleRate, FilterType.Lowpass, layer.Muffle, 0.6);
                }
                layers[j] = buffer;
                for (var k = 0; k < n; k++) mix[k] = (float)((double)mix[k] + buffer[k]);
            }

            var gain = Math.Pow(10, (spec.Target - Loudness(mix, sampleRate)) / 20);
            double peak = 0;
            for (var k = 0; k < n; k++) peak = Math.Max(peak, Math.Abs(mix[k]));
            if (peak * gain > Ceiling) gain = Ceiling / peak;
            var fade = Round(0.08 * sampleRate);
            foreach (var layer in layers) Scale(layer, gain, fade);
            Scale(mix, gain, fade);

            var send = new float[n];
            for (var j = 0; j < layers.Length; j++)
            {
                var level = spec.Layers[j].Send;
                var layer = layers[j];
                for (var k = 0; k < n; k++) send[k] = (float)((double)send[k] + layer[k] * level);
            }
            return new GlazeVoice(cue, bot, spec.Duration, spec.Target, mix, send);
        }

        /// <summary>
        /// Renders one cue for one bot as it plays: the dry mix, and the room it sends. The same
        /// samples as <see cref="RenderAll"/> gives for it, since both put bots in the room in pairs.
        /// </summary>
        public static float[] Render(SoundCue cue, int bot, GlazeRoom room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (bot < 0 || bot >= VoicesOf(cue)) throw new ArgumentOutOfRangeException(nameof(bot), bot, "No such render of " + cue + ".");
            var (first, second) = PairOf(cue, bot, room);
            return bot % 2 == 0 ? first.Samples : second!.Samples;
        }

        /// <summary>
        /// Renders every cue for every bot as it plays, in the soundbook's order; the stage's cue once.
        /// Meant for a worker thread: it takes a second or more.
        /// </summary>
        public static IEnumerable<GlazeClip> RenderAll(int sampleRate)
        {
            CheckRate(sampleRate);
            var room = GlazeRoom.Create(sampleRate);
            foreach (var cue in Cues)
            {
                for (var bot = 0; bot < VoicesOf(cue); bot += 2)
                {
                    var (first, second) = PairOf(cue, bot, room);
                    yield return first;
                    if (second != null) yield return second;
                }
            }
        }

        /// <summary>
        /// An even bot and the next one, put in the room together, so they share its transforms: the
        /// same two voices in the same order always give the same samples.
        /// </summary>
        private static (GlazeClip First, GlazeClip? Second) PairOf(SoundCue cue, int bot, GlazeRoom room)
        {
            var first = bot - bot % 2;
            var voices = new List<GlazeVoice> { RenderVoice(cue, first, room.SampleRate) };
            if (first + 1 < VoicesOf(cue)) voices.Add(RenderVoice(cue, first + 1, room.SampleRate));
            var played = room.Play(voices);
            return (new GlazeClip(cue, first, played[0]), played.Length > 1 ? new GlazeClip(cue, first + 1, played[1]) : null);
        }

        /// <summary>
        /// Loudness roughly as broadcast meters measure it: K-weighting, then the loudest 400 ms window
        /// on a 50 ms hop, in LUFS. The page sets every cue to its target with it.
        /// </summary>
        public static double Loudness(float[] samples, int sampleRate)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            var highpass = new Biquad().Set(FilterType.Highpass, 60, 0.5, sampleRate);
            var shelf = new Biquad().Set(FilterType.HighShelf, 1700, 0.707, sampleRate, 4);
            var window = Round(0.4 * sampleRate);
            var hop = Round(0.05 * sampleRate);
            var sums = new double[samples.Length + 1];
            for (var i = 0; i < samples.Length; i++)
            {
                var y = shelf.Run(highpass.Run(samples[i]));
                sums[i + 1] = sums[i] + y * y;
            }
            var best = 1e-12;
            if (samples.Length <= window)
            {
                best = sums[samples.Length] / window;
            }
            else
            {
                for (var s = 0; s + window <= samples.Length; s += hop) best = Math.Max(best, (sums[s + window] - sums[s]) / window);
            }
            return -0.691 + 10 * Math.Log10(best);
        }

        internal static void CheckRate(int sampleRate)
        {
            if (sampleRate < 8000 || sampleRate > 192000)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Expected an audio sample rate between 8 and 192 kHz.");
            }
        }

        /// <summary>JavaScript's Math.round, which rounds halves up; Math.Round rounds them to even.</summary>
        internal static int Round(double x) => (int)Math.Floor(x + 0.5);

        /// <summary>A raised cosine from 0 to 1. Every onset, release and glide uses it, so nothing clicks.</summary>
        internal static double Rc(double x) => x <= 0 ? 0 : x >= 1 ? 1 : 0.5 - 0.5 * Math.Cos(Math.PI * x);

        private static double Mtof(double note) => 440 * Math.Pow(2, (note - 69) / 12);

        private static int[] BuildScale(int[] pitchClasses)
        {
            var notes = new List<int>();
            for (var m = 12; m <= 120; m++)
            {
                if (Array.IndexOf(pitchClasses, m % 12) >= 0) notes.Add(m);
            }
            return notes.ToArray();
        }

        /// <summary>The note a number of scale steps from another, staying within the scale's range.</summary>
        private static int D(int note, int steps)
        {
            var i = 0;
            while (i + 1 < Pentatonic.Length && Pentatonic[i + 1] <= note) i++;
            return Pentatonic[Math.Max(0, Math.Min(Pentatonic.Length - 1, i + steps))];
        }

        private static void Scale(float[] data, double gain, int fade)
        {
            var n = data.Length;
            for (var q = 0; q < n; q++) data[q] = (float)(data[q] * (gain * (q > n - fade ? Rc((double)(n - q) / fade) : 1)));
        }

        private static void Filter(float[] buffer, int sampleRate, FilterType type, double frequency, double q)
        {
            var filter = new Biquad().Set(type, frequency, q, sampleRate);
            for (var i = 0; i < buffer.Length; i++) buffer[i] = (float)filter.Run(buffer[i]);
        }

        /// <summary>The page's Glaze cues. Times are in seconds from the cue's start.</summary>
        private static Spec SpecOf(SoundCue cue, int bot)
        {
            var h = Homes[bot];
            switch (cue)
            {
                case SoundCue.Working:
                {
                    var f = Mtof(h);
                    return new Spec(1.0, -28, new Layer(0.2, (o, sr) =>
                    {
                        Strike(o, sr, 0.02, f, vel: 0.5, decay: 0.5);
                        Strike(o, sr, 0.13, f, vel: 0.32, decay: 0.45, contact: false);
                    }));
                }
                case SoundCue.CheckingItsWork:
                {
                    var notes = new[] { h, D(h, 1), D(h, 2), D(h, 1) };
                    var velocities = new[] { 0.34, 0.3, 0.3, 0.26 };
                    return new Spec(0.9, -28, new Layer(0.2, (o, sr) =>
                    {
                        for (var i = 0; i < 4; i++)
                        {
                            Strike(o, sr, 0.02 + 0.1 * i, Mtof(notes[i]), vel: velocities[i], decay: 0.3, bright: 0.8, contact: i == 0);
                        }
                    }));
                }
                case SoundCue.WaitingForYou:
                {
                    var f2 = Mtof(D(h, 2));
                    return new Spec(1.6, -20, new Layer(0.22, (o, sr) =>
                    {
                        Strike(o, sr, 0.02, Mtof(h), vel: 0.55, decay: 0.8);
                        Strike(o, sr, 0.19, f2, vel: 0.8, decay: 0.9);
                        Ring(o, sr, 0.19, f2, 1.25, 0.16);
                    }));
                }
                case SoundCue.FinishedThisRound:
                    return new Spec(1.5, -24, new Layer(0.22, (o, sr) =>
                    {
                        Strike(o, sr, 0.02, Mtof(D(h, 2)), vel: 0.45, decay: 0.7);
                        Strike(o, sr, 0.24, Mtof(h), vel: 0.42, decay: 0.7, bright: 0.8);
                    }));
                case SoundCue.CouldNotFinish:
                {
                    var fa = Mtof(D(h, -2));
                    return new Spec(1.0, -22, new Layer(0.2, (o, sr) =>
                    {
                        Strike(o, sr, 0.02, fa, vel: 0.75, decay: 0.5, crack: 0.8, bright: 0.7);
                        Strike(o, sr, 0.17, fa * Math.Pow(2, -3.0 / 12), vel: 0.55, decay: 0.45, crack: 1, damp: 0.24, bright: 0.6, contact: false);
                    }));
                }
                case SoundCue.CantTellYet:
                {
                    var f = Mtof(h);
                    return new Spec(1.4, -25, new Layer(0.28, (o, sr) =>
                    {
                        Strike(o, sr, 0.03, f, vel: 0.5, decay: 0.7, blur: 1, attack: 0.03);
                        Strike(o, sr, 0.42, f * Math.Pow(2, -0.45 / 12), vel: 0.3, decay: 0.5, blur: 1, attack: 0.05, contact: false);
                        Swell(o, sr, 0, 1.3, f, f * 0.97, q: 7, amp: 0.05, peak: 0.45, seed: 13);
                    }));
                }
                case SoundCue.Stopped:
                {
                    var f = Mtof(h);
                    return new Spec(0.5, -25, new Layer(0.18, (o, sr) => Strike(o, sr, 0.02, f, vel: 0.6, decay: 1, damp: 0.13)));
                }
                case SoundCue.LastKnown:
                {
                    // Every bot's note in turn from the left, gliding down a little, through a wall.
                    var layers = new Layer[Bots];
                    for (var j = 0; j < Bots; j++)
                    {
                        var at = 0.03 + 0.08 * j;
                        var f = Mtof(Homes[j]);
                        layers[j] = new Layer(
                            0.55,
                            (o, sr) => Strike(o, sr, at, f, vel: 0.42, attack: 0.03, decay: 0.7, glide: -0.1, contact: false),
                            muffle: 650);
                    }
                    return new Spec(1.9, -26, layers);
                }
                case SoundCue.Open:
                {
                    var notes = new[] { h, D(h, 2), D(h, 4), D(h, 5) };
                    var layers = new List<Layer>();
                    for (var i = 0; i < 4; i++)
                    {
                        var at = 0.02 + 0.06 * i;
                        var f = Mtof(notes[i]);
                        var vel = 0.35 + 0.07 * i;
                        var contact = i == 0;
                        layers.Add(new Layer(0.12, (o, sr) => Strike(o, sr, at, f, vel: vel, decay: 0.65, contact: contact)));
                    }
                    layers.Add(new Layer(0.1, (o, sr) => Swell(o, sr, 0, 0.6, 380, 1500, q: 1.3, amp: 0.1, peak: 0.7, seed: 19)));
                    return new Spec(1.3, -24, layers.ToArray());
                }
                case SoundCue.Close:
                {
                    var notes = new[] { D(h, 4), D(h, 2), h };
                    var layers = new List<Layer>();
                    for (var i = 0; i < 3; i++)
                    {
                        var at = 0.02 + 0.075 * i;
                        var f = Mtof(notes[i]);
                        var vel = 0.4 - 0.05 * i;
                        var decay = i == 2 ? 0.55 : 0.5;
                        var contact = i == 2;
                        layers.Add(new Layer(0.12, (o, sr) => Strike(o, sr, at, f, vel: vel, decay: decay, contact: contact)));
                    }
                    layers.Add(new Layer(0.1, (o, sr) => Swell(o, sr, 0, 0.5, 1500, 380, q: 1.3, amp: 0.1, peak: 0.3, seed: 23)));
                    return new Spec(1.2, -26, layers.ToArray());
                }
                case SoundCue.Approve:
                    return new Spec(1.2, -24, new Layer(0.1, (o, sr) =>
                    {
                        Strike(o, sr, 0.02, Mtof(h), vel: 0.6, decay: 0.65);
                        Strike(o, sr, 0.032, Mtof(D(h, 3)), vel: 0.55, decay: 0.65, contact: false);
                    }));
                case SoundCue.Deny:
                    return new Spec(0.7, -25, new Layer(0.08, (o, sr) =>
                    {
                        Strike(o, sr, 0.02, Mtof(h), vel: 0.5, decay: 0.35);
                        Strike(o, sr, 0.13, Mtof(D(h, -2)), vel: 0.46, decay: 0.35, damp: 0.2, contact: false);
                    }));
                case SoundCue.TellIt:
                {
                    var notes = new[] { D(h, 1), D(h, 2), h };
                    var velocities = new[] { 0.3, 0.28, 0.3 };
                    return new Spec(0.6, -29, new Layer(0.08, (o, sr) =>
                    {
                        for (var i = 0; i < 3; i++)
                        {
                            Strike(o, sr, 0.02 + 0.07 * i, Mtof(notes[i]), vel: velocities[i], decay: 0.18, contact: i == 0);
                        }
                    }));
                }
                case SoundCue.SendAnswer:
                    // Waiting for you asked by rising from its note; the answer falls back onto it,
                    // a light tap, as Tell it's, then a warmer strike: quicker and lighter than
                    // Finished this round's fall, and from the workspace.
                    return new Spec(0.8, -27, new Layer(0.08, (o, sr) =>
                    {
                        Strike(o, sr, 0.02, Mtof(D(h, 2)), vel: 0.32, decay: 0.2);
                        Strike(o, sr, 0.11, Mtof(h), vel: 0.48, decay: 0.5, contact: false);
                    }));
                case SoundCue.Stop:
                {
                    var f = Mtof(D(h, -2));
                    return new Spec(0.5, -25, new Layer(0.06, (o, sr) =>
                    {
                        Strike(o, sr, 0.02, f, vel: 0.55, decay: 0.6, damp: 0.06);
                        Thud(o, sr, 0.03, 0.12, 150, 0.08, 29);
                    }));
                }
                case SoundCue.Touch:
                {
                    // Working's first strike alone, on the key's note, quieter: the press was taken.
                    var f = Mtof(Tonic);
                    return new Spec(0.6, -32, new Layer(0.08, (o, sr) => Strike(o, sr, 0.02, f, vel: 0.5, decay: 0.5)));
                }
                case SoundCue.NotNow:
                    // Deny's damped step, a note lower and quieter: the control can't take a press now.
                    return new Spec(0.7, -30, new Layer(0.08, (o, sr) =>
                    {
                        Strike(o, sr, 0.02, Mtof(D(Tonic, -1)), vel: 0.5, decay: 0.35);
                        Strike(o, sr, 0.13, Mtof(D(Tonic, -3)), vel: 0.46, decay: 0.35, damp: 0.2, contact: false);
                    }));
                default:
                    throw new ArgumentOutOfRangeException(nameof(cue), cue, "Unhandled cue.");
            }
        }

        /// <summary>
        /// A small glazed object struck with felt: six inharmonic modes, each paired with a twin a
        /// fraction of a hertz away, so the ring shimmers slowly like a real object instead of a pure
        /// tone. Crack detunes and dulls the modes, blur makes the pitch wander, glide bends it, damp
        /// catches the ring with a hand, and contact adds the mallet landing.
        /// </summary>
        private static void Strike(
            float[] output,
            int sr,
            double t0,
            double f,
            double vel = 0.7,
            double decay = 1,
            double attack = 0.004,
            double crack = 0,
            double blur = 0,
            double glide = 0,
            double bright = 1,
            double? damp = null,
            bool contact = true)
        {
            var random = new Random32((uint)(Round(f * 7) + 3));
            var baseDecay = decay * 0.42 * Math.Pow(220 / f, 0.3);
            var s0 = Round(t0 * sr);
            var attackSamples = Math.Max(1, Round(attack * sr));
            var dampAt = damp.HasValue ? Round(damp.Value * sr) : -1;
            var dampStep = Math.Exp(-1 / (0.02 * sr));
            for (var i = 0; i < Modes.GetLength(0); i++)
            {
                double ratio = Modes[i, 0], amp = Modes[i, 1], tm = Modes[i, 2];
                if (crack != 0)
                {
                    ratio *= 1 + (random.Next() - 0.5) * 0.07 * crack;
                    tm *= 1 - 0.55 * crack;
                }
                var fr = f * ratio;
                if (fr > sr * 0.42) continue;
                var a = amp * Math.Pow(vel, 0.6 + 0.7 * i) * Math.Pow(bright, i);
                var tau = baseDecay * tm;
                var beat = (0.35 + 0.22 * i) * (1 + 14 * crack);
                var twin = 0.3 + 0.3 * crack;
                var n = Math.Min(output.Length - s0, (int)Math.Ceiling((attack + tau * 7) * sr));
                // Two rotating phasors per mode, cheaper than a sine per sample; a glide or blur
                // retunes them every 32 samples.
                var p1 = random.Next() * Tau;
                var p2 = random.Next() * Tau;
                var wr = 0.8 + 0.41 * i;
                var wp = random.Next() * Tau;
                double c1 = Math.Cos(p1), s1 = Math.Sin(p1), c2 = Math.Cos(p2), s2 = Math.Sin(p2);
                double w1c = Math.Cos(Tau * fr / sr), w1s = Math.Sin(Tau * fr / sr);
                double w2c = Math.Cos(Tau * (fr + beat) / sr), w2s = Math.Sin(Tau * (fr + beat) / sr);
                double env = 1, dec = Math.Exp(-1 / (tau * sr)), dmp = 1;
                for (var k = 0; k < n; k++)
                {
                    if ((blur != 0 || glide != 0) && (k & 31) == 0)
                    {
                        var t = (double)k / sr;
                        var fm = glide != 0 ? Math.Pow(2, glide * t / 12) : 1;
                        if (blur != 0) fm *= 1 + blur * 0.028 * Math.Sin(Tau * wr * t + wp);
                        w1c = Math.Cos(Tau * fr * fm / sr);
                        w1s = Math.Sin(Tau * fr * fm / sr);
                        w2c = Math.Cos(Tau * (fr + beat) * fm / sr);
                        w2s = Math.Sin(Tau * (fr + beat) * fm / sr);
                        var m1 = 1 / Math.Sqrt(c1 * c1 + s1 * s1);
                        var m2 = 1 / Math.Sqrt(c2 * c2 + s2 * s2);
                        c1 *= m1;
                        s1 *= m1;
                        c2 *= m2;
                        s2 *= m2;
                    }
                    if (dampAt >= 0 && k > dampAt) dmp *= dampStep;
                    var e = env * dmp * (k < attackSamples ? Rc((double)k / attackSamples) : 1);
                    output[s0 + k] = (float)(output[s0 + k] + a * e * (s1 + twin * s2));
                    env *= dec;
                    if (k > attackSamples && env * dmp < 1e-5) break;
                    var nc1 = c1 * w1c - s1 * w1s;
                    s1 = s1 * w1c + c1 * w1s;
                    c1 = nc1;
                    var nc2 = c2 * w2c - s2 * w2s;
                    s2 = s2 * w2c + c2 * w2s;
                    c2 = nc2;
                }
            }
            if (contact) Thud(output, sr, t0, 0.05 * vel, 900, 0.014, 5);
            if (damp.HasValue) Thud(output, sr, t0 + damp.Value, 0.12 * vel, 170, 0.06, 9);
        }

        /// <summary>A low, soft knock of filtered noise: a felt mallet landing, or a hand pressing down.</summary>
        private static void Thud(float[] output, int sr, double t0, double amp, double cutoff, double length, uint seed)
        {
            var random = new Random32(seed);
            var a = new Biquad().Set(FilterType.Lowpass, cutoff, 0.7, sr);
            var b = new Biquad().Set(FilterType.Lowpass, cutoff, 0.7, sr);
            var s0 = Round(t0 * sr);
            var n = Round(length * sr);
            for (var k = 0; k < n && s0 + k < output.Length; k++)
            {
                var t = (double)k / sr;
                var e = Rc(t / 0.002) * Math.Exp(-t / (length / 4));
                output[s0 + k] = (float)(output[s0 + k] + amp * e * b.Run(a.Run(random.Next() * 2 - 1)) * 6);
            }
        }

        /// <summary>A soft sustained tone with a slow tremolo: the part of a strike that keeps waiting for the person.</summary>
        private static void Ring(float[] output, int sr, double t0, double f, double duration, double amp)
        {
            var s0 = Round(t0 * sr);
            var n = Math.Min(output.Length - s0, Round(duration * sr));
            double p = 0;
            for (var k = 0; k < n; k++)
            {
                var t = (double)k / sr;
                p += Tau * f / sr;
                var e = Rc(t / 0.08) * Math.Exp(-t / (duration * 0.45)) * Rc((duration - t) / 0.08) * (0.75 + 0.25 * Math.Cos(Tau * 4.4 * t));
                output[s0 + k] = (float)(output[s0 + k] + amp * e * Math.Sin(p));
            }
        }

        /// <summary>A swell of soft noise through a band that glides from one frequency to another.</summary>
        private static void Swell(float[] output, int sr, double t0, double duration, double f0, double f1, double q, double amp, double peak, uint seed)
        {
            var s0 = Round(t0 * sr);
            var n = Math.Min(output.Length - s0, Round(duration * sr));
            var noise = new PinkNoise(new Random32(seed));
            var a = new Biquad();
            var b = new Biquad();
            for (var k = 0; k < n; k++)
            {
                if (k % 32 == 0)
                {
                    var fc = f0 * Math.Pow(f1 / f0, Rc((double)k / n));
                    a.Set(FilterType.Bandpass, fc, q, sr);
                    b.Set(FilterType.Bandpass, fc, q, sr);
                }
                var x = (double)k / sr / duration;
                var envelope = x < peak ? Rc(x / peak) : Rc((1 - x) / (1 - peak));
                output[s0 + k] = (float)(output[s0 + k] + amp * envelope * b.Run(a.Run(noise.Next())) * 3);
            }
        }

        private sealed class Spec
        {
            public Spec(double duration, double target, params Layer[] layers)
            {
                Duration = duration;
                Target = target;
                Layers = layers;
            }

            public double Duration { get; }

            public double Target { get; }

            public Layer[] Layers { get; }
        }

        private sealed class Layer
        {
            public Layer(double send, Action<float[], int> draw, double muffle = 0)
            {
                Send = send;
                Draw = draw;
                Muffle = muffle;
            }

            /// <summary>How much of the layer goes to the room.</summary>
            public double Send { get; }

            public Action<float[], int> Draw { get; }

            /// <summary>The cutoff of the wall the layer is heard through, or 0 for none.</summary>
            public double Muffle { get; }
        }

        /// <summary>Soft pink noise (Paul Kellet's economy filter): less hiss than white noise, so less sharpness.</summary>
        private sealed class PinkNoise
        {
            private readonly Random32 random;
            private double b0;
            private double b1;
            private double b2;

            public PinkNoise(Random32 random) => this.random = random;

            public double Next()
            {
                var w = random.Next() * 2 - 1;
                b0 = 0.99765 * b0 + w * 0.099046;
                b1 = 0.963 * b1 + w * 0.2965164;
                b2 = 0.57 * b2 + w * 1.0526913;
                return (b0 + b1 + b2 + w * 0.1848) * 0.18;
            }
        }
    }

    /// <summary>A cue before the room, as the page renders it.</summary>
    public sealed class GlazeVoice
    {
        public GlazeVoice(SoundCue cue, int bot, double duration, double target, float[] dry, float[] send)
        {
            Cue = cue;
            Bot = bot;
            Duration = duration;
            Target = target;
            Dry = dry;
            Send = send;
        }

        public SoundCue Cue { get; }

        public int Bot { get; }

        /// <summary>In seconds.</summary>
        public double Duration { get; }

        /// <summary>In LUFS.</summary>
        public double Target { get; }

        /// <summary>Every layer mixed, at the cue's loudness, with the fade at its end.</summary>
        public float[] Dry { get; }

        /// <summary>What the layers send to the room, each at its own level.</summary>
        public float[] Send { get; }
    }

    /// <summary>A cue rendered for one bot, as it plays.</summary>
    public sealed class GlazeClip
    {
        public GlazeClip(SoundCue cue, int bot, float[] samples)
        {
            Cue = cue;
            Bot = bot;
            Samples = samples;
        }

        public SoundCue Cue { get; }

        /// <summary>The bot whose note it plays; 0 for the stage's cue, which plays every bot's note.</summary>
        public int Bot { get; }

        /// <summary>Mono samples at the sample rate they were rendered at.</summary>
        public float[] Samples { get; }
    }

    /// <summary>
    /// The short room the page played every cue in: an early reflection or two, then a tail that
    /// darkens as it fades, 1.3 seconds long. A cue plays as its dry mix plus its send convolved with
    /// the room's first channel, which is what a centered cue sent to the page's left ear.
    /// </summary>
    /// <remarks>
    /// The convolution is overlap-add with one transform size, the smallest power of two at least
    /// twice the room's length: each send is cut into segments that fit, and since the room is real,
    /// one complex transform convolves two segments at once, one as its real part and the other as
    /// its imaginary part. Not thread safe: the room keeps its scratch buffers.
    /// </remarks>
    public sealed class GlazeRoom
    {
        /// <summary>How long the room's response is, in seconds.</summary>
        public const double Seconds = 1.3;

        /// <summary>The page's seed for the room.</summary>
        public const uint Seed = 7;

        /// <summary>A played cue ends where it stays this far below its peak, 60 dB.</summary>
        public const double Floor = 0.001;

        private static readonly double[,] Taps =
        {
            { 0.007, 0.5 },
            { 0.013, -0.35 },
            { 0.019, 0.3 },
            { 0.029, -0.22 },
            { 0.041, 0.16 },
        };

        private readonly float[] response;
        private readonly int size;
        private readonly int segment;
        private readonly int[] reversed;

        /// <summary>For the stage that joins transforms of half size h, cos and sin of pi j / h at h - 1 + j.</summary>
        private readonly double[] cos;
        private readonly double[] sin;
        private readonly double[] roomRe;
        private readonly double[] roomIm;
        private readonly double[] re;
        private readonly double[] im;

        private GlazeRoom(int sampleRate, float[] response)
        {
            SampleRate = sampleRate;
            this.response = response;
            size = 1;
            while (size < 2 * response.Length) size <<= 1;
            segment = size - response.Length + 1;
            var bits = 0;
            while (1 << bits < size) bits++;
            reversed = new int[size];
            for (var i = 0; i < size; i++)
            {
                var r = 0;
                for (var b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b);
                reversed[i] = r;
            }
            cos = new double[size];
            sin = new double[size];
            for (var half = 1; half < size; half <<= 1)
            {
                for (var j = 0; j < half; j++)
                {
                    var angle = Math.PI * j / half;
                    cos[half - 1 + j] = Math.Cos(angle);
                    sin[half - 1 + j] = Math.Sin(angle);
                }
            }
            roomRe = new double[size];
            roomIm = new double[size];
            for (var k = 0; k < response.Length; k++) roomRe[k] = response[k];
            Transform(roomRe, roomIm, inverse: false);
            re = new double[size];
            im = new double[size];
        }

        public int SampleRate { get; }

        /// <summary>The room's impulse response, its first channel.</summary>
        public IReadOnlyList<float> Response => response;

        public static GlazeRoom Create(int sampleRate)
        {
            GlazeSynthesizer.CheckRate(sampleRate);
            var n = GlazeSynthesizer.Round(Seconds * sampleRate);
            var random = new Random32(Seed);
            var d = new float[n];
            var t60 = Seconds * 0.8;
            var pre = GlazeSynthesizer.Round(0.012 * sampleRate);
            double lp = 0;
            for (var k = pre; k < n; k++)
            {
                var t = (double)(k - pre) / sampleRate;
                var env = Math.Pow(10, -3 * t / t60);
                var cut = 0.55 - 0.45 * Math.Min(1, t / t60);
                lp += cut * (random.Next() * 2 - 1 - lp);
                d[k] = (float)(lp * env);
            }
            for (var i = 0; i < Taps.GetLength(0); i++)
            {
                var at = pre + GlazeSynthesizer.Round(Taps[i, 0] * sampleRate);
                if (at < n) d[at] = (float)(d[at] + Taps[i, 1] * 0.6);
            }
            double energy = 0;
            for (var k = 0; k < n; k++) energy += (double)d[k] * d[k];
            var scale = 0.9 / Math.Sqrt(energy);
            for (var k = 0; k < n; k++) d[k] = (float)(d[k] * scale);
            return new GlazeRoom(sampleRate, d);
        }

        /// <summary>
        /// Voices as they play in the room: each one's dry mix plus its send convolved with the room,
        /// ending where it stays <see cref="Floor"/> below its peak, with a 10 ms fade so it ends
        /// without a click. Segments are paired in the order of the voices, so the same voices in the
        /// same order always give the same samples.
        /// </summary>
        internal float[][] Play(IReadOnlyList<GlazeVoice> voices)
        {
            if (voices == null) throw new ArgumentNullException(nameof(voices));
            var wet = new double[voices.Count][];
            var jobs = new List<(int Voice, int Offset, int Length)>();
            for (var v = 0; v < voices.Count; v++)
            {
                var send = voices[v].Send;
                wet[v] = new double[send.Length + response.Length - 1];
                for (var offset = 0; offset < send.Length; offset += segment)
                {
                    jobs.Add((v, offset, Math.Min(segment, send.Length - offset)));
                }
            }
            for (var j = 0; j < jobs.Count; j += 2)
            {
                Array.Clear(re, 0, size);
                Array.Clear(im, 0, size);
                Load(re, voices, jobs[j]);
                if (j + 1 < jobs.Count) Load(im, voices, jobs[j + 1]);
                Transform(re, im, inverse: false);
                for (var k = 0; k < size; k++)
                {
                    double xr = re[k], xi = im[k];
                    re[k] = xr * roomRe[k] - xi * roomIm[k];
                    im[k] = xr * roomIm[k] + xi * roomRe[k];
                }
                Transform(re, im, inverse: true);
                Add(re, wet, jobs[j]);
                if (j + 1 < jobs.Count) Add(im, wet, jobs[j + 1]);
            }
            var played = new float[voices.Count][];
            for (var v = 0; v < voices.Count; v++) played[v] = Finish(voices[v].Dry, wet[v]);
            return played;
        }

        private static void Load(double[] into, IReadOnlyList<GlazeVoice> voices, (int Voice, int Offset, int Length) job)
        {
            var send = voices[job.Voice].Send;
            for (var k = 0; k < job.Length; k++) into[k] = send[job.Offset + k];
        }

        private void Add(double[] from, double[][] wet, (int Voice, int Offset, int Length) job)
        {
            var into = wet[job.Voice];
            var count = Math.Min(job.Length + response.Length - 1, into.Length - job.Offset);
            for (var k = 0; k < count; k++) into[job.Offset + k] += from[k] / size;
        }

        /// <summary>Adds the dry mix to the room, in place, and cuts the silent tail.</summary>
        private float[] Finish(float[] dry, double[] wet)
        {
            double peak = 0;
            for (var k = 0; k < wet.Length; k++)
            {
                if (k < dry.Length) wet[k] += dry[k];
                peak = Math.Max(peak, Math.Abs(wet[k]));
            }
            var last = wet.Length - 1;
            while (last > 0 && Math.Abs(wet[last]) <= peak * Floor) last--;
            var fade = GlazeSynthesizer.Round(0.01 * SampleRate);
            var length = Math.Min(wet.Length, last + 1 + fade);
            var output = new float[length];
            for (var k = 0; k < length; k++)
            {
                var toEnd = length - k;
                output[k] = (float)(wet[k] * (toEnd < fade ? GlazeSynthesizer.Rc((double)toEnd / fade) : 1));
            }
            return output;
        }

        /// <summary>A radix-2 fast Fourier transform, in place; the inverse is not scaled.</summary>
        private void Transform(double[] real, double[] imaginary, bool inverse)
        {
            for (var i = 0; i < size; i++)
            {
                var j = reversed[i];
                if (i >= j) continue;
                var t = real[i];
                real[i] = real[j];
                real[j] = t;
                t = imaginary[i];
                imaginary[i] = imaginary[j];
                imaginary[j] = t;
            }
            // The first stage needs no twiddle factors.
            for (var u = 0; u < size; u += 2)
            {
                double ar = real[u], ai = imaginary[u], br = real[u + 1], bi = imaginary[u + 1];
                real[u] = ar + br;
                imaginary[u] = ai + bi;
                real[u + 1] = ar - br;
                imaginary[u + 1] = ai - bi;
            }
            var sign = inverse ? 1.0 : -1.0;
            for (var half = 2; half < size; half <<= 1)
            {
                var length = half << 1;
                var twiddles = half - 1;
                for (var start = 0; start < size; start += length)
                {
                    for (var j = 0; j < half; j++)
                    {
                        var wr = cos[twiddles + j];
                        var wi = sign * sin[twiddles + j];
                        var u = start + j;
                        var v = u + half;
                        var tr = real[v] * wr - imaginary[v] * wi;
                        var ti = real[v] * wi + imaginary[v] * wr;
                        real[v] = real[u] - tr;
                        imaginary[v] = imaginary[u] - ti;
                        real[u] += tr;
                        imaginary[u] += ti;
                    }
                }
            }
        }
    }

    /// <summary>Biquad filters in the forms of Robert Bristow-Johnson's audio EQ cookbook.</summary>
    internal enum FilterType
    {
        Lowpass,
        Highpass,
        Bandpass,
        HighShelf,
    }

    internal sealed class Biquad
    {
        private double b0 = 1;
        private double b1;
        private double b2;
        private double a1;
        private double a2;
        private double x1;
        private double x2;
        private double y1;
        private double y2;

        /// <summary>Sets the coefficients and keeps the state, so a filter can glide.</summary>
        public Biquad Set(FilterType type, double f, double q, double sr, double db = 0)
        {
            f = Math.Max(10, Math.Min(f, sr * 0.45));
            var w0 = Math.PI * 2 * f / sr;
            var c = Math.Cos(w0);
            var s = Math.Sin(w0);
            var al = s / (2 * q);
            double nb0, nb1, nb2, a0, na1, na2;
            switch (type)
            {
                case FilterType.Lowpass:
                    nb0 = (1 - c) / 2;
                    nb1 = 1 - c;
                    nb2 = nb0;
                    a0 = 1 + al;
                    na1 = -2 * c;
                    na2 = 1 - al;
                    break;
                case FilterType.Highpass:
                    nb0 = (1 + c) / 2;
                    nb1 = -(1 + c);
                    nb2 = nb0;
                    a0 = 1 + al;
                    na1 = -2 * c;
                    na2 = 1 - al;
                    break;
                case FilterType.Bandpass:
                    nb0 = al;
                    nb1 = 0;
                    nb2 = -al;
                    a0 = 1 + al;
                    na1 = -2 * c;
                    na2 = 1 - al;
                    break;
                default:
                    var a = Math.Pow(10, db / 40);
                    var sq = 2 * Math.Sqrt(a) * al;
                    nb0 = a * (a + 1 + (a - 1) * c + sq);
                    nb1 = -2 * a * (a - 1 + (a + 1) * c);
                    nb2 = a * (a + 1 + (a - 1) * c - sq);
                    a0 = a + 1 - (a - 1) * c + sq;
                    na1 = 2 * (a - 1 - (a + 1) * c);
                    na2 = a + 1 - (a - 1) * c - sq;
                    break;
            }
            b0 = nb0 / a0;
            b1 = nb1 / a0;
            b2 = nb2 / a0;
            a1 = na1 / a0;
            a2 = na2 / a0;
            return this;
        }

        public double Run(double x)
        {
            var y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;
            return y;
        }
    }

    /// <summary>The page's seeded generator (mulberry32), so every render of a cue is identical.</summary>
    internal sealed class Random32
    {
        private uint state;

        public Random32(uint seed) => state = seed;

        /// <summary>A number from 0 up to, but not including, 1.</summary>
        public double Next()
        {
            unchecked
            {
                state += 0x6d2b79f5;
                var t = (state ^ (state >> 15)) * (1u | state);
                t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }
}

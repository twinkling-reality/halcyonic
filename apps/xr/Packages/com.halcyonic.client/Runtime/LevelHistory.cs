#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>
    /// The microphone's real level while hold to talk records, for the bar's level (DESIGN's SYSTEM-v4 and BUILD-SPEC 2.1;
    /// the lead's truth rule, 2026-11-05): one level every <see cref="StepSeconds"/> from the samples the headset recorded in
    /// that time, a rolling history of <see cref="Bars"/>, newest at the right. Never a made-up animation: where nothing is
    /// recorded there is no history, and the bar shows no level. Allocates once.
    /// </summary>
    public sealed class LevelHistory
    {
        public const int Bars = 26;

        public const float StepSeconds = 0.05f;

        /// <summary>The quietest level drawn, in dB below full scale: anything quieter is the floor.</summary>
        public const float FloorDb = -54f;

        /// <summary>The loudest level drawn, in dB below full scale: anything louder is the top.</summary>
        public const float TopDb = -12f;

        private readonly float[] levels = new float[Bars];
        private int newest = -1;
        private int count;

        /// <summary>How many levels were taken since recording began, at most <see cref="Bars"/>.</summary>
        public int Count => count;

        /// <summary>Takes a level from <paramref name="length"/> samples of <paramref name="samples"/>, in -1 to 1.</summary>
        public void Add(float[] samples, int length)
        {
            newest = (newest + 1) % Bars;
            levels[newest] = Level(samples, length);
            if (count < Bars) count++;
        }

        /// <summary>
        /// The level <paramref name="bar"/> shows, 0 (the floor) to 1 (the top), counting from the left: the newest at bar
        /// <see cref="Bars"/> - 1; a bar older than what was recorded is at the floor.
        /// </summary>
        public float this[int bar]
        {
            get
            {
                if (bar < 0 || bar >= Bars) throw new ArgumentOutOfRangeException(nameof(bar));
                var age = Bars - 1 - bar;
                if (age >= count) return 0f;
                return levels[(newest - age + Bars) % Bars];
            }
        }

        public void Clear()
        {
            Array.Clear(levels, 0, levels.Length);
            newest = -1;
            count = 0;
        }

        /// <summary>The samples' RMS as a level from <see cref="FloorDb"/> (0) to <see cref="TopDb"/> (1).</summary>
        public static float Level(float[] samples, int length)
        {
            if (length <= 0) return 0f;
            var sum = 0.0;
            for (var index = 0; index < length && index < samples.Length; index++) sum += (double)samples[index] * samples[index];
            var rms = Math.Sqrt(sum / Math.Min(length, samples.Length));
            if (rms <= 0.0) return 0f;
            var db = 20.0 * Math.Log10(rms);
            return (float)Math.Max(0.0, Math.Min(1.0, (db - FloorDb) / (TopDb - FloorDb)));
        }
    }
}

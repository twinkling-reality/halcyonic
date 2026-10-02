#nullable enable
using System;
using System.Globalization;

namespace Halcyonic.Client
{
    /// <summary>
    /// Counts the frames of one stretch of time, so a headset session can read the frame rate from
    /// the app's own log: how many frames, over how long, the slowest, and how many took longer than
    /// a 60th of a second, the competition's floor, and longer than one refresh of the display. Each
    /// <see cref="Add"/> allocates nothing; <see cref="Line"/> is called once a stretch ends, and says
    /// only numbers.
    /// </summary>
    /// <remarks>
    /// Time is the caller's: Unity's unscaled frame time. A frame that took longer than
    /// <see cref="LongestCounted"/>, such as the one after the headset slept, ends the stretch
    /// without being counted, since it measures the pause rather than the drawing.
    /// </remarks>
    public sealed class FrameTally
    {
        /// <summary>A 60th of a second: a frame slower than this is below 60 frames a second.</summary>
        public const double SixtiethSeconds = 1.0 / 60.0;

        /// <summary>A frame longer than this is a pause, not a frame.</summary>
        public const double LongestCounted = 1.0;

        /// <summary>How long a stretch lasts before <see cref="Add"/> says it is complete.</summary>
        public double StretchSeconds { get; }

        /// <summary>Frames counted in this stretch.</summary>
        public int Frames { get; private set; }

        /// <summary>The time those frames took, in seconds.</summary>
        public double Seconds { get; private set; }

        /// <summary>The slowest frame of the stretch, in seconds.</summary>
        public double Slowest { get; private set; }

        /// <summary>Frames slower than a 60th of a second.</summary>
        public int BelowSixty { get; private set; }

        /// <summary>Frames slower than one refresh of the display, with a tenth to spare; zero when the refresh is unknown.</summary>
        public int Missed { get; private set; }

        /// <summary>Pauses that ended the stretch early, such as a sleep.</summary>
        public bool Interrupted { get; private set; }

        private double refreshSeconds;

        public FrameTally(double stretchSeconds = 60.0)
        {
            if (!(stretchSeconds > 0)) throw new ArgumentOutOfRangeException(nameof(stretchSeconds), stretchSeconds, "A stretch must last.");
            StretchSeconds = stretchSeconds;
        }

        /// <summary>
        /// The display's refresh rate, in hertz, when known (0 when not): a frame slower than one
        /// refresh, with a tenth to spare, counts as missed.
        /// </summary>
        public double RefreshHertz
        {
            get => refreshSeconds > 0 ? 1.0 / refreshSeconds : 0;
            set => refreshSeconds = value > 0 ? 1.0 / value : 0;
        }

        /// <summary>
        /// Counts one frame that took <paramref name="seconds"/>. Returns true when the stretch is
        /// complete: it has lasted <see cref="StretchSeconds"/>, or a pause ended it. Read
        /// <see cref="Line"/>, then <see cref="Reset"/>.
        /// </summary>
        public bool Add(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) return false;
            if (seconds > LongestCounted)
            {
                if (Frames == 0) return false;
                Interrupted = true;
                return true;
            }
            Frames++;
            Seconds += seconds;
            if (seconds > Slowest) Slowest = seconds;
            if (seconds > SixtiethSeconds) BelowSixty++;
            if (refreshSeconds > 0 && seconds > refreshSeconds * 1.1) Missed++;
            return Seconds >= StretchSeconds;
        }

        /// <summary>Frames a second over the stretch, or 0 before any.</summary>
        public double PerSecond => Seconds > 0 ? Frames / Seconds : 0;

        /// <summary>
        /// The stretch in one log line of numbers, for example
        /// "frames 4320 in 60.0 s, 72.0 a second at 72 Hz, slowest 18.4 ms, 3 below 60, 5 missed".
        /// </summary>
        public string Line()
        {
            var line = "frames " + Frames.ToString(CultureInfo.InvariantCulture)
                + " in " + Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s, "
                + PerSecond.ToString("0.0", CultureInfo.InvariantCulture) + " a second";
            if (refreshSeconds > 0) line += " at " + RefreshHertz.ToString("0", CultureInfo.InvariantCulture) + " Hz";
            line += ", slowest " + (Slowest * 1000).ToString("0.0", CultureInfo.InvariantCulture) + " ms, "
                + BelowSixty.ToString(CultureInfo.InvariantCulture) + " below 60";
            if (refreshSeconds > 0) line += ", " + Missed.ToString(CultureInfo.InvariantCulture) + " missed";
            if (Interrupted) line += ", ended by a pause";
            return line;
        }

        /// <summary>Starts a new stretch.</summary>
        public void Reset()
        {
            Frames = 0;
            Seconds = 0;
            Slowest = 0;
            BelowSixty = 0;
            Missed = 0;
            Interrupted = false;
        }
    }
}

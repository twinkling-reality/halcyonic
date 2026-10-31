#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>
    /// The room's motion tokens and curves (the art direction's handoff, 2026-10-30, section 2): cubic-bezier curves as CSS
    /// gives them, and the moment card's spring (damping ratio 0.75, natural frequency 15.5 rad/s: it settles in about 420 ms
    /// with about 3 percent overshoot).
    /// </summary>
    public static class KitMotion
    {
        public const double PointMs = 120, PressMs = 80, FadeMs = 200, MoveMs = 320, OpenMs = 480, FoldMs = 250, GrowMs = 600;

        /// <summary>Under Keep Still every change that is not a point or a press is a fade this long, and nothing loops.</summary>
        public const double StillFadeMs = 120;

        public const double SpringDamping = 0.75, SpringFrequency = 15.5;

        /// <summary>cubic-bezier(0.2, 0, 0, 1).</summary>
        public static double Standard(double t) => Bezier(0.2, 0, 0, 1, t);

        /// <summary>cubic-bezier(0.05, 0.7, 0.1, 1).</summary>
        public static double Decelerate(double t) => Bezier(0.05, 0.7, 0.1, 1, t);

        /// <summary>cubic-bezier(0.3, 0, 0.8, 0.15).</summary>
        public static double Accelerate(double t) => Bezier(0.3, 0, 0.8, 0.15, t);

        /// <summary>The spring's step response at <paramref name="ms"/> after it starts: 0 to 1, overshooting a little.</summary>
        public static double Spring(double ms)
        {
            if (ms <= 0) return 0;
            var t = ms / 1000.0;
            var zeta = SpringDamping;
            var omega = SpringFrequency;
            var damped = omega * Math.Sqrt(1 - zeta * zeta);
            return 1 - Math.Exp(-zeta * omega * t) * (Math.Cos(damped * t) + zeta / Math.Sqrt(1 - zeta * zeta) * Math.Sin(damped * t));
        }

        /// <summary>How far through a span of <paramref name="duration"/> ms that began at <paramref name="start"/> the clock stands, 0 to 1.</summary>
        public static double Through(double ms, double start, double duration) => duration <= 0 ? (ms >= start ? 1 : 0) : Math.Max(0, Math.Min(1, (ms - start) / duration));

        /// <summary>A CSS cubic-bezier from (0, 0) to (1, 1) at <paramref name="x"/>: the curve's y where its x is <paramref name="x"/>.</summary>
        public static double Bezier(double x1, double y1, double x2, double y2, double x)
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            double At(double a, double b, double s) => 3 * a * s * (1 - s) * (1 - s) + 3 * b * s * s * (1 - s) + s * s * s;
            double Slope(double a, double b, double s) => 3 * a * (1 - s) * (1 - s) + 6 * (b - a) * s * (1 - s) + 3 * (1 - b) * s * s;
            var u = x;
            for (var i = 0; i < 8; i++)
            {
                var slope = Slope(x1, x2, u);
                if (Math.Abs(slope) < 1e-6) break;
                u -= (At(x1, x2, u) - x) / slope;
                u = Math.Max(0, Math.Min(1, u));
            }
            // A bisection finish where Newton's steps were too flat to settle.
            double low = 0, high = 1;
            for (var i = 0; i < 30 && Math.Abs(At(x1, x2, u) - x) > 1e-7; i++)
            {
                if (At(x1, x2, u) < x) low = u; else high = u;
                u = (low + high) / 2;
            }
            return At(y1, y2, u);
        }
    }

    /// <summary>One frame of a moment's arrival or retirement: every value the card and its light line take at that time.</summary>
    public readonly struct KitMomentFrame
    {
        public KitMomentFrame(double lineProgress, double lineOpacity, double cardScale, double cardOpacity, double bloom, Func<int, (double RiseDp, double Opacity)> part)
        {
            LineProgress = lineProgress;
            LineOpacity = lineOpacity;
            CardScale = cardScale;
            CardOpacity = cardOpacity;
            Bloom = bloom;
            this.part = part;
        }

        private readonly Func<int, (double RiseDp, double Opacity)> part;

        /// <summary>How far the light line has risen from the character's head to the card's lower corner, 0 to 1.</summary>
        public double LineProgress { get; }

        public double LineOpacity { get; }

        /// <summary>The card's scale about 14 percent across its bottom edge: 0.86 opening, 1 at rest, 0.2 retired.</summary>
        public double CardScale { get; }

        public double CardOpacity { get; }

        /// <summary>The bloom's strength, 0 to 1 of its peak: from the clock alone, never from any progress (AUDIT's A2-S2).</summary>
        public double Bloom { get; }

        /// <summary>A part's rise below its place, in dp, and its opacity, by its index in reading order.</summary>
        public (double RiseDp, double Opacity) Part(int index) => part(index);
    }

    /// <summary>One frame of an answered moment, from the answer: the answers' fade, the shrink to the receipt, the receipt's fade in.</summary>
    public readonly struct KitAnsweredFrame
    {
        public KitAnsweredFrame(double others, double kept, double body, double shrink, double receipt, bool lit, bool retiring)
        {
            OthersOpacity = others;
            KeptOpacity = kept;
            BodyOpacity = body;
            Shrink = shrink;
            ReceiptOpacity = receipt;
            Lit = lit;
            Retiring = retiring;
        }

        /// <summary>The answers not taken.</summary>
        public double OthersOpacity { get; }

        /// <summary>The answer taken: it stays where it stood until the card shrinks.</summary>
        public double KeptOpacity { get; }

        /// <summary>The card's other parts below its eyebrow: the Title, the well, the risk line.</summary>
        public double BodyOpacity { get; }

        /// <summary>How far the card has shrunk from its full height to its receipt's, 0 to 1.</summary>
        public double Shrink { get; }

        public double ReceiptOpacity { get; }

        /// <summary>The glass still lit in the moment's tone: until the card shrinks, since an answered question needs no one.</summary>
        public bool Lit { get; }

        /// <summary>From 1600 ms the card retires (<see cref="KitMomentTimeline.Retirement"/>, from that time).</summary>
        public bool Retiring { get; }
    }

    /// <summary>
    /// A moment's arrival and retirement as functions of the time since they began, and of nothing else (the art direction's
    /// choreography, 2026-10-30): the light line rises from the character at 60 ms; the card opens from it at 160 ms on the
    /// spring with its bloom; its parts rise into place in reading order from 300 ms, 40 ms apart; the character turns to the
    /// person at 700 ms. Keep Still lands each change as a 120 ms fade: no line, no scale, no rise, no bloom.
    /// </summary>
    public static class KitMomentTimeline
    {
        public const double LineAtMs = 60, LineRiseMs = 120, LineFadeAfterMs = 120, LineFadeMs = 260;
        public const double OpenAtMs = 160, OpenFromScale = 0.86, BloomMs = 1400, BloomHoldShare = 0.25;
        public const double PartsAtMs = 300, PartStepMs = 40, PartMs = 200, PartRiseDp = 6;
        public const double RetireMs = 260, LineDropMs = 160, RetireToScale = 0.2;

        /// <summary>The character's beats, for its own view to play: it dips and looks up at 0, turns to the person at 700 ms.</summary>
        public const double DipAtMs = 0, DipMs = 160, TurnAtMs = 700, TurnMs = 280;

        /// <summary>A pass's check mark draws itself 420 ms after the card opens, over 300 ms (the handoff's mark).</summary>
        public const double CheckAtMs = OpenAtMs + 420, CheckMs = 300;

        /// <summary>How much of a pass's check mark is drawn at <paramref name="ms"/>: whole at once under Keep Still.</summary>
        public static double CheckDrawn(double ms, bool still) => still ? KitMotion.Through(ms, 0, KitMotion.StillFadeMs) : KitMotion.Standard(KitMotion.Through(ms, CheckAtMs, CheckMs));

        /// <summary>When an arrival of <paramref name="parts"/> parts stands at rest, everything settled.</summary>
        public static double ArrivalMs(int parts) =>
            Math.Max(Math.Max(OpenAtMs + BloomMs, CheckAtMs + CheckMs), PartsAtMs + Math.Max(0, parts - 1) * PartStepMs + PartMs);

        /// <summary>The bloom at <paramref name="ms"/> after the arrival began: full from the open, held a quarter of its span, then out.</summary>
        public static double Bloom(double ms, bool still)
        {
            if (still || ms < OpenAtMs) return 0;
            var through = (ms - OpenAtMs) / BloomMs;
            if (through >= 1) return 0;
            if (through <= BloomHoldShare) return 1;
            return 1 - KitMotion.Standard((through - BloomHoldShare) / (1 - BloomHoldShare));
        }

        /// <summary>
        /// An answered moment (the handoff's Answered): the answers not taken fade over 120 ms while the one taken stays; at 200 ms
        /// the card shrinks to its receipt over 320 ms (standard), its parts below the eyebrow out over its first 80 ms (mine:
        /// gone before the receipt arrives), and the receipt fades in over 200 ms, 80 ms after the shrink begins, when the Sent cue
        /// sounds; at 1600 ms the card retires.
        /// </summary>
        public const double OthersFadeMs = 120, ShrinkAtMs = 200, ShrinkMs = 320, BodyOutMs = 80, ReceiptAfterMs = 80, ReceiptMs = 200;

        public const double SentAtMs = ShrinkAtMs + ReceiptAfterMs, AnsweredRetireAtMs = 1600;

        /// <summary>The answered moment at <paramref name="ms"/> after the answer. Keep Still: the parts fade out over 120 ms, the receipt's size lands, the receipt fades in over 120 ms.</summary>
        public static KitAnsweredFrame Answered(double ms, bool still)
        {
            var retiring = ms >= AnsweredRetireAtMs;
            if (still)
            {
                // The parts fade out at full height; then the receipt's size lands and the receipt fades in.
                var gone = 1 - KitMotion.Through(ms, 0, KitMotion.StillFadeMs);
                var landed = ms >= KitMotion.StillFadeMs;
                return new KitAnsweredFrame(gone, gone, gone, landed ? 1 : 0, KitMotion.Through(ms, KitMotion.StillFadeMs, KitMotion.StillFadeMs), !landed, retiring);
            }
            var others = 1 - KitMotion.Standard(KitMotion.Through(ms, 0, OthersFadeMs));
            var body = 1 - KitMotion.Standard(KitMotion.Through(ms, ShrinkAtMs, BodyOutMs));
            var shrink = KitMotion.Standard(KitMotion.Through(ms, ShrinkAtMs, ShrinkMs));
            var receipt = KitMotion.Standard(KitMotion.Through(ms, SentAtMs, ReceiptMs));
            return new KitAnsweredFrame(others, body, body, shrink, receipt, ms < ShrinkAtMs, retiring);
        }

        /// <summary>The arrival at <paramref name="ms"/> after it began.</summary>
        public static KitMomentFrame Arrival(double ms, bool still)
        {
            if (still)
            {
                var fade = KitMotion.Through(ms, 0, KitMotion.StillFadeMs);
                return new KitMomentFrame(0, 0, 1, fade, 0, _ => (0, fade));
            }
            var rise = KitMotion.Decelerate(KitMotion.Through(ms, LineAtMs, LineRiseMs));
            var lineFade = 1 - KitMotion.Through(ms, LineAtMs + LineFadeAfterMs, LineFadeMs);
            var lineOpacity = ms < LineAtMs ? 0 : (0.15 + 0.85 * rise) * lineFade;
            var opened = ms < OpenAtMs ? 0 : KitMotion.Spring(ms - OpenAtMs);
            var scale = OpenFromScale + (1 - OpenFromScale) * opened;
            var cardOpacity = KitMotion.Standard(KitMotion.Through(ms, OpenAtMs, KitMotion.FadeMs));
            return new KitMomentFrame(rise, lineOpacity, ms < OpenAtMs ? OpenFromScale : scale, cardOpacity, Bloom(ms, false), index =>
            {
                var through = KitMotion.Decelerate(KitMotion.Through(ms, PartsAtMs + index * PartStepMs, PartMs));
                return (PartRiseDp * (1 - through), through);
            });
        }

        /// <summary>The retirement at <paramref name="ms"/> after it began: the card scales down and fades as the line drops back.</summary>
        public static KitMomentFrame Retirement(double ms, bool still)
        {
            if (still)
            {
                var gone = 1 - KitMotion.Through(ms, 0, KitMotion.StillFadeMs);
                return new KitMomentFrame(0, 0, 1, gone, 0, _ => (0, gone));
            }
            var through = KitMotion.Accelerate(KitMotion.Through(ms, 0, RetireMs));
            var drop = 1 - KitMotion.Accelerate(KitMotion.Through(ms, 0, LineDropMs));
            return new KitMomentFrame(drop, drop, 1 - (1 - RetireToScale) * through, 1 - through, 0, _ => (0, 1 - through));
        }
    }
}

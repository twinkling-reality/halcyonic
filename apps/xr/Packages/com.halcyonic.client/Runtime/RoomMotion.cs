#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>How a moving part stands at one moment: how far into view, and how far below its place.</summary>
    public readonly struct RoomMove
    {
        public RoomMove(float shown, float lowerDp)
        {
            Shown = shown;
            LowerDp = lowerDp;
        }

        /// <summary>How far into view, 0 to 1.</summary>
        public float Shown { get; }

        /// <summary>How far below its own place it stands, in dp; above it where negative.</summary>
        public float LowerDp { get; }

        /// <summary>At rest in its place, whole.</summary>
        public static RoomMove Settled => new RoomMove(1f, 0f);

        /// <summary>Not in view.</summary>
        public static RoomMove Gone => new RoomMove(0f, 0f);
    }

    /// <summary>
    /// The redesign's motion (DESIGN's BUILD-SPEC-v4 1.5): every move a formula of the seconds since its event, so a view asks
    /// where a part stands now and a render fixes the clock. Two curves, no springs: ease out (cubic), and the standard curve
    /// (0.2, 0, 0, 1). A time before an event's start is its start, and after its end its end.
    /// </summary>
    public static class RoomMotion
    {
        /// <summary>The agent's words band fades in, and fades out when it leaves.</summary>
        public const float BandSeconds = 0.36f;

        /// <summary>A band's first line starts this long after the band, each next one <see cref="LineApartSeconds"/> later, and takes <see cref="LineSeconds"/>, rising <see cref="RiseDp"/>.</summary>
        public const float BandLinesAfterSeconds = 0.12f;

        public const float LineApartSeconds = 0.06f;

        public const float LineSeconds = 0.24f;

        public const float RiseDp = 8f;

        /// <summary>A line leaving for a next part or newer words rises <see cref="RiseDp"/> and fades over this long.</summary>
        public const float LineLeaveSeconds = 0.12f;

        /// <summary>The band's height eases to its newer words' over this long, once the old lines have left.</summary>
        public const float BandHeightSeconds = 0.24f;

        /// <summary>Where newer words replace older ones, their first line starts once the old have left and the height has eased.</summary>
        public const float ReplaceLinesAfterSeconds = LineLeaveSeconds + BandHeightSeconds;

        /// <summary>The working field fades in, and out, over this long, on the standard curve.</summary>
        public const float FieldSeconds = 0.6f;

        /// <summary>The information panel arrives over this long, rising about a degree, ease out; it leaves over the same, on the standard curve.</summary>
        public const float PanelSeconds = 0.36f;

        public const float PanelRiseDp = 16f;

        /// <summary>The column's first action starts this long after its moment, each next one <see cref="LineApartSeconds"/> later, and takes <see cref="LineSeconds"/>, rising <see cref="RiseDp"/>.</summary>
        public const float ActionsAfterSeconds = 0.22f;

        /// <summary>An action answers pointing over this long, ease out: its halo, its chip and its scale, to 1.03. Held, it is 0.96 at once.</summary>
        public const float PointedSeconds = 0.15f;

        public const float PointedScale = 1.03f;

        public const float PressedScale = 0.96f;

        /// <summary>A chosen answer's mark fills, and the one before it empties, over this long, ease out.</summary>
        public const float MarkSeconds = 0.25f;

        /// <summary>Confirm's ring fills over this long once the request is read, in a straight line; then it fades and the chip lights over <see cref="ConfirmLightsSeconds"/>.</summary>
        public const float ConfirmRingSeconds = 0.7f;

        public const float ConfirmLightsSeconds = 0.12f;

        /// <summary>The agent's cursor glides to its next stop over this long, ease out.</summary>
        public const float CursorSeconds = 0.6f;

        /// <summary>Between steps the cursor drifts at most this far from its stop, in degrees at the eye.</summary>
        public const float CursorDriftDegrees = 0.35f;

        /// <summary>Ease out, cubic: fast first, settling; 0 at 0 and 1 from 1 on.</summary>
        public static float EaseOut(float progress)
        {
            var left = 1f - Clamp01(progress);
            return 1f - left * left * left;
        }

        /// <summary>The standard curve, cubic-bezier(0.2, 0, 0, 1).</summary>
        public static float Standard(float progress) => Bezier(0.2f, 0f, 0f, 1f, progress);

        /// <summary>How far the band's ground is in view <paramref name="seconds"/> after it starts to arrive.</summary>
        public static float BandArrives(float seconds) => EaseOut(seconds / BandSeconds);

        /// <summary>How far the band, its words with it, is still in view <paramref name="seconds"/> after it starts to leave.</summary>
        public static float BandLeaves(float seconds) => 1f - Standard(seconds / BandSeconds);

        /// <summary>
        /// A line of the band <paramref name="seconds"/> after its lines' turn starts: line <paramref name="line"/> (from 0)
        /// starts 60 ms after the one above it, rises 8 dp to its place and fades in over 240 ms, ease out.
        /// </summary>
        public static RoomMove LineArrives(float seconds, int line)
        {
            var progress = EaseOut((seconds - line * LineApartSeconds) / LineSeconds);
            return new RoomMove(progress, (1f - progress) * RiseDp);
        }

        /// <summary>A line <paramref name="seconds"/> after it starts to leave: it rises 8 dp above its place and fades out over 120 ms, the standard curve.</summary>
        public static RoomMove LineLeaves(float seconds)
        {
            var progress = Standard(seconds / LineLeaveSeconds);
            return new RoomMove(1f - progress, -progress * RiseDp);
        }

        /// <summary>
        /// The band's height <paramref name="seconds"/> after newer words start to replace older ones: the old height while
        /// the old lines leave, then easing to the new one over 240 ms, ease out.
        /// </summary>
        public static float BandHeight(float seconds, float from, float to) =>
            from + (to - from) * EaseOut((seconds - LineLeaveSeconds) / BandHeightSeconds);

        /// <summary>How far the working field is in view <paramref name="seconds"/> after it starts to fade in: 600 ms, the standard curve.</summary>
        public static float FieldArrives(float seconds) => Standard(seconds / FieldSeconds);

        /// <summary>How far the working field is still in view <paramref name="seconds"/> after it starts to fade out.</summary>
        public static float FieldLeaves(float seconds) => 1f - Standard(seconds / FieldSeconds);

        /// <summary>The information panel <paramref name="seconds"/> after it starts to arrive: it rises about a degree to its place and fades in over 360 ms, ease out.</summary>
        public static RoomMove PanelArrives(float seconds)
        {
            var progress = EaseOut(seconds / PanelSeconds);
            return new RoomMove(progress, (1f - progress) * PanelRiseDp);
        }

        /// <summary>How far a panel is still in view <paramref name="seconds"/> after it starts to leave: 360 ms, the standard curve.</summary>
        public static float PanelLeaves(float seconds) => 1f - Standard(seconds / PanelSeconds);

        /// <summary>
        /// An action of the choices column <paramref name="seconds"/> after its moment starts: action <paramref name="index"/>
        /// (from 0, the main one first) starts after 220 ms and 60 ms after the one above it, rises 8 dp and fades in over
        /// 240 ms, ease out. An action that arrives later by itself (Approve, once the request is read) is index 0 of its own moment
        /// with no wait: <see cref="LineArrives"/> at line 0. It leaves as a line leaves (<see cref="LineLeaves"/>).
        /// </summary>
        public static RoomMove ActionArrives(float seconds, int index) => LineArrives(seconds - ActionsAfterSeconds, index);

        /// <summary>How far an action has answered pointing <paramref name="seconds"/> after the pointer came, 0 to 1: 150 ms, ease out. Leaving, it runs back the same way.</summary>
        public static float Pointed(float seconds) => EaseOut(seconds / PointedSeconds);

        /// <summary>An action's scale at <paramref name="pointed"/> (0 to 1) of its answer to pointing, or held.</summary>
        public static float ActionScale(float pointed, bool held) => held ? PressedScale : 1f + (PointedScale - 1f) * Clamp01(pointed);

        /// <summary>How full a newly chosen answer's mark is <paramref name="seconds"/> after it is chosen; the one chosen before is as much emptier.</summary>
        public static float MarkFills(float seconds) => EaseOut(seconds / MarkSeconds);

        /// <summary>How full Confirm's ring is <paramref name="seconds"/> after the request is read: a straight line over 700 ms.</summary>
        public static float ConfirmRing(float seconds) => Clamp01(seconds / ConfirmRingSeconds);

        /// <summary>How far Confirm's chip has lit, and its ring faded, <paramref name="seconds"/> after the request is read: over the 120 ms after the ring is full.</summary>
        public static float ConfirmLights(float seconds) => Clamp01((seconds - ConfirmRingSeconds) / ConfirmLightsSeconds);

        /// <summary>How far along the way to its next stop the cursor is <paramref name="seconds"/> after a step starts: 600 ms, ease out.</summary>
        public static float CursorGlides(float seconds) => EaseOut(seconds / CursorSeconds);

        /// <summary>
        /// The cursor's drift round its stop at <paramref name="seconds"/> on its clock, in degrees at the eye: right, and
        /// up. A slow wander, never more than 0.35 degrees across and 0.21 up or down.
        /// </summary>
        public static (float Right, float Up) CursorDrift(float seconds) =>
            ((float)Math.Sin(seconds * 0.9) * CursorDriftDegrees, -(float)Math.Cos(seconds * 0.7) * CursorDriftDegrees * 0.6f);

        /// <summary>A cubic bezier easing through (x1, y1) and (x2, y2): the value where the curve's time is <paramref name="progress"/>.</summary>
        private static float Bezier(float x1, float y1, float x2, float y2, float progress)
        {
            if (!(progress > 0f)) return 0f;
            if (progress >= 1f) return 1f;
            float low = 0f, high = 1f;
            for (var step = 0; step < 24; step++)
            {
                var middle = (low + high) / 2f;
                if (Cubic(x1, x2, middle) < progress) low = middle;
                else high = middle;
            }
            return Cubic(y1, y2, (low + high) / 2f);
        }

        private static float Cubic(float first, float second, float at)
        {
            var left = 1f - at;
            return 3f * left * left * at * first + 3f * left * at * at * second + at * at * at;
        }

        private static float Clamp01(float value) => !(value > 0f) ? 0f : value > 1f ? 1f : value;
    }
}

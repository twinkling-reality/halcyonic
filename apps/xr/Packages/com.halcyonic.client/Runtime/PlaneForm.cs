#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>
    /// The plane's widths under the redesign (the lead's 18:07Z: a width, not two forms): the panel laid at one of FIELD's measured
    /// steps, the column beside it, never under the floor of the text size, where the recap card stops laying whole (FIELD's
    /// 59-width-table, F106, 2026-11-13T20:53Z: Standard 360 dp, Larger 400 dp).
    /// </summary>
    public static class PlaneWidth
    {
        /// <summary>The panel widths the kit's envelope is measured at (FIELD's record), widest first, in dp.</summary>
        public static readonly float[] Steps = { RoomGeometry.PanelDp, 400f, 360f, 320f, 280f, 240f };

        /// <summary>The narrowest step measured.</summary>
        public const float NarrowestDp = 240f;

        /// <summary>The floor at the standard text size: every main screen whole at and above it (F106).</summary>
        public const float StandardFloorDp = 360f;

        /// <summary>The floor at the larger text size (F106).</summary>
        public const float LargerFloorDp = 400f;

        /// <summary>The floor at a text size of <paramref name="textScale"/> (1 standard, <see cref="Comfort.LargerTextScale"/> larger).</summary>
        public static float FloorDp(float textScale) => textScale > 1.0001f ? LargerFloorDp : StandardFloorDp;
    }

    /// <summary>What made the plane decide its form again: only what a person does or what the room changes, never a frame.</summary>
    public enum PlaneFormEvent
    {
        /// <summary>The plane opened, or the person placed or moved it.</summary>
        PlaneMoved,

        /// <summary>A shelf placed, moved or removed.</summary>
        ShelfChanged,

        /// <summary>A task started or ended, so a character came or went.</summary>
        CharactersChanged,

        /// <summary>The person's text size changed.</summary>
        TextSizeChanged,
    }

    /// <summary>An extent in degrees from the eyes, about the plane's heading: left and right of it, bottom and top.</summary>
    public readonly struct AngularExtent
    {
        public AngularExtent(float left, float right, float bottom, float top)
        {
            Left = left; Right = right; Bottom = bottom; Top = top;
        }

        public float Left { get; }

        public float Right { get; }

        public float Bottom { get; }

        public float Top { get; }

        /// <summary>
        /// The extent in degrees, about the plane's heading, of a rectangle on a plane centred at <paramref name="direction"/>, given in the
        /// plane's units (<see cref="PlaneComposition.PointOf"/>) from (<paramref name="originRight"/>, <paramref name="originUp"/>): each
        /// point through the geometry the plane stands its parts with, read from the eyes. Corners, edge middles and centre: a flat plane's
        /// highest and lowest points as the eyes see them lie at a corner or at an edge's middle, by which side of the eyes it stands.
        /// </summary>
        public static AngularExtent OnPlane(PanelDirection direction, float originRight, float originUp, float left, float right, float bottom, float top)
        {
            float leftmost = float.PositiveInfinity, rightmost = float.NegativeInfinity, lowest = float.PositiveInfinity, highest = float.NegativeInfinity;
            // Allocating nothing, since a drag asks every frame it moves.
            for (var point = 0; point < 9; point++)
            {
                var x = point % 3 == 0 ? left : point % 3 == 1 ? (left + right) / 2f : right;
                var y = point / 3 == 0 ? bottom : point / 3 == 1 ? (bottom + top) / 2f : top;
                var (px, py, pz) = PlaneComposition.PointOf(direction, originRight + x, originUp + y);
                var yaw = Wrap(MathF.Atan2(px, pz) * 180f / MathF.PI - direction.Yaw);
                var elevation = MathF.Atan2(py, MathF.Sqrt(px * px + pz * pz)) * 180f / MathF.PI;
                leftmost = MathF.Min(leftmost, yaw); rightmost = MathF.Max(rightmost, yaw); lowest = MathF.Min(lowest, elevation); highest = MathF.Max(highest, elevation);
            }
            return new AngularExtent(leftmost, rightmost, lowest, highest);
        }

        private static float Wrap(float degrees)
        {
            var wrapped = degrees % 360f;
            if (wrapped > 180f) wrapped -= 360f;
            if (wrapped <= -180f) wrapped += 360f;
            return wrapped;
        }

        /// <summary>How far apart two extents stand, in degrees: across, down, or diagonally past a corner; at most 0 where they overlap.</summary>
        public float Apart(AngularExtent other)
        {
            var across = Math.Max(Left - other.Right, other.Left - Right);
            var down = Math.Max(Bottom - other.Top, other.Bottom - Top);
            return across > 0f && down > 0f ? MathF.Sqrt(across * across + down * down) : Math.Max(across, down);
        }
    }

    /// <summary>
    /// The plane's width, held (the lead's rulings of 2026-11-13T17:16Z and 19:34Z): the widest of <see cref="PlaneWidth.Steps"/> at
    /// or above the floor whose largest footprint clears every shelf inside the field, decided again only on a
    /// <see cref="PlaneFormEvent"/>, never because a character moved, so the plane never changes width while a person reads. Where
    /// even the floor does not clear, the floor all the same: the room yields (a shelf stands aside, characters keep out), never the
    /// plane, and under the floor nothing is laid.
    /// </summary>
    public sealed class PlaneWidthRule
    {
        /// <summary>The frames a placing that just ended waits for the shelf it placed to stand (it stands a frame later).</summary>
        public const int PlacedShelfFrames = 2;

        /// <summary>
        /// Whether the plane waits before deciding again (JUDGE's charR; AUDIT's 22:30Z): only where placing just ended, no shelf
        /// stands yet, the text size did not change (the plane returning and the shelf's characters arriving come with the placing's
        /// end, so the host never counts them), and no more than <see cref="PlacedShelfFrames"/>
        /// frames have passed (<see cref="ComingShelfFrames"/> where <paramref name="shelfComing"/>: the room applied a shelf the stage has
        /// not stood yet); so a placed shelf changes the width once, and a cancelled placing, or any real change, is decided at once.
        /// </summary>
        public static bool AwaitsPlacedShelf(bool wasPlacing, bool placing, bool shelfStands, bool otherChange, int framesSincePlacingEnded,
            bool shelfComing = false, bool cancelled = false) =>
            // A cancelled placing applied nothing, so no shelf is coming and the plane leaves its placing form at once (XR's 21:34Z: a
            // walk's cancel waited for a shelf it never brings, and the next Place Agents searched a plane still placing).
            !cancelled && wasPlacing && !placing && !shelfStands && !otherChange && framesSincePlacingEnded < (shelfComing ? ComingShelfFrames : PlacedShelfFrames);

        /// <summary>
        /// The draws a placing that just ended waits where the room says a shelf was applied that the stage has not stood yet: the stage
        /// stands it on its own next update, which may come after several of the plane's draws (JUDGE's charV: Apply laid 440 with no shelf,
        /// then 360). Bounded all the same, so a shelf the stage never stands cannot hold the plane.
        /// </summary>
        public const int ComingShelfFrames = 30;

        /// <summary>The panel width held now, in dp.</summary>
        public float WidthDp { get; private set; } = RoomGeometry.PanelDp;

        /// <summary>The event that last changed the width, for the log line that says each change and its cause.</summary>
        public PlaneFormEvent? Cause { get; private set; }

        /// <summary>
        /// Decides the width again for <paramref name="happened"/>: <paramref name="clears"/> says whether the plane's largest footprint
        /// at a width, laid where the plane stands, clears every shelf inside the field; <paramref name="floorDp"/> is the text size's
        /// floor. True where the width changed.
        /// </summary>
        public bool Reconsider(PlaneFormEvent happened, Func<float, bool> clears, float floorDp)
        {
            if (clears == null) throw new ArgumentNullException(nameof(clears));
            var next = floorDp;
            foreach (var step in PlaneWidth.Steps)
            {
                if (step < floorDp - 0.5f) break;
                if (clears(step)) { next = step; break; }
            }
            if (Math.Abs(next - WidthDp) < 0.5f) return false;
            WidthDp = next;
            Cause = happened;
            return true;
        }
    }
}

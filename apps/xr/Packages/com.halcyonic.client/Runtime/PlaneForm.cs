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
        /// Where a bar of <paramref name="widthUnits"/> by <paramref name="heightUnits"/> (metres at 1 m, DESIGN units) stands: centred on
        /// the heading, from its foot line up; the foot at <paramref name="footPitch"/>, moved by as much as the person raised or lowered
        /// the plane (<paramref name="standingPitch"/> against <paramref name="placedPitch"/>).
        /// </summary>
        public static AngularExtent OfBar(float widthUnits, float heightUnits, float footPitch, float placedPitch, float standingPitch)
        {
            var half = MathF.Atan(widthUnits / 2f) * 180f / MathF.PI;
            var foot = footPitch + (standingPitch - placedPitch);
            return new AngularExtent(-half, half, foot, foot + MathF.Atan(heightUnits) * 180f / MathF.PI);
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
        /// frames have passed; so a placed shelf changes the width once, and a cancelled placing, or any real change, is decided at once.
        /// </summary>
        public static bool AwaitsPlacedShelf(bool wasPlacing, bool placing, bool shelfStands, bool otherChange, int framesSincePlacingEnded) =>
            wasPlacing && !placing && !shelfStands && !otherChange && framesSincePlacingEnded < PlacedShelfFrames;

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

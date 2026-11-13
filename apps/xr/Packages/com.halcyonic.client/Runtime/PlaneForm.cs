#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>The plane's two forms under the redesign: the pair side by side, or the narrow bar, its panel above its column.</summary>
    public enum PlaneForm
    {
        Full,
        Narrow,
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
    /// The plane's form, held (the lead's ruling of 2026-11-13T17:16Z): the widest form that clears every shelf's envelope inside the
    /// field, decided again only on a <see cref="PlaneFormEvent"/>, never because a character moved, so the plane never changes form
    /// while a person reads. The full form wherever it clears; the narrow form where only it clears; and where neither clears, the
    /// narrow one all the same, the shelf search having refused to place anything under it.
    /// </summary>
    public sealed class PlaneFormRule
    {
        /// <summary>The form held now.</summary>
        public PlaneForm Form { get; private set; } = PlaneForm.Full;

        /// <summary>The event that last changed the form, for the log line that says each change and its cause.</summary>
        public PlaneFormEvent? Cause { get; private set; }

        /// <summary>
        /// Decides the form again for <paramref name="happened"/>: <paramref name="clears"/> says whether a form, laid where the plane
        /// stands, clears every shelf's envelope inside the field. True where the form changed.
        /// </summary>
        public bool Reconsider(PlaneFormEvent happened, Func<PlaneForm, bool> clears)
        {
            if (clears == null) throw new ArgumentNullException(nameof(clears));
            var next = clears(PlaneForm.Full) ? PlaneForm.Full : PlaneForm.Narrow;
            if (next == Form) return false;
            Form = next;
            Cause = happened;
            return true;
        }
    }
}

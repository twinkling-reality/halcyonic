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

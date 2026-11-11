#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>What one row of a file's page is: a line the change left, one it added or removed, or a gap where it skips lines that did not change.</summary>
    public enum RoomLineKind
    {
        Same,
        Added,
        Removed,
        Gap,
    }

    /// <summary>
    /// One row of a card's page of reading (<see cref="RoomCard.Reading"/>): a file's line, or the part of a long line that
    /// wraps onto the next row (<see cref="Continued"/>), or a gap. Its text is always the file's own words (theirs, mono).
    /// </summary>
    public readonly struct RoomReadingRow
    {
        /// <param name="number">The file's line number; on a gap, how many lines it skips (<see cref="Gap"/>); null on a continued row.</param>
        /// <param name="indent">
        /// The characters of space the line starts with (tabs as the screen expands them): outside words lose their leading
        /// spaces by <see cref="LabelText.Plain"/>'s rule, so the card stands the words that many characters in instead.
        /// </param>
        public RoomReadingRow(RoomLineKind kind, int? number, LineWords? text, bool continued = false, int indent = 0)
        {
            Indent = kind == RoomLineKind.Gap || indent < 0 ? 0 : indent;
            Kind = kind;
            Number = continued ? null : kind == RoomLineKind.Gap ? number is int skipped && skipped > 0 ? skipped : (int?)null : number;
            Text = kind == RoomLineKind.Gap ? null : text ?? LineWords.Ours("");
            Continued = kind != RoomLineKind.Gap && continued;
        }

        /// <summary>A gap, where the change skips lines it did not touch: drawn as a hairline, nothing written.</summary>
        public static RoomReadingRow GapRow => new RoomReadingRow(RoomLineKind.Gap, null, null);

        /// <summary>A gap that says how many lines it skips: "23 Lines" quiet on its hairline, as a diff view says it.</summary>
        public static RoomReadingRow Gap(int skipped) => new RoomReadingRow(RoomLineKind.Gap, skipped > 0 ? skipped : (int?)null, null);

        public RoomLineKind Kind { get; }

        public int? Number { get; }

        /// <summary>The row's words; null on a gap.</summary>
        public LineWords? Text { get; }

        /// <summary>The characters of space before the row's words.</summary>
        public int Indent { get; }

        /// <summary>The rest of a line too long for one row, hung in a little under its start.</summary>
        public bool Continued { get; }
    }

    /// <summary>
    /// The measures of a card's page of reading, so a screen wraps and pages a file exactly as the card draws it: the
    /// mono face at 15 dp on a 22 dp pitch, the line number in a 34 dp gutter, the change's mark, the words, a continued
    /// row hung 16 dp in, 12 dp kept clear at the right. The view checks every one of these (KitRender's 47-card-reading).
    /// A screen wraps a line at a space, or after / ? &amp; , ; : but never inside an operator (=&gt;, ==, !=, &lt;=, -&gt;, ::,
    /// &amp;&amp;, ||) or a name, and gives a long line as many continued rows as it needs.
    /// </summary>
    public static class RoomReading
    {
        /// <summary>The words on a gap that says how many lines it skips.</summary>
        public static string Skipped(int lines) => lines == 1 ? "1 Line" : lines + " Lines";

        public const float TypeDp = 15f;
        public const float LineDp = 22f;
        /// <summary>The line number's size: the words' own, set quieter, since nothing under 14 dp reads (the kit's floor).</summary>
        public const float NumberDp = 15f;
        public const float GutterDp = 34f;
        public const float MarkGapDp = 4f;
        public const float MarkDp = 14f;
        public const float TextGapDp = 8f;
        public const float HangDp = 16f;
        public const float GapRowDp = 14f;

        /// <summary>The room kept clear at a row's right, inside its band, so no line touches the band's edge.</summary>
        public const float RightPadDp = 12f;

        /// <summary>The mono face's advance, a share of its size: every character the same width.</summary>
        public const float AdvanceEm = 0.6f;

        /// <summary>The rows a page holds at either text size: a continued row counts as a row, a gap as one.</summary>
        public const int RowsAPage = 12;

        /// <summary>The dp from the card's left to where a row's words start, at a text size of <paramref name="textScale"/>.</summary>
        public static float TextLeftDp(float textScale, bool continued = false) =>
            (GutterDp + MarkGapDp + MarkDp + TextGapDp + (continued ? HangDp : 0f)) * textScale;

        /// <summary>
        /// The characters a row holds at a text size of <paramref name="textScale"/> (1 standard, <see cref="Comfort.LargerTextScale"/>
        /// larger), its indent counted among them: wrap a file's lines to this, a continued row to its own.
        /// </summary>
        public static int Columns(float textScale, bool continued = false)
        {
            if (textScale <= 0f) throw new ArgumentOutOfRangeException(nameof(textScale), textScale, "A text size is more than nothing.");
            var inner = RoomGeometry.PanelDp - 2f * RoomGeometry.PanelPadDp * textScale;
            var words = inner - TextLeftDp(textScale, continued) - RightPadDp * textScale;
            return (int)Math.Floor(words / (TypeDp * textScale * AdvanceEm) + 1e-4);
        }
    }
}

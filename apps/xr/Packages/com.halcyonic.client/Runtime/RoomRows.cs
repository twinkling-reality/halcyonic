#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>The mark a row of the information panel carries before its words: none, the check of what held, or the cross of what did not.</summary>
    public enum RoomRowMark
    {
        None,
        Check,
        Cross,
    }

    /// <summary>One row of the information panel: its words, and its mark.</summary>
    public readonly struct RoomRow
    {
        /// <param name="label">What the row's words are, ours, a quiet line of its own above them ("Project" over a person's idea); never joined to them on a dot.</param>
        /// <param name="note">The row's state, ours, a quiet note at its right ("Confirmed" beside "Create Project"); never joined on a dot.</param>
        public RoomRow(LineWords words, RoomRowMark mark = RoomRowMark.None, int fromLine = 0, int? lines = null, string? label = null, string? note = null)
        {
            Label = string.IsNullOrWhiteSpace(label) ? null : label!.Trim();
            Note = string.IsNullOrWhiteSpace(note) ? null : note!.Trim();
            Words = words;
            Mark = mark;
            FromLine = fromLine < 0 ? 0 : fromLine;
            Lines = lines;
        }

        public LineWords Words { get; }

        /// <summary>What the words are, ours, drawn quiet above them; null for none.</summary>
        public string? Label { get; }

        /// <summary>The row's state, ours, drawn quiet at its right; null for none.</summary>
        public string? Note { get; }

        public RoomRowMark Mark { get; }

        /// <summary>
        /// Where the row shows a part of its words, as a review read part by part does: the first of its lines to show
        /// (from 0) and how many, counted in the lines the rows themselves wrap the words to. Null lines is all of them.
        /// </summary>
        public int FromLine { get; }

        public int? Lines { get; }
    }

    /// <summary>
    /// The information panel's rows (the redesign's 1.2, 2.6 and 2.8, and the addendum's recap, steps and limits): each
    /// fact a row of its own, parted from the next by the 1 dp hairline, with a check or a cross before its words where
    /// the row is a step that held or did not. Where any row of a page carries a mark, every row's words start after the
    /// mark's place, so they stand on one line down the panel. A page holds eight rows at most; more turn by the
    /// screen's own Next Page.
    /// </summary>
    public static class RoomRows
    {
        /// <summary>The rows a page holds (2.6: eight lines a page).</summary>
        public const int APage = 8;

        /// <summary>The space above a hairline and below it, the hairline's own width, the mark's size and the gap after it, in dp (the page's rows).</summary>
        public const float GapDp = 9f;
        public const float HairlineDp = 1f;
        public const float MarkDp = 20f;
        public const float MarkGapDp = 10f;

        /// <summary>The hairline: white at 12 percent as the page draws it, over the panel.</summary>
        public const float HairlineAlpha = 0.12f;

        /// <summary>The panel's own tint the hairline stands on, in sRGB (the middle of the page's two, #383E43, by its green).</summary>
        public const float PanelGround = 0x3E / 255f;

        /// <summary>
        /// The alpha that shows, blended in linear light as the headset blends, what the page shows with white at
        /// <paramref name="pageAlpha"/> mixed in sRGB over a ground of <paramref name="ground"/> (sRGB, 0 to 1): the
        /// page's 0.12 over the panel comes to about 0.05.
        /// </summary>
        public static float LightAlpha(float pageAlpha, float ground)
        {
            var mixed = ground + (1f - ground) * pageAlpha;
            return (Decode(mixed) - Decode(ground)) / System.Math.Max(1f - Decode(ground), 1e-4f);
        }

        private static float Decode(float value) => value <= 0.04045f ? value / 12.92f : (float)System.Math.Pow((value + 0.055f) / 1.055f, 2.4);

        /// <summary>A check's colour and a cross's (the result's own: #69D98D, #F7857D).</summary>
        public const int PassedColour = 0x69D98D;
        public const int FailedColour = 0xF7857D;

        /// <summary>How many pages <paramref name="rows"/> rows take.</summary>
        public static int Pages(int rows) => rows <= 0 ? 1 : (rows + APage - 1) / APage;

        /// <summary>The rows of page <paramref name="page"/> (from 0); a page past the last is the last.</summary>
        public static IReadOnlyList<RoomRow> Page(IReadOnlyList<RoomRow> rows, int page)
        {
            var at = page < 0 ? 0 : page >= Pages(rows.Count) ? Pages(rows.Count) - 1 : page;
            return rows.Skip(at * APage).Take(APage).ToList();
        }

        /// <summary>Whether the page's words start after a mark's place: any of its rows carries one.</summary>
        public static bool Marked(IReadOnlyList<RoomRow> rows) => rows.Any(row => row.Mark != RoomRowMark.None);

        /// <summary>How far in a row's words start, in dp at the standard text size: after the mark and its gap where the page is marked.</summary>
        public static float IndentDp(IReadOnlyList<RoomRow> rows) => Marked(rows) ? MarkDp + MarkGapDp : 0f;
    }
}

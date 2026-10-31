#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>The reading surface's own words (the art direction's handoff; the owner's voice standard), for XR to reword with the rest.</summary>
    public static class KitReaderWords
    {
        /// <summary>The locked primary while the command is not yet read to its end.</summary>
        public const string ReadToEnd = "Read to End";

        /// <summary>Under the command's last line, before what answering may do.</summary>
        public const string EndOfCommand = "End of Command";

        /// <summary>Closes the reader, back to the card.</summary>
        public const string Fold = "Fold";
    }

    /// <summary>One row of the reading surface: a part of one of the command's lines, as wide as the well holds.</summary>
    public sealed class KitReadingRow
    {
        public KitReadingRow(int line, string text, bool continued)
        {
            Line = line;
            Text = text;
            Continued = continued;
        }

        /// <summary>A row after its line's first: it hangs indented by two characters, so a wrap never reads as a new command.</summary>
        public bool Continued { get; }

        /// <summary>The command's line this row is part of.</summary>
        public int Line { get; }

        /// <summary>The row's part of that line, as shown.</summary>
        public string Text { get; }
    }

    /// <summary>
    /// A command read on the reading surface (the art direction's handoff; SECURITY.md: a command is read whole before it can
    /// run): its lines, which of them are comments, the rows they wrap to in the well, where the reading stands, and how far it
    /// has ever reached. The reading goes by rows, so a line longer than the well holds is read to its last row before it
    /// counts as read: Run unlocks only once the last row of the last line has been shown, and scrolling back never locks it
    /// again. The lines are theirs.
    /// </summary>
    public sealed class KitReading
    {
        /// <summary>
        /// Reads <paramref name="command"/>, its lines wrapped into rows by <paramref name="fits"/>: whether words, as shown, fit
        /// one row of the well (the second argument true for a row after a line's first, which hangs indented and so holds less).
        /// Without it, each line is one row. A line wraps only at spaces; a token alone longer than a row breaks inside, after a
        /// "/", "?", "&amp;", "=", ",", ";" or ":" where one fits and otherwise where the row ends, never after a "-" (so never between
        /// "--" and its name) where any other break fits, and never inside a surrogate pair. Every character is kept, in order.
        /// </summary>
        public KitReading(string command, Func<LineWords, bool, bool>? fits = null)
        {
            var lines = (command ?? "").Replace("\r\n", "\n").Split('\n').ToList();
            while (lines.Count > 1 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            Lines = lines;
            var rows = new List<KitReadingRow>();
            for (var line = 0; line < lines.Count; line++)
                foreach (var text in Wrap(ShownLine(line), fits))
                    rows.Add(new KitReadingRow(line, text, rows.Count > 0 && rows[rows.Count - 1].Line == line));
            Rows = rows;
            Furthest = -1;
        }

        private const string TokenBreaks = "/?&=,;:";

        private static IEnumerable<string> Wrap(string shown, Func<LineWords, bool, bool>? fits)
        {
            var from = 0;
            var continued = false;
            // Trailing spaces hang past the row's end, as a terminal's do: they are measured out.
            bool Fits(int end) => fits!(LineWords.Shown(shown.Substring(from, end - from).TrimEnd(' ')), continued);
            while (fits != null && from < shown.Length && !Fits(shown.Length))
            {
                // The longest part from here that fits (widths only grow as characters are added), at least one character.
                int low = from + 1, high = shown.Length - 1;
                while (low < high)
                {
                    var middle = (low + high + 1) / 2;
                    if (Fits(middle)) low = middle;
                    else high = middle - 1;
                }
                var most = Math.Max(from + 1, low);
                var at = -1;
                // After the last run of spaces whose start fits, with words before it on the row.
                var scan = most;
                while (scan < shown.Length && shown[scan] == ' ') scan++;
                for (var i = scan; i > from; i--)
                {
                    if (shown[i - 1] != ' ' || (i < shown.Length && shown[i] == ' ')) continue;
                    var run = i;
                    while (run > from && shown[run - 1] == ' ') run--;
                    if (run > from && run <= most) { at = i; break; }
                }
                // A token alone longer than a row: after its last break mark that fits, then where the row ends.
                if (at < 0)
                    for (var i = most; i > from + 1 && at < 0; i--)
                        if (TokenBreaks.IndexOf(shown[i - 1]) >= 0) at = i;
                if (at < 0)
                {
                    at = most;
                    for (var i = most; i > from + 1; i--)
                        if (shown[i - 1] != '-') { at = i; break; }
                }
                if (at < shown.Length && char.IsLowSurrogate(shown[at])) at += at - 1 > from ? -1 : 1;
                yield return shown.Substring(from, at - from);
                from = at;
                continued = true;
            }
            if (from < shown.Length || !continued) yield return shown.Substring(from);
        }

        /// <summary>The command's lines, as written: theirs.</summary>
        public IReadOnlyList<string> Lines { get; }

        /// <summary>The rows the lines wrap to in the well, in order: every character of every shown line, once.</summary>
        public IReadOnlyList<KitReadingRow> Rows { get; }

        /// <summary>
        /// A line as the reader shows it: by <see cref="LabelText.Exact"/>'s rule, so a tab or any other white space but the space
        /// shows as its code point, and with its indentation and trailing spaces kept.
        /// </summary>
        public string ShownLine(int line)
        {
            // Visible bookends keep Exact's label trimming from losing indentation or trailing spaces (as RecordedResultText).
            var shown = LabelText.Exact("x" + Lines[line] + "x");
            return shown.Substring(1, shown.Length - 2);
        }

        /// <summary>A line as the reader shows it, as words of theirs.</summary>
        public LineWords Words(int line) => LineWords.Shown(ShownLine(line));

        /// <summary>A row as the reader shows it, as words of theirs.</summary>
        public LineWords RowWords(int row) => LineWords.Shown(Rows[row].Text);

        /// <summary>The first row shown now.</summary>
        public int First { get; private set; }

        /// <summary>The furthest row ever shown; -1 before anything is shown.</summary>
        public int Furthest { get; private set; }

        /// <summary>A comment line: its first visible character is "#". Shown quieter, still read.</summary>
        public bool IsComment(int line) => Lines[line].TrimStart().StartsWith("#", StringComparison.Ordinal);

        /// <summary>How far the command has been read, 0 to 1: the furthest row shown over all of them.</summary>
        public double Progress => Rows.Count == 0 ? 1 : (Furthest + 1) / (double)Rows.Count;

        /// <summary>The last row of the last line has been shown: Run may stand in Read to End's place, and stays.</summary>
        public bool ReadWhole => Furthest >= Rows.Count - 1;

        /// <summary>Rows hidden above and below what is shown, for the fades.</summary>
        public bool HiddenAbove => First > 0;

        public bool HiddenBelow(int shown) => First + shown < Rows.Count;

        /// <summary>
        /// Shows <paramref name="shown"/> rows from <paramref name="first"/> (clamped so the last page is full where it can be)
        /// and records how far the reading has reached; returns the first row shown.
        /// </summary>
        public int Show(int first, int shown)
        {
            shown = Math.Max(1, shown);
            First = Math.Max(0, Math.Min(first, Math.Max(0, Rows.Count - shown)));
            Furthest = Math.Max(Furthest, Math.Min(Rows.Count - 1, First + shown - 1));
            return First;
        }
    }
}

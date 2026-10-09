#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Halcyonic.Client
{
    /// <summary>A run of a line's words: Halcyonic's own, or text from outside, as shown.</summary>
    public sealed class WordRun
    {
        internal WordRun(string text, bool theirs)
        {
            Text = text;
            Theirs = theirs;
        }

        /// <summary>The words as shown: Halcyonic's as written, outside text by <see cref="LabelText"/>'s rule.</summary>
        public string Text { get; }

        /// <summary>Text from outside: it leans on a ground only Halcyonic draws, and is cut before any of Halcyonic's words.</summary>
        public bool Theirs { get; }
    }

    /// <summary>
    /// A line's words as runs, Halcyonic's own and text from outside (ADR 0026, lane V's treatment):
    /// Halcyonic's stand upright; outside text leans, on a ground only Halcyonic draws where it meets
    /// Halcyonic's words, and nothing is quoted. Outside text is shown by <see cref="LabelText"/>'s
    /// rule when its run is made, so a run's ends are where the shown text's are: what an agent types
    /// can never move them, never close a quote, never draw one of Halcyonic's icons, and never pass
    /// for Halcyonic's words. A line that doesn't fit is cut in outside text first, inside its run;
    /// Halcyonic's words are never cut while outside text still shows.
    /// </summary>
    public sealed class LineWords
    {
        /// <summary>What a cut run ends in, inside the run, so it leans on its ground.</summary>
        public const string Ellipsis = "…";

        private LineWords(IReadOnlyList<WordRun> runs)
        {
            Runs = runs;
            Text = string.Concat(runs.Select(run => run.Text));
        }

        /// <summary>Nothing, to add runs to.</summary>
        public static LineWords None { get; } = new LineWords(Array.Empty<WordRun>());

        /// <summary>
        /// Halcyonic's own words, as written, spaces at either end included so they join the runs
        /// beside them. They hold nothing <see cref="LabelText.Plain"/> would change but those spaces.
        /// </summary>
        public static LineWords Ours(string words)
        {
            if (words == null) throw new ArgumentNullException(nameof(words));
            if (LabelText.Plain(words) != words.Trim(' ')) throw new ArgumentException("Halcyonic's own words show as written.", nameof(words));
            return words.Length == 0 ? None : new LineWords(new[] { new WordRun(words, theirs: false) });
        }

        /// <summary>
        /// A line's words given whole as a string, upright as Halcyonic's: its own, or the person's,
        /// which the line's builder has already shown by <see cref="LabelText"/>'s rule.
        /// </summary>
        internal static LineWords Written(string words) => string.IsNullOrEmpty(words) ? None : new LineWords(new[] { new WordRun(words, theirs: false) });

        /// <summary>
        /// Text from outside as its caller has already shown it by a rule of its own, as the review before
        /// Start building spells every character (<see cref="NewWorkReview"/>): nothing is changed here.
        /// </summary>
        internal static LineWords Shown(string text) => Of(text);

        /// <summary>Text from outside, by <see cref="LabelText.Plain"/>'s rule: none where that leaves nothing.</summary>
        public static LineWords Theirs(string? text) => Of(LabelText.Plain(text));

        /// <summary>A name from outside, by <see cref="LabelText.Name"/>'s rule, so a blank one still shows.</summary>
        public static LineWords TheirName(string? text) => Of(LabelText.Name(text));

        private static LineWords Of(string shown) => shown.Length == 0 ? None : new LineWords(new[] { new WordRun(shown, theirs: true) });

        /// <summary>The runs in order, two side by side never the same kind.</summary>
        public IReadOnlyList<WordRun> Runs { get; }

        /// <summary>The words as shown, every run in order.</summary>
        public string Text { get; }

        public bool IsEmpty => Runs.Count == 0;

        /// <summary>Every word is from outside, as an agent's answer or a source's summary.</summary>
        public bool AllTheirs => Runs.Count > 0 && Runs.All(run => run.Theirs);

        public bool AnyTheirs => Runs.Any(run => run.Theirs);

        /// <summary>
        /// Some of Halcyonic's words come after outside text, so an ellipsis at the line's end would
        /// cut them: a line that doesn't fit is cut by <see cref="CutToFit"/>, never at its end.
        /// </summary>
        public bool OursAfterTheirs
        {
            get
            {
                var seen = false;
                foreach (var run in Runs)
                {
                    if (run.Theirs) seen = true;
                    else if (seen) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// These runs, then <paramref name="next"/>'s; two runs of one kind that meet become one, and so do two
        /// runs of outside text with only Halcyonic's punctuation between them (a separator doesn't make a line
        /// Halcyonic's, lane V's rule): "{header}: {labels}" is outside text whole.
        /// </summary>
        public LineWords Then(LineWords next)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            if (next.IsEmpty) return this;
            if (IsEmpty) return next;
            var runs = Runs.ToList();
            foreach (var run in next.Runs)
            {
                var last = runs[runs.Count - 1];
                if (last.Theirs == run.Theirs) runs[runs.Count - 1] = new WordRun(last.Text + run.Text, run.Theirs);
                else if (run.Theirs && runs.Count >= 2 && runs[runs.Count - 2].Theirs && Separator(last.Text))
                {
                    runs[runs.Count - 2] = new WordRun(runs[runs.Count - 2].Text + last.Text + run.Text, theirs: true);
                    runs.RemoveAt(runs.Count - 1);
                }
                else runs.Add(run);
            }
            return new LineWords(runs);
        }

        /// <summary>Halcyonic's words that only separate, as ": ", ", ", "; " or " · ": spaces and one punctuation mark at most.</summary>
        private static bool Separator(string words)
        {
            var marks = words.Trim(' ');
            return marks.Length <= 1 && (marks.Length == 0 || ",:;\u00B7".IndexOf(marks[0]) >= 0);
        }

        public static LineWords operator +(LineWords first, LineWords second) => first.Then(second);

        /// <summary>
        /// The text for a TextMeshPro label with rich text off and escape parsing on, every backslash
        /// doubled as <see cref="LabelText.ForTextMeshPro"/> does, and where each run of outside text
        /// stands in it, as the index of its first character and the one past its last: the ranges
        /// are taken on that text, so a doubled backslash, a code point shown by code, a surrogate
        /// pair or a combining mark never moves a run's edge. Where outside text meets Halcyonic's words
        /// one more space stands between them, outside the run, so a ground sits in the sentence as a
        /// word does: beside Halcyonic's own space, a space (<see cref="Pad"/>), the ground's padding
        /// inside it; beside Halcyonic's punctuation, a narrow one (<see cref="NarrowPad"/>), so the mark
        /// follows the ground's edge as it follows a letter, clear of it and nearer than a word space
        /// (lane V's rule).
        /// </summary>
        public (string Text, IReadOnlyList<(int Start, int End)> Theirs) ForTextMeshPro()
        {
            var text = new StringBuilder();
            var theirs = new List<(int, int)>();
            for (var index = 0; index < Runs.Count; index++)
            {
                var run = Runs[index];
                if (index > 0)
                {
                    var ours = run.Theirs ? Runs[index - 1].Text : run.Text;
                    text.Append((run.Theirs ? ours[ours.Length - 1] : ours[0]) == ' ' ? Pad : NarrowPad);
                }
                var start = text.Length;
                text.Append(run.Text.Replace("\\", "\\\\"));
                if (run.Theirs) theirs.Add((start, text.Length));
            }
            return (text.ToString(), theirs);
        }

        /// <summary>The room added where outside text meets Halcyonic's words, for the ground's padding: one space.</summary>
        public const string Pad = " ";

        /// <summary>
        /// The room added where outside text meets Halcyonic's punctuation rather than its space: a narrow
        /// no-break space, about seven tenths of a space, that a line never breaks at, so the mark stays
        /// beside the ground and the ground's padding stops halfway to it.
        /// </summary>
        public const string NarrowPad = "\u202F";

        /// <summary>
        /// These words cut to what <paramref name="fits"/>: outside text only, every run of it held to
        /// one length, the longest that fits, so a short name beside a long one stays whole; a run
        /// longer ends in <see cref="Ellipsis"/> inside it (<see cref="WorkspaceText.Truncate"/>), never
        /// inside a code point <see cref="LabelText.Plain"/> shows or a surrogate pair. Halcyonic's
        /// words are never cut, so where even every run of outside text cut to its ellipsis doesn't
        /// fit, that is what comes back, for the render checks to find.
        /// </summary>
        public LineWords CutToFit(Func<LineWords, bool> fits)
        {
            if (fits == null) throw new ArgumentNullException(nameof(fits));
            if (fits(this)) return this;
            var longestRun = Runs.Where(run => run.Theirs).Select(run => run.Text.Length).DefaultIfEmpty(0).Max();
            // The longest length that fits, found by halving: a shorter length never takes more room.
            LineWords? best = null;
            int shortest = 1, longest = longestRun - 1;
            while (shortest <= longest)
            {
                var most = (shortest + longest) / 2;
                var tried = HeldTo(most);
                if (fits(tried))
                {
                    best = tried;
                    shortest = most + 1;
                }
                else
                {
                    longest = most - 1;
                }
            }
            return best ?? HeldTo(1);
        }

        /// <summary>These words with every run of outside text at most <paramref name="most"/> characters, its ellipsis included.</summary>
        private LineWords HeldTo(int most) =>
            new LineWords(Runs.Select(run => run.Theirs ? new WordRun(WorkspaceText.Truncate(run.Text, most), theirs: true) : run).ToList());

        public override string ToString() => string.Concat(Runs.Select(run => run.Theirs ? "[" + run.Text + "]" : run.Text));
    }
}

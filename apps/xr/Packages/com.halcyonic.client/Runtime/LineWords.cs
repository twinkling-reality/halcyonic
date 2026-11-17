#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Halcyonic.Client
{
    /// <summary>A run of a line's words: Halcyonic's own, text from outside, or a mark Halcyonic draws in a literal well, as shown.</summary>
    public sealed class WordRun
    {
        internal WordRun(string text, bool theirs, bool spelled = true, bool mark = false)
        {
            Text = text;
            Theirs = theirs;
            Spelled = spelled;
            Mark = mark;
        }

        /// <summary>
        /// A mark Halcyonic draws in a literal well (<see cref="LineWords.Literal"/>) where a character would not show as
        /// itself: a line break, a tab, a space at an end, a control or bidi character by its code point. Never outside text
        /// (<see cref="Theirs"/> is false), and drawn apart from it, so the same glyphs typed by an agent never read as one.
        /// </summary>
        public bool Mark { get; }

        /// <summary>The words as shown: Halcyonic's as written, outside text by <see cref="LabelText"/>'s rule.</summary>
        public string Text { get; }

        /// <summary>Text from outside: it leans on a ground only Halcyonic draws, and is cut before any of Halcyonic's words.</summary>
        public bool Theirs { get; }

        /// <summary>
        /// Shown by a rule already; false only for outside text as it came (<see cref="LineWords.Unspelled"/>), which its
        /// builder spells before anything shows it and a label refuses.
        /// </summary>
        internal bool Spelled { get; }
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
        /// The host's own words, laid upright as Halcyonic's: as <see cref="Ours"/>, except that a character the headset folds
        /// (a no-break space, a tab, a control character) is shown as <see cref="LabelText.Plain"/> shows it instead of refused,
        /// so a person's card never stops drawing because a host sentence held one. Every fold is counted in <see cref="Folds"/>,
        /// so a check that reads the count round one state fails that state whatever an earlier state folded; each folded line
        /// is said once through <see cref="Folded"/> by its kind alone, never the words, however often its card is laid again,
        /// so the device log says it once (AUDIT's 05:34Z and 05:35Z).
        /// </summary>
        public static LineWords HostPlain(string words)
        {
            if (words == null) throw new ArgumentNullException(nameof(words));
            var plain = LabelText.Plain(words);
            if (plain == words.Trim(' ')) return Ours(words);
            System.Threading.Interlocked.Increment(ref folds);
            bool first;
            lock (foldedLines) first = foldedLines.Add(words.GetHashCode());
            if (first) Folded?.Invoke(FoldKind(words));
            return Ours(plain);
        }

        /// <summary>How many times <see cref="HostPlain"/> has shown a host line plain since the app started: every fold, every lay.</summary>
        public static int Folds => System.Threading.Volatile.Read(ref folds);

        /// <summary>Said once for each host line <see cref="HostPlain"/> showed plain: the kind of the first character it folded, never the words.</summary>
        public static event Action<string>? Folded;

        private static int folds;

        /// <summary>The folded lines already said, by their hash in this process alone, never their words: what the log says once.</summary>
        private static readonly HashSet<int> foldedLines = new HashSet<int>();

        private static string FoldKind(string words)
        {
            foreach (var unit in words)
            {
                if (unit == '\u00A0') return "no_break_space";
                if (unit == '\t') return "tab";
                if (unit == '\n' || unit == '\r') return "line_break";
                if (char.IsControl(unit)) return "control";
            }
            return "other";
        }

        /// <summary>
        /// A line's words given whole as a string, upright as Halcyonic's own,
        /// already shown by the line's builder through <see cref="LabelText"/>'s rule (<see cref="AsShown"/>).
        /// </summary>
        internal static LineWords Written(string words) => string.IsNullOrEmpty(words) ? None : new LineWords(new[] { new WordRun(AsShown(words, nameof(words)), theirs: false) });

        /// <summary>
        /// Text from outside as its caller has already shown it by a rule of its own, as the review before
        /// Start building spells every character (<see cref="NewWorkReview"/>): nothing is changed here, and
        /// nothing <see cref="LabelText.Plain"/> would change is taken (<see cref="AsShown"/>).
        /// </summary>
        internal static LineWords Shown(string text) => Of(AsShown(text, nameof(text)));

        /// <summary>
        /// Outside text exactly as it came, for a builder that spells every character before anything shows it, as the
        /// review before Start building does (<see cref="NewWorkReview"/>): a label refuses it (<see cref="ForTextMeshPro"/>),
        /// so it never reaches one unspelled. None where it is empty.
        /// </summary>
        public static LineWords Unspelled(string? text) =>
            string.IsNullOrEmpty(text) ? None : new LineWords(new[] { new WordRun(text!, theirs: true, spelled: false) });

        /// <summary>
        /// <paramref name="text"/>, refused where any of its lines holds something <see cref="LabelText.Plain"/> would change
        /// but the spaces at its ends (AUDIT's X1, 2026-11-03), as <see cref="Ours"/> refuses: text given whole reaches a label
        /// only once shown by the rule, so a builder that forgot it fails its tests here instead of putting a control, an
        /// override or one of Halcyonic's icons on a label. The rule is idempotent, so every builder that applied it passes.
        /// </summary>
        private static string AsShown(string text, string parameter)
        {
            foreach (var line in text.Split('\n'))
                if (LabelText.Plain(line) != line.Trim(' ')) throw new ArgumentException("Text given whole is shown by LabelText's rule first, line by line.", parameter);
            return text;
        }

        /// <summary>Text from outside, by <see cref="LabelText.Plain"/>'s rule: none where that leaves nothing.</summary>
        public static LineWords Theirs(string? text) => Of(LabelText.Plain(text));

        /// <summary>A name from outside, by <see cref="LabelText.Name"/>'s rule, so a blank one still shows.</summary>
        public static LineWords TheirName(string? text) => Of(LabelText.Name(text));

        private static LineWords Of(string shown) => shown.Length == 0 ? None : new LineWords(new[] { new WordRun(shown, theirs: true) });

        /// <summary>A line break of Halcyonic's own, between the blocks of a literal well (<see cref="WorkspaceText.Well"/>).</summary>
        internal static LineWords Break { get; } = new LineWords(new[] { new WordRun("\n", theirs: false) });

        /// <summary>A line break's mark in a literal well; the line breaks after it.</summary>
        public const string LineBreakMark = "↵";

        /// <summary>A tab's mark in a literal well.</summary>
        public const string TabMark = "⇥";

        /// <summary>
        /// The mark of a space a literal well would not show, at either end of its text or just before a line break: a middle dot,
        /// written by its code so no scan mistakes the mark for facts joined by a dot.
        /// </summary>
        public const string SpaceMark = "\u00B7";

        /// <summary>
        /// Text whose every character can change what happens, as a command or a tool's input a person approves, for a well that
        /// draws it literally (AUDIT3's LI4, 2026-11-17): outside text, with every mark a <see cref="WordRun.Mark"/> run of its own.
        /// The client's one marking function: nothing else marks a character, and it shrinks to its first rule alone once the
        /// host writes every mark itself (the lead's ruling, 2026-11-17).
        /// <list type="bullet">
        /// <item>
        /// The marks the host writes are given the mark role, styling only, never changed: a code point shown by code ("‹", "U+",
        /// hex digits, "›", as ‹U+202E›), <see cref="LineBreakMark"/> and <see cref="TabMark"/>. The host escapes those characters
        /// where an app's own text holds them (‹U+2039› for a ‹), so any that arrives is a mark. Until that host lands, a ↵ an
        /// agent typed is styled as a mark too; the host closes that.
        /// </item>
        /// <item>
        /// Until the host writes them, the marks of what reaches the client raw: <see cref="LineBreakMark"/> before a line feed,
        /// unless one already stands just before it, the line still breaking after it; <see cref="TabMark"/> for a tab;
        /// <see cref="SpaceMark"/> for a space at either end, or just before a line break; and, as a guard, any other white
        /// space, control, format, bidi, default ignorable or private use character, or a surrogate without its pair, by its
        /// code point, as <see cref="LabelText.Exact"/> shows it. So text from the new host is never marked twice.
        /// </item>
        /// </list>
        /// Everything else is outside text as written, spaces between words included, and so is "\u{202E}" text, which older
        /// events carry and which cannot be told from what an agent typed. None where the text is empty.
        /// </summary>
        public static LineWords Literal(string? text)
        {
            if (string.IsNullOrEmpty(text)) return None;
            var runs = new List<WordRun>();
            void Add(string piece, bool theirs, bool mark)
            {
                var last = runs.Count > 0 ? runs[runs.Count - 1] : null;
                if (last != null && last.Theirs == theirs && last.Mark == mark) runs[runs.Count - 1] = new WordRun(last.Text + piece, theirs, mark: mark);
                else runs.Add(new WordRun(piece, theirs, mark: mark));
            }
            var lineBreak = LineBreakMark[0];
            var tab = TabMark[0];
            // A space shows as a mark where nothing visible stands between it and an end or a line break (a ↵ or a carriage
            // return just before the break counts as the break): from the end back, then every leading one from the start.
            var marked = new bool[text!.Length];
            var open = true;
            for (var index = text.Length - 1; index >= 0; index--)
            {
                var unit = text[index];
                if (unit == ' ') marked[index] = open;
                else open = unit == '\n' || ((unit == '\r' || unit == lineBreak) && index + 1 < text.Length && text[index + 1] == '\n');
            }
            for (var index = 0; index < text.Length && text[index] == ' '; index++) marked[index] = true;
            for (var index = 0; index < text.Length; index++)
            {
                var unit = text[index];
                if (ShownCodeAt(text, index) is var length && length > 0)
                {
                    Add(text.Substring(index, length), theirs: false, mark: true);
                    index += length - 1;
                    continue;
                }
                var paired = char.IsHighSurrogate(unit) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]);
                var codePoint = paired ? char.ConvertToUtf32(unit, text[index + 1]) : unit;
                if (unit == '\n')
                {
                    if (index == 0 || text[index - 1] != lineBreak) Add(LineBreakMark, theirs: false, mark: true);
                    Add("\n", theirs: false, mark: false);
                }
                else if (unit == lineBreak || unit == tab) Add(unit.ToString(), theirs: false, mark: true);
                else if (unit == '\t') Add(TabMark, theirs: false, mark: true);
                else if (unit == ' ' && marked[index]) Add(SpaceMark, theirs: false, mark: true);
                else if (unit != ' ' && (LabelText.IsWhiteSpace(codePoint) || LabelText.ShowsByCode(codePoint) || (!paired && char.IsSurrogate(unit))))
                    Add(LabelText.CodePoint(codePoint), theirs: false, mark: true);
                else Add(text.Substring(index, paired ? 2 : 1), theirs: true, mark: false);
                if (paired) index++;
            }
            return new LineWords(runs);
        }

        /// <summary>
        /// The length of a code point shown by code that starts at <paramref name="at"/> in <paramref name="text"/>: "‹", "U+", one
        /// to six hex digits, "›", as ‹U+202E›; 0 where none starts there.
        /// </summary>
        private static int ShownCodeAt(string text, int at)
        {
            if (text[at] != '‹' || at + 3 >= text.Length || text[at + 1] != 'U' || text[at + 2] != '+') return 0;
            var end = at + 3;
            while (end < text.Length && end - at - 3 < 6 && Uri.IsHexDigit(text[end])) end++;
            return end > at + 3 && end < text.Length && text[end] == '›' ? end - at + 1 : 0;
        }

        /// <summary>The runs in order, two side by side never the same kind.</summary>
        public IReadOnlyList<WordRun> Runs { get; }

        /// <summary>The words as shown, every run in order.</summary>
        public string Text { get; }

        public bool IsEmpty => Runs.Count == 0;

        /// <summary>Every word is from outside, as an agent's answer or a source's summary.</summary>
        public bool AllTheirs => Runs.Count > 0 && Runs.All(run => run.Theirs);

        public bool AnyTheirs => Runs.Any(run => run.Theirs);

        /// <summary>A mark run here draws a line break's mark (<see cref="LineBreakMark"/>), so a well's key line belongs beside it.</summary>
        public bool MarksLineBreak => Runs.Any(run => run.Mark && run.Text.Contains(LineBreakMark));

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
                // A run joined with one not yet spelled is not spelled either, so a label still refuses it.
                var last = runs[runs.Count - 1];
                // A mark is a kind of its own: it joins only another mark, and is never a separator.
                if (last.Theirs == run.Theirs && last.Mark == run.Mark) runs[runs.Count - 1] = new WordRun(last.Text + run.Text, run.Theirs, last.Spelled && run.Spelled, run.Mark);
                else if (run.Theirs && !last.Mark && runs.Count >= 2 && runs[runs.Count - 2].Theirs && Separator(last.Text))
                {
                    runs[runs.Count - 2] = new WordRun(runs[runs.Count - 2].Text + last.Text + run.Text, theirs: true, runs[runs.Count - 2].Spelled && run.Spelled);
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
        /// (lane V's rule). Outside text not yet spelled (<see cref="Unspelled"/>) is refused: it never reaches a label.
        /// </summary>
        /// <param name="padded">
        /// The room for a ground: false where no ground is drawn under outside text (the room's faces), so where the runs
        /// meet only their own spacing stands, never a second space (the critic, 2026-11-12: "New  kite-run  in  Projects").
        /// </param>
        public (string Text, IReadOnlyList<(int Start, int End)> Theirs) ForTextMeshPro(bool padded = true)
        {
            var (text, theirs, _) = ForTextMeshProWithMarks(padded);
            return (text, theirs);
        }

        /// <summary>
        /// <see cref="ForTextMeshPro"/>'s text and outside text's ranges, and where each <see cref="WordRun.Mark"/> stands in it, on
        /// the same text: what a literal well draws apart from the text it marks. No room is added beside a mark, so a mark
        /// stands tight against what it marks.
        /// </summary>
        public (string Text, IReadOnlyList<(int Start, int End)> Theirs, IReadOnlyList<(int Start, int End)> Marks) ForTextMeshProWithMarks(bool padded = true)
        {
            if (Runs.Any(run => !run.Spelled)) throw new InvalidOperationException("Outside text reaches a label only once spelled by its builder's rule.");
            var text = new StringBuilder();
            var theirs = new List<(int, int)>();
            var marks = new List<(int, int)>();
            for (var index = 0; index < Runs.Count; index++)
            {
                var run = Runs[index];
                text.Append(PadBefore(index, padded));
                var start = text.Length;
                text.Append(run.Text.Replace("\\", "\\\\"));
                if (run.Theirs) theirs.Add((start, text.Length));
                if (run.Mark) marks.Add((start, text.Length));
            }
            return (text.ToString(), theirs, marks);
        }

        /// <summary>The room added before run <paramref name="index"/> where it meets the run before it (<see cref="ForTextMeshPro"/>), or none.</summary>
        private string PadBefore(int index, bool padded)
        {
            if (index == 0 || !padded) return "";
            var run = Runs[index];
            var before = Runs[index - 1];
            if (run.Mark || before.Mark) return "";
            var ours = run.Theirs ? before.Text : run.Text;
            return (run.Theirs ? ours[ours.Length - 1] : ours[0]) == ' ' ? Pad : NarrowPad;
        }

        /// <summary>
        /// The runs that stand from <paramref name="start"/> to <paramref name="end"/> of <see cref="ForTextMeshPro"/>'s text with
        /// <paramref name="padded"/> room, each kept its kind (a well's part keeps its marks, the agent's text and Halcyonic's), each
        /// doubled backslash one again and the room between runs left out: what a part of these words laid on a label reads.
        /// <paramref name="start"/> and <paramref name="end"/> fall where characters start (<see cref="LabelText.CharacterStart"/>).
        /// </summary>
        public LineWords Between(int start, int end, bool padded = true)
        {
            var runs = new List<WordRun>();
            var at = 0;
            for (var index = 0; index < Runs.Count; index++)
            {
                var run = Runs[index];
                at += PadBefore(index, padded).Length;
                var shown = run.Text.Replace("\\", "\\\\");
                var from = Math.Max(start, at);
                var to = Math.Min(end, at + shown.Length);
                if (from < to) runs.Add(new WordRun(LabelText.FromTextMeshPro(shown.Substring(from - at, to - from)), run.Theirs, run.Spelled, run.Mark));
                at += shown.Length;
            }
            return runs.Count == 0 ? None : new LineWords(runs);
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
            new LineWords(Runs.Select(run => run.Theirs ? new WordRun(WorkspaceText.Truncate(run.Text, most), theirs: true, run.Spelled) : run).ToList());

        public override string ToString() => string.Concat(Runs.Select(run => run.Theirs ? "[" + run.Text + "]" : run.Mark ? "{" + run.Text + "}" : run.Text));
    }
}

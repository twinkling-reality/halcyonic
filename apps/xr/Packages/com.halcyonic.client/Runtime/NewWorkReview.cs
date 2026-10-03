#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Halcyonic.Client
{
    /// <summary>One entry of the review: Halcyonic's own label, and the value it names, spelled in ASCII.</summary>
    public sealed class ReviewItem
    {
        public ReviewItem(string label, string value)
        {
            Label = label;
            Value = value;
        }

        /// <summary>Halcyonic's words, for example "Project: ".</summary>
        public string Label { get; }

        /// <summary>The value as sent, with every character the headset font may lack spelled as its code point.</summary>
        public string Value { get; }

        /// <summary>The label and the value, as the panel lays them out.</summary>
        public string Text => Label + Value;
    }

    /// <summary>The lines of one review item that one page shows: all of them, or a stretch of an item taller than a page.</summary>
    public readonly struct ReviewPart
    {
        public ReviewPart(int item, int firstLine, int lines, int part)
        {
            Item = item;
            FirstLine = firstLine;
            Lines = lines;
            Part = part;
        }

        public int Item { get; }

        public int FirstLine { get; }

        public int Lines { get; }

        /// <summary>Which page's worth of the item this is, from 0; always 0 for an item that fits a page.</summary>
        public int Part { get; }
    }

    /// <summary>
    /// The complete request shown before the headset creates or starts work: each value as it will
    /// be sent, under Halcyonic's own label, in pages the person steps through. The panel lays the
    /// items out at its own width, wrapping at word boundaries, and tells the review how many lines
    /// each takes (<see cref="Paginate"/>); the review then fills each page with whole items, and
    /// splits an item across pages only when it alone is taller than a page. A line counts as read
    /// only once the panel has drawn it (<see cref="Drawn"/>), and the final action is offered only on
    /// the last page with every line of the current layout drawn. Laid out again, as when the text
    /// size changes or a banner leaves room, an item drawn whole stays read and one drawn in part is
    /// read again, since its lines may wrap elsewhere now.
    /// </summary>
    /// <remarks>
    /// Values are text from outside: the person's own words, names from the Mac and from runtimes,
    /// given as they are, never already shown through <see cref="LabelText.Plain"/>, so nothing is
    /// spelled twice. The bundled headset font cannot show every character, so every non-ASCII and
    /// control character is spelled as its code point, <c>\u{HEX}</c>, and a typed backslash is
    /// doubled so it can never read as one of those. Halcyonic's own words are never spelled: its
    /// labels are ASCII, and the ellipsis after a title it cut shows as written, so a literal "…" in
    /// the request is always Halcyonic's. Nothing else is shortened: the pages together show every
    /// character.
    /// </remarks>
    public sealed class NewWorkReview
    {
        /// <summary>
        /// Seconds a part shows, once drawn, before Next part moves on: a second press as quick as a
        /// double press never passes a part almost unseen.
        /// </summary>
        public const double NextPause = 0.4;

        private readonly List<ReviewItem> items = new List<ReviewItem>();
        private readonly List<List<ReviewPart>> pages = new List<List<ReviewPart>>();

        /// <summary>For each item, which of its lines in the current layout the panel has drawn.</summary>
        private readonly List<bool[]> drawn = new List<bool[]>();

        /// <summary>When the page showing was first drawn since it came to show, on the panel's steady clock; null until it is.</summary>
        private double? pageDrawnAt;

        /// <param name="folder">Where the project's files will live, in words, or null when it is not part of the request.</param>
        /// <param name="folderBefore">
        /// An existing project's folder now, when the request moves it to <paramref name="folder"/>:
        /// both show, since all later work in the project runs in the new one.
        /// </param>
        /// <param name="titleCut">
        /// Halcyonic cut <paramref name="title"/>, the part of the objective the title holds as typed
        /// (<see cref="NewWorkDraft.TitleSource"/>): its own ellipsis follows, as written, never spelled
        /// as a code point the way a typed one is.
        /// </param>
        /// <summary>The labels whose values a build checks against what it sends (<see cref="Shows"/>).</summary>
        public const string ProjectLabel = "Project: ";
        public const string ModelIdLabel = "Model id: ";
        public const string FirstTaskLabel = "First task: ";

        public NewWorkReview(string project, string title, string runtime, string model, string modelFacts, string modelRef, string objective,
            string? folder = null, string? folderBefore = null, bool titleCut = false)
        {
            Add(ProjectLabel, project);
            if (folderBefore != null && folder != null)
            {
                Add("Folder now: ", folderBefore);
                Add("Folder from now on: ", folder);
            }
            else if (folder != null) Add("Where its files live: ", folder);
            items.Add(new ReviewItem("Task name: ", Safe(title) + (titleCut ? "…" : "")));
            Add("Agent app: ", runtime);
            Add("Model: ", model);
            Add("Where the model runs: ", modelFacts);
            Add(ModelIdLabel, modelRef);
            Add(FirstTaskLabel, objective);
            // Beside the first task, before Yes: what it names that the person should check (TaskWarnings).
            foreach (var note in TaskWarnings.Of(objective)) items.Add(new ReviewItem(note, ""));
        }

        public IReadOnlyList<ReviewItem> Items => items;

        /// <summary>The items have been laid out in pages; until then nothing can be confirmed.</summary>
        public bool Paginated => pages.Count > 0;

        public int Page { get; private set; }

        public int PageCount => pages.Count;

        /// <summary>What the page showing holds, top to bottom.</summary>
        public IReadOnlyList<ReviewPart> Parts => Paginated ? pages[Page] : (IReadOnlyList<ReviewPart>)Array.Empty<ReviewPart>();

        public IReadOnlyList<IReadOnlyList<ReviewPart>> Pages => pages;

        /// <summary>
        /// The final action can be taken: the last page shows, every line of every item has been drawn
        /// in this layout, and it has not been taken already.
        /// </summary>
        public bool CanConfirm => !Spent && Paginated && Page == PageCount - 1 && AllDrawn;

        /// <summary>The final action was taken: a review confirms one send, and sending again reads a fresh one.</summary>
        public bool Spent { get; private set; }

        /// <summary>Takes the final action: true once, and only while it can be confirmed.</summary>
        public bool Spend()
        {
            if (!CanConfirm) return false;
            Spent = true;
            return true;
        }

        /// <summary>Whether <paramref name="other"/> shows exactly this request, item by item, as a review made afresh from the same choices would.</summary>
        public bool SameRequest(NewWorkReview? other) =>
            other != null && other.items.Count == items.Count && items.Zip(other.items, (mine, theirs) => mine.Text == theirs.Text).All(same => same);

        /// <summary>Whether the review shows <paramref name="value"/>, spelled as it spells it, under <paramref name="label"/>.</summary>
        public bool Shows(string label, string value) => items.Any(item => item.Label == label && item.Value == Safe(value));

        /// <summary>Every line of every item has been drawn in the current layout.</summary>
        public bool AllDrawn => Paginated && drawn.All(lines => lines.All(line => line));

        /// <summary>Every line on the page showing has been drawn.</summary>
        public bool PageDrawn => Paginated && Parts.All(part => Enumerable.Range(part.FirstLine, part.Lines).All(line => drawn[part.Item][line]));

        /// <summary>Whether <paramref name="item"/> has been drawn whole, in this layout or one before it.</summary>
        public bool DrawnWhole(int item) => item >= 0 && item < drawn.Count && drawn[item].All(line => line);

        /// <summary>
        /// Lays the items out on pages of <paramref name="pageLines"/> lines, from the lines each takes
        /// at the panel's width, <paramref name="itemLines"/>, in the order of <see cref="Items"/>. An
        /// item that fits a page is never split: it starts a new page when the one it would end on is
        /// full. An item taller than a page starts on a page of its own and fills whole pages; what
        /// follows it continues under its last part. Items drawn whole before stay read; an item drawn in
        /// part is read again. Shows the first page with a line not yet drawn, or the last page when
        /// every line has been.
        /// </summary>
        public void Paginate(IReadOnlyList<int> itemLines, int pageLines)
        {
            if (itemLines == null) throw new ArgumentNullException(nameof(itemLines));
            if (itemLines.Count != items.Count) throw new ArgumentException("Give every item its lines.", nameof(itemLines));
            if (pageLines < 1) throw new ArgumentOutOfRangeException(nameof(pageLines), pageLines, "A page holds at least one line.");
            // Read whole stays read; read in part can't be mapped onto lines that may wrap elsewhere now.
            var whole = Enumerable.Range(0, items.Count).Select(DrawnWhole).ToList();
            drawn.Clear();
            for (var index = 0; index < itemLines.Count; index++)
            {
                var lines = new bool[Math.Max(1, itemLines[index])];
                if (whole[index]) for (var line = 0; line < lines.Length; line++) lines[line] = true;
                drawn.Add(lines);
            }
            pages.Clear();
            var page = new List<ReviewPart>();
            var used = 0;
            for (var index = 0; index < itemLines.Count; index++)
            {
                var lines = Math.Max(1, itemLines[index]);
                if (lines <= pageLines)
                {
                    if (used + lines > pageLines)
                    {
                        pages.Add(page);
                        page = new List<ReviewPart>();
                        used = 0;
                    }
                    page.Add(new ReviewPart(index, 0, lines, 0));
                    used += lines;
                    continue;
                }
                if (used > 0)
                {
                    pages.Add(page);
                    page = new List<ReviewPart>();
                    used = 0;
                }
                for (var first = 0; first < lines; first += pageLines)
                {
                    var shown = Math.Min(pageLines, lines - first);
                    page.Add(new ReviewPart(index, first, shown, first / pageLines));
                    used = shown;
                    if (first + pageLines < lines)
                    {
                        pages.Add(page);
                        page = new List<ReviewPart>();
                        used = 0;
                    }
                }
            }
            if (page.Count > 0) pages.Add(page);
            Page = 0;
            while (Page < PageCount - 1 && PageDrawn) Page++;
            pageDrawnAt = null;
        }

        /// <summary>
        /// The panel drew the page showing, at <paramref name="now"/> seconds on a steady clock: its
        /// lines count as read. Returns whether that made the final action available.
        /// </summary>
        public bool Drawn(double now)
        {
            if (!Paginated) return false;
            var could = CanConfirm;
            foreach (var part in Parts)
            {
                for (var line = part.FirstLine; line < part.FirstLine + part.Lines; line++) drawn[part.Item][line] = true;
            }
            if (pageDrawnAt == null) pageDrawnAt = now;
            return !could && CanConfirm;
        }

        /// <summary>
        /// Moves to the next page, once the page showing has been drawn and has shown for
        /// <see cref="NextPause"/> seconds by <paramref name="now"/>. Returns whether it moved; a press
        /// before then changes nothing.
        /// </summary>
        public bool Next(double now)
        {
            if (Page + 1 >= PageCount || !(pageDrawnAt is double at) || now - at < NextPause) return false;
            Page++;
            pageDrawnAt = null;
            return true;
        }

        public bool Previous()
        {
            if (Page == 0) return false;
            Page--;
            pageDrawnAt = null;
            return true;
        }

        private void Add(string label, string value) => items.Add(new ReviewItem(label, Safe(value)));

        // The bundled TMP font cannot show every character. Spell each non-ASCII code point and
        // control character in ASCII, and double a literal backslash so a typed marker differs.
        private static string Safe(string value)
        {
            var result = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                var unit = value[index];
                var paired = char.IsHighSurrogate(unit) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]);
                var codePoint = paired ? char.ConvertToUtf32(unit, value[index + 1]) : unit;
                if (unit == '\\') result.Append("\\\\");
                else if (codePoint >= 0x20 && codePoint <= 0x7E) result.Append(unit);
                else result.Append("\\u{").Append(codePoint.ToString("X", CultureInfo.InvariantCulture)).Append('}');
                if (paired) index++;
            }
            return result.ToString();
        }
    }
}

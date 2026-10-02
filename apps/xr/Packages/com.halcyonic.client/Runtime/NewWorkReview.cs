#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// splits an item across pages only when it alone is taller than a page. The final action is
    /// offered only on the last page, after the person has advanced through every preceding page.
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
        private readonly List<ReviewItem> items = new List<ReviewItem>();
        private readonly List<List<ReviewPart>> pages = new List<List<ReviewPart>>();

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
        public NewWorkReview(string project, string title, string runtime, string model, string modelFacts, string modelRef, string objective,
            string? folder = null, string? folderBefore = null, bool titleCut = false)
        {
            Add("Project: ", project);
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
            Add("Model id: ", modelRef);
            Add("First task: ", objective);
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

        public bool CanConfirm => Paginated && Page == PageCount - 1;

        /// <summary>
        /// Lays the items out on pages of <paramref name="pageLines"/> lines, from the lines each takes
        /// at the panel's width, <paramref name="itemLines"/>, in the order of <see cref="Items"/>. An
        /// item that fits a page is never split: it starts a new page when the one it would end on is
        /// full. An item taller than a page starts on a page of its own and fills whole pages; what
        /// follows it continues under its last part. Shows the first page.
        /// </summary>
        public void Paginate(IReadOnlyList<int> itemLines, int pageLines)
        {
            if (itemLines == null) throw new ArgumentNullException(nameof(itemLines));
            if (itemLines.Count != items.Count) throw new ArgumentException("Give every item its lines.", nameof(itemLines));
            if (pageLines < 1) throw new ArgumentOutOfRangeException(nameof(pageLines), pageLines, "A page holds at least one line.");
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
        }

        public void Next()
        {
            if (Page + 1 < PageCount) Page++;
        }

        public void Previous()
        {
            if (Page > 0) Page--;
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

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>What a review asks the person to look at before Yes: a web address it names, something it runs, or anything else.</summary>
    public enum ReviewCheckKind
    {
        Other,
        WebAddress,
        Command,
    }

    /// <summary>One fact of a review: its kind's mark, its value alone, set in mono where it is a name to type.</summary>
    public readonly struct ReviewFact
    {
        /// <param name="slot">Short enough to stand as a slot of the card's loadout; else a row under the slots.</param>
        public ReviewFact(RoomKind mark, LineWords value, bool mono = false, bool slot = false)
        {
            Mark = mark;
            Value = value;
            Mono = mono;
            Slot = slot;
        }

        public RoomKind Mark { get; }
        public LineWords Value { get; }
        public bool Mono { get; }
        public bool Slot { get; }
    }

    /// <summary>
    /// One thing to check before Yes: its kind, the value itself where one is to be seen (a web address, whole), and the host's
    /// own line for it where the host says one ("This cannot be undone"); without one, the kind's words say it.
    /// </summary>
    public readonly struct ReviewCheck
    {
        public ReviewCheck(ReviewCheckKind kind, LineWords? shown = null, string? line = null)
        {
            Kind = kind;
            Shown = shown;
            Line = string.IsNullOrWhiteSpace(line) ? null : line;
        }

        public ReviewCheckKind Kind { get; }
        public LineWords? Shown { get; }
        public string? Line { get; }
    }

    /// <summary>
    /// One review, of any command (the lead's decision of 2026-11-12: ONE card reads every command's review): what it acts on,
    /// the command's own long words where it sends some, its facts, and what to check. Nothing in it says which command it is.
    /// </summary>
    public sealed class ReviewSheet
    {
        /// <param name="title">What the command acts on, as a project's name.</param>
        /// <param name="words">The long words the command sends, as a task or a message: paged as the card's description.</param>
        /// <param name="checkWords">The person's own sentences that hold what is to be checked, shown on the check's part.</param>
        /// <param name="line">What happens, in one plain line of the host's words ("Your files go to the project's repository as a new branch").</param>
        /// <param name="items">What the command touches, in the order to read (a file and how it changes), a page at a time.</param>
        /// <param name="itemsTotal">How many items there are in all where the host sent only some; the items' own count otherwise.</param>
        public ReviewSheet(LineWords? title, RoomCardHead head = RoomCardHead.None, LineWords? words = null, IReadOnlyList<ReviewFact>? facts = null,
            IReadOnlyList<ReviewCheck>? checks = null, LineWords? checkWords = null, LineWords? line = null, IReadOnlyList<RoomListRow>? items = null,
            int? itemsTotal = null)
        {
            Title = title;
            Head = head;
            Words = words == null || words.IsEmpty ? null : words;
            Facts = facts ?? new ReviewFact[0];
            Checks = checks ?? new ReviewCheck[0];
            CheckWords = checkWords == null || checkWords.IsEmpty ? null : checkWords;
            Line = line == null || line.IsEmpty ? null : line;
            Items = items ?? new RoomListRow[0];
            ItemsTotal = itemsTotal is int total && total > Items.Count ? total : Items.Count;
        }

        public LineWords? Line { get; }
        public IReadOnlyList<RoomListRow> Items { get; }
        public int ItemsTotal { get; }

        public LineWords? Title { get; }
        public RoomCardHead Head { get; }
        public LineWords? Words { get; }
        public IReadOnlyList<ReviewFact> Facts { get; }
        public IReadOnlyList<ReviewCheck> Checks { get; }
        public LineWords? CheckWords { get; }
    }

    /// <summary>
    /// The review before Yes as cards (the addendum's A17), for any command's <see cref="ReviewSheet"/>: exactly what will be done,
    /// every fact in a part of a card, never a label joined on a dot. It reads in parts, so no card stands taller than a panel
    /// holds in view (the critics and the parts check, 2026-11-11):
    /// <list type="bullet">
    /// <item>The command's own words where it sends some: the title with its head, the words as the description, four lines a
    /// part (<see cref="RoomCard.DescriptionPageLines"/>), as many parts as it takes.</item>
    /// <item>Its facts: the title; the short facts as slots, then the rest as rows, each its value alone under its kind's mark.</item>
    /// <item>Where there is something to check, a last part right before Yes: the title over the status "Check First"
    /// (Attention, the alert's mark) with a short line of what kind of thing it is, the person's own sentences that hold it,
    /// and each value to be seen in mono in a slot, so what is to be checked is in view.</item>
    /// </list>
    /// Every part's foot line says which it is: "Part 2 of 3". The words' parts are as many as the card's view made of its
    /// description (<c>RoomCardView.DescriptionPages</c>, shown with part 0); <see cref="Parts(ReviewSheet, int)"/> counts them
    /// all. Each part counts as read when its frame is reported drawn with the card standing whole (<c>RoomCardView.LaidWhole</c>).
    /// </summary>
    public static class RoomReview
    {
        public const string CheckTitle = "Check First";

        /// <summary>The words before the folder a move leaves: "From kite-run".</summary>
        public const string MovesFrom = "From ";

        /// <summary>What a kind of thing to check is called in the check's line: the one table (R3).</summary>
        public static string CheckWordsOf(ReviewCheckKind kind) => kind switch
        {
            ReviewCheckKind.WebAddress => "a web address",
            ReviewCheckKind.Command => "something to run",
            _ => "something to check",
        };

        /// <summary>
        /// The check's line, adding to its title and ending in no full stop (the critic, 2026-11-11): "It names a web address",
        /// "It names a web address and something to run".
        /// </summary>
        public static string CheckLine(IEnumerable<ReviewCheckKind> kinds)
        {
            var named = kinds.Distinct().OrderBy(kind => kind == ReviewCheckKind.Other ? 1 : 0).Select(CheckWordsOf).ToList();
            if (named.Count == 0) return "It names " + CheckWordsOf(ReviewCheckKind.Other);
            var said = named.Count == 1 ? named[0] : string.Join(", ", named.Take(named.Count - 1)) + " and " + named[named.Count - 1];
            return "It names " + said;
        }

        public static string CheckAddressLine => CheckLine(new[] { ReviewCheckKind.WebAddress });
        public static string CheckCommandLine => CheckLine(new[] { ReviewCheckKind.Command });
        public static string CheckBothLine => CheckLine(new[] { ReviewCheckKind.WebAddress, ReviewCheckKind.Command });

        /// <summary>The foot line naming a part: "Part 2 of 3".</summary>
        public static string PartOf(int part, int parts) => "Part " + (part + 1) + " of " + parts;

        /// <summary>The parts of <paramref name="sheet"/> whose words made <paramref name="wordPages"/> pages.</summary>
        public static int Parts(ReviewSheet sheet, int wordPages) => WordParts(sheet, wordPages) + FactParts(sheet) + (sheet.Checks.Count > 0 ? 1 : 0);

        /// <summary>The facts' parts: the first holds the facts and the first page of items, each later one the next page of items.</summary>
        private static int FactParts(ReviewSheet sheet)
        {
            var rows = sheet.Facts.Count(fact => !fact.Slot) + Math.Max(0, sheet.Facts.Count(fact => fact.Slot) - RoomCard.MostSlots);
            var first = Math.Max(0, RoomCard.ListAPage - rows);
            var rest = Math.Max(0, sheet.Items.Count - first);
            return 1 + (rest + RoomCard.ListAPage - 1) / RoomCard.ListAPage;
        }

        /// <summary>
        /// Where there is something to check, every part carries it as the status under the title, so a Confirm offered on any
        /// part is never pressed with a warning unseen (Confirm stands on every part; paging is never required, R1).
        /// </summary>
        private static RoomCardStatus? Warned(ReviewSheet sheet) =>
            sheet.Checks.Count == 0 ? (RoomCardStatus?)null : new RoomCardStatus(CheckTitle, CheckLineOf(sheet), RoomTone.Attention, RoomKind.Failed);

        /// <summary>The check's line: the host's own line for the first thing to check where it says one, else the kinds' words.</summary>
        public static string CheckLineOf(ReviewSheet sheet) => sheet.Checks.Select(check => check.Line).FirstOrDefault(line => line != null)
            ?? CheckLine(sheet.Checks.Select(check => check.Kind));

        private static int WordParts(ReviewSheet sheet, int wordPages) => sheet.Words == null ? 0 : wordPages < 1 ? 1 : wordPages;

        /// <summary>
        /// The review's part <paramref name="part"/> (from 0) of <see cref="Parts(ReviewSheet, int)"/>: a page of the words while
        /// <paramref name="part"/> is under their pages, then the facts, then what to check where there is any. Measure the
        /// words' pages with part 0 at any count, then show each part with the count the view gave.
        /// </summary>
        public static RoomCard Card(ReviewSheet sheet, int part = 0, int wordPages = 1)
        {
            var pages = WordParts(sheet, wordPages);
            var factParts = FactParts(sheet);
            var parts = Parts(sheet, wordPages);
            var at = part < 0 ? 0 : part >= parts ? parts - 1 : part;
            var foot = parts > 1 ? LineWords.Ours(PartOf(at, parts)) : null;
            var warned = Warned(sheet);
            if (at < pages)
                return new RoomCard(sheet.Title, sheet.Words, sheet.Head, status: warned, more: foot, descriptionPage: at);

            if (at == pages + factParts)
            {
                // What to check, shown, not only named: the person's own sentences that hold it, under a short line that says
                // what kind of thing it is, and each value to be seen as a name to type, in mono under its mark.
                var shown = sheet.Checks.Where(check => check.Shown != null).Take(RoomCard.MostSlots)
                    .Select(check => new RoomSlot(MarkOf(check.Kind), check.Shown!, mono: true)).ToList();
                return new RoomCard(sheet.Title, sheet.CheckWords, sheet.Head, status: warned, slots: shown, more: foot);
            }

            // Each fact its value alone, its kind's own mark saying what it is: no note that names a field (the critic, 2026-11-11).
            var factRows = sheet.Facts.Where(fact => !fact.Slot).Concat(sheet.Facts.Where(fact => fact.Slot).Skip(RoomCard.MostSlots))
                .Select(fact => new RoomListRow(fact.Mark, fact.Value, null, mono: fact.Mono)).ToList();
            var firstItems = Math.Max(0, RoomCard.ListAPage - factRows.Count);
            var page = at - pages;
            if (page == 0)
            {
                var slots = sheet.Facts.Where(fact => fact.Slot).Take(RoomCard.MostSlots).Select(fact => new RoomSlot(fact.Mark, fact.Value, fact.Mono)).ToList();
                var rows = factRows.Concat(sheet.Items.Take(firstItems)).ToList();
                return new RoomCard(sheet.Title, sheet.Line, sheet.Head, status: warned, slots: slots, list: rows, more: foot);
            }
            var later = sheet.Items.Skip(firstItems + (page - 1) * RoomCard.ListAPage).Take(RoomCard.ListAPage).ToList();
            // Every part under the same header, so paging never moves the title or changes its form (the critic, 2026-11-12).
            return new RoomCard(sheet.Title, head: sheet.Head, status: warned, list: later, more: foot);
        }

        /// <summary>The mark a value to check stands under: a web address its link.</summary>
        public static RoomKind MarkOf(ReviewCheckKind kind) => kind == ReviewCheckKind.WebAddress ? RoomKind.Link : RoomKind.None;

        /// <summary>
        /// The sentences of <paramref name="text"/>, as the person wrote them, that hold what to check: each sentence the task's
        /// warnings find something in, in order; null where none stands alone.
        /// </summary>
        public static LineWords? Naming(LineWords text)
        {
            var words = text.Text;
            var sentences = new List<string>();
            var start = 0;
            for (var index = 0; index < words.Length; index++)
            {
                var end = index == words.Length - 1 || ((words[index] == '.' || words[index] == '!' || words[index] == '?') && char.IsWhiteSpace(words[index + 1])) || words[index] == '\n';
                if (!end) continue;
                var sentence = words.Substring(start, index + 1 - start).Trim();
                if (sentence.Length > 0 && TaskWarnings.Of(sentence).Count > 0) sentences.Add(sentence);
                start = index + 1;
            }
            return sentences.Count == 0 ? null : LineWords.Theirs(string.Join(" ", sentences));
        }

        /// <summary>The web addresses <paramref name="text"/> names, each whole as written, its sentence's end left off.</summary>
        public static IReadOnlyList<string> Addresses(string text)
        {
            var found = new List<string>();
            foreach (var word in text.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries))
            {
                var bare = word.TrimEnd('.', ',', ';', ':', '!', '?', ')', '"', '\'').TrimStart('(', '"', '\'');
                if (bare.Length > 0 && TaskWarnings.Of(bare).Contains(TaskWarnings.WebAddress) && !found.Contains(bare)) found.Add(bare);
            }
            return found;
        }

        /// <summary>
        /// The review a new task's start sends (<see cref="NewWorkReview"/>), as the one sheet every command's review is read on.
        /// This is the one place that knows the start's items; it gives way to the host's general review when that lands.
        /// </summary>
        public static ReviewSheet SheetOf(NewWorkReview review)
        {
            LineWords? Value(ReviewItemKind kind)
            {
                var item = review.Items.FirstOrDefault(each => each.Kind == kind);
                return item == null || item.ValueRuns.IsEmpty ? null : item.ValueRuns;
            }
            var facts = new List<ReviewFact>();
            // The model by the exact id the start sends where it has one: the one the person reads is the one that runs.
            var model = Value(ReviewItemKind.ModelId) ?? Value(ReviewItemKind.Model);
            // A folder's name and a model's exact id are names to type, set in mono.
            if (Value(ReviewItemKind.Folder) is LineWords folder) facts.Add(new ReviewFact(RoomKind.Folder, folder, mono: true, slot: true));
            if (model != null) facts.Add(new ReviewFact(RoomKind.Agent, model, mono: Value(ReviewItemKind.ModelId) != null, slot: true));
            else if (Value(ReviewItemKind.AgentApp) is LineWords agent) facts.Add(new ReviewFact(RoomKind.Agent, agent, slot: true));
            facts.Add(new ReviewFact(RoomKind.Level, LineWords.Ours(AgentColumn.LevelWords(review.NeverAsk)), slot: true));
            if (Value(ReviewItemKind.TaskName) is LineWords name) facts.Add(new ReviewFact(RoomKind.Task, name));
            // The agent app stands as a row only where the model took its slot, so nothing sent goes unshown.
            if (model != null && Value(ReviewItemKind.AgentApp) is LineWords app) facts.Add(new ReviewFact(RoomKind.App, app));
            if (Value(ReviewItemKind.ModelLocation) is LineWords where) facts.Add(new ReviewFact(RoomKind.Computer, where));
            if (Value(ReviewItemKind.FolderNow) is LineWords before) facts.Add(new ReviewFact(RoomKind.Folder, LineWords.Ours(MovesFrom) + before));

            var task = Value(ReviewItemKind.FirstTask);
            var warnings = review.Items.Where(each => each.Kind == ReviewItemKind.Warning).Select(each => each.Label.Trim()).Where(each => each.Length > 0).Distinct().ToList();
            var checks = new List<ReviewCheck>();
            foreach (var warning in warnings)
            {
                var kind = warning == TaskWarnings.WebAddress ? ReviewCheckKind.WebAddress : warning == TaskWarnings.Command ? ReviewCheckKind.Command : ReviewCheckKind.Other;
                // An address stands only in its slot, so it is never shown twice; something to run has no slot of its own.
                var addresses = kind == ReviewCheckKind.WebAddress && task != null ? Addresses(task.Text) : new string[0];
                if (addresses.Count > 0) checks.AddRange(addresses.Select(address => new ReviewCheck(kind, LineWords.Theirs(address))));
                else checks.Add(new ReviewCheck(kind));
            }
            var command = checks.Any(check => check.Kind == ReviewCheckKind.Command);
            return new ReviewSheet(Value(ReviewItemKind.Project), RoomCardHead.Project, task ?? LineWords.Ours(""), facts, checks,
                command && task != null ? Naming(task) : null);
        }

        /// <summary>The parts of a new task's <paramref name="review"/> whose first task made <paramref name="taskPages"/> pages.</summary>
        public static int Parts(NewWorkReview review, int taskPages) => Parts(SheetOf(review), taskPages);

        /// <summary>A new task's review's part <paramref name="part"/>, as <see cref="Card(ReviewSheet, int, int)"/>.</summary>
        public static RoomCard Card(NewWorkReview review, int part = 0, int taskPages = 1) => Card(SheetOf(review), part, taskPages);
    }
}

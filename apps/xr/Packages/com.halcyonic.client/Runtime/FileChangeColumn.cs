#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>Which of a file's two readings shows: its change over the turn, or the whole file as it is now.</summary>
    public enum FileChangeStage
    {
        /// <summary>A read is out: the sweep's sentence, no card.</summary>
        Reading,

        /// <summary>The file's change over the turn the computer observed.</summary>
        Change,

        /// <summary>The whole file, as it is now.</summary>
        File,

        /// <summary>The history has no earlier version to compare with, so the change cannot be read (Whole File still can).</summary>
        NoVersion,

        /// <summary>The computer keeps no history.</summary>
        NotSetUp,

        /// <summary>A read failed or its answer could not be read.</summary>
        Unreadable,
    }

    public enum FileChangeActionKind
    {
        NextPage,
        PreviousPage,

        /// <summary>The whole file as it is now, from its change.</summary>
        WholeFile,

        TryAgain,

        /// <summary>From the whole file to the change; from the change to Changes.</summary>
        Back,
    }

    public readonly struct FileChangeAction
    {
        public FileChangeAction(FileChangeActionKind kind, string words, bool main = false)
        {
            Kind = kind;
            Words = words;
            Main = main;
        }

        public FileChangeActionKind Kind { get; }

        public string Words { get; }

        public bool Main { get; }
    }

    /// <summary>
    /// A file of a task's Changes read on the headset (the parity map's gaps 5 and 10, what Cursor, Bolt and Lovable give as
    /// the diff and the code view): its change over the turn the computer observed, every line marked added, removed or the
    /// same, then the whole file as it is now, each a card with FIELD's page of reading (<see cref="RoomReading"/>). Both are
    /// paged to FIELD's measures, each line wrapped by the reading surface's rule (<see cref="KitReading"/>: at spaces, then
    /// after a token's break marks), so nothing is cut or ellipsed; a tab shows as spaces to the next fourth column, and a
    /// line's leading spaces as its indent. Every line is the file's, theirs, never the product's words. It holds no
    /// network: the host reads through <see cref="FileChangeReader"/> and hands each answer here, and draws
    /// <see cref="Card"/> and <see cref="Actions"/>.
    /// </summary>
    public sealed class FileChangeColumn
    {
        /// <summary>The rows a page of reading shows: FIELD's (<see cref="RoomReading.RowsAPage"/>), a continued row and a gap each one.</summary>
        public const int RowsAPage = RoomReading.RowsAPage;

        /// <summary>A tab's columns.</summary>
        public const int TabColumns = 4;

        private readonly List<(RoomLineKind Kind, int? Number, string Text)> changeLines = new List<(RoomLineKind, int?, string)>();
        private readonly List<(RoomLineKind Kind, int? Number, string Text)> fileLines = new List<(RoomLineKind, int?, string)>();
        private float textScale;
        private IReadOnlyList<IReadOnlyList<(int Line, RoomReadingRow Row)>>? pages;
        private FileChangeStage changeStage = FileChangeStage.Reading;
        private ProjectFileChangeResponse? change;
        private ProjectFileResponse? file;
        private int changePage;
        private int filePage;
        private bool firstVersion;
        private bool? unreadAnswered = false;

        /// <param name="textScale">The person's text size (<see cref="Comfort.TextScale"/>): the rows wrap to <see cref="RoomReading.Columns"/> at it.</param>
        public FileChangeColumn(string projectId, RoomChange row, float textScale = 1f)
        {
            ProjectId = string.IsNullOrEmpty(projectId) ? throw new ArgumentException("A project is required.", nameof(projectId)) : projectId;
            Row = row;
            if (string.IsNullOrEmpty(row.Path)) throw new ArgumentException("A file is required.", nameof(row));
            this.textScale = textScale > 0f ? textScale : throw new ArgumentOutOfRangeException(nameof(textScale), textScale, "A text size is more than nothing.");
        }

        public string ProjectId { get; }

        /// <summary>The row of Changes this reading opened from: its kind and path.</summary>
        public RoomChange Row { get; }

        public string Path => Row.Path;

        /// <summary>The file's own name, the path's last part.</summary>
        public string Name => Path.TrimEnd('/').Split('/').Last();

        public FileChangeStage Stage { get; private set; } = FileChangeStage.Reading;

        /// <summary>Whether the whole file shows or is being read (Back then goes to the change).</summary>
        public bool Whole { get; private set; }

        /// <summary>The page of the reading shown, from 0.</summary>
        public int Page => Whole ? filePage : changePage;

        public int PageCount => Pages().Count;

        public float TextScale => textScale;

        /// <summary>What the column's sentence says while a read is out, else null: the sweep's words.</summary>
        public string? Sentence => Stage == FileChangeStage.Reading ? FileChangeText.Reading : null;

        /// <summary>
        /// The person's text size now (<see cref="Comfort.TextScale"/>): where it changed, the rows wrap again to FIELD's
        /// columns at it, and the page shown keeps its first line.
        /// </summary>
        public void AtScale(float scale)
        {
            if (scale <= 0f || Math.Abs(scale - textScale) < 1e-4f) return;
            var first = FirstLine();
            textScale = scale;
            pages = null;
            if (first is int line) SetPage(PageOfLine(line));
        }

        /// <summary>The versions around the turn, as the history answered: the change is read next, or why it cannot be.</summary>
        /// <returns>Whether the change is to be read now.</returns>
        public bool Located(TurnVersions versions)
        {
            if (Whole) return false;
            switch (versions.Found)
            {
                case TurnVersionsFound.Found:
                    Stage = changeStage = FileChangeStage.Reading;
                    return true;
                case TurnVersionsFound.NotSetUp:
                    Stage = changeStage = FileChangeStage.NotSetUp;
                    return false;
                default:
                    firstVersion = versions.Found == TurnVersionsFound.First;
                    Stage = changeStage = FileChangeStage.NoVersion;
                    return false;
            }
        }

        /// <summary>
        /// Whether a failed read had an answer: false only where no answer came (the computer could not be reached, or the
        /// read was given up), so "did not answer" and Try Again stand for that alone; any other failure (a refusal, an
        /// answer for another file, one that could not be read) had one, and its reason is not known here (AUDIT's F2).
        /// </summary>
        public static bool? Answered(Exception error) => error switch
        {
            ControlPlaneRequestException { Status: null } => false,
            OperationCanceledException or TimeoutException or System.Net.Http.HttpRequestException or System.IO.IOException => false,
            ControlPlaneRequestException { Status: not null } or FormatException or Newtonsoft.Json.JsonException => true,
            // Anything else: whether an answer came is not known, and is said so (R3).
            _ => null,
        };

        /// <summary>The file's change as the computer answered: its unified diff parsed into rows.</summary>
        public void Changed(ProjectFileChangeResponse response)
        {
            change = response ?? throw new ArgumentNullException(nameof(response));
            changeLines.Clear();
            if (!response.Binary && !response.Withheld && response.Diff != null) changeLines.AddRange(ParseDiff(response.Diff));
            changePage = 0;
            pages = null;
            changeStage = FileChangeStage.Change;
            if (!Whole) Stage = FileChangeStage.Change;
        }

        /// <summary>The whole file as the computer answered.</summary>
        public void FileRead(ProjectFileResponse response)
        {
            file = response ?? throw new ArgumentNullException(nameof(response));
            fileLines.Clear();
            if (response.Kind == ProjectFileKind.Text && response.Text is string text && text.Length > 0)
            {
                var lines = text.Replace("\r\n", "\n").Split('\n');
                var count = text.EndsWith("\n", StringComparison.Ordinal) ? lines.Length - 1 : lines.Length;
                for (var index = 0; index < count; index++) fileLines.Add((RoomLineKind.Same, index + 1, lines[index]));
            }
            filePage = 0;
            pages = null;
            if (Whole) Stage = FileChangeStage.File;
        }

        /// <summary>
        /// The read now out failed, or its answer could not be read: the change's, or with <paramref name="whole"/> the whole
        /// file's. A whole file's read that fails after Back left it says nothing (Whole File reads it again).
        /// </summary>
        /// <param name="answered">Whether the computer answered (<see cref="Answered"/>): then the card says the reason is
        /// unknown and offers no Try Again, which could not help.</param>
        public void ReadFailed(bool whole = false, bool? answered = false)
        {
            if (whole != Whole) return;
            unreadAnswered = answered;
            if (!Whole) changeStage = FileChangeStage.Unreadable;
            Stage = FileChangeStage.Unreadable;
        }

        /// <summary>Try Again: the read that failed goes out again.</summary>
        /// <returns>True where the whole file is to be read again; false where the change is.</returns>
        public bool Reread()
        {
            if (Stage != FileChangeStage.Unreadable || unreadAnswered != false) throw new InvalidOperationException("Only a read that had no answer is tried again.");
            if (!Whole) changeStage = FileChangeStage.Reading;
            Stage = FileChangeStage.Reading;
            return Whole;
        }

        /// <summary>Whole File: the whole file as it is now; true where it is to be read (it is read once).</summary>
        public bool OpenWhole()
        {
            if (!WholeOffered) return false;
            Whole = true;
            pages = null;
            if (file != null)
            {
                Stage = FileChangeStage.File;
                return false;
            }
            Stage = FileChangeStage.Reading;
            return true;
        }

        /// <summary>The reading's next page, while there is one.</summary>
        public bool NextPage()
        {
            if (!Paged || Page + 1 >= PageCount) return false;
            SetPage(Page + 1);
            return true;
        }

        /// <summary>The reading's page before, while there is one.</summary>
        public bool PreviousPage()
        {
            if (!Paged || Page == 0) return false;
            SetPage(Page - 1);
            return true;
        }

        /// <summary>Back: from the whole file to the change, where it was; false at the change, which leaves for Changes.</summary>
        public bool Back()
        {
            if (!Whole) return false;
            Whole = false;
            pages = null;
            Stage = changeStage;
            return true;
        }

        /// <summary>Whether Whole File stands: never for a file the turn removed, nor for one the computer withholds.</summary>
        public bool WholeOffered => !Whole && Row.Kind != RoomChangeKind.Removed && change?.Withheld != true
            && Stage is FileChangeStage.Change or FileChangeStage.NoVersion or FileChangeStage.NotSetUp;

        /// <summary>The rows the page shown holds: none where no reading shows.</summary>
        public IReadOnlyList<RoomReadingRow> Rows => Shown().Select(each => each.Row).ToList();

        /// <summary>
        /// The card the panel shows now, or null while a read is out (the sentence sweeps instead). The file's name is its
        /// title, its path the line under it where it has folders; the change's kind and its lines as slots; the page of
        /// reading under them, and the page's place at the foot where there are more. A file whose lines cannot be shown
        /// says why as a status.
        /// </summary>
        public RoomCard? Card()
        {
            switch (Stage)
            {
                case FileChangeStage.Reading:
                    return null;
                case FileChangeStage.Unreadable:
                    return Status(HistoryText.NotRead, unreadAnswered switch { false => HistoryText.NotReadLine, true => FileChangeText.UnknownLine, null => FileChangeText.ReasonUnknownLine },
                        RoomTone.Bad, RoomKind.None);
                case FileChangeStage.NotSetUp:
                    return Status(HistoryText.NoHistory, HistoryText.NoneKeptLine, RoomTone.Quiet, RoomKind.None);
                case FileChangeStage.NoVersion:
                    return Status(FileChangeText.NoVersion, firstVersion ? FileChangeText.NoVersionLine : FileChangeText.NotFoundLine, RoomTone.Quiet, RoomKind.None);
                case FileChangeStage.Change:
                    if (change!.Withheld) return Status(FileChangeText.KeptPrivate, FileChangeText.KeptPrivateLine, RoomTone.Quiet, RoomKind.Folder);
                    if (change.Binary || change.Diff == null) return Status(FileChangeText.NotText, null, RoomTone.Quiet, RoomKind.None);
                    // A change cut at the Mac's bound says so on its last page, in place of the page's place (the critic: "2 of 2"
                    // beside "the rest" read as a contradiction).
                    return Head(ChangeSlots(), !change.Complete && Page == PageCount - 1 ? FileChangeText.TooLong : Foot());
                default:
                    if (file!.Kind == ProjectFileKind.Withheld) return Status(FileChangeText.KeptPrivate, FileChangeText.KeptPrivateLine, RoomTone.Quiet, RoomKind.Folder);
                    if (file.Kind == ProjectFileKind.Binary) return Status(FileChangeText.NotText, null, RoomTone.Quiet, RoomKind.None);
                    if (file.Kind == ProjectFileKind.TooLarge) return Status(FileChangeText.TooLarge, FileChangeText.TooLargeLine, RoomTone.Quiet, RoomKind.File);
                    if (fileLines.Count == 0) return Status(FileChangeText.Empty, null, RoomTone.Quiet, RoomKind.File);
                    return Head(null, Foot());
            }
        }

        /// <summary>The actions the column offers now: Next Page (the main one while there is one), Previous Page, Whole File, Try Again, then Back.</summary>
        public IReadOnlyList<FileChangeAction> Actions()
        {
            var actions = new List<FileChangeAction>();
            if (Paged && Page + 1 < PageCount) actions.Add(new FileChangeAction(FileChangeActionKind.NextPage, FileChangeText.NextPage, main: true));
            if (Paged && Page > 0) actions.Add(new FileChangeAction(FileChangeActionKind.PreviousPage, FileChangeText.PreviousPage));
            if (Stage == FileChangeStage.Unreadable && unreadAnswered == false) actions.Add(new FileChangeAction(FileChangeActionKind.TryAgain, HistoryText.TryAgain, main: true));
            if (WholeOffered) actions.Add(new FileChangeAction(FileChangeActionKind.WholeFile, FileChangeText.WholeFile, main: Stage is FileChangeStage.NoVersion or FileChangeStage.NotSetUp));
            actions.Add(new FileChangeAction(FileChangeActionKind.Back, EntryText.Back));
            return actions;
        }

        /// <summary>The lines the change adds and removes, counted from its rows.</summary>
        public int Added => changeLines.Count(line => line.Kind == RoomLineKind.Added);

        public int Removed => changeLines.Count(line => line.Kind == RoomLineKind.Removed);

        /// <summary>
        /// A unified diff of one file as lines: each part's lines after its "@@" head, numbered as the file numbers them (a
        /// removed line by the file before, the rest by the file after), a gap between parts; the heads, the file's own header
        /// lines and git's "\ No newline" note are not the file's lines and are left out.
        /// </summary>
        public static IReadOnlyList<(RoomLineKind Kind, int? Number, string Text)> ParseDiff(string diff)
        {
            var rows = new List<(RoomLineKind, int?, string)>();
            int before = 0, after = 0;
            var inPart = false;
            foreach (var raw in diff.Replace("\r\n", "\n").Split('\n'))
            {
                if (raw.StartsWith("@@", StringComparison.Ordinal))
                {
                    var ended = after;
                    if (!TryHead(raw, out before, out after)) { inPart = false; continue; }
                    // A gap between parts says how many of the file's lines it skips, counted as the file is after the change.
                    if (rows.Count > 0) rows.Add((RoomLineKind.Gap, after > ended ? after - ended : (int?)null, ""));
                    inPart = true;
                    continue;
                }
                if (!inPart || raw.Length == 0) continue;
                switch (raw[0])
                {
                    case ' ':
                        rows.Add((RoomLineKind.Same, after, raw.Substring(1)));
                        before++;
                        after++;
                        break;
                    case '+':
                        rows.Add((RoomLineKind.Added, after++, raw.Substring(1)));
                        break;
                    case '-':
                        rows.Add((RoomLineKind.Removed, before++, raw.Substring(1)));
                        break;
                    case '\\':
                        break;
                    default:
                        // A line outside a part's shape (the next file's header): the part has ended.
                        inPart = false;
                        break;
                }
            }
            return rows;
        }

        /// <summary>A line with each tab as spaces to the next fourth column.</summary>
        public static string Untabbed(string line)
        {
            if (line.IndexOf('\t') < 0) return line;
            var result = new StringBuilder(line.Length + 8);
            foreach (var unit in line)
            {
                if (unit == '\t') result.Append(' ', TabColumns - result.Length % TabColumns);
                else result.Append(unit);
            }
            return result.ToString();
        }

        /// <summary>
        /// One line as rows at <paramref name="scale"/>: its leading spaces as the rows' indent (at most half a row, so words
        /// always stand), the rest wrapped to FIELD's columns less the indent, a continued row to its own (<see cref="Breaks"/>:
        /// at a space, after , ; : / ? &amp;, or before a dot or bracket that follows a name; never inside an operator or a name
        /// where any other break fits). A long line takes as many continued rows as it needs; every character but the leading
        /// spaces is kept, in order. A white space other than the space shows as its code point, as the reading surface's does.
        /// </summary>
        public static IReadOnlyList<RoomReadingRow> Wrap(RoomLineKind kind, int? number, string text, float scale)
        {
            var line = Untabbed(text);
            var words = line.TrimStart(' ');
            var columns = RoomReading.Columns(scale);
            var continuing = RoomReading.Columns(scale, continued: true);
            var indent = Math.Min(line.Length - words.Length, columns / 2);
            if (words.Length == 0) return new[] { new RoomReadingRow(kind, number, null, indent: indent) };
            // Visible bookends keep Exact's trimming from losing the line's own trailing spaces.
            var bookended = LabelText.Exact("x" + words + "x");
            var shown = bookended.Substring(1, bookended.Length - 2);
            var rows = new List<RoomReadingRow>();
            foreach (var (part, continued) in Breaks(shown, Math.Max(1, columns - indent), Math.Max(1, continuing - indent)))
                rows.Add(new RoomReadingRow(kind, number, LineWords.Shown(part), continued, indent));
            return rows;
        }

        /// <summary>The operators a row never breaks inside (FIELD's 1533Z rule).</summary>
        private static readonly string[] Operators = { "===", "!==", "=>", "==", "!=", "<=", ">=", "->", "::", "&&", "||" };

        /// <summary>
        /// <paramref name="line"/> cut into rows of at most <paramref name="first"/> characters, then <paramref name="rest"/> for
        /// each continued row (trailing spaces hang past a row's end): at the last break that fits, else where the row ends,
        /// never inside a surrogate pair or a combined character.
        /// </summary>
        public static IEnumerable<(string Part, bool Continued)> Breaks(string line, int first, int rest)
        {
            var elements = new List<string>();
            var walk = StringInfo.GetTextElementEnumerator(line);
            while (walk.MoveNext()) elements.Add((string)walk.Current);
            var from = 0;
            var continued = false;
            while (from < elements.Count)
            {
                var room = continued ? rest : first;
                // The furthest end whose row, its trailing spaces left out, holds no more than the room.
                var end = from;
                var length = 0;
                var spaces = 0;
                for (var next = from; next < elements.Count; next++)
                {
                    if (elements[next] == " ") spaces++;
                    else
                    {
                        if (length + spaces + 1 > room) break;
                        length += spaces + 1;
                        spaces = 0;
                    }
                    end = next + 1;
                }
                if (end >= elements.Count)
                {
                    yield return (string.Concat(elements.Skip(from)), continued);
                    yield break;
                }
                // A space or a break after punctuation first; before a dot or a bracket only where the row holds neither (FIELD's 1602Z).
                var at = -1;
                for (var tier = 0; tier < 2 && at < 0; tier++)
                    for (var i = end; i > from; i--)
                        if (CanBreak(elements, i, tier)) { at = i; break; }
                // No break fits: where the row ends, stepping back off an operator where one would be split.
                if (at < 0)
                    for (var i = end; i > from && at < 0; i--)
                        if (!InsideOperator(elements, i)) at = i;
                if (at < 0) at = Math.Max(from + 1, end);
                yield return (string.Concat(elements.Skip(from).Take(at - from)), continued);
                from = at;
                continued = true;
            }
        }

        private static bool NamePart(string element) => element.Length > 0 && (char.IsLetterOrDigit(element[0]) || element[0] == '_' || element[0] == '$');

        /// <summary>Whether a break before element <paramref name="i"/> would split one of <see cref="Operators"/>.</summary>
        private static bool InsideOperator(IReadOnlyList<string> elements, int i)
        {
            foreach (var op in Operators)
                for (var start = Math.Max(0, i - op.Length + 1); start < i; start++)
                    if (start + op.Length <= elements.Count && string.Concat(elements.Skip(start).Take(op.Length)) == op) return true;
            return false;
        }

        /// <summary>Whether a row may end before element <paramref name="i"/>: at <paramref name="tier"/> 0 after a space or punctuation, at 1 before a dot or bracket that follows a name.</summary>
        private static bool CanBreak(IReadOnlyList<string> elements, int i, int tier)
        {
            var before = elements[i - 1];
            var after = elements[i];
            if (after == " ") return false;
            if (InsideOperator(elements, i)) return false;
            if (tier == 0) return before == " " || before is "," or ";" or ":" or "/" or "?" or "&";
            // Before a dot or a bracket that follows a name.
            return (after == "." || after == "(" || after == "[") && NamePart(before);
        }

        private static bool TryHead(string head, out int before, out int after)
        {
            before = after = 0;
            // "@@ -12,7 +12,9 @@ context": the first number after "-" and after "+".
            var minus = head.IndexOf(" -", StringComparison.Ordinal);
            var plus = head.IndexOf(" +", StringComparison.Ordinal);
            return minus >= 0 && plus > minus && Number(head, minus + 2, out before) && Number(head, plus + 2, out after);
        }

        private static bool Number(string text, int from, out int value)
        {
            var end = from;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            return int.TryParse(text.Substring(from, end - from), NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        private bool Paged => Stage is FileChangeStage.Change or FileChangeStage.File && PageCount > 1;

        /// <summary>The page's place where the reading has more than one ("Page 1 of 2"); null for one.</summary>
        private string? Foot() => PageCount > 1 ? FileChangeText.PageOf(Page, PageCount) : null;

        /// <summary>
        /// The change's facts as slots (FIELD's 1533Z): its kind ("Edited", "New", "Removed"), then its lines where the diff came
        /// whole, the added and the removed each a slot of their own under its mark, which the kit colours.
        /// </summary>
        private IReadOnlyList<RoomSlot> ChangeSlots()
        {
            var slots = new List<RoomSlot>();
            switch (Row.Kind)
            {
                case RoomChangeKind.Made:
                    slots.Add(new RoomSlot(RoomKind.Added, LineWords.Ours(RoomPanelCards.NewNote)));
                    if (change!.Complete && Added > 0) slots.Add(new RoomSlot(RoomKind.Added, LineWords.Ours(FileChangeText.Lines(Added))));
                    break;
                case RoomChangeKind.Removed:
                    slots.Add(new RoomSlot(RoomKind.Removed, LineWords.Ours(RoomPanelCards.RemovedNote)));
                    if (change!.Complete && Removed > 0) slots.Add(new RoomSlot(RoomKind.Removed, LineWords.Ours(FileChangeText.Lines(Removed))));
                    break;
                default:
                    slots.Add(new RoomSlot(RoomKind.Changed, LineWords.Ours(RoomPanelCards.EditedNote)));
                    if (change!.Complete && Added > 0) slots.Add(new RoomSlot(RoomKind.Added, LineWords.Ours(FileChangeText.Lines(Added))));
                    if (change.Complete && Removed > 0) slots.Add(new RoomSlot(RoomKind.Removed, LineWords.Ours(FileChangeText.Lines(Removed))));
                    break;
            }
            return slots;
        }

        private RoomCard Head(IReadOnlyList<RoomSlot>? slots, string? foot) =>
            new RoomCard(LineWords.TheirName(Name), Description(), slots: slots, more: foot == null ? null : LineWords.Ours(foot), reading: Rows, titleMono: true);

        /// <summary>The path where it has folders (theirs), and on the whole file the honest "as it is now", the path last.</summary>
        private LineWords? Description()
        {
            var folders = Path.TrimEnd('/').Contains('/');
            if (!Whole) return folders ? LineWords.TheirName(Path) : null;
            return folders ? LineWords.Ours(FileChangeText.AsItIsNowBefore) + LineWords.TheirName(Path) : LineWords.Ours(FileChangeText.AsItIsNow);
        }

        /// <summary>
        /// A file whose lines do not show: the file's name first, in mono (FIELD's order wherever a card has a title), its path
        /// under it where it has folders, then why as a status with its own mark: the file's for a text file not shown, the
        /// folder's for one kept private, the default dash for the rest.
        /// </summary>
        private RoomCard Status(string title, string? line, RoomTone tone, RoomKind mark) =>
            new RoomCard(LineWords.TheirName(Name), PathLine(), status: new RoomCardStatus(title, line, tone, mark), titleMono: true);

        /// <summary>The path where it has folders, alone: a card that shows no lines has nothing to say "as it is now" of (SCREENS's critic, 2026-11-12).</summary>
        private LineWords? PathLine() => Path.TrimEnd('/').Contains('/') ? LineWords.TheirName(Path) : null;

        private IReadOnlyList<(int Line, RoomReadingRow Row)> Shown()
        {
            if (Stage is not (FileChangeStage.Change or FileChangeStage.File)) return Array.Empty<(int, RoomReadingRow)>();
            var all = Pages();
            return all.Count > 0 ? all[Math.Min(Page, all.Count - 1)] : Array.Empty<(int, RoomReadingRow)>();
        }

        private int? FirstLine()
        {
            if (pages == null) return null;
            var shown = Shown();
            return shown.Count == 0 ? (int?)null : shown[0].Line;
        }

        /// <summary>The page that holds <paramref name="line"/>'s first row, or the first page after it where the line was a gap left out.</summary>
        private int PageOfLine(int line)
        {
            var all = Pages();
            for (var page = 0; page < all.Count; page++)
                if (all[page].Count > 0 && all[page][all[page].Count - 1].Line >= line) return page;
            return Math.Max(0, all.Count - 1);
        }

        private void SetPage(int page)
        {
            if (Whole) filePage = page;
            else changePage = page;
        }

        /// <summary>
        /// The reading's pages: every line wrapped to rows, then laid in pages of <see cref="RowsAPage"/>. A line moves whole
        /// to the next page where it would cross one and fits a page; a bare gap never opens or closes a page, and a gap that counts
        /// the lines it skips opens the page of the part it leads to rather than closing one.
        /// </summary>
        private IReadOnlyList<IReadOnlyList<(int Line, RoomReadingRow Row)>> Pages()
        {
            if (pages != null) return pages;
            var all = new List<List<(int, RoomReadingRow)>>();
            var current = new List<(int, RoomReadingRow)>();
            var lines = Whole ? fileLines : changeLines;
            for (var line = 0; line < lines.Count; line++)
            {
                var (kind, number, text) = lines[line];
                // One gutter, the file's numbers as it is after the change: a removed line has none (the critic: the old and new
                // numbers in one column read as a mistake); its minus says what it is.
                var rows = kind == RoomLineKind.Gap ? new[] { RoomReadingRow.Gap(number ?? 0) } : Wrap(kind, kind == RoomLineKind.Removed ? null : number, text, textScale);
                if (kind == RoomLineKind.Gap && number is int skipped && skipped > 0)
                {
                    // A gap that says how many lines it skips is part of what follows it (SCREENS's critic, 2026-11-12: at a page's
                    // edge it was dropped, so "36 Lines" never showed): it opens the next page with the part it leads to, rather
                    // than closing this one or being left out.
                    var next = line + 1 < lines.Count ? Wrap(lines[line + 1].Kind, lines[line + 1].Number, lines[line + 1].Text, textScale).Count : 0;
                    if (current.Count > 0 && current.Count + 1 + (next <= RowsAPage - 1 ? next : 1) > RowsAPage)
                    {
                        all.Add(current);
                        current = new List<(int, RoomReadingRow)>();
                    }
                }
                else if (kind == RoomLineKind.Gap && (current.Count == 0 || current.Count >= RowsAPage)) continue;
                if (current.Count > 0 && current.Count + rows.Count > RowsAPage && rows.Count <= RowsAPage)
                {
                    all.Add(current);
                    current = new List<(int, RoomReadingRow)>();
                }
                foreach (var row in rows)
                {
                    if (current.Count >= RowsAPage)
                    {
                        all.Add(current);
                        current = new List<(int, RoomReadingRow)>();
                    }
                    current.Add((line, row));
                }
            }
            if (current.Count > 0) all.Add(current);
            // A gap left last on a page leads nowhere: it goes (a counted one never ends a page, above).
            foreach (var page in all)
                if (page.Count > 0 && page[page.Count - 1].Item2.Kind == RoomLineKind.Gap) page.RemoveAt(page.Count - 1);
            pages = all.Where(page => page.Count > 0).Select(page => (IReadOnlyList<(int, RoomReadingRow)>)page).ToList();
            return pages;
        }
    }

    /// <summary>A file's reading in the product's own words (WORDS.md; the lead's yes and FIELD's words of 2026-11-11).</summary>
    public static class FileChangeText
    {
        public const string WholeFile = "Whole File";
        public const string Reading = "Reading File";
        public const string NextPage = "Next Page";
        public const string PreviousPage = "Previous Page";
        public const string NoVersion = "Nothing to Compare";
        public const string NoVersionLine = "The computer kept no earlier version of this file";

        /// <summary>Where no version of the turn was found among those read (the critics' words, for FIELD to word).</summary>
        public const string NotFoundLine = "The earlier version was not found";

        /// <summary>A read the computer answered but that could not be shown (a refusal, an answer for another file); for FIELD to word.</summary>
        public const string UnknownLine = "The computer's reply can't be shown";

        /// <summary>A failed read of which it is not known whether the computer answered (R3: unknown, in plain words).</summary>
        public const string ReasonUnknownLine = "Reason unknown";
        public const string KeptPrivate = "Kept Private";
        public const string KeptPrivateLine = "Its name says it may hold secrets";
        public const string NotText = "Not Text";
        public const string TooLarge = "Too Large to Show";
        public const string TooLargeLine = "Over 256 KB";
        public const string Empty = "Empty File";
        public const string TooLong = "The rest is too long to show here";
        public const string AsItIsNow = "As it is now.";

        /// <summary>Before the path, on the whole file's line: the path stands last, so nothing of ours follows theirs.</summary>
        public const string AsItIsNowBefore = "As it is now:";

        /// <summary>"Page 1 of 2", from page 0 of <paramref name="pages"/> (FIELD's foot line).</summary>
        public static string PageOf(int page, int pages) =>
            "Page " + (page + 1).ToString(CultureInfo.InvariantCulture) + " of " + pages.ToString(CultureInfo.InvariantCulture);

        /// <summary>"1 Line", "4 Lines".</summary>
        public static string Lines(int lines) => lines == 1 ? "1 Line" : lines.ToString(CultureInfo.InvariantCulture) + " Lines";
    }
}

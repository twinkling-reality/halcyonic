#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// What kind of fact or file a card's slot or list row is (the addendum's A17): the view draws each kind as its own
    /// glyph in a tile, so a fact never needs its label written beside its value.
    /// </summary>
    public enum RoomKind
    {
        /// <summary>No kind: a dash.</summary>
        None,

        /// <summary>What a project starts from: the headset.</summary>
        Starter,

        /// <summary>The agent or model: its orb.</summary>
        Agent,

        /// <summary>The level it runs at: the shield.</summary>
        Level,

        /// <summary>The project's folder.</summary>
        Folder,

        /// <summary>A check that held, a step done: the check.</summary>
        Passed,

        /// <summary>A check that did not: the cross.</summary>
        Failed,

        /// <summary>A check not run: the dash.</summary>
        NotRun,

        /// <summary>Files waiting to be added to the project: the tray.</summary>
        ToAdd,

        /// <summary>A file changed: the modified mark.</summary>
        Changed,

        /// <summary>A file made new, or brought back: the plus.</summary>
        Added,

        /// <summary>A file removed: the minus.</summary>
        Removed,

        /// <summary>A file changed in both places: the crossing arrows.</summary>
        Conflict,

        /// <summary>A moment before a task started: the pin.</summary>
        Started,

        /// <summary>A moment after a task worked: the flag.</summary>
        Worked,

        /// <summary>A file that goes back to how it was: the arrow turning back.</summary>
        Reverts,

        /// <summary>The computer an agent runs on.</summary>
        Computer,

        /// <summary>The agent uses tools.</summary>
        Tools,

        /// <summary>The app an agent runs in.</summary>
        App,

        /// <summary>A usage window of hours: the stopwatch.</summary>
        Session,

        /// <summary>A usage window of days: the calendar.</summary>
        Week,

        /// <summary>A way to share by a link: the chain.</summary>
        Link,

        /// <summary>A way to share on the Wi-Fi: its arcs.</summary>
        Wifi,
    }

    /// <summary>How a card's status, step or row stands: its colour.</summary>
    public enum RoomTone
    {
        /// <summary>Nothing to say of it yet: a check waiting to run, a fact.</summary>
        Quiet,
        Good,
        Bad,
        Working,

        /// <summary>It never ran, and will not: the run ended first. A dim, empty mark.</summary>
        Skipped,

        /// <summary>It needs the person (it asks them something): the attention colour, amber, never the main action's accent.</summary>
        Attention,
    }

    /// <summary>What heads a card beside its title: nothing, the project's emblem, the result's picture, or the agent's orb.</summary>
    public enum RoomCardHead
    {
        None,
        Project,
        Picture,
        Agent,
    }

    /// <summary>One fact of a card's loadout: the glyph of its kind in a tile, its value under it, and no label.</summary>
    public readonly struct RoomSlot
    {
        public RoomSlot(RoomKind kind, LineWords value)
        {
            Kind = kind;
            Value = value;
        }

        public RoomKind Kind { get; }

        public LineWords Value { get; }
    }

    /// <summary>
    /// A code to scan, as the host made it (a live share's, ADR 0054): its cells a side and which are dark, row by row
    /// from the top left. The kit only draws it, dark cells on a light well with a quiet margin of
    /// <see cref="MarginCells"/> cells round it, never reads or makes one. The host's cells come without that margin.
    /// </summary>
    public sealed class RoomCode
    {
        /// <param name="side">The cells a side, <see cref="FewestSide"/> to <see cref="MostSide"/>.</param>
        /// <param name="dark">Side times side cells, row by row from the top left, true where a cell is dark.</param>
        public RoomCode(int side, IReadOnlyList<bool> dark)
        {
            if (side < FewestSide || side > MostSide)
                throw new System.ArgumentOutOfRangeException(nameof(side), side, "A code to scan is " + FewestSide + " to " + MostSide + " cells a side.");
            if (dark == null) throw new System.ArgumentNullException(nameof(dark));
            if (dark.Count != side * side)
                throw new System.ArgumentException("A code " + side + " cells a side has " + side * side + " cells, not " + dark.Count + ".", nameof(dark));
            Side = side;
            Dark = dark.ToArray();
        }

        /// <summary>The fewest and the most cells a side a code to scan has.</summary>
        public const int FewestSide = 21;

        public const int MostSide = 177;

        /// <summary>The light cells the kit draws round the code on every side, so a camera finds its edge.</summary>
        public const int MarginCells = 4;

        public int Side { get; }

        public IReadOnlyList<bool> Dark { get; }

        /// <summary>Whether the cell <paramref name="column"/> across and <paramref name="row"/> down from the top left is dark.</summary>
        public bool IsDark(int column, int row) => Dark[row * Side + column];
    }

    /// <summary>A card's status: a title word, and the quieter line under it, in its tone.</summary>
    public readonly struct RoomCardStatus
    {
        public RoomCardStatus(string title, string? line, RoomTone tone)
        {
            Title = title;
            Line = line;
            Tone = tone;
        }

        public string Title { get; }

        public string? Line { get; }

        public RoomTone Tone { get; }
    }

    /// <summary>One step of a card's row of steps (the checks): its name under its mark, and where the agent tried to fix it, its tries as pips hung on the mark.</summary>
    public readonly struct RoomCardStep
    {
        public RoomCardStep(string name, RoomTone tone, int triesUsed = 0, int triesOf = 0)
        {
            Name = name;
            Tone = tone;
            TriesOf = triesOf < 0 ? 0 : triesOf;
            TriesUsed = triesUsed < 0 ? 0 : triesUsed > TriesOf ? TriesOf : triesUsed;
        }

        /// <summary>How many tries the step has, and how many were used: pips like a game's lives; none where no try was made.</summary>
        public int TriesOf { get; }

        public int TriesUsed { get; }

        public string Name { get; }

        public RoomTone Tone { get; }
    }

    /// <summary>
    /// One row of a card's list: the glyph of its kind in a tile, its name, and a quiet note at the right; never a dot
    /// between them. A row that can be pressed carries the file's own control.
    /// </summary>
    public sealed class RoomListRow
    {
        public RoomListRow(RoomKind kind, LineWords name, string? note = null, RoomTone tone = RoomTone.Quiet, bool mono = false, KitControl? press = null,
            bool chosen = false)
        {
            Kind = kind;
            Name = name;
            Note = note;
            Tone = tone;
            Mono = mono;
            Press = press;
            Chosen = chosen;
        }

        public RoomKind Kind { get; }

        public LineWords Name { get; }

        /// <summary>The note at the right: a state ("Changed"), a time, a count. Null for none.</summary>
        public string? Note { get; }

        public RoomTone Tone { get; }

        /// <summary>The name is a file's path, set in the mono face.</summary>
        public bool Mono { get; }

        public KitControl? Press { get; }

        public bool Chosen { get; }
    }

    /// <summary>One gauge of a card: its window's kind, how much is left, the window's word, and when it resets.</summary>
    public readonly struct RoomGauge
    {
        /// <param name="label">The window's word, as runs: the provider's own label is theirs and leans (AUDIT's F1, 2026-11-11).</param>
        public RoomGauge(LineWords label, int percentLeft, string? note, RoomKind kind = RoomKind.Session)
        {
            Kind = kind;
            Label = label;
            PercentLeft = percentLeft < 0 ? 0 : percentLeft > 100 ? 100 : percentLeft;
            Note = note;
        }

        /// <summary>The window's kind, its glyph: a session of hours, or a week.</summary>
        public RoomKind Kind { get; }

        public LineWords Label { get; }

        public int PercentLeft { get; }

        public string? Note { get; }
    }

    /// <summary>
    /// The information panel as a card (the addendum's A17, the owner's rejection of facts strung on dots, 2026-11-11): a
    /// thing's name is its title, with a line of description; its facts are slots, each a kind's glyph over its value; a
    /// status is a title word over a quieter line; tries are pips; a list's rows are a kind's glyph, the name, and a quiet
    /// note at the right. Every part is optional, and a card holds no words joined on a dot (test <see cref="AnyDot"/>).
    ///
    /// Which parts may hold outside words (a name, a path, an agent's or a companion's words) and so are runs
    /// (<see cref="LineWords"/>, theirs leaning, never drawn as the product's own; AUDIT's F1, 2026-11-11): the title, the
    /// description, each slot's value, each list row's name, the foot line (<see cref="More"/>), and each gauge's label.
    /// Plain strings, which are always the product's own words: the status's title and line, each list row's note, each
    /// step's name, each gauge's note. A card that needs an outside voice in one of those makes that part runs first
    /// (ask FIELD).
    /// </summary>
    public sealed class RoomCard
    {
        public RoomCard(LineWords? title = null, LineWords? description = null, RoomCardHead head = RoomCardHead.None, RoomCardStatus? status = null,
            IReadOnlyList<RoomSlot>? slots = null, IReadOnlyList<RoomCardStep>? steps = null, int? trackAt = null, int? trackOf = null,
            IReadOnlyList<RoomListRow>? list = null, IReadOnlyList<RoomGauge>? gauges = null, LineWords? more = null, RoomCode? code = null,
            IReadOnlyList<RoomReadingRow>? reading = null)
        {
            Code = code;
            Reading = reading ?? new RoomReadingRow[0];
            Title = title;
            Description = description;
            Head = head;
            Status = status;
            Slots = slots ?? new RoomSlot[0];
            Steps = steps ?? new RoomCardStep[0];
            TrackOf = trackOf is int of && of > 0 ? of : (int?)null;
            TrackAt = TrackOf == null ? null : trackAt;
            List = list ?? new RoomListRow[0];
            Gauges = gauges ?? new RoomGauge[0];
            More = more == null || more.IsEmpty ? null : more;
        }

        /// <summary>The slots a loadout holds at most: more do not fit the panel's width.</summary>
        public const int MostSlots = 3;

        /// <summary>The rows a list shows at most on one page.</summary>
        public const int ListAPage = 5;

        public LineWords? Title { get; }

        public LineWords? Description { get; }

        public RoomCardHead Head { get; }

        public RoomCardStatus? Status { get; }

        public IReadOnlyList<RoomSlot> Slots { get; }

        /// <summary>
        /// A code to scan, the card's centre: laid under the line and the gauges, over the slots, as wide as the card allows.
        /// It holds no words. Null for none.
        /// </summary>
        public RoomCode? Code { get; }

        /// <summary>
        /// A page of a file's lines (<see cref="RoomReading"/>): each its number, its change's mark and its words, theirs and
        /// mono, laid under the slots. A screen wraps and pages them to <see cref="RoomReading.Columns"/> and
        /// <see cref="RoomReading.RowsAPage"/>. Empty for none.
        /// </summary>
        public IReadOnlyList<RoomReadingRow> Reading { get; }

        public IReadOnlyList<RoomCardStep> Steps { get; }

        /// <summary>
        /// The tries of a status card whose steps are not known: how many there are, and how many were used or are under way
        /// (from 1). Drawn as pips hung on the status's mark, like a game's lives, the used ones filled. Null where there are none.
        /// </summary>
        public int? TrackOf { get; }

        public int? TrackAt { get; }

        public IReadOnlyList<RoomListRow> List { get; }

        public IReadOnlyList<RoomGauge> Gauges { get; }

        /// <summary>
        /// A quiet line at the card's foot: a notice, a companion's proposal, "And 4 Files More". As runs, so an outside
        /// voice's words (a companion's, an agent's) stay theirs and lean, never drawn as the product's own (AUDIT's F1,
        /// 2026-11-11). Null for none.
        /// </summary>
        public LineWords? More { get; }

        /// <summary>
        /// Every word the card shows, in its order: what a view that draws no card shows, and what a check of the
        /// separator rule reads.
        /// </summary>
        public IEnumerable<string> Words() => Words(reading: true);

        private IEnumerable<string> Words(bool reading)
        {
            if (Status is RoomCardStatus status)
            {
                yield return status.Title;
                if (status.Line != null) yield return status.Line;
            }
            if (Title != null) yield return Title.Text;
            if (Description != null) yield return Description.Text;
            foreach (var gauge in Gauges)
            {
                yield return gauge.Label.Text;
                if (gauge.Note != null) yield return gauge.Note;
            }
            foreach (var slot in Slots) yield return slot.Value.Text;
            if (reading)
                foreach (var row in Reading)
                    if (row.Text != null) yield return row.Text.Text;
            foreach (var step in Steps) yield return step.Name;
            foreach (var row in List)
            {
                yield return row.Name.Text;
                if (row.Note != null) yield return row.Note;
            }
            if (More != null) yield return More.Text;
        }

        /// <summary>
        /// Whether any of the card's words joins two facts on the separator: never, in the card's language. A file's own lines
        /// are not the card's facts and may hold any character, so a page of reading is not read for it.
        /// </summary>
        public bool AnyDot => Words(reading: false).Any(words => words.Contains(KitMomentWords.Dot.Trim()));
    }
}

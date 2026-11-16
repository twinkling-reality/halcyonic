#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>Measured local shelf controls. The visible current draw alone authorizes each preview movement or Apply.</summary>
    public sealed class AgentShelfColumn : IMenuColumn, IPlacesInRoom
    {
        public const string Apply = "shelf-apply";
        public const string Move = "shelf-move";
        public const string ReadRoom = "shelf-room";
        public const string Subject = "Place Agents";
        /// <summary>The turn rows' words, which are also the direction RoomPlacement.MovePreview turns by: one source for both.</summary>
        public const string TurnLeft = "Turn Left", TurnRight = "Turn Right";
        private static readonly string[] Directions = { "Left", "Right", "Closer", "Farther", "Up", "Down", TurnLeft, TurnRight };
        private readonly IMenuHost host;
        private readonly AgentShelfDraft draft;
        private readonly Action<string> move;
        private readonly Action apply;
        private readonly Action cancel;
        private readonly Action readRoom;
        private readonly string? taskTitle;
        private readonly Func<bool>? taskCurrent;
        private MenuFrame? frame;
        private MenuFrame? drawn;
        private int page;
        private bool ended;

        /// <inheritdoc/>
        public bool ShelfApplied { get; private set; }
        public AgentShelfColumn(IMenuHost host, AgentShelfDraft draft, Action<string> move, Action apply, Action cancel, Action readRoom, string? taskTitle = null, Func<bool>? taskCurrent = null)
        {
            this.taskTitle = taskTitle; this.taskCurrent = taskCurrent;
            this.host = host; this.draft = draft; this.move = move; this.apply = apply; this.cancel = cancel; this.readRoom = readRoom;
            draft.Changed += Refresh;
        }
        public bool Active => !ended && draft.Candidate != null && taskCurrent?.Invoke() != false;
        public bool CurrentDraw => Active && drawn != null && ReferenceEquals(drawn, frame);
        public event Action? Changed;
        public event Action? Closed;
        public MenuFrame? Frame
        {
            get
            {
                if (ended || draft.Candidate == null) return null;
                if (frame != null) return frame;
                var subject = taskTitle ?? Subject;
                var room = host.PageHeight(host.TitleRows(subject, Glaze.Menu.FileColumnDegrees), false, focused: true);
                // Two instructions, two lines, never joined on a dot (the owner's 2026-11-11; the lead's 21:11Z), the words as they
                // were: each a line of its own, which every reader draws (an under line some readers drop: AUDIT's 21:14Z).
                var (first, then) = taskTitle == null ? ("Hold Move Shelf to drag it.", "Apply keeps this place for now.")
                    // Sentences, as sentences read (AUDIT2 2026-11-16 17:48Z), never labels' Title Case.
                    : ("Apply shows this task for now.", "Cancel keeps the shelf as it is.");
                var note = new PageLine(first, rows: host.RowsOf(first, Glaze.Menu.FileColumnDegrees), tone: LineTone.Secondary);
                var noteThen = new PageLine(then, rows: host.RowsOf(then, Glaze.Menu.FileColumnDegrees), tone: LineTone.Secondary);
                var used = MenuPage.Words(host.RowsOf(note, Glaze.Menu.FileColumnDegrees) + host.RowsOf(noteThen, Glaze.Menu.FileColumnDegrees)) + MenuPage.TargetGap;
                // Under the redesign the general reader pages the moves, keeping each pair on one page (the lead's 21:11Z): the frame
                // hands them all at once rather than cutting them by the older view's room.
                var count = RoomDesign.Enabled ? Directions.Length + 1
                    : Math.Max(1, (int)Math.Floor((room - used + MenuPage.TargetGap) / (MenuPage.Target() + MenuPage.TargetGap)));
                var pages = (Directions.Length + 1 + count - 1) / count;
                page %= pages;
                var lines = new List<PageLine> { note, noteThen };
                for (var index = page * count; index < Math.Min(Directions.Length + 1, (page + 1) * count); index++)
                    lines.Add(index == Directions.Length
                        ? new PageLine("Find Room Surface", action: ReadRoom, control: LineControl.Action).WithGroup("find")
                        // Moves a person uses together stay on one page (the lead's 21:11Z): each pair, Left and Right, Closer and
                        // Farther, Up and Down, the two turns, is one group, which the reader never breaks.
                        : new PageLine(Directions[index], action: Move, key: Directions[index], control: LineControl.Action).WithGroup("pair-" + index / 2));
                return frame = new MenuFrame(subject, new Footer(new Prompt(Footer.Close, "Cancel", GlazeIcon.Close, PromptKind.Close),
                    secondary: pages > 1 ? new Prompt(Footer.NextPage, Footer.NextPageWords(page, pages), GlazeIcon.Next, PromptKind.NextPage) : null,
                    farRight: new Prompt(Apply, "Apply", GlazeIcon.Change, main: true)), lines: lines, subjectIsData: taskTitle != null).Focus();
            }
        }
        public void Act(string id, string? key)
        {
            if (ended || drawn == null || !ReferenceEquals(drawn, frame)) return;
            if (id == Footer.Close) { End(false); return; }
            if (taskCurrent?.Invoke() == false) { End(false); return; }
            if (id == Apply && drawn.Footer.All.Any(prompt => prompt.Prompt.Id == Apply)) { End(true); return; }
            if (id == Footer.NextPage && drawn.Footer.All.Any(prompt => prompt.Prompt.Id == Footer.NextPage)) { page++; Refresh(); return; }
            if (id == ReadRoom && drawn.Lines.Any(line => line.Action == ReadRoom)) { readRoom(); Refresh(); return; }
            if (id == Move && key != null && drawn.Lines.Any(line => line.Action == Move && line.Key == key)) move(key);
        }
        private void Refresh() { frame = null; drawn = null; if (!ended) Changed?.Invoke(); }
        private void End(bool commit, bool notify = true)
        {
            if (ended) return;
            ended = true; frame = null; drawn = null; draft.Changed -= Refresh;
            ShelfApplied = commit;
            if (commit) apply(); else cancel();
            if (notify) Closed?.Invoke();
        }
        public void Drawn(MenuFrame offered, Footer? sideFooter) { if (!ended && ReferenceEquals(offered, frame) && sideFooter == null) drawn = offered; }
        public void FocusLeft() => End(false, notify: false);
        public void Tick() { if (!ended && taskCurrent?.Invoke() == false) End(false); }
        public void HoldStarted(string id) { }
        public void HoldEnded(string id, bool letGo) { }
        public void Heard(string words) { }
        public void Said(string words) { }
    }
}

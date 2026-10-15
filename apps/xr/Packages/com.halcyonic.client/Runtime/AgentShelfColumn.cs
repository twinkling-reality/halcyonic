#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>Measured local shelf controls. The visible current draw alone authorizes each preview movement or Apply.</summary>
    public sealed class AgentShelfColumn : IMenuColumn
    {
        public const string Apply = "shelf-apply";
        public const string Move = "shelf-move";
        public const string ReadRoom = "shelf-room";
        public const string Subject = "Place agents";
        private static readonly string[] Directions = { "Left", "Right", "Closer", "Farther", "Up", "Down", "Turn left", "Turn right" };
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
                var explanation = taskTitle == null ? "Hold Move shelf to drag its preview. Apply places it for this session."
                    : "Apply shows this task for this session. Cancel keeps your shelf. Hold Move shelf to drag.";
                var note = new PageLine(explanation, rows: host.RowsOf(explanation, Glaze.Menu.FileColumnDegrees), tone: LineTone.Secondary);
                var used = MenuPage.Words(host.RowsOf(note, Glaze.Menu.FileColumnDegrees)) + MenuPage.TargetGap;
                var count = Math.Max(1, (int)Math.Floor((room - used + MenuPage.TargetGap) / (MenuPage.Target() + MenuPage.TargetGap)));
                var pages = (Directions.Length + 1 + count - 1) / count;
                page %= pages;
                var lines = new List<PageLine> { note };
                for (var index = page * count; index < Math.Min(Directions.Length + 1, (page + 1) * count); index++)
                    lines.Add(index == Directions.Length
                        ? new PageLine("Find a room surface", action: ReadRoom, control: LineControl.Action)
                        : new PageLine(Directions[index], action: Move, key: Directions[index], control: LineControl.Action));
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

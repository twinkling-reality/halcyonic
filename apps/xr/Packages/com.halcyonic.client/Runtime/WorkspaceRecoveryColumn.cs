#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>Explicit inspection of saved ordinary drafts, including drafts whose original execution has ended.</summary>
    public sealed class WorkspaceRecoveryColumn : IMenuColumn
    {
        public const string OpenDraft = "workspace-open-saved-draft";
        public const string RestoreDraft = "workspace-restore-saved-draft";
        public const string KeepReturn = "workspace-keep-native-return";
        private readonly IMenuHost host;
        private readonly IReadOnlyList<WorkspaceContinuation> records;
        private readonly Func<WorkspaceContinuation, bool> current;
        private readonly Func<WorkspaceContinuation, bool> restore;
        private readonly Func<WorkspaceContinuation, bool>? keepReturn;
        private readonly string? pendingReturnId;
        private readonly Func<WorkspaceContinuation, bool>? saved;
        private int selected = -1;
        private int part;
        private MenuFrame? given;
        private MenuFrame? drawn;
        private DateTimeOffset? drawnAt;
        public WorkspaceRecoveryColumn(IMenuHost host, IReadOnlyList<WorkspaceContinuation> records,
            Func<WorkspaceContinuation, bool> current, Func<WorkspaceContinuation, bool> restore,
            Func<WorkspaceContinuation, bool>? keepReturn = null, string? pendingReturnId = null, Func<WorkspaceContinuation, bool>? saved = null)
        { this.host = host; this.records = records; this.current = current; this.restore = restore; this.keepReturn = keepReturn; this.pendingReturnId = pendingReturnId; this.saved = saved; }
        public event Action? Changed;
        public event Action? Closed;
        public MenuFrame? Frame
        {
            get
            {
                var close = new Prompt(Footer.Close, selected < 0 ? "Back to workspace" : "Back to saved drafts", GlazeIcon.Back, PromptKind.Close);
                if (selected < 0)
                {
                    var lines = new List<PageLine>();
                    if (records.Count == 0) lines.Add(new PageLine("No saved task views."));
                    else
                    {
                        part %= records.Count;
                        var record = records[part];
                        var title = record.WorkstreamId != null && host.State?.Workstreams.TryGetValue(record.WorkstreamId, out var task) == true
                            ? LineWords.TheirName(task.Title) : LineWords.Ours("Saved task view");
                        lines.Add(new PageLine(title, icon: GlazeIcon.Next, action: OpenDraft, key: part.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                        lines.Add(new PageLine((record.Instruction != null && record.Request != null ? "Instruction and request drafts" : record.Instruction != null ? "Instruction draft" : record.Request != null ? "Request draft" : "View only")
                            + (saved?.Invoke(record) == false ? " · Not saved" : ""), tone: LineTone.Secondary));
                    }
                    return given = new MenuFrame("Saved drafts", new Footer(close, farRight: records.Count > 1
                        ? new Prompt(Footer.NextPage, Footer.NextPageWords(part, records.Count), GlazeIcon.Next, PromptKind.NextPage) : null), lines: lines).Focus();
                }
                var chosen = records[selected];
                var runs = LineWords.Ours((current(chosen) ? "Current task and run. " : "Earlier or unavailable task and run. ")
                    + (saved?.Invoke(chosen) == false ? "Not saved. " : ""));
                if (chosen.Instruction != null) runs = runs.Then(LineWords.Written("\nInstruction draft\n")).Then(LineWords.Theirs(chosen.Instruction.Text));
                if (chosen.Request != null) runs = runs.Then(LineWords.Written("\nRequest draft\n")).Then(LineWords.Theirs(chosen.Request.Text))
                    .Then(LineWords.Ours(chosen.Request.PendingCommandId != null ? "\nPending result. Restore to check." : chosen.Request.RequestId == null ? "\nNot submitted." : "\nSubmitted request reference retained. Restore to read its recorded result."));
                if (chosen.Instruction == null && chosen.Request == null) runs = runs.Then(LineWords.Ours("This saved task view has no draft."));
                var rows = Math.Max(1, host.RowsOf(new PageLine(runs, rows: int.MaxValue), Glaze.Menu.FileColumnDegrees));
                var room = Math.Max(1, (int)Math.Floor(host.PageHeight(host.TitleRows("Saved task view", Glaze.Menu.FileColumnDegrees), false, focused: true) / MenuPage.Words(1)));
                var parts = Math.Max(1, (rows + room - 1) / room);
                part %= parts;
                var next = parts > 1 ? new Prompt(Footer.NextPage, Footer.NextPageWords(part, parts), GlazeIcon.Next, PromptKind.NextPage) : null;
                var restorePrompt = current(chosen) ? new Prompt(RestoreDraft, "Restore view", GlazeIcon.Next) : null;
                var keep = keepReturn != null && chosen.HandoffId == pendingReturnId ? new Prompt(KeepReturn, "Keep draft", GlazeIcon.Next) : null;
                return given = new MenuFrame("Saved task view", new Footer(close, rare: restorePrompt ?? keep, farRight: next),
                    lines: new[] { new PageLine(runs, rows: Math.Min(room, rows - part * room), fromRow: part * room) }).Focus();
            }
        }
        public void Act(string id, string? key)
        {
            if (drawn == null || drawn != given) return;
            if (id == Footer.Close)
            { if (selected < 0) Closed?.Invoke(); else { selected = -1; part = 0; Invalidate(); } return; }
            if (id == OpenDraft && int.TryParse(key, out var index) && index == part && index >= 0 && index < records.Count)
            { selected = index; part = 0; Invalidate(); return; }
            if (drawnAt == null || host.Clock - drawnAt < FileScreen.TurnGuard) return;
            if (id == Footer.NextPage && drawn.Footer[PromptSlot.FarRight]?.Id == Footer.NextPage) { part++; Invalidate(); return; }
            if (selected < 0) return;
            if (id == RestoreDraft && drawn.Footer.All.Any(prompt => prompt.Prompt.Id == RestoreDraft) && current(records[selected]) && restore(records[selected])) Closed?.Invoke();
            if (id == KeepReturn && drawn.Footer.All.Any(prompt => prompt.Prompt.Id == KeepReturn) && keepReturn?.Invoke(records[selected]) == true) Closed?.Invoke();
        }
        private void Invalidate() { given = null; drawn = null; drawnAt = null; Changed?.Invoke(); }
        public void Drawn(MenuFrame frame, Footer? sideFooter) { if (frame == given && sideFooter == null) { drawn = frame; drawnAt ??= host.Clock; } }
        public void FocusLeft() => Invalidate();
        public void Tick() { }
        public void HoldStarted(string id) { }
        public void HoldEnded(string id, bool letGo) { }
        public void Heard(string words) { }
        public void Said(string words) { }
    }
}

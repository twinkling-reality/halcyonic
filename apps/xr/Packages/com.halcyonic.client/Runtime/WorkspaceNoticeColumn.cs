#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>A measured local explanation. It owns no command, draft or decision authority.</summary>
    public sealed class WorkspaceNoticeColumn : IMenuColumn
    {
        private readonly IMenuHost host;
        private readonly LineWords words;
        private int part;
        private MenuFrame? given;
        private MenuFrame? drawn;
        private DateTimeOffset? drawnAt;
        public WorkspaceNoticeColumn(IMenuHost host, string words) { this.host = host; this.words = LineWords.Ours(words); }
        public event Action? Changed;
        public event Action? Closed;
        public MenuFrame? Frame
        {
            get
            {
                var rows = Math.Max(1, host.RowsOf(new PageLine(words, rows: int.MaxValue), Glaze.Menu.FileColumnDegrees));
                var available = Math.Max(1, (int)Math.Floor(host.PageHeight(host.TitleRows(WorkspaceText.WhatHappened, Glaze.Menu.FileColumnDegrees), false, focused: true) / MenuPage.Words(1)));
                var parts = Math.Max(1, (rows + available - 1) / available);
                part %= parts;
                return given = new MenuFrame(WorkspaceText.WhatHappened,
                    new Footer(new Prompt(Footer.Close, "Back to workspace", GlazeIcon.Back, PromptKind.Close),
                        farRight: parts > 1 ? new Prompt(Footer.NextPage, Footer.NextPageWords(part, parts), GlazeIcon.Next, PromptKind.NextPage) : null),
                    lines: new[] { new PageLine(words, rows: Math.Min(available, rows - part * available), fromRow: part * available) }).Focus();
            }
        }
        public void Act(string id, string? key)
        {
            if (drawn == null || drawn != given) return;
            if (id == Footer.Close) { Closed?.Invoke(); return; }
            if (id == Footer.NextPage && drawnAt != null && host.Clock - drawnAt >= FileScreen.TurnGuard && drawn.Footer[PromptSlot.FarRight]?.Id == Footer.NextPage)
            { part++; Invalidate(); }
        }
        private void Invalidate() { drawn = null; given = null; drawnAt = null; Changed?.Invoke(); }
        public void Drawn(MenuFrame frame, Footer? sideFooter) { if (frame == given && sideFooter == null) { drawn = frame; drawnAt ??= host.Clock; } }
        public void FocusLeft() => Invalidate();
        public void Tick() { }
        public void HoldStarted(string id) { }
        public void HoldEnded(string id, bool letGo) { }
        public void Heard(string words) { }
        public void Said(string words) { }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>Local workspace movement help and Reset position. It makes no claims about another app's window.</summary>
    public sealed class PlacementColumn : IMenuColumn
    {
        public const string Reset = "placement-reset";
        public const string Subject = "Move";
        public const string ReadTitle = "placement-read-title";
        public const string Back = "placement-back";
        public const string HoldInstruction = "Hold Move in the workspace header and drag to reposition it. Reset position brings it back in front of you.";
        private readonly IMenuHost host;
        private readonly Action reset;
        private readonly string? title;
        private bool readingTitle;
        private int titlePage;
        private MenuFrame? given;
        private MenuFrame? drawn;
        public PlacementColumn(IMenuHost host, Action reset, string? title = null)
        {
            this.host = host;
            this.reset = reset;
            this.title = title;
        }
        public bool IsDrawn(MenuFrame? frame) => frame != null && frame == drawn && frame == given;
        public event Action? Changed;
        public event Action? Closed;
        public MenuFrame? Frame
        {
            get
            {
                var context = new List<PageLine> { new PageLine(HoldInstruction, rows: host.RowsOf(HoldInstruction, Glaze.Menu.FileColumnDegrees), tone: LineTone.Secondary) };
                if (readingTitle && title != null)
                {
                    var total = host.RowsOf(new PageLine(LineWords.Theirs(title), rows: int.MaxValue), Glaze.Menu.FileColumnDegrees);
                    var rows = Math.Max(1, (int)Math.Floor(host.PageHeight(1, false, focused: true) / MenuPage.Words(1)));
                    var parts = Math.Max(1, (total + rows - 1) / rows);
                    titlePage %= parts;
                    var readFooter = new Footer(new Prompt(Footer.Close, "Close details", GlazeIcon.Close, PromptKind.Close),
                        rare: new Prompt(Back, "Back", GlazeIcon.Back),
                        farRight: parts > 1 ? new Prompt(Footer.NextPage, Footer.NextPageWords(titlePage, parts), GlazeIcon.Next, PromptKind.NextPage) : null);
                    return given = new MenuFrame("Task title", readFooter, lines: new[] { new PageLine(LineWords.Theirs(title), rows: Math.Min(rows, total - titlePage * rows), fromRow: titlePage * rows) }).Focus();
                }
                if (title != null) context.Add(new PageLine("Read task title", action: ReadTitle, opens: true, control: LineControl.Action));
                var footer = new Footer(new Prompt(Footer.Close, "Close details", GlazeIcon.Close, PromptKind.Close),
                    farRight: new Prompt(Reset, "Reset position", GlazeIcon.ResetPosition));
                return given = new MenuFrame(Subject, footer, lines: context).Focus();
            }
        }
        public void Act(string id, string? key)
        {
            if (drawn == null || drawn != given) return;
            if (id == Footer.Close) { Closed?.Invoke(); return; }
            if (id == ReadTitle && title != null) { readingTitle = true; Refresh(); return; }
            if (id == Back && readingTitle) { readingTitle = false; Refresh(); return; }
            if (id == Footer.NextPage && readingTitle) { titlePage++; Refresh(); return; }
            if (readingTitle) return;
            if (id == Reset) { reset(); Refresh(); return; }
        }
        private void Refresh() { drawn = null; given = null; Changed?.Invoke(); }
        public void Drawn(MenuFrame frame, Footer? sideFooter) { if (frame == given && sideFooter == null) drawn = frame; }
        public void Tick() { }
        public void FocusLeft() => Refresh();
        public void HoldStarted(string id) { }
        public void HoldEnded(string id, bool letGo) { }
        public void Heard(string words) { }
        public void Said(string words) { }
    }
}

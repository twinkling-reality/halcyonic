#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>Local workspace controls. Every action belongs to the exact complete page drawn.</summary>
    public sealed class PlacementColumn : IMenuColumn
    {
        public const string Reset = "placement-reset";
        public const string Subject = "Workspace";
        public const string ReadTitle = "placement-read-title";
        public const string Back = "placement-back";
        public const string TogglePortraits = "placement-task-portraits";
        public const string BeginMove = "placement-begin-move";
        public const string Minimize = "placement-minimize";
        public const string OtherApps = "placement-other-apps";
        public const string Navigate = "placement-navigate";
        public const string HoldInstruction = "Hold Move at the top of the workspace and drag to reposition it. Choose Done moving when it is in place.";
        private readonly IMenuHost host;
        private readonly Action reset;
        private readonly string? title;
        private readonly Func<bool>? portraits;
        private readonly Action<bool>? setPortraits;
        private readonly Action<bool>? setMoving;
        private readonly Action? minimize;
        private readonly Action? otherApps;
        private readonly Func<bool>? canOpenApps;
        private readonly Action<MenuPlace>? navigate;
        private bool readingTitle;
        private int titlePage, page;
        public bool Moving { get; private set; }
        private MenuFrame? given, drawn;
        public PlacementColumn(IMenuHost host, Action reset, string? title = null, Func<bool>? portraits = null, Action<bool>? setPortraits = null,
            Action<bool>? setMoving = null, Action? minimize = null, Action? otherApps = null, Func<bool>? canOpenApps = null, Action<MenuPlace>? navigate = null)
        {
            this.host = host; this.reset = reset; this.title = title;
            this.portraits = portraits; this.setPortraits = setPortraits; this.setMoving = setMoving;
            this.minimize = minimize; this.otherApps = otherApps; this.canOpenApps = canOpenApps; this.navigate = navigate;
        }
        public bool IsDrawn(MenuFrame? frame) => frame != null && frame == drawn && frame == given;
        public event Action? Changed;
        public event Action? Closed;
        public MenuFrame? Frame
        {
            get
            {
                if (readingTitle && title != null)
                {
                    var total = host.RowsOf(new PageLine(LineWords.Theirs(title), rows: int.MaxValue), Glaze.Menu.FileColumnDegrees);
                    var rows = Math.Max(1, (int)Math.Floor(host.PageHeight(1, false, focused: true) / MenuPage.Words(1)));
                    var parts = Math.Max(1, (total + rows - 1) / rows); titlePage %= parts;
                    var readFooter = new Footer(new Prompt(Footer.Close, "Close details", GlazeIcon.Close, PromptKind.Close),
                        rare: new Prompt(Back, "Back", GlazeIcon.Back),
                        farRight: parts > 1 ? new Prompt(Footer.NextPage, Footer.NextPageWords(titlePage, parts), GlazeIcon.Next, PromptKind.NextPage) : null);
                    return given = new MenuFrame("Task title", readFooter, lines: new[] { new PageLine(LineWords.Theirs(title), rows: Math.Min(rows, total - titlePage * rows), fromRow: titlePage * rows) }).Focus();
                }
                if (Moving)
                    return given = new MenuFrame("Move workspace", new Footer(new Prompt(Footer.Close, "Done moving", GlazeIcon.Close, PromptKind.Close)),
                        lines: new[] { new PageLine(HoldInstruction, rows: host.RowsOf(HoldInstruction, Glaze.Menu.FileColumnDegrees), tone: LineTone.Secondary), Control("Reset position", Reset) }).Focus();
                var actions = Actions();
                var budget = host.PageHeight(1, false, focused: true);
                if (actions.Any(action => MenuPage.Target(action.Rows) > budget + 0.00001f))
                    return given = new MenuFrame(Subject, new Footer(new Prompt(Footer.Close, "Close details", GlazeIcon.Close, PromptKind.Close)),
                        lines: new[] { new PageLine("Make more room to open workspace controls.", tone: LineTone.Secondary, rows: 2) }).Focus();
                var pages = new List<List<PageLine>> { new List<PageLine>() };
                var used = 0f;
                foreach (var action in actions)
                {
                    var height = MenuPage.Target(action.Rows);
                    var gap = pages.Last().Count > 0 ? MenuPage.TargetGap : 0f;
                    if (pages.Last().Count > 0 && used + gap + height > budget + 0.00001f) { pages.Add(new List<PageLine>()); used = 0f; gap = 0f; }
                    pages.Last().Add(action); used += gap + height;
                }
                page %= pages.Count;
                return given = new MenuFrame(Subject, new Footer(new Prompt(Footer.Close, "Close details", GlazeIcon.Close, PromptKind.Close),
                    farRight: pages.Count > 1 ? new Prompt(Footer.NextPage, Footer.NextPageWords(page, pages.Count), GlazeIcon.Next, PromptKind.NextPage) : null), lines: pages[page]).Focus();
            }
        }
        private PageLine Control(string words, string action, string? key = null, bool available = true) =>
            new PageLine(words, action: action, key: key, opens: true, control: LineControl.Action, available: available,
                rows: host.RowsOf(words, Glaze.Menu.FileColumnDegrees));
        private List<PageLine> Actions()
        {
            var actions = new List<PageLine>();
            if (setMoving != null) actions.Add(Control("Move workspace", BeginMove));
            actions.Add(Control("Reset position", Reset));
            if (minimize != null) actions.Add(Control("Minimize workspace", Minimize));
            if (otherApps != null) actions.Add(Control("Open other apps", OtherApps, available: canOpenApps?.Invoke() == true));
            if (portraits != null && setPortraits != null) actions.Add(Control(portraits() ? "Hide task portraits" : "Show task portraits", TogglePortraits));
            if (title != null) actions.Add(Control("Read task title", ReadTitle));
            if (navigate != null)
                foreach (var destination in new[] { MenuPlace.Projects, MenuPlace.Tasks, MenuPlace.Usage, MenuPlace.Settings })
                    actions.Add(Control("Open " + destination.ToString().ToLowerInvariant(), Navigate, destination.ToString()));
            return actions;
        }
        public void Act(string id, string? key)
        {
            if (drawn == null || drawn != given) return;
            if (id == Footer.Close) { Closed?.Invoke(); return; }
            if (id == Back && readingTitle) { readingTitle = false; Refresh(); return; }
            if (id == Footer.NextPage && drawn.Footer.All.Any(item => item.Prompt.Id == id))
            { if (readingTitle) titlePage++; else page++; Refresh(); return; }
            if (readingTitle || !drawn.Lines.Any(line => line.Action == id && line.Key == key && line.Available)) return;
            if (id == ReadTitle && title != null) { readingTitle = true; Refresh(); return; }
            if (id == BeginMove && setMoving != null) { Moving = true; setMoving(true); Refresh(); return; }
            if (id == TogglePortraits && portraits != null && setPortraits != null) { setPortraits(!portraits()); Refresh(); return; }
            if (id == Reset) { reset(); Refresh(); return; }
            if (id == Minimize) { minimize?.Invoke(); return; }
            if (id == OtherApps && canOpenApps?.Invoke() == true) { otherApps?.Invoke(); return; }
            if (id == Navigate && Enum.TryParse<MenuPlace>(key, out var destination)) navigate?.Invoke(destination);
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

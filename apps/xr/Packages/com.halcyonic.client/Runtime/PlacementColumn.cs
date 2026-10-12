#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>A local placement detail. Choosing never moves anything; Apply makes the chosen arrangement.</summary>
    public sealed class PlacementColumn : IMenuColumn
    {
        public const string Choose = "placement-choose";
        public const string Apply = "placement-apply";
        public const string Reset = "placement-reset";
        public const string Subject = "Move";
        public const string ReadTitle = "placement-read-title";
        public const string Back = "placement-back";
        public const string HoldInstruction = "Hold Move to drag the whole workspace.";
        public const string WindowInstruction = "The workspace stays below the window. Choose where the characters stand.";
        public const string DeskInstruction = "The characters stand on your desk. Reset brings the workspace back within reach.";
        private readonly IMenuHost host;
        private readonly Func<StageArrangement?> arrangement;
        private readonly Action<StageArrangement> apply;
        private readonly Action reset;
        private readonly Func<bool> besideWindow;
        private readonly string? title;
        private StageArrangement? chosen;
        private int page;
        private bool readingTitle;
        private int titlePage;
        private MenuFrame? given;
        private MenuFrame? drawn;
        private static readonly StageArrangement[] Choices = { StageArrangement.InFront, StageArrangement.TurnedAside, StageArrangement.BesideAWindow };

        public PlacementColumn(IMenuHost host, Func<StageArrangement?> arrangement, Action<StageArrangement> apply, Action reset, Func<bool> besideWindow, string? title = null)
        {
            this.host = host;
            this.arrangement = arrangement;
            this.apply = apply;
            this.reset = reset;
            this.besideWindow = besideWindow;
            this.title = title;
        }
        public bool IsDrawn(MenuFrame? frame) => frame != null && frame == drawn && frame == given;
        public event Action? Changed;
        public event Action? Closed;
        public MenuFrame? Frame
        {
            get
            {
                var instruction = arrangement() == null ? DeskInstruction : besideWindow() ? WindowInstruction : HoldInstruction;
                var summary = chosen is StageArrangement candidate ? "Chosen: " + Name(candidate) : instruction;
                var context = new List<PageLine> { new PageLine(summary, rows: Math.Max(host.RowsOf(instruction, Glaze.Menu.FileColumnDegrees), host.RowsOf(summary, Glaze.Menu.FileColumnDegrees)), tone: LineTone.Secondary) };
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
                var room = host.PageHeight(1, false, focused: true);
                var navigation = new PageLine("More placements", action: Footer.NextPage, control: LineControl.Action);
                if (title != null) context.Add(new PageLine("Read title", action: ReadTitle, opens: true, control: LineControl.Action,
                    besideNext: host.FitsHalf(navigation, Glaze.Menu.FileColumnDegrees)));
                if (arrangement() != null) context.Add(navigation);
                var used = 0f;
                for (var index = 0; index < context.Count; index++)
                {
                    used += (context[index].Action != null ? MenuPage.Target() : MenuPage.Words(context[index].Rows)) + MenuPage.TargetGap;
                    if (context[index].BesideNext && index + 1 < context.Count && host.FitsHalf(context[index], Glaze.Menu.FileColumnDegrees)) index++;
                }
                var count = Math.Max(1, Math.Min(3, (int)Math.Floor((room - used + MenuPage.TargetGap) / (MenuPage.Target() + MenuPage.TargetGap))));
                var pages = arrangement() == null ? 1 : (Choices.Length + count - 1) / count;
                page %= pages;
                if (pages == 1) context.Remove(navigation);
                var lines = context;
                if (arrangement() != null)
                {
                    foreach (var choice in Choices.Skip(page * count).Take(count))
                        lines.Add(new PageLine(Name(choice), fact: arrangement() == choice ? "Now" : null, action: Choose, key: choice.ToString(), choice: true, chosen: chosen == choice, answerMark: AnswerMark.Single));
                }
                var footer = new Footer(new Prompt(Footer.Close, "Close details", GlazeIcon.Close, PromptKind.Close),
                    rare: new Prompt(Reset, "Reset position", GlazeIcon.ResetPosition),
                    farRight: chosen != null ? new Prompt(Apply, "Apply", GlazeIcon.Change, main: true) : null);
                return given = new MenuFrame(Subject, footer, lines: lines).Focus();
            }
        }
        public static string Name(StageArrangement choice) => choice switch
        {
            StageArrangement.TurnedAside => "Room for a window",
            StageArrangement.BesideAWindow => "Either side of a window",
            _ => "In front of you",
        };
        public void Act(string id, string? key)
        {
            if (drawn == null || drawn != given) return;
            if (id == Footer.Close) { Closed?.Invoke(); return; }
            if (id == ReadTitle && title != null) { readingTitle = true; Refresh(); return; }
            if (id == Back && readingTitle) { readingTitle = false; Refresh(); return; }
            if (id == Footer.NextPage && readingTitle) { titlePage++; Refresh(); return; }
            if (readingTitle) return;
            if (id == Reset) { reset(); Refresh(); return; }
            if (id == Choose && key != null && Enum.TryParse<StageArrangement>(key, out var choice) && drawn.Lines.Any(line => line.Key == key)) chosen = choice;
            else if (id == Apply && chosen is StageArrangement selected && arrangement() != null) { apply(selected); chosen = null; }
            else if (id == Footer.NextPage) page++;
            else return;
            Refresh();
        }
        private void Refresh() { drawn = null; given = null; Changed?.Invoke(); }
        public void Drawn(MenuFrame frame, Footer? sideFooter) { if (frame == given && sideFooter == null) drawn = frame; }
        public void Tick() { }
        public void FocusLeft() { chosen = null; Refresh(); }
        public void HoldStarted(string id) { }
        public void HoldEnded(string id, bool letGo) { }
        public void Heard(string words) { }
        public void Said(string words) { }
    }
}

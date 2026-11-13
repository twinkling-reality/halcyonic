using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The redesign's general rule for a frame with no moment of its own (RoomBar.OfFrame): Halcyonic's lines in the panel,
/// the frame's actions in the column, one line of outside words on the band; and, since the general reader (2026-11-13), every
/// other part of any frame in the kit's own parts, so no frame falls to the older view.
/// </summary>
public class RoomPageTests
{
    private readonly CommandFactory factory = new(Samples.Client);

    private static Prompt Close => new(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close);

    private static MenuFrame Frame(IReadOnlyList<PageLine> lines, Footer? footer = null, IReadOnlyList<FrameSection>? sections = null, SidePanel? side = null) =>
        new("New Project", footer ?? new Footer(Close), sections: sections, lines: lines, side: side);

    private static string Show(RoomBar bar) => bar.State + " | panel: " + bar.Body.Text + " | says: " + bar.Sentence + " | band: " + bar.Band + " | " +
        string.Join(", ", bar.Column.Select(action => action.Words + (action.Main ? "*" : "") + (action.Locks ? "(lock)" : "") + (action.Chosen ? "[x]" : "")));

    [Test]
    public void APageOfTheFileWithNoPlaceOfItsOwnIsDrawnByThePair()
    {
        var work = new WaitingWork();
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Completed; }, WorkstreamStatus.Completed);
        var host = new FileMenuHost();
        var column = new FileColumn(host, () => work.Present(), factory, () => null, _ => null, () => "");
        column.Reflow();
        column.Drawn(column.Frame!, sidePanel: null);
        host.Wait(1);
        column.Act(MenuFrame.ChooseSection, FileScreens.Key(FileSection.Checks));
        var frame = column.Frame!;
        Assert.That(KitFileBinding.Of(frame, column.Steering.Armed, column.Now!, "", new ConnectionStatus(ConnectionPhase.Live), "Storefront",
            new[] { new KitSpineSection(new KitRowModel[0]) }, roomDesign: true), Is.Null, "the task's binding has no place for Checks");

        var plain = KitFileBinding.PlainOf(frame);
        var page = RoomBar.Of(plain, VoiceStage.Idle, false);
        Assert.That((page.State, page.Pair, page.FromMoment, page.Wide), Is.EqualTo((RoomBarState.Page, true, true, false)), Show(page));
        Assert.That(page.Body.Text, Is.EqualTo(frame.Lines.Single().Words), "the page's own line in the panel");
        Assert.That(page.Band, Is.Null);
        // The page's own prompt, then the file's other sections, and Close last: each the frame's own action.
        Assert.That(page.Column.Last().Control.Action, Is.EqualTo(Footer.Close));
        Assert.That(page.Column.Where(action => action.Place == KitPlace.Section).Select(action => action.Control.Key),
            Is.EqualTo(frame.Sections.Where(section => !section.Chosen).Select(section => section.Key)));
        Assert.That(page.Column.All(action => ReferenceEquals(action.Control.Frame, frame)), Is.True);
        Assert.That(() => plain.Model, Throws.InvalidOperationException, "a plain binding has no task's model");

        // A section's action is the frame's own: pressed, the file shows that section.
        column.Drawn(frame, sidePanel: null);
        host.Wait(1);
        var activity = page.Column.Single(action => action.Control.Key == FileScreens.Key(FileSection.Activity));
        column.Act(activity.Control.Action, activity.Control.Key);
        Assert.That(column.Screen.Section, Is.EqualTo(FileSection.Activity));
    }

    [Test]
    public void LinesActionsOutsideWordsAndWhatWaitsEachTakeTheirPlace()
    {
        var footer = new Footer(Close, rare: new Prompt("start-over", "Start Over", GlazeIcon.Close),
            farRight: new Prompt("make-recap", "Make Recap", GlazeIcon.Next, main: true, available: false, reason: "Describe First Task"));
        var frame = Frame(new[]
        {
            new PageLine("Your Idea"),
            new PageLine("A game where balloons pop for points", wordsAreData: true),
            new PageLine("Fixed Questions"),
            new PageLine("Talk It Through", action: "talk-it-through"),
            new PageLine("Light", action: "answer", key: "0", choice: true, chosen: true, answerMark: AnswerMark.Single),
        }, footer, new[] { new FrameSection("idea", "Your Idea", chosen: true), new FrameSection("questions", "Questions"), new FrameSection("build", "Build", reached: false) });
        var page = RoomBar.OfFrame(frame, VoiceStage.Idle)!;
        Assert.That(page.Lines!.Select(line => line.Text), Is.EqualTo(new[] { "Your Idea", "Fixed Questions" }), "Halcyonic's lines, each its own");
        Assert.That(page.Band, Is.EqualTo("A game where balloons pop for points"), "the outside words stand on the band, never in the panel");
        Assert.That(page.Body.Text, Does.Not.Contain("balloons"));
        Assert.That(page.Sentence, Is.EqualTo("Describe First Task"), "why the main action waits");
        Assert.That(page.Column.Select(action => (action.Control.Action, action.Control.Key, action.Main)), Is.EqualTo(new[]
        {
            ("talk-it-through", (string?)null, false), ("answer", "0", false), ("start-over", null, false),
            (MenuFrame.ChooseSection, "questions", false), (Footer.Close, null, false),
        }), "the lines that act, the other prompts, the sections reached, then the way out; the main action, which cannot act, is not drawn");
        var answer = page.Column[1];
        Assert.That((answer.Mark, answer.Chosen), Is.EqualTo((AnswerMark.Single, true)));

        // Once the main action can act it leads, and a confirmation's Yes stands behind its lock.
        var before = new Footer(Close, farRight: new Prompt("start-over", "Start Over", GlazeIcon.Close));
        var asked = Footer.Confirm(before, PromptSlot.FarRight, new Prompt("yes", "Confirm", GlazeIcon.Next, PromptKind.Yes), new Prompt("cancel", "Cancel", GlazeIcon.Close, PromptKind.Cancel));
        var ready = RoomBar.OfFrame(Frame(new[] { new PageLine("Start Over?") }, asked), VoiceStage.Idle)!;
        Assert.That((ready.Column[0].Control.Action, ready.Column[0].Main, ready.Column[0].Locks, ready.Sentence), Is.EqualTo(("yes", true, true, (string?)null)), Show(ready));
        Assert.That(ready.Column.Last().Place, Is.EqualTo(KitPlace.Cancel), Show(ready));

        // With nothing of Halcyonic's to say there is no panel: the actions take its place.
        var bare = RoomBar.OfFrame(Frame(new[] { new PageLine("Continue", action: "continue") }), VoiceStage.Idle)!;
        Assert.That((bare.Wide, bare.Body.IsEmpty, bare.Column.Count), Is.EqualTo((true, true, 2)));
    }

    /// <summary>
    /// The general reader (the lead, 2026-11-13T16:40Z) takes every frame with anything in it, each part as the frame hands it
    /// apart, and never joins two lines: what once fell to the older view is drawn in the kit's own parts.
    /// </summary>
    [Test]
    public void TheGeneralReaderTakesEveryFrameEachPartApartAndNeverDeclines()
    {
        PageLine Words(string words) => new(words);
        RoomBar Of(MenuFrame frame) => RoomBar.OfFrame(frame, VoiceStage.Idle) ?? throw new AssertionException("the general reader declined a frame");

        // A side panel stands in the page's place: its subject the bar's words, its facts as label and value rows, its own Close last.
        var side = Of(Frame(new[] { new PageLine("Report", action: "open-report", opens: true, chosen: true) },
            side: new SidePanel("Report", facts: new[] { new SideFact("Tests", "12 Passed"), new SideFact("Checked", "Today") })));
        Assert.That(side.Words.Text, Is.EqualTo("Report"));
        Assert.That(side.Facts!.Select(row => (row.Label, row.Words.Text, row.Note)), Is.EqualTo(new[] { ("Tests", "12 Passed", (string?)null), ("Checked", "Today", null) }),
            "a value is the row's words under its name, never a note");
        Assert.That(side.Column.Select(action => action.Control.Action), Is.EqualTo(new[] { SidePanel.Close }), "the side panel's own Close is its one way out");
        Assert.That(side.Column.All(action => action.Control.Action != "open-report"), Is.True, "the page under it is one Close away, not drawn beside it");

        // A field: its words and the caret, opened by the line's own action.
        var field = Of(Frame(new[] { new PageLine("Make a kite", control: LineControl.Input, action: "edit") }));
        Assert.That((field.State, field.Caret, field.Body.Text, field.Field!.Action, field.Field.Place), Is.EqualTo((RoomBarState.Draft, true, "Make a kite", "edit", KitPlace.Field)));

        // A text cut in parts is the row of its part, never paged again by the reader.
        var part = Of(Frame(new[] { new PageLine("A long text", fromRow: 8, rows: 8) }));
        Assert.That(part.Facts!.Select(row => (row.Words.Text, row.FromLine, row.Lines)), Is.EqualTo(new[] { ("A long text", 8, (int?)8) }));

        // A fact beside a line: the line its label, the fact its words, apart.
        var fact = Of(Frame(new[] { Words("Settings"), new PageLine("Tests", fact: "12 Passed") }));
        Assert.That(fact.Facts!.Select(row => (row.Label, row.Words.Text)), Is.EqualTo(new[] { ((string?)null, "Settings"), ("Tests", "12 Passed") }));

        // A line that cannot act now is a row, never dropped; one that can, with a fact, is an action with the fact as its detail.
        var dark = Of(Frame(new[] { new PageLine("Dark", action: "answer", available: false), new PageLine("Text Size", fact: "Larger", action: "open", opens: true) }));
        Assert.That(dark.Body.Text, Is.EqualTo("Dark"));
        Assert.That(dark.Column.Select(action => (action.Control.Action, action.Words, action.Detail)), Is.EqualTo(new[] { ("open", "Text Size", (string?)"Larger"), (Footer.Close, "Close", null) }));

        // A held prompt keeps its hold.
        var held = Of(Frame(new[] { Words("Say It") }, new Footer(Close, secondary: new Prompt("hold", "Hold to Talk", GlazeIcon.Next, holds: true))));
        Assert.That(held.Column.Single(action => action.Control.Action == "hold").Control is { Holds: true, Place: KitPlace.Talk }, Is.True);

        // Two lines of outside words: the first on the band, the second a row of its own, theirs.
        var two = Of(Frame(new[] { new PageLine("first", wordsAreData: true), new PageLine("second", wordsAreData: true) }));
        Assert.That((two.Band, two.Body.Text), Is.EqualTo(("first", "second")));

        // Nothing at all still says what it is, never a blank bar.
        Assert.That(Of(Frame(new PageLine[0], new Footer())).Body.Text, Is.EqualTo("New Project"));

        // Lines of Halcyonic's own are rows, never joined on a dot.
        var lines = Of(Frame(new[] { Words("Your Idea"), Words("Fixed Questions") }));
        Assert.That((lines.Body.IsEmpty, lines.Facts!.Select(row => row.Words.Text)), Is.EqualTo((true, new[] { "Your Idea", "Fixed Questions" })));
        Assert.That(lines.Body.Text + string.Join("", lines.Facts!.Select(row => row.Words.Text)), Does.Not.Contain(KitMomentWords.Dot.Trim()));

        Assert.That(lines.Whole, Is.True);

        // A confirmation is drawn as the frame gives it: its words whole, its Yes the main action behind the lock, then Cancel.
        var asked = Footer.Confirm(new Footer(Close, farRight: new Prompt("go", "Go", GlazeIcon.Next)), PromptSlot.FarRight,
            new Prompt("yes", "Forget Computer", GlazeIcon.Next, PromptKind.Yes), new Prompt("cancel", "Cancel", GlazeIcon.Close, PromptKind.Cancel));
        var confirming = Of(Frame(new[] { Words("This headset stops reaching the computer until it is paired again.") }, asked));
        Assert.That((confirming.Body.Text, confirming.Column[0].Control.Action, confirming.Column[0].Locks, confirming.Whole),
            Is.EqualTo(("This headset stops reaching the computer until it is paired again.", "yes", true, true)));

        // An agent's claim is outside words whoever spells it.
        Assert.That(RoomBar.OfFrame(Frame(new[] { new PageLine("All tests pass", chip: "Agent says", claim: true) }), VoiceStage.Idle)!.Band, Is.EqualTo("All tests pass"));
        Assert.That(RoomBar.OfFrame(Frame(new[] { new PageLine(LineWords.Theirs("Pop the balloons")) }), VoiceStage.Idle)!.Band, Is.EqualTo("Pop the balloons"));
        // A name from outside inside Halcyonic's own sentence stays in the panel, as a file's name does in Changes.
        var mixed = RoomBar.OfFrame(Frame(new[] { new PageLine(LineWords.Ours("Folder · ") + LineWords.TheirName("balloon-pop")) }), VoiceStage.Idle)!;
        Assert.That((mixed.Band, mixed.Body.Text), Is.EqualTo(((string?)null, "Folder · balloon-pop")));
    }

    /// <summary>
    /// A side panel is never dropped (AUDIT's D, 2026-11-13T16:47Z): whichever reader's frame it opens over, from the chosen line
    /// that opens it as the product does, the bar lays the panel's title, its value and its own Close; and while it stands, only
    /// what acts on it stays in the column (AUDIT's G): a prompt safe in its place and the panel's Close, never the hidden page's.
    /// </summary>
    [Test]
    public void ASidePanelIsLaidWithItsTitleAndItsCloseOverEveryReadersFrameAndOnlyWhatActsOnIt()
    {
        var side = new SidePanel("Text Size", facts: new[] { new SideFact("Now", "Larger") });
        PageLine Opening() => new("Text Size", fact: "Larger", action: "open", opens: true, chosen: true);
        var footer = new Footer(Close, rare: new Prompt("change", "Change", GlazeIcon.Change, safeInPlace: true),
            farRight: new Prompt("send", "Send", GlazeIcon.Next, main: true));
        var frames = new[]
        {
            Frame(new[] { Opening() }, footer, side: side),
            new MenuFrame(AskMenuColumn.Subject, footer, lines: new[] { Opening(), new PageLine("Ask anything") }, side: side),
            new MenuFrame(PairingColumn.Subject, footer, lines: new[] { Opening(), new PageLine("Pair") }, side: side),
            new MenuFrame(ProjectsText.Subject, footer, lines: new[] { Opening(), new PageLine("Kite Run", action: "open-project", opens: true) }, side: side),
        };
        foreach (var frame in frames)
        {
            var bar = RoomBar.OfFrame(frame, VoiceStage.Idle);
            Assert.That(bar, Is.Not.Null, frame.Subject + ": the side panel was dropped");
            Assert.That(bar!.Words.Text, Is.EqualTo("Text Size"), frame.Subject);
            Assert.That(bar.Facts!.Single().Words.Text, Is.EqualTo("Larger"), frame.Subject);
            Assert.That(bar.Column.Select(action => action.Control.Action), Is.EqualTo(new[] { "change", SidePanel.Close }),
                frame.Subject + ": only what acts on the panel, then its Close; never the hidden page's Send or its own Close");
        }
    }

    /// <summary>
    /// The general reader pages what passes a page or the column itself (AUDIT's MUST I): nothing dropped, the way out on every
    /// page, and the frame counted as read only once every page was shown.
    /// </summary>
    [Test]
    public void TheGeneralReaderPagesRowsAndActionsItselfKeepingTheWayOutAndCountsReadOnlyOnceEveryPageShowed()
    {
        var nine = RoomBar.OfFrame(Frame(Enumerable.Range(1, RoomRows.APage + 1).Select(n => new PageLine("Line " + n)).ToList()), VoiceStage.Idle)!;
        Assert.That((nine.Facts!.Count, nine.Whole, nine.GeneralPart), Is.EqualTo((RoomRows.APage, false, (0, 2))));
        Assert.That(nine.Column.Select(action => (action.Control.Action, action.Words, action.Detail)),
            Is.EqualTo(new[] { (RoomBar.TurnPage, "Next Page", (string?)"2 of 2"), (Footer.Close, "Close", null) }));
        Assert.That(nine.Turn(), Is.True);
        Assert.That((nine.Facts!.Select(row => row.Words.Text), nine.Whole), Is.EqualTo((new[] { "Line 9" }, true)), "row nine reached by a press, and now every page was seen");
        Assert.That(nine.Column.Select(action => action.Words), Is.EqualTo(new[] { "First Page", "Close" }));

        var eight = RoomBar.OfFrame(Frame(Enumerable.Range(1, 8).Select(n => new PageLine("Do " + n, action: "do", key: n.ToString())).ToList()), VoiceStage.Idle)!;
        var keys = new List<string?>();
        for (var page = 0; page < eight.GeneralPart.Pages; page++)
        {
            Assert.That(eight.Column.Count, Is.LessThanOrEqualTo(RoomBar.ColumnMost));
            Assert.That(eight.Column.Last().Control.Action, Is.EqualTo(Footer.Close), "Close on every page");
            keys.AddRange(eight.Column.Where(action => action.Control.Action == "do").Select(action => action.Control.Key));
            Assert.That(eight.Whole, Is.EqualTo(page == eight.GeneralPart.Pages - 1));
            eight.Turn();
        }
        Assert.That(keys, Is.EqualTo(Enumerable.Range(1, 8).Select(n => n.ToString())), "every action reached, none dropped");
    }

    /// <summary>
    /// The busiest column a footer gives (a confirmation: its Yes, Cancel and Close) still pages the frame's other actions, the
    /// pager between, and loses none (AUDIT's SHOULD, 16:56Z; the reader also never divides by a room of nought).
    /// </summary>
    [Test]
    public void TheBusiestFooterStillPagesEveryOtherAction()
    {
        var footer = Footer.Confirm(new Footer(Close, farRight: new Prompt("go", "Go", GlazeIcon.Next)), PromptSlot.FarRight,
            new Prompt("yes", "Confirm", GlazeIcon.Next, PromptKind.Yes), new Prompt("cancel", "Cancel", GlazeIcon.Close, PromptKind.Cancel));
        var bar = RoomBar.OfFrame(Frame(Enumerable.Range(1, 5).Select(n => new PageLine("Do " + n, action: "do", key: n.ToString())).ToList(), footer), VoiceStage.Idle)!;
        Assert.That(bar.GeneralPart.Pages, Is.GreaterThan(1));
        var keys = new List<string?>();
        for (var page = 0; page < bar.GeneralPart.Pages; page++)
        {
            Assert.That((bar.Column[0].Control.Action, bar.Column[0].Locks), Is.EqualTo(("yes", true)), "the Yes first, behind its lock, on every page");
            Assert.That(bar.Column.Count, Is.LessThanOrEqualTo(RoomBar.ColumnMost));
            keys.AddRange(bar.Column.Where(action => action.Control.Action == "do").Select(action => action.Control.Key));
            bar.Turn();
        }
        Assert.That(keys, Is.EqualTo(new[] { "1", "2", "3", "4", "5" }));
    }
}

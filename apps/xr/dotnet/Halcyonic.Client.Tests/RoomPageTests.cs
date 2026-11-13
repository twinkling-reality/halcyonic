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
        // Across the reader's pages (four actions a page in all): Close last on each, every other section reached in order.
        var actions = new List<RoomAction>();
        for (var at = 0; at < page.GeneralPart.Pages; at++)
        {
            Assert.That(page.Column.Last().Control.Action, Is.EqualTo(Footer.Close));
            Assert.That(page.Column.All(action => ReferenceEquals(action.Control.Frame, frame)), Is.True);
            actions.AddRange(page.Column);
            page.Turn();
        }
        Assert.That(actions.Where(action => action.Place == KitPlace.Section).Select(action => action.Control.Key),
            Is.EqualTo(frame.Sections.Where(section => !section.Chosen).Select(section => section.Key)));
        Assert.That(() => plain.Model, Throws.InvalidOperationException, "a plain binding has no task's model");

        // A section's action is the frame's own: pressed, the file shows that section.
        column.Drawn(frame, sidePanel: null);
        host.Wait(1);
        var activity = actions.Single(action => action.Control.Key == FileScreens.Key(FileSection.Activity));
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
        Assert.That(page.Lines!.Select(line => line.Text), Is.EqualTo(new[] { "New Project", "Your Idea", "Fixed Questions" }), "where am I first, then Halcyonic's lines, each its own");
        Assert.That(page.Band, Is.EqualTo("A game where balloons pop for points"), "the outside words stand on the band, never in the panel");
        Assert.That(page.Body.Text, Does.Not.Contain("balloons"));
        Assert.That(page.Sentence, Is.EqualTo("Describe First Task"), "why the main action waits");
        // In order across the reader's pages (four actions a page in all, the spec's): the lines that act, the other prompts, the
        // sections reached, the way out on every page; the main action, which cannot act, is not drawn.
        var order = new List<(string, string?, bool)>();
        for (var at = 0; at < page.GeneralPart.Pages; at++)
        {
            Assert.That(page.Column.Count, Is.LessThanOrEqualTo(RoomForm.SpecActions));
            Assert.That(page.Column.Last().Control.Action, Is.EqualTo(Footer.Close), "the way out on every page");
            order.AddRange(page.Column.Where(action => action.Control.Action is not (RoomBar.TurnPage or Footer.Close))
                .Select(action => (action.Control.Action, action.Control.Key, action.Main)));
            if (at + 1 < page.GeneralPart.Pages) page.Turn();
        }
        Assert.That(order, Is.EqualTo(new[]
        {
            ("talk-it-through", (string?)null, false), ("answer", "0", false), ("start-over", null, false), (MenuFrame.ChooseSection, "questions", false),
        }));
        page = RoomBar.OfFrame(frame, VoiceStage.Idle)!;
        var answer = page.Column[1];
        Assert.That((answer.Mark, answer.Chosen), Is.EqualTo((AnswerMark.Single, true)));

        // Once the main action can act it leads, and a confirmation's Yes stands behind its lock.
        var before = new Footer(Close, farRight: new Prompt("start-over", "Start Over", GlazeIcon.Close));
        var asked = Footer.Confirm(before, PromptSlot.FarRight, new Prompt("yes", "Confirm", GlazeIcon.Next, PromptKind.Yes), new Prompt("cancel", "Cancel", GlazeIcon.Close, PromptKind.Cancel));
        var ready = RoomBar.OfFrame(Frame(new[] { new PageLine("Start Over?") }, asked), VoiceStage.Idle)!;
        Assert.That((ready.Column[0].Control.Action, ready.Column[0].Main, ready.Column[0].Locks, ready.Sentence), Is.EqualTo(("yes", true, true, (string?)null)), Show(ready));
        Assert.That(ready.Column.Last().Place, Is.EqualTo(KitPlace.Cancel), Show(ready));
        Assert.That(ready.Column[0].Control.Destructive, Is.False, "a Yes is not destructive unless its model says so");

        // A Yes that forgets something is the destructive kind, as its model declares it, carried to the kit's control (AUDIT, 2026-11-13T1754Z).
        var forgets = Footer.Confirm(before, PromptSlot.FarRight, new Prompt("yes", "Forget Computer", GlazeIcon.Next, PromptKind.Yes, destructive: true),
            new Prompt("cancel", "Cancel", GlazeIcon.Close, PromptKind.Cancel));
        var forgetting = RoomBar.OfFrame(Frame(new[] { new PageLine("Forget this computer?") }, forgets), VoiceStage.Idle)!;
        Assert.That((forgetting.Column[0].Control.Destructive, forgetting.Column[0].Locks), Is.EqualTo((true, true)), Show(forgetting));

        // With nothing of Halcyonic's to say there is no panel: the actions take its place.
        var bare = RoomBar.OfFrame(Frame(new[] { new PageLine("Continue", action: "continue") }), VoiceStage.Idle)!;
        Assert.That((bare.Wide, bare.Body.Text, bare.Column.Count), Is.EqualTo((false, "New Project", 2)), "where am I, then the actions");
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
        Assert.That(side.Facts!.Select(row => (row.Label, row.Words.Text, row.Note)),
            Is.EqualTo(new[] { ((string?)null, "Report", (string?)null), ("Tests", "12 Passed", null), ("Checked", "Today", null) }),
            "a value is the row's words under its name, never a note");
        Assert.That(side.Column.Select(action => action.Control.Action), Is.EqualTo(new[] { SidePanel.Close }), "the side panel's own Close is its one way out");
        Assert.That(side.Column.All(action => action.Control.Action != "open-report"), Is.True, "the page under it is one Close away, not drawn beside it");

        // A field: its words and the caret, opened by the line's own action.
        var field = Of(Frame(new[] { new PageLine("Make a kite", control: LineControl.Input, action: "edit") }));
        Assert.That((field.State, field.Caret, field.Body.Text, field.Field!.Action, field.Field.Place), Is.EqualTo((RoomBarState.Draft, true, "Make a kite", "edit", KitPlace.Field)));

        // A text cut in parts is the row of its part, never paged again by the reader.
        var part = Of(Frame(new[] { new PageLine("A long text", fromRow: 8, rows: 8) }));
        Assert.That(part.Facts!.Select(row => (row.Words.Text, row.FromLine, row.Lines)), Is.EqualTo(new[] { ("New Project", 0, (int?)null), ("A long text", 8, (int?)8) }));

        // A fact beside a line: the line its label, the fact its words, apart.
        var fact = Of(Frame(new[] { Words("Settings"), new PageLine("Tests", fact: "12 Passed") }));
        Assert.That(fact.Facts!.Select(row => (row.Label, row.Words.Text)), Is.EqualTo(new[] { ((string?)null, "New Project"), (null, "Settings"), ("Tests", "12 Passed") }));

        // A line that cannot act now is a row, never dropped; one that can, with a fact, is an action with the fact as its detail.
        var dark = Of(Frame(new[] { new PageLine("Dark", action: "answer", available: false), new PageLine("Text Size", fact: "Larger", action: "open", opens: true) }));
        // The line that acts with a value is a pressed row of a card (AUDIT's 17:56Z): its words the name, its value the note; the
        // card's title is where am I, the line that cannot act its description.
        Assert.That((dark.Card!.Title!.Text, dark.Card.Description!.Text), Is.EqualTo(("New Project", "Dark")));
        Assert.That(dark.Card.List.Select(row => (row.Name.Text, row.Note, row.Press!.Action)), Is.EqualTo(new[] { ("Text Size", (string?)"Larger", "open") }));
        Assert.That(dark.Column.Select(action => action.Control.Action), Is.EqualTo(new[] { Footer.Close }));

        // A held prompt keeps its hold.
        var held = Of(Frame(new[] { Words("Say It") }, new Footer(Close, secondary: new Prompt("hold", "Hold to Talk", GlazeIcon.Next, holds: true))));
        Assert.That(held.Column.Single(action => action.Control.Action == "hold").Control is { Holds: true, Place: KitPlace.Talk }, Is.True);

        // Two lines of outside words: the first on the band, the second a row of its own, theirs.
        var two = Of(Frame(new[] { new PageLine("first", wordsAreData: true), new PageLine("second", wordsAreData: true) }));
        Assert.That((two.Band, two.Facts!.Select(row => row.Words.Text)), Is.EqualTo(("first", new[] { "New Project", "second" })));

        // Nothing at all still says what it is, never a blank bar.
        Assert.That(Of(Frame(new PageLine[0], new Footer())).Body.Text, Is.EqualTo("New Project"));

        // Lines of Halcyonic's own are rows, never joined on a dot.
        var lines = Of(Frame(new[] { Words("Your Idea"), Words("Fixed Questions") }));
        Assert.That((lines.Body.IsEmpty, lines.Facts!.Select(row => row.Words.Text)), Is.EqualTo((true, new[] { "New Project", "Your Idea", "Fixed Questions" })));
        Assert.That(lines.Body.Text + string.Join("", lines.Facts!.Select(row => row.Words.Text)), Does.Not.Contain(KitMomentWords.Dot.Trim()));

        Assert.That(lines.Whole, Is.True);

        // A confirmation is drawn as the frame gives it: its words whole, its Yes the main action behind the lock, then Cancel.
        var asked = Footer.Confirm(new Footer(Close, farRight: new Prompt("go", "Go", GlazeIcon.Next)), PromptSlot.FarRight,
            new Prompt("yes", "Forget Computer", GlazeIcon.Next, PromptKind.Yes), new Prompt("cancel", "Cancel", GlazeIcon.Close, PromptKind.Cancel));
        var confirming = Of(Frame(new[] { Words("This headset stops reaching the computer until it is paired again.") }, asked));
        Assert.That((confirming.Facts!.Last().Words.Text, confirming.Column[0].Control.Action, confirming.Column[0].Locks, confirming.Whole),
            Is.EqualTo(("This headset stops reaching the computer until it is paired again.", "yes", true, true)));
        Assert.That(confirming.Column.Select(action => action.Control.Place), Is.EqualTo(new[] { KitPlace.Yes, KitPlace.Cancel }),
            "a confirmation's one way out is Cancel, last; never Close beside it (the critic, 17:49Z)");

        // An agent's claim is outside words whoever spells it.
        Assert.That(RoomBar.OfFrame(Frame(new[] { new PageLine("All tests pass", chip: "Agent says", claim: true) }), VoiceStage.Idle)!.Band, Is.EqualTo("All tests pass"));
        Assert.That(RoomBar.OfFrame(Frame(new[] { new PageLine(LineWords.Theirs("Pop the balloons")) }), VoiceStage.Idle)!.Band, Is.EqualTo("Pop the balloons"));
        // A name from outside inside Halcyonic's own sentence stays in the panel, as a file's name does in Changes.
        var mixed = RoomBar.OfFrame(Frame(new[] { new PageLine(LineWords.Ours("Folder · ") + LineWords.TheirName("balloon-pop")) }), VoiceStage.Idle)!;
        Assert.That((mixed.Band, mixed.Facts!.Last().Words.Text), Is.EqualTo(((string?)null, "Folder · balloon-pop")));
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
            Assert.That(bar.Facts!.Select(row => row.Words.Text), Is.EqualTo(new[] { "Text Size", "Larger" }), frame.Subject + ": what the panel is about, then its value");
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
            Is.EqualTo(new[] { (RoomBar.TurnPage, "Next Page", (string?)"Page 1 of 2"), (Footer.Close, "Close", null) }));
        Assert.That(nine.Turn(), Is.True);
        Assert.That((nine.Facts!.Select(row => row.Words.Text), nine.Whole), Is.EqualTo((new[] { "Line 8", "Line 9" }, true)), "row nine reached by a press, and now every page was seen");
        Assert.That(nine.Column.Select(action => (action.Words, action.Detail)), Is.EqualTo(new[] { ("First Page", (string?)"Page 2 of 2"), ("Close", null) }), "the pager says where you are");

        var eight = RoomBar.OfFrame(Frame(Enumerable.Range(1, 8).Select(n => new PageLine("Do " + n, action: "do", key: n.ToString())).ToList()), VoiceStage.Idle)!;
        var keys = new List<string?>();
        for (var page = 0; page < eight.GeneralPart.Pages; page++)
        {
            Assert.That(eight.Column.Count, Is.LessThanOrEqualTo(RoomBar.ColumnMost));
            Assert.That(eight.Column.Last().Control.Action, Is.EqualTo(Footer.Close), "Close on every page");
            keys.AddRange(eight.Column.Where(action => action.Control.Action == "do").Select(action => action.Control.Key));
            Assert.That(eight.Whole, Is.True, "only actions page: nothing to read is hidden on a later page");
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

    /// <summary>
    /// The general reader reads a frame for the form the plane will lay it in (the lead's 17:16Z ruling, XR's shape of 17:22Z): its
    /// rows a page are the form's, measured by the plane, and the bar keeps the form it was read for. Full width, eight a page,
    /// where none is given.
    /// </summary>
    [Test]
    public void TheGeneralReaderPagesByTheFormThePlaneWillLayItIn()
    {
        var frame = Frame(Enumerable.Range(1, 7).Select(n => new PageLine("Line " + n)).ToList());
        var full = RoomBar.OfFrame(frame, VoiceStage.Idle)!;
        Assert.That((full.Form.Narrow, full.Form.RowsAPage, full.GeneralPart.Pages, full.Whole), Is.EqualTo((false, RoomRows.APage, 1, true)));

        var narrow = RoomBar.OfFrame(frame, VoiceStage.Idle, new RoomForm(narrow: true, rowsAPage: 3))!;
        Assert.That((narrow.Form.Narrow, narrow.GeneralPart.Pages, narrow.Facts!.Count, narrow.Whole), Is.EqualTo((true, 3, 3, false)));
        var seen = new List<string>(narrow.Facts.Select(row => row.Words.Text));
        narrow.Turn();
        seen.AddRange(narrow.Facts!.Select(row => row.Words.Text));
        Assert.That(narrow.Whole, Is.False, "two of three pages seen");
        narrow.Turn();
        seen.AddRange(narrow.Facts!.Select(row => row.Words.Text));
        Assert.That((narrow.Whole, seen), Is.EqualTo((true, new[] { "New Project" }.Concat(Enumerable.Range(1, 7).Select(n => "Line " + n)))), "every row reached, read once every page was shown");

        Assert.That((new RoomForm(true, 0).RowsAPage, new RoomForm(false, 99).RowsAPage, new RoomForm(true, 2).Fewer().Fewer().RowsAPage), Is.EqualTo((1, RoomRows.APage, 1)),
            "one to eight rows a page; the plane's fewer never reaches nought");
        // A specific reader's bar carries the form too, so the plane lays it in the form it asked for.
        Assert.That(RoomBar.OfFrame(Frame(new[] { new PageLine("All tests pass", chip: "Agent says", claim: true) }), VoiceStage.Idle, new RoomForm(true, 4))!.Form.Narrow, Is.True);
    }

    /// <summary>
    /// A frame that pages itself keeps ONE pager when the general reader pages its actions too (gen-n2: Usage at Larger drew two
    /// Next Page): the reader's Next Page before its last page, the frame's own Next Page on the last, and where the actions fit
    /// on one page the frame's own alone.
    /// </summary>
    [Test]
    public void AFrameThatPagesItselfKeepsOnePagerWhenTheGeneralReaderPagesItToo()
    {
        var own = new Footer(Close).WithNext(new Prompt(Footer.NextPage, "Next Page", GlazeIcon.Next, PromptKind.NextPage));
        var many = RoomBar.OfFrame(Frame(Enumerable.Range(1, 7).Select(n => new PageLine("Do " + n, action: "do", key: n.ToString())).ToList(), own), VoiceStage.Idle)!;
        var pagers = new List<string>();
        var keys = new List<string?>();
        for (var page = 0; page < many.GeneralPart.Pages; page++)
        {
            var turning = many.Column.Where(action => action.Control.Action is RoomBar.TurnPage or Footer.NextPage).ToList();
            Assert.That(turning.Count, Is.EqualTo(1), "one pager on page " + (page + 1));
            pagers.Add(turning[0].Control.Action);
            keys.AddRange(many.Column.Where(action => action.Control.Action == "do").Select(action => action.Control.Key));
            Assert.That(many.Column.Count, Is.LessThanOrEqualTo(RoomBar.ColumnMost));
            many.Turn();
        }
        Assert.That(pagers.Last(), Is.EqualTo(Footer.NextPage), "the frame's own pager on the reader's last page");
        Assert.That(pagers.Take(pagers.Count - 1).All(action => action == RoomBar.TurnPage), Is.True);
        Assert.That(keys, Is.EqualTo(Enumerable.Range(1, 7).Select(n => n.ToString())));

        var few = RoomBar.OfFrame(Frame(new[] { new PageLine("Do 1", action: "do", key: "1") }, own), VoiceStage.Idle)!;
        Assert.That((few.GeneralPart.Pages, few.Column.Count(action => action.Control.Action == Footer.NextPage), few.Column.Any(action => action.Control.Action == RoomBar.TurnPage)),
            Is.EqualTo((1, 1, false)), "on one page, the frame's own pager alone");
    }

    /// <summary>
    /// A line that acts and carries a value or a quieter line is a pressed row of a card, so no part is lost on a button (AUDIT's
    /// 17:56Z MUST): its words, its value and its quieter line laid apart, and its press the line's own action and key; five rows a
    /// page; a label stands above the name (FIELD's row label), so a line with a label, a value and a quieter line keeps all of them.
    /// </summary>
    [Test]
    public void ALineThatActsWithAValueIsAPressedRowWithEveryPartAndItsOwnPress()
    {
        var frame = Frame(Enumerable.Range(1, 6).Select(n => new PageLine(LineWords.TheirName("App " + n), fact: LineWords.Ours("Up to " + (10 * n) + "% Left"),
            action: "open-limit", key: "limit-" + n, opens: true).WithUnder(LineWords.Ours("Seen 2 minutes ago. Resets at 18:40."))).ToList());
        var bar = RoomBar.OfFrame(frame, VoiceStage.Idle)!;
        Assert.That(bar.Card!.List.Count, Is.EqualTo(RoomCard.ListAPage), "five a page");
        var row = bar.Card.List[0];
        Assert.That((row.Name.Text, row.Name.AllTheirs, row.Note, row.Under!.Text, row.Press!.Action, row.Press.Key),
            Is.EqualTo(("App 1", true, (string?)"Up to 10% Left", "Seen 2 minutes ago. Resets at 18:40.", "open-limit", "limit-1")));
        Assert.That((bar.GeneralPart.Pages, bar.Whole), Is.EqualTo((2, false)));
        bar.Turn();
        Assert.That((bar.Card!.List.Single().Name.Text, bar.Whole), Is.EqualTo(("App 6", true)), "the sixth on the second page, whole once both were shown");
        Assert.That(bar.Card.AnyDot, Is.False);

        var labelled = RoomBar.OfFrame(Frame(new[] { new PageLine(LineWords.TheirName("Ember App"), fact: LineWords.Ours("Up to 60% Left"), action: "open-limit", key: "a", opens: true)
            .WithRowLabel("5-Hour Window").WithUnder(LineWords.Ours("Seen 2 minutes ago.")) }), VoiceStage.Idle)!;
        var limit = labelled.Card!.List.Single();
        Assert.That((limit.Label, limit.Name.Text, limit.Note, limit.Under!.Text, labelled.Whole, labelled.LostPart, bar.LostPart),
            Is.EqualTo(((string?)"5-Hour Window", "Ember App", (string?)"Up to 60% Left", "Seen 2 minutes ago.", true, false, false)),
            "all four parts of a limit on its row: the window above, the app, the amount, seen and resets under; nothing lost");
    }

    /// <summary>A card's pressed rows page by the form's rows a page too, so the plane's measure can shorten a card (FIELD's F100).</summary>
    [Test]
    public void ACardsPressedRowsPageByTheFormsRowsAPage()
    {
        var frame = Frame(Enumerable.Range(1, 4).Select(n => new PageLine("Setting " + n, fact: "On", action: "open", key: n.ToString(), opens: true)).ToList());
        Assert.That(RoomBar.OfFrame(frame, VoiceStage.Idle)!.Card!.List.Count, Is.EqualTo(4), "four on one card at full form");
        var two = RoomBar.OfFrame(frame, VoiceStage.Idle, new RoomForm(narrow: true, rowsAPage: 2))!;
        var keys = new List<string?>();
        for (var page = 0; page < two.GeneralPart.Pages; page++)
        {
            Assert.That(two.Card!.List.Count, Is.LessThanOrEqualTo(2));
            keys.AddRange(two.Card.List.Select(row => row.Press!.Key));
            two.Turn();
        }
        Assert.That((two.GeneralPart.Pages, keys), Is.EqualTo((2, new[] { "1", "2", "3", "4" })));
    }

    /// <summary>
    /// A page holds at most the spec's four actions IN ALL (BUILD-SPEC-v4 1.1, the lead's 19:41Z, AUDIT's 19:43Z): the main action,
    /// the ways out and the pager among them, fewer where the plane asks, and every action reached.
    /// </summary>
    [Test]
    public void AColumnHoldsTheSpecsFourActionsAPageInAllAndFewerWhereThePlaneAsks()
    {
        List<string?> Walk(RoomBar bar, int most)
        {
            var keys = new List<string?>();
            for (var page = 0; page < bar.GeneralPart.Pages; page++)
            {
                Assert.That(bar.Column.Count, Is.LessThanOrEqualTo(most), "page " + (page + 1) + ": " + string.Join(", ", bar.Column.Select(action => action.Words)));
                keys.AddRange(bar.Column.Where(action => action.Control.Action == "choose").Select(action => action.Control.Key));
                bar.Turn();
            }
            return keys;
        }
        var eight = Enumerable.Range(1, 8).Select(n => new PageLine("Choice " + n, action: "choose", key: n.ToString())).ToList();
        var all = Enumerable.Range(1, 8).Select(n => (string?)n.ToString()).ToList();
        Assert.That(Walk(RoomBar.OfFrame(Frame(eight), VoiceStage.Idle)!, RoomForm.SpecActions), Is.EqualTo(all), "Close among the four");
        var confirm = Footer.Confirm(new Footer(Close, farRight: new Prompt("go", "Go", GlazeIcon.Next)), PromptSlot.FarRight,
            new Prompt("yes", "Confirm", GlazeIcon.Next, PromptKind.Yes), new Prompt("cancel", "Cancel", GlazeIcon.Close, PromptKind.Cancel));
        Assert.That(Walk(RoomBar.OfFrame(Frame(eight, confirm), VoiceStage.Idle)!, RoomForm.SpecActions), Is.EqualTo(all), "Yes and Cancel among the four");
        Assert.That(Walk(RoomBar.OfFrame(Frame(eight), VoiceStage.Idle, RoomForm.Full.FewerActions())!, 3), Is.EqualTo(all), "fewer where the plane asks");
        Assert.That(new RoomForm(true, 8, 0).ActionsAPage, Is.EqualTo(1), "never fewer than one action a page");
    }

    /// <summary>
    /// Pages that only turn actions never hold a frame back from being read (JUDGE's 20:08Z: the shelf's Apply waited for a draw
    /// that never came while its Move buttons paged): the rows stand on every page, so the bar is whole from its first page; rows
    /// that page still need every page shown.
    /// </summary>
    [Test]
    public void PagesThatOnlyTurnActionsNeverHoldAFrameBackFromBeingRead()
    {
        var footer = new Footer(new Prompt(Footer.Close, "Cancel", GlazeIcon.Close, PromptKind.Close), farRight: new Prompt("apply", "Apply", GlazeIcon.Change, main: true));
        var moves = new[] { "Left", "Right", "Closer", "Farther", "Up", "Down" }.Select(each => new PageLine(each, action: "move", key: each, control: LineControl.Action)).ToList();
        var shelf = RoomBar.OfFrame(Frame(new[] { new PageLine("Hold Move Shelf to drag it.") }.Concat(moves).ToList(), footer), VoiceStage.Idle)!;
        Assert.That(shelf.GeneralPart.Pages, Is.GreaterThan(1), "the moves page");
        Assert.That(shelf.Whole, Is.True, "nothing to read is hidden on a later page, so Apply is not held");
        Assert.That(shelf.Column[0].Control.Action, Is.EqualTo("apply"), "Apply on the first page, first");
        var rows = RoomBar.OfFrame(Frame(Enumerable.Range(1, 12).Select(n => new PageLine("Line " + n)).ToList()), VoiceStage.Idle)!;
        Assert.That(rows.Whole, Is.False, "rows on a later page still hold it");
    }

    /// <summary>An action that carries words to read counts as content (AUDIT's 20:10Z): a later page holding a line with its description (a pressed row) holds the frame back until shown.</summary>
    [Test]
    public void AnActionThatCarriesWordsToReadHoldsItsPageAsContent()
    {
        var answers = Enumerable.Range(1, 6).Select(n => new PageLine("Answer " + n, action: "answer", key: n.ToString())
            .WithUnder(n == 5 ? LineWords.Ours("Keeps the tail short and steady.") : null)).ToList();
        var bar = RoomBar.OfFrame(Frame(answers), VoiceStage.Idle)!;
        Assert.That(bar.GeneralPart.Pages, Is.GreaterThan(1));
        Assert.That(bar.Whole, Is.False, "a later page holds an answer's description");
        while (bar.GeneralPart.Page < bar.GeneralPart.Pages - 1) bar.Turn();
        Assert.That(bar.Whole, Is.True, "shown, now read");
    }

    /// <summary>
    /// Eight lines means eight lines (the lead's 2026-11-13T20:15Z): with the plane's line measure the general reader fills a page
    /// to eight LINES, a row's label counted; a row of forty lines, and an agent's longest unbroken words, stand in their own
    /// parts, each part keeping the row's label, every line reached once, and no page passes eight lines; the frame counts as read
    /// only once every part was shown.
    /// </summary>
    [Test]
    public void EightLinesMeansEightLinesAndALongRowStandsInItsOwnPartsWithItsLabel()
    {
        int Measure(LineWords words) => System.Math.Max(1, (words.Text.Length + 29) / 30);
        var form = RoomForm.Full.Measured((row, indented) => (row.Label == null ? 0 : Measure(LineWords.Ours(row.Label))) + Measure(row.Words));
        int LinesOn(IReadOnlyList<RoomRow> rows) => rows.Sum(row => (row.Label == null ? 0 : Measure(LineWords.Ours(row.Label))) + (row.Lines ?? Measure(row.Words)));

        var forty = new string('a', 30 * 40 - 1);
        var agents = new string('b', 30 * 12 - 1);
        var frame = Frame(new[]
        {
            new PageLine("A short line"),
            new PageLine(LineWords.Ours("Notes"), fact: LineWords.Theirs(forty)),
            new PageLine(LineWords.Ours("Link"), fact: LineWords.Theirs(agents)),
        });
        var bar = RoomBar.OfFrame(frame, VoiceStage.Idle, form)!;
        var parts = new Dictionary<string, List<(int From, int Lines)>>();
        for (var page = 0; page < bar.GeneralPart.Pages; page++)
        {
            var rows = (IReadOnlyList<RoomRow>?)bar.Facts ?? new[] { new RoomRow(bar.Body) };
            Assert.That(LinesOn(rows), Is.LessThanOrEqualTo(RoomForm.SpecLines), "page " + (page + 1) + " passes eight lines");
            foreach (var row in rows.Where(row => row.Label != null))
            {
                if (!parts.ContainsKey(row.Label!)) parts[row.Label!] = new List<(int, int)>();
                parts[row.Label!].Add((row.FromLine, row.Lines ?? Measure(row.Words)));
            }
            if (page < bar.GeneralPart.Pages - 1) Assert.That(bar.Whole, Is.False, "parts still to read");
            bar.Turn();
        }
        foreach (var (label, total) in new[] { ("Notes", 40), ("Link", 12) })
        {
            var seen = parts[label];
            Assert.That(seen.Count, Is.GreaterThan(1), label + " stands in parts, its label on each");
            Assert.That(seen.Select(part => part.From), Is.EqualTo(seen.Select((part, at) => seen.Take(at).Sum(each => each.Lines))), label + "'s parts follow one another");
            Assert.That(seen.Sum(part => part.Lines), Is.EqualTo(total), "every line of " + label + " reached once");
        }

        // Without a measure, rows a page as before.
        Assert.That(RoomBar.OfFrame(frame, VoiceStage.Idle)!.GeneralPart.Pages, Is.EqualTo(1));
    }

    /// <summary>
    /// A marked page lays its rows' words a mark's indent narrower (AUDIT's 20:20Z MUST): the general reader measures a frame's rows
    /// indented wherever any of them carries a mark. It lays no mark of its own (marks are a start's steps', a specific reader's),
    /// so its pages are measured, and laid, at the full words width; the measure is asked so, and no page passes eight lines.
    /// </summary>
    [Test]
    public void TheGeneralReaderMeasuresRowsAtTheWidthTheyAreLaid()
    {
        var asked = new List<bool>();
        int Measure(LineWords words, bool indented) => System.Math.Max(1, (words.Text.Length + (indented ? 27 : 29)) / (indented ? 28 : 30));
        int MeasureRow(RoomRow row, bool indented)
        {
            asked.Add(indented);
            return (row.Label == null ? 0 : Measure(LineWords.Ours(row.Label), indented)) + Measure(row.Words, indented);
        }
        var row = new string('c', 60);
        var frame = Frame(new[] { new PageLine("Checks") }.Concat(Enumerable.Range(1, 6).Select(n => new PageLine(LineWords.Theirs(row)))).ToList());
        var bar = RoomBar.OfFrame(frame, VoiceStage.Idle, RoomForm.Full.Measured(MeasureRow))!;
        Assert.That(asked.Count, Is.GreaterThan(0));
        Assert.That(asked.All(indented => !indented), Is.True, "no mark on the general reader's rows, so none is measured indented");
        for (var page = 0; page < bar.GeneralPart.Pages; page++)
        {
            var rows = (IReadOnlyList<RoomRow>?)bar.Facts ?? new[] { new RoomRow(bar.Body) };
            Assert.That(RoomRows.Marked(rows), Is.False);
            Assert.That(rows.Sum(each => each.Lines ?? Measure(each.Words, false)), Is.LessThanOrEqualTo(RoomForm.SpecLines), "page " + (page + 1));
            bar.Turn();
        }
    }
}

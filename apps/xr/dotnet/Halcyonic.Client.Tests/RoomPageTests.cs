using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The redesign's general rule for a frame with no moment of its own (RoomBar.OfFrame): Halcyonic's lines in the panel,
/// the frame's actions in the column, one line of outside words on the band; and null wherever it could not show
/// everything the frame holds, so the older view draws the frame.
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

    [Test]
    public void WhereItCouldNotShowEverythingItLeavesTheFrameToTheOlderView()
    {
        PageLine Words(string words) => new(words);
        Assert.That(RoomBar.OfFrame(Frame(new[] { new PageLine("Report", action: "open-report", opens: true, chosen: true) }, side: new SidePanel("Report", lines: new[] { Words("Whole") })), VoiceStage.Idle), Is.Null, "a side panel");
        Assert.That(RoomBar.OfFrame(Frame(new[] { new PageLine("Idea", control: LineControl.Input, action: "edit") }), VoiceStage.Idle), Is.Null, "a field");
        Assert.That(RoomBar.OfFrame(Frame(new[] { new PageLine("A long text", fromRow: 8, rows: 8) }), VoiceStage.Idle), Is.Null, "a text cut in parts");
        Assert.That(RoomBar.OfFrame(Frame(new[] { new PageLine("Tests", fact: "12 Passed") }), VoiceStage.Idle), Is.Null, "a fact beside a line");
        Assert.That(RoomBar.OfFrame(Frame(new[] { new PageLine("Dark", action: "answer", available: false) }), VoiceStage.Idle), Is.Null, "a line that acts but cannot now");
        Assert.That(RoomBar.OfFrame(Frame(new[] { Words("Say It") }, new Footer(Close, secondary: new Prompt("hold", "Hold to Talk", GlazeIcon.Next, holds: true))), VoiceStage.Idle), Is.Null, "a held prompt");
        Assert.That(RoomBar.OfFrame(Frame(new[] { new PageLine("first", wordsAreData: true), new PageLine("second", wordsAreData: true) }), VoiceStage.Idle), Is.Null, "two lines of outside words");
        Assert.That(RoomBar.OfFrame(Frame(new PageLine[0], new Footer()), VoiceStage.Idle), Is.Null, "nothing at all");
        // An agent's claim is outside words whoever spells it.
        Assert.That(RoomBar.OfFrame(Frame(new[] { new PageLine("All tests pass", chip: "Agent says", claim: true) }), VoiceStage.Idle)!.Band, Is.EqualTo("All tests pass"));
        Assert.That(RoomBar.OfFrame(Frame(new[] { new PageLine(LineWords.Theirs("Pop the balloons")) }), VoiceStage.Idle)!.Band, Is.EqualTo("Pop the balloons"));
        // A name from outside inside Halcyonic's own sentence stays in the panel, as a file's name does in Changes.
        var mixed = RoomBar.OfFrame(Frame(new[] { new PageLine(LineWords.Ours("Folder · ") + LineWords.TheirName("balloon-pop")) }), VoiceStage.Idle)!;
        Assert.That((mixed.Band, mixed.Body.Text), Is.EqualTo(((string?)null, "Folder · balloon-pop")));
    }
}

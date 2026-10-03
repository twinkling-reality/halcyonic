using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>A file's Waiting page: an approval, the whole request before Yes, and the agent's question.</summary>
public class FileWaitingTests
{
    private readonly CommandFactory factory = new(Samples.Client);

    private static readonly AnswerRoom Room = new(4);

    private static MenuFrame Screen(WorkspacePresentation workspace, WorkspaceSteering steering, FileScreen screen) =>
        FileScreens.Screen(workspace, steering, screen, Room);

    private static WorkspacePresentation Approving(WaitingWork work) =>
        FileScreensTests.Offering(work.Present(), WorkspaceAction.Approve, WorkspaceAction.Deny, WorkspaceAction.Interrupt);

    [Test]
    public void AnApprovalShowsWhatItWantsInTheWaitingColourWithApproveAsTheMainActionAndDenyBesideIt()
    {
        var work = new WaitingWork();
        var workspace = Approving(work);
        var screen = new FileScreen { Section = FileScreens.Opening(workspace) };
        var frame = Screen(workspace, new WorkspaceSteering(factory), screen);
        Assert.That(screen.Section, Is.EqualTo(FileSection.Waiting), "the file opens on what waits");
        Assert.That(frame.Lines.Select(line => (line.Words, line.Tone)).Take(2), Is.EqualTo(new[]
        {
            ("It wants to run a command:", LineTone.Waiting),
            ("Run the migration", LineTone.Primary),
        }));
        Assert.That(frame.Lines[1].Rows, Is.EqualTo(2), "as much of the request as fits beside the source line; Approve shows it whole");
        Assert.That(frame.Lines.Sum(line => line.Rows) + 1, Is.LessThanOrEqualTo(Room.Rows), "the source line takes one of the page's rows");
        var roomy = FileScreens.Screen(workspace, new WorkspaceSteering(factory), screen, new AnswerRoom(8));
        Assert.That(roomy.Lines[1].Rows, Is.EqualTo(FileScreens.RequestRows));
        Assert.That(roomy.Lines.Sum(line => line.Rows) + 1, Is.LessThanOrEqualTo(8), "the notes fill only the room left");
        Assert.That(FileScreensTests.Slots(frame.Footer), Is.EqualTo(new[] { Footer.Close, FileScreens.Stop, null, FileScreens.Deny, FileScreens.Approve }));
        Assert.That(frame.Footer[PromptSlot.FarRight]!.DrawnAsMain, Is.True);
        Assert.That(frame.Footer[PromptSlot.Secondary]!.DrawnAsMain, Is.False);
        Assert.That(frame.Lines.Any(line => line.Pressable), Is.False, "nothing on the page sends anything");
        Assert.That(frame.Source, Is.EqualTo("As the agent reported it"), "the work's own state names the agent, never an app's name");
    }

    [Test]
    public void ApprovingShowsTheWholeRequestInPartsAndYesOnlyOnceTheLastPartHasShown()
    {
        var work = new WaitingWork();
        var workspace = Approving(work);
        var steering = new WorkspaceSteering(factory);
        var screen = new FileScreen();
        Assert.That(steering.Press(WorkspaceAction.Approve, workspace).Step, Is.EqualTo(SteeringStep.Confirm));

        var unmeasured = Screen(workspace, steering, screen);
        Assert.That(unmeasured.Footer[PromptSlot.Free], Is.Null, "before the layout measured the request, there is no Yes");
        Assert.That(unmeasured.Footer[PromptSlot.FarRight]!.Kind, Is.EqualTo(PromptKind.Cancel), "Cancel stands where Approve was");

        screen.ReadRequest(rows: 7, partRows: 3, steering);
        Assert.That(screen.RequestParts, Is.EqualTo(3));
        var first = Screen(workspace, steering, screen);
        Assert.That(first.Lines[0].Words, Is.EqualTo("bash: Run the migration"));
        Assert.That((first.Lines[0].Rows, first.Lines[0].FromRow), Is.EqualTo((3, 0)));
        Assert.That((first.Lines[1].Words, first.Lines[1].Action, first.Lines[1].Key), Is.EqualTo(("Next part, 2 of 3", FileScreens.NextPart, FileScreens.RequestKey)));
        Assert.That(first.Lines[2].Words, Is.EqualTo(WorkspaceText.ReadRequestFirst));
        Assert.That(first.Footer[PromptSlot.Free], Is.Null);
        Assert.That(steering.Confirm(workspace).Step, Is.EqualTo(SteeringStep.Explain), "nothing is sent before the whole request shows");
        Assert.That(steering.Armed, Is.EqualTo(WorkspaceAction.Approve), "and it stays armed, so the rest can still be read");

        screen.NextRequestPart(steering);
        var second = Screen(workspace, steering, screen);
        Assert.That((second.Lines[0].FromRow, second.Lines[1].Words), Is.EqualTo((3, "Next part, 3 of 3")));
        Assert.That(second.Footer[PromptSlot.Free], Is.Null);

        screen.NextRequestPart(steering);
        var last = Screen(workspace, steering, screen);
        Assert.That((last.Lines[0].FromRow, last.Lines[1].Words), Is.EqualTo((6, "First part, 1 of 3")));
        Assert.That(last.Footer[PromptSlot.Free]!.Words, Is.EqualTo("Yes, approve"));
        Assert.That(last.Footer[PromptSlot.Free]!.Kind, Is.EqualTo(PromptKind.Yes));
        Assert.That(FileScreensTests.Slots(last.Footer), Is.EqualTo(new[] { Footer.Close, null, FileScreens.Yes, null, FileScreens.Cancel }),
            "while it asks, the other actions step aside");
        Assert.That(last.Lines[2].Words, Is.EqualTo("Approve the request above?"));

        screen.NextRequestPart(steering);
        var again = Screen(workspace, steering, screen);
        Assert.That(again.Lines[0].FromRow, Is.Zero, "from the last part the row goes back to the first");
        Assert.That(again.Footer[PromptSlot.Free], Is.Not.Null, "and the whole request has still been shown");
    }

    [Test]
    public void YesSendsExactlyOnce()
    {
        var work = new WaitingWork();
        var workspace = Approving(work);
        var steering = new WorkspaceSteering(factory);
        var screen = new FileScreen();
        steering.Press(WorkspaceAction.Approve, workspace);
        screen.ReadRequest(rows: 2, partRows: 3, steering);
        Assert.That(Screen(workspace, steering, screen).Footer[PromptSlot.Free], Is.Not.Null, "a request of one part shows whole at once");
        var sent = steering.Confirm(workspace);
        Assert.That(sent.Step, Is.EqualTo(SteeringStep.Send));
        Assert.That(sent.Command, Is.InstanceOf<ExecutionRespondToApprovalCommand>());
        Assert.That(steering.Confirm(workspace).Step, Is.EqualTo(SteeringStep.None), "a second Yes sends nothing");
        var after = Screen(workspace, steering, screen);
        Assert.That(after.Footer.Confirming, Is.False);
        Assert.That(after.Footer[PromptSlot.Free], Is.Null);
    }

    [Test]
    public void ArmedAgainForTheSameRequestItIsReadAfreshFromItsFirstPart()
    {
        var work = new WaitingWork();
        var workspace = Approving(work);
        var steering = new WorkspaceSteering(factory);
        var screen = new FileScreen();
        steering.Press(WorkspaceAction.Approve, workspace);
        screen.ReadRequest(rows: 6, partRows: 3, steering);
        screen.NextRequestPart(steering);
        Assert.That(Screen(workspace, steering, screen).Footer[PromptSlot.Free], Is.Not.Null);

        // Cancelled and pressed again before the page redraws: the same request, the same size.
        steering.Cancel();
        steering.Press(WorkspaceAction.Approve, workspace);
        screen.ReadRequest(rows: 6, partRows: 3, steering);
        var frame = Screen(workspace, steering, screen);
        Assert.That(frame.Lines[0].FromRow, Is.Zero, "a new confirmation starts at the first part");
        Assert.That(frame.Footer[PromptSlot.Free], Is.Null, "no earlier reading counts towards it");
        Assert.That(steering.Confirm(workspace).Step, Is.EqualTo(SteeringStep.Explain));
    }

    [Test]
    public void APartRowFromAnEarlierConfirmationTurnsNothing()
    {
        var work = new WaitingWork();
        var workspace = Approving(work);
        var steering = new WorkspaceSteering(factory);
        var screen = new FileScreen();
        steering.Press(WorkspaceAction.Approve, workspace);
        screen.ReadRequest(rows: 6, partRows: 3, steering);
        steering.Cancel();
        steering.Press(WorkspaceAction.Approve, workspace);
        screen.NextRequestPart(steering);
        Assert.That((screen.RequestPart, steering.WholeRequestShown), Is.EqualTo((0, false)), "a press read for the last confirmation is not this one's");
    }

    [Test]
    public void DenyingAsksWithCancelInDenysPlace()
    {
        var work = new WaitingWork();
        var workspace = Approving(work);
        var steering = new WorkspaceSteering(factory);
        var screen = new FileScreen();
        steering.Press(WorkspaceAction.Deny, workspace);
        screen.ReadRequest(rows: 6, partRows: 3, steering);
        var frame = Screen(workspace, steering, screen);
        Assert.That(FileScreensTests.Slots(frame.Footer), Is.EqualTo(new[] { Footer.Close, null, FileScreens.Yes, FileScreens.Cancel, null }));
        Assert.That(frame.Footer[PromptSlot.Free]!.Words, Is.EqualTo("Yes, deny"));
        Assert.That(frame.Lines.Last().Words, Is.EqualTo("Deny the request above?"));
    }

    [Test]
    public void WaitingReadsOnlyTheWorksOwnStateNeverAnAnswerStillBeingRead()
    {
        var work = new WaitingWork();
        var workspace = Approving(work);
        var screen = new FileScreen { WhatChanged = null, Checked = null };
        var frame = Screen(workspace, new WorkspaceSteering(factory), screen);
        Assert.That(frame.Lines[0].Words, Is.EqualTo("It wants to run a command:"));
        Assert.That(frame.Lines.Select(line => line.Words), Has.None.Contains("Still reading"));
    }

    [Test]
    public void TheSectionsCarryWaitingsAmberDotOnlyWhileSomethingWaits()
    {
        var work = new WaitingWork();
        var waiting = Screen(Approving(work), new WorkspaceSteering(factory), new FileScreen());
        Assert.That(waiting.Sections.Select(section => (section.Words, section.Waits)), Is.EqualTo(new[]
        {
            ("Waiting", true), ("Activity", false), ("Changes", false), ("Checks", false),
        }));
        work.Change(execution => execution.PendingApprovals.Clear(), WorkstreamStatus.Running);
        var workspace = work.Present();
        Assert.That(FileScreens.Opening(workspace), Is.EqualTo(FileSection.Activity));
        var screen = new FileScreen { Section = FileSection.Waiting };
        var calm = Screen(workspace, new WorkspaceSteering(factory), screen);
        Assert.That(calm.Sections.Any(section => section.Waits), Is.False);
        Assert.That(calm.Lines.Select(line => line.Words), Is.EqualTo(new[] { FileScreens.NothingWaits }));
        Assert.That(FileScreensTests.Slots(calm.Footer), Is.EqualTo(new[] { Footer.Close, null, null, null, null }));
    }
}

using System;
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

    /// <summary>The whole request the waiting work's approval answers, as the confirmation shows it.</summary>
    private const string Migration = "bash: Run the migration";

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

    private DateTimeOffset clock = DateTimeOffset.Parse("2026-10-02T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The view draws the part showing, now.</summary>
    private void Draw(FileScreen screen, WorkspaceSteering steering) => screen.RequestDrawn(screen.RequestPart, steering, clock);

    /// <summary>The view draws the part showing, and a second later the person presses its row.</summary>
    private void Turn(FileScreen screen, WorkspaceSteering steering)
    {
        Draw(screen, steering);
        clock += TimeSpan.FromSeconds(1);
        screen.NextRequestPart(steering, clock);
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

        screen.ReadRequest(Migration, 7, 3, steering);
        Assert.That(screen.RequestParts, Is.EqualTo(3));
        var first = Screen(workspace, steering, screen);
        Assert.That(first.Lines[0].Words, Is.EqualTo(Migration));
        Assert.That((first.Lines[0].Rows, first.Lines[0].FromRow), Is.EqualTo((3, (int?)0)));
        Assert.That((first.Lines[1].Words, first.Lines[1].Action, first.Lines[1].Key), Is.EqualTo(("Next part, 2 of 3", FileScreens.NextPart, FileScreens.RequestKey)));
        Assert.That(first.Lines[2].Words, Is.EqualTo(WorkspaceText.ReadRequestFirst));
        Assert.That(first.Footer[PromptSlot.Free], Is.Null);
        Draw(screen, steering);
        Assert.That(steering.Confirm(workspace).Step, Is.EqualTo(SteeringStep.Explain), "nothing is sent before the whole request shows");
        Assert.That(steering.Armed, Is.EqualTo(WorkspaceAction.Approve), "and it stays armed, so the rest can still be read");

        Turn(screen, steering);
        var second = Screen(workspace, steering, screen);
        Assert.That((second.Lines[0].FromRow, second.Lines[1].Words), Is.EqualTo(((int?)3, "Next part, 3 of 3")));
        Assert.That(second.Footer[PromptSlot.Free], Is.Null);

        Turn(screen, steering);
        Assert.That(Screen(workspace, steering, screen).Footer[PromptSlot.Free], Is.Null, "the last part on the page is not yet the last part seen");
        Draw(screen, steering);
        var last = Screen(workspace, steering, screen);
        Assert.That((last.Lines[0].FromRow, last.Lines[1].Words), Is.EqualTo(((int?)6, "First part, 1 of 3")));
        Assert.That(last.Footer[PromptSlot.Free]!.Words, Is.EqualTo("Yes, approve"));
        Assert.That(last.Footer[PromptSlot.Free]!.Kind, Is.EqualTo(PromptKind.Yes));
        Assert.That(FileScreensTests.Slots(last.Footer), Is.EqualTo(new[] { Footer.Close, null, FileScreens.Yes, null, FileScreens.Cancel }),
            "while it asks, the other actions step aside");
        Assert.That(last.Lines[2].Words, Is.EqualTo("Approve the request above?"));

        Turn(screen, steering);
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
        screen.ReadRequest(Migration, 2, 3, steering);
        Draw(screen, steering);
        Assert.That(Screen(workspace, steering, screen).Footer[PromptSlot.Free], Is.Not.Null, "a request of one part shows whole once drawn");
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
        screen.ReadRequest(Migration, 6, 3, steering);
        Turn(screen, steering);
        Draw(screen, steering);
        Assert.That(Screen(workspace, steering, screen).Footer[PromptSlot.Free], Is.Not.Null);

        // Cancelled and pressed again before the page redraws: the same request, the same size.
        steering.Cancel();
        steering.Press(WorkspaceAction.Approve, workspace);
        screen.ReadRequest(Migration, 6, 3, steering);
        var frame = Screen(workspace, steering, screen);
        Assert.That(frame.Lines[0].FromRow, Is.Zero, "a new confirmation starts at the first part");
        Assert.That(frame.Footer[PromptSlot.Free], Is.Null, "no earlier reading counts towards it");
        Assert.That(steering.Confirm(workspace).Step, Is.EqualTo(SteeringStep.Explain));
    }

    [Test]
    public void APartRowOrADrawFromAnEarlierConfirmationCountsForNothing()
    {
        var work = new WaitingWork();
        var workspace = Approving(work);
        var steering = new WorkspaceSteering(factory);
        var screen = new FileScreen();
        steering.Press(WorkspaceAction.Approve, workspace);
        screen.ReadRequest(Migration, 6, 3, steering);
        Draw(screen, steering);
        steering.Cancel();
        steering.Press(WorkspaceAction.Approve, workspace);
        screen.NextRequestPart(steering, clock + TimeSpan.FromSeconds(5));
        Draw(screen, steering);
        Assert.That((screen.RequestPart, steering.WholeRequestShown), Is.EqualTo((0, false)), "a press or a draw for the last confirmation is not this one's");
    }

    [Test]
    public void AMeasurementFromAnEarlierConfirmationCountsForNothing()
    {
        var work = new WaitingWork();
        var workspace = Approving(work);
        var steering = new WorkspaceSteering(factory);
        var screen = new FileScreen();
        steering.Press(WorkspaceAction.Approve, workspace);
        screen.ReadRequest(Migration, 7, 3, steering);
        for (var part = 0; part < 3; part++) Turn(screen, steering);
        steering.Cancel();
        steering.Press(WorkspaceAction.Approve, workspace);
        var frame = Screen(workspace, steering, screen);
        Assert.That(frame.Footer[PromptSlot.Free], Is.Null, "no Yes before this confirmation's own measurement");
        Assert.That((frame.Lines[0].FromRow, frame.Lines[0].Rows), Is.EqualTo(((int?)0, FileScreens.RequestRows)), "and the request from its start");
    }

    [Test]
    public void ARequestThatReadsDifferentlyUnderTheSameApprovalLapsesItsConfirmation()
    {
        var work = new WaitingWork();
        var workspace = Approving(work);
        var steering = new WorkspaceSteering(factory);
        var screen = new FileScreen();
        var other = new WorkspaceSteering(factory);
        steering.Press(WorkspaceAction.Approve, workspace);
        other.Press(WorkspaceAction.Approve, workspace);
        screen.ReadRequest(Migration, 2, 3, steering);
        Draw(screen, steering);
        Assert.That(Screen(workspace, steering, screen).Footer[PromptSlot.Free], Is.Not.Null);

        work.Change(execution => execution.PendingApprovals[0] = WaitingWork.Approval(WaitingWork.ApprovalId, "Drop the database", Samples.Time));
        var changed = Approving(work);
        Assert.That(other.Confirm(changed).Message, Is.EqualTo("Nothing was sent: the request changed. Read it again."), "Yes itself lapses");
        var frame = Screen(changed, steering, screen);
        Assert.That(frame.Footer.Confirming, Is.False, "dropped before the file is drawn");
        Assert.That(frame.Lines.Last().Words, Is.EqualTo("Nothing was sent: the request changed. Read it again."));
        Assert.That(steering.Armed, Is.Null);
    }

    [Test]
    public void MeasuredAgainARequestDrawnInPartIsReadAgainAndOneDrawnWholeStaysRead()
    {
        var work = new WaitingWork();
        var workspace = Approving(work);

        var steering = new WorkspaceSteering(factory);
        var screen = new FileScreen();
        steering.Press(WorkspaceAction.Approve, workspace);
        screen.ReadRequest(Migration, 9, 3, steering);
        Turn(screen, steering);
        Draw(screen, steering);
        screen.ReadRequest(Migration, 6, 3, steering);
        var narrower = Screen(workspace, steering, screen);
        Assert.That((narrower.Lines[0].FromRow, narrower.Footer[PromptSlot.Free], steering.WholeRequestShown), Is.EqualTo(((int?)0, (Prompt?)null, false)),
            "two of three parts drawn, then laid out in two: it is read again from the start");
        Turn(screen, steering);
        Draw(screen, steering);
        Assert.That(Screen(workspace, steering, screen).Footer[PromptSlot.Free], Is.Not.Null);

        screen.ReadRequest(Migration, 9, 3, steering);
        var wider = Screen(workspace, steering, screen);
        Assert.That((wider.Lines[0].FromRow, wider.Footer[PromptSlot.Free] != null), Is.EqualTo(((int?)6, true)),
            "drawn whole, it stays read, showing its last part");
    }

    [Test]
    public void APartCountsOnlyOnceTheViewDrewItAndTheWholeOnlyOnceEveryPartHas()
    {
        var work = new WaitingWork();
        var workspace = Approving(work);
        var steering = new WorkspaceSteering(factory);
        var screen = new FileScreen();
        steering.Press(WorkspaceAction.Approve, workspace);
        screen.ReadRequest(Migration, 7, 3, steering);
        screen.NextRequestPart(steering, clock);
        Assert.That(screen.RequestPart, Is.Zero, "the next part waits until this one has been drawn");
        Screen(workspace, steering, screen);
        Assert.That(steering.WholeRequestShown, Is.False, "building the page draws nothing");
        Turn(screen, steering);
        Turn(screen, steering);
        Draw(screen, steering);
        Assert.That(steering.WholeRequestShown, Is.True, "every part was drawn");
    }

    [Test]
    public void ADoublePressCannotSkipAPartAlmostUnseen()
    {
        var work = new WaitingWork();
        var workspace = Approving(work);
        var steering = new WorkspaceSteering(factory);
        var screen = new FileScreen();
        steering.Press(WorkspaceAction.Approve, workspace);
        screen.ReadRequest(Migration, 7, 3, steering);
        Draw(screen, steering);
        screen.NextRequestPart(steering, clock + TimeSpan.FromSeconds(0.5));
        Assert.That(screen.RequestPart, Is.EqualTo(1));
        screen.RequestDrawn(1, steering, clock + TimeSpan.FromSeconds(0.55));
        screen.NextRequestPart(steering, clock + TimeSpan.FromSeconds(0.7));
        Assert.That(screen.RequestPart, Is.EqualTo(1), "a second press 0.15 s after its part was drawn turns nothing");
        screen.NextRequestPart(steering, clock + TimeSpan.FromSeconds(1.0));
        Assert.That(screen.RequestPart, Is.EqualTo(2), "0.45 s after, it does");
    }

    [Test]
    public void WhileADecisionSentMayStillTakeEffectNeitherApproveNorDenyIsOfferedAgain()
    {
        var work = new WaitingWork();
        var steering = new WorkspaceSteering(factory);
        var screen = new FileScreen();
        var before = work.Present();
        steering.Press(WorkspaceAction.Approve, before);
        screen.ReadRequest(Migration, 2, 3, steering);
        Draw(screen, steering);
        Screen(before, steering, screen);
        var sent = steering.Confirm(before);
        Assert.That(sent.Step, Is.EqualTo(SteeringStep.Send));

        var submissions = new CommandSubmissions();
        _ = submissions.SubmitAsync(_ => new TaskCompletionSource<CommandAckMessage>().Task, sent.Command!, "e1");
        var workspace = work.Present(submissions: submissions);
        Assert.That(workspace.Actions, Has.None.EqualTo(WorkspaceAction.Approve).And.None.EqualTo(WorkspaceAction.Deny));
        Assert.That(workspace.ApprovalInFlight, Is.True);
        var frame = Screen(workspace, steering, screen);
        var held = frame.Footer[PromptSlot.FarRight]!;
        Assert.That((held.Words, held.Available, held.Reason), Is.EqualTo((WorkspaceText.Sent, false, (string?)FileScreens.SentWaiting)));
        Assert.That(frame.Footer[PromptSlot.Secondary], Is.Null, "no Deny to race the approval on its way");
        Assert.That(steering.Press(WorkspaceAction.Deny, workspace).Step, Is.EqualTo(SteeringStep.Explain), "and the steering refuses one too");
    }

    [Test]
    public void StopPressedOnWaitingAsksThereAndWhateverIsArmedAsksOnEverySection()
    {
        var work = new WaitingWork();
        var workspace = Approving(work);
        var steering = new WorkspaceSteering(factory);
        var screen = new FileScreen { Section = FileSection.Waiting };
        Assert.That(steering.Press(WorkspaceAction.Interrupt, workspace).Step, Is.EqualTo(SteeringStep.Confirm));
        foreach (FileSection section in System.Enum.GetValues(typeof(FileSection)))
        {
            screen.Section = section;
            var frame = Screen(workspace, steering, screen);
            Assert.That(FileScreensTests.Slots(frame.Footer), Is.EqualTo(new[] { Footer.Close, FileScreens.Cancel, FileScreens.Yes, null, null }), section.ToString());
            Assert.That(frame.Footer[PromptSlot.Free]!.Words, Is.EqualTo("Yes, stop"));
            Assert.That(frame.Lines.Single().Words, Does.StartWith("Stop what it's doing now?"));
            Assert.That(frame.Sections.Single(each => each.Chosen).Key, Is.EqualTo(FileScreens.Key(section)), "the section stays where the person put it");
        }
        steering.Cancel();
        screen.Section = FileSection.Waiting;
        Assert.That(Screen(workspace, steering, screen).Footer.Confirming, Is.False, "cancelled, the page is the request again");
    }

    [Test]
    public void DenyingShowsYesAtOnceSoAPersonCanRefuseFromTheFirstPart()
    {
        var work = new WaitingWork();
        var workspace = Approving(work);
        var steering = new WorkspaceSteering(factory);
        var screen = new FileScreen();
        steering.Press(WorkspaceAction.Deny, workspace);
        screen.ReadRequest(Migration, 6, 3, steering);
        var frame = Screen(workspace, steering, screen);
        Assert.That((frame.Lines[0].FromRow, frame.Lines[1].Words), Is.EqualTo(((int?)0, "Next part, 2 of 2")), "on the first of two parts");
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

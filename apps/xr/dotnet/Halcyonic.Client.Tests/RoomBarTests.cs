using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The redesign's instruction (DESIGN's FINISH-3, 2026-11-05) over the frames a task's file draws: the row at rest, Listening,
/// the typed draft and the heard instruction's confirmation as moments, each action the file's own control with its own words,
/// and an action that cannot act never drawn.
/// </summary>
public class RoomBarTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-09T09:00:00Z");
    private static readonly ConnectionStatus Live = new(ConnectionPhase.Live);
    private static readonly KitSpineSection[] Spine = { new(new KitRowModel[0]) };
    private readonly CommandFactory factory = new(Samples.Client);

    private static WorkspacePresentation Running(params WorkspaceAction[] offered)
    {
        var work = new WaitingWork();
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Running; }, WorkstreamStatus.Running);
        var running = work.Present();
        return new WorkspacePresentation(running.Character, running.Objective, running.Execution, running.Runtime, offered,
            offered.Where(running.RequiresConfirmation).ToList(), running.Commands, new ActivityEntry[0]);
    }

    private static KitFileBinding Bind(WorkspacePresentation workspace, WorkspaceSteering steering, FileScreen screen, ConnectionStatus? connection = null)
    {
        var frame = FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4));
        var binding = KitFileBinding.Of(frame, steering.Armed, workspace, screen.Instruction.Open ? screen.Instruction.Words : "", connection ?? Live,
            "Storefront", Spine, roomDesign: true, draftSpoken: screen.Instruction.Open && screen.Instruction.Spoken);
        Assert.That(binding, Is.Not.Null, "the redesign binds the frame");
        return binding!;
    }

    private static IEnumerable<(KitPlace, string, bool)> Actions(IReadOnlyList<RoomAction> actions) => actions.Select(action => (action.Place, action.Words, action.Main));

    [Test]
    public void AtRestHoldToTalkAndTypeFloatWithNoFieldAndHeldItReadsListening()
    {
        var workspace = Running(WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var binding = Bind(workspace, new WorkspaceSteering(factory), new FileScreen { Section = FileSection.Activity, Speak = true });
        var rest = RoomBar.Of(binding, VoiceStage.Idle, sentAwaitingStart: false);
        Assert.That((rest.State, rest.Pair), Is.EqualTo((RoomBarState.Rest, false)));
        Assert.That(Actions(rest.Row), Is.EqualTo(new[] { (KitPlace.Talk, "Hold to Talk", true), (KitPlace.Field, "Type", false) }));
        Assert.That(rest.Row[1].Control.Action, Is.EqualTo(FileScreens.TellIt), "Type opens the file's own instruction");

        var listening = RoomBar.Of(binding, VoiceStage.Listening, sentAwaitingStart: false);
        Assert.That(Actions(listening.Row), Is.EqualTo(new[] { (KitPlace.Talk, "Listening", true) }), "Type goes while it listens");
        Assert.That(Actions(RoomBar.Of(binding, VoiceStage.WritingDown, false).Row), Is.EqualTo(new[] { (KitPlace.Talk, "Transcribing", true) }));

        var sent = RoomBar.Of(binding, VoiceStage.Idle, sentAwaitingStart: true);
        Assert.That((sent.State, sent.Words.Text, sent.Row.Count), Is.EqualTo((RoomBarState.Sent, "Sent · Awaiting Start", 0)));
    }

    [Test]
    public void WithoutVoiceTypeStandsAloneAndWhereNothingCanBeWrittenItsReasonStandsAsWords()
    {
        var quiet = Bind(Running(WorkspaceAction.Instruct), new WorkspaceSteering(factory), new FileScreen { Section = FileSection.Activity, Speak = false });
        Assert.That(Actions(RoomBar.Of(quiet, VoiceStage.Idle, false).Row), Is.EqualTo(new[] { (KitPlace.Field, "Type", true) }));

        var closed = Bind(Running(WorkspaceAction.Interrupt), new WorkspaceSteering(factory), new FileScreen { Section = FileSection.Activity, Speak = true });
        var none = RoomBar.Of(closed, VoiceStage.Idle, false);
        Assert.That(none.Row, Is.Empty, "no action the file does not offer");
        Assert.That(none.State, Is.AnyOf(RoomBarState.Words, RoomBarState.Hidden));
    }

    [Test]
    public void ATypedDraftIsTheFieldWithOnlyItsWayOutUntilAWordExistsAndNoSentenceBar()
    {
        var workspace = Running(WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var steering = new WorkspaceSteering(factory);
        var empty = new FileScreen { Section = FileSection.Activity, Speak = true };
        empty.Instruction.Restore(workspace.Execution!.ExecutionId, "", false, 0);
        var blank = RoomBar.Of(Bind(workspace, steering, empty), VoiceStage.Idle, false);
        Assert.That((blank.State, blank.Caret, blank.Body.Text, blank.Sentence), Is.EqualTo((RoomBarState.Draft, true, "", (string?)null)));
        Assert.That(blank.Column.Select(action => action.Place), Is.EqualTo(new[] { KitPlace.Close }), "only the way out before a word");
        Assert.That(blank.Field!.Action, Is.EqualTo(FileScreens.EditInstruction), "the panel is the field: a press opens the keyboard");

        var worded = new FileScreen { Section = FileSection.Activity, Speak = true };
        worded.Instruction.Restore(workspace.Execution!.ExecutionId, "Run the tests again.", false, 0);
        var draft = RoomBar.Of(Bind(workspace, steering, worded), VoiceStage.Idle, false);
        Assert.That(draft.Body.Text, Is.EqualTo("Run the tests again."));
        Assert.That(draft.Body.Runs[0].Theirs, Is.True, "the person's words");
        // Not read yet: Send cannot act, so it is not drawn; a typed draft has no sentence bar for the read rule (DESIGN 2.1).
        Assert.That(draft.Column.Any(action => action.Place == KitPlace.Send), Is.False);
        Assert.That((draft.Sentence, draft.Caret), Is.EqualTo(((string?)null, true)));
    }

    [Test]
    public void AHeardDraftAsksInItsSentenceBarAndOffersTalkAgainAsTheFramesOwnHoldToTalk()
    {
        var workspace = Running(WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var heard = new FileScreen { Section = FileSection.Activity, Speak = true };
        heard.Instruction.Restore(workspace.Execution!.ExecutionId, "Add a golden balloon worth five points.", true, 0);
        var binding = Bind(workspace, new WorkspaceSteering(factory), heard);
        var draft = RoomBar.Of(binding, VoiceStage.Idle, false);
        Assert.That((draft.State, draft.Body.Text, draft.Caret), Is.EqualTo((RoomBarState.Draft, "Add a golden balloon worth five points.", false)));
        // Read first, in the product's words; Send Instruction arrives once read, with "Send Heard Instruction?".
        Assert.That(draft.Sentence, Is.EqualTo(FileScreens.InstructionNotRead));
        Assert.That(Actions(draft.Column), Is.EqualTo(new[] { (KitPlace.Talk, "Talk Again", false), (KitPlace.Close, "Cancel", false) }));
        Assert.That(draft.Column[0].Control.Action, Is.EqualTo(FileScreens.HoldToTalk), "Talk Again is the frame's own hold to talk");
        // Held, it stays in its place, lit, reading Listening: the moment stands while it records afresh.
        var again = RoomBar.Of(binding, VoiceStage.Listening, false);
        Assert.That((again.State, again.Column[0].Place, again.Column[0].Words, again.Column[0].Main), Is.EqualTo((RoomBarState.Draft, KitPlace.Talk, "Listening", true)));
    }

    [Test]
    public void AnArmedInstructionIsAMomentItsWordsInThePanelAndConfirmArrivesOnceRead()
    {
        var workspace = Running(WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var steering = new WorkspaceSteering(factory, () => Now);
        Assert.That(steering.Spoken("Add a golden balloon worth five points.", workspace).Step, Is.EqualTo(SteeringStep.Confirm));
        var screen = new FileScreen { Section = FileSection.Activity, Speak = true };
        var request = steering.ToRead(workspace)!;
        screen.ReadRequest(request, 1, 3, steering);

        var unread = RoomBar.Of(Bind(workspace, steering, screen), VoiceStage.Idle, false);
        // The file's own question: read to the last part first, then whether to send what was heard.
        Assert.That((unread.State, unread.Part!.Words, unread.Body.Text, unread.Sentence), Is.EqualTo((RoomBarState.Confirm, request, request, EntryText.ReadToPart(1))));
        Assert.That(unread.Column.Select(action => action.Place), Is.EqualTo(new[] { KitPlace.Cancel }), "no Confirm before the words were drawn");

        screen.RequestDrawn(0, steering, Now);
        var read = RoomBar.Of(Bind(workspace, steering, screen), VoiceStage.Idle, false);
        Assert.That(read.Sentence, Is.EqualTo("Send Heard Instruction?"));
        Assert.That(Actions(read.Column), Is.EqualTo(new[] { (KitPlace.Yes, "Confirm", true), (KitPlace.Cancel, "Cancel", false) }));
        Assert.That(read.Column[0].Control.Action, Is.EqualTo(FileScreens.Yes), "Confirm is the confirmation's own Yes");
    }

    [Test]
    public void WithoutTheRedesignAnInstructionsConfirmationStaysTheFilesOwn()
    {
        var workspace = Running(WorkspaceAction.Instruct);
        var steering = new WorkspaceSteering(factory, () => Now);
        steering.Spoken("Add a test.", workspace);
        var frame = FileScreens.Screen(workspace, steering, new FileScreen { Section = FileSection.Activity }, new AnswerRoom(4));
        Assert.That(KitFileBinding.Of(frame, steering.Armed, workspace, "", Live, "Storefront", Spine), Is.Null);
    }

    [Test]
    public void AnApprovalWaitingHidesItForTheLaterUnits()
    {
        var work = new WaitingWork();
        var steering = new WorkspaceSteering(factory);
        var binding = Bind(work.Present(), steering, new FileScreen { Section = FileSection.Waiting });
        Assert.That(RoomBar.Of(binding, VoiceStage.Idle, false).State, Is.EqualTo(RoomBarState.Hidden));
    }
}

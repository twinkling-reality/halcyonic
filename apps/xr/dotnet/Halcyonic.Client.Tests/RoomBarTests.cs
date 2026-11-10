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
    public void AThreePartInstructionOffersConfirmOnlyAfterItsLastPartWasDrawnAndSendsOnce()
    {
        var now = Now;
        var workspace = Running(WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var steering = new WorkspaceSteering(factory, () => now);
        var words = "Add a golden balloon worth five points, then a silver one worth three, then a bomb that ends the round when it is popped.";
        Assert.That(steering.Spoken(words, workspace).Step, Is.EqualTo(SteeringStep.Confirm));
        var screen = new FileScreen { Section = FileSection.Activity, Speak = true };
        // Three parts: 3 rows, one a part.
        screen.ReadRequest(steering.ToRead(workspace)!, 3, 1, steering);

        var first = RoomBar.Of(Bind(workspace, steering, screen), VoiceStage.Idle, false);
        Assert.That(first.Column.Any(action => action.Place == KitPlace.Yes), Is.False, "no Confirm at part 1");
        // A Confirm no control offers: the steering refuses it and stays armed.
        Assert.That(steering.Confirm(workspace).Step, Is.EqualTo(SteeringStep.Explain));
        Assert.That(steering.Armed, Is.EqualTo(WorkspaceAction.Instruct));

        // Next Part before the part was drawn turns nothing; a double press turns one part only.
        screen.NextRequestPart(steering, now);
        Assert.That(screen.RequestPart, Is.EqualTo(0), "not drawn, not turned");
        screen.RequestDrawn(0, steering, now);
        screen.NextRequestPart(steering, now.AddSeconds(0.1));
        Assert.That(screen.RequestPart, Is.EqualTo(0), "inside the turn guard");
        now = now.AddSeconds(1);
        screen.NextRequestPart(steering, now);
        screen.NextRequestPart(steering, now);
        Assert.That(screen.RequestPart, Is.EqualTo(1), "a double press turns one part");
        Assert.That(RoomBar.Of(Bind(workspace, steering, screen), VoiceStage.Idle, false).Column.Any(action => action.Place == KitPlace.Yes), Is.False);
        screen.RequestDrawn(1, steering, now);
        now = now.AddSeconds(1);
        screen.NextRequestPart(steering, now);
        Assert.That(screen.RequestPart, Is.EqualTo(2));
        Assert.That(RoomBar.Of(Bind(workspace, steering, screen), VoiceStage.Idle, false).Column.Any(action => action.Place == KitPlace.Yes), Is.False,
            "turned to the last part is not read");
        Assert.That(steering.Confirm(workspace).Step, Is.EqualTo(SteeringStep.Explain), "turned to, not drawn: refused");

        screen.RequestDrawn(2, steering, now);
        Assert.That(RoomBar.Of(Bind(workspace, steering, screen), VoiceStage.Idle, false).Column[0].Place, Is.EqualTo(KitPlace.Yes));

        // Confirm, twice: one command with the armed words, then nothing.
        var sent = steering.Confirm(workspace);
        Assert.That(sent.Step, Is.EqualTo(SteeringStep.Send));
        Assert.That(((ExecutionSendInstructionCommand)sent.Command!).Payload.Text, Is.EqualTo(words));
        Assert.That(steering.Confirm(workspace).Step, Is.EqualTo(SteeringStep.None));
        Assert.That(Bind(workspace, steering, screen).Model.Instruction, Is.Null, "after the send nothing is armed");
    }

    [Test]
    public void AfterCancelATimeoutOrAChangedTaskConfirmSendsNothing()
    {
        var now = Now;
        var workspace = Running(WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var steering = new WorkspaceSteering(factory, () => now);
        var screen = new FileScreen { Section = FileSection.Activity, Speak = true };

        // Cancel, then a late Confirm.
        steering.Spoken("Add a test.", workspace);
        screen.ReadRequest(steering.ToRead(workspace)!, 1, 3, steering);
        screen.RequestDrawn(0, steering, now);
        Assert.That(steering.CanConfirm, Is.True);
        steering.Cancel();
        screen.ForgetRequest();
        Assert.That(steering.Confirm(workspace).Step, Is.EqualTo(SteeringStep.None), "after Cancel");

        // A read counted for one arming never counts for the next, even with the same words.
        steering.Spoken("Add a test.", workspace);
        screen.RequestDrawn(0, steering, now);
        Assert.That(steering.CanConfirm, Is.False, "a new arming is unread");
        Assert.That(RoomBar.Of(Bind(workspace, steering, screen), VoiceStage.Idle, false).Column.Any(action => action.Place == KitPlace.Yes), Is.False);

        // Read, then the task no longer takes instructions: refused, and disarmed.
        screen.ReadRequest(steering.ToRead(workspace)!, 1, 3, steering);
        screen.RequestDrawn(0, steering, now);
        Assert.That(steering.CanConfirm, Is.True);
        Assert.That(steering.Confirm(Running(WorkspaceAction.Interrupt)).Step, Is.EqualTo(SteeringStep.Explain));
        Assert.That(steering.Armed, Is.Null);

        // Read, then left too long: refused.
        steering.Spoken("Add a test.", workspace);
        screen.ReadRequest(steering.ToRead(workspace)!, 1, 3, steering);
        screen.RequestDrawn(0, steering, now);
        now = now.AddMinutes(30);
        Assert.That(steering.Confirm(workspace).Step, Is.EqualTo(SteeringStep.Explain));
    }

    [Test]
    public void ABackslashInTheInstructionReachesThePanelsLabelEscapedExactlyOnce()
    {
        var workspace = Running(WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var steering = new WorkspaceSteering(factory, () => Now);
        const string words = @"Match \d+ in C:\temp";
        steering.Spoken(words, workspace);
        var screen = new FileScreen { Section = FileSection.Activity, Speak = true };
        screen.ReadRequest(steering.ToRead(workspace)!, 1, 3, steering);
        var bar = RoomBar.Of(Bind(workspace, steering, screen), VoiceStage.Idle, false);
        Assert.That(bar.Part!.FromRow, Is.Not.Null, "a confirmation's line is always a part, so the panel always reads it as the file cut it");
        // The file's part is a piece of the label's own text, each backslash doubled; the panel makes it words again before it sets them.
        var piece = bar.Part.Runs.ForTextMeshPro().Text;
        Assert.That(piece.Count(c => c == '\\'), Is.EqualTo(4));
        Assert.That(LabelText.FromTextMeshPro(piece), Is.EqualTo(words));
        Assert.That(LineWords.Theirs(LabelText.FromTextMeshPro(piece)).ForTextMeshPro().Text, Is.EqualTo(piece), "set on the label, it is escaped once, so the label draws the words as written");
        Assert.That(LineWords.Theirs(piece).ForTextMeshPro().Text.Count(c => c == '\\'), Is.EqualTo(8), "handed on as it came, each backslash would be drawn twice");
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

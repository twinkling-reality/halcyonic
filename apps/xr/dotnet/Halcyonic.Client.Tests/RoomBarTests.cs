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
        Assert.That(rest.FromMoment, Is.False, "the row at rest is the task's own loop");
        Assert.That(Actions(rest.Row), Is.EqualTo(new[] { (KitPlace.Talk, "Hold to Talk", true), (KitPlace.Field, "Type", false) }));
        Assert.That(rest.Row[1].Control.Action, Is.EqualTo(FileScreens.TellIt), "Type opens the file's own instruction");

        var listening = RoomBar.Of(binding, VoiceStage.Listening, sentAwaitingStart: false);
        Assert.That(Actions(listening.Row), Is.EqualTo(new[] { (KitPlace.Talk, "Listening", true) }), "Type goes while it listens");
        Assert.That(Actions(RoomBar.Of(binding, VoiceStage.WritingDown, false).Row), Is.EqualTo(new[] { (KitPlace.Talk, "Typing It Out", true) }));

        var sent = RoomBar.Of(binding, VoiceStage.Idle, sentAwaitingStart: true);
        Assert.That((sent.State, sent.Words.Text, sent.Row.Count), Is.EqualTo((RoomBarState.Sent, "Sent · Waiting for the agent to start.", 0)));
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
    public void ADraftLongerThanThePanelStandsInTheFilesPartsTheColumnTurnsAndSendsOnceReadWhole()
    {
        var workspace = Running(WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var steering = new WorkspaceSteering(factory);
        var screen = new FileScreen { Section = FileSection.Activity, Speak = true };
        const string words = "Rework the scoring so every balloon colour is worth its own points, and keep the high score between rounds.";
        screen.Instruction.Restore(workspace.Execution!.ExecutionId, words, false, 0);
        // Twenty of the panel's lines on the redesign's composer page: parts of seven, the pager's row left beside them.
        screen.Instruction.Measure(20, KitFileBinding.ComposerBudget(RoomBar.RequestLines), TextSize.Standard, pairedPager: true);
        Assert.That((screen.Instruction.Parts, screen.Instruction.PartRows), Is.EqualTo((3, 7)));

        var first = RoomBar.Of(Bind(workspace, steering, screen), VoiceStage.Idle, false);
        Assert.That((first.State, first.Body.Text, first.Part?.FromRow, first.Part?.Rows), Is.EqualTo((RoomBarState.Draft, words, (int?)0, (int?)7)), "the whole draft, the part's rows of it");
        Assert.That(first.Column.Select(action => action.Place).ToArray(), Is.EqualTo(new[] { KitPlace.Step, KitPlace.Close }),
            "no Send while the whole is unread; the way out stays");
        var next = first.Column[0];
        Assert.That((next.Control.Action, next.Main, next.Icon), Is.EqualTo((FileScreens.NextInstructionPart, true, (GlazeIcon?)GlazeIcon.Next)),
            "the next part is the main action while Send waits for the whole");
        Assert.That(first.Caret, Is.False, "the words go on past this part");

        // Each part read in order: on the last, Send stands, the previous part after it, and the caret where the words end.
        var at = Now;
        for (var part = 0; part < 2; part++)
        {
            screen.Instruction.Drawn(at);
            at += FileScreen.TurnGuard;
            screen.Instruction.Next(at);
        }
        screen.Instruction.Drawn(at);
        Assert.That((screen.Instruction.Part, screen.Instruction.Read), Is.EqualTo((2, true)));
        var last = RoomBar.Of(Bind(workspace, steering, screen), VoiceStage.Idle, false);
        Assert.That((last.Part?.FromRow, last.Caret), Is.EqualTo(((int?)14, true)));
        Assert.That(last.Column.Select(action => (action.Place, action.Control.Action, action.Main)).Take(2), Is.EqualTo(new[]
        {
            (KitPlace.Send, FileScreens.SendInstruction, true), (KitPlace.Step, FileScreens.PreviousInstructionPart, false),
        }));
        Assert.That(last.Column.Single(action => action.Control.Action == FileScreens.PreviousInstructionPart).Icon, Is.EqualTo(GlazeIcon.Back));
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
        // Read first, in the product's words; Send Instruction arrives once read, with "Send This?".
        Assert.That(draft.Sentence, Is.EqualTo(FileScreens.InstructionNotRead));
        Assert.That(Actions(draft.Column), Is.EqualTo(new[] { (KitPlace.Talk, "Talk Again", false), (KitPlace.Close, "Cancel", false) }));
        Assert.That(draft.Column[0].Control.Action, Is.EqualTo(FileScreens.HoldToTalk), "Talk Again is the frame's own hold to talk");
        // Held, it stays in its place, lit, reading Listening: the moment stands while it records afresh.
        var again = RoomBar.Of(binding, VoiceStage.Listening, false);
        Assert.That((again.State, again.Column[0].Place, again.Column[0].Words, again.Column[0].Main), Is.EqualTo((RoomBarState.Draft, KitPlace.Talk, "Listening", true)));
    }

    [Test]
    public void AHeardDraftStandsAsTheSubtitleFromBeforeItIsReadAndATypedOneNever()
    {
        var workspace = Running(WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var heard = new FileScreen { Section = FileSection.Activity, Speak = true };
        heard.Instruction.Restore(workspace.Execution!.ExecutionId, "Add a golden balloon worth five points.", true, 0);
        var draft = RoomBar.Of(Bind(workspace, new WorkspaceSteering(factory), heard), VoiceStage.Idle, false);
        // Before it is read there is no Send yet, and it is the subtitle all the same, so Send arriving changes no look.
        Assert.That((draft.AsSubtitle, draft.Column.Any(action => action.Main)), Is.EqualTo((true, false)));
        Assert.That(draft.AsDialog, Is.False);
        var typed = new FileScreen { Section = FileSection.Activity };
        typed.Instruction.Restore(workspace.Execution!.ExecutionId, "Add a golden balloon worth five points.", false, 0);
        var caret = RoomBar.Of(Bind(workspace, new WorkspaceSteering(factory), typed), VoiceStage.Idle, false);
        Assert.That((caret.State, caret.AsSubtitle), Is.EqualTo((RoomBarState.Draft, false)), "a draft being typed keeps its panel and caret");
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
        Assert.That(read.Sentence, Is.EqualTo("Send This?"));
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
    public void AnApprovalIsThePairItsRequestInMonoWhatItAsksInTheSentenceBarAndApproveThenDenyTheFilesOwn()
    {
        var work = Commanded();
        var steering = new WorkspaceSteering(factory);
        var asked = RoomBar.Of(Bind(work.Present(), steering, new FileScreen { Section = FileSection.Waiting }), VoiceStage.Idle, false);
        Assert.That((asked.State, asked.Pair, asked.Mono, asked.Body.Text, asked.Sentence, asked.Waits),
            Is.EqualTo((RoomBarState.Request, true, true, "pnpm migrate", (string?)"Run Command?", false)));
        Assert.That(asked.Description?.Text, Is.EqualTo("Run the migration"), "the agent's sentence above the command's well");
        Assert.That(Actions(asked.Column), Is.EqualTo(new[] { (KitPlace.Approve, "Approve", true), (KitPlace.Deny, "Deny", false) }));
        Assert.That(asked.Column.Select(action => (action.Control.Action, action.Locks)), Is.EqualTo(new[] { (FileScreens.Approve, false), (FileScreens.Deny, false) }));
        Assert.That(asked.Row, Is.Empty, "no Hold to Talk or Type while an approval waits");
    }

    [Test]
    public void ConfirmStandsBehindItsLockOnceTheRequestIsReadAndItsPressSendsTheApprovalThenItReadsSendingWithNoAction()
    {
        var work = Commanded();
        var before = work.Present();
        var steering = new WorkspaceSteering(factory, () => Now);
        var screen = new FileScreen { Section = FileSection.Waiting };
        Assert.That(steering.Press(WorkspaceAction.Approve, before).Step, Is.EqualTo(SteeringStep.Confirm));
        var request = steering.ToRead(before)!;
        screen.ReadRequest(request, 1, 3, steering);

        var unread = RoomBar.Of(Bind(before, steering, screen), VoiceStage.Idle, false);
        Assert.That((unread.State, unread.Mono, unread.Sentence, unread.Body.Text),
            Is.EqualTo((RoomBarState.Request, true, (string?)WorkspaceText.ReadRequestFirst, "pnpm migrate")), "the command alone, as it stood before its confirmation");
        Assert.That(unread.Column.Select(action => action.Place), Is.EqualTo(new[] { KitPlace.Cancel }), "no Confirm before the request was drawn");
        // The rows the file counted reach the panel, which draws the confirmation only where it wraps the same words to as many.
        var counted = KitFileBinding.Of(FileScreens.Screen(before, steering, screen, new AnswerRoom(4)), steering.Armed, before, "", Live, "Storefront", Spine,
            roomDesign: true, requestRows: screen.RequestRows)!;
        Assert.That((unread.Rows, RoomBar.Of(counted, VoiceStage.Idle, false).Rows), Is.EqualTo(((int?)null, (int?)1)));

        screen.RequestDrawn(0, steering, Now);
        var read = RoomBar.Of(Bind(before, steering, screen), VoiceStage.Idle, false);
        Assert.That(read.Sentence, Is.EqualTo("Approve Request?"));
        Assert.That(read.Column.Select(action => (action.Place, action.Words, action.Main, action.Locks, action.Icon)), Is.EqualTo(new[]
        {
            (KitPlace.Yes, "Confirm", true, true, (GlazeIcon?)GlazeIcon.Approve), (KitPlace.Cancel, "Cancel", false, false, (GlazeIcon?)GlazeIcon.Close),
        }));

        // Confirm is the file's own Yes: its press is the steering's Confirm, which sends the answer to the approval.
        Assert.That(read.Column[0].Control.Action, Is.EqualTo(FileScreens.Yes));
        var sent = steering.Confirm(before);
        Assert.That((sent.Step, sent.Command!.CommandType), Is.EqualTo((SteeringStep.Send, "execution.respond_to_approval")));

        // Accepted is not done: until the runtime resolves it, Sending sweeps and the column is empty.
        var submissions = new CommandSubmissions();
        _ = submissions.SubmitAsync(_ => new System.Threading.Tasks.TaskCompletionSource<CommandAckMessage>().Task, sent.Command!, "e1");
        var flying = work.Present(submissions: submissions);
        Assert.That(flying.ApprovalInFlight, Is.True);
        var sending = RoomBar.Of(Bind(flying, steering, screen), VoiceStage.Idle, false);
        Assert.That((sending.State, sending.Sentence, sending.Waits, sending.Column.Count, sending.Body.Text),
            Is.EqualTo((RoomBarState.Request, (string?)"Sending", true, 0, "pnpm migrate")));
        var frame = FileScreens.Screen(flying, steering, screen, new AnswerRoom(4));
        Assert.That(KitFileBinding.Of(frame, steering.Armed, flying, "", Live, "Storefront", Spine), Is.Null, "without the redesign the file still draws an answer in flight");

        // Resolved, the approval is gone and so is its pair.
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Running; }, WorkstreamStatus.Running);
        var after = work.Present();
        Assert.That(RoomBar.Of(Bind(after, steering, new FileScreen { Section = FileSection.Activity }), VoiceStage.Idle, false).State, Is.Not.EqualTo(RoomBarState.Request));
    }

    [Test]
    public void ALongRequestIsReadPartByPartAsTheFileCutsItTheNextPartMainUntilConfirmArrives()
    {
        var work = Commanded();
        var workspace = work.Present();
        var steering = new WorkspaceSteering(factory, () => Now);
        var screen = new FileScreen { Section = FileSection.Waiting };
        steering.Press(WorkspaceAction.Approve, workspace);
        var request = steering.ToRead(workspace)!;
        screen.ReadRequest(request, 7, 3, steering);

        var first = RoomBar.Of(Bind(workspace, steering, screen), VoiceStage.Idle, false);
        Assert.That((first.Part!.Rows, first.Part.FromRow, first.Sentence), Is.EqualTo((3, (int?)0, (string?)WorkspaceText.ReadRequestFirst)));
        Assert.That(Actions(first.Column), Is.EqualTo(new[] { (KitPlace.Step, "Next Part", true), (KitPlace.Cancel, "Cancel", false) }));
        Assert.That((first.Column[0].Control.Action, first.Column[0].Control.Key), Is.EqualTo((FileScreens.NextPart, (string?)FileScreens.RequestKey)));
        Assert.That(first.Body.Text, Is.EqualTo("pnpm migrate"), "the panel reads the command alone, whole, and shows the rows the part names");
        // What is read, and how: for an approval's request, its bare command alone in mono, never the tool's name its line leads
        // with; with no command, the agent's sentence as prose; a line that is not that request, or names no tool, as the file
        // wrote it; an instruction's words as prose.
        var line = FileScreens.RequestLine(steering, request);
        Assert.That(RoomBar.Reading(line, "Run the migration", instruction: false, command: "pnpm migrate"), Is.EqualTo((LineWords.Theirs("pnpm migrate"), true)).Using<(LineWords Words, bool Mono)>(
            (a, b) => a.Words.Text == b.Words.Text && a.Mono == b.Mono));
        Assert.That(RoomBar.Reading(line, "Run the migration", instruction: false), Is.EqualTo((LineWords.Theirs("Run the migration"), false)).Using<(LineWords Words, bool Mono)>(
            (a, b) => a.Words.Text == b.Words.Text && a.Mono == b.Mono));
        Assert.That(RoomBar.Reading(line, "another request", instruction: false, command: "pnpm migrate"), Is.EqualTo((LineWords.Theirs(request), false)).Using<(LineWords Words, bool Mono)>(
            (a, b) => a.Words.Text == b.Words.Text && a.Mono == b.Mono));
        Assert.That(RoomBar.Reading(line, (string?)null, instruction: false), Is.EqualTo((line.Runs, false)));
        Assert.That(RoomBar.Reading(line, "Run the migration", instruction: true), Is.EqualTo((line.Runs, false)));

        screen.RequestDrawn(0, steering, Now);
        screen.NextRequestPart(steering, Now + TimeSpan.FromSeconds(1));
        var second = RoomBar.Of(Bind(workspace, steering, screen), VoiceStage.Idle, false);
        Assert.That(second.Part!.FromRow, Is.EqualTo(3));
        Assert.That(second.Body.Text, Is.EqualTo("pnpm migrate"), "every part reads from the same whole");

        // A denial may be confirmed at once, as denying runs nothing: Confirm leads, the next part after it.
        steering.Press(WorkspaceAction.Deny, workspace);
        screen.ReadRequest(steering.ToRead(workspace)!, 7, 3, steering);
        var denying = RoomBar.Of(Bind(workspace, steering, screen), VoiceStage.Idle, false);
        Assert.That(denying.Sentence, Is.EqualTo("Deny Request?"));
        Assert.That(denying.Column.Select(action => (action.Place, action.Main, action.Locks, action.Icon)), Is.EqualTo(new[]
        {
            (KitPlace.Yes, true, true, (GlazeIcon?)GlazeIcon.Deny), (KitPlace.Step, false, false, (GlazeIcon?)GlazeIcon.Next), (KitPlace.Cancel, false, false, (GlazeIcon?)GlazeIcon.Close),
        }));
    }

    /// <summary>The waiting work's approval with its command's own parts, as every agent app's shell approval carries them.</summary>
    private static WaitingWork Commanded()
    {
        var work = new WaitingWork();
        work.Change(execution => ((ToolUseSubject)execution.PendingApprovals[0].Subject).Command = Bare("pnpm migrate"));
        return work;
    }

    /// <summary>A task waiting on one approval of <paramref name="subject"/>.</summary>
    private static WorkspacePresentation Asking(ToolUseSubject subject)
    {
        var work = new WaitingWork();
        work.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.PendingApprovals.Add(new ApprovalView { ApprovalId = WaitingWork.ApprovalId, Subject = subject, Approvable = true, RequestedAt = Samples.Time });
        });
        return work.Present();
    }

    private static ApprovalCommand Bare(string text) => new() { Text = text, RunsIn = new TaskFolderCommandPlace(), Background = false, InputToRunning = false };

    /// <summary>Asked, then under its confirmation with its one part drawn: the two bars an approval shows.</summary>
    private (RoomBar Asked, RoomBar Confirming, PageLine Line) AskedAndConfirming(WorkspacePresentation workspace)
    {
        var steering = new WorkspaceSteering(factory, () => Now);
        var screen = new FileScreen { Section = FileSection.Waiting };
        var asked = RoomBar.Of(Bind(workspace, steering, screen), VoiceStage.Idle, false);
        steering.Press(WorkspaceAction.Approve, workspace);
        screen.ReadRequest(steering.ToRead(workspace)!, 1, 8, steering);
        screen.RequestDrawn(0, steering, Now);
        var confirming = RoomBar.Of(Bind(workspace, steering, screen), VoiceStage.Idle, false);
        return (asked, confirming, FileScreens.RequestLine(steering, steering.ToRead(workspace)!));
    }

    [Test]
    public void AnApprovalsWellHoldsTheBareCommandAloneAndTheAgentsSentenceStandsAboveItWithoutItsBackticks()
    {
        // The live case (2026-11-17): the host's sentence and its bare command, handed apart.
        var workspace = Asking(new ToolUseSubject
        {
            ToolName = "Bash", Summary = "Run `pnpm db:migrate` against the local development database", Command = Bare("pnpm db:migrate"),
        });
        var (asked, confirming, line) = AskedAndConfirming(workspace);
        Assert.That((asked.Mono, asked.Body.Text, asked.Sentence), Is.EqualTo((true, "pnpm db:migrate", (string?)"Run Command?")), "the well holds the command alone");
        Assert.That((confirming.Mono, confirming.Body.Text, confirming.Sentence), Is.EqualTo((true, "pnpm db:migrate", (string?)"Approve Request?")));
        Assert.That(RoomBar.Reading(line, workspace, instruction: false), Is.EqualTo((LineWords.Theirs("pnpm db:migrate"), true)).Using<(LineWords Words, bool Mono)>(
            (a, b) => a.Words.Text == b.Words.Text && a.Mono == b.Mono), "the file's parts count the command's own rows");
        foreach (var bar in new[] { asked, confirming })
        {
            Assert.That(new[] { bar.Body.Text, bar.Sentence, bar.DialogTitle?.Text, bar.Description?.Text }.Where(text => text != null),
                Has.None.Contains("Bash: ").And.None.Contains("Bash:"), "the tool's name never joins the request");
            Assert.That(bar.Body.Text, Does.Not.Contain("against"), "the host's sentence never fills the well");
            Assert.That(bar.Description?.Text, Is.EqualTo("Run pnpm db:migrate against the local development database"),
                "the agent's sentence above the well, whole, only its code span's backticks gone");
            Assert.That(bar.Description!.Text, Does.Not.Contain("`"));
            Assert.That(bar.Description.AllTheirs, Is.True, "the sentence is the agent's words");
        }
        Assert.That(asked.AsDialog, Is.True, "the approval stands as its dialog, the command in its well");
    }

    [Test]
    public void ABacktickThatIsPartOfTheCommandStaysInTheWell()
    {
        var workspace = Asking(new ToolUseSubject { ToolName = "bash", Summary = "Print today's date with `date`", Command = Bare("echo `date` > today.txt") });
        var (asked, confirming, _) = AskedAndConfirming(workspace);
        Assert.That((asked.Mono, asked.Body.Text), Is.EqualTo((true, "echo `date` > today.txt")));
        Assert.That((confirming.Mono, confirming.Body.Text), Is.EqualTo((true, "echo `date` > today.txt")));
        Assert.That(asked.Description?.Text, Is.EqualTo("Print today's date with date"), "the sentence's code span loses its backticks; the command keeps its own");
    }

    [Test]
    public void WithoutItsBareCommandAnApprovalDrawsNoWellOnlyTheAgentsSentence()
    {
        var workspace = Asking(new ToolUseSubject { ToolName = "Bash", Summary = "Run `pnpm db:migrate` against the local development database" });
        var (asked, confirming, line) = AskedAndConfirming(workspace);
        const string sentence = "Run pnpm db:migrate against the local development database";
        Assert.That((asked.Mono, asked.Body.Text, asked.Sentence, asked.AsDialog), Is.EqualTo((false, sentence, (string?)"Run Command?", false)), "no well, the sentence alone");
        Assert.That((confirming.Mono, confirming.Body.Text), Is.EqualTo((false, sentence)));
        Assert.That(RoomBar.Reading(line, workspace, instruction: false).Mono, Is.False);
        Assert.That(asked.Body.Text, Does.Not.Contain("Bash"));
        Assert.That((asked.Description, confirming.Description), Is.EqualTo(((LineWords?)null, (LineWords?)null)), "the sentence is the body, never laid twice");
    }

    [Test]
    public void ACodeSpansBackticksGoAndEveryOtherCharacterStays()
    {
        Assert.That(WorkspaceText.WithoutCodeMarks("Run `pnpm db:migrate` now"), Is.EqualTo("Run pnpm db:migrate now"));
        Assert.That(WorkspaceText.WithoutCodeMarks("Use ``a `tick` inside`` here"), Is.EqualTo("Use a `tick` inside here"), "a longer run closes only on its own length");
        Assert.That(WorkspaceText.WithoutCodeMarks("A lone ` stays"), Is.EqualTo("A lone ` stays"), "a run nothing closes is no code span");
        Assert.That(WorkspaceText.WithoutCodeMarks("So does ``this`"), Is.EqualTo("So does ``this`"), "nor runs of other lengths");
        Assert.That(WorkspaceText.WithoutCodeMarks("No marks at all"), Is.EqualTo("No marks at all"));
    }

    [Test]
    public void AToolThatIsNoCommandNamesItselfInTheQuestionAndItsRequestStandsAloneWithNoWell()
    {
        var work = new WaitingWork();
        work.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.PendingApprovals.Add(new ApprovalView
            {
                ApprovalId = WaitingWork.ApprovalId, Subject = new ToolUseSubject { ToolName = "Edit", Summary = "src/game.js" }, Approvable = true, RequestedAt = Samples.Time,
            });
        });
        var workspace = work.Present();
        var steering = new WorkspaceSteering(factory, () => Now);
        var screen = new FileScreen { Section = FileSection.Waiting };
        var asked = RoomBar.Of(Bind(workspace, steering, screen), VoiceStage.Idle, false);
        Assert.That((asked.Body.Text, asked.Mono, asked.Sentence), Is.EqualTo(("src/game.js", false, (string?)"Use Edit")),
            "the tool's name in the question, never joined to the request, and no well without a command (AUDIT2, 2026-11-17)");

        steering.Press(WorkspaceAction.Approve, workspace);
        screen.ReadRequest(steering.ToRead(workspace)!, 1, 8, steering);
        screen.RequestDrawn(0, steering, Now);
        var confirming = RoomBar.Of(Bind(workspace, steering, screen), VoiceStage.Idle, false);
        Assert.That((confirming.Body.Text, confirming.Mono, confirming.Sentence), Is.EqualTo(("src/game.js", false, (string?)"Approve Request?")));
        var line = FileScreens.RequestLine(steering, steering.ToRead(workspace)!);
        Assert.That(line.Words, Is.EqualTo("Edit: src/game.js"), "the file's own line still leads with the tool's name");
        Assert.That(RoomBar.Reading(line, workspace, instruction: false).Words.Text, Is.EqualTo("src/game.js"));
        Assert.That(RoomBar.Reading(line, "src/game.js", instruction: false, command: "sed -i s/a/b/ src/game.js"), Is.EqualTo((LineWords.Theirs("sed -i s/a/b/ src/game.js"), true))
            .Using<(LineWords Words, bool Mono)>((a, b) => a.Words.Text == b.Words.Text && a.Mono == b.Mono), "a bare command stands alone in the well");
    }

    [Test]
    public void ARequestThatCanOnlyBeDeniedSaysWhyInTheSentenceBarAndALapseReadsInTheProductsWords()
    {
        var cut = new WaitingWork();
        cut.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.PendingApprovals.Add(new ApprovalView
            {
                ApprovalId = WaitingWork.ApprovalId,
                Subject = new ToolUseSubject { ToolName = "bash", Summary = "pnpm test --reporter [truncated]" },
                Approvable = false,
                RequestedAt = Samples.Time,
            });
        });
        var only = RoomBar.Of(Bind(cut.Present(), new WorkspaceSteering(factory), new FileScreen { Section = FileSection.Waiting }), VoiceStage.Idle, false);
        Assert.That(only.Sentence, Is.EqualTo(WorkspaceText.TooLongToShow));
        Assert.That(Actions(only.Column), Is.EqualTo(new[] { (KitPlace.Deny, "Deny", true) }.Select(each => (each.Item1, each.Item2, false))));

        // A confirmation not given in time lapses: the file says so, and the sentence bar reads it with both answers back.
        var work = new WaitingWork();
        var workspace = work.Present();
        var clock = Now;
        var steering = new WorkspaceSteering(factory, () => clock);
        var screen = new FileScreen { Section = FileSection.Waiting };
        steering.Press(WorkspaceAction.Approve, workspace);
        clock += TimeSpan.FromSeconds(16);
        var lapsed = RoomBar.Of(Bind(workspace, steering, screen), VoiceStage.Idle, false);
        Assert.That(steering.Armed, Is.Null);
        Assert.That(lapsed.Sentence, Is.EqualTo("Nothing was sent: the confirmation timed out. Press again."));
        Assert.That(lapsed.Column.Select(action => action.Place), Is.EqualTo(new[] { KitPlace.Approve, KitPlace.Deny }));
    }
}

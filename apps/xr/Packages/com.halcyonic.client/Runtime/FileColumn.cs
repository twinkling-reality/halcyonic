#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// A task's file as one column of the menu's plane (ADR 0026, <see cref="IMenuColumn"/>): its
    /// <see cref="FileScreen"/> and <see cref="WorkspaceSteering"/>, its frame built by
    /// <see cref="FileScreens"/>, and what Changes and Checks read. Every press is judged by the
    /// steering's rules and the page's own, and only they send, through <see cref="IMenuHost.Submit"/>.
    /// What counts as read counts only as the director reports drawing the very frame this column built
    /// (<see cref="Drawn"/>). The page packs by height against what a page holds on its stage, the lower of
    /// alone and beside the menu, read when the file opens and again only when the text size changes, so it
    /// never packs again as the head moves; at a new size the request and the question are read again.
    /// </summary>
    public sealed class FileColumn : IMenuColumn, ICompactableMenuColumn
    {
        /// <summary>How often the file reads its work again by itself, in seconds of the host's clock.</summary>
        public const double RefreshSeconds = 0.5;

        /// <summary>How long a notice stays on the page, in seconds of the host's clock.</summary>
        public const double NoticeSeconds = 8;

        private readonly IMenuHost host;
        private readonly CommandFactory commands;
        private readonly Func<WorkspacePresentation?> present;
        private readonly Func<string, IReadOnlyList<PresetInstruction>?> recorded;
        private readonly Func<string> historyNote;
        private readonly Action<WorkspaceAct>? acted;
        private readonly IntelligenceFeed<UnderstandingResponse> understanding;
        private readonly IntelligenceFeed<EvaluationResponse> evaluation;
        private readonly Func<IIntelligenceReader?> reader;
        private PageBudget? budget;
        private bool budgetFocused;
        private TextSize budgetAt;
        private QuestionDraft? draft;
        private QuestionDraft? measuredFor;
        private TextSize measuredAt;
        private IReadOnlyList<PromptMeasure>? measures;
        private bool presets;
        private IReadOnlyList<PresetInstruction>? recordedNow;
        private string? notice;
        private MenuFrame? drawnPresetFrame;
        private double noticeUntil;
        private double nextRefresh;
        private bool answering;
        private int answerPrompt;

        /// <summary>The question Hold to talk was held for, which what is heard may type into only while it still shows.</summary>
        private QuestionDraft? answerDraft;
        private int answerInputSession;
        private bool answerVoiceStarted;
        private int instructionKeyboardSession;
        private bool instructionVoiceStarted;
        private bool instructionVoiceValid;
        private string? instructionVoiceExecution;
        private int instructionVoiceEpoch;
        private FileSection instructionVoiceSection;
        private MenuFrame? drawnInstructionFrame;
        private MenuFrame? drawnInstructionNoticeFrame;
        private MenuFrame? drawnResultsFrame;
        private MenuFrame? drawnShowTaskFrame;
        private Func<bool>? canShowTask;
        private Action? showTask;
        private string? showTaskJournal;
        private string? showTaskId;
        private MenuFrame? drawnColourFrame;
        private Func<bool>? colourAvailable;
        private Action? openCharacterColour;
        private string? colourJournal, colourTask;

        /// <param name="present">The work as the director presents it now, its own commands in flight included.</param>
        /// <param name="commands">Makes the commands the steering sends.</param>
        /// <param name="reader">Where Changes and Checks read: the control plane, or the demonstration while it plays.</param>
        /// <param name="recorded">The instructions the demonstration recorded for an execution, offered in place of the keyboard.</param>
        /// <param name="historyNote">How reading the work's earlier activity goes, said after its activity.</param>
        /// <param name="acted">What the person did, for the stage's sound.</param>
        public FileColumn(IMenuHost host, Func<WorkspacePresentation?> present, CommandFactory commands, Func<IIntelligenceReader?> reader,
            Func<string, IReadOnlyList<PresetInstruction>?> recorded, Func<string> historyNote, Action<WorkspaceAct>? acted = null)
        {
            this.host = host;
            this.commands = commands;
            this.present = present;
            this.reader = reader;
            this.recorded = recorded;
            this.historyNote = historyNote;
            this.acted = acted;
            Steering = new WorkspaceSteering(commands, () => host.Clock);
            understanding = new IntelligenceFeed<UnderstandingResponse>((id, cancel) => Reader().ReadUnderstandingAsync(id, cancel), AccessRefused);
            evaluation = new IntelligenceFeed<EvaluationResponse>((id, cancel) => Reader().ReadEvaluationAsync(id, cancel), AccessRefused);
            Screen.Zone = host.Zone;
            Screen.Speak = host.VoiceOffered;
            if (present() is WorkspacePresentation opening) Screen.Section = FileScreens.Opening(opening);
            Rebuild();
        }

        /// <summary>The section showing and where the person is in it.</summary>
        public FileScreen Screen { get; } = new FileScreen();

        public WorkspaceSteering Steering { get; }

        /// <summary>The work as last presented; presses are judged against it.</summary>
        public WorkspacePresentation? Now { get; private set; }

        public MenuFrame? Frame { get; private set; }
        private WorkspaceRequestContinuation? retainedRequest;
        private string? requestExecution;
        private Action? openWorkspaceRequest;
        private Action? openResults;
        public WorkspaceRequestColumn? RequestView { get; private set; }
        public WorkspaceSurface Surface { get; set; } = WorkspaceSurface.Task;
        public void OfferWorkspaceRequests(Action open)
        { if (openWorkspaceRequest != null) return; openWorkspaceRequest = open; Screen.WorkspaceRequestOffered = true; Rebuild(); }
        /// <summary>Offers only an available local presentation action; it grants no runtime capability.</summary>
        public void OfferShowTaskHere(Func<bool> available, Action show)
        {
            if (showTask != null) return;
            canShowTask = available; showTask = show;
            showTaskJournal = host.State?.Journal?.JournalId; showTaskId = Now?.Character.WorkstreamId; Rebuild();
        }
        private bool CanShowTaskHere() => showTask != null && canShowTask?.Invoke() == true && host.Connected && !host.Demonstration
            && showTaskJournal != null && showTaskJournal == host.State?.Journal?.JournalId && showTaskId == Now?.Character.WorkstreamId;
        public void OfferResults(Action open)
        { if (openResults != null) return; openResults = open; Screen.ResultsOffered = true; Rebuild(); }
        public void OfferCharacterColours(Func<bool> available, Action open)
        {
            if (colourAvailable != null) return;
            colourAvailable = available; openCharacterColour = open;
            colourJournal = host.State?.Journal?.JournalId; colourTask = Now?.Character.WorkstreamId; Rebuild();
        }
        private bool CanColour() => colourAvailable?.Invoke() == true && host.Connected && !host.Demonstration
            && colourJournal != null && colourJournal == host.State?.Journal?.JournalId && colourTask == Now?.Character.WorkstreamId;
        public WorkspaceRequestContinuation? ContinuationRequest() => RequestView?.ExecutionId == Now?.Execution?.ExecutionId
            ? RequestView?.Draft : requestExecution == Now?.Execution?.ExecutionId && retainedRequest != null ? new WorkspaceRequestContinuation
            { RequestId = retainedRequest.RequestId, PendingCommandId = retainedRequest.PendingCommandId, Text = retainedRequest.Text, Spoken = retainedRequest.Spoken, Part = retainedRequest.Part } : null;
        public WorkspaceRequestColumn RequestFor(WorkspaceContinuation context, Func<bool> current)
        {
            if (context.ExecutionId != Now?.Execution?.ExecutionId || context.WorkstreamId != Now?.Character.WorkstreamId)
                throw new InvalidOperationException("A request view cannot retarget another task or execution.");
            if (RequestView != null)
            {
                if (RequestView.ExecutionId != context.ExecutionId) throw new InvalidOperationException("A retained request belongs to another execution.");
                return RequestView;
            }
            context.Request = ContinuationRequest();
            requestExecution = context.ExecutionId;
            RequestView = new WorkspaceRequestColumn(host, commands, context, current);
            RequestView.Changed += RequestChanged;
            return RequestView;
        }
        private void RequestChanged() => Changed?.Invoke();
        private void DropRequest()
        {
            if (RequestView != null) { RequestView.Changed -= RequestChanged; RequestView.FocusLeft(); }
            RequestView = null; retainedRequest = null; requestExecution = null;
        }

        /// <summary>The local ordinary editor draft, never a question answer or armed decision.</summary>
        public WorkspaceInstructionDraft? ContinuationDraft() => Screen.Instruction.Open && Screen.Instruction.ExecutionId == Now?.Execution?.ExecutionId
            ? new WorkspaceInstructionDraft { Text = Screen.Instruction.Words, Spoken = Screen.Instruction.Spoken, Part = Screen.Instruction.Part } : null;

        /// <summary>Applies a validated current panel view with fresh input/read/confirmation proof. It sends nothing.</summary>
        public bool RestoreContinuation(WorkspaceContinuation continuation)
        {
            if (Now == null || continuation.WorkstreamId != Now.Character.WorkstreamId
                || continuation.ExecutionId != Now.Execution?.ExecutionId || continuation.Page < 0 || continuation.Page > 100000
                || !Enum.IsDefined(typeof(WorkspaceSection), continuation.Section)) return false;
            var draft = continuation.Instruction;
            var requestDraft = continuation.Request;
            if (requestDraft != null && (continuation.ExecutionId == null || !WorkspaceContinuity.IsDraftTextWithinLimit(requestDraft.Text) || requestDraft.Part < 0 || requestDraft.Part > 100000)) return false;
            if (draft != null && (continuation.ExecutionId == null || !WorkspaceContinuity.IsDraftTextWithinLimit(draft.Text) || draft.Part < 0 || draft.Part > 100000)) return false;
            Compact();
            Steering.Cancel();
            Screen.ReadAgainAtNewSize();
            Screen.Section = (FileSection)continuation.Section;
            Screen.Page = (int)continuation.Page;
            if (draft != null) Screen.Instruction.Restore(continuation.ExecutionId!, draft.Text, draft.Spoken, (int)draft.Part);
            else Screen.Instruction.Close();
            DropRequest();
            Surface = continuation.Surface;
            requestExecution = continuation.ExecutionId;
            retainedRequest = requestDraft == null ? null : new WorkspaceRequestContinuation
            { RequestId = requestDraft.RequestId, PendingCommandId = requestDraft.PendingCommandId, Text = requestDraft.Text, Spoken = requestDraft.Spoken, Part = requestDraft.Part };
            budget = null;
            Rebuild();
            return true;
        }

        /// <summary>Explicit workspace movement changes page room and revokes prior input and reading proof.</summary>
        public void Reflow()
        {
            Compact();
            Screen.ReadAgainAtNewSize();
            Steering.Cancel();
            budget = null;
            Rebuild();
            Changed?.Invoke();
        }

        /// <summary>Whether the page drawn offers Stop, so a refusal of words says to press it only where it can be.</summary>
        private bool StopOnPage => Frame?.Footer[PromptSlot.Rare]?.Id == FileScreens.Stop || Frame?.Lines.Any(line => line.Action == FileScreens.Stop) == true;

        public event Action? Changed;

        public event Action? Closed;

        /// <summary>Preserve only ordinary words before an execution change invalidates the local editor.</summary>
        public event Action<string, WorkspaceInstructionDraft>? OrdinaryDraftLeaving;
        public event Action<string, WorkspaceRequestContinuation>? RequestDraftLeaving;

        /// <summary>A refused credential, as the connection says it for how this headset reaches the computer.</summary>
        private string AccessRefused() => host.Api?.AccessRefused ?? ConnectionText.AccessRefused;

        private IIntelligenceReader Reader() => reader() ?? throw new UnaskedReadException(IntelligenceText.NotConnected);

        public void Act(string id, string? key)
        {
            if (Now == null) return;
            // The text size changed and nothing has laid the page again: it is laid again first, so the
            // press is judged against what is read at this size, never the reading at the other.
            if (Resized()) Rebuild();
            // A press is judged against the work as it is now, not as the last rebuild read it up to
            // RefreshSeconds ago, so the steering's checks see a request or question that changed since.
            if (present() is WorkspacePresentation fresh)
            {
                if (Now.Execution?.ExecutionId != fresh.Execution?.ExecutionId)
                {
                    // Preserve/revoke the old editors while Now still belongs to their execution.
                    // The old drawn action is discarded; the new run needs a new physical draw.
                    Rebuild(); return;
                }
                Now = fresh;
            }
            else
            {
                Closed?.Invoke();
                return;
            }
            var question = Screen.Question;
            var clock = host.Clock;
            switch (id)
            {
                case FileScreens.ShowTaskHere:
                    var taskWasDrawn = drawnShowTaskFrame == Frame;
                    drawnShowTaskFrame = null; // Failed authority cannot revive an old draw after reconnect.
                    if (taskWasDrawn && Frame?.Lines.Any(line => line.Action == id && line.Available) == true && CanShowTaskHere()) showTask?.Invoke();
                    return;
                case FileScreens.OpenCharacterColour:
                    var colourWasDrawn = drawnColourFrame == Frame;
                    drawnColourFrame = null;
                    if (colourWasDrawn && CanColour() && Frame?.Lines.Any(line => line.Action == id && line.Available) == true) openCharacterColour?.Invoke();
                    return;
                case FileScreens.OpenResults:
                    if (drawnResultsFrame == Frame && Frame?.Lines.Any(line => line.Action == id && line.Available) == true) openResults?.Invoke();
                    return;
                case FileScreens.OpenWorkspaceRequest:
                    if (Frame?.Lines.Any(line => line.Action == id && line.Available) == true
                        && Now.QuestionToAnswer?.Prompts.Any(prompt => prompt.Secret) != true) openWorkspaceRequest?.Invoke();
                    return;
                case Footer.Close:
                    instructionKeyboardSession++;
                    Screen.Instruction.Close();
                    instructionVoiceValid = false;
                    Steering.StopTyping();
                    question.CancelInput();
                    answering = false;
                    acted?.Invoke(WorkspaceAct.Collapse);
                    Closed?.Invoke();
                    return;
                case MenuFrame.ChooseSection when FileScreens.SectionOf(key) is FileSection section:
                    if (Screen.Instruction.Open) return;
                    instructionVoiceValid = false;
                    Screen.Section = section;
                    break;
                case FileScreens.Open:
                    Screen.Chosen = Screen.Chosen == key ? null : key;
                    break;
                case SidePanel.Close:
                    // Closes whichever side panel shows, a chosen answer's too, so the page comes back.
                    Screen.Chosen = null;
                    question.CloseSide();
                    break;
                case FileScreens.Refresh:
                    var execution = Now.Execution;
                    understanding.Refresh(execution?.UpdatedAt, clock);
                    evaluation.Refresh(execution?.UpdatedAt, clock);
                    break;
                case Footer.NextPage:
                    if (Screen.Section == FileSection.Waiting && question.SideOption != null) question.NextSidePart(clock);
                    else Screen.NextPage();
                    break;
                case FileScreens.NextPart when key == FileScreens.RequestKey:
                    Screen.NextRequestPart(Steering, clock);
                    break;
                case FileScreens.NextPart when key == FileScreens.QuestionKey || question.QuestionPart != null:
                    question.NextPart(clock);
                    break;
                case FileScreens.ReadWhy when Index(key) is int alike:
                    question.ExplainAmbiguity(alike);
                    answering = false;
                    break;
                case FileScreens.BackAmbiguity:
                    question.CloseAmbiguity();
                    break;
                case FileScreens.NextAmbiguityPart:
                    question.NextAmbiguityPart(clock);
                    break;
                case FileScreens.ReadQuestion:
                    question.ReopenQuestion();
                    break;
                case FileScreens.MoreAnswers:
                    question.MoreAnswers(clock);
                    break;
                case FileScreens.NextQuestion:
                    if (FileScreens.WhyNextWaits(question) == null) question.NextQuestion(clock);
                    break;
                case FileScreens.BackQuestion:
                    question.Back(clock);
                    break;
                case FileScreens.PreviousReviewPart:
                    question.PreviousReviewPart(clock);
                    break;
                case FileScreens.CancelInput:
                    question.CancelInput();
                    answering = false;
                    break;
                case FileScreens.NextInputPart:
                    question.NextInputPart(clock);
                    break;
                case FileScreens.PreviousInputPart:
                    question.PreviousInputPart(clock);
                    break;
                case FileScreens.UseInput:
                    if (draft != null && CurrentInput(draft, question.Prompt, question.InputSession))
                    {
                        var before = draft.AnswersNow;
                        if (question.UseInput(StopOnPage) && !QuestionDraft.SameAnswers(before, draft.AnswersNow)) Steering.AnswerChanging();
                    }
                    answering = false;
                    break;
                case FileScreens.EditInput:
                    if (question.Writing) { question.InputEditing = true; OpenAnswerKeyboard(); Rebuild(); }
                    return;
                case FileScreens.GoToQuestion when Index(key) is int prompt:
                    question.GoTo(prompt);
                    break;
                case FileScreens.Choose when Index(key) is int option:
                    // Only an answer on the page in view is taken; nothing is sent.
                    question.Choose(option);
                    break;
                case FileScreens.TypeAnswer:
                    instructionVoiceValid = false;
                    if (draft != null && question.BeginInput()) OpenAnswerKeyboard();
                    Rebuild();
                    return;
                case FileScreens.HoldToTalk:
                case FileScreens.SpeakAnswer:
                    // Pressed and let go before its hold started.
                    Notify(VoiceText.TooShort);
                    break;
                case FileScreens.BackPresetNotice:
                    if (drawnPresetFrame != Frame) return;
                    Screen.BackFromPresetNotice();
                    notice = null;
                    break;
                case FileScreens.NextPresetNotice:
                    if (drawnPresetFrame != Frame) return;
                    Screen.NextPresetNotice(clock);
                    break;
                case FileScreens.NextPresetPage:
                    if (drawnPresetFrame != Frame) return;
                    Screen.NextPresetPage(clock);
                    break;
                case FileScreens.Preset when Index(key) is int chosen:
                    if (drawnPresetFrame != Frame || Screen.ShowingPresetNotice || !Screen.PresetIndices.Contains(chosen)) return;
                    // A row only chooses; Review instruction copies the chosen words into the local editor.
                    Screen.ChoosePreset(chosen);
                    break;
                case FileScreens.BackInstructionNotice:
                    if (drawnInstructionNoticeFrame != Frame) return;
                    Screen.Instruction.BackFromNotice();
                    notice = null;
                    break;
                case FileScreens.NextInstructionNotice:
                    if (drawnInstructionNoticeFrame != Frame) return;
                    Screen.Instruction.NextNotice(host.Clock);
                    break;
                case FileScreens.CancelInstruction:
                    instructionKeyboardSession++;
                    instructionVoiceValid = false;
                    Screen.Instruction.Close();
                    Steering.StopTyping();
                    Steering.Cancel();
                    presets = false;
                    break;
                case FileScreens.EditInstruction:
                    if (Steering.Armed != null || drawnInstructionFrame != Frame || Frame?.Lines.Any(line => line.Action == FileScreens.EditInstruction && line.Available) != true) return;
                    OpenInstructionKeyboard();
                    return;
                case FileScreens.NextInstructionPart:
                    if (drawnInstructionFrame == Frame && Frame!.Lines.Any(line => line.Action == id)) Screen.Instruction.Next(host.Clock);
                    break;
                case FileScreens.PreviousInstructionPart:
                    if (drawnInstructionFrame == Frame && Frame!.Lines.Any(line => line.Action == id)) Screen.Instruction.Previous(host.Clock);
                    break;
                case FileScreens.SendInstruction:
                    if (drawnInstructionFrame != Frame || !host.Connected || FileScreens.InstructionSendReason(Now, Screen) != null) return;
                    instructionKeyboardSession++;
                    instructionVoiceValid = false;
                    Steer(Steering.SendInstruction(Screen.Instruction, Now));
                    return;
                case FileScreens.TellIt when Screen.Presets != null:
                    if (Screen.PresetToSend is string words)
                    {
                        presets = false;
                        if (Now.Execution == null) return;
                        Screen.Instruction.Begin(Now.Execution.ExecutionId);
                        Screen.Instruction.Changed(words, spoken: false);
                        Rebuild();
                        return;
                    }
                    Notify(FileScreens.ChooseAnInstruction);
                    break;
                case FileScreens.Yes:
                    Steer(Steering.Confirm(Now));
                    return;
                case FileScreens.Cancel:
                    instructionKeyboardSession++;
                    Steering.StopTyping();
                    Steering.Cancel();
                    presets = false;
                    Screen.ForgetRequest();
                    break;
                case FileScreens.SendAnswer:
                    Steer(Steering.SendAnswer(Screen, Now));
                    return;
                default:
                    if (WorkspaceScreens.ActionOf(id) is WorkspaceAction action && action != WorkspaceAction.Answer)
                    {
                        Steer(Steering.Press(action, Now));
                        return;
                    }
                    return;
            }
            Rebuild();
        }

        private static int? Index(string? key) => int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : (int?)null;

        /// <summary>
        /// Carries out what the steering decided: sends through the host, says why nothing was sent, or
        /// asks for the words of an instruction. Nothing else sends.
        /// </summary>
        private void Steer(SteeringOutcome outcome)
        {
            switch (outcome.Step)
            {
                case SteeringStep.Send:
                    Submit(outcome.Command!);
                    break;
                case SteeringStep.Explain:
                    Notify(outcome.Message!);
                    break;
                case SteeringStep.Type:
                    AskForWords();
                    break;
            }
            Rebuild();
        }

        private void Submit(CommandEnvelope command)
        {
            if (host.Submit(command) is null)
            {
                Notify("Couldn't send: " + HostText.Your + " isn't connected. Try again when it is.");
                return;
            }
            notice = null;
            // An answer sent shows how it goes with the activity: sent, then taken, refused or not confirmed.
            if (command is ExecutionSendInstructionCommand) Screen.Instruction.Close();
            if (command is ExecutionAnswerQuestionCommand) Screen.Section = FileSection.Activity;
            if (WorkspaceActs.Of(command) is WorkspaceAct act) acted?.Invoke(act);
        }

        /// <summary>
        /// Tell it: the demonstration's recorded instructions where it plays, as rows to choose; else the
        /// keyboard, whose words the steering takes. Closed without words, the page's Cancel stops it.
        /// </summary>
        private void AskForWords()
        {
            var execution = Now?.Execution?.ExecutionId;
            if (execution == null) return;
            recordedNow = recorded(execution);
            if (recordedNow?.Count > 0 || !host.KeyboardOffered)
            {
                Steering.StopTyping();
                presets = true;
                return;
            }
            Screen.Instruction.Begin(execution);
            OpenInstructionKeyboard();
        }

        private bool CurrentInstruction(string execution, int epoch)
        {
            var current = present();
            return host.Connected && Screen.Instruction.Open && Screen.Instruction.ExecutionId == execution && Screen.Instruction.Epoch == epoch
                && current?.Execution?.ExecutionId == execution && current.Actions.Contains(WorkspaceAction.Instruct) && WorkspaceText.NeedFromYou(current) == null && Steering.Armed == null;
        }

        private void OpenInstructionKeyboard()
        {
            if (!host.KeyboardOffered || !Screen.Instruction.Open || Screen.Instruction.ExecutionId is not string execution) return;
            Steering.Cancel();
            Screen.ForgetRequest();
            instructionVoiceValid = false;
            Screen.Instruction.Edit();
            var epoch = Screen.Instruction.Epoch;
            var session = ++instructionKeyboardSession;
            Rebuild();
            if (!CurrentInstruction(execution, epoch)) return;
            host.OpenKeyboard(Screen.Instruction.Words, WorkspaceText.TellItPrompt, text =>
            {
                if (session != instructionKeyboardSession || !CurrentInstruction(execution, epoch)) return;
                instructionKeyboardSession++;
                Screen.Instruction.Changed(text, spoken: false);
                Steering.StopTyping();
                Rebuild();
            }, () =>
            {
                if (session != instructionKeyboardSession || !CurrentInstruction(execution, epoch)) return;
                instructionKeyboardSession++;
                Screen.Instruction.Suspend();
                Steering.StopTyping();
                Rebuild();
            });
        }

        private bool CurrentInput(QuestionDraft answeringDraft, int prompt, int session)
        {
            var current = present();
            return host.Connected && Screen.Section == FileSection.Waiting && Screen.Question.Writing
                && Screen.Question.InputSession == session && Screen.Question.Prompt == prompt && ReferenceEquals(answeringDraft, draft)
                && current?.Execution != null && answeringDraft.Answers(current.Execution.ExecutionId, current.QuestionToAnswer)
                && answeringDraft.Prompts[prompt].FreeText && WorkspaceText.Answerable(answeringDraft.Question)
                && WorkspaceText.NeedFromYou(current) == null && !current.AnswerInFlight && current.Actions.Contains(WorkspaceAction.Answer) && Steering.Armed == null;
        }

        private void OpenAnswerKeyboard()
        {
            var question = Screen.Question;
            if (!question.Writing || draft == null || !host.KeyboardOffered) return;
            question.InvalidateInput();
            question.InputEditing = true;
            question.InputFocused = true;
            var forDraft = draft;
            var prompt = question.Prompt;
            var session = question.InputSession;
            Rebuild();
            if (!CurrentInput(forDraft, prompt, session)) return;
            MenuKeyboard.Open(host, question.Input, "Your answer", text =>
            {
                if (!CurrentInput(forDraft, prompt, session)) return;
                question.ChangeInput(text);
                Rebuild();
            }, () =>
            {
                if (!CurrentInput(forDraft, prompt, session)) return;
                question.KeyboardClosed();
                Rebuild();
            });
        }

        public void Drawn(MenuFrame drawn, Footer? sidePanel)
        {
            // Drawn at the size before: laid again at this one, that draw counts for nothing.
            if (Resized())
            {
                Rebuild();
                return;
            }
            // Only the frame this column stands by counts: what it shows is this state's.
            if (drawn != Frame || Now == null) return;
            var clock = host.Clock;
            if (Screen.Presets != null && Steering.Armed == null && (Screen.ShowingPresetNotice || drawn.Lines.Any(line => line.Action == FileScreens.Preset)))
            {
                drawnPresetFrame = drawn;
                if (Screen.ShowingPresetNotice) Screen.PresetNoticeDrawn(clock);
                else Screen.PresetDrawn(clock);
            }
            if (sidePanel != null)
            {
                if (Steering.Armed == null && Screen.Section == FileSection.Waiting && Screen.Question.SideOption != null) Screen.Question.SideDrawn(clock);
                return;
            }
            if (drawn.Lines.Any(line => line.Action == FileScreens.ShowTaskHere && line.Available)) drawnShowTaskFrame = drawn;
            if (drawn.Lines.Any(line => line.Action == FileScreens.OpenResults && line.Available)) drawnResultsFrame = drawn;
            if (drawn.Lines.Any(line => line.Action == FileScreens.OpenCharacterColour && line.Available)) drawnColourFrame = drawn;
            if (Steering.ToRead(Now) is string request)
            {
                var from = Screen.RequestPart * Screen.RequestPartRows;
                if (Screen.Measured(Steering, request) && drawn.Lines.Any(line => line.Words == request && line.FromRow == from))
                {
                    var could = Steering.CanConfirm;
                    Screen.RequestDrawn(Screen.RequestPart, Steering, clock);
                    // Built again only when Yes may show now. Building on every draw would hand the
                    // director a new frame each time, so no press would ever stand on the one drawn.
                    if (Steering.CanConfirm != could) Rebuild();
                }
                return;
            }
            if (Screen.Instruction.Open && Steering.Armed == null)
            {
                if (Screen.Instruction.ExplainingNotice)
                {
                    drawnInstructionNoticeFrame = drawn;
                    Screen.Instruction.NoticeDrawn(host.Clock);
                    return;
                }
                drawnInstructionFrame = drawn;
                var before = Screen.Instruction.Read;
                Screen.Instruction.Drawn(host.Clock);
                if (before != Screen.Instruction.Read) Rebuild();
                return;
            }
            // Only the question's own page, as built, counts toward it, never another Waiting shows meanwhile,
            // whatever its words: an approval's request may read as the prompt itself.
            if (Steering.Armed == null && Screen.Section == FileSection.Waiting && Screen.QuestionPage && Screen.Question.Draft != null)
            {
                var before = Frame!.Footer[PromptSlot.FarRight]?.Available;
                var beforeReason = Frame.Footer[PromptSlot.FarRight]?.Reason;
                var beforeExplains = Frame.Footer[PromptSlot.FarRight]?.PageExplains;
                Screen.Question.Drawn(clock);
                var after = FileScreens.QuestionForwardAvailable(Now, Screen);
                if (before != after || beforeReason != FileScreens.QuestionForwardReason(Now, Screen)
                    || (before.HasValue && Screen.Question.QuestionPart == null && !Screen.Question.Reviewing && !Screen.Question.Writing && !Screen.Question.ExplainingAmbiguity && beforeExplains != FileScreens.ChoiceReasonExplained(Screen.Question))) Rebuild();
            }
        }

        public void HoldStarted(string id)
        {
            if (id != FileScreens.SpeakAnswer)
            {
                answerVoiceStarted = false;
                answering = false;
                instructionVoiceStarted = id == FileScreens.HoldToTalk;
                var current = present();
                instructionVoiceExecution = current?.Execution?.ExecutionId;
                instructionVoiceEpoch = Screen.Instruction.Epoch;
                instructionVoiceSection = Screen.Section;
                instructionVoiceValid = instructionVoiceStarted && Frame?.Footer.All.Any(each => each.Prompt.Id == FileScreens.HoldToTalk && each.Prompt.Available) == true
                    && host.VoiceOffered && host.Connected && instructionVoiceExecution != null
                    && current!.Actions.Contains(WorkspaceAction.Instruct) && WorkspaceText.NeedFromYou(current) == null && Steering.Armed == null && !Screen.Question.Writing && !Screen.Question.Reviewing;
                return;
            }
            instructionVoiceValid = false;
            answerVoiceStarted = true;
            answering = draft != null && Screen.Question.Writing && !Screen.Question.InputReading && host.VoiceOffered
                && CurrentInput(draft, Screen.Question.Prompt, Screen.Question.InputSession);
            if (!answering) return;
            answerPrompt = Screen.Question.Prompt;
            answerDraft = draft;
            answerInputSession = Screen.Question.InputSession;
        }

        /// <summary>Where the voice stands after a hold shows on Hold to talk itself, drawn by the plane from the menu's one voice (ADR 0027).</summary>
        public void HoldEnded(string id, bool letGo)
        {
            if (id == FileScreens.SpeakAnswer && !letGo) answering = false;
            if (id == FileScreens.HoldToTalk && !letGo) instructionVoiceValid = false;
        }

        public void Heard(string text)
        {
            if (answerVoiceStarted)
            {
                if (!answering || answerDraft == null || !CurrentInput(answerDraft, answerPrompt, answerInputSession))
                    Notify(VoiceText.QuestionChangedWhileSpeaking);
                else Screen.Question.ChangeInput(text);
                answering = false;
                // Retain the answer route after cancellation, so delayed voice can never fall through as an instruction.
                Rebuild();
                return;
            }
            var current = present();
            if (instructionVoiceStarted && instructionVoiceValid && host.Connected && instructionVoiceEpoch == Screen.Instruction.Epoch && instructionVoiceSection == Screen.Section
                && !Screen.Question.Writing && !Screen.Question.Reviewing
                && current?.Execution?.ExecutionId == instructionVoiceExecution && current!.Actions.Contains(WorkspaceAction.Instruct) && WorkspaceText.NeedFromYou(current) == null && Steering.Armed == null)
            {
                instructionKeyboardSession++;
                Screen.Instruction.Begin(instructionVoiceExecution!);
                Screen.Instruction.Changed(text, spoken: true);
                Steering.StopTyping();
            }
            instructionVoiceValid = false;
            Rebuild();
        }

        public void Said(string words)
        {
            // Where the voice stands, listening or writing down, shows on Hold to talk itself, drawn by the plane
            // from the menu's one voice: a line for it would grow the page and move the very prompt held.
            // Anything else it says is a notice.
            if (words == VoiceText.Listening || words == VoiceText.Hearing) return;
            Notify(words);
            Rebuild();
        }

        public void Tick()
        {
            var execution = Now?.Execution;
            var clock = host.Clock;
            if (Screen.Section == FileSection.Changes || Screen.Section == FileSection.Checks)
            {
                understanding.Show(execution?.ExecutionId, execution?.UpdatedAt, clock, TimeSpan.FromSeconds(2));
            }
            if (Screen.Section == FileSection.Checks) evaluation.Show(execution?.ExecutionId, execution?.UpdatedAt, clock);
            var read = understanding.Poll() | evaluation.Poll();
            // A text size changed in Settings packs the page again at once, so it never stands taller than fits.
            if (read || host.Now >= nextRefresh || Resized()) Rebuild();
        }

        /// <summary>Retains the exact local page and buffer while dropping every interaction authorization.</summary>
        public void Compact()
        {
            drawnColourFrame = null;
            drawnResultsFrame = null;
            drawnShowTaskFrame = null;
            RequestView?.Compact();
            drawnPresetFrame = null;
            Screen.SuspendPresets();
            Screen.Question.SuspendPresentation();
            Screen.Instruction.Suspend();
            drawnInstructionFrame = null;
            drawnInstructionNoticeFrame = null;
            instructionVoiceValid = false;
            instructionKeyboardSession++;
            Steering.StopTyping();
            answering = false;
            var outcome = Steering.FocusLeft();
            if (outcome.Step == SteeringStep.Explain) Notify(outcome.Message!);
            Rebuild();
        }

        public void FocusLeft()
        {
            drawnColourFrame = null;
            drawnResultsFrame = null;
            drawnShowTaskFrame = null;
            RequestView?.FocusLeft();
            drawnPresetFrame = null;
            Screen.SuspendPresets();
            Screen.Question.InvalidateInput();
            Screen.Instruction.Suspend();
            drawnInstructionFrame = null;
            drawnInstructionNoticeFrame = null;
            instructionVoiceValid = false;
            instructionKeyboardSession++;
            Steering.StopTyping();
            answering = false;
            var outcome = Steering.FocusLeft();
            if (outcome.Step == SteeringStep.Explain) Notify(outcome.Message!);
            Rebuild();
        }

        /// <summary>The text size changed since the page was last packed, and nothing has laid it again yet.</summary>
        private bool Resized() => budget != null && budgetAt != host.TextSize;

        private void Notify(string words)
        {
            notice = words;
            noticeUntil = host.Now + NoticeSeconds;
            // This is a new feedback event even when its words match the previous failure.
            if (Screen.Section == FileSection.Activity) Screen.Page = 0;
        }

        /// <summary>
        /// Builds the frame from the work as it stands: the question measured as the view wraps it, the
        /// request's parts, the answers read, then the page; the director draws it once told.
        /// </summary>
        private void Rebuild()
        {
            drawnColourFrame = null;
            drawnPresetFrame = null;
            nextRefresh = host.Now + RefreshSeconds;
            drawnResultsFrame = null;
            drawnShowTaskFrame = null;
            var presentation = present();
            if (presentation == null)
            {
                // The work is gone from the state.
                Closed?.Invoke();
                return;
            }
            var leavingExecution = Now?.Execution?.ExecutionId;
            if (leavingExecution != null && leavingExecution != presentation.Execution?.ExecutionId && ContinuationRequest() is WorkspaceRequestContinuation leavingRequest)
            { RequestDraftLeaving?.Invoke(leavingExecution, leavingRequest); DropRequest(); Surface = WorkspaceSurface.Task; }
            Now = presentation;
            Screen.CharacterColourOffered = CanColour();
            drawnInstructionFrame = null;
            drawnInstructionNoticeFrame = null;
            if (Screen.Instruction.Open && Screen.Instruction.ExecutionId != presentation.Execution?.ExecutionId)
            {
                OrdinaryDraftLeaving?.Invoke(Screen.Instruction.ExecutionId!, new WorkspaceInstructionDraft
                { Text = Screen.Instruction.Words, Spoken = Screen.Instruction.Spoken, Part = Screen.Instruction.Part });
                Screen.Instruction.Close();
                instructionKeyboardSession++;
                instructionVoiceValid = false;
                Steering.Cancel();
                Notify(FileScreens.InstructionChanged);
            }
            var instructionEligible = host.Connected && presentation.Actions.Contains(WorkspaceAction.Instruct) && WorkspaceText.NeedFromYou(presentation) == null;
            if (!instructionEligible && (Screen.Instruction.InputFocused || instructionVoiceValid))
            {
                instructionKeyboardSession++;
                Screen.Instruction.Suspend();
                instructionVoiceValid = false;
                Steering.StopTyping();
                Steering.FocusLeft();
            }
            var asked = presentation.QuestionToAnswer;
            var execution = presentation.Execution?.ExecutionId;
            if (asked == null || execution == null) draft = null;
            else if (draft == null || !draft.Answers(execution, asked)) draft = new QuestionDraft(execution, asked);
            if (Steering.Refresh(presentation) is string lapse) Notify(lapse);
            // At another text size, what was read was read at the other: the request and the question are
            // read again, and an answer armed to send is not sent on a Yes for what was read before.
            var resized = budget != null && budgetAt != host.TextSize;
            if (resized)
            {
                Screen.ReadAgainAtNewSize();
                if (Steering.TextSizeChanged() is string cancelled) Notify(cancelled);
            }
            if (notice != null && host.Now > noticeUntil) notice = null;
            if (Screen.Section == FileSection.Waiting && !WorkspaceText.SomethingWaits(presentation) && Steering.Armed == null) Screen.Section = FileSection.Activity;
            Screen.Notice = notice;
            Screen.KeyboardOffered = host.KeyboardOffered;
            Screen.Presets = presets ? (recordedNow?.Count > 0 ? recordedNow : WorkspaceText.PresetInstructions) : null;
            Screen.ActivityNote = historyNote();

            var focused = Screen.Instruction.Open || (Screen.Presets != null && Steering.Armed == null)
                || FileScreens.FocusesAnswer(presentation, Screen.Section == FileSection.Waiting && Steering.Armed == null, Steering);
            if (budget == null || resized || budgetFocused != focused)
            {
                budgetFocused = focused;
                budget = Budget(presentation);
                budgetAt = host.TextSize;
            }
            if (Screen.Presets != null)
            {
                var recordedPresets = !ReferenceEquals(Screen.Presets, WorkspaceText.PresetInstructions);
                Screen.MeasurePresets(Screen.Presets.Select((preset, index) => Math.Min(2, host.RowsOf(FileScreens.PresetLine(preset, index, recordedPresets), Glaze.Menu.FileColumnDegrees))).ToList(), budget,
                    notice == null ? 0 : host.RowsOf(new PageLine(notice, rows: int.MaxValue), Glaze.Menu.FileColumnDegrees));
            }
            if (draft != null) ReadQuestion(draft, budget);
            Screen.ShowTaskHereOffered = CanShowTaskHere();
            if (Screen.ShowTaskHereOffered) Screen.ShowTaskHereRows = Math.Max(1, host.RowsOf(new PageLine("Show this task here", rows: int.MaxValue), Glaze.Menu.FileColumnDegrees));
            if (Screen.WorkspaceRequestOffered) Screen.WorkspaceRequestRows = Math.Max(1, host.RowsOf(new PageLine("Ask workspace", icon: GlazeIcon.Next,
                action: FileScreens.OpenWorkspaceRequest, opens: true, rows: int.MaxValue), Glaze.Menu.FileColumnDegrees));
            if (Screen.CharacterColourOffered) Screen.CharacterColourRows = Math.Max(1, host.RowsOf(new PageLine("Character colour", rows: int.MaxValue), Glaze.Menu.FileColumnDegrees));
            if (Screen.ResultsOffered) Screen.ResultsRows = Math.Max(1, host.RowsOf(FileScreens.ReadResultsLine(rows: int.MaxValue), Glaze.Menu.FileColumnDegrees));
            Screen.InstructionResultsOffered = Screen.ResultsOffered && presentation.Execution != null && Steering.Armed == null
                && WorkspaceText.NeedFromYou(presentation) == null && asked == null && presentation.ApprovalToAnswer == null && notice == null;
            if (Screen.Instruction.Open)
                Screen.Instruction.Measure(host.RowsOf(FileScreens.InstructionLine(Screen.Instruction, host.KeyboardOffered, rows: int.MaxValue), Glaze.Menu.FileColumnDegrees), budget, host.TextSize,
                    host.FitsHalf(FileScreens.InstructionPager(previous: true), Glaze.Menu.FileColumnDegrees) && host.FitsHalf(FileScreens.InstructionPager(), Glaze.Menu.FileColumnDegrees),
                    notice, notice == null ? 0 : host.RowsOf(new PageLine(notice, rows: int.MaxValue), Glaze.Menu.FileColumnDegrees),
                    navigationHeight: Screen.InstructionResultsOffered ? budget.Target(Screen.ResultsRows) + budget.TargetGap : 0);
            // Answers armed to send that changed since, by a choice, a page turned or a new layout, cancel
            // its Yes at the change, as words heard or typed do.
            if (Steering.AnswersMoved() is string moved)
            {
                Notify(moved);
                Screen.Notice = notice;
            }
            // The request an approval answers, or an instruction's words, read whole before its Yes.
            if (Steering.ToRead(presentation) is string request)
            {
                Screen.ReadRequest(request, host.RowsOf(FileScreens.RequestLine(Steering, request), Glaze.Menu.FileColumnDegrees),
                    RequestPartRows(FileScreens.AllAsks(Steering), budget), Steering);
            }

            // Each line measured as the page draws it, its runs, chip and icon (FileScreens.Line).
            var room = new AnswerRoom(host.PageRows(sourceLine: false), line =>
                host.RowsOf(FileScreens.Line(line, rows: 1), Glaze.Menu.FileColumnDegrees));
            var clock = host.Clock;
            if (Screen.Section == FileSection.Changes)
            {
                Screen.WhatChanged = Understand(UnderstandPrompt.WhatChanged, room, clock);
                Screen.WhyChanged = Understand(UnderstandPrompt.WhyChanged, room, clock);
                Screen.HowBuilt = Understand(UnderstandPrompt.HowBuilt, room, clock);
            }
            if (Screen.Section == FileSection.Checks)
            {
                Screen.Checked = understanding.Last == null && understanding.Error == null ? null : new FileAnswer(
                    CheckedPresenter.Present(understanding, evaluation, clock, host.Zone, room, AnswerDepth.Brief),
                    CheckedPresenter.Present(understanding, evaluation, clock, host.Zone, AnswerRoom.Unlimited, AnswerDepth.Full));
            }
            if (!host.Connected || Screen.Section != FileSection.Waiting || Steering.Armed != null || WorkspaceText.NeedFromYou(presentation) != null
                || !presentation.Actions.Contains(WorkspaceAction.Answer))
            {
                if (Screen.Question.Writing) Screen.Question.CancelInput();
                answering = false;
            }
            Frame = FileScreens.Screen(presentation, Steering, Screen, room, budget);
            Changed?.Invoke();
        }

        /// <summary>
        /// The page's room on this stage, read when the file opens and again only when the text size
        /// changes, so it packs once a size, never as the head moves: the file's alone (ADR 0026), the
        /// menu stepping aside where the two together would not fit.
        /// </summary>
        private PageBudget Budget(WorkspacePresentation presentation)
        {
            var rows = host.TitleRows(presentation.Character.Title, Glaze.Menu.FileColumnDegrees);
            return new HeightBudget(host.PageHeight(rows, besideMenu: false, focused: budgetFocused));
        }

        private FileAnswer? Understand(UnderstandPrompt prompt, AnswerRoom room, DateTimeOffset clock) =>
            understanding.Last == null && understanding.Error == null ? null : new FileAnswer(
                UnderstandingPresenter.Present(prompt, understanding, clock, host.Zone, room, AnswerDepth.Brief),
                UnderstandingPresenter.Present(prompt, understanding, clock, host.Zone, AnswerRoom.Unlimited, AnswerDepth.Full));

        /// <summary>
        /// The agent's question measured as the view wraps it at the file's and the side panel's widths:
        /// every prompt and every answer, and the typed answers and the person's answers as they read now.
        /// </summary>
        private void ReadQuestion(QuestionDraft asked, PageBudget page)
        {
            const float File = Glaze.Menu.FileColumnDegrees;
            const float Side = Glaze.Menu.SideColumnDegrees;
            if (measuredFor != asked || measuredAt != host.TextSize || measures == null)
            {
                measures = asked.Prompts.Select((prompt, index) =>
                {
                    return new PromptMeasure(
                        host.RowsOf(new PageLine(FileScreens.QuestionWords(prompt)), File),
                        Enumerable.Range(0, prompt.Options.Count).Select(option => host.RowsOf(FileScreens.OptionRow(prompt, option), File)).ToList(),
                        prompt.Options.Select(option => host.RowsOf(FileScreens.SideAnswerLine(option, null), Side)).ToList(),
                        host.FitsHalf(FileScreens.InputNavigation(Screen.KeyboardOffered), File) && host.FitsHalf(FileScreens.ChoicePager(), File) && host.FitsHalf(FileScreens.ChoicePager(first: true), File),
                        host.FitsHalf(FileScreens.InputNavigation(Screen.KeyboardOffered, edit: true), File) && host.FitsHalf(FileScreens.ChoicePager(), File) && host.FitsHalf(FileScreens.ChoicePager(first: true), File),
                        host.RowsOf(new PageLine(FileScreens.QuestionCaption(index, asked.Prompts.Count, prompt)), File),
                        host.RowsOf(new PageLine(FileScreens.QuestionCaption(index, asked.Prompts.Count, prompt, Math.Max(0, prompt.Options.Count - 1), Math.Max(1, prompt.Options.Count))), File));
                }).ToList();
                measuredFor = asked;
                measuredAt = host.TextSize;
            }
            Screen.ReadQuestion(asked, measures, page, page);
            for (var prompt = 0; prompt < asked.Prompts.Count; prompt++)
            {
                if (!(asked.Typed(prompt) is string typed)) continue;
                Screen.Question.MeasureTyped(prompt,
                    host.RowsOf(FileScreens.SelectedTypedLine(typed), File),
                    host.RowsOf(FileScreens.SideAnswerLine(null, typed), Side));
            }
            if (Screen.Question.ExplainingAmbiguity) Screen.Question.MeasureAmbiguity(host.RowsOf(FileScreens.AmbiguityLine(Screen.Question), File));
            if (Screen.Question.Writing) Screen.Question.MeasureInput(host.RowsOf(FileScreens.InputLine(Screen.Question, Screen.KeyboardOffered), File));
            if (asked.Prompts.Count > 0)
            {
                Screen.Question.MeasureReview(Enumerable.Range(0, asked.Prompts.Count)
                    .Select(prompt => host.RowsOf(FileScreens.ReviewRow(asked, prompt), File))
                    .ToList());
            }
        }

        /// <summary>
        /// The rows of a request a part shows: what is left of the page beside the part's row and the
        /// tallest question the confirmation may ask, so its parts stay put when that question changes.
        /// </summary>
        public int RequestPartRows(IEnumerable<string> asking, PageBudget page) =>
            RequestPartRows(asking.Select(words => host.RowsOf(words, Glaze.Menu.FileColumnDegrees)).DefaultIfEmpty(1).Max(), page);

        /// <summary>The rows of a request a part shows, beside the part's row and a question of <paramref name="askingRows"/> rows, at most two.</summary>
        public static int RequestPartRows(int askingRows, PageBudget page)
        {
            var asked = Math.Min(2, Math.Max(1, askingRows));
            return page.WordsIn(page.Room - page.LineGap - page.Target() - page.LineGap - page.Words(asked));
        }
    }
}

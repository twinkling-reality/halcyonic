#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    public enum SteeringStep
    {
        /// <summary>Nothing changed.</summary>
        None,

        /// <summary>The action waits for a deliberate confirmation; show <see cref="WorkspaceSteering.Prompt"/>.</summary>
        Confirm,

        /// <summary>Open the keyboard for an instruction, then report it with <see cref="WorkspaceSteering.Typed"/>.</summary>
        Type,

        /// <summary>Send <see cref="SteeringOutcome.Command"/>.</summary>
        Send,

        /// <summary>Nothing was sent; <see cref="SteeringOutcome.Message"/> says why.</summary>
        Explain,
    }

    public sealed class SteeringOutcome
    {
        public static readonly SteeringOutcome Nothing = new SteeringOutcome(SteeringStep.None, null, null);

        private SteeringOutcome(SteeringStep step, CommandEnvelope? command, string? message)
        {
            Step = step;
            Command = command;
            Message = message;
        }

        public SteeringStep Step { get; }

        /// <summary>The command to send, for <see cref="SteeringStep.Send"/>.</summary>
        public CommandEnvelope? Command { get; }

        /// <summary>Why nothing was sent, for <see cref="SteeringStep.Explain"/>.</summary>
        public string? Message { get; }

        internal static SteeringOutcome Of(SteeringStep step) => new SteeringOutcome(step, null, null);

        internal static SteeringOutcome Send(CommandEnvelope command) => new SteeringOutcome(SteeringStep.Send, command, null);

        internal static SteeringOutcome Explain(string message) => new SteeringOutcome(SteeringStep.Explain, null, message);
    }

    /// <summary>
    /// Turns presses in an open workspace into commands. Only actions the workspace offers are taken,
    /// and those the control plane's policy marks for review wait for a second, deliberate press that
    /// names exactly what will be sent. An approval is confirmed only once the whole request it
    /// answers has been shown, part by part when it is long (<see cref="RequestShown"/>), so nobody
    /// approves what they could not read; an instruction only once all its words have, so none is sent
    /// unread, a mishearing least of all. A confirmation lapses when it is not given in time, or when
    /// the state it was asked about changes, so an old question is never answered by accident. One
    /// instance serves one open workspace; not thread safe.
    /// </summary>
    public sealed class WorkspaceSteering
    {
        public static readonly TimeSpan DefaultConfirmationWindow = TimeSpan.FromSeconds(15);

        private const string QuestionChanged = "Nothing was sent: the question changed. Read it again.";
        private const string NoInstructions = "Nothing was sent: it no longer takes instructions.";

        private readonly CommandFactory commands;
        private readonly Func<DateTimeOffset> now;
        private readonly TimeSpan window;
        private DateTimeOffset armedAt;
        private QuestionDraft? armedDraft;

        /// <summary>The answers as they stood when Send answer armed its confirmation, which Yes sends only unchanged.</summary>
        private IReadOnlyList<(string Key, string[] Chosen, string? Typed)>? armedAnswers;
        private int shownPart;

        public WorkspaceSteering(CommandFactory commands, Func<DateTimeOffset>? now = null, TimeSpan? confirmationWindow = null)
        {
            this.commands = commands;
            this.now = now ?? (() => DateTimeOffset.UtcNow);
            window = confirmationWindow ?? DefaultConfirmationWindow;
        }

        /// <summary>The action waiting for a deliberate confirmation, if any.</summary>
        public WorkspaceAction? Armed { get; private set; }

        /// <summary>
        /// How many times an action has been armed: each press that arms one, even the same action for
        /// the same request again, is a new confirmation whose parts are read afresh.
        /// </summary>
        public int Armings { get; private set; }

        /// <summary>The approval an armed approve or deny answers.</summary>
        public string? ArmedApprovalId { get; private set; }

        /// <summary>
        /// What the armed confirmation shows whole before its Yes, as it read when armed: the whole request
        /// an approve or deny answers, or an instruction's words, quoted (<see cref="Quoted"/>). Should the
        /// runtime report a request differently under the same approval, the confirmation lapses, so what
        /// is approved is what was read.
        /// </summary>
        public string? ArmedRequest { get; private set; }

        /// <summary>The instruction an armed instruct sends.</summary>
        public string? Instruction { get; private set; }

        /// <summary>The keyboard is open for an instruction.</summary>
        public bool Typing { get; private set; }

        /// <summary>The armed instruction was spoken and heard on the Mac, not typed.</summary>
        public bool Heard { get; private set; }

        /// <summary>
        /// Every part of the request the armed approval or denial answers, or of the armed instruction's
        /// words, has been shown (<see cref="RequestShown"/>).
        /// </summary>
        public bool WholeRequestShown { get; private set; }

        /// <summary>
        /// The armed action can be confirmed now: an approval or an instruction only once all of what it
        /// answers or sends has been shown; anything else at once, as denying or stopping runs nothing.
        /// </summary>
        public bool CanConfirm => Armed != null && (!(Armed == WorkspaceAction.Approve || Armed == WorkspaceAction.Instruct) || WholeRequestShown);

        /// <summary>The person pressed an action.</summary>
        public SteeringOutcome Press(WorkspaceAction action, WorkspacePresentation workspace)
        {
            if (Typing) return SteeringOutcome.Nothing;
            Cancel();
            // An answer goes only with the answers chosen, through SendAnswer.
            if (action == WorkspaceAction.Answer) return SteeringOutcome.Explain("Choose your answers, then press Send answer.");
            if (!workspace.Actions.Contains(action))
            {
                return SteeringOutcome.Explain(WorkspaceText.WhyNoActions(workspace) ?? "Nothing was sent: you can't do that now.");
            }
            if (action == WorkspaceAction.Instruct)
            {
                Typing = true;
                return SteeringOutcome.Of(SteeringStep.Type);
            }
            var approvalId = IsAnswer(action) ? workspace.ApprovalToAnswer?.ApprovalId : null;
            if (workspace.RequiresConfirmation(action))
            {
                Arm(action, approvalId, null);
                ArmedRequest = IsAnswer(action) ? Request(workspace) : null;
                return SteeringOutcome.Of(SteeringStep.Confirm);
            }
            return SteeringOutcome.Send(Build(action, workspace.Execution!.ExecutionId, approvalId, null));
        }

        /// <summary>
        /// The person pressed Send answer for the question shown (ADR 0022): the answers go as they
        /// are in <paramref name="draft"/>, only for the question the workspace shows now, only when
        /// every prompt is answered and was shown whole, and, should the control plane's policy ask
        /// for it, only after a second press. Nothing else sends an answer, so the gesture that brings
        /// focus back cannot.
        /// </summary>
        /// <summary>
        /// The person pressed Send answer on a file's page: the answers go only when the page has no
        /// reason to wait (<see cref="FileScreens.WhySendWaits"/>), which this always asks, so the file's
        /// path can't send what was not read whole or is out of view.
        /// </summary>
        public SteeringOutcome SendAnswer(FileScreen screen, WorkspacePresentation workspace)
        {
            if (!(screen.Question.Draft is QuestionDraft draft)) return SteeringOutcome.Explain(FileScreens.QuestionNotReady);
            return SendAnswer(draft, workspace, FileScreens.WhySendWaits(screen) ?? NoReason);
        }

        /// <summary>Marks the file's path as having asked the page: no reason is no reason.</summary>
        private const string? NoReason = null;

        /// <param name="waits">
        /// Why the surface showing the question can't send yet, as a file's page says it
        /// (<see cref="FileScreens.WhySendWaits"/>): what is to be sent not yet read whole or not in view.
        /// Nothing is sent while there is a reason.
        /// </param>
        public SteeringOutcome SendAnswer(QuestionDraft draft, WorkspacePresentation workspace, string? waits = null)
        {
            if (Typing) return SteeringOutcome.Nothing;
            Cancel();
            if (waits != null) return SteeringOutcome.Explain(waits);
            if (!workspace.Actions.Contains(WorkspaceAction.Answer))
            {
                return SteeringOutcome.Explain(WorkspaceText.WhyNoActions(workspace) ?? "Nothing was sent: it's no longer waiting for this answer.");
            }
            if (!draft.Answers(workspace.Execution!.ExecutionId, workspace.QuestionToAnswer))
            {
                return SteeringOutcome.Explain(QuestionChanged);
            }
            if (draft.Problem is string problem) return SteeringOutcome.Explain(problem);
            if (workspace.RequiresConfirmation(WorkspaceAction.Answer))
            {
                Arm(WorkspaceAction.Answer, null, null);
                armedDraft = draft;
                armedAnswers = draft.AnswersNow;
                return SteeringOutcome.Of(SteeringStep.Confirm);
            }
            return SteeringOutcome.Send(commands.AnswerQuestion(draft.ExecutionId, draft.QuestionId, draft.Build()));
        }

        /// <summary>
        /// The person confirmed the armed action. An approval whose whole request has not been shown
        /// sends nothing and stays armed, so the rest can still be read.
        /// </summary>
        public SteeringOutcome Confirm(WorkspacePresentation workspace)
        {
            if (Armed == null) return SteeringOutcome.Nothing;
            var lapse = Lapse(workspace);
            if (lapse == null && !CanConfirm)
            {
                return SteeringOutcome.Explain(Armed == WorkspaceAction.Instruct ? WorkspaceText.InstructionNotRead : WorkspaceText.RequestNotRead);
            }
            var action = Armed.Value;
            var approvalId = ArmedApprovalId;
            var instruction = Instruction;
            var draft = armedDraft;
            var answers = armedAnswers;
            Cancel();
            if (lapse != null) return SteeringOutcome.Explain(lapse);
            if (action == WorkspaceAction.Answer)
            {
                if (draft == null || !draft.Answers(workspace.Execution!.ExecutionId, workspace.QuestionToAnswer))
                {
                    return SteeringOutcome.Explain(QuestionChanged);
                }
                // What Yes was asked for, never answers changed since, as by words heard or typed after or a
                // choice cleared, judged while the draft is still the question's, before anything else.
                if (!QuestionDraft.SameAnswers(draft.AnswersNow, answers)) return SteeringOutcome.Explain(WorkspaceText.AnswerChanged);
                if (draft.Problem != null) return SteeringOutcome.Explain(QuestionChanged);
                return SteeringOutcome.Send(commands.AnswerQuestion(draft.ExecutionId, draft.QuestionId, draft.Build()));
            }
            return SteeringOutcome.Send(Build(action, workspace.Execution!.ExecutionId, approvalId, instruction));
        }

        /// <summary>The keyboard closed with this text; an empty text sends nothing.</summary>
        public SteeringOutcome Typed(string? text, WorkspacePresentation workspace)
        {
            Typing = false;
            var instruction = text?.Trim() ?? "";
            if (instruction.Length == 0) return SteeringOutcome.Explain("Nothing was sent: nothing was typed.");
            if (!workspace.Actions.Contains(WorkspaceAction.Instruct))
            {
                return SteeringOutcome.Explain(WorkspaceText.WhyNoActions(workspace) ?? NoInstructions);
            }
            if (workspace.RequiresConfirmation(WorkspaceAction.Instruct))
            {
                Arm(WorkspaceAction.Instruct, null, instruction);
                return SteeringOutcome.Of(SteeringStep.Confirm);
            }
            return SteeringOutcome.Send(commands.SendInstruction(workspace.Execution!.ExecutionId, instruction));
        }

        /// <summary>
        /// The Mac heard this instruction in a held clip (ADR 0021). Unlike a typed one, it is always
        /// held for a deliberate confirmation that shows it as heard, whose Yes waits until all of it has
        /// been shown, so a mishearing is never sent unread; an empty transcript sends nothing.
        /// </summary>
        public SteeringOutcome Spoken(string? text, WorkspacePresentation workspace)
        {
            if (Typing) return SteeringOutcome.Nothing;
            Cancel();
            var instruction = text?.Trim() ?? "";
            if (instruction.Length == 0) return SteeringOutcome.Explain("I didn't catch anything, so nothing was sent.");
            if (!workspace.Actions.Contains(WorkspaceAction.Instruct))
            {
                return SteeringOutcome.Explain(WorkspaceText.WhyNoActions(workspace) ?? NoInstructions);
            }
            Arm(WorkspaceAction.Instruct, null, instruction);
            Heard = true;
            return SteeringOutcome.Of(SteeringStep.Confirm);
        }

        /// <summary>The keyboard closed without text.</summary>
        public void StopTyping() => Typing = false;

        /// <summary>
        /// Focus went to another window. A confirmation half done is dropped, so the person confirms
        /// afresh once back, from the start of the request; nothing is sent, and the runtime's request
        /// itself stays pending. Typing an instruction is the app's own keyboard and carries on.
        /// </summary>
        public SteeringOutcome FocusLeft()
        {
            if (Armed == null) return SteeringOutcome.Nothing;
            Cancel();
            return SteeringOutcome.Explain(WorkspaceText.ConfirmAfresh);
        }

        /// <summary>
        /// The person's answer is about to change, as words heard or typed arrive: an answer armed to send is
        /// cancelled, so Send answer's checks apply to the answer as it will be. What to say, or null.
        /// </summary>
        public string? AnswerChanging()
        {
            if (Armed != WorkspaceAction.Answer) return null;
            Cancel();
            return WorkspaceText.AnswerChanged;
        }

        /// <summary>
        /// The answers armed to send no longer stand as Send answer asked its Yes for them, changed by
        /// whatever reached the draft since, a choice or a page turned that cleared one: the Yes is
        /// cancelled at the change rather than refused when pressed, so it never stands over answers it
        /// would not send. What to say, or null while they stand.
        /// </summary>
        public string? AnswersMoved()
        {
            if (Armed != WorkspaceAction.Answer || armedDraft == null || QuestionDraft.SameAnswers(armedDraft.AnswersNow, armedAnswers)) return null;
            Cancel();
            return WorkspaceText.AnswerChanged;
        }

        /// <summary>
        /// The text size changed, so the person's answers are read again at the new size: an answer armed to
        /// send is cancelled, its Yes having been for what was read at the other. What to say, or null.
        /// </summary>
        public string? TextSizeChanged()
        {
            if (Armed != WorkspaceAction.Answer) return null;
            Cancel();
            return WorkspaceText.TextSizeChanged;
        }

        /// <summary>Drops a pending confirmation or instruction.</summary>
        public void Cancel()
        {
            armedDraft = null;
            armedAnswers = null;
            Armed = null;
            ArmedApprovalId = null;
            ArmedRequest = null;
            Instruction = null;
            Heard = false;
            WholeRequestShown = false;
            shownPart = 0;
        }

        /// <summary>
        /// The workspace shows <paramref name="part"/> of the <paramref name="parts"/> the request of
        /// the armed approval or denial takes, or the armed instruction's words, which it steps through
        /// in order: once the last has shown, the whole has. Turning to another part is deliberate, so
        /// the confirmation's time starts again with it; showing the same part again changes nothing.
        /// </summary>
        public void RequestShown(int part, int parts)
        {
            if (Armed == null || !Reads(Armed.Value) || part < 1) return;
            if (part != shownPart) armedAt = now();
            shownPart = part;
            if (part >= parts) WholeRequestShown = true;
        }

        /// <summary>
        /// The request the armed approval or denial answers, or the armed instruction's words, was
        /// measured again and lays out differently, as at another text size: it is read again from its
        /// first part, and Yes waits for its last once more. The confirmation's time starts again with it.
        /// </summary>
        public void ReadAgain()
        {
            if (Armed == null || !Reads(Armed.Value)) return;
            WholeRequestShown = false;
            shownPart = 0;
            armedAt = now();
        }

        /// <summary>
        /// The whole request the armed approval or denial answers, as the workspace shows it below
        /// the question, or null while neither is armed.
        /// </summary>
        public string? Request(WorkspacePresentation workspace)
        {
            if (Armed == null || !IsAnswer(Armed.Value)) return null;
            return WorkspaceText.Request(workspace.Execution?.PendingApprovals.FirstOrDefault(pending => pending.ApprovalId == ArmedApprovalId));
        }

        /// <summary>
        /// What the armed confirmation shows whole, in parts where it is long, before its Yes: the request
        /// an approval or denial answers (<see cref="Request"/>), or the armed instruction's words, quoted;
        /// null while neither is armed.
        /// </summary>
        public string? ToRead(WorkspacePresentation workspace) => Armed == WorkspaceAction.Instruct ? ArmedRequest : Request(workspace);

        /// <summary>An instruction's words as its confirmation shows them whole: one line by the one rule, quoted.</summary>
        public static string Quoted(string instruction) => "“" + WorkspaceText.OneLine(instruction) + "”";

        /// <summary>
        /// Drops a confirmation that lapsed: too late, no longer offered, or asked about an approval
        /// that is no longer pending. Returns why it dropped one, or null when nothing lapsed.
        /// </summary>
        public string? Refresh(WorkspacePresentation workspace)
        {
            if (Armed == null) return null;
            var lapse = Lapse(workspace);
            if (lapse != null) Cancel();
            return lapse;
        }

        /// <summary>
        /// The question the armed confirmation asks, or, for an approval whose whole request has not
        /// been shown yet, that it must be read first; null when nothing is armed.
        /// </summary>
        public string? Prompt(WorkspacePresentation workspace)
        {
            if (Armed == null) return null;
            // An instruction's words stand in its own question; a file shows them above it instead (FileScreens.Asks).
            if (!CanConfirm && Armed == WorkspaceAction.Approve) return WorkspaceText.ReadRequestFirst;
            return Asked();
        }

        /// <summary>
        /// Every question the armed confirmation may ask before it is answered: to read the request
        /// first, then its own. A request measured beside the taller keeps its parts where they are
        /// when the one shown changes, as when its last part is read; none while nothing is armed.
        /// </summary>
        public IReadOnlyList<string> Prompts() =>
            Armed == null ? Array.Empty<string>() : new[] { WorkspaceText.ReadRequestFirst, Asked() };

        private string Asked() => Heard ? VoiceText.SendHeard(Instruction ?? "") : WorkspaceText.ConfirmationPrompt(Armed!.Value, Instruction);

        private void Arm(WorkspaceAction action, string? approvalId, string? instruction)
        {
            Armings++;
            Armed = action;
            ArmedApprovalId = approvalId;
            Instruction = instruction;
            ArmedRequest = action == WorkspaceAction.Instruct && instruction != null ? Quoted(instruction) : null;
            Heard = false;
            WholeRequestShown = false;
            shownPart = 0;
            armedAt = now();
        }

        /// <summary>Why the armed confirmation no longer holds, or null while it does.</summary>
        private string? Lapse(WorkspacePresentation workspace)
        {
            if (now() - armedAt > window) return "Nothing was sent: you didn't confirm in time. Press it again.";
            if (!workspace.Actions.Contains(Armed!.Value)) return "Nothing was sent: things changed before you confirmed. Check it, then try again.";
            if (IsAnswer(Armed.Value)
                && workspace.Execution?.PendingApprovals.Any(pending => pending.ApprovalId == ArmedApprovalId) != true)
            {
                return "Nothing was sent: that request was already answered.";
            }
            // Answers armed for a question no longer shown would ask "Send these answers?" over none.
            if (Armed == WorkspaceAction.Answer
                && (armedDraft == null || workspace.Execution == null || !armedDraft.Answers(workspace.Execution.ExecutionId, workspace.QuestionToAnswer)))
            {
                return QuestionChanged;
            }
            if (IsAnswer(Armed.Value) && ArmedRequest != null && Request(workspace) != ArmedRequest)
            {
                return "Nothing was sent: the request changed. Read it again.";
            }
            return null;
        }

        private CommandEnvelope Build(WorkspaceAction action, string executionId, string? approvalId, string? instruction) => action switch
        {
            WorkspaceAction.Approve => commands.RespondToApproval(executionId, approvalId!, ApprovalDecision.Approve),
            WorkspaceAction.Deny => commands.RespondToApproval(executionId, approvalId!, ApprovalDecision.Deny),
            WorkspaceAction.Interrupt => commands.Interrupt(executionId),
            WorkspaceAction.Instruct => commands.SendInstruction(executionId, instruction!),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unhandled action."),
        };

        private static bool IsAnswer(WorkspaceAction action) => action == WorkspaceAction.Approve || action == WorkspaceAction.Deny;

        /// <summary>The action's confirmation shows something whole, in parts where it is long: a request, or an instruction's words.</summary>
        private static bool Reads(WorkspaceAction action) => IsAnswer(action) || action == WorkspaceAction.Instruct;
    }
}

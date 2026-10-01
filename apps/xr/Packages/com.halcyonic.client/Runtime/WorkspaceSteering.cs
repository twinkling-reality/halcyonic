#nullable enable
using System;
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
    /// approves what they could not read. A confirmation lapses when it is not given in time, or when
    /// the state it was asked about changes, so an old question is never answered by accident. One
    /// instance serves one open workspace; not thread safe.
    /// </summary>
    public sealed class WorkspaceSteering
    {
        public static readonly TimeSpan DefaultConfirmationWindow = TimeSpan.FromSeconds(15);

        private readonly CommandFactory commands;
        private readonly Func<DateTimeOffset> now;
        private readonly TimeSpan window;
        private DateTimeOffset armedAt;
        private QuestionDraft? armedDraft;
        private int shownPart;

        public WorkspaceSteering(CommandFactory commands, Func<DateTimeOffset>? now = null, TimeSpan? confirmationWindow = null)
        {
            this.commands = commands;
            this.now = now ?? (() => DateTimeOffset.UtcNow);
            window = confirmationWindow ?? DefaultConfirmationWindow;
        }

        /// <summary>The action waiting for a deliberate confirmation, if any.</summary>
        public WorkspaceAction? Armed { get; private set; }

        /// <summary>The approval an armed approve or deny answers.</summary>
        public string? ArmedApprovalId { get; private set; }

        /// <summary>The instruction an armed instruct sends.</summary>
        public string? Instruction { get; private set; }

        /// <summary>The keyboard is open for an instruction.</summary>
        public bool Typing { get; private set; }

        /// <summary>The armed instruction was spoken and heard on the Mac, not typed.</summary>
        public bool Heard { get; private set; }

        /// <summary>
        /// Every part of the request the armed approval or denial answers has been shown
        /// (<see cref="RequestShown"/>).
        /// </summary>
        public bool WholeRequestShown { get; private set; }

        /// <summary>
        /// The armed action can be confirmed now: an approval only once its whole request has been
        /// shown; anything else at once.
        /// </summary>
        public bool CanConfirm => Armed != null && (Armed != WorkspaceAction.Approve || WholeRequestShown);

        /// <summary>The person pressed an action.</summary>
        public SteeringOutcome Press(WorkspaceAction action, WorkspacePresentation workspace)
        {
            if (Typing) return SteeringOutcome.Nothing;
            Cancel();
            // An answer goes only with the answers chosen, through SendAnswer.
            if (action == WorkspaceAction.Answer) return SteeringOutcome.Explain("Choose your answers, then press Send answer.");
            if (!workspace.Actions.Contains(action))
            {
                return SteeringOutcome.Explain(WorkspaceText.WhyNoActions(workspace) ?? "That is no longer possible, so nothing was sent.");
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
        public SteeringOutcome SendAnswer(QuestionDraft draft, WorkspacePresentation workspace)
        {
            if (Typing) return SteeringOutcome.Nothing;
            Cancel();
            if (!workspace.Actions.Contains(WorkspaceAction.Answer))
            {
                return SteeringOutcome.Explain(WorkspaceText.WhyNoActions(workspace) ?? "It no longer waits for this answer, so nothing was sent.");
            }
            if (!draft.Answers(workspace.Execution!.ExecutionId, workspace.QuestionToAnswer))
            {
                return SteeringOutcome.Explain("The question changed, so nothing was sent. Check it again.");
            }
            if (draft.Problem is string problem) return SteeringOutcome.Explain(problem);
            if (workspace.RequiresConfirmation(WorkspaceAction.Answer))
            {
                Arm(WorkspaceAction.Answer, null, null);
                armedDraft = draft;
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
            if (lapse == null && !CanConfirm) return SteeringOutcome.Explain(WorkspaceText.RequestNotRead);
            var action = Armed.Value;
            var approvalId = ArmedApprovalId;
            var instruction = Instruction;
            var draft = armedDraft;
            Cancel();
            if (lapse != null) return SteeringOutcome.Explain(lapse);
            if (action == WorkspaceAction.Answer)
            {
                if (draft == null || draft.Problem != null || !draft.Answers(workspace.Execution!.ExecutionId, workspace.QuestionToAnswer))
                {
                    return SteeringOutcome.Explain("The question changed, so nothing was sent. Check it again.");
                }
                return SteeringOutcome.Send(commands.AnswerQuestion(draft.ExecutionId, draft.QuestionId, draft.Build()));
            }
            return SteeringOutcome.Send(Build(action, workspace.Execution!.ExecutionId, approvalId, instruction));
        }

        /// <summary>The keyboard closed with this text; an empty text sends nothing.</summary>
        public SteeringOutcome Typed(string? text, WorkspacePresentation workspace)
        {
            Typing = false;
            var instruction = text?.Trim() ?? "";
            if (instruction.Length == 0) return SteeringOutcome.Explain("Nothing was typed, so nothing was sent.");
            if (!workspace.Actions.Contains(WorkspaceAction.Instruct))
            {
                return SteeringOutcome.Explain(WorkspaceText.WhyNoActions(workspace) ?? "It no longer takes instructions, so nothing was sent.");
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
        /// held for a deliberate confirmation that shows it as heard, so a mishearing is never sent
        /// unread; an empty transcript sends nothing.
        /// </summary>
        public SteeringOutcome Spoken(string? text, WorkspacePresentation workspace)
        {
            if (Typing) return SteeringOutcome.Nothing;
            Cancel();
            var instruction = text?.Trim() ?? "";
            if (instruction.Length == 0) return SteeringOutcome.Explain("I didn't catch anything, so nothing was sent.");
            if (!workspace.Actions.Contains(WorkspaceAction.Instruct))
            {
                return SteeringOutcome.Explain(WorkspaceText.WhyNoActions(workspace) ?? "It no longer takes instructions, so nothing was sent.");
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

        /// <summary>Drops a pending confirmation or instruction.</summary>
        public void Cancel()
        {
            armedDraft = null;
            Armed = null;
            ArmedApprovalId = null;
            Instruction = null;
            Heard = false;
            WholeRequestShown = false;
            shownPart = 0;
        }

        /// <summary>
        /// The workspace shows <paramref name="part"/> of the <paramref name="parts"/> the request of
        /// the armed approval or denial takes, which it steps through in order: once the last has
        /// shown, the whole has. Turning to another part is deliberate, so the confirmation's time
        /// starts again with it; showing the same part again changes nothing.
        /// </summary>
        public void RequestShown(int part, int parts)
        {
            if (Armed == null || !IsAnswer(Armed.Value) || part < 1) return;
            if (part != shownPart) armedAt = now();
            shownPart = part;
            if (part >= parts) WholeRequestShown = true;
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
            if (!CanConfirm) return WorkspaceText.ReadRequestFirst;
            return Heard ? VoiceText.SendHeard(Instruction ?? "") : WorkspaceText.ConfirmationPrompt(Armed.Value, Instruction);
        }

        private void Arm(WorkspaceAction action, string? approvalId, string? instruction)
        {
            Armed = action;
            ArmedApprovalId = approvalId;
            Instruction = instruction;
            Heard = false;
            WholeRequestShown = false;
            shownPart = 0;
            armedAt = now();
        }

        /// <summary>Why the armed confirmation no longer holds, or null while it does.</summary>
        private string? Lapse(WorkspacePresentation workspace)
        {
            if (now() - armedAt > window) return "The confirmation timed out, so nothing was sent.";
            if (!workspace.Actions.Contains(Armed!.Value)) return "The state changed before the confirmation, so nothing was sent.";
            if (IsAnswer(Armed.Value)
                && workspace.Execution?.PendingApprovals.Any(pending => pending.ApprovalId == ArmedApprovalId) != true)
            {
                return "That approval was already answered, so nothing was sent.";
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
    }
}

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
    /// names exactly what will be sent. A confirmation lapses when it is not given in time, or when
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

        /// <summary>The person pressed an action.</summary>
        public SteeringOutcome Press(WorkspaceAction action, WorkspacePresentation workspace)
        {
            if (Typing) return SteeringOutcome.Nothing;
            Cancel();
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

        /// <summary>The person confirmed the armed action.</summary>
        public SteeringOutcome Confirm(WorkspacePresentation workspace)
        {
            if (Armed == null) return SteeringOutcome.Nothing;
            var lapse = Lapse(workspace);
            var action = Armed.Value;
            var approvalId = ArmedApprovalId;
            var instruction = Instruction;
            Cancel();
            if (lapse != null) return SteeringOutcome.Explain(lapse);
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

        /// <summary>The keyboard closed without text.</summary>
        public void StopTyping() => Typing = false;

        /// <summary>Drops a pending confirmation or instruction.</summary>
        public void Cancel()
        {
            Armed = null;
            ArmedApprovalId = null;
            Instruction = null;
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

        /// <summary>The question the armed confirmation asks, or null when nothing is armed.</summary>
        public string? Prompt(WorkspacePresentation workspace)
        {
            if (Armed == null) return null;
            var approval = workspace.Execution?.PendingApprovals.FirstOrDefault(pending => pending.ApprovalId == ArmedApprovalId);
            return WorkspaceText.ConfirmationPrompt(Armed.Value, approval, Instruction);
        }

        private void Arm(WorkspaceAction action, string? approvalId, string? instruction)
        {
            Armed = action;
            ArmedApprovalId = approvalId;
            Instruction = instruction;
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

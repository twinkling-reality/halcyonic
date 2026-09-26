#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    public enum WorkspaceAction
    {
        Approve,
        Deny,
        Interrupt,
        Instruct,
    }

    /// <summary>How a command issued against the execution is going, in words.</summary>
    public sealed class CommandFeedback
    {
        public CommandFeedback(string commandId, CommandType commandType, CommandStatus status, string text)
        {
            CommandId = commandId;
            CommandType = commandType;
            Status = status;
            Text = text;
        }

        public string CommandId { get; }

        public CommandType CommandType { get; }

        public CommandStatus Status { get; }

        public string Text { get; }
    }

    /// <summary>
    /// The expanded form of a workstream: the same work as its character, with what is needed to
    /// understand and steer it (docs/internal/product/PRODUCT.md, "The mechanic to prove").
    /// </summary>
    public sealed class WorkspacePresentation
    {
        private readonly IReadOnlyCollection<WorkspaceAction> confirm;

        public WorkspacePresentation(
            CharacterPresentation character,
            string? objective,
            ExecutionView? execution,
            RuntimeDescriptor? runtime,
            IReadOnlyCollection<WorkspaceAction> actions,
            IReadOnlyCollection<WorkspaceAction> confirm,
            IReadOnlyList<CommandFeedback> commands,
            IReadOnlyList<ActivityEntry> activity)
        {
            this.confirm = confirm;
            Character = character;
            Objective = objective;
            Execution = execution;
            Runtime = runtime;
            Actions = actions;
            Commands = commands;
            Activity = activity;
        }

        public CharacterPresentation Character { get; }

        public string? Objective { get; }

        /// <summary>The execution the workstream reports, if it has one.</summary>
        public ExecutionView? Execution { get; }

        /// <summary>The runtime it runs on, or null when the control plane no longer offers it (for example a replayed trace).</summary>
        public RuntimeDescriptor? Runtime { get; }

        /// <summary>Only actions the runtime supports in the current state; nothing is offered while the session is not live.</summary>
        public IReadOnlyCollection<WorkspaceAction> Actions { get; }

        /// <summary>Recent commands against the execution, newest first.</summary>
        public IReadOnlyList<CommandFeedback> Commands { get; }

        public IReadOnlyList<ActivityEntry> Activity { get; }

        /// <summary>The action needs an explicit, deliberate gesture, per the control plane's command policy.</summary>
        public bool RequiresConfirmation(WorkspaceAction action) => confirm.Contains(action);
    }

    public static class WorkspacePresenter
    {
        private const int RecentCommands = 5;

        public static WorkspacePresentation Present(WorkstreamView workstream, ClientProjection state, ActivityLog activity, bool live)
        {
            var execution = state.CurrentExecution(workstream);
            var runtime = execution == null ? null : state.RuntimeOf(execution);
            var actions = live && execution != null && runtime != null
                ? ActionsFor(execution, runtime)
                : new WorkspaceAction[0];
            var commands = execution == null
                ? new List<CommandFeedback>()
                : state.Commands.Values
                    .Where(command => command.ExecutionId == execution.ExecutionId)
                    .OrderByDescending(command => command.IssuedAt, System.StringComparer.Ordinal)
                    .Take(RecentCommands)
                    .Select(Feedback)
                    .ToList();
            var confirm = actions.Where(action => state.RequiresConfirmation(CommandTypeOf(action))).ToList();
            return new WorkspacePresentation(
                CharacterPresenter.Present(workstream, state, live),
                workstream.Objective,
                execution,
                runtime,
                actions,
                confirm,
                commands,
                execution == null ? new ActivityEntry[0] : activity.For(execution.ExecutionId));
        }

        /// <summary>
        /// The actions the control plane would admit now, from the runtime's declared capabilities and
        /// the execution's status (packages/domain/src/admission.ts). The control plane still decides;
        /// a refusal comes back as command feedback.
        /// </summary>
        public static IReadOnlyCollection<WorkspaceAction> ActionsFor(ExecutionView execution, RuntimeDescriptor runtime)
        {
            var capabilities = runtime.Capabilities;
            var status = execution.Status;
            var turnActive = status == ExecutionStatus.Running
                || status == ExecutionStatus.Verifying
                || status == ExecutionStatus.WaitingForHuman;
            var atRest = status == ExecutionStatus.Completed
                || status == ExecutionStatus.Failed
                || status == ExecutionStatus.Interrupted;
            var started = execution.StartedAt != null || execution.TurnCount > 0;

            var actions = new List<WorkspaceAction>();
            if (capabilities.RespondToApproval && status == ExecutionStatus.WaitingForHuman && execution.PendingApprovals.Count > 0)
            {
                actions.Add(WorkspaceAction.Approve);
                actions.Add(WorkspaceAction.Deny);
            }
            if (capabilities.Interrupt && turnActive) actions.Add(WorkspaceAction.Interrupt);
            if (started && ((atRest && capabilities.InstructAtRest) || (turnActive && capabilities.InstructWhileRunning)))
            {
                actions.Add(WorkspaceAction.Instruct);
            }
            return actions;
        }

        public static CommandType CommandTypeOf(WorkspaceAction action) => action switch
        {
            WorkspaceAction.Approve => CommandType.ExecutionRespondToApproval,
            WorkspaceAction.Deny => CommandType.ExecutionRespondToApproval,
            WorkspaceAction.Interrupt => CommandType.ExecutionInterrupt,
            _ => CommandType.ExecutionSendInstruction,
        };

        public static CommandFeedback Feedback(CommandView command)
        {
            string text;
            switch (command.Status)
            {
                case CommandStatus.Accepted:
                    text = Pending(command.CommandType);
                    break;
                case CommandStatus.Completed:
                    text = Done(command.CommandType);
                    break;
                case CommandStatus.Rejected:
                    text = "Refused: " + (command.Rejection?.Message ?? "no reason given");
                    break;
                default:
                    var failure = command.Failure;
                    text = "Failed: " + (failure?.Message ?? "no reason given")
                        + (failure?.Effect == FailureEffect.Unknown ? " It may have taken effect anyway." : "");
                    break;
            }
            return new CommandFeedback(command.CommandId, command.CommandType, command.Status, text);
        }

        private static string Pending(CommandType type) => type switch
        {
            CommandType.ExecutionRespondToApproval => "Answering the approval…",
            CommandType.ExecutionInterrupt => "Stopping the turn…",
            CommandType.ExecutionSendInstruction => "Sending the instruction…",
            CommandType.ExecutionStart => "Starting…",
            _ => "Working on it…",
        };

        private static string Done(CommandType type) => type switch
        {
            CommandType.ExecutionRespondToApproval => "Approval answered",
            CommandType.ExecutionInterrupt => "Turn stopped",
            CommandType.ExecutionSendInstruction => "Instruction delivered",
            CommandType.ExecutionStart => "Started",
            _ => "Done",
        };
    }
}

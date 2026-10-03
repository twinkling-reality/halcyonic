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

        /// <summary>Send the answers chosen to the question shown (ADR 0022).</summary>
        Answer,
    }

    /// <summary>How a command issued against the execution is going, in words.</summary>
    public sealed class CommandFeedback
    {
        public CommandFeedback(string commandId, CommandType commandType, CommandStatus? status, string text)
        {
            CommandId = commandId;
            CommandType = commandType;
            Status = status;
            Text = text;
        }

        public string CommandId { get; }

        public CommandType CommandType { get; }

        /// <summary>
        /// The control plane's status of the command, or null while only this client knows about it:
        /// sending, not sent, or with an unknown outcome (<see cref="CommandSubmissions"/>).
        /// </summary>
        public CommandStatus? Status { get; }

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

        /// <summary>
        /// An answer this client sent to the question shown may still take effect, so Send answer is
        /// not offered meanwhile: the workspace says it was sent, rather than offering anything else in its place.
        /// </summary>
        public bool AnswerInFlight { get; internal set; }

        /// <summary>
        /// A decision this client sent on the request approving or denying answers may still take
        /// effect, so neither is offered meanwhile: the file says it was sent instead.
        /// </summary>
        public bool ApprovalInFlight { get; internal set; }

        public IReadOnlyList<ActivityEntry> Activity { get; }

        /// <summary>The approval that approving or denying answers: the oldest one pending.</summary>
        public ApprovalView? ApprovalToAnswer =>
            Execution?.PendingApprovals.OrderBy(approval => approval.RequestedAt, System.StringComparer.Ordinal).FirstOrDefault();

        /// <summary>
        /// The question the person answers here: the first the execution shows, which the control
        /// plane orders answerable first, then oldest first; at most three are shown, and more may
        /// follow as those are answered.
        /// </summary>
        public QuestionView? QuestionToAnswer => Execution?.PendingQuestions.FirstOrDefault();

        /// <summary>The action needs an explicit, deliberate gesture, per the control plane's command policy.</summary>
        public bool RequiresConfirmation(WorkspaceAction action) => confirm.Contains(action);
    }

    public static class WorkspacePresenter
    {
        private const int RecentCommands = 5;

        /// <param name="submissions">
        /// This client's own commands, so that one still sending, not sent or with an unknown outcome
        /// is described too; without it, only the control plane's records are.
        /// </param>
        public static WorkspacePresentation Present(
            WorkstreamView workstream,
            ClientProjection state,
            ActivityLog activity,
            bool live,
            CommandSubmissions? submissions = null)
        {
            var execution = state.CurrentExecution(workstream);
            var runtime = execution == null ? null : state.RuntimeOf(execution);
            IReadOnlyCollection<WorkspaceAction> actions = live && execution != null && runtime != null
                ? ActionsFor(execution, runtime)
                : new WorkspaceAction[0];
            // An answer this client sent that may still take effect is not raced by another.
            var asked = execution?.PendingQuestions.FirstOrDefault();
            var answerInFlight = false;
            if (asked != null && submissions != null && actions.Contains(WorkspaceAction.Answer)
                && submissions.AnswerPending(execution!.ExecutionId, asked.QuestionId, state))
            {
                actions = actions.Where(action => action != WorkspaceAction.Answer).ToList();
                answerInFlight = true;
            }
            // So is a decision on the request approving or denying answers.
            var approval = execution?.PendingApprovals.OrderBy(pending => pending.RequestedAt, System.StringComparer.Ordinal).FirstOrDefault();
            var approvalInFlight = false;
            if (approval != null && submissions != null && (actions.Contains(WorkspaceAction.Approve) || actions.Contains(WorkspaceAction.Deny))
                && submissions.ApprovalPending(execution!.ExecutionId, approval.ApprovalId, state))
            {
                actions = actions.Where(action => action != WorkspaceAction.Approve && action != WorkspaceAction.Deny).ToList();
                approvalInFlight = true;
            }
            IReadOnlyList<CommandFeedback> commands;
            if (execution == null)
            {
                commands = new List<CommandFeedback>();
            }
            else if (submissions != null)
            {
                commands = submissions.FeedbackFor(execution.ExecutionId, state, RecentCommands);
            }
            else
            {
                commands = state.Commands.Values
                    .Where(command => command.ExecutionId == execution.ExecutionId)
                    .OrderByDescending(command => command.IssuedAt, System.StringComparer.Ordinal)
                    .Take(RecentCommands)
                    .Select(Feedback)
                    .ToList();
            }
            var confirm = actions.Where(action => AlwaysConfirmed(action) || state.RequiresConfirmation(CommandTypeOf(action))).ToList();
            return new WorkspacePresentation(
                CharacterPresenter.Present(workstream, state, live),
                workstream.Objective,
                execution,
                runtime,
                actions,
                confirm,
                commands,
                execution == null ? new ActivityEntry[0] : activity.For(execution.ExecutionId))
            {
                AnswerInFlight = answerInFlight,
                ApprovalInFlight = approvalInFlight,
            };
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
            if (capabilities.AnswerQuestion && status == ExecutionStatus.WaitingForHuman
                && execution.PendingQuestions.FirstOrDefault() is QuestionView asked && WorkspaceText.Answerable(asked))
            {
                actions.Add(WorkspaceAction.Answer);
            }
            if (capabilities.Interrupt && turnActive) actions.Add(WorkspaceAction.Interrupt);
            if (started && ((atRest && capabilities.InstructAtRest) || (turnActive && capabilities.InstructWhileRunning)))
            {
                actions.Add(WorkspaceAction.Instruct);
            }
            return actions;
        }

        /// <summary>
        /// Approving, denying and stopping always wait for a deliberate Yes, whatever the control
        /// plane's policy says: each acts on the work at once, and an approval's Yes waits until its
        /// whole request has been read, which a policy of low consequence would skip.
        /// </summary>
        public static bool AlwaysConfirmed(WorkspaceAction action) =>
            action == WorkspaceAction.Approve || action == WorkspaceAction.Deny || action == WorkspaceAction.Interrupt;

        public static CommandType CommandTypeOf(WorkspaceAction action) => action switch
        {
            WorkspaceAction.Approve => CommandType.ExecutionRespondToApproval,
            WorkspaceAction.Deny => CommandType.ExecutionRespondToApproval,
            WorkspaceAction.Interrupt => CommandType.ExecutionInterrupt,
            WorkspaceAction.Answer => CommandType.ExecutionAnswerQuestion,
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
                    // The recorded demonstration says in its own words that nothing reached an agent
                    // and how the recording continues; "Refused" would contradict what plays next.
                    text = command.Rejection?.Code == RejectionCode.Demonstration
                        ? command.Rejection.Message
                        : command.Rejection?.Code == RejectionCode.QuestionNotFound
                        ? "Couldn't send: it's no longer waiting for this answer. See what it's doing now."
                        : "Couldn't do that: " + (command.Rejection?.Message ?? "no reason given");
                    break;
                default:
                    var failure = command.Failure;
                    // An answer the runtime never confirmed may or may not have reached the agent.
                    // An effect that cannot be ruled out is never put in words that say nothing happened.
                    text = command.CommandType == CommandType.ExecutionAnswerQuestion && failure?.Effect == FailureEffect.Unknown
                        ? WorkspaceText.AnswerNotConfirmed
                        : failure?.Effect == FailureEffect.Unknown
                        ? "Not sure it happened. Check its activity before you try again."
                        : "Couldn't do that: " + (failure?.Message ?? "no reason given");
                    break;
            }
            return new CommandFeedback(command.CommandId, command.CommandType, command.Status, text);
        }

        /// <summary>Accepted is not done: sent, and what it waits for.</summary>
        internal static string Pending(CommandType type) => type switch
        {
            CommandType.ExecutionInterrupt => "Sent. Waiting for it to stop…",
            CommandType.ExecutionStart => "Sent. Waiting for it to start…",
            _ => "Sent. Waiting for the agent…",
        };

        /// <summary>Only the control plane's completed record says so.</summary>
        private static string Done(CommandType type) => type switch
        {
            CommandType.ExecutionRespondToApproval => "Confirmed: it has your decision.",
            CommandType.ExecutionAnswerQuestion => "Confirmed: it has your answer.",
            CommandType.ExecutionInterrupt => "Confirmed: stopped.",
            CommandType.ExecutionSendInstruction => "Confirmed: it has your instruction.",
            CommandType.ExecutionStart => "Confirmed: started.",
            _ => "Confirmed.",
        };
    }
}

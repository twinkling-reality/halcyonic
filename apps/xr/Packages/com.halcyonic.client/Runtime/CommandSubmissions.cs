#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>Where a command this client sent stands, as far as this client knows.</summary>
    public enum SubmissionState
    {
        Sending,

        /// <summary>The control plane answered with a disposition; accepted is not done.</summary>
        Acknowledged,

        /// <summary>The command never left this client.</summary>
        NotSent,

        /// <summary>The command may or may not have reached the control plane.</summary>
        OutcomeUnknown,
    }

    /// <summary>
    /// The commands this client sent, described in words until the control plane's own record of
    /// each one (a <see cref="CommandView"/> in <see cref="ClientProjection.Commands"/>) takes over.
    /// From then on only that record speaks, so completion is shown only when the runtime confirmed
    /// it. Safe to use from several threads.
    /// </summary>
    public sealed class CommandSubmissions
    {
        private readonly object gate = new object();
        private readonly List<Submission> submissions = new List<Submission>();
        private readonly int capacity;
        private int version;

        public CommandSubmissions(int capacity = 20)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Must be at least 1.");
            this.capacity = capacity;
        }

        /// <summary>Changes whenever a submission changes, so a presentation knows to redraw.</summary>
        public int Version => Volatile.Read(ref version);

        /// <summary>The submission of a command, if this client sent it and still remembers it.</summary>
        public SubmissionState? StateOf(string commandId)
        {
            lock (gate)
            {
                return Find(commandId)?.State;
            }
        }

        public void Sending(CommandEnvelope command, string executionId)
        {
            lock (gate)
            {
                if (Find(command.CommandId) != null) return;
                submissions.Add(new Submission(command.CommandId, TypeOf(command), executionId, command.IssuedAt));
                if (submissions.Count > capacity) submissions.RemoveAt(0);
            }
            Interlocked.Increment(ref version);
        }

        public void Acknowledged(CommandAckMessage ack) =>
            Update(ack.CommandId, submission =>
            {
                submission.State = SubmissionState.Acknowledged;
                submission.Ack = ack;
            });

        public void NotSent(string commandId, string reason) =>
            Update(commandId, submission =>
            {
                submission.State = SubmissionState.NotSent;
                submission.Reason = reason;
            });

        public void OutcomeUnknown(string commandId, string reason) =>
            Update(commandId, submission =>
            {
                submission.State = SubmissionState.OutcomeUnknown;
                submission.Reason = reason;
            });

        /// <summary>Forgets everything, for example when the control plane serves another journal.</summary>
        public void Clear()
        {
            lock (gate)
            {
                submissions.Clear();
            }
            Interlocked.Increment(ref version);
        }

        /// <summary>
        /// Sends a command and records how the attempt ended. Expected failures become words instead
        /// of exceptions: not connected means not sent, anything after sending began means the outcome
        /// is unknown.
        /// </summary>
        public async Task SubmitAsync(Func<CommandEnvelope, Task<CommandAckMessage>> submit, CommandEnvelope command, string executionId)
        {
            // The same command is already on its way; a second copy would only race it.
            if (StateOf(command.CommandId) == SubmissionState.Sending) return;
            Sending(command, executionId);
            try
            {
                // No ConfigureAwait(false): in Unity the rest of this method runs on the main thread.
                Acknowledged(await submit(command));
            }
            catch (SessionUnavailableException)
            {
                NotSent(command.CommandId, "not connected to the control plane.");
            }
            catch (Exception error)
            {
                // CommandOutcomeUnknownException, or anything unexpected once sending may have begun.
                OutcomeUnknown(command.CommandId, error.Message);
            }
        }

        /// <summary>
        /// Feedback on the commands against an execution, newest first: the control plane's records,
        /// and this client's own submissions that no record covers yet. A submission whose record was
        /// seen once is never described from this client's view again, even after the record ages out.
        /// </summary>
        public IReadOnlyList<CommandFeedback> FeedbackFor(string executionId, ClientProjection state, int max)
        {
            var feedback = state.Commands.Values
                .Where(command => command.ExecutionId == executionId)
                .Select(command => (command.IssuedAt, WorkspacePresenter.Feedback(command)))
                .ToList();
            lock (gate)
            {
                foreach (var submission in submissions)
                {
                    if (submission.ExecutionId != executionId) continue;
                    if (state.Commands.ContainsKey(submission.CommandId)) submission.HandedOver = true;
                    if (submission.HandedOver) continue;
                    feedback.Add((submission.IssuedAt, submission.Describe()));
                }
            }
            return feedback
                .OrderByDescending(item => item.IssuedAt, StringComparer.Ordinal)
                .Take(max)
                .Select(item => item.Item2)
                .ToList();
        }

        private static CommandType TypeOf(CommandEnvelope command) => command switch
        {
            ProjectCreateCommand _ => CommandType.ProjectCreate,
            WorkstreamCreateCommand _ => CommandType.WorkstreamCreate,
            ExecutionStartCommand _ => CommandType.ExecutionStart,
            ExecutionSendInstructionCommand _ => CommandType.ExecutionSendInstruction,
            ExecutionRespondToApprovalCommand _ => CommandType.ExecutionRespondToApproval,
            ExecutionInterruptCommand _ => CommandType.ExecutionInterrupt,
            _ => throw new ArgumentOutOfRangeException(nameof(command), command.CommandType, "Unhandled command type."),
        };

        private Submission? Find(string commandId)
        {
            foreach (var submission in submissions)
            {
                if (submission.CommandId == commandId) return submission;
            }
            return null;
        }

        private void Update(string commandId, Action<Submission> change)
        {
            lock (gate)
            {
                var submission = Find(commandId);
                if (submission == null) return;
                change(submission);
            }
            Interlocked.Increment(ref version);
        }

        private sealed class Submission
        {
            public Submission(string commandId, CommandType commandType, string executionId, string issuedAt)
            {
                CommandId = commandId;
                CommandType = commandType;
                ExecutionId = executionId;
                IssuedAt = issuedAt;
            }

            public string CommandId { get; }

            public CommandType CommandType { get; }

            public string ExecutionId { get; }

            public string IssuedAt { get; }

            public SubmissionState State { get; set; } = SubmissionState.Sending;

            public CommandAckMessage? Ack { get; set; }

            public string? Reason { get; set; }

            /// <summary>The control plane's record of this command was seen, so it speaks from now on.</summary>
            public bool HandedOver { get; set; }

            public CommandFeedback Describe()
            {
                switch (State)
                {
                    case SubmissionState.Sending:
                        return Local("Sending to the control plane…");
                    case SubmissionState.NotSent:
                        return Local("Not sent: " + Reason);
                    case SubmissionState.OutcomeUnknown:
                        return Local("Outcome unknown: " + Reason + " If it arrived, it shows here after reconnecting.");
                }
                var ack = Ack!;
                if (ack.Command != null && ack.Disposition != CommandAckDisposition.Conflict)
                {
                    return WorkspacePresenter.Feedback(ack.Command);
                }
                return ack.Disposition switch
                {
                    CommandAckDisposition.Rejected => Local("Refused by the control plane."),
                    CommandAckDisposition.Conflict => Local("Refused: this command id was already used for another command."),
                    CommandAckDisposition.Duplicate => Local("Already received by the control plane."),
                    _ => Local(WorkspacePresenter.Pending(CommandType)),
                };
            }

            private CommandFeedback Local(string text) => new CommandFeedback(CommandId, CommandType, null, text);
        }
    }
}

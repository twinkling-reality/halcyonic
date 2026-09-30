#nullable enable
using System;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    public enum NewWorkSubmissionState
    {
        Waiting,
        OutcomeUnknown,
        NotSent,
        Rejected,
        Failed,
        Completed,
    }

    /// <summary>
    /// A command's projected record wins over a missing or lost acknowledgement. An unknown
    /// acknowledgement keeps its command id until a projected terminal record resolves it.
    /// </summary>
    public sealed class NewWorkSubmission
    {
        private CommandView? acknowledgement;
        private bool outcomeUnknown;
        private bool notSent;

        public NewWorkSubmission(CommandEnvelope command) => Command = command;

        public CommandEnvelope Command { get; }

        public CommandView? Record { get; private set; }

        public CommandView? EffectiveRecord => Record ?? acknowledgement;

        public bool HasExpectedResult => State == NewWorkSubmissionState.Completed && Command switch
        {
            ProjectCreateCommand => EffectiveRecord?.Result is ProjectCreatedResult,
            // Binding a project to a folder completes with no result.
            ProjectSetLocationCommand => EffectiveRecord?.Result == null,
            WorkstreamCreateCommand => EffectiveRecord?.Result is WorkstreamCreatedResult,
            ExecutionStartCommand => EffectiveRecord?.Result is ExecutionCreatedResult,
            _ => false,
        };

        public NewWorkSubmissionState State
        {
            get
            {
                var record = EffectiveRecord;
                if (record != null)
                {
                    return record.Status switch
                    {
                        CommandStatus.Completed => NewWorkSubmissionState.Completed,
                        CommandStatus.Rejected => NewWorkSubmissionState.Rejected,
                        CommandStatus.Failed => NewWorkSubmissionState.Failed,
                        _ => outcomeUnknown ? NewWorkSubmissionState.OutcomeUnknown : NewWorkSubmissionState.Waiting,
                    };
                }
                if (notSent) return NewWorkSubmissionState.NotSent;
                return outcomeUnknown ? NewWorkSubmissionState.OutcomeUnknown : NewWorkSubmissionState.Waiting;
            }
        }

        public void Observe(CommandView? record)
        {
            if (record != null && record.CommandId == Command.CommandId) Record = record;
        }

        public void Acknowledge(CommandAckMessage ack)
        {
            if (ack.CommandId != Command.CommandId) return;
            if (ack.Command == null || ack.Disposition == CommandAckDisposition.Conflict)
            {
                outcomeUnknown = true;
                return;
            }
            acknowledgement = ack.Command;
        }

        public void LostAcknowledgement(Exception error)
        {
            if (error is SessionUnavailableException) notSent = true;
            else outcomeUnknown = true;
        }
    }
}

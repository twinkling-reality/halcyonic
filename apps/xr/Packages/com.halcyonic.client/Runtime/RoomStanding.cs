#nullable enable
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// How a task at rest stands (B; FIELD's table, 2026-11-12): one function from its run's status and reason code to a card's
    /// status, shown only where the result has nothing else to say. A finished turn is never Good, since a finished turn is not
    /// a passed check; a code with no words of its own says it is unknown, never another case's words (the lead's rule for every
    /// default arm). A failure's message is the computer's own and stands under the status, whole, as theirs.
    /// </summary>
    public static class RoomStanding
    {
        public const string Finished = "Turn Finished";
        public const string FinishedLine = "Nothing checked yet";
        public const string Failed = "Turn Failed";
        public const string Stopped = "Turn Stopped";
        public const string StoppedLine = "Stopped on request";
        public const string NotReached = "Run Not Reached";
        public const string NotReachedLine = "Your computer restarted and may reach it again";
        public const string Lost = "Run Lost";
        public const string LostLine = "Your computer restarted and couldn't reach it again";
        public const string StartUnknown = "Start Unknown";
        public const string StartUnknownLine = "It may or may not have started";
        public const string Unknown = "Outcome Unknown";
        public const string UnknownLine = "Your computer can't see this run";

        /// <summary>The status of <paramref name="execution"/> at rest; null where it is not at rest or there is none.</summary>
        public static RoomCardStatus? Of(ExecutionView? execution) => execution?.Status switch
        {
            ExecutionStatus.Completed => new RoomCardStatus(Finished, FinishedLine, RoomTone.Quiet),
            ExecutionStatus.Failed => new RoomCardStatus(Failed, null, RoomTone.Bad),
            ExecutionStatus.Interrupted => new RoomCardStatus(Stopped, StoppedLine, RoomTone.Quiet),
            ExecutionStatus.Unknown => execution.StatusReason?.Code switch
            {
                "control_plane_restarted" => new RoomCardStatus(NotReached, NotReachedLine, RoomTone.Quiet, RoomKind.Waiting),
                "session_lost" => new RoomCardStatus(Lost, LostLine, RoomTone.Bad),
                "start_outcome_unknown" => new RoomCardStatus(StartUnknown, StartUnknownLine, RoomTone.Quiet),
                _ => new RoomCardStatus(Unknown, UnknownLine, RoomTone.Quiet),
            },
            _ => null,
        };

        /// <summary>
        /// The card for a task at rest whose result says nothing: the task's name, its status, and where the run failed with a
        /// message, the message whole as the card's description, theirs. Null where <see cref="Of"/> is.
        /// </summary>
        public static RoomCard? Card(ExecutionView? execution, LineWords task)
        {
            if (Of(execution) is not RoomCardStatus status) return null;
            var message = execution!.Status == ExecutionStatus.Failed && !string.IsNullOrWhiteSpace(execution.StatusReason?.Message)
                ? LineWords.Theirs(execution.StatusReason!.Message) : (LineWords?)null;
            return new RoomCard(task, message, RoomCardHead.Picture, status: status);
        }
    }
}

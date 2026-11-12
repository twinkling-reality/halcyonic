#nullable enable
using System;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>What one Ask is about (ADR 0030): a task and its current execution, a whole project, or all of the person's work.</summary>
    public sealed class AskScope
    {
        private AskScope(string? projectId, string? workstreamId, string? executionId, LineWords name, RoomCardHead head)
        {
            ProjectId = projectId;
            WorkstreamId = workstreamId;
            ExecutionId = executionId;
            Name = name;
            Head = head;
        }

        /// <summary>A task's Ask: its project, the task, and the task's current execution, and the task's name.</summary>
        public static AskScope Task(string projectId, string workstreamId, string executionId, string? taskName) =>
            new AskScope(projectId, workstreamId, executionId, LineWords.TheirName(taskName), RoomCardHead.None);

        /// <summary>A whole project's Ask: read-only, about every task in it.</summary>
        public static AskScope Project(string projectId, string? projectName) =>
            new AskScope(projectId, null, null, LineWords.TheirName(projectName), RoomCardHead.Project);

        /// <summary>An Ask about all of the person's work: read-only, about every project.</summary>
        public static AskScope AllWork() => new AskScope(null, null, null, LineWords.Ours(AskColumn.Words.AllWork), RoomCardHead.None);

        public string? ProjectId { get; }
        public string? WorkstreamId { get; }
        public string? ExecutionId { get; }

        /// <summary>The card's title: the thing asked about.</summary>
        public LineWords Name { get; }

        public RoomCardHead Head { get; }

        public bool IsTask => ExecutionId != null;
        public bool IsAllWork => ProjectId == null;
    }

    /// <summary>Where one Ask stands.</summary>
    public enum AskStage
    {
        /// <summary>The person writes or speaks the question; nothing is sent.</summary>
        Writing,

        /// <summary>Sent: the computer has not yet said it holds the request.</summary>
        Sent,

        /// <summary>The assistant reads and plans.</summary>
        Thinking,

        /// <summary>A task's Ask gave its agent the change and waits for its outcome.</summary>
        Waiting,

        /// <summary>The assistant asked the person something.</summary>
        NeedsYou,

        /// <summary>The assistant replied.</summary>
        Answered,

        /// <summary>No answer: not sent, refused, stopped at a limit, failed, or stopped by the person.</summary>
        NotAnswered,
    }

    /// <summary>
    /// One Ask from the headset (gap 7; ADR 0030; FIELD's A12), walked from writing the question to its answer, each state
    /// as a <see cref="RoomCard"/> in the card language (the addendum's A17): the thing asked about is the title, a state is
    /// a status, the person's own question is the description, and no fact is joined to another by a dot. The assistant's
    /// words never stand in the card (FIELD, 2026-11-11): its question and its reply are <see cref="Band"/>, for the band low
    /// in the view, outside words and reported, never the product's own. This holds no network: the bar sends <see cref="Submit"/>'s command and feeds back what the
    /// computer answers (<see cref="Offer"/>, <see cref="Acknowledge"/>, <see cref="ReadCommand"/>, <see cref="Read"/>).
    /// Nothing is sent by itself.
    /// </summary>
    public sealed class AskColumn
    {
        /// <summary>The column's words, in one place for FIELD to settle (a note of 2026-11-11 asked its yes).</summary>
        public static class Words
        {
            public const string AllWork = "All Work";
            public const string EmptyTask = "Ask what it changed, or whether it works.";
            public const string EmptyProject = "Ask what is going on in this project.";
            public const string EmptyAllWork = "Ask what is going on with your work.";
            public const string Sent = "Sent";
            public const string SentLine = "Waiting for the assistant";
            public const string Thinking = "Thinking";
            public const string ThinkingLine = "It reads what your computer saw";
            public const string Waiting = "Waiting";
            public const string WaitingLine = "The agent is doing what it asked";
            public const string NeedsYou = "Needs You";
            public const string NeedsYouLine = "It asks you something below";
            /// <summary>Waiting for the person with no question of the assistant's on the band (AUDIT MUST 1): it claims nothing the screen lacks.</summary>
            public const string WaitsLine = "It waits for you";
            public const string Answered = "Answered";
            public const string AnsweredLine = "The assistant's reply is below";
            /// <summary>Responded with no reply to show (AUDIT MUST 1).</summary>
            public const string NoReplyLine = "No reply came";
            public const string AnsweredBy = "Answered by ";
            public const string NotAnswered = "Not Answered";
            public const string NotSent = "Not Sent";
            public const string Stopped = "Stopped";
            public const string NoAssistant = "No assistant on your computer";
            public const string Refused = "Your computer refused it";
            /// <summary>A send whose outcome is not known: it may or may not have reached the computer (AUDIT, 2026-11-11; FIELD to settle).</summary>
            public const string MayNotHaveReached = "It may not have reached your computer";
            public const string AtLimit = "It stopped at a limit";
            public const string CouldNotGoOn = "The assistant could not go on";
            public const string YouStopped = "You stopped it";
        }

        private readonly CommandFactory commands;
        private string draft = "";
        private string? reply, notAnswered;

        /// <summary>The model that wrote the reply, from the reply alone; never the one offered (AUDIT MUST 2).</summary>
        private string? repliedBy;
        /// <summary>The hosted provider that wrote the reply, as it declares its name; null on this computer.</summary>
        private string? repliedOn;
        private bool unavailable, notSent, stopped;

        public AskColumn(CommandFactory commands, AskScope scope)
        {
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        }

        public AskScope Scope { get; }
        public AskStage Stage { get; private set; } = AskStage.Writing;

        /// <summary>The submit command's id once sent, to match its acknowledgement.</summary>
        public string? CommandId { get; private set; }

        /// <summary>The request the computer holds, once it said so.</summary>
        public string? RequestId { get; private set; }

        /// <summary>The words written or spoken so far.</summary>
        public string Draft => draft;

        public event Action? Changed;

        /// <summary>Whether the bar may offer Send now: words to send, an assistant, nothing sent yet.</summary>
        public bool CanSend => Stage == AskStage.Writing && draft.Trim().Length > 0 && !unavailable;

        /// <summary>Whether the computer said it has no assistant, as its latest read of it says.</summary>
        public bool Unavailable => unavailable;

        /// <summary>The words the person writes or speaks, while nothing is sent.</summary>
        public void Write(string? words)
        {
            if (Stage != AskStage.Writing) return;
            draft = words ?? "";
            Changed?.Invoke();
        }

        /// <summary>What the computer says of its assistant: that it has one, or that there is none. Its model is not who answered.</summary>
        public void Offer(WorkspaceAgentStatus? status)
        {
            switch (status)
            {
                case AvailableWorkspaceAgent:
                    unavailable = false;
                    break;
                case UnavailableWorkspaceAgent:
                    unavailable = true;
                    break;
                default:
                    return;
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// The submit command for the bar to send, for this Ask's binding (a task, a project alone, or nothing for all work),
        /// or null when nothing may be sent (<see cref="CanSend"/>). Once built, the Ask stands at Sent.
        /// </summary>
        public WorkspaceRequestSubmitCommand? Submit()
        {
            if (!CanSend) return null;
            var text = draft.Trim();
            var command = Scope.IsTask
                ? commands.Request(Scope.ProjectId!, Scope.WorkstreamId!, Scope.ExecutionId!, text)
                : Scope.IsAllWork ? commands.RequestAboutAllWork(text) : commands.RequestAboutProject(Scope.ProjectId!, text);
            CommandId = command.CommandId;
            sent = command;
            unknown = false;
            Stage = AskStage.Sent;
            Changed?.Invoke();
            return command;
        }

        /// <summary>The submit as it was built, kept so a send whose outcome is unknown goes again under its own id.</summary>
        private WorkspaceRequestSubmitCommand? sent;
        private bool unknown;

        /// <summary>
        /// Whether the send may or may not have reached the computer (AUDIT, 2026-11-11): its acknowledgement failed after
        /// it left, or never came. Unknown stays unknown: the Ask stays Sent, says so, and waits for the command to show.
        /// </summary>
        public bool OutcomeUnknown => Stage == AskStage.Sent && unknown && RequestId == null;

        /// <summary>A send whose outcome is not known: the Ask stays Sent and says it may not have reached the computer.</summary>
        public void Unknown()
        {
            if (Stage != AskStage.Sent || RequestId != null) return;
            unknown = true;
            Changed?.Invoke();
        }

        /// <summary>
        /// The same submit again, its id unchanged, for a send whose outcome is unknown: the computer answers a reused id
        /// with the original command, never a second request. Null where the outcome is known.
        /// </summary>
        public WorkspaceRequestSubmitCommand? SendAgain()
        {
            if (!OutcomeUnknown || sent == null) return null;
            unknown = false;
            Changed?.Invoke();
            return sent;
        }

        /// <summary>The computer's acknowledgement of the submit: the request it holds, or that it refused it.</summary>
        public void Acknowledge(CommandAckMessage? ack)
        {
            if (ack == null || CommandId == null || ack.CommandId != CommandId || Stage != AskStage.Sent) return;
            if (ack.Disposition == CommandAckDisposition.Rejected || ack.Disposition == CommandAckDisposition.Conflict)
            {
                NotAnsweredBy(Words.Refused, sent: false);
                return;
            }
            if (ack.Command != null) ReadCommand(ack.Command);
        }

        /// <summary>The submit command as the computer later records it: its result, or its refusal or failure.</summary>
        public void ReadCommand(CommandView? command)
        {
            if (command == null || CommandId == null || command.CommandId != CommandId || Stage != AskStage.Sent) return;
            if (command.Result is WorkspaceRequestCreatedResult created)
            {
                RequestId = created.RequestId;
                Stage = AskStage.Thinking;
                Changed?.Invoke();
            }
            else if (command.Status == CommandStatus.Rejected || command.Status == CommandStatus.Failed)
                NotAnsweredBy(Words.Refused, sent: false);
        }

        /// <summary>
        /// The request as the computer holds it, read by its owner: its state and the assistant's reply. A view of another
        /// request, or of another binding, is ignored.
        /// </summary>
        public void Read(WorkspaceRequestView? view)
        {
            if (view == null || RequestId == null || view.RequestId != RequestId) return;
            if (view.ProjectId != Scope.ProjectId || view.WorkstreamId != Scope.WorkstreamId || view.ExecutionId != Scope.ExecutionId) return;
            var status = view.Status?.Type == Newtonsoft.Json.Linq.JTokenType.String ? view.Status.ToString() : "";
            if (view.Reply != null)
            {
                reply = view.Reply.Text;
                repliedBy = view.Reply.Model;
                repliedOn = view.Reply.Served == AssistantServed.Remote ? view.Reply.Provider : null;
            }
            switch (status)
            {
                case "running":
                    Stage = AskStage.Thinking;
                    break;
                case "waiting_for_runtime":
                    Stage = AskStage.Waiting;
                    break;
                case "waiting_for_human":
                    Stage = AskStage.NeedsYou;
                    break;
                case "responded":
                    Stage = AskStage.Answered;
                    break;
                case "interrupted":
                    NotAnsweredBy(Words.AtLimit, sent: true);
                    return;
                case "failed":
                    NotAnsweredBy(Words.CouldNotGoOn, sent: true);
                    return;
                case "stopped":
                    stopped = true;
                    NotAnsweredBy(Words.YouStopped, sent: true);
                    return;
                default:
                    return;
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// The send never left the headset (no session to send it through): the Ask is written again, its words kept, and
        /// nothing is waited for (AUDIT MUST). Only while it stands Sent with no request.
        /// </summary>
        public void Unsent()
        {
            if (Stage != AskStage.Sent || RequestId != null) return;
            CommandId = null;
            sent = null;
            unknown = false;
            Stage = AskStage.Writing;
            Changed?.Invoke();
        }

        /// <summary>Whether Try Again may write and send the Ask anew: only one that was never sent.</summary>
        public bool CanTryAgain => Stage == AskStage.NotAnswered && notSent && RequestId == null;

        /// <summary>The Ask written again, its words kept, after a send that did not go.</summary>
        public void TryAgain()
        {
            if (!CanTryAgain) return;
            CommandId = null;
            notAnswered = null;
            notSent = false;
            stopped = false;
            sent = null;
            unknown = false;
            Stage = AskStage.Writing;
            Changed?.Invoke();
        }

        private void NotAnsweredBy(string why, bool sent)
        {
            notAnswered = why;
            notSent = !sent;
            Stage = AskStage.NotAnswered;
            Changed?.Invoke();
        }

        /// <summary>The Ask as a card, for its state.</summary>
        public RoomCard Card
        {
            get
            {
                var question = LineWords.Theirs(draft.Trim());
                var asked = question.Text.Length == 0 ? Empty : question;
                switch (Stage)
                {
                    case AskStage.Writing:
                        return unavailable
                            ? Status(Words.NotAnswered, Words.NoAssistant, RoomTone.Bad, asked)
                            : new RoomCard(Scope.Name, asked, Scope.Head);
                    case AskStage.Sent:
                        return Status(Words.Sent, unknown ? Words.MayNotHaveReached : Words.SentLine, RoomTone.Working, asked);
                    case AskStage.Thinking:
                        return Status(Words.Thinking, Words.ThinkingLine, RoomTone.Working, asked);
                    case AskStage.Waiting:
                        return Status(Words.Waiting, Words.WaitingLine, RoomTone.Working, asked);
                    case AskStage.NeedsYou:
                        // "Below" only where the band holds the assistant's question (AUDIT MUST 1).
                        return Status(Words.NeedsYou, Band != null ? Words.NeedsYouLine : Words.WaitsLine, RoomTone.Attention, asked);
                    case AskStage.Answered:
                        // An answer is a report, not a passed check: quiet, never green. The model on the foot line, never a slot.
                        return Band != null
                            ? Status(Words.Answered, Words.AnsweredLine, RoomTone.Quiet, asked, AnsweredBy)
                            : Status(Words.Answered, Words.NoReplyLine, RoomTone.Quiet, asked);
                    default:
                        var title = stopped ? Words.Stopped : notSent ? Words.NotSent : Words.NotAnswered;
                        return Status(title, notAnswered, RoomTone.Bad, asked);
                }
            }
        }

        /// <summary>
        /// The assistant's words for the band low in the view, read part by part as the band reads: its question while it
        /// needs the person, its reply once it answered. Outside words, reported. Null on every other state, and before it
        /// has any. The bar gives them to the band; the card never holds them.
        /// </summary>
        public LineWords? Band => Stage == AskStage.NeedsYou || Stage == AskStage.Answered ? Reported : null;

        /// <summary>The assistant's words, outside and reported, or null before it has any.</summary>
        private LineWords? Reported => reply == null || LineWords.Theirs(reply).Text.Length == 0 ? null : LineWords.Theirs(reply);

        /// <summary>
        /// "Answered by" and the model that wrote the reply, as every model chip names it and as a capture hides it
        /// (CaptureNames), or null: only from the reply's own model, never the model the computer offered (AUDIT MUST 2).
        /// </summary>
        private LineWords? AnsweredBy
        {
            get
            {
                var named = repliedBy == null ? null : CaptureNames.ModelNameOrNull(KitPresenter.ShortModelName(repliedBy) ?? repliedBy);
                if (named == null) return null;
                var by = LineWords.Ours(Words.AnsweredBy) + LineWords.TheirName(named);
                // Where it ran is said whenever it is not this computer (ADR 0030, 2026-11-12).
                return string.IsNullOrWhiteSpace(repliedOn) ? by : by + LineWords.Ours(" on ") + LineWords.TheirName(repliedOn);
            }
        }

        private LineWords Empty => LineWords.Ours(Scope.IsTask ? Words.EmptyTask : Scope.IsAllWork ? Words.EmptyAllWork : Words.EmptyProject);

        private RoomCard Status(string title, string? line, RoomTone tone, LineWords description, LineWords? more = null) =>
            new RoomCard(Scope.Name, description, Scope.Head, new RoomCardStatus(title, line, tone), more: more);
    }
}

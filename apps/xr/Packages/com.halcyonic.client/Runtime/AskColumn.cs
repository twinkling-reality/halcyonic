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
            public const string Answered = "Answered";
            public const string AnsweredLine = "The assistant's reply is below";
            public const string AnsweredBy = "Answered by ";
            public const string NotAnswered = "Not Answered";
            public const string NotSent = "Not Sent";
            public const string Stopped = "Stopped";
            public const string NoAssistant = "No assistant on your computer";
            public const string Refused = "Your computer refused it";
            public const string AtLimit = "It stopped at a limit";
            public const string CouldNotGoOn = "The assistant could not go on";
            public const string YouStopped = "You stopped it";
        }

        private readonly CommandFactory commands;
        private string draft = "";
        private string? model, reply, notAnswered;
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

        /// <summary>The words the person writes or speaks, while nothing is sent.</summary>
        public void Write(string? words)
        {
            if (Stage != AskStage.Writing) return;
            draft = words ?? "";
            Changed?.Invoke();
        }

        /// <summary>What the computer says of its assistant: its model, or that there is none.</summary>
        public void Offer(WorkspaceAgentStatus? status)
        {
            switch (status)
            {
                case AvailableWorkspaceAgent available:
                    model = available.Model;
                    unavailable = false;
                    break;
                case UnavailableWorkspaceAgent:
                    model = null;
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
            Stage = AskStage.Sent;
            Changed?.Invoke();
            return command;
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
                model = view.Reply.Model;
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
                        return Status(Words.Sent, Words.SentLine, RoomTone.Working, asked);
                    case AskStage.Thinking:
                        return Status(Words.Thinking, Words.ThinkingLine, RoomTone.Working, asked);
                    case AskStage.Waiting:
                        return Status(Words.Waiting, Words.WaitingLine, RoomTone.Working, asked);
                    case AskStage.NeedsYou:
                        return Status(Words.NeedsYou, Words.NeedsYouLine, RoomTone.Attention, asked);
                    case AskStage.Answered:
                        // An answer is a report, not a passed check: quiet, never green. The model on the foot line, never a slot.
                        return Status(Words.Answered, Words.AnsweredLine, RoomTone.Quiet, asked, AnsweredBy);
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

        /// <summary>"Answered by" and the model as every model chip names it and as a capture hides it (CaptureNames), or null.</summary>
        private LineWords? AnsweredBy
        {
            get
            {
                var named = model == null ? null : CaptureNames.ModelNameOrNull(KitPresenter.ShortModelName(model) ?? model);
                return named == null ? null : LineWords.Ours(Words.AnsweredBy) + LineWords.TheirName(named);
            }
        }

        private LineWords Empty => LineWords.Ours(Scope.IsTask ? Words.EmptyTask : Scope.IsAllWork ? Words.EmptyAllWork : Words.EmptyProject);

        private RoomCard Status(string title, string? line, RoomTone tone, LineWords description, LineWords? more = null) =>
            new RoomCard(Scope.Name, description, Scope.Head, new RoomCardStatus(title, line, tone), more: more);
    }
}

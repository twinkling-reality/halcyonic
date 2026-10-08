#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using Newtonsoft.Json;

namespace Halcyonic.Client
{
    /// <summary>
    /// Plays a <see cref="DemonstrationRecording"/> as though a control plane sent it, entirely on this
    /// device: it opens no socket and sends nothing anywhere. Hello is answered with the recorded
    /// welcome and the beginning's snapshot, and the events follow at their recorded pace. Where the
    /// recording offers an answer, a person's command switches to the continuation recorded for it;
    /// at a decision the recording holds until one comes. The command itself goes nowhere: it is
    /// answered with a <c>rejected</c> acknowledgement whose code is <c>demonstration</c>, saying in
    /// words that nothing reached an agent and which recorded answer the recording continues with, and
    /// no command is ever reported accepted or done. Once a final state has held, the demonstration
    /// starts again within the same connection, with the beginning's snapshot, so the session never
    /// reads as disconnected.
    /// </summary>
    public sealed class DemonstrationTransport : IRealtimeTransport
    {
        /// <summary>The endpoint a demonstration session is given. Nothing connects to it.</summary>
        public static readonly Uri Endpoint = new Uri("halcyonic-demonstration:recorded");

        /// <summary>The answer to a command for which the recording holds nothing where it stands.</summary>
        public const string NothingRecorded = "Not sent to any agent; the recording has no answer recorded for that here, so nothing changes.";

        private readonly DemonstrationOptions options;
        private readonly DemonstrationPlayer? player;
        private readonly Task<DemonstrationRecording>? loading;
        private DemonstrationRecording? read;
        private readonly ConcurrentQueue<string?> outbox = new ConcurrentQueue<string?>();
        private readonly SemaphoreSlim pending = new SemaphoreSlim(0);
        private readonly CancellationTokenSource closing = new CancellationTokenSource();
        private readonly object gate = new object();
        private readonly Stopwatch clock = new Stopwatch();
        private TaskCompletionSource<bool> wake = NewWake();
        private int helloReceived;
        private int disposed;
        private int node;
        private int played;
        private TimeSpan nodeStart;
        private TimeSpan? holdStart;

        /// <summary>The answers taken since the beginning: each node left and how many of its events had played.</summary>
        private readonly List<(int Node, int After)> path = new List<(int Node, int After)>();

        public DemonstrationTransport(DemonstrationRecording recording, DemonstrationOptions? options = null)
            : this(options, null)
        {
            read = recording;
        }

        /// <summary>Plays the player's recording once it has been read, and tells the player where it stands.</summary>
        internal DemonstrationTransport(DemonstrationPlayer player)
            : this(player.Options, player)
        {
            loading = player.Loading;
            player.PlayedBy(this);
        }

        private DemonstrationTransport(DemonstrationOptions? options, DemonstrationPlayer? player)
        {
            this.options = options ?? new DemonstrationOptions();
            this.player = player;
            if (!(this.options.Speed > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(options), this.options.Speed, "The speed must be positive.");
            }
        }

        /// <summary>The recording this connection plays; there is one once the connection is open.</summary>
        private DemonstrationRecording Playing => read ?? throw new InvalidOperationException("The demonstration has not been read yet.");

        public string? CloseDescription { get; private set; }

        /// <summary>
        /// What the acknowledgement says when a person's answer has a recorded continuation: that it
        /// reached no agent, and which recorded answer the recording continues with.
        /// </summary>
        public static string Answered(DemonstrationAnswer answer) => answer.Kind switch
        {
            DemonstrationAnswerKind.Approve => "Not sent to any agent; the recording continues as recorded for approving.",
            DemonstrationAnswerKind.Deny => "Not sent to any agent; the recording continues as recorded for denying.",
            DemonstrationAnswerKind.Interrupt => "Not sent to any agent; the recording continues as recorded for stopping it.",
            DemonstrationAnswerKind.Answer => "Nothing is sent to an agent. The recording goes on as if you answered \u201C" + answer.Label + "\u201D.",
            _ => "Not sent to any agent; the recording continues as recorded for “" + answer.Label + "”.",
        };

        /// <summary>
        /// What the acknowledgement says when a person typed an instruction the recording does not hold:
        /// it continues with a recorded one instead, and names it.
        /// </summary>
        public static string AnsweredInstead(DemonstrationAnswer answer) =>
            "Not sent to any agent; the recording holds only its own instructions, so it continues as recorded for “" + answer.Label + "”.";

        /// <summary>
        /// There is nothing to connect to, and the token is never used; the connection opens once the
        /// recording has been read, and fails with the reason if it cannot be.
        /// </summary>
        public async Task ConnectAsync(Uri endpoint, string accessToken, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (read != null || loading == null) return;
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                await Task.WhenAny(loading, cancelled.Task).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            read = await loading.ConfigureAwait(false);
        }

        public Task SendAsync(string message, CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref disposed) == 1) throw new ObjectDisposedException(nameof(DemonstrationTransport));
            ClientMessage received;
            try
            {
                received = HalcyonicJson.Deserialize<ClientMessage>(message);
            }
            catch (JsonException)
            {
                Send(Error("invalid_message", "The message does not match the realtime contract."));
                return Task.CompletedTask;
            }
            switch (received)
            {
                case HelloMessage _:
                    if (Interlocked.Exchange(ref helloReceived, 1) == 1)
                    {
                        Send(Error("unexpected_hello", "hello was already received."));
                        break;
                    }
                    lock (gate)
                    {
                        clock.Start();
                        // A connection after a pause goes on where the last one stood, if the client
                        // holds what was played up to there; otherwise it plays from the beginning.
                        if (!Resume(((HelloMessage)received).Resume))
                        {
                            Send(Playing.Welcome);
                            Send(Playing.Snapshot);
                            Begin(0);
                        }
                    }
                    _ = PlayAsync(closing.Token);
                    break;
                case PingMessage ping:
                    Send(new PongMessage { Nonce = ping.Nonce });
                    break;
                case CommandMessage command:
                    Answer(command.Command);
                    break;
            }
            return Task.CompletedTask;
        }

        /// <summary>The next message, or null once the connection has ended.</summary>
        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
        {
            await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
            outbox.TryDequeue(out var message);
            return message;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            closing.Cancel();
            lock (gate)
            {
                // Where this connection stood, for the next one to go on from, as after a pause.
                if (player != null && read != null && helloReceived == 1)
                {
                    var now = clock.Elapsed;
                    player.Stood(new DemonstrationPlace(path.ToArray(), node, played, now - nodeStart, holdStart.HasValue ? now - holdStart.Value : (TimeSpan?)null));
                }
            }
        }

        /// <summary>
        /// Goes on where the player's last connection stood: welcomes the client as resumed and sends
        /// only the events along that path after the position it holds, then keeps the timing it had.
        /// False, with nothing sent, when there is no such place or the client's cursor is not on its
        /// path, as after a journal it never saw.
        /// </summary>
        private bool Resume(ResumeCursor? cursor)
        {
            var place = player?.TakeStood();
            if (place == null || cursor == null || cursor.JournalId != Playing.Welcome.Journal.JournalId) return false;
            var steps = new List<(int Node, int Count)>(place.Path);
            steps.Add((place.Node, place.Played));
            var position = Playing.Snapshot.Snapshot.Position;
            var onPath = cursor.Position == position;
            var missed = new List<EventMessage>();
            foreach (var (index, count) in steps)
            {
                var events = Playing.Nodes[index].Events;
                for (var each = 0; each < count; each++)
                {
                    var message = events[each].Message;
                    position = message.Position;
                    if (position == cursor.Position) onPath = true;
                    else if (position > cursor.Position) missed.Add(message);
                }
            }
            var current = Playing.Nodes[place.Node];
            var ended = place.Played == current.Events.Count && current.EndingSnapshot != null;
            if (!onPath || ended) return false;
            Send(new WelcomeMessage
            {
                Protocol = Playing.Welcome.Protocol,
                Journal = Playing.Welcome.Journal,
                Head = position,
                Resumed = true,
                ServerTime = Playing.Welcome.ServerTime,
                CommandPolicies = Playing.Welcome.CommandPolicies,
            });
            foreach (var message in missed) Send(message);
            path.Clear();
            path.AddRange(place.Path);
            node = place.Node;
            played = place.Played;
            var now = clock.Elapsed;
            nodeStart = now - place.IntoNode;
            holdStart = place.IntoHold.HasValue ? now - place.IntoHold.Value : (TimeSpan?)null;
            Report();
            return true;
        }

        private async Task PlayAsync(CancellationToken stop)
        {
            try
            {
                while (true)
                {
                    TimeSpan? wait;
                    Task woken;
                    lock (gate)
                    {
                        wait = Advance();
                        woken = wake.Task;
                    }
                    if (wait == TimeSpan.Zero) continue;
                    using (var waiting = CancellationTokenSource.CreateLinkedTokenSource(stop))
                    {
                        await Task.WhenAny(Task.Delay(wait ?? Timeout.InfiniteTimeSpan, waiting.Token), woken).ConfigureAwait(false);
                        waiting.Cancel();
                    }
                    stop.ThrowIfCancellationRequested();
                }
            }
            catch (OperationCanceledException)
            {
                // The connection closed.
            }
            catch (Exception error)
            {
                End("the demonstration could not continue: " + error.Message);
            }
        }

        /// <summary>
        /// Sends whatever is due. Returns zero when it sent something and should be asked again, how
        /// long until something is due, or null while the recording holds for an answer.
        /// </summary>
        private TimeSpan? Advance()
        {
            var now = clock.Elapsed;
            var current = Playing.Nodes[node];
            if (played < current.Events.Count)
            {
                var due = nodeStart + Scaled(current.Events[played].At);
                if (due > now) return due - now;
                // One instant at a time: a person answers only between instants, never inside one.
                var at = current.Events[played].At;
                while (played < current.Events.Count && current.Events[played].At == at)
                {
                    Send(current.Events[played].Message);
                    played++;
                }
                if (played == current.Events.Count) Finish(now);
                Report();
                return TimeSpan.Zero;
            }
            var hold = HoldOf(current);
            if (hold == null || player?.Reading == true) return null;
            var end = holdStart.GetValueOrDefault(now) + hold.Value;
            if (end > now) return end - now;
            // The final state has held: the demonstration starts again, in the same connection.
            Send(Playing.Snapshot);
            Begin(0);
            return TimeSpan.Zero;
        }

        /// <summary>Continues with a node from its first event, now.</summary>
        private void Begin(int index)
        {
            if (index == 0) path.Clear();
            node = index;
            played = 0;
            nodeStart = clock.Elapsed;
            holdStart = null;
            if (index == 0) player?.Began();
            if (Playing.Nodes[index].Events.Count == 0) Finish(nodeStart);
            Report();
        }

        /// <summary>The node's last event has played: its ending snapshot follows, and its hold begins.</summary>
        private void Finish(TimeSpan now)
        {
            var ending = Playing.Nodes[node].EndingSnapshot;
            if (ending != null) Send(ending);
            holdStart = now;
        }

        private void Report()
        {
            var current = Playing.Nodes[node];
            player?.Report(node, played, played == current.Events.Count && current.EndingSnapshot != null);
        }

        /// <summary>How long a node's final state holds, scaled; null while it waits for an answer.</summary>
        private TimeSpan? HoldOf(DemonstrationNode current)
        {
            if (current.Hold == null) return null;
            var hold = options.Hold ?? current.Hold.Value;
            return hold == Timeout.InfiniteTimeSpan ? (TimeSpan?)null : Scaled(hold);
        }

        private TimeSpan Scaled(TimeSpan recorded) => TimeSpan.FromTicks((long)(recorded.Ticks / options.Speed));

        /// <summary>
        /// Answers a command in words and, where the recording holds a continuation for it here,
        /// continues with that. Nothing is journaled and nothing leaves this device.
        /// </summary>
        private void Answer(CommandEnvelope command)
        {
            lock (gate)
            {
                // Before hello nothing plays, so nothing is offered.
                var offered = read == null || helloReceived == 0
                    ? Array.Empty<DemonstrationBranch>()
                    : read.Nodes[node].BranchesAfter(played);
                var branch = Match(offered, command, out var words);
                Send(Acknowledge(command, words));
                if (branch == null) return;
                path.Add((node, played));
                Begin(branch.Node);
                Wake();
            }
        }

        /// <summary>
        /// A task's file opened or closed (<see cref="DemonstrationPlayer.Reading"/>). Where a final state
        /// holds, its hold runs whole from the close, however long the file was open.
        /// </summary>
        internal void ReadingChanged(bool reading)
        {
            lock (gate)
            {
                if (read == null || helloReceived == 0) return;
                if (!reading && played == read.Nodes[node].Events.Count) holdStart = clock.Elapsed;
                Wake();
            }
        }

        /// <summary>Wakes the playback to look again at what is due; called holding the gate.</summary>
        private void Wake()
        {
            var woken = wake;
            wake = NewWake();
            woken.TrySetResult(true);
        }

        private static DemonstrationBranch? Match(IReadOnlyList<DemonstrationBranch> offered, CommandEnvelope command, out string words)
        {
            words = NothingRecorded;
            switch (command)
            {
                case ExecutionRespondToApprovalCommand respond:
                    var kind = respond.Payload.Decision == ApprovalDecision.Approve ? DemonstrationAnswerKind.Approve : DemonstrationAnswerKind.Deny;
                    foreach (var branch in offered)
                    {
                        var answer = branch.Answer;
                        if (answer.Kind != kind || answer.ExecutionId != respond.Payload.ExecutionId || answer.ApprovalId != respond.Payload.ApprovalId) continue;
                        words = Answered(answer);
                        return branch;
                    }
                    return null;
                case ExecutionInterruptCommand interrupt:
                    foreach (var branch in offered)
                    {
                        if (branch.Answer.Kind != DemonstrationAnswerKind.Interrupt || branch.Answer.ExecutionId != interrupt.Payload.ExecutionId) continue;
                        words = Answered(branch.Answer);
                        return branch;
                    }
                    return null;
                case ExecutionAnswerQuestionCommand answered:
                    foreach (var branch in offered)
                    {
                        var answer = branch.Answer;
                        if (answer.Kind != DemonstrationAnswerKind.Answer || answer.ExecutionId != answered.Payload.ExecutionId
                            || answer.QuestionId != answered.Payload.QuestionId || !SameChoice(answer.Answers, answered.Payload.Answers)) continue;
                        words = Answered(answer);
                        return branch;
                    }
                    return null;
                case ExecutionSendInstructionCommand instruct:
                    DemonstrationBranch? first = null;
                    foreach (var branch in offered)
                    {
                        if (branch.Answer.Kind != DemonstrationAnswerKind.Instruct || branch.Answer.ExecutionId != instruct.Payload.ExecutionId) continue;
                        if (Same(branch.Answer.Text!, instruct.Payload.Text))
                        {
                            words = Answered(branch.Answer);
                            return branch;
                        }
                        first ??= branch;
                    }
                    if (first != null) words = AnsweredInstead(first.Answer);
                    return first;
                default:
                    return null;
            }
        }

        /// <summary>
        /// The same options chosen for every prompt the recording answered, and nothing typed: a
        /// recorded question offers only its options (its plan allows nothing else).
        /// </summary>
        private static bool SameChoice(IReadOnlyList<QuestionAnswer> recorded, IReadOnlyList<QuestionAnswer> sent)
        {
            if (sent.Count != recorded.Count) return false;
            foreach (var expected in recorded)
            {
                QuestionAnswer? given = null;
                foreach (var each in sent) if (each.Key == expected.Key) given = each;
                if (given == null || !string.IsNullOrEmpty(given.Text) || given.Selected.Count != expected.Selected.Count) return false;
                foreach (var option in expected.Selected) if (!given.Selected.Contains(option)) return false;
            }
            return true;
        }

        /// <summary>The same instruction, whatever its spacing and letter case.</summary>
        private static bool Same(string recorded, string sent) =>
            string.Equals(Collapse(recorded), Collapse(sent), StringComparison.OrdinalIgnoreCase);

        private static string Collapse(string text)
        {
            var result = new StringBuilder(text.Length);
            foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (result.Length > 0) result.Append(' ');
                result.Append(word);
            }
            return result.ToString();
        }

        private void Send(ServerMessage message) => Enqueue(HalcyonicJson.Serialize(message));

        private void End(string description)
        {
            CloseDescription = description;
            Enqueue(null);
        }

        private void Enqueue(string? message)
        {
            outbox.Enqueue(message);
            pending.Release();
        }

        private static TaskCompletionSource<bool> NewWake() =>
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private static ErrorMessage Error(string code, string message) =>
            new ErrorMessage
            {
                Error = new ErrorBody { Code = code, Message = message, Issues = new List<ValidationIssue>() },
                Fatal = false,
            };

        /// <summary>
        /// The answer to a person's command: rejected with the code <c>demonstration</c>, in words. It is
        /// not journaled, and the recorded continuation, if any, carries the recording's own command.
        /// </summary>
        private static CommandAckMessage Acknowledge(CommandEnvelope command, string words)
        {
            var view = new CommandView
            {
                CommandId = command.CommandId,
                CommandType = JsonConvert.DeserializeObject<CommandType>(JsonConvert.ToString(command.CommandType), HalcyonicJson.Tolerant),
                Status = CommandStatus.Rejected,
                IssuedAt = command.IssuedAt,
                UpdatedAt = HalcyonicJson.FormatTimestamp(DateTimeOffset.UtcNow),
                Rejection = new CommandRejection { Code = RejectionCode.Demonstration, Message = words },
            };
            switch (command)
            {
                case WorkstreamCreateCommand create:
                    view.ProjectId = create.Payload.ProjectId;
                    break;
                case ExecutionStartCommand start:
                    view.WorkstreamId = start.Payload.WorkstreamId;
                    break;
                case ExecutionSendInstructionCommand instruct:
                    view.ExecutionId = instruct.Payload.ExecutionId;
                    break;
                case ExecutionRespondToApprovalCommand respond:
                    view.ExecutionId = respond.Payload.ExecutionId;
                    break;
                case ExecutionInterruptCommand interrupt:
                    view.ExecutionId = interrupt.Payload.ExecutionId;
                    break;
                case ExecutionAnswerQuestionCommand answer:
                    view.ExecutionId = answer.Payload.ExecutionId;
                    break;
            }
            return new CommandAckMessage
            {
                CommandId = command.CommandId,
                Disposition = CommandAckDisposition.Rejected,
                Command = view,
            };
        }
    }
}

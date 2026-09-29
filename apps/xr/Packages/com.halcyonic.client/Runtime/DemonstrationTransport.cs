#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using Newtonsoft.Json;

namespace Halcyonic.Client
{
    public sealed class DemonstrationOptions
    {
        /// <summary>How fast to play: 1 is the recorded pace, 2 twice as fast.</summary>
        public double Speed { get; set; } = 1;

        /// <summary>
        /// How long the final state stays, at the recorded pace, before the connection ends and the
        /// session starts the demonstration again. <see cref="Timeout.InfiniteTimeSpan"/> holds it
        /// until the connection closes.
        /// </summary>
        public TimeSpan HoldAtEnd { get; set; } = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Plays a <see cref="DemonstrationRecording"/> as though a control plane sent it, entirely on this
    /// device: it opens no socket and sends nothing anywhere. Each connection plays the recording from
    /// its start: hello is answered with the recorded welcome and snapshot, the events follow at their
    /// recorded pace, the final state holds, and then the connection ends, so the session starts the
    /// demonstration again. A recording cannot confirm a command, so every command is refused, in
    /// words; none is ever reported as accepted or done.
    /// </summary>
    public sealed class DemonstrationTransport : IRealtimeTransport
    {
        /// <summary>The endpoint a demonstration session is given. Nothing connects to it.</summary>
        public static readonly Uri Endpoint = new Uri("halcyonic-demonstration:recorded");

        /// <summary>Why every command is refused.</summary>
        public const string Refusal = "This is a recorded demonstration played on this device, so nothing was sent to an agent.";

        private readonly DemonstrationRecording recording;
        private readonly DemonstrationOptions options;
        private readonly ConcurrentQueue<string?> outbox = new ConcurrentQueue<string?>();
        private readonly SemaphoreSlim pending = new SemaphoreSlim(0);
        private readonly CancellationTokenSource closing = new CancellationTokenSource();
        private int helloReceived;
        private int disposed;

        public DemonstrationTransport(DemonstrationRecording recording, DemonstrationOptions? options = null)
        {
            this.recording = recording;
            this.options = options ?? new DemonstrationOptions();
            if (!(this.options.Speed > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(options), this.options.Speed, "The speed must be positive.");
            }
        }

        /// <summary>A session that plays the recording, and starts it again after each end.</summary>
        public static RealtimeSession CreateSession(DemonstrationRecording recording, ClientInfo client, DemonstrationOptions? options = null) =>
            new RealtimeSession(
                new RealtimeSessionOptions(Endpoint, string.Empty, client),
                () => new DemonstrationTransport(recording, options));

        public string? CloseDescription { get; private set; }

        /// <summary>There is nothing to connect to, and the token is never used.</summary>
        public Task ConnectAsync(Uri endpoint, string accessToken, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
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
                    Send(recording.Welcome);
                    Send(recording.Snapshot);
                    _ = PlayAsync(closing.Token);
                    break;
                case PingMessage ping:
                    Send(new PongMessage { Nonce = ping.Nonce });
                    break;
                case CommandMessage command:
                    Send(Refuse(command.Command));
                    break;
            }
            return Task.CompletedTask;
        }

        /// <summary>The next message, or null once the demonstration has ended.</summary>
        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
        {
            await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
            outbox.TryDequeue(out var message);
            return message;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) closing.Cancel();
        }

        private async Task PlayAsync(CancellationToken stop)
        {
            try
            {
                var previous = TimeSpan.Zero;
                foreach (var recorded in recording.Events)
                {
                    if (recorded.At > previous) await WaitAsync(recorded.At - previous, stop).ConfigureAwait(false);
                    previous = recorded.At;
                    Send(recorded.Message);
                }
                await WaitAsync(options.HoldAtEnd, stop).ConfigureAwait(false);
                End("the demonstration ended and starts again");
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

        /// <summary>Waits out a recorded span, scaled by the speed.</summary>
        private Task WaitAsync(TimeSpan recorded, CancellationToken stop) =>
            recorded == Timeout.InfiniteTimeSpan
                ? Task.Delay(Timeout.InfiniteTimeSpan, stop)
                : Task.Delay(TimeSpan.FromTicks((long)(recorded.Ticks / options.Speed)), stop);

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

        private static ErrorMessage Error(string code, string message) =>
            new ErrorMessage
            {
                Error = new ErrorBody { Code = code, Message = message, Issues = new List<ValidationIssue>() },
                Fatal = false,
            };

        /// <summary>A rejection in words. It is not journaled: nothing in the recording changes.</summary>
        private static CommandAckMessage Refuse(CommandEnvelope command)
        {
            var view = new CommandView
            {
                CommandId = command.CommandId,
                CommandType = JsonConvert.DeserializeObject<CommandType>(JsonConvert.ToString(command.CommandType), HalcyonicJson.Tolerant),
                Status = CommandStatus.Rejected,
                IssuedAt = command.IssuedAt,
                UpdatedAt = HalcyonicJson.FormatTimestamp(DateTimeOffset.UtcNow),
                Rejection = new CommandRejection { Code = RejectionCode.InvalidState, Message = Refusal },
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

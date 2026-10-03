#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using Newtonsoft.Json;

namespace Halcyonic.Client
{
    public sealed class RealtimeSessionOptions
    {
        public RealtimeSessionOptions(Uri endpoint, string accessToken, ClientInfo client)
        {
            Endpoint = endpoint;
            AccessToken = accessToken;
            Client = client;
        }

        /// <summary>The realtime endpoint, for example ws://127.0.0.1:47800/realtime.</summary>
        public Uri Endpoint { get; }

        public string AccessToken { get; }

        public ClientInfo Client { get; }

        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// What the person is told when the control plane refuses the credential: how to fix it
        /// depends on how this device reaches the Mac (<see cref="ControlPlaneTarget"/>).
        /// </summary>
        public string AccessRefused { get; set; } = ConnectionText.AccessRefused;

        /// <summary>How often the session pings, so that a silently broken network is noticed.</summary>
        public TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>A connection that delivers nothing, not even a pong, for this long is abandoned.</summary>
        public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

        public TimeSpan CommandAckTimeout { get; set; } = TimeSpan.FromSeconds(15);

        public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

        public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Received messages not yet applied by <see cref="RealtimeSession.Pump"/>. Beyond this the
        /// session drops them and resynchronizes from a fresh snapshot instead of growing without bound.
        /// </summary>
        public int MaxPendingMessages { get; set; } = 10_000;
    }

    /// <summary>
    /// A resilient connection to the control plane's realtime stream, protocol version 1
    /// (docs/internal/architecture/REALTIME.md). Networking runs in the background.
    /// <see cref="Pump"/> applies whatever arrived to <see cref="State"/> on the calling thread, so a
    /// game loop calls it once per frame and never shares state across threads.
    /// </summary>
    public sealed class RealtimeSession : IDisposable
    {
        private readonly RealtimeSessionOptions options;
        private readonly Func<IRealtimeTransport> createTransport;
        private readonly ConcurrentQueue<object> inbox = new ConcurrentQueue<object>();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<CommandAckMessage>> awaitingAck =
            new ConcurrentDictionary<string, TaskCompletionSource<CommandAckMessage>>();
        private readonly Random jitter = new Random();
        private readonly object gate = new object();
        private CancellationTokenSource? stopping;
        private Task? loop;
        private IRealtimeTransport? commandChannel;

        /// <summary>The journal the command channel's connection was welcomed to, set with the channel.</summary>
        private string? channelJournal;

        private readonly object pauseGate = new object();
        private Task pauseTransition = Task.CompletedTask;
        private bool paused;
        /// <summary>Whether the last pause stopped a running session, so that resuming starts it again.</summary>
        private bool resumeAfterPause;

        /// <summary>The position of the last message received; used only by the background loop.</summary>
        private ResumeCursor? cursor;

        public RealtimeSession(RealtimeSessionOptions options, Func<IRealtimeTransport>? createTransport = null)
        {
            this.options = options;
            this.createTransport = createTransport ?? (() => new LoopbackWebSocketTransport());
        }

        public ClientProjection State { get; } = new ClientProjection();

        /// <summary>The connection status as of the last <see cref="Pump"/>.</summary>
        public ConnectionStatus Status { get; private set; } = ConnectionStatus.Stopped;

        public void Start()
        {
            lock (gate)
            {
                if (loop != null) throw new InvalidOperationException("The session is already running.");
                var source = new CancellationTokenSource();
                stopping = source;
                loop = Task.Run(() => RunAsync(source.Token));
            }
        }

        /// <summary>Stops the session. A pause in progress no longer resumes it.</summary>
        public Task StopAsync()
        {
            lock (pauseGate)
            {
                resumeAfterPause = false;
            }
            return StopRunningAsync();
        }

        /// <summary>
        /// Follows the application's pause state; a headset that goes to sleep loses its sockets.
        /// Pausing stops a running session, and resuming starts it again from the last position, but
        /// only if that pause stopped it. A repeated or unmatched notification, such as the resume a
        /// platform reports when an app starts, changes nothing. Transitions run in order.
        /// </summary>
        public Task SetPausedAsync(bool pause)
        {
            lock (pauseGate)
            {
                if (pause == paused) return pauseTransition;
                paused = pause;
                pauseTransition = pause ? PauseAfterAsync(pauseTransition) : ResumeAfterAsync(pauseTransition);
                return pauseTransition;
            }
        }

        private async Task PauseAfterAsync(Task previous)
        {
            await Settled(previous).ConfigureAwait(false);
            bool running;
            // Held together, so that a StopAsync in between cannot be undone by a stale answer.
            lock (pauseGate)
            {
                lock (gate)
                {
                    running = loop != null;
                }
                resumeAfterPause = running;
            }
            if (running) await StopRunningAsync().ConfigureAwait(false);
        }

        private async Task ResumeAfterAsync(Task previous)
        {
            await Settled(previous).ConfigureAwait(false);
            // Held together, so that a StopAsync cannot slip in between the check and the start.
            lock (pauseGate)
            {
                if (!resumeAfterPause) return;
                resumeAfterPause = false;
                lock (gate)
                {
                    if (loop == null) Start();
                }
            }
        }

        /// <summary>Waits for an earlier transition; its failure belongs to whoever awaited it.</summary>
        private static async Task Settled(Task transition)
        {
            try
            {
                await transition.ConfigureAwait(false);
            }
            catch
            {
                // Reported through the task returned for that transition.
            }
        }

        private async Task StopRunningAsync()
        {
            Task? running;
            CancellationTokenSource? source;
            lock (gate)
            {
                running = loop;
                source = stopping;
                loop = null;
                stopping = null;
            }
            if (running == null || source == null) return;
            source.Cancel();
            try
            {
                await running.ConfigureAwait(false);
            }
            finally
            {
                source.Dispose();
            }
        }

        public void Dispose()
        {
            lock (pauseGate)
            {
                resumeAfterPause = false;
            }
            lock (gate)
            {
                stopping?.Cancel();
            }
        }

        /// <summary>Applies everything received since the last call, in order, on the calling thread.</summary>
        public StateChanges Pump()
        {
            var changes = new StateChanges();
            while (inbox.TryDequeue(out var item))
            {
                switch (item)
                {
                    case ConnectionStatus status:
                        Status = status;
                        changes.ConnectionChanged = true;
                        break;
                    case WelcomeMessage welcome:
                        State.ApplyWelcome(welcome);
                        break;
                    case SnapshotMessage snapshot:
                        State.ApplySnapshot(snapshot.Snapshot, changes);
                        break;
                    case EventMessage message:
                        State.ApplyEvent(message, changes);
                        break;
                    case ErrorMessage error:
                        changes.ServerErrors.Add(error.Error);
                        break;
                }
            }
            return changes;
        }

        /// <summary>
        /// Sends a command and waits for its acknowledgement. An <c>accepted</c> acknowledgement means
        /// admitted, not done: the outcome arrives later as command events in <see cref="State"/>.
        /// Called on the thread that pumps the session, it sends only on a connection welcomed to the
        /// journal <see cref="State"/> shows then: a reconnect welcomed to another journal opens its
        /// command channel before its snapshot is applied, and a command decided on the last journal's
        /// state must never reach it.
        /// </summary>
        /// <exception cref="SessionUnavailableException">Not connected, or connected to another journal than the one shown; the command was not sent.</exception>
        /// <exception cref="CommandOutcomeUnknownException">The command may or may not have arrived.</exception>
        public async Task<CommandAckMessage> SubmitAsync(CommandEnvelope command, CancellationToken cancellationToken = default)
        {
            IRealtimeTransport? channel;
            string? welcomedTo;
            lock (gate)
            {
                channel = commandChannel;
                welcomedTo = channelJournal;
            }
            if (channel == null)
            {
                throw new SessionUnavailableException("Not connected to the control plane; the command was not sent.");
            }
            if (State.Journal?.JournalId is string shown && welcomedTo != null && welcomedTo != shown)
            {
                throw new SessionUnavailableException("Connected to another journal than the one shown, whose snapshot is still to be applied; the command was not sent.");
            }
            var ack = new TaskCompletionSource<CommandAckMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!awaitingAck.TryAdd(command.CommandId, ack))
            {
                throw new InvalidOperationException("Command " + command.CommandId + " is already awaiting its acknowledgement.");
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                try
                {
                    var message = HalcyonicJson.Serialize(new CommandMessage { Command = command });
                    await channel.SendAsync(message, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception error) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new CommandOutcomeUnknownException(command.CommandId, "Sending failed: " + error.Message, error);
                }
                var delay = Task.Delay(options.CommandAckTimeout, timeout.Token);
                if (await Task.WhenAny(ack.Task, delay).ConfigureAwait(false) == ack.Task)
                {
                    return await ack.Task.ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                throw new CommandOutcomeUnknownException(command.CommandId, "The control plane did not acknowledge the command in time.");
            }
            finally
            {
                timeout.Cancel();
                awaitingAck.TryRemove(command.CommandId, out _);
            }
        }

        private async Task RunAsync(CancellationToken stop)
        {
            var failures = 0;
            while (!stop.IsCancellationRequested)
            {
                Publish(new ConnectionStatus(ConnectionPhase.Connecting));
                var ending = await ServeConnectionAsync(stop).ConfigureAwait(false);
                if (stop.IsCancellationRequested) break;
                if (ending.Refused)
                {
                    Publish(new ConnectionStatus(ConnectionPhase.Refused, ending.Reason, accessRefused: ending.AccessRefused));
                    return;
                }
                failures = ending.WasLive ? 1 : failures + 1;
                var delay = RetryDelay(failures);
                Publish(new ConnectionStatus(ConnectionPhase.WaitingToRetry, ending.Reason, delay));
                try
                {
                    await Task.Delay(delay, stop).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            Publish(ConnectionStatus.Stopped);
        }

        private async Task<Ending> ServeConnectionAsync(CancellationToken stop)
        {
            using var transport = createTransport();
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(stop);
            Task? pinging = null;
            var live = false;
            try
            {
                using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop))
                {
                    connectTimeout.CancelAfter(options.ConnectTimeout);
                    try
                    {
                        await transport
                            .ConnectAsync(options.Endpoint, options.AccessToken, connectTimeout.Token)
                            .ConfigureAwait(false);
                    }
                    catch (Exception) when (connectTimeout.IsCancellationRequested && !stop.IsCancellationRequested)
                    {
                        return Ending.Failed("The control plane did not answer within " + Seconds(options.ConnectTimeout) + ".", false);
                    }
                }
                var hello = new HelloMessage { Client = options.Client, Resume = cursor };
                await transport.SendAsync(HalcyonicJson.Serialize(hello), connection.Token).ConfigureAwait(false);
                pinging = PingAsync(transport, connection.Token);

                while (true)
                {
                    var text = await ReceiveAsync(transport, connection.Token).ConfigureAwait(false);
                    if (text == null)
                    {
                        return Ending.Failed("The control plane closed the connection (" + transport.CloseDescription + ").", live);
                    }
                    switch (HalcyonicJson.Deserialize<ServerMessage>(text))
                    {
                        case WelcomeMessage welcome:
                            if (welcome.Protocol != ContractVersions.RealtimeProtocol)
                            {
                                return Ending.Refuse("The control plane speaks realtime protocol " + welcome.Protocol + ".");
                            }
                            lock (gate)
                            {
                                commandChannel = transport;
                                channelJournal = welcome.Journal?.JournalId;
                            }
                            Enqueue(welcome);
                            live = welcome.Resumed;
                            Publish(new ConnectionStatus(live ? ConnectionPhase.Live : ConnectionPhase.Synchronizing));
                            break;
                        case SnapshotMessage snapshot:
                            cursor = new ResumeCursor
                            {
                                JournalId = snapshot.Snapshot.Journal.JournalId,
                                Position = snapshot.Snapshot.Position,
                            };
                            if (!Enqueue(snapshot)) return Resynchronize();
                            if (!live)
                            {
                                live = true;
                                Publish(new ConnectionStatus(ConnectionPhase.Live));
                            }
                            break;
                        case EventMessage message:
                            if (cursor != null && message.Position > cursor.Position)
                            {
                                cursor = new ResumeCursor { JournalId = cursor.JournalId, Position = message.Position };
                            }
                            if (!Enqueue(message)) return Resynchronize();
                            break;
                        case CommandAckMessage ack:
                            if (awaitingAck.TryGetValue(ack.CommandId, out var waiter)) waiter.TrySetResult(ack);
                            break;
                        case ErrorMessage error:
                            Enqueue(error);
                            if (error.Fatal)
                            {
                                var reason = "The control plane refused the connection: " + error.Error.Message;
                                return error.Error.Code == "unsupported_protocol" ? Ending.Refuse(reason) : Ending.Failed(reason, live);
                            }
                            break;
                    }
                }
            }
            catch (Exception) when (stop.IsCancellationRequested)
            {
                return Ending.Stopped;
            }
            catch (UpgradeRefusedException refused) when (refused.Status == 401)
            {
                // The Mac answered and refused the credential; the same credential will be refused again.
                return Ending.RefuseAccess(options.AccessRefused);
            }
            catch (TokenNotSentException notSent) when (notSent.Outcome == LoopbackProofOutcome.Unproved)
            {
                // Something answered without proving it holds the access token: the Mac's is another, or
                // something else took its port. The token stayed here, and the same proof would fail again.
                return Ending.RefuseAccess(ConnectionText.AccessTokenUnproved);
            }
            catch (TokenNotSentException notSent) when (notSent.Outcome == LoopbackProofOutcome.NotLoopback)
            {
                // An endpoint by name, never sent the token: trying again would not change it.
                return Ending.RefuseAccess(notSent.Message);
            }
            catch (JsonException error)
            {
                // Never the parser's own words: they quote the value it could not read, which can be an
                // instruction, agent text or a title, and the status is logged (XR_CLIENT.md).
                return Ending.Failed("The control plane sent a message this app cannot read (" + error.GetType().Name + ").", live);
            }
            catch (Exception error)
            {
                // Any failure ends this connection and never the session: the next attempt resynchronizes.
                return Ending.Failed(error.Message, live);
            }
            finally
            {
                connection.Cancel();
                lock (gate)
                {
                    commandChannel = null;
                    channelJournal = null;
                }
                foreach (var pair in awaitingAck)
                {
                    pair.Value.TrySetException(new CommandOutcomeUnknownException(
                        pair.Key, "The connection closed before the command was acknowledged."));
                }
                if (pinging != null) await pinging.ConfigureAwait(false);
            }
        }

        private async Task<string?> ReceiveAsync(IRealtimeTransport transport, CancellationToken connection)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(connection);
            idle.CancelAfter(options.IdleTimeout);
            try
            {
                return await transport.ReceiveAsync(idle.Token).ConfigureAwait(false);
            }
            catch (Exception) when (idle.IsCancellationRequested && !connection.IsCancellationRequested)
            {
                throw new TimeoutException("The control plane sent nothing for " + Seconds(options.IdleTimeout) + ".");
            }
        }

        private async Task PingAsync(IRealtimeTransport transport, CancellationToken connection)
        {
            var ping = HalcyonicJson.Serialize(new PingMessage());
            try
            {
                while (true)
                {
                    await Task.Delay(options.PingInterval, connection).ConfigureAwait(false);
                    await transport.SendAsync(ping, connection).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // The receive loop notices a broken connection and ends it.
            }
        }

        private void Publish(ConnectionStatus status) => inbox.Enqueue(status);

        /// <summary>Queues a message for <see cref="Pump"/>; false when the consumer has fallen too far behind.</summary>
        private bool Enqueue(ServerMessage message)
        {
            if (inbox.Count >= options.MaxPendingMessages) return false;
            inbox.Enqueue(message);
            return true;
        }

        /// <summary>Drops the unapplied backlog; the next connection starts from a fresh snapshot.</summary>
        private Ending Resynchronize()
        {
            inbox.Clear();
            cursor = null;
            return Ending.Failed("The client fell behind; resynchronizing.", false);
        }

        private TimeSpan RetryDelay(int failures)
        {
            var ceiling = Math.Min(
                options.MaxRetryDelay.TotalMilliseconds,
                options.InitialRetryDelay.TotalMilliseconds * Math.Pow(2, Math.Min(failures - 1, 16)));
            // Jitter spreads out clients that lost the same control plane at the same moment.
            return TimeSpan.FromMilliseconds(ceiling * (0.8 + 0.2 * jitter.NextDouble()));
        }

        private static string Seconds(TimeSpan span) => span.TotalSeconds + " s";

        private sealed class Ending
        {
            public static readonly Ending Stopped = new Ending(null, false, false);

            private Ending(string? reason, bool wasLive, bool refused, bool accessRefused = false)
            {
                Reason = reason;
                WasLive = wasLive;
                Refused = refused;
                AccessRefused = accessRefused;
            }

            public string? Reason { get; }

            public bool WasLive { get; }

            public bool Refused { get; }

            public bool AccessRefused { get; }

            public static Ending Failed(string reason, bool wasLive) => new Ending(reason, wasLive, false);

            public static Ending Refuse(string reason) => new Ending(reason, false, true);

            public static Ending RefuseAccess(string words) => new Ending(words, false, true, accessRefused: true);
        }
    }

    /// <summary>The session is not connected, so the command was not sent.</summary>
    public sealed class SessionUnavailableException : Exception
    {
        public SessionUnavailableException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// The command may or may not have reached the control plane. Commands are idempotent by id:
    /// submitting the same command again reports its current state.
    /// </summary>
    public sealed class CommandOutcomeUnknownException : Exception
    {
        public CommandOutcomeUnknownException(string commandId, string message, Exception? inner = null)
            : base(message, inner)
        {
            CommandId = commandId;
        }

        public string CommandId { get; }
    }
}

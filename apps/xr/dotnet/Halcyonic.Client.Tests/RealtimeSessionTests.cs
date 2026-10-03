using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class RealtimeSessionTests
{
    private FakeServer server = null!;
    private RealtimeSession session = null!;

    private static RealtimeSessionOptions Options(Action<RealtimeSessionOptions>? adjust = null)
    {
        var options = new RealtimeSessionOptions(new Uri("ws://127.0.0.1:1/realtime"), "test-token", Samples.Client)
        {
            PingInterval = TimeSpan.FromHours(1),
            IdleTimeout = TimeSpan.FromSeconds(5),
            CommandAckTimeout = TimeSpan.FromSeconds(2),
            InitialRetryDelay = TimeSpan.FromMilliseconds(10),
            MaxRetryDelay = TimeSpan.FromMilliseconds(40),
        };
        adjust?.Invoke(options);
        return options;
    }

    private void StartSession(Action<RealtimeSessionOptions>? adjust = null)
    {
        server = new FakeServer();
        session = new RealtimeSession(Options(adjust), server.CreateTransport);
        session.Start();
    }

    [TearDown]
    public async Task StopSession()
    {
        if (session != null) await session.StopAsync();
    }

    private async Task<FakeConnection> ConnectLiveAsync(Snapshot snapshot)
    {
        var connection = await server.AcceptAsync();
        await connection.WelcomeWithSnapshotAsync(snapshot);
        await Pumping.Until(session, s => s.Status.IsLive, "the session is live");
        return connection;
    }

    [Test]
    public async Task SaysHelloFirstAndGoesLiveOnlyWithTheSnapshot()
    {
        StartSession();
        var connection = await server.AcceptAsync();
        var hello = await connection.ReceiveFromClientAsync();
        Assert.That((string?)hello["type"], Is.EqualTo("hello"));
        Assert.That((int?)hello["protocol"], Is.EqualTo(1));
        Assert.That((string?)hello["client"]!["name"], Is.EqualTo(Samples.Client.Name));
        Assert.That(hello["resume"]!.Type, Is.EqualTo(Newtonsoft.Json.Linq.JTokenType.Null));
        Assert.That(connection.AccessToken, Is.EqualTo("test-token"));

        connection.Send(Samples.Welcome(resumed: false, head: 3));
        await Pumping.Until(session, s => s.Status.Phase == ConnectionPhase.Synchronizing, "the session is synchronizing");
        Assert.That(session.State.Journal, Is.Null);
        Assert.That(session.State.RequiresConfirmation(CommandType.ExecutionInterrupt), Is.True);
        Assert.That(session.State.RequiresConfirmation(CommandType.ExecutionSendInstruction), Is.False);

        connection.Send(new SnapshotMessage { Snapshot = Samples.Snapshot(3, new[] { Samples.Workstream("w1") }) });
        await Pumping.Until(session, s => s.Status.IsLive, "the session is live");
        Assert.That(session.State.Position, Is.EqualTo(3));
        Assert.That(session.State.Workstreams.Keys, Is.EquivalentTo(new[] { "w1" }));
    }

    [Test]
    public async Task AppliesEventsInOrderAndIgnoresRepeats()
    {
        StartSession();
        var connection = await ConnectLiveAsync(Samples.Snapshot(3, new[] { Samples.Workstream("w1") }));

        connection.Send(Samples.Event(4, Samples.Workstream("w1", WorkstreamStatus.Starting)));
        connection.Send(Samples.Event(6, Samples.Workstream("w1", WorkstreamStatus.Running)));
        connection.Send(Samples.Event(6, Samples.Workstream("w1", WorkstreamStatus.Failed)));
        var changes = await Pumping.Until(session, s => s.State.Position == 6, "position 6 is applied");
        await Task.Delay(50);
        session.Pump();

        Assert.That(session.State.Workstreams["w1"].Status, Is.EqualTo(WorkstreamStatus.Running));
        Assert.That(changes.Workstreams, Is.EquivalentTo(new[] { "w1" }));
    }

    [Test]
    public async Task ReconnectsAndResumesFromTheLastPosition()
    {
        StartSession(options =>
        {
            options.InitialRetryDelay = TimeSpan.FromMilliseconds(200);
            options.MaxRetryDelay = TimeSpan.FromMilliseconds(400);
        });
        var first = await ConnectLiveAsync(Samples.Snapshot(5, new[] { Samples.Workstream("w1") }));
        first.Send(Samples.Event(7, Samples.Workstream("w1", WorkstreamStatus.Running)));
        await Pumping.Until(session, s => s.State.Position == 7, "position 7 is applied");

        first.Close("1001 going away");
        await Pumping.Until(session, s => s.Status.Phase == ConnectionPhase.WaitingToRetry, "the session waits to retry");
        Assert.That(session.Status.Detail, Does.Contain("1001 going away"));
        Assert.That(session.State.Workstreams["w1"].Status, Is.EqualTo(WorkstreamStatus.Running), "the last known state stays");

        var second = await server.AcceptAsync();
        var hello = await second.ReceiveFromClientAsync();
        Assert.That((string?)hello["resume"]!["journal_id"], Is.EqualTo(Samples.JournalId));
        Assert.That((long?)hello["resume"]!["position"], Is.EqualTo(7));

        second.Send(Samples.Welcome(resumed: true, head: 7));
        var changes = await Pumping.Until(session, s => s.Status.IsLive, "the session is live again");
        Assert.That(changes.Workstreams, Is.Empty, "nothing was resent");
        Assert.That(session.State.Workstreams["w1"].Status, Is.EqualTo(WorkstreamStatus.Running));
    }

    [Test]
    public async Task ASnapshotFromAnotherJournalReplacesEverything()
    {
        StartSession();
        var first = await ConnectLiveAsync(Samples.Snapshot(5, new[] { Samples.Workstream("w1") }));
        first.Close("1012 restarting");

        var second = await server.AcceptAsync();
        const string otherJournal = "01a0dcf1-5a80-7000-8000-0000000000ff";
        await second.WelcomeWithSnapshotAsync(
            Samples.Snapshot(2, new[] { Samples.Workstream("w9") }, journal: Samples.Journal(otherJournal, JournalOrigin.Fixture)));
        await Pumping.Until(session, s => s.State.Journal?.JournalId == otherJournal && s.Status.IsLive, "the new journal is live");

        Assert.That(session.State.Workstreams.Keys, Is.EquivalentTo(new[] { "w9" }));
        Assert.That(session.State.Position, Is.EqualTo(2));
    }

    [Test]
    public async Task ACommandDecidedOnOneJournalIsNotSentOnAReconnectWelcomedToAnotherBeforeItsSnapshotIsApplied()
    {
        StartSession();
        var first = await ConnectLiveAsync(Samples.Snapshot(5));
        first.Close("1012 restarting");

        var second = await server.AcceptAsync();
        await second.ReceiveFromClientAsync();
        const string otherJournal = "01a0dcf1-5a80-7000-8000-0000000000ff";
        // The welcome opens the command channel at once; the snapshot that moves the state comes after.
        second.Send(Samples.Welcome(resumed: false, head: 2, journalId: otherJournal));
        await Pumping.Until(session, s => s.Status.Phase == ConnectionPhase.Synchronizing, "welcomed to another journal, its snapshot still to come");
        Assert.That(session.State.Journal!.JournalId, Is.EqualTo(Samples.JournalId), "the state still shows the last journal");
        var decided = new CommandFactory(Samples.Client).CreateProject("Halcyonic");
        Assert.ThrowsAsync<SessionUnavailableException>(() => session.SubmitAsync(decided), "not sent, so it can be tried again on what shows next");

        second.Send(new SnapshotMessage { Snapshot = Samples.Snapshot(2, journal: Samples.Journal(otherJournal)) });
        await Pumping.Until(session, s => s.Status.IsLive && s.State.Journal?.JournalId == otherJournal, "the other journal is live");
        var next = new CommandFactory(Samples.Client).CreateProject("Halcyonic");
        var submitted = session.SubmitAsync(next);
        var sent = await second.ReceiveFromClientAsync();
        Assert.That((string?)sent["command"]!["command_id"], Is.EqualTo(next.CommandId), "the command decided on the last journal never reached this one");
        second.Send(new CommandAckMessage { CommandId = next.CommandId, Disposition = CommandAckDisposition.Accepted, Command = null });
        Assert.That((await submitted).Disposition, Is.EqualTo(CommandAckDisposition.Accepted));
    }

    [Test]
    public async Task ACommandResolvesWithItsAcknowledgement()
    {
        StartSession();
        var connection = await ConnectLiveAsync(Samples.Snapshot(1));
        var command = new CommandFactory(Samples.Client).CreateProject("Halcyonic");

        var submitted = session.SubmitAsync(command);
        var sent = await connection.ReceiveFromClientAsync();
        Assert.That((string?)sent["type"], Is.EqualTo("command"));
        Assert.That((string?)sent["command"]!["command_id"], Is.EqualTo(command.CommandId));

        connection.Send(new CommandAckMessage { CommandId = command.CommandId, Disposition = CommandAckDisposition.Accepted, Command = null });
        var ack = await submitted;
        Assert.That(ack.Disposition, Is.EqualTo(CommandAckDisposition.Accepted));
    }

    [Test]
    public void ACommandIsNotSentWithoutAConnection()
    {
        server = new FakeServer();
        session = new RealtimeSession(Options(), server.CreateTransport);
        var command = new CommandFactory(Samples.Client).CreateProject("Halcyonic");
        Assert.ThrowsAsync<SessionUnavailableException>(() => session.SubmitAsync(command));
    }

    [Test]
    public async Task ACommandCutOffByADisconnectHasAnUnknownOutcome()
    {
        StartSession();
        var connection = await ConnectLiveAsync(Samples.Snapshot(1));
        var command = new CommandFactory(Samples.Client).Interrupt("01a0dcf1-5a80-7000-8000-0000000000e1");

        var submitted = session.SubmitAsync(command);
        await connection.ReceiveFromClientAsync();
        connection.Close("1006 abnormal closure");

        var error = Assert.ThrowsAsync<CommandOutcomeUnknownException>(() => submitted);
        Assert.That(error!.CommandId, Is.EqualTo(command.CommandId));
    }

    [Test]
    public async Task AnUnacknowledgedCommandTimesOutWithAnUnknownOutcome()
    {
        StartSession(options => options.CommandAckTimeout = TimeSpan.FromMilliseconds(100));
        var connection = await ConnectLiveAsync(Samples.Snapshot(1));
        var command = new CommandFactory(Samples.Client).CreateProject("Halcyonic");

        var submitted = session.SubmitAsync(command);
        await connection.ReceiveFromClientAsync();
        Assert.ThrowsAsync<CommandOutcomeUnknownException>(() => submitted);
    }

    [Test]
    public async Task AnUnsupportedProtocolIsRefusedWithoutRetrying()
    {
        StartSession();
        var connection = await server.AcceptAsync();
        await connection.ReceiveFromClientAsync();
        connection.Send(new ErrorMessage
        {
            Error = new ErrorBody { Code = "unsupported_protocol", Message = "This server speaks realtime protocol 2.", Issues = new() },
            Fatal = true,
        });

        var changes = await Pumping.Until(session, s => s.Status.Phase == ConnectionPhase.Refused, "the session is refused");
        Assert.That(changes.ServerErrors.Single().Code, Is.EqualTo("unsupported_protocol"));
        await Task.Delay(100);
        Assert.That(server.Attempts, Is.EqualTo(1));
    }

    [Test]
    public async Task ASilentConnectionIsAbandonedAndReplaced()
    {
        StartSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMilliseconds(200);
            options.InitialRetryDelay = TimeSpan.FromMilliseconds(200);
            options.MaxRetryDelay = TimeSpan.FromMilliseconds(400);
        });
        var first = await ConnectLiveAsync(Samples.Snapshot(1));

        await Pumping.Until(session, s => s.Status.Phase == ConnectionPhase.WaitingToRetry, "the silent connection is abandoned");
        Assert.That(session.Status.Detail, Does.Contain("sent nothing"));
        var second = await server.AcceptAsync();
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(first.Disposed, Is.True);
    }

    [Test]
    public async Task PingsFlowWhileConnected()
    {
        StartSession(options => options.PingInterval = TimeSpan.FromMilliseconds(20));
        var connection = await ConnectLiveAsync(Samples.Snapshot(1));
        var ping = await connection.ReceiveFromClientAsync(includePings: true);
        Assert.That((string?)ping["type"], Is.EqualTo("ping"));
    }

    [Test]
    public async Task FailedConnectionsBackOffUpToTheLimit()
    {
        server = new FakeServer { ConnectFailure = new WebSocketException("Connection refused") };
        session = new RealtimeSession(
            Options(options =>
            {
                options.InitialRetryDelay = TimeSpan.FromMilliseconds(20);
                options.MaxRetryDelay = TimeSpan.FromMilliseconds(200);
            }),
            server.CreateTransport);
        session.Start();

        var delays = new List<TimeSpan>();
        await Pumping.Until(
            session,
            s =>
            {
                if (s.Status.RetryIn is { } delay && (delays.Count == 0 || delays[^1] != delay)) delays.Add(delay);
                return delays.Count >= 5;
            },
            "five retries are scheduled");

        Assert.That(session.Status.Detail, Does.Contain("Connection refused"));
        Assert.That(delays.Max(), Is.LessThanOrEqualTo(TimeSpan.FromMilliseconds(200)));
        Assert.That(delays[^1], Is.GreaterThan(delays[0]));
    }

    [Test]
    public async Task AMalformedMessageEndsTheConnectionButNotTheSession()
    {
        StartSession();
        var first = await ConnectLiveAsync(Samples.Snapshot(1));
        first.SendRaw("{\"type\":\"welcome\"");

        var second = await server.AcceptAsync();
        await second.WelcomeWithSnapshotAsync(Samples.Snapshot(2));
        await Pumping.Until(session, s => s.Status.IsLive && s.State.Position == 2, "the session is live again");
    }

    [Test]
    public async Task AConsumerThatFallsBehindIsResynchronized()
    {
        StartSession(options => options.MaxPendingMessages = 3);
        var first = await server.AcceptAsync();
        await first.WelcomeWithSnapshotAsync(Samples.Snapshot(1));
        for (var position = 2; position <= 6; position++) first.Send(Samples.Event(position));

        var second = await server.AcceptAsync();
        var hello = await second.ReceiveFromClientAsync();
        Assert.That(hello["resume"]!.Type, Is.EqualTo(Newtonsoft.Json.Linq.JTokenType.Null), "a fresh snapshot is requested");
    }

    [Test]
    public async Task StoppingEndsTheSession()
    {
        StartSession();
        await ConnectLiveAsync(Samples.Snapshot(1));
        await session.StopAsync();
        session.Pump();
        Assert.That(session.Status.Phase, Is.EqualTo(ConnectionPhase.Stopped));
    }

    [Test]
    public async Task AResumeWithoutAPauseChangesNothing()
    {
        // Unity reports a resume when an app starts and when an XR session starts in the editor.
        StartSession();
        await ConnectLiveAsync(Samples.Snapshot(1));
        await session.SetPausedAsync(false);
        session.Pump();
        Assert.That(session.Status.IsLive, Is.True);
        Assert.That(server.Attempts, Is.EqualTo(1));
    }

    [Test]
    public async Task PausingStopsAndResumingReconnectsFromTheLastPosition()
    {
        StartSession();
        var first = await ConnectLiveAsync(Samples.Snapshot(5, new[] { Samples.Workstream("w1") }));
        await session.SetPausedAsync(true);
        session.Pump();
        Assert.That(session.Status.Phase, Is.EqualTo(ConnectionPhase.Stopped));
        Assert.That(first.Disposed, Is.True);
        Assert.That(session.State.Workstreams.ContainsKey("w1"), Is.True, "the last known state stays");

        await session.SetPausedAsync(false);
        var second = await server.AcceptAsync();
        var hello = await second.ReceiveFromClientAsync();
        Assert.That((long?)hello["resume"]!["position"], Is.EqualTo(5));
        second.Send(Samples.Welcome(resumed: true, head: 5));
        await Pumping.Until(session, s => s.Status.IsLive, "the session is live again");
    }

    [Test]
    public async Task QuickPausesAndResumesRunInOrder()
    {
        StartSession();
        await ConnectLiveAsync(Samples.Snapshot(1));
        var transitions = new[]
        {
            session.SetPausedAsync(true),
            session.SetPausedAsync(false),
            session.SetPausedAsync(true),
            session.SetPausedAsync(false),
        };
        await Task.WhenAll(transitions);
        Assert.Throws<InvalidOperationException>(() => session.Start(), "the session runs again");
    }

    [Test]
    public async Task AStopDuringAPauseIsNotUndoneByTheResume()
    {
        // An app disabled while paused stops its session; a resume already queued must not revive it.
        StartSession();
        await ConnectLiveAsync(Samples.Snapshot(1));
        var pause = session.SetPausedAsync(true);
        var resume = session.SetPausedAsync(false);
        await session.StopAsync();
        await Task.WhenAll(pause, resume);
        Assert.That(server.Attempts, Is.EqualTo(1));
        Assert.DoesNotThrow(() => session.Start(), "the session was not running");
    }

    [Test]
    public async Task AResumeDoesNotStartASessionThatWasNotRunning()
    {
        server = new FakeServer();
        session = new RealtimeSession(Options(), server.CreateTransport);
        await session.SetPausedAsync(true);
        await session.SetPausedAsync(false);
        Assert.DoesNotThrow(() => session.Start(), "the session was not running");
    }
}

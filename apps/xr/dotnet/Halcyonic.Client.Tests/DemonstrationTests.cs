using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

internal static class Demonstration
{
    public const string Path = "apps/xr/Assets/Halcyonic/Resources/HalcyonicDemonstration.json";

    /// <summary>The demonstration the Unity project bundles, as `pnpm demonstration:record` wrote it.</summary>
    public static string Text() => File.ReadAllText(Repository.PathTo(Path));

    public static DemonstrationRecording Recording() => DemonstrationRecording.Parse(Text());

    /// <summary>Plays fast, then holds the final state until the connection closes.</summary>
    public static DemonstrationOptions Fast() => new() { Speed = 1000, HoldAtEnd = Timeout.InfiniteTimeSpan };

    public static string Edit(Action<JObject> edit)
    {
        var document = (JObject)Json.Parse(Text());
        edit(document);
        return document.ToString(Formatting.None);
    }
}

public class DemonstrationRecordingTests
{
    [Test]
    public void TheBundledDemonstrationPlaysTheWholeDemoTraceAndEveryMessageRoundTrips()
    {
        var document = (JObject)Json.Parse(Demonstration.Text());
        var events = (JArray)document["events"]!;
        Json.AssertRoundTrips<ServerMessage>(document["welcome"]!.ToString(Formatting.None));
        Json.AssertRoundTrips<ServerMessage>(document["snapshot"]!.ToString(Formatting.None));
        foreach (var entry in events) Json.AssertRoundTrips<ServerMessage>(entry["message"]!.ToString(Formatting.None));

        var trace = Repository.Trace();
        Assert.That(events, Has.Count.EqualTo(trace.Length));
        for (var index = 0; index < trace.Length; index++)
        {
            Assert.That(JToken.DeepEquals(events[index]["message"]!["event"], Json.Parse(trace[index])), Is.True, "event " + (index + 1));
        }

        var recording = Demonstration.Recording();
        Assert.That(recording.Source, Is.EqualTo("fixtures/traces/multiple_workstreams.jsonl"));
        Assert.That(recording.Welcome.Journal.Origin, Is.EqualTo(JournalOrigin.Fixture));
        Assert.That(recording.Snapshot.Snapshot.Runtimes, Is.Empty, "a replay has no runtime, so no action is offered");
        Assert.That(recording.Events.Select(e => e.Message.Position), Is.EqualTo(Enumerable.Range(1, trace.Length).Select(p => (long)p)));
        Assert.That(recording.Duration, Is.EqualTo(TimeSpan.FromMilliseconds(11_600)));
    }

    [Test]
    public void ALiveJournalIsNeverPlayedAsADemonstration()
    {
        var live = Demonstration.Edit(document =>
        {
            document["welcome"]!["journal"]!["origin"] = "live";
            document["snapshot"]!["snapshot"]!["journal"]!["origin"] = "live";
        });
        var error = Assert.Throws<InvalidDataException>(() => DemonstrationRecording.Parse(live));
        Assert.That(error!.Message, Does.Contain("fixture"));
    }

    [Test]
    public void AnythingElseItCannotPlayTruthfullyIsRefused()
    {
        var reordered = Demonstration.Edit(document =>
        {
            var events = (JArray)document["events"]!;
            var first = events[0];
            events.RemoveAt(0);
            events.Add(first);
        });
        var otherJournal = Demonstration.Edit(document => document["snapshot"]!["snapshot"]!["journal"]!["journal_id"] = "01a0dcf1-5a80-7000-8000-000000000009");
        var newerFormat = Demonstration.Edit(document => document["version"] = 2);
        var noEvents = Demonstration.Edit(document => document.Remove("events"));
        foreach (var text in new[] { reordered, otherJournal, newerFormat, noEvents, "not json", "[]" })
        {
            Assert.Throws<InvalidDataException>(() => DemonstrationRecording.Parse(text), text.Length > 40 ? text.Substring(0, 40) : text);
        }
    }
}

public class DemonstrationTransportTests
{
    private readonly CommandFactory commands = new(Samples.Client);

    private static async Task<ServerMessage> ReceiveAsync(DemonstrationTransport transport)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var text = await transport.ReceiveAsync(timeout.Token);
        Assert.That(text, Is.Not.Null, "the demonstration ended early");
        return Json.AssertRoundTrips<ServerMessage>(text!);
    }

    private static Task SendAsync(DemonstrationTransport transport, ClientMessage message) =>
        transport.SendAsync(HalcyonicJson.Serialize(message), CancellationToken.None);

    private static async Task<DemonstrationTransport> ConnectAsync(DemonstrationOptions options)
    {
        var transport = new DemonstrationTransport(Demonstration.Recording(), options);
        await transport.ConnectAsync(DemonstrationTransport.Endpoint, string.Empty, CancellationToken.None);
        return transport;
    }

    [Test]
    public async Task HelloIsAnsweredWithTheRecordedWelcomeAndSnapshotThenEveryEventInOrder()
    {
        using var transport = await ConnectAsync(Demonstration.Fast());
        await SendAsync(transport, new HelloMessage { Client = Samples.Client, Resume = null });

        var welcome = (WelcomeMessage)await ReceiveAsync(transport);
        Assert.That(welcome.Journal.Origin, Is.EqualTo(JournalOrigin.Fixture));
        Assert.That(welcome.Resumed, Is.False);
        var snapshot = (SnapshotMessage)await ReceiveAsync(transport);
        Assert.That(snapshot.Snapshot.Position, Is.EqualTo(welcome.Head));
        var positions = new List<long>();
        for (var index = 0; index < Repository.Trace().Length; index++)
        {
            positions.Add(((EventMessage)await ReceiveAsync(transport)).Position);
        }
        Assert.That(positions, Is.EqualTo(Enumerable.Range(1, positions.Count).Select(p => (long)p)));

        await SendAsync(transport, new PingMessage { Nonce = "still-there" });
        Assert.That(((PongMessage)await ReceiveAsync(transport)).Nonce, Is.EqualTo("still-there"));
    }

    [Test]
    public async Task EveryCommandIsRefusedInWordsAndNoneIsAccepted()
    {
        using var transport = await ConnectAsync(new DemonstrationOptions { Speed = 1, HoldAtEnd = Timeout.InfiniteTimeSpan });
        await SendAsync(transport, new HelloMessage { Client = Samples.Client, Resume = null });
        const string execution = "01a0dcf1-5a80-7000-8000-0000000000e1";
        var sent = new CommandEnvelope[]
        {
            commands.CreateProject("Anything"),
            commands.CreateWorkstream(Samples.ProjectId, "Anything", null),
            commands.StartExecution("01a0dcf1-5a80-7000-8000-0000000000b1", "mock", "Do it."),
            commands.SendInstruction(execution, "Keep going."),
            commands.RespondToApproval(execution, "approval-1", ApprovalDecision.Approve),
            commands.Interrupt(execution),
        };
        foreach (var command in sent) await SendAsync(transport, new CommandMessage { Command = command });

        var acks = new List<CommandAckMessage>();
        while (acks.Count < sent.Length)
        {
            if (await ReceiveAsync(transport) is CommandAckMessage ack) acks.Add(ack);
        }
        Assert.That(acks.Select(ack => ack.CommandId), Is.EqualTo(sent.Select(command => command.CommandId)));
        foreach (var ack in acks)
        {
            Assert.That(ack.Disposition, Is.EqualTo(CommandAckDisposition.Rejected));
            Assert.That(ack.Command!.Status, Is.EqualTo(CommandStatus.Rejected));
            Assert.That(ack.Command.Rejection!.Message, Is.EqualTo(DemonstrationTransport.Refusal));
            Assert.That(ack.Command.Result, Is.Null);
            Assert.That(WorkspacePresenter.Feedback(ack.Command).Text, Is.EqualTo("Refused: " + DemonstrationTransport.Refusal));
        }
        Assert.That(acks.Select(ack => ack.Command!.CommandType), Is.EqualTo(new[]
        {
            CommandType.ProjectCreate,
            CommandType.WorkstreamCreate,
            CommandType.ExecutionStart,
            CommandType.ExecutionSendInstruction,
            CommandType.ExecutionRespondToApproval,
            CommandType.ExecutionInterrupt,
        }));
        Assert.That(acks.Skip(3).Select(ack => ack.Command!.ExecutionId), Is.All.EqualTo(execution));
    }

    [Test]
    public async Task TheConnectionEndsAfterTheFinalStateHasHeld()
    {
        using var transport = await ConnectAsync(new DemonstrationOptions { Speed = 1000, HoldAtEnd = TimeSpan.FromSeconds(1) });
        await SendAsync(transport, new HelloMessage { Client = Samples.Client, Resume = null });
        for (var index = 0; index < Repository.Trace().Length + 2; index++) await ReceiveAsync(transport);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.That(await transport.ReceiveAsync(timeout.Token), Is.Null);
        Assert.That(transport.CloseDescription, Does.Contain("starts again"));
    }

    [Test]
    public async Task MalformedMessagesAreAnsweredWithoutEndingTheConnection()
    {
        using var transport = await ConnectAsync(Demonstration.Fast());
        await transport.SendAsync("not json", CancellationToken.None);
        var error = (ErrorMessage)await ReceiveAsync(transport);
        Assert.That(error.Error.Code, Is.EqualTo("invalid_message"));
        Assert.That(error.Fatal, Is.False);

        await SendAsync(transport, new HelloMessage { Client = Samples.Client, Resume = null });
        await SendAsync(transport, new HelloMessage { Client = Samples.Client, Resume = null });
        var answers = new List<ServerMessage>();
        for (var index = 0; index < Repository.Trace().Length + 3; index++) answers.Add(await ReceiveAsync(transport));
        Assert.That(answers.OfType<WelcomeMessage>().Count(), Is.EqualTo(1));
        Assert.That(answers.OfType<ErrorMessage>().Single().Error.Code, Is.EqualTo("unexpected_hello"));
    }

    [Test]
    public void ASpeedThatIsNotPositiveIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemonstrationTransport(Demonstration.Recording(), new DemonstrationOptions { Speed = 0 }));
    }
}

public class DemonstrationSessionTests
{
    private RealtimeSession? session;

    [TearDown]
    public async Task StopSession()
    {
        if (session != null) await session.StopAsync();
    }

    [Test]
    public async Task ASessionPlaysTheDemonstrationToItsEndWithEverythingLabeledAsRecorded()
    {
        session = DemonstrationTransport.CreateSession(Demonstration.Recording(), Samples.Client, Demonstration.Fast());
        session.Start();
        var seen = await Pumping.Until(session, s => s.State.Position == Repository.Trace().Length, "the demonstration has played");
        var activity = new ActivityLog();
        activity.Record(seen.Events);

        Assert.That(session.Status.IsLive, Is.True);
        Assert.That(session.State.Journal!.Origin, Is.EqualTo(JournalOrigin.Fixture));
        Assert.That(session.State.Runtimes, Is.Empty);
        var workstreams = session.State.Workstreams.Values.OrderBy(w => w.CreatedAt, StringComparer.Ordinal).ToList();
        Assert.That(workstreams.Select(w => w.Title), Is.EqualTo(new[]
        {
            "Add password reset", "Fix flaky checkout tests", "Move sessions to their own table",
        }));
        foreach (var workstream in workstreams)
        {
            var character = CharacterPresenter.Present(workstream, session.State, session.Status.IsLive);
            Assert.That(character.Recorded, Is.True, workstream.Title);
            Assert.That(character.Synthetic, Is.True, workstream.Title);
            Assert.That(character.Stale, Is.False, workstream.Title);
            Assert.That(character.Activity, Is.EqualTo(CharacterActivity.TurnFinished), workstream.Title);
            var workspace = WorkspacePresenter.Present(workstream, session.State, activity, session.Status.IsLive);
            Assert.That(workspace.Actions, Is.Empty, "a recording has no runtime to act on");
            Assert.That(workspace.Runtime, Is.Null);
        }
        Assert.That(CharacterPresenter.Present(workstreams[1], session.State, true).AttentionNotes.Single(), Does.StartWith("Tests failed"));

        // On the way it needed a person, and the recording's own operator answered.
        var approving = session.State.CurrentExecution(workstreams[2])!;
        var lines = activity.For(approving.ExecutionId).Select(entry => entry.Text).ToList();
        Assert.That(lines, Has.Some.StartWith("Approval requested to use"));
        Assert.That(lines, Has.Some.EqualTo("halcyonic-fixture-recorder asked to approve"));
        Assert.That(lines, Has.Some.EqualTo("Approved"));
    }

    [Test]
    public async Task EventsArriveAtTheRecordedPace()
    {
        // At ten times the recorded pace, the last event comes 1.16 s after the first.
        session = DemonstrationTransport.CreateSession(
            Demonstration.Recording(), Samples.Client, new DemonstrationOptions { Speed = 10, HoldAtEnd = Timeout.InfiniteTimeSpan });
        var clock = Stopwatch.StartNew();
        session.Start();
        await Pumping.Until(session, s => s.Status.IsLive, "the demonstration is live");
        Assert.That(session.State.Position, Is.LessThan(Repository.Trace().Length), "events are paced, not sent at once");
        await Pumping.Until(session, s => s.State.Position == Repository.Trace().Length, "the demonstration has played", TimeSpan.FromSeconds(10));
        Assert.That(clock.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public async Task ACommandIsRefusedInWordsAndChangesNothing()
    {
        session = DemonstrationTransport.CreateSession(Demonstration.Recording(), Samples.Client, Demonstration.Fast());
        session.Start();
        await Pumping.Until(session, s => s.State.Position == Repository.Trace().Length, "the demonstration has played");
        var execution = session.State.Executions.Keys.First();
        var command = new CommandFactory(Samples.Client).Interrupt(execution);

        var ack = await session.SubmitAsync(command);

        Assert.That(ack.Disposition, Is.EqualTo(CommandAckDisposition.Rejected));
        Assert.That(WorkspacePresenter.Feedback(ack.Command!).Text, Is.EqualTo("Refused: " + DemonstrationTransport.Refusal));
        Assert.That(session.State.Commands.ContainsKey(command.CommandId), Is.False, "nothing was journaled");
        Assert.That(session.State.Executions[execution].Status, Is.EqualTo(ExecutionStatus.Completed));
    }

    [Test]
    public async Task TheDemonstrationStartsAgainAfterHoldingItsFinalState()
    {
        var connections = 0;
        var recording = Demonstration.Recording();
        var options = new RealtimeSessionOptions(DemonstrationTransport.Endpoint, string.Empty, Samples.Client)
        {
            InitialRetryDelay = TimeSpan.FromMilliseconds(10),
            MaxRetryDelay = TimeSpan.FromMilliseconds(40),
        };
        session = new RealtimeSession(options, () =>
        {
            Interlocked.Increment(ref connections);
            return new DemonstrationTransport(recording, new DemonstrationOptions { Speed = 1000, HoldAtEnd = TimeSpan.FromSeconds(20) });
        });
        session.Start();

        var seen = await Pumping.Until(
            session,
            s => Volatile.Read(ref connections) >= 2 && s.Status.IsLive && s.State.Position == recording.Events.Count,
            "the demonstration has played twice");
        Assert.That(seen.Events.Count, Is.GreaterThan(recording.Events.Count), "the second play applied the events again");
        Assert.That(session.State.Workstreams, Has.Count.EqualTo(3));
    }
}

public class DemonstrationFallbackTests
{
    private readonly List<DemonstrationFallback> fallbacks = new();
    private FakeServer server = null!;

    [SetUp]
    public void CreateServer() => server = new FakeServer();

    [TearDown]
    public async Task StopAll()
    {
        foreach (var fallback in fallbacks) await fallback.StopAsync();
    }

    private RealtimeSession ControlPlane() =>
        new(
            new RealtimeSessionOptions(new Uri("ws://127.0.0.1:1/realtime"), "test-token", Samples.Client)
            {
                PingInterval = TimeSpan.FromHours(1),
                InitialRetryDelay = TimeSpan.FromMilliseconds(10),
                MaxRetryDelay = TimeSpan.FromMilliseconds(40),
            },
            server.CreateTransport);

    private DemonstrationFallback Start(RealtimeSession? controlPlane, Func<RealtimeSession?> demonstration)
    {
        var fallback = new DemonstrationFallback(controlPlane, demonstration);
        fallbacks.Add(fallback);
        fallback.Start();
        return fallback;
    }

    private static RealtimeSession PlayDemonstration() =>
        DemonstrationTransport.CreateSession(Demonstration.Recording(), Samples.Client, Demonstration.Fast());

    /// <summary>Pumps as a game loop would each frame; returns what each pump reported.</summary>
    private static async Task<List<StateChanges>> Until(DemonstrationFallback fallback, Func<bool> condition, string description)
    {
        var pumped = new List<StateChanges>();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            pumped.Add(fallback.Pump());
            if (condition()) return pumped;
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Timed out waiting until " + description + ". Showing: " + fallback.Current?.Status + ", reason " + fallback.Reason + ".");
            }
            await Task.Delay(5);
        }
    }

    [Test]
    public async Task WithNoControlPlaneTheDemonstrationIsShownAndSaysSo()
    {
        var fallback = Start(null, PlayDemonstration);

        Assert.That(fallback.Current, Is.SameAs(fallback.Demonstration));
        Assert.That(fallback.Reason, Is.EqualTo(DemonstrationReason.NotConfigured));
        Assert.That(fallback.Line, Is.EqualTo(
            "Demonstration: recorded work played on this device, not live.\nNothing done here reaches an agent."));
        await Until(fallback, () => fallback.Current!.State.Position == Repository.Trace().Length, "the demonstration has played");
        Assert.That(fallback.Current!.Status.IsLive, Is.True);
    }

    [Test]
    public async Task AnUnreachableControlPlaneGivesWayToTheDemonstrationAndIsTriedAgain()
    {
        server.ConnectFailure = new IOException("Connection refused");
        var controlPlane = ControlPlane();
        var fallback = Start(controlPlane, PlayDemonstration);
        Assert.That(fallback.Current, Is.SameAs(controlPlane), "the control plane is tried first");

        var pumped = await Until(fallback, () => fallback.Reason == DemonstrationReason.Unreachable, "the demonstration is shown");
        Assert.That(pumped[^1].Resynchronized && pumped[^1].ConnectionChanged, Is.True, "consumers redraw everything");
        Assert.That(fallback.Current, Is.SameAs(fallback.Demonstration));
        Assert.That(fallback.Line, Does.Contain("not live").And.Contain("The control plane is not reachable; trying again."));

        await Until(fallback, () => server.Attempts >= 3 && fallback.Line!.Contains("Connection refused"), "the control plane is tried again");
        Assert.That(fallback.Current, Is.SameAs(fallback.Demonstration));
    }

    [Test]
    public async Task OnceTheControlPlaneIsLiveItIsShownAndTheDemonstrationNeverReturns()
    {
        server.ConnectFailure = new IOException("Connection refused");
        var controlPlane = ControlPlane();
        var fallback = Start(controlPlane, PlayDemonstration);
        await Until(fallback, () => fallback.Demonstration?.State.Position == Repository.Trace().Length, "the demonstration has played");

        server.ConnectFailure = null;
        var connection = await server.AcceptAsync();
        await connection.WelcomeWithSnapshotAsync(Samples.Snapshot(3, new[] { Samples.Workstream("w1") }));
        var pumped = await Until(fallback, () => fallback.Current == controlPlane, "the control plane is shown");

        Assert.That(pumped[^1].Resynchronized && pumped[^1].ConnectionChanged, Is.True, "consumers redraw everything");
        Assert.That(fallback.Reason, Is.Null);
        Assert.That(fallback.Line, Is.Null);
        Assert.That(fallback.Demonstration, Is.Null);
        Assert.That(controlPlane.State.Workstreams.Keys, Is.EquivalentTo(new[] { "w1" }));

        connection.Close("stopped");
        await Until(fallback, () => controlPlane.Status.Phase == ConnectionPhase.WaitingToRetry, "the control plane is lost");
        for (var frame = 0; frame < 10; frame++)
        {
            fallback.Pump();
            await Task.Delay(5);
        }
        Assert.That(fallback.Current, Is.SameAs(controlPlane), "a control plane that was live shows its last known state");
        Assert.That(fallback.Reason, Is.Null);
    }

    [Test]
    public async Task AReachableControlPlaneNeverStartsTheDemonstration()
    {
        var created = 0;
        var controlPlane = ControlPlane();
        var fallback = Start(controlPlane, () =>
        {
            created++;
            return PlayDemonstration();
        });

        var connection = await server.AcceptAsync();
        await connection.WelcomeWithSnapshotAsync(Samples.Snapshot(1, new[] { Samples.Workstream("w1") }));
        await Until(fallback, () => controlPlane.Status.IsLive, "the control plane is live");

        Assert.That(fallback.Current, Is.SameAs(controlPlane));
        Assert.That(fallback.Reason, Is.Null);
        Assert.That(created, Is.Zero);
    }

    [Test]
    public void WithNothingToShowThereIsNoSession()
    {
        var fallback = Start(null, () => null);

        Assert.That(fallback.Current, Is.Null);
        Assert.That(fallback.Reason, Is.Null);
        Assert.That(fallback.Pump().IsEmpty, Is.True);
    }

    [Test]
    public async Task PausingStopsTheDemonstrationAndResumingPlaysItAgain()
    {
        var fallback = Start(null, PlayDemonstration);
        await Until(fallback, () => fallback.Current!.Status.IsLive, "the demonstration is live");

        await fallback.SetPausedAsync(true);
        await Until(fallback, () => fallback.Current!.Status.Phase == ConnectionPhase.Stopped, "the demonstration stops");
        await fallback.SetPausedAsync(false);
        await Until(fallback, () => fallback.Current!.Status.IsLive, "the demonstration plays again");
    }

    [Test]
    public void TheLineNamesWhyTheControlPlaneIsNotShown()
    {
        var waiting = DemonstrationFallback.Describe(
            DemonstrationReason.Unreachable, new ConnectionStatus(ConnectionPhase.WaitingToRetry, "The control plane did not answer within 10 s."));
        Assert.That(waiting, Does.EndWith("The control plane is not reachable; trying again. The control plane did not answer within 10 s."));

        var refused = DemonstrationFallback.Describe(
            DemonstrationReason.Unreachable, new ConnectionStatus(ConnectionPhase.Refused, "The control plane speaks realtime protocol 2."));
        Assert.That(refused, Does.EndWith("The control plane refused this client. The control plane speaks realtime protocol 2."));

        foreach (var line in new[] { waiting, refused, DemonstrationFallback.Describe(DemonstrationReason.NotConfigured, null) })
        {
            Assert.That(line, Does.StartWith("Demonstration: recorded work played on this device, not live.\nNothing done here reaches an agent."));
        }
    }
}

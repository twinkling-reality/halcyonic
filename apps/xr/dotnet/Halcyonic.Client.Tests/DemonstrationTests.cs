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
    public const string Directed = "Add rate limiting to the sign-in endpoint";

    private static readonly Lazy<DemonstrationRecording> Bundled = new(() => DemonstrationRecording.Parse(Text()));

    /// <summary>The demonstration the Unity project bundles, as `pnpm demonstration:record` wrote it.</summary>
    public static string Text() => File.ReadAllText(Repository.PathTo(Path));

    public static DemonstrationRecording Recording() => Bundled.Value;

    /// <summary>Plays fast, and holds wherever the recording times a hold, until an answer.</summary>
    public static DemonstrationOptions Fast() => new() { Speed = 1000, Hold = Timeout.InfiniteTimeSpan };

    public static string Edit(Action<JObject> edit)
    {
        var document = (JObject)Json.Parse(Text());
        edit(document);
        return document.ToString(Formatting.None);
    }

    /// <summary>The answer the beginning offers once it holds at the approval.</summary>
    public static DemonstrationBranch AtApproval(DemonstrationAnswerKind kind)
    {
        var beginning = Recording().Nodes[0];
        return beginning.BranchesAfter(beginning.Events.Count).Single(branch => branch.Answer.Kind == kind);
    }

    /// <summary>The recorded instructions offered once the approved turn has ended.</summary>
    public static IReadOnlyList<DemonstrationBranch> InstructionsAfterApproving()
    {
        var approved = Recording().Nodes[AtApproval(DemonstrationAnswerKind.Approve).Node];
        return approved.BranchesAfter(approved.Events.Count);
    }

    public static WorkstreamView DirectedWorkstream(RealtimeSession session) =>
        session.State.Workstreams.Values.Single(workstream => workstream.Title == Directed);

    /// <summary>The directed work's execution, once the session holds its workstream and it has started.</summary>
    public static ExecutionView? DirectedExecution(RealtimeSession session)
    {
        var workstream = session.State.Workstreams.Values.SingleOrDefault(candidate => candidate.Title == Directed);
        return workstream == null ? null : session.State.CurrentExecution(workstream);
    }
}

public class DemonstrationRecordingTests
{
    [Test]
    public void TheBundledDemonstrationReadsAndEveryMessageRoundTrips()
    {
        var document = (JObject)Json.Parse(Demonstration.Text());
        Json.AssertRoundTrips<ServerMessage>(document["welcome"]!.ToString(Formatting.None));
        Json.AssertRoundTrips<ServerMessage>(document["snapshot"]!.ToString(Formatting.None));
        var messages = 0;
        foreach (var node in (JArray)document["nodes"]!)
        {
            foreach (var entry in (JArray)node["events"]!)
            {
                Json.AssertRoundTrips<ServerMessage>(entry["message"]!.ToString(Formatting.None));
                messages++;
            }
            if (node["ending"]!["snapshot"]!.Type == JTokenType.Object)
            {
                Json.AssertRoundTrips<ServerMessage>(node["ending"]!["snapshot"]!.ToString(Formatting.None));
            }
        }

        var recording = Demonstration.Recording();
        Assert.That(recording.Source, Is.EqualTo("apps/control-plane/src/fixtures/demonstration.ts"));
        Assert.That(recording.Nodes.Sum(node => node.Events.Count), Is.EqualTo(messages));
        Assert.That(recording.Welcome.Journal.Origin, Is.EqualTo(JournalOrigin.Fixture));
        var beginning = recording.Snapshot.Snapshot;
        Assert.That(beginning.Workstreams.Select(w => (w.Title, w.Status)), Is.EquivalentTo(new[]
        {
            ("Paginate the order history endpoint", WorkstreamStatus.Created),
            (Demonstration.Directed, WorkstreamStatus.Created),
            ("Send an order confirmation email", WorkstreamStatus.Created),
        }), "before anything starts, the workstreams exist, so playing it again keeps its characters");
        Assert.That(beginning.Runtimes.Select(runtime => (runtime.DisplayName, runtime.Synthetic)), Is.EqualTo(new[]
        {
            ("Simulated agent (demonstration)", true),
            ("Simulated agent (demonstration, watch only)", true),
        }));
    }

    [Test]
    public void ItTakesLessThanASecondToRead()
    {
        var text = Demonstration.Text();
        DemonstrationRecording.Parse(text);
        var clock = Stopwatch.StartNew();
        DemonstrationRecording.Parse(text);
        TestContext.Out.WriteLine("Read " + text.Length / 1024 + " KiB in " + clock.ElapsedMilliseconds + " ms.");
        Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
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
        JArray Nodes(JObject document) => (JArray)document["nodes"]!;
        JArray EventsOf(JObject document, int node) => (JArray)Nodes(document)[node]["events"]!;
        JArray AnswersOf(JObject document, int node) => (JArray)Nodes(document)[node]["answers"]!;
        var cases = new Dictionary<string, string>
        {
            ["reordered"] = Demonstration.Edit(document =>
            {
                var events = EventsOf(document, 0);
                var first = events[0];
                events.RemoveAt(0);
                events.Add(first);
            }),
            ["another journal"] = Demonstration.Edit(document =>
                document["snapshot"]!["snapshot"]!["journal"]!["journal_id"] = "01a0dcf1-5a80-7000-8000-000000000009"),
            ["a newer format"] = Demonstration.Edit(document => document["version"] = 3),
            ["no nodes"] = Demonstration.Edit(document => document.Remove("nodes")),
            ["inside an instant"] = Demonstration.Edit(document =>
            {
                // The directed start is one instant of several events.
                var events = EventsOf(document, 0);
                var inside = Enumerable.Range(1, events.Count - 1).First(i => (long)events[i]["at_ms"]! == (long)events[i - 1]["at_ms"]!);
                AnswersOf(document, 0)[0]["after"] = inside;
            }),
            ["an answer's node twice"] = Demonstration.Edit(document => AnswersOf(document, 0)[1]["node"] = AnswersOf(document, 0)[0]["node"]),
            ["an earlier node"] = Demonstration.Edit(document =>
            {
                var answers = AnswersOf(document, 1);
                answers.Add(new JObject { ["after"] = 1, ["node"] = 0, ["answer"] = AnswersOf(document, 0)[0]["answer"]!.DeepClone() });
            }),
            ["an ending from another journal"] = Demonstration.Edit(document =>
            {
                var ending = Nodes(document).First(node => node["ending"]!["snapshot"]!.Type == JTokenType.Object);
                ending["ending"]!["snapshot"]!["snapshot"]!["journal"]!["journal_id"] = "01a0dcf1-5a80-7000-8000-000000000009";
            }),
            ["an unknown answer"] = Demonstration.Edit(document => AnswersOf(document, 0)[0]["answer"]!["kind"] = "undo"),
            ["not json"] = "not json",
            ["not a document"] = "[]",
        };
        foreach (var (name, text) in cases)
        {
            Assert.Throws<InvalidDataException>(() => DemonstrationRecording.Parse(text), name);
        }
    }

    /// <summary>
    /// The heart of the scripted demonstration: at every point where a person could act, the
    /// workspace offers exactly the actions the recording holds a continuation for, computed as it
    /// is live, from the recorded runtimes' declared capabilities and each execution's status.
    /// </summary>
    [Test]
    public void TheWorkspaceOffersExactlyTheActionsTheRecordingHoldsAnAnswerFor()
    {
        var recording = Demonstration.Recording();
        var checkedAnswers = 0;
        var boundaries = 0;
        Walk(recording, 0, new List<EventMessage>(), (index, before) =>
        {
            var node = recording.Nodes[index];
            var state = new ClientProjection();
            state.ApplyWelcome(recording.Welcome);
            state.ApplySnapshot(recording.Snapshot.Snapshot, new StateChanges());
            foreach (var message in before) Assert.That(state.ApplyEvent(message, new StateChanges()), Is.True);
            for (var played = 1; played <= node.Events.Count; played++)
            {
                Assert.That(state.ApplyEvent(node.Events[played - 1].Message, new StateChanges()), Is.True);
                if (played < node.Events.Count && node.Events[played].At == node.Events[played - 1].At) continue;
                if (played == node.Events.Count && node.EndingSnapshot != null)
                {
                    state.ApplySnapshot(node.EndingSnapshot.Snapshot, new StateChanges());
                }
                var offered = node.BranchesAfter(played);
                foreach (var workstream in state.Workstreams.Values)
                {
                    var workspace = WorkspacePresenter.Present(workstream, state, new ActivityLog(), live: true);
                    var recorded = offered
                        .Where(branch => branch.Answer.ExecutionId == workspace.Execution?.ExecutionId)
                        .ToList();
                    Assert.That(workspace.Actions, Is.EquivalentTo(recorded.Select(branch => ActionOf(branch.Answer.Kind)).Distinct()),
                        "node " + index + " after " + played + ", " + workstream.Title);
                    foreach (var branch in recorded.Where(branch => branch.Answer.ApprovalId != null))
                    {
                        Assert.That(branch.Answer.ApprovalId, Is.EqualTo(workspace.ApprovalToAnswer?.ApprovalId), "the approval the workspace answers");
                    }
                    checkedAnswers += recorded.Count;
                }
                boundaries++;
            }
        });
        Assert.That(checkedAnswers, Is.EqualTo(recording.Nodes.Sum(node => node.Branches.Count)), "every answer is where a workspace offers it");
        Assert.That(boundaries, Is.GreaterThan(checkedAnswers));
    }

    private static WorkspaceAction ActionOf(DemonstrationAnswerKind kind) => kind switch
    {
        DemonstrationAnswerKind.Approve => WorkspaceAction.Approve,
        DemonstrationAnswerKind.Deny => WorkspaceAction.Deny,
        DemonstrationAnswerKind.Interrupt => WorkspaceAction.Interrupt,
        _ => WorkspaceAction.Instruct,
    };

    /// <summary>Visits every node with the events played before it on its way from the beginning.</summary>
    private static void Walk(DemonstrationRecording recording, int index, List<EventMessage> before, Action<int, List<EventMessage>> visit)
    {
        visit(index, before);
        var node = recording.Nodes[index];
        foreach (var branch in node.Branches)
        {
            Walk(recording, branch.Node, before.Concat(node.Events.Take(branch.After).Select(e => e.Message)).ToList(), visit);
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
        Assert.That(text, Is.Not.Null, "the connection ended");
        return Json.AssertRoundTrips<ServerMessage>(text!);
    }

    private static async Task<List<ServerMessage>> ReceiveAsync(DemonstrationTransport transport, int count)
    {
        var received = new List<ServerMessage>();
        for (var index = 0; index < count; index++) received.Add(await ReceiveAsync(transport));
        return received;
    }

    /// <summary>Whether anything more arrives soon; the recording holds while nothing does.</summary>
    private static async Task<bool> SilentAsync(DemonstrationTransport transport)
    {
        using var shortly = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        try
        {
            await transport.ReceiveAsync(shortly.Token);
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }

    private static Task SendAsync(DemonstrationTransport transport, ClientMessage message) =>
        transport.SendAsync(HalcyonicJson.Serialize(message), CancellationToken.None);

    private static Task SendAsync(DemonstrationTransport transport, CommandEnvelope command) =>
        SendAsync(transport, new CommandMessage { Command = command });

    private static async Task<DemonstrationTransport> PlayToTheApprovalAsync(DemonstrationOptions options)
    {
        var transport = new DemonstrationTransport(Demonstration.Recording(), options);
        await transport.ConnectAsync(DemonstrationTransport.Endpoint, string.Empty, CancellationToken.None);
        await SendAsync(transport, new HelloMessage { Client = Samples.Client, Resume = null });
        await ReceiveAsync(transport, 2 + Demonstration.Recording().Nodes[0].Events.Count);
        return transport;
    }

    private static CommandAckMessage AssertAnsweredInWords(ServerMessage message, CommandEnvelope command, string words)
    {
        var ack = (CommandAckMessage)message;
        Assert.That(ack.CommandId, Is.EqualTo(command.CommandId));
        Assert.That(ack.Disposition, Is.EqualTo(CommandAckDisposition.Rejected), "never accepted");
        Assert.That(ack.Command!.Status, Is.EqualTo(CommandStatus.Rejected));
        Assert.That(ack.Command.Result, Is.Null);
        Assert.That(ack.Command.Rejection!.Code, Is.EqualTo(RejectionCode.Demonstration));
        Assert.That(ack.Command.Rejection.Message, Is.EqualTo(words));
        Assert.That(words, Does.StartWith("Not sent to any agent"));
        return ack;
    }

    [Test]
    public async Task HelloIsAnsweredWithTheWelcomeAndTheBeginningThenTheEventsUntilTheApprovalHolds()
    {
        var recording = Demonstration.Recording();
        var transport = new DemonstrationTransport(recording, Demonstration.Fast());
        using var _ = transport;
        await SendAsync(transport, new HelloMessage { Client = Samples.Client, Resume = null });

        var welcome = (WelcomeMessage)await ReceiveAsync(transport);
        Assert.That(welcome.Journal.Origin, Is.EqualTo(JournalOrigin.Fixture));
        Assert.That(welcome.Resumed, Is.False);
        var snapshot = (SnapshotMessage)await ReceiveAsync(transport);
        Assert.That(snapshot.Snapshot.Position, Is.EqualTo(welcome.Head));
        var events = (await ReceiveAsync(transport, recording.Nodes[0].Events.Count)).Cast<EventMessage>().ToList();
        Assert.That(events.Select(e => e.Position), Is.EqualTo(Enumerable.Range(1, events.Count).Select(p => welcome.Head + p)));
        Assert.That(events.Last().Event, Is.InstanceOf<RuntimeApprovalRequestedEvent>());
        Assert.That(await SilentAsync(transport), Is.True, "the recording holds at the approval until someone answers");

        await SendAsync(transport, new PingMessage { Nonce = "still-there" });
        Assert.That(((PongMessage)await ReceiveAsync(transport)).Nonce, Is.EqualTo("still-there"));
    }

    [Test]
    public async Task AnApprovalIsAnsweredInWordsAndTheRecordingContinuesWithTheApprovalItRecorded()
    {
        using var transport = await PlayToTheApprovalAsync(Demonstration.Fast());
        var approve = Demonstration.AtApproval(DemonstrationAnswerKind.Approve);
        var command = commands.RespondToApproval(approve.Answer.ExecutionId, approve.Answer.ApprovalId!, ApprovalDecision.Approve);

        await SendAsync(transport, command);

        AssertAnsweredInWords(await ReceiveAsync(transport), command, DemonstrationTransport.Answered(approve.Answer));
        var continuation = Demonstration.Recording().Nodes[approve.Node];
        var events = (await ReceiveAsync(transport, continuation.Events.Count)).Cast<EventMessage>().ToList();
        var accepted = (CommandAcceptedEvent)events[0].Event;
        Assert.That(accepted.Payload.Command.Client.Name, Is.EqualTo("demonstration recorder"), "the recording's own command, not the person's");
        Assert.That(accepted.Payload.Command.CommandId, Is.Not.EqualTo(command.CommandId));
        Assert.That(events.Select(e => e.Event).OfType<RuntimeApprovalResolvedEvent>().Single().Payload.Decision, Is.EqualTo(ApprovalResolution.Approved));
        Assert.That(await SilentAsync(transport), Is.True, "the turn has ended and the recording offers its instructions");
    }

    [Test]
    public async Task EachAnswerSaysWhatTheRecordingContinuesWith()
    {
        var deny = Demonstration.AtApproval(DemonstrationAnswerKind.Deny);
        var interrupt = Demonstration.AtApproval(DemonstrationAnswerKind.Interrupt);
        Assert.That(DemonstrationTransport.Answered(deny.Answer), Is.EqualTo("Not sent to any agent; the recording continues as recorded for denying."));
        Assert.That(DemonstrationTransport.Answered(interrupt.Answer), Is.EqualTo("Not sent to any agent; the recording continues as recorded for stopping the turn."));
        Assert.That(DemonstrationTransport.Answered(Demonstration.AtApproval(DemonstrationAnswerKind.Approve).Answer),
            Is.EqualTo("Not sent to any agent; the recording continues as recorded for approving."));
        var instruction = Demonstration.InstructionsAfterApproving()[0].Answer;
        Assert.That(DemonstrationTransport.Answered(instruction), Is.EqualTo("Not sent to any agent; the recording continues as recorded for “Count per account too”."));
        Assert.That(DemonstrationTransport.AnsweredInstead(instruction),
            Is.EqualTo("Not sent to any agent; the recording holds only its own instructions, so it continues as recorded for “Count per account too”."));

        using var transport = await PlayToTheApprovalAsync(Demonstration.Fast());
        var denial = commands.RespondToApproval(deny.Answer.ExecutionId, deny.Answer.ApprovalId!, ApprovalDecision.Deny, "Not now.");
        await SendAsync(transport, denial);
        AssertAnsweredInWords(await ReceiveAsync(transport), denial, DemonstrationTransport.Answered(deny.Answer));
        var denied = Demonstration.Recording().Nodes[deny.Node];
        var events = (await ReceiveAsync(transport, denied.Events.Count)).Cast<EventMessage>().ToList();
        Assert.That(events.Select(e => e.Event).OfType<RuntimeApprovalResolvedEvent>().Single().Payload.Decision, Is.EqualTo(ApprovalResolution.Denied));
    }

    [Test]
    public async Task ACommandTheRecordingHoldsNoAnswerForChangesNothing()
    {
        using var transport = await PlayToTheApprovalAsync(Demonstration.Fast());
        var approve = Demonstration.AtApproval(DemonstrationAnswerKind.Approve);
        var sent = new CommandEnvelope[]
        {
            commands.CreateProject("Anything"),
            commands.RespondToApproval(approve.Answer.ExecutionId, "approval-that-is-not-pending", ApprovalDecision.Approve),
            commands.SendInstruction(approve.Answer.ExecutionId, "Keep going."),
            commands.Interrupt("01a0dcf1-5a80-7000-8000-0000000000e1"),
        };
        foreach (var command in sent)
        {
            await SendAsync(transport, command);
            AssertAnsweredInWords(await ReceiveAsync(transport), command, DemonstrationTransport.NothingRecorded);
        }
        Assert.That(await SilentAsync(transport), Is.True, "still holding at the approval");
    }

    [Test]
    public async Task AtItsEndTheRecordingHoldsWithoutARuntimeThenStartsAgainWithoutEndingTheConnection()
    {
        var recording = Demonstration.Recording();
        using var transport = await PlayToTheApprovalAsync(new DemonstrationOptions { Speed = 1000, Hold = TimeSpan.FromSeconds(1) });
        var interrupt = Demonstration.AtApproval(DemonstrationAnswerKind.Interrupt);
        var command = commands.Interrupt(interrupt.Answer.ExecutionId);

        await SendAsync(transport, command);

        AssertAnsweredInWords(await ReceiveAsync(transport), command, DemonstrationTransport.Answered(interrupt.Answer));
        var stopped = recording.Nodes[interrupt.Node];
        await ReceiveAsync(transport, stopped.Events.Count);
        var ending = (SnapshotMessage)await ReceiveAsync(transport);
        Assert.That(ending.Snapshot.Runtimes, Is.Empty, "a finished recording offers no action");
        Assert.That(ending.Snapshot.Position, Is.EqualTo(stopped.Events.Last().Message.Position));
        var again = (SnapshotMessage)await ReceiveAsync(transport);
        Assert.That(JToken.DeepEquals(JToken.FromObject(again, JsonSerializer.Create(HalcyonicJson.Strict)),
            JToken.FromObject(recording.Snapshot, JsonSerializer.Create(HalcyonicJson.Strict))), Is.True, "it starts again from the beginning");
        var replayed = (await ReceiveAsync(transport, recording.Nodes[0].Events.Count)).Cast<EventMessage>();
        Assert.That(replayed.Last().Event, Is.InstanceOf<RuntimeApprovalRequestedEvent>());
    }

    [Test]
    public async Task MalformedMessagesAreAnsweredWithoutEndingTheConnection()
    {
        using var transport = new DemonstrationTransport(Demonstration.Recording(), Demonstration.Fast());
        await transport.SendAsync("not json", CancellationToken.None);
        var error = (ErrorMessage)await ReceiveAsync(transport);
        Assert.That(error.Error.Code, Is.EqualTo("invalid_message"));
        Assert.That(error.Fatal, Is.False);

        await SendAsync(transport, new HelloMessage { Client = Samples.Client, Resume = null });
        await SendAsync(transport, new HelloMessage { Client = Samples.Client, Resume = null });
        var answers = await ReceiveAsync(transport, Demonstration.Recording().Nodes[0].Events.Count + 3);
        Assert.That(answers.OfType<WelcomeMessage>().Count(), Is.EqualTo(1));
        Assert.That(answers.OfType<ErrorMessage>().Single().Error.Code, Is.EqualTo("unexpected_hello"));
    }

    [Test]
    public void ASpeedThatIsNotPositiveIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemonstrationTransport(Demonstration.Recording(), new DemonstrationOptions { Speed = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemonstrationPlayer(Demonstration.Recording(), Samples.Client, new DemonstrationOptions { Speed = -1 }));
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

    private DemonstrationPlayer Play(DemonstrationOptions options)
    {
        var player = new DemonstrationPlayer(Demonstration.Recording(), Samples.Client, options);
        session = player.Session;
        session.Start();
        return player;
    }

    /// <summary>
    /// A judge's first minutes: the watched work finishes, the directed work asks for approval, the
    /// judge approves through the workspace, sees the tests fail, gives a recorded instruction and
    /// sees them pass. Every command is answered in words, none is reported accepted or done, and
    /// everything reads as recorded and simulated.
    /// </summary>
    [Test]
    public async Task AJudgeApprovesSeesTheWorkContinueThenInstructsAndSeesTheTestsPass()
    {
        var player = Play(Demonstration.Fast());
        var activity = new ActivityLog();
        var submissions = new CommandSubmissions();
        var steering = new WorkspaceSteering(new CommandFactory(Samples.Client));
        var seen = await Pumping.Until(session!, s => Demonstration.DirectedExecution(s)?.Status == ExecutionStatus.WaitingForHuman, "the directed work needs a person");
        activity.Record(seen.Events);
        var phases = new List<ConnectionPhase>();

        var directed = Demonstration.DirectedWorkstream(session!);
        var workspace = WorkspacePresenter.Present(directed, session!.State, activity, session.Status.IsLive, submissions);
        Assert.That(workspace.Actions, Is.EquivalentTo(new[] { WorkspaceAction.Approve, WorkspaceAction.Deny, WorkspaceAction.Interrupt }));
        Assert.That(workspace.Character.Recorded && workspace.Character.Synthetic && !workspace.Character.Stale, Is.True);
        Assert.That(WorkspaceText.RunDetails(workspace, TimeZoneInfo.Utc)[0].Line, Does.StartWith("Practice run: builds nothing"));
        Assert.That(WorkspaceText.Attention(workspace).Single(), Does.StartWith("It wants to run: Run make migrate"));
        foreach (var watched in session.State.Workstreams.Values.Where(w => w.Title != Demonstration.Directed))
        {
            var beside = WorkspacePresenter.Present(watched, session.State, activity, true, submissions);
            Assert.That(beside.Actions, Is.Empty, watched.Title);
            Assert.That(beside.Character.Activity, Is.EqualTo(CharacterActivity.TurnFinished), watched.Title);
            Assert.That(WorkspaceText.RunDetails(beside, TimeZoneInfo.Utc)[0].Line, Does.StartWith("Practice run: builds nothing"));
            Assert.That(beside.Character.Recorded, Is.True, "watched work reads as recorded");
        }

        // Approving needs the deliberate second press, as it does live, once the whole request has shown.
        Assert.That(steering.Press(WorkspaceAction.Approve, workspace).Step, Is.EqualTo(SteeringStep.Confirm));
        Assert.That(steering.Request(workspace), Does.StartWith("shell: Run make migrate"));
        steering.RequestShown(1, 1);
        var approving = steering.Confirm(workspace);
        Assert.That(approving.Step, Is.EqualTo(SteeringStep.Send));
        await submissions.SubmitAsync(c => session.SubmitAsync(c), approving.Command!, workspace.Execution!.ExecutionId);
        var answer = submissions.FeedbackFor(workspace.Execution.ExecutionId, session.State, 5).First();
        Assert.That(answer.Text, Is.EqualTo("Not sent to any agent; the recording continues as recorded for approving."));
        Assert.That(answer.Status, Is.EqualTo(CommandStatus.Rejected));

        seen = await Pumping.Until(session, s =>
        {
            phases.Add(s.Status.Phase);
            return Demonstration.DirectedExecution(s)?.Status == ExecutionStatus.Completed;
        }, "the approved turn ends");
        activity.Record(seen.Events);
        var execution = Demonstration.DirectedExecution(session)!;
        var lines = activity.For(execution.ExecutionId).Select(entry => entry.Text).ToList();
        Assert.That(lines, Has.Some.EqualTo("Asked to approve"));
        Assert.That(lines, Has.Some.EqualTo("Approved"));
        Assert.That(lines, Has.Some.EqualTo("shell: make migrate"));
        Assert.That(lines, Has.Some.EqualTo("Tests failed: 1 failed, 23 passed"));
        Assert.That(execution.LastTestRun!.Outcome, Is.EqualTo(TestOutcome.Failed));

        workspace = WorkspacePresenter.Present(Demonstration.DirectedWorkstream(session), session.State, activity, session.Status.IsLive, submissions);
        Assert.That(workspace.Actions, Is.EquivalentTo(new[] { WorkspaceAction.Instruct }));
        var presets = player.InstructionsFor(execution.ExecutionId);
        Assert.That(presets.Select(preset => preset.Label), Is.EqualTo(new[] { "Count per account too", "Change the test instead" }));
        Assert.That(player.InstructionsFor("01a0dcf1-5a80-7000-8000-0000000000e1"), Is.Empty);

        Assert.That(steering.Press(WorkspaceAction.Instruct, workspace).Step, Is.EqualTo(SteeringStep.Type));
        var instructing = steering.Typed(presets[0].Text, workspace);
        Assert.That(instructing.Step, Is.EqualTo(SteeringStep.Send), "an instruction needs no second press");
        await submissions.SubmitAsync(c => session.SubmitAsync(c), instructing.Command!, execution.ExecutionId);
        Assert.That(submissions.FeedbackFor(execution.ExecutionId, session.State, 5).First().Text,
            Is.EqualTo("Not sent to any agent; the recording continues as recorded for “Count per account too”."));

        seen = await Pumping.Until(session, s =>
        {
            phases.Add(s.Status.Phase);
            return s.State.Runtimes.Count == 0;
        }, "the recording ends");
        activity.Record(seen.Events);
        execution = Demonstration.DirectedExecution(session)!;
        Assert.That(execution.LastTestRun!.Outcome, Is.EqualTo(TestOutcome.Passed));
        Assert.That(execution.Status, Is.EqualTo(ExecutionStatus.Completed));
        Assert.That(player.Ended, Is.True);
        workspace = WorkspacePresenter.Present(Demonstration.DirectedWorkstream(session), session.State, activity, session.Status.IsLive, submissions);
        Assert.That(workspace.Actions, Is.Empty);
        Assert.That(WorkspaceText.WhyNoActions(workspace), Is.EqualTo("Nothing can be sent: it isn't available on your Mac now."));
        Assert.That(session.State.Commands.Values.Where(command => command.ExecutionId == execution.ExecutionId).Select(command => command.Status),
            Is.All.EqualTo(CommandStatus.Completed), "only the recording's own commands, confirmed by its runtime");
        Assert.That(phases, Is.All.EqualTo(ConnectionPhase.Live), "never disconnected");
    }

    [Test]
    public async Task TypedTextIsAnsweredWithARecordedInstructionAndSaysSo()
    {
        var player = Play(Demonstration.Fast());
        await Pumping.Until(session!, s => Demonstration.DirectedExecution(s)?.Status == ExecutionStatus.WaitingForHuman, "the directed work needs a person");
        var approve = Demonstration.AtApproval(DemonstrationAnswerKind.Approve);
        var factory = new CommandFactory(Samples.Client);
        await session!.SubmitAsync(factory.RespondToApproval(approve.Answer.ExecutionId, approve.Answer.ApprovalId!, ApprovalDecision.Approve));
        await Pumping.Until(session, s => player.InstructionsFor(approve.Answer.ExecutionId).Count > 0, "the recording offers its instructions");

        var ack = await session.SubmitAsync(factory.SendInstruction(approve.Answer.ExecutionId, "Please make every test pass."));

        var first = Demonstration.InstructionsAfterApproving()[0].Answer;
        Assert.That(WorkspacePresenter.Feedback(ack.Command!).Text, Is.EqualTo(DemonstrationTransport.AnsweredInstead(first)));
        await Pumping.Until(session, s => s.State.Runtimes.Count == 0, "the recording continues with its first instruction and ends");
        Assert.That(Demonstration.DirectedExecution(session)!.LastTestRun!.Outcome, Is.EqualTo(TestOutcome.Passed));

        // Letter case and spacing do not matter to a recorded instruction.
        Assert.That(DemonstrationTransport.Answered(first), Does.Contain(first.Label));
    }

    [Test]
    public async Task StartingAgainRewindsTheSameJournalWithoutADisconnect()
    {
        var player = Play(new DemonstrationOptions { Speed = 1000, Hold = TimeSpan.FromSeconds(1) });
        await Pumping.Until(session!, s => Demonstration.DirectedExecution(s)?.Status == ExecutionStatus.WaitingForHuman, "the directed work needs a person");
        var journal = session!.State.Journal!.JournalId;
        var interrupt = Demonstration.AtApproval(DemonstrationAnswerKind.Interrupt);
        await session.SubmitAsync(new CommandFactory(Samples.Client).Interrupt(interrupt.Answer.ExecutionId));

        // Frame by frame, as a game loop pumps: the recording ends, holds, and starts again.
        var rewound = false;
        var phases = new List<ConnectionPhase>();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!rewound)
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("The demonstration did not start again. Plays: " + player.Plays + ".");
            rewound = session.Pump().Rewound;
            phases.Add(session.Status.Phase);
            await Task.Delay(1);
        }

        Assert.That(player.Plays, Is.EqualTo(2));
        Assert.That(session.State.Journal!.JournalId, Is.EqualTo(journal), "the same journal, back at its beginning");
        Assert.That(session.State.Runtimes, Has.Count.EqualTo(2), "the runtimes are back for the new play");
        Assert.That(phases, Is.All.EqualTo(ConnectionPhase.Live), "never disconnected");
    }

    [Test]
    public async Task EventsArriveAtTheRecordedPace()
    {
        // At ten times the recorded pace, the approval comes 0.89 s after the beginning.
        Play(new DemonstrationOptions { Speed = 10, Hold = Timeout.InfiniteTimeSpan });
        var clock = Stopwatch.StartNew();
        await Pumping.Until(session!, s => s.Status.IsLive, "the demonstration is live");
        Assert.That(Demonstration.DirectedExecution(session!), Is.Null, "events are paced, not sent at once");
        await Pumping.Until(session!, s => Demonstration.DirectedExecution(s)?.Status == ExecutionStatus.WaitingForHuman, "the approval", TimeSpan.FromSeconds(10));
        Assert.That(clock.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(0.8)));
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

    private DemonstrationFallback Start(RealtimeSession? controlPlane, Func<DemonstrationPlayer?> demonstration)
    {
        var fallback = new DemonstrationFallback(controlPlane, demonstration);
        fallbacks.Add(fallback);
        fallback.Start();
        return fallback;
    }

    private static DemonstrationPlayer PlayDemonstration() =>
        new(Demonstration.Recording(), Samples.Client, Demonstration.Fast());

    private static bool AtTheApproval(DemonstrationFallback fallback) =>
        fallback.Current != null && fallback.Current.State.Workstreams.Count > 0
        && Demonstration.DirectedExecution(fallback.Current)?.Status == ExecutionStatus.WaitingForHuman;

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
        Assert.That(fallback.Player, Is.Not.Null);
        Assert.That(fallback.Reason, Is.EqualTo(DemonstrationReason.NotConfigured));
        Assert.That(fallback.Line, Is.EqualTo(
            "Demonstration: recorded, simulated work played on this device, not live.\nIt follows your answers, and nothing reaches an agent."));
        await Until(fallback, () => AtTheApproval(fallback), "the demonstration holds at the approval");
        Assert.That(fallback.Current!.Status.IsLive, Is.True);
    }

    [Test]
    public async Task TheInstructionsItOffersComeFromWhereTheRecordingStands()
    {
        var fallback = Start(null, PlayDemonstration);
        await Until(fallback, () => AtTheApproval(fallback), "the demonstration holds at the approval");
        var approve = Demonstration.AtApproval(DemonstrationAnswerKind.Approve);
        Assert.That(fallback.InstructionsFor(approve.Answer.ExecutionId), Is.Empty, "nothing to instruct while it waits for an approval");

        await fallback.Current!.SubmitAsync(new CommandFactory(Samples.Client).RespondToApproval(approve.Answer.ExecutionId, approve.Answer.ApprovalId!, ApprovalDecision.Approve));
        await Until(fallback, () => fallback.InstructionsFor(approve.Answer.ExecutionId).Count == 2, "the recording offers its instructions");
        Assert.That(fallback.InstructionsFor(approve.Answer.ExecutionId).Select(preset => preset.Text),
            Is.EqualTo(Demonstration.InstructionsAfterApproving().Select(branch => branch.Answer.Text)));
    }

    [Test]
    public async Task WhenTheRecordingEndsTheLineSaysItStartsAgain()
    {
        var fallback = Start(null, PlayDemonstration);
        await Until(fallback, () => AtTheApproval(fallback), "the demonstration holds at the approval");
        var interrupt = Demonstration.AtApproval(DemonstrationAnswerKind.Interrupt);
        await fallback.Current!.SubmitAsync(new CommandFactory(Samples.Client).Interrupt(interrupt.Answer.ExecutionId));
        await Until(fallback, () => fallback.Current!.State.Runtimes.Count == 0, "the recording ends");

        Assert.That(fallback.Player!.Ended, Is.True);
        Assert.That(fallback.Line, Does.EndWith("\nThis recording has ended and starts again shortly."));
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
        Assert.That(fallback.Line, Does.Contain("not live").And.Contain(ConnectionText.Unreachable));

        await Until(fallback, () => server.Attempts >= 3 && fallback.Line!.Contains("Connection refused"), "the control plane is tried again");
        Assert.That(fallback.Current, Is.SameAs(fallback.Demonstration));
    }

    [Test]
    public async Task OnceTheControlPlaneIsLiveItIsShownAndTheDemonstrationNeverReturns()
    {
        server.ConnectFailure = new IOException("Connection refused");
        var controlPlane = ControlPlane();
        var fallback = Start(controlPlane, PlayDemonstration);
        await Until(fallback, () => AtTheApproval(fallback), "the demonstration has played to the approval");

        server.ConnectFailure = null;
        var connection = await server.AcceptAsync();
        await connection.WelcomeWithSnapshotAsync(Samples.Snapshot(3, new[] { Samples.Workstream("w1") }));
        var pumped = await Until(fallback, () => fallback.Current == controlPlane, "the control plane is shown");

        Assert.That(pumped[^1].Resynchronized && pumped[^1].ConnectionChanged, Is.True, "consumers redraw everything");
        Assert.That(fallback.Reason, Is.Null);
        Assert.That(fallback.Line, Is.Null);
        Assert.That(fallback.Demonstration, Is.Null);
        Assert.That(fallback.InstructionsFor("any"), Is.Empty);
        Assert.That(controlPlane.State.Workstreams.Keys, Is.EquivalentTo(new[] { "w1" }));

        connection.Close("stopped");
        // Waiting to retry lasts milliseconds before the next attempt connects; any phase but live will do.
        await Until(fallback, () => !controlPlane.Status.IsLive, "the control plane is lost");
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
    public async Task PausingStopsTheDemonstrationAndResumingPlaysItFromTheBeginning()
    {
        var fallback = Start(null, PlayDemonstration);
        await Until(fallback, () => AtTheApproval(fallback), "the demonstration holds at the approval");

        await fallback.SetPausedAsync(true);
        await Until(fallback, () => fallback.Current!.Status.Phase == ConnectionPhase.Stopped, "the demonstration stops");
        await fallback.SetPausedAsync(false);
        var pumped = await Until(fallback, () => fallback.Current!.Status.IsLive, "the demonstration plays again");
        Assert.That(pumped.Any(changes => changes.Rewound), Is.True, "consumers learn it went back to its beginning");
        Assert.That(fallback.Player!.Plays, Is.EqualTo(2));
    }

    [Test]
    public void TheLineNamesWhyTheControlPlaneIsNotShown()
    {
        var waiting = DemonstrationFallback.Describe(
            DemonstrationReason.Unreachable, new ConnectionStatus(ConnectionPhase.WaitingToRetry, "The control plane did not answer within 10 s."));
        Assert.That(waiting, Does.EndWith(ConnectionText.Unreachable + " (The control plane did not answer within 10 s.)"));

        var refused = DemonstrationFallback.Describe(
            DemonstrationReason.Unreachable, new ConnectionStatus(ConnectionPhase.Refused, "The control plane speaks realtime protocol 2."));
        Assert.That(refused, Does.EndWith("Your Mac refused this app. The control plane speaks realtime protocol 2."));

        var token = DemonstrationFallback.Describe(
            DemonstrationReason.Unreachable, new ConnectionStatus(ConnectionPhase.Refused, ConnectionText.AccessTokenRefused, accessRefused: true));
        Assert.That(token, Does.EndWith("\n" + ConnectionText.AccessTokenRefused), "a refused token says what to do, and never that the Mac is unreachable");
        Assert.That(token, Does.Not.Contain("Can't reach"));

        foreach (var line in new[] { waiting, refused, token, DemonstrationFallback.Describe(DemonstrationReason.NotConfigured, null) })
        {
            Assert.That(line, Does.StartWith(
                "Demonstration: recorded, simulated work played on this device, not live.\nIt follows your answers, and nothing reaches an agent."));
        }
    }
}

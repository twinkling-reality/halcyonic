using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The C# client against a real control plane running the mock runtime: every message it receives
/// must round-trip through the generated contracts, and every command it builds must be accepted.
/// </summary>
[Category("ControlPlane")]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class LiveControlPlaneTests
{
    private readonly List<RecordingTransport> connections = new();
    private readonly ActivityLog activity = new();
    private readonly List<ControlPlaneProcess> processes = new();
    private string dataDir = null!;
    private RealtimeSession? session;
    private CommandFactory commands = null!;

    [SetUp]
    public void CreateDataDirectory()
    {
        dataDir = Directory.CreateTempSubdirectory("halcyonic-client-tests-").FullName;
        commands = new CommandFactory(Samples.Client);
    }

    [TearDown]
    public async Task CleanUp()
    {
        if (session != null) await session.StopAsync();
        foreach (var process in processes) process.Dispose();
        Directory.Delete(dataDir, recursive: true);
    }

    private async Task<ControlPlaneProcess> StartControlPlaneAsync(int port)
    {
        var process = await ControlPlaneProcess.StartAsync(dataDir, port);
        processes.Add(process);
        return process;
    }

    private RealtimeSession Connect(ControlPlaneProcess controlPlane)
    {
        var options = new RealtimeSessionOptions(controlPlane.RealtimeEndpoint, controlPlane.AccessToken, Samples.Client)
        {
            InitialRetryDelay = TimeSpan.FromMilliseconds(100),
            MaxRetryDelay = TimeSpan.FromMilliseconds(500),
        };
        session = new RealtimeSession(options, () =>
        {
            var transport = new RecordingTransport(new ClientWebSocketTransport());
            lock (connections) connections.Add(transport);
            return transport;
        });
        session.Start();
        return session;
    }

    /// <summary>Pumps until the condition holds, keeping the activity log current as a client would.</summary>
    private async Task<StateChanges> Until(Func<RealtimeSession, bool> condition, string description, int seconds = 15)
    {
        var changes = await Pumping.Until(
            session!, condition, description, TimeSpan.FromSeconds(seconds), () => string.Join("\n", processes.Select(p => p.Output)));
        activity.Record(changes.Events);
        return changes;
    }

    /// <summary>Submits a command, requires it to be accepted, and waits for its result.</summary>
    private async Task<CommandResult> RunAsync(CommandEnvelope command)
    {
        var ack = await session!.SubmitAsync(command);
        Assert.That(ack.Disposition, Is.EqualTo(CommandAckDisposition.Accepted), command.CommandType);
        await Until(
            s => s.State.Commands.TryGetValue(command.CommandId, out var view) && view.Status == CommandStatus.Completed,
            command.CommandType + " completes");
        return session.State.Commands[command.CommandId].Result!;
    }

    private async Task<(string WorkstreamId, string ExecutionId)> StartWorkAsync(string scenario)
    {
        var project = (ProjectCreatedResult)await RunAsync(commands.CreateProject("Client tests"));
        var workstream = (WorkstreamCreatedResult)await RunAsync(
            commands.CreateWorkstream(project.ProjectId, "Move sessions to their own table", null));
        var execution = (ExecutionCreatedResult)await RunAsync(commands.StartExecution(
            workstream.WorkstreamId,
            "mock",
            "Write and apply the migration.",
            new Dictionary<string, JToken> { ["scenario"] = scenario }));
        return (workstream.WorkstreamId, execution.ExecutionId);
    }

    private void AssertEveryServerMessageRoundTrips()
    {
        var received = connections.SelectMany(connection => connection.Received).ToList();
        Assert.That(received, Is.Not.Empty);
        foreach (var message in received) Json.AssertRoundTrips<ServerMessage>(message);
    }

    [Test]
    public async Task DrivesAnApprovalToCompletionAndResumesAfterADroppedConnection()
    {
        var controlPlane = await StartControlPlaneAsync(ControlPlaneProcess.FreePort());
        Connect(controlPlane);
        await Until(s => s.Status.IsLive, "the session is live");
        var mock = session!.State.Runtimes.Single(runtime => runtime.RuntimeId == "mock");
        Assert.That(mock.Synthetic, Is.True);
        Assert.That(mock.Capabilities.RespondToApproval, Is.True);

        var (workstreamId, executionId) = await StartWorkAsync("approval_required");
        await Until(
            s => s.State.Workstreams[workstreamId].Status == WorkstreamStatus.WaitingForHuman,
            "the workstream waits for a human");
        var character = CharacterPresenter.Present(session.State.Workstreams[workstreamId], session.State, session.Status.IsLive);
        Assert.That(character.Activity, Is.EqualTo(CharacterActivity.WaitingForHuman));
        Assert.That(character.Attention, Is.EqualTo(AttentionLevel.ActionRequired));
        Assert.That(character.Synthetic, Is.True);
        Assert.That(character.AttentionNotes.Single(), Does.StartWith("Approval needed to use bash"));
        var waiting = WorkspacePresenter.Present(session.State.Workstreams[workstreamId], session.State, activity, live: true);
        Assert.That(waiting.Actions, Is.EqualTo(new[] { WorkspaceAction.Approve, WorkspaceAction.Deny, WorkspaceAction.Interrupt }));
        Assert.That(waiting.Actions.All(waiting.RequiresConfirmation), Is.True, "approving and interrupting are review_required");

        var approval = session.State.Executions[executionId].PendingApprovals.Single();
        await RunAsync(commands.RespondToApproval(executionId, approval.ApprovalId, ApprovalDecision.Approve));
        await Until(s => s.State.Executions[executionId].Status == ExecutionStatus.Completed, "the turn finishes", seconds: 20);
        Assert.That(session.State.Workstreams[workstreamId].Attention.Level, Is.EqualTo(AttentionLevel.None));
        var workspace = WorkspacePresenter.Present(session.State.Workstreams[workstreamId], session.State, activity, live: true);
        Assert.That(workspace.Actions, Is.EqualTo(new[] { WorkspaceAction.Instruct }));
        Assert.That(workspace.Commands.First().Text, Is.EqualTo("Approval answered"));

        // A client that joins now gets current state from the snapshot and the history over REST.
        using var history = new ControlPlaneApi(ControlPlaneApi.BaseUriFor(controlPlane.RealtimeEndpoint), controlPlane.AccessToken);
        var fromHistory = new ActivityLog();
        fromHistory.Record(await history.ReadAllAsync(workstreamId, session.State.Journal!.JournalId));
        Assert.That(fromHistory.For(executionId).Select(entry => entry.Text), Is.EqualTo(activity.For(executionId).Select(entry => entry.Text)));
        Assert.That(fromHistory.For(executionId).Select(entry => entry.Text), Does.Contain("Approved"));

        // Understanding is read through to Salidium, which never observes the mock runtime.
        var understanding = await history.GetUnderstandingAsync(executionId);
        Assert.That(understanding.Result, Is.TypeOf<UnavailableUnderstanding>());
        Assert.That(((UnavailableUnderstanding)understanding.Result).Reason.Code, Is.EqualTo("runtime_not_observed"));
        using var raw = new System.Net.Http.HttpClient();
        raw.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", controlPlane.AccessToken);
        Json.AssertRoundTrips<UnderstandingResponse>(
            await raw.GetStringAsync(new Uri(ControlPlaneApi.BaseUriFor(controlPlane.RealtimeEndpoint), "api/executions/" + executionId + "/understanding")));

        // So is evaluation, read through to Seorak, which does not observe the mock runtime either.
        var evaluation = await history.GetEvaluationAsync(executionId);
        Assert.That(evaluation.Result, Is.TypeOf<UnavailableEvaluation>());
        Assert.That(((UnavailableEvaluation)evaluation.Result).Reason.Code, Is.EqualTo("runtime_not_observed"));
        Json.AssertRoundTrips<EvaluationResponse>(
            await raw.GetStringAsync(new Uri(ControlPlaneApi.BaseUriFor(controlPlane.RealtimeEndpoint), "api/executions/" + executionId + "/evaluation")));

        var position = session.State.Position;
        connections.Last().Abort();
        await Until(s => connections.Count == 2 && s.Status.IsLive, "the session reconnects");
        var hello = Json.Parse(connections[1].Sent[0]);
        Assert.That((long?)hello["resume"]!["position"], Is.EqualTo(position));
        var welcome = HalcyonicJson.Deserialize<WelcomeMessage>(connections[1].Received[0], strict: true);
        Assert.That(welcome.Resumed, Is.True, "nothing happened while disconnected, so no snapshot is needed");
        Assert.That(connections[1].Received.Any(message => message.Contains("\"type\":\"snapshot\"")), Is.False);

        AssertEveryServerMessageRoundTrips();
    }

    [Test]
    public async Task SteersFromTheWorkspaceToRuntimeConfirmedResults()
    {
        var controlPlane = await StartControlPlaneAsync(ControlPlaneProcess.FreePort());
        Connect(controlPlane);
        await Until(s => s.Status.IsLive, "the session is live");
        var (workstreamId, executionId) = await StartWorkAsync("approval_required");
        await Until(
            s => s.State.Workstreams[workstreamId].Status == WorkstreamStatus.WaitingForHuman,
            "the workstream waits for a human");

        var submissions = new CommandSubmissions();
        var steering = new WorkspaceSteering(commands);
        WorkspacePresentation Workspace() => WorkspacePresenter.Present(
            session!.State.Workstreams[workstreamId], session.State, activity, session.Status.IsLive, submissions);

        Assert.That(steering.Press(WorkspaceAction.Approve, Workspace()).Step, Is.EqualTo(SteeringStep.Confirm),
            "the control plane marks answering an approval for review");
        var approve = steering.Confirm(Workspace());
        Assert.That(approve.Step, Is.EqualTo(SteeringStep.Send));
        await submissions.SubmitAsync(command => session!.SubmitAsync(command), approve.Command!, executionId);
        Assert.That(submissions.StateOf(approve.Command!.CommandId), Is.EqualTo(SubmissionState.Acknowledged));

        await Until(s => s.State.Executions[executionId].Status == ExecutionStatus.Completed, "the turn finishes", seconds: 20);
        var answered = Workspace().Commands.First();
        Assert.That(answered.Text, Is.EqualTo("Approval answered"));
        Assert.That(answered.Status, Is.EqualTo(CommandStatus.Completed), "completion comes from the control plane's record");

        Assert.That(steering.Press(WorkspaceAction.Instruct, Workspace()).Step, Is.EqualTo(SteeringStep.Type));
        var instruct = steering.Typed("Summarize what you changed.", Workspace());
        Assert.That(instruct.Step, Is.EqualTo(SteeringStep.Send), "an instruction is low consequence, so it needs no confirmation");
        await submissions.SubmitAsync(command => session!.SubmitAsync(command), instruct.Command!, executionId);
        await Until(
            s => s.State.Commands.TryGetValue(instruct.Command!.CommandId, out var view) && view.Status == CommandStatus.Completed,
            "the runtime takes the instruction");
        Assert.That(Workspace().Commands.First().Text, Is.EqualTo("Instruction delivered"));
        Assert.That(activity.For(executionId).Last(entry => entry.Kind == ActivityKind.Command).Text,
            Is.EqualTo(Samples.Client.Name + " asked to send an instruction"));
    }

    [Test]
    public async Task ShowsWorkInFlightAsUnknownAfterTheControlPlaneRestarts()
    {
        var port = ControlPlaneProcess.FreePort();
        var controlPlane = await StartControlPlaneAsync(port);
        Connect(controlPlane);
        await Until(s => s.Status.IsLive, "the session is live");
        var (workstreamId, executionId) = await StartWorkAsync("successful_feature");
        await Until(s => s.State.Executions[executionId].Status == ExecutionStatus.Running, "the execution runs");

        controlPlane.Kill();
        await Until(s => !s.Status.IsLive, "the session notices");
        var stale = CharacterPresenter.Present(session!.State.Workstreams[workstreamId], session.State, session.Status.IsLive);
        Assert.That(stale.Stale, Is.True, "a disconnected client shows its last known state as such");
        Assert.That(stale.Activity, Is.EqualTo(CharacterActivity.Working));

        await StartControlPlaneAsync(port);
        await Until(
            s => s.Status.IsLive && s.State.Executions[executionId].Status == ExecutionStatus.Unknown,
            "the restarted control plane reports the execution as unknown");
        var execution = session.State.Executions[executionId];
        Assert.That(execution.StatusReason!.Code, Is.EqualTo("control_plane_restarted"));
        var character = CharacterPresenter.Present(session.State.Workstreams[workstreamId], session.State, session.Status.IsLive);
        Assert.That(character.Activity, Is.EqualTo(CharacterActivity.Unknown));
        Assert.That(character.Stale, Is.False);
        Assert.That(character.AttentionNotes.Single(), Does.StartWith("State unknown"));

        AssertEveryServerMessageRoundTrips();
    }

    /// <summary>
    /// A test host that dies without disposing its control plane, however it dies, closes the
    /// control plane's standard input as it goes. The control plane must then stop by itself instead
    /// of running on, orphaned, on its private port.
    /// </summary>
    [Test]
    public async Task StopsByItselfWhenItsStandardInputCloses()
    {
        var controlPlane = await StartControlPlaneAsync(ControlPlaneProcess.FreePort());

        controlPlane.CloseStandardInput();

        Assert.That(
            controlPlane.WaitForExit(TimeSpan.FromSeconds(10)),
            Is.EqualTo(0),
            "the control plane shuts down cleanly:\n" + controlPlane.Output);
    }
}

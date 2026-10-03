using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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

    private async Task<ControlPlaneProcess> StartControlPlaneAsync(int port = 0)
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
            new Dictionary<string, JToken> { ["scenario"] = scenario },
            // The mock lists models in a live control plane, so a start chooses one (model_required).
            modelRef: "mock/fast"));
        return (workstream.WorkstreamId, execution.ExecutionId);
    }

    [Test]
    public async Task TellsARefusedAccessTokenFromAnUnreachableMac()
    {
        var controlPlane = await StartControlPlaneAsync();
        // The fifth headset session: a token pushed earlier no longer matched the Mac's, and the app
        // said "Unable to connect to the remote server" although the control plane answered 401.
        session = ControlPlaneTarget.Local(controlPlane.RealtimeEndpoint, "an-access-token-from-an-earlier-session").CreateSession(Samples.Client);
        session.Start();
        await Until(s => s.Status.Phase == ConnectionPhase.Refused, "the stale token is refused", seconds: 20);
        Assert.That(session.Status.AccessRefused, Is.True);
        Assert.That(session.Status.Detail, Is.EqualTo(ConnectionText.AccessTokenRefused));
        Assert.That(ConnectionText.WhyNotLive(session.Status), Does.StartWith("Your computer refused this headset's access token"));
        Assert.That(DemonstrationFallback.Describe(DemonstrationReason.Unreachable, session.Status),
            Does.EndWith(ConnectionText.AccessTokenRefused));
        await session.StopAsync();

        // Nothing listening: unreachable, retried, and never called a refusal.
        session = ControlPlaneTarget.Local(new Uri($"ws://127.0.0.1:{ControlPlaneProcess.FreePort()}/realtime"), controlPlane.AccessToken)
            .CreateSession(Samples.Client);
        session.Start();
        await Until(s => s.Status.Phase == ConnectionPhase.WaitingToRetry, "nothing answers", seconds: 20);
        Assert.That(session.Status.AccessRefused, Is.False);
        Assert.That(ConnectionText.WhyNotLive(session.Status), Does.StartWith(ConnectionText.Unreachable));
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
        var controlPlane = await StartControlPlaneAsync();
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
        Assert.That(character.AttentionNotes.Single(), Does.StartWith("It wants to run: "));
        var waiting = WorkspacePresenter.Present(session.State.Workstreams[workstreamId], session.State, activity, live: true);
        Assert.That(waiting.Actions, Is.EqualTo(new[] { WorkspaceAction.Approve, WorkspaceAction.Deny, WorkspaceAction.Interrupt }));
        Assert.That(waiting.Actions.All(waiting.RequiresConfirmation), Is.True, "approving and interrupting are review_required");

        var approval = session.State.Executions[executionId].PendingApprovals.Single();
        await RunAsync(commands.RespondToApproval(executionId, approval.ApprovalId, ApprovalDecision.Approve));
        await Until(s => s.State.Executions[executionId].Status == ExecutionStatus.Completed, "the turn finishes", seconds: 20);
        Assert.That(session.State.Workstreams[workstreamId].Attention.Level, Is.EqualTo(AttentionLevel.None));
        var workspace = WorkspacePresenter.Present(session.State.Workstreams[workstreamId], session.State, activity, live: true);
        Assert.That(workspace.Actions, Is.EqualTo(new[] { WorkspaceAction.Instruct }));
        Assert.That(workspace.Commands.First().Text, Is.EqualTo("Confirmed: it has your decision."));

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

        // What the workspace's two sections say about it, read through the same interface as the demonstration's answers.
        IIntelligenceReader reader = history;
        var read = await reader.ReadUnderstandingAsync(executionId, CancellationToken.None);
        Assert.That(read.Recorded, Is.False);
        foreach (UnderstandPrompt prompt in Enum.GetValues(typeof(UnderstandPrompt)))
        {
            Assert.That(UnderstandingPresenter.Present(prompt, executionId, read, false, null, DateTimeOffset.UtcNow, TimeZoneInfo.Utc).Provenance,
                Is.EqualTo("From Salidium · Understanding unavailable: Salidium does not observe sessions of the mock runtime."));
        }
        var measured = await reader.ReadEvaluationAsync(executionId, CancellationToken.None);
        var checkedSection = CheckedPresenter.Present(executionId, read, false, null, measured, false, null, DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
        Assert.That(checkedSection.Provenance, Is.EqualTo("From Salidium · Understanding unavailable: Salidium does not observe sessions of the mock runtime."));
        Assert.That(checkedSection.Lines.Select(line => line.Text),
            Is.EqualTo(new[] { "From Seorak · Evaluation unavailable: Seorak does not observe sessions of the mock runtime." }));

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
        var controlPlane = await StartControlPlaneAsync();
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
        Assert.That(steering.Request(Workspace()), Does.StartWith("bash: "), "the whole request shows below the question");
        steering.RequestShown(1, 1);
        var approve = steering.Confirm(Workspace());
        Assert.That(approve.Step, Is.EqualTo(SteeringStep.Send));
        await submissions.SubmitAsync(command => session!.SubmitAsync(command), approve.Command!, executionId);
        Assert.That(submissions.StateOf(approve.Command!.CommandId), Is.EqualTo(SubmissionState.Acknowledged));

        await Until(s => s.State.Executions[executionId].Status == ExecutionStatus.Completed, "the turn finishes", seconds: 20);
        var answered = Workspace().Commands.First();
        Assert.That(answered.Text, Is.EqualTo("Confirmed: it has your decision."));
        Assert.That(answered.Status, Is.EqualTo(CommandStatus.Completed), "completion comes from the control plane's record");

        Assert.That(steering.Press(WorkspaceAction.Instruct, Workspace()).Step, Is.EqualTo(SteeringStep.Type));
        var instruct = steering.Typed("Summarize what you changed.", Workspace());
        Assert.That(instruct.Step, Is.EqualTo(SteeringStep.Send), "an instruction is low consequence, so it needs no confirmation");
        await submissions.SubmitAsync(command => session!.SubmitAsync(command), instruct.Command!, executionId);
        await Until(
            s => s.State.Commands.TryGetValue(instruct.Command!.CommandId, out var view) && view.Status == CommandStatus.Completed,
            "the runtime takes the instruction");
        Assert.That(Workspace().Commands.First().Text, Is.EqualTo("Confirmed: it has your instruction."));
        Assert.That(activity.For(executionId).Last(entry => entry.Kind == ActivityKind.Command).Text,
            Is.EqualTo("Asked to send an instruction"));
    }

    [Test]
    public async Task AnswersAQuestionTheAgentAsked()
    {
        var controlPlane = await StartControlPlaneAsync();
        Connect(controlPlane);
        await Until(s => s.Status.IsLive, "the session is live");
        var mock = session!.State.Runtimes.Single(runtime => runtime.RuntimeId == "mock");
        Assert.That(mock.Capabilities.AnswerQuestion, Is.True);

        var (workstreamId, executionId) = await StartWorkAsync("question_asked");
        await Until(
            s => s.State.Executions[executionId].PendingQuestions.Count == 1,
            "the agent asks");
        Assert.That(session.State.Workstreams[workstreamId].Status, Is.EqualTo(WorkstreamStatus.WaitingForHuman));
        var question = session.State.Executions[executionId].PendingQuestions.Single();
        Assert.That(question.Answerable, Is.True);
        Assert.That(question.Prompts.Select(prompt => prompt.Key), Is.EqualTo(new[] { "q0", "q1" }));
        Assert.That(session.State.Workstreams[workstreamId].Attention.Reasons.Single(), Is.TypeOf<QuestionPendingReason>());

        // An answer that does not fit the questions is refused in words, and nothing changes.
        var wrong = await session.SubmitAsync(commands.AnswerQuestion(executionId, question.QuestionId, new[]
        {
            new QuestionAnswer { Key = "q0", Selected = new List<string> { "Purple" } },
        }));
        Assert.That(wrong.Disposition, Is.EqualTo(CommandAckDisposition.Rejected));
        Assert.That(wrong.Command!.Rejection!.Code, Is.EqualTo(RejectionCode.InvalidAnswer));

        await RunAsync(commands.AnswerQuestion(executionId, question.QuestionId, new[]
        {
            new QuestionAnswer { Key = "q0", Selected = new List<string>(), Text = "Dark, with the brand blue" },
            new QuestionAnswer { Key = "q1", Selected = new List<string> { "Orders", "Settings" } },
        }));
        await Until(
            s => s.State.Executions[executionId].Status == ExecutionStatus.Completed,
            "the turn finishes after the answer");
        Assert.That(session.State.Executions[executionId].PendingQuestions, Is.Empty);
        AssertEveryServerMessageRoundTrips();
    }

    [Test]
    public async Task TheHeadsetAnswersTheAgentsQuestionOnlyThroughSendAnswer()
    {
        var controlPlane = await StartControlPlaneAsync();
        Connect(controlPlane);
        await Until(s => s.Status.IsLive, "the session is live");
        var (workstreamId, executionId) = await StartWorkAsync("question_asked");
        await Until(s => s.State.Executions[executionId].PendingQuestions.Count == 1, "the agent asks");

        // What the workspace shows and does, from the live state and the control plane's own policies.
        WorkspacePresentation Now() => WorkspacePresenter.Present(session!.State.Workstreams[workstreamId], session.State, activity, live: true);
        var workspace = Now();
        Assert.That(WorkspaceText.FirstQuestion(workspace), Is.EqualTo(WorkspaceQuestion.NeedFromYou));
        Assert.That(workspace.Actions, Does.Contain(WorkspaceAction.Answer));
        Assert.That(workspace.RequiresConfirmation(WorkspaceAction.Answer), Is.False, "low consequence, per the control plane");
        var draft = new QuestionDraft(executionId, workspace.QuestionToAnswer!);
        var steering = new WorkspaceSteering(commands);
        Assert.That(steering.SendAnswer(draft, workspace).Step, Is.EqualTo(SteeringStep.Explain), "nothing goes unanswered");

        draft.Choose(0, "Dark");
        draft.Choose(1, "Orders");
        draft.Choose(1, "Settings");
        draft.ShownWhole(0);
        draft.ShownWhole(1);
        var outcome = steering.SendAnswer(draft, Now());
        Assert.That(outcome.Step, Is.EqualTo(SteeringStep.Send));
        // Sent as the headset sends it, through its submissions, which once threw on an answer.
        var submissions = new CommandSubmissions();
        await submissions.SubmitAsync(sent => session!.SubmitAsync(sent), outcome.Command!, executionId);
        Assert.That(submissions.StateOf(outcome.Command!.CommandId), Is.EqualTo(SubmissionState.Acknowledged));
        Assert.That(WorkspacePresenter.Present(session!.State.Workstreams[workstreamId], session.State, activity, live: true, submissions).Actions,
            Does.Not.Contain(WorkspaceAction.Answer), "no second answer races the first");
        await Until(s => s.State.Commands.TryGetValue(outcome.Command!.CommandId, out var view) && view.Status == CommandStatus.Completed, "the answer completes");
        Assert.That(WorkspacePresenter.Feedback(session!.State.Commands[outcome.Command!.CommandId]).Text, Is.EqualTo("Confirmed: it has your answer."));
        await Until(s => s.State.Executions[executionId].Status == ExecutionStatus.Completed, "the turn finishes after the answer");
        Assert.That(Now().QuestionToAnswer, Is.Null);
        var lines = activity.For(executionId).Select(entry => entry.Text).ToList();
        Assert.That(lines, Does.Contain("Asked to answer the agent's question"));
        Assert.That(lines.IndexOf("Started on Mock runtime (development fixture)"), Is.GreaterThan(lines.IndexOf("Asked Mock runtime (development fixture) to start")),
            "started only after the runtime says so");
        Assert.That(lines, Has.None.Contains("halcyonic").And.None.Contains("execution.").And.None.Contains("Execution"),
            "no client names or command types reach the headset");
        Assert.That(WorkspaceText.Questions(Now()), Does.Not.Contain(WorkspaceQuestion.NeedFromYou), "the question's tab goes once it is answered");
    }

    [Test]
    public async Task BindsProjectsToFoldersTheHostLists()
    {
        var root = Directory.CreateTempSubdirectory("halcyonic-client-projects-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "storefront"));
            var controlPlane = await ControlPlaneProcess.StartAsync(dataDir, projectRoot: root);
            processes.Add(controlPlane);
            Connect(controlPlane);
            await Until(s => s.Status.IsLive, "the session is live");
            Assert.That(session!.State.Runtimes.Single(runtime => runtime.RuntimeId == "mock").UsesProjectLocation, Is.False);

            using var api = new ControlPlaneApi(ControlPlaneApi.BaseUriFor(controlPlane.RealtimeEndpoint), controlPlane.AccessToken);
            var listed = (await api.GetLocationsAsync()).Roots.Single();
            Assert.That(listed.Status, Is.EqualTo(LocationRootStatus.Available));
            Assert.That(listed.Folders.Select(folder => folder.Name), Is.EqualTo(new[] { "storefront" }));

            // A folder already there, named as the host listed it.
            var existing = (ProjectCreatedResult)await RunAsync(commands.CreateProject(
                "Storefront", new ExistingFolderChoice { Root = listed.Path, FolderName = listed.Folders[0].Name }));
            var bound = session.State.Projects[existing.ProjectId].Location!;
            Assert.That(bound.Path, Is.EqualTo(listed.Folders[0].Path));
            Assert.That(bound.Name, Is.EqualTo("storefront"));
            Assert.That(bound.Created, Is.False);

            // A new folder, which the host makes.
            var created = (ProjectCreatedResult)await RunAsync(commands.CreateProject(
                "Greeting card", new NewFolderChoice { Root = listed.Path, FolderName = "greeting-card" }));
            Assert.That(session.State.Projects[created.ProjectId].Location!.Created, Is.True);
            Assert.That(Directory.Exists(Path.Combine(root, "greeting-card")), Is.True);

            // A name already taken is refused with a code the headset can put in words.
            var ack = await session.SubmitAsync(commands.CreateProject(
                "Greeting card again", new NewFolderChoice { Root = listed.Path, FolderName = "greeting-card" }));
            Assert.That(ack.Disposition, Is.EqualTo(CommandAckDisposition.Rejected));
            Assert.That(ack.Command!.Rejection!.Code, Is.EqualTo(RejectionCode.LocationExists));

            // Bound again, here to the root itself.
            await RunAsync(commands.SetProjectLocation(
                existing.ProjectId, new ExistingFolderChoice { Root = listed.Path, FolderName = null }));
            Assert.That(session.State.Projects[existing.ProjectId].Location!.Path, Is.EqualTo(listed.Path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Start building as the headset drives it, against a real host with a project root: a new
    /// folder whose name is taken is refused with location_exists, Use that folder creates the
    /// project there instead, and adding work to it in a new folder binds it first.
    /// </summary>
    [Test]
    public async Task StartsBuildingInAFolderTheHeadsetChose()
    {
        var root = Directory.CreateTempSubdirectory("halcyonic-client-build-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "recipes"));
            var controlPlane = await ControlPlaneProcess.StartAsync(dataDir, projectRoot: root);
            processes.Add(controlPlane);
            Connect(controlPlane);
            await Until(s => s.Status.IsLive, "the session is live");
            using var api = new ControlPlaneApi(ControlPlaneApi.BaseUriFor(controlPlane.RealtimeEndpoint), controlPlane.AccessToken);
            var listing = await api.GetLocationsAsync();
            var place = ProjectFolder.Options(listing).First(option => option.Kind == FolderOptionKind.NewFolder).Root;
            var mock = session!.State.Runtimes.Single(runtime => runtime.RuntimeId == "mock");

            async Task<NewWorkDraft> Draft(string? projectId, string objective)
            {
                var draft = new NewWorkDraft(commands) { ProjectId = projectId, Objective = objective };
                draft.ChooseRuntime(mock);
                if (mock.ModelChoice == ModelChoice.Listed)
                {
                    draft.SetModels(await api.GetRuntimeModelsAsync("mock"));
                    draft.ChooseModel(draft.Models[0]);
                }
                return draft;
            }

            var taken = ProjectFolder.New(place, "recipes")!;
            var sequence = new BuildSequence(await Draft(null, "Plan the week's dinners."), commands, "Recipes", taken.ToContract());
            await Drive(sequence, sequence.Begin(Samples.Reviewed(sequence)));
            Assert.That(sequence.StoppedAt?.Refusal, Is.EqualTo(RejectionCode.LocationExists));
            Assert.That(EntryText.AboutFolder(sequence.StoppedAt!), Is.True);

            await Drive(sequence, sequence.Retry(Samples.Reviewed(sequence), folder: ProjectFolder.Existing(taken).ToContract()));
            Assert.That(sequence.Started, Is.True, string.Join(", ", sequence.Steps.Select(EntryText.StepStatus)));
            var project = session.State.Projects[sequence.ProjectId!];
            Assert.That((project.Location!.Name, project.Location.Created), Is.EqualTo(("recipes", false)));

            var moved = ProjectFolder.New(place, ProjectFolder.SuggestName("Recipes, again!"))!;
            var more = new BuildSequence(await Draft(project.ProjectId, "Write the shopping list."), commands, null, moved.ToContract());
            Assert.That(more.Steps[0].Kind, Is.EqualTo(BuildStepKind.BindFolder));
            await Drive(more, more.Begin(Samples.Reviewed(more)));
            Assert.That(more.Started, Is.True, string.Join(", ", more.Steps.Select(EntryText.StepStatus)));
            Assert.That(session.State.Projects[project.ProjectId].Location!.Name, Is.EqualTo("recipes-again"));
            Assert.That(Directory.Exists(Path.Combine(root, "recipes-again")), Is.True);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Sends each command a sequence asks for, as the entry panel does, until it starts the work or stops.</summary>
    private async Task Drive(BuildSequence sequence, CommandEnvelope first)
    {
        CommandEnvelope? next = first;
        while (next != null)
        {
            try
            {
                sequence.Acknowledged(await session!.SubmitAsync(next));
            }
            catch (Exception error)
            {
                sequence.AcknowledgementLost(error);
            }
            next = null;
            await Until(s =>
            {
                next = sequence.Advance(s.State);
                return next != null || sequence.Started || sequence.Stopped;
            }, "the step is settled");
        }
    }

    [Test]
    public async Task StartsWorkOnAModelTheRuntimeLists()
    {
        var controlPlane = await StartControlPlaneAsync();
        Connect(controlPlane);
        await Until(s => s.Status.IsLive, "the session is live");
        var mock = session!.State.Runtimes.Single(runtime => runtime.RuntimeId == "mock");
        Assert.That(mock.ModelChoice, Is.EqualTo(ModelChoice.Listed));

        var baseUri = ControlPlaneApi.BaseUriFor(controlPlane.RealtimeEndpoint);
        using var api = new ControlPlaneApi(baseUri, controlPlane.AccessToken);
        var listed = await api.GetRuntimeModelsAsync("mock");
        Assert.That(listed.RuntimeId, Is.EqualTo("mock"));
        var models = ((AvailableModels)listed.Result).Models;
        Assert.That(models.Select(model => model.ModelRef), Is.EqualTo(new[] { "mock/fast", "mock/hosted", "mock/no-tools" }));
        Assert.That(models.Select(model => model.Served), Is.EqualTo(new[] { ModelServed.ThisMac, ModelServed.Remote, ModelServed.ThisMac }));
        Assert.That(models.Select(model => model.ToolCalling),
            Is.EqualTo(new[] { ModelToolCalling.Declared, ModelToolCalling.Declared, ModelToolCalling.NotDeclared }));
        using var raw = new System.Net.Http.HttpClient();
        raw.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", controlPlane.AccessToken);
        Json.AssertRoundTrips<RuntimeModelsResponse>(await raw.GetStringAsync(new Uri(baseUri, "api/runtimes/mock/models")));

        var project = (ProjectCreatedResult)await RunAsync(commands.CreateProject("Client tests"));
        var draft = new NewWorkDraft(commands)
        {
            ProjectId = project.ProjectId,
            Objective = "Move sessions to their own table and test the migration.",
        };
        draft.ChooseRuntime(mock);
        draft.SetModels(listed);
        draft.ChooseModel(models[0]);
        var workstream = (WorkstreamCreatedResult)await RunAsync(draft.CreateWorkstream());
        var options = new Dictionary<string, JToken> { ["scenario"] = "successful_feature" };

        // A model the runtime does not list is refused in words, and nothing starts.
        var unlisted = await session.SubmitAsync(commands.StartExecution(
            workstream.WorkstreamId, "mock", "Write and apply the migration.", options, modelRef: "mock/gone"));
        Assert.That(unlisted.Disposition, Is.EqualTo(CommandAckDisposition.Rejected));
        Assert.That(unlisted.Command!.Rejection!.Message, Is.EqualTo("The mock runtime lists no model mock/gone."));

        var chosen = draft.StartExecution(workstream.WorkstreamId);
        Assert.That(chosen.Payload.Options, Is.Empty, "the headset sends no mock scenario option");
        var execution = (ExecutionCreatedResult)await RunAsync(chosen);
        await Until(
            s => s.State.Executions[execution.ExecutionId].ModelRef == "mock/fast",
            "the execution shows the model the runtime reports using");
        await Until(
            s => s.State.Executions[execution.ExecutionId].Status == ExecutionStatus.Completed,
            "the default simulated turn finishes");

        AssertEveryServerMessageRoundTrips();
    }

    [Test]
    public async Task ShowsWorkInFlightAsUnknownAfterTheControlPlaneRestarts()
    {
        var controlPlane = await StartControlPlaneAsync();
        // Started again below on the port this one chose, where the session looks for it.
        var port = controlPlane.Port;
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
        Assert.That(character.AttentionNotes.Single(), Does.StartWith("Can't tell what it's doing"));

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
        var controlPlane = await StartControlPlaneAsync();

        controlPlane.CloseStandardInput();

        Assert.That(
            controlPlane.WaitForExit(TimeSpan.FromSeconds(10)),
            Is.EqualTo(0),
            "the control plane shuts down cleanly:\n" + controlPlane.Output);
    }
}

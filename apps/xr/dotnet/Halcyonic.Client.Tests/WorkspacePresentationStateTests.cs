using System.Collections.Generic;
using Halcyonic.Contracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

[TestFixture]
public class WorkspacePresentationStateTests
{
    private static ClientProjection ProjectionWith(
        WorkstreamView workstream,
        ExecutionView execution,
        ProjectView? project = null)
    {
        var state = new ClientProjection();
        state.ApplyWelcome(Samples.Welcome(resumed: false, head: 1));
        var snapshot = Samples.Snapshot(1, new[] { workstream }, new[] { execution });
        if (project != null)
        {
            snapshot.Projects = new List<ProjectView> { project };
        }
        state.ApplySnapshot(snapshot, new StateChanges());
        return state;
    }

    [Test]
    public void AmbientPresenceKeepsPlacedAgentsVisibleWhenConsoleMinimizes()
    {
        var state = new WorkspacePresentationState(keepAgentsWhenMinimized: true);
        Assert.That((state.Minimized, state.AgentsEnabled, state.AgentsVisible), Is.EqualTo((false, false, false)));

        state.SetAgentsEnabled(true);
        Assert.That((state.Minimized, state.AgentsVisible), Is.EqualTo((false, true)));

        var events = new List<string>();
        var statesSeen = new List<(bool Minimized, bool AgentsVisible)>();
        state.Suspending += () =>
        {
            events.Add("suspending");
            statesSeen.Add((state.Minimized, state.AgentsVisible));
        };
        state.Changed += () =>
        {
            events.Add("changed");
            statesSeen.Add((state.Minimized, state.AgentsVisible));
        };

        Assert.That(state.Minimize(), Is.True);
        Assert.That((state.Minimized, state.AgentsVisible), Is.EqualTo((true, true)));
        Assert.That(events, Is.EqualTo(new[] { "suspending", "changed" }));
        Assert.That(statesSeen, Is.EqualTo(new[] { (false, true), (true, true) }));

        state.SetAgentsEnabled(false);
        Assert.That(state.AgentsVisible, Is.False);
        state.SetAgentsEnabled(true);
        Assert.That(state.AgentsVisible, Is.True);

        Assert.That(state.Restore(), Is.True);
        Assert.That((state.Minimized, state.AgentsVisible), Is.EqualTo((false, true)));
    }

    [Test]
    public void ContextualMomentOpensForActionRequiredOrInspectionAndCollapsesWhenAnsweredOrClosed()
    {
        const string workstreamId = "ws-rate-limit";
        const string executionId = "ex-rate-limit";
        const string approvalId = "ap-1";

        var execution = Samples.Execution(executionId, workstreamId, ExecutionStatus.WaitingForHuman, synthetic: false);
        execution.PendingApprovals.Add(new ApprovalView
        {
            ApprovalId = approvalId,
            RequestedAt = Samples.Time,
            Approvable = true,
            Subject = new ToolUseSubject
            {
                ToolName = "bash",
                Summary = "npm install express-rate-limit",
            },
        });
        var workstream = Samples.Workstream(
            workstreamId,
            WorkstreamStatus.WaitingForHuman,
            executionId,
            AttentionLevel.ActionRequired,
            new ApprovalPendingReason { ExecutionId = executionId, ApprovalId = approvalId });
        var projection = ProjectionWith(workstream, execution);

        var presentation = new WorkspacePresentationState(keepAgentsWhenMinimized: true);
        presentation.SetAgentsEnabled(true);
        presentation.Minimize();

        // While minimized with a placed visible character in ActionRequired, ActiveMoment returns the waiting question card.
        var active = presentation.ActiveMoment(projection, id => id == workstreamId);
        Assert.That(active.HasValue, Is.True);
        Assert.That(active!.Value.WorkstreamId, Is.EqualTo(workstreamId));
        Assert.That(active.Value.Slot.Shown.HasValue, Is.True);
        var shownMoment = active.Value.Slot.Moments[active.Value.Slot.Shown!.Value];
        Assert.That(shownMoment.Kind, Is.EqualTo(KitMomentKind.Question));
        Assert.That(shownMoment.Well, Is.EqualTo("npm install express-rate-limit"));

        // Explicitly closing the moment dismisses that pending approval until new state arrives or the avatar is inspected.
        Assert.That(presentation.CloseMoment(projection), Is.True);
        Assert.That(presentation.ActiveMoment(projection, id => id == workstreamId), Is.Null);

        // Explicitly inspecting the avatar reopens the moment card.
        Assert.That(presentation.InspectMoment(workstreamId), Is.True);
        Assert.That(presentation.ActiveMoment(projection, id => id == workstreamId), Is.Not.Null);

        // Once the approval is answered in the projection, the moment card collapses back to the avatar automatically.
        execution.PendingApprovals.Clear();
        execution.Status = ExecutionStatus.Running;
        workstream.Status = WorkstreamStatus.Running;
        workstream.Attention = new Attention { Level = AttentionLevel.None, Reasons = new List<AttentionReason>() };
        projection = ProjectionWith(workstream, execution);

        Assert.That(presentation.ActiveMoment(projection, id => id == workstreamId), Is.Null);
    }

    [Test]
    public void OnDemandProjectScreenBuildsScreenModelAndTogglesCommandBar()
    {
        const string workstreamId = "ws-garden";
        const string executionId = "ex-garden";

        var checkJson = new JObject
        {
            ["check_id"] = "01a19000-0000-7000-8000-0000000000c1",
            ["recorded_at"] = Samples.Time,
            ["position"] = 1,
            ["files"] = "current",
            ["findings"] = new JObject
            {
                ["tsc"] = new JObject { ["exit"] = 0, ["seconds"] = 1 },
                ["build"] = new JObject { ["exit"] = 0, ["seconds"] = 2 },
                ["page"] = null,
                ["unknown"] = null,
                ["passed"] = true,
                ["reads_confined"] = true,
                ["laptop"] = new JObject
                {
                    ["layer"] = "starter",
                    ["targets"] = 1,
                    ["mouse"] = "reached",
                    ["key"] = "reached",
                    ["screen_changed"] = true,
                    ["response_seen"] = null,
                    ["ready"] = true,
                },
            },
        };
        var projectJson = new JObject
        {
            ["project_id"] = Samples.ProjectId,
            ["name"] = "Tower Toss",
            ["location"] = null,
            ["starter"] = "iwsdk-vr",
            ["created_at"] = Samples.Time,
            ["updated_at"] = Samples.Time,
            ["check"] = checkJson,
            ["preview"] = new JObject { ["check_id"] = "01a19000-0000-7000-8000-0000000000c1", ["port"] = 47931, ["files"] = "current" },
        };
        var project = HalcyonicJson.Deserialize<ProjectView>(projectJson.ToString(), strict: true);
        var execution = Samples.Execution(executionId, workstreamId, ExecutionStatus.Completed, synthetic: false);
        var workstream = Samples.Workstream(workstreamId, WorkstreamStatus.Completed, executionId);
        var projection = ProjectionWith(workstream, execution, project);

        var presentation = new WorkspacePresentationState(keepAgentsWhenMinimized: true);
        Assert.That(presentation.ActiveScreen(projection), Is.Null);

        Assert.That(presentation.ShowProjectScreen(Samples.ProjectId, barOpen: false), Is.True);
        var folded = presentation.ActiveScreen(projection);
        Assert.That(folded, Is.Not.Null);
        Assert.That(folded!.Project, Is.EqualTo("Tower Toss"));
        Assert.That(folded.Folded, Is.True);
        Assert.That(folded.CanTry, Is.True);
        Assert.That(folded.ShowsPicture, Is.False);

        Assert.That(presentation.SetScreenBarOpen(true), Is.True);
        var open = presentation.ActiveScreen(projection);
        Assert.That(open, Is.Not.Null);
        Assert.That(open!.BarOpen, Is.True);
        Assert.That(open.Primary, Is.EqualTo(KitScreenAction.ChangeIt));
        Assert.That(open.Shown, Contains.Item(KitScreenAction.TryIt));

        Assert.That(presentation.CloseProjectScreen(), Is.True);
        Assert.That(presentation.ActiveScreen(projection), Is.Null);
    }

    [Test]
    public void ScreenOfEnforcesConfinementTruthWordsAndVrPrimaryAction()
    {
        var unconfinedCheckJson = new JObject
        {
            ["check_id"] = "01a19000-0000-7000-8000-0000000000c2",
            ["recorded_at"] = Samples.Time,
            ["position"] = 1,
            ["files"] = "current",
            ["findings"] = new JObject
            {
                ["tsc"] = new JObject { ["exit"] = 0, ["seconds"] = 1 },
                ["build"] = new JObject { ["exit"] = 0, ["seconds"] = 2 },
                ["page"] = null,
                ["unknown"] = null,
                ["passed"] = true,
                ["reads_confined"] = false,
                ["laptop"] = new JObject
                {
                    ["layer"] = "starter",
                    ["targets"] = 1,
                    ["mouse"] = "reached",
                    ["key"] = "reached",
                    ["screen_changed"] = true,
                    ["response_seen"] = null,
                    ["ready"] = true,
                },
            },
        };
        var unconfinedProjectJson = new JObject
        {
            ["project_id"] = Samples.ProjectId,
            ["name"] = "Unconfined App",
            ["location"] = null,
            ["starter"] = null,
            ["created_at"] = Samples.Time,
            ["updated_at"] = Samples.Time,
            ["check"] = unconfinedCheckJson,
            ["preview"] = new JObject { ["check_id"] = "01a19000-0000-7000-8000-0000000000c2", ["port"] = 47932, ["files"] = "current" },
        };
        var unconfinedProject = HalcyonicJson.Deserialize<ProjectView>(unconfinedProjectJson.ToString(), strict: true);

        var emptyState = new ClientProjection();
        emptyState.ApplyWelcome(Samples.Welcome(resumed: false, head: 1));
        var emptySnapshot = Samples.Snapshot(1, System.Array.Empty<WorkstreamView>(), System.Array.Empty<ExecutionView>());
        emptySnapshot.Projects = new List<ProjectView> { unconfinedProject };
        emptyState.ApplySnapshot(emptySnapshot, new StateChanges());

        var screenNoWork = WorkspacePresentationState.ScreenOf(unconfinedProject, emptyState, barOpen: true);
        Assert.That(screenNoWork.CanTry, Is.False, "unconfined check must never offer Try Build");
        Assert.That(screenNoWork.ShowsPicture, Is.False, "no screenshot texture is displayed yet");
        Assert.That(screenNoWork.State, Is.EqualTo(StateLanguage.WordOf(WorkState.NotStarted)));
        Assert.That(screenNoWork.Primary, Is.EqualTo(KitScreenAction.MakeAVrVersion));

        var verifyingExecution = Samples.Execution("ex-verify", "ws-verify", ExecutionStatus.Running, synthetic: false);
        var verifyingWorkstream = Samples.Workstream("ws-verify", WorkstreamStatus.Verifying, "ex-verify");
        var verifyingState = ProjectionWith(verifyingWorkstream, verifyingExecution, unconfinedProject);
        var screenVerifying = WorkspacePresentationState.ScreenOf(unconfinedProject, verifyingState, barOpen: true);
        Assert.That(screenVerifying.State, Is.EqualTo("Checking · Reported"));
    }

    [Test]
    public void MomentSlotOfMapsClarifyingQuestionsToAgentSaysNotRunCommand()
    {
        const string workstreamId = "ws-question";
        const string executionId = "ex-question";

        var execution = Samples.Execution(executionId, workstreamId, ExecutionStatus.WaitingForHuman, synthetic: false);
        execution.PendingQuestions.Add(new QuestionView
        {
            QuestionId = "q-1",
            AskedAt = Samples.Time,
            Prompts = new List<QuestionPrompt>
            {
                new()
                {
                    Header = "Session window",
                    Text = "How long should rate-limit windows last?",
                    Options = new List<QuestionOption>(),
                },
            },
        });
        var workstream = Samples.Workstream(
            workstreamId,
            WorkstreamStatus.WaitingForHuman,
            executionId,
            AttentionLevel.ActionRequired,
            new QuestionPendingReason { ExecutionId = executionId, QuestionId = "q-1" });
        var projection = ProjectionWith(workstream, execution);

        var slot = WorkspacePresentationState.MomentSlotOf(workstream, projection);
        Assert.That(slot, Is.Not.Null);
        Assert.That(slot!.Shown.HasValue, Is.True);
        var moment = slot.Moments[slot.Shown!.Value];
        Assert.That(moment.Kind, Is.EqualTo(KitMomentKind.AgentSays));
        Assert.That(moment.Prose?.Text, Is.EqualTo("Session window: How long should rate-limit windows last?"));
    }

    [Test]
    public void CloseMomentDismissesEachWaitingWorkstreamIndependently()
    {
        var execA = Samples.Execution("ex-a", "ws-a", ExecutionStatus.WaitingForHuman, synthetic: false);
        execA.PendingApprovals.Add(new ApprovalView
        {
            ApprovalId = "ap-a",
            RequestedAt = Samples.Time,
            Approvable = true,
            Subject = new ToolUseSubject { ToolName = "bash", Summary = "npm test" },
        });
        var workA = Samples.Workstream(
            "ws-a",
            WorkstreamStatus.WaitingForHuman,
            "ex-a",
            AttentionLevel.ActionRequired,
            new ApprovalPendingReason { ExecutionId = "ex-a", ApprovalId = "ap-a" });

        var execB = Samples.Execution("ex-b", "ws-b", ExecutionStatus.WaitingForHuman, synthetic: false);
        execB.PendingApprovals.Add(new ApprovalView
        {
            ApprovalId = "ap-b",
            RequestedAt = Samples.Time,
            Approvable = true,
            Subject = new ToolUseSubject { ToolName = "bash", Summary = "npm run lint" },
        });
        var workB = Samples.Workstream(
            "ws-b",
            WorkstreamStatus.WaitingForHuman,
            "ex-b",
            AttentionLevel.ActionRequired,
            new ApprovalPendingReason { ExecutionId = "ex-b", ApprovalId = "ap-b" });

        var state = new ClientProjection();
        state.ApplyWelcome(Samples.Welcome(resumed: false, head: 1));
        state.ApplySnapshot(Samples.Snapshot(1, new[] { workA, workB }, new[] { execA, execB }), new StateChanges());

        var presentation = new WorkspacePresentationState(keepAgentsWhenMinimized: true);
        presentation.SetAgentsEnabled(true);
        presentation.Minimize();

        var first = presentation.ActiveMoment(state);
        Assert.That(first, Is.Not.Null);
        Assert.That( presentation.CloseMoment(state), Is.True);

        var second = presentation.ActiveMoment(state);
        Assert.That(second, Is.Not.Null);
        Assert.That(second!.Value.WorkstreamId, Is.Not.EqualTo(first!.Value.WorkstreamId));
        Assert.That(presentation.CloseMoment(state), Is.True);
        Assert.That(presentation.ActiveMoment(state), Is.Null);
    }

    [Test]
    public void ScreenOfDisablesCanTryWhenHostCheckFilesAreStaleAndRoutesScreenAndMomentActions()
    {
        var staleCheckJson = new JObject
        {
            ["check_id"] = "01a19000-0000-7000-8000-0000000000c9",
            ["recorded_at"] = Samples.Time,
            ["position"] = 1,
            ["files"] = "changed",
            ["findings"] = new JObject
            {
                ["tsc"] = new JObject { ["exit"] = 0, ["seconds"] = 1 },
                ["build"] = new JObject { ["exit"] = 0, ["seconds"] = 2 },
                ["page"] = null,
                ["unknown"] = null,
                ["passed"] = true,
                ["reads_confined"] = true,
                ["laptop"] = new JObject
                {
                    ["layer"] = "starter",
                    ["targets"] = 1,
                    ["mouse"] = "reached",
                    ["key"] = "reached",
                    ["screen_changed"] = true,
                    ["response_seen"] = null,
                    ["ready"] = true,
                },
            },
        };
        var staleProjectJson = new JObject
        {
            ["project_id"] = Samples.ProjectId,
            ["name"] = "Recipe Tracker",
            ["location"] = new JObject { ["path"] = "/Users/person/Projects/recipe-tracker", ["name"] = "recipe-tracker", ["created"] = true },
            ["starter"] = null,
            ["created_at"] = Samples.Time,
            ["updated_at"] = Samples.Time,
            ["check"] = staleCheckJson,
            ["preview"] = new JObject { ["check_id"] = "01a19000-0000-7000-8000-0000000000c9", ["port"] = 47939, ["files"] = "current" },
        };
        var staleProject = HalcyonicJson.Deserialize<ProjectView>(staleProjectJson.ToString(), strict: true);
        var execution = Samples.Execution("ex-stale", "ws-stale", ExecutionStatus.Completed, synthetic: false);
        var workstream = Samples.Workstream("ws-stale", WorkstreamStatus.Completed, "ex-stale");
        var state = ProjectionWith(workstream, execution, staleProject);

        var staleScreen = WorkspacePresentationState.ScreenOf(staleProject, state, barOpen: true);
        Assert.That(staleScreen.NewestChangeAt, Is.EqualTo(System.DateTimeOffset.MaxValue), "files != current marks the host check stale");
        Assert.That(staleScreen.CanTry, Is.False, "stale check files must never offer Try Build");

        Assert.That(ProjectPlayFacts.TryVrVersionTarget(staleProject, out var root, out var vrFolder), Is.True);
        Assert.That((root, vrFolder), Is.EqualTo(("/Users/person/Projects", "recipe-tracker-vr")));

        var commands = new CommandFactory(Samples.Client);
        var makeVr = (ProjectMakeVrVersionCommand)WorkspacePresentationState.CommandForScreenAction(staleProject, KitScreenAction.MakeAVrVersion, commands)!;
        Assert.That((makeVr.Payload.OriginalProjectId, makeVr.Payload.Root, makeVr.Payload.FolderName),
            Is.EqualTo((Samples.ProjectId, "/Users/person/Projects", "recipe-tracker-vr")));
        Assert.That(WorkspacePresentationState.CommandForScreenAction(staleProject, KitScreenAction.TryIt, commands), Is.Null,
            "TryIt must not dispatch CheckProject when check files are stale");

        var currentCheckJson = (JObject)staleCheckJson.DeepClone();
        currentCheckJson["files"] = "current";
        var readyProjectJson = (JObject)staleProjectJson.DeepClone();
        readyProjectJson["check"] = currentCheckJson;
        var readyProject = HalcyonicJson.Deserialize<ProjectView>(readyProjectJson.ToString(), strict: true);
        var openOnMac = (ProjectOpenOnMacCommand)WorkspacePresentationState.CommandForScreenAction(readyProject, KitScreenAction.TryIt, commands)!;
        Assert.That(openOnMac.Payload.ProjectId, Is.EqualTo(Samples.ProjectId));

        var noPreviewProjectJson = (JObject)readyProjectJson.DeepClone();
        noPreviewProjectJson["preview"] = null;
        var noPreviewProject = HalcyonicJson.Deserialize<ProjectView>(noPreviewProjectJson.ToString(), strict: true);
        var noPreviewState = ProjectionWith(workstream, execution, noPreviewProject);
        var noPreviewScreen = WorkspacePresentationState.ScreenOf(noPreviewProject, noPreviewState, barOpen: true);
        Assert.That(noPreviewScreen.CanTry, Is.True, "passed check with NoPreviewOfCheck offers Try Build so preview starts automatically");
        var startPreview = (ProjectPreviewStartCommand)WorkspacePresentationState.CommandForScreenAction(noPreviewProject, KitScreenAction.TryIt, commands)!;
        Assert.That(startPreview.Payload.ProjectId, Is.EqualTo(Samples.ProjectId));

        // Once StartPreview is dispatched for TryIt and the preview starts in the projection, OpenOnMac is dispatched automatically.
        var presentation = new WorkspacePresentationState(keepAgentsWhenMinimized: true);
        presentation.RecordCommandDispatched(startPreview);
        Assert.That(presentation.PendingOpenOnMacProjectId, Is.EqualTo(Samples.ProjectId));
        Assert.That(presentation.AdvancePendingCommands(noPreviewState, commands, out _), Is.Null, "still waiting for preview to start");

        var readyState = ProjectionWith(workstream, execution, readyProject);
        var autoOpen = (ProjectOpenOnMacCommand)presentation.AdvancePendingCommands(readyState, commands, out _)!;
        Assert.That(autoOpen.Payload.ProjectId, Is.EqualTo(Samples.ProjectId));
        Assert.That(presentation.PendingOpenOnMacProjectId, Is.Null);
        Assert.That(presentation.AdvancePendingCommands(readyState, commands, out _), Is.Null);

        Assert.That(WorkspacePresentationState.SectionForScreenAction(KitScreenAction.Steps), Is.EqualTo(FileSection.Activity));
        Assert.That(WorkspacePresentationState.SectionForScreenAction(KitScreenAction.Why), Is.EqualTo(FileSection.Changes));
        Assert.That(WorkspacePresentationState.SectionForScreenAction(KitScreenAction.HowItWent), Is.EqualTo(FileSection.Checks));

        Assert.That(WorkspacePresentationState.SectionForMomentAction(KitMomentAction.YesRunIt), Is.EqualTo(FileSection.Waiting));
        Assert.That(WorkspacePresentationState.SectionForMomentAction(KitMomentAction.ReadIt), Is.EqualTo(FileSection.Waiting));
        Assert.That(WorkspacePresentationState.SectionForMomentAction(KitMomentAction.SeeWhatChanged), Is.EqualTo(FileSection.Changes));
        Assert.That(WorkspacePresentationState.SectionForMomentAction(KitMomentAction.SeeTheOutput), Is.EqualTo(FileSection.Checks));
        Assert.That(WorkspacePresentationState.SectionForMomentAction(KitMomentAction.Stop), Is.EqualTo(FileSection.Activity));
        Assert.That(WorkspacePresentationState.SectionForMomentAction(KitMomentAction.TryIt), Is.Null);
    }

    [Test]
    public void MakeAVrVersionAutoSelectsNewVrProjectAndStartsConversionWorkstream()
    {
        var sourceProjectJson = new JObject
        {
            ["project_id"] = Samples.ProjectId,
            ["name"] = "Recipe Tracker",
            ["location"] = new JObject { ["path"] = "/Users/person/Projects/recipe-tracker", ["name"] = "recipe-tracker", ["created"] = true },
            ["starter"] = null,
            ["created_at"] = Samples.Time,
            ["updated_at"] = Samples.Time,
        };
        var sourceProject = HalcyonicJson.Deserialize<ProjectView>(sourceProjectJson.ToString(), strict: true);
        var state = new ClientProjection();
        state.ApplyWelcome(Samples.Welcome(resumed: false, head: 1));
        var snapshot = Samples.Snapshot(1, System.Array.Empty<WorkstreamView>(), System.Array.Empty<ExecutionView>());
        snapshot.Projects = new List<ProjectView> { sourceProject };
        state.ApplySnapshot(snapshot, new StateChanges());

        var commands = new CommandFactory(Samples.Client);
        var presentation = new WorkspacePresentationState(keepAgentsWhenMinimized: true);
        presentation.ShowProjectScreen(Samples.ProjectId, barOpen: true);

        var makeVr = (ProjectMakeVrVersionCommand)WorkspacePresentationState.CommandForScreenAction(sourceProject, KitScreenAction.MakeAVrVersion, commands)!;
        presentation.RecordCommandDispatched(makeVr);
        Assert.That(presentation.PendingVrConversionSourceProjectId, Is.EqualTo(Samples.ProjectId));
        Assert.That(presentation.AdvancePendingCommands(state, commands, out var noneYet), Is.Null);
        Assert.That(noneYet, Is.Null);

        // The control plane creates the sibling VR starter project and emits project.made_from with the conversion prompt.
        const string vrProjectId = "01a19000-0000-7000-8000-000000000099";
        const string conversionPrompt = "Build a VR version of Recipe Tracker in recipe-tracker-vr using /Users/person/Projects/recipe-tracker as read-only reference.";
        var vrProject = new ProjectView
        {
            ProjectId = vrProjectId,
            Name = "Recipe Tracker VR",
            Starter = "iwsdk-vr",
            Location = new ProjectLocation { Path = "/Users/person/Projects/recipe-tracker-vr", Name = "recipe-tracker-vr", Created = true },
            CreatedAt = "2026-10-31T05:01:00.000Z",
            UpdatedAt = "2026-10-31T05:01:00.000Z",
        };
        var madeFromEvent = Samples.Event(2);
        madeFromEvent.Changes.Projects.Add(vrProject);
        madeFromEvent.Event = new ProjectMadeFromEvent
        {
            EventId = "ev-vr-made",
            OccurredAt = "2026-10-31T05:01:00.000Z",
            IngestedAt = "2026-10-31T05:01:00.000Z",
            Source = new ControlPlaneSource(),
            ProjectId = vrProjectId,
            Payload = new ProjectMadeFromPayload
            {
                OriginalProjectId = Samples.ProjectId,
                Instruction = conversionPrompt,
            },
        };
        Assert.That(state.ApplyEvent(madeFromEvent, new StateChanges()), Is.True);

        // Advancing selects the new VR project on the room screen and dispatches workstream.create.
        var createWs = (WorkstreamCreateCommand)presentation.AdvancePendingCommands(state, commands, out var convertedProjectId)!;
        Assert.That(convertedProjectId, Is.EqualTo(vrProjectId));
        Assert.That(presentation.ScreenProjectId, Is.EqualTo(vrProjectId));
        Assert.That(createWs.Payload.ProjectId, Is.EqualTo(vrProjectId));
        Assert.That(createWs.Payload.Objective, Is.EqualTo(conversionPrompt));

        // Once workstream.create completes in the projection, advancing dispatches execution.start with the conversion instruction.
        const string vrWorkstreamId = "ws-vr-convert";
        var wsCreatedEvent = Samples.Event(3);
        wsCreatedEvent.Changes.Commands.Add(new CommandView
        {
            CommandId = createWs.CommandId,
            CommandType = CommandType.WorkstreamCreate,
            Status = CommandStatus.Completed,
            IssuedAt = Samples.Time,
            UpdatedAt = Samples.Time,
            Result = new WorkstreamCreatedResult { WorkstreamId = vrWorkstreamId },
        });
        Assert.That(state.ApplyEvent(wsCreatedEvent, new StateChanges()), Is.True);

        var startExec = (ExecutionStartCommand)presentation.AdvancePendingCommands(state, commands, out _)!;
        Assert.That(startExec.Payload.WorkstreamId, Is.EqualTo(vrWorkstreamId));
        Assert.That(startExec.Payload.RuntimeId, Is.EqualTo("mock"));
        Assert.That(startExec.Payload.Instruction, Is.EqualTo(conversionPrompt));

        // Once execution.start completes, the sequence finishes cleanly.
        var execCreatedEvent = Samples.Event(4);
        execCreatedEvent.Changes.Commands.Add(new CommandView
        {
            CommandId = startExec.CommandId,
            CommandType = CommandType.ExecutionStart,
            Status = CommandStatus.Completed,
            IssuedAt = Samples.Time,
            UpdatedAt = Samples.Time,
            Result = new ExecutionCreatedResult { ExecutionId = "ex-vr-convert" },
        });
        Assert.That(state.ApplyEvent(execCreatedEvent, new StateChanges()), Is.True);
        Assert.That(presentation.AdvancePendingCommands(state, commands, out _), Is.Null);
        Assert.That(presentation.PendingVrConversionSequence, Is.Null);
    }

    [Test]
    public void SelectRuntimeForEnvironmentAndNextRuntimeCycleAvailableRuntimesDeterministically()
    {
        var macClaude = new RuntimeDescriptor
        {
            RuntimeId = "claude",
            Kind = "claude",
            DisplayName = "Claude Code",
            Synthetic = false,
            ApprovalMayLeaveSandbox = true,
            Capabilities = new RuntimeCapabilities { StartExecution = true },
        };
        var sandboxCodex = new RuntimeDescriptor
        {
            RuntimeId = "codex",
            Kind = "codex",
            DisplayName = "Codex",
            Synthetic = false,
            ApprovalMayLeaveSandbox = false,
            Capabilities = new RuntimeCapabilities { StartExecution = true },
        };
        var undeclaredOpenCode = new RuntimeDescriptor
        {
            RuntimeId = "opencode",
            Kind = "opencode",
            DisplayName = "OpenCode",
            Synthetic = false,
            ApprovalMayLeaveSandbox = null,
            Capabilities = new RuntimeCapabilities { StartExecution = true },
        };
        var disabledRuntime = new RuntimeDescriptor
        {
            RuntimeId = "disabled",
            Kind = "disabled",
            DisplayName = "Disabled",
            Synthetic = false,
            ApprovalMayLeaveSandbox = false,
            Capabilities = new RuntimeCapabilities { StartExecution = false },
        };
        var runtimes = new[] { macClaude, sandboxCodex, undeclaredOpenCode, disabledRuntime };

        // Toggling to Sandbox (sandbox: true) selects the available runtime with ApprovalMayLeaveSandbox == false.
        var toSandbox = WorkspacePresentationState.SelectRuntimeForEnvironment(runtimes, "claude", sandbox: true);
        Assert.That(toSandbox?.RuntimeId, Is.EqualTo("codex"));

        // Toggling back to This Mac (sandbox: false) selects the available runtime with ApprovalMayLeaveSandbox == true.
        var toMac = WorkspacePresentationState.SelectRuntimeForEnvironment(runtimes, "codex", sandbox: false);
        Assert.That(toMac?.RuntimeId, Is.EqualTo("claude"));

        // Cycling Agent (NextRuntime) steps through available StartExecution runtimes and skips disabled ones.
        Assert.That(WorkspacePresentationState.NextRuntime(runtimes, "claude")?.RuntimeId, Is.EqualTo("codex"));
        Assert.That(WorkspacePresentationState.NextRuntime(runtimes, "codex")?.RuntimeId, Is.EqualTo("opencode"));
        Assert.That(WorkspacePresentationState.NextRuntime(runtimes, "opencode")?.RuntimeId, Is.EqualTo("claude"));
        Assert.That(WorkspacePresentationState.NextRuntime(runtimes, null)?.RuntimeId, Is.EqualTo("claude"));

        var presentation = new WorkspacePresentationState();
        var changed = 0;
        presentation.Changed += () => changed++;
        Assert.That(presentation.SelectRuntime("codex"), Is.True);
        Assert.That(presentation.SelectedRuntimeId, Is.EqualTo("codex"));
        Assert.That(changed, Is.EqualTo(1));
        Assert.That(presentation.SelectRuntime("codex"), Is.False);
        presentation.ResetPendingCommands();
        Assert.That(presentation.SelectedRuntimeId, Is.Null);
    }
}

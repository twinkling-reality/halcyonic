using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The composer's Host and Agent chips name the runtime its Send instructs: the task's own execution's
/// (<c>KitPresenter.ChipsOf</c>, as the director binds them), both locked, whatever
/// a press would choose. AUDIT's four cases against U1 (2026-11-03), with the runtimes declaring as they do on macOS:
/// Codex that an approved command may leave the sandbox, OpenCode that it stays inside one, Claude Agent nothing.
/// </summary>
public class KitComposerChipsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-28T09:00:00Z");
    private static readonly ConnectionStatus Live = new(ConnectionPhase.Live);
    private static readonly KitSpineSection[] Spine = { new(new KitRowModel[0]) };

    private static RuntimeDescriptor Runtime(string id, string name, bool? mayLeave, bool synthetic = false) => new()
    {
        RuntimeId = id,
        Kind = id,
        DisplayName = name,
        Synthetic = synthetic,
        ApprovalMayLeaveSandbox = mayLeave,
        Capabilities = new RuntimeCapabilities { StartExecution = true, InstructAtRest = true, InstructWhileRunning = true, Interrupt = true },
    };

    private static RuntimeDescriptor Codex() => Runtime("codex", "Codex", true);

    private static RuntimeDescriptor OpenCode() => Runtime("opencode", "OpenCode", false);

    private static RuntimeDescriptor ClaudeAgent() => Runtime("claude", "Claude Agent", null);

    /// <summary>A running task's composer with a draft, its execution on <c>runsOn</c>, beside the registered runtimes.</summary>
    private sealed class Composer
    {
        public Composer(RuntimeDescriptor runsOn, IReadOnlyList<RuntimeDescriptor> registered, ExecutionStatus status = ExecutionStatus.Running,
            IReadOnlyList<CommandFeedback>? commands = null, bool reviewInstructions = false)
        {
            Registered = registered;
            var workstream = Samples.Workstream("w1", WorkstreamStatus.Running, "e1");
            var execution = Samples.Execution("e1", "w1", status, synthetic: false);
            execution.Runtime = new RuntimeRef { RuntimeId = runsOn.RuntimeId, Kind = runsOn.Kind, DisplayName = runsOn.DisplayName, Synthetic = runsOn.Synthetic };
            var snapshot = Samples.Snapshot(1, new[] { workstream }, new[] { execution });
            snapshot.Runtimes = registered.ToList();
            State.ApplySnapshot(snapshot, new StateChanges());
            var welcome = Samples.Welcome(resumed: false, head: 1);
            if (reviewInstructions)
                welcome.CommandPolicies = new List<CommandPolicy> { new() { CommandType = CommandType.ExecutionSendInstruction, Policy = PolicyCategory.ReviewRequired } };
            State.ApplyWelcome(welcome);
            var presented = WorkspacePresenter.Present(workstream, State, new ActivityLog(), true);
            var actions = status == ExecutionStatus.Running ? new[] { WorkspaceAction.Instruct, WorkspaceAction.Interrupt } : new[] { WorkspaceAction.Instruct };
            Work = new WorkspacePresentation(presented.Character, presented.Objective, presented.Execution, presented.Runtime, actions,
                actions.Where(presented.RequiresConfirmation).ToList(), commands ?? presented.Commands, presented.Activity);
            Steering = new WorkspaceSteering(new CommandFactory(Samples.Client), () => KitComposerChipsTests.Now);
            Screen.Instruction.Restore(execution.ExecutionId, "Run the tests again.", false, 0);
        }

        public ClientProjection State { get; } = new();
        public IReadOnlyList<RuntimeDescriptor> Registered { get; }
        public WorkspacePresentation Work { get; }
        public WorkspaceSteering Steering { get; }
        public FileScreen Screen { get; } = new() { Section = FileSection.Activity, Speak = true };
        public WorkspacePresentationState Presentation { get; } = new();

        /// <summary>The chips as the director binds them (MenuDirector.KitBindingOf).</summary>
        public KitFileBinding Bind()
        {
            var frame = FileScreens.Screen(Work, Steering, Screen, new AnswerRoom(4));
            return KitFileBinding.Of(frame, Steering.Armed, Work, Screen.Instruction.Words, Live, "Storefront", Spine,
                selectors: KitPresenter.ChipsOf(Work, Presentation.ExecutionModelDisplay(Work.Runtime, Work.Execution)))!;
        }

        /// <summary>What a Host press would choose, were the chip to take one; the locked chip takes none.</summary>
        public void ChooseOtherHost() => Presentation.CycleEnvironment(Registered, Work.Runtime?.RuntimeId);

        /// <summary>What an Agent press would choose, were the chip to take one; the locked chip takes none.</summary>
        public void ChooseNextAgent() => Presentation.CycleAgent(Registered, Work.Runtime?.RuntimeId, Work.Execution?.ModelRef);

        /// <summary>The runtime the composer's Send reaches: the execution its instruction is addressed to runs on it.</summary>
        public RuntimeDescriptor? SendReaches()
        {
            Screen.Instruction.Drawn(KitComposerChipsTests.Now);
            var outcome = Steering.SendInstruction(Screen.Instruction, Work);
            Assert.That(outcome.Step, Is.EqualTo(SteeringStep.Send), outcome.Message);
            var executionId = ((ExecutionSendInstructionCommand)outcome.Command!).Payload.ExecutionId;
            return State.RuntimeOf(State.Executions[executionId]);
        }

        /// <summary>The chips name what Send reaches, and neither takes a press.</summary>
        public void ChipsNameWhatRuns(string because)
        {
            var bound = Bind().Model;
            var runs = SendReaches();
            Assert.That((bound.Environment, bound.Agent), Is.EqualTo((KitPresenter.EnvironmentOf(runs), KitPresenter.AgentOf(runs, Work.Execution))), because);
            Assert.That((bound.EnvironmentChangeable, bound.AgentChangeable), Is.EqualTo((false, false)), "no command carries another runtime");
        }
    }

    /// <summary>What Send does with the composer's draft, given the person's choice (SWITCH): the command it sends.</summary>
    private static CommandEnvelope? Send(Composer composer, RunChoice? choice)
    {
        composer.Screen.Instruction.Drawn(Now);
        var outcome = composer.Steering.SendInstruction(composer.Screen.Instruction, composer.Work, KitPresenter.NewRunOf(composer.Work, choice));
        return outcome.Step == SteeringStep.Send ? outcome.Command : null;
    }

    [Test]
    public void AtRestAChosenAgentStartsTheTasksNextRunOnIt()
    {
        var composer = new Composer(Codex(), new[] { Codex(), OpenCode() }, ExecutionStatus.Completed);
        Assert.That(KitPresenter.AtRest(composer.Work), Is.True);
        var chips = KitPresenter.ChipsOf(composer.Work, null, OpenCode(), null, composer.Registered);
        Assert.That((chips.Environment, chips.Agent, chips.EnvironmentChangeable, chips.AgentChangeable), Is.EqualTo(("Sandbox", "OpenCode", true, true)),
            "at rest the chips are the choice, and both can change");
        var start = Send(composer, new RunChoice("opencode", null, false)) as ExecutionStartCommand;
        Assert.That(start, Is.Not.Null, "Send starts the next run");
        Assert.That((start!.Payload.WorkstreamId, start.Payload.RuntimeId, start.Payload.Instruction, start.Payload.ModelRef),
            Is.EqualTo(("w1", "opencode", "Run the tests again.", (string?)null)));
    }

    [Test]
    public void WhileTheRunIsActiveSendInstructsItWhateverIsChosen()
    {
        var composer = new Composer(Codex(), new[] { Codex(), OpenCode() });
        Assert.That(KitPresenter.NewRunOf(composer.Work, new RunChoice("opencode", null, false)), Is.Null);
        Assert.That(Send(composer, new RunChoice("opencode", null, false)), Is.InstanceOf<ExecutionSendInstructionCommand>());
        var chips = KitPresenter.ChipsOf(composer.Work, null, OpenCode(), null, composer.Registered);
        Assert.That((chips.Agent, chips.EnvironmentChangeable, chips.AgentChangeable), Is.EqualTo(("Codex", false, false)), "the execution's own, locked");
    }

    [Test]
    public void AtRestTheExecutionsOwnChoiceInstructsIt()
    {
        var composer = new Composer(Codex(), new[] { Codex(), OpenCode() }, ExecutionStatus.Interrupted);
        Assert.That(Send(composer, new RunChoice("codex", null, false)), Is.InstanceOf<ExecutionSendInstructionCommand>());
        Assert.That(Send(composer, null), Is.InstanceOf<ExecutionSendInstructionCommand>());
    }

    [Test]
    public void AStartOnARuntimeThatListsItsModelsWaitsForOne()
    {
        var composer = new Composer(Codex(), new[] { Codex(), OpenCode() }, ExecutionStatus.Completed);
        composer.Screen.Instruction.Drawn(Now);
        var outcome = composer.Steering.SendInstruction(composer.Screen.Instruction, composer.Work, new RunChoice("opencode", null, modelRequired: true));
        Assert.That((outcome.Step, outcome.Message), Is.EqualTo((SteeringStep.Explain, WorkspaceSteering.ChooseAModel)));
        var withModel = Send(composer, new RunChoice("opencode", "ollama/muse-glimmer:30b-mlx", modelRequired: true)) as ExecutionStartCommand;
        Assert.That(withModel?.Payload.ModelRef, Is.EqualTo("ollama/muse-glimmer:30b-mlx"));
    }

    [Test]
    public void AStartTheControlPlaneAsksToReviewArmsAndYesStartsIt()
    {
        var composer = new Composer(Codex(), new[] { Codex(), OpenCode() }, ExecutionStatus.Completed, reviewInstructions: true);
        composer.Screen.Instruction.Drawn(Now);
        var armed = composer.Steering.SendInstruction(composer.Screen.Instruction, composer.Work, KitPresenter.NewRunOf(composer.Work, new RunChoice("opencode", null, false)));
        Assert.That(armed.Step, Is.EqualTo(SteeringStep.Confirm));
        var request = composer.Steering.ToRead(composer.Work);
        composer.Screen.ReadRequest(request!, 1, 1, composer.Steering);
        composer.Screen.RequestDrawn(0, composer.Steering, Now);
        var confirmed = composer.Steering.Confirm(composer.Work);
        Assert.That((confirmed.Command as ExecutionStartCommand)?.Payload.RuntimeId, Is.EqualTo("opencode"), confirmed.Message);
    }

    [Test]
    public void NoStartIsOfferedWhileACommandToTheRunIsUnderWay()
    {
        var underWay = new[] { new CommandFeedback("c1", CommandType.ExecutionSendInstruction, CommandStatus.Accepted, "Sent") };
        var composer = new Composer(Codex(), new[] { Codex(), OpenCode() }, ExecutionStatus.Completed, underWay);
        Assert.That(KitPresenter.AtRest(composer.Work), Is.False);
        Assert.That(KitPresenter.NewRunOf(composer.Work, new RunChoice("opencode", null, false)), Is.Null);
    }

    [Test]
    public void SendSaysItStartsANewRunWhereItWould()
    {
        var composer = new Composer(Codex(), new[] { Codex(), OpenCode() }, ExecutionStatus.Completed);
        composer.Screen.StartsNewRun = true;
        var frame = FileScreens.Screen(composer.Work, composer.Steering, composer.Screen, new AnswerRoom(4));
        Assert.That(frame.Footer.All.Select(slot => slot.Prompt).Where(prompt => prompt.Id == FileScreens.SendInstruction).Select(prompt => prompt.Words),
            Is.EqualTo(new[] { FileScreens.StartNewRun }));
    }

    [Test]
    public void OnTheOwnersMacACodexTaskSaysThisMacThoughOpenCodeStaysInside()
    {
        var composer = new Composer(Codex(), new[] { Runtime("mock", "Simulated agent", null, synthetic: true), OpenCode(), Codex() });
        composer.ChipsNameWhatRuns("before any press");
        composer.ChooseOtherHost();
        Assert.That(composer.Presentation.SelectedRuntimeId, Is.EqualTo("opencode"), "the choice a Host press would make exists");
        composer.ChipsNameWhatRuns("Send still instructs the Codex execution, which may leave the sandbox");
    }

    [Test]
    public void ACodexTaskSaysThisMacThoughASandboxedRuntimeIsRegistered()
    {
        var composer = new Composer(Codex(), new[] { Codex(), Runtime("sandboxed", "Sandboxed agent", false) });
        composer.ChooseOtherHost();
        composer.ChipsNameWhatRuns("a Host choice is not what runs");
    }

    [Test]
    public void AnOpenCodeTaskSaysSandboxAndOpenCodeWhateverAgentIsNext()
    {
        var composer = new Composer(OpenCode(), new[] { Codex(), OpenCode(), ClaudeAgent() });
        composer.ChooseNextAgent();
        Assert.That(composer.Presentation.SelectedRuntimeId, Is.EqualTo("claude"), "the choice an Agent press would make exists");
        composer.ChipsNameWhatRuns("Send still instructs the OpenCode execution, inside its sandbox");
    }

    [Test]
    public void ACodexTaskKeepsTheThisMacItRunsOnWhenTheNextAgentDeclaresNothing()
    {
        var composer = new Composer(Codex(), new[] { Codex(), ClaudeAgent() });
        composer.ChooseNextAgent();
        composer.ChipsNameWhatRuns("the Host chip stays This Mac: the Codex execution is what runs");
        Assert.That(composer.Bind().Model.Environment, Is.EqualTo("This Mac"));
    }

    [Test]
    public void ATaskOnARuntimeThatDeclaresNothingShowsNoHostChip()
    {
        var composer = new Composer(ClaudeAgent(), new[] { ClaudeAgent(), Codex() });
        composer.ChooseNextAgent();
        composer.ChipsNameWhatRuns("never an environment the runtime that runs did not declare");
        Assert.That((composer.Bind().Model.Environment, composer.Bind().Model.Agent), Is.EqualTo(((string?)null, "Claude Agent")));
    }
}

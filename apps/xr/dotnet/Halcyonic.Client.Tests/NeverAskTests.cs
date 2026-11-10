using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The level of a task's next start (ADR 0043) in the client: kept per project, sent as never ask only where the
/// person chose it AND the runtime's descriptor offers it, left out otherwise (which means ask); the level a start
/// will use shown before it goes, and the run's own recorded level after, never the choice.
/// </summary>
public class NeverAskTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-10T09:00:00Z");
    private static readonly ConnectionStatus Live = new(ConnectionPhase.Live);
    private static readonly KitSpineSection[] Spine = { new(new KitRowModel[0]) };

    private static RuntimeDescriptor Runtime(string id, bool? offersNeverAsk) => new()
    {
        RuntimeId = id, Kind = id, DisplayName = id, Synthetic = false, OffersNeverAsk = offersNeverAsk,
        Capabilities = new RuntimeCapabilities { StartExecution = true, InstructAtRest = true, InstructWhileRunning = true, Interrupt = true },
    };

    /// <summary>A task whose run on <c>runsOn</c> is at rest (or running), its record carrying <c>recorded</c>, beside the registered runtimes.</summary>
    private sealed class Task
    {
        public Task(RuntimeDescriptor runsOn, IReadOnlyList<RuntimeDescriptor> registered, ApprovalLevel? recorded = null, ExecutionStatus status = ExecutionStatus.Completed)
        {
            Registered = registered;
            var workstream = Samples.Workstream("w1", WorkstreamStatus.Running, "e1");
            var execution = Samples.Execution("e1", "w1", status, synthetic: false);
            execution.Runtime = new RuntimeRef { RuntimeId = runsOn.RuntimeId, Kind = runsOn.Kind, DisplayName = runsOn.DisplayName, Synthetic = false };
            execution.ApprovalLevel = recorded;
            var state = new ClientProjection();
            var snapshot = Samples.Snapshot(1, new[] { workstream }, new[] { execution });
            snapshot.Runtimes = registered.ToList();
            state.ApplySnapshot(snapshot, new StateChanges());
            state.ApplyWelcome(Samples.Welcome(resumed: false, head: 1));
            var presented = WorkspacePresenter.Present(workstream, state, new ActivityLog(), true);
            var actions = status == ExecutionStatus.Running ? new[] { WorkspaceAction.Instruct, WorkspaceAction.Interrupt } : new[] { WorkspaceAction.Instruct };
            Work = new WorkspacePresentation(presented.Character, presented.Objective, presented.Execution, presented.Runtime, actions,
                actions.Where(presented.RequiresConfirmation).ToList(), presented.Commands, presented.Activity);
            Screen.Instruction.Restore(execution.ExecutionId, "Run the tests again.", false, 0);
        }

        public IReadOnlyList<RuntimeDescriptor> Registered { get; }
        public WorkspacePresentation Work { get; }
        public WorkspaceSteering Steering { get; } = new(new CommandFactory(Samples.Client), () => Now);
        public FileScreen Screen { get; } = new() { Section = FileSection.Activity, Speak = true };

        /// <summary>The choice as the director makes it for this task (MenuDirector.RunChoiceNow).</summary>
        public RunChoice? Choice(string? selectedRuntime, bool neverAskChosen) => KitPresenter.RunChoiceOf(Work, selectedRuntime, null, Registered, neverAskChosen);

        /// <summary>What Send does with the draft, given the choice: the command it sends.</summary>
        public CommandEnvelope? Send(RunChoice? choice)
        {
            Screen.Instruction.Drawn(Now);
            var outcome = Steering.SendInstruction(Screen.Instruction, Work, KitPresenter.NewRunOf(Work, choice));
            return outcome.Step == SteeringStep.Send ? outcome.Command : null;
        }

        public KitWorkModel Model(string? selectedRuntime, bool neverAskChosen)
        {
            var chosen = selectedRuntime != null ? Registered.First(item => item.RuntimeId == selectedRuntime) : null;
            var frame = FileScreens.Screen(Work, Steering, Screen, new AnswerRoom(4));
            return KitFileBinding.Of(frame, Steering.Armed, Work, Screen.Instruction.Words, Live, "Storefront", Spine,
                selectors: KitPresenter.ChipsOf(Work, null, chosen, null, Registered, neverAskChosen))!.Model;
        }
    }

    [Test]
    public void TheLevelIsKeptPerProjectAndSaysWhenItChanged()
    {
        var state = new WorkspacePresentationState();
        var changes = 0;
        state.Changed += () => changes++;
        Assert.That(state.NeverAskFor("p1"), Is.False, "ask until the person chooses otherwise");
        Assert.That(state.ChooseNeverAsk("p1", true), Is.True);
        Assert.That(state.ChooseNeverAsk("p1", true), Is.False, "the same choice again changes nothing");
        Assert.That((state.NeverAskFor("p1"), state.NeverAskFor("p2"), state.NeverAskFor(null), changes), Is.EqualTo((true, false, false, 1)));
        Assert.That(state.ChooseNeverAsk("p1", false), Is.True);
        Assert.That((state.NeverAskFor("p1"), changes), Is.EqualTo((false, 2)));
        state.ChooseNeverAsk("p1", true);
        state.ResetPendingCommands();
        Assert.That(state.NeverAskFor("p1"), Is.False, "it goes where the chosen runtime goes");
    }

    [Test]
    public void NeverAskGoesWithAStartOnlyWhereChosenAndOffered()
    {
        var offering = Runtime("codex", true);
        var plain = Runtime("opencode", null);
        var task = new Task(offering, new[] { offering, plain });

        // Chosen and offered, with no other runtime picked: the task's next run starts on its own runtime at never ask.
        var start = task.Send(task.Choice(null, neverAskChosen: true)) as ExecutionStartCommand;
        Assert.That(start, Is.Not.Null, "the level applies to a start, so the next instruction starts a run");
        Assert.That((start!.Payload.RuntimeId, start.Payload.ApprovalLevel), Is.EqualTo(("codex", (ApprovalLevel?)ApprovalLevel.NeverAsk)));

        // Not chosen: nothing changes, the instruction goes to the run as before, and no start is made for it.
        Assert.That(task.Choice(null, neverAskChosen: false), Is.Null);
        Assert.That(new Task(offering, new[] { offering, plain }).Send(null), Is.InstanceOf<ExecutionSendInstructionCommand>());

        // Chosen, on a runtime that does not offer it: never sent. A start on it leaves the level out, which means ask.
        var onPlain = new Task(plain, new[] { offering, plain });
        Assert.That(onPlain.Choice(null, neverAskChosen: true), Is.Null, "nothing to start for: the run's own runtime offers none");
        var picked = new Task(offering, new[] { offering, plain });
        var other = picked.Send(picked.Choice("opencode", neverAskChosen: true)) as ExecutionStartCommand;
        Assert.That((other!.Payload.RuntimeId, other.Payload.ApprovalLevel), Is.EqualTo(("opencode", (ApprovalLevel?)null)));

        // A descriptor that says false, or says nothing, offers nothing.
        var refusing = Runtime("claude", false);
        Assert.That(new Task(refusing, new[] { refusing }).Choice(null, neverAskChosen: true), Is.Null);
    }

    [Test]
    public void TheRunsOwnRecordDecidesWhetherTheLevelDiffers()
    {
        var offering = Runtime("codex", true);
        // The last run ran at never ask and the person still chooses it: no new start, the instruction goes to the run.
        var same = new Task(offering, new[] { offering }, recorded: ApprovalLevel.NeverAsk);
        Assert.That(same.Choice(null, neverAskChosen: true), Is.Null);
        // Back to ask after a never-ask run: the next instruction starts a run that leaves the level out.
        var back = new Task(offering, new[] { offering }, recorded: ApprovalLevel.NeverAsk);
        var start = back.Send(back.Choice(null, neverAskChosen: false)) as ExecutionStartCommand;
        Assert.That(start, Is.Not.Null);
        Assert.That(start!.Payload.ApprovalLevel, Is.Null, "left out means ask");
        // While the run works nothing starts, whatever is chosen.
        var working = new Task(offering, new[] { offering }, status: ExecutionStatus.Running);
        Assert.That(working.Send(working.Choice(null, neverAskChosen: true)), Is.InstanceOf<ExecutionSendInstructionCommand>(), "a run under way keeps its level");
    }

    [Test]
    public void TheModelShowsTheLevelTheNextStartWillUseAndTheRunsOwnRecord()
    {
        var offering = Runtime("codex", true);
        var plain = Runtime("opencode", null);

        var rest = new Task(offering, new[] { offering, plain }, recorded: ApprovalLevel.Ask);
        var chosen = rest.Model(null, neverAskChosen: true);
        Assert.That((chosen.OffersNeverAsk, chosen.NeverAsk, chosen.RunNeverAsk), Is.EqualTo((true, true, (bool?)false)),
            "the next start's level before it goes, beside what the last run recorded");
        var asking = rest.Model(null, neverAskChosen: false);
        Assert.That((asking.OffersNeverAsk, asking.NeverAsk), Is.EqualTo((true, false)));

        // The chosen runtime does not offer it: the choice is not the level, and nothing says never ask.
        var notOffered = rest.Model("opencode", neverAskChosen: true);
        Assert.That((notOffered.OffersNeverAsk, notOffered.NeverAsk), Is.EqualTo((false, false)));

        // A run with no recorded level says none; a running one shows its own record, and no choice for a next start.
        var old = new Task(offering, new[] { offering }).Model(null, neverAskChosen: true);
        Assert.That(old.RunNeverAsk, Is.Null, "an older run's record carries no level");
        var working = new Task(offering, new[] { offering }, recorded: ApprovalLevel.NeverAsk, status: ExecutionStatus.Running).Model(null, neverAskChosen: false);
        Assert.That((working.OffersNeverAsk, working.NeverAsk, working.RunNeverAsk), Is.EqualTo((false, false, (bool?)true)), "the run's own record, never the choice");
    }
}

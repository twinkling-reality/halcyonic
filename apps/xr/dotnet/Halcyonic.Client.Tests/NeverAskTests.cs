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
        public Task(RuntimeDescriptor runsOn, IReadOnlyList<RuntimeDescriptor> registered, ApprovalLevel? recorded = null, ExecutionStatus status = ExecutionStatus.Completed,
            List<CommandPolicy>? policies = null)
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
            var welcome = Samples.Welcome(resumed: false, head: 1);
            if (policies != null) welcome.CommandPolicies = policies;
            state.ApplyWelcome(welcome);
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

    /// <summary>A device's preferences, in memory, as the headset keeps them.</summary>
    private sealed class Kept : IDevicePreferences
    {
        public readonly Dictionary<string, string> Values = new();
        public string? Get(string key) => Values.TryGetValue(key, out var value) ? value : null;
        public void Set(string key, string? value) { if (value == null) Values.Remove(key); else Values[key] = value; }
    }

    [Test]
    public void NeverAskIsTheDefaultOnlyWhereTheLevelIsShownBeforeTheStart()
    {
        // The redesign's row names the level at rest: there the default is never ask.
        var shown = new WorkspacePresentationState { NeverAskByDefault = () => true };
        var changes = 0;
        shown.Changed += () => changes++;
        Assert.That((shown.NeverAskFor("p1"), shown.NeverAskFor(null)), Is.EqualTo((true, false)), "never ask until the person chooses Ask First; no project, no choice");
        Assert.That(shown.ChooseNeverAsk("p1", true), Is.False, "choosing the default changes nothing");
        Assert.That(shown.ChooseNeverAsk("p1", false), Is.True);
        Assert.That(shown.ChooseNeverAsk("p1", false), Is.False, "the same choice again changes nothing");
        Assert.That((shown.NeverAskFor("p1"), shown.NeverAskFor("p2"), changes), Is.EqualTo((false, true, 1)));
        Assert.That(shown.ChooseNeverAsk("p1", true), Is.True, "going back is the same press");
        Assert.That((shown.NeverAskFor("p1"), changes), Is.EqualTo((true, 2)));
        shown.ChooseNeverAsk("p1", false);
        shown.ResetPendingCommands();
        Assert.That(shown.NeverAskFor("p1"), Is.False, "a reset of the session forgets no project's choice");

        // The older kit names no level: there the default is Ask First, and only the person's own choice is never ask.
        var was = RoomDesign.Enabled;
        try
        {
            RoomDesign.Enabled = false;
            var older = new WorkspacePresentationState();
            Assert.That(older.NeverAskFor("p1"), Is.False, "nothing names the level, so nothing starts at never ask by default");
            older.ChooseNeverAsk("p1", true);
            Assert.That((older.NeverAskFor("p1"), older.NeverAskChosenFor("p1"), older.NeverAskChosenFor("p2")), Is.EqualTo((true, true, false)));
            RoomDesign.Enabled = true;
            Assert.That(new WorkspacePresentationState().NeverAskFor("p1"), Is.True, "the redesign on: the default follows it");
            Assert.That(new WorkspacePresentationState().NeverAskChosenFor("p1"), Is.False, "a default is never a choice");
        }
        finally { RoomDesign.Enabled = was; }

        // New work asks unless the flow that shows the level before the start sets it.
        Assert.That(new NewWorkDraft(new CommandFactory(Samples.Client)).NeverAsk, Is.False);
    }

    [Test]
    public void EachProjectsChoiceSurvivesARestartOfTheHeadsetApp()
    {
        var device = new Kept();
        var before = new WorkspacePresentationState { Preferences = device, NeverAskByDefault = () => true };
        before.UseProject("p1");
        Assert.That(before.SelectRuntime("codex", "gpt-5"), Is.True);
        before.ChooseNeverAsk("p1", false);
        before.UseProject("p2");
        Assert.That((before.SelectedRuntimeId, before.SelectedModelRef), Is.EqualTo(((string?)null, (string?)null)), "another project keeps its own choice, none here");
        before.SelectRuntime("opencode");
        Assert.That(device.Values.ContainsKey(WorkspacePresentationState.ProjectChoicesKey), Is.True, "kept where the device keeps its settings");

        // The headset app starts again.
        var after = new WorkspacePresentationState { Preferences = device, NeverAskByDefault = () => true };
        Assert.That((after.NeverAskFor("p1"), after.NeverAskFor("p2")), Is.EqualTo((false, true)), "Ask First kept; the other project still at the default");
        Assert.That(after.UseProject("p1"), Is.True);
        Assert.That((after.SelectedRuntimeId, after.SelectedModelRef), Is.EqualTo(("codex", "gpt-5")));
        after.UseProject("p2");
        Assert.That((after.SelectedRuntimeId, after.SelectedModelRef), Is.EqualTo(("opencode", (string?)null)));
        Assert.That(after.UseProject("p2"), Is.False, "the same project again changes nothing");

        // What cannot be read is no choice, never a failure.
        device.Values[WorkspacePresentationState.ProjectChoicesKey] = "{not json";
        Assert.That(new WorkspacePresentationState { Preferences = device, NeverAskByDefault = () => true }.NeverAskFor("p1"), Is.True);
        Assert.That(ProjectChoices.Read("[1,2]").Of("p1"), Is.Null);
    }

    [Test]
    public void AKeptLevelAppliesOnlyWhereTheRuntimeStillOffersIt()
    {
        var device = new Kept();
        var state = new WorkspacePresentationState { Preferences = device, NeverAskByDefault = () => true };
        Assert.That(state.NeverAskFor("p1"), Is.True);
        // The runtime no longer offers it: the next start asks, and the model says so.
        var plain = Runtime("codex", false);
        var task = new Task(plain, new[] { plain });
        Assert.That(task.Choice(null, state.NeverAskFor("p1")), Is.Null, "nothing to start for: the run's own level stands");
        var model = task.Model(null, state.NeverAskFor("p1"));
        Assert.That((model.OffersNeverAsk, model.NeverAsk), Is.EqualTo((false, false)), "the level shown is the level the start would use: ask");
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

    [Test]
    public void AnArmedStartGoesOnlyOnTheChoiceItWasArmedWith()
    {
        var offering = Runtime("codex", true);
        var policies = new List<CommandPolicy> { new() { CommandType = CommandType.ExecutionSendInstruction, Policy = PolicyCategory.ReviewRequired } };

        // Armed at ask, on the run's own agent: the person then chooses never ask. Confirm sends nothing and says so.
        RunChoice? now = null;
        var task = new Task(offering, new[] { offering }, policies: policies);
        task.Steering.ChoiceNow = () => KitPresenter.NewRunOf(task.Work, now);
        task.Screen.Instruction.Drawn(Now);
        Assert.That(task.Steering.SendInstruction(task.Screen.Instruction, task.Work, KitPresenter.NewRunOf(task.Work, now)).Step, Is.EqualTo(SteeringStep.Confirm));
        now = task.Choice(null, neverAskChosen: true);
        Assert.That(now, Is.Not.Null);
        var changed = task.Steering.Confirm(task.Work);
        Assert.That((changed.Step, changed.Command, changed.Message), Is.EqualTo((SteeringStep.Explain, (CommandEnvelope?)null, "Not Sent · Task Changed · Check and Retry")),
            "the level chosen since is not the level armed: nothing goes");
        Assert.That(task.Steering.Armed, Is.Null);

        // Armed at never ask: the person goes back to ask before confirming. Nothing starts at never ask.
        var armedNever = new Task(offering, new[] { offering }, policies: policies);
        RunChoice? chosen = armedNever.Choice(null, neverAskChosen: true);
        armedNever.Steering.ChoiceNow = () => KitPresenter.NewRunOf(armedNever.Work, chosen);
        armedNever.Screen.Instruction.Drawn(Now);
        Assert.That(armedNever.Steering.SendInstruction(armedNever.Screen.Instruction, armedNever.Work, KitPresenter.NewRunOf(armedNever.Work, chosen)).Step, Is.EqualTo(SteeringStep.Confirm));
        chosen = null;
        Assert.That(armedNever.Steering.Refresh(armedNever.Work), Is.EqualTo("Not Sent · Task Changed · Check and Retry"), "the confirmation drops as soon as the choice is another");

        // The same choice still: Confirm starts the run at the level it was armed with and shown.
        var kept = new Task(offering, new[] { offering }, policies: policies);
        var same = kept.Choice(null, neverAskChosen: true);
        kept.Steering.ChoiceNow = () => KitPresenter.NewRunOf(kept.Work, kept.Choice(null, neverAskChosen: true));
        kept.Screen.Instruction.Drawn(Now);
        Assert.That(kept.Steering.SendInstruction(kept.Screen.Instruction, kept.Work, KitPresenter.NewRunOf(kept.Work, same)).Step, Is.EqualTo(SteeringStep.Confirm));
        kept.Steering.RequestShown(1, 1);
        var sent = kept.Steering.Confirm(kept.Work);
        Assert.That(sent.Step, Is.EqualTo(SteeringStep.Send), sent.Message);
        Assert.That(((ExecutionStartCommand)sent.Command!).Payload.ApprovalLevel, Is.EqualTo(ApprovalLevel.NeverAsk));
    }
}

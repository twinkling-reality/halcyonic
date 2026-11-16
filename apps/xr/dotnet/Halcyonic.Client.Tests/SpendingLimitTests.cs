using System;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// SCREENS's spending-limit unit: a task at its limit says so, and its composer carries on only under a new limit the person
/// pressed, sent exactly as shown (AUDIT2's words, 23:14Z; AUDIT3's S1 to S6, 23:14Z).
/// </summary>
public sealed class SpendingLimitTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-16T23:20:00Z");

    private sealed class Composer
    {
        public Composer(SpendingCapView? cap, bool reviewInstructions = false)
        {
            var runtime = new RuntimeDescriptor
            {
                RuntimeId = "claude", Kind = "claude", DisplayName = "Claude Agent", OffersSpendingCap = true,
                Capabilities = new RuntimeCapabilities { StartExecution = true, InstructAtRest = true, InstructWhileRunning = true, Interrupt = true },
            };
            var workstream = Samples.Workstream("w1", WorkstreamStatus.Completed, "e1");
            var execution = Samples.Execution("e1", "w1", ExecutionStatus.Completed, synthetic: false);
            execution.Runtime = new RuntimeRef { RuntimeId = runtime.RuntimeId, Kind = runtime.Kind, DisplayName = runtime.DisplayName };
            execution.SpendingCap = cap;
            var snapshot = Samples.Snapshot(1, new[] { workstream }, new[] { execution });
            snapshot.Runtimes = new() { runtime };
            var state = new ClientProjection();
            state.ApplySnapshot(snapshot, new StateChanges());
            var welcome = Samples.Welcome(resumed: false, head: 1);
            if (reviewInstructions)
                welcome.CommandPolicies = new() { new() { CommandType = CommandType.ExecutionSendInstruction, Policy = PolicyCategory.ReviewRequired } };
            state.ApplyWelcome(welcome);
            var presented = WorkspacePresenter.Present(workstream, state, new ActivityLog(), true);
            var actions = new[] { WorkspaceAction.Instruct };
            Work = new WorkspacePresentation(presented.Character, presented.Objective, presented.Execution, presented.Runtime, actions,
                actions.Where(presented.RequiresConfirmation).ToList(), presented.Commands, presented.Activity);
            Steering = new WorkspaceSteering(new CommandFactory(Samples.Client), () => SpendingLimitTests.Now);
            Screen.Instruction.Restore("e1", "Finish the tests.", false, 0);
            Screen.Instruction.Drawn(SpendingLimitTests.Now);
        }

        public WorkspacePresentation Work { get; }
        public WorkspaceSteering Steering { get; }
        public FileScreen Screen { get; } = new() { Section = FileSection.Activity, Speak = true };
        public MenuFrame Frame => FileScreens.Screen(Work, Steering, Screen, new AnswerRoom(8));
        public PageLine[] Amounts => Frame.Lines.Where(line => line.Action == FileScreens.ChooseLimit).ToArray();
    }

    private static SpendingCapView Cap(SpendingCapViewState state, double limit, double? spent) => new() { State = state, LimitUsd = limit, SpentUsd = spent };

    [Test]
    public void ATasksLimitIsSaidAsItIsNeverGuessed()
    {
        static ExecutionView With(SpendingCapView? cap) => new() { SpendingCap = cap };
        Assert.That(SpendingLimit.Of(With(Cap(SpendingCapViewState.Reached, 1, 0.3))), Is.EqualTo(((string?)"At Spending Limit", "Spent $0.30 of $1.00")));
        Assert.That(SpendingLimit.Of(With(Cap(SpendingCapViewState.Reached, 1, null))), Is.EqualTo(((string?)"At Spending Limit", "Limit $1.00")), "no spent figure where none is known");
        Assert.That(SpendingLimit.Of(With(Cap(SpendingCapViewState.Waiting, 2.5, null))), Is.EqualTo(((string?)"Spending Not Known", "Limit $2.50")));
        Assert.That(SpendingLimit.Of(With(Cap(SpendingCapViewState.Waiting, 2.5, 1.2))), Is.EqualTo(((string?)"Spending Not Known", "Limit $2.50")), "waiting never shows a figure (S3)");
        Assert.That(SpendingLimit.Of(With(Cap(SpendingCapViewState.Open, 5, 0.1))), Is.EqualTo(((string?)null, "Limit $5.00")));
        Assert.That(SpendingLimit.Of(With(null)), Is.Null);
    }

    [TestCase(SpendingCapViewState.Reached)]
    [TestCase(SpendingCapViewState.Waiting)]
    public void ATaskAtItsLimitCarriesOnOnlyUnderTheAmountThePersonPressed(SpendingCapViewState state)
    {
        var composer = new Composer(Cap(state, 1, state == SpendingCapViewState.Reached ? 1.02 : null));
        Assert.That(composer.Amounts.Select(line => line.Words), Is.EqualTo(new[] { "$1 More", "$5 More", "$10 More" }));
        Assert.That(composer.Amounts.Any(line => line.Chosen), Is.False, "none is chosen until pressed (S1)");
        Assert.That(composer.Amounts[0].Under?.Text, Is.EqualTo(SpendingLimit.CountsFromNow), "new money from now, beside the amounts (S4)");
        var send = composer.Frame.Footer[PromptSlot.FarRight]!;
        Assert.That((send.Available, send.Reason), Is.EqualTo((false, (string?)SpendingLimit.ChooseAmountFirst)));
        var refused = composer.Steering.SendInstruction(composer.Screen.Instruction, composer.Work);
        Assert.That((refused.Step, refused.Command), Is.EqualTo((SteeringStep.Explain, (CommandEnvelope?)null)), "no command without a limit (S5)");

        composer.Screen.Instruction.ChooseLimit(SpendingLimit.FromKey(composer.Amounts[1].Key)!.Value);
        Assert.That(composer.Amounts.Select(line => line.Chosen), Is.EqualTo(new[] { false, true, false }));
        Assert.That(composer.Frame.Footer[PromptSlot.FarRight]!.Available, Is.True);
        var sent = composer.Steering.SendInstruction(composer.Screen.Instruction, composer.Work);
        Assert.That(sent.Step, Is.EqualTo(SteeringStep.Send), sent.Message);
        var payload = ((ExecutionSendInstructionCommand)sent.Command!).Payload;
        Assert.That((payload.Text, payload.SpendingCapUsd), Is.EqualTo(("Finish the tests.", (double?)5)), "exactly the shown amount, with the person's words (S2)");
    }

    [Test]
    public void TheKitsComposerBindsTheAmountsAsChoices()
    {
        var composer = new Composer(Cap(SpendingCapViewState.Reached, 1, 1));
        var binding = KitFileBinding.Of(composer.Frame, composer.Steering.Armed, composer.Work, composer.Screen.Instruction.Words,
            new ConnectionStatus(ConnectionPhase.Live), "Storefront", new[] { new KitSpineSection(new KitRowModel[0]) }, roomDesign: true);
        Assert.That(binding, Is.Not.Null, "the composer stays the kit's, its amounts among its controls");
        Assert.That(binding!.Controls.Where(control => control.Action == FileScreens.ChooseLimit).Select(control => (control.Place, control.Words)),
            Is.EqualTo(new[] { (KitPlace.Choice, "$1 More"), (KitPlace.Choice, "$5 More"), (KitPlace.Choice, "$10 More") }));
        Assert.That(binding.Controls.First(control => control.Action == FileScreens.ChooseLimit).Under?.Text, Is.EqualTo(SpendingLimit.CountsFromNow),
            "counts from now travels with the presses (S4)");
    }

    [Test]
    public void TheConfirmShowsTheAmountWholeAndSendsIt()
    {
        var composer = new Composer(Cap(SpendingCapViewState.Reached, 1, 1), reviewInstructions: true);
        composer.Screen.Instruction.ChooseLimit(10);
        var armed = composer.Steering.SendInstruction(composer.Screen.Instruction, composer.Work);
        Assert.That(armed.Step, Is.EqualTo(SteeringStep.Confirm), armed.Message);
        Assert.That(composer.Steering.ArmedLimit, Is.EqualTo(10));
        Assert.That(SpendingLimit.Confirm(10), Is.EqualTo("Send Instruction, Spend Up To $10.00 More"));
        composer.Steering.Cancel();
        Assert.That(composer.Steering.ArmedLimit, Is.Null, "a cancelled confirm keeps no amount");
    }

    [Test]
    public void NoOtherTaskOrPathCarriesALimit()
    {
        var open = new Composer(Cap(SpendingCapViewState.Open, 5, 0.1));
        Assert.That(open.Amounts, Is.Empty, "an open limit asks for nothing");
        var plain = open.Steering.SendInstruction(open.Screen.Instruction, open.Work);
        Assert.That(((ExecutionSendInstructionCommand)plain.Command!).Payload.SpendingCapUsd, Is.Null, "no path sends a limit without the press (S5)");

        var none = new Composer(null);
        Assert.That(none.Amounts, Is.Empty);
        Assert.That(((ExecutionSendInstructionCommand)none.Steering.SendInstruction(none.Screen.Instruction, none.Work).Command!).Payload.SpendingCapUsd, Is.Null);

        var reached = new Composer(Cap(SpendingCapViewState.Reached, 1, 1));
        reached.Screen.Instruction.ChooseLimit(5);
        reached.Screen.Instruction.Close();
        Assert.That(reached.Screen.Instruction.NewLimit, Is.Null, "closing the composer drops the amount; it never carries over");
        reached.Screen.Instruction.ChooseLimit(5);
        reached.Screen.Instruction.ChooseLimit(5);
        Assert.That(reached.Screen.Instruction.NewLimit, Is.Null, "pressing the chosen amount again takes it back");
        Assert.That(SpendingLimit.FromKey("7.00"), Is.Null, "only an offered amount is ever chosen");
    }
}

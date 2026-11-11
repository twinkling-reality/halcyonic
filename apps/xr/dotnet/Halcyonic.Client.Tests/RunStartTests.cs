using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// What a run's start was, from the task's own record (SCREENS's <see cref="RunStart"/>, the view's plan, plan_left and
/// unconfined_reason): the result's card says Planning, Plan Ready or nothing, and Not Kept to Folder as a slot, in
/// FIELD's words; Carry Out Plan stands only for a plan at rest on a runtime that offers plans, and sends leave_plan.
/// </summary>
public class RunStartTests
{
    private const string NotConfined = "Halcyonic's sandbox is on macOS only, so this run is not confined.";

    private static ExecutionView Run(ExecutionStatus status, bool? plan = null, bool? left = null, string? reason = null) => new()
    {
        ExecutionId = "e1", WorkstreamId = "w1", ProjectId = "p1", Instruction = "Plan the kite.", Status = status, Plan = plan, PlanLeft = left, UnconfinedReason = reason,
        Runtime = new RuntimeRef { RuntimeId = "opencode", Kind = "opencode", DisplayName = "Agent", Synthetic = false },
    };

    private static readonly RuntimeDescriptor Planning = new() { RuntimeId = "opencode", DisplayName = "Agent", Kind = "opencode", OffersPlan = true, Capabilities = new RuntimeCapabilities() };

    [Test]
    public void TheRecordSaysWhetherTheTaskIsAPlanAndWhetherItCanBeCarriedOutNow()
    {
        Assert.That(RunStart.Of(null), Is.Null);
        var none = RunStart.Of(Run(ExecutionStatus.Completed))!.Value;
        Assert.That((none.IsPlan, none.NotConfined, none.CanCarryOut), Is.EqualTo((false, false, false)), "absent never reads as a plan, nor as not kept");
        var ready = RunStart.Of(Run(ExecutionStatus.Completed, plan: true), Planning)!.Value;
        Assert.That((ready.IsPlan, ready.AtRest, ready.CanCarryOut), Is.EqualTo((true, true, true)));
        Assert.That(RunStart.Of(Run(ExecutionStatus.Running, plan: true), Planning)!.Value.CanCarryOut, Is.False, "only at rest");
        Assert.That(RunStart.Of(Run(ExecutionStatus.Completed, plan: true))!.Value.CanCarryOut, Is.False, "only where the runtime offers plans");
        var carried = RunStart.Of(Run(ExecutionStatus.Completed, plan: true, left: true), Planning)!.Value;
        Assert.That((carried.IsPlan, carried.CanCarryOut), Is.EqualTo((false, false)), "carried out: a task as any other");
        Assert.That(RunStart.Of(Run(ExecutionStatus.Completed, reason: NotConfined))!.Value.NotConfinedReason, Is.EqualTo(NotConfined));
    }

    [Test]
    public void TheResultCardSaysPlanReadyOrPlanningAndNotKeptToFolderInFieldsWords()
    {
        RoomCard Card(RunStart start, bool? passed = null) =>
            RoomPanelCards.Result(new RoomResult(false, false, false, passed, null, null, start: start), LineWords.TheirName("Kite Run"))!;
        var ready = Card(RunStart.Of(Run(ExecutionStatus.Completed, plan: true), Planning)!.Value).Status!.Value;
        Assert.That((ready.Title, ready.Line, ready.Tone), Is.EqualTo((RunStart.PlanReady, RunStart.PlanReadyLine, RoomTone.Attention)));
        var planning = Card(RunStart.Of(Run(ExecutionStatus.Failed, plan: true), Planning)!.Value).Status!.Value;
        Assert.That((planning.Title, planning.Tone), Is.EqualTo((RunStart.Planning, RoomTone.Working)));
        Assert.That(new RoomResult(false, false, false, null, null, null, start: RunStart.Of(Run(ExecutionStatus.Completed, plan: true, left: true))).Any, Is.False,
            "a plan carried out says nothing new");

        var loose = Card(RunStart.Of(Run(ExecutionStatus.Completed, reason: NotConfined))!.Value, passed: true);
        var slots = loose.Slots.Select(slot => (slot.Kind, slot.Value.Text)).ToList();
        Assert.That(slots, Does.Contain((RoomKind.Folder, RunStart.NotKeptToFolder)));
        Assert.That(loose.Words(), Has.None.Contains("onfined").And.None.Contains("andbox"), "never the mechanism's words on a screen");
        Assert.That(loose.AnyDot, Is.False);
    }

    [Test]
    public void CarryOutPlanSendsOneInstructionThatLeavesThePlan()
    {
        var command = new CommandFactory(Samples.Client).SendInstruction("e1", RunStart.CarryOutInstruction, leavePlan: true);
        Assert.That((command.Payload.ExecutionId, command.Payload.LeavePlan), Is.EqualTo(("e1", (bool?)true)));
        Assert.That(new CommandFactory(Samples.Client).SendInstruction("e1", "Go on.").Payload.LeavePlan, Is.Null, "an ordinary instruction leaves nothing");
    }
}

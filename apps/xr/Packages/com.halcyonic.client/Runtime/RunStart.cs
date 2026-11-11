#nullable enable
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// What a run's start was, from the task's own record (<see cref="ExecutionView"/>: VRIFY's <c>plan</c>, <c>plan_left</c>,
    /// BOX-4's <c>unconfined_reason</c>): whether it is a plan still, whether its plan was carried out, and, where a start
    /// that asked for Halcyonic's sandbox ran without it or is not counted inside it, the Mac's reason. Absent means not
    /// recorded, which never reads as a plan, and a run is said to be not kept to its folder only where the Mac says so.
    /// </summary>
    public readonly struct RunStart
    {
        public RunStart(bool plan, string? notConfinedReason, bool planLeft = false, bool atRest = false, bool canCarryOut = false)
        {
            Plan = plan;
            PlanLeft = planLeft;
            AtRest = atRest;
            CanCarryOut = plan && !planLeft && atRest && canCarryOut;
            NotConfinedReason = string.IsNullOrWhiteSpace(notConfinedReason) ? null : notConfinedReason;
        }

        /// <summary>The run's record, read as the result needs it; null with no run.</summary>
        /// <param name="runtime">The runtime the run is on: Carry Out Plan stands only where it offers a plan.</param>
        public static RunStart? Of(ExecutionView? execution, RuntimeDescriptor? runtime = null) => execution == null
            ? (RunStart?)null
            : new RunStart(execution.Plan == true, execution.UnconfinedReason, execution.PlanLeft == true,
                atRest: execution.Status is ExecutionStatus.Completed, canCarryOut: runtime?.OffersPlan == true);

        /// <summary>The run started as a plan (it stays so after the plan is carried out).</summary>
        public bool Plan { get; }

        /// <summary>The plan was carried out: from here the task changes files as any task does.</summary>
        public bool PlanLeft { get; }

        /// <summary>The task is a plan now: started as one, not yet carried out.</summary>
        public bool IsPlan => Plan && !PlanLeft;

        /// <summary>The run's turn ended and nothing runs: a plan then waits for the person.</summary>
        public bool AtRest { get; }

        /// <summary>Carry Out Plan stands: a plan now, at rest, on a runtime that offers plans.</summary>
        public bool CanCarryOut { get; }

        /// <summary>The Mac's reason the run is not kept to its folder; null where it said none.</summary>
        public string? NotConfinedReason { get; }

        public bool NotConfined => NotConfinedReason != null;

        /// <summary>FIELD's words (1424Z): the slot of a run not kept to its folder, never "confined" or "sandbox".</summary>
        public const string NotKeptToFolder = "Not Kept to Folder";

        /// <summary>FIELD's words (1424Z): a task that is a plan, while its run is not at rest.</summary>
        public const string Planning = "Planning";

        public const string PlanningLine = "It plans first, then waits for you to carry it out.";

        /// <summary>FIELD's words (1424Z): a plan whose run ended, waiting for the person.</summary>
        public const string PlanReady = "Plan Ready";

        public const string PlanReadyLine = "Read it, then carry it out or change it.";

        /// <summary>FIELD's words (1424Z): the press that carries the plan out, the main action.</summary>
        public const string CarryOutPlan = "Carry Out Plan";

        /// <summary>What the press sends the agent with <c>leave_plan</c>: an instruction, never shown on a screen as ours.</summary>
        public const string CarryOutInstruction = "Carry out the plan.";
    }
}

#nullable enable
namespace Halcyonic.Client
{
    /// <summary>
    /// What a run's start was, as the runtime confirmed it (<c>runtime.execution.started</c>): whether the runtime reported
    /// the session in its own plan mode (VRIFY's planning), and, where a start that asked for Halcyonic's sandbox ran without
    /// it or is not counted inside it, the Mac's reason (BOX-4's <c>unconfined_reason</c>). Both are observed by the runtime's
    /// adapter; absent means not recorded, which never reads as a plan, and never as kept to the folder either way: a run is
    /// said to be not kept to its folder only where the Mac says so.
    /// </summary>
    public readonly struct RunStart
    {
        public RunStart(bool plan, string? notConfinedReason)
        {
            Plan = plan;
            NotConfinedReason = string.IsNullOrWhiteSpace(notConfinedReason) ? null : notConfinedReason;
        }

        /// <summary>The runtime reported the session in its own plan mode.</summary>
        public bool Plan { get; }

        /// <summary>The Mac's reason the run is not kept to its folder; null where it said none.</summary>
        public string? NotConfinedReason { get; }

        public bool NotConfined => NotConfinedReason != null;

        /// <summary>FIELD's words (1424Z): the slot of a run not kept to its folder, never "confined" or "sandbox".</summary>
        public const string NotKeptToFolder = "Not Kept to Folder";

        /// <summary>FIELD's words (1424Z): the status of a task that is a plan, where planning and a plan ready are not told apart.</summary>
        public const string Planning = "Planning";

        public const string PlanningLine = "It plans first, then waits for you to carry it out.";
    }
}

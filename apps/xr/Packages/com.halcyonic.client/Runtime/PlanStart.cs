#nullable enable
namespace Halcyonic.Client
{
    /// <summary>
    /// A task started as a plan (VRIFY's planning, ADR 0057's start: <c>execution.start</c> with <c>plan</c>; FIELD's words):
    /// where the chosen runtime offers a plan (<c>offers_plan</c>), Plan First stands as a third answer after Ask First and
    /// Never Ask wherever a new start's level is chosen, kept at once. A plan always asks before a command, so it is never
    /// started with never ask. The level's words then read Plan First on the recap's slot and the review's Model row.
    /// </summary>
    public static class PlanStart
    {
        /// <summary>The answer, and the level's words for a start that plans first (FIELD's 1424Z words).</summary>
        public const string PlanFirst = "Plan First";

        /// <summary>The answer's key beside <see cref="AgentColumn.Ask"/> and <see cref="AgentColumn.Never"/>.</summary>
        public const string Key = "plan";

        /// <summary>Whether <paramref name="runtime"/> offers a start as a plan: declared by its descriptor, never for a practice run.</summary>
        public static bool Offered(Halcyonic.Contracts.RuntimeDescriptor? runtime) => runtime != null && !runtime.Synthetic && runtime.OffersPlan == true;

        /// <summary>The level the start will use, in words: Plan First, else Never Ask or Ask First.</summary>
        public static string LevelWords(NewWorkDraft draft) => draft.StartsPlan ? PlanFirst : AgentColumn.LevelWords(draft.StartsNeverAsk);
    }
}

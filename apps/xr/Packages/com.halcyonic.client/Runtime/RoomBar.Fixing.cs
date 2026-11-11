#nullable enable
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    public sealed partial class RoomBar
    {
        /// <summary>
        /// The information panel as a card where a moment gives it one (the addendum's A17): a name or a status as its title,
        /// facts as slots, tries as pips, a list. Null where the panel holds words alone or rows.
        /// </summary>
        public RoomCard? Card { get; private set; }

        /// <summary>The fixing card's title and its quieter line while a try is under way (the addendum's A7 and A17).</summary>
        public const string FixingTitle = "Fixing";
        public const string FixingLine = "The agent works on it by itself";

        /// <summary>The ended chain's card: its title and line where the check passed after a try, and where the tries ran out.</summary>
        public const string FixedTitle = "All Passing";
        public const string FixedLine = "The agent fixed it by itself";
        public const string StoppedTitle = "Checks Failed";
        public const string StoppedLine = "The tries ran out";

        /// <summary>
        /// While the task's failed check is back with its agent on its own (ADR 0044; the addendum's A7 and A17): a status
        /// card, "Fixing" over its quieter line, its tries as pips under its mark, the one under way filled. The
        /// field and the cursor still say what the agent does. The title names the outcome, never the check's own name, which
        /// its node carries. Null where no try is under way.
        /// </summary>
        public static RoomCard? FixingCard(AutoFixView? fix)
        {
            if (fix == null || fix.State != AutoFixViewState.Fixing || fix.Attempt < 1 || fix.Cap < 1) return null;
            return new RoomCard(status: new RoomCardStatus(FixingTitle, FixingLine, RoomTone.Working), trackAt: (int)fix.Attempt, trackOf: (int)fix.Cap);
        }

        /// <summary>
        /// The card a result at rest shows where a chain of tries ended (the addendum's A7 and A17): "All Passing" over its
        /// line, the tries it took filled, where the check passed after one; "Checks Failed" where the tries ran out, every try spent. Null for
        /// any other end: the result says the rest, as before any try.
        /// </summary>
        public static RoomCard? FixedCard(AutoFixView? fix)
        {
            if (fix == null || fix.State != AutoFixViewState.Ended || fix.Attempt < 1 || fix.Cap < 1) return null;
            return fix.Reason switch
            {
                AutoFixEndReason.Passed => new RoomCard(status: new RoomCardStatus(FixedTitle, FixedLine, RoomTone.Good), trackAt: (int)fix.Attempt, trackOf: (int)fix.Cap),
                AutoFixEndReason.CapReached => new RoomCard(status: new RoomCardStatus(StoppedTitle, StoppedLine, RoomTone.Bad), trackAt: (int)fix.Cap, trackOf: (int)fix.Cap),
                _ => null,
            };
        }

        /// <summary>
        /// The line a result at rest adds where a chain of tries ended, as XR's result moment lays it until the result is a
        /// card of its own (A17's next part): "Fixed · Try {n} of {m}" where the check passed after a try, "Stopped After {m}
        /// Tries" where the tries ran out. Null for any other end. The card for the same end is <see cref="FixedCard"/>.
        /// </summary>
        public static string? FixedWords(AutoFixView? fix)
        {
            if (fix == null || fix.State != AutoFixViewState.Ended || fix.Attempt < 1) return null;
            return fix.Reason switch
            {
                AutoFixEndReason.Passed => "Fixed" + KitMomentWords.Dot + "Try " + fix.Attempt + " of " + fix.Cap,
                AutoFixEndReason.CapReached => "Stopped After " + fix.Cap + " Tries",
                _ => null,
            };
        }

        /// <summary>
        /// Gives the task's bar at work its fixing card where a try is under way, and answers nothing: the frame stays the
        /// task's (as <see cref="AgentAtWork"/> does).
        /// </summary>
        private static RoomBar? AutoFixing(KitFileBinding binding, VoiceStage voice, RoomBar tasks)
        {
            if (tasks.Working && FixingCard(binding.Model.AutoFix) is RoomCard card) tasks.Card = card;
            return null;
        }
    }
}

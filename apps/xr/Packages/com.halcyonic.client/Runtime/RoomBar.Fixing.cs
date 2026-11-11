#nullable enable
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    public sealed partial class RoomBar
    {
        /// <summary>
        /// While the task's failed check is back with its agent on its own (ADR 0044; the addendum's A7): the failure the
        /// try answers, as the panel's row with its cross, and "Fixing · Try {n} of {m}" as the sentence under it, sweeping.
        /// The field and the cursor still say what the agent does. Null where no try is under way.
        /// </summary>
        public string? Fixing { get; private set; }

        /// <summary>The panel's one row while a try is under way: what failed, with its cross.</summary>
        public const string FixingFailed = "Check Failed";

        /// <summary>The sentence while try <paramref name="attempt"/> of <paramref name="cap"/> is under way.</summary>
        public static string FixingWords(long attempt, long cap) => "Fixing" + KitMomentWords.Dot + "Try " + attempt + " of " + cap;

        /// <summary>
        /// The line a result at rest adds where a chain of tries ended (the addendum's A7, the lead's words of 2105Z):
        /// "Fixed · Try {n} of {m}" where the check passed after a try, "Stopped After {m} Tries" where the tries ran out.
        /// Null for any other end: the result's own lines say the rest, as before any try.
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
        /// Gives the task's bar at work its fixing line where a try is under way, and answers nothing: the frame stays the
        /// task's (as <see cref="AgentAtWork"/> does).
        /// </summary>
        private static RoomBar? AutoFixing(KitFileBinding binding, VoiceStage voice, RoomBar tasks)
        {
            if (tasks.Working && binding.Model.AutoFix is { State: AutoFixViewState.Fixing } fix && fix.Attempt >= 1)
            {
                tasks.Fixing = FixingWords(fix.Attempt, fix.Cap);
                tasks.Facts = new[] { new RoomRow(LineWords.Ours(FixingFailed), RoomRowMark.Cross) };
            }
            return null;
        }
    }
}

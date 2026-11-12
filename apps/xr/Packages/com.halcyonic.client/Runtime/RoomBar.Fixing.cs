#nullable enable
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// What the person must know of everything this bar shows, standing over it on every screen and never dismissed: where
        /// the work shown is recorded or a demonstration, the product's own line for it (DemonstrationFallback's first
        /// sentence, word for word: "Recorded: real agent work, played back here. Nothing here is live."). Null for live work.
        /// Set by the reader that knows the work is recorded (the lead's honesty rule, 2026-11-12).
        /// </summary>
        public LineWords? Notice { get; set; }

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
            // What is being fixed, named in the line ("Working on Build by itself"), the tries as pips on the status's mark:
            // a node of its own under the mark repeated the mark (the critics, 2026-11-11).
            // A step this client cannot name leaves the plain line, never another step's name: a line that named only some of
            // the steps would say less than is being fixed (AUDIT's F1, 2026-11-12).
            var named = fix.FailedSteps is { Count: > 0 } failed ? failed.Distinct().Select(FailedStepWords).ToList() : null;
            var line = named != null && named.All(each => each != null) ? FixingOn(named.Select(each => each!).ToList()) : FixingLine;
            return new RoomCard(status: new RoomCardStatus(FixingTitle, line, RoomTone.Working), trackAt: (int)fix.Attempt, trackOf: (int)fix.Cap);
        }

        /// <summary>The fixing line naming its steps: "Working on Build by itself", "Working on Type Check and Build by itself".</summary>
        public static string FixingOn(IReadOnlyList<string> steps)
        {
            var named = steps.Count == 1 ? steps[0] : string.Join(", ", steps.Take(steps.Count - 1)) + " and " + steps[steps.Count - 1];
            return "Working on " + named + " by itself";
        }

        /// <summary>
        /// A failed step's name, as the checks card names its steps (<see cref="RoomResult.StepWords"/>), a project's own check
        /// (ADR 0055) <see cref="OwnCheckWords"/>; null for any step this client does not know.
        /// </summary>
        public static string? FailedStepWords(AutoFixViewFailedStepsItem step) => step switch
        {
            AutoFixViewFailedStepsItem.Typecheck => RoomResult.StepWords(CheckStepKind.TypeCheck),
            AutoFixViewFailedStepsItem.Build => RoomResult.StepWords(CheckStepKind.Build),
            AutoFixViewFailedStepsItem.Page => RoomResult.StepWords(CheckStepKind.PageDrawn),
            AutoFixViewFailedStepsItem.OwnCheck => OwnCheckWords,
            _ => null,
        };

        /// <summary>A project's own check where the fixing line names it: the view carries no script's name.</summary>
        public const string OwnCheckWords = "Project Check";

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

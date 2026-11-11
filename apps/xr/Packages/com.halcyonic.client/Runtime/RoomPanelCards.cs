#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The panels the owner first reads at rest as cards (the addendum's A17), built from the facts XR gives apart
    /// (<see cref="RoomResult"/>, <see cref="UsageLeftRow"/>): the result, its Changes and its Checks, and usage. FIELD's
    /// builders; XR's moments (<see cref="RoomBar"/>'s result, the menu) wire them in place of their dotted lines. Nothing
    /// here joins two facts on a dot; every outside word stays as runs (RoomCard's own list of which parts are runs).
    /// </summary>
    public static class RoomPanelCards
    {
        public const string ChangesTitle = "Changes";
        public const string ChecksTitle = "Checks";
        public const string UsageTitle = "Usage";
        public const string Passed = "Passed";
        public const string Failed = "Failed";
        public const string AllPassing = "All Passing";
        public const string ChecksFailed = "Checks Failed";
        public const string NotSeen = "Not Seen";
        public const string NotSeenLine = "The computer could not say what changed";
        public const string NewNote = "New";
        public const string EditedNote = "Edited";
        public const string RemovedNote = "Removed";
        public const string SessionWords = "Session";
        public const string WeekWords = "Week";

        /// <summary>What a usage card says of its numbers: at most what is left, as the provider reported it, never a current value.</summary>
        public const string UsageLine = "At most what is left, as the provider last reported it.";

        /// <summary>
        /// The result at rest: the task's name as the title beside the result's picture; its facts as slots (the check, and
        /// how many files changed, or the files waiting to be added where A13's copy says so); where a chain of tries
        /// ended, its status first (<see cref="RoomBar.FixedCard"/>). The agent's own words stand on the band, never here.
        /// Null where nothing is known of the task.
        /// </summary>
        /// <param name="task">The task's name, theirs.</param>
        /// <param name="toAdd">The files waiting to be added (<see cref="RoomAdd.Slot"/>), which stands in the count's place.</param>
        public static RoomCard? Result(RoomResult result, LineWords task, AutoFixView? fix = null, RoomSlot? toAdd = null)
        {
            var ended = RoomBar.FixedCard(fix);
            // "All Passing" is the computer's check speaking for these files: it stands only while the result has that check
            // (XR's rule in the result moment, AUDIT 2026-11-11). That the tries ran out stays true whatever the folder did.
            if (fix?.Reason == AutoFixEndReason.Passed && result.Passed != true) ended = null;
            if (!result.Any && ended == null) return null;
            var slots = new List<RoomSlot>();
            if (result.Passed is bool passed) slots.Add(new RoomSlot(passed ? RoomKind.Passed : RoomKind.Failed, LineWords.Ours(passed ? Passed : Failed)));
            if (toAdd is RoomSlot waiting) slots.Add(waiting);
            else if (result.FilesChanged is long count) slots.Add(new RoomSlot(RoomKind.ToAdd, LineWords.Ours(RoomAdd.Files(count))));
            return new RoomCard(task, head: RoomCardHead.Picture, status: ended?.Status, slots: slots, trackAt: ended?.TrackAt, trackOf: ended?.TrackOf);
        }

        /// <summary>
        /// Changes: the title, what the task did as its line, and a list, each file its kind's mark, its path (theirs, mono),
        /// and a quiet note of the kind ("New", "Edited", "Removed"). Where the computer could not say, a status says so.
        /// </summary>
        public static RoomCard Changes(RoomResult result, LineWords task)
        {
            if (result.NotObserved) return new RoomCard(LineWords.Ours(ChangesTitle), status: new RoomCardStatus(NotSeen, NotSeenLine, RoomTone.Quiet));
            var rows = result.Changes.Select(change => new RoomListRow(
                change.Kind == RoomChangeKind.Made ? RoomKind.Added : change.Kind == RoomChangeKind.Removed ? RoomKind.Removed : RoomKind.Changed,
                LineWords.Theirs(change.Path),
                change.Kind == RoomChangeKind.Made ? NewNote : change.Kind == RoomChangeKind.Removed ? RemovedNote : EditedNote,
                mono: true)).ToList();
            return new RoomCard(LineWords.Ours(ChangesTitle), LineWords.Ours("What ") + task + LineWords.Ours(" did to the project."), list: rows);
        }

        /// <summary>
        /// Checks: a status naming the outcome ("All Passing", "Checks Failed"), never a step's own name, and the steps as
        /// nodes, in order, those never reached included: dim where the run ended, waiting where it still runs. A chain of
        /// tries hangs its pips on the failed step.
        /// </summary>
        public static RoomCard Checks(RoomResult result, AutoFixView? fix = null)
        {
            var fixing = fix?.State == AutoFixViewState.Fixing;
            var tries = fix != null && fix.Attempt >= 1 && fix.Cap >= 1 ? ((int)System.Math.Min(fix.Attempt, fix.Cap), (int)fix.Cap) : (0, 0);
            var steps = result.StepFacts.Select(step =>
            {
                var tone = step.Tone switch
                {
                    RoomStepTone.Passed => RoomTone.Good,
                    RoomStepTone.Failed => fixing ? RoomTone.Working : RoomTone.Bad,
                    _ => fixing ? RoomTone.Quiet : RoomTone.Skipped,
                };
                // The tries hang on the step that failed; once a chain passed, the result does not say which step it fixed, so none carries them.
                return step.Tone == RoomStepTone.Failed ? new RoomCardStep(step.Name, tone, tries.Item1, tries.Item2) : new RoomCardStep(step.Name, tone);
            }).ToList();
            RoomCardStatus? status = result.Passed switch
            {
                true => new RoomCardStatus(AllPassing, fix?.Reason == AutoFixEndReason.Passed ? RoomBar.FixedLine : "Every check the computer ran passed", RoomTone.Good),
                false when fixing => new RoomCardStatus(RoomBar.FixingTitle, RoomBar.FixingLine, RoomTone.Working),
                false => new RoomCardStatus(ChecksFailed, fix?.Reason == AutoFixEndReason.CapReached ? RoomBar.StoppedLine : "The computer's check stopped on a step", RoomTone.Bad),
                _ => (RoomCardStatus?)null,
            };
            return new RoomCard(status == null ? LineWords.Ours(ChecksTitle) : null, status: status, steps: steps);
        }

        /// <summary>
        /// Usage as gauges: one well a window, the number left large with its window's word beside it, the bar, and when it
        /// resets; the provider's own label leaning where several providers report. The card's line says the numbers are
        /// at most what is left, as reported, never a current value. Null where there is no reading.
        /// </summary>
        public static RoomCard? Usage(IReadOnlyList<UsageLeftRow> rows)
        {
            if (rows.Count == 0) return null;
            var several = rows.Select(row => row.Agent ?? "").Distinct().Count() > 1;
            var gauges = rows.Take(2).Select(row =>
            {
                var week = row.Window == UsageLeftPresenter.Weekly;
                var window = LineWords.Ours(week ? WeekWords : SessionWords);
                var label = several && !row.Label.IsEmpty ? row.Label + LineWords.Ours(" ") + window : window;
                return new RoomGauge(label, row.Left, row.Resets.Length > 0 ? "Resets " + row.Resets : null, week ? RoomKind.Week : RoomKind.Session);
            }).ToList();
            return new RoomCard(LineWords.Ours(UsageTitle), LineWords.Ours(UsageLine), gauges: gauges);
        }
    }
}

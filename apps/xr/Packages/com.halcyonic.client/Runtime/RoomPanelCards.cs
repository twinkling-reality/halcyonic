#nullable enable
using System;
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

        /// <summary>The review card's checks fact: how many of the computer's own check steps passed, of all of them ("4 of 4 Checks Passed", AUDIT2 23:51Z).</summary>
        public static string ChecksCount(int passed, int of) =>
            passed.ToString(System.Globalization.CultureInfo.InvariantCulture) + " of " + of.ToString(System.Globalization.CultureInfo.InvariantCulture) + " Checks Passed";
        public const string NotSeen = "Not Seen";
        public const string NotSeenLine = "The computer could not say what changed";

        /// <summary>Changes where the computer saw the task change no file: said, so an empty list never reads as a failed read.</summary>
        public const string NothingChanged = "Nothing Changed";
        public const string NewNote = "New";
        public const string EditedNote = "Edited";
        public const string RemovedNote = "Removed";
        public const string SessionWords = "5-Hour Limit";
        public const string WeekWords = "Weekly Limit";

        /// <summary>What a usage card says of its numbers: at most what is left, as the provider reported it, never a current value.</summary>
        public const string UsageLine = "At most what is left, as last reported.";

        /// <summary>The gauge's figure with its own word, so it never reads as used (AUDIT2, 2026-11-16 04:38Z): "62% Left". FIELD's view draws it.</summary>
        public static string GaugeWords(int percentLeft) => percentLeft.ToString(System.Globalization.CultureInfo.InvariantCulture) + "% Left";

        /// <summary>
        /// The usage card's line with when the numbers were read (AUDIT's condition, 2026-11-16 04:23Z: numbers from an earlier
        /// read say when, never read as now), one sentence (AUDIT2): "At most what is left, as last reported Today 04:20."
        /// </summary>
        public static string UsageLineSeen(IReadOnlyList<UsageLeftRow> rows) =>
            rows.FirstOrDefault(row => !string.IsNullOrEmpty(row.Seen)) is { } seen ? UsageLine.TrimEnd('.') + " " + seen.Seen + "." : UsageLine;

        /// <summary>
        /// The result at rest: the task's name as the title beside the result's picture; its facts as slots (the check, and
        /// how many files changed, or the files waiting to be added where A13's copy says so); where a chain of tries
        /// ended, its status first (<see cref="RoomBar.FixedCard"/>). The agent's own words stand on the band, never here.
        /// Null where nothing is known of the task.
        /// </summary>
        /// <param name="task">The task's name, theirs.</param>
        /// <param name="toAdd">The files waiting to be added (<see cref="RoomAdd.Slot"/>), which stands in the count's place.</param>
        /// <param name="changes">The control that opens Changes: the files' slot takes it, so what changed is one press from the result.</param>
        /// <param name="checks">The control that opens Checks: the checks' slot takes it, so how the check went is one press from the result.</param>
        public static RoomCard? Result(RoomResult result, LineWords task, AutoFixView? fix = null, RoomSlot? toAdd = null, KitControl? changes = null,
            KitControl? checks = null)
        {
            var ended = RoomBar.FixedCard(fix);
            // "All Passing" is the computer's check speaking for these files: it stands only while the result has that check
            // (XR's rule in the result moment, AUDIT 2026-11-11). That the tries ran out stays true whatever the folder did.
            if (fix?.Reason == AutoFixEndReason.Passed && result.Passed != true) ended = null;
            if (!result.Any && ended == null) return null;
            var slots = new List<RoomSlot>();
            // The review card (TASKS, take 3; Detroit's Analysis conclusion): the check's outcome is the card's one large line, its
            // status, in the Checks page's own words (AUDIT2 2026-11-16 10:05Z W1), and the checks a fact under it, one press from
            // the result: how many of the computer's own steps passed. Where a chain of tries that ended already names the outcome,
            // its status stands and the outcome is not said twice (W2).
            RoomCardStatus? outcome = null;
            if (result.Passed is bool passed && ended?.Status == null)
                outcome = new RoomCardStatus(passed ? AllPassing : ChecksFailed, null, passed ? RoomTone.Good : RoomTone.Attention);
            // A check whose verdict is not known: said as such, with no mark of passed or failed (VRIFY, AUDIT2's words).
            else if (result.PartlyChecked) slots.Add(new RoomSlot(RoomKind.None, LineWords.Ours(KitMomentWords.PartlyChecked)));
            if (result.Passed != null && result.Steps.Count > 0)
                slots.Add(new RoomSlot(result.Passed == true ? RoomKind.Passed : RoomKind.Failed,
                    LineWords.Ours(ChecksCount(result.Steps.Count(step => step.Result == CheckStepResult.Passed), result.Steps.Count)), press: checks));
            if (toAdd is RoomSlot waiting) slots.Add(new RoomSlot(waiting.Kind, waiting.Value, waiting.Mono, waiting.Press ?? changes));
            else if (result.FilesChanged is long count) slots.Add(new RoomSlot(RoomKind.ToAdd, LineWords.Ours(RoomAdd.Files(count)), press: changes));
            // SCREENS's run start (FIELD's 1424Z words): a run the Mac says is not kept to its folder, and a task that is a plan.
            if (result.Start is { NotConfined: true }) slots.Add(new RoomSlot(RoomKind.Folder, LineWords.Ours(RunStart.NotKeptToFolder)));
            // A plan whose run ended waits for the person (Plan Ready, the attention tone); while its run is at work it plans. A plan
            // whose run failed, was stopped or is unknown says neither: the card's own status speaks for that run (AUDIT's MUST 2).
            var status = ended?.Status ?? (result.Start is { IsPlan: true } plan
                ? plan.AtRest ? new RoomCardStatus(RunStart.PlanReady, RunStart.PlanReadyLine, RoomTone.Attention)
                    : plan.Running ? new RoomCardStatus(RunStart.Planning, RunStart.PlanningLine, RoomTone.Working) : (RoomCardStatus?)null
                : (RoomCardStatus?)null)
                // Where the computer's own check gives no outcome, the card says what the Checks page says (JUDGE's ALERT 05:04Z):
                // a check under way, or the demonstration's simulated checks, said as simulated; never "Nothing checked yet".
                ?? (result.CheckRunning ? new RoomCardStatus(TaskSteps.Checking, null, RoomTone.Working) : result.Simulated)
                ?? outcome;
            // Where Play in Headset is not drawn and why matters to the person, its reason stands at the card's foot.
            var refusal = result.Play is PlayInHeadset play ? HeadsetPlay.RefusalLine(play) : null;
            return new RoomCard(task, head: RoomCardHead.Picture, status: status, slots: slots, trackAt: ended?.TrackAt, trackOf: ended?.TrackOf,
                more: refusal != null ? LineWords.Ours(refusal) : null);
        }

        /// <summary>
        /// Changes: the title, what the task did as its line, and a list, each file its kind's mark, its path (theirs, mono),
        /// and a quiet note of the kind ("New", "Edited", "Removed"). Where the computer could not say, a status says so.
        /// </summary>
        /// <param name="press">SCREENS's: a row's own control, so a file is read from its row (<see cref="FileChangeColumn"/>); none where null.</param>
        public static RoomCard Changes(RoomResult result, LineWords task, Func<RoomChange, KitControl?>? press = null)
        {
            if (result.NotObserved) return new RoomCard(LineWords.Ours(ChangesTitle), status: new RoomCardStatus(NotSeen, NotSeenLine, RoomTone.Quiet));
            // Seen, and nothing changed: said as the desk says it, never an empty list (SCREENS's seam, 2026-11-12).
            if (result.Changes.Count == 0) return new RoomCard(LineWords.Ours(ChangesTitle), status: new RoomCardStatus(NothingChanged, null, RoomTone.Quiet));
            var rows = result.Changes.Select(change => new RoomListRow(
                change.Kind == RoomChangeKind.Made ? RoomKind.Added : change.Kind == RoomChangeKind.Removed ? RoomKind.Removed : RoomKind.Changed,
                LineWords.Theirs(change.Path),
                change.Kind == RoomChangeKind.Made ? NewNote : change.Kind == RoomChangeKind.Removed ? RemovedNote : EditedNote,
                mono: true, press: press?.Invoke(change))).ToList();
            // The title above already names the task: the line says what it is (AUDIT2, 2026-11-16 03:23Z W3).
            return new RoomCard(LineWords.Ours(ChangesTitle), LineWords.Ours(ChangesLine), list: rows);
        }

        /// <summary>
        /// Checks: a status naming the outcome ("All Passing", "Checks Failed"), never a step's own name, and the steps as
        /// nodes, in order, those never reached included: dim where the run ended, waiting where it still runs. A chain of
        /// tries hangs its pips on the failed step.
        /// </summary>
        /// <summary>Changes' line under its title (AUDIT2's W3).</summary>
        public const string ChangesLine = "What this task changed in the project.";

        /// <summary>A failed check's line: the step it stopped at by the row's own word (AUDIT2's W5), else that it stopped on a step.</summary>
        public static string StoppedAt(RoomResult result) =>
            result.StepFacts.FirstOrDefault(step => step.Tone == RoomStepTone.Failed) is { } failed && !string.IsNullOrEmpty(failed.Name)
                ? "The check stopped at " + failed.Name + "."
                : "The computer's check stopped on a step";

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
                false => new RoomCardStatus(ChecksFailed, fix?.Reason == AutoFixEndReason.CapReached ? RoomBar.StoppedLineFor(fix.Cap) : StoppedAt(result), RoomTone.Bad),
                null when result.PartlyChecked => new RoomCardStatus(KitMomentWords.PartlyChecked, KitMomentWords.PartlyCheckedLine, RoomTone.Quiet),
                _ => (RoomCardStatus?)null,
            };
            // Partly Checked: the host's own finding under its line (AUDIT2's order).
            var finding = result.PartlyChecked && result.Finding is string said ? LineWords.Ours(said) : null;
            return new RoomCard(status == null ? LineWords.Ours(ChecksTitle) : null, finding, status: status, steps: steps);
        }

        /// <summary>
        /// Usage as gauges: one well a window, the number left large with its window's word beside it, the bar, and when it
        /// resets; the provider's own label leaning where several providers report. The card's line says the numbers are
        /// at most what is left, as reported, never a current value. Null where there is no reading.
        /// </summary>
        /// <param name="assistant">The assistant's row (<see cref="Assistant"/>), under the gauges; null where it is not read.</param>
        public static RoomCard? Usage(IReadOnlyList<UsageLeftRow> rows, RoomListRow? assistant = null)
        {
            if (rows.Count == 0 && assistant == null) return null;
            var several = rows.Select(row => row.Agent ?? "").Distinct().Count() > 1;
            var gauges = rows.Take(2).Select(row =>
            {
                var week = row.Window == UsageLeftPresenter.Weekly;
                var window = LineWords.Ours(week ? WeekWords : SessionWords);
                var label = several && !row.Label.IsEmpty ? row.Label + LineWords.Ours(" ") + window : window;
                return new RoomGauge(label, row.Left, row.Resets.Length > 0 ? "Resets " + row.Resets : null, week ? RoomKind.Week : RoomKind.Session);
            }).ToList();
            return new RoomCard(LineWords.Ours(UsageTitle), gauges.Count > 0 ? LineWords.Ours(UsageLineSeen(rows)) : null, gauges: gauges,
                list: assistant == null ? null : new[] { assistant });
        }

        public const string AssistantWords = "Assistant";

        public const string AssistantUnavailable = "Assistant Unavailable";

        /// <summary>
        /// The assistant's row of the usage card (the lead, 2026-11-12): the model it runs on, as its maker names it, the row's
        /// main line; under it what the row is and where the model runs, this computer or the provider's own name, and the tile
        /// says where too (the computer, or the link out to a host; the failure mark where it cannot run); the row's press is the host's Use Model, its chevron.
        /// Where the assistant cannot run there is no model to name: the row says the assistant is unavailable, and the host's own
        /// words why stand under it across the row, whole (the critic, 2026-11-12: a foot apart read as the card's). Label, value and where are
        /// each their own words, never joined by a mark.
        /// </summary>
        /// <param name="model">The model's id, theirs; null or empty where the assistant cannot run.</param>
        /// <param name="provider">The provider's own name where the model is hosted; null where it runs on this computer.</param>
        /// <param name="reason">Where the assistant cannot run, the host's own words why; ignored where it can.</param>
        /// <param name="press">The host's entry that chooses the model, as XR raises it; null where none is offered.</param>
        public static RoomListRow Assistant(string? model, string? provider, string? reason, KitControl? press)
        {
            if (string.IsNullOrWhiteSpace(model))
                return new RoomListRow(RoomKind.Failed, LineWords.Ours(AssistantUnavailable), tone: RoomTone.Bad, press: press,
                    under: string.IsNullOrWhiteSpace(reason) ? null : LineWords.HostPlain(reason!.Trim()));
            var hosted = !string.IsNullOrWhiteSpace(provider);
            var where = hosted ? LineWords.Ours(AssistantWords + " Hosted by ") + LineWords.TheirName(provider!.Trim()) : LineWords.Ours(AssistantWords + " on " + CaptureNames.Host);
            return new RoomListRow(hosted ? RoomKind.Link : RoomKind.Computer, LineWords.TheirName(model!.Trim()), press: press, under: where, excerpt: true);
        }
    }
}

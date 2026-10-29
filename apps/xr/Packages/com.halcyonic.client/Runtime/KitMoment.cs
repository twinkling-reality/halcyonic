#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>Where a moment's facts come from: the person's computer saw them, or the runtime says so.</summary>
    public enum KitProvenance
    {
        Observed,

        Reported,
    }

    /// <summary>What a moment stands for (ADR 0040, rule 4).</summary>
    public enum KitMomentKind
    {
        /// <summary>The person's request went to the runtime; nothing has started.</summary>
        Sent,

        /// <summary>A file the runtime says it made, changed or removed: its report, until the computer reads the folder.</summary>
        ReportedEdit,

        /// <summary>A file the computer saw changed when it read the folder at the turn's end (ADR 0038).</summary>
        ObservedChange,

        /// <summary>What the runtime says of its work, in its own words.</summary>
        AgentSays,

        /// <summary>A command the runtime asks to run.</summary>
        Question,

        /// <summary>A check the person's computer ran itself.</summary>
        HostCheck,

        /// <summary>A command the runtime says it ran, with the outcome it reports.</summary>
        AgentCheck,

        /// <summary>A package the runtime wanted that the starter doesn't hold: refused, its install not run.</summary>
        PackageRefused,

        /// <summary>The computer didn't read this turn's files, for a reason it gives: never read as no changes.</summary>
        NotObserved,

        /// <summary>The work is ready to try, after a check of the computer's own passed.</summary>
        Ready,
    }

    /// <summary>A moment's colour: none, or one of the three meanings (STYLE.md, Spatial system).</summary>
    public enum KitMomentTone
    {
        Neutral,

        /// <summary>Needs the person.</summary>
        Attention,

        Failure,

        Passed,
    }

    /// <summary>What happened to a file.</summary>
    public enum KitFileChange
    {
        Created,

        Changed,

        Deleted,
    }

    /// <summary>What the runtime declares an approved command does with its sandbox (ADR 0033).</summary>
    public enum KitSandbox
    {
        NotDeclared,

        MayLeave,

        StaysInside,
    }

    /// <summary>What a check of the computer's own covers: today's builds it; the open-check also opens it.</summary>
    public enum KitCheckScope
    {
        Builds,

        BuildsAndOpens,
    }

    /// <summary>An action beneath a moment.</summary>
    public enum KitMomentAction
    {
        ReadIt,

        Stop,

        YesRunIt,

        No,

        SaySomethingElse,

        SeeTheOutput,

        TryIt,

        SeeWhatChanged,
    }

    /// <summary>A file's added and removed lines from the computer's own checkpoint diff (ADR 0036), counted.</summary>
    public sealed class KitCheckpointDiff
    {
        public KitCheckpointDiff(int added, int removed)
        {
            Added = added;
            Removed = removed;
        }

        public int Added { get; }

        public int Removed { get; }
    }

    /// <summary>
    /// One moment of the room (ADR 0040): what the runtime reported, what the computer saw, who ran a check, and the
    /// actions that stand beneath it. Each says where it comes from, and what one source said is never another's.
    /// </summary>
    public sealed class KitMoment
    {
        private KitMoment(KitMomentKind kind, KitProvenance provenance, string source, LineWords headline, string? fact,
            IReadOnlyList<string> lines, KitMomentTone tone, IReadOnlyList<KitMomentAction> actions, DateTimeOffset? at,
            KitCheckpointDiff? diff = null, bool? passed = null, KitCheckScope? scope = null)
        {
            Kind = kind;
            Provenance = provenance;
            Source = source;
            Headline = headline;
            Fact = fact;
            Lines = lines;
            Tone = tone;
            Actions = actions;
            At = at;
            Diff = diff;
            Passed = passed;
            Scope = scope;
        }

        public KitMomentKind Kind { get; }

        public KitProvenance Provenance { get; }

        /// <summary>Who says it, on the moment's chip: "Codex says", "Seen on your computer", "Checked by your computer".</summary>
        public string Source { get; }

        /// <summary>The moment's subject; anything the runtime wrote inside it is theirs.</summary>
        public LineWords Headline { get; }

        /// <summary>One short fact beneath the subject, Halcyonic's words.</summary>
        public string? Fact { get; }

        /// <summary>Halcyonic's lines beneath the subject, in order.</summary>
        public IReadOnlyList<string> Lines { get; }

        public KitMomentTone Tone { get; }

        /// <summary>The moment's own actions; <see cref="KitMomentSlot.Actions"/> adds Stop while the work goes on.</summary>
        public IReadOnlyList<KitMomentAction> Actions { get; }

        /// <summary>When the computer recorded it, where it did.</summary>
        public DateTimeOffset? At { get; }

        /// <summary>The checkpoint diff behind an observed change, only where undo is on; never the runtime's lines.</summary>
        public KitCheckpointDiff? Diff { get; }

        /// <summary>Undo stands for this change: the checkpoint that holds it is shown.</summary>
        public bool Undoable => Diff != null;

        /// <summary>A check's outcome where its exit was seen; null otherwise.</summary>
        public bool? Passed { get; }

        public KitCheckScope? Scope { get; }

        private static readonly IReadOnlyList<string> NoLines = Array.Empty<string>();

        private static readonly IReadOnlyList<KitMomentAction> NoActions = Array.Empty<KitMomentAction>();

        /// <summary>The person's request went to <paramref name="runtime"/>: accepted is not started, so it waits.</summary>
        public static KitMoment Sent(string runtime) => new KitMoment(KitMomentKind.Sent, KitProvenance.Observed,
            KitMomentWords.SentTo + LabelText.Name(runtime), LineWords.Ours(KitMomentWords.WaitingToStart), null, NoLines, KitMomentTone.Neutral, NoActions, null);

        /// <summary>A file <paramref name="runtime"/> says it changed: its report, in the past tense, not seen yet.</summary>
        public static KitMoment ReportedEdit(string runtime, KitFileChange change, string file, DateTimeOffset? at) =>
            new KitMoment(KitMomentKind.ReportedEdit, KitProvenance.Reported, Says(runtime),
                LineWords.Ours(KitMomentWords.Reported(change)) + LineWords.TheirName(file), KitMomentWords.NotSeenYet, NoLines,
                KitMomentTone.Neutral, new[] { KitMomentAction.ReadIt }, at);

        /// <summary>
        /// A file the computer saw changed when it read the folder: its line counts before and after and the time, never its
        /// lines (ADR 0038). Added and removed lines only from a checkpoint diff, and only where undo is on (ADR 0036).
        /// </summary>
        public static KitMoment ObservedChange(KitFileChange change, string file, int? linesBefore, int? linesAfter, DateTimeOffset seen,
            KitCheckpointDiff? diff = null, bool undoOn = false)
        {
            var words = LineWords.Ours(KitMomentWords.Observed(change)) + LineWords.TheirName(file) + LineWords.Ours(KitMomentWords.LinesAndTime(change, linesBefore, linesAfter, seen));
            var shown = undoOn ? diff : null;
            return new KitMoment(KitMomentKind.ObservedChange, KitProvenance.Observed, KitMomentWords.SeenOnYourComputer, words,
                shown == null ? null : KitMomentWords.Counts(shown), NoLines, KitMomentTone.Neutral,
                shown == null ? NoActions : new[] { KitMomentAction.ReadIt }, seen, shown);
        }

        /// <summary>What <paramref name="runtime"/> says of its work, its own words.</summary>
        public static KitMoment AgentSays(string runtime, string text, DateTimeOffset? at = null) => new KitMoment(KitMomentKind.AgentSays, KitProvenance.Reported,
            Says(runtime), LineWords.Theirs(text), null, NoLines, KitMomentTone.Neutral, NoActions, at);

        /// <summary>
        /// <paramref name="runtime"/> asks to run <paramref name="command"/>: ADR 0033's line in the form it declares, and Yes only
        /// for a request shown whole.
        /// </summary>
        public static KitMoment Question(string runtime, string command, KitSandbox sandbox, bool complete, DateTimeOffset? at)
        {
            var lines = new List<string>();
            if (KitMomentWords.Sandbox(sandbox) is string line) lines.Add(line);
            lines.Add(KitMomentWords.NothingRunsYet);
            var actions = complete
                ? new[] { KitMomentAction.YesRunIt, KitMomentAction.No, KitMomentAction.SaySomethingElse }
                : new[] { KitMomentAction.No, KitMomentAction.SaySomethingElse };
            return new KitMoment(KitMomentKind.Question, KitProvenance.Reported, LabelText.Name(runtime) + KitMomentWords.AsksSuffix,
                LineWords.Ours(KitMomentWords.RunThisCommand) + LineWords.TheirName(command), null, lines, KitMomentTone.Attention, actions, at);
        }

        /// <summary>
        /// A check the computer ran itself: under way only while no exit was seen; an exit with no outcome said as not known
        /// (AUDIT's wiring note); its words name what the check covered.
        /// </summary>
        public static KitMoment HostCheck(KitCheckScope scope, bool ended, bool? passed, DateTimeOffset? at)
        {
            if (!ended && passed != null) throw new ArgumentException("A check's outcome needs its exit.", nameof(passed));
            var tone = passed == true ? KitMomentTone.Passed : passed == false ? KitMomentTone.Failure : KitMomentTone.Neutral;
            var actions = ended ? new[] { KitMomentAction.SeeTheOutput } : NoActions;
            return new KitMoment(KitMomentKind.HostCheck, KitProvenance.Observed, KitMomentWords.CheckedByYourComputer,
                LineWords.Ours(KitMomentWords.Check(scope, ended, passed)), null, NoLines, tone, actions, at, null, passed, scope);
        }

        /// <summary>A command <paramref name="runtime"/> says it ran and the outcome it reports: its words, no colour.</summary>
        public static KitMoment AgentCheck(string runtime, string command, string outcome, DateTimeOffset? at) =>
            new KitMoment(KitMomentKind.AgentCheck, KitProvenance.Reported, LabelText.Name(runtime) + KitMomentWords.RanSuffix,
                LineWords.TheirName(command) + LineWords.Ours(": ") + LineWords.Theirs(outcome), null, NoLines, KitMomentTone.Neutral,
                new[] { KitMomentAction.SeeTheOutput }, at);

        /// <summary>A package the runtime wanted that the starter doesn't hold: refused, its install not run (what is on disk is not claimed).</summary>
        public static KitMoment PackageRefused(string package, DateTimeOffset? at) => new KitMoment(KitMomentKind.PackageRefused, KitProvenance.Observed,
            KitMomentWords.SeenOnYourComputer, LineWords.Ours("Wanted ") + LineWords.TheirName(package) + LineWords.Ours(", which isn't in this starter"),
            KitMomentWords.InstallDidNotRun, NoLines, KitMomentTone.Neutral, NoActions, at);

        /// <summary>The computer didn't read this turn's files, for <paramref name="reason"/>: said, never read as no changes.</summary>
        public static KitMoment NotObserved(string reason, DateTimeOffset? at) => new KitMoment(KitMomentKind.NotObserved, KitProvenance.Observed,
            KitMomentWords.Skipped, LineWords.Ours(KitMomentWords.NotObservedWords + reason), null, NoLines, KitMomentTone.Neutral, NoActions, at);

        /// <summary>
        /// <paramref name="project"/> is ready: only after a check of the computer's own passed, saying what that check covered;
        /// Try it only where a route to try it exists.
        /// </summary>
        public static KitMoment Ready(string project, TimeSpan? elapsed, KitMoment check, bool tryRoute)
        {
            if (check.Kind != KitMomentKind.HostCheck || check.Passed != true || check.Scope is not KitCheckScope scope)
                throw new ArgumentException("Ready follows a passed check of the computer's own.", nameof(check));
            var actions = tryRoute ? new[] { KitMomentAction.TryIt, KitMomentAction.SeeWhatChanged } : new[] { KitMomentAction.SeeWhatChanged };
            return new KitMoment(KitMomentKind.Ready, KitProvenance.Observed, KitMomentWords.CheckedByYourComputer,
                LineWords.TheirName(project) + LineWords.Ours(KitMomentWords.IsReady), null, new[] { KitMomentWords.Reward(elapsed, scope) },
                KitMomentTone.Passed, actions, check.At, null, true, scope);
        }

        private static string Says(string runtime) => LabelText.Name(runtime) + KitMomentWords.SaysSuffix;
    }

    /// <summary>
    /// The one place moments come out (ADR 0040, rule 4): one shown at a time, the newest, unless a question still waits for
    /// the person; the rest stacked behind it and counted.
    /// </summary>
    public sealed class KitMomentSlot
    {
        public KitMomentSlot(IReadOnlyList<KitMoment> moments, bool working)
        {
            Moments = moments;
            Working = working;
        }

        public IReadOnlyList<KitMoment> Moments { get; }

        /// <summary>The runtime's own state says the work goes on; never read from the moments.</summary>
        public bool Working { get; }

        /// <summary>
        /// The moment shown: the newest question among the moments, else the newest; null with none. A question the person
        /// answered leaves the moments: what follows it is the runtime's next event.
        /// </summary>
        public int? Shown
        {
            get
            {
                if (Moments.Count == 0) return null;
                for (var index = Moments.Count - 1; index >= 0; index--)
                    if (Moments[index].Kind == KitMomentKind.Question) return index;
                return Moments.Count - 1;
            }
        }

        /// <summary>"Earlier: {n} more", counting only the moments before the shown one; null with none before it.</summary>
        public string? Earlier => Shown is int shown && shown > 0 ? KitMomentWords.Earlier(shown) : null;

        /// <summary>"{n} newer", counting the moments after a waiting question that stays shown; null with none after it.</summary>
        public string? Newer => Shown is int shown && shown < Moments.Count - 1 ? KitMomentWords.Newer(Moments.Count - 1 - shown) : null;

        /// <summary>The shown moment's actions, with Stop beneath while the work goes on, except on a question or at ready.</summary>
        public IReadOnlyList<KitMomentAction> Actions
        {
            get
            {
                if (Shown is not int shown) return Array.Empty<KitMomentAction>();
                var moment = Moments[shown];
                var stop = Working && moment.Kind != KitMomentKind.Question && moment.Kind != KitMomentKind.Ready;
                return stop ? moment.Actions.Append(KitMomentAction.Stop).ToList() : moment.Actions;
            }
        }
    }

    /// <summary>The room's moment words (WORDS.md, The room of projects).</summary>
    public static class KitMomentWords
    {
        public const string SentTo = "Sent to ";

        public const string WaitingToStart = "Waiting for it to start.";

        public const string SaysSuffix = " says";

        public const string AsksSuffix = " asks";

        public const string RanSuffix = " ran";

        public const string NotSeenYet = "Not seen on your computer yet.";

        public const string SeenOnYourComputer = "Seen on your computer";

        public const string CheckedByYourComputer = "Checked by your computer";

        public const string Skipped = "Skipped by your computer";

        public const string NotObservedWords = "Not observed: ";

        public const string InstallDidNotRun = "The install didn't run";

        public const string RunThisCommand = "Run this command? ";

        public const string NothingRunsYet = "Nothing runs until you answer.";

        public const string IsReady = " is ready";

        public static string Reported(KitFileChange change) => change switch
        {
            KitFileChange.Created => "Made ",
            KitFileChange.Deleted => "Removed ",
            _ => "Edited ",
        };

        public static string Observed(KitFileChange change) => change switch
        {
            KitFileChange.Created => "Made ",
            KitFileChange.Deleted => "Removed ",
            _ => "Changed ",
        };

        /// <summary>", 41 to 54 lines, seen 10:42": the counts the computer keeps for a file, as many as the change has, and when it read them.</summary>
        public static string LinesAndTime(KitFileChange change, int? before, int? after, DateTimeOffset seen)
        {
            var counts = change switch
            {
                KitFileChange.Changed when before is int was && after is int now => ", " + Number(was) + " to " + Lines(now),
                KitFileChange.Created when after is int made => ", " + Lines(made),
                _ => "",
            };
            return counts + ", seen " + seen.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>"+14 −3 lines" from a checkpoint diff.</summary>
        public static string Counts(KitCheckpointDiff diff) => "+" + Number(diff.Added) + " −" + Number(diff.Removed) + " lines";

        /// <summary>ADR 0033's line in the form the runtime declares; none where it declared nothing.</summary>
        public static string? Sandbox(KitSandbox sandbox) => sandbox switch
        {
            KitSandbox.MayLeave => "If you approve, it may run outside its sandbox, with your access to your computer.",
            KitSandbox.StaysInside => "If you approve, it still runs inside a sandbox.",
            _ => null,
        };

        /// <summary>A check of the computer's own: under way, its outcome, or not known; naming what it covered.</summary>
        public static string Check(KitCheckScope scope, bool ended, bool? passed)
        {
            var covered = scope == KitCheckScope.BuildsAndOpens ? "it builds and opens" : "it builds";
            if (!ended) return "Checking " + covered;
            return passed is bool outcome ? "Checked " + covered + (outcome ? ": passed" : ": failed") : "Ran a check: result not known";
        }

        /// <summary>The ready line: Built, in its time where known, then what the passed check covered, and no more.</summary>
        public static string Reward(TimeSpan? elapsed, KitCheckScope scope) =>
            (elapsed is TimeSpan taken ? "Built in " + Time(taken) : "Built") + ", checked " + (scope == KitCheckScope.BuildsAndOpens ? "it builds and opens." : "it builds.");

        public static string Earlier(int count) => "Earlier: " + Number(count) + " more";

        public static string Newer(int count) => Number(count) + " newer";

        public static string Action(KitMomentAction action) => action switch
        {
            KitMomentAction.ReadIt => "Read it",
            KitMomentAction.Stop => "Stop",
            KitMomentAction.YesRunIt => "Yes, run it",
            KitMomentAction.No => "No",
            KitMomentAction.SaySomethingElse => "Say something else",
            KitMomentAction.SeeTheOutput => "See the output",
            KitMomentAction.TryIt => "Try it",
            KitMomentAction.SeeWhatChanged => "See what changed",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        /// <summary>Whole seconds under a minute, else minutes and the seconds left; a moment under a second says so.</summary>
        public static string Time(TimeSpan time)
        {
            var seconds = (int)Math.Round(time.TotalSeconds, MidpointRounding.AwayFromZero);
            if (time.TotalSeconds < 0.5) return "under 1 s";
            if (seconds < 60) return Number(seconds) + " s";
            var minutes = seconds / 60;
            var left = seconds % 60;
            return Number(minutes) + " min" + (left > 0 ? " " + Number(left) + " s" : "");
        }

        private static string Lines(int count) => Number(count) + (count == 1 ? " line" : " lines");

        private static string Number(int count) => count.ToString(CultureInfo.InvariantCulture);
    }
}

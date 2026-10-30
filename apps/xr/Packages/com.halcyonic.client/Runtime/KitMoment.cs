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

        /// <summary>Who says it and how it is known, on the moment's eyebrow: "Codex · Reported", "Your Computer · Observed", "Agent · Question".</summary>
        public string Source { get; }

        /// <summary>The moment's subject; anything the runtime wrote inside it is theirs.</summary>
        public LineWords Headline { get; }

        /// <summary>
        /// The card's Title (the art direction's anatomy): Halcyonic's few words, with a name of theirs in its run where the
        /// words carry one; none where the moment is their prose or their command alone.
        /// </summary>
        public LineWords? Title { get; private set; }

        /// <summary>The card's metadata line, as segments the card joins with " · ": paths and names of theirs in their runs.</summary>
        public IReadOnlyList<LineWords> Meta { get; private set; } = Array.Empty<LineWords>();

        /// <summary>A command of theirs, shown in a well in their monospace, whole or cut with what is left said.</summary>
        public string? Well { get; private set; }

        /// <summary>Their own words, read as prose: what the runtime says of its work.</summary>
        public LineWords? Prose { get; private set; }

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
            KitMomentWords.YourComputer, LineWords.Ours(KitMomentWords.SentTitle + KitMomentWords.Dot) + LineWords.TheirName(runtime) + LineWords.Ours(KitMomentWords.Dot + KitMomentWords.WaitingToStart),
            null, NoLines, KitMomentTone.Neutral, NoActions, null)
            .Parts(LineWords.Ours(KitMomentWords.SentTitle), new[] { LineWords.TheirName(runtime), LineWords.Ours(KitMomentWords.WaitingToStart) });

        /// <summary>A file <paramref name="runtime"/> says it changed: its report, in the past tense, not seen yet.</summary>
        public static KitMoment ReportedEdit(string runtime, KitFileChange change, string file, DateTimeOffset? at) =>
            new KitMoment(KitMomentKind.ReportedEdit, KitProvenance.Reported, Says(runtime),
                LineWords.Ours(KitMomentWords.Reported(change) + KitMomentWords.Dot) + LineWords.TheirName(file), KitMomentWords.NotSeenYet, NoLines,
                KitMomentTone.Neutral, new[] { KitMomentAction.ReadIt }, at)
            .Parts(LineWords.Ours(KitMomentWords.Reported(change)), new[] { LineWords.TheirName(file) });

        /// <summary>
        /// A file the computer saw changed when it read the folder: its line counts before and after and the time, never its
        /// lines (ADR 0038). Added and removed lines only from a checkpoint diff, and only where undo is on (ADR 0036).
        /// </summary>
        public static KitMoment ObservedChange(KitFileChange change, string file, int? linesBefore, int? linesAfter, DateTimeOffset seen,
            KitCheckpointDiff? diff = null, bool undoOn = false)
        {
            var words = LineWords.Ours(KitMomentWords.Observed(change) + KitMomentWords.Dot) + LineWords.TheirName(file) + LineWords.Ours(KitMomentWords.LinesAndTime(change, linesBefore, linesAfter, seen));
            var shown = undoOn ? diff : null;
            return new KitMoment(KitMomentKind.ObservedChange, KitProvenance.Observed, KitMomentWords.YourComputer, words,
                shown == null ? null : KitMomentWords.Counts(shown), NoLines, KitMomentTone.Neutral,
                shown == null ? NoActions : new[] { KitMomentAction.ReadIt }, seen, shown)
                .Parts(LineWords.Ours(KitMomentWords.Observed(change)), new[]
                {
                    LineWords.TheirName(file),
                    KitMomentWords.LineCount(change, linesBefore, linesAfter) is string count ? LineWords.Ours(count) : null,
                    LineWords.Ours(KitMomentWords.SeenAt(seen)),
                });
        }

        /// <summary>What <paramref name="runtime"/> says of its work, its own words.</summary>
        public static KitMoment AgentSays(string runtime, string text, DateTimeOffset? at = null) => new KitMoment(KitMomentKind.AgentSays, KitProvenance.Reported,
            Says(runtime), LineWords.Theirs(text), null, NoLines, KitMomentTone.Neutral, NoActions, at).Parts(LineWords.Ours(KitMomentWords.AgentNote), prose: LineWords.Theirs(text));

        /// <summary>
        /// <paramref name="runtime"/> asks to run <paramref name="command"/>: ADR 0033's line in the form it declares, and Yes only
        /// for a request shown whole.
        /// </summary>
        public static KitMoment Question(string runtime, string command, KitSandbox sandbox, bool complete, DateTimeOffset? at)
        {
            // "Nothing runs until you answer" is deleted (the voice standard): the question and its locked answers carry it.
            var lines = new List<string>();
            if (KitMomentWords.Sandbox(sandbox) is string line) lines.Add(line);
            var actions = complete
                ? new[] { KitMomentAction.YesRunIt, KitMomentAction.No, KitMomentAction.SaySomethingElse }
                : new[] { KitMomentAction.No, KitMomentAction.SaySomethingElse };
            return new KitMoment(KitMomentKind.Question, KitProvenance.Reported, KitMomentWords.AgentQuestion,
                LineWords.Ours(KitMomentWords.RunThisCommand + KitMomentWords.Dot) + LineWords.TheirName(command), null, lines, KitMomentTone.Attention, actions, at)
                .Parts(LineWords.Ours(KitMomentWords.RunThisCommand), well: LabelText.Plain(command));
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
            return new KitMoment(KitMomentKind.HostCheck, KitProvenance.Observed, KitMomentWords.YourComputer,
                LineWords.Ours(KitMomentWords.Check(scope, ended, passed)), null, NoLines, tone, actions, at, null, passed, scope)
                .Parts(LineWords.Ours(KitMomentWords.CheckTitle(scope, ended, passed)), new[] { LineWords.Ours(KitMomentWords.CheckOutcome(scope, ended, passed)) });
        }

        /// <summary>A command <paramref name="runtime"/> says it ran and the outcome it reports: its words, no colour.</summary>
        public static KitMoment AgentCheck(string runtime, string command, string outcome, DateTimeOffset? at) =>
            new KitMoment(KitMomentKind.AgentCheck, KitProvenance.Reported, Says(runtime),
                LineWords.Ours(KitMomentWords.CheckReported + KitMomentWords.Dot) + LineWords.TheirName(command) + LineWords.Ours(KitMomentWords.Dot) + LineWords.Theirs(outcome),
                null, NoLines, KitMomentTone.Neutral, new[] { KitMomentAction.SeeTheOutput }, at)
            .Parts(LineWords.Ours(KitMomentWords.CheckReported), new[] { LineWords.Theirs(outcome) }, LabelText.Plain(command));

        /// <summary>A package the runtime wanted that the starter doesn't hold: refused, its install not run (what is on disk is not claimed).</summary>
        public static KitMoment PackageRefused(string package, DateTimeOffset? at) => new KitMoment(KitMomentKind.PackageRefused, KitProvenance.Observed,
            KitMomentWords.YourComputer, LineWords.Ours(KitMomentWords.NotInStarter + KitMomentWords.Dot) + LineWords.TheirName(package),
            KitMomentWords.InstallDidNotRun, NoLines, KitMomentTone.Neutral, NoActions, at)
            .Parts(LineWords.Ours(KitMomentWords.NotInStarter), new[] { LineWords.TheirName(package) });

        /// <summary>The computer didn't read this turn's files, for <paramref name="reason"/>: said, never read as no changes.</summary>
        public static KitMoment NotObserved(string reason, DateTimeOffset? at) => new KitMoment(KitMomentKind.NotObserved, KitProvenance.Observed,
            KitMomentWords.YourComputer, LineWords.Ours(KitMomentWords.NotObservedWords + KitMomentWords.Dot + reason), null, NoLines, KitMomentTone.Neutral, NoActions, at)
            .Parts(LineWords.Ours(KitMomentWords.NotObservedWords), new[] { LineWords.Ours(reason) });

        /// <summary>
        /// <paramref name="project"/> is ready: only after a check of the computer's own passed, saying what that check covered;
        /// Try it only where a route to try it exists.
        /// </summary>
        public static KitMoment Ready(string project, TimeSpan? elapsed, KitMoment check, bool tryRoute)
        {
            if (check.Kind != KitMomentKind.HostCheck || check.Passed != true || check.Scope is not KitCheckScope scope)
                throw new ArgumentException("Ready follows a passed check of the computer's own.", nameof(check));
            var actions = tryRoute ? new[] { KitMomentAction.TryIt, KitMomentAction.SeeWhatChanged } : new[] { KitMomentAction.SeeWhatChanged };
            return new KitMoment(KitMomentKind.Ready, KitProvenance.Observed, KitMomentWords.YourComputer,
                LineWords.Ours(KitMomentWords.BuildReady + KitMomentWords.Dot) + LineWords.TheirName(project), null, new[] { KitMomentWords.Reward(elapsed, scope) },
                KitMomentTone.Passed, actions, check.At, null, true, scope)
                .Parts(LineWords.Ours(KitMomentWords.BuildReady), new[] { LineWords.TheirName(project) });
        }

        private static string Says(string runtime) => LabelText.Name(runtime) + KitMomentWords.ReportedSuffix;

        /// <summary>Sets the card's parts (<see cref="Title"/>, <see cref="Meta"/>, <see cref="Well"/>, <see cref="Prose"/>) and returns the moment.</summary>
        private KitMoment Parts(LineWords? title, IEnumerable<LineWords?>? meta = null, string? well = null, LineWords? prose = null)
        {
            Title = title;
            Meta = (meta ?? Array.Empty<LineWords?>()).Where(segment => segment != null && !segment.IsEmpty).Select(segment => segment!).ToList();
            Well = well;
            Prose = prose;
            return this;
        }
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

        /// <summary>"Earlier · {n} More", counting only the moments before the shown one; null with none before it.</summary>
        public string? Earlier => Shown is int shown && shown > 0 ? KitMomentWords.Earlier(shown) : null;

        /// <summary>"{n} Newer", counting the moments after a waiting question that stays shown; null with none after it.</summary>
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
        /// <summary>The cadence's join between a line's segments (the owner's voice standard, 2026-10-30).</summary>
        public const string Dot = " · ";

        /// <summary>After a runtime's name on the eyebrow: what it says is its report.</summary>
        public const string ReportedSuffix = " · Reported";

        /// <summary>The eyebrow of what the computer itself saw or ran.</summary>
        public const string YourComputer = "Your Computer · Observed";

        /// <summary>The eyebrow of a question the agent asks: no app's name in our words, and that it is reported (WORDS.md, T2 and T7).</summary>
        public const string AgentQuestion = "Agent · Question · Reported";

        public const string SentTitle = "Sent";

        /// <summary>Accepted is not started (T1).</summary>
        public const string WaitingToStart = "Awaiting Start";

        public const string NotSeenYet = "Not Yet Observed";

        public const string AgentNote = "Agent Note";

        public const string CheckReported = "Check Reported";

        public const string NotInStarter = "Not in Starter";

        public const string NotObservedWords = "Not Observed";

        public const string InstallDidNotRun = "Install Not Run";

        public const string RunThisCommand = "Run Command?";

        public const string BuildReady = "Build Ready";

        /// <summary>A file the runtime says it changed, as the card's Title: its report.</summary>
        public static string Reported(KitFileChange change) => change switch
        {
            KitFileChange.Created => "New File Reported",
            KitFileChange.Deleted => "Removal Reported",
            _ => "Edit Reported",
        };

        /// <summary>A file the computer saw change, as the card's Title.</summary>
        public static string Observed(KitFileChange change) => change switch
        {
            KitFileChange.Created => "File Made",
            KitFileChange.Deleted => "File Removed",
            _ => "Change Observed",
        };

        /// <summary>" · 41 to 54 Lines · 10:42": the counts the computer keeps for a file, as many as the change has, and when it read them.</summary>
        public static string LinesAndTime(KitFileChange change, int? before, int? after, DateTimeOffset seen) =>
            (LineCount(change, before, after) is string count ? Dot + count : "") + Dot + SeenAt(seen);

        /// <summary>"41 to 54 Lines" or "12 Lines": the counts the computer keeps for a file, as many as the change has; none for a removal.</summary>
        public static string? LineCount(KitFileChange change, int? before, int? after) => change switch
        {
            KitFileChange.Changed when before is int was && after is int now => Number(was) + " to " + Lines(now),
            KitFileChange.Created when after is int made => Lines(made),
            _ => null,
        };

        /// <summary>"10:42": when the computer read the file.</summary>
        public static string SeenAt(DateTimeOffset seen) => seen.ToString("HH:mm", CultureInfo.InvariantCulture);

        /// <summary>"+14 −3 Lines" from a checkpoint diff.</summary>
        public static string Counts(KitCheckpointDiff diff) => "+" + Number(diff.Added) + " −" + Number(diff.Removed) + " Lines";

        /// <summary>ADR 0033's line in the form the runtime declares; none where it declared nothing.</summary>
        public static string? Sandbox(KitSandbox sandbox) => sandbox switch
        {
            KitSandbox.MayLeave => "May Run Outside Sandbox · Your Access",
            KitSandbox.StaysInside => "Runs Inside Sandbox",
            _ => null,
        };

        /// <summary>
        /// A check of the computer's own in one line, its Title then what it covered: "Computer Check · Builds · Opens · Under Way"
        /// while it runs, so it never shares the agent's own "Checking" (AUDIT's X17A-S1); "Build Passed · Builds · Opens",
        /// "Build Failed · Builds", or "Check Ran · Result Unknown" where its exit carried no outcome.
        /// </summary>
        public static string Check(KitCheckScope scope, bool ended, bool? passed) => CheckTitle(scope, ended, passed) + Dot + CheckOutcome(scope, ended, passed);

        /// <summary>The check's Title: Computer Check while it runs, then its outcome in it (ART's review of A3a).</summary>
        public static string CheckTitle(KitCheckScope scope, bool ended, bool? passed) =>
            !ended ? "Computer Check" : passed is bool outcome ? outcome ? "Build Passed" : "Build Failed" : "Check Ran";

        /// <summary>The check's metadata: what it covered and, while it runs, that it is under way; Result Unknown where its exit carried no outcome.</summary>
        public static string CheckOutcome(KitCheckScope scope, bool ended, bool? passed) =>
            ended && passed == null ? "Result Unknown" : Covered(scope) + (ended ? "" : Dot + "Under Way");

        private static string Covered(KitCheckScope scope) => scope == KitCheckScope.BuildsAndOpens ? "Builds" + Dot + "Opens" : "Builds";

        /// <summary>The ready line: what the passed check covered, then the time it took where known, and no more.</summary>
        public static string Reward(TimeSpan? elapsed, KitCheckScope scope) => Covered(scope) + (elapsed is TimeSpan taken ? Dot + Time(taken) : "");

        public static string Earlier(int count) => "Earlier" + Dot + Number(count) + " More";

        public static string Newer(int count) => Number(count) + " Newer";

        /// <summary>Under a command shown cut: what is left, in the handoff's words ("Cut · 18 More Lines").</summary>
        public static string CutMore(int lines) => "Cut · " + Number(lines) + (lines == 1 ? " More Line" : " More Lines");

        public static string Action(KitMomentAction action) => action switch
        {
            KitMomentAction.ReadIt => "Read File",
            KitMomentAction.Stop => "Stop",
            KitMomentAction.YesRunIt => "Run",
            KitMomentAction.No => "Decline",
            KitMomentAction.SaySomethingElse => "Reply",
            KitMomentAction.SeeTheOutput => "Read Output",
            KitMomentAction.TryIt => "Try Build",
            KitMomentAction.SeeWhatChanged => "See Changes",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        /// <summary>Whole seconds under a minute, else minutes and the seconds left; a moment under a second says so.</summary>
        public static string Time(TimeSpan time)
        {
            var seconds = (int)Math.Round(time.TotalSeconds, MidpointRounding.AwayFromZero);
            if (time.TotalSeconds < 0.5) return "Under 1 s";
            if (seconds < 60) return Number(seconds) + " s";
            var minutes = seconds / 60;
            var left = seconds % 60;
            return Number(minutes) + " min" + (left > 0 ? " " + Number(left) + " s" : "");
        }

        private static string Lines(int count) => Number(count) + (count == 1 ? " Line" : " Lines");

        private static string Number(int count) => count.ToString(CultureInfo.InvariantCulture);
    }
}

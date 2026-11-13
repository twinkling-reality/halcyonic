#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>How a line of a section reads. Color follows the words and never replaces them.</summary>
    public enum SectionTone
    {
        Normal,
        Secondary,
        /// <summary>Someone's words quoted, or a model's explanation: a claim, shown apart from facts.</summary>
        Claim,
        Attention,
        Problem,
        Good,
    }

    /// <summary>Which tab a section answers: Help me understand, or What was checked?.</summary>
    public enum SectionKind
    {
        Understanding,
        Checked,
    }

    /// <summary>
    /// How a line of an answer is known, which decides its chip. The source's five classes keep the
    /// source's own word; a measurement is the evaluation source's; Halcyonic's own words (a count, a
    /// heading, a provenance line) are no one's claim.
    /// </summary>
    public enum Evidence
    {
        /// <summary>Recorded by a runtime or the source: no chip, with the source named on its page.</summary>
        Observed,

        /// <summary>Said by the agent or a subagent, relayed and attributed: chipped with who said it.</summary>
        Reported,

        /// <summary>The source's deterministic heuristic: always chipped, never read as fact.</summary>
        Inferred,

        /// <summary>A step of a plan, not yet done: chipped.</summary>
        Planned,

        /// <summary>Written by a model: chipped, never evidence.</summary>
        Explained,

        /// <summary>The evaluation source's measurement and its statement about itself: no chip, the source named on its page.</summary>
        Measured,

        /// <summary>Halcyonic's own words about the answer, as a count or a heading: no chip, no source's claim.</summary>
        Halcyonic,
    }

    /// <summary>One line of a section: a claim with its epistemic class, or a measurement with its part.</summary>
    public sealed class SectionLine
    {
        /// <param name="rows">The most rows it may wrap to; a detail takes two unless told otherwise, any other line one.</param>
        /// <param name="source">Where the lines under it come from, as a second source's provenance line.</param>
        /// <param name="startsPage">It begins a step of a flow, so a page starts with it.</param>
        /// <param name="repeats">It heads its step, so a step that takes more than a page shows it again on the next.</param>
        /// <param name="evidence">How it is known; from <paramref name="tag"/> when not given.</param>
        /// <param name="words">What it says beside its chip, without saying the chip again; <paramref name="text"/> when not given.</param>
        /// <param name="chip">Its chip's words; from its evidence when not given.</param>
        /// <param name="said">
        /// What it says beside its chip as runs, Halcyonic's words and outside text (<see cref="LineWords"/>),
        /// which a file's page draws, outside text leaning; <paramref name="words"/> as Halcyonic's when not given.
        /// </param>
        public SectionLine(string tag, string text, SectionTone tone, bool detail = false, int rows = 0, bool source = false, bool startsPage = false,
            bool repeats = false, Evidence? evidence = null, string? words = null, string? chip = null, FileKind? file = null, LineWords? said = null,
            SourceWords? from = null)
        {
            Tag = tag;
            Text = text;
            Tone = tone;
            Detail = detail;
            Rows = rows > 0 ? rows : detail ? 2 : 1;
            Source = source;
            StartsPage = startsPage;
            Repeats = repeats;
            Evidence = evidence ?? (source ? Client.Evidence.Halcyonic : EvidenceOf(tag));
            Words = said?.Text ?? words ?? text;
            // Words given as a string alone may carry outside text: spelled first, so none is refused or shown raw.
            Runs = said ?? LineWords.Written(LabelText.Plain(Words));
            Chip = chip ?? ChipOf(Evidence);
            File = file;
            From = from;
        }

        /// <summary>A source line's parts apart (<see cref="SourceWords"/>), its text their one phrase; null for any other line.</summary>
        public SourceWords? From { get; }

        /// <summary>What it says beside its chip as runs: outside text, a quote, a label or a name, leans on Halcyonic's ground.</summary>
        public LineWords Runs { get; }

        /// <summary>The kind of the changed file the line names, for the generic glyph beside it; null for any other line.</summary>
        public FileKind? File { get; }

        /// <summary>How it is known, which decides whether it has a chip.</summary>
        public Evidence Evidence { get; }

        /// <summary>
        /// The chip a surface shows before or beside it: "Inferred", "Agent says", "Planned",
        /// "Model explains"; null for an observed fact, a measurement or Halcyonic's own words, which
        /// stand on a page that names their source.
        /// </summary>
        public string? Chip { get; }

        /// <summary>What it says beside its chip: the words alone beside their chip, for one.</summary>
        public string Words { get; }

        /// <summary>The evidence a tag names: a source's class word, else a measurement's part, else Halcyonic's own.</summary>
        public static Evidence EvidenceOf(string tag) => tag switch
        {
            "observed" => Client.Evidence.Observed,
            "reported" => Client.Evidence.Reported,
            "inferred" => Client.Evidence.Inferred,
            "planned" => Client.Evidence.Planned,
            "explained" => Client.Evidence.Explained,
            "" => Client.Evidence.Halcyonic,
            _ => Client.Evidence.Measured,
        };

        /// <summary>The chip for a class, null where the line goes without one.</summary>
        public static string? ChipOf(Evidence evidence) => evidence switch
        {
            Client.Evidence.Reported => AgentChip,
            Client.Evidence.Inferred => "Inferred",
            Client.Evidence.Planned => "Planned",
            Client.Evidence.Explained => ExplanationChip,
            _ => null,
        };

        /// <summary>The chip beside an agent's own words: who said it, and that it is reported (WORDS.md).</summary>
        public const string AgentChip = "Agent · Reported";

        /// <summary>The chip beside a subagent's own words.</summary>
        public const string SubagentChip = "Subagent · Reported";

        /// <summary>The chip beside words whose author the source does not know.</summary>
        public const string AuthorUnknownChip = "Author Unknown";

        /// <summary>The chip beside a model's account of the work, after it.</summary>
        public const string ExplanationChip = "Explanation · Reported";

        /// <summary>
        /// Whether a chip attributes an agent's or a subagent's words whole, so the words beside it may
        /// stand upright (ADR 0026): <see cref="AgentChip"/>, the kit's report chip too, or <see cref="SubagentChip"/>.
        /// </summary>
        public static bool AttributesWhole(string? chip) => chip == AgentChip || chip == SubagentChip;

        /// <summary>
        /// The same line with some of it changed, keeping its evidence, its words and its chip as they
        /// are: a subagent's words copied keep the subagent's chip.
        /// </summary>
        public SectionLine With(string? tag = null, string? text = null, string? words = null, string? chip = null, bool? startsPage = null,
            bool? repeats = null) =>
            new SectionLine(tag ?? Tag, text ?? Text, Tone, Detail, Rows, Source, startsPage ?? StartsPage, repeats ?? Repeats, Evidence, words ?? Words,
                chip ?? Chip, File, words == null ? Runs : null, From);

        /// <summary>
        /// The claim's epistemic class in the source's own word ("observed", "reported", "inferred",
        /// "planned", "explained"), the part a measurement belongs to ("Cost"), or empty.
        /// </summary>
        public string Tag { get; }

        /// <summary>Plain text: the source's words as <see cref="LabelText.Plain"/> shows them, or the app's own. Never markup.</summary>
        public string Text { get; }

        public SectionTone Tone { get; }

        /// <summary>A line about the line above it, such as a part's availability, coverage and freshness, drawn smaller.</summary>
        public bool Detail { get; }

        /// <summary>The most rows it wraps to; past them it ends in an ellipsis.</summary>
        public int Rows { get; }

        /// <summary>
        /// A provenance line inside the section, saying where the lines under it come from, as What
        /// was checked? says for its second source, in the product's own words (<see cref="SourceWords"/>).
        /// </summary>
        public bool Source { get; }

        /// <summary>A step of a flow begins here: a page starts with it.</summary>
        public bool StartsPage { get; }

        /// <summary>It heads its step of a flow, shown again at the top of the step's next page when the step takes more than one.</summary>
        public bool Repeats { get; }
    }

    /// <summary>What one section of the workspace shows, in words, so the XR layer only lays it out.</summary>
    public sealed class SectionPresentation
    {
        /// <param name="provenanceRuns">The provenance as runs; Halcyonic's words alone when not given.</param>
        /// <param name="from">The provenance's parts apart, where it says where an answer came from or why there is none.</param>
        public SectionPresentation(SectionKind kind, string provenance, SectionTone provenanceTone, IReadOnlyList<SectionLine> lines, bool simulated,
            bool steps = false, LineWords? provenanceRuns = null, SourceWords? from = null)
        {
            Kind = kind;
            Provenance = provenance;
            From = from;
            ProvenanceRuns = provenanceRuns ?? LineWords.Written(LabelText.Plain(provenance));
            ProvenanceTone = provenanceTone;
            // A source's sentence left blank, as its contract allows, is absent: a line of no words says nothing
            // and can't be drawn.
            Lines = lines.Where(line => !string.IsNullOrWhiteSpace(line.Words) && !string.IsNullOrWhiteSpace(line.Text)).ToList();
            Simulated = simulated;
            Steps = steps;
        }

        public SectionKind Kind { get; }

        /// <summary>
        /// Where the answer comes from and when, or why there is none, in the product's own words: never a
        /// source product's name or the version it names for itself, and "Simulated" for a stand-in's.
        /// </summary>
        public string Provenance { get; }

        /// <summary><see cref="Provenance"/>'s parts apart, where it is a source's (<see cref="SourceWords"/>); null for a section's own words.</summary>
        public SourceWords? From { get; }

        /// <summary><see cref="Provenance"/> as runs.</summary>
        public LineWords ProvenanceRuns { get; }

        public SectionTone ProvenanceTone { get; }

        public IReadOnlyList<SectionLine> Lines { get; }

        /// <summary>The answer came from a stand-in for the source, as in the recorded demonstration.</summary>
        public bool Simulated { get; }

        /// <summary>The answer is a flow a person steps through, a page for each step, rather than a list that pages.</summary>
        public bool Steps { get; }
    }

    /// <summary>
    /// Turns what the evaluation source measured about an execution into the measurement What was
    /// checked? ends with. Each part, the checks, the cost and the outcome, keeps its own availability,
    /// coverage and freshness, never combined; the cost is always the source's estimate from tokens;
    /// nothing is a score; and a value the source does not have is said to be unknown or pending,
    /// never zero.
    /// </summary>
    public static class EvaluationPresenter
    {
        /// <summary>
        /// The measurement's lines: first a <see cref="SectionLine.Source"/> line saying where they
        /// come from and when, or why there are none, then each part with its own statement.
        /// </summary>
        /// <param name="now">This device's time; a recorded answer is described as of when it was recorded.</param>
        public static IReadOnlyList<SectionLine> Measurement(
            IntelligenceRead<EvaluationResponse>? read,
            bool loading,
            string? error,
            DateTimeOffset now,
            TimeZoneInfo zone)
        {
            SectionLine Source(string text, SectionTone tone) => new SectionLine("", text, tone, rows: 2, source: true);
            SectionLine From(SourceWords from, SectionTone tone) => new SectionLine("", from.Text, tone, rows: 2, source: true, from: from);
            if (read == null)
            {
                return new[]
                {
                    loading ? Source("Reading Measurement", SectionTone.Secondary)
                    : error == DemonstrationReads.NotRecorded ? Source(error, SectionTone.Secondary)
                    : error != null ? From(new SourceWords(IntelligenceText.Measurement, state: "Measurement Unreadable", why: new Notice(error)), SectionTone.Problem)
                    : Source("Not Read", SectionTone.Secondary),
                };
            }
            var status = IntelligenceText.ReadingStatus(loading, error);
            switch (read.Response.Result)
            {
                case AvailableEvaluation available:
                    var evaluation = available.Evaluation;
                    var synthetic = evaluation.Source.Synthetic;
                    var at = read.Recorded ? read.ReadAt : now;
                    var lines = new List<SectionLine>
                    {
                        From(IntelligenceText.EvaluationSource(read, synthetic, now, zone, status), SectionTone.Secondary),
                    };
                    lines.AddRange(Lines(evaluation, at, zone));
                    return lines;
                case NotFoundEvaluation notFound:
                    return new[] { From(IntelligenceText.Without(IntelligenceText.Measurement, "Not Measured", notFound.Reason, status), SectionTone.Secondary) };
                case UnavailableEvaluation unavailable:
                    return new[] { From(IntelligenceText.Without(IntelligenceText.Measurement, "Measurement Unavailable", unavailable.Reason, status), SectionTone.Secondary) };
                case IncompatibleEvaluation incompatible:
                    return new[] { From(IntelligenceText.Without(IntelligenceText.Measurement, "Measurement Unreadable", incompatible.Reason, status), SectionTone.Problem) };
                case UnauthorizedEvaluation unauthorized:
                    return new[] { From(IntelligenceText.Without(IntelligenceText.Measurement, "Measurement Not Allowed", unauthorized.Reason, status), SectionTone.Secondary) };
                default:
                    return new[] { Source("Measurement Format Unknown", SectionTone.Secondary) };
            }
        }

        /// <summary>The checks first, as What was checked? asks about them, then the cost and the outcome.</summary>
        private static IReadOnlyList<SectionLine> Lines(Evaluation evaluation, DateTimeOffset now, TimeZoneInfo zone)
        {
            var lines = new List<SectionLine>();
            var verification = evaluation.Verification;
            var lens = verification.Lens;
            // Each part's name heads its line in a file, where no tag is drawn (settled 2026-10-08): "Checks · ", "Cost · ", "Outcome · ".
            var checks = LineWords.Written("Checks · ");
            if (lens == null)
            {
                lines.Add(new SectionLine("Checks", "No Verification Lens", SectionTone.Secondary, said: checks + LineWords.Written("No Verification Lens")));
            }
            else if (lens.ByKind.Count == 0)
            {
                lines.Add(new SectionLine("Checks", IntelligenceText.PlainOr(lens.EmptyReason, IntelligenceText.NothingMeasured), SectionTone.Secondary,
                    said: checks + UnderstandingPresenter.SummaryOr(lens.EmptyReason)));
            }
            else
            {
                var kinds = lens.ByKind.Select(KindRuns).Aggregate((said, next) => said + LineWords.Written("; ") + next);
                lines.Add(new SectionLine("Checks", kinds.Text, SectionTone.Normal, said: checks + kinds));
            }
            lines.Add(Status(verification.Availability, verification.Coverage, verification.Freshness, now, zone));

            var cost = evaluation.Cost;
            var costLead = LineWords.Written("Cost · ");
            if (cost.EstimatedUsd is double usd)
            {
                var costRuns = LineWords.Written(Money(usd)) + (LineWords.Theirs(cost.Note) is { IsEmpty: false } note ? LineWords.Written(" · ") + note : LineWords.None);
                lines.Add(new SectionLine("Cost", costRuns.Text, SectionTone.Normal, said: costLead + costRuns));
            }
            else
            {
                lines.Add(new SectionLine("Cost", "Unknown · Unpriced or Not Measured", SectionTone.Secondary, said: costLead + LineWords.Written("Unknown · Unpriced or Not Measured")));
            }
            lines.Add(Status(cost.Availability, cost.Coverage, cost.Freshness, now, zone));

            var outcome = evaluation.Outcome;
            var measure = outcome.Measure;
            if (measure == null)
            {
                lines.Add(new SectionLine("Outcome", IntelligenceText.NothingMeasured, SectionTone.Secondary, said: LineWords.Written("Outcome · " + IntelligenceText.NothingMeasured)));
            }
            else
            {
                var outcomeWords = Commits(measure.CommitsLanded) + " · " + Errors(measure, zone) + " · " + End(measure.EndReason);
                lines.Add(new SectionLine("Outcome", outcomeWords, SectionTone.Normal, said: LineWords.Written("Outcome · " + outcomeWords)));
                lines.Add(new SectionLine("", Uncommitted(measure.Uncommitted) + " · " + Survival(measure.LineSurvival), SectionTone.Normal,
                    evidence: Evidence.Measured));
            }
            lines.Add(Status(outcome.Availability, outcome.Coverage, outcome.Freshness, now, zone));
            return lines;
        }

        /// <summary>One part's own statement about itself: whether it is available, what it covers, and how current it is.</summary>
        public static SectionLine Status(EvaluationAvailability availability, EvaluationCoverage coverage, EvaluationFreshness freshness, DateTimeOffset now, TimeZoneInfo zone)
        {
            // Amber is for what waits for the person only; the words say partial, stale or unavailable.
            const SectionTone tone = SectionTone.Secondary;
            string state;
            switch (availability.State)
            {
                case EvaluationAvailabilityState.Available:
                    state = "Available";
                    break;
                case EvaluationAvailabilityState.Partial:
                    state = "Partly Available" + (availability.Reason is EvaluationAvailabilityReason partly ? " · " + Reason(partly) : "");
                    break;
                default:
                    state = "Unavailable" + (availability.Reason is EvaluationAvailabilityReason reason ? " · " + Reason(reason) : "");
                    break;
            }
            var covers = coverage.IncludedSessions.ToString(CultureInfo.InvariantCulture) + " of "
                + IntelligenceText.Plural(coverage.MatchedSessions, "Session");
            if (coverage.Complete)
            {
                covers += " · Complete";
            }
            else
            {
                covers += " · Incomplete" + (coverage.Omissions.Count == 0 ? "" : " · " + string.Join(", ", coverage.Omissions.Select(Omission)));
            }
            string current;
            if (IntelligenceText.TryParse(freshness.StaleAt, out var staleAt) && now >= staleAt)
            {
                // After this instant the part must read as stale, whatever its state said when read.
                current = "Stale Since " + IntelligenceText.Clock(staleAt, zone, seconds: false);
            }
            else
            {
                current = freshness.State switch
                {
                    EvaluationFreshnessState.Fresh => "Fresh",
                    EvaluationFreshnessState.Stale => "Stale",
                    _ => "Recomputing",
                };
                current += freshness.DataThrough != null && IntelligenceText.TryParse(freshness.DataThrough, out var through)
                    ? " · Data to " + IntelligenceText.Clock(through, zone, seconds: true)
                    : " · No Data Yet";
            }
            return new SectionLine("", state + " · " + covers + " · " + current, tone, detail: true, evidence: Evidence.Measured);
        }

        /// <summary>A kind of check as Seorak measured it, Halcyonic's counts first and its label last, leaning (settled 2026-10-08).</summary>
        private static LineWords KindRuns(EvaluationVerificationKind kind)
        {
            var tally = kind.Passed is long passes && kind.Runs is long count
                ? passes.ToString(CultureInfo.InvariantCulture) + " of " + count.ToString(CultureInfo.InvariantCulture) + " Passed"
                : (kind.Passed is long passed ? passed.ToString(CultureInfo.InvariantCulture) + " Passed" : "Passes Unknown")
                    + " · " + (kind.Runs is long runs ? IntelligenceText.Plural(runs, "Run") : "Runs Unknown");
            var rate = kind.PassRate is double fraction ? (fraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "No Pass Rate";
            var counts = LineWords.Written(tally + " · " + rate);
            return LineWords.Theirs(kind.Label) is { IsEmpty: false } label ? counts + LineWords.Written(" · ") + label : counts;
        }

        private static string Commits(long? landed) => landed switch
        {
            null => "Commits Unknown",
            0 => "No Commits Landed",
            _ => IntelligenceText.Plural(landed.Value, "Commit") + " Landed",
        };

        private static string Errors(EvaluationOutcomeMeasure measure, TimeZoneInfo zone)
        {
            if (!(measure.ErrorCount is long count)) return "Tool Errors Unknown";
            if (count == 0) return "No Tool Errors";
            var first = measure.FirstErrorAt != null && IntelligenceText.TryParse(measure.FirstErrorAt, out var at)
                ? " · First at " + IntelligenceText.Clock(at, zone, seconds: false)
                : "";
            return IntelligenceText.Plural(count, "Tool Error") + first;
        }

        private static string End(EvaluationEndReason? reason) => reason switch
        {
            null => "End Unknown",
            EvaluationEndReason.Clear => "Ended · Cleared",
            EvaluationEndReason.Resume => "Ended · Resumed Elsewhere",
            EvaluationEndReason.Logout => "Ended · Logged Out",
            EvaluationEndReason.PromptInputExit => "Ended · Person Exited",
            EvaluationEndReason.BypassPermissionsDisabled => "Ended · Permission Bypass Off",
            EvaluationEndReason.Other => "Ended · Other Reason",
            _ => "Ended · Reason Unnamed",
        };

        private static string Uncommitted(EvaluationUncommitted? uncommitted)
        {
            if (uncommitted == null) return "Uncommitted · Known at End";
            var text = "Uncommitted · " + IntelligenceText.Plural(uncommitted.FilesTouched, "File") + " · +"
                + uncommitted.LinesAdded.ToString(CultureInfo.InvariantCulture) + " −" + uncommitted.LinesRemoved.ToString(CultureInfo.InvariantCulture);
            if (uncommitted.GeneratedLinesExcluded > 0)
            {
                text += " · " + IntelligenceText.Plural(uncommitted.GeneratedLinesExcluded, "Generated Line") + " Apart";
            }
            return text;
        }

        private static string Survival(EvaluationLineSurvival? survival)
        {
            if (survival == null) return "3-Day Survival · Pending";
            var rate = survival.Rate is double fraction ? (fraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "Rate Unknown";
            var lines = " · " + survival.LinesSurviving.ToString(CultureInfo.InvariantCulture) + " of "
                + IntelligenceText.Plural(survival.LinesAuthored, "Line");
            return survival.Fate switch
            {
                EvaluationLineSurvivalFate.Retained => "Kept After 3 Days · " + rate + lines,
                EvaluationLineSurvivalFate.Overwritten => "Overwritten Within 3 Days" + lines,
                EvaluationLineSurvivalFate.Unreachable => "Unreachable After 3 Days" + lines,
                _ => "3-Day Survival Unknown",
            };
        }

        private static string Reason(EvaluationAvailabilityReason reason) => reason switch
        {
            EvaluationAvailabilityReason.NotCaptured => "Not Captured",
            EvaluationAvailabilityReason.NotRetained => "No Longer Kept",
            EvaluationAvailabilityReason.NotYetComputed => "Not Yet Computed",
            EvaluationAvailabilityReason.OutsideCredentialRestriction => "Outside Credential's Reach",
            EvaluationAvailabilityReason.TemporarilyUnavailable => "Temporarily Unavailable",
            EvaluationAvailabilityReason.ResultLimit => "Result Limit Reached",
            _ => "Reason Unnamed",
        };

        private static string Omission(EvaluationCoverageOmission omission) => omission switch
        {
            EvaluationCoverageOmission.OutsideRetention => "Outside Retention",
            EvaluationCoverageOmission.CaptureUnavailable => "Capture Unavailable",
            EvaluationCoverageOmission.ProjectionPending => "Still Computing",
            EvaluationCoverageOmission.CredentialRestriction => "Credential Restriction",
            EvaluationCoverageOmission.ResultLimit => "Result Limit",
            _ => "Unnamed Gap",
        };

        private static string Money(double usd) =>
            usd < 0.005 ? "Under $0.01" : "About $" + usd.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>The words and text handling the sections share.</summary>
    public static class IntelligenceText
    {
        /// <summary>
        /// Why a source answered without conclusions, by its code, after the lead ("Measurement Unavailable · "),
        /// never the reason's message, which is the source's or the reader's own and can hold an address, a path
        /// or the source's words. The source is named only by the provenance line before it, so the why never
        /// names it (settled by the coordinator, 2026-10-07).
        /// </summary>
        public static string Why(ErrorInfo reason) => Reason(reason).Phrase;

        /// <summary>
        /// The same as a title and a line apart (FIELD's words, 2026-11-12): Refresh is the button, so no line repeats it, and a
        /// code with no words of its own is "Reason Unknown", never another case's words (AUDIT's census: the host's
        /// own_sources_not_set_up read "No Reason Given · Refresh").
        /// </summary>
        public static Notice Reason(ErrorInfo reason) => reason.Code switch
        {
            "own_sources_not_set_up" => new Notice("Session Not Seen", "Its source hasn't been told where this app keeps sessions; Setup on your computer shows how"),
            "not_running" => new Notice("Not Running", "Start it on your computer"),
            "unreachable" or "timed_out" => new Notice("No Answer"),
            "temporarily_unavailable" or "server_error" => new Notice("Source Busy"),
            "rate_limited" => new Notice("Too Many Requests", "Wait a while before you refresh"),
            "credential_missing" => new Notice("Credential Missing", "Set one up on your computer"),
            // Neither reader sends a credential that isn't one of its kind (their tests: no request with it).
            "credential_malformed" => new Notice("Wrong Kind of Credential", "It was not sent; set up another"),
            "credential_rejected" => new Notice("Credential Refused", "Set up a new one"),
            "credential_forbidden" or "insufficient_scope" => new Notice("Credential Lacks Access", "Set up another"),
            "outside_credential_restriction" => new Notice("Credential for Other Work", "Set up another"),
            "not_captured" or "not_observed" => new Notice("Task Not Seen Yet"),
            "not_yet_computed" => new Notice("Not Computed Yet"),
            "not_retained" => new Notice("Task No Longer Kept"),
            "runtime_not_observed" => new Notice("Agent App Not Followed"),
            "not_resolvable" or "not_observable" => new Notice("Task Not Found", "Nothing goes by its name"),
            "result_limit" => new Notice("Result Limit Reached"),
            "invalid_document" or "unexpected_status" or "unsupported_contract" or "answer_too_large" or "invalid_understanding" or "invalid_evaluation"
                => new Notice("Answer Unreadable", "Check that both run the same version"),
            "host_not_allowed" or "origin_not_allowed" => new Notice("Request Turned Away", "Check the setup on your computer"),
            // Salidium's reader asks the port who it is before any credential goes, but that question is sent.
            "instance_mismatch" => new Notice("Wrong Source Answered", "Check the setup on your computer"),
            "discovery_unreadable" => new Notice("Setup Unreadable", "Check it on your computer"),
            NotAsked => new Notice("Session Not Known Yet"),
            _ => Notice.Unknown,
        };

        /// <summary>
        /// Why this headset's own read failed (a section's, the catalog's, the projects'), by what failed and never the error's
        /// message, which is the control plane's or the runtime's and can hold an address: a plain sentence of what happened,
        /// naming no control, since the page's own Refresh is drawn; a remedy no control gives is a second sentence (the lead's
        /// ruling, 2026-11-13). A refused credential reads as <paramref name="accessRefused"/> says it for how this headset
        /// reaches the computer (settled by the coordinator, 2026-10-07).
        /// </summary>
        public static string WhyUnread(Exception? error, string accessRefused) => error switch
        {
            OperationCanceledException => "The computer took too long to answer.",
            _ when Within<UnaskedReadException>(error) is UnaskedReadException unasked => unasked.Message,
            _ when Within<CertificateMismatchException>(error) != null => ConnectionText.NotThePairedComputer,
            _ when Within<TokenNotSentException>(error) is TokenNotSentException notSent && notSent.Outcome == LoopbackProofOutcome.Unproved
                => "What answered couldn't prove it is your computer, so nothing was sent. Check that the app is running there.",
            _ when Within<TokenNotSentException>(error) is TokenNotSentException notSent && notSent.Outcome == LoopbackProofOutcome.Unreachable => DidntAnswer,
            ControlPlaneRequestException { Code: "device_revoked", Status: 401 } => ConnectionText.PairingRefused,
            ControlPlaneRequestException { Code: "unauthorized", Status: 401 } => accessRefused,
            ControlPlaneRequestException { Code: "too_many_requests", Status: 429 }
                => "The computer paused this headset after too many tries. Wait a minute.",
            _ when Within<Newtonsoft.Json.JsonException>(error) != null => ConnectionText.Unreadable,
            // Something answered, with what isn't HTTP or too much of it, or took the connection and set up no secure
            // one: never "didn't answer".
            _ when Within<System.IO.InvalidDataException>(error) != null || Within<FormatException>(error) != null || Within<OverflowException>(error) != null
                || Within<HandshakeFailedException>(error) != null => SomethingWentWrong,
            ControlPlaneRequestException { Code: null, InnerException: System.Net.Http.HttpRequestException } => DidntAnswer,
            _ => SomethingWentWrong,
        };

        private const string DidntAnswer = "The computer didn't answer.";
        private const string SomethingWentWrong = "Something went wrong reading it.";

        /// <summary>The headset's read when there is nothing to ask (settled by the coordinator, 2026-10-07).</summary>
        public const string NotConnected = "This headset isn't connected to the computer.";

        /// <summary>Said where a source measured nothing, or said so in words that show as nothing (settled by the coordinator, 2026-10-08).</summary>
        public const string NothingMeasured = "Not Measured";

        /// <summary>A source's sentence made plain, or Halcyonic's own words where it shows as nothing, so the fact it states is never left unsaid.</summary>
        public static string PlainOr(string? text, string instead) => Plain(text) is { Length: > 0 } plain ? plain : instead;

        private static T? Within<T>(Exception? error) where T : Exception
        {
            for (var each = error; each != null; each = each.InnerException)
            {
                if (each is T found) return found;
            }
            return null;
        }

        /// <summary>
        /// Text from a source, made plain by the one rule for text Halcyonic did not write
        /// (<see cref="LabelText.Plain"/>): one line of exactly what it says, with every character
        /// that would not show as itself, such as a bidirectional override or a zero width space,
        /// shown as its code point. Markup characters stay as they are; the labels that show them
        /// never interpret markup.
        /// </summary>
        public static string Plain(string? text) => LabelText.Plain(text);

        /// <summary>A path's last part, which fits a line where the whole path would not.</summary>
        public static string FileName(string path)
        {
            var plain = Plain(path).TrimEnd('/', '\\');
            var slash = Math.Max(plain.LastIndexOf('/'), plain.LastIndexOf('\\'));
            return slash < 0 ? plain : plain.Substring(slash + 1);
        }

        public static string Truncate(string text, int maxLength) => WorkspaceText.Truncate(text, maxLength);

        public static string Plural(long count, string noun) =>
            count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? noun : noun + "s");

        /// <summary>How long ago an instant was, by this device's clock, in words.</summary>
        public static string Ago(string timestamp, DateTimeOffset now, TimeZoneInfo zone) =>
            TryParse(timestamp, out var at) ? Ago(at, now, zone) : "Time Unknown";

        public static string Ago(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
        {
            var elapsed = now - at;
            if (elapsed < TimeSpan.FromMinutes(1)) return "Just Now";
            if (elapsed < TimeSpan.FromHours(1)) return ((long)elapsed.TotalMinutes).ToString(CultureInfo.InvariantCulture) + " min ago";
            if (elapsed < TimeSpan.FromDays(1)) return ((long)elapsed.TotalHours).ToString(CultureInfo.InvariantCulture) + " h ago";
            return TimeZoneInfo.ConvertTime(at, zone).ToString("d MMM HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>A clock time in the person's zone.</summary>
        public static string Clock(DateTimeOffset at, TimeZoneInfo zone, bool seconds) =>
            TimeZoneInfo.ConvertTime(at, zone).ToString(seconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);

        public static bool TryParse(string timestamp, out DateTimeOffset at) =>
            DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out at);

        /// <summary>
        /// Where an understanding came from, in the product's own words, and when: "Explanation, 2 min ago", or a
        /// stand-in's, said to be simulated: an explanation, or, above the check runs it saw, checks. The source's
        /// own name and the version it names for itself are data and never drawn (the lead's ruling, 2026-11-13).
        /// </summary>
        public static SourceWords UnderstandingSource(IntelligenceRead<UnderstandingResponse> read, UnderstandingSource source, DateTimeOffset now,
            TimeZoneInfo zone, bool checks = false, Notice? status = null)
        {
            var when = read.Recorded ? "Recorded " + Clock(read.ReadAt, zone, seconds: true) : Ago(source.GeneratedAt, now, zone);
            var from = source.Synthetic ? checks ? "Simulated Checks" : "Simulated Explanation" : Explanation;
            return new SourceWords(from, when: when, status: status);
        }

        /// <summary><see cref="UnderstandingSource"/>'s one phrase.</summary>
        public static string UnderstandingProvenance(IntelligenceRead<UnderstandingResponse> read, UnderstandingSource source, DateTimeOffset now, TimeZoneInfo zone,
            bool checks = false) => UnderstandingSource(read, source, now, zone, checks).Text;

        /// <summary><see cref="UnderstandingSource"/>'s one phrase as runs, all Halcyonic's words.</summary>
        public static LineWords UnderstandingProvenanceRuns(IntelligenceRead<UnderstandingResponse> read, UnderstandingSource source, DateTimeOffset now,
            TimeZoneInfo zone, bool checks = false, Notice? status = null) => LineWords.Written(UnderstandingSource(read, source, now, zone, checks, status).Text);

        /// <summary>Where a measurement came from and when: "Measurement, read 1 min ago", or a stand-in's, said to be simulated.</summary>
        public static SourceWords EvaluationSource(IntelligenceRead<EvaluationResponse> read, bool synthetic, DateTimeOffset now, TimeZoneInfo zone,
            Notice? status = null)
        {
            var when = read.Recorded ? "Recorded " + Clock(read.ReadAt, zone, seconds: true) : "Read " + Ago(read.ReadAt, now, zone);
            return new SourceWords(synthetic ? "Simulated Measurement" : Measurement, when: when, status: status);
        }

        /// <summary><see cref="EvaluationSource"/>'s one phrase.</summary>
        public static string EvaluationProvenance(IntelligenceRead<EvaluationResponse> read, bool synthetic, DateTimeOffset now, TimeZoneInfo zone) =>
            EvaluationSource(read, synthetic, now, zone).Text;

        /// <summary>A read of a section under way again, or failed with this headset's words for why (<see cref="WhyUnread"/>); null when neither.</summary>
        internal static Notice? ReadingStatus(bool loading, string? error) =>
            loading ? new Notice("Reading Again") : error != null ? new Notice("Reread Failed", error) : (Notice?)null;

        internal static SectionPresentation Empty(SectionKind kind, string text) =>
            new SectionPresentation(kind, text, SectionTone.Secondary, Array.Empty<SectionLine>(), simulated: false);

        /// <summary>The section while the understanding it shows has not been read.</summary>
        internal static SectionPresentation Waiting(SectionKind kind, bool loading, string? error)
        {
            if (loading) return Empty(kind, "Reading Understanding");
            // A recorded run carries no understanding read: nothing failed, so it is said calmly.
            if (error == DemonstrationReads.NotRecorded) return Empty(kind, error);
            if (error != null)
            {
                var from = new SourceWords(Explanation, state: "Understanding Unreadable", why: new Notice(error));
                return new SectionPresentation(kind, from.Text, SectionTone.Problem, Array.Empty<SectionLine>(), simulated: false, from: from);
            }
            return Empty(kind, "Not Read");
        }

        /// <summary>
        /// A source that answered without conclusions, or a measurement: what is missing and why, apart, in the
        /// product's own words; the source is a part of the model and never a product's name.
        /// </summary>
        internal static SourceWords Without(string source, string state, ErrorInfo reason, Notice? status) =>
            new SourceWords(reason.Code == NotAsked ? null : source, state: state, why: Reason(reason), status: status);

        /// <summary>A section whose source answered without conclusions: what is missing and why, said apart (<see cref="Without"/>).</summary>
        internal static SectionPresentation Failure(SectionKind kind, string state, ErrorInfo reason, SectionTone tone, Notice? status)
        {
            var from = Without(Explanation, state, reason, status);
            return new SectionPresentation(kind, from.Text, tone, Array.Empty<SectionLine>(), simulated: false, from: from);
        }

        /// <summary>
        /// The control plane's own answer before it asks any source: the runtime has not said which
        /// session it is, so no source was asked and none is named.
        /// </summary>
        public const string NotAsked = "native_id_unknown";

        /// <summary>Where an understanding came from, in the product's own words: the only words the headset has for it.</summary>
        public const string Explanation = "Explanation";

        /// <summary>Where a measurement came from, in the product's own words: the only words the headset has for it.</summary>
        public const string Measurement = "Measurement";
    }
    /// <summary>
    /// Where a section's answer came from, or why there is none, as parts apart (the lead's ruling, 2026-11-13): the
    /// source in the product's own words (never a source product's name: a provider's id is data in the records, never
    /// drawn), what is missing, why (a <see cref="Notice"/> from the host's code, never its message), when, and how this
    /// headset's own read of it stands. A slot of one string takes them as one phrase (<see cref="Text"/>), as a
    /// <see cref="Notice"/> does: never joined on a dot.
    /// </summary>
    public sealed class SourceWords
    {
        public SourceWords(string? source, string? when = null, string? state = null, Notice? why = null, Notice? status = null)
        {
            Source = source;
            When = when;
            State = state;
            Why = why;
            Status = status;
        }

        /// <summary>The source in the product's own words ("Explanation", "Simulated Checks"); null where no source was asked.</summary>
        public string? Source { get; }

        /// <summary>When the answer was made or read ("2 min ago", "Recorded 09:00:00"); null for a missing one.</summary>
        public string? When { get; }

        /// <summary>What is missing ("Conclusions Unavailable"); null for an answer.</summary>
        public string? State { get; }

        /// <summary>Why it is missing, in the product's words for the host's code.</summary>
        public Notice? Why { get; }

        /// <summary>This headset's read again, under way or failed; null when neither.</summary>
        public Notice? Status { get; }

        /// <summary>
        /// The parts as one phrase, as <see cref="Notice.Phrase"/> joins a title and its line: an answer's source and when
        /// ("Explanation, 2 min ago"), or what is missing and why ("Conclusions Unavailable, Agent App Not Followed"), the
        /// state of this headset's read after; the source is not said where something is missing, since what is missing
        /// names it.
        /// </summary>
        public string Text
        {
            get
            {
                var parts = new List<string>();
                if (State == null)
                {
                    if (Source != null) parts.Add(Source);
                    if (When != null) parts.Add(When);
                }
                else
                {
                    parts.Add(State);
                    if (Why is Notice why) parts.Add(why.Phrase);
                }
                // This headset's own words for its read, each as it is: "Reread Failed", then why.
                if (Status is Notice status)
                {
                    parts.Add(status.Title);
                    if (status.Line is { Length: > 0 } line) parts.Add(line);
                }
                return string.Join(", ", parts);
            }
        }
    }
}

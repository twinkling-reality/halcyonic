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
            bool repeats = false, Evidence? evidence = null, string? words = null, string? chip = null, FileKind? file = null, LineWords? said = null)
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
            Runs = said ?? LineWords.Written(Words);
            Chip = chip ?? ChipOf(Evidence);
            File = file;
        }

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
                chip ?? Chip, File, words == null ? Runs : null);

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
        /// was checked? says for its second source. Besides the section's own provenance, the only
        /// line that names a product.
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
        /// <param name="provenanceRuns">The provenance as runs, a source's version leaning; Halcyonic's words alone when not given.</param>
        public SectionPresentation(SectionKind kind, string provenance, SectionTone provenanceTone, IReadOnlyList<SectionLine> lines, bool simulated,
            bool steps = false, LineWords? provenanceRuns = null)
        {
            Kind = kind;
            Provenance = provenance;
            ProvenanceRuns = provenanceRuns ?? LineWords.Written(provenance);
            ProvenanceTone = provenanceTone;
            // A source's sentence left blank, as its contract allows, is absent: a line of no words says nothing
            // and can't be drawn.
            Lines = lines.Where(line => !string.IsNullOrWhiteSpace(line.Words) && !string.IsNullOrWhiteSpace(line.Text)).ToList();
            Simulated = simulated;
            Steps = steps;
        }

        public SectionKind Kind { get; }

        /// <summary>
        /// Where the answer comes from and when, or why there is none. Besides an about line and a
        /// <see cref="SectionLine.Source"/> line, the only words that name the product, Salidium or
        /// Seorak, and it says "Simulated" for a stand-in's.
        /// </summary>
        public string Provenance { get; }

        /// <summary><see cref="Provenance"/> as runs: "From Salidium " Halcyonic's, the version the source names for itself leaning.</summary>
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
            if (read == null)
            {
                return new[]
                {
                    loading ? Source("Reading Measurement", SectionTone.Secondary)
                    : error != null ? Source("Measurement Unreadable · " + error, SectionTone.Problem)
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
                        Source(IntelligenceText.EvaluationProvenance(read, synthetic, now, zone) + status, SectionTone.Secondary),
                    };
                    lines.AddRange(Lines(evaluation, at, zone));
                    return lines;
                case NotFoundEvaluation notFound:
                    return new[]
                    {
                        Source(IntelligenceText.From(IntelligenceText.FromSeorak, notFound.Reason) + "Not Measured · " + IntelligenceText.Why(notFound.Reason) + status,
                            SectionTone.Secondary),
                    };
                case UnavailableEvaluation unavailable:
                    return new[] { Source(IntelligenceText.FromSeorak + " · Measurement Unavailable · " + IntelligenceText.Why(unavailable.Reason) + status, SectionTone.Secondary) };
                case IncompatibleEvaluation incompatible:
                    return new[] { Source(IntelligenceText.FromSeorak + " · Measurement Unreadable · " + IntelligenceText.Why(incompatible.Reason) + status, SectionTone.Problem) };
                case UnauthorizedEvaluation unauthorized:
                    return new[] { Source(IntelligenceText.FromSeorak + " · Measurement Not Allowed · " + IntelligenceText.Why(unauthorized.Reason) + status, SectionTone.Secondary) };
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
        public static string Why(ErrorInfo reason) => reason.Code switch
        {
            "not_running" => "Not Running · Start It · Refresh",
            "unreachable" or "timed_out" => "No Answer · Refresh Soon",
            "temporarily_unavailable" or "server_error" => "Busy · Refresh Soon",
            "rate_limited" => "Too Many Requests · Refresh Later",
            "credential_missing" => "Credential Missing · Set Up on Computer",
            // Neither reader sends a credential that isn't one of its kind (their tests: no request with it).
            "credential_malformed" => "Credential Wrong Kind · Not Sent · Set Up Another",
            "credential_rejected" => "Credential Refused · Set Up New One",
            "credential_forbidden" or "insufficient_scope" => "Credential Lacks Access · Set Up Another",
            "outside_credential_restriction" => "Credential Covers Other Work · Set Up Another",
            "not_captured" or "not_observed" => "Task Not Seen Yet · Refresh Soon",
            "not_yet_computed" => "Not Computed Yet · Refresh Soon",
            "not_retained" => "Task No Longer Kept",
            "runtime_not_observed" => "Agent App Not Followed",
            "not_resolvable" or "not_observable" => "Task Not Found by Name",
            "result_limit" => "Result Limit Reached · Refresh",
            "invalid_document" or "unexpected_status" or "unsupported_contract" or "answer_too_large" or "invalid_understanding" or "invalid_evaluation"
                => "Answer Unreadable · Check Version on Computer",
            "host_not_allowed" or "origin_not_allowed" => "Request Turned Away · Check Setup on Computer",
            // Salidium's reader asks the port who it is before any credential goes, but that question is sent.
            "instance_mismatch" => "Wrong Source Answered · Check Setup on Computer",
            "discovery_unreadable" => "Setup Unreadable · Check on Computer",
            NotAsked => "Session Unknown Yet · Refresh Soon",
            _ => "No Reason Given · Refresh",
        };

        /// <summary>
        /// Why this headset's own read of a section failed, after "Measurement Unreadable · " or
        /// " · Reread Failed · ", by what failed and never the error's message, which is the control
        /// plane's or the runtime's and can hold an address: one line per cause, as the folders say it, a
        /// refused credential as <paramref name="accessRefused"/> says it for how this headset reaches the
        /// computer (settled by the coordinator, 2026-10-07).
        /// </summary>
        public static string WhyUnread(Exception? error, string accessRefused) => error switch
        {
            OperationCanceledException => "Computer Timed Out · Refresh",
            _ when Within<UnaskedReadException>(error) is UnaskedReadException unasked => unasked.Message,
            _ when Within<CertificateMismatchException>(error) != null => ConnectionText.NotThePairedComputer,
            _ when Within<TokenNotSentException>(error) is TokenNotSentException notSent && notSent.Outcome == LoopbackProofOutcome.Unproved
                => "Host Unverified · Code Not Sent · Check App · Refresh",
            _ when Within<TokenNotSentException>(error) is TokenNotSentException notSent && notSent.Outcome == LoopbackProofOutcome.Unreachable => DidntAnswer,
            ControlPlaneRequestException { Code: "device_revoked", Status: 401 } => ConnectionText.PairingRefused,
            ControlPlaneRequestException { Code: "unauthorized", Status: 401 } => accessRefused,
            ControlPlaneRequestException { Code: "too_many_requests", Status: 429 }
                => "Pairing Paused · Too Many Tries · Refresh in 1 min",
            _ when Within<Newtonsoft.Json.JsonException>(error) != null => ConnectionText.Unreadable,
            // Something answered, with what isn't HTTP or too much of it, or took the connection and set up no secure
            // one: never "didn't answer".
            _ when Within<System.IO.InvalidDataException>(error) != null || Within<FormatException>(error) != null || Within<OverflowException>(error) != null
                || Within<HandshakeFailedException>(error) != null => SomethingWentWrong,
            ControlPlaneRequestException { Code: null, InnerException: System.Net.Http.HttpRequestException } => DidntAnswer,
            _ => SomethingWentWrong,
        };

        private const string DidntAnswer = "No Answer · Refresh";
        private const string SomethingWentWrong = "Read Failed · Refresh";

        /// <summary>The headset's read when there is nothing to ask (settled by the coordinator, 2026-10-07).</summary>
        public const string NotConnected = "Not Connected · Refresh Once Connected";

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
        /// Where an understanding came from and when: "Salidium 0.6.1 · 2 min ago", or a
        /// stand-in's, said to be simulated without naming a product: an explanation, or, above the
        /// check runs it saw, checks.
        /// </summary>
        public static string UnderstandingProvenance(IntelligenceRead<UnderstandingResponse> read, UnderstandingSource source, DateTimeOffset now, TimeZoneInfo zone,
            bool checks = false) => UnderstandingProvenanceRuns(read, source, now, zone, checks).Text;

        /// <summary>Where an answer came from as runs: Halcyonic's words upright, the version Salidium names for itself leaning (settled 2026-10-08).</summary>
        public static LineWords UnderstandingProvenanceRuns(IntelligenceRead<UnderstandingResponse> read, UnderstandingSource source, DateTimeOffset now,
            TimeZoneInfo zone, bool checks = false)
        {
            var when = read.Recorded ? "Recorded " + Clock(read.ReadAt, zone, seconds: true) : Ago(source.GeneratedAt, now, zone);
            if (source.Synthetic) return LineWords.Written((checks ? "Simulated Checks · " : "Simulated Explanation · ") + when);
            return LineWords.Written(FromSalidium + " ") + LineWords.TheirName(Truncate(Plain(source.Version), 24)) + LineWords.Written(" · " + when);
        }

        /// <summary>Where a measurement came from and when: "Seorak · Read 1 min ago", or a stand-in's, said to be simulated.</summary>
        public static string EvaluationProvenance(IntelligenceRead<EvaluationResponse> read, bool synthetic, DateTimeOffset now, TimeZoneInfo zone)
        {
            var when = read.Recorded ? "Recorded " + Clock(read.ReadAt, zone, seconds: true) : "Read " + Ago(read.ReadAt, now, zone);
            return synthetic ? "Simulated Measurement · " + when : FromSeorak + " · " + when;
        }

        internal static string ReadingStatus(bool loading, string? error) =>
            loading ? " · Reading Again" : error != null ? " · Reread Failed · " + error : "";

        internal static SectionPresentation Empty(SectionKind kind, string text) =>
            new SectionPresentation(kind, text, SectionTone.Secondary, Array.Empty<SectionLine>(), simulated: false);

        /// <summary>The section while the understanding it shows has not been read.</summary>
        internal static SectionPresentation Waiting(SectionKind kind, bool loading, string? error)
        {
            if (loading) return Empty(kind, "Reading Understanding");
            if (error != null)
            {
                return new SectionPresentation(kind, "Understanding Unreadable · " + error, SectionTone.Problem, Array.Empty<SectionLine>(), simulated: false);
            }
            return Empty(kind, "Not Read");
        }

        /// <summary>
        /// A section whose source answered without conclusions: its provenance line names the source,
        /// as it does for an answer, then says why there is none, in words.
        /// </summary>
        internal static SectionPresentation Failure(SectionKind kind, string source, string lead, ErrorInfo reason, SectionTone tone, string status) =>
            new SectionPresentation(kind, From(source, reason) + lead + Why(reason) + status, tone, Array.Empty<SectionLine>(), simulated: false);

        /// <summary>
        /// The control plane's own answer before it asks any source: the runtime has not said which
        /// session it is, so no source was asked and none is named.
        /// </summary>
        public const string NotAsked = "native_id_unknown";

        /// <summary>"Salidium · " before why there is no answer, unless no source was asked.</summary>
        internal static string From(string source, ErrorInfo reason) => reason.Code == NotAsked ? "" : source + " · ";

        /// <summary>The source an understanding comes from, as a provenance line names it.</summary>
        public const string FromSalidium = "Salidium";

        /// <summary>The source a measurement comes from, as a provenance line names it.</summary>
        public const string FromSeorak = "Seorak";
    }
}

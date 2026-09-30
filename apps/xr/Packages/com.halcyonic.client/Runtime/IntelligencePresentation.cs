#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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

    public enum SectionKind
    {
        Understanding,
        Evaluation,
    }

    /// <summary>One line of a section: a claim with its epistemic class, or a measurement with its part.</summary>
    public sealed class SectionLine
    {
        public SectionLine(string tag, string text, SectionTone tone, bool detail = false)
        {
            Tag = tag;
            Text = text;
            Tone = tone;
            Detail = detail;
        }

        /// <summary>
        /// The claim's epistemic class in the source's own word ("observed", "reported", "inferred",
        /// "planned", "explained"), the part a measurement belongs to ("Cost"), or empty.
        /// </summary>
        public string Tag { get; }

        /// <summary>Plain text: the source's words cleaned of control characters, or the app's own. Never markup.</summary>
        public string Text { get; }

        public SectionTone Tone { get; }

        /// <summary>A line about the line above it, such as a part's availability, coverage and freshness, drawn smaller.</summary>
        public bool Detail { get; }
    }

    /// <summary>What one section of the workspace shows, in words, so the XR layer only lays it out.</summary>
    public sealed class SectionPresentation
    {
        public SectionPresentation(SectionKind kind, string provenance, SectionTone provenanceTone, IReadOnlyList<SectionLine> lines, bool simulated)
        {
            Kind = kind;
            Provenance = provenance;
            ProvenanceTone = provenanceTone;
            Lines = lines;
            Simulated = simulated;
        }

        public SectionKind Kind { get; }

        /// <summary>The section's name: the capability, never the product that provides it.</summary>
        public string Title => IntelligenceText.TitleOf(Kind);

        /// <summary>
        /// Where the answer comes from and when, or why there is none. Besides an about line, the only
        /// words that name the product, Salidium or Seorak, and it says "Simulated" for a stand-in's.
        /// </summary>
        public string Provenance { get; }

        public SectionTone ProvenanceTone { get; }

        public IReadOnlyList<SectionLine> Lines { get; }

        /// <summary>The answer came from a stand-in for the source, as in the recorded demonstration.</summary>
        public bool Simulated { get; }
    }

    /// <summary>
    /// Turns what the understanding source concluded about an execution into the Understanding
    /// section. Every claim keeps the source's epistemic class, agent text stays a quote, a value the
    /// source does not have is said to be unknown, and every text from the source is plain text.
    /// </summary>
    public static class UnderstandingPresenter
    {
        public static SectionPresentation Present(IntelligenceFeed<UnderstandingResponse> feed, DateTimeOffset now, TimeZoneInfo zone, int maxLines) =>
            Present(feed.ExecutionId, feed.Last, feed.Loading, feed.Error, now, zone, maxLines);

        /// <param name="now">This device's time; a recorded answer is described as of when it was recorded.</param>
        /// <param name="maxLines">How many lines below the provenance fit; the most important are kept.</param>
        public static SectionPresentation Present(
            string? executionId,
            IntelligenceRead<UnderstandingResponse>? read,
            bool loading,
            string? error,
            DateTimeOffset now,
            TimeZoneInfo zone,
            int maxLines)
        {
            if (executionId == null) return IntelligenceText.Empty(SectionKind.Understanding, "Nothing to understand until work starts.");
            if (read == null) return IntelligenceText.Waiting(SectionKind.Understanding, loading, error);
            var status = IntelligenceText.ReadingStatus(loading, error);
            switch (read.Response.Result)
            {
                case AvailableUnderstanding available:
                    var understanding = available.Understanding;
                    var source = understanding.Source;
                    var when = read.Recorded
                        ? "recorded at " + IntelligenceText.Clock(read.ReadAt, zone, seconds: true)
                        : IntelligenceText.Ago(source.GeneratedAt, now, zone);
                    var provenance = source.Synthetic
                        ? "Simulated, not from Salidium · " + when
                        : "From Salidium " + IntelligenceText.Truncate(IntelligenceText.Plain(source.Version), 24) + ", " + when;
                    return new SectionPresentation(SectionKind.Understanding, provenance + status, source.Synthetic ? SectionTone.Attention : SectionTone.Secondary,
                        Lines(understanding, maxLines), source.Synthetic);
                case NotFoundUnderstanding notFound:
                    return IntelligenceText.Failure(SectionKind.Understanding, "No understanding yet: ", notFound.Reason, SectionTone.Secondary, status);
                case UnavailableUnderstanding unavailable:
                    return IntelligenceText.Failure(SectionKind.Understanding, "Understanding unavailable: ", unavailable.Reason, SectionTone.Secondary, status);
                case IncompatibleUnderstanding incompatible:
                    return IntelligenceText.Failure(SectionKind.Understanding, "Understanding unreadable: ", incompatible.Reason, SectionTone.Problem, status);
                case UnauthorizedUnderstanding unauthorized:
                    return IntelligenceText.Failure(SectionKind.Understanding, "Understanding not allowed: ", unauthorized.Reason, SectionTone.Attention, status);
                default:
                    return IntelligenceText.Empty(SectionKind.Understanding, "The understanding came back in a form this app does not know.");
            }
        }

        /// <summary>The lines, the most important first when they do not all fit, shown in reading order.</summary>
        private static IReadOnlyList<SectionLine> Lines(Understanding understanding, int maxLines)
        {
            // (reading order, importance, line): importance 0 is kept first.
            var candidates = new List<(int Order, int Importance, SectionLine Line)>();
            void Add(int order, int importance, SectionLine line) => candidates.Add((order, importance, line));

            var verdict = understanding.Verdict;
            var because = IntelligenceText.Plain(verdict.Because);
            Add(0, 0, new SectionLine(Word(verdict.Epistemic), Sentence(IntelligenceText.Plain(verdict.Headline), because), ToneOf(verdict.Tone)));

            var waiting = understanding.Waiting;
            if (waiting != null && IntelligenceText.Plain(waiting.Summary) != because)
            {
                Add(1, 1, new SectionLine(Word(waiting.Epistemic), WaitingFor(waiting.Kind) + IntelligenceText.Plain(waiting.Summary), SectionTone.Attention));
            }

            var statement = understanding.LatestStatement;
            if (statement != null) Add(2, 2, new SectionLine(Word(statement.Epistemic), Quote(statement), SectionTone.Claim));

            var changes = understanding.Changes;
            // The source observes paths, counts and kinds.
            Add(3, 4, new SectionLine("observed", IntelligenceText.Plain(changes.Summary), SectionTone.Normal));
            if (changes.Files.Count > 0)
            {
                var unverified = understanding.Verification.UnverifiedFiles;
                var by = changes.Files.Select(file => file.Coverage.By).FirstOrDefault(value => value != null);
                var coverage = unverified.Count == 0
                    ? "Every changed file was checked after its last change" + (by == null ? "" : ", by " + IntelligenceText.Plain(by))
                    : IntelligenceText.Plural(unverified.Count, "file") + " not checked after the last change: "
                        + string.Join(", ", unverified.Select(IntelligenceText.FileName));
                // Coverage is always the source's inference.
                Add(4, 5, new SectionLine("inferred", coverage, unverified.Count == 0 ? SectionTone.Normal : SectionTone.Attention));
            }

            var runs = understanding.Verification.LatestByMethod;
            if (runs.Count == 0)
            {
                Add(5, 3, new SectionLine("", IntelligenceText.Plain(understanding.Verification.Summary), SectionTone.Secondary));
            }
            for (var index = 0; index < runs.Count && index < 2; index++)
            {
                Add(5, index == 0 ? 3 : 9, Run(runs[index]));
            }

            var review = understanding.Review;
            if (review.Open > 0 && review.Groups.Count > 0)
            {
                var labels = string.Join("; ", review.Groups.Take(2).Select(group => IntelligenceText.Plain(group.Label)));
                var first = review.Groups[0].Items.Count > 0 ? Word(review.Groups[0].Items[0].Epistemic) : "";
                Add(6, 6, new SectionLine(first, IntelligenceText.Plain(review.Summary) + ": " + labels, SectionTone.Attention));
            }

            var explanation = Explanation(understanding.Explanation);
            if (explanation != null) Add(7, 7, explanation);

            // A failing check the source also lists as remaining is the run already shown.
            var shownRuns = new HashSet<string>(runs.Select(run => IntelligenceText.Plain(run.Label)));
            var remaining = understanding.Remaining.Items
                .Where(item => item.Source != UnderstandingRemainingItemSource.Verification || !shownRuns.Contains(IntelligenceText.Plain(item.Text)))
                .ToList();
            for (var index = 0; index < remaining.Count && index < 2; index++)
            {
                Add(8, 8 + index, Remaining(remaining[index]));
            }

            return candidates
                .Select((candidate, index) => (candidate, index))
                .OrderBy(entry => entry.candidate.Importance).ThenBy(entry => entry.index)
                .Take(Math.Max(0, maxLines))
                .OrderBy(entry => entry.candidate.Order).ThenBy(entry => entry.index)
                .Select(entry => entry.candidate.Line)
                .ToList();
        }

        private static SectionLine Run(UnderstandingVerificationRun run)
        {
            var method = run.Method switch
            {
                UnderstandingVerificationRunMethod.Test => "Tests",
                UnderstandingVerificationRunMethod.Typecheck => "Type check",
                UnderstandingVerificationRunMethod.Lint => "Lint",
                UnderstandingVerificationRunMethod.Build => "Build",
                _ => "Check",
            };
            var (outcome, tone) = run.Outcome switch
            {
                UnderstandingVerificationRunOutcome.Pass => ("passed", SectionTone.Good),
                UnderstandingVerificationRunOutcome.Fail => ("failed", SectionTone.Problem),
                UnderstandingVerificationRunOutcome.Partial => ("partly passed", SectionTone.Attention),
                _ => ("outcome unknown", SectionTone.Secondary),
            };
            var text = method + " " + outcome + ": " + IntelligenceText.Plain(run.Label);
            if (run.Stale) text += "; files changed since";
            if (run.LaterUnreadable > 0) text += "; " + IntelligenceText.Plural(run.LaterUnreadable, "later run") + " unreadable";
            return new SectionLine(Word(run.Epistemic), text, tone);
        }

        private static SectionLine Remaining(UnderstandingRemainingItem item)
        {
            var text = IntelligenceText.Plain(item.Text);
            return item.Status switch
            {
                UnderstandingRemainingItemStatus.Reported => new SectionLine(Word(item.Epistemic), "Agent reports: “" + text + "”", SectionTone.Claim),
                UnderstandingRemainingItemStatus.Failing => new SectionLine(Word(item.Epistemic), "Failing: " + text, SectionTone.Problem),
                UnderstandingRemainingItemStatus.InProgress => new SectionLine(Word(item.Epistemic), "In progress: " + text, SectionTone.Normal),
                _ => new SectionLine(Word(item.Epistemic),
                    (item.Source == UnderstandingRemainingItemSource.Plan ? "Planned, not done: " : "To do: ") + text, SectionTone.Normal),
            };
        }

        /// <summary>A model's explanation, a claim weaker than what the agent reported, and never evidence.</summary>
        private static SectionLine? Explanation(UnderstandingExplanation explanation)
        {
            switch (explanation.Status)
            {
                case UnderstandingExplanationStatus.Generated when explanation.Content != null:
                    var content = explanation.Content;
                    var text = "Explanation: " + IntelligenceText.Plain(content.What.Currently ?? content.How.Summary);
                    if (!explanation.Current) text += " (from before the latest evidence)";
                    return new SectionLine("explained", text, SectionTone.Claim);
                case UnderstandingExplanationStatus.Generating:
                    return new SectionLine("explained", "An explanation is being written.", SectionTone.Secondary);
                case UnderstandingExplanationStatus.Unavailable:
                    return new SectionLine("explained", "No explanation can be written now.", SectionTone.Secondary);
                case UnderstandingExplanationStatus.Failed:
                    return new SectionLine("explained", "The explanation could not be written.", SectionTone.Secondary);
                default:
                    return null;
            }
        }

        private static string Quote(UnderstandingStatement statement)
        {
            var who = statement.Author switch
            {
                UnderstandingStatementAuthor.Agent => "Agent says",
                UnderstandingStatementAuthor.Subagent => "Subagent says",
                _ => "Quoted, author unknown",
            };
            return who + ": “" + IntelligenceText.Plain(statement.Text) + "”";
        }

        private static string WaitingFor(UnderstandingWaitingKind kind) => kind switch
        {
            UnderstandingWaitingKind.Permission => "Waiting for permission: ",
            UnderstandingWaitingKind.Question => "Waiting for an answer: ",
            _ => "Waiting for input: ",
        };

        private static string Sentence(string headline, string because)
        {
            if (because.Length == 0) return headline;
            var end = headline.Length > 0 ? headline[headline.Length - 1] : '.';
            return headline + (end == '.' || end == ':' || end == '!' || end == '?' ? " " : ". ") + because;
        }

        private static SectionTone ToneOf(UnderstandingVerdictTone tone) => tone switch
        {
            UnderstandingVerdictTone.Pass => SectionTone.Good,
            UnderstandingVerdictTone.Fail => SectionTone.Problem,
            UnderstandingVerdictTone.Attention => SectionTone.Attention,
            _ => SectionTone.Normal,
        };

        /// <summary>The source's own word for how it knows a claim.</summary>
        public static string Word(UnderstandingEpistemic epistemic) => epistemic switch
        {
            UnderstandingEpistemic.Observed => "observed",
            UnderstandingEpistemic.Reported => "reported",
            UnderstandingEpistemic.Inferred => "inferred",
            UnderstandingEpistemic.Planned => "planned",
            _ => "explained",
        };
    }

    /// <summary>
    /// Turns what the evaluation source measured about an execution into the Evaluation section. Each
    /// part, the cost, the outcome and the checks, keeps its own availability, coverage and freshness,
    /// never combined; the cost is always the source's estimate from tokens; nothing is a score; and a
    /// value the source does not have is said to be unknown or pending, never zero.
    /// </summary>
    public static class EvaluationPresenter
    {
        public static SectionPresentation Present(IntelligenceFeed<EvaluationResponse> feed, DateTimeOffset now, TimeZoneInfo zone) =>
            Present(feed.ExecutionId, feed.Last, feed.Loading, feed.Error, now, zone);

        /// <param name="now">This device's time; a recorded answer is described as of when it was recorded.</param>
        public static SectionPresentation Present(
            string? executionId,
            IntelligenceRead<EvaluationResponse>? read,
            bool loading,
            string? error,
            DateTimeOffset now,
            TimeZoneInfo zone)
        {
            if (executionId == null) return IntelligenceText.Empty(SectionKind.Evaluation, "Nothing to evaluate until work starts.");
            if (read == null) return IntelligenceText.Waiting(SectionKind.Evaluation, loading, error);
            var status = IntelligenceText.ReadingStatus(loading, error);
            switch (read.Response.Result)
            {
                case AvailableEvaluation available:
                    var evaluation = available.Evaluation;
                    var synthetic = evaluation.Source.Synthetic;
                    var at = read.Recorded ? read.ReadAt : now;
                    var when = read.Recorded
                        ? "recorded at " + IntelligenceText.Clock(read.ReadAt, zone, seconds: true)
                        : "read " + IntelligenceText.Ago(read.ReadAt, now, zone);
                    var provenance = synthetic ? "Simulated, not from Seorak · " + when : "From Seorak, " + when;
                    return new SectionPresentation(SectionKind.Evaluation, provenance + status, synthetic ? SectionTone.Attention : SectionTone.Secondary,
                        Lines(evaluation, at, zone), synthetic);
                case NotFoundEvaluation notFound:
                    return IntelligenceText.Failure(SectionKind.Evaluation, "No evaluation yet: ", notFound.Reason, SectionTone.Secondary, status);
                case UnavailableEvaluation unavailable:
                    return IntelligenceText.Failure(SectionKind.Evaluation, "Evaluation unavailable: ", unavailable.Reason, SectionTone.Secondary, status);
                case IncompatibleEvaluation incompatible:
                    return IntelligenceText.Failure(SectionKind.Evaluation, "Evaluation unreadable: ", incompatible.Reason, SectionTone.Problem, status);
                case UnauthorizedEvaluation unauthorized:
                    return IntelligenceText.Failure(SectionKind.Evaluation, "Evaluation not allowed: ", unauthorized.Reason, SectionTone.Attention, status);
                default:
                    return IntelligenceText.Empty(SectionKind.Evaluation, "The evaluation came back in a form this app does not know.");
            }
        }

        private static IReadOnlyList<SectionLine> Lines(Evaluation evaluation, DateTimeOffset now, TimeZoneInfo zone)
        {
            var lines = new List<SectionLine>();
            var cost = evaluation.Cost;
            lines.Add(cost.EstimatedUsd is double usd
                ? new SectionLine("Cost", "About " + Money(usd) + ". " + IntelligenceText.Plain(cost.Note), SectionTone.Normal)
                : new SectionLine("Cost", "Unknown: unpriced, or not measured yet.", SectionTone.Secondary));
            lines.Add(Status(cost.Availability, cost.Coverage, cost.Freshness, now, zone));

            var outcome = evaluation.Outcome;
            var measure = outcome.Measure;
            if (measure == null)
            {
                lines.Add(new SectionLine("Outcome", "Nothing measured yet.", SectionTone.Secondary));
            }
            else
            {
                lines.Add(new SectionLine("Outcome", Commits(measure.CommitsLanded) + " · " + Errors(measure, zone) + " · " + End(measure.EndReason),
                    measure.ErrorCount > 0 ? SectionTone.Attention : SectionTone.Normal));
                lines.Add(new SectionLine("", Uncommitted(measure.Uncommitted) + " · " + Survival(measure.LineSurvival), SectionTone.Normal));
            }
            lines.Add(Status(outcome.Availability, outcome.Coverage, outcome.Freshness, now, zone));

            var verification = evaluation.Verification;
            var lens = verification.Lens;
            if (lens == null)
            {
                lines.Add(new SectionLine("Checks", "No verification lens.", SectionTone.Secondary));
            }
            else if (lens.ByKind.Count == 0)
            {
                lines.Add(new SectionLine("Checks", IntelligenceText.Plain(lens.EmptyReason ?? "Nothing measured."), SectionTone.Secondary));
            }
            else
            {
                lines.Add(new SectionLine("Checks", string.Join("; ", lens.ByKind.Select(Kind)), SectionTone.Normal));
            }
            lines.Add(Status(verification.Availability, verification.Coverage, verification.Freshness, now, zone));
            return lines;
        }

        /// <summary>One part's own statement about itself: whether it is available, what it covers, and how current it is.</summary>
        public static SectionLine Status(EvaluationAvailability availability, EvaluationCoverage coverage, EvaluationFreshness freshness, DateTimeOffset now, TimeZoneInfo zone)
        {
            var tone = SectionTone.Secondary;
            string state;
            switch (availability.State)
            {
                case EvaluationAvailabilityState.Available:
                    state = "available";
                    break;
                case EvaluationAvailabilityState.Partial:
                    state = "partly available" + (availability.Reason is EvaluationAvailabilityReason partly ? " (" + Reason(partly) + ")" : "");
                    tone = SectionTone.Attention;
                    break;
                default:
                    state = "unavailable" + (availability.Reason is EvaluationAvailabilityReason reason ? ": " + Reason(reason) : "");
                    tone = SectionTone.Attention;
                    break;
            }
            var covers = "covers " + coverage.IncludedSessions.ToString(CultureInfo.InvariantCulture) + " of "
                + IntelligenceText.Plural(coverage.MatchedSessions, "session");
            if (!coverage.Complete)
            {
                covers += ", incomplete" + (coverage.Omissions.Count == 0 ? "" : ": " + string.Join(", ", coverage.Omissions.Select(Omission)));
                tone = SectionTone.Attention;
            }
            string current;
            if (IntelligenceText.TryParse(freshness.StaleAt, out var staleAt) && now >= staleAt)
            {
                // After this instant the part must read as stale, whatever its state said when read.
                current = "stale since " + IntelligenceText.Clock(staleAt, zone, seconds: false);
                tone = SectionTone.Attention;
            }
            else
            {
                current = freshness.State switch
                {
                    EvaluationFreshnessState.Fresh => "fresh",
                    EvaluationFreshnessState.Stale => "stale",
                    _ => "being recomputed",
                };
                current += freshness.DataThrough != null && IntelligenceText.TryParse(freshness.DataThrough, out var through)
                    ? ", data through " + IntelligenceText.Clock(through, zone, seconds: true)
                    : ", no data yet";
                if (freshness.State == EvaluationFreshnessState.Stale) tone = SectionTone.Attention;
            }
            return new SectionLine("", state + " · " + covers + " · " + current, tone, detail: true);
        }

        private static string Kind(EvaluationVerificationKind kind)
        {
            var label = IntelligenceText.Plain(kind.Label);
            var runs = kind.Runs is long count ? IntelligenceText.Plural(count, "run") : "runs unknown";
            var passed = kind.Passed is long passes ? passes.ToString(CultureInfo.InvariantCulture) + " passed" : "passes unknown";
            var rate = kind.PassRate is double fraction ? (fraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "no pass rate";
            return label + ": " + passed + " of " + runs + " (" + rate + ")";
        }

        private static string Commits(long? landed) => landed switch
        {
            null => "commits landed unknown",
            0 => "no commits landed",
            _ => IntelligenceText.Plural(landed.Value, "commit") + " landed",
        };

        private static string Errors(EvaluationOutcomeMeasure measure, TimeZoneInfo zone)
        {
            if (!(measure.ErrorCount is long count)) return "tool errors unknown";
            if (count == 0) return "no tool errors";
            var first = measure.FirstErrorAt != null && IntelligenceText.TryParse(measure.FirstErrorAt, out var at)
                ? ", the first at " + IntelligenceText.Clock(at, zone, seconds: false)
                : "";
            return IntelligenceText.Plural(count, "tool error") + first;
        }

        private static string End(EvaluationEndReason? reason) => reason switch
        {
            null => "not ended, or its end not captured",
            EvaluationEndReason.Clear => "ended: cleared",
            EvaluationEndReason.Resume => "ended: resumed elsewhere",
            EvaluationEndReason.Logout => "ended: logged out",
            EvaluationEndReason.PromptInputExit => "ended: the person exited",
            EvaluationEndReason.BypassPermissionsDisabled => "ended: permission bypass turned off",
            EvaluationEndReason.Other => "ended for another reason",
            _ => "ended for a reason not named",
        };

        private static string Uncommitted(EvaluationUncommitted? uncommitted)
        {
            if (uncommitted == null) return "uncommitted changes: measured when it ends";
            var text = "uncommitted: " + IntelligenceText.Plural(uncommitted.FilesTouched, "file") + ", +"
                + uncommitted.LinesAdded.ToString(CultureInfo.InvariantCulture) + " −" + uncommitted.LinesRemoved.ToString(CultureInfo.InvariantCulture);
            if (uncommitted.GeneratedLinesExcluded > 0)
            {
                text += " (" + IntelligenceText.Plural(uncommitted.GeneratedLinesExcluded, "generated line") + " apart)";
            }
            return text;
        }

        private static string Survival(EvaluationLineSurvival? survival)
        {
            if (survival == null) return "lines kept after 3 days: pending";
            var rate = survival.Rate is double fraction ? (fraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "rate unknown";
            var lines = " (" + survival.LinesSurviving.ToString(CultureInfo.InvariantCulture) + " of "
                + IntelligenceText.Plural(survival.LinesAuthored, "line") + ")";
            return survival.Fate switch
            {
                EvaluationLineSurvivalFate.Retained => "lines kept after 3 days: " + rate + lines,
                EvaluationLineSurvivalFate.Overwritten => "lines overwritten within 3 days" + lines,
                EvaluationLineSurvivalFate.Unreachable => "lines no longer reachable after 3 days" + lines,
                _ => "lines after 3 days: unknown",
            };
        }

        private static string Reason(EvaluationAvailabilityReason reason) => reason switch
        {
            EvaluationAvailabilityReason.NotCaptured => "not captured",
            EvaluationAvailabilityReason.NotRetained => "no longer kept",
            EvaluationAvailabilityReason.NotYetComputed => "not yet computed",
            EvaluationAvailabilityReason.OutsideCredentialRestriction => "outside what the credential may read",
            EvaluationAvailabilityReason.TemporarilyUnavailable => "temporarily unavailable",
            EvaluationAvailabilityReason.ResultLimit => "a result limit was reached",
            _ => "for a reason not named",
        };

        private static string Omission(EvaluationCoverageOmission omission) => omission switch
        {
            EvaluationCoverageOmission.OutsideRetention => "outside retention",
            EvaluationCoverageOmission.CaptureUnavailable => "capture unavailable",
            EvaluationCoverageOmission.ProjectionPending => "still being computed",
            EvaluationCoverageOmission.CredentialRestriction => "the credential's restriction",
            EvaluationCoverageOmission.ResultLimit => "a result limit",
            _ => "a gap not named",
        };

        private static string Money(double usd) =>
            usd < 0.005 ? "less than $0.01" : "$" + usd.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>The words and text handling both sections share.</summary>
    public static class IntelligenceText
    {
        /// <summary>The section names: the capabilities, never the products that provide them.</summary>
        public const string UnderstandingTitle = "Understanding";

        public const string EvaluationTitle = "Evaluation";

        public static string TitleOf(SectionKind kind) => kind == SectionKind.Understanding ? UnderstandingTitle : EvaluationTitle;

        /// <summary>
        /// Text from a source, made plain: control and format characters (such as bidirectional
        /// overrides and zero-width characters) removed, and line breaks and runs of whitespace
        /// collapsed into single spaces, so it is one line of exactly what it says. Markup characters
        /// stay as they are; the labels that show them never interpret markup.
        /// </summary>
        public static string Plain(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var result = new StringBuilder(text!.Length);
            var space = false;
            foreach (var character in text)
            {
                var category = char.GetUnicodeCategory(character);
                if (char.IsWhiteSpace(character) || category == UnicodeCategory.LineSeparator || category == UnicodeCategory.ParagraphSeparator)
                {
                    space = result.Length > 0;
                    continue;
                }
                if (category == UnicodeCategory.Control || category == UnicodeCategory.Format) continue;
                if (space) result.Append(' ');
                space = false;
                result.Append(character);
            }
            return result.ToString();
        }

        /// <summary>
        /// Text as a TextMeshPro label with its escape parsing on shows it, character for character.
        /// Even with rich text off, TextMeshPro turns a backslash sequence such as \n, \t, A or
        /// \U0001F600 in a label's text into another character; a doubled backslash shows one, so every
        /// backslash is doubled. Use it only on text a label shows, with rich text off.
        /// </summary>
        public static string ForTextMeshPro(string text) => text.Replace("\\", "\\\\");

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
            TryParse(timestamp, out var at) ? Ago(at, now, zone) : "at a time it did not say";

        public static string Ago(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
        {
            var elapsed = now - at;
            if (elapsed < TimeSpan.FromMinutes(1)) return "just now";
            if (elapsed < TimeSpan.FromHours(1)) return Plural((long)elapsed.TotalMinutes, "minute") + " ago";
            if (elapsed < TimeSpan.FromDays(1)) return Plural((long)elapsed.TotalHours, "hour") + " ago";
            return "on " + TimeZoneInfo.ConvertTime(at, zone).ToString("d MMM 'at' HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>A clock time in the person's zone.</summary>
        public static string Clock(DateTimeOffset at, TimeZoneInfo zone, bool seconds) =>
            TimeZoneInfo.ConvertTime(at, zone).ToString(seconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);

        public static bool TryParse(string timestamp, out DateTimeOffset at) =>
            DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out at);

        internal static string ReadingStatus(bool loading, string? error) =>
            loading ? " · reading again…" : error != null ? " · could not read it again: " + Plain(error) : "";

        internal static SectionPresentation Empty(SectionKind kind, string text) =>
            new SectionPresentation(kind, text, SectionTone.Secondary, Array.Empty<SectionLine>(), simulated: false);

        internal static SectionPresentation Waiting(SectionKind kind, bool loading, string? error)
        {
            if (loading) return Empty(kind, kind == SectionKind.Understanding ? "Asking what the understanding source concluded…" : "Asking what the evaluation source measured…");
            if (error != null)
            {
                return new SectionPresentation(kind, "Could not read the " + (kind == SectionKind.Understanding ? "understanding" : "evaluation") + ": " + Plain(error),
                    SectionTone.Problem, Array.Empty<SectionLine>(), simulated: false);
            }
            return Empty(kind, "Not read yet.");
        }

        internal static SectionPresentation Failure(SectionKind kind, string lead, ErrorInfo reason, SectionTone tone, string status) =>
            new SectionPresentation(kind, lead + Plain(reason.Message) + status, tone, Array.Empty<SectionLine>(), simulated: false);
    }
}

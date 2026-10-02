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

    /// <summary>One line of a section: a claim with its epistemic class, or a measurement with its part.</summary>
    public sealed class SectionLine
    {
        /// <param name="rows">The most rows it may wrap to; a detail takes two unless told otherwise, any other line one.</param>
        /// <param name="source">Where the lines under it come from, as a second source's provenance line.</param>
        /// <param name="startsPage">It begins a step of a flow, so a page starts with it.</param>
        /// <param name="repeats">It heads its step, so a step that takes more than a page shows it again on the next.</param>
        public SectionLine(string tag, string text, SectionTone tone, bool detail = false, int rows = 0, bool source = false, bool startsPage = false,
            bool repeats = false)
        {
            Tag = tag;
            Text = text;
            Tone = tone;
            Detail = detail;
            Rows = rows > 0 ? rows : detail ? 2 : 1;
            Source = source;
            StartsPage = startsPage;
            Repeats = repeats;
        }

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
        public SectionPresentation(SectionKind kind, string provenance, SectionTone provenanceTone, IReadOnlyList<SectionLine> lines, bool simulated,
            bool steps = false)
        {
            Kind = kind;
            Provenance = provenance;
            ProvenanceTone = provenanceTone;
            Lines = lines;
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
                    loading ? Source("Asking what the evaluation source measured…", SectionTone.Secondary)
                    : error != null ? Source("Could not read the evaluation: " + IntelligenceText.Plain(error), SectionTone.Problem)
                    : Source("Not read yet.", SectionTone.Secondary),
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
                        Source(IntelligenceText.EvaluationProvenance(read, synthetic, now, zone) + status, synthetic ? SectionTone.Attention : SectionTone.Secondary),
                    };
                    lines.AddRange(Lines(evaluation, at, zone));
                    return lines;
                case NotFoundEvaluation notFound:
                    return new[]
                    {
                        Source(IntelligenceText.From(IntelligenceText.FromSeorak, notFound.Reason) + "No evaluation yet: " + IntelligenceText.Plain(notFound.Reason.Message) + status,
                            SectionTone.Secondary),
                    };
                case UnavailableEvaluation unavailable:
                    return new[] { Source(IntelligenceText.FromSeorak + " · Evaluation unavailable: " + IntelligenceText.Plain(unavailable.Reason.Message) + status, SectionTone.Secondary) };
                case IncompatibleEvaluation incompatible:
                    return new[] { Source(IntelligenceText.FromSeorak + " · Evaluation unreadable: " + IntelligenceText.Plain(incompatible.Reason.Message) + status, SectionTone.Problem) };
                case UnauthorizedEvaluation unauthorized:
                    return new[] { Source(IntelligenceText.FromSeorak + " · Evaluation not allowed: " + IntelligenceText.Plain(unauthorized.Reason.Message) + status, SectionTone.Attention) };
                default:
                    return new[] { Source("The evaluation came back in a form this app does not know.", SectionTone.Secondary) };
            }
        }

        /// <summary>The checks first, as What was checked? asks about them, then the cost and the outcome.</summary>
        private static IReadOnlyList<SectionLine> Lines(Evaluation evaluation, DateTimeOffset now, TimeZoneInfo zone)
        {
            var lines = new List<SectionLine>();
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
                    state = "partly available" + (availability.Reason is EvaluationAvailabilityReason partly ? ": " + Reason(partly) : "");
                    tone = SectionTone.Attention;
                    break;
                default:
                    state = "unavailable" + (availability.Reason is EvaluationAvailabilityReason reason ? ": " + Reason(reason) : "");
                    tone = SectionTone.Attention;
                    break;
            }
            var covers = coverage.IncludedSessions.ToString(CultureInfo.InvariantCulture) + " of "
                + IntelligenceText.Plural(coverage.MatchedSessions, "session");
            if (coverage.Complete)
            {
                covers += ", complete";
            }
            else
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
                    _ => "recomputing",
                };
                current += freshness.DataThrough != null && IntelligenceText.TryParse(freshness.DataThrough, out var through)
                    ? ", data to " + IntelligenceText.Clock(through, zone, seconds: true)
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
            if (uncommitted == null) return "uncommitted: known once it ends";
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
            if (survival == null) return "3-day line survival: pending";
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

    /// <summary>The words and text handling the sections share.</summary>
    public static class IntelligenceText
    {
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

        /// <summary>
        /// Where an understanding came from and when: "From Salidium 0.6.1, 2 minutes ago", or a
        /// stand-in's, said to be simulated without naming a product: an explanation, or, above the
        /// check runs it saw, checks.
        /// </summary>
        public static string UnderstandingProvenance(IntelligenceRead<UnderstandingResponse> read, UnderstandingSource source, DateTimeOffset now, TimeZoneInfo zone,
            bool checks = false)
        {
            var when = read.Recorded ? "recorded at " + Clock(read.ReadAt, zone, seconds: true) : Ago(source.GeneratedAt, now, zone);
            if (source.Synthetic) return (checks ? "Simulated checks · " : "Simulated explanation · ") + when;
            return "From Salidium " + Truncate(Plain(source.Version), 24) + ", " + when;
        }

        /// <summary>Where a measurement came from and when: "From Seorak, read 1 minute ago", or a stand-in's, said to be simulated.</summary>
        public static string EvaluationProvenance(IntelligenceRead<EvaluationResponse> read, bool synthetic, DateTimeOffset now, TimeZoneInfo zone)
        {
            var when = read.Recorded ? "recorded at " + Clock(read.ReadAt, zone, seconds: true) : "read " + Ago(read.ReadAt, now, zone);
            return synthetic ? "Simulated measurement · " + when : "From Seorak, " + when;
        }

        internal static string ReadingStatus(bool loading, string? error) =>
            loading ? " · reading again…" : error != null ? " · could not read it again: " + Plain(error) : "";

        internal static SectionPresentation Empty(SectionKind kind, string text) =>
            new SectionPresentation(kind, text, SectionTone.Secondary, Array.Empty<SectionLine>(), simulated: false);

        /// <summary>The section while the understanding it shows has not been read.</summary>
        internal static SectionPresentation Waiting(SectionKind kind, bool loading, string? error)
        {
            if (loading) return Empty(kind, "Asking what the understanding source concluded…");
            if (error != null)
            {
                return new SectionPresentation(kind, "Could not read the understanding: " + Plain(error), SectionTone.Problem, Array.Empty<SectionLine>(), simulated: false);
            }
            return Empty(kind, "Not read yet.");
        }

        /// <summary>
        /// A section whose source answered without conclusions: its provenance line names the source,
        /// as it does for an answer, then says why there is none, in words.
        /// </summary>
        internal static SectionPresentation Failure(SectionKind kind, string source, string lead, ErrorInfo reason, SectionTone tone, string status) =>
            new SectionPresentation(kind, From(source, reason) + lead + Plain(reason.Message) + status, tone, Array.Empty<SectionLine>(), simulated: false);

        /// <summary>
        /// The control plane's own answer before it asks any source: the runtime has not said which
        /// session it is, so no source was asked and none is named.
        /// </summary>
        public const string NotAsked = "native_id_unknown";

        /// <summary>"From Salidium · " before why there is no answer, unless no source was asked.</summary>
        internal static string From(string source, ErrorInfo reason) => reason.Code == NotAsked ? "" : source + " · ";

        /// <summary>The source an understanding comes from, as a provenance line names it.</summary>
        public const string FromSalidium = "From Salidium";

        /// <summary>The source a measurement comes from, as a provenance line names it.</summary>
        public const string FromSeorak = "From Seorak";
    }
}

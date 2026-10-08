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
        public SectionLine(string tag, string text, SectionTone tone, bool detail = false, int rows = 0, bool source = false, bool startsPage = false,
            bool repeats = false, Evidence? evidence = null, string? words = null, string? chip = null, FileKind? file = null)
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
            Words = words ?? text;
            Chip = chip ?? ChipOf(Evidence);
            File = file;
        }

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

        /// <summary>What it says beside its chip: the quote alone beside "Agent says", for one.</summary>
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
            Client.Evidence.Reported => "Agent says",
            Client.Evidence.Inferred => "Inferred",
            Client.Evidence.Planned => "Planned",
            Client.Evidence.Explained => "Model explains",
            _ => null,
        };

        /// <summary>
        /// The same line with some of it changed, keeping its evidence, its words and its chip as they
        /// are: a subagent's quote copied stays "Subagent says".
        /// </summary>
        public SectionLine With(string? tag = null, string? text = null, string? words = null, string? chip = null, bool? startsPage = null,
            bool? repeats = null) =>
            new SectionLine(tag ?? Tag, text ?? Text, Tone, Detail, Rows, Source, startsPage ?? StartsPage, repeats ?? Repeats, Evidence, words ?? Words,
                chip ?? Chip, File);

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
                    : error != null ? Source("Couldn't read the evaluation: " + error, SectionTone.Problem)
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
                        Source(IntelligenceText.EvaluationProvenance(read, synthetic, now, zone) + status, SectionTone.Secondary),
                    };
                    lines.AddRange(Lines(evaluation, at, zone));
                    return lines;
                case NotFoundEvaluation notFound:
                    return new[]
                    {
                        Source(IntelligenceText.From(IntelligenceText.FromSeorak, notFound.Reason) + "No evaluation yet: " + IntelligenceText.Why(notFound.Reason) + status,
                            SectionTone.Secondary),
                    };
                case UnavailableEvaluation unavailable:
                    return new[] { Source(IntelligenceText.FromSeorak + " · Evaluation unavailable: " + IntelligenceText.Why(unavailable.Reason) + status, SectionTone.Secondary) };
                case IncompatibleEvaluation incompatible:
                    return new[] { Source(IntelligenceText.FromSeorak + " · Evaluation unreadable: " + IntelligenceText.Why(incompatible.Reason) + status, SectionTone.Problem) };
                case UnauthorizedEvaluation unauthorized:
                    return new[] { Source(IntelligenceText.FromSeorak + " · Evaluation not allowed: " + IntelligenceText.Why(unauthorized.Reason) + status, SectionTone.Secondary) };
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
                    SectionTone.Normal));
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
                    state = "available";
                    break;
                case EvaluationAvailabilityState.Partial:
                    state = "partly available" + (availability.Reason is EvaluationAvailabilityReason partly ? ": " + Reason(partly) : "");
                    break;
                default:
                    state = "unavailable" + (availability.Reason is EvaluationAvailabilityReason reason ? ": " + Reason(reason) : "");
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
            }
            string current;
            if (IntelligenceText.TryParse(freshness.StaleAt, out var staleAt) && now >= staleAt)
            {
                // After this instant the part must read as stale, whatever its state said when read.
                current = "stale since " + IntelligenceText.Clock(staleAt, zone, seconds: false);
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
            }
            return new SectionLine("", state + " · " + covers + " · " + current, tone, detail: true, evidence: Evidence.Measured);
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
        /// Why a source answered without conclusions, by its code, after the lead ("Evaluation unavailable: "),
        /// never the reason's message, which is the source's or the reader's own and can hold an address, a path
        /// or the source's words. The source is named only by the provenance line before it, so the why says
        /// "it" (settled by the coordinator, 2026-10-07).
        /// </summary>
        public static string Why(ErrorInfo reason) => reason.Code switch
        {
            "not_running" => "it isn't running on " + HostText.Your + ". Start it there, then press Refresh.",
            "unreachable" or "timed_out" => "it didn't answer on " + HostText.Your + ". Press Refresh in a moment.",
            "temporarily_unavailable" or "server_error" => "it couldn't answer just now. Press Refresh in a moment.",
            "rate_limited" => "it asked for a pause after too many requests. Press Refresh a little later.",
            "credential_missing" => HostText.Your + " has no credential set up for it. Set one up there.",
            // Neither reader sends a credential that isn't one of its kind (their tests: no request with it).
            "credential_malformed" => "the credential set up for it on " + HostText.Your + " isn't the right kind, so it wasn't sent. Set up another there.",
            "credential_rejected" => "it turned down the credential set up on " + HostText.Your + ". Set up a new one there.",
            "credential_forbidden" or "insufficient_scope" => "the credential set up on " + HostText.Your + " can't read this. Set up one that can.",
            "outside_credential_restriction" => "the credential set up on " + HostText.Your + " covers other projects or dates. Set up one that covers this task.",
            "not_captured" or "not_observed" => "it hasn't seen this task yet. Press Refresh in a moment.",
            "not_yet_computed" => "it hasn't worked this task out yet. Press Refresh in a moment.",
            "not_retained" => "it no longer keeps this task, so there's nothing more to read.",
            "runtime_not_observed" => "it doesn't follow tasks this agent app runs.",
            "not_resolvable" or "not_observable" => "it can't look up this task by the name its agent app gave it.",
            "result_limit" => "it stopped at a limit before it found this task. Press Refresh to try again.",
            "invalid_document" or "unexpected_status" or "unsupported_contract" or "answer_too_large" or "invalid_understanding" or "invalid_evaluation"
                => "its answer isn't one this app can read. Check its version on " + HostText.Your + ".",
            "host_not_allowed" or "origin_not_allowed" => "it turned the request away on " + HostText.Your + ". Check its setup there.",
            // Salidium's reader asks the port who it is before any credential goes, but that question is sent.
            "instance_mismatch" => "what answered isn't it. Check its setup on " + HostText.Your + ".",
            "discovery_unreadable" => "its setup on " + HostText.Your + " can't be read. Check it there.",
            NotAsked => "the agent app hasn't said which session this is yet. Press Refresh in a moment.",
            _ => "it didn't say why. Press Refresh to try again.",
        };

        /// <summary>
        /// Why this headset's own read of a section failed, after "Couldn't read the evaluation: " or
        /// " · couldn't read it again: ", by what failed and never the error's message, which is the control
        /// plane's or the runtime's and can hold an address: one line per cause, as the folders say it, a
        /// refused credential as <paramref name="accessRefused"/> says it for how this headset reaches the
        /// computer (settled by the coordinator, 2026-10-07).
        /// </summary>
        public static string WhyUnread(Exception? error, string accessRefused) => error switch
        {
            OperationCanceledException => "your computer didn't answer in time. Press Refresh to try again.",
            _ when Within<UnaskedReadException>(error) is UnaskedReadException unasked => AfterColon(unasked.Message),
            _ when Within<CertificateMismatchException>(error) != null => AfterColon(ConnectionText.NotThePairedComputer),
            _ when Within<TokenNotSentException>(error) is TokenNotSentException notSent && notSent.Outcome == LoopbackProofOutcome.Unproved
                => "what answered couldn't prove it holds the access code, so the headset sent nothing. Check that this app is running there, then press Refresh.",
            _ when Within<TokenNotSentException>(error) is TokenNotSentException notSent && notSent.Outcome == LoopbackProofOutcome.Unreachable => DidntAnswer,
            ControlPlaneRequestException { Code: "device_revoked", Status: 401 } => AfterColon(ConnectionText.PairingRefused),
            ControlPlaneRequestException { Code: "unauthorized", Status: 401 } => AfterColon(accessRefused),
            ControlPlaneRequestException { Code: "too_many_requests", Status: 429 }
                => "your computer is turning this headset away for a minute after too many tries. Press Refresh after a minute.",
            _ when Within<Newtonsoft.Json.JsonException>(error) != null => AfterColon(ConnectionText.Unreadable),
            // Something answered, with what isn't HTTP or too much of it, or took the connection and set up no secure
            // one: never "didn't answer".
            _ when Within<System.IO.InvalidDataException>(error) != null || Within<FormatException>(error) != null || Within<OverflowException>(error) != null
                || Within<HandshakeFailedException>(error) != null => SomethingWentWrong,
            ControlPlaneRequestException { Code: null, InnerException: System.Net.Http.HttpRequestException } => DidntAnswer,
            _ => SomethingWentWrong,
        };

        private const string DidntAnswer = "your computer didn't answer. Press Refresh to try again.";
        private const string SomethingWentWrong = "something went wrong. Press Refresh to try again.";

        /// <summary>The headset's read when there is nothing to ask (settled by the coordinator, 2026-10-07).</summary>
        public const string NotConnected = HostText.YourStart + " isn't connected. Press Refresh when it is.";

        /// <summary>
        /// One of the app's own sentences, reused after a colon: its first letter lowered, the rest as it is
        /// ("Couldn't read the evaluation: your computer sent something this app can't read. ...").
        /// </summary>
        public static string AfterColon(string sentence) =>
            sentence.Length == 0 ? sentence : char.ToLowerInvariant(sentence[0]).ToString() + sentence.Substring(1);

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
            loading ? " · reading again…" : error != null ? " · couldn't read it again: " + error : "";

        internal static SectionPresentation Empty(SectionKind kind, string text) =>
            new SectionPresentation(kind, text, SectionTone.Secondary, Array.Empty<SectionLine>(), simulated: false);

        /// <summary>The section while the understanding it shows has not been read.</summary>
        internal static SectionPresentation Waiting(SectionKind kind, bool loading, string? error)
        {
            if (loading) return Empty(kind, "Asking what the understanding source concluded…");
            if (error != null)
            {
                return new SectionPresentation(kind, "Couldn't read the understanding: " + error, SectionTone.Problem, Array.Empty<SectionLine>(), simulated: false);
            }
            return Empty(kind, "Not read yet.");
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

        /// <summary>"From Salidium · " before why there is no answer, unless no source was asked.</summary>
        internal static string From(string source, ErrorInfo reason) => reason.Code == NotAsked ? "" : source + " · ";

        /// <summary>The source an understanding comes from, as a provenance line names it.</summary>
        public const string FromSalidium = "From Salidium";

        /// <summary>The source a measurement comes from, as a provenance line names it.</summary>
        public const string FromSeorak = "From Seorak";
    }
}

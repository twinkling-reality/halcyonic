#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// Seorak's measure of a run as the evidence view's strip (the owner's 2026-11-18 02:24Z shape; plan v2 row 1, AUDIT's 9): a
    /// row of single readouts along the top of the view, each a short head over its figure (AUDIT2's 02:27Z and 02:30Z), in the
    /// lead's order: lines added and removed, checks, time and cost, then lines kept, then tool calls, commits and errors. Every figure is Seorak's own field (evaluation.ts); a stat the read lacks is left out, never shown as 0;
    /// no stat carries a direction or a grade (Seorak's replay.md:272). The person chooses which stats stand (<see cref="Shown"/>).
    /// </summary>
    public sealed class EvidenceStrip
    {
        /// <summary>The strip's words (AUDIT2's 02:22Z and 02:27Z).</summary>
        public static class Words
        {
            public const string LinesAdded = "Lines Added", LinesRemoved = "Lines Removed", Time = "Time", ToolCalls = "Tool Calls";
            public const string Checks = "Checks", LinesKept = "Lines Kept", Commits = "Commits", Errors = "Errors", Cost = "Cost";
            public const string Estimated = "Estimated", AfterThreeDays = "After 3 Days";
            public const string PartlyMeasured = "Partly Measured", OutOfDate = "Out of Date";
        }

        /// <summary>One readout: its key (for the person's choice), its head over the figure, the figure, a kind line under it, a rate for a bar.</summary>
        public sealed class Stat
        {
            public Stat(string key, string head, string figure, string? kind = null, double? rate = null) =>
                (Key, Head, Figure, Kind, Rate) = (key, head, figure, kind, rate);

            public string Key { get; }
            public string Head { get; }
            public string Figure { get; }
            /// <summary>A word under the figure (ESTIMATED, a check's kind, After 3 Days), its own line; null for none.</summary>
            public string? Kind { get; }
            public double? Rate { get; }
        }

        /// <summary>How many stats stand before the person chooses: the first that read fastest (plan v2: the film's set).</summary>
        public const int DefaultCount = 5;

        private EvidenceStrip(IReadOnlyList<Stat> all, IReadOnlyList<string> notes) => (All, Notes) = (all, notes);

        /// <summary>Every stat the read holds, instant to wow: what Choose lists.</summary>
        public IReadOnlyList<Stat> All { get; }

        /// <summary>The strip's own quiet line where a part is partial or stale ("Partly Measured", "Out of Date"); empty where neither.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>
        /// The stats that stand: the person's chosen keys in their order, each only where the read holds it; with no choice
        /// made, the first <see cref="DefaultCount"/>. A chosen stat this run lacks is not drawn, never a stand-in.
        /// </summary>
        public IReadOnlyList<Stat> Shown(IReadOnlyList<string>? chosen)
        {
            // Commits and errors read only to a coder: never in the default set (AUDIT's 03:00Z); Choose still offers them.
            if (chosen == null) return All.Where(stat => stat.Key != "commits" && stat.Key != "errors").Take(DefaultCount).ToList();
            var byKey = All.ToDictionary(stat => stat.Key, StringComparer.Ordinal);
            return chosen.Where(byKey.ContainsKey).Select(key => byKey[key]).ToList();
        }

        /// <summary>The strip of <paramref name="evaluation"/>; null where there is none.</summary>
        public static EvidenceStrip? Of(Evaluation? evaluation)
        {
            if (evaluation == null) return null;
            var stats = new List<Stat>();
            var notes = new List<string>();
            var measure = Measured(evaluation.Outcome?.Availability) ? evaluation.Outcome?.Measure : null;
            var activity = Measured(evaluation.Activity?.Availability) ? evaluation.Activity : null;
            // The order the lead set at 02:4xZ: lines added and removed, checks, time, cost, then the wow (lines kept), then the rest.
            if (measure?.Uncommitted is EvaluationUncommitted lines)
            {
                stats.Add(new Stat("lines-added", Words.LinesAdded, Count(lines.LinesAdded)));
                stats.Add(new Stat("lines-removed", Words.LinesRemoved, Count(lines.LinesRemoved)));
            }
            if (Measured(evaluation.Verification?.Availability) && evaluation.Verification?.Lens is EvaluationVerificationLens lens)
                foreach (var kind in lens.ByKind.Where(kind => kind.Passed is long && kind.Runs is long))
                    // One readout a kind of check, its own label under the figure, theirs; never a sum Seorak did not give.
                    // One line a newcomer reads at once: "1 of 1 test passed", the kind the source's own word (AUDIT's 03:00Z).
                    stats.Add(new Stat("checks:" + kind.Label, Words.Checks, Count(kind.Passed!.Value) + " of " + Count(kind.Runs!.Value) + " " + kind.Label + " passed", null, kind.PassRate));
            if (activity != null) stats.Add(new Stat("time", Words.Time, Time(activity.ElapsedSeconds)));
            if (Measured(evaluation.Cost?.Availability) && evaluation.Cost?.EstimatedUsd is double usd)
                stats.Add(new Stat("cost", Words.Cost, usd < 0.005 ? "Under $0.01" : "$" + usd.ToString("0.00", CultureInfo.InvariantCulture), Words.Estimated));
            if (measure?.LineSurvival is EvaluationLineSurvival kept && kept.Fate != EvaluationLineSurvivalFate.Unknown && kept.Rate is double rate)
                stats.Add(new Stat("lines-kept", Words.LinesKept, Count(kept.LinesSurviving) + " of " + Count(kept.LinesAuthored), Words.AfterThreeDays, rate));
            if (activity != null) stats.Add(new Stat("tool-calls", Words.ToolCalls, Count(activity.ToolCalls)));
            if (measure?.CommitsLanded is long commits) stats.Add(new Stat("commits", Words.Commits, Count(commits)));
            if (measure?.ErrorCount is long errors) stats.Add(new Stat("errors", Words.Errors, Count(errors)));
            var parts = new[] { evaluation.Cost?.Availability, evaluation.Outcome?.Availability, evaluation.Verification?.Availability, evaluation.Activity?.Availability };
            if (parts.Any(part => part?.State == EvaluationAvailabilityState.Partial)) notes.Add(Words.PartlyMeasured);
            var freshness = new[] { evaluation.Cost?.Freshness, evaluation.Outcome?.Freshness, evaluation.Verification?.Freshness, evaluation.Activity?.Freshness };
            if (freshness.Any(part => part?.State == EvaluationFreshnessState.Stale)) notes.Add(Words.OutOfDate);
            return new EvidenceStrip(stats, notes);
        }

        private static bool Measured(EvaluationAvailability? availability) =>
            availability != null && availability.State != EvaluationAvailabilityState.Unavailable;

        private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>"18 min", from an hour "1 h 5 min", rounded to the minute; under a minute "Under 1 min" (AUDIT2's 02:30Z); never a clock form.</summary>
        private static string Time(double seconds)
        {
            var minutes = (long)Math.Round(Math.Max(0, seconds) / 60d, MidpointRounding.AwayFromZero);
            if (seconds < 60) return "Under 1 min";
            if (minutes < 60) return minutes.ToString(CultureInfo.InvariantCulture) + " min";
            var rest = minutes % 60;
            return (minutes / 60).ToString(CultureInfo.InvariantCulture) + " h" + (rest > 0 ? " " + rest.ToString(CultureInfo.InvariantCulture) + " min" : "");
        }
    }
}

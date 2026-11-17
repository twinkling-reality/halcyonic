#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// Seorak's evaluation of one run as "How It Went" reads it (the lead's 2026-11-17 20:15Z; AUDIT2's 20:17Z words; laid after
    /// hud-titanfall-2-end-of-match.png and result-death-stranding-results.png): sections of rows, each a measure's words and the
    /// source's own value, untouched. A part the source marks unavailable, and any null, is left out, never shown as 0, a dash
    /// or a placeholder; no grade, rating or overall score is ever made. The view names the source once (SEORAK MEASURED).
    /// </summary>
    public sealed class RunResults
    {
        /// <summary>The view's own words (AUDIT2's 20:17Z).</summary>
        public static class Words
        {
            public const string Title = "How It Went";
            public const string Source = "Seorak Measured";
            public const string NotYet = "Not measured yet";
            public const string Activity = "Activity", Changes = "Changes", Checks = "Checks", Cost = "Cost";
            public const string Time = "Time", ToolCalls = "Tool Calls", Instructions = "Instructions";
            public const string FilesChanged = "Files Changed", LinesAdded = "Lines Added", LinesRemoved = "Lines Removed";
            public const string Commits = "Commits", Errors = "Errors", LinesKept = "Lines Kept";
        }

        /// <summary>One measure: its words, the source's value, a line under the value where the measure has one, and a rate for a bar.</summary>
        public sealed class Row
        {
            public Row(string label, string value, string? detail = null, double? rate = null) =>
                (Label, Value, Detail, Rate) = (label, value, detail, rate);

            public string Label { get; }
            public string Value { get; }
            public string? Detail { get; }
            /// <summary>The source's own fraction, 0 to 1, for the row's bar; null for a row with none.</summary>
            public double? Rate { get; }
        }

        /// <summary>A section: its head in the label role, its rows, or the source's own words where it measured none.</summary>
        public sealed class Section
        {
            public Section(string head, IReadOnlyList<Row> rows, string? empty = null) => (Head, Rows, Empty) = (head, rows, empty);

            public string Head { get; }
            public IReadOnlyList<Row> Rows { get; }
            /// <summary>The source's own reason it measured nothing here (theirs), or null.</summary>
            public string? Empty { get; }
        }

        private RunResults(IReadOnlyList<Section> sections) => Sections = sections;

        public IReadOnlyList<Section> Sections { get; }

        /// <summary>The results of <paramref name="evaluation"/>; null where there is none.</summary>
        public static RunResults? Of(Evaluation? evaluation)
        {
            if (evaluation == null) return null;
            var sections = new List<Section>();
            if (Measured(evaluation.Activity?.Availability) && evaluation.Activity is EvaluationActivity activity)
            {
                var rows = new List<Row> { new Row(Words.Time, Time(activity.ElapsedSeconds)), new Row(Words.ToolCalls, Count(activity.ToolCalls)) };
                if (activity.Prompts is long prompts) rows.Add(new Row(Words.Instructions, Count(prompts)));
                sections.Add(new Section(Words.Activity, rows));
            }
            if (Measured(evaluation.Outcome?.Availability) && evaluation.Outcome?.Measure is EvaluationOutcomeMeasure measure)
            {
                var rows = new List<Row>();
                if (measure.Uncommitted is EvaluationUncommitted changed)
                {
                    rows.Add(new Row(Words.FilesChanged, Count(changed.FilesTouched)));
                    // Two values, never netted (AUDIT2's 20:17Z).
                    rows.Add(new Row(Words.LinesAdded, Count(changed.LinesAdded)));
                    rows.Add(new Row(Words.LinesRemoved, Count(changed.LinesRemoved)));
                }
                if (measure.CommitsLanded is long commits) rows.Add(new Row(Words.Commits, Count(commits)));
                if (measure.ErrorCount is long errors) rows.Add(new Row(Words.Errors, Count(errors)));
                // Only where the source measured a fate (never Unknown) and gave its own rate.
                if (measure.LineSurvival is EvaluationLineSurvival kept && kept.Fate != EvaluationLineSurvivalFate.Unknown && kept.Rate is double rate)
                    rows.Add(new Row(Words.LinesKept, Count(kept.LinesSurviving) + " of " + Count(kept.LinesAuthored) + " Kept",
                        Count(kept.CommitsChecked) + (kept.CommitsChecked == 1 ? " Commit Checked" : " Commits Checked"), rate));
                if (rows.Count > 0) sections.Add(new Section(Words.Changes, rows));
            }
            if (Measured(evaluation.Verification?.Availability) && evaluation.Verification?.Lens is EvaluationVerificationLens lens)
            {
                var rows = lens.ByKind
                    .Where(kind => kind.Passed is long && kind.Runs is long)
                    .Select(kind => new Row(kind.Label, Count(kind.Passed!.Value) + " of " + Count(kind.Runs!.Value) + " Passed", null, kind.PassRate))
                    .ToList();
                if (rows.Count > 0 || !string.IsNullOrWhiteSpace(lens.EmptyReason))
                    sections.Add(new Section(Words.Checks, rows, rows.Count == 0 ? lens.EmptyReason : null));
            }
            if (Measured(evaluation.Cost?.Availability) && evaluation.Cost?.EstimatedUsd is double usd)
                // Always marked estimated, as the source's own note says it is (AUDIT2's 20:17Z).
                sections.Add(new Section(Words.Cost, new[] { new Row(Words.Cost, "$" + usd.ToString("0.00", CultureInfo.InvariantCulture) + " Estimated") }));
            return new RunResults(sections);
        }

        private static bool Measured(EvaluationAvailability? availability) =>
            availability != null && availability.State != EvaluationAvailabilityState.Unavailable;

        private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>Minutes and seconds from the source's elapsed seconds: "4 min 12 s", and under a minute "48 s".</summary>
        private static string Time(double seconds)
        {
            var whole = (long)Math.Floor(Math.Max(0, seconds));
            if (whole < 60) return whole.ToString(CultureInfo.InvariantCulture) + " s";
            var minutes = whole / 60;
            var rest = whole % 60;
            return minutes.ToString(CultureInfo.InvariantCulture) + " min" + (rest > 0 ? " " + rest.ToString(CultureInfo.InvariantCulture) + " s" : "");
        }
    }
}

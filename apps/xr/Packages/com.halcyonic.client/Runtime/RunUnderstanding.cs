#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// Salidium's understanding of one run as the project screen's Reasoning door reads it (the lead's 2026-11-17 20:15Z; AUDIT2's 20:19Z words;
    /// laid after result-detroit-analysis-conclusion.jpg, the one finding as the hero sentence, and
    /// list-death-stranding-cargo-management.png, grouped rows with a mark, words and a right-aligned value): the source's verdict,
    /// each method's latest run, and its own counts and items. Every value is the source's own; a count it did not give is never
    /// guessed, and each outcome is told by its mark's form (the kit's glyphs), never by a colour.
    /// </summary>
    public sealed class RunUnderstanding
    {
        /// <summary>The view's own words (AUDIT2's 20:19Z).</summary>
        public static class Words
        {
            /// <summary>The page takes its door's word (AUDIT2's 20:52Z): the project screen's door opens a page of its own name.</summary>
            public static string Title => KitScreenModel.Words(KitScreenAction.Why);
            /// <summary>The verdict's class in the product's own words, never the source's name (the lead's 2026-11-13 ruling); a stand-in's says so.</summary>
            public const string Observed = "Observed", Reported = "Reported", Simulated = "Simulated";
            public const string Runs = "Runs", Changes = "Changes", Review = "Review", Remaining = "Remaining";
            public const string Passed = "Passed", Failed = "Failed", PartlyPassed = "Partly Passed", CantTell = "Can't Tell";
            public const string OutOfDate = "Out of Date";
            public const string Files = "Files", Commits = "Commits", Open = "Open", Resolved = "Resolved";
        }

        /// <summary>A run's outcome, for its mark's form.</summary>
        public enum Mark { None, Passed, Failed, Partly, CantTell }

        /// <summary>One row: its words, the value on the right, the source's own facts after it, a quiet note under it, and its mark.</summary>
        public sealed class Row
        {
            public Row(string label, string? value = null, IReadOnlyList<string>? facts = null, string? note = null, Mark mark = Mark.None) =>
                (Label, Value, Facts, Note, Mark) = (label, value, facts ?? new string[0], note, mark);

            public string Label { get; }
            public string? Value { get; }
            public IReadOnlyList<string> Facts { get; }
            public string? Note { get; }
            public Mark Mark { get; }
        }

        public sealed class Section
        {
            public Section(string head, IReadOnlyList<Row> rows) => (Head, Rows) = (head, rows);

            public string Head { get; }
            public IReadOnlyList<Row> Rows { get; }
        }

        private RunUnderstanding(string source, string headline, string? because, IReadOnlyList<Section> sections) =>
            (Source, Headline, Because, Sections) = (source, headline, because, sections);

        /// <summary>The view's label: OBSERVED where the source observed its verdict, else REPORTED; SIMULATED for a stand-in.</summary>
        public string Source { get; }

        /// <summary>The source's verdict, its own words: the hero sentence.</summary>
        public string Headline { get; }

        /// <summary>Why, as the source says it; null where it gives none.</summary>
        public string? Because { get; }

        public IReadOnlyList<Section> Sections { get; }

        /// <summary>The understanding of <paramref name="understanding"/>; null where there is none.</summary>
        public static RunUnderstanding? Of(Understanding? understanding)
        {
            if (understanding?.Verdict == null) return null;
            var sections = new List<Section>();
            var runs = (understanding.Verification?.LatestByMethod ?? new List<UnderstandingVerificationRun>()).Select(RunRow).ToList();
            if (runs.Count > 0) sections.Add(new Section(Words.Runs, runs));
            if (understanding.Changes != null)
                sections.Add(new Section(Words.Changes, new[]
                {
                    new Row(Words.Files, Count(understanding.Changes.Files.Count)), new Row(Words.Commits, Count(understanding.Changes.Commits.Count)),
                }));
            if (understanding.Review != null)
                sections.Add(new Section(Words.Review, new[] { new Row(Words.Open, Count(understanding.Review.Open)), new Row(Words.Resolved, Count(understanding.Review.Resolved)) }));
            var remaining = (understanding.Remaining?.Items ?? new List<UnderstandingRemainingItem>())
                .Where(item => !string.IsNullOrWhiteSpace(item.Text)).Select(item => new Row(item.Text)).ToList();
            if (remaining.Count > 0) sections.Add(new Section(Words.Remaining, remaining));
            var source = understanding.Source?.Synthetic == true ? Words.Simulated
                : understanding.Verdict.Epistemic == UnderstandingEpistemic.Observed ? Words.Observed : Words.Reported;
            return new RunUnderstanding(source, understanding.Verdict.Headline, understanding.Verdict.Because, sections);
        }

        private static Row RunRow(UnderstandingVerificationRun run)
        {
            var (word, mark) = run.Outcome switch
            {
                UnderstandingVerificationRunOutcome.Pass => (Words.Passed, Mark.Passed),
                UnderstandingVerificationRunOutcome.Fail => (Words.Failed, Mark.Failed),
                UnderstandingVerificationRunOutcome.Partial => (Words.PartlyPassed, Mark.Partly),
                _ => (Words.CantTell, Mark.CantTell),
            };
            var facts = new List<string>();
            var counts = run.Counts;
            // Only what the source gave: both counts as "p of t Passed", else each one given alone, never a guessed total.
            if (counts?.Passed is long passed && counts.Total is long total) facts.Add(Count(passed) + " of " + Count(total) + " Passed");
            else
            {
                if (counts?.Passed is long alone) facts.Add(Count(alone) + " Passed");
                if (counts?.Failed is long failed) facts.Add(Count(failed) + " Failed");
            }
            if (run.Exit?.Code is long code) facts.Add("Exit " + code.ToString(CultureInfo.InvariantCulture));
            return new Row(run.Label, word, facts, run.Stale ? Words.OutOfDate : null, mark);
        }

        private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
    }
}

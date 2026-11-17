#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// One evidence view as the headset lays it (the lead's 2026-11-17 20:50Z): the Outcome page from Seorak's evaluation
    /// (<see cref="RunResults"/>) or the Reasoning page from Salidium's understanding (<see cref="RunUnderstanding"/>), read through
    /// the plane's own reads. Where the read gives no answer the page says so honestly, in the words the text sections already use
    /// (Not Measured, Measurement Unavailable, Measurement Unreadable, Measurement Not Allowed; No Conclusions, Conclusions
    /// Unavailable, Conclusions Unreadable, Conclusions Not Allowed), with the reason's own phrase; before any read, the state's own
    /// word with <see cref="NothingRead"/>. Nothing on a page is made up: every value is the source's.
    /// </summary>
    public sealed class EvidencePage
    {
        /// <summary>One row: its words, the value on the one right edge, the source's facts after it, a quiet note, a rate, a mark.</summary>
        public sealed class Row
        {
            public Row(string label, string? value, IReadOnlyList<string>? facts = null, string? note = null, double? rate = null,
                RunUnderstanding.Mark mark = RunUnderstanding.Mark.None) =>
                (Label, Value, Facts, Note, Rate, Mark) = (label, value, facts ?? new string[0], note, rate, mark);

            public string Label { get; }
            public string? Value { get; }
            public IReadOnlyList<string> Facts { get; }
            public string? Note { get; }
            public double? Rate { get; }
            public RunUnderstanding.Mark Mark { get; }
        }

        public sealed class Section
        {
            public Section(string head, IReadOnlyList<Row> rows, string? empty = null) => (Head, Rows, Empty) = (head, rows, empty);

            public string Head { get; }
            public IReadOnlyList<Row> Rows { get; }
            /// <summary>The source's own reason it measured nothing here (theirs), or null.</summary>
            public string? Empty { get; }
        }

        private EvidencePage(string title, string? label, string? headline, string? because, IReadOnlyList<Section> sections, string? state, string? why) =>
            (Title, Label, Headline, Because, Sections, State, Why) = (title, label, headline, because, sections, state, why);

        /// <summary>The view's title.</summary>
        public string Title { get; }

        /// <summary>How the page knows, once, in the label role (MEASURED, OBSERVED, REPORTED or SIMULATED), never the source's name; null without an answer.</summary>
        public string? Label { get; }

        /// <summary>The source's verdict as the hero sentence, and why (theirs); null on the Outcome page.</summary>
        public string? Headline { get; }
        public string? Because { get; }

        public IReadOnlyList<Section> Sections { get; }

        /// <summary>Where the read gave no answer: its state in the sections' own words; null for an answer.</summary>
        public string? State { get; }

        /// <summary>The reason's own phrase under the state; null for none.</summary>
        public string? Why { get; }

        /// <summary>The Outcome page from <paramref name="response"/>, or its honest state; before any read, the state and <see cref="NothingRead"/>.</summary>
        public static EvidencePage Of(EvaluationResponse? response)
        {
            var title = RunResults.Words.Title;
            switch (response?.Result)
            {
                case AvailableEvaluation available when RunResults.Of(available.Evaluation) is RunResults results:
                    return new EvidencePage(title, results.Source, null, null,
                        results.Sections.Select(section => new Section(section.Head,
                            section.Rows.Select(row => new Row(row.Label, row.Value, row.Detail == null ? null : new[] { row.Detail }, null, row.Rate)).ToList(),
                            section.Empty)).ToList(), null, null);
                case NotFoundEvaluation notFound: return Without(title, "Not Measured", notFound.Reason);
                case UnavailableEvaluation unavailable: return Without(title, "Measurement Unavailable", unavailable.Reason);
                case IncompatibleEvaluation incompatible: return Without(title, "Measurement Unreadable", incompatible.Reason);
                case UnauthorizedEvaluation unauthorized: return Without(title, "Measurement Not Allowed", unauthorized.Reason);
                default: return Without(title, "Not Measured", null, NothingRead);
            }
        }

        /// <summary>Before any read, the state's own word with this under it (AUDIT2's 20:52Z), so the title line has one form.</summary>
        public const string NothingRead = "Nothing has been read for this task yet.";

        /// <summary>The Reasoning page from <paramref name="response"/>, or its honest state; before any read, the state and <see cref="NothingRead"/>.</summary>
        public static EvidencePage Of(UnderstandingResponse? response)
        {
            var title = RunUnderstanding.Words.Title;
            switch (response?.Result)
            {
                case AvailableUnderstanding available when RunUnderstanding.Of(available.Understanding) is RunUnderstanding understood:
                    return new EvidencePage(title, understood.Source, understood.Headline, understood.Because,
                        understood.Sections.Select(section => new Section(section.Head,
                            section.Rows.Select(row => new Row(row.Label, row.Value, row.Facts, row.Note, null, row.Mark)).ToList())).ToList(), null, null);
                case NotFoundUnderstanding notFound: return Without(title, "No Conclusions", notFound.Reason);
                case UnavailableUnderstanding unavailable: return Without(title, "Conclusions Unavailable", unavailable.Reason);
                case IncompatibleUnderstanding incompatible: return Without(title, "Conclusions Unreadable", incompatible.Reason);
                case UnauthorizedUnderstanding unauthorized: return Without(title, "Conclusions Not Allowed", unauthorized.Reason);
                default: return Without(title, "No Conclusions", null, NothingRead);
            }
        }

        /// <summary>
        /// The Outcome page from the file's own feed (<see cref="FileColumn"/>'s reads): its last answer, else where the read itself
        /// failed the feed's own reason under Measurement Unavailable, else nothing read yet.
        /// </summary>
        public static EvidencePage Of(IntelligenceFeed<EvaluationResponse>? feed) =>
            feed?.Last == null && feed?.Error is string failed ? Without(RunResults.Words.Title, "Measurement Unavailable", null, failed) : Of(feed?.Last?.Response);

        /// <summary>The Reasoning page from the file's own feed, as <see cref="Of(IntelligenceFeed{EvaluationResponse})"/>.</summary>
        public static EvidencePage Of(IntelligenceFeed<UnderstandingResponse>? feed) =>
            feed?.Last == null && feed?.Error is string failed ? Without(RunUnderstanding.Words.Title, "Conclusions Unavailable", null, failed) : Of(feed?.Last?.Response);

        /// <summary>Every row in its order, with its section's head; a section the source measured nothing in stands as its reason alone.</summary>
        private IReadOnlyList<(Section Section, Row? Row)> Entries => Sections
            .SelectMany(section => section.Rows.Count > 0 ? section.Rows.Select(row => (section, (Row?)row)) : section.Empty != null ? new[] { (section, (Row?)null) } : new (Section, Row?)[0])
            .ToList();

        /// <summary>The page's parts on the kit's card, five rows a part (<see cref="RoomCard.ListAPage"/>); one where it is a state alone.</summary>
        public int Parts => State != null ? 1 : System.Math.Max(1, (Entries.Count + RoomCard.ListAPage - 1) / RoomCard.ListAPage);

        /// <summary>
        /// Part <paramref name="part"/> on the kit's card (laid after list-death-stranding-cargo-management.png: grouped rows, a
        /// mark, the words, the value on the one right edge; and result-detroit-analysis-conclusion.jpg: the one finding first):
        /// how it knows in the label role over the door's word, the finding said once as the description on the first part, then the
        /// card's results (<see cref="RoomCard.Results"/>): each section's head once in the label role over its rows. A state alone
        /// stands as the card's status, its reason under it. The kit draws every mark by its form, never by a colour.
        /// </summary>
        public RoomCard Card(int part)
        {
            var title = LineWords.Ours(Title);
            if (State != null) return new RoomCard(title, status: new RoomCardStatus(State, Why, RoomTone.Quiet));
            var entries = Entries;
            var at = System.Math.Max(0, System.Math.Min(part, Parts - 1)) * RoomCard.ListAPage;
            // The part's rows by their sections, a section's head once over its rows on each part it reaches (AUDIT's 21:13Z point 1).
            var sections = new List<(string Head, List<RoomResultsRow> Rows)>();
            for (var index = at; index < System.Math.Min(entries.Count, at + RoomCard.ListAPage); index++)
            {
                var (section, row) = entries[index];
                if (index == at || !ReferenceEquals(entries[index - 1].Section, section)) sections.Add((section.Head, new List<RoomResultsRow>()));
                var rows = sections[sections.Count - 1].Rows;
                if (row == null)
                {
                    rows.Add(new RoomResultsRow(LineWords.Theirs(section.Empty)));
                    continue;
                }
                // The source's own names (a method, a kind of check, an item left) lean as theirs; a method's command reads in mono.
                var theirs = section.Head is RunUnderstanding.Words.Runs or RunUnderstanding.Words.Remaining or RunResults.Words.Checks;
                var lines = new List<string>();
                if (row.Note != null) lines.Add(row.Note);
                lines.AddRange(row.Facts);
                rows.Add(new RoomResultsRow(theirs ? LineWords.Theirs(row.Label) : LineWords.Ours(row.Label), row.Value, lines, KindOf(row.Mark),
                    mono: section.Head == RunUnderstanding.Words.Runs, rate: row.Rate));
            }
            LineWords? description = null;
            if (at == 0 && Headline != null)
                // The finding is said once (AUDIT's 21:13Z point 4): a why that only repeats a run's own name, or the headline, is left to it.
                description = Because == null || Because == Headline || Sections.Any(section => section.Rows.Any(row => row.Label == Because))
                    ? LineWords.Theirs(Headline) : LineWords.Theirs(Headline) + LineWords.Written("\n") + LineWords.Theirs(Because);
            return new RoomCard(title, description, label: Label)
                .WithResults(sections.Select(section => new RoomResultsSection(section.Head, section.Rows)).ToList());
        }

        private static RoomKind KindOf(RunUnderstanding.Mark mark) => mark switch
        {
            RunUnderstanding.Mark.Passed => RoomKind.Passed,
            RunUnderstanding.Mark.Failed => RoomKind.Failed,
            _ => RoomKind.None,
        };

        private static EvidencePage Without(string title, string state, ErrorInfo? reason, string? said = null) =>
            new EvidencePage(title, null, null, null, new Section[0], state, reason == null ? said : IntelligenceText.Why(reason));
    }
}

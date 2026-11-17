#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// One evidence view as the headset lays it (the lead's 2026-11-17 20:50Z): "How It Went" from Seorak's evaluation
    /// (<see cref="RunResults"/>) or "What It Understood" from Salidium's understanding (<see cref="RunUnderstanding"/>), read through
    /// the plane's own reads. Where the read gives no answer the page says so honestly, in the words the text sections already use
    /// (Not Measured, Measurement Unavailable, Measurement Unreadable, Measurement Not Allowed; No Conclusions, Conclusions
    /// Unavailable, Conclusions Unreadable, Conclusions Not Allowed), with the reason's own phrase; before any read, "Not measured
    /// yet". Nothing on a page is made up: every value is the source's.
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

        /// <summary>The source named once, in the label role (SEORAK MEASURED, SALIDIUM OBSERVED or REPORTED); null without an answer.</summary>
        public string? Label { get; }

        /// <summary>The source's verdict as the hero sentence, and why (theirs); null on "How It Went".</summary>
        public string? Headline { get; }
        public string? Because { get; }

        public IReadOnlyList<Section> Sections { get; }

        /// <summary>Where the read gave no answer: its state in the sections' own words; null for an answer.</summary>
        public string? State { get; }

        /// <summary>The reason's own phrase under the state; null for none.</summary>
        public string? Why { get; }

        /// <summary>"How It Went" from <paramref name="response"/>, or its honest state; before any read, "Not measured yet".</summary>
        public static EvidencePage Of(EvaluationResponse? response)
        {
            var title = RunResults.Words.Title;
            switch (response?.Result)
            {
                case AvailableEvaluation available when RunResults.Of(available.Evaluation) is RunResults results:
                    return new EvidencePage(title, RunResults.Words.Source, null, null,
                        results.Sections.Select(section => new Section(section.Head,
                            section.Rows.Select(row => new Row(row.Label, row.Value, row.Detail == null ? null : new[] { row.Detail }, null, row.Rate)).ToList(),
                            section.Empty)).ToList(), null, null);
                case NotFoundEvaluation notFound: return Without(title, "Not Measured", notFound.Reason);
                case UnavailableEvaluation unavailable: return Without(title, "Measurement Unavailable", unavailable.Reason);
                case IncompatibleEvaluation incompatible: return Without(title, "Measurement Unreadable", incompatible.Reason);
                case UnauthorizedEvaluation unauthorized: return Without(title, "Measurement Not Allowed", unauthorized.Reason);
                default: return Without(title, RunResults.Words.NotYet, null);
            }
        }

        /// <summary>"What It Understood" from <paramref name="response"/>, or its honest state; before any read, "Not measured yet".</summary>
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
                default: return Without(title, RunResults.Words.NotYet, null);
            }
        }

        private static EvidencePage Without(string title, string state, ErrorInfo? reason) =>
            new EvidencePage(title, null, null, null, new Section[0], state, reason == null ? null : IntelligenceText.Why(reason));
    }
}

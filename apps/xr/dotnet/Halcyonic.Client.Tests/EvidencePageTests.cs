using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The two evidence views as pages from the plane's own reads (the lead's 2026-11-17 20:50Z): an answer becomes its sections, and
/// every read without one says so honestly, in the text sections' own words with the reason's own phrase, never a figure.
/// </summary>
public class EvidencePageTests
{
    private static readonly ErrorInfo Reason = new() { Code = "source_unreachable", Message = "developer words" };

    private static EvaluationResponse Evaluation(EvaluationResult result) => new() { ExecutionId = "e1", Result = result };
    private static UnderstandingResponse Understanding(UnderstandingResult result) => new() { ExecutionId = "e1", Result = result };

    [Test]
    public void AnAnswerBecomesItsSectionsUnderTheSourcesLabel()
    {
        var measured = new Evaluation
        {
            Cost = new EvaluationCost { Availability = new EvaluationAvailability { State = EvaluationAvailabilityState.Available }, EstimatedUsd = 0.5 },
            Outcome = new EvaluationOutcome { Availability = new EvaluationAvailability { State = EvaluationAvailabilityState.Unavailable } },
            Verification = new EvaluationVerification
            {
                Availability = new EvaluationAvailability { State = EvaluationAvailabilityState.Available },
                Lens = new EvaluationVerificationLens { ByKind = new List<EvaluationVerificationKind> { new() { Label = "Tests", Runs = 4, Passed = 3, PassRate = 0.75 } } },
            },
        };
        var page = EvidencePage.Of(Evaluation(new AvailableEvaluation { Evaluation = measured }));
        Assert.That((page.Title, page.Label, page.State), Is.EqualTo(("Outcome", "Measured", (string?)null)));
        Assert.That(page.Sections.Select(section => section.Head), Is.EqualTo(new[] { "Checks", "Cost" }));
        Assert.That(page.Sections[0].Rows.Single().Rate, Is.EqualTo(0.75));
        var results = page.Card(0).Results;
        Assert.That(results.Select(section => (section.Head, string.Join("|", section.Rows.Select(row => row.Name.Text + "=" + row.Value)))),
            Is.EqualTo(new[] { ("Checks", "Tests=3 of 4 Passed"), ("Cost", "Cost=$0.50 Estimated") }), "each section's head once over its rows, the value plain");
        Assert.That(results[0].Rows.Single().Rate, Is.EqualTo(0.75), "the rate for its bar");

        // The finding is said once: a why that only repeats a run's own name is left to the run (AUDIT's 21:13Z point 4).
        var repeated = new Halcyonic.Contracts.Understanding
        {
            Verdict = new UnderstandingVerdict { Headline = "1 test failing", Because = "make test", Epistemic = UnderstandingEpistemic.Observed },
            Verification = new UnderstandingVerification
            {
                Summary = "",
                LatestByMethod = new List<UnderstandingVerificationRun>
                {
                    new() { Label = "make test", Outcome = UnderstandingVerificationRunOutcome.Fail, At = "2026-11-17T19:30:00.000Z", Epistemic = UnderstandingEpistemic.Observed },
                },
            },
        };
        Assert.That(EvidencePage.Of(Understanding(new AvailableUnderstanding { Understanding = repeated })).Card(0).Description?.Text, Is.EqualTo("1 test failing"));

        var understood = EvidencePage.Of(Understanding(new AvailableUnderstanding
        {
            Understanding = new Halcyonic.Contracts.Understanding
            {
                Verdict = new UnderstandingVerdict { Headline = "It works.", Because = "The tests passed.", Epistemic = UnderstandingEpistemic.Reported },
                Verification = new UnderstandingVerification { Summary = "", LatestByMethod = new List<UnderstandingVerificationRun>() },
            },
        }));
        Assert.That((understood.Title, understood.Label, understood.Headline, understood.Because),
            Is.EqualTo(("Reasoning", "Reported", "It works.", "The tests passed.")), "the page takes its door's word (AUDIT2's 20:52Z)");
    }

    [Test]
    public void EveryReadWithoutAnAnswerSaysSoInTheSectionsOwnWords()
    {
        var why = IntelligenceText.Why(Reason);
        var measurement = new (EvaluationResult Result, string State)[]
        {
            (new NotFoundEvaluation { Reason = Reason }, "Not Measured"),
            (new UnavailableEvaluation { Reason = Reason }, "Measurement Unavailable"),
            (new IncompatibleEvaluation { Reason = Reason }, "Measurement Unreadable"),
            (new UnauthorizedEvaluation { Reason = Reason }, "Measurement Not Allowed"),
        };
        foreach (var (result, state) in measurement)
        {
            var page = EvidencePage.Of(Evaluation(result));
            Assert.That((page.State, page.Why, page.Label, page.Sections.Count), Is.EqualTo((state, why, (string?)null, 0)), state);
            Assert.That(page.Why, Does.Not.Contain("developer"), "the reason's own phrase, never its message");
        }
        var conclusions = new (UnderstandingResult Result, string State)[]
        {
            (new NotFoundUnderstanding { Reason = Reason }, "No Conclusions"),
            (new UnavailableUnderstanding { Reason = Reason }, "Conclusions Unavailable"),
            (new IncompatibleUnderstanding { Reason = Reason }, "Conclusions Unreadable"),
            (new UnauthorizedUnderstanding { Reason = Reason }, "Conclusions Not Allowed"),
        };
        foreach (var (result, state) in conclusions)
            Assert.That(EvidencePage.Of(Understanding(result)).State, Is.EqualTo(state), state);
    }

    [Test]
    public void BeforeAnyReadThePageSaysItsStateAndThatNothingWasRead()
    {
        var before = EvidencePage.Of((EvaluationResponse?)null);
        Assert.That((before.State, before.Why), Is.EqualTo(("Not Measured", "Nothing has been read for this task yet.")), "one form for the title line (AUDIT2's 20:52Z)");
        Assert.That(EvidencePage.Of((UnderstandingResponse?)null).State, Is.EqualTo("No Conclusions"));
    }
}

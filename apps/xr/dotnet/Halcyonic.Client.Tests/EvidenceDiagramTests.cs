using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The evidence view's diagram and strip (plan v2, AUDIT's 9) built from the demonstration's recorded answers in the reads' own
/// field shape: every node one field, every class the source's, nothing made up, and the ids stable so the view grows node by node.
/// </summary>
public class EvidenceDiagramTests
{
    private const string Run = "01a0ec64-6fac-77a9-827e-bdcf356af403";
    private const string Asked = "Add cursor pagination to GET /orders, 50 orders per page, with tests.";

    private static T Recorded<T>(string source, int at) where T : class =>
        HalcyonicJson.Deserialize<T>(JObject.Parse(Demonstration.Text())[source]![Run]![at]!["response"]!.ToString());

    [Test]
    public void EachPlaceHoldsTheReadsOwnFieldsInTheRoutesOrder()
    {
        var diagram = EvidenceDiagram.Of(Asked, Recorded<UnderstandingResponse>("understanding", 7));
        Assert.That(diagram.Places.Select(place => place.Head), Is.EqualTo(new[] { "You Asked", "Changes", "How", "Checks", "Left to Do", "Verdict", "Why" }));
        var places = diagram.Places.ToDictionary(place => place.Kind);
        Assert.That(places[DiagramPlaceKind.Asked].Nodes.Single().Words.Text, Is.EqualTo(Asked));
        Assert.That(places[DiagramPlaceKind.Changes].Nodes.Select(node => (node.Words.Text, string.Join("|", node.Lines), node.Class)), Is.EqualTo(new[]
        {
            ("test/orders.test.ts", "+52", DiagramClass.Observed),
            ("src/routes/orders-all.ts", "-24", DiagramClass.Observed),
            ("src/routes/orders.ts", "+38|-9", DiagramClass.Observed),
        }), "each count on its own line, a zero left out");
        var run = places[DiagramPlaceKind.Checks].Nodes.Single();
        Assert.That((run.Words.Text, run.Value, run.Mark, run.Class, string.Join("|", run.Lines)),
            Is.EqualTo(("31 passed (make test SUITE=orders)", (string?)"Passed", RoomKind.Passed, DiagramClass.Observed, "31 Passed")));
        Assert.That(places[DiagramPlaceKind.How].Nodes.Select(node => node.Words.Text),
            Is.EqualTo(new[] { "src/routes/orders.ts", "Sort by creation time", "Read 50 after the cursor", "Return the next cursor" }));
        Assert.That(places[DiagramPlaceKind.How].Nodes.All(node => node.Class == DiagramClass.Explained), "the explanation is a model's account, never more");
        Assert.That(places[DiagramPlaceKind.How].Apart, Is.EqualTo(new[] { ("What It Touches", "Not Read Yet") }), "the footprint is said, never drawn, until it is read");
        Assert.That(places[DiagramPlaceKind.LeftToDo].Nodes.Select(node => node.Words.Text), Is.EqualTo(new[] { "Document the cursor parameter in the API reference" }));
        var verdict = places[DiagramPlaceKind.Verdict].Nodes.Single();
        Assert.That((verdict.Words.Text, verdict.Class), Is.EqualTo(("3 files changed, verified", DiagramClass.Inferred)), "the verdict keeps Salidium's own class");
        Assert.That(diagram.Places.SelectMany(place => place.Nodes).Select(node => node.Words.Text), Has.None.Contain("Salidium").And.None.Contain("Seorak"));
    }

    [Test]
    public void AFileJoinsTheRunThatVerifiedItAsInferredAndOneNoCheckCoveredSaysSo()
    {
        var response = Recorded<UnderstandingResponse>("understanding", 7);
        var diagram = EvidenceDiagram.Of(Asked, response);
        var toRuns = diagram.Links.Where(link => link.To.StartsWith("run:")).ToList();
        Assert.That(toRuns.Select(link => link.From), Is.EquivalentTo(new[] { "file:test/orders.test.ts", "file:src/routes/orders-all.ts", "file:src/routes/orders.ts" }));
        Assert.That(toRuns.Select(link => link.Class), Has.All.EqualTo(DiagramClass.Inferred), "Salidium fixes coverage as inferred (schemas.ts:228)");
        Assert.That(diagram.Links.Where(link => link.From.StartsWith("how:")).Select(link => link.Class), Has.All.EqualTo(DiagramClass.Explained));

        ((AvailableUnderstanding)response.Result).Understanding.Changes.Files[0].Coverage.VerifiedAfter = false;
        var unchecked_ = EvidenceDiagram.Of(Asked, response);
        Assert.That(unchecked_.Places.Single(place => place.Kind == DiagramPlaceKind.Changes).Nodes[0].Lines.Last(), Is.EqualTo("Not Checked Since"));
        Assert.That(unchecked_.Links.Count(link => link.From == "file:test/orders.test.ts"), Is.Zero, "no line where no check covered it");
    }

    [Test]
    public void TheDiagramGrowsByTheNodesTheNewestReadBrought()
    {
        var early = EvidenceDiagram.Of(Asked, Recorded<UnderstandingResponse>("understanding", 3));
        var later = EvidenceDiagram.Of(Asked, Recorded<UnderstandingResponse>("understanding", 7));
        Assert.That(later.Sequence!.Value, Is.GreaterThan(early.Sequence!.Value), "Salidium's evidence sequence grows");
        var grown = later.NewSince(early);
        Assert.That(grown, Does.Contain("run:31 passed (make test SUITE=orders)"), "the check run arrived after the early read");
        Assert.That(grown, Has.None.EqualTo("asked"), "the ask was there from the start");
        Assert.That(later.NewSince(later), Is.Empty, "the same read grows nothing");
    }

    [Test]
    public void WithoutAnAnswerTheAskStandsWithTheHonestState()
    {
        var before = EvidenceDiagram.Of(Asked, null);
        Assert.That(before.Places.Select(place => place.Kind), Is.EqualTo(new[] { DiagramPlaceKind.Asked }));
        Assert.That((before.Without?.State, before.Without?.Why), Is.EqualTo(("No Conclusions", (string?)EvidencePage.NothingRead)));
        var refused = EvidenceDiagram.Of(null, new UnderstandingResponse
        {
            ExecutionId = "e1",
            Result = new UnauthorizedUnderstanding { Reason = new ErrorInfo { Code = "credential_rejected", Message = "developer words" } },
        });
        Assert.That((refused.Places.Count, refused.Without?.State), Is.EqualTo((0, (string?)"Conclusions Not Allowed")));
    }

    [Test]
    public void TheStripReadsInstantToWowAndOnlyWhatSeorakMeasured()
    {
        var strip = EvidenceStrip.Of(((AvailableEvaluation)Recorded<EvaluationResponse>("evaluation", 7).Result).Evaluation)!;
        Assert.That(strip.All.Select(stat => (stat.Head, stat.Figure, stat.Kind)), Is.EqualTo(new[]
        {
            ("Checks", "1 of 1 test passed", (string?)null), ("Cost", "$0.23", "Estimated"), ("Commits", "0", null), ("Errors", "0", null),
        }), "this run's Seorak gave no lines or activity: left out, never 0");
        Assert.That(strip.Notes, Is.Empty);
        Assert.That(strip.Shown(null).Select(stat => stat.Key), Is.EqualTo(new[] { "checks:test", "cost" }), "commits and errors never in the default set");

        var full = new Evaluation
        {
            Cost = new EvaluationCost { Availability = Available(), EstimatedUsd = 1.2 },
            Activity = new EvaluationActivity { Availability = Available(), ElapsedSeconds = 252, ToolCalls = 12 },
            Outcome = new EvaluationOutcome
            {
                Availability = new EvaluationAvailability { State = EvaluationAvailabilityState.Partial },
                Measure = new EvaluationOutcomeMeasure
                {
                    Uncommitted = new EvaluationUncommitted { FilesTouched = 3, LinesAdded = 140, LinesRemoved = 22 },
                    LineSurvival = new EvaluationLineSurvival { Fate = EvaluationLineSurvivalFate.Retained, Rate = 0.9, LinesAuthored = 140, LinesSurviving = 126, CommitsChecked = 4 },
                },
            },
            Verification = new EvaluationVerification { Availability = Available() },
        };
        var wide = EvidenceStrip.Of(full)!;
        Assert.That(wide.All.Select(stat => stat.Key), Is.EqualTo(new[] { "lines-added", "lines-removed", "time", "cost", "lines-kept", "tool-calls" }),
            "the lead's order: lines, checks, time and cost, then lines kept");
        Assert.That(wide.Shown(null).Select(stat => stat.Figure), Is.EqualTo(new[] { "140", "22", "4 min", "$1.20", "126 of 140" }), "the first five by default, AUDIT2's units");
        Assert.That(wide.Shown(new[] { "cost", "checks:lint", "time" }).Select(stat => stat.Key), Is.EqualTo(new[] { "cost", "time" }), "the person's order; a stat this run lacks is not drawn");
        Assert.That(wide.Notes, Is.EqualTo(new[] { "Partly Measured" }));
    }

    private static EvaluationAvailability Available() => new() { State = EvaluationAvailabilityState.Available };
}

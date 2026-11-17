using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// Salidium's understanding of a run as "What It Understood" reads it (the lead's 2026-11-17 20:15Z; AUDIT2's 20:19Z words;
/// AUDIT's references: Detroit's one finding, Death Stranding's cargo list): the verdict as the source said it, each method's run
/// with its own outcome, counts and exit, and the source's own counts, every one untouched and nothing guessed.
/// </summary>
public class RunUnderstandingTests
{
    private static UnderstandingVerificationRun Run(string label, UnderstandingVerificationRunOutcome outcome, long? passed, long? total, long? exit, bool stale = false) => new()
    {
        Label = label,
        Outcome = outcome,
        At = "2026-11-17T19:30:00.000Z",
        Counts = passed == null && total == null ? null : new UnderstandingVerificationRunCounts { Passed = passed, Total = total },
        Exit = new UnderstandingVerificationRunExit { Code = exit },
        Stale = stale,
        Epistemic = UnderstandingEpistemic.Observed,
    };

    private static Understanding Understood(UnderstandingEpistemic verdict = UnderstandingEpistemic.Observed) => new()
    {
        Verdict = new UnderstandingVerdict { Headline = "The check passes on the new parser.", Because = "go test ran green after the last change.", Epistemic = verdict },
        Verification = new UnderstandingVerification
        {
            Summary = "two methods",
            LatestByMethod = new List<UnderstandingVerificationRun>
            {
                Run("go test", UnderstandingVerificationRunOutcome.Pass, 41, 41, 0),
                Run("go vet", UnderstandingVerificationRunOutcome.Fail, null, null, 1, stale: true),
                Run("lint", UnderstandingVerificationRunOutcome.Unknown, 3, null, null),
            },
        },
        Changes = new UnderstandingChanges
        {
            Summary = "two files",
            Files = new List<UnderstandingChangedFile> { new() { Path = "encode.go" }, new() { Path = "encode_test.go" } },
            Commits = new List<UnderstandingCommit> { new() { Sha = "abc", At = "2026-11-17T19:20:00.000Z" } },
        },
        Review = new UnderstandingReview { Summary = "none open", Open = 0, Resolved = 2 },
        Remaining = new UnderstandingRemaining { Summary = "one", Items = new List<UnderstandingRemainingItem> { new() { Text = "Document the new option." } } },
    };

    [Test]
    public void TheVerdictIsTheSourcesOwnUnderItsClass()
    {
        var understood = RunUnderstanding.Of(Understood())!;
        Assert.That((understood.Source, understood.Headline, understood.Because),
            Is.EqualTo(("Salidium Observed", "The check passes on the new parser.", "go test ran green after the last change.")));
        Assert.That(RunUnderstanding.Of(Understood(UnderstandingEpistemic.Reported))!.Source, Is.EqualTo("Salidium Reported"));
        Assert.That(RunUnderstanding.Of(null), Is.Null);
    }

    [Test]
    public void EachRunSaysItsOwnOutcomeCountsAndExitAndNothingGuessed()
    {
        var runs = RunUnderstanding.Of(Understood())!.Sections.Single(section => section.Head == "Runs").Rows;
        Assert.That(runs.Select(row => (row.Label, row.Value, row.Mark)), Is.EqualTo(new[]
        {
            ("go test", "Passed", RunUnderstanding.Mark.Passed),
            ("go vet", "Failed", RunUnderstanding.Mark.Failed),
            ("lint", "Can't Tell", RunUnderstanding.Mark.CantTell),
        }));
        Assert.That(runs[0].Facts, Is.EqualTo(new[] { "41 of 41 Passed", "Exit 0" }));
        Assert.That(runs[1].Facts, Is.EqualTo(new[] { "Exit 1" }), "no counts given, none shown");
        Assert.That(runs[1].Note, Is.EqualTo("Out of Date"), "a stale run says so, quietly");
        Assert.That(runs[2].Facts, Is.EqualTo(new[] { "3 Passed" }), "only the count given, never a guessed total");
    }

    [Test]
    public void TheSourcesCountsAndItemsStandAsTheyAre()
    {
        var sections = RunUnderstanding.Of(Understood())!.Sections.ToDictionary(section => section.Head);
        Assert.That(sections["Changes"].Rows.Select(row => (row.Label, row.Value)), Is.EqualTo(new[] { ("Files", "2"), ("Commits", "1") }));
        Assert.That(sections["Review"].Rows.Select(row => (row.Label, row.Value)), Is.EqualTo(new[] { ("Open", "0"), ("Resolved", "2") }));
        Assert.That(sections["Remaining"].Rows.Select(row => row.Label), Is.EqualTo(new[] { "Document the new option." }));
    }
}

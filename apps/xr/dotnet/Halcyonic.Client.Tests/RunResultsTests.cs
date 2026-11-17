using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// Seorak's evaluation of a run as the Outcome door reads it (the lead's 2026-11-17 20:15Z; AUDIT2's 20:17Z words and rows): every
/// value is the source's own field, untouched; a part the source marks unavailable, and any null, is left out, never a 0.
/// </summary>
public class RunResultsTests
{
    private static EvaluationAvailability Available => new() { State = EvaluationAvailabilityState.Available };
    private static EvaluationAvailability Unavailable => new() { State = EvaluationAvailabilityState.Unavailable, Reason = EvaluationAvailabilityReason.NotCaptured };

    private static Evaluation Measured(bool activity = true, bool outcome = true, double? cost = 1.2, bool lens = true, double elapsed = 252) => new()
    {
        Cost = new EvaluationCost { Availability = cost == null ? Unavailable : Available, EstimatedUsd = cost },
        Activity = new EvaluationActivity
        {
            Availability = activity ? Available : Unavailable,
            StartedAt = "2026-11-17T19:00:00.000Z",
            ElapsedSeconds = elapsed,
            ToolCalls = 12,
            Prompts = 3,
        },
        Outcome = new EvaluationOutcome
        {
            Availability = outcome ? Available : Unavailable,
            Measure = new EvaluationOutcomeMeasure
            {
                CommitsLanded = 2,
                ErrorCount = null,
                Uncommitted = new EvaluationUncommitted { FilesTouched = 5, LinesAdded = 140, LinesRemoved = 22 },
                LineSurvival = new EvaluationLineSurvival
                {
                    Fate = EvaluationLineSurvivalFate.Retained, Rate = 0.9, LinesAuthored = 140, LinesSurviving = 126, CommitsChecked = 4,
                },
            },
        },
        Verification = new EvaluationVerification
        {
            Availability = Available,
            Lens = lens
                ? new EvaluationVerificationLens
                {
                    ByKind = new List<EvaluationVerificationKind>
                    {
                        new() { Label = "Tests", Runs = 4, Passed = 3, PassRate = 0.75 },
                        new() { Label = "Lint", Runs = 2, Passed = 2, PassRate = 1 },
                    },
                }
                : new EvaluationVerificationLens { ByKind = new List<EvaluationVerificationKind>(), EmptyReason = "No checks ran in this window." },
        },
    };

    private static (string Head, (string Label, string Value, string? Detail)[] Rows)[] Laid(RunResults results) =>
        results.Sections.Select(section => (section.Head, section.Rows.Select(row => (row.Label, row.Value, row.Detail)).ToArray())).ToArray();

    [Test]
    public void EveryValueIsTheSourcesOwnFieldInItsSection()
    {
        var laid = Laid(RunResults.Of(Measured())!);
        Assert.That(laid.Select(section => section.Head), Is.EqualTo(new[] { "Activity", "Changes", "Checks", "Cost" }));
        Assert.That(laid[0].Rows, Is.EqualTo(new (string, string, string?)[] { ("Time", "4 min 12 s", null), ("Tool Calls", "12", null), ("Instructions", "3", null) }));
        Assert.That(laid[1].Rows, Is.EqualTo(new (string, string, string?)[]
        {
            ("Files Changed", "5", null), ("Lines Added", "140", null), ("Lines Removed", "22", null), ("Commits", "2", null),
            ("Lines Kept", "126 of 140 Kept", "4 Commits Checked"),
        }), "lines added and removed never netted; a null error count left out, never a 0");
        Assert.That(laid[2].Rows, Is.EqualTo(new (string, string, string?)[] { ("Tests", "3 of 4 Passed", null), ("Lint", "2 of 2 Passed", null) }));
        Assert.That(laid[3].Rows, Is.EqualTo(new (string, string, string?)[] { ("Estimated", "$1.20", null) }), "the cost is always marked estimated");
        Assert.That(RunResults.Of(Measured())!.Sections[2].Rows.Select(row => row.Rate), Is.EqualTo(new double?[] { 0.75, 1 }), "each check kind carries its own rate for its bar");
    }

    [Test]
    public void ErrorsStandUnderActivityNotChanges()
    {
        var measured = Measured();
        measured.Outcome.Measure!.ErrorCount = 4;
        var laid = Laid(RunResults.Of(measured)!);
        Assert.That(laid[0].Rows.Last(), Is.EqualTo(("Errors", "4", (string?)null)), "an error count is not a change (AUDIT2's 21:20Z)");
        Assert.That(laid[1].Rows.Select(row => row.Label), Has.None.EqualTo("Errors"));
        measured.Activity = null!;
        Assert.That(Laid(RunResults.Of(measured)!)[0], Is.EqualTo(("Activity", new (string, string, string?)[] { ("Errors", "4", null) })),
            "Activity shows only the rows measured");
    }

    [Test]
    public void UnderAMinuteTheTimeIsSecondsAlone()
    {
        Assert.That(RunResults.Of(Measured(elapsed: 48))!.Sections[0].Rows[0].Value, Is.EqualTo("48 s"));
    }

    [Test]
    public void AnEmptyLensSaysItsOwnReasonAndAnUnmeasuredPartIsLeftOut()
    {
        var empty = RunResults.Of(Measured(lens: false))!;
        Assert.That(empty.Sections.Single(section => section.Head == "Checks").Empty, Is.EqualTo("No checks ran in this window."));
        var bare = Laid(RunResults.Of(Measured(activity: false, outcome: false, cost: null))!);
        Assert.That(bare.Select(section => section.Head), Is.EqualTo(new[] { "Checks" }), "no activity, no outcome, no cost: no rows, never zeros");
        Assert.That(RunResults.Of(null), Is.Null);
    }
}

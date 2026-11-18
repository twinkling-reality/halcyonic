using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// Choosing the evidence strip's stats (<see cref="EvidenceChoice"/>): one row a kind in the strip's own order, each chosen,
/// open or not measured; a press joins or leaves the choice; and the choice kept on the headset between runs.
/// </summary>
public class EvidenceChoiceTests
{
    private sealed class Preferences : IDevicePreferences
    {
        public Dictionary<string, string> Kept { get; } = new();

        public string? Get(string key) => Kept.TryGetValue(key, out var value) ? value : null;

        public void Set(string key, string? value)
        {
            if (value == null) Kept.Remove(key);
            else Kept[key] = value;
        }
    }

    private static EvaluationAvailability Available() => new() { State = EvaluationAvailabilityState.Available };

    /// <summary>Lines, two kinds of check, time, cost and tool calls; no lines kept, commits or errors.</summary>
    private static EvidenceStrip Strip(bool checks = true) => EvidenceStrip.Of(new Evaluation
    {
        Cost = new EvaluationCost { Availability = Available(), EstimatedUsd = 1.2 },
        Activity = new EvaluationActivity { Availability = Available(), ElapsedSeconds = 252, ToolCalls = 12 },
        Outcome = new EvaluationOutcome
        {
            Availability = Available(),
            Measure = new EvaluationOutcomeMeasure { Uncommitted = new EvaluationUncommitted { FilesTouched = 3, LinesAdded = 140, LinesRemoved = 22 } },
        },
        Verification = new EvaluationVerification
        {
            Availability = Available(),
            Lens = checks
                ? new EvaluationVerificationLens
                {
                    ByKind = new List<EvaluationVerificationKind>
                    {
                        new() { Label = "test", Runs = 4, Passed = 4, PassRate = 1 },
                        new() { Label = "lint", Runs = 1, Passed = 1, PassRate = 1 },
                    },
                }
                : null,
        },
    })!;

    private static IEnumerable<(string Key, EvidenceChoiceState State)> Read(IEnumerable<EvidenceChoiceRow> rows) => rows.Select(row => (row.Key, row.State));

    [Test]
    public void RowsFollowTheStripsOrderAndTheDefaultChoice()
    {
        var rows = EvidenceChoice.Rows(Strip(), null);
        Assert.That(Read(rows), Is.EqualTo(new[]
        {
            ("lines-added", EvidenceChoiceState.Chosen), ("lines-removed", EvidenceChoiceState.Chosen), (EvidenceChoice.ChecksKey, EvidenceChoiceState.Chosen),
            ("time", EvidenceChoiceState.Chosen), ("cost", EvidenceChoiceState.Open),
            ("lines-kept", EvidenceChoiceState.NotMeasured), ("tool-calls", EvidenceChoiceState.Open), ("commits", EvidenceChoiceState.NotMeasured),
            ("errors", EvidenceChoiceState.NotMeasured),
        }), "no choice made: the strip's first five stand");
        Assert.That(rows.Select(row => row.Head), Is.EqualTo(new[]
        {
            EvidenceStrip.Words.LinesAdded, EvidenceStrip.Words.LinesRemoved, EvidenceStrip.Words.Checks, EvidenceStrip.Words.Time,
            EvidenceStrip.Words.Cost, EvidenceStrip.Words.LinesKept, EvidenceStrip.Words.ToolCalls, EvidenceStrip.Words.Commits, EvidenceStrip.Words.Errors,
        }), "the strip's own words, two kinds of check one row");
        Assert.That(rows.Select(row => row.Kind), Is.EqualTo(new[]
        {
            null, null, null, null, null, "Not Measured", null, "Not Measured", "Not Measured",
        }), "a check row's head alone stands; a kind the read lacks says so");
    }

    [Test]
    public void AnExplicitChoiceDecidesWhatIsChosen()
    {
        var rows = EvidenceChoice.Rows(Strip(), new[] { "cost", "checks:lint", "lines-kept" });
        Assert.That(rows.Where(row => row.State == EvidenceChoiceState.Chosen).Select(row => row.Key), Is.EqualTo(new[] { EvidenceChoice.ChecksKey, "cost" }),
            "chosen where the read holds it, in the strip's order; one kind of check chosen chooses the row");
        Assert.That(rows.Single(row => row.Key == "lines-kept").State, Is.EqualTo(EvidenceChoiceState.NotMeasured), "a chosen kind this run lacks is still not measured");
        Assert.That(rows.Single(row => row.Key == "lines-added").State, Is.EqualTo(EvidenceChoiceState.Open));
        Assert.That(EvidenceChoice.Rows(Strip(), new string[0]).Count(row => row.State == EvidenceChoiceState.Chosen), Is.Zero, "an empty choice chooses nothing");
    }

    [Test]
    public void ChecksWithNoLabelsAreOneRowNotMeasured()
    {
        var rows = EvidenceChoice.Rows(Strip(checks: false), null);
        var checks = rows.Where(row => row.Head == EvidenceStrip.Words.Checks).ToList();
        Assert.That(checks.Count, Is.EqualTo(1));
        Assert.That((checks[0].State, checks[0].Kind), Is.EqualTo((EvidenceChoiceState.NotMeasured, (string?)EvidenceChoice.Words.NotMeasured)));
        Assert.That(rows.Select(row => row.Key).Take(4), Is.EqualTo(new[] { "lines-added", "lines-removed", EvidenceChoice.ChecksKey, "time" }), "in the strip's order");
    }

    [Test]
    public void WithNoStripEveryKindIsNotMeasured()
    {
        var rows = EvidenceChoice.Rows(null, new[] { "cost" });
        Assert.That(rows.Select(row => row.Head), Is.EqualTo(new[]
        {
            EvidenceStrip.Words.LinesAdded, EvidenceStrip.Words.LinesRemoved, EvidenceStrip.Words.Checks, EvidenceStrip.Words.Time, EvidenceStrip.Words.Cost,
            EvidenceStrip.Words.LinesKept, EvidenceStrip.Words.ToolCalls, EvidenceStrip.Words.Commits, EvidenceStrip.Words.Errors,
        }));
        Assert.That(rows.All(row => row.State == EvidenceChoiceState.NotMeasured && row.Kind == EvidenceChoice.Words.NotMeasured), Is.True);
    }

    [Test]
    public void ToggleJoinsAtTheEndAndLeaves()
    {
        var strip = Strip();
        Assert.That(EvidenceChoice.Toggle(new[] { "time", "lines-added" }, "cost", strip), Is.EqualTo(new[] { "time", "lines-added", "cost" }), "an open stat joins at the end");
        Assert.That(EvidenceChoice.Toggle(new[] { "time", "cost", "lines-added" }, "cost", strip), Is.EqualTo(new[] { "time", "lines-added" }), "a chosen stat leaves");
        Assert.That(EvidenceChoice.Toggle(new[] { "lines-kept", "time" }, "time", strip), Is.EqualTo(new[] { "lines-kept" }),
            "a kept kind this run lacks stays chosen for the runs that have it");
    }

    [Test]
    public void ToggleOfAKindTheReadLacksChangesNothing()
    {
        var strip = Strip();
        Assert.That(EvidenceChoice.Toggle(new[] { "time" }, "commits", strip), Is.EqualTo(new[] { "time" }));
        Assert.That(EvidenceChoice.Toggle(new[] { "time" }, "checks:build", strip), Is.EqualTo(new[] { "time" }));
        Assert.That(EvidenceChoice.Toggle(new[] { "time" }, "checks:test", strip), Is.EqualTo(new[] { "time" }), "one kind of check is no row of its own");
        Assert.That(EvidenceChoice.Toggle(new[] { "time" }, EvidenceChoice.ChecksKey, Strip(checks: false)), Is.EqualTo(new[] { "time" }), "no check measured");
        Assert.That(EvidenceChoice.Toggle(null, "commits", strip), Is.EqualTo(new[] { "lines-added", "lines-removed", "checks:test", "checks:lint", "time" }));
    }

    [Test]
    public void ToggleFromNoChoiceStartsFromTheDefault()
    {
        var strip = Strip();
        Assert.That(EvidenceChoice.Toggle(null, "cost", strip), Is.EqualTo(new[] { "lines-added", "lines-removed", "checks:test", "checks:lint", "time", "cost" }));
        Assert.That(EvidenceChoice.Toggle(null, EvidenceChoice.ChecksKey, strip), Is.EqualTo(new[] { "lines-added", "lines-removed", "time" }));
    }

    [Test]
    public void TheChecksRowChoosesEveryKindOfCheckTogether()
    {
        var strip = Strip();
        var rows = EvidenceChoice.Rows(strip, new[] { "time" });
        Assert.That(rows.Count(row => row.Head == EvidenceStrip.Words.Checks), Is.EqualTo(1), "two kinds of check are one row");
        Assert.That(rows.Any(row => row.Key.StartsWith(EvidenceChoice.ChecksPrefix)), Is.False, "no row is one kind of check");
        Assert.That(rows.Single(row => row.Key == EvidenceChoice.ChecksKey).State, Is.EqualTo(EvidenceChoiceState.Open));

        var joined = EvidenceChoice.Toggle(new[] { "time", "cost" }, EvidenceChoice.ChecksKey, strip);
        Assert.That(joined, Is.EqualTo(new[] { "time", "cost", "checks:test", "checks:lint" }), "both join at the end, in the strip's order");
        Assert.That(strip.Shown(joined).Select(stat => stat.Key), Is.EqualTo(joined), "the strip understands the kept keys");
        Assert.That(EvidenceChoice.Rows(strip, joined).Single(row => row.Key == EvidenceChoice.ChecksKey).State, Is.EqualTo(EvidenceChoiceState.Chosen));
        Assert.That(EvidenceChoice.Toggle(joined, EvidenceChoice.ChecksKey, strip), Is.EqualTo(new[] { "time", "cost" }), "both leave");

        Assert.That(EvidenceChoice.Rows(strip, new[] { "checks:lint" }).Single(row => row.Key == EvidenceChoice.ChecksKey).State, Is.EqualTo(EvidenceChoiceState.Chosen),
            "any kind of check chosen chooses the row");
        Assert.That(EvidenceChoice.Toggle(new[] { "time", "checks:lint", "cost" }, EvidenceChoice.ChecksKey, strip), Is.EqualTo(new[] { "time", "cost" }), "every kind leaves");
        Assert.That(EvidenceChoice.Toggle(new[] { "time", "checks:build", "cost" }, EvidenceChoice.ChecksKey, strip),
            Is.EqualTo(new[] { "time", "checks:test", "checks:lint", "checks:build", "cost" }), "a kind another run measured keeps its place: the run's join at the first");
        Assert.That(EvidenceChoice.Rows(strip, new[] { "checks:build" }).Single(row => row.Key == EvidenceChoice.ChecksKey).State, Is.EqualTo(EvidenceChoiceState.Open),
            "a kind this run lacks does not choose the row");
    }

    [Test]
    public void TheChoiceIsKeptOnTheHeadset()
    {
        var prefs = new Preferences();
        Assert.That(EvidenceChoice.Load(prefs), Is.Null, "nothing kept: no choice");
        EvidenceChoice.Save(prefs, new[] { "cost", "checks:lint", "time" });
        Assert.That(prefs.Kept.Keys, Is.EqualTo(new[] { EvidenceChoice.PreferenceKey }));
        Assert.That(EvidenceChoice.Load(prefs), Is.EqualTo(new[] { "cost", "checks:lint", "time" }));
        EvidenceChoice.Save(prefs, new string[0]);
        Assert.That(EvidenceChoice.Load(prefs), Is.Empty, "choosing nothing is a choice, never the default");
    }
}

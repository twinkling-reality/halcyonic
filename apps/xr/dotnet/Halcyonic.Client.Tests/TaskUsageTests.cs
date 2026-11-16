using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// What a task used, under its steps (the lead's ruling of 2026-11-16 03:22Z; LINK's rules from the Mac's Usage page):
/// the agent app's own count, unknown said and never 0, "At least this much" where short, a cost only where given.
/// </summary>
public class TaskUsageTests
{
    private static RunUsage Usage(long? input, long? cacheRead, long? cacheWrite, long? output, long? reasoning = null, long turns = 1, long ended = 1, double? usd = null) => new()
    {
        Turns = turns,
        TurnsEnded = ended,
        Tokens = new UsageTokens { Input = input, CacheRead = cacheRead, CacheWrite = cacheWrite, Output = output, Reasoning = reasoning },
        Cost = usd is double d ? new UsageEstimate { EstimatedUsd = d } : null,
    };

    [Test]
    public void TokensAreTheFourPartsAddedWithReasoningInsideOutput()
    {
        var figure = TaskUsage.Tokens(Usage(200_000, 10_000, 500, 200, reasoning: 150), true);
        Assert.That((figure.Label, figure.Value, figure.Known, figure.Notes), Is.EqualTo((TaskUsage.TokensUsed, "210,700", true, new string[0])));
    }

    [Test]
    public void UnknownIsSaidNeverZero()
    {
        Assert.That(TaskUsage.Tokens(null, true).Value, Is.EqualTo(TaskUsage.NotReportedYet));
        Assert.That(TaskUsage.Tokens(null, null).Value, Is.EqualTo(TaskUsage.NotReportedByThisApp), "an app that does not say it reports is not counted as reporting");
        Assert.That(TaskUsage.Tokens(null, false).Value, Is.EqualTo(TaskUsage.NotReportedByThisApp));
        var none = TaskUsage.Tokens(Usage(null, null, null, null), true);
        Assert.That((none.Value, none.Known), Is.EqualTo((TaskUsage.NotKnown, false)));
    }

    [Test]
    public void AFigureShortOfTheWholeSaysAtLeastAndWhy()
    {
        var parts = TaskUsage.Tokens(Usage(1_000, null, null, 50), true);
        Assert.That((parts.Value, parts.Notes), Is.EqualTo(("1,050", new[] { TaskUsage.AtLeast, TaskUsage.PartsMissing })));
        var turns = TaskUsage.Tokens(Usage(1_000, 0, 0, 50, turns: 2, ended: 3), true);
        Assert.That(turns.Notes, Is.EqualTo(new[] { TaskUsage.AtLeast, "2 of 3 turns reported" }));
    }

    [Test]
    public void CostStandsOnlyWhereTheAgentGaveOne()
    {
        Assert.That(TaskUsage.CostOf(Usage(1, 0, 0, 1)), Is.Null, "no estimate, no line: never $0");
        Assert.That(TaskUsage.CostOf(null), Is.Null);
        var cost = TaskUsage.CostOf(Usage(1, 0, 0, 1, usd: 0.4249))!;
        Assert.That((cost.Label, cost.Value, cost.Notes), Is.EqualTo((TaskUsage.Cost, "$0.42", new[] { TaskUsage.AgentsCount })));
        Assert.That(TaskUsage.CostOf(Usage(1, 0, 0, 1, turns: 1, ended: 2, usd: 1.5))!.Notes, Is.EqualTo(new[] { TaskUsage.AtLeast, TaskUsage.AgentsCount }));
    }

    [Test]
    public void MoneyUnderACentKeepsItsFirstFigures()
    {
        Assert.That(TaskUsage.Money(0.0042), Is.EqualTo("$0.0042"));
        Assert.That(TaskUsage.Money(0.00456), Is.EqualTo("$0.0046"));
        Assert.That(TaskUsage.Money(0), Is.EqualTo("$0.00"), "a given zero is said as given");
        Assert.That(TaskUsage.Money(1234.5), Is.EqualTo("$1,234.50"));
    }
}

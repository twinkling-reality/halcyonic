using System;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

[TestFixture]
public class UsageColumnTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-30T19:20:00Z");

    /// <summary>Four windows, as the recording might hold them.</summary>
    private const string Limits = """
        {
          "availability": "available",
          "source": { "system": "seorak", "synthetic": false, "api_version": "v1" },
          "complete": true,
          "readings": [
            { "agent": "claude", "label": "Claude Code", "window": "rolling-5h", "used_percent": 40.2,
              "resets_at": "2026-09-30T21:05:00.000Z", "observed_at": "2026-09-30T19:08:00.000Z",
              "freshness": "fresh", "account": { "state": "unidentified" } },
            { "agent": "claude", "label": "Claude Code", "window": "weekly", "used_percent": 15.1,
              "resets_at": "2026-10-06T09:00:00.000Z", "observed_at": "2026-09-30T19:18:00.000Z",
              "freshness": "fresh", "account": { "state": "unidentified" } },
            { "agent": "codex", "label": "Codex", "window": "rolling-5h", "used_percent": 97.2,
              "resets_at": "2026-09-30T21:05:00.000Z", "observed_at": "2026-09-30T19:08:00.000Z",
              "freshness": "fresh", "account": { "state": "unidentified" } },
            { "agent": "codex", "label": "Codex", "window": "weekly", "used_percent": 60.4,
              "resets_at": "2026-10-06T09:00:00.000Z", "observed_at": "2026-09-30T19:18:00.000Z",
              "freshness": "fresh", "account": { "state": "unidentified" } }
          ]
        }
        """;

    private static AvailableUsageLimits Recorded => (AvailableUsageLimits)Json.AssertRoundTrips<UsageLimitsResponse>(Limits);

    private static (UsageColumn Usage, FakeMenuHost Host) Demonstration(AvailableUsageLimits? limits)
    {
        var host = new FakeMenuHost { Demonstration = true, Connected = false, Clock = Now };
        return (new UsageColumn(host, _ => limits), host);
    }

    [Test]
    public void EachLimitIsARowWithItsShareLeftInWordsAndAChevron()
    {
        var (usage, _) = Demonstration(Recorded);
        var frame = usage.Frame!;
        Assert.That(frame.Subject, Is.EqualTo("How much is left before each limit?"));
        Assert.That(frame.Lines.Select(line => (line.Words, line.Fact)), Is.EqualTo(new[]
        {
            ("Claude Code, 5-hour window", "At most 60% left"),
            ("Claude Code, weekly", "At most 85% left"),
            ("Codex, 5-hour window", "At most 3% left"),
        }), "a source line counts as one of the page's rows: 3 a page");
        Assert.That(frame.Lines.All(line => line.Opens && line.WordsAreData && line.Icon == null), Is.True, "no icons, so the words start on the content line");
        Assert.That(frame.Source, Is.EqualTo(UsageLeftPresenter.Recorded));
        Assert.That(frame.Footer[PromptSlot.Rare], Is.Null, "no Refresh in the demonstration");
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Next page"));
    }

    [Test]
    public void AChosenLimitSaysWhenItWasSeenWhenItResetsAndWhoseAccountItIs()
    {
        var (usage, _) = Demonstration(Recorded);
        usage.Act(UsageColumn.OpenLimit, "0");
        var frame = usage.Frame!;
        Assert.That(frame.Lines[0].Chosen, Is.True);
        Assert.That(frame.Side!.Subject, Is.EqualTo("Claude Code, 5-hour window"));
        Assert.That(frame.Side.Facts.Select(fact => (fact.Name, fact.Value)), Is.EqualTo(new[]
        {
            ("Seen", "Today at 19:08"),
            ("Resets", "Today at 21:05"),
            ("Account", "Part of the recording"),
        }));
        Assert.That(frame.Footer[PromptSlot.FarRight], Is.Null, "paging waits while a row is chosen");
        usage.Act(SidePanel.Close, null);
        Assert.That(usage.Frame!.Side, Is.Null, "Close details");
        Assert.That(usage.Frame!.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Next page"));
    }

    [Test]
    public void APageItAskedForPagesAndAChosenRowKeepsItsPage()
    {
        var (usage, _) = Demonstration(Recorded);
        usage.Act(Footer.NextPage, null);
        Assert.That(usage.Frame!.Lines.Single().Words, Is.EqualTo("Codex, weekly"));
        Assert.That(usage.Frame!.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("First page"));
        usage.Act(UsageColumn.OpenLimit, "3");
        Assert.That(usage.Frame!.Side!.Subject, Is.EqualTo("Codex, weekly"));
        usage.Act(UsageColumn.OpenLimit, "9");
        Assert.That(usage.Frame!.Side!.Subject, Is.EqualTo("Codex, weekly"), "a key past the list does nothing");
    }

    [Test]
    public void WithoutLimitsItSaysWhyAndNeverSends()
    {
        var (usage, host) = Demonstration(null);
        Assert.That(usage.Frame!.Lines.Single().Words, Is.EqualTo(UsageLeftPresenter.NotInDemo));
        var outside = new FakeMenuHost { Clock = Now };
        var unreachable = new UsageColumn(outside, _ => null);
        var frame = unreachable.Frame!;
        Assert.That(frame.Lines.Single().Tone, Is.EqualTo(LineTone.Problem), "no computer to ask");
        Assert.That(frame.Footer[PromptSlot.Rare]!.Words, Is.EqualTo(WorkspaceText.Refresh), "Refresh tries again");
        Assert.That(host.Sent.Concat(outside.Sent), Is.Empty, "Usage only reads");
        var closed = false;
        unreachable.Closed += () => closed = true;
        unreachable.Act(Footer.Close, null);
        Assert.That(closed, Is.True);
    }
}

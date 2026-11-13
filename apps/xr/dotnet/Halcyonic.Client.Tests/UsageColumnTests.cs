using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>Two windows of one agent app, as the redesign's usage card holds them.</summary>
    private const string TwoWindows = """
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
              "freshness": "fresh", "account": { "state": "unidentified" } }
          ]
        }
        """;

    private static AvailableUsageLimits Recorded => (AvailableUsageLimits)Json.AssertRoundTrips<UsageLimitsResponse>(Limits);

    private static (UsageColumn Usage, FakeMenuHost Host) Demonstration(AvailableUsageLimits? limits)
    {
        var host = new FakeMenuHost { Demonstration = true, Connected = false, Clock = Now, Height = MenuPage.Rows(6) };
        return (new UsageColumn(host, _ => limits), host);
    }

    /// <summary>
    /// The menu under the redesign (the addendum's A4 and A15): the usage card with its windows as wells, and the column
    /// Settings, Compact, Close (no Refresh in a recording), Compact never main; more windows than the card holds, and the
    /// older view draws the page. Known by the chosen section Usage, never by its title.
    /// </summary>
    [Test]
    public void TheRedesignsMenuIsTheUsageCardWithSettingsCompactAndClose()
    {
        var was = RoomDesign.Enabled;
        RoomDesign.Enabled = true;
        try
        {
            var two = (AvailableUsageLimits)Json.AssertRoundTrips<UsageLimitsResponse>(TwoWindows);
            var (usage, _) = Demonstration(two);
            var sections = new MenuBar(MenuPlace.Usage, "Nothing Waiting").Sections();
            var bar = RoomBar.OfFrame(usage.Frame!.WithSections(sections, sections.Count), VoiceStage.Idle)!;
            Assert.That(bar, Is.Not.Null, "the redesign draws the menu");
            Assert.That(bar.Card!.Gauges.Count, Is.EqualTo(2));
            Assert.That(bar.Column.Select(action => (action.Control.Action, action.Control.Key)), Is.EqualTo(new[]
            {
                (MenuFrame.ChooseSection, (string?)nameof(MenuPlace.Settings)), (UsageColumn.Compact, null), (Footer.Close, null),
            }));
            Assert.That(bar.Column.Any(action => action.Main), Is.False, "Compact is never main");

            var (none, _) = Demonstration(null);
            var empty = RoomBar.OfFrame(none.Frame!.WithSections(sections, sections.Count), VoiceStage.Idle)!;
            Assert.That((empty.Card!.Gauges.Count, empty.Card.Description?.Text), Is.EqualTo((0, none.Frame!.Lines.Single().Words)), "the panel's one line, the product's own");

            var (four, _) = Demonstration(Recorded);
            Assert.That(RoomBar.OfFrame(four.Frame!.WithSections(sections, sections.Count), VoiceStage.Idle), Is.Null, "four windows: the older view, until the card holds them");
        }
        finally { RoomDesign.Enabled = was; }
    }

    [Test]
    public void EachLimitIsARowWithItsShareLeftInWordsAndAChevron()
    {
        var (usage, _) = Demonstration(Recorded);
        var frame = usage.Frame!;
        Assert.That(frame.Subject, Is.EqualTo("Usage"));
        Assert.That(frame.Lines.Select(line => (line.Words, line.Fact)), Is.EqualTo(new[]
        {
            ("Claude Code · 5-Hour Window", "Up to 60% Left"),
            ("Claude Code · Weekly", "Up to 85% Left"),
            ("Codex · 5-Hour Window", "Up to 3% Left"),
        }), "a source line counts as one of the page's rows: 3 a page");
        Assert.That(frame.Lines.All(line => line.Opens && line.WordsAreData && line.Icon == null), Is.True, "no icons, so the words start on the content line");
        Assert.That(frame.Source, Is.EqualTo(UsageLeftPresenter.Recorded));
        Assert.That(frame.Footer[PromptSlot.Rare], Is.Null, "no Refresh in the demonstration");
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Next Page"));
    }

    [Test]
    public void AChosenLimitSaysWhenItWasSeenWhenItResetsAndWhoseAccountItIs()
    {
        var (usage, _) = Demonstration(Recorded);
        usage.Act(UsageColumn.OpenLimit, "0");
        var frame = usage.Frame!;
        Assert.That(frame.Lines[0].Chosen, Is.True);
        Assert.That(frame.Side!.Subject, Is.EqualTo("Claude Code · 5-Hour Window"));
        Assert.That(frame.Side.Facts.Select(fact => (fact.Name, fact.Value)), Is.EqualTo(new[]
        {
            ("Agent App", "claude"),
            ("Seen", "Today 19:08"),
            ("Resets", "Today 21:05"),
            ("Account", "Part of Recording"),
        }));
        Assert.That(frame.Footer[PromptSlot.FarRight], Is.Null, "paging waits while a row is chosen");
        usage.Act(SidePanel.Close, null);
        Assert.That(usage.Frame!.Side, Is.Null, "Close Details");
        Assert.That(usage.Frame!.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Next Page"));
    }

    [Test]
    public void APageItAskedForPagesAndAChosenRowKeepsItsPage()
    {
        var (usage, _) = Demonstration(Recorded);
        usage.Act(Footer.NextPage, null);
        Assert.That(usage.Frame!.Lines.Single().Words, Is.EqualTo("Codex · Weekly"));
        Assert.That(usage.Frame!.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("First Page"));
        usage.Act(UsageColumn.OpenLimit, "3");
        Assert.That(usage.Frame!.Side!.Subject, Is.EqualTo("Codex · Weekly"));
        usage.Act(UsageColumn.OpenLimit, "9");
        Assert.That(usage.Frame!.Side!.Subject, Is.EqualTo("Codex · Weekly"), "a key past the list does nothing");
    }

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void MeasuredHeadingAndSourceLeaveEveryLimitReachable(TextSize text)
    {
        var (usage, host) = Demonstration(Recorded);
        host.TextSize = text;
        host.MeasureTitle = (subject, width) =>
        {
            if (width == Glaze.Menu.MenuColumnDegrees) Assert.That(subject, Is.EqualTo(UsageText.Subject));
            else Assert.That(width, Is.EqualTo(Glaze.Menu.SideColumnDegrees));
            return 2;
        };
        host.MeasurePage = (subjectRows, besideMenu) =>
        {
            Assert.That(subjectRows, Is.EqualTo(2), "the narrowed heading's actual rows affect page room");
            Assert.That(besideMenu, Is.True);
            return MenuPage.Rows(1) + MenuPage.SourceLine + MenuPage.TargetGap / 2f;
        };
        var keys = new System.Collections.Generic.List<string>();
        for (var part = 0; part < 4; part++)
        {
            var frame = usage.Frame!;
            var line = frame.Lines.Single();
            keys.Add(line.Key!);
            Assert.That(frame.Source, Is.EqualTo(UsageLeftPresenter.Recorded));
            Assert.That(frame.Footer[PromptSlot.FarRight], Is.Not.Null, "each limit remains offered through the pager");
            usage.Act(UsageColumn.OpenLimit, line.Key);
            var opened = usage.Frame!;
            Assert.That(opened.Side!.Subject, Is.EqualTo(line.Words));
            var allFacts = DetailFacts(usage);
            Assert.That(allFacts.Single(fact => fact.Name == UsageText.Account).Value, Is.EqualTo(UsageText.PartOfTheRecording));
            Assert.That(allFacts.Select(fact => fact.Name), Is.EqualTo(new[] { UsageText.AgentApp, UsageText.Seen, UsageText.Resets, UsageText.Account }));
            Assert.That(usage.Frame!.Lines.Single().Key, Is.EqualTo(line.Key), "detail paging never advances the limit list");
            usage.Act(SidePanel.Close, null);
            usage.Act(Footer.NextPage, null);
        }
        Assert.That(keys, Is.EqualTo(new[] { "0", "1", "2", "3" }));
        Assert.That(usage.Frame!.Lines.Single().Key, Is.EqualTo("0"), "the offered pager returns to the first page");
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void ResizingKeepsTheChosenGlobalLimitAndItsEvidence()
    {
        var (usage, host) = Demonstration(Recorded);
        usage.Act(UsageColumn.OpenLimit, "3");
        var before = usage.Frame!.Side!;
        host.TextSize = TextSize.Larger;
        host.MeasureTitle = (_, _) => 2;
        host.MeasurePage = (_, _) => MenuPage.Rows(1) + MenuPage.SourceLine;
        var frame = usage.Frame!;
        Assert.That(frame.Lines.Single().Key, Is.EqualTo("3"));
        Assert.That(frame.Lines.Single().Chosen, Is.True);
        Assert.That(frame.Side!.SubjectRuns.Runs, Is.EqualTo(before.SubjectRuns.Runs));
        Assert.That(DetailFacts(usage).Select(fact => (fact.Name, fact.Value)), Is.EqualTo(before.Facts.Select(fact => (fact.Name, fact.Value))));
        Assert.That(frame.Side.Source, Is.EqualTo(before.Source));
        Assert.That(frame.Side.Parts!.Value.Parts, Is.GreaterThan(1), "the larger detail offers complete evidence through existing side-panel parts");
        usage.Act(SidePanel.Close, null);
        Assert.That(usage.Frame!.Lines.Single().Key, Is.EqualTo("3"));
        usage.Act(Footer.NextPage, null);
        Assert.That(usage.Frame!.Lines.Single().Key, Is.EqualTo("0"));
    }

    private static SideFact[] DetailFacts(UsageColumn usage)
    {
        var facts = new System.Collections.Generic.List<SideFact>();
        var first = usage.Frame!;
        var count = first.Side!.Parts?.Parts ?? 1;
        var key = first.Lines.Single(line => line.Chosen).Key;
        for (var part = 0; part < count; part++)
        {
            var frame = usage.Frame!;
            Assert.That(frame.Side!.Source, Is.EqualTo(first.Side.Source), "each part retains the exact source");
            Assert.That(frame.Lines.Single(line => line.Chosen).Key, Is.EqualTo(key));
            facts.AddRange(frame.Side.Facts);
            if (count > 1)
            {
                Assert.That(frame.Side.Parts, Is.EqualTo((part, count)));
                Assert.That(frame.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(Footer.NextPage));
                Assert.That(frame.Footer.InPlace(frame.Side)[PromptSlot.FarRight]!.Id, Is.EqualTo(Footer.NextPage));
                usage.Act(Footer.NextPage, null);
            }
        }
        return facts.ToArray();
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
    /// <summary>The computer answering the column's two reads: the limits, and where the assistant runs.</summary>
    private sealed class TwoReads : HttpMessageHandler
    {
        private readonly string? agent;

        /// <param name="agent">GET /api/workspace/agent's body, or null for a read that fails.</param>
        public TwoReads(string? agent) => this.agent = agent;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var (status, body) = path.EndsWith("/api/usage-limits", StringComparison.Ordinal) ? (HttpStatusCode.OK, TwoWindows)
                : path.EndsWith("/api/workspace/agent", StringComparison.Ordinal) && agent != null ? (HttpStatusCode.OK, agent)
                : (HttpStatusCode.InternalServerError, "{}");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    /// <summary>The live column under the redesign, both reads taken; its card.</summary>
    private static async Task<RoomCard> LiveCard(string? agent)
    {
        using var api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", new TwoReads(agent));
        var host = new FakeMenuHost { Clock = Now, Height = MenuPage.Rows(6), Api = api };
        var usage = new UsageColumn(host, _ => null);
        for (var tries = 0; tries < 200 && (usage.Frame?.Card?.Gauges.Count ?? 0) == 0; tries++)
        {
            await Task.Delay(10);
            usage.Tick();
        }
        // The agent's read may land after the limits: tick until it has, or long enough that a failed read has been taken.
        for (var tries = 0; tries < 20; tries++)
        {
            await Task.Delay(10);
            usage.Tick();
        }
        return usage.Frame!.Card!;
    }

    private static List<RoomListRow> AssistantRows(RoomCard card) =>
        card.List.Where(row => row.Kind is RoomKind.Computer or RoomKind.Link or RoomKind.Failed).ToList();

    /// <summary>
    /// The usage card's Assistant row, read from the computer beside the limits (the lead, 2026-11-12): the model on this
    /// computer, the model and the provider's own name where hosted, the host's reason where it cannot run; a read that
    /// fails draws no row, as nothing is known.
    /// </summary>
    [Test]
    public async Task TheUsageCardCarriesTheAssistantsRowAsTheComputerSaysIt()
    {
        var was = RoomDesign.Enabled;
        RoomDesign.Enabled = true;
        try
        {
            var here = AssistantRows(await LiveCard(HalcyonicJson.Serialize(new AvailableWorkspaceAgent { Model = "local-model:tag" })));
            Assert.That(here.Select(row => (row.Kind, row.Name.Text)), Is.EqualTo(new[] { (RoomKind.Computer, "local-model:tag") }));

            var hosted = AssistantRows(await LiveCard(HalcyonicJson.Serialize(
                new AvailableWorkspaceAgent { Model = "test-model-1", Served = AssistantServed.Remote, Provider = "Test Hosted" })));
            Assert.That(hosted.Select(row => (row.Kind, row.Name.Text)), Is.EqualTo(new[] { (RoomKind.Link, "test-model-1") }));
            Assert.That(hosted[0].Under!.Text, Does.Contain("Test Hosted"));

            var refused = AssistantRows(await LiveCard(HalcyonicJson.Serialize(new UnavailableWorkspaceAgent
                { Reason = new ErrorInfo { Code = "key_refused", Message = "Test Hosted refused its key. Give it a new key on this computer." } })));
            Assert.That(refused.Select(row => (row.Kind, row.Name.Text)), Is.EqualTo(new[] { (RoomKind.Failed, RoomPanelCards.AssistantUnavailable) }));
            Assert.That(refused[0].Under!.Text, Is.EqualTo("Test Hosted refused its key. Give it a new key on this computer."));

            var card = await LiveCard(null);
            Assert.That(AssistantRows(card), Is.Empty, "a failed read draws no row");
            Assert.That(card.Gauges.Count, Is.EqualTo(2), "the windows stand without it");
        }
        finally { RoomDesign.Enabled = was; }
    }
}

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

    /// <summary>The computer behind Use Model: the limits, where the assistant runs, the host's list, and the request a press makes.</summary>
    private sealed class UseModelComputer : HttpMessageHandler
    {
        public bool Confirmed { get; set; }

        /// <summary>The step's own command after the confirm: its status, and the host's failure line where it failed.</summary>
        public string ChooseStatus { get; set; } = "completed";
        public string? ChooseFailure { get; set; }

        /// <summary>Reads of the step's own command that still say accepted before <see cref="ChooseStatus"/>.</summary>
        public int ChooseAcceptedReads { get; set; }

        public List<string> Asked { get; } = new();

        private static string Command(string id, string type, string status, string? failure) =>
            "{\"journal_id\":\"journal-1\",\"command\":{\"command_id\":\"" + id + "\",\"command_type\":\"" + type + "\",\"status\":\"" + status
            + "\",\"project_id\":null,\"workstream_id\":null,\"execution_id\":null,\"issued_at\":\"2026-11-13T00:00:00.000Z\",\"updated_at\":\"2026-11-13T00:00:00.000Z\","
            + "\"rejection\":null,\"failure\":" + (failure == null ? "null" : "{\"code\":\"invalid_state\",\"message\":\"" + failure + "\",\"effect\":\"none\"}")
            + ",\"result\":null}}";

        private const string Request = """
            {"request":{"request_id":"request-1","journal_id":"journal-1","project_id":null,"workstream_id":null,"execution_id":null,
             "original_text":"Use Model","status":"STATUS","reply":null,"earlier_steps":0,
             "created_at":"2026-11-13T00:00:00.000Z","updated_at":"2026-11-13T00:00:00.000Z",
             "steps":[{"step_id":"step-1","tool_name":"assistant_model_choose","status":"STEP","command_status":null,
               "observation_position":0,"active_turn_id":null,"had_active_turn":false,"command_id":COMMAND,"observation_after":0,"observed_turn_id":null,
               "runtime_status":null,"summary":"Use test-model-1",
               "proposal":{"command_type":"assistant.model.choose","confirm":{},"review":"review-1","said":[],
                 "what_happens":"The assistant runs on this hosted model, paid with your key, and what each request reads goes to it"}}]}}
            """;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            Asked.Add(path);
            var body = path.EndsWith("/api/usage-limits", StringComparison.Ordinal) ? TwoWindows
                : path.EndsWith("/api/workspace/agent", StringComparison.Ordinal) ? HalcyonicJson.Serialize(Confirmed
                    ? new AvailableWorkspaceAgent { Model = "test-model-1", Served = AssistantServed.Remote, Provider = "Test Hosted" }
                    : new AvailableWorkspaceAgent { Model = "local-model:tag" })
                : path.Contains("/api/actions/choices?list=assistant_models", StringComparison.Ordinal) ? Models
                : path.EndsWith("/api/workspace/requests/request-1", StringComparison.Ordinal)
                    ? Request.Replace("STATUS", Confirmed ? "responded" : "waiting_for_human", StringComparison.Ordinal)
                        .Replace("STEP", Confirmed ? "running" : "waiting", StringComparison.Ordinal)
                        .Replace("COMMAND", Confirmed ? "\"choose-1\"" : "null", StringComparison.Ordinal)
                : path.EndsWith("/api/workspace/commands/choose-1", StringComparison.Ordinal)
                    ? Command("choose-1", "assistant.model.choose", ChooseAcceptedReads-- > 0 ? "accepted" : ChooseStatus, ChooseFailure)
                : path.Contains("/api/workspace/commands/", StringComparison.Ordinal)
                    ? Command(path[(path.LastIndexOf('/') + 1)..], "workspace.request.resume", "completed", null)
                : null;
            return Task.FromResult(new HttpResponseMessage(body == null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            { Content = new StringContent(body ?? "{}", Encoding.UTF8, "application/json") });
        }
    }

    private const string Models = """
        {"scope":{"project_id":null,"workstream_id":null,"execution_id":null},"list":"assistant_models","position":3,"digest":"digest-1","total":3,
         "rows":[{"index":0,"kind":"model","label":"This Computer's Model","detail":"local-model:tag","theirs":false,"actions":["assistant.model.choose"]},
                 {"index":1,"kind":"model","label":"test-model-1","detail":"On Test Hosted","theirs":true,"actions":["assistant.model.choose"]},
                 {"index":2,"kind":"model","label":"test-model-2","detail":"Other Hosted refused its key. Give it a new key on this computer.","theirs":true,"actions":[]}]}
        """;

    /// <summary>Ticks the column, the clock moving on, until <paramref name="done"/> or about two seconds.</summary>
    private static async Task Until(UsageColumn usage, FakeMenuHost host, Func<bool> done)
    {
        for (var tries = 0; tries < 200 && !done(); tries++)
        {
            await Task.Delay(10);
            host.Now += 0.3;
            usage.Tick();
        }
    }

    private static CommandAckMessage AckOf(CommandEnvelope sent, CommandView? view = null) =>
        new() { CommandId = sent.CommandId, Disposition = CommandAckDisposition.Accepted, Command = view };

    /// <summary>
    /// Use Model, end to end against the host's seam (LOOP's f34b93c7f): the Assistant row presses the host's entry, bound by the
    /// bar to the frame drawn; the host's list in its order, the place it runs now marked, a row the host offers nothing on shown
    /// with its reason and pressing nothing; a row pressed sends Use Model with the row's index and the list's digest; the ONE
    /// review card stands with the host's line and its Confirm the main action, one press; the confirm carries the review's
    /// digest, and once the request is done the row reads the computer again and names the model now chosen.
    /// </summary>
    [Test]
    public async Task UseModelIsTheRowsPressTheHostsListItsOneReviewAndTheRowReadAgain()
    {
        var was = RoomDesign.Enabled;
        RoomDesign.Enabled = true;
        try
        {
            var computer = new UseModelComputer();
            using var api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", computer);
            var host = new FakeMenuHost { Clock = Now, Height = MenuPage.Rows(6), Api = api };
            var usage = new UsageColumn(host, _ => null, new CommandFactory(Samples.Client));
            var sections = new MenuBar(MenuPlace.Usage, "Nothing Waiting").Sections();
            RoomBar Bar() => RoomBar.OfFrame(usage.Frame!.WithSections(sections, sections.Count), VoiceStage.Idle)!;
            await Until(usage, host, () => AssistantRows(usage.Frame!.Card!).Count == 1 && usage.Frame!.Card!.Gauges.Count == 2);

            // The row presses Use Model, made from the frame the bar draws.
            var drawn = usage.Frame!.WithSections(sections, sections.Count);
            var bar = RoomBar.OfFrame(drawn, VoiceStage.Idle)!;
            var row = AssistantRows(bar.Card!).Single();
            Assert.That(row.Press?.Action, Is.EqualTo(UsageColumn.UseModel));
            Assert.That(row.Press!.Frame, Is.SameAs(drawn), "a press must come from the frame that stands");

            // The host's list, in its order.
            usage.Act(UsageColumn.UseModel, null);
            await Until(usage, host, () => usage.Frame!.Card!.List.Count == 3);
            var listed = usage.Frame!.WithSections(sections, sections.Count);
            var list = RoomBar.OfFrame(listed, VoiceStage.Idle)!;
            Assert.That(list.Card!.Title!.Text, Is.EqualTo(AssistantPlace.UseModelTitle));
            Assert.That(list.Card.List[1].Press!.Frame, Is.SameAs(listed), "a row's press comes from the frame drawn");
            Assert.That(list.Card.List.Select(each => (each.Kind, each.Name.Text, each.Chosen, each.Press != null)), Is.EqualTo(new[]
            {
                (RoomKind.Computer, "This Computer's Model", true, true),
                (RoomKind.Link, "test-model-1", false, true),
                (RoomKind.Link, "test-model-2", false, false),
            }));
            Assert.That(list.Card.List[2].Under!.Text, Is.EqualTo("Other Hosted refused its key. Give it a new key on this computer."));
            Assert.That(list.Column.Select(action => action.Control.Action), Does.Contain(UsageColumn.Back));
            Assert.That(computer.Asked.Count(path => path.Contains("/api/actions/choices", StringComparison.Ordinal)), Is.EqualTo(1));

            // A row the host offers nothing on presses nothing.
            usage.Act(UsageColumn.ChooseModel, "2");
            Assert.That(host.Sent, Is.Empty);

            // A row pressed: Use Model, by the row's index and the list's digest.
            host.Ack = new TaskCompletionSource<CommandAckMessage>();
            usage.Act(UsageColumn.ChooseModel, "1");
            Assert.That(host.Sent.Count, Is.EqualTo(1));
            var press = (WorkspaceRequestSubmitCommand)host.Sent[0];
            var act = Newtonsoft.Json.Linq.JObject.Parse(HalcyonicJson.Serialize(press))["payload"]!["act"]!;
            Assert.That(Newtonsoft.Json.Linq.JToken.DeepEquals(act, Newtonsoft.Json.Linq.JObject.Parse(
                """{"command_type":"assistant.model.choose","values":{"model":1},"lists":{"assistant_models":"digest-1"}}""")), Is.True, act.ToString());
            host.Ack.SetResult(AckOf(press, new CommandView
            {
                CommandId = press.CommandId, CommandType = CommandType.WorkspaceRequestSubmit, Status = CommandStatus.Completed,
                IssuedAt = Samples.Time, UpdatedAt = Samples.Time, Result = new WorkspaceRequestCreatedResult { RequestId = "request-1" },
            }));

            // Its one review card: the host's line, Confirm the main action.
            await Until(usage, host, () => usage.Frame!.Footer.All.Any(each => each.Prompt.Id == ReviewCard.Confirm));
            var reviewed = Bar();
            Assert.That(reviewed.Card!.Description!.Text, Is.EqualTo("The assistant runs on this hosted model, paid with your key, and what each request reads goes to it"));
            Assert.That(reviewed.Card.Slots.Select(slot => slot.Value.Text), Is.EqualTo(new[] { "test-model-1" }), "the card names the model pressed");
            Assert.That(reviewed.Card.Slots[0].Value.AllTheirs, Is.True);
            Assert.That(reviewed.Column.First().Control.Action, Is.EqualTo(ReviewCard.Confirm));
            Assert.That(reviewed.Column.First().Main, Is.True);
            Assert.That(reviewed.Column.Select(action => action.Control.Action), Does.Contain(ReviewCard.Cancel));

            // The one press confirms the step with the review's digest.
            host.Ack = new TaskCompletionSource<CommandAckMessage>();
            usage.Act(ReviewCard.Confirm, null);
            Assert.That(host.Sent.Count, Is.EqualTo(2));
            var confirm = (WorkspaceRequestResumeCommand)host.Sent[1];
            Assert.That((confirm.Payload.RequestId, confirm.Payload.Confirm!.StepId, confirm.Payload.Confirm.Review), Is.EqualTo(("request-1", "step-1", "review-1")));
            computer.Confirmed = true;
            computer.ChooseAcceptedReads = 2;
            host.Ack.SetResult(AckOf(confirm));

            // Done: the usage card again, its row read afresh, naming the model now chosen.
            await Until(usage, host, () => AssistantRows(usage.Frame!.Card!).Any(each => each.Kind == RoomKind.Link));
            var after = AssistantRows(usage.Frame!.Card!);
            Assert.That(after.Select(each => (each.Kind, each.Name.Text)), Is.EqualTo(new[] { (RoomKind.Link, "test-model-1") }));
            // Accepted is not done: the row is read again only after the step's own command says completed.
            Assert.That(computer.Asked.FindLastIndex(path => path.EndsWith("/api/workspace/agent", StringComparison.Ordinal)),
                Is.GreaterThan(computer.Asked.FindLastIndex(path => path.EndsWith("/commands/choose-1", StringComparison.Ordinal))));
            Assert.That(computer.Asked.Count(path => path.EndsWith("/commands/choose-1", StringComparison.Ordinal)), Is.EqualTo(3));
            Assert.That(usage.Frame!.Card!.Gauges.Count, Is.EqualTo(2));
        }
        finally { RoomDesign.Enabled = was; }
    }

    /// <summary>
    /// Use Model is never offered where there is no way to send it; Cancel on the review leaves the usage card with nothing
    /// confirmed and the press's request cancelled.
    /// </summary>
    [Test]
    public async Task UseModelIsOfferedOnlyWhereTheComputerCanBeAskedAndCancelConfirmsNothing()
    {
        var was = RoomDesign.Enabled;
        RoomDesign.Enabled = true;
        try
        {
            var computer = new UseModelComputer();
            using var api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", computer);
            var host = new FakeMenuHost { Clock = Now, Height = MenuPage.Rows(6), Api = api };
            var unsent = new UsageColumn(host, _ => null);
            await Until(unsent, host, () => AssistantRows(unsent.Frame!.Card!).Count == 1);
            Assert.That(AssistantRows(unsent.Frame!.Card!).Single().Press, Is.Null, "no command factory: the row presses nothing");
            unsent.Act(UsageColumn.UseModel, null);
            Assert.That(unsent.Frame!.Card!.Gauges.Count, Is.EqualTo(2), "and a press of it changes nothing");

            var usage = new UsageColumn(host, _ => null, new CommandFactory(Samples.Client));
            await Until(usage, host, () => AssistantRows(usage.Frame!.Card!).Count == 1);
            usage.Act(UsageColumn.UseModel, null);
            await Until(usage, host, () => usage.Frame!.Card!.List.Count == 3);
            host.Ack = new TaskCompletionSource<CommandAckMessage>();
            usage.Act(UsageColumn.ChooseModel, "1");
            var press = host.Sent.Single();
            host.Ack.SetResult(AckOf(press, new CommandView
            {
                CommandId = press.CommandId, CommandType = CommandType.WorkspaceRequestSubmit, Status = CommandStatus.Completed,
                IssuedAt = Samples.Time, UpdatedAt = Samples.Time, Result = new WorkspaceRequestCreatedResult { RequestId = "request-1" },
            }));
            await Until(usage, host, () => usage.Frame!.Footer.All.Any(each => each.Prompt.Id == ReviewCard.Cancel));
            usage.Act(ReviewCard.Cancel, null);
            await Until(usage, host, () => usage.Frame!.Card!.Gauges.Count == 2);
            Assert.That(host.Sent.Count, Is.EqualTo(2));
            Assert.That(((WorkspaceRequestCancelCommand)host.Sent[1]).Payload.RequestId, Is.EqualTo("request-1"),
                "Cancel confirms nothing, and the request the press made is cancelled, so none waits on the person");
            Assert.That(AssistantRows(usage.Frame!.Card!).Single().Kind, Is.EqualTo(RoomKind.Computer));
        }
        finally { RoomDesign.Enabled = was; }
    }

    /// <summary>To the review of a press of test-model-1, through the column's own reads.</summary>
    private static async Task<(UsageColumn Usage, FakeMenuHost Host)> Reviewing(UseModelComputer computer, ControlPlaneApi api)
    {
        var host = new FakeMenuHost { Clock = Now, Height = MenuPage.Rows(6), Api = api };
        var usage = new UsageColumn(host, _ => null, new CommandFactory(Samples.Client));
        await Until(usage, host, () => AssistantRows(usage.Frame!.Card!).Count == 1);
        usage.Act(UsageColumn.UseModel, null);
        await Until(usage, host, () => usage.Frame!.Card!.List.Count == 3);
        host.Ack = new TaskCompletionSource<CommandAckMessage>();
        usage.Act(UsageColumn.ChooseModel, "1");
        var press = host.Sent.Single();
        host.Ack.SetResult(AckOf(press, new CommandView
        {
            CommandId = press.CommandId, CommandType = CommandType.WorkspaceRequestSubmit, Status = CommandStatus.Completed,
            IssuedAt = Samples.Time, UpdatedAt = Samples.Time, Result = new WorkspaceRequestCreatedResult { RequestId = "request-1" },
        }));
        await Until(usage, host, () => usage.Frame!.Footer.All.Any(each => each.Prompt.Id == ReviewCard.Confirm));
        return (usage, host);
    }

    /// <summary>
    /// The confirmed step's own command failing (a refused key, say) is not the choice made: the host's list again, its own line
    /// on the card, the row unchanged; and the recording offers no Use Model at all, as it reads nothing of the computer.
    /// </summary>
    [Test]
    public async Task AChoiceTheHostFailsAfterTheConfirmSaysTheHostsWordsAndTheRecordingOffersNone()
    {
        var was = RoomDesign.Enabled;
        RoomDesign.Enabled = true;
        try
        {
            var computer = new UseModelComputer { ChooseStatus = "failed", ChooseFailure = "Test Hosted refused its key. Give it a new key on this computer." };
            using var api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", computer);
            var (usage, host) = await Reviewing(computer, api);
            host.Ack = new TaskCompletionSource<CommandAckMessage>();
            usage.Act(ReviewCard.Confirm, null);
            computer.Confirmed = true;
            host.Ack.SetResult(AckOf(host.Sent[1]));
            await Until(usage, host, () => usage.Frame!.Card!.List.Count == 3);
            Assert.That(usage.Frame!.Card!.Description!.Text, Is.EqualTo("Test Hosted refused its key. Give it a new key on this computer."));
            Assert.That(usage.Frame.Card.List.Count, Is.EqualTo(3), "the host's list again, to choose another");

            var (recording, _) = Demonstration(null);
            Assert.That(recording.Frame!.Card?.List.Any(row => row.Press?.Action == UsageColumn.UseModel) ?? false, Is.False);
            recording.Act(UsageColumn.UseModel, null);
            Assert.That(recording.Frame!.Footer.All.Any(each => each.Prompt.Id == UsageColumn.Back), Is.False, "a press of it opens nothing");
        }
        finally { RoomDesign.Enabled = was; }
    }

    /// <summary>
    /// A confirm the computer has not answered never holds the person: Back leaves for the usage card, the row read again; a
    /// press the computer refuses at once returns to the list with its words.
    /// </summary>
    [Test]
    public async Task BackLeavesAnUnansweredConfirmAndARefusedPressSaysTheHostsWords()
    {
        var was = RoomDesign.Enabled;
        RoomDesign.Enabled = true;
        try
        {
            var computer = new UseModelComputer();
            using var api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", computer);
            var (usage, host) = await Reviewing(computer, api);
            host.Ack = new TaskCompletionSource<CommandAckMessage>();
            usage.Act(ReviewCard.Confirm, null);
            await Until(usage, host, () => false);
            Assert.That(usage.Frame!.Footer.All.Any(each => each.Prompt.Id == UsageColumn.Back), Is.True, "the way out stands while it waits");
            usage.Act(UsageColumn.Back, null);
            await Until(usage, host, () => usage.Frame!.Card!.Gauges.Count == 2);
            Assert.That(usage.Frame!.Card!.Gauges.Count, Is.EqualTo(2));

            var (refusing, refusingHost) = await Reviewing(computer, api);
            refusingHost.Ack = new TaskCompletionSource<CommandAckMessage>();
            refusing.Act(ReviewCard.Confirm, null);
            var confirm = refusingHost.Sent[1];
            refusingHost.Ack.SetResult(new CommandAckMessage
            {
                CommandId = confirm.CommandId, Disposition = CommandAckDisposition.Accepted,
                Command = new CommandView
                {
                    CommandId = confirm.CommandId, CommandType = CommandType.WorkspaceRequestResume, Status = CommandStatus.Failed,
                    IssuedAt = Samples.Time, UpdatedAt = Samples.Time,
                    Failure = new CommandFailure { Code = "invalid_state", Message = "This request no longer waits on you.", Effect = FailureEffect.None },
                },
            });
            await Until(refusing, refusingHost, () => refusing.Frame!.Card!.List.Count == 3);
            Assert.That(refusing.Frame!.Card!.Description!.Text, Is.EqualTo("This request no longer waits on you."));
        }
        finally { RoomDesign.Enabled = was; }
    }
}

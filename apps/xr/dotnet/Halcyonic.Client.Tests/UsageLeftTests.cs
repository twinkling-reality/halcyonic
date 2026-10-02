using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class UsageLeftTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-30T19:20:00Z");

    /// <summary>What the control plane serves from Seorak's example: two Codex windows, one stale.</summary>
    private const string Available = """
        {
          "availability": "available",
          "source": { "system": "seorak", "synthetic": false, "api_version": "v1" },
          "complete": true,
          "readings": [
            { "agent": "codex", "label": "Codex", "window": "rolling-5h", "used_percent": 40.2,
              "resets_at": "2026-09-30T21:05:00.000Z", "observed_at": "2026-09-30T19:08:00.000Z",
              "freshness": "stale", "account": { "state": "unidentified" } },
            { "agent": "codex", "label": "Codex", "window": "weekly", "used_percent": 61.5,
              "resets_at": "2026-10-06T09:00:00.000Z", "observed_at": "2026-09-30T19:18:00.000Z",
              "freshness": "fresh", "account": { "state": "unidentified" } }
          ]
        }
        """;

    private static UsageLeftPresentation Present(string json, DateTimeOffset? now = null) =>
        UsageLeftPresenter.Present(Json.AssertRoundTrips<UsageLimitsResponse>(json), now ?? Now, TimeZoneInfo.Utc);

    private static string Failure(string availability, string code) =>
        "{\"availability\":\"" + availability + "\",\"reason\":{\"code\":\"" + code + "\",\"message\":\"Details for the Mac.\"}}";

    [Test]
    public void SaysAtMostWhatWasLeftWhenSeenAndWhenItResets()
    {
        var glance = Present(Available);
        Assert.That(glance.Failed, Is.False);
        Assert.That(glance.Rows.Select(row => row.Title), Is.EqualTo(new[] { "Codex, 5-hour window", "Codex, weekly" }));
        Assert.That(glance.Rows[0].Text, Is.EqualTo("At most 60% left, seen today at 19:08, resets today at 21:05"));
        Assert.That(glance.Rows[1].Text, Is.EqualTo("At most 39% left, seen today at 19:18, resets 6 Oct at 09:00"),
            "38.5% rounds up so that at most stays true");
    }

    [Test]
    public void AMeterDrawsTheShareItsWordsSay()
    {
        var glance = Present(Available);
        Assert.That(glance.Rows.Select(row => row.Left), Is.EqualTo(new[] { 60, 39 }), "the same rounded-up share as the words");
        foreach (var row in glance.Rows) Assert.That(row.Text, Does.StartWith("At most " + row.Left + "% left"));
    }

    [Test]
    public void SaysWhereTheReadingsComeFromAndThatTheAccountIsNotIdentified()
    {
        var glance = Present(Available);
        Assert.That(glance.Source, Is.EqualTo("From Seorak, as the provider reported"));
        Assert.That(glance.Note, Is.EqualTo("Account not identified: these may come from any account used on your computer."));
    }

    [Test]
    public void NeverSaysAllowanceOrACurrentValue()
    {
        var words = string.Join(" ", Present(Available).Rows.Select(row => row.Title + " " + row.Text)) + Present(Available).Source + Present(Available).Note;
        Assert.That(words, Does.Not.Contain("allowance").IgnoreCase);
        Assert.That(words, Does.Not.Contain("remaining").IgnoreCase);
        Assert.That(words, Does.Not.Contain(" now").IgnoreCase);
        Assert.That(words, Does.Not.Contain("\u2014"));
    }

    [Test]
    public void SaysWhenSomeLimitsCouldNotBeReadAndShowsOnlyWhatWas()
    {
        var glance = Present(Available.Replace("\"complete\": true", "\"complete\": false"));
        Assert.That(glance.Rows, Has.Count.EqualTo(2), "only the readings returned, nothing inferred");
        Assert.That(glance.Note, Is.EqualTo("Some limits couldn't be read this time. Account not identified: these may come from any account used on your computer."));
        Assert.That(Present(Available).Note, Does.Not.Contain("couldn't be read"));
    }

    [Test]
    public void SaysSimulatedForAStandInsReadings()
    {
        var glance = Present(Available.Replace("\"synthetic\": false", "\"synthetic\": true"));
        Assert.That(glance.Source, Is.EqualTo("Simulated, not from Seorak"));
    }

    [Test]
    public void DropsAWindowPastItsReset()
    {
        var glance = Present(Available, DateTimeOffset.Parse("2026-09-30T21:05:00Z"));
        Assert.That(glance.Rows.Select(row => row.Title), Is.EqualTo(new[] { "Codex, weekly" }));
        var later = Present(Available, DateTimeOffset.Parse("2026-10-06T09:00:01Z"));
        Assert.That(later.Rows, Is.Empty);
        Assert.That(later.Note, Is.EqualTo("No reading since the last reset. Refresh later."));
        Assert.That(later.Source, Is.Null, "with no rows, nothing to say they come from");
    }

    [Test]
    public void ShowsAnotherDaysSightingWithItsDate()
    {
        var glance = Present(Available, DateTimeOffset.Parse("2026-10-01T08:00:00Z"));
        Assert.That(glance.Rows.Single().Text, Is.EqualTo("At most 39% left, seen 30 Sep at 19:18, resets 6 Oct at 09:00"));
    }

    [Test]
    public void DaysAreJudgedInThePersonsZone()
    {
        // 19:18 UTC on 30 Sep is already 1 Oct in Tokyo; seen from there at 08:00 on 1 Oct, it was today.
        var tokyo = TimeZoneInfo.CreateCustomTimeZone("UTC+9", TimeSpan.FromHours(9), "UTC+9", "UTC+9");
        var glance = UsageLeftPresenter.Present(Json.AssertRoundTrips<UsageLimitsResponse>(Available),
            DateTimeOffset.Parse("2026-09-30T23:00:00Z"), tokyo);
        Assert.That(glance.Rows.Single().Text, Is.EqualTo("At most 39% left, seen today at 04:18, resets 6 Oct at 18:00"));
    }

    [Test]
    public void ShowsAnAgentsLabelAsPlainText()
    {
        var json = Available.Replace("\"label\": \"Codex\", \"window\": \"weekly\"", "\"label\": \"<b>Agent</b>\", \"window\": \"weekly\"");
        var glance = Present(json);
        Assert.That(glance.Rows[1].Title, Is.EqualTo(LabelText.Plain("<b>Agent</b>") + ", weekly"));
    }

    [Test]
    public void CutsALongAgentNameShort()
    {
        var name = new string('A', 80);
        var glance = Present(Available.Replace("\"label\": \"Codex\", \"window\": \"weekly\"", "\"label\": \"" + name + "\", \"window\": \"weekly\""));
        Assert.That(glance.Rows[1].Title, Is.EqualTo(IntelligenceText.Truncate(name, UsageLeftPresenter.LabelLimit) + ", weekly"));
        Assert.That(glance.Rows[1].Title.Length, Is.LessThan(45));
    }

    [TestCase("unauthorized", "insufficient_scope")]
    [TestCase("unauthorized", "credential_missing")]
    [TestCase("unauthorized", "outside_credential_restriction")]
    [TestCase("unavailable", "not_configured")]
    [TestCase("unavailable", "limits_not_served")]
    public void EverySetupProblemSaysItIsNotSetUpWithoutScopeDetails(string availability, string code)
    {
        var glance = Present(Failure(availability, code));
        Assert.That(glance.Rows, Is.Empty);
        Assert.That(glance.Failed, Is.False, "not being set up is said plainly, not as a failure");
        Assert.That(glance.Note, Is.EqualTo("Usage left isn't set up on your computer yet. Set it up there to see it here."));
    }

    [Test]
    public void NoReadingIsNeverZero()
    {
        var glance = Present(Failure("unavailable", "not_captured"));
        Assert.That(glance.Rows, Is.Empty);
        Assert.That(glance.Failed, Is.False);
        Assert.That(glance.Note, Is.EqualTo("No usage reading yet."));
        Assert.That(glance.Note, Does.Not.Contain("0%"));
    }

    [TestCase("unavailable", "not_running")]
    [TestCase("unavailable", "rate_limited")]
    [TestCase("incompatible", "invalid_usage_limits")]
    public void OtherFailuresSayItCannotBeReadNow(string availability, string code)
    {
        var glance = Present(Failure(availability, code));
        Assert.That(glance.Failed, Is.True);
        Assert.That(glance.Note, Is.EqualTo("Usage left can't be read right now. Try again later."));
    }

    [Test]
    public void AnUnreachableMacIsAFailureRefreshCanRetry()
    {
        var glance = UsageLeftPresenter.Unreachable();
        Assert.That(glance.Failed, Is.True);
        Assert.That(glance.Rows, Is.Empty);
        Assert.That(glance.Note, Is.EqualTo("Couldn't reach your computer. Press Refresh to try again."));
    }

    [Test]
    public async Task ReadsUsageLimitsFromTheControlPlane()
    {
        var handler = new ControlPlaneApiTests.CannedHandler(HttpStatusCode.OK, Available);
        using var api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", handler);
        var response = await api.GetUsageLimitsAsync();
        var request = handler.Requests.Single();
        Assert.That(request.RequestUri, Is.EqualTo(new Uri("http://127.0.0.1:47800/api/usage-limits")));
        Assert.That(request.Headers.Authorization?.ToString(), Is.EqualTo("Bearer test-token"));
        var readings = ((AvailableUsageLimits)response).Readings;
        Assert.That(readings.Select(reading => reading.Window), Is.EqualTo(new[] { UsageLimitWindow.Rolling5h, UsageLimitWindow.Weekly }));
        Assert.That(readings[0].Freshness, Is.EqualTo(UsageLimitFreshness.Stale));
        Assert.That(readings[0].Account.State, Is.EqualTo("unidentified"));
    }
}

public class UsageLeftScreensTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-30T19:20:00Z");

    private const string Available = """
        {
          "availability": "available",
          "source": { "system": "seorak", "synthetic": true, "api_version": "v1" },
          "complete": false,
          "readings": [
            { "agent": "codex", "label": "Codex", "window": "rolling-5h", "used_percent": 40.2,
              "resets_at": "2026-09-30T21:05:00.000Z", "observed_at": "2026-09-30T19:08:00.000Z",
              "freshness": "fresh", "account": { "state": "unidentified" } },
            { "agent": "codex", "label": "Codex", "window": "weekly", "used_percent": 61.5,
              "resets_at": "2026-10-06T09:00:00.000Z", "observed_at": "2026-09-30T19:18:00.000Z",
              "freshness": "fresh", "account": { "state": "unidentified" } }
          ]
        }
        """;

    private static UsageLeftPresentation Present() =>
        UsageLeftPresenter.Present(Json.AssertRoundTrips<UsageLimitsResponse>(Available), Now, TimeZoneInfo.Utc);

    [Test]
    public void EachWindowShowsItsNameWithAMeterOverWhatWasSeen()
    {
        var presentation = Present();
        var model = UsageLeftScreens.Screen(presentation, reading: false, canRead: true);
        Assert.That(model.Title, Is.EqualTo("Usage left"));
        Assert.That(model.Movable, Is.False);
        Assert.That(model.Tabs, Is.Empty, "Close stands in the header of a panel that stays put with no tabs");
        Assert.That(model.Context, Is.EqualTo("Simulated, not from Seorak"), "every page says where the windows come from");
        Assert.That(model.PartsNote, Is.EqualTo(presentation.Note), "every page says what is unknown");
        Assert.That(model.Rows.Select(row => row.Title), Is.EqualTo(new[]
        {
            "Codex, 5-hour window", "At most 60% left, seen today at 19:08, resets today at 21:05",
            "Codex, weekly", "At most 39% left, seen today at 19:18, resets 6 Oct at 09:00",
        }));
        Assert.That(model.Rows.All(row => row.Line && !row.Pressable), Is.True, "nothing in the list takes a press");
        Assert.That(model.Rows.Select(row => row.Meter), Is.EqualTo(new float?[] { 0.60f, null, 0.39f, null }), "each meter draws the share its words say");
        Assert.That(model.Rows.Where(row => row.Meter.HasValue).All(row => row.TitleIsData && !row.MeterWaiting), Is.True);
        Assert.That(model.Rows.Where(row => !row.Meter.HasValue).All(row => row.Continues), Is.True, "what was seen stays with its window");
        var refresh = model.Actions.All.Single();
        Assert.That((refresh.Id, refresh.Label, refresh.Role, refresh.Available), Is.EqualTo((UsageLeftScreens.Refresh, "Refresh", PanelActionRole.Secondary, true)));
        Assert.That(model.BarNote, Is.Null);
    }

    [Test]
    public void WhileReadingAgainTheRowsStayTheirMetersWaitAndRefreshWaits()
    {
        var model = UsageLeftScreens.Screen(Present(), reading: true, canRead: true);
        Assert.That(model.Rows.Where(row => row.Meter.HasValue).All(row => row.MeterWaiting), Is.True);
        Assert.That(model.Actions.All.Single().Available, Is.False);
        Assert.That(model.BarNote, Is.EqualTo("Reading usage left…"));
    }

    [Test]
    public void WithNoRowsTheListSaysWhyOnce()
    {
        var model = UsageLeftScreens.Screen(UsageLeftPresenter.Message(UsageLeftPresenter.Reading), reading: true, canRead: true);
        Assert.That(model.Rows.Single().Title, Is.EqualTo("Reading usage left…"));
        Assert.That(model.BarNote, Is.Null, "said in the list, not again at the bar");
        Assert.That(model.Context, Is.Null);
        Assert.That(model.PartsNote, Is.Null);
        Assert.That(model.Actions.All.Single().Available, Is.False);
    }

    [Test]
    public void OnlyAFailureIsSaidInTheFailureTone()
    {
        Assert.That(UsageLeftScreens.Screen(UsageLeftPresenter.Unreachable(), reading: false, canRead: true).Rows.Single().Tone, Is.EqualTo(GlazeTone.Failure));
        var notSetUp = UsageLeftScreens.Screen(UsageLeftPresenter.Message(UsageLeftPresenter.NotSetUp), reading: false, canRead: true);
        Assert.That(notSetUp.Rows.Single().Tone, Is.Null);
        Assert.That(notSetUp.Actions.All.Single().Available, Is.True, "Refresh shows it once it is set up");
    }

    [Test]
    public void TheDemonstrationOffersNoRefresh()
    {
        var model = UsageLeftScreens.Screen(UsageLeftPresenter.Message(UsageLeftPresenter.NotInDemo), reading: false, canRead: false);
        Assert.That(model.Rows.Single().Title, Is.EqualTo("Usage left isn't part of the demo."));
        Assert.That(model.Actions.All, Is.Empty, "there is nothing to read again");
    }
}

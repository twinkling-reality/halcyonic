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
        Assert.That(glance.Problem, Is.False);
        Assert.That(glance.Rows.Select(row => row.Title), Is.EqualTo(new[] { "Codex, 5-hour window", "Codex, weekly window" }));
        Assert.That(glance.Rows[0].Text, Is.EqualTo("At most 60% left, seen at 19:08, resets at 21:05"));
        Assert.That(glance.Rows[1].Text, Is.EqualTo("At most 39% left, seen at 19:18, resets 6 Oct at 09:00"),
            "38.5% rounds up so that at most stays true");
    }

    [Test]
    public void SaysTheAccountIsNotIdentified()
    {
        Assert.That(Present(Available).Note, Is.EqualTo("From Seorak, as the provider reported. Account not identified: these may come from any account used on your Mac."));
    }

    [Test]
    public void NeverSaysAllowanceOrACurrentValue()
    {
        var words = string.Join(" ", Present(Available).Rows.Select(row => row.Title + " " + row.Text)) + Present(Available).Note;
        Assert.That(words, Does.Not.Contain("allowance").IgnoreCase);
        Assert.That(words, Does.Not.Contain("remaining").IgnoreCase);
        Assert.That(words, Does.Not.Contain(" now").IgnoreCase);
        Assert.That(words, Does.Not.Contain("\u2014"));
    }

    [Test]
    public void SaysSimulatedForAStandInsReadings()
    {
        var glance = Present(Available.Replace("\"synthetic\": false", "\"synthetic\": true"));
        Assert.That(glance.Note, Does.StartWith("Simulated, not from Seorak."));
    }

    [Test]
    public void DropsAWindowPastItsReset()
    {
        var glance = Present(Available, DateTimeOffset.Parse("2026-09-30T21:05:00Z"));
        Assert.That(glance.Rows.Select(row => row.Title), Is.EqualTo(new[] { "Codex, weekly window" }));
        var later = Present(Available, DateTimeOffset.Parse("2026-10-06T09:00:01Z"));
        Assert.That(later.Rows, Is.Empty);
        Assert.That(later.Note, Is.EqualTo("No reading since the last reset. Read again later."));
    }

    [Test]
    public void ShowsAnotherDaysSightingWithItsDate()
    {
        var glance = Present(Available, DateTimeOffset.Parse("2026-10-01T08:00:00Z"));
        Assert.That(glance.Rows.Single().Text, Is.EqualTo("At most 39% left, seen 30 Sep at 19:18, resets 6 Oct at 09:00"));
    }

    [Test]
    public void ShowsAnAgentsLabelAsPlainText()
    {
        var json = Available.Replace("\"label\": \"Codex\", \"window\": \"weekly\"", "\"label\": \"<b>Agent</b>\", \"window\": \"weekly\"");
        var glance = Present(json);
        Assert.That(glance.Rows[1].Title, Is.EqualTo(LabelText.Plain("<b>Agent</b>") + ", weekly window"));
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
        Assert.That(glance.Problem, Is.True);
        Assert.That(glance.Note, Is.EqualTo("Usage left isn't set up on your Mac."));
    }

    [Test]
    public void NoReadingIsNeverZero()
    {
        var glance = Present(Failure("unavailable", "not_captured"));
        Assert.That(glance.Rows, Is.Empty);
        Assert.That(glance.Problem, Is.False);
        Assert.That(glance.Note, Is.EqualTo("No usage reading yet."));
        Assert.That(glance.Note, Does.Not.Contain("0%"));
    }

    [TestCase("unavailable", "not_running")]
    [TestCase("unavailable", "rate_limited")]
    [TestCase("incompatible", "invalid_usage_limits")]
    public void OtherFailuresSayItCannotBeReadNow(string availability, string code)
    {
        var glance = Present(Failure(availability, code));
        Assert.That(glance.Problem, Is.True);
        Assert.That(glance.Note, Is.EqualTo("Usage left can't be read right now. Try again later."));
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

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>Short, honest text for the optional global usage glance.</summary>
    public static class UsageLimitsPresenter
    {
        public static IReadOnlyList<string> Lines(UsageLimitsResponse response, TimeZoneInfo zone)
        {
            if (response is UnauthorizedUsageLimits)
                return new[] { "Usage unavailable.", "Ask Seorak for an account-wide", "period:read credential." };
            if (response is UnavailableUsageLimits unavailable)
                return unavailable.Reason.Code == "outside_credential_restriction"
                    ? new[] { "Usage unavailable.", "Use an account-wide Seorak", "period:read credential." }
                    : new[] { "No provider limit captured yet.", "Open Codex and try again later." };
            if (response is not AvailableUsageLimits available)
                return new[] { "Usage could not be read.", "Try again later." };

            var lines = new List<string>();
            foreach (var reading in available.Readings.Take(2))
            {
                if (!DateTimeOffset.TryParse(reading.ObservedAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var observed) ||
                    !DateTimeOffset.TryParse(reading.ResetsAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var reset) ||
                    double.IsNaN(reading.RemainingPercent) || reading.RemainingPercent < 0 ||
                    reading.RemainingPercent > 100 || reset <= DateTimeOffset.UtcNow)
                    continue;
                var provider = reading.Provider.ToString() == "codex" ? "Codex" : "Claude Code";
                var window = reading.Window.ToString() == "rolling-5h" ? "5-hour" : "weekly";
                lines.Add("Last " + provider + " " + window + " limit seen:");
                lines.Add(reading.RemainingPercent.ToString("0.#", CultureInfo.InvariantCulture) + "% left");
                lines.Add("Seen " + TimeZoneInfo.ConvertTime(observed, zone).ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture)
                    + "  Resets " + TimeZoneInfo.ConvertTime(reset, zone).ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture));
            }
            return lines.Count > 0 ? lines : new[] { "No usable provider limit captured.", "Try again after Codex reports one." };
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>One limit window in the Usage left glance: whose window it is, and what was seen.</summary>
    public sealed class UsageLeftRow
    {
        public UsageLeftRow(string title, string text)
        {
            Title = title;
            Text = text;
        }

        /// <summary>The agent's name as the control plane gives it, plain text, and the window.</summary>
        public string Title { get; }

        /// <summary>"At most X% left, seen at …, resets …". Never a current value.</summary>
        public string Text { get; }
    }

    /// <summary>What the Usage left glance shows, in words, so the XR layer only lays it out.</summary>
    public sealed class UsageLeftPresentation
    {
        public UsageLeftPresentation(IReadOnlyList<UsageLeftRow> rows, string note, bool problem)
        {
            Rows = rows;
            Note = note;
            Problem = problem;
        }

        public IReadOnlyList<UsageLeftRow> Rows { get; }

        /// <summary>Under the rows: where they come from and that no account is identified, or why there are no rows. The only words that name Seorak.</summary>
        public string Note { get; }

        /// <summary>There is nothing to show, for a reason the person may want to fix.</summary>
        public bool Problem { get; }
    }

    /// <summary>
    /// Turns the control plane's provider usage limits into the Usage left glance. A provider's used
    /// share is shown as the most that was left when it was seen, never as what is left now, and a
    /// window past its reset is not shown at all. The source cannot tell accounts apart, so the
    /// glance says so rather than tie a window to the selected runtime or model.
    /// </summary>
    public static class UsageLeftPresenter
    {
        /// <summary>The most of an agent's name a row shows; the rest ends in an ellipsis.</summary>
        public const int LabelLimit = 32;

        public const string NotSetUp = "Usage left isn't set up on your Mac.";
        public const string Reading = "Reading usage left…";

        /// <summary>Said when the source read only some limits; a window not shown is unknown, never zero.</summary>
        public const string Incomplete = "Some limits couldn't be read this time.";
        public const string Unidentified = "Account not identified: these may come from any account used on your Mac.";

        public static UsageLeftPresentation Present(UsageLimitsResponse response, DateTimeOffset now, TimeZoneInfo zone)
        {
            switch (response)
            {
                case AvailableUsageLimits available:
                    return Present(available.Readings, available.Source.Synthetic, available.Complete, now, zone);
                case UnauthorizedUsageLimits:
                    return Problem(NotSetUp);
                case UnavailableUsageLimits unavailable:
                    return unavailable.Reason.Code switch
                    {
                        "not_configured" or "limits_not_served" => Problem(NotSetUp),
                        "not_captured" => Quiet("No usage reading yet."),
                        "no_current_reading" => Quiet("No reading since the last reset. Read again later."),
                        _ => Problem("Usage left can't be read right now. Try again later."),
                    };
                default:
                    return Problem("Usage left can't be read right now. Try again later.");
            }
        }

        /// <summary>The glance when this device could not reach the control plane at all.</summary>
        public static UsageLeftPresentation Unreachable() => Problem("Couldn't reach your Mac. Try again.");

        public static UsageLeftPresentation Message(string text) => Quiet(text);

        private static UsageLeftPresentation Present(IEnumerable<UsageLimit> readings, bool synthetic, bool complete, DateTimeOffset now, TimeZoneInfo zone)
        {
            var rows = new List<UsageLeftRow>();
            foreach (var reading in readings)
            {
                if (!IntelligenceText.TryParse(reading.ResetsAt, out var resets) ||
                    !IntelligenceText.TryParse(reading.ObservedAt, out var observed) ||
                    resets <= now || double.IsNaN(reading.UsedPercent) ||
                    reading.UsedPercent < 0 || reading.UsedPercent > 100)
                    continue;
                // Rounded up, so that "at most" stays true.
                var left = (int)Math.Ceiling(100 - reading.UsedPercent);
                rows.Add(new UsageLeftRow(
                    IntelligenceText.Truncate(IntelligenceText.Plain(reading.Label), LabelLimit) + ", " + (reading.Window == UsageLimitWindow.Rolling5h ? "5-hour window" : "weekly"),
                    "At most " + left.ToString(CultureInfo.InvariantCulture) + "% left, seen " + When(observed, now, zone)
                        + ", resets " + When(resets, now, zone)));
            }
            return rows.Count == 0
                ? Quiet("No reading since the last reset. Read again later.")
                : new UsageLeftPresentation(rows, (complete ? "" : Incomplete + " ")
                    + (synthetic ? "Simulated, not from Seorak. " : "From Seorak, as the provider reported. ") + Unidentified, problem: false);
        }

        /// <summary>"today at 15:18" in the person's zone, or "6 Oct at 09:00" on another day, so a time never reads as today when it is not.</summary>
        public static string When(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
        {
            var local = TimeZoneInfo.ConvertTime(at, zone);
            var today = TimeZoneInfo.ConvertTime(now, zone).Date;
            return local.Date == today
                ? "today at " + local.ToString("HH:mm", CultureInfo.InvariantCulture)
                : local.ToString("d MMM 'at' HH:mm", CultureInfo.InvariantCulture);
        }

        private static UsageLeftPresentation Problem(string note) =>
            new UsageLeftPresentation(Array.Empty<UsageLeftRow>(), note, problem: true);

        private static UsageLeftPresentation Quiet(string note) =>
            new UsageLeftPresentation(Array.Empty<UsageLeftRow>(), note, problem: false);
    }
}

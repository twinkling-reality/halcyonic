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
        public UsageLeftRow(string title, string text, int left, string seen = "", string resets = "")
            : this(LineWords.Written(title), text, left, seen, resets)
        {
        }

        /// <param name="title">The provider's label for the limit, leaning, then Halcyonic's word for its window.</param>
        /// <param name="agent">The agent the reading belongs to, as Seorak names it; null where not known.</param>
        public UsageLeftRow(LineWords title, string text, int left, string seen = "", string resets = "", string? agent = null)
        {
            TitleRuns = title;
            Title = title.Text;
            Agent = agent;
            Text = text;
            Left = left;
            Seen = seen;
            Resets = resets;
        }

        /// <summary>The agent's name as the control plane gives it, plain text, and the window.</summary>
        public string Title { get; }

        /// <summary>Its title as runs: the provider's label leaning, Halcyonic's window upright.</summary>
        public LineWords TitleRuns { get; }

        /// <summary>The agent the reading belongs to, as Seorak names it, outside text; null where not known.</summary>
        public string? Agent { get; }

        /// <summary>"At most X% left, seen at …, resets …". Never a current value.</summary>
        public string Text { get; }

        /// <summary>The X of "at most X% left", rounded up so "at most" stays true: what a meter of this window draws.</summary>
        public int Left { get; }

        /// <summary>When it was seen and when it resets, as "today at 15:18", for the menu's side panel.</summary>
        public string Seen { get; }

        public string Resets { get; }
    }

    /// <summary>What the Usage left glance shows, in words, so the XR layer only lays it out.</summary>
    public sealed class UsageLeftPresentation
    {
        public UsageLeftPresentation(IReadOnlyList<UsageLeftRow> rows, string note, bool failed = false, string? source = null)
        {
            Rows = rows;
            Note = note;
            Failed = failed;
            Source = source;
        }

        public IReadOnlyList<UsageLeftRow> Rows { get; }

        /// <summary>With the rows: that some limits couldn't be read, and that no account is identified. Without: why there are none.</summary>
        public string Note { get; }

        /// <summary>
        /// Reading went wrong, so there is nothing to show: said in the colour of what went wrong. Usage
        /// left not being set up is not a failure, and is said as plainly as any other answer.
        /// </summary>
        public bool Failed { get; }

        /// <summary>Where the rows come from, Seorak or a stand-in that simulates it, while there are rows: the only words that name Seorak.</summary>
        public string? Source { get; }
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

        /// <summary>The glance's title, and the rail's chip that opens it.</summary>
        public const string Title = "Usage Left";

        public const string NotSetUp = "Usage Left Not Set Up · Set Up on Computer";
        public const string Reading = "Reading Usage";

        /// <summary>Said while the recorded demonstration plays, when its recording holds no usage limits.</summary>
        public const string NotInDemo = "Not in Demo";

        /// <summary>Where the demonstration's limits come from, in place of a source's name.</summary>
        public const string Recorded = "Recorded for Demo · No Account";

        /// <summary>Under the demonstration's limits, in place of what is said of an account.</summary>
        public const string PartOfTheRecording = "Limits Part of Recording";

        /// <summary>Said when the source read only some limits; a window not shown is unknown, never zero.</summary>
        public const string Incomplete = "Some Limits Unread";
        public const string Unidentified = "Account Unknown · Any Account on Computer";

        public const string FromSeorak = "Seorak · Provider Reported";
        public const string Simulated = "Simulated · Not Seorak";

        private const string NoReadingSinceReset = "No Reading Since Reset · Refresh Later";

        /// <param name="recorded">The limits are the recorded demonstration's (<see cref="DemonstrationRecording.UsageLimitsAt"/>), so they say so instead of naming a source or an account.</param>
        public static UsageLeftPresentation Present(UsageLimitsResponse response, DateTimeOffset now, TimeZoneInfo zone, bool recorded = false)
        {
            switch (response)
            {
                case AvailableUsageLimits available:
                    return Present(available.Readings, available.Source.Synthetic, available.Complete, now, zone, recorded);
                case UnauthorizedUsageLimits:
                    return Quiet(NotSetUp);
                case UnavailableUsageLimits unavailable:
                    return unavailable.Reason.Code switch
                    {
                        "not_configured" or "limits_not_served" => Quiet(NotSetUp),
                        "not_captured" => Quiet("No Reading Yet"),
                        "no_current_reading" => Quiet(NoReadingSinceReset),
                        _ => Failure("Usage Unreadable · Retry Later"),
                    };
                default:
                    return Failure("Usage Unreadable · Retry Later");
            }
        }

        /// <summary>The glance when this device could not reach the control plane at all.</summary>
        public static UsageLeftPresentation Unreachable() => Failure("Computer Unreachable · Refresh");

        public static UsageLeftPresentation Message(string text) => Quiet(text);

        private static UsageLeftPresentation Present(IEnumerable<UsageLimit> readings, bool synthetic, bool complete, DateTimeOffset now, TimeZoneInfo zone, bool recorded)
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
                    LineWords.TheirName(IntelligenceText.Truncate(IntelligenceText.Plain(reading.Label), LabelLimit))
                        + LineWords.Ours(" · " + (reading.Window == UsageLimitWindow.Rolling5h ? "5-Hour Window" : "Weekly")),
                    "Up to " + left.ToString(CultureInfo.InvariantCulture) + "% Left · Seen " + When(observed, now, zone)
                        + " · Resets " + When(resets, now, zone),
                    left, When(observed, now, zone), When(resets, now, zone), reading.Agent));
            }
            return rows.Count == 0
                ? Quiet(NoReadingSinceReset)
                : new UsageLeftPresentation(rows, (complete ? "" : Incomplete + " · ") + (recorded ? PartOfTheRecording : Unidentified),
                    source: recorded ? Recorded : synthetic ? Simulated : FromSeorak);
        }

        /// <summary>"today at 15:18" in the person's zone, or "6 Oct at 09:00" on another day, so a time never reads as today when it is not.</summary>
        public static string When(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
        {
            var local = TimeZoneInfo.ConvertTime(at, zone);
            var today = TimeZoneInfo.ConvertTime(now, zone).Date;
            return local.Date == today
                ? "Today " + local.ToString("HH:mm", CultureInfo.InvariantCulture)
                : local.ToString("d MMM HH:mm", CultureInfo.InvariantCulture);
        }

        private static UsageLeftPresentation Failure(string note) =>
            new UsageLeftPresentation(Array.Empty<UsageLeftRow>(), note, failed: true);

        private static UsageLeftPresentation Quiet(string note) =>
            new UsageLeftPresentation(Array.Empty<UsageLeftRow>(), note);
    }
}

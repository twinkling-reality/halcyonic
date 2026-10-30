#nullable enable
using System;
using System.Globalization;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The words of the projects gallery (ADR 0039): a catalog project's state in Halcyonic's own
    /// words and the group it is shown in, where a project is, how old the catalog is, and why there
    /// is no catalog. The catalog's own names and titles are never here: they lean, as outside words.
    /// </summary>
    public static class GalleryText
    {
        /// <summary>The catalog's source as people see it, one name until the owner settles it.</summary>
        public const string Source = "Phat Porcupine";

        /// <summary>Over every project a sample catalog holds, so none is taken for a real one.</summary>
        public const string SampleProjects = "Sample Projects";

        /// <summary>A state or a reason Halcyonic does not know yet: shown with what needs the person.</summary>
        public const string NeedsALook = "Needs a Look";

        /// <summary>A Halcyonic project bound to a folder there.</summary>
        public const string OnYourComputer = "On Your Computer";

        /// <summary>A Halcyonic project with no folder, such as the mock runtime's.</summary>
        public const string NoFolder = "No Folder Yet";

        /// <summary>
        /// A catalog project no Halcyonic project is linked to. Whether it is on the computer is not known
        /// here, so the words never say it isn't.
        /// </summary>
        public const string NotLinked = "Not Linked";

        /// <summary>Before a folder the computer matched to a catalog project by a rule: inferred, never confirmed.</summary>
        public const string MaybeOnYourComputer = "Possible Match · ";

        /// <summary>
        /// A catalog project's state, as the source names it, in Halcyonic's words and its group. Any
        /// state the source adds later needs a look, since it may be one that waits for the person. The
        /// words never repeat a task's own ("Needs You", "Checks Failed"), so a reported state is
        /// never read as one Halcyonic observed (agreed with DESIGN, 2026-10-29).
        /// </summary>
        public static (GallerySection Section, string Line) State(string state) => state switch
        {
            "awaiting_input" => (GallerySection.NeedsYou, "Agent Waiting"),
            "session_failed" => (GallerySection.NeedsYou, "Agent Failed"),
            "checks_failing" => (GallerySection.NeedsYou, "Checks Failing"),
            "review_requested" => (GallerySection.NeedsYou, "Review Requested"),
            "changes_requested" => (GallerySection.NeedsYou, "Changes Requested"),
            "ready_to_merge" => (GallerySection.NeedsYou, "Ready to Merge"),
            "agent_running" => (GallerySection.WorkingNow, "Agent Running"),
            "session_complete" => (GallerySection.Recent, "Agent Finished"),
            "active" => (GallerySection.Recent, "Changed Recently"),
            "inactive" => (GallerySection.Quiet, "Quiet"),
            "archived" => (GallerySection.Quiet, "Archived"),
            "access_lost" => (GallerySection.Quiet, "Unreachable"),
            _ => (GallerySection.NeedsYou, NeedsALook),
        };

        /// <summary>
        /// Why there are no other projects in a recording, the demonstration's or a recorded run's: no
        /// recording reaches the source, so its name and setup are never shown there.
        /// </summary>
        public const string NotInThisRecording = "Not in Recording";

        /// <summary>
        /// Why the gallery holds only Halcyonic's own projects, by what the control plane answered:
        /// one line per cause, the way on included; null for a catalog that was read. A recorded answer
        /// (<paramref name="recorded"/>) says only <see cref="NotInThisRecording"/>, so the source is named
        /// only where a real control plane answered.
        /// </summary>
        public static string? Why(CatalogResponse response, bool recorded = false) => response switch
        {
            AvailableCatalog _ => null,
            _ when recorded => NotInThisRecording,
            UnauthorizedCatalog _ => Source + " · Access Refused · Set Up Again",
            IncompatibleCatalog _ => Source + " · Version Unsupported",
            UnavailableCatalog { Reason: { Code: "not_running" } } => Source + " · Not Running",
            UnavailableCatalog { Reason: { Code: "not_configured" } } => Source + " · Not Set Up · Set Up on Computer",
            UnavailableCatalog { Reason: { Code: "not_ready" } } => Source + " · Gathering · Refresh",
            _ => "Projects Unreadable · Refresh",
        };

        /// <summary>This headset's own read failed: <paramref name="why"/> is <see cref="IntelligenceText.WhyUnread"/>'s.</summary>
        public static string Unread(string why) => "Projects Unreadable · " + why;

        /// <summary>
        /// How old the catalog is, by when its source last read the person's repositories: "Checked · Today
        /// 14:50", or that the last read failed and when one last worked, so it never reads as live.
        /// </summary>
        public static string Age(AvailableSync sync, DateTimeOffset now, TimeZoneInfo zone)
        {
            var at = Parse(sync.RepositoriesAt);
            if (sync.RepositoriesFailed)
            {
                return at is DateTimeOffset last
                    ? "Refresh Failed · Last · " + When(last, now, zone)
                    : "Refresh Failed";
            }
            return at is DateTimeOffset checkedAt ? "Checked · " + When(checkedAt, now, zone) : "Not Checked";
        }

        /// <summary>"Today 14:50", or "27 Oct 09:05" where that isn't today: a time in the cadence's metadata.</summary>
        public static string When(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
        {
            var local = TimeZoneInfo.ConvertTime(at, zone);
            var today = TimeZoneInfo.ConvertTime(now, zone).Date;
            return local.Date == today
                ? "Today " + local.ToString("HH:mm", CultureInfo.InvariantCulture)
                : local.ToString("d MMM HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>A time as the source wrote it, or null when it wrote none or one this app can't read.</summary>
        internal static DateTimeOffset? Parse(string? time) =>
            time != null && DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : (DateTimeOffset?)null;
    }
}

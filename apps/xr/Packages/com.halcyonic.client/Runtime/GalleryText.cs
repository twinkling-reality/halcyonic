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
        public const string SampleProjects = "Sample projects";

        /// <summary>A state or a reason Halcyonic does not know yet: shown with what needs the person.</summary>
        public const string NeedsALook = "Needs a look";

        /// <summary>A Halcyonic project bound to a folder there.</summary>
        public const string OnYourComputer = "On " + HostText.Your;

        /// <summary>A Halcyonic project with no folder, such as the mock runtime's.</summary>
        public const string NoFolder = "No folder on " + HostText.Your + " yet";

        /// <summary>
        /// A catalog project no Halcyonic project is linked to. Whether it is on the computer is not known
        /// here, so the words never say it isn't.
        /// </summary>
        public const string NotLinked = "Not linked to a project here";

        /// <summary>Before a folder the computer matched to a catalog project by a rule: inferred, never confirmed.</summary>
        public const string MaybeOnYourComputer = "Maybe on " + HostText.Your + ": ";

        /// <summary>
        /// A catalog project's state, as the source names it, in Halcyonic's words and its group. Any
        /// state the source adds later needs a look, since it may be one that waits for the person. The
        /// words never repeat a task's own ("Waiting for you", "Checks failed"), so a reported state is
        /// never read as one Halcyonic observed (agreed with DESIGN, 2026-10-29).
        /// </summary>
        public static (GallerySection Section, string Line) State(string state) => state switch
        {
            "awaiting_input" => (GallerySection.NeedsYou, "An agent is waiting for you"),
            "session_failed" => (GallerySection.NeedsYou, "An agent couldn't finish"),
            "checks_failing" => (GallerySection.NeedsYou, "Its checks are failing"),
            "review_requested" => (GallerySection.NeedsYou, "Asked for your review"),
            "changes_requested" => (GallerySection.NeedsYou, "Someone asked for changes"),
            "ready_to_merge" => (GallerySection.NeedsYou, "Ready to merge"),
            "agent_running" => (GallerySection.WorkingNow, "An agent is working on it"),
            "session_complete" => (GallerySection.Recent, "An agent finished"),
            "active" => (GallerySection.Recent, "Changed recently"),
            "inactive" => (GallerySection.Quiet, "Quiet for a while"),
            "archived" => (GallerySection.Quiet, "Archived"),
            "access_lost" => (GallerySection.Quiet, "Can't reach it now"),
            _ => (GallerySection.NeedsYou, NeedsALook),
        };

        /// <summary>
        /// Why there are no other projects in a recording, the demonstration's or a recorded run's: no
        /// recording reaches the source, so its name and setup are never shown there.
        /// </summary>
        public const string NotInThisRecording = "Your other projects aren't part of this recording.";

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
            UnauthorizedCatalog _ => Source + " no longer lets Halcyonic read your projects. Set it up again on " + HostText.Your + ".",
            IncompatibleCatalog _ => "This version of " + Source + " doesn't work with Halcyonic yet.",
            UnavailableCatalog { Reason: { Code: "not_running" } } => "Your other projects show here while " + Source + " is running on " + HostText.Your + ".",
            UnavailableCatalog { Reason: { Code: "not_configured" } } => "Your other projects can show here too once " + Source + " is set up on " + HostText.Your + ".",
            UnavailableCatalog { Reason: { Code: "not_ready" } } => Source + " is still gathering your projects. Press Refresh in a moment.",
            _ => "Couldn't read your other projects just now. Press Refresh in a moment.",
        };

        /// <summary>This headset's own read failed: <paramref name="why"/> is <see cref="IntelligenceText.WhyUnread"/>'s.</summary>
        public static string Unread(string why) => "Couldn't read your other projects: " + why;

        /// <summary>
        /// How old the catalog is, by when its source last read the person's repositories: "Checked today
        /// at 14:50", or that the last read failed and when one last worked, so it never reads as live.
        /// </summary>
        public static string Age(AvailableSync sync, DateTimeOffset now, TimeZoneInfo zone)
        {
            var at = Parse(sync.RepositoriesAt);
            if (sync.RepositoriesFailed)
            {
                return at is DateTimeOffset last
                    ? "Couldn't check just now; last checked " + UsageLeftPresenter.When(last, now, zone)
                    : "Couldn't check just now";
            }
            return at is DateTimeOffset checkedAt ? "Checked " + UsageLeftPresenter.When(checkedAt, now, zone) : "Not checked yet";
        }

        /// <summary>A time as the source wrote it, or null when it wrote none or one this app can't read.</summary>
        internal static DateTimeOffset? Parse(string? time) =>
            time != null && DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : (DateTimeOffset?)null;
    }
}

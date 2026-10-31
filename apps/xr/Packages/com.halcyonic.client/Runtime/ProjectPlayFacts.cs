#nullable enable
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>Whether Play on Laptop is offered for a project now, and if not, the one fact that stops it.</summary>
    public enum PlayOnLaptop
    {
        /// <summary>Every fact the host admits it on holds; the host may still refuse it.</summary>
        Offered,
        /// <summary>The host has recorded no check of the project.</summary>
        NoCheck,
        /// <summary>The latest check did not pass.</summary>
        CheckNotPassed,
        /// <summary>The latest check did not see the game work with a mouse and keyboard, or did not try.</summary>
        NotReadyOnLaptop,
        /// <summary>Not every agent run in the project was confined (S2).</summary>
        NotConfined,
        /// <summary>No preview of the latest check runs.</summary>
        NoPreviewOfCheck,
        /// <summary>The game already plays on the computer.</summary>
        Playing,
        /// <summary>The host has no browser set up for it (it said capability_unsupported): unavailable, not an error.</summary>
        Unavailable,
    }

    /// <summary>Whether a check's browser had none of the app's macOS permissions (S1).</summary>
    public enum CheckPermissions
    {
        /// <summary>The check recorded nothing about it, as checks before S1 did.</summary>
        Unknown,
        /// <summary>It ran as its own process for macOS privacy permissions.</summary>
        OwnPermissions,
        /// <summary>It ran with the permissions of the app that started Halcyonic.</summary>
        AppPermissions,
    }

    /// <summary>
    /// The facts a starter project's screen shows about its check, its preview and Play on Laptop
    /// (M1.8e), read only from the project's view as the host sent it: nothing is derived from events,
    /// an absent member is never read as false, and Play on Laptop is offered only on the host's own
    /// admission facts (DOMAIN_MODEL.md: a passed check that saw the game work with a mouse and
    /// keyboard and found every agent run confined, served by the running preview, with no play
    /// running). The host decides; this only keeps the screen from offering what it would refuse.
    /// </summary>
    public static class ProjectPlayFacts
    {
        /// <summary>The project's latest check as the host recorded it, or null when it has none.</summary>
        public static ProjectViewCheck? LatestCheck(ProjectView project) => project.Check;

        /// <summary>The running preview when it serves the latest check, else null.</summary>
        public static ProjectViewPreview? PreviewOfLatestCheck(ProjectView project) =>
            project.Check != null && project.Preview != null && project.Preview.CheckId == project.Check.CheckId
                ? project.Preview
                : null;

        /// <summary>Whether the game plays on the computer now, as the host said.</summary>
        public static bool IsPlaying(ProjectView project) => project.MacPlay != null;

        /// <summary>What the latest check recorded about its browser's permissions.</summary>
        public static CheckPermissions BrowserPermissions(ProjectView project) =>
            project.Check?.Findings.OwnPermissions switch
            {
                true => CheckPermissions.OwnPermissions,
                false => CheckPermissions.AppPermissions,
                null => CheckPermissions.Unknown,
            };

        /// <summary>
        /// Whether Play on Laptop is offered, from the project's view and the last Play on Laptop this
        /// client sent for it, if any: a host that refused it as capability_unsupported has no browser
        /// for it, which reads as unavailable until another attempt says otherwise.
        /// </summary>
        public static PlayOnLaptop PlayOnLaptopFor(ProjectView project, CommandView? lastAttempt = null)
        {
            if (lastAttempt != null
                && lastAttempt.CommandType == CommandType.ProjectOpenOnMac
                && lastAttempt.Status == CommandStatus.Rejected
                && lastAttempt.Rejection?.Code == RejectionCode.CapabilityUnsupported)
            {
                return PlayOnLaptop.Unavailable;
            }
            var check = project.Check;
            if (check == null) return PlayOnLaptop.NoCheck;
            if (!check.Findings.Passed) return PlayOnLaptop.CheckNotPassed;
            if (check.Findings.Laptop?.Ready != true) return PlayOnLaptop.NotReadyOnLaptop;
            if (!check.Findings.ReadsConfined) return PlayOnLaptop.NotConfined;
            if (PreviewOfLatestCheck(project) == null) return PlayOnLaptop.NoPreviewOfCheck;
            if (IsPlaying(project)) return PlayOnLaptop.Playing;
            return PlayOnLaptop.Offered;
        }
    }
}

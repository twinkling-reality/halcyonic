#nullable enable
using System;
using System.Globalization;

namespace Halcyonic.Client
{
    /// <summary>
    /// The words of Connect a folder (WORDS.md): the folders on the person's computer that no project
    /// uses yet, what is known about each, and how connecting one went. A folder's facts say only what
    /// the computer read from the folder's own entry, and "changed" is the latest change it saw there.
    /// Folder and root names are text from outside, shown by <see cref="LabelText"/>'s rule.
    /// </summary>
    public static class ConnectText
    {
        public const string ConnectFolder = "Connect Folder";
        public const string FoldersLine = "Unused Folders · Connecting Changes Nothing Inside";
        public const string NoFreeFolders = "No Free Folders · New Project";
        public const string Connect = "Connect";
        public const string Connecting = "Sent · Awaiting Computer";
        public const string NotConnectedYet = "Not Sent · Not Connected · Try Again";
        public const string Gone = "Folder Unavailable · Choose Another";
        public const string LooksLikeAnother = "Similar Name";

        /// <summary>Under a folder whose shown name looks like another's: how to tell them apart.</summary>
        public const string CheckWhichOne = "Similar Name · Check Place and Date";

        /// <summary>Connect waits while another folder's connection may still be on its way.</summary>
        /// <remarks>It stands where a prompt's reason does, which holds Halcyonic's words alone, so it names no folder (settled by the coordinator, 2026-10-08).</remarks>
        public static string WaitingOn(ConnectableFolder other) =>
            "Awaiting Other Folder · Connect Waits";

        /// <summary>
        /// Connect waits on another folder's connection: still on its way, or sent with an outcome nobody
        /// can know any more, which is said as such rather than as still coming.
        /// </summary>
        public static string WaitingOn(FolderConnection other) => other.Step.Status == BuildStepStatus.Waiting
            ? WaitingOn(other.Folder)
            : "Other Folder Unconfirmed · Check Projects · Connect Waits";

        /// <summary>A name from outside inside one of Halcyonic's sentences, leaning on Halcyonic's ground so it never reads as Halcyonic's words.</summary>
        public static LineWords Named(string name) => LineWords.TheirName(WorkspaceText.Truncate(LabelText.Name(name), NameLimit));

        /// <summary>The most of a name a sentence quotes, so Halcyonic's words after it, as what connecting changes, always show whole.</summary>
        public const int NameLimit = 40;

        /// <summary>
        /// What is known about a folder, in one line: "Repository · changed 3 days ago", "Changed 3 days
        /// ago", or that the computer couldn't look inside it.
        /// </summary>
        public static string Facts(ConnectableFolder folder, DateTimeOffset now, TimeZoneInfo zone)
        {
            if (folder.Repository == null) return "Contents Unreadable";
            var changed = folder.ChangedAt == null ? null : "Changed " + Ago(folder.ChangedAt.Value, now, zone);
            if (folder.Repository == true) return changed == null ? "Repository" : "Repository · " + changed;
            return changed ?? "Folder";
        }

        /// <summary>A row's detail: its facts, and the place it is in when there is more than one.</summary>
        public static string Detail(ConnectableFolder folder, DateTimeOffset now, TimeZoneInfo zone, bool manyPlaces) =>
            (folder.LooksLikeAnother ? LooksLikeAnother + " · " : "") + Facts(folder, now, zone) + (manyPlaces ? " · In " + folder.RootName : "");

        /// <summary>Above Connect: where the folder is, and what connecting does.</summary>
        /// <remarks>What connecting never changes comes first, so no name, however long, pushes it past the line's end.</remarks>
        public static string WhatConnectingDoes(ConnectableFolder folder) => WhatConnectingDoesRuns(folder).Text;

        /// <summary>Above Connect, as runs: Halcyonic's words upright, the folder's, place's and project's names leaning.</summary>
        public static LineWords WhatConnectingDoesRuns(ConnectableFolder folder) =>
            LineWords.Ours("Unchanged Until a Task · ")
            + (folder.Folder == null ? LineWords.Ours("Folder ") + Named(folder.RawName) : LineWords.Ours("In ") + Named(ProjectFolder.LabelOf(folder.Root)))
            + LineWords.Ours(" · New Project · ") + Named(folder.ProjectName);

        /// <summary>How connecting went: sent is not done, and only the completed record confirms it.</summary>
        public static string Outcome(FolderConnection connection) => OutcomeRuns(connection).Text;

        /// <summary>How connecting went, as runs: a project's name leaning, everything else Halcyonic's.</summary>
        public static LineWords OutcomeRuns(FolderConnection connection)
        {
            var step = connection.Step;
            if (connection.Connected)
            {
                return LineWords.Ours("Connected · ") + Named(connection.Folder.ProjectName) + LineWords.Ours(" · Add Task");
            }
            LineWords Ours(string words) => LineWords.Written(words);
            return step.Status switch
            {
                BuildStepStatus.Waiting => Ours(Connecting),
                // Why, by its code, never the control plane's message.
                BuildStepStatus.Refused when step.Refusal == Halcyonic.Contracts.RejectionCode.DeviceRevoked => Ours(ConnectionText.PairingRefused),
                BuildStepStatus.Refused => Ours(WorkspaceText.Couldnt("Connect Refused", WorkspaceText.WhyRefused(step.Refusal))),
                // A failure that may have had an effect is never put in words that say nothing happened.
                BuildStepStatus.Failed when !step.EffectUnknown => Ours(WorkspaceText.Couldnt("Connect Failed", (EntryText.FolderProblem(step.Refusal, null) ?? WorkspaceText.WhyFailed(step.Failure, running: false, step: true)) ?? WorkspaceText.NothingChanged)),
                BuildStepStatus.NotSent => Ours(NotConnectedYet),
                _ => Ours("Connect Unconfirmed · ") + Named(connection.Folder.ProjectName) + Ours(" · Check Projects"),
            };
        }

        /// <summary>How long ago, by this device's clock: "just now", "5 minutes ago", "3 days ago", "on 4 Mar 2025" in the person's zone.</summary>
        public static string Ago(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
        {
            var elapsed = now - at;
            if (elapsed < TimeSpan.Zero) return TimeZoneInfo.ConvertTime(at, zone).ToString("d MMM yyyy", CultureInfo.InvariantCulture);
            if (elapsed < TimeSpan.FromMinutes(1)) return "Just Now";
            if (elapsed < TimeSpan.FromHours(1)) return Count((long)elapsed.TotalMinutes, "Minute") + " Ago";
            if (elapsed < TimeSpan.FromDays(1)) return Count((long)elapsed.TotalHours, "Hour") + " Ago";
            if (elapsed < TimeSpan.FromDays(60)) return Count((long)elapsed.TotalDays, "Day") + " Ago";
            return TimeZoneInfo.ConvertTime(at, zone).ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        }

        private static string Count(long count, string noun) =>
            count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? noun : noun + "s");
    }
}

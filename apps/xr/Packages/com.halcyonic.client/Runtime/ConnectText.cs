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
        public const string ConnectFolder = "Connect a folder";
        public const string FoldersLine = "Folders on " + HostText.Your + " that no project uses yet. Connecting one makes a project for it and changes nothing inside it.";
        public const string NoFreeFolders = "Every folder " + HostText.Your + " allows is a project already. To start a new one, create a project.";
        public const string Connect = "Connect";
        public const string Connecting = "Sent. Waiting for " + HostText.Your + "…";
        public const string NotConnectedYet = "Couldn't connect: " + HostText.Your + " isn't connected. Try again when it is.";
        public const string Gone = "This folder isn't offered any more: a project may use it now, or it moved. Choose another.";
        public const string LooksLikeAnother = "Look-alike name";

        /// <summary>Under a folder whose shown name looks like another's: how to tell them apart.</summary>
        public const string CheckWhichOne = "Another folder or project has a name that looks the same. Check where it is and when it changed to be sure it's the one you mean.";

        /// <summary>Connect waits while another folder's connection may still be on its way.</summary>
        public static string WaitingOn(ConnectableFolder other) =>
            "Still waiting to hear whether " + Quoted(other.ProjectName) + " was connected. Connecting another folder waits until that's known.";

        /// <summary>
        /// Connect waits on another folder's connection: still on its way, or sent with an outcome nobody
        /// can know any more, which is said as such rather than as still coming.
        /// </summary>
        public static string WaitingOn(FolderConnection other) => other.Step.Status == BuildStepStatus.Waiting
            ? WaitingOn(other.Folder)
            : "Not sure whether " + Quoted(other.Folder.ProjectName) + " was connected. Look for it in Projects. Connecting another folder waits until that's known, or until you restart.";

        /// <summary>A name from outside inside one of Halcyonic's sentences, quoted so it never reads as Halcyonic's words.</summary>
        public static string Quoted(string name) => "\u201C" + LabelText.Name(name) + "\u201D";

        /// <summary>
        /// What is known about a folder, in one line: "Repository · changed 3 days ago", "Changed 3 days
        /// ago", or that the computer couldn't look inside it.
        /// </summary>
        public static string Facts(ConnectableFolder folder, DateTimeOffset now, TimeZoneInfo zone)
        {
            if (folder.Repository == null) return HostText.YourStart + " can't look inside it";
            var changed = folder.ChangedAt == null ? null : "changed " + Ago(folder.ChangedAt.Value, now, zone);
            if (folder.Repository == true) return changed == null ? "Repository" : "Repository · " + changed;
            return changed == null ? "Folder" : char.ToUpperInvariant(changed[0]) + changed.Substring(1);
        }

        /// <summary>A row's detail: its facts, and the place it is in when there is more than one.</summary>
        public static string Detail(ConnectableFolder folder, DateTimeOffset now, TimeZoneInfo zone, bool manyPlaces) =>
            (folder.LooksLikeAnother ? LooksLikeAnother + " · " : "") + Facts(folder, now, zone) + (manyPlaces ? " · in " + folder.RootName : "");

        /// <summary>Above Connect: where the folder is, and what connecting does.</summary>
        public static string WhatConnectingDoes(ConnectableFolder folder) =>
            (folder.Folder == null ? "The folder " + Quoted(folder.RawName) + " itself" : "In " + Quoted(ProjectFolder.LabelOf(folder.Root)))
            + ". Connecting makes a project called " + Quoted(folder.ProjectName)
            + " that works in this folder. Nothing in it changes until you add a task.";

        /// <summary>How connecting went: sent is not done, and only the completed record confirms it.</summary>
        public static string Outcome(FolderConnection connection)
        {
            var step = connection.Step;
            if (connection.Connected) return "Connected: " + Quoted(connection.Folder.ProjectName) + " is a project now. Add a task to start work in it.";
            return step.Status switch
            {
                BuildStepStatus.Waiting => Connecting,
                // Why, by its code, never the control plane's message.
                BuildStepStatus.Refused => WorkspaceText.Couldnt("Couldn't connect", WorkspaceText.WhyRefused(step.Refusal)),
                // A failure that may have had an effect is never put in words that say nothing happened.
                BuildStepStatus.Failed when !step.EffectUnknown => WorkspaceText.Couldnt("Couldn't connect", (EntryText.FolderProblem(step.Refusal, null) ?? WorkspaceText.WhyFailed(step.Failure, running: false)) ?? WorkspaceText.NothingChanged),
                BuildStepStatus.NotSent => NotConnectedYet,
                _ => "Not sure whether " + Quoted(connection.Folder.ProjectName) + " was connected. Look for it in Projects.",
            };
        }

        /// <summary>How long ago, by this device's clock: "just now", "5 minutes ago", "3 days ago", "on 4 Mar 2025" in the person's zone.</summary>
        public static string Ago(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
        {
            var elapsed = now - at;
            if (elapsed < TimeSpan.Zero) return "on " + TimeZoneInfo.ConvertTime(at, zone).ToString("d MMM yyyy", CultureInfo.InvariantCulture);
            if (elapsed < TimeSpan.FromMinutes(1)) return "just now";
            if (elapsed < TimeSpan.FromHours(1)) return Count((long)elapsed.TotalMinutes, "minute") + " ago";
            if (elapsed < TimeSpan.FromDays(1)) return Count((long)elapsed.TotalHours, "hour") + " ago";
            if (elapsed < TimeSpan.FromDays(60)) return Count((long)elapsed.TotalDays, "day") + " ago";
            return "on " + TimeZoneInfo.ConvertTime(at, zone).ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        }

        private static string Count(long count, string noun) =>
            count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? noun : noun + "s");
    }
}

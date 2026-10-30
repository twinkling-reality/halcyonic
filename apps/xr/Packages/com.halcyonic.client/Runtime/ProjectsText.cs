#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// The words of the menu's Projects place (ADR 0026, WORDS.md): its subject, the heading over the
    /// folders, the names of a side panel's facts and the footer's prompts. Connecting a folder keeps
    /// <see cref="ConnectText"/>'s words; a project's work keeps <see cref="EntryText"/>'s.
    /// </summary>
    public static class ProjectsText
    {
        /// <summary>
        /// Projects' subject, the place's purpose, which New project and the rows answer. It keeps to one
        /// line of its column: 21.9 of 29.2 degrees in the component render.
        /// </summary>
        public const string Subject = "Projects";

        public const string FoldersHeading = "Folders";
        public const string NewProject = "New Project";
        public const string ShowOnStage = "Show on Stage";
        public const string HideFromStage = "Hide from Stage";
        public const string Close = "Close";

        // The names of a side panel's facts.
        public const string ItsWork = "Work";
        public const string OnTheStage = "On Stage";
        public const string Place = "Place";
        public const string Repository = "Repository";
        public const string Changed = "Changed";
        public const string ItsName = "Name";
        public const string Connecting = "Connecting";
        public const string WhatHappened = "What Happened";

        // Why no folder is listed: a row, and its side panel's subject; Try again stands in the footer.
        public const string FoldersUnread = "Folders Unreadable";
        public const string NoFolders = "No Folders Allowed";
        public const string AllowAFolder = "Allow on Computer · Try Again";

        public const string Shown = "Shown";
        public const string Hidden = "Hidden";
        public const string Yes = "Yes";
        public const string No = "No";
        public const string CantTell = "Contents Unreadable";

        /// <summary>Local recovery when supplied physical measurements cannot hold the complete project rows.</summary>
        public const string MoreRoom = "No Room · Move Workspace";

        /// <summary>Under a project whose shown name looks like another project's or a free folder's.</summary>
        public const string ProjectLooksAlike = "Similar Name Exists · Check Its Work";

        /// <summary>
        /// A project's small fact on its row: what matters most, what waits for the person first, as a
        /// short count that keeps its noun ("1 task waiting", "2 tasks running", "1 task to look at" for
        /// work that failed, can't be told or failed its checks; at rest, "1 task paused" for work that ran,
        /// else "3 tasks not started"), after "Hidden · " when its work is not
        /// on the stage, and after the look-alike mark when its name looks like another's. A long name
        /// shortens to make room, never this.
        /// </summary>
        public static string ProjectFact(ProjectSummary project, bool looksAlike = false)
        {
            var most = project.NeedsYou > 0 ? "Needs You · " + Tasks(project.NeedsYou)
                : project.Notice > 0 ? "Ready · " + Tasks(project.Notice)
                : project.Active > 0 ? "Running · " + Tasks(project.Active)
                : project.Work == 0 ? NoWork
                : project.Work > project.NotStarted ? "Paused · " + Tasks(project.Work - project.NotStarted)
                : "Not Started · " + Tasks(project.NotStarted);
            var fact = project.Shown ? most : Hidden + " · " + most;
            return looksAlike ? ConnectText.LooksLikeAnother + " · " + fact : fact;
        }

        /// <summary>
        /// A project's work in full, for its side panel: every task, what waits for the person first,
        /// then what to look at, what runs, what ran and rests, and what never started, naming tasks
        /// once: "1 task waiting for you, 2 paused", "2 tasks running, 1 not started", at most "1 task
        /// waiting for you, 1 to look at, 1 running, 1 paused, 1 not started"; or "No work yet".
        /// </summary>
        public static string Work(ProjectSummary project)
        {
            if (project.Work == 0) return NoWork;
            var parts = new System.Collections.Generic.List<string>();
            string Counted(int count) => count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var paused = project.Work - project.NeedsYou - project.Notice - project.Active - project.NotStarted;
            if (project.NeedsYou > 0) parts.Add("Needs You · " + Counted(project.NeedsYou));
            if (project.Notice > 0) parts.Add("Ready · " + Counted(project.Notice));
            if (project.Active > 0) parts.Add("Running · " + Counted(project.Active));
            if (paused > 0) parts.Add("Paused · " + Counted(paused));
            if (project.NotStarted > 0) parts.Add("Not Started · " + Counted(project.NotStarted));
            return string.Join(" · ", parts);
        }

        /// <summary>A project with no tasks yet.</summary>
        public const string NoWork = "No Work";

        private static string Tasks(int count) =>
            count.ToString(System.Globalization.CultureInfo.InvariantCulture) + (count == 1 ? " Task" : " Tasks");

        /// <summary>
        /// A folder's small fact on its row: its place where there are several, or that it is a
        /// repository, then when it changed ("Repository · changed 3 days ago", "In Work · changed 5
        /// hours ago"), after the look-alike mark when its name looks like another's, since the place
        /// and the time are what tell look-alikes apart. <c>IsData</c> when it holds a place's name.
        /// </summary>
        public static (string? Text, bool IsData) FolderFact(ConnectableFolder folder, DateTimeOffset now, TimeZoneInfo zone, bool manyPlaces) =>
            FolderFactRuns(folder, now, zone, manyPlaces) is LineWords fact ? (fact.Text, fact.AnyTheirs) : ((string?)null, false);

        /// <summary>A folder's fact as runs: Halcyonic's words upright, begun with a capital, the place's name leaning.</summary>
        public static LineWords? FolderFactRuns(ConnectableFolder folder, DateTimeOffset now, TimeZoneInfo zone, bool manyPlaces)
        {
            var parts = new List<LineWords>();
            // Every part begins with Halcyonic's words, so the fact's first letter is always Halcyonic's to capitalise.
            LineWords Lead(string words) => LineWords.Ours(parts.Count == 0 ? Sentence(words) : words);
            if (folder.LooksLikeAnother) parts.Add(Lead(ConnectText.LooksLikeAnother));
            if (folder.Repository == null) parts.Add(Lead(CantTell));
            else if (manyPlaces) parts.Add(Lead("In ") + LineWords.TheirName(folder.RootName));
            else if (folder.Repository == true) parts.Add(Lead(Repository));
            if (folder.ChangedAt != null) parts.Add(Lead("Changed " + ConnectText.Ago(folder.ChangedAt.Value, now, zone)));
            return parts.Count == 0 ? null : parts.Aggregate((said, next) => said + LineWords.Ours(" · ") + next);
        }

        /// <summary>A sentence of Halcyonic's own, begun with a capital, as a side panel's value.</summary>
        public static string Sentence(string text) =>
            text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}

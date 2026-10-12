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

        public const string FoldersHeading = "Folders on " + HostText.Your;
        public const string NewProject = "New project";
        public const string ShowOnStage = "Show on stage";
        public const string HideFromStage = "Hide from stage";
        public const string Close = "Close";

        // The names of a side panel's facts.
        public const string ItsWork = "Its work";
        public const string OnTheStage = "On the stage";
        public const string Place = "Place";
        public const string Repository = "Repository";
        public const string Changed = "Changed";
        public const string ItsName = "Its name";
        public const string Connecting = "Connecting";
        public const string WhatHappened = "What happened";

        // Why no folder is listed: a row, and its side panel's subject; Try again stands in the footer.
        public const string FoldersUnread = "Couldn't read " + HostText.Your + "'s folders";
        public const string NoFolders = "No folders are approved for projects";
        public const string AllowAFolder = "Approve a project folder on " + HostText.Your + ", then try again.";

        public const string Shown = "Shown";
        public const string Hidden = "Hidden";
        public const string Yes = "Yes";
        public const string No = "No";
        public const string CantTell = HostText.YourStart + " can't look inside it";

        /// <summary>Under a project whose shown name looks like another project's or a free folder's.</summary>
        public const string ProjectLooksAlike = "Another project or folder has a name that looks the same. Check its work to be sure it's the one you mean.";

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
            var most = project.NeedsYou > 0 ? Tasks(project.NeedsYou) + " waiting"
                : project.Notice > 0 ? Tasks(project.Notice) + " to look at"
                : project.Active > 0 ? Tasks(project.Active) + " running"
                : project.Work == 0 ? "No work yet"
                : project.Work > project.NotStarted ? Tasks(project.Work - project.NotStarted) + " paused"
                : Tasks(project.NotStarted) + " not started";
            var fact = project.Shown ? most : Hidden + " · " + (project.Work == 0 ? "no work yet" : most);
            return looksAlike ? ConnectText.LooksLikeAnother + " · " + Lower(fact) : fact;
        }

        /// <summary>
        /// A project's work in full, for its side panel: every task, what waits for the person first,
        /// then what to look at, what runs, what ran and rests, and what never started, naming tasks
        /// once: "1 task waiting for you, 2 paused", "2 tasks running, 1 not started", at most "1 task
        /// waiting for you, 1 to look at, 1 running, 1 paused, 1 not started"; or "No work yet".
        /// </summary>
        public static string Work(ProjectSummary project)
        {
            if (project.Work == 0) return "No work yet";
            var parts = new System.Collections.Generic.List<string>();
            string Counted(int count) => parts.Count == 0 ? Tasks(count) : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var paused = project.Work - project.NeedsYou - project.Notice - project.Active - project.NotStarted;
            if (project.NeedsYou > 0) parts.Add(Counted(project.NeedsYou) + " waiting for you");
            if (project.Notice > 0) parts.Add(Counted(project.Notice) + " to look at");
            if (project.Active > 0) parts.Add(Counted(project.Active) + " running");
            if (paused > 0) parts.Add(Counted(paused) + " paused");
            if (project.NotStarted > 0) parts.Add(Counted(project.NotStarted) + " not started");
            return string.Join(", ", parts);
        }

        private static string Lower(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text.Substring(1);

        private static string Tasks(int count) =>
            count.ToString(System.Globalization.CultureInfo.InvariantCulture) + (count == 1 ? " task" : " tasks");

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
            if (folder.Repository == null) parts.Add(Lead(Lower(CantTell)));
            else if (manyPlaces) parts.Add(Lead("in ") + LineWords.TheirName(folder.RootName));
            else if (folder.Repository == true) parts.Add(Lead(Repository));
            if (folder.ChangedAt != null) parts.Add(Lead("changed " + ConnectText.Ago(folder.ChangedAt.Value, now, zone)));
            return parts.Count == 0 ? null : parts.Aggregate((said, next) => said + LineWords.Ours(" · ") + next);
        }

        /// <summary>A sentence of Halcyonic's own, begun with a capital, as a side panel's value.</summary>
        public static string Sentence(string text) =>
            text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}

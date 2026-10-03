#nullable enable
using System;
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
        public const string Subject = "What would you like to work on?";

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
        public const string NoFolders = HostText.YourStart + " doesn't allow any folder yet";
        public const string AllowAFolder = "Allow a folder on " + HostText.Your + ", then try again.";

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
        /// work that failed, can't be told or failed its checks), after "Hidden · " when its work is not
        /// on the stage, and after the look-alike mark when its name looks like another's. A long name
        /// shortens to make room, never this.
        /// </summary>
        public static string ProjectFact(ProjectSummary project, bool looksAlike = false)
        {
            var most = project.NeedsYou > 0 ? Tasks(project.NeedsYou) + " waiting"
                : project.Notice > 0 ? Tasks(project.Notice) + " to look at"
                : project.Active > 0 ? Tasks(project.Active) + " running"
                : project.Work == 0 ? "No work yet" : Tasks(project.Work) + " paused";
            var fact = project.Shown ? most : Hidden + " · " + (project.Work == 0 ? "no work yet" : most);
            return looksAlike ? ConnectText.LooksLikeAnother + " · " + Lower(fact) : fact;
        }

        /// <summary>
        /// A project's work in full, for its side panel, what waits for the person first, naming tasks
        /// once: "1 task waiting for you, 1 to look at, 2 running", or "No work yet", "3 tasks paused".
        /// </summary>
        public static string Work(ProjectSummary project)
        {
            var parts = new System.Collections.Generic.List<string>();
            string Counted(int count) => parts.Count == 0 ? Tasks(count) : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (project.NeedsYou > 0) parts.Add(Counted(project.NeedsYou) + " waiting for you");
            if (project.Notice > 0) parts.Add(Counted(project.Notice) + " to look at");
            if (project.Active > 0) parts.Add(Counted(project.Active) + " running");
            if (parts.Count > 0) return string.Join(", ", parts);
            return project.Work == 0 ? "No work yet" : Tasks(project.Work) + " paused";
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
        public static (string? Text, bool IsData) FolderFact(ConnectableFolder folder, DateTimeOffset now, TimeZoneInfo zone, bool manyPlaces)
        {
            var mark = folder.LooksLikeAnother ? ConnectText.LooksLikeAnother : null;
            var first = folder.Repository == null ? Lower(CantTell) : manyPlaces ? "in " + folder.RootName : folder.Repository == true ? Repository : null;
            var changed = folder.ChangedAt == null ? null : "changed " + ConnectText.Ago(folder.ChangedAt.Value, now, zone);
            var fact = string.Join(" · ", new[] { mark, first, changed }.Where(part => part != null));
            return (fact.Length == 0 ? null : Sentence(fact), folder.Repository != null && manyPlaces);
        }

        /// <summary>A sentence of Halcyonic's own, begun with a capital, as a side panel's value.</summary>
        public static string Sentence(string text) =>
            text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}

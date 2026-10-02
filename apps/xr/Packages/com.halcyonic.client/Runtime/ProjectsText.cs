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

        public const string Shown = "Shown";
        public const string Hidden = "Hidden";
        public const string Yes = "Yes";
        public const string No = "No";
        public const string CantTell = HostText.YourStart + " can't look inside it";

        /// <summary>
        /// A project's small fact on its row: what matters most, what waits for the person first, as a
        /// short count that keeps its noun ("1 task waiting", "2 tasks running"), after "Hidden · " when
        /// its work is not on the stage. A long name shortens to make room, never this.
        /// </summary>
        public static string ProjectFact(ProjectSummary project)
        {
            var most = project.NeedsYou > 0 ? Tasks(project.NeedsYou) + " waiting"
                : project.Notice > 0 ? Tasks(project.Notice) + " finished"
                : project.Active > 0 ? Tasks(project.Active) + " running"
                : project.Work == 0 ? "No work yet" : Tasks(project.Work) + " paused";
            return project.Shown ? most : Hidden + " · " + (project.Work == 0 ? "no work yet" : most);
        }

        private static string Tasks(int count) =>
            count.ToString(System.Globalization.CultureInfo.InvariantCulture) + (count == 1 ? " task" : " tasks");

        /// <summary>
        /// A folder's small fact on its row, short: the look-alike mark when its name looks like
        /// another's; else its place where there are several, or that it is a repository, then when it
        /// changed ("Repository · changed 3 days ago", "In Work · changed today"). Its side panel says
        /// the rest.
        /// </summary>
        public static string? FolderFact(ConnectableFolder folder, DateTimeOffset now, TimeZoneInfo zone, bool manyPlaces)
        {
            if (folder.LooksLikeAnother) return ConnectText.LooksLikeAnother;
            if (folder.Repository == null) return CantTell;
            var first = manyPlaces ? "in " + folder.RootName : folder.Repository == true ? Repository : null;
            var changed = folder.ChangedAt == null ? null : "changed " + ConnectText.Ago(folder.ChangedAt.Value, now, zone);
            var fact = string.Join(" · ", new[] { first, changed }.Where(part => part != null));
            return fact.Length == 0 ? null : Sentence(fact);
        }

        /// <summary>A sentence of Halcyonic's own, begun with a capital, as a side panel's value.</summary>
        public static string Sentence(string text) =>
            text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}

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

        /// <summary>Pairing from the first screen (LINK's cut, 2026-11-12): the setting's own words (SpaceSettings).</summary>
        public const string PairComputer = "Pair Computer";
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
        public const string AllowAFolder = "Allow a folder on your computer and try again.";

        /// <summary>The problems as a card's status lines (FIELD, 2026-11-12), the redesign's in place of the side panel.</summary>
        public const string FoldersUnreadLine = "Your computer couldn't list its folders";

        public const string NoFoldersLine = "Allow a folder on your computer, then try again";

        public const string Shown = "Shown";
        public const string Hidden = "Hidden";
        public const string Yes = "Yes";
        public const string No = "No";
        public const string CantTell = "Contents Unreadable";

        /// <summary>Local recovery when supplied physical measurements cannot hold the complete project rows.</summary>
        /// <summary>
        /// A page with room for no row: the notice alone, "No Room" (WORDS.md), never joined to its way on. Moving the workspace is the
        /// chrome's own Move where the older view draws this; under the redesign a standing character no longer shrinks a page, so it
        /// is left for a room truly too small.
        /// </summary>
        public const string MoreRoom = "No Room";

        /// <summary>Under a project whose shown name looks like another project's or a free folder's.</summary>
        public const string ProjectLooksAlike = "A project has a similar name. Check its work.";

        /// <summary>
        /// A project's small fact on its row: what matters most, what waits for the person first, as a
        /// plain phrase that keeps its noun (<see cref="Phrase"/>: "2 tasks need you", "1 task ready", "3 tasks working",
        /// "1 task paused" for work that ran, else "3 tasks not started"), after "Hidden." when its work is not on the
        /// stage, and after "Similar Name." when its name looks like another's. A long name shortens to make room, never this.
        /// </summary>
        /// <summary>The project's state apart (A17): the word of what matters most, how many tasks it counts, and whether the project is on the stage.</summary>
        public static ProjectState StateOf(ProjectSummary project) =>
            project.NeedsYou > 0 ? new ProjectState("Needs You", project.NeedsYou, project.Shown, project.Work, needsYou: true)
            : project.Notice > 0 ? new ProjectState("Ready", project.Notice, project.Shown, project.Work)
            : project.Active > 0 ? new ProjectState("Working", project.Active, project.Shown, project.Work, running: true)
            : project.Work == 0 ? new ProjectState(NoWork, 0, project.Shown, 0)
            : project.Work > project.NotStarted ? new ProjectState("Paused", project.Work - project.NotStarted, project.Shown, project.Work)
            : new ProjectState("Not Started", project.NotStarted, project.Shown, project.Work);

        public static string ProjectFact(ProjectSummary project, bool looksAlike = false)
        {
            // A row's one fact, as a plain phrase ("2 tasks need you"), never parts on a dot; hidden and the look-alike mark
            // are their own sentences before it.
            var state = StateOf(project);
            var most = project.Work == 0 ? NoWork : Phrase(state.Word, state.Tasks);
            var fact = project.Shown ? most : Hidden + ". " + most;
            return looksAlike ? ConnectText.LooksLikeAnother + ". " + fact : fact;
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
            // One line where one is all the place holds: a plain phrase, never facts on a dot (the owner's ban, 2026-11-11).
            return string.Join(", ", WorkFacts(project).Select(fact => Phrase(fact.Name, fact.Count)));
        }

        /// <summary>
        /// A project's work as facts apart (AUDIT and AUDIT2, 2026-11-16 10:48Z): each state its own label ("Needs You") and its
        /// value ("2 Tasks"), what waits for the person first, so a view lays the label small and the value strong. None for no work.
        /// </summary>
        public static IReadOnlyList<(string Name, string Value, int Count)> WorkFacts(ProjectSummary project)
        {
            var facts = new List<(string Name, string Value, int Count)>();
            var paused = project.Work - project.NeedsYou - project.Notice - project.Active - project.NotStarted;
            void Add(string name, int count) { if (count > 0) facts.Add((name, Tasks(count), count)); }
            Add("Needs You", project.NeedsYou);
            Add("Ready", project.Notice);
            Add("Working", project.Active);
            Add("Paused", paused);
            Add("Not Started", project.NotStarted);
            return facts;
        }

        /// <summary>A state's count as a plain phrase: "1 task needs you", "2 tasks need you", "3 tasks working".</summary>
        public static string Phrase(string word, int count)
        {
            var tasks = count.ToString(System.Globalization.CultureInfo.InvariantCulture) + (count == 1 ? " task " : " tasks ");
            return tasks + (word == "Needs You" ? (count == 1 ? "needs you" : "need you") : word.ToLowerInvariant());
        }

        /// <summary>A project with no tasks yet.</summary>
        public const string NoWork = "No Work";

        public static string Tasks(int count) =>
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

    /// <summary>A project's state apart, never joined (A17): the word, the tasks it counts, whether it is on the stage, whether it waits on the person.</summary>
    public sealed class ProjectState
    {
        public ProjectState(string word, int tasks, bool shown, int work = 0, bool needsYou = false, bool running = false)
        {
            Word = word;
            Tasks = tasks;
            Shown = shown;
            Work = work;
            NeedsYou = needsYou;
            Running = running;
        }

        /// <summary>Every task the project counts.</summary>
        public int Work { get; }

        /// <summary>A task of it is under way.</summary>
        public bool Running { get; }

        /// <summary>
        /// The row's one note on the Projects card (FIELD's parts note): "Needs You" where a task waits on the person, else
        /// "Working" where one is under way, else the count of its tasks, else none. Never a state and a count together.
        /// </summary>
        public string? Note => NeedsYou ? Word : Running ? Word : Work > 0 ? ProjectsText.Tasks(Work) : null;

        public string Word { get; }

        public int Tasks { get; }

        public bool Shown { get; }

        public bool NeedsYou { get; }

        /// <summary>"1 Task", "3 Tasks"; null where it counts none.</summary>
        public string? TasksWords => Tasks > 0 ? ProjectsText.Tasks(Tasks) : null;
    }
}

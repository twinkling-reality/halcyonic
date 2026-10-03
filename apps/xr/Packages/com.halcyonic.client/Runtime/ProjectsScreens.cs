#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The menu's Projects place as a <see cref="MenuFrame"/> (ADR 0026): the projects Halcyonic's
    /// journal knows, then the folders on the person's computer that no project uses
    /// (<see cref="FolderConnect"/>). A row only takes the person somewhere: choosing one lights it and
    /// slides its side panel out, the project's work or the folder's facts and what connecting does,
    /// and the footer then carries what can be done with it, its main action always at the far right,
    /// never where the row's press landed. With no row chosen, New project is the main action.
    /// Connecting keeps <see cref="FolderConnection"/>'s rules: sent is not done, and a connection that
    /// may have run is never sent again. Nothing here sends anything.
    /// </summary>
    public static class ProjectsScreens
    {
        // What each line and prompt raises, for the menu to act on.
        public const string ChooseProject = "projects-choose-project";
        public const string ChooseFolder = "projects-choose-folder";
        public const string NewProject = "projects-new-project";
        public const string AddTask = "projects-add-task";
        public const string ShowProject = "projects-show-project";
        public const string HideProject = "projects-hide-project";
        public const string Connect = "projects-connect";
        public const string TryAgain = "projects-try-again";
        public const string ChooseAnother = "projects-choose-another";
        public const string ChooseProblem = "projects-choose-folders-problem";
        public const string ReadAgain = "projects-read-again";

        /// <summary>The key of the row that says why no folder is listed.</summary>
        public const string ProblemKey = "folders";

        /// <summary>What Projects shows now, gathered by the menu from the session, the listing and the person's choices.</summary>
        public sealed class State
        {
            /// <summary>Every project and its work, or null before the computer answered.</summary>
            public WorkOverview? Overview { get; set; }

            /// <summary>The host's folders, or null while they are read.</summary>
            public LocationsResponse? Listing { get; set; }

            /// <summary>Why the folders could not be read, as it arrived.</summary>
            public string? ListingProblem { get; set; }

            /// <summary>Connected to a control plane, outside the demonstration.</summary>
            public bool Live { get; set; }

            /// <summary>The recorded demonstration, which connects nothing and starts nothing.</summary>
            public bool Demonstration { get; set; }

            /// <summary>The chosen project's id, or null.</summary>
            public string? ChosenProject { get; set; }

            /// <summary>The chosen folder's key (<see cref="ConnectableFolder.Key"/>), or null.</summary>
            public string? ChosenFolder { get; set; }

            /// <summary>The row that says why no folder is listed is chosen.</summary>
            public bool ChosenProblem { get; set; }

            /// <summary>The person's reading size (<see cref="Comfort.Text"/>), which sets the rows a page (<see cref="MenuFrame.RowsAPage"/>).</summary>
            public TextSize TextSize { get; set; }

            /// <summary>The connection sent last, for whichever folder.</summary>
            public FolderConnection? Connection { get; set; }

            /// <summary>
            /// The folders the projects are bound to now (<c>ProjectView.location.path</c>), so a folder
            /// connected since the listing was read is not offered again before the next read.
            /// </summary>
            public IReadOnlyCollection<string> BoundPaths { get; set; } = Array.Empty<string>();

            /// <summary>
            /// The page asked for, from 0, counted on by each press of Next page: past the last page it
            /// starts again at the first. A chosen row's own page wins.
            /// </summary>
            public int Page { get; set; }

            public DateTimeOffset Now { get; set; }

            public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Utc;
        }

        /// <summary>
        /// What the chosen row is, which the frame shows and every press acts on, so the footer's prompts
        /// and the menu's handling of them never disagree: a project, a folder with its own connection
        /// if one was sent, or the row that says why no folder is listed; none when nothing is chosen.
        /// </summary>
        public sealed class Target
        {
            internal Target(ProjectSummary? project, ConnectableFolder? folder, FolderConnection? connection, bool problem)
            {
                Project = project;
                Folder = folder;
                Connection = connection;
                Problem = problem;
            }

            /// <summary>The chosen project: Hide from stage, Show on stage and Add a task act on it.</summary>
            public ProjectSummary? Project { get; }

            /// <summary>The chosen folder: Connect sends it, Try again sends its connection again.</summary>
            public ConnectableFolder? Folder { get; }

            /// <summary>The chosen folder's own connection, once sent.</summary>
            public FolderConnection? Connection { get; }

            /// <summary>The row that says why no folder is listed: Try again reads the folders again.</summary>
            public bool Problem { get; }

            /// <summary>
            /// The project Add a task adds to: the chosen project, or the project the chosen folder became;
            /// null when Add a task is not offered.
            /// </summary>
            public string? AddTaskTo => Project?.ProjectId ?? (Connection?.Connected == true ? Connection.ProjectId : null);

            /// <summary>That project's name, as the person will read it in New project.</summary>
            public string? AddTaskName => Project?.Name ?? (Connection?.Connected == true ? Connection.Folder.ProjectName : null);
        }

        /// <summary>The chosen row as <see cref="Projects"/> shows it and the menu acts on it.</summary>
        public static Target TargetOf(State state)
        {
            var projects = state.Overview?.Projects ?? (IReadOnlyList<ProjectSummary>)Array.Empty<ProjectSummary>();
            var project = state.ChosenProject == null ? null : projects.FirstOrDefault(each => each.ProjectId == state.ChosenProject);
            // Folders are listed, and so can be chosen, only while connected outside the demonstration.
            var folder = project != null || state.ChosenFolder == null || !state.Live || state.Demonstration
                ? null
                : FolderConnect.Find(OffersOf(state, projects), state.ChosenFolder);
            var connection = folder != null && state.Connection != null && state.Connection.Folder.Key == folder.Key ? state.Connection : null;
            var problem = project == null && folder == null && state.ChosenProblem && FoldersProblem(state) != null;
            return new Target(project, folder, connection, problem);
        }

        /// <summary>
        /// The folders offered: the listing's free ones, each marked when its name looks like another
        /// folder's or a project's, less any folder a project is bound to since the listing was read,
        /// except the chosen folder whose own connection made it a project, which keeps its row and outcome.
        /// </summary>
        private static IReadOnlyList<ConnectableFolder> OffersOf(State state, IReadOnlyList<ProjectSummary> projects)
        {
            if (state.Listing == null) return Array.Empty<ConnectableFolder>();
            var bound = new HashSet<string>(state.BoundPaths, StringComparer.Ordinal);
            var kept = state.Connection != null && state.Connection.Folder.Key == state.ChosenFolder ? state.ChosenFolder : null;
            return FolderConnect.Offers(state.Listing, projects.Select(each => each.Name))
                .Where(offer => offer.Key == kept || !bound.Contains(offer.Folder?.Path ?? offer.Root.Path))
                .ToList();
        }

        public static MenuFrame Projects(State state)
        {
            // The place's purpose as its subject, every visit; the lit place under it already says "Projects".
            var subject = ProjectsText.Subject;
            var projects = state.Overview?.Projects ?? (IReadOnlyList<ProjectSummary>)Array.Empty<ProjectSummary>();
            var offers = OffersOf(state, projects);
            var target = TargetOf(state);
            var project = target.Project;
            var folder = target.Folder;
            var problem = FoldersProblem(state);
            var problemChosen = target.Problem;
            var lines = Lines(state, projects, offers, project, folder, problemChosen);

            var pages = Paginate(lines, MenuFrame.RowsAPage(state.TextSize));
            var page = Math.Max(0, state.Page) % pages.Count;
            // A chosen row shows on its own page, so its side panel slides out beside it.
            var chosenAt = pages.FindIndex(each => each.Any(line => line.Chosen));
            if (chosenAt >= 0) page = chosenAt;

            var close = new Prompt(Footer.Close, ProjectsText.Close, GlazeIcon.Close, PromptKind.Close);
            Footer footer;
            SidePanel? side = null;
            if (project != null)
            {
                var facts = new List<SideFact>
                {
                    new SideFact(ProjectsText.ItsWork, ProjectsText.Work(project)),
                    new SideFact(ProjectsText.OnTheStage, project.Shown ? ProjectsText.Shown : ProjectsText.Hidden),
                };
                if (LooksAlike(project, projects, offers)) facts.Add(new SideFact(ProjectsText.ItsName, ProjectsText.ProjectLooksAlike));
                side = new SidePanel(project.Name, subjectIsData: true, facts: facts);
                footer = new Footer(
                    close,
                    secondary: project.Shown
                        ? new Prompt(HideProject, ProjectsText.HideFromStage, GlazeIcon.ShowAll)
                        : new Prompt(ShowProject, ProjectsText.ShowOnStage, GlazeIcon.ShowAll),
                    farRight: Starting(AddTask, EntryText.AddTask, GlazeIcon.AddTask, state));
            }
            else if (folder != null)
            {
                var own = target.Connection;
                var waitingOn = own == null && state.Connection?.Unresolved != null ? state.Connection : null;
                side = FolderPanel(folder, own, state);
                footer = new Footer(close, farRight: FolderAction(own, waitingOn, state));
            }
            else if (problemChosen)
            {
                side = state.ListingProblem != null && state.Listing == null
                    ? new SidePanel(problem!, facts: new[] { new SideFact(ProjectsText.WhatHappened, LabelText.Plain(state.ListingProblem), valueIsData: true) })
                    : new SidePanel(problem!, lines: new[] { new PageLine(ProjectsText.AllowAFolder, rows: 2) });
                footer = new Footer(close, farRight: new Prompt(ReadAgain, EntryText.TryAgain, GlazeIcon.Refresh, main: true));
            }
            else
            {
                footer = new Footer(close, farRight: new Prompt(NewProject, ProjectsText.NewProject, GlazeIcon.CreateProject, main: true));
            }
            // One prompt pages: Next page, which on the last page reads First page and starts again, at the
            // far right where nothing is the main action, else beside it. It waits while a row is chosen:
            // the row's actions hold both right-hand places, and its row is on this page.
            if (side == null && pages.Count > 1)
            {
                footer = footer.WithNext(new Prompt(Footer.NextPage, Footer.NextPageWords(page, pages.Count), GlazeIcon.Next, PromptKind.NextPage));
            }
            return new MenuFrame(subject, footer, lines: pages[page], side: side);
        }

        /// <summary>
        /// Why the folders could not be listed, as its row and side panel say it, when that is so: they
        /// could not be read, or no folder is allowed yet. Each is a row that opens its side panel, with
        /// Try again as the footer's main action while it is chosen, since a row never acts itself.
        /// </summary>
        private static string? FoldersProblem(State state)
        {
            if (state.Demonstration || !state.Live) return null;
            if (state.Listing == null) return state.ListingProblem == null ? null : ProjectsText.FoldersUnread;
            return state.Listing.Roots.Count == 0 ? ProjectsText.NoFolders : null;
        }

        private static List<PageLine> Lines(State state, IReadOnlyList<ProjectSummary> projects, IReadOnlyList<ConnectableFolder> offers,
            ProjectSummary? chosenProject, ConnectableFolder? chosenFolder, bool problemChosen)
        {
            var lines = new List<PageLine>();
            if (state.Overview == null) lines.Add(Say(EntryText.WaitingForMac));
            foreach (var project in projects)
            {
                lines.Add(new PageLine(project.Name, wordsAreData: true, fact: ProjectsText.ProjectFact(project, LooksAlike(project, projects, offers)),
                    action: ChooseProject, key: project.ProjectId, opens: true, chosen: project == chosenProject));
            }
            // The demonstration connects nothing, so it lists no folders.
            if (state.Demonstration) return lines;
            lines.Add(Say(ProjectsText.FoldersHeading));
            if (!state.Live)
            {
                lines.Add(Say(ConnectText.NotConnectedYet, rows: 2));
                return lines;
            }
            if (FoldersProblem(state) is string problem)
            {
                lines.Add(new PageLine(problem, tone: state.Listing == null ? LineTone.Problem : LineTone.Secondary,
                    action: ChooseProblem, key: ProblemKey, opens: true, chosen: problemChosen));
                return lines;
            }
            var listing = state.Listing;
            if (listing == null)
            {
                lines.Add(Say(EntryText.ReadingFolders));
                return lines;
            }
            if (offers.Count == 0)
            {
                lines.Add(Say(ConnectText.NoFreeFolders, rows: 2));
                return lines;
            }
            var manyPlaces = offers.Select(offer => offer.Root.Path).Distinct(StringComparer.Ordinal).Count() > 1;
            foreach (var offer in offers)
            {
                var (fact, factIsData) = ProjectsText.FolderFact(offer, state.Now, state.Zone, manyPlaces);
                lines.Add(new PageLine(offer.Name, wordsAreData: true, fact: fact, factIsData: factIsData,
                    action: ChooseFolder, key: offer.Key, opens: true, chosen: offer.Key == chosenFolder?.Key));
            }
            if (FolderConnect.AnyCut(listing)) lines.Add(Say(EntryText.FoldersCut, rows: 2));
            return lines;
        }

        /// <summary>A project whose shown name looks like another project's or a free folder's.</summary>
        private static bool LooksAlike(ProjectSummary project, IReadOnlyList<ProjectSummary> projects, IReadOnlyList<ConnectableFolder> offers)
        {
            var likeness = FolderConnect.Likeness(project.Name);
            return projects.Any(other => other != project && FolderConnect.Likeness(other.Name) == likeness)
                || offers.Any(offer => FolderConnect.Likeness(offer.Name) == likeness);
        }

        /// <summary>
        /// Pages of at most <paramref name="rows"/> rows, in order; a heading never ends a page, so it
        /// stays with the first row under it. Always at least one page.
        /// </summary>
        private static List<List<PageLine>> Paginate(List<PageLine> lines, int rows)
        {
            var pages = new List<List<PageLine>> { new List<PageLine>() };
            var used = 0;
            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];
                var heading = line.Words == ProjectsText.FoldersHeading && line.Action == null && index + 1 < lines.Count;
                var needs = line.Rows + (heading ? lines[index + 1].Rows : 0);
                if (used > 0 && used + needs > rows)
                {
                    pages.Add(new List<PageLine>());
                    used = 0;
                }
                pages[pages.Count - 1].Add(line);
                used += line.Rows;
            }
            return pages;
        }

        private static SidePanel FolderPanel(ConnectableFolder folder, FolderConnection? connection, State state)
        {
            var facts = new List<SideFact>
            {
                new SideFact(ProjectsText.Place, folder.RootName, valueIsData: true),
                new SideFact(ProjectsText.Repository, folder.Repository == null ? ProjectsText.CantTell : folder.Repository == true ? ProjectsText.Yes : ProjectsText.No),
                new SideFact(ProjectsText.Changed, folder.ChangedAt == null
                    ? ProjectsText.CantTell
                    : ProjectsText.Sentence(ConnectText.Ago(folder.ChangedAt.Value, state.Now, state.Zone))),
            };
            if (folder.LooksLikeAnother) facts.Add(new SideFact(ProjectsText.ItsName, ConnectText.CheckWhichOne));
            // What connecting will do, until it was sent; then how it went takes its place.
            facts.Add(connection == null
                ? new SideFact(ProjectsText.Connecting, ConnectText.WhatConnectingDoes(folder))
                : new SideFact(ProjectsText.WhatHappened, ConnectText.Outcome(connection)));
            return new SidePanel(folder.Name, subjectIsData: true, facts: facts);
        }

        /// <summary>
        /// The chosen folder's main action: Connect before it is sent; once it is a project, Add a task;
        /// after a refusal, Choose a folder; after a send that never left, Try again. While it may still
        /// be on its way, or may have run, nothing but Close.
        /// </summary>
        private static Prompt? FolderAction(FolderConnection? connection, FolderConnection? waitingOn, State state)
        {
            if (connection == null)
            {
                var reason = !state.Live ? ConnectText.NotConnectedYet : waitingOn != null ? ConnectText.WaitingOn(waitingOn) : null;
                return new Prompt(Connect, ConnectText.Connect, GlazeIcon.ConnectProjects, main: true, available: reason == null, reason: reason);
            }
            if (connection.Connected) return Starting(AddTask, EntryText.AddTask, GlazeIcon.AddTask, state);
            if (!connection.CanRetry) return null;
            return connection.Step.Status == BuildStepStatus.NotSent
                ? new Prompt(TryAgain, EntryText.TryAgain, GlazeIcon.Refresh, main: true, available: state.Live, reason: state.Live ? null : ConnectText.NotConnectedYet)
                : new Prompt(ChooseAnother, EntryText.ChooseAnotherFolder, GlazeIcon.Change, main: true);
        }

        /// <summary>An action that starts work: only while connected, never in the demonstration, with why not.</summary>
        private static Prompt Starting(string id, string words, GlazeIcon icon, State state)
        {
            var reason = state.Demonstration ? EntryText.DemoCannotStart : !state.Live ? ConnectText.NotConnectedYet : null;
            return new Prompt(id, words, icon, main: true, available: reason == null, reason: reason);
        }

        private static PageLine Say(string words, int rows = 1) => new PageLine(words, tone: LineTone.Secondary, rows: rows);
    }
}

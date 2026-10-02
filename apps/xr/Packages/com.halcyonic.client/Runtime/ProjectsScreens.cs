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
        public const string ReadAgain = "projects-read-again";

        /// <summary>How many rows a page holds; a line that wraps counts its rows.</summary>
        public const int Rows = 4;

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

            /// <summary>The first visit: the subject asks what the person would like to work on.</summary>
            public bool FirstVisit { get; set; }

            /// <summary>The longer first-visit question wraps at this text size, so the shorter one is asked.</summary>
            public bool ShortQuestion { get; set; }

            /// <summary>The chosen project's id, or null.</summary>
            public string? ChosenProject { get; set; }

            /// <summary>The chosen folder's key (<see cref="ConnectableFolder.Key"/>), or null.</summary>
            public string? ChosenFolder { get; set; }

            /// <summary>The connection sent last, for whichever folder.</summary>
            public FolderConnection? Connection { get; set; }

            /// <summary>The page asked for, from 0; a chosen row's own page wins.</summary>
            public int Page { get; set; }

            public DateTimeOffset Now { get; set; }

            public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Utc;
        }

        public static MenuFrame Projects(State state)
        {
            var subject = state.FirstVisit ? (state.ShortQuestion ? ProjectsText.FirstVisitShort : ProjectsText.FirstVisit) : ProjectsText.Subject;
            var projects = state.Overview?.Projects ?? (IReadOnlyList<ProjectSummary>)Array.Empty<ProjectSummary>();
            var offers = state.Listing == null ? (IReadOnlyList<ConnectableFolder>)Array.Empty<ConnectableFolder>() : FolderConnect.Offers(state.Listing);
            var project = state.ChosenProject == null ? null : projects.FirstOrDefault(each => each.ProjectId == state.ChosenProject);
            // Folders are listed, and so can be chosen, only while connected outside the demonstration.
            var folder = project != null || state.ChosenFolder == null || !state.Live || state.Demonstration ? null : FolderConnect.Find(offers, state.ChosenFolder);
            var lines = Lines(state, projects, offers, project, folder);

            var pages = Paginate(lines);
            var page = Math.Max(0, Math.Min(state.Page, pages.Count - 1));
            // A chosen row shows on its own page, so its side panel slides out beside it.
            var chosenAt = pages.FindIndex(each => each.Any(line => line.Chosen));
            if (chosenAt >= 0) page = chosenAt;

            var close = new Prompt(Footer.Close, ProjectsText.Close, GlazeIcon.Close, PromptKind.Close);
            Footer footer;
            SidePanel? side = null;
            if (project != null)
            {
                side = new SidePanel(project.Name, subjectIsData: true, facts: new[]
                {
                    new SideFact(ProjectsText.ItsWork, ProjectsText.Sentence(EntryText.Counts(project))),
                    new SideFact(ProjectsText.OnTheStage, project.Shown ? ProjectsText.Shown : ProjectsText.Hidden),
                });
                footer = new Footer(
                    close,
                    secondary: project.Shown
                        ? new Prompt(HideProject, ProjectsText.HideFromStage, GlazeIcon.ShowAll)
                        : new Prompt(ShowProject, ProjectsText.ShowOnStage, GlazeIcon.ShowAll),
                    farRight: Starting(AddTask, EntryText.AddTask, GlazeIcon.AddTask, state));
            }
            else if (folder != null)
            {
                var own = state.Connection != null && state.Connection.Folder.Key == folder.Key ? state.Connection : null;
                var waitingOn = own == null && state.Connection?.Unresolved != null ? state.Connection.Folder : null;
                side = FolderPanel(folder, own, state);
                footer = new Footer(close, farRight: FolderAction(own, waitingOn, state));
            }
            else
            {
                footer = new Footer(close, farRight: new Prompt(NewProject, ProjectsText.NewProject, GlazeIcon.CreateProject, main: true));
            }
            // Paging waits while a row is chosen: its actions hold the footer, and its row is on this page.
            if (side == null && pages.Count > 1)
            {
                footer = footer.WithPages(
                    new Prompt(Footer.PreviousPage, ProjectsText.PreviousPage, GlazeIcon.Back, PromptKind.PreviousPage, available: page > 0),
                    new Prompt(Footer.NextPage, ProjectsText.NextPage, GlazeIcon.Next, PromptKind.NextPage, available: page < pages.Count - 1));
            }
            return new MenuFrame(subject, footer, lines: pages[page], side: side);
        }

        private static List<PageLine> Lines(State state, IReadOnlyList<ProjectSummary> projects, IReadOnlyList<ConnectableFolder> offers,
            ProjectSummary? chosenProject, ConnectableFolder? chosenFolder)
        {
            var lines = new List<PageLine>();
            if (state.Overview == null) lines.Add(Say(EntryText.WaitingForMac));
            foreach (var project in projects)
            {
                lines.Add(new PageLine(project.Name, wordsAreData: true, fact: ProjectsText.ProjectFact(project),
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
            var listing = state.Listing;
            if (listing == null)
            {
                lines.Add(state.ListingProblem == null
                    ? Say(EntryText.ReadingFolders)
                    : new PageLine(EntryText.FoldersUnread(state.ListingProblem), tone: LineTone.Problem, action: ReadAgain, rows: 2));
                return lines;
            }
            if (listing.Roots.Count == 0)
            {
                lines.Add(new PageLine(EntryText.NoFolders, tone: LineTone.Secondary, action: ReadAgain, rows: 2));
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
                lines.Add(new PageLine(offer.Name, wordsAreData: true, fact: ProjectsText.FolderFact(offer, state.Now, state.Zone, manyPlaces),
                    action: ChooseFolder, key: offer.Key, opens: true, chosen: offer == chosenFolder));
            }
            if (FolderConnect.AnyCut(listing)) lines.Add(Say(EntryText.FoldersCut, rows: 2));
            return lines;
        }

        /// <summary>
        /// Pages of at most <see cref="Rows"/> rows, in order; a heading never ends a page, so it stays
        /// with the first row under it. Always at least one page.
        /// </summary>
        private static List<List<PageLine>> Paginate(List<PageLine> lines)
        {
            var pages = new List<List<PageLine>> { new List<PageLine>() };
            var used = 0;
            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];
                var heading = line.Words == ProjectsText.FoldersHeading && line.Action == null && index + 1 < lines.Count;
                var needs = line.Rows + (heading ? lines[index + 1].Rows : 0);
                if (used > 0 && used + needs > Rows)
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
        private static Prompt? FolderAction(FolderConnection? connection, ConnectableFolder? waitingOn, State state)
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

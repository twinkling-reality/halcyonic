#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The menu's Projects place as a column of its plane (ADR 0026, <see cref="IMenuColumn"/>): it
    /// keeps what only Projects needs (the host's folders, the chosen row, the page, the connection
    /// sent last) and builds its frame with <see cref="ProjectsScreens"/>. Every press goes through
    /// <see cref="ProjectsScreens.Allows(ProjectsScreens.State, string, string?)"/> on the state the
    /// frame was built from, so a press the frame no longer offers does nothing. The one thing it
    /// sends is Connect's <c>project.create</c>, through <see cref="IMenuHost.Submit"/>; Show and Hide
    /// change the stage on this device; New project and Add a task open New project beside the menu.
    /// The folders are read when the column opens and when the person asks again, never on a timer.
    /// </summary>
    public sealed class ProjectsColumn : IMenuColumn
    {
        private readonly IMenuHost host;
        private readonly CommandFactory commands;
        private readonly ProjectsMemory memory;
        private readonly Func<WorkOverview?> overview;
        private readonly Action<string, bool> show;
        private readonly Func<CancellationToken, Task<LocationsResponse>>? readLocations;
        private LocationsResponse? listing;
        private string? listingProblem;
        private Task<LocationsResponse>? reading;
        private CancellationTokenSource? cancel;
        private string? chosenProject;
        private string? chosenFolder;
        private bool chosenProblem;
        private int page;
        private bool closed;
        private MenuFrame? frame;
        private (long Position, bool Connected, bool Demonstration, TextSize Text, long Minute, WorkOverview? Overview) seen;

        /// <param name="memory">
        /// What outlives this column for the session, kept by the director and given to every Projects
        /// column it opens: the connection sent last, so one whose outcome is unknown still holds Connect
        /// back after Projects is closed and opened again.
        /// </param>
        /// <param name="overview">
        /// Every project and its work, as the stage counts it; null before the first snapshot. It must give
        /// the same object until the projection or the stage's visibility changes: the column compares it
        /// by reference each tick to know when to draw again.
        /// </param>
        /// <param name="show">Shows (true) or hides (false) a project's work on the stage, kept on this device.</param>
        /// <param name="readLocations">Reads the host's folders; the host's API when null, as on the headset.</param>
        public ProjectsColumn(IMenuHost host, CommandFactory commands, ProjectsMemory memory, Func<WorkOverview?> overview, Action<string, bool> show,
            Func<CancellationToken, Task<LocationsResponse>>? readLocations = null)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
            this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
            this.overview = overview ?? throw new ArgumentNullException(nameof(overview));
            this.show = show ?? throw new ArgumentNullException(nameof(show));
            this.readLocations = readLocations;
            Read();
        }

        public event Action? Changed;

        public event Action? Closed;

        /// <summary>What the frame shows now, built from the column's state when it changed.</summary>
        public MenuFrame? Frame => frame ??= ProjectsScreens.Projects(State());

        /// <summary>The state the frame is built from now, as <see cref="ProjectsScreens"/> takes it.</summary>
        public ProjectsScreens.State State() => new ProjectsScreens.State
        {
            Overview = overview(),
            Listing = listing,
            ListingProblem = listingProblem,
            Live = host.Connected && !host.Demonstration,
            Demonstration = host.Demonstration,
            ChosenProject = chosenProject,
            ChosenFolder = chosenFolder,
            ChosenProblem = chosenProblem,
            TextSize = host.TextSize,
            Connection = memory.Connection,
            BoundPaths = BoundPaths(host.State),
            Page = page,
            Now = host.Clock,
            Zone = host.Zone,
        };

        /// <summary>The folders the projects are bound to now, from the session's projection.</summary>
        public static IReadOnlyCollection<string> BoundPaths(ClientProjection? state) =>
            state == null
                ? Array.Empty<string>()
                : state.Projects.Values.Select(project => project.Location?.Path).OfType<string>().ToList();

        public void Act(string id, string? key)
        {
            // Once closed, a press still queued for it does nothing.
            if (closed) return;
            var state = State();
            if (!ProjectsScreens.Allows(state, id, key)) return;
            var target = ProjectsScreens.TargetOf(state);
            switch (id)
            {
                case Footer.Close:
                    closed = true;
                    Cancel();
                    Closed?.Invoke();
                    return;
                case SidePanel.Close:
                    Choose(null, null, problem: false);
                    return;
                case Footer.NextPage:
                    page = Math.Max(0, page) + 1;
                    break;
                case ProjectsScreens.ChooseProject:
                    Choose(key, null, problem: false);
                    return;
                case ProjectsScreens.ChooseFolder:
                    Choose(null, key, problem: false);
                    return;
                case ProjectsScreens.ChooseProblem:
                    Choose(null, null, problem: true);
                    return;
                case ProjectsScreens.NewProject:
                    host.OpenNewProject(null, null);
                    return;
                case ProjectsScreens.AddTask when target.AddTaskTo != null:
                    host.OpenNewProject(target.AddTaskTo, target.AddTaskName);
                    return;
                case ProjectsScreens.HideProject when target.Project != null:
                    show(target.Project.ProjectId, false);
                    break;
                case ProjectsScreens.ShowProject when target.Project != null:
                    show(target.Project.ProjectId, true);
                    break;
                case ProjectsScreens.Connect when target.Folder != null:
                    memory.Connection = new FolderConnection(target.Folder, commands);
                    Send(memory.Connection.Begin());
                    break;
                case ProjectsScreens.TryAgain when memory.Connection != null && memory.Connection.CanRetry:
                    Send(memory.Connection.Retry());
                    break;
                case ProjectsScreens.ChooseAnother:
                    chosenFolder = null;
                    Read();
                    break;
                case ProjectsScreens.ReadAgain:
                    chosenProblem = false;
                    Read();
                    break;
                default:
                    return;
            }
            Change();
        }

        public void Drawn(MenuFrame drawn, bool sidePanel)
        {
            // Nothing in Projects counts as read only once it has shown.
        }

        public void HoldStarted(string id)
        {
            // Projects offers nothing to hold.
        }

        public void HoldEnded(string id, bool letGo)
        {
        }

        public void Heard(string text)
        {
        }

        public void Said(string words)
        {
        }

        public void FocusLeft()
        {
            // Projects arms no confirmation.
        }

        /// <summary>
        /// Looks at what the column awaits, the folders' read and Connect's acknowledgement and record,
        /// and at what the session changed, and draws again only when what it shows would change.
        /// </summary>
        public void Tick()
        {
            if (closed) return;
            var changed = false;
            var read = reading;
            if (read != null && read.IsCompleted)
            {
                reading = null;
                if (!read.IsCanceled)
                {
                    if (read.IsFaulted) listingProblem = read.Exception?.GetBaseException().Message ?? "No reason given.";
                    else listing = read.Result;
                    changed = true;
                }
            }
            var current = memory.Connection;
            if (current != null)
            {
                var ack = memory.Acknowledgement;
                if (ack != null && ack.IsCompleted)
                {
                    memory.Acknowledgement = null;
                    if (ack.IsFaulted || ack.IsCanceled) current.AcknowledgementLost(ack.Exception?.GetBaseException() ?? new InvalidOperationException("The acknowledgement was lost."));
                    else current.Acknowledged(ack.Result);
                }
                var before = (current.Step.Status, current.Connected);
                current.Advance(host.State);
                changed |= before != (current.Step.Status, current.Connected);
            }
            // The session moved on (counts, a project bound, the connection lost), the reading size
            // changed, or a minute passed for "changed 5 minutes ago": draw again. Compared without
            // building anything, since this runs every frame.
            var now = (host.State?.Position ?? -1, host.Connected, host.Demonstration, host.TextSize,
                host.Clock.ToUnixTimeSeconds() / 60, overview());
            if (changed || now != seen)
            {
                seen = now;
                Change();
            }
        }

        private void Choose(string? project, string? folder, bool problem)
        {
            chosenProject = project;
            chosenFolder = folder;
            chosenProblem = problem;
            Change();
        }

        /// <summary>Reads the host's folders again, dropping what was read before; nothing to read from in the demonstration.</summary>
        private void Read()
        {
            Cancel();
            listing = null;
            listingProblem = null;
            var read = readLocations ?? (host.Api is ControlPlaneApi api ? api.GetLocationsAsync : (Func<CancellationToken, Task<LocationsResponse>>?)null);
            if (read == null || host.Demonstration)
            {
                listingProblem = host.Demonstration ? null : ConnectText.NotConnectedYet;
                return;
            }
            cancel = new CancellationTokenSource();
            reading = read(cancel.Token);
        }

        private void Cancel()
        {
            cancel?.Cancel();
            cancel?.Dispose();
            cancel = null;
            reading = null;
        }

        /// <summary>Sends through the host's one send path; no session means it never left.</summary>
        private void Send(CommandEnvelope command)
        {
            memory.Acknowledgement = host.Submit(command);
            if (memory.Acknowledgement == null) memory.Connection?.AcknowledgementLost(new SessionUnavailableException("Not connected."));
        }

        private void Change()
        {
            frame = null;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// What Projects keeps for the session, outliving any one <see cref="ProjectsColumn"/>: the
    /// connection sent last and its acknowledgement still on its way. The director keeps one for the
    /// session and gives it to every Projects column it opens, so a connection whose outcome is unknown
    /// keeps every Connect held back, and its record still resolves it, however often Projects is
    /// closed and opened again.
    /// </summary>
    public sealed class ProjectsMemory
    {
        public FolderConnection? Connection { get; set; }

        public Task<CommandAckMessage>? Acknowledgement { get; set; }
    }
}

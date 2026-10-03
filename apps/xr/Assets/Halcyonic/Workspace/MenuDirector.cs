#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The menu on the stage (ADR 0026): it holds the menu's bar, the place open, the closed bar and the
    /// column beside the menu (<see cref="MenuNavigator"/>), draws them on one <see cref="MenuPlane"/>,
    /// and routes every press, draw and held prompt to the column it came from, each only while the app
    /// has focus. It never sends anything itself: a column's own rules send through
    /// <see cref="Submit"/>. It is every column's <see cref="IMenuHost"/>, over the session, the
    /// computer's API, the keyboard and Hold to talk's one voice its host gives it.
    /// <see cref="WorkspaceDirector"/> hosts it and keeps the realtime session and what the journal says.
    /// </summary>
    public sealed class MenuDirector : MonoBehaviour, IMenuHost
    {
        /// <summary>Where the person is and what stands on the stage, as the plane is placed against it.</summary>
        public readonly struct Stage
        {
            public Stage(Vector3 eyes, Vector3 looking, IReadOnlyList<CharacterTarget> characters, float? surfaceHeight, bool besideWindow)
            {
                Eyes = eyes;
                Looking = looking;
                Characters = characters;
                SurfaceHeight = surfaceHeight;
                BesideWindow = besideWindow;
            }

            public Vector3 Eyes { get; }

            public Vector3 Looking { get; }

            public IReadOnlyList<CharacterTarget> Characters { get; }

            public float? SurfaceHeight { get; }

            public bool BesideWindow { get; }
        }

        /// <summary>What the director's host gives it: the session and the computer, the stage, and how to make the columns other lanes own.</summary>
        public sealed class Setup
        {
            /// <summary>Makes the commands Projects' Connect sends.</summary>
            public CommandFactory? Commands { get; set; }

            /// <summary>Every project and its work as the stage counts it, the same object until something changes; null before the first snapshot.</summary>
            public Func<WorkOverview?> Overview { get; set; } = () => null;

            /// <summary>Shows (true) or hides (false) a project's work on the stage, kept on this device, as Projects' Show and Hide ask.</summary>
            public Action<string, bool> ShowProject { get; set; } = (_, _) => { };

            /// <summary>Reads the computer's folders for Projects; the API when null.</summary>
            public Func<CancellationToken, Task<LocationsResponse>>? ReadLocations { get; set; }

            /// <summary>The demonstration's usage limits as if read at a time, for Usage while it plays.</summary>
            public Func<DateTimeOffset, AvailableUsageLimits?> RecordedUsage { get; set; } = _ => null;

            /// <summary>The person's comfort settings, which Settings changes, and what keeps and applies them once changed.</summary>
            public Comfort Comfort { get; set; } = new Comfort();

            public Action ComfortSaved { get; set; } = () => { };

            /// <summary>Settings' rows for where the person is (Your space), from what owns each: the room, the characters' arrangement, the menu's position.</summary>
            public Func<IReadOnlyList<MenuSetting>> Space { get; set; } = () => Array.Empty<MenuSetting>();

            /// <summary>A task's file as a column, or null when it can't be shown.</summary>
            public Func<IMenuHost, string, IMenuColumn?> File { get; set; } = (_, _) => null;

            /// <summary>
            /// New project as a column, opened for a new project (null, null) or to add a task to one: lane
            /// C's flow, kept in <see cref="Memory"/> for the journal (MenuMemory.NewProjectFor, made by
            /// NewProjectColumn.Create), which the host opens on the project before giving it. The director
            /// ticks it while it isn't beside the menu, so a build confirmed in it goes on.
            /// </summary>
            public Func<IMenuHost, string?, string?, IMenuColumn?> NewProject { get; set; } = (_, _, _) => null;

            /// <summary>The session's projection, the demonstration's while it plays; null before the first snapshot.</summary>
            public Func<ClientProjection?> State { get; set; } = () => null;

            /// <summary>The realtime session is live.</summary>
            public Func<bool> Connected { get; set; } = () => false;

            /// <summary>
            /// How a command is sent, by the host's own submissions, so what is in flight shows as sent and
            /// a second decision waits for the first; null when nothing can be sent.
            /// </summary>
            public Func<CommandEnvelope, Task<CommandAckMessage>?> Submit { get; set; } = _ => null;

            /// <summary>The system keyboard can open here.</summary>
            public Func<bool> KeyboardOffered { get; set; } = () => false;

            public Func<ControlPlaneApi?> Api { get; set; } = () => null;

            public Func<bool> Demonstration { get; set; } = () => false;

            public Func<Stage> StageNow { get; set; } = () => new Stage(Vector3.zero, Vector3.forward, Array.Empty<CharacterTarget>(), null, false);

            /// <summary>The character a task stands on the stage as, for the file's light line and its placement.</summary>
            public Func<string, CharacterTarget?> CharacterOf { get; set; } = _ => null;

            /// <summary>The bar as it reads now: the place chosen, the closed line, and where something waits; the same object until it changes.</summary>
            public Func<MenuPlace, MenuBar> Bar { get; set; } = place => new MenuBar(place, "Nothing is waiting for you.");

            /// <summary>Something waits for the person, so the closed bar's Open opens Tasks.</summary>
            public Func<bool> SomethingWaits { get; set; } = () => false;

            /// <summary>The system keyboard: done only when the person finishes.</summary>
            public Action<string, string, Action<string>> Keyboard { get; set; } = (_, _, _) => { };

            /// <summary>Hold to talk's one voice, or null where the build has none.</summary>
            public HoldToTalk? Voice { get; set; }

            /// <summary>Something was pressed, after its column had it: the stage's sounds answer.</summary>
            public Action<MenuColumn, string, string?>? Acted { get; set; }
        }

        private Setup setup = new Setup();
        private MenuPlane plane = null!;
        private MenuNavigator navigator = null!;
        private IMenuColumn? holding;

        /// <summary>The column being made beside the menu, a task's file or New project (no task), whose page height reads against it.</summary>
        private (bool Opening, string? Task) opening;

        /// <summary>What outlives a column and a reconnect, for the app's run on one journal.</summary>
        private readonly MenuMemory memory = new MenuMemory();

        private bool dirty = true;
        private MenuBar? shownBar;

        /// <summary>What the menu keeps for the session; its host forgets it on a re-pairing.</summary>
        public MenuMemory Memory => memory;

        /// <summary>What the menu shows, for its host and the renders.</summary>
        public MenuNavigator Navigator => navigator;

        public MenuPlane Plane => plane;

        public static MenuDirector Create(Transform parent, Setup setup)
        {
            var go = new GameObject("Menu");
            go.transform.SetParent(parent, false);
            var director = go.AddComponent<MenuDirector>();
            director.setup = setup;
            director.plane = MenuPlane.Create(go.transform);
            // Each place's column is made afresh each time the menu shows it after opening.
            director.navigator = new MenuNavigator(new Dictionary<MenuPlace, Func<IMenuColumn>>
            {
                [MenuPlace.Tasks] = () => new TasksColumn(director),
                // Over the memory kept for the app's run on this journal, so a Connect in flight is never sent twice.
                [MenuPlace.Projects] = () => new ProjectsColumn(director, setup.Commands ?? throw new InvalidOperationException("Projects sends through the host's command factory."),
                    director.memory.ProjectsFor(director.State?.Journal?.JournalId), setup.Overview, setup.ShowProject, setup.ReadLocations),
                [MenuPlace.Usage] = () => new UsageColumn(director, setup.RecordedUsage),
                [MenuPlace.Settings] = () => new SettingsColumn(director, setup.Space().Concat(ComfortSettings.Of(setup.Comfort, setup.ComfortSaved)).ToList()),
            });
            director.navigator.Changed += () => director.dirty = true;
            director.plane.Acted += director.OnActed;
            director.plane.Opened += () => director.navigator.OpenMenu(somethingWaits: setup.SomethingWaits());
            director.plane.Drawn += director.OnDrawn;
            director.plane.HoldStarted += director.OnHoldStarted;
            director.plane.HoldEnded += director.OnHoldEnded;
            if (setup.Voice != null)
            {
                setup.Voice.Heard += text => director.holding?.Heard(text);
                setup.Voice.Said += words => director.holding?.Said(words);
            }
            FocusGuard.Left += director.OnFocusLeft;
            // New project's flow let go for another journal comes off the plane.
            director.memory.NewProjectDropped += flow =>
            {
                if (director.navigator.Beside == flow) director.navigator.CloseBeside();
            };
            return director;
        }

        /// <summary>Opens the menu on <paramref name="place"/>, or as the closed bar's Open does.</summary>
        public void Open(MenuPlace? place = null) => navigator.OpenMenu(place, setup.SomethingWaits());

        /// <summary>Closes the menu to its bar.</summary>
        public void CloseMenu() => navigator.CloseMenu();

        /// <summary>Draws again at once, as after the stage moved: the plane re-centres on the stage it stands over.</summary>
        public void Redraw() => dirty = true;

        /// <summary>Draws what shows now, its parts at their places at once, for the renders.</summary>
        public void DrawNow()
        {
            Draw(immediately: true);
            plane.Advance(MenuPlane.SlideSeconds);
        }

        private void OnDestroy() => FocusGuard.Left -= OnFocusLeft;

        private void Update()
        {
            navigator.Tick();
            // New project's kept flow ticks while it isn't beside the menu, so a build confirmed in it goes on.
            memory.Journal(State?.Journal?.JournalId);
            if (memory.NewProject is IMenuColumn flow && navigator.Beside != flow) flow.Tick();
            var bar = setup.Bar(navigator.Place);
            if (dirty || shownBar == null || !Same(bar, shownBar)) Draw(immediately: false);
        }

        private void Draw(bool immediately)
        {
            dirty = false;
            var bar = shownBar != null && Same(setup.Bar(navigator.Place), shownBar) ? shownBar : setup.Bar(navigator.Place);
            shownBar = bar;
            // Tasks keeps the row of the file beside the menu chosen.
            if (navigator.IsOpen && navigator.PlaceColumn is TasksColumn tasks) tasks.Showing(navigator.BesideTask);
            var (menu, beside) = navigator.Frames(bar);
            var stage = setup.StageNow();
            var character = navigator.BesideTask is string task ? setup.CharacterOf(task) : null;
            plane.Show(bar, menu, beside, character, stage.Characters, stage.Eyes, stage.Looking, stage.SurfaceHeight, immediately, stage.BesideWindow);
        }

        /// <summary>Two bars that read the same: a bar made again each frame draws nothing again.</summary>
        private static bool Same(MenuBar a, MenuBar b)
        {
            if (a.Chosen != b.Chosen || a.ClosedLine != b.ClosedLine) return false;
            foreach (var place in MenuBar.Places)
            {
                if (a.Waits(place) != b.Waits(place)) return false;
            }
            return true;
        }

        /// <summary>A press, only while the app has focus and only on the frame last drawn in its slot; the stage's sounds answer one taken.</summary>
        private void OnActed(MenuColumn from, string action, string? key, MenuFrame? frame, SidePanel? side)
        {
            if (FocusGuard.InputSuspended) return;
            if (navigator.Act(from, action, key, frame, side)) setup.Acted?.Invoke(from, action, key);
        }

        /// <summary>A view drew its frame whole: the column that gave it learns of it, never while the plane is folded away.</summary>
        private void OnDrawn(MenuColumn from, MenuFrameView view)
        {
            if (FocusGuard.Folded) return;
            navigator.Drawn(from, view.Frame, view.Side);
        }

        private void OnHoldStarted(MenuColumn from, Prompt prompt)
        {
            if (FocusGuard.InputSuspended) return;
            holding = navigator.ColumnOf(from);
            holding?.HoldStarted(prompt.Id);
            setup.Voice?.Begin();
        }

        private void OnHoldEnded(MenuColumn from, Prompt prompt, bool letGo)
        {
            holding?.HoldEnded(prompt.Id, letGo);
            if (letGo) setup.Voice?.End(true);
            else setup.Voice?.Drop();
        }

        private void OnFocusLeft()
        {
            setup.Voice?.Drop();
            navigator.FocusLeft();
        }

        // ---------------------------------------------------------------------------------------------
        // IMenuHost: what every column may use.

        public ClientProjection? State => setup.State();

        public bool Connected => setup.Connected();

        public bool Demonstration => setup.Demonstration();

        public double Now => Time.realtimeSinceStartupAsDouble;

        public DateTimeOffset Clock => DateTimeOffset.Now;

        public TimeZoneInfo Zone => TimeZoneInfo.Local;

        public TextSize TextSize => setup.Comfort.Text;

        public bool VoiceOffered => HoldToTalk.Offered && setup.Voice != null && Connected && !Demonstration;

        public ControlPlaneApi? Api => Demonstration ? null : setup.Api();

        /// <summary>
        /// Sends through the host's submissions, the demonstration's session while it plays, which
        /// answers the recorded question and approves the recorded request as a live one would.
        /// </summary>
        public Task<CommandAckMessage>? Submit(CommandEnvelope command) => setup.Submit(command);

        public bool KeyboardOffered => setup.KeyboardOffered();

        public void OpenKeyboard(string text, string prompt, Action<string> done) => setup.Keyboard(text, prompt, done);

        public int RowsOf(string words, float columnDegrees) => MenuFrameView.RowsOf(words, columnDegrees);

        public int RowsOf(PageLine line, float columnDegrees) => MenuFrameView.RowsOf(line, columnDegrees);

        public bool FitsHalf(PageLine answer, float columnDegrees) => MenuFrameView.FitsHalf(answer, columnDegrees);

        public int TitleRows(string subject, float columnDegrees) => MenuFrameView.TitleRows(subject, columnDegrees);

        public int PageRows(bool sourceLine) => MenuFrame.RowsAPage(TextSize, sourceLine);

        /// <summary>
        /// The page height on this stage, read against the character of the file being opened while it
        /// is made, else of the file beside the menu; New project, being opened, has none.
        /// </summary>
        public float PageHeight(int subjectRows, bool besideMenu)
        {
            var stage = setup.StageNow();
            var task = opening.Opening ? opening.Task : navigator?.BesideTask;
            var character = task != null ? setup.CharacterOf(task) : null;
            var top = MenuPlane.TopLine(character, stage.Characters, stage.Eyes, stage.Looking, stage.SurfaceHeight, stage.BesideWindow, besideMenu);
            return MenuPage.Height(TextSize, subjectRows, top, ViewField.Current, besideMenu);
        }

        public void OpenFile(string workstreamId)
        {
            opening = (true, workstreamId);
            try
            {
                if (setup.File(this, workstreamId) is IMenuColumn file) navigator.ShowBeside(file, workstreamId);
            }
            finally
            {
                opening = (false, null);
            }
        }

        public void OpenNewProject(string? projectId, string? projectName)
        {
            opening = (true, null);
            try
            {
                if (setup.NewProject(this, projectId, projectName) is IMenuColumn flow) navigator.ShowBeside(flow, null);
            }
            finally
            {
                opening = (false, null);
            }
        }
    }
}

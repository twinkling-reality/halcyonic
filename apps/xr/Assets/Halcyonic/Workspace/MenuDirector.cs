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
    /// has focus. It never sends anything itself: a column's own rules send through the host it was made
    /// with, a <see cref="SessionBoundHost"/> over this director, which sends only to the session shown
    /// when the column was made. Another session or journal lets every column made for the last go. It
    /// gives every column the session, the computer's API, the keyboard and Hold to talk's one voice
    /// its host gives it.
    /// <see cref="WorkspaceDirector"/> hosts it and keeps the realtime session and what the journal says.
    /// </summary>
    public sealed partial class MenuDirector : MonoBehaviour, IMenuHost
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
            public WorkspacePresentationState Presentation { get; set; } = new WorkspacePresentationState();
            public bool SpatialWorkspace { get; set; }
            /// <summary>Only the first anchor waits for valid wearer tracking; an established workspace never follows ordinary head or focus changes.</summary>
            public Func<bool> InitialPlacementReady { get; set; } = () => true;
            public CharacterColourSettings CharacterColours { get; set; } = new CharacterColourSettings();
            public Func<string?> CharacterSourceConnectionKey { get; set; } = () => null;
            public Func<string, bool> SaveCharacterColours { get; set; } = _ => false;
            public Func<ControlPlaneTarget?> ConnectionTarget { get; set; } = () => null;
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

            public AppearanceSettings Appearance { get; set; } = new AppearanceSettings();
            public Action AppearanceSaved { get; set; } = () => { };

            /// <summary>Settings' rows for where the person is (Your space), from what owns each: the room, the characters' arrangement, the menu's position.</summary>
            public Func<IReadOnlyList<MenuSetting>> Space { get; set; } = () => Array.Empty<MenuSetting>();
            public Func<StageArrangement?> Arrangement { get; set; } = () => null;
            public Action<StageArrangement> PlaceCharacters { get; set; } = _ => { };

            /// <summary>A task's file as a column, or null when it can't be shown.</summary>
            public Func<IMenuHost, string, IMenuColumn?> File { get; set; } = (_, _) => null;

            /// <summary>
            /// Makes New project's flow over the host given (<see cref="NewProjectColumn.Create"/>); null
            /// when it can't be. The director keeps it in <see cref="Memory"/> for the session and journal,
            /// opens it on a project or for a new one, ticks it while it isn't beside the menu so a build
            /// confirmed in it goes on, and lets it go for another session or journal, or once it says it
            /// is for another session.
            /// </summary>
            public Func<IMenuHost, NewProjectFlow?> MakeNewProject { get; set; } = _ => null;

            /// <summary>The session's projection, the demonstration's while it plays; null before the first snapshot.</summary>
            public Func<ClientProjection?> State { get; set; } = () => null;

            /// <summary>The realtime session is live.</summary>
            public Func<bool> Connected { get; set; } = () => false;

            /// <summary>The session shown now, the demonstration's while it plays; null with none. Each column is bound to the one shown when it is made.</summary>
            public Func<RealtimeSession?> Session { get; set; } = () => null;

            /// <summary>
            /// Sends a command to that very session, by the host's own submissions, so what is in flight
            /// shows as sent and a second decision waits for the first; null when it can't be sent, as when
            /// that session is no longer the one shown. Never sends to the session shown instead.
            /// </summary>
            public Func<RealtimeSession, CommandEnvelope, Task<CommandAckMessage>?> Submit { get; set; } = (_, _) => null;

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
            public Action<string, string, Action<string>, Action?> Keyboard { get; set; } = (_, _, _, _) => { };

            /// <summary>Hold to talk's one voice, or null where the build has none.</summary>
            public HoldToTalk? Voice { get; set; }

            /// <summary>Something was pressed, after its column had it: the stage's sounds answer.</summary>
            public Action<MenuColumn, string, string?>? Acted { get; set; }
        }

        private Setup setup = new Setup();
        private MenuPlane plane = null!;
        private MenuNavigator navigator = null!;

        /// <summary>Hold to talk's one voice, its words only for the column that held; null where the build has none.</summary>
        private MenuVoice? voice;

        /// <summary>What outlives a column and a reconnect, for the app's run on one journal.</summary>
        private readonly MenuMemory memory = new MenuMemory();

        private bool dirty = true;
        private MenuBar? shownBar;

        /// <summary>Where Hold to talk's one voice stood when the plane was last laid, so a change lays it again.</summary>
        private (MenuColumn Kind, string Id, VoiceStage Stage)? drawnVoice;

        /// <summary>Focus was away, or the plane folded, last frame: what was drawn then counted for nothing, so it is drawn again on return.</summary>
        private bool away;

        /// <summary>
        /// The eyes and gaze the plane is placed from, taken when what stands on it last changed (the menu
        /// open or closed, the column beside it) or on <see cref="ResetPosition"/>, so any other redraw,
        /// a page, a side panel or a value, stays where it was rather than following the head.
        /// </summary>
        private (Vector3 Eyes, Vector3 Looking)? anchor;
        private (bool Open, IMenuColumn? Beside) anchoredFor;

        /// <summary>The stage's arrangement as the plane was last laid against it: beside a window or not, and the surface under the characters.</summary>
        private (bool BesideWindow, float? Surface) arranged;

        /// <summary>The characters standing when the drag began, which its rules were judged against.</summary>
        private readonly List<CharacterTarget> dragStage = new List<CharacterTarget>();

        /// <summary>When the drag under way began, so presses begun under the moving plane end with it.</summary>
        private float dragStartedAt;

        /// <summary>How far a drag left the plane from where the stage places it, kept until the plane is placed afresh.</summary>
        private (float Yaw, float Elevation) moved;
        private bool movedByHand;

        /// <summary>The plane held by a file's subject, while it is (ADR 0026).</summary>
        private MenuDrag? drag;
        private IMenuColumn? placement;
        private MenuFrame? placementFrame;
        private MenuFrame? placementDrawn;
        public bool IsCompact => navigator.IsCompact;
        public bool Compact()
        {
            if (Away) return false;
            EndDrag();
            var closedPlacement = placement != null;
            if (closedPlacement) LocalClosed();
            if (closedPlacement) { navigator.Moved(); dirty = true; }
            return navigator.Compact() || closedPlacement;
        }
        public bool Expand() => !Away && navigator.Expand();

        /// <summary>A file's subject holds the plane, which follows the hand: nothing is pressed or held meanwhile.</summary>
        public bool Dragging => drag != null;

        /// <summary>The person dragged the plane from where it stands by itself, until Reset position or a fresh placement puts it back: Settings says so.</summary>
        public bool MovedByHand => movedByHand && moved != default;

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
            setup.CharacterColours.Changed += director.ApplyCharacterColours;
            director.viewBindings = new WorkspaceViewBindings(director.RecordFor, director.ViewsChanged);
            director.plane = MenuPlane.Create(go.transform, director.OpenAs);
            director.plane.SpatialWorkspace = setup.SpatialWorkspace;
            director.plane.PlacePressed += place =>
            {
                if (!director.OrganizationStanding(MenuColumn.Menu, null, null)) return;
                if (director.placement != null) director.LocalClosed();
                director.navigator.CloseBeside();
                director.Open(place);
            };
            // The session shown first is no change; another one later is.
            director.memory.Session(setup.Session());
            // Each place's column is made afresh each time the menu shows it after opening, bound to the session shown then.
            director.navigator = new MenuNavigator(new Dictionary<MenuPlace, Func<IMenuColumn>>
            {
                [MenuPlace.Tasks] = () => new TasksColumn(director.Bound(), () => director.SavedViewCount, director.OpenSavedViews,
                    () => director.memory.SelectedProjectId, director.OpenProjectDetails, director.OpenAllTasks),
                // Over the memory kept for the app's run on this journal, so a Connect in flight is never sent twice.
                [MenuPlace.Projects] = () => new ProjectsColumn(director.Bound(), setup.Commands ?? throw new InvalidOperationException("Projects sends through the host's command factory."),
                    director.memory.ProjectsFor(director.State?.Journal?.JournalId), setup.Overview, setup.ShowProject, setup.ReadLocations, director.OpenProjectTasks),
                [MenuPlace.Usage] = () => new UsageColumn(director.Bound(), setup.RecordedUsage),
                [MenuPlace.Settings] = () => new SettingsHubColumn(director.Bound(), setup.Space().Concat(ComfortSettings.Of(setup.Comfort, setup.ComfortSaved)).ToList(), setup.Appearance, setup.AppearanceSaved),
            });
            director.navigator.OpenMenu(MenuPlace.Projects);
            director.navigator.Changed += () => { director.dirty = true; director.viewsDirty = true; };
            director.plane.Acted += (from, action, key, frame, side) => director.OnActed(from, action, key, frame, side);
            director.plane.Opened += () =>
            {
                if (director.OrganizationStanding(MenuColumn.Menu, null, null))
                {
                    if (!director.RestoreWorkspace()) director.navigator.OpenMenu();
                }
            };
            director.plane.CompactPressed += (from, frame, side) => { if (director.OrganizationStanding(from, frame, side)) director.MinimizeWorkspace(); };
            director.plane.MovePressed += (from, frame, side) => { if (director.OrganizationStanding(from, frame, side)) director.OpenPlacement(); };
            director.plane.MinimizePressed += () => { if (director.OrganizationStanding(MenuColumn.Menu, null, null)) director.MinimizeWorkspace(); };
            director.plane.OtherAppsPressed += director.OpenOtherApps;
            director.plane.ProjectsPressed += () =>
            {
                if (!director.OrganizationStanding(MenuColumn.Menu, null, null)) return;
                if (director.placement != null) director.LocalClosed();
                director.navigator.CloseBeside();
                director.Open(MenuPlace.Projects);
            };
            director.plane.Drawn += director.OnDrawn;
            director.plane.HoldStarted += director.OnHoldStarted;
            director.plane.HoldEnded += director.OnHoldEnded;
            director.plane.SubjectPressed += director.OnSubjectPressed;
            director.plane.SubjectHeld += director.OnSubjectHeld;
            director.plane.SubjectDragged += director.OnSubjectDragged;
            director.plane.SubjectLetGo += director.OnSubjectLetGo;
            if (setup.Voice is HoldToTalk talk)
            {
                var voice = new MenuVoice(() => talk.Busy, talk.Begin, () => talk.End(true), talk.Drop);
                talk.CaptureHeard = voice.CaptureHeard;
                talk.Said += voice.Said;
                // The column that held leaving the plane stops the voice: its words would reach no one.
                director.navigator.Left += voice.Left;
                director.voice = voice;
            }
            FocusGuard.Left += director.OnFocusLeft;
            Glaze.PaletteChanged += director.OnPaletteChanged;
            GlazeText.ScaleChanged += director.OnTextScaleChanged;
            setup.Presentation.Suspending += director.SuspendWorkspace;
            setup.Presentation.Changed += director.PresentationChanged;
            // New project's flow let go for another session or journal comes off the plane.
            director.memory.NewProjectDropped += flow =>
            {
                if (director.navigator.Beside == flow) director.navigator.CloseBeside();
            };
            director.memory.FileDropped += (task, column) =>
            {
                director.ViewDropped(column);
                director.navigator.CloseDroppedTask(task, column);
            };
            director.LoadViews();
            return director;
        }

        /// <summary>Minimizes the whole workspace, preserving local work while revoking its authority first.</summary>
        public bool MinimizeWorkspace() => !Away && setup.Presentation.Minimize();

        public bool RestoreWorkspace() => !Away && setup.Presentation.Restore();

        private void SuspendWorkspace()
        {
            EndDrag();
            voice?.FocusLeft();
            if (placement != null) LocalClosed();
            navigator.Compact();
            memory.SuspendFiles(navigator.Beside);
            navigator.Moved();
            dirty = true;
        }

        private void PresentationChanged()
        {
            if (!setup.Presentation.Minimized) navigator.Expand();
            navigator.Moved();
            dirty = true;
        }

        /// <summary>Opens the menu on <paramref name="place"/>, or as the closed bar's Open does.</summary>
        public void Open(MenuPlace? place = null)
        {
            if (setup.Presentation.Minimized) setup.Presentation.Restore();
            navigator.OpenMenu(place);
        }

        /// <summary>Closes the menu to its bar.</summary>
        public void CloseMenu() => MinimizeWorkspace();

        /// <summary>The computer has had no task yet: the menu asks the first question, with Settings alone in its row (<see cref="MenuNavigator.BeforeFirstTask"/>).</summary>
        public bool BeforeFirstTask
        {
            get => navigator.BeforeFirstTask;
            set => navigator.BeforeFirstTask = value;
        }

        /// <summary>A column stands beside the menu, or in its place: a task's file or New project.</summary>
        public bool BesideOpen => navigator.Beside != null;

        /// <summary>Draws again at once, as after the stage moved: the plane re-centres on the stage it stands over, and a drag judged against where it stood ends.</summary>
        public void Redraw()
        {
            EndDrag();
            ReflowFiles();
            dirty = true;
        }

        /// <summary>
        /// The stage brought its characters up to date: a drag ends if any character came or went, or the
        /// arrangement or the surface changed, since its rules were judged against the stage as it began.
        /// </summary>
        public void StageRefreshed()
        {
            if (drag == null) return;
            var now = setup.StageNow();
            if (now.BesideWindow != arranged.BesideWindow || now.SurfaceHeight != arranged.Surface || !SameCharacters(now.Characters)) EndDrag();
        }

        /// <summary>Whether <paramref name="characters"/> are the very ones standing when the drag began, none of them gone.</summary>
        private bool SameCharacters(IReadOnlyList<CharacterTarget> characters)
        {
            var count = 0;
            for (var index = 0; index < characters.Count; index++)
            {
                if (characters[index] == null) continue;
                if (!dragStage.Contains(characters[index])) return false;
                count++;
            }
            return count == dragStage.Count && !AnyGone();
        }

        /// <summary>A character standing when the drag began has since been destroyed, as one that left the stage.</summary>
        private bool AnyGone()
        {
            for (var index = 0; index < dragStage.Count; index++)
            {
                if (dragStage[index] == null) return true;
            }
            return false;
        }

        /// <summary>
        /// Settings' Reset position: the plane is placed afresh where the person looks now, as when the menu
        /// opens, any drag let go of, and takes no press until it is drawn there.
        /// </summary>
        public void ResetPosition()
        {
            drag = null;
            anchor = null;
            movedByHand = false;
            moved = default;
            ReflowFiles();
            navigator.Moved();
            dirty = true;
        }

        private void ReflowFiles()
        {
            foreach (var pair in memory.Files.ToList())
                if (memory.Files.TryGetValue(pair.Key, out var kept) && kept == pair.Value && kept is FileColumn file) file.Reflow();
        }

#if UNITY_EDITOR
        private GlazeButton? renderMove;
        private const int RenderMovePointer = 734;

        /// <summary>For renders: press and mature the visible Move target through its real pointer and hold events.</summary>
        public void HoldSubjectForRender(Vector3 point)
        {
            PressSubjectForRender();
            MatureHoldForRender(point);
        }

        /// <summary>For renders: begin a press on the currently visible named Move target.</summary>
        public void PressSubjectForRender()
        {
            renderMove?.Target.PointerForRender(RenderMovePointer, Oculus.Interaction.PointerEventType.Cancel, renderMove.transform.position);
            renderMove?.Target.PointerForRender(RenderMovePointer, Oculus.Interaction.PointerEventType.Unhover, renderMove.transform.position);
            var view = plane.Shown.FirstOrDefault(item => item.Kind == MenuColumn.File).View;
            view?.SettleSubjectForRender();
            renderMove = plane.Chrome.Visible ? plane.Chrome.Targets.FirstOrDefault(button => button.Label.text == WorkspaceText.MoveWorkspace)
                : plane.Bar?.Controls.Move.gameObject.activeSelf == true ? plane.Bar.Controls.Move : null;
            if (renderMove == null) return;
            renderMove.SettleForRender();
            renderMove.Target.PointerForRender(RenderMovePointer, Oculus.Interaction.PointerEventType.Hover, renderMove.transform.position);
            renderMove.Target.PointerForRender(RenderMovePointer, Oculus.Interaction.PointerEventType.Select, renderMove.transform.position);
        }

        /// <summary>For renders: advance only the originating Move button's hold clock; rebinding or dropping it remains revoked.</summary>
        public void MatureHoldForRender(Vector3 point)
        {
            renderMove?.Target.PointerForRender(RenderMovePointer, Oculus.Interaction.PointerEventType.Move, point);
            renderMove?.MatureHoldForRender();
        }

        /// <summary>For renders: a Move hold originating on the compact bar matures.</summary>
        public void MatureCompactMoveForRender(Vector3 point) => OnSubjectHeld(MenuColumn.Menu, null, point);

        /// <summary>For the editor's renders: a press on the plane, through the director's own handler; true when a column took it.</summary>
        public bool PressForRender(MenuColumn from, string action, string? key, MenuFrame? frame, SidePanel? side) => OnActed(from, action, key, frame, side);

        /// <summary>For the editor's renders: a held prompt, through the director's own handler.</summary>
        public void HoldPromptForRender(MenuColumn from, Prompt prompt, MenuFrame? frame, SidePanel? side) => OnHoldStarted(from, prompt, frame, side);

        /// <summary>For the editor's renders: one frame, as the headset runs it, drawing only what changed.</summary>
        public void UpdateForRender() => Update();

        /// <summary>For the editor's renders, which have no microphone: the voice the director's holds go to.</summary>
        public void VoiceForRender(MenuVoice voice) => this.voice = voice;

        /// <summary>For the editor's renders: another window took focus.</summary>
        public void FocusLeftForRender() => OnFocusLeft();

        /// <summary>For the editor's renders: the stage brought its characters up to date.</summary>
        public void StageRefreshedForRender() => StageRefreshed();

        /// <summary>For the editor's renders: the held point moved to <paramref name="point"/>.</summary>
        public void DragSubjectForRender(Vector3 point) => OnSubjectDragged(point);

        /// <summary>For the editor's renders: the subject let go of.</summary>
        public void LetGoForRender()
        {
            renderMove?.Target.PointerForRender(RenderMovePointer, Oculus.Interaction.PointerEventType.Cancel, renderMove.transform.position);
            renderMove?.Target.PointerForRender(RenderMovePointer, Oculus.Interaction.PointerEventType.Unhover, renderMove.transform.position);
            renderMove = null;
            OnSubjectLetGo();
        }

        /// <summary>Draws what shows now, its parts at their places at once, for the renders.</summary>
        public void DrawNow()
        {
            Fold();
            Follow();
            LetGoIfMoved();
            Draw(immediately: true);
            plane.Advance(Mathf.Max(Glaze.SlideSeconds, MenuPlane.OpeningSeconds));
        }
#endif

        private void OnDestroy()
        {
            FocusGuard.Left -= OnFocusLeft;
            Glaze.PaletteChanged -= OnPaletteChanged;
            GlazeText.ScaleChanged -= OnTextScaleChanged;
            if (setup != null)
            {
                setup.Presentation.Suspending -= SuspendWorkspace;
                setup.Presentation.Changed -= PresentationChanged;
            }
            if (placement != null) LocalClosed();
            if (setup != null)
            {
                foreach (var pair in colourOrigins)
                    if (pair.Key != null && pair.Key.View != null) pair.Key.View.SetIdentityHue(CharacterColourSettings.Hue(setup.CharacterColours.Applied(pair.Value)));
                colourOrigins.Clear();
                setup.CharacterColours.Changed -= ApplyCharacterColours;
            }
            navigator?.FocusLeft();
        }

        private void OnPaletteChanged()
        {
            // Colours change, geometry and reading evidence do not. Old targets wait for the exact redraw.
            navigator.Moved();
            dirty = true;
        }

        private void OnTextScaleChanged()
        {
            // Edit-mode render fixtures can destroy non-executing behaviours without OnDestroy.
            // Never let a retained static delegate visit their destroyed Unity children.
            if (this == null || plane == null)
            {
                GlazeText.ScaleChanged -= OnTextScaleChanged;
                return;
            }
            // The same words now occupy different hit areas. A press or capture begun against
            // the previous geometry cannot finish against the newly packed frame.
            EndDrag();
            subjectPressTaken = false;
            subjectOrigin = null;
            voice?.FocusLeft();
            plane.UnsettleShown(float.MinValue);
            foreach (var button in plane.Chrome.Targets) { button.EndPressSince(float.MinValue); button.Unsettle(); }
            if (plane.Bar is MenuBarView bar) { bar.Target.EndPressSince(float.MinValue); bar.Target.Unsettle(); }
            placementDrawn = null;
            navigator.Moved();
            dirty = true;
        }

        private void Update()
        {
            Fold();
            KeepViews();
            Follow();
            RecoverViews();
            placement?.Tick();
            navigator.Tick();
            memory.TickFiles(navigator.Beside);
            // New project's kept flow ticks while it isn't beside the menu, so a build confirmed in it goes on.
            if (memory.NewProject is NewProjectFlow flow && navigator.Beside != flow) flow.Tick();
            var nowAway = Away;
            if (away && !nowAway) dirty = true;
            away = nowAway;
            // Hold to talk shows where the voice stands: listening, writing down, or idle again (ADR 0027).
            if (Voiced() != drawnVoice) dirty = true;
            LetGoIfMoved();
            // Mid-drag nothing is laid again under the hand; the plane is drawn where it was left once let go.
            if (drag != null) return;
            var bar = setup.Bar(navigator.Place);
            if (dirty || shownBar == null || !Same(bar, shownBar)) Draw(immediately: false);
        }

        /// <summary>
        /// The session and journal shown now: another session, as the computer's live session taking the
        /// demonstration's place or a re-pairing, or another journal lets go of everything made for the
        /// last, New project's flow with it (<see cref="MenuMemory"/>), takes the column beside the menu
        /// off the plane and makes the menu's places afresh, so nothing made for one ticks or shows for
        /// another; what was made can't send to another anyway (<see cref="SessionBoundHost"/>).
        /// </summary>
        private void Follow()
        {
            bool another;
            renewingViews = true;
            try
            {
                another = memory.Session(setup.Session());
                another |= memory.Journal(State?.Journal?.JournalId);
            }
            finally { renewingViews = false; }
            if (!another) return;
            recoveredScope = null;
            if (placement != null) LocalClosed();
            navigator.CloseBeside();
            navigator.Renew();
        }

        /// <summary>
        /// Folds the plane away while another window keeps focus (<see cref="FocusGuard.Folded"/>), as large
        /// panels do, so the stage's banner shows again and names what is still open; back, it is as it was.
        /// Folding ends a drag, since no hand can let go of a plane that does not show.
        /// </summary>
        private void Fold()
        {
            var shows = !FocusGuard.Folded;
            if (plane.gameObject.activeSelf == shows) return;
            if (!shows) EndDrag();
            plane.gameObject.SetActive(shows);
        }

        /// <summary>What the banner names as still open while the plane is folded: the task whose file is open, New project, or the menu's place; null with only the closed bar.</summary>
        private string? OpenAs()
        {
            if (navigator == null) return null;
            if (navigator.BesideTask is string task && State?.Workstreams.TryGetValue(task, out var work) == true) return work.Title;
            if (navigator.Beside != null) return ProjectsText.NewProject;
            return navigator.IsOpen ? MenuBar.Word(navigator.Place) : null;
        }

        /// <summary>A host for a column made now: this director's, sending only to the session shown now.</summary>
        private IMenuHost Bound() => new SessionBoundHost(this, setup.Session, setup.Submit);

        /// <summary>Focus is away, input still suspended just after it returns, or the plane is folded away.</summary>
        private static bool Away => FocusGuard.InputSuspended || FocusGuard.Folded;

        private void Draw(bool immediately)
        {
            if (anchor == null && !setup.InitialPlacementReady())
            {
                // Opening while unworn must not freeze a fallback/table pose as the permanent anchor.
                // Retry on every update without drawing or granting authority to hidden controls.
                plane.gameObject.SetActive(false);
                dirty = true;
                return;
            }
            plane.gameObject.SetActive(!FocusGuard.Folded);
            dirty = false;
            var bar = shownBar != null && Same(setup.Bar(navigator.Place), shownBar) ? shownBar : setup.Bar(navigator.Place);
            shownBar = bar;
            // Tasks keeps the row of the file beside the menu chosen.
            if (navigator.Shown is TasksColumn tasks) tasks.Showing(navigator.BesideTask);
            var (menu, beside) = navigator.Frames(bar);
            var stage = setup.StageNow();
            ApplyCharacterColours();
            if (placement != null) { menu = null; beside = placementFrame = placement.Frame; }
            var standing = (navigator.IsOpen, navigator.Beside);
            if (anchor == null)
            {
                anchor = (stage.Eyes, stage.Looking);
                moved = default;
                movedByHand = false;
            }
            anchoredFor = standing;
            // Optional agent geometry can revoke a drag, but never re-anchor the console.
            var arrangement = (stage.BesideWindow, stage.SurfaceHeight);
            if (arrangement != arranged)
            {
                arranged = arrangement;
            }
            var (eyes, looking) = anchor.Value;
            var character = navigator.BesideTask is string task ? setup.CharacterOf(task) : null;
            drawnVoice = Voiced();
            plane.Voice = drawnVoice;
            plane.ConnectionState = Demonstration ? WorkspaceText.Demo : Connected ? WorkspaceText.Connected
                : setup.Session()?.Status.Phase is ConnectionPhase.Connecting or ConnectionPhase.Synchronizing ? WorkspaceText.Connecting : WorkspaceText.Disconnected;
            plane.SelectionContext = WorkspaceContext();
            plane.OtherAppsAvailable = CanOpenOtherApps;
            void ShowComposition() => plane.Show(bar, menu, beside, character, stage.Characters, eyes, looking, stage.SurfaceHeight, immediately, stage.BesideWindow, moved, placement ?? (object?)navigator.Beside, navigator.IsCompact ? CompactIdentity() : null, navigator.IsCompact ? CompactState() : null,
                navigator.IsCompact && State != null ? State.Workstreams.Values.Count(item => item.Attention.Level == AttentionLevel.ActionRequired) : 0,
                navigator.IsCompact ? (object?)navigator.Beside ?? navigator.Shown ?? (object)navigator : null);
            ShowComposition();
            // The actual layout owns visibility: focused answers and files that cannot fit beside the
            // menu hide it; menu-owned details in its place still let the person act on that setting.
            var menuVisible = menu != null && plane.Front == menu;
            for (var index = 0; index < plane.Shown.Count && !menuVisible; index++)
                menuVisible = plane.Shown[index].Kind == MenuColumn.Menu;
            if (navigator.MenuVisibility(menuVisible))
            {
                (menu, beside) = navigator.Frames(bar);
                if (placement != null) { menu = null; beside = placementFrame = placement.Frame; }
                drawnVoice = Voiced();
                plane.Voice = drawnVoice;
                // One bounded repaint after FocusLeft changed the retained menu and its temporary palette.
                // Only the views' normal settled Drawn callbacks grant authority to these refreshed frames.
                ShowComposition();
            }
            // Passive redraws preserve the exact hand placement; the active drag owns changes to it.
            moved = plane.Moved;
        }

        /// <summary>
        /// Where Hold to talk's one voice stands for the column that held, as the plane draws it on that
        /// column's held prompt; null while it is idle or that column is off the plane. Read every frame, it
        /// allocates nothing.
        /// </summary>
        private (MenuColumn Kind, string Id, VoiceStage Stage)? Voiced()
        {
            if (voice?.Holding is not (IMenuColumn column, string id)) return null;
            var stage = voice.Stage;
            if (stage == VoiceStage.Idle) return null;
            if (navigator.ColumnOf(MenuColumn.File) == column) return (MenuColumn.File, id, stage);
            if (navigator.ColumnOf(MenuColumn.Menu) == column) return (MenuColumn.Menu, id, stage);
            return null;
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
        private bool OnActed(MenuColumn from, string action, string? key, MenuFrame? frame, SidePanel? side)
        {
            if (Away || dirty || drag != null || plane.Sliding) return false;
            if (placement != null)
            {
                if (frame != placementFrame || from != MenuColumn.File || frame == null || frame != placementDrawn
                    || !(frame.Footer.All.Any(each => each.Prompt.Id == action && each.Prompt.Available)
                        || frame.Lines.Any(line => line.Action == action && line.Key == key && line.Pressable))) return false;
                placement.Act(action, key);
                return true;
            }
            if (!navigator.Act(from, action, key, frame, side)) return false;
            setup.Acted?.Invoke(from, action, key);
            return true;
        }

        /// <summary>
        /// A view drew its frame whole: the column that gave it learns of it, never while focus is away or
        /// the plane is folded, since the person is elsewhere; on return it is drawn, and counted, again.
        /// </summary>
        private void OnDrawn(MenuColumn from, MenuFrameView view)
        {
            if (Away) return;
            if (placement != null)
            {
                if (from == MenuColumn.File && view.Frame == placementFrame)
                {
                    placementDrawn = placementFrame;
                    placement.Drawn(placementFrame!, null);
                }
                return;
            }
            // A side panel reports the frame whose place it stands in, whose footer it carries.
            navigator.Drawn(from, from == MenuColumn.Side ? view.InPlaceOf : view.Frame, view.Side);
        }

        /// <summary>
        /// A held prompt, only on the frame last drawn in its slot, as a press: the voice records for its
        /// column, unless it is still busy with another hold's words.
        /// </summary>
        private void OnHoldStarted(MenuColumn from, Prompt prompt, MenuFrame? frame, SidePanel? side)
        {
            if (FocusGuard.InputSuspended || drag != null) return;
            if (navigator.Taking(from, prompt.Id, frame, side) is IMenuColumn column) voice?.Hold(column, prompt.Id);
        }

        /// <summary>A hold ended: only the hold that started the recording ends it.</summary>
        private void OnHoldEnded(MenuColumn from, Prompt prompt, bool letGo) => voice?.Ended(navigator.ColumnOf(from), prompt.Id, letGo);

        private void OnFocusLeft()
        {
            EndDrag();
            voice?.FocusLeft();
            navigator.FocusLeft();
            if (placement != null) LocalClosed();
        }

        /// <summary>What stands on the plane changed under a drag: it lets go, and the plane is placed afresh.</summary>
        private LineWords CompactIdentity()
        {
            if (navigator.BesideTask is string task && State?.Workstreams.TryGetValue(task, out var work) == true)
                return LineWords.TheirName(work.Title);
            if (navigator.BesideTask != null) return navigator.Beside?.Frame?.SubjectRuns ?? LineWords.Ours("Task unavailable");
            return LineWords.Ours(navigator.Beside is NewProjectFlow ? ProjectsText.NewProject : MenuBar.Word(navigator.Place));
        }

        private LineWords WorkspaceContext()
        {
            plane.SelectedState = null;
            plane.SelectedMarks = Array.Empty<WorkMark>();
            if (navigator.BesideTask is string task && State?.Workstreams.TryGetValue(task, out var work) == true)
            {
                var presentation = CharacterPresenter.Present(work, State, Connected && !Demonstration);
                plane.SelectedState = StateLanguage.BadgeOf(presentation);
                plane.SelectedMarks = StateLanguage.MarksOf(presentation);
                return State.Projects.TryGetValue(work.ProjectId, out var project)
                    ? LineWords.Ours("› ").Then(LineWords.TheirName(project.Name)) : LineWords.Ours("Project unavailable");
            }
            if (navigator.Place == MenuPlace.Tasks && memory.SelectedProjectId is string selected && State?.Projects.TryGetValue(selected, out var filteredProject) == true)
                return LineWords.Ours("› ").Then(LineWords.TheirName(filteredProject.Name));
            return LineWords.Ours(MenuBar.Word(navigator.Place));
        }

        private StateBadge? CompactState()
        {
            if (navigator.BesideTask is not string task || State?.Workstreams.TryGetValue(task, out var work) != true) return null;
            return StateLanguage.BadgeOf(CharacterPresenter.Present(work, State, Connected));
        }

        private bool OrganizationStanding(MenuColumn from, MenuFrame? frame, SidePanel? side)
        {
            if (Away || dirty || drag != null || plane.Sliding) return false;
            // Global frame controls remain usable over a local overlay. Its task/content input
            // still belongs exclusively to the exact overlay frame that was physically drawn.
            if (frame == null && side == null) return plane.Chrome.Visible || plane.Bar is MenuBarView bar && bar.Shown >= 1f;
            if (placement != null) return from == MenuColumn.File && frame != null && frame == placementFrame && frame == placementDrawn;
            return !plane.Opening(from) && navigator.Standing(from, frame, side) != null;
        }

        // A shown body's last accepted source stays attached while its projection is cached/offline.
        // Rollback therefore never relabels an old body with a newly configured connection.
        private readonly Dictionary<CharacterTarget, CharacterColourScope> colourOrigins = new Dictionary<CharacterTarget, CharacterColourScope>();
        private bool CurrentColourSource(out string key)
        {
            key = setup.CharacterSourceConnectionKey() ?? "";
            return CurrentSnapshot && key.Length > 0 && TryKey(out var configured) && configured == key;
        }
        private void ApplyCharacterColours()
        {
            if (this == null || setup == null) return;
            var canBind = CurrentColourSource(out var key);
            var shown = setup.StageNow().Characters;
            foreach (var target in shown)
            {
                if (target == null || target.View == null) continue;
                if (canBind && State?.Journal != null && State.Workstreams.ContainsKey(target.View.WorkstreamId))
                    colourOrigins[target] = new CharacterColourScope(key, State.Journal.JournalId, target.View.WorkstreamId);
                if (colourOrigins.TryGetValue(target, out var origin))
                    target.View.SetIdentityHue(setup.CharacterColours.DisplayedHue(origin));
            }
            foreach (var gone in colourOrigins.Keys.Where(target => target == null || !shown.Contains(target)).ToArray())
            {
                if (gone != null && gone.View != null) gone.View.SetIdentityHue(CharacterColourSettings.Hue(setup.CharacterColours.Applied(colourOrigins[gone])));
                colourOrigins.Remove(gone);
            }
        }

        private bool ColourTarget(FileColumn file, out CharacterColourScope? scope, out CharacterTarget? target)
        {
            scope = null; target = null;
            var task = file.Now?.Character.WorkstreamId;
            if (task == null || Away || FocusGuard.InputSuspended || setup.Presentation.Minimized || !CurrentColourSource(out var key)
                || State?.Journal == null || !State.Workstreams.ContainsKey(task)) return false;
            target = setup.CharacterOf(task);
            if (target == null || target.View == null || !target.gameObject.activeInHierarchy) return false;
            scope = new CharacterColourScope(key, State.Journal.JournalId, task);
            return colourOrigins.TryGetValue(target, out var accepted) && accepted.Equals(scope);
        }

        private void OpenCharacterColour(FileColumn file)
        {
            if (!ColourTarget(file, out var scope, out var target) || scope == null || target == null) return;
            var session = setup.Session();
            CharacterColourColumn? column = null;
            column = new CharacterColourColumn(setup.CharacterColours, scope,
                () => ReferenceEquals(placement, column) && ReferenceEquals(setup.Session(), session)
                    && ColourTarget(file, out var current, out var shown) && scope.Equals(current) && shown == target,
                setup.SaveCharacterColours);
            if (!OpenLocal(column)) column.FocusLeft();
        }

        private void OpenPlacement()
        {
            OpenLocal(new PlacementColumn(this, ResetPosition, navigator.BesideTask is string task && State?.Workstreams.TryGetValue(task, out var work) == true ? work.Title : null));
        }

        /// <summary>An explicitly requested local overlay that preserves retained work and owns no runtime authority.</summary>
        public bool OpenLocal(IMenuColumn column)
        {
            if (Away || drag != null || placement != null || setup.Presentation.Minimized) return false;
            plane.RetainWorkspaceFootprint();
            if (!navigator.IsCompact) { navigator.Compact(); navigator.Expand(); }
            navigator.Moved();
            voice?.FocusLeft();
            placement = column;
            placementDrawn = null;
            placement.Changed += LocalChanged;
            placement.Closed += LocalClosed;
            dirty = true;
            return true;
        }

        private void LocalChanged() { placementDrawn = null; dirty = true; }
        private void LocalClosed()
        {
            var closing = placement;
            placement = null;
            placementFrame = null;
            placementDrawn = null;
            plane.ReleaseWorkspaceFootprint();
            if (closing != null)
            {
                closing.Changed -= LocalChanged;
                closing.Closed -= LocalClosed;
                closing.FocusLeft();
            }
            ReflowFiles();
            navigator.Moved();
            dirty = true;
        }

        private void LetGoIfMoved()
        {
            if (drag != null && (navigator.IsOpen, navigator.Beside) != anchoredFor) EndDrag();
        }

        /// <summary>The subject's press, taken only where a drag may start now; its hold matures into a drag only if this one was.</summary>
        private bool subjectPressTaken;
        private (MenuColumn From, MenuFrame? Frame, SidePanel? Side, bool Compact, IMenuColumn? Retained)? subjectOrigin;

        private void OnSubjectPressed(MenuColumn from, MenuFrame? frame)
        {
            var side = plane.Shown.FirstOrDefault(item => item.Kind == from).View?.Side;
            subjectOrigin = (from, frame, side, navigator.IsCompact, navigator.Beside);
            subjectPressTaken = MayDrag(from, frame);
        }

        /// <summary>
        /// Whether a hold on <paramref name="from"/>'s subject, showing <paramref name="frame"/>, may drag the
        /// plane now: a task's file on the frame last drawn in its slot, as a press, with input there, no
        /// drag already, and no confirmation standing on the plane, so a Yes never moves.
        /// </summary>
        private bool MayDrag(MenuColumn from, MenuFrame? frame)
        {
            if (Away || drag != null || placement != null) return false;
            if (frame == null && plane.Chrome.Visible) return OrganizationStanding(from, frame, null) && plane.Composition != null && plane.Front?.Footer.Confirming != true;
            if (navigator.IsCompact) return !dirty && !plane.Sliding && plane.Bar != null && plane.Bar.Shown >= 1f;
            var drawnView = plane.Shown.FirstOrDefault(item => item.Kind == from).View;
            if (drawnView == null || drawnView.Frame != frame || plane.Opening(from) || dirty || plane.Sliding) return false;
            if (navigator.Standing(from, frame, drawnView.Side) == null) return false;
            if (plane.Composition == null) return false;
            if (plane.Front?.Footer.Confirming == true) return false;
            foreach (var (_, view) in plane.Shown)
            {
                // What each footer shows, as a side panel in its frame's place carries that frame's confirmation.
                if (view.Footer.Showing?.Confirming == true) return false;
            }
            return true;
        }

        /// <summary>
        /// A file's subject held long enough (ADR 0026): the whole plane follows the hand round the eyes,
        /// only where a drag might start both when the subject was pressed and now (<see cref="MayDrag"/>);
        /// it starts no voice, and no prompt held drags. Until the plane is drawn where it is left, nothing
        /// pressed counts.
        /// </summary>
        private void OnSubjectHeld(MenuColumn from, MenuFrame? frame, Vector3 point)
        {
            var origin = subjectOrigin;
            var currentSide = plane.Shown.FirstOrDefault(item => item.Kind == from).View?.Side;
            var same = origin == (from, frame, currentSide, navigator.IsCompact, navigator.Beside);
            var taken = subjectPressTaken && same;
            subjectPressTaken = false;
            subjectOrigin = null;
            if (!same) return;
            if (!taken || !MayDrag(from, frame))
            {
                var side = plane.Shown.FirstOrDefault(item => item.Kind == from).View?.Side;
                if (setup.StageNow().BesideWindow && OrganizationStanding(from, frame, side)) OpenPlacement();
                return;
            }
            var composition = plane.Composition ?? (plane.Bar is MenuBarView bar
                ? new PlaneComposition(new[] { new PlaneColumn(bar.Size.x, bar.Size.y) }, GlazeText.Scale) : null);
            if (composition == null) return;
            var stage = setup.StageNow();
            var eyes = plane.Eyes;
            var bodies = new List<BodyInView>(stage.Characters.Count);
            dragStage.Clear();
            foreach (var character in stage.Characters)
            {
                if (character == null) continue;
                bodies.Add(WorkspaceLayout.InView(character, eyes));
                dragStage.Add(character);
            }
            var (grabYaw, grabElevation) = AnglesOf(point - eyes);
            // Last of all, the plane's own judgement, its light line included, from the geometry alone.
            drag = new MenuDrag(plane.Placed, moved, grabYaw, grabElevation, composition, bodies,
                stage.SurfaceHeight is float surface ? eyes.y - surface : (float?)null, MenuPlane.DragField,
                offset => plane.Allows(MenuDrag.Turned(plane.Placed, offset)), plane.StageYaw);
            navigator.Moved();
            // A press under way as the plane starts to move ends with it.
            dragStartedAt = Time.unscaledTime;
            plane.UnsettleShown(float.MinValue);
        }

        /// <summary>The held point moved: the plane follows where its rules allow, every part at once.</summary>
        private void OnSubjectDragged(Vector3 point)
        {
            if (drag == null) return;
            // A character gone from the stage mid-drag: its rules were judged against it, so the drag ends.
            if (FocusGuard.InputSuspended || AnyGone())
            {
                EndDrag();
                return;
            }
            var (yaw, elevation) = AnglesOf(point - plane.Eyes);
            if (!drag.Follow(yaw, elevation)) return;
            moved = drag.Moved;
            movedByHand = true;
            plane.Turn(moved);
        }

        /// <summary>The subject's press ended, its hold matured or not: the press counts no more, and any drag ends.</summary>
        private void OnSubjectLetGo()
        {
            subjectPressTaken = false;
            subjectOrigin = null;
            EndDrag();
        }

        /// <summary>The subject let go of, or the drag ended otherwise: the plane is drawn where it was left, and presses count again once it is.</summary>
        private void EndDrag()
        {
            if (drag == null) return;
            drag = null;
            ReflowFiles();
            dirty = true;
            // What was pressed under the moving plane is no press, nor a hold, as it settles again.
            plane.UnsettleShown(dragStartedAt);
        }

        /// <summary>A direction's yaw to the right and elevation up, in degrees.</summary>
        private static (float Yaw, float Elevation) AnglesOf(Vector3 toward) =>
            (Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg, Mathf.Atan2(toward.y, new Vector2(toward.x, toward.z).magnitude) * Mathf.Rad2Deg);

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
        /// The director sends nothing itself: each column sends through the host it was made with
        /// (<see cref="Bound"/>), to the session shown then, the demonstration's while it plays, which
        /// answers the recorded question and approves the recorded request as a live one would.
        /// </summary>
        public Task<CommandAckMessage>? Submit(CommandEnvelope command) => null;

        public bool KeyboardOffered => setup.KeyboardOffered();

        public void OpenKeyboard(string text, string prompt, Action<string> done, Action? dismissed = null) => setup.Keyboard(text, prompt, done, dismissed);

        public int RowsOf(string words, float columnDegrees) => MenuFrameView.RowsOf(words, columnDegrees);

        public int RowsOf(PageLine line, float columnDegrees) => MenuFrameView.RowsOf(line, columnDegrees);

        public bool FitsHalf(PageLine answer, float columnDegrees) => MenuFrameView.FitsHalf(answer, columnDegrees);

        public int TitleRows(string subject, float columnDegrees) => plane.SpatialWorkspace
            ? MenuFrameView.SpatialTitleRows(subject, Glaze.Menu.FileColumnDegrees) : MenuFrameView.TitleRows(subject, columnDegrees);

        public int PageRows(bool sourceLine) => MenuFrame.RowsAPage(TextSize, sourceLine);

        /// <summary>
        /// The available page at the independently positioned console, respecting full header, field
        /// and retained occupied agent geometry without moving either object.
        /// </summary>
        public float PageHeight(int subjectRows, bool besideMenu, bool focused = false)
        {
            var stage = setup.StageNow();
            var at = anchor?.Eyes ?? stage.Eyes;
            var occupied = new List<BodyInView>();
            foreach (var character in stage.Characters)
                if (character != null) occupied.Add(WorkspaceLayout.InView(character, at));
            var looking = anchor?.Looking ?? stage.Looking;
            var direction = new PanelDirection(Mathf.Atan2(looking.x, looking.z) * Mathf.Rad2Deg + moved.Yaw, moved.Elevation, true, false);
            return MenuPage.Height(TextSize, subjectRows, 0f, ViewField.Current, besideMenu, focused, plane.SpatialWorkspace ? WorkspaceChromeView.SpatialTop + WorkspaceChromeView.SpatialBase + Mathf.Max(0f, MenuFrameView.SpatialSubjectHeightOfRows(subjectRows, true) - MenuPage.Subject(subjectRows, true)) : WorkspaceChromeView.Height,
                centered: true, placement: direction, occupied: occupied, readable: focused);

        }

        public void OpenFile(string workstreamId)
        {
            memory.Session(setup.Session());
            if (setup.Presentation.Minimized) setup.Presentation.Restore();
            if (navigator.TryExpandTask(workstreamId)) return;
            if (memory.FileFor(State?.Journal?.JournalId, workstreamId, () => setup.File(Bound(), workstreamId)) is IMenuColumn file)
            {
                if (file is FileColumn taskFile) taskFile.Surface = WorkspaceSurface.Task;
                BindView(file);
                navigator.ShowBeside(file, workstreamId);
                KeepViews();
            }
        }

        public void OpenProjectTasks(string projectId)
        {
            if (State?.Projects.ContainsKey(projectId) != true) return;
            memory.SelectProject(projectId);
            if (navigator.BesideTask is string task && State?.Workstreams.TryGetValue(task, out var selected) == true && selected.ProjectId != projectId)
                navigator.CloseBeside();
            Open(MenuPlace.Tasks);
        }

        private void OpenAllTasks()
        {
            memory.SelectProject(null);
            Open(MenuPlace.Tasks);
        }

        private void OpenProjectDetails()
        {
            if (memory.SelectedProjectId is not string projectId || State?.Projects.ContainsKey(projectId) != true) return;
            Open(MenuPlace.Projects);
            if (navigator.Shown is ProjectsColumn projects) projects.OpenDetails(projectId);
        }

        public bool CloseFile(string workstreamId) => memory.CloseFile(workstreamId);

        /// <summary>Actual visible workspace geometry vetoes shelf overlap; placement never displaces this console.</summary>
        public bool ClearsShelf(IReadOnlyList<Vector3> worldCorners, Vector3 viewer) => plane.ClearsShelf(worldCorners, viewer);

        public bool ReportLocal(string reason) => OpenLocal(new WorkspaceNoticeColumn(this, reason));

        /// <summary>
        /// The first question's Start a project: New project in its place, with the idea Hold to talk heard,
        /// if any, as the person's own words to check, only where it opens on Your idea; a draft kept from
        /// before that opens on a later step takes no words it wasn't asked for there, and says so.
        /// </summary>
        private void StartFirstProject(string? heard)
        {
            OpenNewProject(null, null);
            if (heard == null || !(navigator.Beside is NewProjectFlow flow)) return;
            // Its frame settles the step it shows, as a kept draft's Questions or Build may turn out to be Your idea.
            _ = flow.Frame;
            if (flow.Step == NewProjectStep.YourIdea) flow.Heard(heard);
            else flow.Said(VoiceText.NotOnYourIdea);
        }

        public void OpenNewProject(string? projectId, string? projectName)
        {
            if (setup.Presentation.Minimized) setup.Presentation.Restore();
            // The flow kept for this session and journal, or one made now, bound to the session shown.
            Follow();
            if (!(memory.NewProjectFor(State?.Journal?.JournalId, () => setup.MakeNewProject(Bound())) is NewProjectFlow flow)) return;
            flow.Open(projectId, projectName);
            navigator.ShowBeside(flow, null);
        }
    }
}

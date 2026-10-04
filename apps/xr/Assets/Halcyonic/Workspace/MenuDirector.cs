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
            public Action<string, string, Action<string>> Keyboard { get; set; } = (_, _, _) => { };

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

        /// <summary>The column being made beside the menu, a task's file or New project (no task), whose page height reads against it.</summary>
        private (bool Opening, string? Task) opening;

        /// <summary>What outlives a column and a reconnect, for the app's run on one journal.</summary>
        private readonly MenuMemory memory = new MenuMemory();

        private bool dirty = true;
        private MenuBar? shownBar;

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

        /// <summary>How far a drag left the plane from where the stage places it, kept until the plane is placed afresh.</summary>
        private (float Yaw, float Elevation) moved;

        /// <summary>The plane held by a file's subject, while it is (ADR 0026).</summary>
        private MenuDrag? drag;

        /// <summary>A file's subject holds the plane, which follows the hand: nothing is pressed or held meanwhile.</summary>
        public bool Dragging => drag != null;

        /// <summary>The person dragged the plane from where it stands by itself, until Reset position or a fresh placement puts it back: Settings says so.</summary>
        public bool MovedByHand => moved != default;

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
            // The session shown first is no change; another one later is.
            director.memory.Session(setup.Session());
            // Each place's column is made afresh each time the menu shows it after opening, bound to the session shown then.
            director.navigator = new MenuNavigator(new Dictionary<MenuPlace, Func<IMenuColumn>>
            {
                [MenuPlace.Tasks] = () => new TasksColumn(director.Bound()),
                // Over the memory kept for the app's run on this journal, so a Connect in flight is never sent twice.
                [MenuPlace.Projects] = () => new ProjectsColumn(director.Bound(), setup.Commands ?? throw new InvalidOperationException("Projects sends through the host's command factory."),
                    director.memory.ProjectsFor(director.State?.Journal?.JournalId), setup.Overview, setup.ShowProject, setup.ReadLocations),
                [MenuPlace.Usage] = () => new UsageColumn(director.Bound(), setup.RecordedUsage),
                [MenuPlace.Settings] = () => new SettingsColumn(director.Bound(), setup.Space().Concat(ComfortSettings.Of(setup.Comfort, setup.ComfortSaved)).ToList()),
            });
            director.navigator.Changed += () => director.dirty = true;
            director.plane.Acted += (from, action, key, frame, side) => director.OnActed(from, action, key, frame, side);
            director.plane.Opened += () => director.navigator.OpenMenu(somethingWaits: setup.SomethingWaits());
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
                talk.Heard += voice.Heard;
                talk.Said += voice.Said;
                // The column that held leaving the plane stops the voice: its words would reach no one.
                director.navigator.Left += voice.Left;
                director.voice = voice;
            }
            FocusGuard.Left += director.OnFocusLeft;
            // New project's flow let go for another session or journal comes off the plane.
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

        /// <summary>Draws again at once, as after the stage moved: the plane re-centres on the stage it stands over, and a drag judged against where it stood ends.</summary>
        public void Redraw()
        {
            EndDrag();
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
            navigator.Moved();
            dirty = true;
        }

        /// <summary>For the editor's renders: the file's subject held at <paramref name="point"/>, past the hold's time, as a hand would.</summary>
        public void HoldSubjectForRender(Vector3 point)
        {
            OnSubjectPressed(MenuColumn.File, plane.Showing(MenuColumn.File));
            OnSubjectHeld(MenuColumn.File, plane.Showing(MenuColumn.File), point);
        }

        /// <summary>For the editor's renders: the subject pressed, its hold to mature only by <see cref="MatureHoldForRender"/>.</summary>
        public void PressSubjectForRender() => OnSubjectPressed(MenuColumn.File, plane.Showing(MenuColumn.File));

        /// <summary>For the editor's renders: the subject's hold matures at <paramref name="point"/>, after a press made earlier.</summary>
        public void MatureHoldForRender(Vector3 point) => OnSubjectHeld(MenuColumn.File, plane.Showing(MenuColumn.File), point);

        /// <summary>For the editor's renders: a press on the plane, through the director's own handler; true when a column took it.</summary>
        public bool PressForRender(MenuColumn from, string action, string? key, MenuFrame? frame, SidePanel? side) => OnActed(from, action, key, frame, side);

        /// <summary>For the editor's renders: a held prompt, through the director's own handler.</summary>
        public void HoldPromptForRender(MenuColumn from, Prompt prompt, MenuFrame? frame, SidePanel? side) => OnHoldStarted(from, prompt, frame, side);

        /// <summary>For the editor's renders, which have no microphone: the voice the director's holds go to.</summary>
        public void VoiceForRender(MenuVoice voice) => this.voice = voice;

        /// <summary>For the editor's renders: another window took focus.</summary>
        public void FocusLeftForRender() => OnFocusLeft();

        /// <summary>For the editor's renders: the stage brought its characters up to date.</summary>
        public void StageRefreshedForRender() => StageRefreshed();

        /// <summary>For the editor's renders: the held point moved to <paramref name="point"/>.</summary>
        public void DragSubjectForRender(Vector3 point) => OnSubjectDragged(point);

        /// <summary>For the editor's renders: the subject let go of.</summary>
        public void LetGoForRender() => OnSubjectLetGo();

        /// <summary>Draws what shows now, its parts at their places at once, for the renders.</summary>
        public void DrawNow()
        {
            Follow();
            LetGoIfMoved();
            Draw(immediately: true);
            plane.Advance(MenuPlane.SlideSeconds);
        }

        private void OnDestroy() => FocusGuard.Left -= OnFocusLeft;

        private void Update()
        {
            Follow();
            navigator.Tick();
            // New project's kept flow ticks while it isn't beside the menu, so a build confirmed in it goes on.
            if (memory.NewProject is NewProjectFlow flow && navigator.Beside != flow) flow.Tick();
            var nowAway = Away;
            if (away && !nowAway) dirty = true;
            away = nowAway;
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
            var another = memory.Session(setup.Session());
            another |= memory.Journal(State?.Journal?.JournalId);
            if (!another) return;
            navigator.CloseBeside();
            navigator.Renew();
        }

        /// <summary>A host for a column made now: this director's, sending only to the session shown now.</summary>
        private IMenuHost Bound() => new SessionBoundHost(this, setup.Session, setup.Submit);

        /// <summary>Focus is away, input still suspended just after it returns, or the plane is folded away.</summary>
        private static bool Away => FocusGuard.InputSuspended || FocusGuard.Folded;

        private void Draw(bool immediately)
        {
            dirty = false;
            var bar = shownBar != null && Same(setup.Bar(navigator.Place), shownBar) ? shownBar : setup.Bar(navigator.Place);
            shownBar = bar;
            // Tasks keeps the row of the file beside the menu chosen.
            if (navigator.IsOpen && navigator.PlaceColumn is TasksColumn tasks) tasks.Showing(navigator.BesideTask);
            var (menu, beside) = navigator.Frames(bar);
            var stage = setup.StageNow();
            var standing = (navigator.IsOpen, navigator.Beside);
            if (anchor == null || standing != anchoredFor)
            {
                anchor = (stage.Eyes, stage.Looking);
                anchoredFor = standing;
                moved = default;
            }
            // Another arrangement, as beside a window, or another surface: a drag made for the last holds no more.
            var arrangement = (stage.BesideWindow, stage.SurfaceHeight);
            if (arrangement != arranged)
            {
                arranged = arrangement;
                moved = default;
            }
            var (eyes, looking) = anchor.Value;
            var character = navigator.BesideTask is string task ? setup.CharacterOf(task) : null;
            plane.Show(bar, menu, beside, character, stage.Characters, eyes, looking, stage.SurfaceHeight, immediately, stage.BesideWindow, moved);
            // Only as much of a drag as still holds for what is laid now; none, and Settings says so.
            moved = plane.Moved;
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
            if (FocusGuard.InputSuspended || drag != null) return false;
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
        }

        /// <summary>What stands on the plane changed under a drag: it lets go, and the plane is placed afresh.</summary>
        private void LetGoIfMoved()
        {
            if (drag != null && (navigator.IsOpen, navigator.Beside) != anchoredFor) EndDrag();
        }

        /// <summary>The subject's press, taken only where a drag may start now; its hold matures into a drag only if this one was.</summary>
        private bool subjectPressTaken;

        private void OnSubjectPressed(MenuColumn from, MenuFrame? frame) => subjectPressTaken = MayDrag(from, frame);

        /// <summary>
        /// Whether a hold on <paramref name="from"/>'s subject, showing <paramref name="frame"/>, may drag the
        /// plane now: a task's file on the frame last drawn in its slot, as a press, with input there, no
        /// drag already, and no confirmation standing on the plane, so a Yes never moves.
        /// </summary>
        private bool MayDrag(MenuColumn from, MenuFrame? frame)
        {
            if (FocusGuard.InputSuspended || drag != null || from != MenuColumn.File || navigator.BesideTask == null) return false;
            // Beside a window the plane stands under the window's lane, which no drag may take it into.
            if (arranged.BesideWindow) return false;
            if (navigator.Standing(MenuColumn.File, frame, null) == null || plane.Composition == null) return false;
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
            var taken = subjectPressTaken;
            subjectPressTaken = false;
            if (!taken || !MayDrag(from, frame) || !(plane.Composition is PlaneComposition composition)) return;
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
            plane.UnsettleShown();
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
            plane.Turn(moved);
        }

        /// <summary>The subject's press ended, its hold matured or not: the press counts no more, and any drag ends.</summary>
        private void OnSubjectLetGo()
        {
            subjectPressTaken = false;
            EndDrag();
        }

        /// <summary>The subject let go of, or the drag ended otherwise: the plane is drawn where it was left, and presses count again once it is.</summary>
        private void EndDrag()
        {
            if (drag == null) return;
            drag = null;
            dirty = true;
            // What was pressed under the moving plane is no press, nor a hold, as it settles again.
            plane.UnsettleShown();
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
                if (setup.File(Bound(), workstreamId) is IMenuColumn file) navigator.ShowBeside(file, workstreamId);
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
                // The flow kept for this session and journal, or one made now, bound to the session shown.
                Follow();
                if (!(memory.NewProjectFor(State?.Journal?.JournalId, () => setup.MakeNewProject(Bound())) is NewProjectFlow flow)) return;
                flow.Open(projectId, projectName);
                navigator.ShowBeside(flow, null);
            }
            finally
            {
                opening = (false, null);
            }
        }
    }
}

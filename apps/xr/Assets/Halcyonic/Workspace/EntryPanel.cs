#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The one foreground panel for entering work, opened from the project rail: the welcome on the
    /// first live visit, Connect projects, More tasks, and Create a project (in
    /// <c>EntryPanel.Create.cs</c>). Each screen is a <see cref="PanelModel"/> from
    /// <see cref="EntryScreens"/>, drawn by a <see cref="PanelFrame"/> at touch distance (ADR 0023),
    /// and it opens where a workspace would, clear of every character and label
    /// (<see cref="WorkspaceLayout.PlaceForeground"/>), so running work stays in view and keeps
    /// updating while the person chooses or creates.
    /// </summary>
    /// <remarks>
    /// One foreground surface at a time: opening the panel collapses an open workspace, and a
    /// workspace opened while the panel shows, by a pinch on a character or by Open now, hides the
    /// panel and brings it back, as it was and where it was, when that workspace closes. Move steps
    /// the panel to the right, the left and back to where it opened, and, held, moves it with the
    /// hand (<see cref="PanelDrag"/>), never while a confirmation is armed; Reset position puts the panel
    /// and the rail in front of where the person faces now. Every button ignores input while the app
    /// lacks focus, and nothing here is sent until the person confirms Start building.
    /// </remarks>
    public sealed partial class EntryPanel : MonoBehaviour
    {
        private const float MoveDegrees = 28f;
        private const string WelcomedPreference = "halcyonic.entry.welcomed";

        private readonly List<BodyInView> scratch = new List<BodyInView>();
        private readonly AttentionWatch watch = new AttentionWatch();
        private ControlPlaneConnection? connection;
        private WorkspaceDirector? director;
        private ProjectRail? rail;
        private CommandFactory commands = null!;

        // Where the panel reads what it shows: the session, or, for the editor's renders, a state given as it is.
        private Func<ClientProjection?> state = () => null;
        private Func<bool> connected = () => false;
        private Func<string?> demonstration = () => null;
        private Func<WorkOverview?> overview = () => null;
        private Func<IEnumerable<CharacterTarget>> characters = () => Array.Empty<CharacterTarget>();
        private Func<float?> surface = () => null;
        private Transform root = null!;
        private PanelFrame frame = null!;
        private Screen screen;
        private bool visible;
        private bool welcomed;
        private int side;
        private Pose placedPose;
        private Vector3 placedEyes;
        private string? returnAfter;
        private PanelDrag? drag;
        private Vector3 dragEyes;
        private float dragDistance;
        private WorkstreamView? offered;
        private float nextRefresh;
        private TouchScreenKeyboard? keyboard;
        private Action<string>? typed;

        /// <summary>What the panel shows.</summary>
        public enum Screen
        {
            Welcome,
            Connect,
            MoreWork,
            CreateStart,
            Guide,
            Recap,
            Options,
            Folder,
            Review,
            Sending,
            Previous,
        }

        /// <summary>The panel shows now.</summary>
        public bool Visible => visible;

        /// <summary>The panel's root, for the editor's renders: scaled by its distance, the frame under it.</summary>
        public Transform Root => root;

        /// <summary>The frame drawing the screen, for the editor's checks.</summary>
        public PanelFrame Frame => frame;

        /// <summary>The labels showing the whole request's items on the page showing, top to bottom, for the editor's checks.</summary>
        public IReadOnlyList<TextMeshPro> RequestLabels => reviewLabels.Where(label => label.gameObject.activeSelf).ToList();

        /// <summary>Every label and button showing now, for the editor's checks that each shows what it was given, whole.</summary>
        public IEnumerable<Component> ShownParts =>
            frame.Buttons.Cast<Component>().Concat(frame.Labels).Concat(reviewLabels.Where(label => label.gameObject.activeSelf));

        /// <summary>
        /// Builds a panel that shows <paramref name="shownState"/> and <paramref name="shownOverview"/>
        /// as they are, for the editor's renders: nothing connects, nothing is sent, and it is placed
        /// clear of <paramref name="shownCharacters"/> as it would be on the headset.
        /// </summary>
        public static EntryPanel ForRender(Transform parent, ClientProjection shownState, WorkOverview shownOverview,
            IReadOnlyList<CharacterTarget> shownCharacters, float? surfaceHeight)
        {
            var go = new GameObject("Entry panel render");
            go.transform.SetParent(parent, false);
            var panel = go.AddComponent<EntryPanel>();
            panel.Build(() => shownState, () => true, () => null, () => shownOverview, () => shownCharacters, () => surfaceHeight);
            return panel;
        }

        /// <summary>
        /// Shows a screen for the editor's renders, with the creation draft given, and the work that
        /// came to need the person while creating, if any, in the banner.
        /// </summary>
        public void ShowForRender(Screen shown, ProjectIdea? shownIdea = null, NewWorkDraft? shownDraft = null, BuildSequence? shownSequence = null,
            ClientProjection? before = null, string? unresolvedCommand = null, LocationsResponse? listing = null)
        {
            rendering = true;
            locations = listing;
            locationsProblem = null;
            idea = shownIdea;
            if (shownDraft != null) draft = shownDraft;
            sequence = shownSequence;
            unresolved = unresolvedCommand;
            confirmingStartOver = false;
            notice = null;
            if (shownIdea != null && shown == Screen.Review) StartBuilding();
            showModels = shown == Screen.Options && draft.Runtime?.ModelChoice == ModelChoice.Listed;
            // What already waited when the screen opened is not news; only the needs-you render brings some.
            var now = before ?? state();
            if (now != null) watch.Begin(now);
            screen = shown;
            visible = true;
            side = 0;
            frame.Page = 0;
            Place();
            root.gameObject.SetActive(true);
            Layout();
        }

        /// <summary>The whole request shown for review, for the editor's renders.</summary>
        public NewWorkReview? Review => review;

        /// <summary>Folded while another window keeps focus; back exactly as it was when focus returns. Public for the editor's renders.</summary>
        public void ApplyFold()
        {
            if (root.gameObject.activeSelf != (visible && !FocusGuard.Folded)) root.gameObject.SetActive(visible && !FocusGuard.Folded);
        }

        /// <summary>Lays the panel out again, as after the editor turned the review's page.</summary>
        public void RedrawForRender() => Layout();

        /// <summary>Acts as a press of <paramref name="id"/> would, for the editor's renders of what a press leads to.</summary>
        public void PressForRender(string id, string? key = null) => OnActed(id, key);

        /// <summary>
        /// Holds Move at <paramref name="from"/> and drags the held point to <paramref name="to"/>, as
        /// seen from <paramref name="eyes"/>, as a hand would, for the editor's renders.
        /// </summary>
        public void DragForRender(Vector3 eyes, Vector3 from, Vector3 to)
        {
            TakeHold(eyes, from);
            Follow(to);
            drag = null;
        }

        private void Awake()
        {
            connection = GetComponent<ControlPlaneConnection>();
            director = GetComponent<WorkspaceDirector>();
            // The editor's renders build the panel themselves.
            if (connection == null || director == null) return;
            var session = connection;
            var work = director;
            var stage = GetComponent<CharacterStage>();
            Build(() => session.Session?.State, () => session.Session?.Status.IsLive == true, () => session.DemonstrationLine,
                () => rail != null ? rail.Overview : null, () => work.Targets, () => stage != null ? stage.SurfaceHeight : null);
        }

        private void Build(Func<ClientProjection?> shownState, Func<bool> isLive, Func<string?> demonstrationLine, Func<WorkOverview?> counted,
            Func<IEnumerable<CharacterTarget>> onStage, Func<float?> surfaceHeight)
        {
            state = shownState;
            connected = isLive;
            demonstration = demonstrationLine;
            overview = counted;
            characters = onStage;
            surface = surfaceHeight;
            // The same client the session introduces itself as (ControlPlaneConnection).
            commands = new CommandFactory(new ClientInfo
            {
                Name = "halcyonic-xr",
                Version = Application.version,
                DeviceLabel = SystemInfo.deviceModel,
            });
            welcomed = PlayerPrefs.GetInt(WelcomedPreference, 0) == 1;
            root = new GameObject("Entry panel").transform;
            root.SetParent(transform, false);
            // The stage's banner steps aside while the panel shows where it goes, and names it while it is folded.
            AmbientCover.Add(root.gameObject, panel: true, () => visible ? frame.Shown?.Title : null);
            frame = PanelFrame.Create(root, "Frame");
            frame.Accepting = () => visible && !FocusGuard.InputSuspended;
            frame.Acted += OnActed;
            // Move held: the panel follows the hand round the eyes until it is let go.
            frame.HoldStarted += id =>
            {
                if (id == PanelModel.Move && frame.MoveHeldPoint is Vector3 point) TakeHold(WorkspaceVisuals.HeadPosition, point);
            };
            frame.HoldEnded += (id, _) =>
            {
                if (id == PanelModel.Move) drag = null;
            };
            frame.Dragged += Follow;
            AwakeCreate();
            root.gameObject.SetActive(false);
        }

        private void Start()
        {
            rail = GetComponent<ProjectRail>();
        }

        private void OnEnable()
        {
            if (director == null) return;
            director.WorkOpened += OnWorkOpened;
            director.WorkClosed += OnWorkClosed;
            FocusGuard.Left += OnFocusLeft;
        }

        private void OnDisable()
        {
            if (director == null) return;
            director.WorkOpened -= OnWorkOpened;
            director.WorkClosed -= OnWorkClosed;
            FocusGuard.Left -= OnFocusLeft;
        }

        private void OnDestroy()
        {
            DestroyCreate();
            if (root != null) Destroy(root.gameObject);
        }

        private void Update()
        {
            if (director == null) return;
            ApplyFold();
            PollKeyboard();
            UpdateCreate();
            if (!visible && !welcomed && returnAfter == null && director.OpenWorkstream == null && Live && keyboard == null) Open(Screen.Welcome);
            if (!visible || Time.unscaledTime < nextRefresh) return;
            Layout();
        }

        /// <summary>Opens Connect projects: every project the Mac knows, to show or hide, with its work.</summary>
        public void ShowConnect()
        {
            frame.Page = 0;
            Open(Screen.Connect);
        }

        /// <summary>Opens More tasks: every task without a character, what needs the person first.</summary>
        public void ShowMoreWork()
        {
            frame.Page = 0;
            Open(Screen.MoreWork);
        }

        /// <summary>Shows or brings the panel back on a screen; placed in front of the person when it was not showing.</summary>
        private void Open(Screen shown)
        {
            if (FocusGuard.InputSuspended && shown != Screen.Welcome) return;
            returnAfter = null;
            if (screen == Screen.Welcome && shown != Screen.Welcome) Welcomed();
            if (shown != screen) frame.Page = 0;
            screen = shown;
            // One foreground surface: the workspace and the panel open in the same place.
            director?.CloseWork();
            if (!visible)
            {
                visible = true;
                side = 0;
                Place();
                root.gameObject.SetActive(true);
            }
            Layout();
        }

        private void Hide()
        {
            visible = false;
            root.gameObject.SetActive(false);
            CloseKeyboard();
        }

        private void Welcomed()
        {
            if (welcomed) return;
            welcomed = true;
            PlayerPrefs.SetInt(WelcomedPreference, 1);
            PlayerPrefs.Save();
        }

        private void OnWorkOpened(string workstreamId)
        {
            if (!visible) return;
            // The panel waits, as it is, until that workspace closes.
            returnAfter = workstreamId;
            visible = false;
            root.gameObject.SetActive(false);
        }

        private void OnWorkClosed(string workstreamId)
        {
            if (returnAfter != workstreamId) return;
            returnAfter = null;
            visible = true;
            root.gameObject.SetActive(true);
            Layout();
        }

        private bool Live => connected() && demonstration() == null;

        /// <summary>The panel's size as placement takes it: at touch distance, half its width and height in meters.</summary>
        private PanelSize PanelSize => new PanelSize(PanelFrame.Distance, frame.Size.x / 2f * PanelFrame.Scale, frame.Size.y / 2f * PanelFrame.Scale);

        /// <summary>
        /// Where a foreground panel opens, clear of every character and its label, facing the eyes, at
        /// touch distance; again only when the panel opens or the person asks for Reset position.
        /// </summary>
        private void Place()
        {
            var eyes = WorkspaceVisuals.HeadPosition;
            var looking = WorkspaceVisuals.Head != null ? WorkspaceVisuals.Head.forward : Vector3.forward;
            var (pose, _) = WorkspaceLayout.PlaceForeground(characters(), eyes, looking, surface(), scratch, PanelSize);
            placedPose = pose;
            placedEyes = eyes;
            Pose();
        }

        /// <summary>The placed pose, turned about the eyes to the side Move chose.</summary>
        private void Pose()
        {
            var turn = Quaternion.Euler(0f, side * MoveDegrees, 0f);
            root.SetPositionAndRotation(placedEyes + turn * (placedPose.position - placedEyes), turn * placedPose.rotation);
            root.localScale = Vector3.one * PanelFrame.Scale;
        }

        private void Move()
        {
            side = side == 0 ? 1 : side > 0 ? -1 : 0;
            Pose();
            Layout();
        }

        /// <summary>Move held: the panel takes hold where the hand holds it, as seen from <paramref name="eyes"/>.</summary>
        private void TakeHold(Vector3 eyes, Vector3 point)
        {
            drag = null;
            if (frame.Shown?.CanMove != true) return;
            var (panelYaw, panelElevation) = AnglesOf(root.position - eyes);
            var (heldYaw, heldElevation) = AnglesOf(point - eyes);
            var height = surface();
            drag = new PanelDrag(panelYaw, panelElevation, heldYaw, heldElevation, PanelSize, height.HasValue ? eyes.y - height.Value : (float?)null,
                ViewField.Current);
            dragEyes = eyes;
            dragDistance = Vector3.Distance(eyes, root.position);
        }

        /// <summary>
        /// The panel follows the held point round the eyes, at its distance and facing them, its center
        /// in the comfortable band; never while a confirmation is armed. Move and Reset position start from there.
        /// </summary>
        private void Follow(Vector3 point)
        {
            if (drag == null || frame.Shown?.CanMove != true) return;
            var (heldYaw, heldElevation) = AnglesOf(point - dragEyes);
            var (yaw, elevation) = drag.Follow(heldYaw, heldElevation);
            var forward = Quaternion.Euler(-elevation, yaw, 0f) * Vector3.forward;
            placedEyes = dragEyes;
            placedPose = new Pose(dragEyes + forward * dragDistance, Quaternion.LookRotation(forward, Vector3.up));
            side = 0;
            Pose();
        }

        /// <summary>A direction's yaw to the right and elevation up, in degrees.</summary>
        private static (float Yaw, float Elevation) AnglesOf(Vector3 toward) =>
            (Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg, Mathf.Atan2(toward.y, new Vector2(toward.x, toward.z).magnitude) * Mathf.Rad2Deg);

        private void ResetPosition()
        {
            side = 0;
            Place();
            if (rail != null) rail.ResetPosition();
            Layout();
        }

        private void Close()
        {
            if (screen == Screen.Welcome) Welcomed();
            Hide();
        }

        /// <summary>Draws the current screen's model; the whole request, when it shows, in the space the frame leaves for it.</summary>
        private void Layout()
        {
            nextRefresh = Time.unscaledTime + 0.5f;
            DropStaleNotice();
            var model = screen switch
            {
                Screen.Welcome => EntryScreens.Welcome(),
                Screen.Connect => EntryScreens.ConnectProjects(overview(), connected(), demonstration() != null),
                Screen.MoreWork => EntryScreens.MoreTasks(overview(), connected()),
                _ => CreateModel(),
            };
            frame.Show(model);
            if (screen == Screen.Review && review != null) LayRequest(model);
            else HideRequest();
        }

        /// <summary>
        /// Work that came to need the person while they create, offered in the banner: Open now or Keep
        /// creating. It never switches by itself; opening it keeps the draft, and the panel returns as
        /// it was when that workspace closes.
        /// </summary>
        private PanelBanner? Banner()
        {
            var now = state();
            offered = now == null ? null : watch.Next(now);
            return offered == null ? null : EntryScreens.WaitingBanner(offered);
        }

        /// <summary>What a press on the frame does, by the action's id: the frame's own, the welcome's and lists', then Create's.</summary>
        private void OnActed(string id, string? key)
        {
            switch (id)
            {
                case PanelModel.Close:
                    Close();
                    return;
                case PanelModel.Move:
                    Move();
                    return;
                case PanelModel.ResetPosition:
                    ResetPosition();
                    return;
                case EntryScreens.Connect:
                    ShowConnect();
                    return;
                case EntryScreens.Create:
                    ShowCreate(null, null);
                    return;
                case EntryScreens.ToggleProject when key != null:
                    rail?.ToggleProject(key);
                    Layout();
                    return;
                case EntryScreens.AddTask when key != null:
                    ShowCreate(key, overview()?.Projects.FirstOrDefault(project => project.ProjectId == key)?.Name);
                    return;
                case EntryScreens.ShowAll:
                    rail?.ShowAllProjects();
                    Layout();
                    return;
                case EntryScreens.Done when screen == Screen.Connect:
                    Close();
                    return;
                case EntryScreens.OpenWork when key != null:
                    director?.OpenWork(key);
                    return;
                case EntryScreens.OpenNow when offered != null:
                    var work = offered.WorkstreamId;
                    watch.Dismiss(work);
                    director?.OpenWork(work);
                    return;
                case EntryScreens.KeepCreating when offered != null:
                    watch.Dismiss(offered.WorkstreamId);
                    Layout();
                    return;
                default:
                    ActCreate(id, key);
                    return;
            }
        }

        /// <summary>
        /// The Quest system keyboard (TouchScreenKeyboard with Require System Keyboard on). While it
        /// is open the app loses input focus; the typed text counts when it closes with Done.
        /// </summary>
        private void OpenKeyboard(string initial, string prompt, Action<string> done)
        {
            if (!TouchScreenKeyboard.isSupported)
            {
                notice = (screen, EntryText.NoKeyboard);
                Layout();
                return;
            }
            typed = done;
            keyboard = FocusGuard.Track(TouchScreenKeyboard.Open(initial, TouchScreenKeyboardType.Default, true, false, false, false, prompt));
        }

        private void PollKeyboard()
        {
            var open = keyboard;
            if (open == null || open.status == TouchScreenKeyboard.Status.Visible) return;
            keyboard = null;
            var done = typed;
            typed = null;
            if (open.status == TouchScreenKeyboard.Status.Done) done?.Invoke(open.text ?? "");
            if (visible) Layout();
        }

        private void CloseKeyboard()
        {
            if (keyboard == null) return;
            keyboard.active = false;
            keyboard = null;
            typed = null;
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The one foreground panel for entering work, opened from the project rail: the welcome on the
    /// first live visit, Connect projects, More work, and Create a project (in
    /// <c>EntryPanel.Create.cs</c>). It has the workspace's size and opens where the workspace would,
    /// clear of every character (<see cref="WorkspaceLayout.PlaceForeground"/>), so running work stays
    /// in view and keeps updating while the person chooses or creates.
    /// </summary>
    /// <remarks>
    /// One foreground surface at a time: opening the panel collapses an open workspace, and a
    /// workspace opened while the panel shows, by a pinch on a character or by Open now, hides the
    /// panel and brings it back, as it was and where it was, when that workspace closes. Move steps
    /// the panel to the right, the left and back to where it opened; Reset position puts the panel
    /// and the rail in front of where the person faces now. Every button ignores input while the app
    /// lacks focus, and nothing here is sent until the person confirms Start building.
    /// </remarks>
    public sealed partial class EntryPanel : MonoBehaviour
    {
        public const float Width = WorkspacePanel.Width;
        public const float Height = WorkspacePanel.Height;
        private const float Margin = 0.035f;
        private const float Left = -Width / 2f + Margin;
        private const float Right = Width / 2f - Margin;
        private const float ContentWidth = Width - 2f * Margin;
        private const float Top = Height / 2f - 0.03f;
        private const float FrameHeight = 0.056f;
        private const float LineTop = 0.215f;
        private const float BodyTop = 0.145f;
        private const float RowHeight = 0.074f;
        private const float RowPitch = 0.084f;
        private const float BigHeight = 0.19f;
        private const float BottomHeight = 0.064f;
        private const float BottomCenter = -Height / 2f + 0.023f + BottomHeight / 2f;
        private const float Gap = 0.012f;
        private const int Rows = 4;
        private const float MoveDegrees = 28f;

        /// <summary>Smaller than the workspace's title, so the longest title and the frame's three buttons share the top row.</summary>
        private const float TitleSize = 0.26f;
        private const string WelcomedPreference = "halcyonic.entry.welcomed";

        private readonly HashSet<Component> used = new HashSet<Component>();
        private readonly List<Component> parts = new List<Component>();
        private readonly List<Slot> rows = new List<Slot>();
        private readonly List<Slot> sides = new List<Slot>();
        private readonly List<Slot> bigs = new List<Slot>();
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
        private TextMeshPro title = null!;
        private TextMeshPro line = null!;
        private TextMeshPro body = null!;
        private TextMeshPro note = null!;
        private TextMeshPro bannerText = null!;
        private TextMeshPro pageCaption = null!;
        private Slot move = null!;
        private Slot reset = null!;
        private Slot close = null!;
        private Slot openNow = null!;
        private Slot keepCreating = null!;
        private Slot bottomLeft = null!;
        private Slot bottomMiddle = null!;
        private Slot bottomRight = null!;
        private Slot pagePrevious = null!;
        private Slot pageNext = null!;
        private Screen screen;
        private bool visible;
        private bool welcomed;
        private int side;
        private int page;
        private Pose placedPose;
        private Vector3 placedEyes;
        private string? returnAfter;
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

        /// <summary>The panel's root, for the editor's renders.</summary>
        public Transform Root => root;

        /// <summary>Every label and button showing now, for the editor's checks that each shows what it was given, whole.</summary>
        public IEnumerable<Component> ShownParts => used;

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
            if (shownIdea != null && shown == Screen.Review) StartBuilding();
            showModels = shown == Screen.Options && draft.Runtime?.ModelChoice == ModelChoice.Listed;
            if (before != null) watch.Begin(before);
            screen = shown;
            visible = true;
            side = 0;
            Place();
            root.gameObject.SetActive(true);
            Layout();
        }

        /// <summary>The whole request shown for review, for the editor's renders.</summary>
        public NewWorkReview? Review => review;

        /// <summary>Lays the panel out again, as after the editor turned the review's page.</summary>
        public void RedrawForRender() => Layout();

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
            WorkspaceVisuals.Plate(root, "Background", new Vector2(Width, Height), WorkspaceVisuals.PanelColor, WorkspaceVisuals.PanelPlateOrder);
            // The background takes the ray, so nothing behind the panel is pointed at through it.
            PointerTarget.Rectangle(root.gameObject, new Vector2(Width, Height), ray: true, poke: false);
            title = Label("Title", TitleSize, WorkspaceVisuals.TextColor, wrap: false);
            line = Label("Line", WorkspaceVisuals.DetailSize, WorkspaceVisuals.SecondaryColor, wrap: true);
            body = Label("Body", WorkspaceVisuals.BodySize, WorkspaceVisuals.TextColor, wrap: true);
            note = Label("Note", WorkspaceVisuals.DetailSize, WorkspaceVisuals.AttentionColor, wrap: true);
            bannerText = Label("Needs you", WorkspaceVisuals.DetailSize, WorkspaceVisuals.AttentionColor, wrap: true);
            pageCaption = Label("Page", WorkspaceVisuals.CaptionSize, WorkspaceVisuals.SecondaryColor, wrap: false);
            pageCaption.alignment = TextAlignmentOptions.Center;
            move = MakeSlot("Move", FrameHeight, WorkspaceVisuals.CaptionSize);
            reset = MakeSlot("Reset position", FrameHeight, WorkspaceVisuals.CaptionSize);
            close = MakeSlot("Close", FrameHeight, WorkspaceVisuals.CaptionSize);
            openNow = MakeSlot("Open now", FrameHeight, WorkspaceVisuals.DetailSize);
            keepCreating = MakeSlot("Keep creating", FrameHeight, WorkspaceVisuals.DetailSize);
            for (var index = 0; index < Rows; index++)
            {
                rows.Add(MakeSlot("Row " + index, RowHeight, WorkspaceVisuals.DetailSize));
                sides.Add(MakeSlot("Row action " + index, RowHeight, WorkspaceVisuals.DetailSize));
            }
            for (var index = 0; index < 2; index++) bigs.Add(MakeSlot("Choice " + index, BigHeight, WorkspaceVisuals.BodySize));
            bottomLeft = MakeSlot("Bottom left", BottomHeight, WorkspaceVisuals.DetailSize);
            bottomMiddle = MakeSlot("Bottom middle", BottomHeight, WorkspaceVisuals.DetailSize);
            bottomRight = MakeSlot("Bottom right", BottomHeight, WorkspaceVisuals.DetailSize);
            pagePrevious = MakeSlot("Previous", FrameHeight, WorkspaceVisuals.DetailSize);
            pageNext = MakeSlot("Next", FrameHeight, WorkspaceVisuals.DetailSize);
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
        }

        private void OnDisable()
        {
            if (director == null) return;
            director.WorkOpened -= OnWorkOpened;
            director.WorkClosed -= OnWorkClosed;
        }

        private void OnDestroy()
        {
            DestroyCreate();
            if (root != null) Destroy(root.gameObject);
        }

        private void Update()
        {
            if (director == null) return;
            PollKeyboard();
            UpdateCreate();
            if (!visible && !welcomed && returnAfter == null && director.OpenWorkstream == null && Live && keyboard == null) Open(Screen.Welcome);
            if (!visible || Time.unscaledTime < nextRefresh) return;
            Layout();
        }

        /// <summary>Opens Connect projects: every project Halcyonic knows, to show or hide, with its counts.</summary>
        public void ShowConnect()
        {
            page = 0;
            Open(Screen.Connect);
        }

        /// <summary>Opens More work: every workstream without a character, what needs the person first.</summary>
        public void ShowMoreWork()
        {
            page = 0;
            Open(Screen.MoreWork);
        }

        /// <summary>Shows or brings the panel back on a screen; placed in front of the person when it was not showing.</summary>
        private void Open(Screen shown)
        {
            if (FocusGuard.InputSuspended && shown != Screen.Welcome) return;
            returnAfter = null;
            if (screen == Screen.Welcome && shown != Screen.Welcome) Welcomed();
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

        /// <summary>
        /// Where the workspace would open, clear of every character, facing the eyes, at the workspace's
        /// scale; again only when the panel opens or the person asks for Reset position.
        /// </summary>
        private void Place()
        {
            var eyes = WorkspaceVisuals.HeadPosition;
            var looking = WorkspaceVisuals.Head != null ? WorkspaceVisuals.Head.forward : Vector3.forward;
            var (pose, _) = WorkspaceLayout.PlaceForeground(characters(), eyes, looking, surface(), scratch);
            placedPose = pose;
            placedEyes = eyes;
            Pose();
        }

        /// <summary>The placed pose, turned about the eyes to the side Move chose.</summary>
        private void Pose()
        {
            var turn = Quaternion.Euler(0f, side * MoveDegrees, 0f);
            root.SetPositionAndRotation(placedEyes + turn * (placedPose.position - placedEyes), turn * placedPose.rotation);
            root.localScale = Vector3.one * WorkspaceLayout.Scale;
        }

        private void Move()
        {
            side = side == 0 ? 1 : side > 0 ? -1 : 0;
            Pose();
            Layout();
        }

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

        /// <summary>Lays the current screen out: every part it uses shown, every other part hidden.</summary>
        private void Layout()
        {
            nextRefresh = Time.unscaledTime + 0.5f;
            used.Clear();
            body.fontSize = WorkspaceVisuals.BodySize;
            pageCaption.alignment = TextAlignmentOptions.Center;
            Frame();
            switch (screen)
            {
                case Screen.Welcome:
                    LayoutWelcome();
                    break;
                case Screen.Connect:
                    LayoutConnect();
                    break;
                case Screen.MoreWork:
                    LayoutMoreWork();
                    break;
                default:
                    LayoutCreate();
                    break;
            }
            foreach (var part in parts)
            {
                if (used.Contains(part)) continue;
                if (part is PanelButton button) button.Hide();
                else part.gameObject.SetActive(false);
            }
        }

        /// <summary>The title at the top left; Move, Reset position and Close at the top right.</summary>
        private void Frame()
        {
            var x = Right;
            x = PutRightAligned(close, screen == Screen.Welcome ? EntryText.NotNow : EntryText.Close, x, Top - FrameHeight / 2f, Close);
            x = PutRightAligned(reset, EntryText.ResetPosition, x, Top - FrameHeight / 2f, ResetPosition);
            x = PutRightAligned(move, EntryText.Move(side), x, Top - FrameHeight / 2f, Move);
            Say(title, TitleOf(), new Vector2(Left, Top), new Vector2(x - Left, 0.05f));
        }

        private string TitleOf() => screen switch
        {
            Screen.Welcome => EntryText.WelcomeTitle,
            Screen.Connect => EntryText.ConnectProjects,
            Screen.MoreWork => EntryText.MoreWork,
            _ => CreateTitle(),
        };

        private void LayoutWelcome()
        {
            SayLine(EntryText.WelcomeLine);
            var width = (ContentWidth - Gap) / 2f;
            Put(bigs[0], EntryText.ConnectProjects, new Vector2(Left + width / 2f, -0.02f), width, ShowConnect, detail: EntryText.ConnectInvite);
            Put(bigs[1], EntryText.CreateProject, new Vector2(Right - width / 2f, -0.02f), width, () => ShowCreate(null, null), detail: EntryText.CreateInvite);
        }

        /// <summary>
        /// Every project Halcyonic's journal knows, a page at a time: pressing one shows or hides its
        /// work on the stage; Add work starts new work in it. Its counts include hidden work, so what
        /// needs the person is never hidden with its project.
        /// </summary>
        private void LayoutConnect()
        {
            var overview = this.overview();
            SayLine(demonstration() != null ? EntryText.ExampleProjects
                : connected() ? EntryText.ConnectLine
                : EntryText.ConnectLine + " " + EntryText.LastKnownProjects);
            if (overview == null || overview.Projects.Count == 0)
            {
                Say(body, overview == null ? "Waiting for your Mac." : EntryText.NoProjects, new Vector2(Left, BodyTop), new Vector2(ContentWidth, 0.12f));
                if (Live) Put(bigs[0], EntryText.CreateProject, new Vector2(0f, -0.07f), 0.4f, () => ShowCreate(null, null), detail: EntryText.CreateInvite);
                return;
            }
            var projects = Paged(overview.Projects);
            var rowWidth = ContentWidth - 0.17f - Gap;
            for (var index = 0; index < projects.Count; index++)
            {
                var project = projects[index];
                var y = BodyTop - RowHeight / 2f - index * RowPitch;
                Put(rows[index], project.Name, new Vector2(Left + rowWidth / 2f, y), rowWidth, () =>
                    {
                        rail?.ToggleProject(project.ProjectId);
                        Layout();
                    },
                    detail: EntryText.ProjectDetail(project), detailColor: project.NeedsYou > 0 ? WorkspaceVisuals.AttentionColor : (Color?)null);
                if (Live)
                {
                    Put(sides[index], EntryText.AddWork, new Vector2(Right - 0.085f, y), 0.17f, () => ShowCreate(project.ProjectId, project.Name));
                }
            }
            Pager(overview.Projects.Count);
            if (!overview.Projects.All(project => project.Shown))
            {
                Put(bottomLeft, EntryText.ShowAll, new Vector2(Left + 0.1f, BottomCenter), 0.2f, () =>
                {
                    rail?.ShowAllProjects();
                    Layout();
                });
            }
        }

        /// <summary>
        /// Every workstream without a character, what needs the person first, a page at a time;
        /// pressing one brings it to the stage and opens it. Nothing is sent.
        /// </summary>
        private void LayoutMoreWork()
        {
            SayLine(EntryText.MoreWorkLine);
            var overview = this.overview();
            if (overview == null || overview.OffStage.Count == 0)
            {
                Say(body, overview == null ? "Waiting for your Mac." : EntryText.AllOnStage, new Vector2(Left, BodyTop), new Vector2(ContentWidth, 0.12f));
                return;
            }
            var live = connected();
            var work = Paged(overview.OffStage);
            for (var index = 0; index < work.Count; index++)
            {
                var item = work[index];
                var y = BodyTop - RowHeight / 2f - index * RowPitch;
                var needing = CharacterLineup.TierOf(item.Workstream) == LineupTier.NeedsYou;
                Put(rows[index], LabelText.Plain(item.Workstream.Title), new Vector2(0f, y), ContentWidth, () => director?.OpenWork(item.Workstream.WorkstreamId),
                    detail: EntryText.OffStageDetail(item, live), detailColor: needing ? WorkspaceVisuals.AttentionColor : (Color?)null);
            }
            Pager(overview.OffStage.Count);
        }

        /// <summary>The items on the current page, keeping the page within the list.</summary>
        private List<T> Paged<T>(IReadOnlyList<T> items)
        {
            var pages = Math.Max(1, (items.Count + Rows - 1) / Rows);
            page = Mathf.Clamp(page, 0, pages - 1);
            return items.Skip(page * Rows).Take(Rows).ToList();
        }

        /// <summary>Previous and Next page at the bottom, with where the page is, while the list takes more than one.</summary>
        private void Pager(int count)
        {
            var pages = (count + Rows - 1) / Rows;
            if (pages <= 1) return;
            Say(pageCaption, "Page " + (page + 1) + " of " + pages, new Vector2(-0.08f, BottomCenter + 0.012f), new Vector2(0.16f, 0.03f));
            if (page > 0) PutRightAligned(bottomMiddle, "Previous page", Right - 0.2f - Gap, BottomCenter, () => { page--; Layout(); });
            if (page < pages - 1) PutRightAligned(bottomRight, "Next page", Right, BottomCenter, () => { page++; Layout(); });
        }

        /// <summary>
        /// Work that came to need the person while they create, offered in the line under the title:
        /// Open now or Keep creating. It never switches by itself; opening it keeps the draft, and the
        /// panel returns as it was when that workspace closes.
        /// </summary>
        private bool Banner()
        {
            var now = state();
            offered = now == null ? null : watch.Next(now);
            if (offered == null) return false;
            var work = offered;
            var x = PutRightAligned(keepCreating, EntryText.KeepCreating, Right, LineTop - FrameHeight / 2f, () =>
            {
                watch.Dismiss(work.WorkstreamId);
                Layout();
            });
            x = PutRightAligned(openNow, EntryText.OpenNow, x, LineTop - FrameHeight / 2f, () =>
            {
                watch.Dismiss(work.WorkstreamId);
                director?.OpenWork(work.WorkstreamId);
            });
            Say(bannerText, EntryText.NeedsYouNow(LabelText.Plain(work.Title)), new Vector2(Left, LineTop), new Vector2(x - Left, 0.058f));
            return true;
        }

        private void SayLine(string text) => Say(line, text, new Vector2(Left, LineTop), new Vector2(ContentWidth, 0.058f));

        /// <summary>Shows a label, its text by the one rule, with its top left corner at <paramref name="topLeft"/>.</summary>
        private void Say(TextMeshPro label, string text, Vector2 topLeft, Vector2 size)
        {
            WorkspaceVisuals.SetLiteral(label, text);
            label.rectTransform.localPosition = new Vector3(topLeft.x, topLeft.y, -0.001f);
            label.rectTransform.sizeDelta = size;
            label.gameObject.SetActive(true);
            used.Add(label);
        }

        private void SayLines(TextMeshPro label, IEnumerable<string> lines, Vector2 topLeft, Vector2 size)
        {
            WorkspaceVisuals.SetLiteralLines(label, lines);
            label.rectTransform.localPosition = new Vector3(topLeft.x, topLeft.y, -0.001f);
            label.rectTransform.sizeDelta = size;
            label.gameObject.SetActive(true);
            used.Add(label);
        }

        private void Put(Slot slot, string text, Vector2 center, float width, Action press, bool confirm = false, string? detail = null,
            Color? detailColor = null)
        {
            slot.Press = press;
            slot.Button.Show(text, center, width, confirm, detail, detailColor);
            used.Add(slot.Button);
        }

        /// <summary>Puts a button with its right edge at <paramref name="right"/>; returns where its left edge is, less the gap.</summary>
        private float PutRightAligned(Slot slot, string text, float right, float centerY, Action press, bool confirm = false)
        {
            var width = slot.Button.Measure(text, 0.1f);
            Put(slot, text, new Vector2(right - width / 2f, centerY), width, press, confirm);
            return right - width - Gap;
        }

        private TextMeshPro Label(string name, float size, Color color, bool wrap)
        {
            var label = WorkspaceVisuals.Text(root, name, size, color, new Vector2(ContentWidth, 0.05f), TextAlignmentOptions.TopLeft, wrap,
                WorkspaceVisuals.PanelTextOrder);
            parts.Add(label);
            return label;
        }

        private Slot MakeSlot(string name, float height, float textSize)
        {
            var slot = new Slot(PanelButton.Create(root, name, height, textSize));
            slot.Button.Accepting = () => visible && !FocusGuard.InputSuspended;
            slot.Button.Pressed += () => slot.Press?.Invoke();
            parts.Add(slot.Button);
            return slot;
        }

        /// <summary>
        /// The Quest system keyboard (TouchScreenKeyboard with Require System Keyboard on). While it
        /// is open the app loses input focus; the typed text counts when it closes with Done.
        /// </summary>
        private void OpenKeyboard(string initial, string prompt, Action<string> done)
        {
            if (!TouchScreenKeyboard.isSupported)
            {
                notice = "Typing needs the headset's system keyboard.";
                Layout();
                return;
            }
            typed = done;
            keyboard = TouchScreenKeyboard.Open(initial, TouchScreenKeyboardType.Default, true, false, false, false, prompt);
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

        /// <summary>A reused button and what pressing it does on the current screen.</summary>
        private sealed class Slot
        {
            public Slot(PanelButton button) => Button = button;

            public PanelButton Button { get; }

            public Action? Press { get; set; }
        }
    }
}

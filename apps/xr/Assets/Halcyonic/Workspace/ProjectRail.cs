#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The low project rail under the stage, in reach of a seated person, in two rows (ADR 0023).
    /// Above, filters: the projects that matter most now, what waits for the person first, each a
    /// pill outlined in the accent while its work is on the stage, with its counts, pressed to show or
    /// hide that work; and See other tasks, while some work has no character, with how much of it
    /// waits for the person. Below, actions: Connect projects, with how many projects show, and Create
    /// a project, at the left; Usage left, when it is offered, and Settings, compact, at the right,
    /// since they open sheets rather than act on work. It says its counts in full where a button has
    /// room, else in their short form. It opens the entry panel (<see cref="EntryPanel"/>) and the
    /// Settings sheet (<see cref="SettingsSheet"/>) for everything else.
    /// </summary>
    /// <remarks>
    /// Which projects show is kept on the device for each journal (<see cref="StageVisibility"/>), a
    /// presentation choice and never an authorization boundary: hiding a project hides its characters
    /// only, and its attention stays counted here and listed in See other tasks. It stands 0.43 m from
    /// the eyes, 44.5 degrees below eye level, about 24 degrees to either side, its rows 40 to 49
    /// degrees down, and steps out of the way while the entry panel, a workspace, Usage left or
    /// Settings is open, since those open where it would be in view: one foreground surface at a time.
    /// Every button ignores input while the app lacks focus. It stays where it was put, in front of
    /// the person when the app starts, and again when the stage moves onto or off a desk, or when
    /// Reset position is pressed.
    /// </remarks>
    public sealed class ProjectRail : MonoBehaviour
    {
        /// <summary>From the eyes to the rail's middle, at touch distance (ADR 0023).</summary>
        public const float Distance = 0.43f;

        /// <summary>The rail's middle, below eye level.</summary>
        public const float BelowDegrees = 44.5f;

        /// <summary>How far to either side the rail reaches, its buttons included.</summary>
        public const float HalfWidthDegrees = 24f;

        /// <summary>The widest a filter pill gets when few show, so a lone project is not a bar.</summary>
        private const float MaxPillDegrees = 17f;

        /// <summary>Over a desk, nearer, so it stays below the lineup's labels in the person's view.</summary>
        public const float DeskAhead = 0.3f;

        public const float DeskBelow = 0.42f;

        private const int MostChips = 3;
        private const string VisibilityPreference = "halcyonic.stage.visibility";

        private readonly List<GlazeButton> chips = new List<GlazeButton>();
        private readonly List<string> chipProjects = new List<string>();
        private readonly HashSet<GlazeButton> used = new HashSet<GlazeButton>();
        private ControlPlaneConnection? connection;
        private CharacterStage? stage;
        private WorkspaceDirector? director;
        private EntryPanel? entry;
        private UsageLeftGlance? glance;
        private SettingsSheet? settings;
        private Transform root = null!;
        private GlazeButton connect = null!;
        private GlazeButton more = null!;
        private GlazeButton create = null!;
        private GlazeButton usageLeft = null!;
        private GlazeButton settingsButton = null!;
        private string? usageLeftLabel;
        private StageVisibility visibility = new StageVisibility();
        private Func<WorkOverview?> count = () => null;
        private Func<float?> surface = () => null;
        private bool placedOnSurface;
        private int savedVersion;
        private float nextRefresh;
        private bool placed;

        /// <summary>The gap kept between buttons, 12 mm wherever the rail stands, in its own units.</summary>
        private float gap = Glaze.TargetGapMeters / Distance;

        /// <summary>What the rail counted last: every project and what has no character.</summary>
        public WorkOverview? Overview { get; private set; }

        /// <summary>Which projects' work stands on the stage.</summary>
        public StageVisibility Visibility => visibility;

        /// <summary>The rail's root, for the editor's renders.</summary>
        public Transform Root => root;

        /// <summary>Every button the rail shows now, for the editor's checks.</summary>
        public IEnumerable<GlazeButton> Shown => used;

        /// <summary>The Usage left button, which the rail places and its glance answers (<see cref="OfferUsageLeft"/>).</summary>
        public GlazeButton UsageLeft => usageLeft;

        /// <summary>The rail's width in its own units: <see cref="HalfWidthDegrees"/> to either side.</summary>
        public static float Width => 2f * GlazeTokens.Units(HalfWidthDegrees);

        /// <summary>
        /// Builds a rail that shows <paramref name="overview"/> as it is, for the editor's renders:
        /// nothing reads a session, and its buttons open nothing.
        /// </summary>
        public static ProjectRail ForRender(Transform parent, WorkOverview overview, float? surfaceHeight)
        {
            var go = new GameObject("Project rail render");
            go.transform.SetParent(parent, false);
            var rail = go.AddComponent<ProjectRail>();
            rail.Build(() => overview, () => surfaceHeight);
            rail.Refresh();
            return rail;
        }

        /// <summary>
        /// Shows the Usage left button with <paramref name="label"/> at the lower row's right end, for
        /// the glance that answers it, or hides it for null. The rail owns its place and size.
        /// </summary>
        public void OfferUsageLeft(string? label)
        {
            if (usageLeftLabel == label) return;
            usageLeftLabel = label;
            nextRefresh = 0f;
            if (root != null) Refresh();
        }

        private void Awake()
        {
            connection = GetComponent<ControlPlaneConnection>();
            stage = GetComponent<CharacterStage>();
            director = GetComponent<WorkspaceDirector>();
            // The editor's renders build the rail themselves.
            if (connection == null || stage == null) return;
            visibility = StageVisibility.Load(PlayerPrefs.GetString(VisibilityPreference, ""));
            savedVersion = visibility.Version;
            var shownStage = stage;
            var shownConnection = connection;
            Build(() => shownConnection.Session == null ? null
                : WorkOverview.Of(shownConnection.Session.State, visibility, id => shownStage.SlotOf(id) >= 0), () => shownStage.SurfaceHeight);
            var found = GetComponent<EntryPanel>();
            entry = found != null ? found : gameObject.AddComponent<EntryPanel>();
            settings = SettingsSheet.On(gameObject);
        }

        private void Build(Func<WorkOverview?> counting, Func<float?> surfaceHeight)
        {
            count = counting;
            surface = surfaceHeight;
            root = new GameObject("Project rail").transform;
            root.SetParent(transform, false);
            connect = Button("Connect projects", ButtonRole.Secondary, false, () => entry?.ShowConnect());
            create = Button("Create a project", ButtonRole.Secondary, false, () => entry?.ShowCreate(null, null));
            more = Button("See other tasks", ButtonRole.Secondary, false, () => entry?.ShowMoreWork());
            usageLeft = Button("Usage left", ButtonRole.Secondary, true, null);
            settingsButton = Button("Settings", ButtonRole.Secondary, true, () => settings?.Toggle());
            for (var index = 0; index < MostChips; index++)
            {
                var slot = index;
                chips.Add(Button("Project " + index, ButtonRole.Filter, false, () => Toggle(slot)));
            }
        }

        private void OnEnable()
        {
            if (connection == null || stage == null) return;
            connection.Changed += OnChanged;
            stage.Refreshed += OnStageRefreshed;
            // The stage may have placed characters before the rail chose which projects show.
            stage.Visibility = visibility;
        }

        private void OnDisable()
        {
            if (connection == null || stage == null) return;
            connection.Changed -= OnChanged;
            stage.Refreshed -= OnStageRefreshed;
        }

        private void OnDestroy()
        {
            if (root != null) Destroy(root.gameObject);
        }

        private void OnChanged(StateChanges changes) => nextRefresh = 0f;

        private void OnStageRefreshed() => nextRefresh = 0f;

        private void Update()
        {
            if (!placed || surface().HasValue != placedOnSurface) ResetPosition();
            // One foreground surface at a time: the entry panel, a workspace, Usage left and Settings open where the rail would show.
            if (glance == null) glance = GetComponent<UsageLeftGlance>();
            var foreground = (entry != null && entry.Visible) || (director != null && director.OpenWorkstream != null) || (glance != null && glance.Open)
                || (settings != null && settings.Open);
            if (root.gameObject.activeSelf == foreground) root.gameObject.SetActive(!foreground);
            if (Time.unscaledTime < nextRefresh) return;
            Refresh();
        }

        /// <summary>Shows or hides a project's work on the stage, and keeps the choice on the device.</summary>
        public void ToggleProject(string projectId)
        {
            var state = connection?.Session?.State;
            if (state == null || FocusGuard.InputSuspended) return;
            visibility.Toggle(projectId, state.Projects.Keys);
            // Work brought forward from More work leaves with its project when the person hides it.
            if (stage != null && stage.Requested is string requested && !visibility.Shows(projectId)
                && state.Workstreams.TryGetValue(requested, out var work) && work.ProjectId == projectId)
            {
                stage.Request(null);
            }
            Apply();
        }

        /// <summary>Shows a project's work, as after the headset created it.</summary>
        public void ShowProject(string projectId)
        {
            visibility.Show(projectId);
            Apply();
        }

        /// <summary>Shows every project's work, including projects that appear later.</summary>
        public void ShowAllProjects()
        {
            if (FocusGuard.InputSuspended) return;
            visibility.ShowAll();
            Apply();
        }

        /// <summary>
        /// Puts the rail in front of where the person faces now, <see cref="BelowDegrees"/> below eye
        /// level at <see cref="Distance"/>; over a desk, nearer and never into it.
        /// </summary>
        public void ResetPosition()
        {
            var head = WorkspaceVisuals.Head;
            if (head == null) return;
            var forward = new Vector3(head.forward.x, 0f, head.forward.z);
            if (forward.sqrMagnitude < 1e-4f) forward = Quaternion.Euler(0f, head.eulerAngles.y, 0f) * Vector3.forward;
            forward.Normalize();
            var desk = surface();
            placedOnSurface = desk.HasValue;
            var below = BelowDegrees * Mathf.Deg2Rad;
            var position = desk.HasValue
                ? head.position + forward * DeskAhead + Vector3.down * DeskBelow
                : head.position + forward * (Distance * Mathf.Cos(below)) + Vector3.down * (Distance * Mathf.Sin(below));
            var scale = Vector3.Distance(head.position, position);
            if (desk.HasValue) position.y = Mathf.Max(position.y, desk.Value + 0.02f + scale * (GlazeButton.HeightOf(false) + gap / 2f));
            scale = Vector3.Distance(head.position, position);
            root.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));
            root.localScale = Vector3.one * scale;
            gap = Glaze.TargetGapMeters / scale;
            placed = true;
            Refresh();
        }

        /// <summary>The stage takes the choice at once, the device keeps it, and the rail counts again.</summary>
        private void Apply()
        {
            if (stage != null) stage.Refresh();
            if (visibility.Version != savedVersion)
            {
                savedVersion = visibility.Version;
                PlayerPrefs.SetString(VisibilityPreference, visibility.Save());
                PlayerPrefs.Save();
            }
            Refresh();
        }

        private void Toggle(int slot)
        {
            if (slot < chipProjects.Count) ToggleProject(chipProjects[slot]);
        }

        /// <summary>Counts again and lays the rail out: the filters above, the actions below.</summary>
        private void Refresh()
        {
            nextRefresh = Time.unscaledTime + 0.5f;
            used.Clear();
            chipProjects.Clear();
            Overview = count();
            var overview = Overview;
            var left = -Width / 2f;
            var right = Width / 2f;
            var height = GlazeButton.HeightOf(false);
            var upper = height / 2f + gap / 2f;
            var lower = -upper;

            // Below: the actions from the left, the sheets' compact buttons from the right.
            var x = Place(connect, EntryText.ConnectProjects, overview == null ? "waiting for your Mac" : EntryText.ConnectDetail(overview), null, null, left, lower);
            Place(create, entry != null && entry.HasDraft ? EntryText.KeepCreating : EntryText.CreateProject, null, null, null, x, lower);
            var end = right;
            end = PlaceFromRight(settingsButton, SettingsText.Settings, end, lower);
            if (usageLeftLabel != null) PlaceFromRight(usageLeft, usageLeftLabel, end, lower);

            // Above: equal pills, each project that matters most, and See other tasks last while some work has no character.
            var offStage = overview != null && overview.OffStage.Count > 0;
            var shown = overview == null ? new List<ProjectSummary>() : new List<ProjectSummary>(overview.ForRail(offStage ? MostChips - 1 : MostChips));
            var slots = shown.Count + (offStage ? 1 : 0);
            if (slots > 0)
            {
                var width = Mathf.Min((Width - (slots - 1) * gap) / slots, GlazeTokens.Units(MaxPillDegrees));
                for (var index = 0; index < shown.Count; index++)
                {
                    var project = shown[index];
                    chipProjects.Add(project.ProjectId);
                    chips[index].On = project.Shown;
                    var detail = Fitting(chips[index], project.Name, width, EntryText.ChipDetailInFull(project), EntryText.ChipDetail(project));
                    Show(chips[index], project.Name, detail, project.NeedsYou > 0 ? GlazeTone.Attention : (GlazeTone?)null,
                        left + width / 2f + index * (width + gap), upper, width);
                }
                if (offStage)
                {
                    var detail = Fitting(more, EntryText.SeeOtherTasks, width, EntryText.MoreWorkDetailInFull(overview!), EntryText.MoreWorkDetail(overview!));
                    Show(more, EntryText.SeeOtherTasks, detail, overview!.NeedsYouOffStage > 0 ? GlazeTone.Attention : (GlazeTone?)null,
                        left + width / 2f + shown.Count * (width + gap), upper, width);
                }
            }
            foreach (var chip in chips)
            {
                if (!used.Contains(chip)) chip.Hide();
            }
            foreach (var button in new[] { connect, more, create, usageLeft, settingsButton })
            {
                if (!used.Contains(button)) button.Hide();
            }
        }

        /// <summary>The counts in full when the button has room for them, else their short form.</summary>
        private static string Fitting(GlazeButton button, string label, float width, string full, string brief) =>
            button.Measure(label, full) <= width + 1e-5f ? full : brief;

        private void Show(GlazeButton button, string label, string? detail, GlazeTone? tone, float center, float y, float width)
        {
            button.Show(label, new Vector2(center, y), width, detail, tone);
            used.Add(button);
        }

        /// <summary>Places a button with its left edge at <paramref name="left"/>; returns where the next one's left edge goes.</summary>
        private float Place(GlazeButton button, string label, string? detail, GlazeTone? tone, float? minWidth, float left, float y)
        {
            var width = Mathf.Max(button.Measure(label, detail), minWidth ?? 0f);
            Show(button, label, detail, tone, left + width / 2f, y, width);
            return left + width + gap;
        }

        /// <summary>Places a button with its right edge at <paramref name="right"/>; returns where the next one's right edge goes.</summary>
        private float PlaceFromRight(GlazeButton button, string label, float right, float y)
        {
            var width = button.Measure(label);
            Show(button, label, null, null, right - width / 2f, y, width);
            return right - width - gap;
        }

        private GlazeButton Button(string name, ButtonRole role, bool compact, Action? pressed)
        {
            var button = GlazeButton.Create(root, name, role, compact);
            button.Accepting = () => !FocusGuard.InputSuspended;
            if (pressed != null) button.Pressed += pressed;
            return button;
        }
    }
}

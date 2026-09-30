#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The low project rail under the stage, in reach of a seated person, in two rows. Above: the
    /// projects that matter most now, what needs the person first, each with its most important count,
    /// pressed to show or hide its work on the stage, and More work, while some work has no character,
    /// with how much of it needs the person. Below: Connect projects, with how many projects show, and
    /// Create a project. It is quiet unless something needs the person, and opens the entry panel
    /// (<see cref="EntryPanel"/>) for everything else.
    /// </summary>
    /// <remarks>
    /// Which projects show is kept on the device for each journal (<see cref="StageVisibility"/>), a
    /// presentation choice and never an authorization boundary: hiding a project hides its characters
    /// only, and its attention stays counted here and listed in More work. The lower row's right end
    /// is left free (<see cref="UsageLeftRoom"/>) for an optional Usage left glance added separately;
    /// the rail adds no floating control of its own. It keeps within about 18 degrees of where the
    /// person faced, clear of the room and pairing controls that rest 26 degrees to either side, and
    /// it steps out of the way while the entry panel or a workspace is open, since those open where
    /// it would be in view: one foreground surface at a time. Every button ignores input while the
    /// app lacks focus. It stays where it was put, in front of the person when the app starts, and
    /// again when the stage moves onto or off a desk, or when Reset position is pressed.
    /// </remarks>
    public sealed class ProjectRail : MonoBehaviour
    {
        /// <summary>The rail's width in the workspace's design units.</summary>
        public const float RailWidth = 0.55f;

        /// <summary>Kept free at the lower row's right end for a Usage left glance; nothing of the rail goes there.</summary>
        public const float UsageLeftRoom = 0.14f;

        public const float ChipHeight = 0.084f;
        public const float Gap = 0.012f;

        /// <summary>Where the rail rests: ahead of the eyes and below them, about 45 degrees down, within a seated person's reach.</summary>
        public const float Ahead = 0.43f;

        public const float Below = 0.42f;

        /// <summary>Over a desk, nearer, so it stays below the lineup's label plates in the person's view.</summary>
        public const float DeskAhead = 0.36f;

        public const float ChipText = 0.19f;
        private const int MostChips = 3;
        private const string VisibilityPreference = "halcyonic.stage.visibility";

        private readonly List<PanelButton> chips = new List<PanelButton>();
        private readonly List<string> chipProjects = new List<string>();
        private readonly HashSet<PanelButton> used = new HashSet<PanelButton>();
        private ControlPlaneConnection? connection;
        private CharacterStage? stage;
        private WorkspaceDirector? director;
        private EntryPanel? entry;
        private Transform root = null!;
        private PanelButton connect = null!;
        private PanelButton more = null!;
        private PanelButton create = null!;
        private StageVisibility visibility = new StageVisibility();
        private Func<WorkOverview?> count = () => null;
        private Func<float?> surface = () => null;
        private bool placedOnSurface;
        private int savedVersion;
        private float nextRefresh;
        private bool placed;

        /// <summary>What the rail counted last: every project and what has no character.</summary>
        public WorkOverview? Overview { get; private set; }

        /// <summary>Which projects' work stands on the stage.</summary>
        public StageVisibility Visibility => visibility;

        /// <summary>The rail's root, for the editor's renders.</summary>
        public Transform Root => root;

        /// <summary>Every button the rail shows now, for the editor's checks.</summary>
        public IEnumerable<PanelButton> Shown => used;

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
        }

        private void Build(Func<WorkOverview?> counting, Func<float?> surfaceHeight)
        {
            count = counting;
            surface = surfaceHeight;
            root = new GameObject("Project rail").transform;
            root.SetParent(transform, false);
            connect = Button("Connect projects", () => entry?.ShowConnect());
            more = Button("More work", () => entry?.ShowMoreWork());
            create = Button("Create a project", () => entry?.ShowCreate(null, null));
            for (var index = 0; index < MostChips; index++)
            {
                var slot = index;
                chips.Add(Button("Project " + index, () => Toggle(slot)));
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
            // One foreground surface at a time: the entry panel and a workspace open where the rail would show.
            var foreground = (entry != null && entry.Visible) || (director != null && director.OpenWorkstream != null);
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
        /// Puts the rail in front of where the person faces now, below the eyes; over a desk, nearer
        /// and never into it.
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
            var position = head.position + forward * (desk.HasValue ? DeskAhead : Ahead) + Vector3.down * Below;
            var scale = Vector3.Distance(head.position, position) / WorkspaceVisuals.PanelDistance;
            if (desk.HasValue) position.y = Mathf.Max(position.y, desk.Value + 0.02f + scale * (ChipHeight + Gap / 2f));
            root.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));
            root.localScale = Vector3.one * (Vector3.Distance(head.position, position) / WorkspaceVisuals.PanelDistance);
            placed = true;
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

        /// <summary>Counts again and lays the rail out: the project chips above, the actions below from the left.</summary>
        private void Refresh()
        {
            nextRefresh = Time.unscaledTime + 0.5f;
            used.Clear();
            chipProjects.Clear();
            Overview = count();
            var overview = Overview;
            var left = -RailWidth / 2f;
            var upper = ChipHeight / 2f + Gap / 2f;
            var lower = -upper;

            var x = Place(connect, EntryText.ConnectProjects, overview == null ? "waiting for your Mac" : EntryText.ConnectDetail(overview), null, left, lower);
            Place(create, entry != null && entry.HasDraft ? EntryText.ContinueCreating : EntryText.CreateProject, null, null, x, lower);

            // Above, equal slots: the projects that matter most, and More work in the last while some work has no character.
            var width = (RailWidth - (MostChips - 1) * Gap) / MostChips;
            var offStage = overview != null && overview.OffStage.Count > 0;
            if (offStage)
            {
                more.Show(EntryText.MoreWork, new Vector2(left + width / 2f + (MostChips - 1) * (width + Gap), upper), width,
                    detail: EntryText.MoreWorkDetail(overview!), detailColor: overview!.NeedsYouOffStage > 0 ? WorkspaceVisuals.AttentionColor : (Color?)null);
                used.Add(more);
            }
            var shown = overview == null ? new List<ProjectSummary>() : new List<ProjectSummary>(overview.ForRail(offStage ? MostChips - 1 : MostChips));
            for (var index = 0; index < shown.Count; index++)
            {
                var project = shown[index];
                chipProjects.Add(project.ProjectId);
                chips[index].Show(project.Name, new Vector2(left + width / 2f + index * (width + Gap), upper), width, detail: EntryText.ChipDetail(project),
                    detailColor: project.NeedsYou > 0 ? WorkspaceVisuals.AttentionColor : (Color?)null);
                used.Add(chips[index]);
            }
            foreach (var chip in chips)
            {
                if (!used.Contains(chip)) chip.Hide();
            }
            foreach (var button in new[] { connect, more, create })
            {
                if (!used.Contains(button)) button.Hide();
            }
        }

        /// <summary>Places a button with its left edge at <paramref name="left"/>; returns where the next one's left edge goes.</summary>
        private float Place(PanelButton button, string label, string? detail, Color? detailColor, float left, float y)
        {
            var width = Mathf.Max(button.Measure(label, 0.12f), detail == null ? 0f : button.Measure(detail, 0.12f) * 0.8f);
            button.Show(label, new Vector2(left + width / 2f, y), width, detail: detail, detailColor: detailColor);
            used.Add(button);
            return left + width + Gap;
        }

        private PanelButton Button(string name, Action pressed)
        {
            var button = PanelButton.Create(root, name, ChipHeight, ChipText);
            button.Accepting = () => !FocusGuard.InputSuspended;
            button.Pressed += pressed;
            return button;
        }
    }
}

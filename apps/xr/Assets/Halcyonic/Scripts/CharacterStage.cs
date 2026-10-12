#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>Optional workstream characters on a shelf the person explicitly placed.
    /// No characters or targets are created before agents are enabled and a confirmed pose exists.
    /// Losing that pose tucks the shelf away; it never creates an eye-relative fallback.</summary>
    [RequireComponent(typeof(ControlPlaneConnection))]
    public sealed class CharacterStage : MonoBehaviour
    {
        /// <summary>The characters' distance from the eyes, by default: beyond system windows, which render within about 2 m.</summary>
        public const float DefaultDistance = 2.4f;

        /// <summary>
        /// The characters' height from the eyes, by default (ADR 0023): their centers about 4 degrees
        /// below eye level at <see cref="DefaultDistance"/>, so their labels end about 14 degrees down
        /// and the banner and any panel open under them, in the comfortable band. To be judged on the
        /// headset; renders stand their characters here too.
        /// </summary>
        public const float DefaultHeightFromEyes = -0.17f;

        /// <summary>How long a notice from the room or the Mac shows on the banner.</summary>
        public const float NoticeSeconds = 8f;

        /// <summary>The angle between the lowest a label reaches and the banner under it: more than the degree kept between things.</summary>
        public const float BannerGapDegrees = 1.25f;

        private const float spanDegrees = 60f; // Internal slot interpolation, not an eye-relative arc.

        [Tooltip("How many characters the stage shows at most.")]
        [SerializeField] private int maxCharacters = 6;

        /// <summary>
        /// How far to the person's right the lineup's middle turns when they make room for a window
        /// (<see cref="StageArrangement.TurnedAside"/>): the arc then runs from about straight ahead to
        /// 62 degrees right, so a window in front of them covers fewer characters. Halcyonic cannot see
        /// the window, so this reduces overlap; it guarantees nothing.
        /// </summary>
        public const float AsideDegrees = 32f;

        /// <summary>
        /// Where a window most often opens, which the characters beside it keep clear: this many
        /// degrees to either side of where the person faced, and above and below eye level.
        /// </summary>
        public const float WindowLaneHalfWidthDegrees = 24f;

        public const float WindowLaneHalfHeightDegrees = 14f;

        /// <summary>
        /// How many characters stand beside a window, and where, by slot from the person's left: the
        /// lineup's middle slots, which it fills first, above eye level either side, and the outer two
        /// under them. Their badges carry words, about 11 degrees wide, so two side by side would
        /// reach past 45 degrees; one above the other keeps the outermost within about 38, a degree
        /// clear of the lane, and leaves a lower character room to rise without reaching the badge
        /// above it.
        /// </summary>
        public const int WindowCapacity = 4;

        public static readonly float[] WindowSlotDegrees = { -32f, -32f, 32f, 32f };

        /// <summary>Each slot's height beside a window, in degrees from eye level: up for the inner slots.</summary>
        public static readonly float[] WindowSlotLiftDegrees = { -15f, 4f, 4f, -15f };

        /// <summary>The gap between a label plate and the surface it rests on, in a character's units.</summary>
        private const float SurfaceClearance = 0.005f;

        private readonly Dictionary<string, CharacterView> views = new Dictionary<string, CharacterView>();
        private readonly List<WorkstreamView> eligible = new List<WorkstreamView>();
        private readonly Dictionary<string, Standing> standings = new Dictionary<string, Standing>();
        private readonly List<string> departed = new List<string>();
        private ControlPlaneConnection connection = null!;
        private CharacterLineup lineup = null!;
        private Transform arc = null!;
        private Transform bannerRoot = null!;
        private StageBanner banner = null!;
        private Transform? head;
        private IStagePlacementSource? source;
        private float nextSourceSearch;
        private bool preferredChanged;
        private bool placedOnce;
        private bool onSurface;
        private Vector3 shelfViewer;
        private WorkspacePresentationState presentation = new WorkspacePresentationState();
        private Transform preview = null!;
        /// <summary>Conservative occupied shelf, label and banner envelope shared by placement and actual scene checks.</summary>
        private static CharacterView? shelfMeasure;
        public static void WriteShelfCorners(Pose pose, float width, Vector3 viewer, Vector3[] corners)
        {
            if (corners.Length != 8) throw new System.ArgumentException("Shelf envelope has eight corners.", nameof(corners));
            // A real label model measures font metrics, whole source marks, title wrapping and eye-facing tilt.
            // It is created only by the explicit placement/geometry consumer and never has a target.
            if (shelfMeasure == null)
            {
                var model = new GameObject("Shelf label measurement") { hideFlags = HideFlags.HideAndDontSave };
                shelfMeasure = CharacterView.Create(model.transform, "shelf-label-measurement");
                shelfMeasure.enabled = false;
            }
            var minimum = new Vector3(-width / 2f, 0f, -AgentShelfDraft.Depth / 2f);
            var maximum = new Vector3(width / 2f, 0.5f, AgentShelfDraft.Depth / 2f);
            var span = Mathf.Max(0f, width - 0.1f - AgentShelfDraft.Spacing * AgentShelfDraft.Scale);
            foreach (var x in new[] { -span / 2f, span / 2f })
            foreach (var marks in new[] { (Synthetic: false, Recorded: true), (Synthetic: true, Recorded: false), (Synthetic: true, Recorded: true) })
            {
                shelfMeasure.transform.SetPositionAndRotation(pose.position + pose.rotation * Vector3.right * x,
                    pose.rotation * Quaternion.Euler(0f, 180f, 0f));
                shelfMeasure.transform.localScale = Vector3.one * AgentShelfDraft.Scale;
                shelfMeasure.Show(new CharacterPresentation("shelf-label-measurement",
                    "A complete task title with two full lines of readable words", CharacterActivity.Verifying,
                    "Checking its work", Halcyonic.Contracts.AttentionLevel.None, System.Array.Empty<string>(), 0,
                    marks.Synthetic, marks.Recorded, false));
                shelfMeasure.PlaceShelfLabel(viewer);
                foreach (var renderer in shelfMeasure.Label.GetComponentsInChildren<Renderer>(false))
                {
                    var bounds = renderer.bounds;
                    for (var index = 0; index < 8; index++)
                    {
                        var point = bounds.center + Vector3.Scale(bounds.extents,
                            new Vector3(index % 2 == 0 ? -1f : 1f, index < 4 ? -1f : 1f, index % 4 < 2 ? -1f : 1f));
                        var local = Quaternion.Inverse(pose.rotation) * (point - pose.position);
                        minimum = Vector3.Min(minimum, local); maximum = Vector3.Max(maximum, local);
                    }
                }
            }
            foreach (var renderer in shelfMeasure.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            for (var index = 0; index < 8; index++)
                corners[index] = pose.position + pose.rotation * new Vector3(index % 2 == 0 ? minimum.x : maximum.x,
                    index < 4 ? minimum.y : maximum.y, index % 4 < 2 ? minimum.z : maximum.z);
        }
        public float ShelfWidth => AgentShelfDraft.Width(maxCharacters);
        public bool Visible => presentation.AgentsVisible && onSurface && arc != null && arc.gameObject.activeInHierarchy;
        public void BindPresentation(WorkspacePresentationState state)
        {
            presentation.Changed -= PresentationChanged;
            presentation = state;
            presentation.Changed += PresentationChanged;
            PresentationChanged();
        }
        private void PresentationChanged()
        {
            if (!presentation.AgentsVisible && arc != null) arc.gameObject.SetActive(false);
            preferredChanged = true;
            Refresh();
        }
        public void PreviewShelf(Pose? pose)
        {
            if (preview == null) return;
            preview.gameObject.SetActive(pose.HasValue && !presentation.Minimized);
            if (pose is Pose candidate) preview.SetPositionAndRotation(candidate.position, candidate.rotation);
        }

        /// <summary>The arc's radius now: the distance setting in front of the person, or the reach to a surface.</summary>
        private float radius;
        private string? shownBanner;
        private BannerKind shownKind;
        private string? shownWaiting;
        private string? shownNotice;
        private string? shownNotShown;
        private LineWords? shownStillOpen;
        private bool shownRaised;

        /// <summary>The banner says its line alone, with no notice under it: raised over the open menu, which says what waits.</summary>
        private bool shownAlone;
        private float? shownPanelTop;
        private int shownScale = -1;

        /// <summary>Where the banner stands now: in its place, stepped aside, or above the stage in the demonstration.</summary>
        private BannerStand bannerStand = BannerStand.InPlace;
        private string? notice;
        private float noticeUntil;
        private StageVisibility visibility = new StageVisibility();

        /// <summary>
        /// Raised with the workstream id when the stage creates a character, so other components can
        /// add to it at runtime; the workspace attaches its ray and poke targets this way.
        /// </summary>
        public event System.Action<string, CharacterView>? CharacterCreated;

        /// <summary>The character showing a workstream, if it is on the stage.</summary>
        public bool TryGetCharacter(string workstreamId, [MaybeNullWhen(false)] out CharacterView view) =>
            views.TryGetValue(workstreamId, out view);

        /// <summary>The height of the surface the characters stand on, in world space, or null while they stand in front of the person.</summary>
        public float? SurfaceHeight => onSurface ? arc.position.y : (float?)null;

        /// <summary>The slot a workstream's character stands in, numbered from the person's left, or -1 without one.</summary>
        public int SlotOf(string workstreamId) => lineup.SlotOf(workstreamId);

        /// <summary>
        /// Which projects' work has characters; the rest waits in the project rail's More work. Every
        /// project unless the person chose, in the rail. A presentation choice, never a boundary.
        /// </summary>
        public StageVisibility Visibility
        {
            get => visibility;
            set
            {
                visibility = value ?? new StageVisibility();
                Refresh();
            }
        }

        /// <summary>The workstream the person asked to see, which has a character whatever its rank or project, or null.</summary>
        public string? Requested => lineup.Requested;

        /// <summary>
        /// Gives a workstream a character whatever its rank or project, as More work asks, until
        /// another is asked for or null withdraws it.
        /// </summary>
        public void Request(string? workstreamId)
        {
            if (lineup.Requested == workstreamId) return;
            lineup.Request(workstreamId);
            Refresh();
        }

        /// <summary>
        /// Keeps a workstream's character on the stage for <see cref="CharacterLineup.KeepFor"/>, as
        /// when the person opened it, whatever older work needs attention.
        /// </summary>
        public void Keep(string workstreamId)
        {
            lineup.Keep(workstreamId, System.DateTimeOffset.UtcNow);
            Refresh();
        }

        /// <summary>Retained gallery property; runtime shelf placement never guesses a window lane.</summary>
        public bool BesideAWindow => false;

        /// <summary>Raised after the characters were brought up to date, so the rail can count what has none.</summary>
        public event System.Action? Refreshed;

        private void Awake()
        {
            connection = GetComponent<ControlPlaneConnection>();
            // No visible shelf exists until an explicit confirmed placement is supplied.
            lineup = new CharacterLineup(BesideAWindow ? WindowCapacity : Mathf.Max(1, maxCharacters));
            radius = AgentShelfDraft.Scale;
            CharacterMaterials.Prepare();

            // Hidden until agents are enabled and a confirmed shelf pose exists.
            arc = new GameObject("Agent shelf").transform;
            arc.SetParent(transform, false);
            arc.gameObject.SetActive(false);

            // The banner reads along its parent's forward axis, away from the person, as the arc's does.
            bannerRoot = new GameObject("Banner").transform;
            bannerRoot.SetParent(arc, false);
            banner = StageBanner.Create(bannerRoot);
            MakeShelf(arc, "Agent shelf", new Color(0.12f, 0.19f, 0.22f, 1f));
            preview = new GameObject("Shelf preview").transform;
            preview.SetParent(transform, false);
            MakeShelf(preview, "Shelf preview surface", new Color(0.16f, 0.4f, 0.48f, 0.7f));
            preview.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            presentation.Changed -= PresentationChanged;
            presentation.Changed += PresentationChanged;
            connection.Changed += OnChanged;
            FocusGuard.FoldChanged += Refresh;
            AmbientCover.Changed += ShowBannerUncovered;
            ShowBannerUncovered();
        }

        private void OnDisable()
        {
            presentation.Changed -= PresentationChanged;
            connection.Changed -= OnChanged;
            FocusGuard.FoldChanged -= Refresh;
            AmbientCover.Changed -= ShowBannerUncovered;
            if (source != null) source.Changed -= OnPreferredChanged;
            source = null;
            nextSourceSearch = 0f;
        }

        private void Start() => Refresh();

        private void LateUpdate()
        {
            var camera = Camera.main;
            var current = camera != null ? camera.transform : null;
            if (current != head)
            {
                head = current;
                foreach (var view in views.Values) view.Person = head;
            }
            FindPlacementSource();
            if (notice != null && Time.unscaledTime >= noticeUntil)
            {
                notice = null;
                ShowBanner(shownBanner ?? "", shownKind, shownWaiting, shownNotShown, shownStillOpen, shownRaised, shownAlone);
            }
            var preferred = source?.Preferred;
            if (!presentation.AgentsVisible || !preferred.HasValue)
            {
                arc.gameObject.SetActive(false);
                onSurface = false;
                placedOnce = false;
                return;
            }
            if (!placedOnce || preferredChanged)
            {
                Place(preferred, "the person confirmed the shelf position");
                Refresh();
            }
            Glide();
            // Raised, the banner follows the panel in its place as it is laid anew, as a page that grows.
            if (shownRaised && AmbientCover.RaisedOver != shownPanelTop) PlaceBanner();
        }

        /// <summary>
        /// Looks for a placement source on this object, once a second until one appears, because the
        /// component that provides it can be added after the stage starts.
        /// </summary>
        private void FindPlacementSource()
        {
            if (source is Object component && component == null)
            {
                source = null;
                preferredChanged = true;
            }
            if (source != null || Time.unscaledTime < nextSourceSearch) return;
            nextSourceSearch = Time.unscaledTime + 1f;
            source = GetComponent<IStagePlacementSource>();
            if (source == null) return;
            source.Changed += OnPreferredChanged;
            preferredChanged = true;
        }

        private void OnPreferredChanged() { preferredChanged = true; Refresh(); }

        private void OnChanged(StateChanges changes) => Refresh();

        /// <summary>Brings the characters up to date with the session, the visibility and the request.</summary>
        public void Refresh()
        {
            if (!presentation.AgentsVisible || source?.Preferred == null)
            {
                if (arc != null) arc.gameObject.SetActive(false);
                Refreshed?.Invoke();
                return;
            }
            var session = connection.Session;
            if (session == null)
            {
                ShowLine(null);
                Refreshed?.Invoke();
                return;
            }

            var live = session.Status.IsLive;
            visibility.UseJournal(session.State.Journal?.JournalId);
            eligible.Clear();
            foreach (var workstream in session.State.Workstreams.Values)
            {
                if (visibility.Shows(workstream.ProjectId) || workstream.WorkstreamId == lineup.Requested) eligible.Add(workstream);
            }
            // New and just opened work keeps its slot a while, by this device's clock.
            lineup.UseJournal(session.State.Journal?.JournalId);
            lineup.Update(eligible, System.DateTimeOffset.UtcNow);
            departed.Clear();
            foreach (var id in views.Keys)
            {
                if (lineup.SlotOf(id) < 0) departed.Add(id);
            }
            foreach (var id in departed)
            {
                Destroy(views[id].gameObject);
                views.Remove(id);
                standings.Remove(id);
            }
            var slots = lineup.Slots;
            for (var slot = 0; slot < slots.Count; slot++)
            {
                var id = slots[slot];
                if (id == null) continue;
                if (!views.TryGetValue(id, out var view))
                {
                    view = CharacterView.Create(arc, id);
                    view.Person = head;
                    view.Grounded = true;
                    views[id] = view;
                    CharacterCreated?.Invoke(id, view);
                }
                view.Show(CharacterPresenter.Present(session.State.Workstreams[id], session.State, live));
                view.BadgeOnly = BesideAWindow;
                MoveToSlot(id, view, slot);
            }
            ShowLine(session);
            Refreshed?.Invoke();
        }

        /// <summary>
        /// The banner's line: whether what the stage shows is live, or a demonstration in its own words,
        /// so a recording is never read as live work. While another window keeps focus it also counts
        /// what waits for the person, so it stays findable when the window covers the characters; beside
        /// a window, how many more tasks have no character; and the panel still open, kept as it was.
        /// </summary>
        private void ShowLine(RealtimeSession? session)
        {
            var demonstration = connection.DemonstrationLine;
            var raised = StandBanner() && demonstration != null;
            // Raised above the stage while the menu stands in its place, it says only that the demonstration plays;
            // over the closed bar alone it keeps a notice and what the folded stage says, as pairing's prompts.
            if (raised && AmbientCover.PanelShowing)
            {
                ShowBanner(demonstration!, BannerKind.Practice, null, null, null, raised: true, alone: true);
                return;
            }
            var folded = FocusGuard.Folded && session != null;
            var waiting = folded ? AmbientText.NeedsYouLine(AmbientText.NeedsYou(session!.State)) : null;
            var notShown = folded && BesideAWindow ? AmbientText.NotShown(session!.State.Workstreams.Count - views.Count) : null;
            var stillOpen = folded && AmbientCover.OpenPanel is string panel ? AmbientText.StillOpen(panel) : null;
            ShowBanner(
                demonstration ?? (session == null ? connection.SetupProblem ?? NotConnected : Describe(session)),
                demonstration != null ? BannerKind.Practice : session != null && session.Status.IsLive ? BannerKind.Live : BannerKind.NotLive,
                waiting, notShown, stillOpen, raised, alone: false);
        }

        /// <summary>
        /// The lineup's slots for how the characters stand now: four beside a window, else six. What it
        /// knows and keeps carries over (<see cref="CharacterLineup.WithCapacity"/>); the characters
        /// take their new slots at the next refresh.
        /// </summary>
        private void Fit()
        {
            var capacity = BesideAWindow ? WindowCapacity : Mathf.Max(1, maxCharacters);
            if (capacity != lineup.Capacity) lineup = lineup.WithCapacity(capacity);
            Refresh();
        }

        /// <summary>Places the stage on the preferred surface, or in front of the person without one.</summary>
        private void Place(Pose? preferred, string reason)
        {
            preferredChanged = false;
            placedOnce = true;
            var wasBeside = BesideAWindow;
            if (!preferred.HasValue || !presentation.AgentsVisible) { arc.gameObject.SetActive(false); return; }
            StandOnSurface(preferred.Value);
            // Onto a surface or off it, the characters beside a window may become six again, or four.
            if (BesideAWindow != wasBeside || (BesideAWindow ? WindowCapacity : Mathf.Max(1, maxCharacters)) != lineup.Capacity) Fit();
            foreach (var view in views.Values) view.BadgeOnly = BesideAWindow;
            CharacterMaterials.SetKeyLight(arc.rotation);
            arc.gameObject.SetActive(true);
            // Every character to its slot, with the current settings; a glide in progress ends.
            foreach (var pair in views)
            {
                var standing = standings[pair.Key];
                var slot = lineup.SlotOf(pair.Key);
                if (slot >= 0)
                {
                    standing.Angle = standing.From = standing.To = SlotAngle(slot);
                    standing.Lift = standing.LiftFrom = standing.LiftTo = SlotLift(slot);
                }
                Stand(pair.Value, standing.Angle, standing.Lift, 1f);
            }
            PlaceBanner();
            Debug.Log("Halcyonic: placed the stage " + (onSurface ? "on its surface" : "in front of the person") + " because " + reason + ".");
        }

        /// <summary>Uses the confirmed shelf pose unchanged, independent of head motion and focus.</summary>
        private void StandOnSurface(Pose pose)
        {
            onSurface = true;
            radius = AgentShelfDraft.Scale;
            shelfViewer = head != null ? head.position : Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            arc.SetPositionAndRotation(pose.position, pose.rotation);
        }

        private Surface MakeShelf(Transform parent, string name, Color color)
        {
            var surface = Surface.Create(parent, name, 0);
            surface.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            surface.Draw(new Vector2(ShelfWidth, AgentShelfDraft.Depth), 0.025f,
                color, new Color(0.45f, 0.65f, 0.7f, 1f), 0.002f);
            return surface;
        }

        /// <summary>
        /// Slots are fixed points on the arc, numbered from the person's left. A new character appears
        /// in its slot; one the lineup moves, only ever to bring attention to the middle, glides there
        /// along the arc, swinging out behind the others rather than passing through them.
        /// </summary>
        private void MoveToSlot(string id, CharacterView view, int slot)
        {
            var angle = SlotAngle(slot);
            var lift = SlotLift(slot);
            if (!standings.TryGetValue(id, out var standing))
            {
                standing = new Standing { Angle = angle, From = angle, To = angle, Lift = lift, LiftFrom = lift, LiftTo = lift };
                standings[id] = standing;
            }
            else if (!Mathf.Approximately(standing.To, angle) || !Mathf.Approximately(standing.LiftTo, lift))
            {
                standing.From = standing.Angle;
                standing.To = angle;
                standing.LiftFrom = standing.Lift;
                standing.LiftTo = lift;
                standing.Started = Time.time;
                standing.Duration = 0.5f + (Mathf.Abs(angle - standing.Angle) + Mathf.Abs(lift - standing.Lift)) / 60f;
                return;
            }
            // Standing again also follows a label plate that grew or shrank, on a surface.
            if (standing.Settled) Stand(view, standing.Angle, standing.Lift, 1f);
        }

        private void Glide()
        {
            foreach (var pair in views)
            {
                var standing = standings[pair.Key];
                if (standing.Settled) continue;
                var progress = Mathf.Clamp01((Time.time - standing.Started) / standing.Duration);
                var eased = Mathf.SmoothStep(0f, 1f, progress);
                standing.Angle = progress >= 1f ? standing.To : Mathf.Lerp(standing.From, standing.To, eased);
                standing.Lift = progress >= 1f ? standing.LiftTo : Mathf.Lerp(standing.LiftFrom, standing.LiftTo, eased);
                Stand(pair.Value, standing.Angle, standing.Lift, 1f + 0.15f * Mathf.Sin(Mathf.PI * progress));
            }
        }

        /// <summary>
        /// How high a slot stands in front of the person, in degrees from eye level: the stage's height
        /// on the arc, or beside a window, the slot's own.
        /// </summary>
        private float SlotLift(int slot) => BesideAWindow
            ? WindowSlotLiftDegrees[Mathf.Clamp(slot, 0, WindowSlotLiftDegrees.Length - 1)]
            : Mathf.Atan2(DefaultHeightFromEyes, radius) * Mathf.Rad2Deg;

        /// <summary>
        /// A slot's angle around the arc from where the person faced, spread as <see cref="Spread"/>
        /// says; beside a window, either side of its lane.
        /// </summary>
        private float SlotAngle(int slot)
        {
            if (BesideAWindow) return WindowSlotDegrees[Mathf.Clamp(slot, 0, WindowSlotDegrees.Length - 1)];
            var count = lineup.Capacity;
            var eyesAbove = onSurface && head != null ? head.position.y - arc.position.y : 0f;
            var span = spanDegrees;
            return count > 1 ? -span / 2f + span * slot / (count - 1) : 0f;
        }

        /// <summary>
        /// How much wider the arc's angles are than the angles the person sees, for characters
        /// <paramref name="radius"/> away and <paramref name="below"/> from the eyes, negative under
        /// them: seen from above, as on a desk, a turn around the arc looks smaller, so the arc spreads
        /// to keep neighbours and their labels as far apart as in front of the person.
        /// </summary>
        public static float Spread(float radius, float below) => 1f / Mathf.Max(Mathf.Cos(Mathf.Atan2(below, radius)), 0.5f);

        /// <summary>
        /// Stands a character on the arc at an angle from where the person faced, facing them, as
        /// <see cref="Stance"/> places it: <paramref name="lift"/> degrees from eye level in front of the
        /// person, its label resting on the surface otherwise.
        /// </summary>
        private void Stand(CharacterView view, float angle, float lift, float reach)
        {
            // The row uses the lineup's continuous slot coordinate during a glide.
            var slot = lineup.Capacity > 1 ? (angle / spanDegrees + 0.5f) * (lineup.Capacity - 1) : 0f;
            view.transform.localPosition = new Vector3((slot - (lineup.Capacity - 1) * 0.5f) * AgentShelfDraft.Spacing * radius, 0f, 0f);
            view.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            view.transform.localScale = Vector3.one * radius;
            view.PlaceShelfLabel(shelfViewer);
        }

        /// <summary>
        /// How high the stage stands a character <paramref name="radius"/> away, from the arc's origin,
        /// and how large: at <paramref name="heightFromEyes"/> in front of the person, or, with no
        /// height, its label resting on the surface at the arc's origin, with the eyes
        /// <paramref name="eyesAbove"/> over it. It scales with its distance from the eyes, so it keeps
        /// its apparent size near or far, above or below, and its label hangs as low as being seen
        /// from above needs and, on a surface, leans back to face the eyes (<see cref="CharacterView.ViewedFrom"/>).
        /// </summary>
        public static (float Height, float Scale) Stance(CharacterView view, float radius, float eyesAbove, float? heightFromEyes)
        {
            var height = heightFromEyes ?? 0f;
            var scale = radius;
            // Standing on a surface depends on the label, which depends on the view: a few passes settle it.
            for (var pass = 0; pass < 3; pass++)
            {
                var below = height - eyesAbove;
                // On a surface, below the eyes, the label leans back to face them.
                view.ViewedFrom(Mathf.Atan2(below, radius) * Mathf.Rad2Deg, faceEyes: heightFromEyes == null);
                scale = Mathf.Sqrt(radius * radius + below * below);
                height = heightFromEyes ?? (SurfaceClearance - view.Footing) * scale;
            }
            return (height, scale);
        }

        /// <summary>
        /// Shows a short notice on the banner for <see cref="NoticeSeconds"/>: news from the room or the
        /// Mac, which no longer comes up in front of the person (ADR 0023); the controls that act on
        /// it are in Settings. The latest notice replaces the one before.
        /// </summary>
        public void ShowNotice(string text)
        {
            notice = text;
            noticeUntil = Time.unscaledTime + NoticeSeconds;
            ShowBanner(shownBanner ?? "", shownKind, shownWaiting, shownNotShown, shownStillOpen, shownRaised, shownAlone);
        }

        /// <param name="waiting">What needs the person, said on a line of its own and in the attention color, or null.</param>
        /// <param name="notShown">Beside a window, how many more tasks have no character, or null.</param>
        /// <param name="stillOpen">The panel kept while another window has focus, or null.</param>
        /// <param name="raised">Above the stage, as the demonstration's banner always stands.</param>
        /// <param name="alone">Saying <paramref name="text"/> alone, with no notice under it, as over the open menu.</param>
        private void ShowBanner(string text, BannerKind kind, string? waiting, string? notShown, LineWords? stillOpen, bool raised, bool alone)
        {
            if (text == shownBanner && kind == shownKind && waiting == shownWaiting && notice == shownNotice
                && notShown == shownNotShown && stillOpen?.ToString() == shownStillOpen?.ToString() && raised == shownRaised && alone == shownAlone && shownScale == GlazeText.Version) return;
            // The text's size changes the banner and how deep the labels above it reach.
            shownScale = GlazeText.Version;
            shownBanner = text;
            shownKind = kind;
            shownWaiting = waiting;
            shownNotice = notice;
            shownNotShown = notShown;
            shownStillOpen = stillOpen;
            shownRaised = raised;
            shownAlone = alone;
            // A connection's detail, a setup problem or a notice can carry a server's or an exception's
            // words, and the panel kept a task's title; the banner shows them by the one rule for text
            // Halcyonic did not write.
            banner.Show(text, kind, waiting, alone ? null : notice, notShown, stillOpen);
            PlaceBanner();
        }

        /// <summary>
        /// In front of the person, the banner hangs under the lowest a label reaches, where the ambient
        /// strip goes, or beside a window, under the window's lane; over a surface, it stands above the
        /// highest a character reaches, risen included, since the lineup puts what waits for the person
        /// in its middle. Raised in the demonstration while the menu stands in its place, it stands above
        /// the highest a character reaches in front of the person too, and beside a window above its lane.
        /// </summary>
        private void PlaceBanner()
        {
            var eyesAbove = onSurface && head != null ? head.position.y - arc.position.y : 0f;
            var top = onSurface ? BannerBottomOnSurface(radius, eyesAbove)
                : shownRaised ? BesideAWindow ? BannerBottomAboveWindow(radius) : BannerBottomAbove(radius, DefaultHeightFromEyes)
                : BesideAWindow ? BannerTopBesideWindow(radius) : BannerTop(radius, DefaultHeightFromEyes);
            // Raised, it also clears the panel in its place, as the menu over a desk's lineup stands where the banner would,
            // or the closed bar alone, which a risen character there lifts as high.
            shownPanelTop = shownRaised ? AmbientCover.RaisedOver : null;
            if (shownRaised) top = RaisedBannerBottom(radius, top - eyesAbove, shownPanelTop) + eyesAbove;
            if (onSurface)
            {
                bannerRoot.localPosition = new Vector3(0f, 0.22f, 0.06f);
                bannerRoot.localScale = Vector3.one * radius;
                banner.transform.localPosition = Vector3.zero;
                return;
            }
            bannerRoot.localPosition = new Vector3(0f, top, radius);
            bannerRoot.localScale = Vector3.one * radius;
            // The banner hangs from its top edge; on a surface or raised it stands on its bottom edge instead.
            banner.transform.localPosition = new Vector3(0f, onSurface || shownRaised ? banner.Height : 0f, 0f);
        }

        /// <summary>
        /// The height of the banner's bottom edge from the eyes when it is raised in front of the person,
        /// in meters, with the characters <paramref name="radius"/> away and their centers
        /// <paramref name="heightFromEyes"/> from the eyes: a little more than a degree over the highest a
        /// character reaches, risen and moving, all along it. The banner is flat, so its ends are farther
        /// than its middle and look lower; it starts higher by as much as its widest ends would sink.
        /// </summary>
        public static float BannerBottomAbove(float radius, float heightFromEyes) =>
            (heightFromEyes + CharacterView.HighestReach * Mathf.Sqrt(radius * radius + heightFromEyes * heightFromEyes)
                + GlazeTokens.Units(BannerGapDegrees) * radius)
            / Mathf.Cos(StageBanner.WidestDegrees / 2f * Mathf.Deg2Rad);

        /// <summary>
        /// The height of the raised banner's bottom edge from the eyes, in meters, <paramref name="radius"/>
        /// ahead: <paramref name="overCharacters"/>, where it clears the characters, or higher, a little more
        /// than a degree over a panel whose top edge stands <paramref name="panelTop"/> degrees from eye
        /// level, all along it.
        /// </summary>
        public static float RaisedBannerBottom(float radius, float overCharacters, float? panelTop)
        {
            if (panelTop == null) return overCharacters;
            var over = Mathf.Tan((panelTop.Value + BannerGapDegrees) * Mathf.Deg2Rad) * radius;
            // Over eye level its flat ends look lower than its middle, as BannerBottomAbove allows for.
            if (over > 0f) over /= Mathf.Cos(StageBanner.WidestDegrees / 2f * Mathf.Deg2Rad);
            return Mathf.Max(overCharacters, over);
        }

        /// <summary>
        /// The height of the banner's bottom edge from the eyes when it is raised beside a window, in
        /// meters, with it <paramref name="radius"/> away: a little more than a degree over the window's
        /// lane, all along it, as it hangs as far under the lane otherwise.
        /// </summary>
        public static float BannerBottomAboveWindow(float radius) => -BannerTopBesideWindow(radius);

        /// <summary>
        /// The height of the banner's bottom edge over a surface, in meters, with the characters
        /// <paramref name="radius"/> away and the eyes <paramref name="eyesAbove"/> over the surface:
        /// a little more than a degree over the highest a character reaches, its label resting on the
        /// surface and its body risen.
        /// </summary>
        public static float BannerBottomOnSurface(float radius, float eyesAbove) =>
            (SurfaceClearance - CharacterLabelView.DeepestBottom + CharacterView.HighestReach + GlazeTokens.Units(BannerGapDegrees))
            * Mathf.Sqrt(radius * radius + eyesAbove * eyesAbove);

        /// <summary>
        /// The banner shows unless a panel or the peek is where it goes (<see cref="AmbientCover"/>); in the
        /// demonstration it always stands raised above the stage instead (<see cref="BannerPlace"/>), saying
        /// its line alone while a panel shows and keeping its notice over the closed bar, so it says its line
        /// again as either changes.
        /// </summary>
        private void ShowBannerUncovered()
        {
            var wasRaised = bannerStand == BannerStand.AboveTheStage;
            // Raised in the demonstration, it says its lines alone over the open menu and keeps its notice over the bar.
            if (StandBanner() != wasRaised || (shownRaised && AmbientCover.PanelShowing != shownAlone)) ShowLine(connection.Session);
        }

        /// <summary>Where the banner stands now, showing it or not; true when it is raised above the stage.</summary>
        private bool StandBanner()
        {
            bannerStand = BannerPlace.Of(connection.DemonstrationLine != null, AmbientCover.PanelShowing, AmbientCover.PeekShowing);
            banner.gameObject.SetActive(bannerStand != BannerStand.Hidden);
            return bannerStand == BannerStand.AboveTheStage;
        }

        private const string NotConnected = "Not connected to " + HostText.Your;

        /// <summary>
        /// The height of the banner's top edge from the eyes, in meters, with the characters
        /// <paramref name="radius"/> away and their centers <paramref name="heightFromEyes"/> from the
        /// eyes: a little more than a degree under the lowest any label reaches, all along it. The
        /// banner is flat, so its ends are farther than its middle and look higher; it starts lower by
        /// as much as its widest ends would rise.
        /// </summary>
        public static float BannerTop(float radius, float heightFromEyes) =>
            (heightFromEyes + (CharacterLabelView.DeepestBottom - GlazeTokens.Units(BannerGapDegrees)) * radius)
            / Mathf.Cos(StageBanner.WidestDegrees / 2f * Mathf.Deg2Rad);

        /// <summary>
        /// The height of the banner's top edge from the eyes beside a window, in meters, with it
        /// <paramref name="radius"/> away: a little more than a degree under the window's lane, all
        /// along it, its ends being farther than its middle.
        /// </summary>
        public static float BannerTopBesideWindow(float radius) =>
            -Mathf.Tan((WindowLaneHalfHeightDegrees + BannerGapDegrees) * Mathf.Deg2Rad) * radius / Mathf.Cos(StageBanner.WidestDegrees / 2f * Mathf.Deg2Rad);

        private static string Describe(RealtimeSession session)
        {
            var status = session.Status;
            var origin = session.State.Journal?.Origin == JournalOrigin.Fixture ? " · Recorded" : "";
            switch (status.Phase)
            {
                case ConnectionPhase.Live:
                    return "Connected to " + HostText.Your + origin;
                case ConnectionPhase.WaitingToRetry:
                    return ConnectionText.Retrying(status);
                case ConnectionPhase.Refused:
                    return ConnectionText.WhyNotLive(status);
                default:
                    return ConnectionText.Phase(status) + origin;
            }
        }

        /// <summary>Where a character stands on the arc, and how high in front of the person, and where it is gliding to.</summary>
        private sealed class Standing
        {
            public float Angle;
            public float From;
            public float To;

            /// <summary>Degrees from eye level, in front of the person.</summary>
            public float Lift;
            public float LiftFrom;
            public float LiftTo;
            public float Started;
            public float Duration;

            public bool Settled => Angle == To && Lift == LiftTo;
        }
    }
}

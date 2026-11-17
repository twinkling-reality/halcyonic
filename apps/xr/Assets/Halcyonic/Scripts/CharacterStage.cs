#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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

        /// <summary>The angle between the lowest a label reaches and the banner under it: more than the degree kept between things.</summary>
        public const float BannerGapDegrees = 1.25f;

        private const float spanDegrees = 60f; // Internal slot interpolation, not an eye-relative arc.

        /// <summary>
        /// How many characters stand at once on the shelf: one, the shelf paging through the rest (the lead, 2026-11-16T17:37Z). The shelf's
        /// places stand at a fixed pitch, about 9.7 degrees, so two stand as close as six, and their drawn tags meet once names run past a few
        /// words: FIELD's F234 measured two places on 3c5b69b6a, the film's sentences -3.8 / -4.3 degrees apart and even "Kite Run" -2.3 / -2.7
        /// when both need the person (Standard / Larger; the floor is 0.5). Raising it needs a page that
        /// takes its pitch from its widest drawn tag, and StageRender's shelf-page check passing at the new count, which it reads from here.
        /// </summary>
        /// <summary>
        /// How many characters stand at once on the person's shelf: the first place's count, set by the stage from <see cref="Places"/>
        /// (the lead, 2026-11-17T18:56Z, no fixed cap). Its renders set it to judge a count; nothing else does. It is static, as the shelf's other
        /// published measures are (<see cref="PlaneRestHalfDegrees"/>, <see cref="NewProjectReach"/>): the app has one stage
        /// (WorkspaceDirector.EnsureAgentStage), and the placement reads the count before any stage exists (RoomPlacement, the shelf's corners).
        /// </summary>
        public static int ShelfCapacity { get; set; } = 1;

        /// <summary>
        /// Every project with work, each its own place (the lead, 2026-11-17T18:56Z: no fixed cap; a project's agents all stand). The
        /// person's shelf is the first place: the selected task's project, every one of its tasks standing (<see cref="RoomPlace.Shown"/>),
        /// so <see cref="ShelfCapacity"/> is that place's count and no longer a constant.
        /// </summary>
        public RoomPlaces Places { get; } = new RoomPlaces();

        /// <summary>
        /// How many of the shelf's characters stand in its front row: all of them up to FIELD's one-row count (the places sheet, 2026-11-17T19:03Z),
        /// the rest in the back row (<see cref="CharacterPlace"/>). Beside a window, and on the older shelf, every character is the front row.
        /// </summary>
        public static int FrontRow => RoomDesign.Enabled ? Mathf.Min(ShelfCapacity, PlaceRule.Default.FullMost) : ShelfCapacity;

        /// <summary>The shelf's places: its front row's, and under the redesign one more for New Project, the shelf's first.</summary>
        public static int ShelfPlaces => FrontRow + (RoomDesign.Enabled ? 1 : 0);

        /// <summary>How far behind the front row the back row stands, in metres on the shelf (FIELD's places sheet).</summary>
        public const float BackRowDepth = 0.35f;

        /// <summary>How far above the front row the back row stands, in metres (FIELD's places sheet).</summary>
        public const float BackRowRise = 0.10f;

        /// <summary>The back row's size against the front row's: the sheet's "smaller" (FIELD's places sheet).</summary>
        public const float BackRowScale = 0.85f;

        /// <summary>
        /// Where the shelf's character <paramref name="index"/> stands in the shelf's own space, and at what size against a front-row character: the front
        /// row first, New Project's place before it, at <paramref name="pitch"/> a place; then the back row, half a place across, behind and above it
        /// (FIELD's places sheet). Its renders stand their characters by the same rule.
        /// </summary>
        public static (Vector3 Local, float Scale) CharacterPlace(float index, float pitch)
        {
            var front = FrontRow;
            var first = ShelfPlaces - front;
            var middle = (ShelfPlaces - 1) * 0.5f;
            if (index < front - 0.5f || front >= ShelfCapacity) return (new Vector3((index + first - middle) * pitch, 0f, 0f), 1f);
            return (new Vector3((index - front + first + 0.5f - middle) * pitch, BackRowRise, BackRowDepth), BackRowScale);
        }

        private Surface? shelfSurface;
        private Surface? previewSurface;
        private int surfacePlaces;
        private readonly AgentShelfPages shelfPages = new AgentShelfPages();
        public AgentShelfPageInfo ShelfPage => shelfPages.Info;
        public bool ShowShelfPage(int direction)
        {
            if (FocusGuard.InputSuspended || !presentation.AgentsVisible || !Visible || !shelfPages.Move(direction)) return false;
            Refresh(); return true;
        }

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

        /// <summary>
        /// Each slot's height beside a window, in degrees from eye level: up for the inner slots. 5.5 and -16.5, not the first
        /// 4 and -15: the upper character's label now lays a filmed name whole on up to three lines (AUDIT's film MUST,
        /// 2026-11-16), and at 4 and -15 it stood 1.4 degrees into the lower character's body at Standard, 1.7 at Larger (F137).
        /// </summary>
        public static readonly float[] WindowSlotLiftDegrees = { -16.5f, 5.5f, 5.5f, -16.5f };

        /// <summary>The gap between a label plate and the surface it rests on, in a character's units.</summary>
        private const float SurfaceClearance = 0.005f;

        private readonly Dictionary<string, CharacterView> views = new Dictionary<string, CharacterView>();
        private readonly List<WorkstreamView> eligible = new List<WorkstreamView>();
        private readonly Dictionary<string, Standing> standings = new Dictionary<string, Standing>();
        private readonly List<string> departed = new List<string>();
        private ControlPlaneConnection connection = null!;
        private CharacterLineup lineup = null!;
        private Transform arc = null!;
        private Transform? head;
        private IStagePlacementSource? source;
        private float nextSourceSearch;
        private bool preferredChanged;
        private bool placedOnce;
        private bool onSurface;
        private Vector3 shelfViewer;
        private WorkspacePresentationState presentation = new WorkspacePresentationState();
        private Transform preview = null!;
        /// <summary>Conservative occupied shelf, rendered label and animated body envelope shared by placement and actual scene checks.</summary>
        private static CharacterView? shelfMeasure;
        private static int shelfMeasureAt = -1;
        /// <param name="places">The shelf's places, where its width is not the natural one for them (<see cref="FittedWidth"/>): its ends' labels stand that far apart.</param>
        public static void WriteShelfCorners(Pose pose, float width, Vector3 viewer, Vector3[] corners, int? places = null)
        {
            if (corners.Length != 8) throw new System.ArgumentException("Shelf envelope has eight corners.", nameof(corners));
            // A real label model measures font metrics, whole source marks, title wrapping and eye-facing tilt.
            // It is created only by the explicit placement/geometry consumer and never has a target.
            if (shelfMeasure == null || shelfMeasureAt != GlazeText.Version)
            {
                if (shelfMeasure != null)
                {
#if UNITY_EDITOR
                    Object.DestroyImmediate(shelfMeasure.transform.parent.gameObject);
#else
                    Object.Destroy(shelfMeasure.transform.parent.gameObject);
#endif
                }
                shelfMeasureAt = GlazeText.Version;
                var model = new GameObject("Shelf label measurement") { hideFlags = HideFlags.HideAndDontSave };
                shelfMeasure = CharacterView.Create(model.transform, "shelf-label-measurement");
                shelfMeasure.enabled = false;
            }
            var minimum = new Vector3(-width / 2f, 0f, -AgentShelfDraft.Depth / 2f);
            var maximum = new Vector3(width / 2f, 0.5f, AgentShelfDraft.Depth / 2f);
            var span = places is int count && count > 0 ? Mathf.Max(0f, width - 0.1f - PitchOf(count, width))
                : Mathf.Max(0f, width - 0.1f - AgentShelfDraft.Spacing * AgentShelfDraft.Scale);
            foreach (var x in new[] { -span / 2f, span / 2f })
            foreach (var marks in new[] { (Synthetic: false, Recorded: true), (Synthetic: true, Recorded: false), (Synthetic: true, Recorded: true) })
            {
                shelfMeasure.transform.SetPositionAndRotation(pose.position + pose.rotation * Vector3.right * x,
                    pose.rotation * Quaternion.Euler(0f, 180f, 0f));
                shelfMeasure.transform.localScale = Vector3.one * AgentShelfDraft.Scale;
                // The widest label the shelf can ever show (AUDIT's 12:52Z and 12:55Z): a name long enough to reach the label's cap (FIELD's
                // four shelf lines at the widest measure, its tail faded past them), in the state that draws the most beside it, Needs You with
                // its count (FIELD's badge figure). The same for every title, so no name, short or long, stands nearer than the clearance,
                // and the shelf never moves because a name came or went.
                var title = WidestStandIn;
                shelfMeasure.Show(new CharacterPresentation("shelf-label-measurement",
                    title, CharacterActivity.WaitingForHuman,
                    "Needs You", Halcyonic.Contracts.AttentionLevel.ActionRequired, System.Array.Empty<string>(), 2,
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
            // Under the redesign New Project stands at the shelf's first place and turns left until it is clear of the widest tag beside
            // it (RoomNewProjectPlace.StandClear): the envelope reaches as far left as that place can, so the field search counts it
            // (XR 2026-11-16T18:35Z).
            if (RoomDesign.Enabled && places is int all && all > ShelfCapacity && NewProjectReach is System.Func<float> reachOf)
            {
                var characterX = (all - ShelfCapacity - (all - 1) * 0.5f) * PitchOf(all, width);
                var distance = Vector3.Distance(viewer, pose.position + pose.rotation * Vector3.right * characterX);
                minimum.x = Mathf.Min(minimum.x, characterX - distance * Mathf.Tan(reachOf()));
            }
            // Round the measured envelope outward in production. This strengthens clearance and
            // contains matrix/bounds roundoff without relaxing the actual-render containment guard.
            minimum -= Vector3.one * 0.002f; maximum += Vector3.one * 0.002f;
            for (var index = 0; index < 8; index++)
                corners[index] = pose.position + pose.rotation * new Vector3(index % 2 == 0 ? minimum.x : maximum.x,
                    index < 4 ? minimum.y : maximum.y, index % 4 < 2 ? minimum.z : maximum.z);
        }
        public float ShelfWidth => FittedWidth(ShelfPlaces);

        /// <summary>How far apart the shelf's places stand, in its own space: its width less its ends shared among its places.</summary>
        public float ShelfPitch => PitchOf(ShelfPlaces, ShelfWidth);

        /// <summary>The pitch of <paramref name="places"/> on a shelf <paramref name="width"/> wide: the natural Spacing x Scale at its natural width.</summary>
        public static float PitchOf(int places, float width) => (width - 0.1f) / Mathf.Max(1, places);

        /// <summary>
        /// The plane at rest, each side of its heading, in degrees, at the text size now, as the plane reasons it through its own geometry
        /// (MenuPlane, from FIELD's measured resting entry); null before a plane is made. Asked at the moment a shelf is fitted, so a text
        /// size changed is fitted at its own rest, never the last size's (JUDGE's landing walk: two fit numbers at Larger).
        /// </summary>
        public static System.Func<float>? PlaneRestHalfDegrees { get; set; }

        /// <summary>
        /// How far left of the first character's middle New Project's place can reach once it stands clear of the widest tag a name draws, as an
        /// angle from the eyes in radians (<c>RoomNewProjectPlace.ReachLeft</c>, published by the room's parts, which this assembly cannot see);
        /// null until they publish, when the envelope holds the labels alone.
        /// </summary>
        public static System.Func<float>? NewProjectReach { get; set; }

        private static (int Places, int Version, float RestHalf, float Field, float Width)? fitted;

        /// <summary>The narrowest a fitted shelf is laid, in metres: its places' margin (<see cref="PitchOf"/>) and nothing between them.</summary>
        private const float MinimumFittedWidth = 0.1f;

        /// <summary>
        /// The width every shelf of <paramref name="places"/> takes (the lead's 2026-11-14T02:21Z ruling 1 (a): the shelf fits the room, one
        /// width for every shelf): its natural width, or narrower where its envelope (the shelf and its ends' widest labels, as
        /// <see cref="WriteShelfCorners"/> measures them) would not fit between the plane at rest and the field's edge less its margin,
        /// with the clearance kept, at the search's nearest distance, where a shelf is widest to the eye. Its places stand closer to
        /// fit; that they never touch and each label stays whole is the walks' check. Derived from measured numbers, none typed; said
        /// once per text size in a line the walk reads.
        /// </summary>
        public static float FittedWidth(int places)
        {
            var natural = AgentShelfDraft.Width(places);
            if (!RoomDesign.Enabled || PlaneRestHalfDegrees is not System.Func<float> restOf) return natural;
            var rest = restOf();
            var measured = ViewField.Current ?? MenuPage.Quest3S;
            var side = (float)System.Math.Min(measured.Left, measured.Right);
            if (fitted is { } was && was.Places == places && was.Version == GlazeText.Version && Mathf.Approximately(was.RestHalf, rest) && Mathf.Approximately(was.Field, side))
                return was.Width;
            var fit = side - ViewField.EdgeMarginDegrees - rest - WorkspacePlacement.ClearanceDegrees;
            var most = 2f * AgentShelfSearch.MinimumDistance * Mathf.Tan(Mathf.Max(1f, fit) * Mathf.Deg2Rad / 2f);
            var width = natural;
            // Where even the narrowest shelf's envelope would not fit, narrowing cannot help: with one character a page the envelope's
            // left end is New Project's turn clear of the tag, which no width moves, and a shelf narrowed to its floor only crowds its places
            // onto each other (b208's parts, XR 2026-11-16T21:21Z). It keeps its natural width, and the search places it as it can.
            var narrows = Envelope(natural, places) > most && Envelope(MinimumFittedWidth, places) <= most;
            if (narrows)
            {
                float low = MinimumFittedWidth, high = natural;
                for (var step = 0; step < 14; step++)
                {
                    var middle = (low + high) / 2f;
                    if (Envelope(middle, places) > most) high = middle; else low = middle;
                }
                width = low;
            }
            fitted = (places, GlazeText.Version, rest, side, width);
            Debug.Log("Halcyonic: the shelf fits " + fit.ToString("0.0") + " degrees wide beside the plane at rest at text size " + GlazeText.Scale.ToString("0.##")
                + ": laid " + width.ToString("0.000") + " m wide (natural " + natural.ToString("0.000") + "), its places " + PitchOf(places, width).ToString("0.000") + " m apart"
                + (!narrows && Envelope(natural, places) > most ? "; no narrower shelf fits, so the natural width stands." : "."));
            return width;
        }

        /// <summary>How wide a shelf <paramref name="width"/> wide with <paramref name="places"/> stands with its ends' labels, in metres.</summary>
        private static float Envelope(float width, int places)
        {
            var corners = new Vector3[8];
            WriteShelfCorners(new Pose(new Vector3(0f, -0.45f, AgentShelfSearch.MinimumDistance), Quaternion.identity), width, Vector3.zero, corners, places);
            float left = float.PositiveInfinity, right = float.NegativeInfinity;
            foreach (var corner in corners) { left = Mathf.Min(left, corner.x); right = Mathf.Max(right, corner.x); }
            return right - left;
        }
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
            if (workstreamId == null || connection.Session is not RealtimeSession session) return;
            // Explicit task selection also works before this optional stage is enabled.
            shelfPages.Update(session.State.Journal?.JournalId, session.State.Workstreams.Values);
            if (!shelfPages.Select(workstreamId)) return;
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
            lineup = new CharacterLineup(BesideAWindow ? WindowCapacity : ShelfCapacity);
            radius = AgentShelfDraft.Scale;
            CharacterMaterials.Prepare();

            // Hidden until agents are enabled and a confirmed shelf pose exists.
            arc = new GameObject("Agent shelf").transform;
            arc.SetParent(transform, false);
            arc.gameObject.SetActive(false);

            shelfSurface = MakeShelf(arc, "Agent shelf", new Color(0.12f, 0.19f, 0.22f, 1f));
            preview = new GameObject("Shelf preview").transform;
            preview.SetParent(transform, false);
            previewSurface = MakeShelf(preview, "Shelf preview surface", new Color(0.16f, 0.4f, 0.48f, 0.7f));
            surfacePlaces = ShelfPlaces;
            preview.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            presentation.Changed -= PresentationChanged;
            presentation.Changed += PresentationChanged;
            connection.Changed += OnChanged;
            FocusGuard.FoldChanged += Refresh;
        }

        private void OnDisable()
        {
            presentation.Changed -= PresentationChanged;
            connection.Changed -= OnChanged;
            FocusGuard.FoldChanged -= Refresh;
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
                Place(Turned(preferred.Value, asideNow), "the person confirmed the shelf position");
                Refresh();
            }
            else if (!Mathf.Approximately(asideNow, asideTarget))
            {
                // Seen to travel, never to vanish and reappear (the lead's 19:34Z): a steady turn about the eyes to the aside place and back;
                // under Keep things still it lands at once, since nothing moves on its own then (the lead's 2026-11-14T06:00Z).
                asideNow = GlazeMotion.Still ? asideTarget : Mathf.MoveTowards(asideNow, asideTarget, ShelfTurnDegreesPerSecond * Time.unscaledDeltaTime);
                StandOnSurface(Turned(preferred.Value, asideNow));
            }
            Glide();
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
                Refreshed?.Invoke();
                return;
            }

            var live = session.Status.IsLive;
            // Optional paging exposes all work independently of old project stage-visibility preferences.
            shelfPages.Update(session.State.Journal?.JournalId, session.State.Workstreams.Values);
            var state = session.State;
            Places.Update(state.Journal?.JournalId, state.Workstreams.Values, id => state.Projects.TryGetValue(id, out var project) ? project.Name : null, shelfPages.SelectedId);
            // The person's shelf stands the selected task's project's place whole; with nothing selected, the first place.
            var selected = shelfPages.SelectedId;
            var place = (selected != null ? Places.PlaceOf(selected) : null) ?? (Places.Places.Count > 0 ? Places.Places[0] : null);
            eligible.Clear();
            if (place != null)
                foreach (var shown in place.Shown)
                    if (state.Workstreams.TryGetValue(shown, out var chosen)) eligible.Add(chosen);
            var standing = Mathf.Max(1, eligible.Count);
            if (standing != ShelfCapacity)
            {
                ShelfCapacity = standing;
                shelfPages.PerPage = standing;
                Fit();
                return;
            }
            lineup.UseJournal(session.State.Journal?.JournalId);
            lineup.Update(eligible, System.DateTimeOffset.UtcNow);
            departed.Clear();
            foreach (var id in views.Keys)
            {
                if (lineup.SlotOf(id) < 0) departed.Add(id);
            }
            foreach (var id in departed)
            {
                views[id].gameObject.SetActive(false);
                Destroy(views[id].gameObject);
                views.Remove(id);
                standings.Remove(id);
            }
            // Under the redesign the place's own order stands them: the one that needs the person first, in the front row (FIELD's sheet).
            var slots = RoomDesign.Enabled && !BesideAWindow && place != null ? place.Shown.Select(id => (string?)id).ToList() : lineup.Slots.ToList();
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
            Refreshed?.Invoke();
        }

        /// <summary>
        /// A name long enough to fill the label to its shelf cap at either text size (FIELD's four lines, 82fc14aea), so the envelope
        /// measures the widest box a label can take. Under the redesign the drawn name is the room's tag, bounded across and never
        /// wider than this label (the shelf journey checks it at each size; AUDIT's 13:23Z).
        /// </summary>
        public const string WidestStandIn = "A complete task title a person gives, in plain words, long enough to fill every line the label can lay at its widest "
            + "measure, with more words after that so its tail fades past its last line, as a very long name does";

        /// <summary>
        /// The lineup's slots for how the characters stand now: four beside a window, else six. What it
        /// knows and keeps carries over (<see cref="CharacterLineup.WithCapacity"/>); the characters
        /// take their new slots at the next refresh.
        /// </summary>
        private void Fit()
        {
            var capacity = BesideAWindow ? WindowCapacity : ShelfCapacity;
            if (capacity != lineup.Capacity) lineup = lineup.WithCapacity(capacity);
            // The surface is as wide as the shelf's places, which the redesign widens by its own and New Project's.
            if (surfacePlaces != ShelfPlaces)
            {
                surfacePlaces = ShelfPlaces;
                DrawShelf(shelfSurface, new Color(0.12f, 0.19f, 0.22f, 1f));
                DrawShelf(previewSurface, new Color(0.16f, 0.4f, 0.48f, 0.7f));
            }
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
            if (BesideAWindow != wasBeside || (BesideAWindow ? WindowCapacity : ShelfCapacity) != lineup.Capacity) Fit();
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
            DrawShelf(surface, color);
            return surface;
        }

        private void DrawShelf(Surface? surface, Color color)
        {
            if (surface == null) return;
            surface.Draw(new Vector2(ShelfWidth, AgentShelfDraft.Depth), 0.025f, color, new Color(0.45f, 0.65f, 0.7f, 1f), 0.002f);
        }

        /// <summary>Where the shelf's place <paramref name="place"/> stands, in the shelf's own space: New Project's first, then the characters'.</summary>
        public Vector3 PlaceOf(int place) => new Vector3((place - (ShelfPlaces - 1) * 0.5f) * ShelfPitch * radius / AgentShelfDraft.Scale, 0f, 0f);

        /// <summary>The shelf as it stands, and the eyes it was placed for: what the shelf's own parts stand on.</summary>
        public Transform Shelf => arc;

        /// <summary>
        /// The turn about the eyes the shelf stands aside by while the plane is open (ShelfAside, the lead's 19:34Z GO), in degrees; 0
        /// back in its own place. View placement only: the place the person chose (the placement's Preferred) is never rewritten,
        /// and nothing is journaled. The shelf travels to it at <see cref="ShelfTurnDegreesPerSecond"/>. (Not the lineup's own
        /// <see cref="AsideDegrees"/>, which turns the characters right of a window.)
        /// </summary>
        public float ShelfTurnDegrees
        {
            get => asideTarget;
            set => asideTarget = float.IsFinite(value) ? value : 0f;
        }

        /// <summary>How fast the shelf turns aside and back, in degrees a second: a move the eye follows.</summary>
        public const float ShelfTurnDegreesPerSecond = 60f;

        private float asideTarget, asideNow;

        /// <summary>The shelf in its own place, the aside turn taken off: what the plane keeps clear of and decides by.</summary>
        public Pose OwnShelf => Turned(new Pose(arc.position, arc.rotation), -asideNow);

        /// <summary><paramref name="pose"/> turned <paramref name="degrees"/> about the eyes' vertical, its facing turned with it.</summary>
        private Pose Turned(Pose pose, float degrees)
        {
            if (Mathf.Approximately(degrees, 0f)) return pose;
            var eyes = head != null ? head.position : Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            var turn = Quaternion.Euler(0f, degrees, 0f);
            var from = pose.position - new Vector3(eyes.x, pose.position.y, eyes.z);
            return new Pose(new Vector3(eyes.x, pose.position.y, eyes.z) + turn * from, turn * pose.rotation);
        }

        public Vector3 ShelfViewer => shelfViewer;

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
                // Under Keep things still a character stands in its new place at once, no glide and no pulse (the lead's 2026-11-14T06:00Z).
                var progress = GlazeMotion.Still ? 1f : Mathf.Clamp01((Time.time - standing.Started) / standing.Duration);
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
            // On the redesign's shelf New Project takes the first place, at the person's left (DESIGN 2.2), and the characters
            // the places after it; beside a window, and on the older shelf, the characters fill it as ever.
            var first = BesideAWindow ? 0 : ShelfPlaces - FrontRow;
            var places = BesideAWindow ? lineup.Capacity : ShelfPlaces;
            var pitch = BesideAWindow ? AgentShelfDraft.Spacing * radius : ShelfPitch * radius / AgentShelfDraft.Scale;
            var (local, size) = BesideAWindow ? (new Vector3((slot + first - (places - 1) * 0.5f) * pitch, 0f, 0f), 1f) : CharacterPlace(slot, pitch);
            view.transform.localPosition = local;
            view.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            view.transform.localScale = Vector3.one * radius * size;
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

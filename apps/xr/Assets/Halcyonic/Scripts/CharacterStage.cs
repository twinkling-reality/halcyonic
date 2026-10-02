#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>
    /// The characters, on an arc in front of the person, kept current from the session. The lineup
    /// in the client core decides which workstreams have a character and which slot each stands in;
    /// this component places the arc relative to the person and says on a banner under the
    /// characters' labels whether the state is live, so a disconnected stage never looks live. It
    /// keeps animating and updating while the app lacks input focus: losing focus is not a pause.
    /// </summary>
    /// <remarks>
    /// The arc keeps every character and its labels within about 36 degrees of where the person
    /// faced, a comfortable field of view on narrower headsets too, and the lineup puts characters
    /// that need attention in its middle. Everything is looked at and pointed at from the seat;
    /// nothing needs the person to stand or reach.
    /// <para>
    /// An <see cref="IStagePlacementSource"/> on the same object, such as a room placement that found
    /// the person's desk, can give the stage a surface instead. Its pose's position is where the
    /// middle of the lineup stands; the arc curves around the person's side of it, at the distance
    /// they were from it when the pose arrived, and every character's label plate rests on the
    /// surface. While the pose is set, only the source moves the stage: recenters leave it where it
    /// is. Without a source, or when it clears its pose, the stage stands in front of the person.
    /// </para>
    /// <para>
    /// In front of the person, the characters stand as the person chose (<see cref="StageArrangement"/>):
    /// on the arc, the arc turned to their right, or at most four either side of a window lane
    /// straight ahead, two on each side, one just above eye level and one below it, each with only
    /// its badge and marks, what waits in the upper places, nearest the window's middle. Halcyonic
    /// cannot see a window, so the lane is where one most often opens; the banner stands under it in
    /// every arrangement.
    /// </para>
    /// <para>
    /// In front of the person, it is placed again only when <see cref="InFrontPlacement"/> says so:
    /// at the start, after a pause, when the person recenters, or after a jump no head can make. The
    /// reference space changes that come in bursts while system windows take and give back focus
    /// move nothing, so they never move it; one that does move the tracking space moves the stage
    /// with it, so it stays where it was around the person. The log says which.
    /// </para>
    /// </remarks>
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

        [Tooltip("How far the characters stand from the person's head, in meters. System windows, such as a virtual display opened over the app, render within about 2 m, so the characters stand beyond them. Characters and their labels scale with this distance, so they keep the same apparent size.")]
        [SerializeField] private float distance = DefaultDistance;

        [Tooltip("The height of the characters' centers relative to the person's eyes when the stage was placed, in meters; negative is below. At -0.17 and 2.4 m they stand about 4 degrees below eye level, their labels end about 14 degrees down, and the banner and any open panel go under the labels. A character that needs the person rises from here toward their eye level.")]
        [SerializeField] private float heightFromEyes = DefaultHeightFromEyes;

        [Tooltip("The angle between the outermost characters, in degrees, centered on where the person faced when the stage was placed. At 60 the outermost stand 30 degrees to each side and their labels end within about 36, a comfortable field of view on narrower headsets too.")]
        [SerializeField] private float spanDegrees = 60f;

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

        private const string ArrangementPreference = "halcyonic.stage.arrangement";

        /// <summary>Where the choice to turn the lineup aside was kept before there were three arrangements.</summary>
        private const string AsidePreference = "halcyonic.stage.aside";
        private StageArrangement arrangement;

        /// <summary>The nearest and the default reach to a surface, in meters.</summary>
        private const float NearestSurface = 0.4f;
        private const float DefaultSurface = 0.8f;

        /// <summary>The gap between a label plate and the surface it rests on, in a character's units.</summary>
        private const float SurfaceClearance = 0.005f;

        private readonly Dictionary<string, CharacterView> views = new Dictionary<string, CharacterView>();
        private readonly List<WorkstreamView> eligible = new List<WorkstreamView>();
        private readonly Dictionary<string, Standing> standings = new Dictionary<string, Standing>();
        private readonly List<string> departed = new List<string>();
        private readonly PersonPlacement placement = new PersonPlacement();
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

        /// <summary>The arc's radius now: the distance setting in front of the person, or the reach to a surface.</summary>
        private float radius;
        private string? shownBanner;
        private BannerKind shownKind;
        private string? shownWaiting;
        private string? shownNotice;
        private string? shownNotShown;
        private string? shownStillOpen;
        private int shownScale = -1;
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

        /// <summary>Where the characters stand in front of the person, as they chose.</summary>
        public StageArrangement Arrangement => arrangement;

        /// <summary>The characters stand beside a window now: chosen, and the stage stands in front of the person.</summary>
        public bool BesideAWindow => arrangement == StageArrangement.BesideAWindow && !onSurface;

        /// <summary>
        /// Stands the characters as <paramref name="value"/> says and keeps the choice on the device.
        /// On a surface the room placement decides where they stand, so this waits until the stage
        /// stands in front of the person again.
        /// </summary>
        public void SetArrangement(StageArrangement value)
        {
            if (value == arrangement) return;
            arrangement = value;
            PlayerPrefs.SetInt(ArrangementPreference, (int)value);
            PlayerPrefs.Save();
            if (!placedOnce || onSurface) return;
            Fit();
            Place(null, value switch
            {
                StageArrangement.TurnedAside => "the person made room for a window",
                StageArrangement.BesideAWindow => "the person stood the characters beside a window",
                _ => "the person brought the characters back in front",
            });
        }

        /// <summary>Raised after the characters were brought up to date, so the rail can count what has none.</summary>
        public event System.Action? Refreshed;

        private void Awake()
        {
            connection = GetComponent<ControlPlaneConnection>();
            arrangement = PlayerPrefs.HasKey(ArrangementPreference)
                ? (StageArrangement)Mathf.Clamp(PlayerPrefs.GetInt(ArrangementPreference), 0, (int)StageArrangement.BesideAWindow)
                : PlayerPrefs.GetInt(AsidePreference, 0) == 1 ? StageArrangement.TurnedAside : StageArrangement.InFront;
            // The stage stands in front of the person until a room placement gives it a surface.
            lineup = new CharacterLineup(BesideAWindow ? WindowCapacity : Mathf.Max(1, maxCharacters));
            radius = distance;
            CharacterMaterials.Prepare();

            // Hidden until it is placed in front of the person.
            arc = new GameObject("Arc").transform;
            arc.SetParent(transform, false);
            arc.gameObject.SetActive(false);

            // The banner reads along its parent's forward axis, away from the person, as the arc's does.
            bannerRoot = new GameObject("Banner").transform;
            bannerRoot.SetParent(arc, false);
            banner = StageBanner.Create(bannerRoot);
        }

        private void OnEnable()
        {
            connection.Changed += OnChanged;
            FocusGuard.FoldChanged += Refresh;
            AmbientCover.Changed += ShowBannerUncovered;
            ShowBannerUncovered();
        }

        private void OnDisable()
        {
            connection.Changed -= OnChanged;
            FocusGuard.FoldChanged -= Refresh;
            AmbientCover.Changed -= ShowBannerUncovered;
            placement.Stop();
            if (source != null) source.Changed -= OnPreferredChanged;
            source = null;
            nextSourceSearch = 0f;
        }

        private void Start() => Refresh();

        // The person may have moved while the headset was off.
        private void OnApplicationPause(bool paused) => placement.Paused(paused, Time.unscaledTime);

        // With system windows open, focus can flap many times a second, each flap with a reference
        // space change that moves nothing: the placement ignores those.
        private void OnApplicationFocus(bool hasFocus) => placement.FocusChanged(Time.unscaledTime);

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
                ShowBanner(shownBanner ?? "", shownKind, shownWaiting, shownNotShown, shownStillOpen);
            }
            var preferred = source?.Preferred;
            var decision = placement.Poll(head, Time.unscaledTime, Time.unscaledDeltaTime);
            var placed = false;
            if (preferredChanged && placedOnce)
            {
                preferredChanged = false;
                placed = preferred.HasValue || onSurface;
                if (preferred.HasValue) Place(preferred, "the room placement gave it a surface");
                else if (onSurface) Place(null, "the room placement has no surface any more");
            }
            switch (decision.Action)
            {
                case PlacementAction.PlaceInFront when !placed:
                    // Once the stage stands on a surface, only the room placement moves it.
                    if (!placedOnce || !preferred.HasValue) Place(preferred, decision.Reason);
                    else Debug.Log("Halcyonic: kept the stage on its surface although " + decision.Reason + ".");
                    break;
                case PlacementAction.Follow when placedOnce && !placed:
                    if (onSurface) Debug.Log("Halcyonic: kept the stage on its surface although " + decision.Reason + ".");
                    else Follow(decision);
                    break;
                case PlacementAction.Kept:
                    Debug.Log("Halcyonic: kept the stage where it stands: " + decision.Reason + ".");
                    break;
            }
            Glide();
        }

        /// <summary>
        /// Moves the stage with the tracking space, which turned and shifted under the person without
        /// them moving, so it stays where it was around them and nothing jumps.
        /// </summary>
        private void Follow(PlacementDecision decision)
        {
            var turn = Quaternion.Euler(0f, decision.Turn, 0f);
            var from = new Vector3(decision.From.X, decision.From.Y, decision.From.Z);
            var to = new Vector3(decision.To.X, decision.To.Y, decision.To.Z);
            arc.SetPositionAndRotation(to + turn * (arc.position - from), turn * arc.rotation);
            CharacterMaterials.SetKeyLight(arc.rotation);
            Debug.Log("Halcyonic: moved the stage with the tracking space, which turned "
                + decision.Turn.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " degrees, because "
                + decision.Reason + ".");
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

        private void OnPreferredChanged() => preferredChanged = true;

        private void OnChanged(StateChanges changes) => Refresh();

        /// <summary>Brings the characters up to date with the session, the visibility and the request.</summary>
        public void Refresh()
        {
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
            var folded = FocusGuard.Folded && session != null;
            var waiting = folded ? AmbientText.NeedsYouLine(AmbientText.NeedsYou(session!.State)) : null;
            var notShown = folded && BesideAWindow ? AmbientText.NotShown(session!.State.Workstreams.Count - views.Count) : null;
            var stillOpen = folded && AmbientCover.OpenPanel is string panel ? AmbientText.StillOpen(panel) : null;
            ShowBanner(
                demonstration ?? (session == null ? connection.SetupProblem ?? NotConnected : Describe(session)),
                demonstration != null ? BannerKind.Practice : session != null && session.Status.IsLive ? BannerKind.Live : BannerKind.NotLive,
                waiting, notShown, stillOpen);
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
            if (preferred.HasValue) StandOnSurface(preferred.Value);
            else StandBeforePerson();
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

        /// <summary>
        /// Stands the arc where the person's head is, facing where they face on the level, so the
        /// characters are in front of them wherever the tracking origin is.
        /// </summary>
        private void StandBeforePerson()
        {
            onSurface = false;
            radius = distance;
            if (head == null) return;
            var forward = head.forward;
            forward.y = 0f;
            var facing = forward.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(forward, Vector3.up)
                : Quaternion.Euler(0f, head.eulerAngles.y, 0f);
            if (arrangement == StageArrangement.TurnedAside) facing *= Quaternion.Euler(0f, AsideDegrees, 0f);
            arc.SetPositionAndRotation(head.position, facing);
        }

        /// <summary>
        /// Stands the arc on a surface: the middle of the lineup at the pose's position, the arc's
        /// center on the person's side of it at the surface's height, and its radius the person's
        /// distance from the pose, so the characters face them.
        /// </summary>
        private void StandOnSurface(Pose pose)
        {
            onSurface = true;
            var toward = head != null ? pose.position - head.position : pose.forward;
            toward.y = 0f;
            if (toward.sqrMagnitude < 1e-4f)
            {
                toward = pose.forward;
                toward.y = 0f;
            }
            if (toward.sqrMagnitude < 1e-6f) toward = Vector3.forward;
            radius = head != null ? Mathf.Clamp(toward.magnitude, NearestSurface, distance) : DefaultSurface;
            var facing = Quaternion.LookRotation(toward.normalized, Vector3.up);
            arc.SetPositionAndRotation(pose.position - facing * Vector3.forward * radius, facing);
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
            : Mathf.Atan2(heightFromEyes, radius) * Mathf.Rad2Deg;

        /// <summary>
        /// A slot's angle around the arc from where the person faced, spread as <see cref="Spread"/>
        /// says; beside a window, either side of its lane.
        /// </summary>
        private float SlotAngle(int slot)
        {
            if (BesideAWindow) return WindowSlotDegrees[Mathf.Clamp(slot, 0, WindowSlotDegrees.Length - 1)];
            var count = lineup.Capacity;
            var eyesAbove = onSurface && head != null ? head.position.y - arc.position.y : 0f;
            var span = spanDegrees * Spread(radius, onSurface ? -eyesAbove : heightFromEyes);
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
            var radians = angle * Mathf.Deg2Rad;
            var level = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
            // The eyes are the arc's origin in front of the person, and above it on a surface.
            var eyesAbove = onSurface && head != null ? head.position.y - arc.position.y : 0f;
            var (height, scale) = Stance(view, radius, eyesAbove, onSurface ? (float?)null : radius * Mathf.Tan(lift * Mathf.Deg2Rad));
            var character = view.transform;
            character.localPosition = level * (radius * reach) + Vector3.up * height;
            character.localRotation = Quaternion.LookRotation(-level, Vector3.up);
            character.localScale = Vector3.one * scale;
        }

        /// <summary>
        /// How high the stage stands a character <paramref name="radius"/> away, from the arc's origin,
        /// and how large: at <paramref name="heightFromEyes"/> in front of the person, or, with no
        /// height, its label resting on the surface at the arc's origin, with the eyes
        /// <paramref name="eyesAbove"/> over it. It scales with its distance from the eyes, so it keeps
        /// its apparent size near or far, above or below, and its label hangs as low as being seen
        /// from above needs (<see cref="CharacterView.ViewedFrom"/>).
        /// </summary>
        public static (float Height, float Scale) Stance(CharacterView view, float radius, float eyesAbove, float? heightFromEyes)
        {
            var height = heightFromEyes ?? 0f;
            var scale = radius;
            // Standing on a surface depends on the label, which depends on the view: a few passes settle it.
            for (var pass = 0; pass < 3; pass++)
            {
                var below = height - eyesAbove;
                view.ViewedFrom(Mathf.Atan2(below, radius) * Mathf.Rad2Deg);
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
            ShowBanner(shownBanner ?? "", shownKind, shownWaiting, shownNotShown, shownStillOpen);
        }

        /// <param name="waiting">What needs the person, said on a line of its own and in the attention color, or null.</param>
        /// <param name="notShown">Beside a window, how many more tasks have no character, or null.</param>
        /// <param name="stillOpen">The panel kept while another window has focus, or null.</param>
        private void ShowBanner(string text, BannerKind kind, string? waiting, string? notShown, string? stillOpen)
        {
            if (text == shownBanner && kind == shownKind && waiting == shownWaiting && notice == shownNotice
                && notShown == shownNotShown && stillOpen == shownStillOpen && shownScale == GlazeText.Version) return;
            // The text's size changes the banner and how deep the labels above it reach.
            shownScale = GlazeText.Version;
            shownBanner = text;
            shownKind = kind;
            shownWaiting = waiting;
            shownNotice = notice;
            shownNotShown = notShown;
            shownStillOpen = stillOpen;
            // A connection's detail, a setup problem or a notice can carry a server's or an exception's
            // words, and the panel kept a task's title; the banner shows them by the one rule for text
            // Halcyonic did not write.
            banner.Show(text, kind, waiting, notice, notShown, stillOpen);
            PlaceBanner();
        }

        /// <summary>
        /// In front of the person, the banner hangs under the lowest a label reaches, where the ambient
        /// strip goes, or beside a window, under the window's lane; over a surface, it stands above the
        /// highest a character reaches, risen included, since the lineup puts what waits for the person
        /// in its middle.
        /// </summary>
        private void PlaceBanner()
        {
            var eyesAbove = onSurface && head != null ? head.position.y - arc.position.y : 0f;
            var top = onSurface ? BannerBottomOnSurface(radius, eyesAbove) : BesideAWindow ? BannerTopBesideWindow(radius) : BannerTop(radius, heightFromEyes);
            bannerRoot.localPosition = new Vector3(0f, top, radius);
            bannerRoot.localScale = Vector3.one * radius;
            // The banner hangs from its top edge; on a surface it stands on its bottom edge instead.
            banner.transform.localPosition = new Vector3(0f, onSurface ? banner.Height : 0f, 0f);
        }

        /// <summary>
        /// The height of the banner's bottom edge over a surface, in meters, with the characters
        /// <paramref name="radius"/> away and the eyes <paramref name="eyesAbove"/> over the surface:
        /// a little more than a degree over the highest a character reaches, its label resting on the
        /// surface and its body risen.
        /// </summary>
        public static float BannerBottomOnSurface(float radius, float eyesAbove) =>
            (SurfaceClearance - CharacterLabelView.DeepestBottom + CharacterView.HighestReach + GlazeTokens.Units(BannerGapDegrees))
            * Mathf.Sqrt(radius * radius + eyesAbove * eyesAbove);

        /// <summary>The banner shows unless a panel or the peek is where it goes (<see cref="AmbientCover"/>).</summary>
        private void ShowBannerUncovered() => banner.gameObject.SetActive(!AmbientCover.Any);

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
                    return "Last known: can't reach " + HostText.Your + ". Trying again…" + (string.IsNullOrEmpty(status.Detail) ? "" : " " + status.Detail);
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

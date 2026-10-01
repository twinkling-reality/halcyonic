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
        /// (<see cref="SetAside"/>): the arc then runs from about straight ahead to 62 degrees right,
        /// so a window in front of them covers fewer characters. Halcyonic cannot see the window, so
        /// this reduces overlap; it guarantees nothing.
        /// </summary>
        public const float AsideDegrees = 32f;

        private const string AsidePreference = "halcyonic.stage.aside";
        private bool aside;

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

        /// <summary>The lineup stands to the person's right, making room for a window in front of them.</summary>
        public bool Aside => aside;

        /// <summary>
        /// Stands the lineup to the person's right, or back in front of them, and keeps the choice on
        /// the device. On a surface the room placement decides where it stands, so this waits until the
        /// stage stands in front of the person again.
        /// </summary>
        public void SetAside(bool value)
        {
            if (value == aside) return;
            aside = value;
            PlayerPrefs.SetInt(AsidePreference, value ? 1 : 0);
            PlayerPrefs.Save();
            if (placedOnce && !onSurface) Place(null, value ? "the person made room for a window" : "the person brought the characters back in front");
        }

        /// <summary>Raised after the characters were brought up to date, so the rail can count what has none.</summary>
        public event System.Action? Refreshed;

        private void Awake()
        {
            connection = GetComponent<ControlPlaneConnection>();
            lineup = new CharacterLineup(Mathf.Max(1, maxCharacters));
            aside = PlayerPrefs.GetInt(AsidePreference, 0) == 1;
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
                ShowBanner(shownBanner ?? "", shownKind, shownWaiting);
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
            // A demonstration says so in its own words, so a recording is never read as live work.
            var demonstration = connection.DemonstrationLine;
            // While another window keeps focus, the line also counts what needs the person, so it stays
            // findable when the window covers the characters.
            var waiting = FocusGuard.Folded && session != null ? AmbientText.NeedsYouLine(AmbientText.NeedsYou(session.State)) : null;
            ShowBanner(
                demonstration ?? (session == null ? connection.SetupProblem ?? NotConnected : Describe(session)),
                demonstration != null ? BannerKind.Practice : session != null && session.Status.IsLive ? BannerKind.Live : BannerKind.NotLive,
                waiting);
            if (session == null)
            {
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
                MoveToSlot(id, view, slot);
            }
            Refreshed?.Invoke();
        }

        /// <summary>Places the stage on the preferred surface, or in front of the person without one.</summary>
        private void Place(Pose? preferred, string reason)
        {
            preferredChanged = false;
            placedOnce = true;
            if (preferred.HasValue) StandOnSurface(preferred.Value);
            else StandBeforePerson();
            CharacterMaterials.SetKeyLight(arc.rotation);
            arc.gameObject.SetActive(true);
            // Every character to its slot, with the current settings; a glide in progress ends.
            foreach (var pair in views)
            {
                var standing = standings[pair.Key];
                var slot = lineup.SlotOf(pair.Key);
                if (slot >= 0) standing.Angle = standing.From = standing.To = SlotAngle(slot);
                Stand(pair.Value, standing.Angle, 1f);
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
            if (aside) facing *= Quaternion.Euler(0f, AsideDegrees, 0f);
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
            if (!standings.TryGetValue(id, out var standing))
            {
                standing = new Standing { Angle = angle, From = angle, To = angle };
                standings[id] = standing;
            }
            else if (!Mathf.Approximately(standing.To, angle))
            {
                standing.From = standing.Angle;
                standing.To = angle;
                standing.Started = Time.time;
                standing.Duration = 0.5f + Mathf.Abs(angle - standing.Angle) / 60f;
                return;
            }
            // Standing again also follows a label plate that grew or shrank, on a surface.
            if (standing.Angle == standing.To) Stand(view, standing.Angle, 1f);
        }

        private void Glide()
        {
            foreach (var pair in views)
            {
                var standing = standings[pair.Key];
                if (standing.Angle == standing.To) continue;
                var progress = Mathf.Clamp01((Time.time - standing.Started) / standing.Duration);
                standing.Angle = progress >= 1f ? standing.To : Mathf.Lerp(standing.From, standing.To, Mathf.SmoothStep(0f, 1f, progress));
                Stand(pair.Value, standing.Angle, 1f + 0.15f * Mathf.Sin(Mathf.PI * progress));
            }
        }

        /// <summary>A slot's angle around the arc from where the person faced, spread as <see cref="Spread"/> says.</summary>
        private float SlotAngle(int slot)
        {
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
        /// <see cref="Stance"/> places it: at the height setting in front of the person, its label
        /// resting on the surface otherwise.
        /// </summary>
        private void Stand(CharacterView view, float angle, float reach)
        {
            var radians = angle * Mathf.Deg2Rad;
            var level = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
            // The eyes are the arc's origin in front of the person, and above it on a surface.
            var eyesAbove = onSurface && head != null ? head.position.y - arc.position.y : 0f;
            var (height, scale) = Stance(view, radius, eyesAbove, onSurface ? (float?)null : heightFromEyes);
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
            ShowBanner(shownBanner ?? "", shownKind, shownWaiting);
        }

        /// <param name="waiting">What needs the person, said on a line of its own and in the attention color, or null.</param>
        private void ShowBanner(string text, BannerKind kind, string? waiting)
        {
            if (text == shownBanner && kind == shownKind && waiting == shownWaiting && notice == shownNotice) return;
            shownBanner = text;
            shownKind = kind;
            shownWaiting = waiting;
            shownNotice = notice;
            // A connection's detail, a setup problem or a notice can carry a server's or an exception's
            // words; the banner shows them by the one rule for text Halcyonic did not write.
            banner.Show(text, kind, waiting, notice);
            PlaceBanner();
        }

        /// <summary>
        /// In front of the person, the banner hangs under the lowest a label reaches, where the ambient
        /// strip goes; over a surface, it stands above the highest a character reaches, risen included,
        /// since the lineup puts what waits for the person in its middle.
        /// </summary>
        private void PlaceBanner()
        {
            var eyesAbove = onSurface && head != null ? head.position.y - arc.position.y : 0f;
            bannerRoot.localPosition = new Vector3(0f, onSurface ? BannerBottomOnSurface(radius, eyesAbove) : BannerTop(radius, heightFromEyes), radius);
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

        private const string NotConnected = "Not connected to your Mac";

        /// <summary>
        /// The height of the banner's top edge from the eyes, in meters, with the characters
        /// <paramref name="radius"/> away and their centers <paramref name="heightFromEyes"/> from the
        /// eyes: a little more than a degree under the lowest any label reaches, all along it. The
        /// banner is flat, so its ends are farther than its middle and look higher; it starts lower by
        /// as much as its widest ends would rise.
        /// </summary>
        public static float BannerTop(float radius, float heightFromEyes) =>
            (heightFromEyes + (CharacterLabelView.DeepestBottom - GlazeTokens.Units(BannerGapDegrees)) * radius)
            / Mathf.Cos(StageBanner.MaxWidthDegrees / 2f * Mathf.Deg2Rad);

        private static string Describe(RealtimeSession session)
        {
            var status = session.Status;
            var origin = session.State.Journal?.Origin == JournalOrigin.Fixture ? " · Recorded" : "";
            switch (status.Phase)
            {
                case ConnectionPhase.Live:
                    return "Connected to your Mac" + origin;
                case ConnectionPhase.WaitingToRetry:
                    return "Last known: can't reach your Mac. Trying again…" + (string.IsNullOrEmpty(status.Detail) ? "" : " " + status.Detail);
                case ConnectionPhase.Refused:
                    return ConnectionText.WhyNotLive(status);
                default:
                    return ConnectionText.Phase(status) + origin;
            }
        }

        /// <summary>Where a character stands on the arc, and where it is gliding to.</summary>
        private sealed class Standing
        {
            public float Angle;
            public float From;
            public float To;
            public float Started;
            public float Duration;
        }
    }
}

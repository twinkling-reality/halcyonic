#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Halcyonic.Client;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.XR;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Halcyonic.XR.Room
{
    /// <summary>
    /// Puts the stage in the person's real room: passthrough on, the characters on their desk, and
    /// the place kept with a spatial anchor so they are there again next time. Implements
    /// <see cref="IStagePlacementSource"/> on the stage object, where <see cref="RoomBootstrap"/>
    /// adds it; <see cref="Preferred"/> is the point on the surface where the middle of the lineup
    /// stands, with a yaw facing away from the person.
    /// </summary>
    /// <remarks>
    /// The real room is the default wherever passthrough works; the person can choose the virtual
    /// space instead (<see cref="SetSpace"/>), and the choice is kept. In the real room it:
    /// <list type="number">
    /// <item>waits for the head to be tracked, so choices are made from where the person sits;</item>
    /// <item>asks once, after a short line of explanation, for access to the room's layout (the
    /// spatial data permission), unless the person declined before;</item>
    /// <item>reads the room through the MR Utility Kit;</item>
    /// <item>restores the anchor saved for that room (or, without a room, the most recent one) if it
    /// localizes and still suits the seat (<see cref="StageSurfaces.StillSuits"/>);</item>
    /// <item>otherwise chooses a surface (<see cref="StageSurfaces.Choose"/>), stands the stage there
    /// at once, and keeps it with a new anchor saved for the room.</item>
    /// </list>
    /// Wherever a step cannot be done (no passthrough, no access, no room, no suitable surface, no
    /// anchor) the stage keeps standing in front of the person and <see cref="Status"/> says why;
    /// nothing waits on the room before the characters appear.
    /// <para>
    /// The anchor follows the real desk. Only its tracked pose moves the stage, never a reference
    /// space event: with system windows such as Virtual Display's open, the session's focus can flap
    /// many times a second and the runtime reports reference space changes each time, although
    /// nothing moved. So while the app has input focus, the pose is published again once the anchor
    /// has moved more than <see cref="MovedMeters"/> or <see cref="MovedDegrees"/> and then held still
    /// for <see cref="SettleSeconds"/>, as after a recenter; it is cleared when the anchor stays
    /// untracked for <see cref="LostSeconds"/> of focused time, and published again when it is
    /// tracked. Without focus nothing changes. A placement without an anchor stays where it was put.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RoomPlacement : MonoBehaviour, IStagePlacementSource
    {
        private const string SpaceKey = "Halcyonic.RoomSpace";
        private const string MemoryKey = "Halcyonic.RoomPlacements";
        private const string DeclinedKey = "Halcyonic.RoomAccessDeclined";

        /// <summary>How long the line explaining the permission shows before the headset asks.</summary>
        private const float ExplainSeconds = 2.5f;

        private const float HeadTrackingSeconds = 3f;
        private const float RestoreSeconds = 6f;
        private const float CreateSeconds = 8f;

        /// <summary>How long, with input focus, an anchor may go untracked before the stage leaves the surface.</summary>
        private const float LostSeconds = 5f;

        /// <summary>
        /// How far the anchored pose must move before the stage follows it. The stage measures its
        /// radius from the head whenever a pose arrives, so anchor jitter must not reach it.
        /// </summary>
        private const float MovedMeters = 0.02f;
        private const float MovedDegrees = 2f;

        /// <summary>How long a moved anchor must hold still, with focus, before the stage follows it.</summary>
        private const float SettleSeconds = 0.5f;

        /// <summary>Still, for <see cref="SettleSeconds"/>: moving no more than this between frames.</summary>
        private const float StillMeters = 0.005f;
        private const float StillDegrees = 0.5f;

        private GameObject root = null!;
        private PassthroughView passthrough = null!;
        private RoomControls? controls;
        private PlacementMemory memory = new PlacementMemory();
        private MRUK? mruk;

        /// <summary>The anchor that keeps the stage's surface this session, what kind of placement it is, and on what.</summary>
        private OVRSpatialAnchor? anchor;
        private StagePlacement anchoredAs = StagePlacement.OnSurface;
        private SurfaceKind? anchoredOn;

        private Pose? published;

        /// <summary>Focused time the anchor has gone untracked, and where a moved anchor has held still since when.</summary>
        private float untrackedFor;
        private Pose? moving;
        private float stillSince;

        private bool focused = true;
        private int run;
        private bool accessDeclined;
        private bool askedAgain;

        /// <inheritdoc />
        public event Action? Changed;

        /// <summary>Raised on the main thread when <see cref="Status"/> changes.</summary>
        public event Action<RoomStatus>? StatusChanged;

        /// <inheritdoc />
        public Pose? Preferred { get; private set; }

        public RoomStatus Status { get; private set; } = RoomStatus.Initial;

        /// <summary>Whether the headset can run its space setup from the app: on a headset, not over a link or in the editor.</summary>
        public bool CanSetUpRoom => Application.platform == RuntimePlatform.Android;

        /// <summary>What the person can do now to put the characters on their desk, if anything.</summary>
        public RoomOffer Offer => Status.Offer(CanSetUpRoom);

        /// <summary>Shows the real room or the virtual space, and keeps the choice for the next session.</summary>
        public void SetSpace(RoomSpace space)
        {
            if (space == Status.Preferred) return;
            PlayerPrefs.SetString(SpaceKey, space.ToString());
            PlayerPrefs.Save();
            Log(space == RoomSpace.Room ? "the person chose the real room" : "the person chose the virtual space");
            SetStatus(Status.With(preferred: space));
            Apply();
        }

        /// <summary>Does what <see cref="Offer"/> offers: asks for room access again, or runs space setup.</summary>
        public void TakeOffer()
        {
            switch (Offer)
            {
                case RoomOffer.AllowRoomAccess:
                    _ = AskAgainAsync();
                    break;
                case RoomOffer.SetUpRoom:
                    _ = SetUpRoomAsync();
                    break;
            }
        }

        private void Awake()
        {
            root = new GameObject("Halcyonic room");
            passthrough = new PassthroughView(root);
            memory = PlacementMemory.Load(PlayerPrefs.GetString(MemoryKey, ""));
            accessDeclined = PlayerPrefs.GetInt(DeclinedKey, 0) == 1;
            var preferred = PlayerPrefs.GetString(SpaceKey, nameof(RoomSpace.Room)) == nameof(RoomSpace.Virtual) ? RoomSpace.Virtual : RoomSpace.Room;
            Status = RoomStatus.Initial.With(preferred: preferred);
        }

        private void Start()
        {
            controls = RoomControls.Create(root.transform, this);
            Apply();
        }

        private void OnDestroy()
        {
            run++;
            passthrough.Stop();
            if (root != null) Destroy(root);
            if (Preferred.HasValue)
            {
                Preferred = null;
                Changed?.Invoke();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) return;
            // Room access may have been allowed in the headset's settings meanwhile.
            if (Status.Shown == RoomSpace.Room && (Status.Scan is RoomScan.NoAccess or RoomScan.AccessOff) && HasRoomAccess())
            {
                Log("room access was allowed in the settings");
                Apply();
            }
            // A read while the headset was off the head, or before it found its surroundings again,
            // finds no room even where one is set up: read again when the person comes back.
            else if (Status.Shown == RoomSpace.Room && (Status.Scan is RoomScan.NotSetUp or RoomScan.OutsideRooms))
            {
                Log("reading the room again, since the last read found none");
                Apply();
            }
        }

        /// <summary>
        /// Focus comes and goes with system windows, sometimes many times a second; each change
        /// starts the anchor's watch afresh, so only a steady, focused view moves or clears the stage.
        /// </summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            focused = hasFocus;
            untrackedFor = 0f;
            moving = null;
        }

        /// <summary>Starts over for the space the person prefers, abandoning anything in progress.</summary>
        private void Apply()
        {
            var current = ++run;
            if (Status.Preferred == RoomSpace.Room) passthrough.Start(Time.unscaledTime);
            else passthrough.Stop();
            SetStatus(Status.With(passthrough: passthrough.State));
            if (Status.Shown == RoomSpace.Virtual)
            {
                if (passthrough.State == PassthroughState.Unavailable) Log("showing the virtual space, because passthrough is unavailable: " + passthrough.Problem);
                else Log("showing the virtual space");
                Clear("the virtual space is shown");
                SetStatus(Status.With(placement: StagePlacement.InFront, scan: RoomScan.NotRead, clearSurface: true));
                return;
            }
            Log("showing the real room");
            _ = PlaceAsync(current);
        }

        private bool Current(int attempt) => attempt == run && this != null;

        private async Task PlaceAsync(int attempt)
        {
            try
            {
                // Back from the virtual space, the surface this session already has stays.
                if (anchor != null && anchor.Localized)
                {
                    Publish(StageAnchors.PoseOf(anchor), "the real room is shown again");
                    SetStatus(Status.With(placement: anchoredAs, surface: anchoredOn, clearSurface: anchoredOn == null));
                    return;
                }
                SetStatus(Status.With(placement: StagePlacement.Searching, scan: RoomScan.NotRead));
                if (!await WaitForHeadAsync(attempt)) return;

                var access = HasRoomAccess();
                if (!access && !accessDeclined)
                {
                    SetStatus(Status.With(scan: RoomScan.AskingAccess));
                    controls?.Present();
                    if (!await WaitAsync(ExplainSeconds, attempt)) return;
                    Log("asking for access to the room's layout");
                    access = await AskRoomAccessAsync();
                    if (!Current(attempt)) return;
                    Log(access ? "room access allowed" : "room access declined");
                    if (!access) RememberDeclined(true);
                }

                RoomRead read;
                if (access)
                {
                    SetStatus(Status.With(scan: RoomScan.Reading));
                    mruk ??= RoomReader.Ensure(root.transform);
                    read = await RoomReader.ReadAsync(mruk, Head.position);
                    if (!Current(attempt)) return;
                }
                else
                {
                    read = RoomRead.Without(RoomScan.NoAccess, "no access");
                }
                Log("read the room: " + read.Scan + ", " + read.Detail);
                SetStatus(Status.With(scan: read.Scan));

                var viewer = ViewerNow();
                if (await RestoreAsync(read, viewer, attempt) || !Current(attempt)) return;

                if (read.Scan != RoomScan.Read || read.Surfaces == null || read.Obstacles == null || read.RoomId == null)
                {
                    Clear("the room's layout is not known");
                    SetStatus(Status.With(placement: StagePlacement.InFront, clearSurface: true));
                    controls?.Present();
                    return;
                }
                var spot = StageSurfaces.Choose(read.Surfaces, read.Obstacles, viewer);
                if (spot == null)
                {
                    Log("found no surface that fits the lineup in comfortable reach and view");
                    foreach (var line in StageSurfaces.Explain(read.Surfaces, read.Obstacles, viewer)) Log("surface: " + line);
                    Clear("the room has no suitable surface");
                    SetStatus(Status.With(placement: StagePlacement.NoSurface, clearSurface: true));
                    controls?.Present();
                    return;
                }
                var pose = PoseOf(spot, viewer);
                Log($"chose {(spot.Surface.Kind == SurfaceKind.Desk ? "a desk" : "other furniture")} {spot.Reach:0.00} m away, {Describe(spot.Turn)}, {spot.Drop:0.00} m below the eyes");
                DropAnchor();
                Publish(pose, "it chose a surface");
                SetStatus(Status.With(placement: StagePlacement.OnSurfaceThisSession, surface: spot.Surface.Kind));

                var (created, saved, detail) = await StageAnchors.CreateAsync(root.transform, pose, CreateSeconds);
                if (!Current(attempt))
                {
                    if (created != null) Destroy(created.gameObject);
                    return;
                }
                if (created == null)
                {
                    Log("the placement holds for this session only: no anchor, because " + detail);
                    return;
                }
                if (!saved)
                {
                    Adopt(created, StagePlacement.OnSurfaceThisSession, spot.Surface.Kind);
                    Log("the placement is anchored for this session only: the anchor was " + detail);
                    return;
                }
                Adopt(created, StagePlacement.OnSurface, spot.Surface.Kind);
                var dropped = memory.Remember(read.RoomId, created.Uuid.ToString("N"));
                SaveMemory();
                Log("kept the placement with a spatial anchor saved for this room");
                Erase(dropped);
                SetStatus(Status.With(placement: StagePlacement.OnSurface));
            }
            catch (Exception error)
            {
                if (!Current(attempt)) return;
                Debug.LogException(error);
                Clear("the room placement failed");
                SetStatus(Status.With(placement: StagePlacement.InFront, scan: RoomScan.Unavailable, clearSurface: true));
            }
        }

        /// <summary>
        /// Restores the placement saved for this room, or without a room the most recent one. Returns
        /// whether the stage now stands where the person left it. An anchor the headset no longer
        /// holds is forgotten; one that does not localize, or no longer suits the seat, is replaced
        /// when a new place is saved for the room.
        /// </summary>
        private async Task<bool> RestoreAsync(RoomRead read, Viewer viewer, int attempt)
        {
            var saved = read.RoomId != null ? memory.AnchorFor(read.RoomId) : memory.MostRecentAnchor;
            if (saved == null || !Guid.TryParseExact(saved, "N", out var uuid)) return false;
            var (restored, found, detail) = await StageAnchors.RestoreAsync(root.transform, uuid, RestoreSeconds);
            if (!Current(attempt))
            {
                if (restored != null) Destroy(restored.gameObject);
                return false;
            }
            if (!found)
            {
                memory.Forget(saved);
                SaveMemory();
            }
            if (restored == null)
            {
                Log("the saved placement was not restored: " + detail);
                return false;
            }
            var pose = StageAnchors.PoseOf(restored);
            if (!StageSurfaces.StillSuits(ToRoom(pose.position), viewer, read.Surfaces, read.Obstacles))
            {
                Log("the saved placement no longer suits where the person sits, so it is chosen again");
                Destroy(restored.gameObject);
                return false;
            }
            DropAnchor();
            var kind = SurfaceUnder(pose.position, read, viewer);
            Adopt(restored, StagePlacement.BackOnSurface, kind);
            if (read.RoomId != null) memory.Touch(read.RoomId);
            SaveMemory();
            Publish(pose, "it restored the placement saved for this room");
            SetStatus(Status.With(placement: StagePlacement.BackOnSurface, surface: kind, clearSurface: kind == null));
            return true;
        }

        private void Adopt(OVRSpatialAnchor adopted, StagePlacement placement, SurfaceKind? kind)
        {
            if (anchor != null && anchor != adopted) Destroy(anchor.gameObject);
            anchor = adopted;
            anchoredAs = placement;
            anchoredOn = kind;
            untrackedFor = 0f;
            moving = null;
        }

        private void Update()
        {
            var state = passthrough.Poll(Time.unscaledTime);
            if (state != Status.Passthrough)
            {
                Log("passthrough " + state + (state == PassthroughState.Unavailable ? ": " + passthrough.Problem : ""));
                SetStatus(Status.With(passthrough: state));
                if (state == PassthroughState.Unavailable) Apply();
            }
            FollowAnchor(Time.unscaledTime, Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Keeps the stage on the real surface by the anchor's tracked pose alone: follows it once it
        /// has moved and settled, as after a recenter; leaves the surface when it stays untracked; and
        /// returns when it is tracked again. Nothing happens without input focus.
        /// </summary>
        private void FollowAnchor(float now, float deltaTime)
        {
            if (anchor == null || !focused || Status.Shown != RoomSpace.Room) return;
            if (Status.Placement is not (StagePlacement.OnSurface or StagePlacement.OnSurfaceThisSession or StagePlacement.BackOnSurface or StagePlacement.LostSurface)) return;
            if (!anchor.IsTracked)
            {
                moving = null;
                // A long frame, as after a pause, is not time spent looking for the anchor.
                untrackedFor += Mathf.Min(deltaTime, 0.1f);
                if (Status.Placement != StagePlacement.LostSurface && untrackedFor > LostSeconds)
                {
                    Clear("its anchor has not been tracked for " + LostSeconds + " seconds");
                    SetStatus(Status.With(placement: StagePlacement.LostSurface));
                }
                return;
            }
            untrackedFor = 0f;
            var pose = StageAnchors.PoseOf(anchor);
            if (Status.Placement == StagePlacement.LostSurface)
            {
                moving = null;
                Publish(pose, "its anchor is tracked again");
                SetStatus(Status.With(placement: anchoredAs, surface: anchoredOn, clearSurface: anchoredOn == null));
                return;
            }
            if (!(published is Pose last) || !Differs(last, pose, MovedMeters, MovedDegrees))
            {
                moving = null;
                return;
            }
            if (!(moving is Pose held) || Differs(held, pose, StillMeters, StillDegrees))
            {
                moving = pose;
                stillSince = now;
                return;
            }
            if (now - stillSince < SettleSeconds) return;
            moving = null;
            var moved = Vector3.Distance(last.position, pose.position);
            Publish(pose, $"its anchor moved {moved:0.00} m with the room, as after a recenter");
        }

        private static bool Differs(Pose a, Pose b, float meters, float degrees) =>
            Vector3.Distance(a.position, b.position) > meters || Quaternion.Angle(a.rotation, b.rotation) > degrees;

        private void Publish(Pose pose, string? reason)
        {
            published = pose;
            Preferred = pose;
            if (reason != null) Log("placed the stage on the surface, because " + reason);
            Changed?.Invoke();
        }

        /// <summary>Returns the stage to its own placement in front of the person.</summary>
        private void Clear(string reason)
        {
            if (!Preferred.HasValue) return;
            Preferred = null;
            Log("the stage stands in front of the person, because " + reason);
            Changed?.Invoke();
        }

        private void DropAnchor()
        {
            if (anchor != null) Destroy(anchor.gameObject);
            anchor = null;
            published = null;
        }

        private async Task AskAgainAsync()
        {
            if (askedAgain)
            {
                SetStatus(Status.With(scan: RoomScan.AccessOff));
                return;
            }
            askedAgain = true;
            Log("asking again for access to the room's layout");
            var granted = await AskRoomAccessAsync();
            if (this == null) return;
            Log(granted ? "room access allowed" : "room access declined again");
            if (granted)
            {
                RememberDeclined(false);
                Apply();
            }
            else
            {
                SetStatus(Status.With(scan: RoomScan.AccessOff));
            }
        }

        private async Task SetUpRoomAsync()
        {
            Log("starting the headset's space setup");
            bool completed;
            try
            {
                // The app pauses while the person sets up the space, and resumes when they finish or cancel.
                completed = await OVRScene.RequestSpaceSetup();
            }
            catch (Exception error)
            {
                completed = false;
                Log("space setup could not start: " + error.GetType().Name);
            }
            if (this == null) return;
            Log(completed ? "space setup ended" : "space setup did not run");
            // The room may be new or changed: read it and choose again, keeping nothing chosen from the old scan.
            DropAnchor();
            Clear("the room is being read again");
            Apply();
        }

        private static bool HasRoomAccess()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission);
#else
            return true;
#endif
        }

        /// <summary>
        /// Asks for the spatial data permission. The headset explains it once, then asks. If its dialog
        /// was already open, the request is dropped without an answer, so the permission is also polled.
        /// </summary>
        private async Task<bool> AskRoomAccessAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => answer.TrySetResult(true);
            callbacks.PermissionDenied += _ => answer.TrySetResult(false);
            Permission.RequestUserPermission(OVRPermissionsRequester.ScenePermission, callbacks);
            while (!answer.Task.IsCompleted)
            {
                if (HasRoomAccess()) return true;
                await Task.Yield();
            }
            return await answer.Task;
#else
            await Task.Yield();
            return true;
#endif
        }

        private void RememberDeclined(bool declined)
        {
            accessDeclined = declined;
            PlayerPrefs.SetInt(DeclinedKey, declined ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void SaveMemory()
        {
            PlayerPrefs.SetString(MemoryKey, memory.Save());
            PlayerPrefs.Save();
        }

        private void Erase(IReadOnlyList<string> anchors)
        {
            var uuids = new List<Guid>();
            foreach (var id in anchors)
            {
                if (Guid.TryParseExact(id, "N", out var uuid)) uuids.Add(uuid);
            }
            if (uuids.Count > 0) _ = EraseAsync(uuids);
        }

        private async Task EraseAsync(List<Guid> uuids) => Log(await StageAnchors.EraseAsync(uuids));

        private void SetStatus(RoomStatus next)
        {
            if (next.Preferred == Status.Preferred && next.Passthrough == Status.Passthrough && next.Scan == Status.Scan &&
                next.Placement == Status.Placement && next.Surface == Status.Surface)
            {
                return;
            }
            Status = next;
            StatusChanged?.Invoke(next);
        }

        /// <summary>Waits until the head is tracked, or a few seconds; false if this attempt was abandoned meanwhile.</summary>
        private async Task<bool> WaitForHeadAsync(int attempt)
        {
            var deadline = Time.unscaledTime + HeadTrackingSeconds;
            while (!HeadTracked() && Time.unscaledTime < deadline)
            {
                await Task.Yield();
                if (!Current(attempt)) return false;
            }
            return Current(attempt);
        }

        private async Task<bool> WaitAsync(float seconds, int attempt)
        {
            var until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until)
            {
                await Task.Yield();
                if (!Current(attempt)) return false;
            }
            return true;
        }

        private static bool HeadTracked()
        {
            if (!XRSettings.isDeviceActive) return true;
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid) return false;
            // A headset that does not report whether it is tracked is taken as tracked.
            return !head.TryGetFeatureValue(CommonUsages.isTracked, out var tracked) || tracked;
        }

        /// <summary>The person's eyes: the main camera, or a seated eye height at the origin without one.</summary>
        private static Transform? HeadTransform => Camera.main != null ? Camera.main.transform : null;

        private static Pose Head => HeadTransform is Transform head
            ? new Pose(head.position, head.rotation)
            : new Pose(new Vector3(0f, 1.2f, 0f), Quaternion.identity);

        /// <summary>Where the person's eyes are and which way they face on the level, even looking straight down.</summary>
        private static Viewer ViewerNow()
        {
            var head = Head;
            var forward = head.rotation * Vector3.forward;
            var level = new Vector3(forward.x, 0f, forward.z);
            if (level.sqrMagnitude < 0.01f)
            {
                // Looking straight down, the top of the head points where the face would; straight up, the other way.
                var up = head.rotation * Vector3.up;
                level = new Vector3(up.x, 0f, up.z) * (forward.y < 0f ? 1f : -1f);
            }
            if (level.sqrMagnitude < 1e-6f) level = Vector3.forward;
            return new Viewer(ToRoom(head.position), new PlanPoint(level.x, level.z));
        }

        private static RoomPoint ToRoom(Vector3 position) => new RoomPoint(position.x, position.y, position.z);

        /// <summary>The spot's point, turned to face away from the person, as the stage reads a pose.</summary>
        private static Pose PoseOf(StageSpot spot, Viewer viewer)
        {
            var position = new Vector3(spot.Point.X, spot.Point.Y, spot.Point.Z);
            var away = spot.Point.Plan - viewer.Eyes.Plan;
            var rotation = Quaternion.LookRotation(new Vector3(away.X, 0f, away.Z).normalized, Vector3.up);
            return new Pose(position, rotation);
        }

        /// <summary>What a kept placement stands on, when the room's layout is known.</summary>
        private static SurfaceKind? SurfaceUnder(Vector3 position, RoomRead read, Viewer viewer)
        {
            if (read.Surfaces == null) return null;
            SurfaceKind? found = null;
            foreach (var surface in read.Surfaces)
            {
                if (Mathf.Abs(surface.Height - position.y) > 0.05f) continue;
                if (StageSurfaces.LineupFits(surface, Array.Empty<RoomObstacle>(), viewer, new PlanPoint(position.x, position.z), 0f))
                {
                    if (surface.Kind == SurfaceKind.Desk) return SurfaceKind.Desk;
                    found = surface.Kind;
                }
            }
            return found;
        }

        private static string Describe(float turn) =>
            Mathf.Abs(turn) < 0.5f ? "straight ahead" : $"{Mathf.Abs(turn):0} degrees {(turn > 0f ? "right" : "left")}";

        /// <summary>
        /// One line to the log without a stack trace. It says what happened and why, with distances
        /// and angles, and never an anchor or room id, a position, or anything the person said.
        /// </summary>
        private void Log(string message) =>
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this, "Halcyonic: room {0}", message);
    }
}

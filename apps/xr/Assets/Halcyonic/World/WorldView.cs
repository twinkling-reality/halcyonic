#nullable enable
using System.Collections.Generic;
using System.Threading;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.Room;
using UnityEngine;
using UnityEngine.XR;

namespace Halcyonic.XR.World
{
    /// <summary>
    /// A person's world, shown where the headset's Virtual Space was the engine's stock sky (ADR 0049): a picture
    /// rendered offline all round the person, on a shell shaped by its depth, with its dusk and night and one place's
    /// light mixed live. It shows only while the room's placement says the virtual space is shown and the build holds
    /// the world's pictures; it stands where the person is when it comes and after they reset their view, and it
    /// arrives by opening outward from them. The night follows the person's world as the Mac last read it (a spoken
    /// change moves it), and the place lights while an agent works in the project the world gives it to. Nothing
    /// from the recorded demonstration moves it: what is shown is never a canned change.
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        /// <summary>Where the world's pictures are in the build, under Resources.</summary>
        public const string PicturesPath = "HalcyonicWorld/Pictures/";

        /// <summary>The depth's size, as the pictures' folder holds it: distances in meters, 32-bit floats, top row first.</summary>
        public const int DepthWidth = 512;

        public const int DepthHeight = 256;

        /// <summary>How long the world takes to arrive, in seconds.</summary>
        public const float ArrivalSeconds = 9f;

        /// <summary>How far the world has opened when it has fully arrived, in meters: past its farthest point.</summary>
        public const float Arrived = 2400f;

        /// <summary>How long the night takes to follow a change, in seconds: long enough to be seen happening.</summary>
        public const float TurnSeconds = 6f;

        /// <summary>Where the render's eyes were above the world's ground, in meters.</summary>
        public const float Eyes = 1.25f;

        private static readonly int NightId = Shader.PropertyToID("_WorldNight");
        private static readonly int RevealId = Shader.PropertyToID("_WorldReveal");
        private static readonly int PanoDuskId = Shader.PropertyToID("_PanoDusk");
        private static readonly int PanoNightId = Shader.PropertyToID("_PanoNight");
        private static readonly int PanoDuskPlaceId = Shader.PropertyToID("_PanoDuskPlace");
        private static readonly int PanoNightPlaceId = Shader.PropertyToID("_PanoNightPlace");
        private static readonly int PanoPlaceOnId = Shader.PropertyToID("_PanoPlaceOn");

        private readonly List<XRInputSubsystem> found = new List<XRInputSubsystem>();
        private readonly List<XRInputSubsystem> heard = new List<XRInputSubsystem>();
        private int resets;
        private RoomPlacement? placement;
        private ControlPlaneConnection? connection;

        /// <summary>The control plane's state changed since it was last read: it is read only then, never every frame.</summary>
        private bool stale = true;
        private float nextSearch;
        private Transform? parts;
        private Material? panorama;
        private WorldSoundPlayer? sound;
        private bool shown;
        private bool missing;
        private float shownAt;
        private float night;
        private float nightWanted;
        private float placeOn;
        private float placeWanted;

        /// <summary>Whether the world's parts are made and showing.</summary>
        public bool Ready => parts != null && parts.gameObject.activeSelf;

        /// <summary>How far night has come now, 0 to 1.</summary>
        public float Night => night;

        private void Update()
        {
            var now = Time.unscaledTime;
            if (now >= nextSearch)
            {
                // The room's placement and the connection are made a few frames after the scene loads.
                nextSearch = now + 1f;
                if (placement == null) placement = FindAnyObjectByType<RoomPlacement>();
                if (connection == null)
                {
                    connection = FindAnyObjectByType<ControlPlaneConnection>();
                    if (connection != null) connection.Changed += OnStateChanged;
                }
                Listen();
            }
            // The person reset their view: the world stands where they are now, as the stage does.
            if (Interlocked.Exchange(ref resets, 0) > 0 && shown) Stand();
            var wanted = placement != null && placement.Status.Shown == RoomSpace.Virtual;
            if (wanted != shown) Show(wanted, now);
            if (sound == null) sound = gameObject.AddComponent<WorldSoundPlayer>();
            sound.Showing = shown && parts != null;
            if (!shown || parts == null) return;
            if (stale)
            {
                stale = false;
                Follow();
            }
            night = Mathf.MoveTowards(night, nightWanted, Time.unscaledDeltaTime / TurnSeconds);
            placeOn = Mathf.MoveTowards(placeOn, placeWanted, Time.unscaledDeltaTime / 1.5f);
            var arrived = Mathf.Clamp01((now - shownAt) / ArrivalSeconds);
            Apply(Smooth(night), Smooth(placeOn), Unrolled(arrived));
            sound.Night = Smooth(night);
            sound.Place = placeOn;
        }

        /// <summary>
        /// Reads what the world should be from the control plane's state: the person's world project's night, and
        /// whether an agent is at work in the project it gives a place to. While the recorded demonstration is shown,
        /// or nothing is connected, the world stays as its pictures were rendered.
        /// </summary>
        private void Follow()
        {
            var session = connection != null && connection.DemonstrationLine == null ? connection.Session : null;
            if (session == null) return;
            var projects = session.State.Projects.Values;
            var world = WorldOf.Project(projects);
            if (world?.World?.Description == null) return;
            var mood = WorldOf.Read(world.World.Description, projects, out _);
            if (mood == null) return;
            nightWanted = (float)mood.Night;
            var working = false;
            if (mood.PlaceProjectIds.Count > 0)
            {
                var place = mood.PlaceProjectIds[0];
                foreach (var execution in session.State.Executions.Values)
                {
                    if (execution.Status != ExecutionStatus.Running) continue;
                    if (session.State.Workstreams.TryGetValue(execution.WorkstreamId, out var workstream) && workstream.ProjectId == place)
                    {
                        working = true;
                        break;
                    }
                }
            }
            placeWanted = working ? 1f : 0f;
        }

        /// <summary>How far the world has opened, in meters, a part of the way through its arrival: slowly near the person first.</summary>
        public static float Unrolled(float part)
        {
            part = Mathf.Clamp01(part);
            return Arrived * part * part * part;
        }

        private void Show(bool wanted, float now)
        {
            shown = wanted;
            stale = true;
            if (wanted)
            {
                shownAt = now;
                Stand();
                if (parts == null && !missing) BuildFromResources();
            }
            if (parts != null) parts.gameObject.SetActive(wanted);
        }

        private void BuildFromResources()
        {
            var dusk = Resources.Load<Texture2D>(PicturesPath + "dusk");
            var nightPicture = Resources.Load<Texture2D>(PicturesPath + "night");
            if (dusk == null || nightPicture == null)
            {
                // A build without the world's pictures keeps the stock Virtual Space, as before.
                missing = true;
                Debug.Log("Halcyonic: this build holds no world pictures, so Virtual Space stays as it was.");
                return;
            }
            var depthAsset = Resources.Load<TextAsset>(PicturesPath + "depth");
            float[]? depth = null;
            if (depthAsset != null && depthAsset.bytes.Length == DepthWidth * DepthHeight * 4)
            {
                var top = new float[DepthWidth * DepthHeight];
                System.Buffer.BlockCopy(depthAsset.bytes, 0, top, 0, depthAsset.bytes.Length);
                depth = new float[top.Length];
                for (var row = 0; row < DepthHeight; row++) System.Array.Copy(top, row * DepthWidth, depth, (DepthHeight - 1 - row) * DepthWidth, DepthWidth);
            }
            BuildPanoramaNow(dusk, nightPicture, Resources.Load<Texture2D>(PicturesPath + "dusk-place"), Resources.Load<Texture2D>(PicturesPath + "night-place"),
                depth, depth == null ? 0 : DepthWidth, depth == null ? 0 : DepthHeight);
        }

        /// <summary>
        /// Shows a world as it was rendered for the person: its dusk and night pictures, each also with its one place
        /// lit, on a shell shaped by the render's depth and centred on the eyes the render was made from.
        /// </summary>
        public void BuildPanoramaNow(Texture dusk, Texture nightPicture, Texture? duskPlace, Texture? nightPlace, float[]? depth, int depthWidth, int depthHeight)
        {
            var root = new GameObject("Halcyonic world parts").transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(0f, Eyes, 0f);
            parts = root;
            panorama = Part(root, "Shell", WorldPanorama.Shell(depth, depthWidth, depthHeight));
            if (panorama == null) return;
            // A far sphere behind the shell, sharing its material, so a crack where the depth steps shows the right picture.
            if (depth != null)
            {
                var backstop = new GameObject("Halcyonic world backstop");
                backstop.transform.SetParent(root, false);
                backstop.AddComponent<MeshFilter>().sharedMesh = WorldPanorama.Shell(null, 0, 0, 128, 64);
                Quiet(backstop.AddComponent<MeshRenderer>()).sharedMaterial = panorama;
            }
            panorama.SetTexture(PanoDuskId, dusk);
            panorama.SetTexture(PanoNightId, nightPicture);
            panorama.SetTexture(PanoDuskPlaceId, duskPlace != null ? duskPlace : dusk);
            panorama.SetTexture(PanoNightPlaceId, nightPlace != null ? nightPlace : nightPicture);
            shown = true;
        }

        private static Material? Part(Transform root, string name, Mesh mesh)
        {
            var shader = Resources.Load<Shader>("HalcyonicWorld/WorldPanorama");
            if (shader == null)
            {
                Debug.LogError("Halcyonic: the world's shader is missing from the build, so the world is not drawn.");
                return null;
            }
            var part = new GameObject("Halcyonic world " + name);
            part.transform.SetParent(root, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            Quiet(part.AddComponent<MeshRenderer>()).sharedMaterial = material;
            return material;
        }

        private static MeshRenderer Quiet(MeshRenderer renderer)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return renderer;
        }

        /// <summary>
        /// Stands the world where the person is, facing where they face on the level, its eyes at theirs: the picture
        /// was rendered from one point, so the person's eyes are put there.
        /// </summary>
        private void Stand()
        {
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head == null) return;
            var ahead = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (ahead.sqrMagnitude < 0.01f) ahead = Vector3.ProjectOnPlane(head.up, Vector3.up);
            var rotation = ahead.sqrMagnitude < 0.0001f ? Quaternion.identity : Quaternion.LookRotation(ahead.normalized, Vector3.up);
            transform.SetPositionAndRotation(head.position - Vector3.up * Eyes, rotation);
        }

        /// <summary>Gives the world's shader one moment: how far night has come, how far the place is lit, and how far the world has opened.</summary>
        public void Apply(float nightNow, float placeNow, float unrolled)
        {
            Shader.SetGlobalFloat(NightId, Mathf.Clamp01(nightNow));
            Shader.SetGlobalFloat(RevealId, unrolled);
            if (panorama != null) panorama.SetFloat(PanoPlaceOnId, Mathf.Clamp01(placeNow));
        }

        /// <summary>Hears the headset's reference space change, as when the person resets their view.</summary>
        private void Listen()
        {
            SubsystemManager.GetSubsystems(found);
            foreach (var subsystem in found)
            {
                if (heard.Contains(subsystem)) continue;
                subsystem.trackingOriginUpdated += OnReset;
                heard.Add(subsystem);
            }
        }

        // Only counts: the event's thread is not documented, and Update acts on it.
        private void OnReset(XRInputSubsystem subsystem) => Interlocked.Increment(ref resets);

        // Raised on the main thread after received messages were applied, or another session is shown.
        private void OnStateChanged(StateChanges changes) => stale = true;

        private static float Smooth(float x) => x * x * (3f - 2f * x);

        private void OnDestroy()
        {
            if (connection != null) connection.Changed -= OnStateChanged;
            foreach (var subsystem in heard) subsystem.trackingOriginUpdated -= OnReset;
            heard.Clear();
        }
    }
}

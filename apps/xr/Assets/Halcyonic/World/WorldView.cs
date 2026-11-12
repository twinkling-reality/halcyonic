#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.Room;
using UnityEngine;
using UnityEngine.XR;

namespace Halcyonic.XR.World
{
    /// <summary>
    /// A person's world, shown where the headset's Virtual Space was the engine's stock sky (ADR 0049): its scene's
    /// layers, rendered offline, mixed by the person's own description into one panorama all round them, on a shell
    /// shaped by its depth. Every member of the description moves something: each light's brightness and tint, how
    /// far night has come, the air, and each place's own light while its project's agent works. A spoken change eases
    /// in over seconds with no new render. It shows only while the room's placement says the virtual space is shown
    /// and the build holds the scene's pictures; it stands where the person is when it comes and after they reset
    /// their view, and it arrives by opening outward from them. Nothing from the recorded demonstration moves it.
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        /// <summary>How long the world takes to arrive, in seconds.</summary>
        public const float ArrivalSeconds = 9f;

        /// <summary>How far the world has opened when it has fully arrived, in meters: past its farthest point.</summary>
        public const float Arrived = 2400f;

        /// <summary>How long a change takes to ease in, in seconds: long enough to be seen happening.</summary>
        public const float TurnSeconds = 6f;

        /// <summary>How long a place takes to light or go dark, in seconds.</summary>
        public const float PlaceSeconds = 1.5f;

        /// <summary>Where the render's eyes were above the world's ground, in meters.</summary>
        public const float Eyes = 1.25f;

        private const int LightSlots = 8;

        private static readonly int RevealId = Shader.PropertyToID("_WorldReveal");
        private static readonly int MixedId = Shader.PropertyToID("_PanoMixed");
        private static readonly int LightWeightsId = Shader.PropertyToID("_LW");
        private static readonly int PlaceWeightsId = Shader.PropertyToID("_PW");
        private static readonly int AirDayId = Shader.PropertyToID("_AirDayW");
        private static readonly int AirNightId = Shader.PropertyToID("_AirNightW");
        private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");

        private readonly List<XRInputSubsystem> found = new List<XRInputSubsystem>();
        private readonly List<XRInputSubsystem> heard = new List<XRInputSubsystem>();
        private readonly Vector4[] lightWeights = new Vector4[LightSlots];
        private readonly Vector4[] placeWeights = new Vector4[WorldOf.MaxPlaces];
        private int resets;
        private RoomPlacement? placement;
        private ControlPlaneConnection? connection;

        /// <summary>The control plane's state changed since it was last read: it is read only then, never every frame.</summary>
        private bool stale = true;
        private float nextSearch;
        private Transform? parts;
        private Material? panorama;
        private Material? mixer;
        private RenderTexture? mixed;
        private WorldPictures? pictures;
        private WorldSoundPlayer? sound;
        private bool shown;
        private bool missing;
        private float shownAt;

        // The mix now, the one it eases from and the one it eases to: one value per light channel, the night, the air.
        private float[] now = new float[0];
        private float[] from = new float[0];
        private float[] to = new float[0];
        private float turnedAt = -999f;
        private float[] placeOn = new float[0];
        private float[] placeWanted = new float[0];
        private bool dirty = true;

        /// <summary>Whether the world's parts are made and showing.</summary>
        public bool Ready => parts != null && parts.gameObject.activeSelf;

        /// <summary>How far night has come now, 0 to 1.</summary>
        public float Night => now.Length > LightSlots * 3 ? now[LightSlots * 3] : 0f;

        /// <summary>The scene shown, or null.</summary>
        public WorldScene? Scene => pictures?.Scene;

        private void Update()
        {
            var time = Time.unscaledTime;
            if (time >= nextSearch)
            {
                // The room's placement and the connection are made a few frames after the scene loads.
                nextSearch = time + 1f;
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
            if (wanted != shown) Show(wanted, time);
            if (sound == null) sound = gameObject.AddComponent<WorldSoundPlayer>();
            sound.Showing = shown && parts != null;
            if (!shown || parts == null || pictures == null) return;
            if (stale)
            {
                stale = false;
                Follow();
            }
            Ease(time, Time.unscaledDeltaTime);
            Shader.SetGlobalFloat(RevealId, Unrolled(Mathf.Clamp01((time - shownAt) / ArrivalSeconds)));
            if (dirty) Mix();
            sound.Night = Smooth(Night);
            var brightest = 0f;
            foreach (var on in placeOn) brightest = Mathf.Max(brightest, on);
            sound.Place = brightest;
        }

        /// <summary>
        /// Reads what the world should be from the control plane's state: the person's world project's description, in
        /// the scene shown, and which of its places' projects an agent is at work in. While the recorded demonstration is
        /// shown, or nothing is connected, the world stays as it is; a description that breaks a rule is not used.
        /// </summary>
        private void Follow()
        {
            var session = connection != null && connection.DemonstrationLine == null ? connection.Session : null;
            if (session == null || pictures == null) return;
            var projects = session.State.Projects.Values;
            var world = WorldOf.Project(projects);
            if (world?.World?.Description == null) return;
            var mix = WorldOf.Read(world.World.Description, pictures.Scene, projects, out var why);
            if (mix == null)
            {
                // Only which rule, by the contract's own member names: never a word of the description.
                Debug.Log("Halcyonic: the world's description was not used (" + why + "), so the world stays as it was.");
                return;
            }
            TurnTo(Target(mix), Time.unscaledTime);
            var working = new HashSet<string>();
            foreach (var execution in session.State.Executions.Values)
            {
                if (execution.Status != ExecutionStatus.Running) continue;
                if (session.State.Workstreams.TryGetValue(execution.WorkstreamId, out var workstream)) working.Add(workstream.ProjectId);
            }
            for (var p = 0; p < pictures.Scene.Places.Count; p++)
            {
                var lit = false;
                foreach (var place in mix.Places)
                {
                    if (place.Place == pictures.Scene.Places[p].Place && working.Contains(place.ProjectId)) lit = true;
                }
                placeWanted[p] = lit ? 1f : 0f;
            }
        }

        /// <summary>A mix as numbers the world eases between: each light's red, green and blue weight, then the night, the air's thickness, and its tint's red, green and blue.</summary>
        private float[] Target(WorldMix mix)
        {
            var slots = new float[LightSlots * 3 + 5];
            for (var i = 0; i < mix.Lights.Count && i < LightSlots; i++)
            {
                var light = mix.Lights[i];
                slots[i * 3] = (float)(light.Brightness * light.Red);
                slots[i * 3 + 1] = (float)(light.Brightness * light.Green);
                slots[i * 3 + 2] = (float)(light.Brightness * light.Blue);
            }
            slots[LightSlots * 3] = (float)mix.Night;
            slots[LightSlots * 3 + 1] = (float)mix.HazeThickness;
            slots[LightSlots * 3 + 2] = (float)mix.HazeRed;
            slots[LightSlots * 3 + 3] = (float)mix.HazeGreen;
            slots[LightSlots * 3 + 4] = (float)mix.HazeBlue;
            return slots;
        }

        /// <summary>The scene as it was made: every light as made, dusk, the air as made.</summary>
        private float[] AsMade()
        {
            var slots = new float[LightSlots * 3 + 5];
            for (var i = 0; i < LightSlots * 3; i++) slots[i] = 1f;
            slots[LightSlots * 3] = 0f;
            for (var i = 1; i <= 4; i++) slots[LightSlots * 3 + i] = 1f;
            return slots;
        }

        private void TurnTo(float[] target, float time)
        {
            var same = to.Length == target.Length;
            for (var i = 0; same && i < target.Length; i++) same = Mathf.Approximately(to[i], target[i]);
            if (same) return;
            from = (float[])now.Clone();
            to = target;
            turnedAt = time;
            dirty = true;
        }

        private void Ease(float time, float delta)
        {
            if (now.Length == to.Length && turnedAt + TurnSeconds >= time - delta)
            {
                var part = Smooth(Mathf.Clamp01((time - turnedAt) / TurnSeconds));
                for (var i = 0; i < now.Length; i++) now[i] = Mathf.Lerp(from[i], to[i], part);
                dirty = true;
            }
            for (var p = 0; p < placeOn.Length; p++)
            {
                var next = Mathf.MoveTowards(placeOn[p], placeWanted[p], delta / PlaceSeconds);
                if (next != placeOn[p]) dirty = true;
                placeOn[p] = next;
            }
        }

        /// <summary>How far the world has opened, in meters, a part of the way through its arrival: slowly near the person first.</summary>
        public static float Unrolled(float part)
        {
            part = Mathf.Clamp01(part);
            return Arrived * part * part * part;
        }

        private void Show(bool wanted, float time)
        {
            shown = wanted;
            stale = true;
            if (wanted)
            {
                shownAt = time;
                Stand();
                if (parts == null && !missing) BuildFromBuild();
            }
            if (parts != null) parts.gameObject.SetActive(wanted);
        }

        private void BuildFromBuild()
        {
            // The scene the person's world names, when the build holds it; before any is read, the first the build holds.
            var session = connection != null && connection.DemonstrationLine == null ? connection.Session : null;
            var named = session == null ? null : WorldOf.Project(session.State.Projects.Values)?.World?.Description?.Scene;
            var scenes = WorldPictures.InBuild();
            var withPictures = scenes.Where(WorldPictures.PicturesInBuild).ToList();
            var scene = named != null && withPictures.Contains(named) ? named : withPictures.Count > 0 ? withPictures[0] : null;
            missing = true;
            if (scene == null)
            {
                // A build without a scene's pictures keeps the stock Virtual Space, as before.
                Debug.Log("Halcyonic: this build holds no world scene to show, so Virtual Space stays as it was.");
                return;
            }
            var made = WorldPictures.FromBuild(scene, out var why);
            if (made == null)
            {
                // A scene whose folder does not match its manifest is never drawn, and never drawn black.
                Debug.LogError("Halcyonic: the build's world scene " + scene + " does not match its manifest (" + why + "), so it is not shown.");
                return;
            }
            missing = false;
            Debug.Log("Halcyonic: the world shows scene " + scene + " from the build's " + WorldPictures.ScenesPath + scene + ".");
            BuildNow(made);
        }

        /// <summary>Shows a scene's pictures, mixed as the scene was made until a description says otherwise.</summary>
        public void BuildNow(WorldPictures made)
        {
            pictures = made;
            var root = new GameObject("Halcyonic world parts").transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(0f, Eyes, 0f);
            parts = root;
            var mixShader = Resources.Load<Shader>("HalcyonicWorld/WorldMix");
            var shellShader = Resources.Load<Shader>("HalcyonicWorld/WorldPanorama");
            if (mixShader == null || shellShader == null)
            {
                Debug.LogError("Halcyonic: the world's shaders are missing from the build, so the world is not drawn.");
                return;
            }
            mixer = new Material(mixShader) { hideFlags = HideFlags.DontSave };
            var any = made.Lights.Count > 0 ? made.Lights[made.Scene.Lights[0].Light] : made.AirDay;
            mixed = new RenderTexture(any.width, any.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                useMipMap = true,
                autoGenerateMips = true,
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                hideFlags = HideFlags.DontSave,
            };
            mixed.Create();
            for (var i = 0; i < made.Scene.Lights.Count && i < LightSlots; i++) mixer.SetTexture("_L" + i, made.Lights[made.Scene.Lights[i].Light]);
            if (made.Places0 != null) mixer.SetTexture("_P0", made.Places0);
            if (made.Places1 != null) mixer.SetTexture("_P1", made.Places1);
            mixer.SetTexture("_AirDay", made.AirDay);
            mixer.SetTexture("_AirNight", made.AirNight);
            mixer.SetTexture("_View", made.View);
            panorama = new Material(shellShader) { hideFlags = HideFlags.DontSave };
            panorama.SetTexture(MixedId, mixed);
            Part(root, "Shell", WorldPanorama.Shell(made.Depth, made.Depth == null ? 0 : WorldPictures.DepthWidth, made.Depth == null ? 0 : WorldPictures.DepthHeight));
            // A far sphere behind the shell, sharing its material, so a crack where the depth steps shows the right picture.
            if (made.Depth != null) Part(root, "Backstop", WorldPanorama.Shell(null, 0, 0, 128, 64));
            now = AsMade();
            from = AsMade();
            to = AsMade();
            placeOn = new float[made.Scene.Places.Count];
            placeWanted = new float[made.Scene.Places.Count];
            dirty = true;
            shown = true;
            Mix();
        }

        /// <summary>Mixes a description into the world at once, with the places lit as given and no easing: for the Editor's renders.</summary>
        public void MixNow(WorldMix mix, IReadOnlyCollection<string> lit, float unrolled)
        {
            if (pictures == null) return;
            now = Target(mix);
            to = (float[])now.Clone();
            from = (float[])now.Clone();
            for (var p = 0; p < pictures.Scene.Places.Count; p++) placeOn[p] = lit.Contains(pictures.Scene.Places[p].Place) ? 1f : 0f;
            Shader.SetGlobalFloat(RevealId, unrolled);
            Mix();
        }

        /// <summary>Makes the panorama again from the layers, as the mix now says.</summary>
        private void Mix()
        {
            dirty = false;
            if (pictures == null || mixer == null || mixed == null) return;
            var night = now[LightSlots * 3];
            var index = pictures.Index;
            for (var i = 0; i < LightSlots; i++)
            {
                if (i >= pictures.Scene.Lights.Count)
                {
                    lightWeights[i] = Vector4.zero;
                    continue;
                }
                var light = pictures.Scene.Lights[i];
                var w = (float)WorldOf.Shining(light.When, night) * index.Lights[light.Light];
                lightWeights[i] = new Vector4(now[i * 3] * w, now[i * 3 + 1] * w, now[i * 3 + 2] * w, 0f);
            }
            for (var p = 0; p < WorldOf.MaxPlaces; p++)
            {
                if (p >= pictures.Scene.Places.Count)
                {
                    placeWeights[p] = Vector4.zero;
                    continue;
                }
                var colour = index.PlaceColours[pictures.Scene.Places[p].Place];
                var w = placeOn[p] * index.Places[pictures.Scene.Places[p].Place];
                placeWeights[p] = new Vector4(colour[0] * w, colour[1] * w, colour[2] * w, 0f);
            }
            mixer.SetVectorArray(LightWeightsId, lightWeights);
            mixer.SetVectorArray(PlaceWeightsId, placeWeights);
            var thickness = now[LightSlots * 3 + 1];
            var tint = new Vector3(now[LightSlots * 3 + 2], now[LightSlots * 3 + 3], now[LightSlots * 3 + 4]) * (thickness * index.Air);
            mixer.SetVector(AirDayId, tint * (float)WorldOf.Shining(WorldLightWhen.Day, night));
            mixer.SetVector(AirNightId, tint * night);
            mixer.SetFloat(ThicknessId, thickness);
            Graphics.Blit(null, mixed, mixer);
        }

        private void Part(Transform root, string name, Mesh mesh)
        {
            var part = new GameObject("Halcyonic world " + name);
            part.transform.SetParent(root, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.sharedMaterial = panorama;
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
            if (mixed != null) mixed.Release();
            Gone(mixed);
            Gone(mixer);
            Gone(panorama);
        }

        // The Editor's renders make and remove the world outside play.
        private static void Gone(Object? thing)
        {
            if (thing == null) return;
            if (Application.isPlaying) Destroy(thing);
            else DestroyImmediate(thing);
        }
    }
}

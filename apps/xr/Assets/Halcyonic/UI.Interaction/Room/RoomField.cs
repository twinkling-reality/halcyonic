#nullable enable
using System;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The working field (DESIGN's BUILD-SPEC-v4 2.3): while the agent works, light points over a soft lifted ground stand
    /// round the working character and the place its output will take, fading to nothing at the field's edge. One
    /// procedural quad (<c>Halcyonic/Room Field</c>), no texture and no mesh of points: a 9 dp grid of 3.0 dp points, one in
    /// twelve lit at 4.6 dp, each twinkling over its own 1.8 to 3.6 s with a band of light crossing every 6 s, a hairline
    /// grid every 36 dp under them, and a dark lift under a white lift so the points read over a white wall. Its size is
    /// its owner's to give (<see cref="Around"/> grows what it surrounds as the design does); it stands centred on its
    /// transform. It fades in and out over 600 ms (<see cref="RoomMotion.FieldArrives"/>). A frame sets two numbers on one
    /// property block and allocates nothing. Kept still (<see cref="GlazeMotion.Still"/>), its clock stops and it is there
    /// or gone at once. Lengths are dp on the plane, each 1 dp long at the plane's middle.
    /// </summary>
    public sealed class RoomField : MonoBehaviour
    {
        private enum Phase
        {
            Hidden,
            Arriving,
            Standing,
            Leaving,
        }

        /// <summary>The points' grid, a point's and a lit point's width, and the share of points lit: about one in twelve.</summary>
        public const float PitchDp = 9f;
        public const float PointDp = 3f;
        public const float LitDp = 4.6f;
        public const float LitShare = 0.08f;

        /// <summary>The hairline grid: 1 dp lines every 36 dp, white at 7 percent.</summary>
        public const float GridDp = 36f;
        public const float GridAlpha = 0.07f;

        /// <summary>Points and lines fade in size and brightness over this share of the way in from the edge.</summary>
        public const float FadeShare = 0.55f;

        /// <summary>The band of light crosses the field once in this many seconds.</summary>
        public const float BandSeconds = 6f;

        /// <summary>The field reaches past what it surrounds: 30 percent across and 50 percent up and down, and the page's 40 px more above and below.</summary>
        public const float SpillAcross = 0.30f;
        public const float SpillUpDown = 0.50f;
        public const float SpillMoreDp = 42.4f;

        private const string MaterialPath = "HalcyonicUI/RoomField";
        private static readonly int FieldId = Shader.PropertyToID("_Field");
        private static readonly int ClockId = Shader.PropertyToID("_Clock");
        private static readonly int ShownId = Shader.PropertyToID("_Shown");
        private static Material? material;
        private static Mesh? quad;

        private MeshRenderer ground = null!;
        private MaterialPropertyBlock block = null!;
        private Phase phase;
        private Vector2 size;
        private float since;
        private float born;
        private float shownFrom;
        private float shown;
        private float seconds;
        private (Vector2 Size, float Shown, float Seconds)? painted;

        /// <summary>The seconds its fade and its twinkle are timed by; a render fixes it.</summary>
        public Func<float> Clock { get; set; } = () => Time.unscaledTime;

        /// <summary>The field's size, in its parent's units; it stands centred on its transform.</summary>
        public Vector2 Size => size;

        /// <summary>How far into view it is now, 0 to 1.</summary>
        public float Shown => shown;

        /// <summary>The clock its points twinkle by now, in seconds since it was first shown; it stops while things are kept still.</summary>
        public float Seconds => seconds;

        /// <summary>Whether it is fading in or out now.</summary>
        public bool Fading => phase == Phase.Arriving || phase == Phase.Leaving;

        /// <summary>What draws the field.</summary>
        public MeshRenderer Ground => ground;

        /// <summary>One dp on the plane, in the plane's units.</summary>
        private static float Dp => RoomType.Units(1f);

        /// <summary>A field under <paramref name="parent"/>, hidden, centred on its transform, drawn at <paramref name="order"/>.</summary>
        public static RoomField Create(Transform parent, int order = KitType.Order.Carrier)
        {
            var go = new GameObject("Working field");
            go.transform.SetParent(parent, false);
            var field = go.AddComponent<RoomField>();
            var groundGo = new GameObject("Points");
            groundGo.transform.SetParent(go.transform, false);
            groundGo.AddComponent<MeshFilter>().sharedMesh = Quad;
            field.ground = groundGo.AddComponent<MeshRenderer>();
            field.ground.sharedMaterial = Material;
            field.ground.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            field.ground.receiveShadows = false;
            field.ground.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            field.ground.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            field.ground.sortingOrder = order;
            field.block = new MaterialPropertyBlock();
            go.SetActive(false);
            return field;
        }

        /// <summary>
        /// The field's size round a box <paramref name="inner"/> (the working character and the information panel's place
        /// together), in the same units: grown 30 percent across, 50 percent up and down, and 42.4 dp more above and below.
        /// </summary>
        public static Vector2 Around(Vector2 inner) =>
            new Vector2(inner.x * (1f + SpillAcross), inner.y * (1f + SpillUpDown) + 2f * SpillMoreDp * Dp);

        /// <summary>Shows the field <paramref name="size"/> big, fading in where it was hidden or leaving; shown already, it only takes the size.</summary>
        public void Show(Vector2 size)
        {
            this.size = size;
            if (phase == Phase.Arriving || phase == Phase.Standing)
            {
                Draw();
                return;
            }
            var now = Clock();
            if (phase == Phase.Hidden)
            {
                gameObject.SetActive(true);
                born = now;
                seconds = 0f;
                shown = 0f;
            }
            shownFrom = shown;
            since = now;
            phase = Phase.Arriving;
            Draw();
        }

        /// <summary>Fades the field out, as when the run ends.</summary>
        public void Leave()
        {
            if (phase == Phase.Hidden || phase == Phase.Leaving) return;
            shownFrom = shown;
            since = Clock();
            phase = Phase.Leaving;
            Draw();
        }

        /// <summary>Gone at once, with no fade.</summary>
        public void Hide()
        {
            phase = Phase.Hidden;
            shown = 0f;
            painted = null;
            gameObject.SetActive(false);
        }

        /// <summary>A fade under way taken to its end at once: for renders, and for a first frame that must be whole.</summary>
        public void Settle()
        {
            if (phase == Phase.Hidden) return;
            since = float.NegativeInfinity;
            Draw();
        }

        private void Update() => Draw();

        /// <summary>Draws the field as it stands at its clock's time; called every frame, and by a render at the times it draws.</summary>
        public void Draw()
        {
            if (phase == Phase.Hidden) return;
            var now = Clock();
            var still = GlazeMotion.Still;
            // Kept still, the points hold the light they have.
            if (!still) seconds = Mathf.Max(0f, now - born);
            var passed = still ? float.PositiveInfinity : Mathf.Max(0f, now - since);
            if (phase == Phase.Arriving)
            {
                shown = Mathf.Lerp(shownFrom, 1f, RoomMotion.FieldArrives(passed));
                if (passed >= RoomMotion.FieldSeconds) phase = Phase.Standing;
            }
            else if (phase == Phase.Leaving)
            {
                shown = shownFrom * RoomMotion.FieldLeaves(passed);
                if (passed >= RoomMotion.FieldSeconds)
                {
                    Hide();
                    return;
                }
            }
            else shown = 1f;
            Paint();
        }

        /// <summary>The quad at the field's size with its clock and its place in view, set again only when one of them changed.</summary>
        private void Paint()
        {
            if (painted is var (lastSize, lastShown, lastSeconds) && lastSize == size && lastShown == shown && lastSeconds == seconds) return;
            if (!painted.HasValue || painted.Value.Size != size)
            {
                ground.transform.localScale = new Vector3(Mathf.Max(size.x, 1e-5f), Mathf.Max(size.y, 1e-5f), 1f);
                ground.transform.localPosition = new Vector3(0f, 0f, RoomType.Units(0.02f));
            }
            painted = (size, shown, seconds);
            block.SetVector(FieldId, new Vector4(size.x / Dp, size.y / Dp, 0f, 0f));
            block.SetFloat(ClockId, seconds);
            block.SetFloat(ShownId, shown);
            ground.SetPropertyBlock(block);
        }

        /// <summary>One point's own numbers, from its place in the grid: the same whole-number arithmetic the shader does.</summary>
        public readonly struct Point
        {
            public Point(float k, bool lit, float phase, float period)
            {
                K = k;
                Lit = lit;
                Phase = phase;
                Period = period;
            }

            /// <summary>Its own brightness, 0 to 1.</summary>
            public float K { get; }

            /// <summary>Whether it is one of the lit points, larger and brighter.</summary>
            public bool Lit { get; }

            /// <summary>Where in its twinkle it starts, in radians, and how long one twinkle takes, 1.8 to 3.6 s.</summary>
            public float Phase { get; }
            public float Period { get; }
        }

        /// <summary>The point at grid place (<paramref name="x"/>, <paramref name="y"/>), counted from the field's middle.</summary>
        public static Point PointAt(int x, int y)
        {
            unchecked
            {
                var h = Mix(((uint)(x + 32768) * 73856093u) ^ ((uint)(y + 32768) * 19349663u));
                var h2 = Mix(h + 0x9e3779b9u);
                return new Point((h & 0xffffu) / 65535f, (h >> 16) / 65535f < LitShare, (h2 & 0xffffu) / 65535f * 6.2831853f, 1.8f + (h2 >> 16) / 65535f * 1.8f);
            }
        }

        private static uint Mix(uint h)
        {
            unchecked
            {
                h ^= h >> 16;
                h *= 0x7feb352du;
                h ^= h >> 15;
                h *= 0x846ca68bu;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>How much of a point or a line stands at <paramref name="p"/>, in dp from the middle of a field <paramref name="sizeDp"/> big: 1 well inside, 0 at the edge.</summary>
        public static float Edge(Vector2 p, Vector2 sizeDp) => Smooth((1f - Out(p, sizeDp)) / FadeShare);

        /// <summary>How far out a place is: 0 at the field's middle, 1 on its edge.</summary>
        public static float Out(Vector2 p, Vector2 sizeDp) => new Vector2(p.x / (sizeDp.x / 2f), p.y / (sizeDp.y / 2f)).magnitude;

        /// <summary>A point's alpha and its radius in dp at <paramref name="clock"/> seconds, at grid place (<paramref name="x"/>, <paramref name="y"/>).</summary>
        public static (float Alpha, float RadiusDp) PointNow(int x, int y, Vector2 sizeDp, float clock)
        {
            var at = new Vector2(x * PitchDp, y * PitchDp);
            var edge = Edge(at, sizeDp);
            if (edge <= 0.01f) return (0f, 0f);
            var point = PointAt(x, y);
            var along = ((at.x + sizeDp.x / 2f) / sizeDp.x + (at.y + sizeDp.y / 2f) / sizeDp.y) * 0.5f;
            var centre = Mathf.Repeat(clock / BandSeconds, 1f) * 1.7f - 0.35f;
            var band = Smooth(1f - Mathf.Abs(along - centre) / 0.4f);
            var twinkle = 0.5f + 0.5f * Mathf.Sin(clock * 6.2831853f / point.Period + point.Phase);
            var alpha = point.Lit ? 0.6f + 0.4f * twinkle : 0.40f + 0.50f * (0.45f * point.K + 0.55f * twinkle);
            return (alpha * (0.55f + 0.45f * band) * edge, (point.Lit ? LitDp : PointDp) / 2f * (0.45f + 0.55f * edge));
        }

        /// <summary>
        /// What the page mixes at <paramref name="p"/> (dp from the field's middle, y up), as the shader mixes it before it
        /// turns to linear light: the colour premultiplied in sRGB, and the alpha. Edges are taken sharp, so it holds at a
        /// point's middle, on a line's middle and on bare ground, not across an edge.
        /// </summary>
        public static Vector4 Mixed(Vector2 p, Vector2 sizeDp, float clock, float shown)
        {
            var rho = Out(p, sizeDp);
            var edge = Edge(p, sizeDp);
            var dark = rho < 0.6f ? Mathf.Lerp(0.22f, 0.11f, rho / 0.6f) : Mathf.Lerp(0.11f, 0f, Mathf.Clamp01((rho - 0.6f) / 0.4f));
            var mixed = new Vector4(10f / 255f * dark, 12f / 255f * dark, 13f / 255f * dark, dark);
            mixed = White(mixed, 0.05f * Mathf.Clamp01(1f - rho));
            if (Mathf.Abs(p.x - GridDp * Mathf.Round(p.x / GridDp)) < 0.5f) mixed = White(mixed, GridAlpha * edge);
            if (Mathf.Abs(p.y - GridDp * Mathf.Round(p.y / GridDp)) < 0.5f) mixed = White(mixed, GridAlpha * edge);
            var x = Mathf.RoundToInt(p.x / PitchDp);
            var y = Mathf.RoundToInt(p.y / PitchDp);
            var (alpha, radius) = PointNow(x, y, sizeDp, clock);
            if ((p - new Vector2(x * PitchDp, y * PitchDp)).magnitude < radius) mixed = White(mixed, alpha);
            return mixed * Mathf.Clamp01(shown);
        }

        private static Vector4 White(Vector4 under, float alpha) =>
            new Vector4(under.x * (1f - alpha) + alpha, under.y * (1f - alpha) + alpha, under.z * (1f - alpha) + alpha, under.w * (1f - alpha) + alpha);

        private static float Smooth(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static Material Material
        {
            get
            {
                if (material != null) return material;
                var loaded = Resources.Load<Material>(MaterialPath);
                if (loaded == null)
                {
                    Debug.LogError("Halcyonic: the material Resources/" + MaterialPath + " is missing, so the working field cannot render.");
                    var fallback = Shader.Find("Hidden/InternalErrorShader");
                    material = new Material(fallback != null ? fallback : Shader.Find("Sprites/Default"));
                    return material;
                }
                material = loaded;
                return material;
            }
        }

        private static Mesh Quad
        {
            get
            {
                if (quad != null) return quad;
                quad = new Mesh { name = "Room field" };
                quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
                quad.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
                quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                quad.RecalculateBounds();
                quad.UploadMeshData(true);
                return quad;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            material = null;
            quad = null;
        }
    }
}

#nullable enable
using Halcyonic.Client;
using UnityEngine;
using UnityEngine.Rendering;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// One panel of the room in the art direction's quiet glass (<see cref="KitGlassMaterial"/>; shader Halcyonic/Kit Glass):
    /// an opaque face that carries the panel's words, a see-through rim that carries none, the light along its edge, a faint
    /// glow, two soft shadows, and on a moment its state edge and bloom. It draws on one quad that reaches past the panel by
    /// the far shadow's blur, so its fill costs <see cref="Overdraw"/> times the panel's area; the glow can be turned off
    /// first if a frame runs long. Flat only: it does not bend onto the instrument's curve.
    /// </summary>
    /// <remarks>
    /// The state light (the edge in the tone, the bloom) is drawn only where the panel's words say the same state, and it
    /// never stands for progress: a moment's arrival lights it once, and nothing here animates it on its own.
    /// </remarks>
    public sealed class KitGlass : MonoBehaviour
    {
        private const string MaterialPath = "HalcyonicUI/KitGlass";

        private static readonly int PanelId = Shader.PropertyToID("_Panel");
        private static readonly int QuadId = Shader.PropertyToID("_Quad");
        private static readonly int ToneId = Shader.PropertyToID("_Tone");
        private static readonly int ToneMidId = Shader.PropertyToID("_ToneMid");
        private static readonly int LightId = Shader.PropertyToID("_Light");

        private static Material? material;
        private static Mesh? quad;

        private MeshRenderer glassRenderer = null!;
        private MaterialPropertyBlock block = null!;
        private float opacity = 1f;

        /// <summary>The panel's outer size and corner radius, in its parent's units: the rim is inside them.</summary>
        public Vector2 Size { get; private set; }

        public float Radius { get; private set; }

        /// <summary>The rim's width in dp: <see cref="KitGlassMaterial.RimDp"/>, or the small and plate rims.</summary>
        public float RimDp { get; private set; }

        /// <summary>The tone lighting the edge and blooming the face, or none at rest.</summary>
        public GlazeColor? Tone { get; private set; }

        /// <summary>How far the bloom stands, 0 to 1 of its peak.</summary>
        public float Bloom { get; private set; }

        public bool Glow { get; private set; } = true;

        /// <summary>The quad's area over the panel's: what the shadow and the glow cost in fill.</summary>
        public double Overdraw { get; private set; }

        /// <summary>The face's size inside the rim, in the parent's units: where words may stand.</summary>
        public Vector2 FaceSize => Size - 2f * Units(RimDp) * Vector2.one;

        /// <summary>One dp, in units of the distance from the eyes.</summary>
        public static float Units(float dp) => KitType.Units(SpatialKit.Degrees(dp));

        public static KitGlass Create(Transform parent, string name, int order = KitType.Order.Carrier)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = Quad;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sortingOrder = order;
            var glass = go.AddComponent<KitGlass>();
            glass.glassRenderer = renderer;
            glass.block = new MaterialPropertyBlock();
            glass.gameObject.SetActive(false);
            return glass;
        }

        /// <summary>
        /// Draws the panel <paramref name="size"/> big, in its parent's units, with outer corners of <paramref name="radius"/>
        /// and a rim of <paramref name="rimDp"/>; a <paramref name="tone"/> lights its edge and, at <paramref name="bloom"/>
        /// above zero, blooms its face. The panel is centred on the glass's parent point; the quad reaches past it.
        /// </summary>
        public void Draw(Vector2 size, float radius, float rimDp = KitGlassMaterial.RimDp, GlazeColor? tone = null, float bloom = 0f, bool glow = true)
        {
            Size = size;
            Radius = radius;
            RimDp = rimDp;
            Tone = tone;
            Bloom = Mathf.Clamp01(bloom);
            Glow = glow;
            var unit = Units(1f);
            var width = size.x / unit;
            var height = size.y / unit;
            var (side, top, bottom) = KitGlassMaterial.Margins;
            var quadSize = new Vector2(width + 2f * side, height + top + bottom);
            Overdraw = KitGlassMaterial.Overdraw(width, height);
            transform.localScale = new Vector3(quadSize.x * unit, quadSize.y * unit, 1f);
            transform.localPosition = new Vector3(0f, (top - bottom) / 2f * unit, transform.localPosition.z);
            block.SetVector(PanelId, new Vector4(width, height, radius / unit, rimDp));
            block.SetVector(QuadId, new Vector4(-width / 2f - side, -height / 2f - bottom, quadSize.x, quadSize.y));
            if (tone is GlazeColor shown)
            {
                var edge = KitGlassMaterial.StateEdge(shown);
                block.SetVector(ToneId, new Vector4(shown.R / 255f, shown.G / 255f, shown.B / 255f, 1f));
                block.SetVector(ToneMidId, new Vector4((float)edge[1].Colour[0], (float)edge[1].Colour[1], (float)edge[1].Colour[2], (float)edge[1].Alpha));
            }
            else
            {
                block.SetVector(ToneId, Vector4.zero);
                block.SetVector(ToneMidId, Vector4.zero);
            }
            block.SetVector(LightId, new Vector4(Bloom, glow ? 1f : 0f, opacity, 0f));
            glassRenderer.SetPropertyBlock(block);
            gameObject.SetActive(true);
        }

        /// <summary>Sets the bloom alone, 0 to 1 of its peak, without drawing the panel again: the arrival's clock drives it.</summary>
        public void SetBloom(float bloom)
        {
            Bloom = Mathf.Clamp01(bloom);
            block.SetVector(LightId, new Vector4(Bloom, Glow ? 1f : 0f, opacity, 0f));
            glassRenderer.SetPropertyBlock(block);
        }

        /// <summary>Draws the whole panel at <paramref name="to"/> of its opacity: for the renders' witness that a faded face is caught.</summary>
        public void Fade(float to)
        {
            opacity = Mathf.Clamp01(to);
            block.SetVector(LightId, new Vector4(Bloom, Glow ? 1f : 0f, opacity, 0f));
            glassRenderer.SetPropertyBlock(block);
        }

        public void Hide() => gameObject.SetActive(false);

        private static Material Material
        {
            get
            {
                if (material != null) return material;
                var loaded = Resources.Load<Material>(MaterialPath);
                if (loaded == null)
                {
                    // Loudly wrong rather than invisible: Unity's error shader draws magenta.
                    Debug.LogError("Halcyonic: the material Resources/" + MaterialPath + " is missing, so the room's glass cannot render.");
                    var fallback = Shader.Find("Hidden/InternalErrorShader");
                    material = new Material(fallback != null ? fallback : Shader.Find("Sprites/Default"));
                    return material;
                }
                // A copy carrying KitGlassMaterial's values, so the client core stays their one source.
                material = new Material(loaded) { name = "Kit glass" };
                Vector4 Srgb(GlazeColor c) => new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, 0f);
                material.SetVector("_N7", Srgb(SpatialKit.Text));
                material.SetVector("_N1", Srgb(SpatialKit.Carrier));
                material.SetVector("_FaceTop", Srgb(SpatialKit.FaceTop));
                material.SetVector("_FaceBottom", Srgb(SpatialKit.FaceBottom));
                var rim = KitGlassMaterial.Rim;
                material.SetVector("_RimStops", new Vector4((float)rim[0].Alpha, (float)rim[1].Alpha, (float)rim[2].Alpha, (float)rim[1].At));
                var edge = KitGlassMaterial.Edge;
                material.SetVector("_EdgeStops", new Vector4((float)edge[0].Alpha, (float)edge[1].Alpha, (float)edge[2].Alpha, (float)edge[1].At));
                material.SetVector("_LitRoom", Srgb(KitGlassMaterial.LitRoom));
                material.SetVector("_DarkRoom", Srgb(KitGlassMaterial.DarkRoom));
                material.SetVector("_Shadow", new Vector4(KitGlassMaterial.ShadowDropDp, KitGlassMaterial.ShadowBlurDp, (float)KitGlassMaterial.ShadowAlpha, (float)KitGlassMaterial.ShadowExponent));
                material.SetVector("_Contact", new Vector4(KitGlassMaterial.ContactDropDp, KitGlassMaterial.ContactBlurDp, (float)KitGlassMaterial.ContactAlpha, 0f));
                material.SetVector("_Glow", new Vector4(KitGlassMaterial.GlowBlurDp, (float)KitGlassMaterial.GlowAlpha, (float)KitGlassMaterial.GlowScale, (float)KitGlassMaterial.GlowPower));
                material.SetVector("_Lights", new Vector4(KitGlassMaterial.EdgeDp, KitGlassMaterial.HighlightDp, (float)KitGlassMaterial.HighlightAlpha, (float)KitGlassMaterial.BloomReach));
                material.SetFloat("_BloomPeak", (float)KitGlassMaterial.BloomPeak);
                return material;
            }
        }

        /// <summary>A unit quad facing along -z, its UVs from 0 to 1.</summary>
        private static Mesh Quad
        {
            get
            {
                if (quad != null) return quad;
                quad = new Mesh { name = "Kit glass" };
                quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
                quad.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
                quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                quad.RecalculateBounds();
                quad.UploadMeshData(true);
                return quad;
            }
        }

        /// <summary>Forgets cached objects when play mode starts without a domain reload.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            material = null;
            quad = null;
        }
    }
}

#nullable enable
using Halcyonic.Client;
using UnityEngine;
using UnityEngine.Rendering;

namespace Halcyonic.XR.UI
{
    /// <summary>What a shape says about choosing (ADR 0026): nothing, what is chosen, what is pointed at, or the main action's cap.</summary>
    public enum SurfaceSelection
    {
        None,

        /// <summary>The chosen one: the lit fill with its frame.</summary>
        Lit,

        /// <summary>What a hand or the eyes point at: the frame alone.</summary>
        Pointed,

        /// <summary>The main action's key cap, the one shape the accent fills.</summary>
        MainCap,
    }

    /// <summary>
    /// A flat shape of the interface (ADR 0023): a rounded rectangle, from a sharp corner to a pill,
    /// with a fill, an edge inside its outline, solid or dashed, and a halftone for what is only last
    /// known. One shader and one material draw every shape (Halcyonic/Glaze Surface), each with its
    /// own properties, so shapes of every component draw alike and together.
    /// </summary>
    /// <remarks>
    /// The material ships the shader into player builds from Assets/Halcyonic/UI/Resources, as the
    /// characters' materials do theirs (<c>CharacterMaterials</c>). A shape is centred on its
    /// transform and faces along its parent's back, as text does, so it reads seen along the
    /// parent's forward axis.
    /// </remarks>
    public sealed class Surface : MonoBehaviour
    {
        private const string MaterialPath = "HalcyonicUI/Surface";

        private static readonly int FillId = Shader.PropertyToID("_Fill");
        private static readonly int EdgeId = Shader.PropertyToID("_Edge");
        private static readonly int ShapeId = Shader.PropertyToID("_Shape");
        private static readonly int PatternId = Shader.PropertyToID("_Pattern");
        private static readonly int GlassId = Shader.PropertyToID("_Glass");

        private static Material? material;
        private static Mesh? quad;

        private MeshRenderer shapeRenderer = null!;
        private MaterialPropertyBlock block = null!;
        private Vector2 size;
        private float radius;
        private Color fill;
        private Color edge;
        private float edgeWidth;
        private float dash;
        private float halftone;
        private Vector4 glass;
        private float opacity = 1f;

        /// <summary>The shape's size, in its parent's units.</summary>
        public Vector2 Size => size;

        public MeshRenderer Renderer => shapeRenderer;

        /// <summary>The shape's fill as drawn, before any fade, for checks.</summary>
        public Color Fill => fill;

        /// <summary>The shape's edge and how wide it is drawn, in the parent's units, for checks.</summary>
        public Color Edge => edge;

        public float EdgeWidth => edgeWidth;

        /// <summary>
        /// What the shape says about choosing (ADR 0026), set by the component that draws it, so the
        /// renders hold one selection treatment (<c>GlazeChecks.OneSelectionTreatment</c>).
        /// </summary>
        public SurfaceSelection Selection { get; set; }

        public static Surface Create(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sortingOrder = order;
            var surface = go.AddComponent<Surface>();
            surface.shapeRenderer = renderer;
            surface.block = new MaterialPropertyBlock();
            // Nothing until drawn, rather than the material's own white square.
            surface.Draw(Vector2.zero, 0f, Color.clear);
            return surface;
        }

        /// <summary>
        /// Draws the shape: <paramref name="size"/> and <paramref name="radius"/> in the parent's
        /// units, a pill when the radius is half the height or more. The edge is drawn inside the
        /// outline, <paramref name="edgeWidth"/> wide, in dashes about <paramref name="dash"/> apart
        /// when that is not zero. A <paramref name="halftone"/> pitch above zero draws the fill as dots.
        /// </summary>
        public void Draw(Vector2 size, float radius, Color fill, Color edge = default, float edgeWidth = 0f, float dash = 0f, float halftone = 0f) =>
            Shape(size, radius, fill, edge, edgeWidth, dash, halftone, Vector4.zero);

        private void Shape(Vector2 size, float radius, Color fill, Color edge, float edgeWidth, float dash, float halftone, Vector4 glass)
        {
            this.glass = glass;
            this.size = size;
            this.radius = radius;
            this.fill = fill;
            this.edge = edge;
            this.edgeWidth = edgeWidth;
            this.dash = dash;
            this.halftone = halftone;
            Apply();
        }

        /// <summary>
        /// Draws the shape as the menu's glass (ADR 0026): the panel colour at
        /// <see cref="Glaze.Menu.GlassOpacity"/> with no blur, a white hairline edge, a light from its
        /// top edge fading out by <paramref name="glowReach"/>, in the parent's units, a third of its
        /// height unless given (a content surface's ends within its top padding, so no row under it
        /// looks lit), and a sheen just inside its top edge, all in one draw.
        /// </summary>
        public void DrawGlass(Vector2 size, float? glowReach = null) =>
            Shape(size, GlazeTokens.Units(Glaze.Menu.RadiusDegrees), GlazeTokens.ColorOf(Glaze.Panel, Glaze.Menu.GlassOpacity),
                new Color(1f, 1f, 1f, Glaze.Menu.HairlineOpacity), GlazeTokens.Units(Glaze.Menu.HairlineDegrees), 0f, 0f,
                new Vector4(Glaze.Menu.GlowOpacity, glowReach ?? size.y * Glaze.Menu.GlowReach, Glaze.Menu.SheenOpacity, GlazeTokens.Units(Glaze.Menu.SheenDegrees)));

        /// <summary>How far down from its top edge the glass's light reaches, in the parent's units; 0 for a shape that isn't glass.</summary>
        public float GlowReach => glass.x > 0f ? glass.y : 0f;

        /// <summary>Draws the shape as it is, as visible as <paramref name="opacity"/>, from 0 to 1, as when it fades in or out.</summary>
        public void Fade(float opacity)
        {
            if (Mathf.Approximately(opacity, this.opacity)) return;
            this.opacity = opacity;
            Apply();
        }

        private void Apply()
        {
            transform.localScale = new Vector3(size.x, size.y, 1f);
            block.SetVector(FillId, Linear(fill, opacity));
            block.SetVector(EdgeId, Linear(edge, opacity));
            block.SetVector(ShapeId, new Vector4(size.x, size.y, radius, edgeWidth));
            // Dashes half on, half off; dots of a radius 0.3 of their pitch, fine enough to read a word over.
            block.SetVector(PatternId, new Vector4(dash, dash > 0f ? 0.5f : 0f, halftone, halftone * 0.3f));
            // The glass's light and sheen fade with the shape.
            block.SetVector(GlassId, new Vector4(glass.x * opacity, glass.y, glass.z * opacity, glass.w));
            shapeRenderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// A colour as the shader takes it: linear in a linear project, its alpha faded. The block's
        /// vectors are passed as they are, so the conversion an authored colour needs happens here.
        /// </summary>
        private static Vector4 Linear(Color color, float opacity)
        {
            var linear = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
            return new Vector4(linear.r, linear.g, linear.b, color.a * opacity);
        }

        private static Material Material
        {
            get
            {
                if (material != null) return material;
                material = Resources.Load<Material>(MaterialPath);
                if (material == null)
                {
                    // Loudly wrong rather than invisible: Unity's error shader draws magenta.
                    Debug.LogError("Halcyonic: the material Resources/" + MaterialPath + " is missing, so the interface's shapes cannot render.");
                    var fallback = Shader.Find("Hidden/InternalErrorShader");
                    if (fallback == null) fallback = Shader.Find("Sprites/Default");
                    material = new Material(fallback);
                }
                return material;
            }
        }

        /// <summary>A unit quad facing along -z, its UVs from 0 to 1, shared by every shape.</summary>
        private static Mesh Quad
        {
            get
            {
                if (quad != null) return quad;
                quad = new Mesh { name = "Glaze surface" };
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
        private static void Reset()
        {
            material = null;
            quad = null;
        }
    }
}

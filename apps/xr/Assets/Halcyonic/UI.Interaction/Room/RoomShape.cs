#nullable enable
using UnityEngine;
using UnityEngine.Rendering;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The redesign's own flat shape: a rounded rectangle of one fill and an optional edge drawn inside its outline, as
    /// the redesign's cards, chips and marks need (the addendum's A17). It draws with the interface's shape shader, flat,
    /// with no glass, no rim and no curve, and is the redesign's own part, never the older kit's: nothing of the redesign
    /// leans on the kit that goes at ON-2 (the lead, 2026-11-11, the judges' walk). Its origin is its middle; its size is
    /// in its parent's units.
    /// </summary>
    public sealed class RoomShape : MonoBehaviour
    {
        private const string MaterialPath = "HalcyonicUI/Surface";
        private static readonly int FillId = Shader.PropertyToID("_Fill");
        private static readonly int EdgeId = Shader.PropertyToID("_Edge");
        private static readonly int ShapeId = Shader.PropertyToID("_Shape");
        private static readonly int PatternId = Shader.PropertyToID("_Pattern");
        private static readonly int PhysicalId = Shader.PropertyToID("_Physical");
        private static readonly int GlassId = Shader.PropertyToID("_Glass");
        private static Material? material;
        private static Mesh? quad;

        private MeshRenderer shapeRenderer = null!;
        private MaterialPropertyBlock block = null!;

        /// <summary>Its size as last drawn, in its parent's units.</summary>
        public Vector2 Size { get; private set; }

        public float CornerRadius { get; private set; }

        public Color Fill { get; private set; }

        public Color Edge { get; private set; }

        public float EdgeWidth { get; private set; }

        public MeshRenderer Renderer => shapeRenderer;

        /// <summary>A shape under <paramref name="parent"/>, drawn at <paramref name="order"/>, nothing until drawn.</summary>
        public static RoomShape Create(Transform parent, string name, int order)
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
            var shape = go.AddComponent<RoomShape>();
            shape.shapeRenderer = renderer;
            shape.block = new MaterialPropertyBlock();
            shape.Draw(Vector2.zero, 0f, Color.clear);
            return shape;
        }

        /// <summary>
        /// Draws the shape <paramref name="size"/> across with corners of <paramref name="radius"/> in its parent's units, a
        /// pill when the radius is half its height or more; its edge <paramref name="edgeWidth"/> wide inside the outline.
        /// Colours are sRGB, their alphas the ones the headset shows.
        /// </summary>
        public void Draw(Vector2 size, float radius, Color fill, Color edge = default, float edgeWidth = 0f)
        {
            Size = size;
            CornerRadius = radius;
            Fill = fill;
            Edge = edge;
            EdgeWidth = edgeWidth;
            transform.localScale = new Vector3(size.x, size.y, 1f);
            block.SetVector(FillId, Linear(fill));
            block.SetVector(EdgeId, Linear(edge));
            block.SetVector(ShapeId, new Vector4(size.x, size.y, radius, edgeWidth));
            block.SetVector(PatternId, Vector4.zero);
            block.SetVector(GlassId, Vector4.zero);
            block.SetVector(PhysicalId, Vector4.zero);
            shapeRenderer.SetPropertyBlock(block);
        }

        private static Vector4 Linear(Color color)
        {
            var linear = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
            return new Vector4(linear.r, linear.g, linear.b, color.a);
        }

        private static Material Material
        {
            get
            {
                if (material != null) return material;
                material = Resources.Load<Material>(MaterialPath);
                if (material == null)
                {
                    Debug.LogError("Halcyonic: the material Resources/" + MaterialPath + " is missing, so the redesign's shapes cannot render.");
                    var fallback = Shader.Find("Hidden/InternalErrorShader");
                    material = new Material(fallback != null ? fallback : Shader.Find("Sprites/Default"));
                }
                return material;
            }
        }

        private static Mesh Quad
        {
            get
            {
                if (quad != null) return quad;
                quad = new Mesh { name = "Room shape" };
                quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
                quad.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
                quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                quad.RecalculateBounds();
                quad.UploadMeshData(true);
                return quad;
            }
        }
    }
}

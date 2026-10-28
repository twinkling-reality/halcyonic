#nullable enable
using System;
using System.Collections.Generic;
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

        /// <summary>A vertical insertion caret in a focused input field, never a selection marker.</summary>
        InputCaret,

        /// <summary>A neutral structural separator inside a composed workspace, never a selection or action marker.</summary>
        CompositionDivider,
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
        private static readonly int PhysicalId = Shader.PropertyToID("_Physical");
        private static readonly int GlassId = Shader.PropertyToID("_Glass");

        private static Material? material;
        private static Mesh? quad;

        private MeshRenderer shapeRenderer = null!;
        private MeshFilter shapeFilter = null!;
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
        private float shown = 1f;
        private bool physical;
        private MeshRenderer? rim;
        private Mesh? rimMesh;
        private MaterialPropertyBlock? rimBlock;
        private Vector2 rimSize;
        private float rimRadius;
        private float rimDepth;
        private CurvedDrawing? curvedOn;
        private Mesh? strip;
        private Matrix4x4 stripAt;
        private int stripVersion = -1;
        private int stripColumns;
        private Mesh? rimBent;
        private Vector2 rimBentSize;
        private float rimBentRadius;
        private float rimBentDepth;
        private Matrix4x4 rimAt;
        private int rimVersion = -1;
        private const float Thickness = 0.006f; // In distance units: 2.8 mm at the normal 0.46 m plane.
        public float MaterialDepth => physical ? Thickness : 0f;
        public bool CoatedInstrument => physical;
        public MeshRenderer? MaterialRim => physical ? rim : null;
#if UNITY_EDITOR
        /// <summary>Same face and geometry allocation, without physical edge, for a negative render control.</summary>
        public void FlattenMaterialForRender() { physical = false; Apply(); }
#endif

        /// <summary>The shape's size, in its parent's units.</summary>
        public Vector2 Size => size;

        public MeshRenderer Renderer => shapeRenderer;

        /// <summary>Whether the shape is drawn on its plane's curve now, as a strip of columns, rather than as the shared flat quad.</summary>
        public bool OnCurve => strip != null && shapeFilter.sharedMesh == strip;

        /// <summary>The vertices the shape draws now, its rim's with them, for the renders' counts.</summary>
        public int DrawnVertexCount => (shapeFilter.sharedMesh != null ? shapeFilter.sharedMesh.vertexCount : 0)
            + (rim != null && rim.gameObject.activeSelf && rim.GetComponent<MeshFilter>().sharedMesh is Mesh drawn ? drawn.vertexCount : 0);

        /// <summary>The shape's fill as drawn, before any fade, for checks.</summary>
        public Color Fill => fill;

        /// <summary>The shape's edge and how wide it is drawn, in the parent's units, for checks.</summary>
        public Color Edge => edge;

        public float EdgeWidth => edgeWidth;
        public float CornerRadius => radius;

        /// <summary>
        /// What the shape says about choosing (ADR 0026), set by the component that draws it, so the
        /// renders hold one selection treatment (<c>GlazeChecks.OneSelectionTreatment</c>).
        /// </summary>
        public SurfaceSelection Selection { get; set; }

        public static Surface Create(Transform parent, string name, int order)
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
            var surface = go.AddComponent<Surface>();
            surface.shapeRenderer = renderer;
            surface.shapeFilter = filter;
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
        public void Draw(Vector2 size, float radius, Color fill, Color edge = default, float edgeWidth = 0f, float dash = 0f, float halftone = 0f)
        { physical = false; Shape(size, radius, fill, edge, edgeWidth, dash, halftone, Vector4.zero); }

        /// <summary>One opaque coated reading face with bounded reflected light and real inward thickness.</summary>
        public void DrawCoatedInstrument(Vector2 size, float radius, Color fill)
        {
            physical = true;
            fill.a = 1f;
            Shape(size, radius, fill, Color.clear, 0f, 0f, 0f, Vector4.zero);
        }

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
        public void DrawGlass(Vector2 size, float? glowReach = null, float? radius = null)
        { physical = false; Shape(size, radius ?? GlazeTokens.Units(Glaze.Menu.RadiusDegrees), GlazeTokens.ColorOf(Glaze.Panel, Glaze.Menu.GlassOpacity),
                GlazeTokens.ColorOf(Glaze.Palette.EdgeLight, Glaze.Menu.HairlineOpacity), GlazeTokens.Units(Glaze.Menu.HairlineDegrees), 0f, 0f,
                new Vector4(Glaze.Menu.GlowOpacity, glowReach ?? size.y * Glaze.Menu.GlowReach, Glaze.Menu.SheenOpacity, GlazeTokens.Units(Glaze.Menu.SheenDegrees))); }

        /// <summary>How far down from its top edge the glass's light reaches, in the parent's units; 0 for a shape that isn't glass.</summary>
        public float GlowReach => glass.x > 0f ? glass.y : 0f;

        /// <summary>Draws the shape as it is, as visible as <paramref name="opacity"/>, from 0 to 1, as when it fades in or out.</summary>
        public void Fade(float opacity)
        {
            if (Mathf.Approximately(opacity, this.opacity)) return;
            this.opacity = opacity;
            Apply();
        }

        /// <summary>
        /// How far the shape has come into view as the column it is on opens or closes, from 0 to 1,
        /// multiplying any <see cref="Fade"/>: kept apart from it, so a badge's own fade and a column's
        /// opening never undo each other.
        /// </summary>
        public float Shown
        {
            get => shown;
            set
            {
                if (value == shown) return;
                shown = value;
                Apply();
            }
        }

        private void Apply()
        {
            var opacity = this.opacity * shown;
            transform.localScale = new Vector3(size.x, size.y, 1f);
            block.SetVector(FillId, Linear(fill, opacity));
            block.SetVector(EdgeId, Linear(edge, opacity));
            block.SetVector(ShapeId, new Vector4(size.x, size.y, radius, edgeWidth));
            // Dashes half on, half off; dots of a radius 0.3 of their pitch, fine enough to read a word over.
            block.SetVector(PatternId, new Vector4(dash, dash > 0f ? 0.5f : 0f, halftone, halftone * 0.3f));
            // The glass's light and sheen fade with the shape.
            block.SetVector(GlassId, new Vector4(glass.x * opacity, glass.y, glass.z * opacity, glass.w));
            block.SetVector(PhysicalId, new Vector4(physical ? 1f : 0f, 0f, 0.10f * opacity, 0f));
            shapeRenderer.SetPropertyBlock(block);
            BendShape();
            UpdateRim(opacity);
        }

        /// <summary>
        /// Draws the shape on <paramref name="curve"/>'s curve while it is on, or flat as laid out with none: called
        /// by the plane after each step, and again by the shape whenever it is drawn anew. A shape whose place, size
        /// and curve are as they were keeps its strip.
        /// </summary>
        public void DrawOn(CurvedDrawing? curve)
        {
            curvedOn = curve;
            BendShape();
            if (physical && rim != null && rim.gameObject.activeSelf) RimMesh();
        }

        private CurvedDrawing? Curve => curvedOn != null && curvedOn.On ? curvedOn : null;

        /// <summary>
        /// The shape as a strip of columns no wider than <see cref="CurvedDrawing.ColumnDegrees"/>, each vertex where
        /// the curve puts it, its UVs kept so the shader draws the same shape, its normals those of the bent face;
        /// rebuilt only when its place, size, column count or the curve's place changes.
        /// </summary>
        private void BendShape()
        {
            if (shapeFilter == null) return;
            var curve = Curve;
            var scale = transform.lossyScale;
            if (curve == null || Mathf.Abs(scale.x) < 1e-6f || Mathf.Abs(scale.y) < 1e-6f)
            {
                if (shapeFilter.sharedMesh != Quad) shapeFilter.sharedMesh = Quad;
                return;
            }
            var at = transform.localToWorldMatrix;
            var columns = curve.Curve.Columns(Mathf.Abs(scale.x), CurvedDrawing.ColumnDegrees);
            if (OnCurve && stripVersion == curve.Version && stripColumns == columns && stripAt == at) return;
            var toLocal = transform.worldToLocalMatrix;
            var points = StripPoints;
            points.Clear();
            for (var i = 0; i <= columns; i++)
            {
                var u = (float)i / columns - 0.5f;
                points.Add(toLocal.MultiplyPoint3x4(curve.Bend(at.MultiplyPoint3x4(new Vector3(u, -0.5f, 0f)))));
                points.Add(toLocal.MultiplyPoint3x4(curve.Bend(at.MultiplyPoint3x4(new Vector3(u, 0.5f, 0f)))));
            }
            strip ??= new Mesh { name = "Glaze surface on the curve" };
            if (stripColumns != columns || strip.vertexCount != points.Count)
            {
                var (uvs, triangles) = StripTopology(columns);
                strip.Clear();
                strip.SetVertices(points);
                strip.SetUVs(0, uvs);
                strip.SetTriangles(triangles, 0);
            }
            else strip.SetVertices(points);
            strip.RecalculateNormals();
            strip.RecalculateBounds();
            shapeFilter.sharedMesh = strip;
            stripAt = at; stripVersion = curve.Version; stripColumns = columns;
        }

        private static readonly List<Vector3> StripPoints = new List<Vector3>();
        private static readonly Dictionary<int, (Vector2[] Uvs, int[] Triangles)> Strips = new Dictionary<int, (Vector2[], int[])>();

        /// <summary>A strip's UVs and triangles for <paramref name="columns"/> columns, shared by every strip that wide: bottom then top at each column's edge.</summary>
        private static (Vector2[] Uvs, int[] Triangles) StripTopology(int columns)
        {
            if (Strips.TryGetValue(columns, out var kept)) return kept;
            var uvs = new Vector2[2 * (columns + 1)];
            var triangles = new int[6 * columns];
            for (var i = 0; i <= columns; i++)
            {
                var u = (float)i / columns;
                uvs[2 * i] = new Vector2(u, 0f);
                uvs[2 * i + 1] = new Vector2(u, 1f);
                if (i == columns) continue;
                // As the shared quad winds: bottom left, top right, bottom right; bottom left, top left, top right.
                var at = 6 * i;
                triangles[at] = 2 * i; triangles[at + 1] = 2 * i + 3; triangles[at + 2] = 2 * i + 2;
                triangles[at + 3] = 2 * i; triangles[at + 4] = 2 * i + 1; triangles[at + 5] = 2 * i + 3;
            }
            kept = (uvs, triangles);
            Strips[columns] = kept;
            return kept;
        }

        private void UpdateRim(float opacity)
        {
            if (!physical || size.x <= 0f || size.y <= 0f) { if (rim != null) rim.gameObject.SetActive(false); return; }
            if (rim == null)
            {
                var go = new GameObject("Inset material bevel and thickness"); go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>(); rim = go.AddComponent<MeshRenderer>(); rim.sharedMaterial = Material;
                rim.shadowCastingMode = ShadowCastingMode.Off; rim.receiveShadows = false;
                rim.lightProbeUsage = LightProbeUsage.Off; rim.reflectionProbeUsage = ReflectionProbeUsage.Off;
                rim.sortingOrder = shapeRenderer.sortingOrder + 1;
            }
            rim.gameObject.SetActive(true);
            RimMesh();
            var properties = rimBlock ??= new MaterialPropertyBlock();
            properties.SetVector(FillId, Linear(GlazeTokens.ColorOf(Glaze.Raised), opacity));
            properties.SetVector(PhysicalId, new Vector4(1f, 0f, 0.10f * opacity, 1f));
            rim.SetPropertyBlock(properties);
        }

        /// <summary>
        /// Draws the rim with the mesh for where it stands: flat as laid out, built again only when its shape or depth
        /// changes and kept while the curve is on, so the plane drawn flat again draws the very mesh it drew; or on the
        /// curve, one mesh of its own built again in place whenever its shape, depth or place on the curve changes, with
        /// a column's points along its top and bottom edges and every vertex where the curve puts it.
        /// </summary>
        private void RimMesh()
        {
            if (rim == null) return;
            var filter = rim.GetComponent<MeshFilter>();
            var corner = Mathf.Clamp(radius, 0f, Mathf.Min(size.x, size.y) / 2f);
            var curve = Curve;
            if (curve == null)
            {
                if (rimMesh == null || rimSize != size || rimRadius != radius || rimDepth != MaterialDepth)
                {
                    if (rimMesh != null) { if (Application.isPlaying) Destroy(rimMesh); else DestroyImmediate(rimMesh); }
                    rimSize = size; rimRadius = radius; rimDepth = MaterialDepth;
                    rimMesh = MakeRim(null, size, corner, MaterialDepth, 0.001f);
                }
                if (filter.sharedMesh != rimMesh) filter.sharedMesh = rimMesh;
                return;
            }
            var at = rim.transform.localToWorldMatrix;
            if (rimBent != null && filter.sharedMesh == rimBent && rimBentSize == size && rimBentRadius == radius && rimBentDepth == MaterialDepth
                && rimVersion == curve.Version && rimAt == at) return;
            rimBentSize = size; rimBentRadius = radius; rimBentDepth = MaterialDepth; rimAt = at; rimVersion = curve.Version;
            CurvedDrawing bending = curve;
            var toLocal = rim.transform.worldToLocalMatrix;
            var columns = bending.Curve.Columns(Mathf.Abs(rim.transform.lossyScale.x), CurvedDrawing.ColumnDegrees);
            rimBent = MakeRim(rimBent, size, corner, MaterialDepth, 0.001f, columns - 1,
                local => toLocal.MultiplyPoint3x4(bending.Bend(at.MultiplyPoint3x4(local))));
            rimBent.name = "Measured instrument bevel on the curve";
            filter.sharedMesh = rimBent;
        }

        /// <param name="into">A mesh to build again in place, or none for a new one.</param>
        /// <param name="edge">Points added along the top and bottom edges between their corners, so a bend follows them.</param>
        /// <param name="bend">Where each vertex is drawn, in the rim's own units; as built when none.</param>
        private static Mesh MakeRim(Mesh? into, Vector2 size, float radius, float depth, float bevel, int edge = 0, Func<Vector3, Vector3>? bend = null)
        {
            const int steps = 8;
            var count = 4 * (steps + 1) + 2 * edge;
            var vertices = new Vector3[count * 3]; var triangles = new int[count * 12];
            var inset = Mathf.Min(bevel, Mathf.Min(size.x, size.y) / 8f);
            (Vector2 Inner, Vector2 Outer) Point(int corner, int step)
            {
                var angle = (corner * 90f + step * 90f / steps) * Mathf.Deg2Rad;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var sign = new Vector2(corner == 0 || corner == 3 ? 1f : -1f, corner < 2 ? 1f : -1f);
                var centre = Vector2.Scale(sign, size / 2f - Vector2.one * radius);
                return (centre + direction * Mathf.Max(0f, radius - inset) - Vector2.Scale(sign, Vector2.one * Mathf.Max(0f, inset - radius)), centre + direction * radius);
            }
            var outline = new List<(Vector2 Inner, Vector2 Outer)>(count);
            for (var corner = 0; corner < 4; corner++)
            {
                for (var step = 0; step <= steps; step++) outline.Add(Point(corner, step));
                // The top edge runs from the first corner to the second, the bottom from the third to the fourth.
                if (corner != 0 && corner != 2) continue;
                var (from, to) = (Point(corner, steps), Point(corner + 1, 0));
                for (var k = 1; k <= edge; k++)
                {
                    var t = (float)k / (edge + 1);
                    outline.Add((Vector2.Lerp(from.Inner, to.Inner, t), Vector2.Lerp(from.Outer, to.Outer, t)));
                }
            }
            for (var index = 0; index < count; index++)
            {
                var (inner, outer) = outline[index];
                vertices[index] = new Vector3(inner.x / size.x, inner.y / size.y, 0f);
                vertices[count + index] = new Vector3(outer.x / size.x, outer.y / size.y, depth * 0.2f);
                vertices[2 * count + index] = new Vector3(outer.x / size.x, outer.y / size.y, depth);
                var next = (index + 1) % count;
                for (var strip = 0; strip < 2; strip++)
                {
                    var at = index * 12 + strip * 6; var a = strip * count + index; var b = strip * count + next;
                    triangles[at] = a; triangles[at + 1] = b; triangles[at + 2] = a + count;
                    triangles[at + 3] = b; triangles[at + 4] = b + count; triangles[at + 5] = a + count;
                }
            }
            if (bend != null) for (var index = 0; index < vertices.Length; index++) vertices[index] = bend(vertices[index]);
            var mesh = into ?? new Mesh { name = "Measured instrument bevel" };
            mesh.Clear(); mesh.vertices = vertices; mesh.triangles = triangles;
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private void OnDestroy()
        {
            if (rimMesh != null) { if (Application.isPlaying) Destroy(rimMesh); else DestroyImmediate(rimMesh); }
            if (rimBent != null) { if (Application.isPlaying) Destroy(rimBent); else DestroyImmediate(rimBent); }
            if (strip != null) { if (Application.isPlaying) Destroy(strip); else DestroyImmediate(strip); }
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
        private static void ResetStatics()
        {
            material = null;
            quad = null;
            Strips.Clear();
        }
    }
}

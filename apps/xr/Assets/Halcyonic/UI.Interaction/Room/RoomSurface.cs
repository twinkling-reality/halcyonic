#nullable enable
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>What a redesign surface is, which decides its colour and corner (DESIGN's SYSTEM-v4, 2026-11-05).</summary>
    public enum RoomSurfaceKind
    {
        /// <summary>The information panel: the computer's facts and the system's requests.</summary>
        Information,

        /// <summary>The deeper tray the choices stand in.</summary>
        Choices,

        /// <summary>A bar's or a name tag's surface, in the panel's material.</summary>
        Bar,
    }

    /// <summary>
    /// The one surface every part of the redesign stands on, in DESIGN's material (FINISH-3, 2026-11-05, <c>Halcyonic/Room
    /// Glass</c>): a translucent tonal layer, its gradient, a 1 dp hairline, squircle corners, an optional lighter sentence bar,
    /// and the one sheen (<see cref="RoomSheen"/>). This is the one place the look changes; a surface draws again only when its
    /// size, kind or sentence bar changed.
    /// </summary>
    public sealed class RoomSurface : MonoBehaviour
    {
        private const string MaterialPath = "HalcyonicUI/RoomGlass";
        private static readonly int PanelId = Shader.PropertyToID("_Panel");
        private static readonly int QuadId = Shader.PropertyToID("_Quad");
        private static readonly int FillTopId = Shader.PropertyToID("_FillTop");
        private static readonly int FillFootId = Shader.PropertyToID("_FillFoot");
        private static readonly int EdgeTopId = Shader.PropertyToID("_EdgeTop");
        private static readonly int EdgeFootId = Shader.PropertyToID("_EdgeFoot");
        private static readonly int ShadeId = Shader.PropertyToID("_Shade");
        private static readonly int InnerId = Shader.PropertyToID("_Inner");
        private static readonly int InnerColourId = Shader.PropertyToID("_InnerColour");
        private static Material? material;
        private static Mesh? quad;

        private MeshRenderer glass = null!;
        private MaterialPropertyBlock block = null!;
        private RoomSurfaceKind kind;
        private Vector2 size;
        private (Vector2 Size, RoomSurfaceKind Kind, (float, float, float)? Inner)? drawn;

        public RoomSurfaceKind Kind => kind;

        public Vector2 Size => size;

        /// <summary>What draws the surface.</summary>
        public MeshRenderer Glass => glass;

        /// <summary>The lighter sentence bar across the panel: its top below the panel's top, its height and its inset from the sides, in dp; null for none.</summary>
        public (float TopDp, float HeightDp, float InsetDp)? InnerBar { get; set; }

        public static RoomSurface Create(Transform parent, string name, RoomSurfaceKind kind, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var room = go.AddComponent<RoomSurface>();
            room.kind = kind;
            var glassGo = new GameObject("Glass");
            glassGo.transform.SetParent(go.transform, false);
            glassGo.AddComponent<MeshFilter>().sharedMesh = Quad;
            room.glass = glassGo.AddComponent<MeshRenderer>();
            room.glass.sharedMaterial = Material;
            room.glass.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            room.glass.receiveShadows = false;
            room.glass.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            room.glass.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            room.glass.sortingOrder = order;
            room.block = new MaterialPropertyBlock();
            glassGo.SetActive(false);
            return room;
        }

        /// <summary>
        /// Draws the surface <paramref name="size"/> big, in the plane's units, centred on its transform, in its kind's look.
        /// Drawn again only when something changed, so a still frame draws nothing.
        /// </summary>
        public void Draw(Vector2 size)
        {
            if (drawn is var (lastSize, lastKind, lastInner) && lastSize == size && lastKind == kind && lastInner == InnerBar) return;
            drawn = (size, kind, InnerBar);
            this.size = size;
            var radius = Mathf.Min(RoomType.Units(CornerDp(kind)), size.y / 2f);
            var unit = RoomType.Units(1f);
            var width = size.x / unit;
            var height = size.y / unit;
            var look = Looks(kind);
            // The quad reaches past the panel for its shadow: its blur round it and its drop below.
            var margin = look.Shadow ? look.ShadowBlurDp + look.ShadowDropDp : 2f;
            var quadSize = new Vector2(width + 2f * margin, height + 2f * margin);
            glass.transform.localScale = new Vector3(quadSize.x * unit, quadSize.y * unit, 1f);
            glass.transform.localPosition = new Vector3(0f, 0f, RoomType.Units(0.02f));
            block.SetVector(PanelId, new Vector4(width, height, radius / unit, 1f));
            block.SetVector(QuadId, new Vector4(-quadSize.x / 2f, -quadSize.y / 2f, quadSize.x, quadSize.y));
            block.SetVector(FillTopId, Srgb(look.FillTop, look.FillTop.a));
            block.SetVector(FillFootId, Srgb(look.FillFoot, look.FillFoot.a));
            block.SetVector(EdgeTopId, Srgb(look.EdgeTop, look.EdgeDp));
            block.SetVector(EdgeFootId, Srgb(look.EdgeFoot, look.EdgeDp > 0f ? 1f : 0f));
            block.SetVector(ShadeId, new Vector4(look.ShadowDropDp, look.ShadowBlurDp, look.ShadowAlpha, look.Shadow ? 1f : 0f));
            block.SetVector(InnerId, InnerBar is var (innerTop, innerHeight, innerInset) && look.InnerColour.a > 0f
                ? new Vector4(innerTop, innerHeight, look.InnerCornerDp, Mathf.Max(innerInset, 0.01f)) : Vector4.zero);
            block.SetVector(InnerColourId, Srgb(look.InnerColour, look.InnerColour.a));
            glass.SetPropertyBlock(block);
            glass.gameObject.SetActive(true);
        }

        /// <summary>
        /// Every kind's look, the one table DESIGN's numbers drop into. The fills carry their alpha, composited premultiplied over
        /// passthrough; the edge, shadow and sentence bar are each a value here, none in the shader.
        /// </summary>
        public static System.Func<RoomSurfaceKind, RoomGlassLook> Looks { get; set; } = DefaultLook;

        /// <summary>
        /// DESIGN's FINISH-3 panel (2026-11-05): a neutral cool tint #3C4247 to #343A3F at 0.86 (the page's 0.72 as Unity blends
        /// in linear), so the room shows through; a 1 dp white hairline at 10 percent over it (#505559 to #484E52, mixed in sRGB
        /// as the page mixes it); no shadow; the sentence bar white at 10 percent over the tint, corner 10. Every kind the same.
        /// </summary>
        public static RoomGlassLook DefaultLook(RoomSurfaceKind kind) =>
            new RoomGlassLook(WithAlpha(Hex(0x3C4247), TintAlpha), WithAlpha(Hex(0x343A3F), TintAlpha), Hex(0x505559), Hex(0x484E52), 1f, shadow: false,
                innerColour: new Color(1f, 1f, 1f, 0.10f), innerCornerDp: RoomGeometry.SentenceCornerDp);

        /// <summary>The tint's alpha in Unity's linear blend: the page's 0.72 over a white wall (DESIGN's FINISH-3).</summary>
        public const float TintAlpha = 0.86f;

        /// <summary>The kind's corner in dp (DESIGN's FINISH-3): panels 20, a bar 22.</summary>
        public static float CornerDp(RoomSurfaceKind kind) => kind == RoomSurfaceKind.Bar ? 22f : RoomGeometry.PanelCornerDp;

        private static Color WithAlpha(Color colour, float alpha) => new Color(colour.r, colour.g, colour.b, alpha);

        public static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        private static Vector4 Srgb(Color colour, float w) => new Vector4(colour.r, colour.g, colour.b, w);

        private static Material Material
        {
            get
            {
                if (material != null) return material;
                var loaded = Resources.Load<Material>(MaterialPath);
                if (loaded == null)
                {
                    Debug.LogError("Halcyonic: the material Resources/" + MaterialPath + " is missing, so the redesign's glass cannot render.");
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
                quad = new Mesh { name = "Room glass" };
                quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
                quad.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
                quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                quad.RecalculateBounds();
                quad.UploadMeshData(true);
                return quad;
            }
        }

        private void LateUpdate() => RoomSheen.Drive(Time.unscaledTime);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            material = null;
            quad = null;
        }
    }

    /// <summary>One glass kind's look: its fill (with alpha) top to foot, its edge line, its shadow and an optional lighter inner bar.</summary>
    public readonly struct RoomGlassLook
    {
        public RoomGlassLook(Color fillTop, Color fillFoot, Color edgeTop, Color edgeFoot, float edgeDp, bool shadow,
            float shadowDropDp = 10f, float shadowBlurDp = 28f, float shadowAlpha = 0.22f, Color innerColour = default, float innerCornerDp = 0f)
        {
            FillTop = fillTop;
            FillFoot = fillFoot;
            EdgeTop = edgeTop;
            EdgeFoot = edgeFoot;
            EdgeDp = edgeDp;
            Shadow = shadow;
            ShadowDropDp = shadowDropDp;
            ShadowBlurDp = shadowBlurDp;
            ShadowAlpha = shadowAlpha;
            InnerColour = innerColour;
            InnerCornerDp = innerCornerDp;
        }

        public Color FillTop { get; }
        public Color FillFoot { get; }
        public Color EdgeTop { get; }
        public Color EdgeFoot { get; }
        public float EdgeDp { get; }
        public bool Shadow { get; }
        public float ShadowDropDp { get; }
        public float ShadowBlurDp { get; }
        public float ShadowAlpha { get; }

        /// <summary>The inner bar's colour and alpha; transparent where the kind has none.</summary>
        public Color InnerColour { get; }
        public float InnerCornerDp { get; }
    }

    /// <summary>
    /// The redesign's one sheen (BUILD-SPEC-v4 1.2): one band across every glass surface together, in the plane's coordinates,
    /// tilted 20 degrees from vertical, 5 degrees wide, white at 5 percent at its peak (FINISH-3) with a sine fade, crossing left to right
    /// in 2 s, then 8 s of rest. Set once a frame as shader globals; kept still, there is none.
    /// </summary>
    public static class RoomSheen
    {
        public const float PassSeconds = 2f;
        public const float RestSeconds = 8f;
        public const float Peak = 0.05f;
        public const float WidthDegrees = 5f;

        /// <summary>Half the width the band crosses, in metres at the plane's 1 m: the pair's 43.5 degrees and a band beyond.</summary>
        public const float HalfSpan = 0.45f;

        private static readonly int OriginId = Shader.PropertyToID("_RoomSheenOrigin");
        private static readonly int RightId = Shader.PropertyToID("_RoomSheenRight");
        private static readonly int UpId = Shader.PropertyToID("_RoomSheenUp");
        private static readonly int BandId = Shader.PropertyToID("_RoomSheenBand");
        private static int drivenFrame = -1;

        /// <summary>The plane the band crosses: the redesign's parts' parent, set by their owner.</summary>
        public static Transform? Plane { get; set; }

        /// <summary>The band's centre across the plane and its strength at <paramref name="now"/>; strength 0 at rest or kept still.</summary>
        public static (float Centre, float Strength) At(float now)
        {
            if (GlazeMotion.Still) return (0f, 0f);
            var t = now % (PassSeconds + RestSeconds);
            if (t >= PassSeconds) return (0f, 0f);
            var phase = t / PassSeconds;
            return (Mathf.Lerp(-HalfSpan, HalfSpan, phase), Peak * Mathf.Sin(Mathf.PI * phase));
        }

        public static void Drive(float now)
        {
            if (Time.frameCount == drivenFrame) return;
            drivenFrame = Time.frameCount;
            var plane = Plane;
            var (centre, strength) = At(now);
            if (plane == null) strength = 0f;
            else
            {
                Shader.SetGlobalVector(OriginId, plane.position);
                Shader.SetGlobalVector(RightId, plane.right);
                Shader.SetGlobalVector(UpId, plane.up);
            }
            Shader.SetGlobalVector(BandId, new Vector4(centre, 2f * Mathf.Tan(WidthDegrees * Mathf.Deg2Rad / 2f), strength, 0f));
        }
    }

    /// <summary>The redesign's one type size: every word Inter Regular 20 dp on a 28 dp line, larger text times 1.15.</summary>
    public static class RoomType
    {
        /// <summary>A length in dp as the plane's units (1 at its distance).</summary>
        public static float Units(float dp) => KitType.Units(SpatialKit.Degrees(dp));

        /// <summary>A length in dp that grows with the person's text size.</summary>
        public static float Scaled(float dp) => Units(dp) * GlazeText.Scale;

        /// <summary>A label in the one size, its box's top left on its transform.</summary>
        public static TextMeshPro Create(Transform parent, string name, Color colour, int order, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            var label = KitType.Create(parent, name, KitRole.Body, colour, order, alignment, SpatialKit.ReadingLeading, room: true);
            Size(label);
            return label;
        }

        /// <summary>Sizes a label to the one size at the person's text size, its line pitch 28 over 20.</summary>
        public static void Size(TMP_Text label)
        {
            var size = GlazeTokens.FontSize(Scaled(RoomGeometry.TypeDp));
            if (!Mathf.Approximately(label.fontSize, size)) label.fontSize = size;
            var spacing = KitType.LineSpacingFor(label, RoomGeometry.LineDp / RoomGeometry.TypeDp);
            if (!Mathf.Approximately(label.lineSpacing, spacing)) label.lineSpacing = spacing;
        }
    }
}

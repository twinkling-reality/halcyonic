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

        /// <summary>The Type or Talk bar.</summary>
        Bar,

        /// <summary>A command's or exact text's well.</summary>
        Well,

        /// <summary>A quiet choice.</summary>
        Choice,

        /// <summary>The main choice.</summary>
        MainChoice,
    }

    /// <summary>
    /// The one surface every part of the redesign stands on. Panels, the bar and wells are DESIGN's glass (BUILD-SPEC-v4 1.2,
    /// final 2026-11-05, <c>Halcyonic/Room Glass</c>): the frost's gradient (opaque for now; 0.90 is a device test), its
    /// inner edge, a soft shadow under a panel, squircle corners, and the one sheen (<see cref="RoomSheen"/>). The buttons'
    /// kinds keep the flat placeholder until DESIGN's button update (the lead, 2026-11-05). Either way this is the one place
    /// the look changes; a surface draws again only when its size, kind or colour changed.
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

        private Surface surface = null!;
        private MeshRenderer glass = null!;
        private MaterialPropertyBlock block = null!;
        private RoomSurfaceKind kind;
        private Vector2 size;
        private (Vector2 Size, RoomSurfaceKind Kind, Color Fill, (float, float)? Inner)? drawn;

        public RoomSurfaceKind Kind => kind;

        public Vector2 Size => size;

        /// <summary>The flat placeholder a button's kind draws; hidden for glass.</summary>
        public Surface Drawn => surface;

        /// <summary>The glass a panel's, the bar's or a well's kind draws; hidden for a button's.</summary>
        public MeshRenderer Glass => glass;

        /// <summary>A lighter inner bar across the panel (DESIGN's FINISH-3, coming): its top below the panel's top and its height, in dp; null for none.</summary>
        public (float TopDp, float HeightDp)? InnerBar { get; set; }

        /// <summary>A corner in dp in place of the kind's, as a round control's 22 dp squircle (DESIGN, 2026-11-05).</summary>
        public float? CornerOverrideDp { get; set; }

        /// <summary>Whether the kind is drawn in glass now: every kind but the buttons', whose look waits for DESIGN.</summary>
        public static bool IsGlass(RoomSurfaceKind kind) => kind is not (RoomSurfaceKind.Choice or RoomSurfaceKind.MainChoice);

        public static RoomSurface Create(Transform parent, string name, RoomSurfaceKind kind, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var room = go.AddComponent<RoomSurface>();
            room.kind = kind;
            room.surface = Surface.Create(go.transform, "Surface", order);
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

        /// <summary>Becomes another kind, as a choice becoming the main one; drawn as it at the next draw.</summary>
        public void SetKind(RoomSurfaceKind value) => kind = value;

        /// <summary>
        /// Draws the surface <paramref name="size"/> big, in the plane's units, centred on its transform, in its kind's look,
        /// a button's in <paramref name="fill"/> where given. Drawn again only when something changed, so a still frame draws nothing.
        /// </summary>
        public void Draw(Vector2 size, Color? fill = null)
        {
            var colour = fill ?? Fill(kind);
            if (drawn is var (lastSize, lastKind, lastFill, lastInner) && lastSize == size && lastKind == kind && lastFill == colour && lastInner == InnerBar) return;
            drawn = (size, kind, colour, InnerBar);
            this.size = size;
            var radius = Mathf.Min(RoomType.Units(CornerOverrideDp ?? CornerDp(kind)), size.y / 2f);
            if (!IsGlass(kind))
            {
                glass.gameObject.SetActive(false);
                surface.gameObject.SetActive(true);
                surface.Draw(size, radius, colour, new Color(1f, 1f, 1f, 0.06f), RoomType.Units(1f));
                return;
            }
            surface.gameObject.SetActive(false);
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
            block.SetVector(InnerId, InnerBar is var (innerTop, innerHeight) && look.InnerColour.a > 0f
                ? new Vector4(innerTop, innerHeight, look.InnerCornerDp, 1f) : Vector4.zero);
            block.SetVector(InnerColourId, Srgb(look.InnerColour, look.InnerColour.a));
            glass.SetPropertyBlock(block);
            glass.gameObject.SetActive(true);
        }

        /// <summary>
        /// Every glass kind's look, the one table DESIGN's numbers drop into (the lead, 2026-11-05: FINISH-3's translucent tonal
        /// panels, hairlines and a lighter inner bar replace these without a rewrite). The fills carry their alpha, composited
        /// premultiplied over passthrough; the edge, shadow and inner bar are each a value here, none in the shader.
        /// </summary>
        public static System.Func<RoomSurfaceKind, RoomGlassLook> Looks { get; set; } = DefaultLook;

        /// <summary>
        /// BUILD-SPEC-v4 1.2's glass as its opaque colours over the frost: the frost #3B4950 to #2E3A40 at alpha 1 (0.90 is a
        /// device test) with its 1 dp edge (#F2F2F2 at 14 percent: #556167 to #495459) and a shadow 10 dp down, 28 dp of blur,
        /// black at 22 percent; a well sunk (black at 16 percent: #323D43 to #273136), no edge and no shadow; no inner bar yet.
        /// </summary>
        public static RoomGlassLook DefaultLook(RoomSurfaceKind kind) => kind switch
        {
            RoomSurfaceKind.Well => new RoomGlassLook(Hex(0x323D43), Hex(0x273136), Hex(0x323D43), Hex(0x273136), 0f, shadow: false),
            _ => new RoomGlassLook(Hex(0x3B4950), Hex(0x2E3A40), Hex(0x556167), Hex(0x495459), 1f, shadow: true),
        };

        /// <summary>The kind's corner in dp (BUILD-SPEC-v4 1.2): panels 34, the bar 30, rows 24, insets 16.</summary>
        public static float CornerDp(RoomSurfaceKind kind) => kind switch
        {
            RoomSurfaceKind.Bar => 30f,
            RoomSurfaceKind.Choice or RoomSurfaceKind.MainChoice => 24f,
            RoomSurfaceKind.Well => 16f,
            _ => 34f,
        };

        /// <summary>The kind's one colour: the frost's top for glass, a button's placeholder fill (its look on hold).</summary>
        public static Color Fill(RoomSurfaceKind kind) => kind switch
        {
            RoomSurfaceKind.MainChoice => Hex(0xD5DADE),
            RoomSurfaceKind.Choice => Hex(0x66737A),
            RoomSurfaceKind.Well => Hex(0x323D43),
            _ => Hex(0x3B4950),
        };

        /// <summary>The words' colour on the kind: dark on the main choice, white elsewhere.</summary>
        public static Color Words(RoomSurfaceKind kind) => kind == RoomSurfaceKind.MainChoice ? Hex(0x172024) : Color.white;

        /// <summary>The muted grey for quieter words on a panel.</summary>
        public static Color Muted => Hex(0xC3CBCF);

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
    /// tilted 20 degrees from vertical, 5 degrees wide, white at 7 percent at its peak with a sine fade, crossing left to right
    /// in 2 s, then 8 s of rest. Set once a frame as shader globals; kept still, there is none.
    /// </summary>
    public static class RoomSheen
    {
        public const float PassSeconds = 2f;
        public const float RestSeconds = 8f;
        public const float Peak = 0.07f;
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

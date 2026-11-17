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

        /// <summary>A bar's surface, in the panel's material.</summary>
        Bar,

        /// <summary>A name tag's surface, or the plate that names the agent while it works, in the panel's material: corner 14 dp (DESIGN 1.2).</summary>
        Tag,
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
        private static readonly int ShownId = Shader.PropertyToID("_Shown");
        private static readonly int LitEdgeId = Shader.PropertyToID("_LitEdge");

        /// <summary>A lit left edge, its width in dp and its strength (the question's dialog, after Mirror's Edge's); null for none.</summary>
        public (float WidthDp, float Strength)? LitEdge { get; set; }
        private float shown = 1f;

        /// <summary>
        /// How far the surface is in view, 0 to 1: its fill, edge, sentence bar and shadow scaled by it together (an
        /// arrival or a leave, DESIGN 1.5). Set at once; a drawn surface keeps its shape.
        /// </summary>
        public float Shown
        {
            get => shown;
            set
            {
                var clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(clamped, shown)) return;
                shown = clamped;
                if (glass == null || block == null) return;
                block.SetFloat(ShownId, shown);
                glass.SetPropertyBlock(block);
            }
        }
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
        /// <summary>
        /// A ring in place of the surface's own hairline, drawn as its edge so it follows the corner exactly: a picked name
        /// tag's 1.5 dp of #E4E8EA (DESIGN 1.2). Null for the kind's own edge.
        /// </summary>
        public (Color Colour, float WidthDp)? Ring { get; set; }

        private (Color Colour, float WidthDp)? ringDrawn;

        public void Draw(Vector2 size)
        {
            if (drawn is var (lastSize, lastKind, lastInner) && lastSize == size && lastKind == kind && lastInner == InnerBar && ringDrawn == Ring) return;
            drawn = (size, kind, InnerBar);
            ringDrawn = Ring;
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
            block.SetVector(EdgeTopId, Ring is var (ring, ringDp) ? Srgb(ring, ringDp) : Srgb(look.EdgeTop, look.EdgeDp));
            block.SetVector(EdgeFootId, Ring is var (ringFoot, _) ? Srgb(ringFoot, 1f) : Srgb(look.EdgeFoot, look.EdgeDp > 0f ? 1f : 0f));
            block.SetVector(ShadeId, new Vector4(look.ShadowDropDp, look.ShadowBlurDp, look.ShadowAlpha, look.Shadow ? 1f : 0f));
            block.SetVector(LitEdgeId, LitEdge is var (litDp, litStrength) ? new Vector4(litDp, litStrength, 0f, 0f) : Vector4.zero);
            // The panel's one sentence stands under a hairline, never in a strip of its own (F6, 2026-11-10: a lighter strip read as a
            // button): a 1 dp rule where the sentence's place starts, from the words' left edge to their right.
            block.SetVector(InnerId, InnerBar is var (innerTop, _, _) && look.InnerColour.a > 0f
                ? new Vector4(innerTop, 1f, 0f, RoomGeometry.PanelPadDp * GlazeText.Scale) : Vector4.zero);
            block.SetVector(InnerColourId, Srgb(look.InnerColour, look.InnerColour.a));
            block.SetFloat(ShownId, shown);
            glass.SetPropertyBlock(block);
            glass.gameObject.SetActive(true);
        }

        /// <summary>
        /// Every kind's look, the one table the glass's numbers drop into. The fills carry their alpha, composited premultiplied over
        /// passthrough; the edge, shadow and sentence bar are each a value here, none in the shader. It follows <see cref="Theme"/>.
        /// </summary>
        public static System.Func<RoomSurfaceKind, RoomGlassLook> Looks { get; set; } = DefaultLook;

        private static PanelTheme theme = PanelTheme.Dark;

        /// <summary>
        /// The panels' glass, Dark (the default) or Light: the one setter the person's Appearance choice calls, at start and on
        /// choosing. Every surface drawn takes the new look at once (the change's ease is the caller's), with nothing else moved.
        /// </summary>
        public static PanelTheme Theme
        {
            get => theme;
            set
            {
                if (theme == value) return;
                theme = value;
                Looks = value == PanelTheme.Light ? LightLook : DarkLook;
                ThemeChanged?.Invoke();
            }
        }

        /// <summary>Raised when <see cref="Theme"/> changes, after <see cref="Looks"/> follows it: every surface draws again, and inks that follow the glass read it.</summary>
        public static event System.Action? ThemeChanged;

        private void OnEnable() => ThemeChanged += DrawAgain;

        private void OnDisable() => ThemeChanged -= DrawAgain;

        /// <summary>The look changed under it: drawn again at its size, kind and sentence bar as they stand.</summary>
        private void DrawAgain()
        {
            if (!(drawn is var (lastSize, _, _))) return;
            drawn = null;
            Draw(lastSize);
        }

        /// <summary>The default look: <see cref="DarkLook"/>.</summary>
        public static RoomGlassLook DefaultLook(RoomSurfaceKind kind) => DarkLook(kind);

        /// <summary>
        /// The owner's reference (the lead's 2026-11-16 13:33Z: equipped-test's ask panel, .eq-ask-bar): black glass
        /// rgba(12,14,22,0.72) under blur(40px) saturate(140%), one inset 1 px hairline of white 0.08, one long soft drop
        /// 0 24px 60px black 0.35, dividers of white 0.08, no top light, no sheen, no glow. Unity has no backdrop blur and blends in
        /// linear, so the PIXELS are matched, not the alphas: the reference rendered in Chrome over the kit's three rooms (#1E1C1A,
        /// #B9AE9E, #EEEEEE) reads its face #121217, #3E3A3A, #4B4C52, and this face lands within 7 of 255 of each in Unity's
        /// blend; the hairline within 13 (the page whitens a face whose colour follows the room, which one linear colour cannot
        /// on all three). The shadow at the reference's proportion to its 640 px, at the panel's 440 dp. The squircle corners stay
        /// (the owner's 2026-11-05 glass squircles). Every kind the same: the kit's one glass token, <see cref="AskGlass"/>.
        /// </summary>
        public static RoomGlassLook DarkLook(RoomSurfaceKind kind) =>
            new RoomGlassLook(WithAlpha(Hex(AskGlass.FaceColour), DarkAlpha), WithAlpha(Hex(AskGlass.FaceColour), DarkAlpha), Hex(LitEdgeTop), Hex(LitEdgeFoot), 1f, shadow: DrawsShadow,
                shadowDropDp: 24f * ShadowScale, shadowBlurDp: 60f * ShadowScale, shadowAlpha: 0.35f, innerColour: new Color(1f, 1f, 1f, AskGlass.HairlineAlpha), innerCornerDp: 0f);

        /// <summary>
        /// The hairline's top and foot (the one language: visionos-glass-window.png's glass catches light along its top edge): a thin
        /// white highlight at the top, #8E9199, easing to #2B2C33 at the foot (the lead's 19:41Z, AUDIT's kit pass at 9 (6)).
        /// </summary>
        public const int LitEdgeTop = 0x8E9199;
        public const int LitEdgeFoot = 0x2B2C33;

        /// <summary>
        /// Halcyonic's own Light, not the owner's reference (it has none; the lead's 13:33Z marks it ours): the same structure
        /// inverted, a frosted near-white rgba(246,247,250,0.80), a hairline of rgb(12,14,22) at 0.08, a drop of rgb(12,14,22) at
        /// 0.18, matched the same way: the page's face over the three rooms (#CBCCCD, #EBE9E6, #F5F6F8) within 5 of 255.
        /// </summary>
        public static RoomGlassLook LightLook(RoomSurfaceKind kind) =>
            new RoomGlassLook(WithAlpha(Hex(0xFBFCFE), LightAlpha), WithAlpha(Hex(0xFBFCFE), LightAlpha), Hex(0xE3E4E8), Hex(0xE3E4E8), 1f, shadow: DrawsShadow,
                shadowDropDp: 24f * ShadowScale, shadowBlurDp: 60f * ShadowScale, shadowAlpha: 0.18f, innerColour: new Color(12f / 255f, 14f / 255f, 22f / 255f, 0.08f), innerCornerDp: 0f);

        /// <summary>
        /// Whether the glass draws the reference's long soft shadow: not yet. It is drawn in the glass's own quad, so every measure of
        /// what a panel draws (the bar's envelope, its crossings with the notice, Watch It Work's lift over the bar, the field)
        /// would read 57 dp of shadow round each panel as panel (F220 on 36d10e286). It waits for a renderer of its own that those
        /// measures leave out; its numbers stand ready in the looks.
        /// </summary>
        public const bool DrawsShadow = false;

        /// <summary>
        /// The dark face's alpha in Unity's linear blend over the room: denser than the owner's Ask glass (AskGlass.FaceAlpha 0.918), since
        /// Quest draws no blur of the passthrough behind it and a bright room read through as a muddy patch (the critics at 9, the lead's
        /// 19:41Z: visionos-glass-window.png's thick, even glass). The room now gives 4 percent of what is seen, not 8.
        /// </summary>
        public const float DarkAlpha = 0.96f;

        /// <summary>The light face's alpha in Unity's linear blend, solved the same way.</summary>
        public const float LightAlpha = 0.64f;

        /// <summary>The panel's 440 dp to the reference's 640 px: what its shadow's drop and blur scale by.</summary>
        public const float ShadowScale = RoomGeometry.PanelDp / 640f;

        /// <summary>The tint's alpha the glass had before the reference (DESIGN's FINISH-3), kept for the readers that measure against it.</summary>
        public const float TintAlpha = DarkAlpha;

        /// <summary>The kind's corner in dp (DESIGN's FINISH-3): panels 20, a bar 22, a name tag 14.</summary>
        public static float CornerDp(RoomSurfaceKind kind) => kind == RoomSurfaceKind.Bar ? 22f : kind == RoomSurfaceKind.Tag ? 14f : RoomGeometry.PanelCornerDp;

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
        /// <summary>None: the owner's reference has no sheen (the lead's 2026-11-16 13:33Z: no top light, no sheen, no glow).</summary>
        public const float Peak = 0f;
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
        /// <summary>
        /// A length in dp as the plane's units (1 at its distance): dp times one dp's length, as DESIGN's page lays them, so a
        /// 440 dp panel is 440 one-dp lengths wide (the tangent of its whole angle would lay it about 8 percent wider).
        /// </summary>
        public static float Units(float dp) => dp * KitType.Units(SpatialKit.Degrees(1f));

        /// <summary>A length in dp that grows with the person's text size.</summary>
        public static float Scaled(float dp) => Units(dp) * GlazeText.Scale;

        /// <summary>The tracking of every word in the one size (BUILD-SPEC-v4 1.3), in hundredths of an em.</summary>
        public const float Tracking = -0.6f;

        /// <summary>A label in the one size, its box's top left on its transform.</summary>
        public static TextMeshPro Create(Transform parent, string name, Color colour, int order, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            var label = KitType.Create(parent, name, KitRole.Body, colour, order, alignment, SpatialKit.ReadingLeading, room: true);
            Size(label);
            return label;
        }

        /// <summary>A label for exact text, a request as the runtime reported it: JetBrains Mono 19 dp on the same 28 dp line, its box's top left on its transform.</summary>
        /// <summary>
        /// The one language's label role (Mirror's Edge's wide capitals, the lead's 2026-11-17 00:54Z): our labels and states in
        /// spaced capitals, 18 dp, 0.24 em apart, quiet; titles and the person's or the agent's words keep their own case. 18, not
        /// the sheet's 15: on a panel or a notice the eye meets at an angle, 15 read 0.75 to 0.87 degrees, under the 14 dp floor
        /// (F265 and F266, 2026-11-17); at 18 the least of them reads 0.90.
        /// 0.53 in Unity's linear blend (the lead's 2026-11-17 02:29Z ruling): 4.5 to 1 over the glass where the bright room shows
        /// through it, about 190 of 255 on the glass over a dark room; quiet by its size, capitals and tracking rather than by brightness.
        /// 0.28 read 135 to 150 on the dark room's glass but only 3.35 to 3.7 to 1 over the bright room's (F267 and F269).
        /// </summary>
        public const float LabelDp = 18f;
        public const float LabelTracking = 24f;
        public const float LabelAlpha = 0.53f;

        /// <summary>Sets <paramref name="label"/> in the label role: <paramref name="words"/> in spaced capitals at <see cref="LabelDp"/>.</summary>
        public static void Label(TMP_Text label, string words)
        {
            Size(label);
            label.fontSize *= LabelDp / RoomGeometry.TypeDp;
            label.characterSpacing = LabelTracking;
            GlazeText.SetLiteral(label, words.ToUpperInvariant());
            label.alpha = LabelAlpha;
        }

        public static TextMeshPro CreateMono(Transform parent, string name, Color colour, int order)
        {
            var label = KitType.Create(parent, name, KitRole.Mono, colour, order, TextAlignmentOptions.TopLeft, SpatialKit.ReadingLeading, room: true);
            SizeMono(label);
            return label;
        }

        /// <summary>Sizes an exact-text label at the person's text size: 19 dp on the 28 dp line.</summary>
        public static void SizeMono(TMP_Text label)
        {
            var size = GlazeTokens.FontSize(Scaled(RoomGeometry.MonoTypeDp));
            if (!Mathf.Approximately(label.fontSize, size)) label.fontSize = size;
            var spacing = KitType.LineSpacingFor(label, RoomGeometry.LineDp / RoomGeometry.MonoTypeDp);
            if (!Mathf.Approximately(label.lineSpacing, spacing)) label.lineSpacing = spacing;
        }

        /// <summary>Sizes a label to the one size at the person's text size, its line pitch 28 over 20.</summary>
        public static void Size(TMP_Text label)
        {
            var size = GlazeTokens.FontSize(Scaled(RoomGeometry.TypeDp));
            if (!Mathf.Approximately(label.fontSize, size)) label.fontSize = size;
            var spacing = KitType.LineSpacingFor(label, RoomGeometry.LineDp / RoomGeometry.TypeDp);
            if (!Mathf.Approximately(label.lineSpacing, spacing)) label.lineSpacing = spacing;
            // DESIGN's tracking for every word, in hundredths of an em: -0.6 percent.
            if (!Mathf.Approximately(label.characterSpacing, Tracking)) label.characterSpacing = Tracking;
        }
    }
}

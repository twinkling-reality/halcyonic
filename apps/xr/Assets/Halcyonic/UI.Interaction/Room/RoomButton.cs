#nullable enable
using System;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>How an action shows now (DESIGN's BUILD-SPEC-v4 1.4).</summary>
    public enum RoomButtonLook
    {
        Rest,
        Main,
        Pointed,
        MainPointed,
        Pressed,
        Disabled,
    }

    /// <summary>How big an action stands: the column's, or the row's at home (DESIGN's FINISH-3).</summary>
    public enum RoomButtonSize
    {
        /// <summary>A 48 dp core, a 32 dp chip, a 20 dp glyph.</summary>
        Action,

        /// <summary>The row at rest: the 60.8 dp hand target itself drawn as the core, a 44 dp chip, a 24 dp glyph.</summary>
        Home,
    }

    /// <summary>
    /// The one button of the redesign (DESIGN's BUILD-SPEC-v4 1.4, FINISH-2 and FINISH-3 2026-11-05): no box and no edge, ever.
    /// An action is a chip holding its glyph, then its words in the one type size, over a faint halo of the panel's foot tint
    /// #343A3F that is solid inside the core, half at its edge and gone 12 dp out (<c>Halcyonic/Room Halo</c>); the main
    /// action's chip is light (#D5DADE) with a dark glyph, its words still white. Pointed it grows 3 percent, held it shrinks to
    /// 96 percent; Confirm's lock is a ring round its chip (<see cref="LockProgress"/>). An action that cannot act is not drawn.
    /// Every target is Meta's 60.8 dp hand target at least. It takes a press by <see cref="KitPress"/>'s rules.
    /// </summary>
    public sealed class RoomButton : MonoBehaviour
    {
        /// <summary>A target's least height and width, in dp: Meta's hand target (<see cref="SpatialKit.TargetDegrees"/>).</summary>
        public static float HeightDp => SpatialKit.TargetDegrees / SpatialKit.Degrees(1f);

        /// <summary>A home action's chip (corner 16) and glyph, its words 12 dp after the chip, 24 dp after them, a halo corner of 28; its core is the hand target, <see cref="HeightDp"/>.</summary>
        public const float HomeChipDp = 44f;
        public const float HomeChipCornerDp = 16f;
        public const float HomeGlyphDp = 24f;
        public const float HomeWordsGapDp = 12f;
        public const float HomeRightDp = 24f;
        public const float HomeHaloCornerDp = 28f;

        /// <summary>An action's core: 48 dp tall, 8 dp above, below and at its left, 20 dp at its right; its chip 32 dp, corner 12, glyph 20; words 10 dp after.</summary>
        public const float CoreDp = 48f;
        public const float CorePadDp = 8f;
        public const float CoreRightDp = 20f;
        public const float ChipDp = 32f;
        public const float ChipCornerDp = 12f;
        public const float GlyphDp = 20f;
        public const float WordsGapDp = 10f;
        public const float HaloCornerDp = 24f;
        public const float HaloReachDp = 12f;

        /// <summary>Confirm's lock ring round the chip: radius 19 dp, 2 dp wide.</summary>
        public const float RingRadiusDp = 19f;
        public const float RingWidthDp = 2f;

        private const float PointedScale = 1.03f;
        private const float PressedScale = 0.96f;
        private const float DisabledOpacity = 0.45f;
        private const float ScaleRate = 20f;

        private static readonly int CoreId = Shader.PropertyToID("_Core");
        private static readonly int QuadId = Shader.PropertyToID("_Quad");
        private static readonly int ChipId = Shader.PropertyToID("_Chip");
        private static readonly int ChipColourId = Shader.PropertyToID("_ChipColour");
        private static readonly int RingId = Shader.PropertyToID("_Ring");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static Material? material;
        private static Mesh? quad;

        private Transform visual = null!;
        private MeshRenderer halo = null!;
        private MaterialPropertyBlock block = null!;
        private TextMeshPro words = null!;
        private TextMeshPro glyph = null!;
        private KitPress press = null!;
        private RoomButtonSize size;
        private bool main;
        private bool held;
        private Vector2 core;
        private float chipCentreX;
        private RoomButtonLook? painted;
        private float? paintedLock;

        public event Action? Pressed;

        public KitPress Press => press;

        public TextMeshPro Words => words;

        public TextMeshPro Glyph => glyph;

        /// <summary>The target's size, in its parent's units.</summary>
        public Vector2 Size => press.Size;

        /// <summary>The core as drawn, the halo's solid middle, in its parent's units.</summary>
        public Vector2 Core => core;

        public bool Main => main;

        /// <summary>How the button shows now.</summary>
        public RoomButtonLook Look => painted ?? RoomButtonLook.Rest;

        public bool Available
        {
            get => press.Available;
            set => press.Available = value;
        }

        /// <summary>Whether a hand holds the button now, for hold to talk.</summary>
        public bool Held => held;

        /// <summary>Confirm's lock, 0 to 1 filled while it holds, or null where there is none: a ring round the chip, and no pointed response.</summary>
        public float? LockProgress { get; set; }

        /// <summary>
        /// Stands the action behind Confirm's lock, its ring <paramref name="progress"/> full, taking no press; null lifts the
        /// lock, and it can be pressed from then, a press already under way dropped (<see cref="KitPress.Available"/>).
        /// </summary>
        public void Lock(float? progress)
        {
            LockProgress = progress;
            press.Available = progress == null;
            if (gameObject.activeSelf) Paint(immediately: false);
        }

        public static RoomButton Create(Transform parent, string name, RoomButtonSize size = RoomButtonSize.Action, int order = KitType.Order.Control)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var button = go.AddComponent<RoomButton>();
            button.size = size;
            // What a look scales: the drawing alone, so pointing or holding never moves the target or its neighbours' gaps.
            button.visual = new GameObject("Visual").transform;
            button.visual.SetParent(go.transform, false);
            var haloGo = new GameObject("Halo and chip");
            haloGo.transform.SetParent(button.visual, false);
            haloGo.AddComponent<MeshFilter>().sharedMesh = Quad;
            button.halo = haloGo.AddComponent<MeshRenderer>();
            button.halo.sharedMaterial = Material;
            button.halo.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            button.halo.receiveShadows = false;
            button.halo.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            button.halo.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            button.halo.sortingOrder = order;
            button.block = new MaterialPropertyBlock();
            button.words = RoomType.Create(button.visual, "Words", Color.white, order + 2, TextAlignmentOptions.Left);
            button.words.rectTransform.pivot = new Vector2(0f, 0.5f);
            button.words.textWrappingMode = TextWrappingModes.NoWrap;
            button.glyph = GlazeIcons.Create(button.visual, "Glyph", SpatialKit.IconDegrees, Color.white, order + 3);
            var height = RoomType.Units(HeightDp);
            button.press = KitPress.Create(go, new Vector2(height, height));
            button.press.Pressed += () => button.Pressed?.Invoke();
            button.press.Target.Selected += () => button.held = true;
            button.press.Target.Released += _ => button.held = false;
            go.SetActive(false);
            return button;
        }

        /// <summary>The core's height in dp: 48 for the column's actions, the hand target at home.</summary>
        public float CoreHeightDp => size == RoomButtonSize.Home ? HeightDp : CoreDp;

        private float ChipSizeDp => size == RoomButtonSize.Home ? HomeChipDp : ChipDp;

        /// <summary>The room left of the chip: what leaves it centred in the core's height (8 dp, 8.4 at home).</summary>
        private float ChipPadDp => (CoreHeightDp - ChipSizeDp) / 2f;

        private float GapDp => size == RoomButtonSize.Home ? HomeWordsGapDp : WordsGapDp;

        /// <summary>The core's room after its words, in dp.</summary>
        public float RightDp => size == RoomButtonSize.Home ? HomeRightDp : CoreRightDp;

        /// <summary>The core's width with these words at the person's text size, in the plane's units: content-sized.</summary>
        public float Measure(string text)
        {
            RoomType.Size(words);
            return RoomType.Units(ChipPadDp + ChipSizeDp + GapDp + RightDp) + words.GetPreferredValues(LabelText.ForTextMeshPro(text)).x;
        }

        /// <summary>Where its words end, from its centre, in its parent's units: what stands after them inside the halo starts here.</summary>
        public float WordsEnd => words.transform.localPosition.x + words.GetPreferredValues(words.text).x;

        /// <summary>
        /// Shows the button centred at <paramref name="centre"/>, content-sized (<paramref name="width"/> its least core width).
        /// Its target is the core grown to Meta's hand target.
        /// </summary>
        public void Show(string text, GlazeIcon? icon, bool isMain, bool available, Vector2 centre, float width)
        {
            // An action that cannot act yet is not drawn (BUILD-SPEC-v4 1.4, 2026-11-05): no disabled look; it arrives when it can act.
            if (!available)
            {
                Hide();
                return;
            }
            var literal = LabelText.ForTextMeshPro(text);
            var changed = !gameObject.activeSelf || words.text != literal || main != isMain;
            main = isMain;
            RoomType.Size(words);
            GlazeText.SetLiteral(words, text);
            var target = RoomType.Units(HeightDp);
            var coreWidth = Mathf.Max(width, Measure(text));
            core = new Vector2(coreWidth, RoomType.Units(CoreHeightDp));
            chipCentreX = -coreWidth / 2f + RoomType.Units(ChipPadDp + ChipSizeDp / 2f);
            words.transform.localPosition = new Vector3(chipCentreX + RoomType.Units(ChipSizeDp / 2f + GapDp), 0f, -RoomType.Units(0.5f));
            press.Resize(new Vector2(Mathf.Max(target, coreWidth), Mathf.Max(target, core.y)));
            Lay(icon, RoomType.Units(size == RoomButtonSize.Home ? HomeGlyphDp : GlyphDp));
            LockProgress = null;
            press.Available = available;
            transform.localPosition = new Vector3(centre.x, centre.y, -RoomType.Units(1f));
            if (changed) press.Unsettle();
            painted = null;
            gameObject.SetActive(true);
            Paint(immediately: true);
        }

        public void Hide() => gameObject.SetActive(false);

        private void Lay(GlazeIcon? icon, float glyphEm)
        {
            glyph.gameObject.SetActive(icon.HasValue);
            if (icon is not GlazeIcon shown) return;
            glyph.fontSize = GlazeTokens.FontSize(glyphEm);
            glyph.rectTransform.sizeDelta = new Vector2(glyphEm, glyphEm);
            GlazeIcons.Show(glyph, shown);
            glyph.transform.localPosition = new Vector3(chipCentreX, 0f, -RoomType.Units(0.6f));
        }

        private void Update() => Paint(immediately: false);

        /// <summary>For renders, which have no hands: the look to show in place of the one its state gives, or null for its own.</summary>
        public RoomButtonLook? LookForRender { get; set; }

        /// <summary>The button's look from its state: disabled, held, pointed, or at rest, main or not; a locked Confirm never points.</summary>
        public RoomButtonLook LookNow()
        {
            if (LookForRender is RoomButtonLook forced) return forced;
            // Confirm behind its lock is a rest action with its ring, its words whole white: it only takes no press yet.
            if (LockProgress != null) return RoomButtonLook.Rest;
            if (!press.Available) return RoomButtonLook.Disabled;
            if (held || press.Pressing) return RoomButtonLook.Pressed;
            var pointed = press.Hovered && LockProgress == null;
            return main ? (pointed ? RoomButtonLook.MainPointed : RoomButtonLook.Main) : (pointed ? RoomButtonLook.Pointed : RoomButtonLook.Rest);
        }

        private void Paint(bool immediately)
        {
            var look = LookNow();
            var scale = look switch
            {
                RoomButtonLook.Pressed => PressedScale,
                RoomButtonLook.Pointed or RoomButtonLook.MainPointed => PointedScale,
                _ => 1f,
            };
            var now = visual.localScale.x;
            var next = immediately || GlazeMotion.Still ? scale : Mathf.MoveTowards(now, scale, ScaleRate * Time.unscaledDeltaTime * Mathf.Abs(scale - now) + 1e-4f);
            if (!Mathf.Approximately(now, next)) visual.localScale = new Vector3(next, next, 1f);
            if (painted == look && Nullable.Equals(paintedLock, LockProgress)) return;
            painted = look;
            paintedLock = LockProgress;
            var unit = RoomType.Units(1f);
            var reach = HaloReachDp + 1f;
            var quadSize = new Vector2(core.x / unit + 2f * reach, core.y / unit + 2f * reach);
            halo.transform.localScale = new Vector3(quadSize.x * unit, quadSize.y * unit, 1f);
            halo.transform.localPosition = new Vector3(0f, 0f, RoomType.Units(0.02f));
            // DESIGN's linear-space alphas on #343A3F (FINISH-3): rest 0.83, main 0.87, pointed 0.90, main pointed 0.93 (page 0.66 to 0.82).
            var haloAlpha = look switch
            {
                RoomButtonLook.Main => 0.87f,
                RoomButtonLook.Pointed or RoomButtonLook.Pressed => 0.90f,
                RoomButtonLook.MainPointed => 0.93f,
                _ => 0.83f,
            };
            var light = RoomSurface.Hex(0xD5DADE);
            var (chipColour, chipAlpha) = look switch
            {
                RoomButtonLook.Main => (light, 1f),
                RoomButtonLook.MainPointed => (Color.white, 1f),
                RoomButtonLook.Pressed => (light * 0.84f, 1f),
                RoomButtonLook.Pointed => (Color.white, 0.13f),
                _ => (Color.white, 0.05f),
            };
            var chipDp = ChipSizeDp;
            var cornerDp = size == RoomButtonSize.Home ? HomeChipCornerDp : ChipCornerDp;
            block.SetVector(CoreId, new Vector4(core.x / unit, core.y / unit, size == RoomButtonSize.Home ? HomeHaloCornerDp : HaloCornerDp, haloAlpha));
            block.SetVector(QuadId, new Vector4(-quadSize.x / 2f, -quadSize.y / 2f, quadSize.x, quadSize.y));
            block.SetVector(ChipId, new Vector4(chipCentreX / unit, 0f, chipDp, cornerDp));
            block.SetVector(ChipColourId, new Vector4(chipColour.r, chipColour.g, chipColour.b, chipAlpha));
            block.SetVector(RingId, new Vector4(RingRadiusDp, RingWidthDp, LockProgress ?? 0f, LockProgress != null ? 1f : 0f));
            var fade = look == RoomButtonLook.Disabled ? DisabledOpacity : 1f;
            block.SetFloat(FadeId, fade);
            halo.SetPropertyBlock(block);
            var dark = look is RoomButtonLook.Main or RoomButtonLook.MainPointed or RoomButtonLook.Pressed;
            glyph.color = dark ? RoomSurface.Hex(0x172024) : new Color(1f, 1f, 1f, fade);
            words.color = Color.white;
        }

        private static Material Material
        {
            get
            {
                if (material != null) return material;
                var loaded = Resources.Load<Material>("HalcyonicUI/RoomHalo");
                if (loaded == null)
                {
                    Debug.LogError("Halcyonic: the material Resources/HalcyonicUI/RoomHalo is missing, so the redesign's actions cannot render.");
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
                quad = new Mesh { name = "Room halo" };
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

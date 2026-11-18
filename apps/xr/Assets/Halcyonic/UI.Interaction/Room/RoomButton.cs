#nullable enable
using System;
using System.Linq;
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

        /// <summary>
        /// An answer's mark in the chip's place (DESIGN 1.4): 22 dp, a circle for one of several, a squircle box of corner 7
        /// where any number may be chosen; a 2 dp line, white at the page's 80 percent (0.66 in Unity's linear blend over the
        /// halo); chosen, a #D5DADE disc or box with a #1F1F1F check 15 dp.
        /// </summary>
        public const float MarkDp = 22f;
        public const float MarkBoxCornerDp = 7f;
        public const float MarkLineDp = 2f;
        public const float MarkLineAlpha = 0.66f;
        public const float MarkCheckDp = 15f;

        /// <summary>An action's words are never cut: no ellipsis, as many lines as they need (this many at the very most), its box and backing as tall as its lines.</summary>
        public const int WordLines = 400;

        /// <summary>
        /// Confirm's lock ring (the addendum's A17, the lead's 0000Z and 0529Z: the ring must not overhang the chip): whole
        /// outside the chip, 4 dp clear of its squircle's edge on its middle line, 2 dp wide, its track white at 34 percent
        /// and its fill white, round the main action's own chip.
        /// </summary>
        public const float RingGapDp = 4f;
        public const float RingWidthDp = 2f;

        private const float DisabledOpacity = 0.45f;

        /// <summary>A chosen answer's mark: white, no hue in the chrome (the lead's 2026-11-17 19:15Z; visionos-label-vibrant-primary.png's white).</summary>
        public const int AccentColour = 0xFFFFFF;

        /// <summary>The ink of a chosen answer's check on its mark.</summary>
        public const int InkColour = 0x1F1F1F;

        /// <summary>
        /// No hue in the chrome (the lead's 2026-11-17 19:15Z and 19:18Z (2); visionos-window-example.png's thin control and
        /// visionos-label-vibrant-primary.png's vibrant white): the moment's one main action stands apart by its lighter thin-glass
        /// capsule (<see cref="MainPlateColour"/>) alone, its glyph and words white: no outline, no hue, no glow (AUDIT, 19:21Z: neither file
        /// shows an edge on a control). The colour stays for the Listening halo, which is white.
        /// </summary>
        public const int RimColour = 0xFFFFFF;

        /// <summary>A destructive main action's rim and glyph: white as any main action's, no hue; its cross glyph and its words say it removes.</summary>
        public const int DestructiveRimColour = 0xFFFFFF;

        /// <summary>The rim's width and its glow's reach, in dp.</summary>
        public const float RimDp = 1.2f;
        public const float RimGlowDp = 2f;

        /// <summary>An action's chip at rest: a dark well, black at the page's 0.45 over the halo (0.68 in Unity's linear blend), so its white glyph holds 4.5 to 1 over a bright wall.</summary>
        public const float WellAlpha = 0.68f;

        private static readonly int CoreId = Shader.PropertyToID("_Core");
        private static readonly int QuadId = Shader.PropertyToID("_Quad");
        private static readonly int ChipId = Shader.PropertyToID("_Chip");
        private static readonly int ChipColourId = Shader.PropertyToID("_ChipColour");
        private static readonly int HaloColourId = Shader.PropertyToID("_HaloColour");
        private static readonly int RingId = Shader.PropertyToID("_Ring");
        private static readonly int MarkId = Shader.PropertyToID("_Mark");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly int RimId = Shader.PropertyToID("_Rim");
        private static readonly int RimColourId = Shader.PropertyToID("_RimColour");
        private static Material? material;
        private static Mesh? quad;

        private Transform visual = null!;
        private MeshRenderer halo = null!;
        private MaterialPropertyBlock block = null!;
        private TextMeshPro words = null!;
        private RoomGlyphView glyph = null!;
        private RoomGlyph? glyphShown;
        private float glyphDp;
        private KitPress press = null!;
        private RoomButtonSize size;
        private bool main;
        private bool destructive;
        private AnswerMark mark;
        private bool chosen;
        private string? shownWords;
        private string? shownAside;

        /// <summary>What the shown action does (its control's action and key): a new command in the same place arrives anew.</summary>
        private string? shownDoing;
        private RoomShape asidePlate = null!;
        private TextMeshPro asideWords = null!;
        private RoomShape asideMark = null!;
        private RoomGlyphView asideCheck = null!;
        private float asideWidth;
        private bool held;
        private Vector2 core;
        private float chipCentreX;
        private RoomButtonLook? painted;
        private bool paintedLocked;
        private (bool Main, bool Destructive, AnswerMark Mark, bool Chosen)? paintedKind;

        /// <summary>The look it eases from and to (its chip's colour and alpha, its halo, its glyph's colour, its scale), and when the easing started.</summary>
        private Vector4 chipFrom, chipTo, chipNow;
        private Vector4 haloFrom, haloTo, haloNow;
        private Color glyphFrom, glyphTo, glyphNow;
        private float scaleFrom = 1f, scaleTo = 1f, scaleNow = 1f;
        private float lookSince = float.NegativeInfinity;

        /// <summary>When it arrived and after how many others of the same moment, and how it was last drawn, so a still frame sets nothing.</summary>
        private float arriveSince = float.NegativeInfinity;
        private int arriveIndex;
        private (Vector4 Chip, float Halo, Color Glyph, float Scale, float Shown, float Lower, float Lock)? pushed;
        private static float arrivalStamp = float.NegativeInfinity;
        private static int arrivalCount;

        public event Action? Pressed;

        public KitPress Press => press;

        public TextMeshPro Words => words;

        /// <summary>The glyph in its chip, drawn from DESIGN's geometry (<see cref="RoomGlyphs"/>).</summary>
        public RoomGlyphView Glyph => glyph;

        /// <summary>The target's size, in its parent's units.</summary>
        public Vector2 Size => press.Size;

        /// <summary>The glass key as drawn: the soft slab behind the chip and the words, its quad <see cref="HaloReachDp"/> and a dp past the core each side.</summary>
        public MeshRenderer Key => halo;

        /// <summary>The core as drawn, the halo's solid middle, in its parent's units.</summary>
        public Vector2 Core => core;

        public bool Main => main;

        /// <summary>The answer's mark it carries in its chip's place, and whether it shows chosen.</summary>
        public AnswerMark Mark => mark;

        public bool Chosen => chosen;

        /// <summary>How the button shows now.</summary>
        public RoomButtonLook Look => painted ?? RoomButtonLook.Rest;

        public bool Available
        {
            get => press.Available;
            set => press.Available = value;
        }

        /// <summary>
        /// A fact of the action's own, as a chip of its own floating beside the action (the addendum's A17: the agent's
        /// level after its name), never inside its plate and never joined to its words on a dot: 10 dp after the core, on
        /// its middle line, its word in the label face 17 dp on a 21 dp line (15 read under the 14 dp floor as seen, 16 at 0.870 degrees under the 0.875 floor; the lead, 2026-11-17), white on its own backing (#2D2D2D at 78
        /// percent, a 1 dp edge of white at 6 percent), 10 dp of room beside it and 3 dp above and below, its corner 10 dp.
        /// It takes no press: the action's target is its core alone.
        /// </summary>
        public const float AsideGapDp = 10f;
        public const float AsidePadXDp = 10f;
        public const float AsidePadYDp = 3f;
        public const float AsideTypeDp = 17f;
        public const float AsideLineDp = 21f;
        public const float AsideCornerDp = 10f;
        public const int AsideGround = 0x2D2D2D;
        public const float AsideAlpha = 0.78f;
        public const float AsideEdgeDp = 1f;
        public const float AsideEdgeAlpha = 0.06f;

        /// <summary>The chip holds a chosen fact (the agent's level): before its word, the chosen answer's small mark, neutral at rest, 16 dp, its check 11 dp, 7 dp from the word.</summary>
        public const float AsideMarkDp = 16f;
        public const float AsideMarkCheckDp = 11f;
        public const float AsideMarkGapDp = 7f;
        public const int AsideMarkColour = 0xE2E7EA;

        /// <summary>The chip after the words as shown, or null where there is none.</summary>
        public string? Aside => shownAside;

        public RoomShape AsidePlate => asidePlate;

        public TextMeshPro AsideWords => asideWords;

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
            button.words.overflowMode = TextOverflowModes.Overflow;
            // Every action's words stand on the soft underlay (DESIGN 1.4): black at 35 percent, no offset.
            RoomBand.Underlay(button.words, action: true);
            button.glyph = RoomGlyphView.Create(button.visual, "Glyph", order + 3);
            button.asidePlate = RoomShape.Create(button.visual, "Aside", order + 2);
            button.asideWords = KitType.Create(button.visual, "Aside words", KitRole.Label, Color.white, order + 3, TextAlignmentOptions.TopLeft, room: true);
            button.asideWords.textWrappingMode = TextWrappingModes.NoWrap;
            button.asideMark = RoomShape.Create(button.visual, "Aside mark", order + 3);
            button.asideCheck = RoomGlyphView.Create(button.visual, "Aside check", order + 4);
            button.asidePlate.gameObject.SetActive(false);
            button.asideMark.gameObject.SetActive(false);
            button.asideCheck.Hide();
            button.asideWords.gameObject.SetActive(false);
            var height = RoomType.Units(HeightDp);
            button.press = KitPress.Create(go, new Vector2(height, height));
            // The press keeps the button's clock, so a check that drives the button drives its press too.
            button.press.Clock = () => button.Clock();
            button.press.Pressed += () => button.Pressed?.Invoke();
            // Held shows only a pinch the press counted (the one press: a refused pinch sinks nothing).
            button.press.Target.Selected += () => button.held = button.press.Pressing;
            button.press.Target.Released += _ => button.held = false;
            go.SetActive(false);
            return button;
        }

        /// <summary>The core's height in dp: 48 for the column's actions, the hand target at home.</summary>
        public float CoreHeightDp => size == RoomButtonSize.Home ? HeightDp : CoreDp * GlazeText.Scale;

        /// <summary>The chip, its glyph and the room round them grow with the person's text size, as every length inside a surface does; the home core stays the hand target.</summary>
        private float ChipSizeDp => (size == RoomButtonSize.Home ? HomeChipDp : ChipDp) * GlazeText.Scale;

        /// <summary>The room left of the chip: what leaves it centred in the core's height (8 dp, 8.4 at home).</summary>
        private float ChipPadDp => (CoreHeightDp - ChipSizeDp) / 2f;

        private float GapDp => (size == RoomButtonSize.Home ? HomeWordsGapDp : WordsGapDp) * GlazeText.Scale;

        /// <summary>The core's room after its words, in dp.</summary>
        public float RightDp => (size == RoomButtonSize.Home ? HomeRightDp : CoreRightDp) * GlazeText.Scale;

        /// <summary>The core's width with these words at the person's text size, in the plane's units: content-sized.</summary>
        public float Measure(string text)
        {
            RoomType.Size(words);
            // As drawn: each part of words joined on the dot its own line, so the widest part decides (see the other Measure).
            JoinedFacts.Given(text);
            var parts = LabelText.ForTextMeshPro(text).Split(new[] { " · " }, StringSplitOptions.None);
            return RoomType.Units(ChipPadDp + ChipSizeDp + GapDp + RightDp) + parts.Max(part => words.GetPreferredValues(part).x);
        }

        /// <summary>
        /// The core with these words, in the plane's units: content-sized, and where <paramref name="wrap"/> is given and the
        /// words would pass it, wrapped inside it and a line taller for each line more. Lays the words; <see cref="Show"/> follows.
        /// </summary>
        /// <param name="detail">
        /// The action's second part, given apart from its words: an answer's description, a pager's count ("2 of 7"). It stands
        /// on its own line under the words, never joined to them on a mark.
        /// </param>
        public Vector2 Measure(string text, float wrap, string? detail = null)
        {
            RoomType.Size(words);
            var hasDetail = !string.IsNullOrWhiteSpace(detail);
            if (hasDetail) JoinedFacts.Given(detail!);
            GlazeText.SetLiteral(words, text);
            // A button never draws the joining dot (the owner, 2026-11-11; the lead, 2026-11-13): each part its own line, an
            // answer's label over its description, wrapped or not.
            JoinedFacts.Given(words.text);
            var parts = words.text.Split(new[] { " · " }, StringSplitOptions.None);
            if (parts.Length > 1) words.text = string.Join("\n", parts);
            // The detail on its own line under the words: each part made literal on its own and joined after, since making
            // them literal together folds the line break into a space (XR, 2026-11-13: "Next Part 2 of 7" on one line).
            if (hasDetail) words.text += "\n" + LabelText.ForTextMeshPro(detail!.Trim());
            var inner = RoomType.Units(ChipPadDp + ChipSizeDp + GapDp + RightDp);
            words.textWrappingMode = TextWrappingModes.NoWrap;
            var wordsWidth = words.GetPreferredValues(words.text).x;
            var lines = words.text.Split('\n').Length;
            if (wrap > 0f && inner + wordsWidth > wrap)
            {
                words.textWrappingMode = TextWrappingModes.Normal;
                // Never one word alone on its last line (JUDGE's judge room, 19:20Z: an answer's words); the button keeps its width.
                var laid = RoomCardView.Balanced(words, wrap - inner, Mathf.Max(WordLines, lines));
                lines = Mathf.Max(1, laid.Lines);
                wordsWidth = wrap - inner;
            }
            else if (lines > 1) KitType.Lay(words, wordsWidth + 1e-4f, lines);
            return new Vector2(inner + wordsWidth, RoomType.Units(CoreHeightDp) + (lines - 1) * RoomType.Scaled(RoomGeometry.LineDp));
        }

        /// <summary>The width the action takes with these words and the chip beside it, in the plane's units: its core, then the chip.</summary>
        public float Measure(string text, string? aside) => Measure(text) + AsideRoom(aside);

        /// <summary>The room a chip of <paramref name="aside"/> takes after the words, its gap with it; none for no chip.</summary>
        private float AsideRoom(string? aside)
        {
            if (string.IsNullOrWhiteSpace(aside)) return 0f;
            asideWords.text = aside;
            asideWords.fontSize = GlazeTokens.FontSize(RoomType.Scaled(AsideTypeDp));
            asideWords.lineSpacing = KitType.LineSpacingFor(asideWords, AsideLineDp / AsideTypeDp);
            asideWords.rectTransform.sizeDelta = new Vector2(RoomType.Units(2000f), RoomType.Scaled(AsideLineDp));
            asideWords.ForceMeshUpdate(true);
            return RoomType.Scaled(AsideGapDp) + RoomType.Scaled(AsideMarkDp + AsideMarkGapDp) + RoomCursor.WordsWidth(asideWords, 0f) + 2f * RoomType.Scaled(AsidePadXDp);
        }

        /// <summary>Where its words end, from its centre, in its parent's units: what stands after them inside the halo starts here.</summary>
        public float WordsEnd => words.transform.localPosition.x + words.GetPreferredValues(words.text).x;

        /// <summary>
        /// Shows the button centred at <paramref name="centre"/>, content-sized (<paramref name="width"/> its least core width).
        /// Its target is the core grown to Meta's hand target.
        /// </summary>
        /// <param name="doing">
        /// What the action does, as its control's identity (<see cref="Doing"/>): where it changes, the button arrives anew and
        /// settles again before a press counts, even with the same words in the same place (AUDIT, 2026-11-12: approval B drawn
        /// where approval A was answered must never take the pinch that answered A).
        /// </param>
        public void Show(string text, GlazeIcon? icon, bool isMain, bool available, Vector2 centre, float width, AnswerMark mark = AnswerMark.None,
            bool chosen = false, float wrap = 0f, string? aside = null, string? doing = null, string? detail = null, bool destructive = false, bool route = false)
        {
            // An action that cannot act yet is not drawn (BUILD-SPEC-v4 1.4, 2026-11-05): no disabled look; it arrives when it can act.
            if (!available)
            {
                Hide();
                return;
            }
            var arrives = !gameObject.activeSelf;
            aside = string.IsNullOrWhiteSpace(aside) ? null : aside;
            var shownAs = string.IsNullOrWhiteSpace(detail) ? text : text + "\n" + detail;
            var another = !arrives && !string.Equals(shownDoing, doing, StringComparison.Ordinal);
            var changed = arrives || shownWords != shownAs || shownAside != aside || main != isMain || this.destructive != destructive || this.mark != mark || this.chosen != chosen
                || !string.Equals(shownDoing, doing, StringComparison.Ordinal);
            shownDoing = doing;
            shownWords = shownAs;
            shownAside = aside;
            main = isMain;
            this.destructive = destructive;
            this.mark = mark;
            this.chosen = chosen;
            var target = RoomType.Units(HeightDp);
            var measured = Measure(text, wrap, detail);
            asideWidth = AsideRoom(aside);
            var coreWidth = Mathf.Max(width - asideWidth, measured.x);
            core = new Vector2(coreWidth, measured.y);
            chipCentreX = -coreWidth / 2f + RoomType.Units(ChipPadDp + ChipSizeDp / 2f);
            words.transform.localPosition = new Vector3(chipCentreX + RoomType.Units(ChipSizeDp / 2f + GapDp), 0f, -RoomType.Units(0.5f));
            asidePlate.gameObject.SetActive(aside != null);
            asideWords.gameObject.SetActive(aside != null);
            asideMark.gameObject.SetActive(aside != null);
            if (aside == null) asideCheck.Hide();
            if (aside != null)
            {
                var plate = new Vector2(asideWidth - RoomType.Scaled(AsideGapDp), RoomType.Scaled(AsideLineDp + 2f * AsidePadYDp));
                var from = coreWidth / 2f + RoomType.Scaled(AsideGapDp);
                DrawAside(plate, 1f);
                asidePlate.transform.localPosition = new Vector3(from + plate.x / 2f, 0f, -RoomType.Units(0.4f));
                var markAt = from + RoomType.Scaled(AsidePadXDp) + RoomType.Scaled(AsideMarkDp) / 2f;
                var markColour = new Color(((AsideMarkColour >> 16) & 0xFF) / 255f, ((AsideMarkColour >> 8) & 0xFF) / 255f, (AsideMarkColour & 0xFF) / 255f, 1f);
                asideMark.Draw(new Vector2(RoomType.Scaled(AsideMarkDp), RoomType.Scaled(AsideMarkDp)), RoomType.Scaled(AsideMarkDp) / 2f, markColour);
                asideMark.transform.localPosition = new Vector3(markAt, 0f, -RoomType.Units(0.5f));
                asideCheck.Show(RoomGlyph.Check, AsideMarkCheckDp * GlazeText.Scale, RoomGlyphs.OnMain);
                asideCheck.transform.localPosition = new Vector3(markAt, 0f, -RoomType.Units(0.6f));
                asideWords.transform.localPosition = new Vector3(from + RoomType.Scaled(AsidePadXDp + AsideMarkDp + AsideMarkGapDp), RoomType.Scaled(AsideLineDp) / 2f, -RoomType.Units(0.5f));
            }
            press.Resize(new Vector2(Mathf.Max(target, coreWidth), Mathf.Max(target, core.y)));
            // A mark holds no glyph but its check once chosen.
            if (mark != AnswerMark.None) Lay(chosen ? RoomGlyph.Check : (RoomGlyph?)null, MarkCheckDp * GlazeText.Scale);
            // The one main action goes on: where it carries the pager's icon (a step forward written as Next), it draws the
            // forward arrow, never the pager's down chevron (the critic, 2026-11-12); a pager is never the main action.
            // A destructive action carries the cross, never the arrow of going on (AUDIT's MUST, 2026-11-13T1754Z).
            // A route to another place (a section of the menu: Tasks, Usage, Projects) points right, the way the More page's routes do
            // (AUDIT's 14:59Z: a down chevron read as opening below, a left one as Back); a place with a glyph of its own keeps it.
            else Lay(destructive ? RoomGlyph.Cross : route && RoomGlyphs.Named(text) is null or RoomGlyph.Back or RoomGlyph.Next ? RoomGlyph.Opens
                : isMain && icon == GlazeIcon.Next && RoomGlyphs.Named(text) == null ? RoomGlyph.Continue : GlyphOf(text, icon),
                (size == RoomButtonSize.Home ? HomeGlyphDp : GlyphDp) * GlazeText.Scale);
            LockProgress = null;
            press.Available = available;
            // The core stands where it would alone; the chip beside it takes the room after it that the caller laid out.
            transform.localPosition = new Vector3(centre.x - asideWidth / 2f, centre.y, -RoomType.Units(1f));
            if (changed) press.Unsettle();
            // Another action in this place shows nothing of the last one's press (JUDGE's walk, 2026-11-16: Approve drawn held and
            // plain where the answer before it was pressed): its hold and its flash were the last action's.
            if (another || arrives)
            {
                // A button that arrives shows nothing of a press from before it was hidden either.
                press.EndFlash();
                held = false;
            }
            if (arrives)
            {
                // The actions of one moment arrive one after another, in the order they are shown (RoomMotion): each 60 ms after the one before.
                var now = Clock();
                if (now - arrivalStamp > 0.05f || now < arrivalStamp) arrivalCount = 0;
                arrivalStamp = now;
                arriveIndex = arrivalCount++;
                arriveSince = now;
                painted = null;
            }
            pushed = null;
            gameObject.SetActive(true);
            Paint(immediately: false);
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>Every move under way taken to its end at once: for renders, and for a first frame that must be whole.</summary>
        public void Settle()
        {
            arriveSince = float.NegativeInfinity;
            lookSince = float.NegativeInfinity;
            if (gameObject.activeSelf) Paint(immediately: true);
        }

        /// <summary>A control's identity for <c>doing</c>: its action and its key (an approval's own id), never its words.</summary>
        public static string Doing(KitControl control) => control.Action + "\n" + (control.Key ?? "");

        /// <summary>
        /// The glyph an action carries: DESIGN's own for its words (<see cref="RoomGlyphs.Named"/>), else the one its icon
        /// stands for (Confirm takes the check or the barred circle of what it confirms), else the arrow of going on. Confirming an
        /// instruction sends it, so its Confirm carries Send's arrow, as an answer's does, never the keyboard of Type (AUDIT2's reader
        /// 4, F1: a keyboard on Confirm read as typing).
        /// </summary>
        public static RoomGlyph? GlyphOf(string text, GlazeIcon? icon)
        {
            if (RoomGlyphs.Named(text) is RoomGlyph named) return named;
            if (icon is not GlazeIcon given) return null;
            if (given == GlazeIcon.TellIt && text == WorkspaceText.ConfirmLabel(WorkspaceAction.Instruct)) return RoomGlyph.Send;
            return given switch
            {
                GlazeIcon.Approve => RoomGlyph.Check,
                GlazeIcon.Deny => RoomGlyph.Barred,
                GlazeIcon.Stop => RoomGlyph.Stop,
                GlazeIcon.SendAnswer => RoomGlyph.Send,
                GlazeIcon.HoldToTalk => RoomGlyph.Microphone,
                GlazeIcon.Type or GlazeIcon.TellIt => RoomGlyph.Keyboard,
                GlazeIcon.Next => RoomGlyph.Next,
                GlazeIcon.Back => RoomGlyph.Back,
                GlazeIcon.OpensMore => RoomGlyph.More,
                GlazeIcon.Refresh => RoomGlyph.Again,
                // Starting over turns back to the beginning; starting a task plays it (the addendum's A3, the critic of 2026-11-12).
                GlazeIcon.StartOver => RoomGlyph.Again,
                GlazeIcon.StartBuilding => RoomGlyph.Play,
                GlazeIcon.Change => RoomGlyph.Pencil,
                GlazeIcon.CreateProject or GlazeIcon.AddTask => RoomGlyph.Plus,
                // The agent's own, on the action that names it where no level stands after its name.
                GlazeIcon.CodeFile => RoomGlyph.Agent,
                _ => RoomGlyph.Continue,
            };
        }

        private void Lay(RoomGlyph? shown, float sizeDp)
        {
            glyphShown = shown;
            glyphDp = sizeDp;
            if (shown == null) glyph.Hide();
            else glyph.transform.localPosition = new Vector3(chipCentreX, 0f, -RoomType.Units(0.6f));
        }

        private void Update() => Paint(immediately: false);

        /// <summary>The seconds its arrival and its easing are timed by; a check fixes it.</summary>
        public Func<float> Clock { get; set; } = () => Time.unscaledTime;

        /// <summary>Draws it as it stands at its clock's time: what every frame does, for a check at the times it draws.</summary>
        public void Draw() => Paint(immediately: false);

        /// <summary>How far into view its arrival has it, 0 to 1; its scale; and its chip's colour (sRGB, with its alpha) as drawn now.</summary>
        public float ArrivalShown => pushed?.Shown ?? 0f;

        public float Scale => scaleNow;

        public Vector4 ChipColour => chipNow;

        /// <summary>The plate the press is painted toward now (its colour and alpha), whatever the ease has reached: what a check counts a prominent plate by.</summary>
        public Vector4 PlateTarget => haloTo;

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

        /// <summary>
        /// The plate every action stands on (the one language: visionOS's thin glass for a control): the owner's Ask glass with the
        /// page's white lift over it, solved as one colour, so on a panel it reads as the sheet's thin glass (8 percent white,
        /// #272727; 16 pointed, #3C3C3C; held, #323232) and over the room it is still a dark capsule its white words hold on.
        /// The main action's plate is visionOS's thin material: a clearly lighter glass (#474747; pointed, #555555), set apart by material, not hue. Opaque, so the glyph's well, drawn in the
        /// plate's own colour, never shows as a square of its own over a bright room (F265's heard draft, 2026-11-17).
        /// </summary>
        public const int PlateColour = 0x272727;
        public const int PlatePointedColour = 0x3C3C3C;
        public const int PlateHeldColour = 0x323232;
        public const int MainPlateColour = 0x474747;
        public const int MainPlatePointedColour = 0x555555;
        public const float PlateAlpha = 1f;

        /// <summary>How far the plate's edge feathers each way, in dp: a crisp capsule.</summary>
        public const float HaloFeatherDp = 1.2f;

        /// <summary>
        /// What a look stands as (F6, 2026-11-10; the one language, 2026-11-17): the chip's colour (the plate's own, so a glyph stands
        /// straight on the plate; an answer's mark keeps its own), the plate's colour and its alpha in Unity's linear blend, the
        /// glyph's colour and the scale. Every action stands on the same thin glass; the moment's one main action, and Confirm behind
        /// its lock, carry the accent as light (<see cref="Lit"/>), its glyph in the rim's colour. Pointed, a plate lifts; held, it dims.
        /// </summary>
        private (Vector4 Chip, Vector4 Halo, Color Glyph, float Scale) Target(RoomButtonLook look, bool locked)
        {
            var pointed = look is RoomButtonLook.Pointed or RoomButtonLook.MainPointed;
            var held = look == RoomButtonLook.Pressed;
            var leads = main || locked;
            var plate = RoomSurface.Hex(held ? PlateHeldColour : leads ? (pointed ? MainPlatePointedColour : MainPlateColour) : pointed ? PlatePointedColour : PlateColour);
            Color chip;
            Color glyphColour;
            // A chosen answer: a white ring and a white check, never a filled disc (the lead's 2026-11-17 01:56Z MUST 1: no orange fill).
            if (mark != AnswerMark.None) (chip, glyphColour) = (new Color(1f, 1f, 1f, 0f), Color.white);
            else (chip, glyphColour) = (plate, leads ? RimNow : Color.white);
            // A quiet action (the dialog's others) stands without its plate at rest: its glyph and words alone, the plate only while pointed or held.
            var plateAlpha = Quiet && !leads && !pointed && !held ? 0f : PlateAlpha;
            if (Quiet && !leads && !pointed && !held && mark == AnswerMark.None) chip = new Color(plate.r, plate.g, plate.b, 0f);
            return (new Vector4(chip.r, chip.g, chip.b, chip.a), new Vector4(plate.r, plate.g, plate.b, plateAlpha), glyphColour, RoomMotion.ActionScale(pointed ? 1f : 0f, held));
        }

        /// <summary>A quiet action (the dialog's others): its glyph and words alone at rest, its plate only while pointed or held.</summary>
        public bool Quiet { get; set; }

        /// <summary>Whether it stands as the moment's one prominent press: the main action, or Confirm behind its lock, drawn on the lighter plate.</summary>
        public bool Prominent => Lit;

        /// <summary>Whether it is the moment's one main action, and Confirm behind its lock (its plate the lighter thin glass).</summary>
        public bool Lit => gameObject.activeSelf && (main || LockProgress != null) && mark == AnswerMark.None;

        /// <summary>
        /// The rim flag the plate was last painted with (what the shader was handed as _Rim.w), not the pixels: a press whose main flag
        /// changed since its last paint reads its old rim here; the pixels themselves are proven by the parts and judges' walks.
        /// </summary>
        public bool RimPainted { get; private set; }

        /// <summary>The rim's colour where <see cref="Lit"/>, from the action's own flag: the kingfisher orange, or the warning red on a destructive main action.</summary>
        public Color RimNow => RoomSurface.Hex(destructive ? DestructiveRimColour : RimColour);

        private void Paint(bool immediately)
        {
            var look = LookNow();
            var locked = LockProgress != null;
            var now = Clock();
            // Its kind decides the colours as much as its look: main, destructive or marked anew in the same look repaints too.
            var kind = (main, destructive, mark, chosen);
            if (painted != look || paintedLocked != locked || paintedKind != kind)
            {
                paintedKind = kind;
                var first = painted == null;
                painted = look;
                paintedLocked = locked;
                (chipTo, haloTo, glyphTo, scaleTo) = Target(look, locked);
                // A look eases from where the last one stood (RoomMotion: 150 ms, ease out); the first one, and a held one, are there at once.
                if (first || look == RoomButtonLook.Pressed) (chipNow, haloNow, glyphNow, scaleNow) = (chipTo, haloTo, glyphTo, scaleTo);
                (chipFrom, haloFrom, glyphFrom, scaleFrom) = (chipNow, haloNow, glyphNow, scaleNow);
                lookSince = now;
            }
            var settled = immediately || GlazeMotion.Still;
            var eased = settled ? 1f : RoomMotion.Pointed(now - lookSince);
            chipNow = Vector4.Lerp(chipFrom, chipTo, eased);
            haloNow = Vector4.Lerp(haloFrom, haloTo, eased);
            glyphNow = Color.Lerp(glyphFrom, glyphTo, eased);
            scaleNow = Mathf.Lerp(scaleFrom, scaleTo, eased);
            // It arrives rising 8 dp and fading in, after the actions shown before it in the same moment.
            var move = settled ? RoomMove.Settled : RoomMotion.LineArrives(now - arriveSince, arriveIndex);
            var state = (chipNow, haloNow.w, glyphNow, scaleNow, move.Shown, move.LowerDp, LockProgress ?? -1f);
            if (pushed is { } last && last.Chip == state.Item1 && last.Halo == state.Item2 && last.Glyph == state.Item3 && last.Scale == state.Item4
                && last.Shown == state.Item5 && last.Lower == state.Item6 && last.Lock == state.Item7) return;
            pushed = state;

            var unit = RoomType.Units(1f);
            var textScale = GlazeText.Scale;
            visual.localScale = new Vector3(scaleNow, scaleNow, 1f);
            visual.localPosition = new Vector3(0f, -move.LowerDp * textScale * unit, 0f);
            var reach = HaloReachDp + 1f;
            var quadSize = new Vector2(core.x / unit + 2f * reach, core.y / unit + 2f * reach);
            halo.transform.localScale = new Vector3(quadSize.x * unit, quadSize.y * unit, 1f);
            halo.transform.localPosition = new Vector3(0f, 0f, RoomType.Units(0.02f));
            var chipDp = ChipSizeDp;
            // Behind its lock Confirm's chip is the main action's own squircle; its ring stands whole outside it.
            var cornerDp = (size == RoomButtonSize.Home ? HomeChipCornerDp : ChipCornerDp) * textScale;
            var markLine = 0f;
            if (mark != AnswerMark.None)
            {
                // In the chip's place: the outline alone, or chosen, the accent's disc or box under its dark check.
                chipDp = MarkDp * textScale;
                cornerDp = mark == AnswerMark.Multiple ? MarkBoxCornerDp * textScale : chipDp / 2f;
                markLine = chosen ? 1f : MarkLineAlpha;
            }
            var fade = (look == RoomButtonLook.Disabled ? DisabledOpacity : 1f) * move.Shown;
            var haloCorner = size == RoomButtonSize.Home ? HomeHaloCornerDp : HaloCornerDp * textScale;
            block.SetVector(CoreId, new Vector4(core.x / unit, core.y / unit, haloCorner, haloNow.w * move.Shown));
            block.SetVector(HaloColourId, new Vector4(haloNow.x, haloNow.y, haloNow.z, HaloFeatherDp));
            var rim = RimNow;
            // No rim on any press (the lead's 19:15Z, AUDIT's 19:21Z): the main action is its lighter plate alone.
            RimPainted = false;
            block.SetVector(RimId, new Vector4(RimDp, RimGlowDp, 1f, 0f));
            block.SetVector(RimColourId, new Vector4(rim.r, rim.g, rim.b, 1f));
            block.SetVector(QuadId, new Vector4(-quadSize.x / 2f, -quadSize.y / 2f, quadSize.x, quadSize.y));
            block.SetVector(ChipId, new Vector4(chipCentreX / unit, 0f, chipDp, cornerDp));
            block.SetVector(ChipColourId, chipNow);
            block.SetVector(RingId, new Vector4(RingRadius(chipDp, cornerDp), RingWidthDp, LockProgress ?? 0f, locked ? 1f : 0f));
            block.SetVector(MarkId, new Vector4(chipDp, cornerDp, MarkLineDp, markLine));
            block.SetFloat(FadeId, fade);
            halo.SetPropertyBlock(block);
            if (glyphShown is RoomGlyph shown) glyph.Show(shown, glyphDp, new Color(glyphNow.r, glyphNow.g, glyphNow.b, glyphNow.a * fade));
            // Every action's words white on its thin glass, the main one's too: the accent is its rim, never its words.
            if (words.color.r != 1f || words.color.g != 1f || words.color.b != 1f) words.color = new Color(1f, 1f, 1f, move.Shown);
            if (!Mathf.Approximately(words.alpha, move.Shown)) words.alpha = move.Shown;
            if (shownAside != null)
            {
                if (!Mathf.Approximately(asideWords.alpha, move.Shown)) asideWords.alpha = move.Shown;
                DrawAside(asidePlate.Size, fade);
            }
        }

        /// <summary>
        /// The lock ring's radius, in dp, for a chip <paramref name="chipDp"/> across with corners of <paramref name="cornerDp"/>:
        /// its inner edge <see cref="RingGapDp"/> past the squircle's farthest point, its corner, so it never touches the chip.
        /// </summary>
        /// <summary>
        /// Whether its chip, its glyph and, behind Confirm's lock, its ring stand wholly inside its core, at the person's text
        /// size: a mark drawn past the button's edge is a defect as a cut word is (AUDIT's MUST, 2026-11-13T1754Z).
        /// </summary>
        public bool MarksInside
        {
            get
            {
                if (!gameObject.activeSelf) return true;
                var unit = RoomType.Units(1f);
                var scale = GlazeText.Scale;
                var chipDp = mark != AnswerMark.None ? MarkDp * scale : ChipSizeDp;
                var cornerDp = mark != AnswerMark.None ? (mark == AnswerMark.Multiple ? MarkBoxCornerDp * scale : chipDp / 2f)
                    : (size == RoomButtonSize.Home ? HomeChipCornerDp : ChipCornerDp) * scale;
                var reach = Mathf.Max(chipDp / 2f, glyphShown != null ? glyphDp / 2f : 0f);
                if (LockProgress != null) reach = Mathf.Max(reach, RingRadius(chipDp, cornerDp) + RingWidthDp / 2f);
                var fromLeft = chipCentreX / unit + core.x / unit / 2f;
                return fromLeft - reach >= -0.05f && reach <= core.y / unit / 2f + 0.05f;
            }
        }

        public static float RingRadius(float chipDp, float cornerDp)
        {
            var half = chipDp / 2f;
            var corner = Mathf.Min(cornerDp, half);
            var farthest = (half - corner) * Mathf.Sqrt(2f) + corner;
            return farthest + RingGapDp + RingWidthDp / 2f;
        }

        private void DrawAside(Vector2 plate, float fade)
        {
            var ground = new Color(((AsideGround >> 16) & 0xFF) / 255f, ((AsideGround >> 8) & 0xFF) / 255f, (AsideGround & 0xFF) / 255f, AsideAlpha * fade);
            asidePlate.Draw(plate, RoomType.Scaled(AsideCornerDp), ground, new Color(1f, 1f, 1f, AsideEdgeAlpha * fade), RoomType.Scaled(AsideEdgeDp));
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

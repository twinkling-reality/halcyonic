#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The information panel as a card, drawn (<see cref="RoomCard"/>, the addendum's A17), every length the design page's:
    /// a status, its mark in a 56 dp disc and its title word over a quieter line, its tries as pips hung on the mark; or a
    /// head, the thing's emblem in a 104 dp tile beside its title (34 dp on a 40 dp line); a description under it; the
    /// gauges as flat wells; the facts as slots, each a flat faint well, its kind's glyph small at its top left and its value
    /// inside it under the glyph, left-aligned (one form everywhere, nothing that reads as a press); the steps
    /// as squircle marks joined by lines, their names under them, a step's tries as pips hung on its mark like a game's
    /// lives; a list, each row a 38 dp tile
    /// of its kind's mark, its name and a quiet note at its right, a hairline between rows; and a quiet line at its foot.
    /// Its origin is the top left of its first part; it grows down and says how tall it stands. It draws nothing of the
    /// panel and takes no press.
    /// </summary>
    public sealed class RoomCardView : MonoBehaviour
    {
        // The design page's numbers (design10's information language), in dp.
        public const float StatusMarkDp = 56f;
        public const float StatusGlyphDp = 40f;
        public const float StatusGapDp = 14f;
        public const float StatusTitleDp = 30f;
        public const float StatusTitleLineDp = 36f;

        /// <summary>A status under a card's own title: a step smaller, so the thing's name stays the card's one title.</summary>
        public const float UnderTitleStatusDp = 24f;
        public const float UnderTitleStatusLineDp = 30f;
        public const float LineTypeDp = 17f;
        public const float LineLineDp = 23f;
        public const float HeadDp = 104f;
        public const float HeadCornerDp = 28f;
        public const float HeadGlyphDp = 52f;
        public const float HeadGapDp = 20f;
        public const float TitleTypeDp = 34f;
        public const float TitleLineDp = 40f;
        public const float LongTitleTypeDp = 26f;
        public const float LongTitleLineDp = 31f;
        public const float FinalTitleTypeDp = 22f;
        public const float FinalTitleLineDp = 27f;
        public const float DescriptionTypeDp = 18f;
        public const float DescriptionLineDp = 25f;
        public const float DescriptionGapDp = 6f;
        public const float BlockGapDp = 22f;
        public const float TileGapDp = 16f;
        /// <summary>A well's corner: smaller than the panel's own 20 dp, so a well sits in the panel and never reads as a tile of its own.</summary>
        public const float TileCornerDp = 14f;
        /// <summary>A slot's well: a step stronger than 0.065, which over the firmer glass and a bright room stood at 1.10:1 and vanished (the critic, 2026-11-12).</summary>
        public const float WellAlpha = 0.10f;

        /// <summary>A neutral row's tile: a step stronger than a slot's well, so the mark reads as a tile over either room.</summary>
        public const float RowTileAlpha = 0.09f;
        public const float SlotPadDp = 14f;
        public const float SlotFootDp = 16f;
        public const float SlotGlyphDp = 24f;
        public const float SlotGlyphBoxDp = 26f;
        public const float SlotLevelDp = 21f;
        public const float SlotLevelCheckDp = 13f;

        /// <summary>A chosen answer's disc at rest, neutral: the accent stays the main action's (#E2E7EA, its check #172024).</summary>
        public const int ChosenAtRest = 0xE2E7EA;
        public const float CaptionTypeDp = 16f;
        public const float CaptionLineDp = 20f;
        public const float CaptionGapDp = 12f;
        public const float StatusCornerDp = 17f;
        public const float StepDp = 38f;
        public const float StepCornerDp = 12f;
        public const float StepGlyphDp = 24f;
        public const float StepJoinDp = 3f;
        public const float StepJoinGapDp = 8f;
        public const float StepNameDp = 17f;
        public const float StepNameLineDp = 21f;
        public const float StepNameGapDp = 36f;
        public const float PipDp = 14f;
        public const float PipCornerDp = 4f;
        public const float PipGapDp = 4f;
        public const float PipLineDp = 2f;
        /// <summary>How far under its node a pip's foot hangs.</summary>
        public const float PipDropDp = 26f;
        public const float TrackTallDp = 8f;
        public const float ListGapDp = 16f;
        public const float RowPadDp = 11f;
        public const float RowTileDp = 38f;
        public const float RowTileCornerDp = 10f;
        public const float RowGlyphDp = 22f;
        public const float RowGapDp = 14f;
        public const float RowNameDp = 20f;
        public const float RowNameLineDp = 26f;
        public const float RowNoteDp = 16f;
        public const float RowNoteLineDp = 20f;

        /// <summary>
        /// The quiet words' white, in the reading face, for every note, a status's line, a card's foot line and a gauge's words:
        /// a step quieter than a name, legible over the bright room and the dark one (0.66 measured 4.05:1 and 0.72 4.55:1 over
        /// the bright room, under the 4.5:1 floor's margin, the critic of 2026-11-12; the kind's colour is the mark's alone).
        /// </summary>
        public const float NoteAlpha = 0.78f;

        /// <summary>
        /// A green or red mark's tile and its ink: a deep tint under a light glyph, so the mark reads over the bright room
        /// as over the dark (0.18 went muddy there, and a red minus on red was the weakest mark).
        /// </summary>
        public const float ToneTileAlpha = 0.38f;
        public const float GaugePadXDp = 14f;
        public const float GaugeGapDp = 12f;
        public const float GaugePadTopDp = 14f;
        public const float GaugePadFootDp = 16f;
        public const float GaugeNumberDp = 40f;
        public const float GaugeNumberLineDp = 44f;
        public const float GaugeLabelDp = 17f;
        public const float GaugeLabelLineDp = 22f;
        public const float GaugeResetDp = 15f;
        public const float GaugeResetLineDp = 19f;
        public const float MoreDp = 16f;
        public const float MoreLineDp = 21f;

        /// <summary>
        /// A code to scan: its well as wide as the card (at most 400 dp, 25 degrees), the card's hero, on every block's left edge, standing in the card's
        /// middle; its cells dark on a near-white well, the kit's margin of light cells round them, the well's corners inside
        /// that margin. Opaque both, so a camera reads it the same over the bright room as over the dark.
        /// </summary>
        public const float CodeDp = 400f;
        public const int CodeGround = 0xF4F7F8;
        public const int CodeInk = 0x0E1214;
        public const float CodeCornerCells = 2f;

        /// <summary>The page of reading's well: the panel's darkest, near solid, its rows 8 dp in from its top and foot.</summary>
        public const int ReadingWell = 0x0E1214;

        /// <summary>A changed run's band, the hue a fifth over the well, solid; the removed rule a lighter red so it weighs as the green.</summary>
        public const int AddedBand = 0x203A2C;
        public const int RemovedBand = 0x3D2929;
        public const int RemovedRule = 0xFF8A80;
        public const float ReadingWellAlpha = 1f;
        public const float ReadingWellPadDp = 8f;

        /// <summary>The colours of the language: the pass, failure and accent hues, and their deep grounds.</summary>
        public const int Passed = 0x69D98D;
        public const int PassedInk = 0x9CF0B6;
        public const int PassedGround = 0x2E5E40;
        public const int PassedNote = 0x7EE0A0;
        public const int Failed = 0xF7857D;
        public const int FailedInk = 0xFFB4AD;
        public const int FailedGround = 0x6E302C;
        public const int FailedNote = 0xFF9C94;
        public const int Accent = 0x59CFF1;
        public const int AccentNote = 0x7FD9F5;
        public const int Dark = 0x172024;

        /// <summary>The attention colour (a task or the assistant needs the person), as the name tag's Needs You chip: amber.</summary>
        public const int Attention = 0xF2B134;
        public const int AttentionNote = 0xFFD27A;
        public const int StepGround = 0x2F3539;

        /// <summary>The deep grounds of the accent and of attention, as the passed and failed ones, and how solid a status mark stands.</summary>
        public const int AccentGround = 0x1E4C5A;
        public const int AttentionGround = 0x543C12;
        public const int AttentionInk = 0x3A2805;
        public const int QuietGround = 0x3C4448;

        /// <summary>A status mark stands solid, so nothing of the room shows through it (the critic, 2026-11-11).</summary>
        public const float StatusGroundAlpha = 1f;

        /// <summary>
        /// One recipe for every status mark and step node (the critics, 2026-11-11): the tone's own hue, solid and saturated,
        /// its glyph bold and white over it; attention's amber is light, so its glyph is dark.
        /// </summary>
        public const int GoodFill = 0x2FA65A;
        public const int BadFill = 0xD4463B;
        public const int WorkingFill = 0x1F98C2;
        public const int QuietFill = 0x4A535A;

        /// <summary>A mark's glyph stroke, heavier than a row's, so it reads at a glance on a solid fill.</summary>
        public const float MarkStroke = 3.4f;

        /// <summary>The tones' mid values, between hue and deep ground: a try's pip, never louder than the mark it hangs on.</summary>
        public const int PassedMid = 0x4C9C67;
        public const int FailedMid = 0xB25A54;
        public const int AccentMid = 0x3B8EA6;
        public const int AttentionMid = 0xA37A23;
        public const int AgentOrb = 0x7A6BD6;
        public const int ProjectTile = 0x3F8FB0;
        public const int TileInk = 0xE8F6FB;

        private readonly Dictionary<KitRole, List<TextMeshPro>> texts = new Dictionary<KitRole, List<TextMeshPro>>();
        private readonly Dictionary<KitRole, int> textsUsed = new Dictionary<KitRole, int>();
        private readonly List<RoomShape> plates = new List<RoomShape>();
        private readonly List<RoomGlyphView> glyphs = new List<RoomGlyphView>();
        private readonly List<(KitPress Press, GameObject Target)> rowPresses = new List<(KitPress, GameObject)>();
        private readonly List<KitControl?> rowControls = new List<KitControl?>();
        private int rowPressesUsed;

        /// <summary>The card's own press, where the card is the way in, and the accent face that shows it, felt on hover.</summary>
        private KitPress? cardPress;
        private RoomShape? cardFace;
        private Vector2 cardFaceSize;
        private bool? cardFaceLit;
        private int cardFaceHue = Accent;

        /// <summary>The control a press on the card raises, while the shown card takes one.</summary>
        private KitControl? cardControl;

        /// <summary>A row picked from a list (<see cref="RoomListRow.Press"/>): its own control, for the bar to act on.</summary>
        public event System.Action<KitControl>? RowPressed;

        /// <summary>The list rows' press targets shown now, in order, with their controls: for checks.</summary>
        public IEnumerable<(KitPress Press, KitControl Control)> RowTargets
        {
            get
            {
                for (var index = 0; index < rowPressesUsed; index++)
                    if (rowControls[index] is KitControl control) yield return (rowPresses[index].Press, control);
            }
        }

        /// <summary>The chevron at a picked-from row's right, 18 dp, and its room before the note.</summary>
        public const float ChevronDp = 18f;
        public const float ChevronGapDp = 10f;
        private RoomCodeView? codeView;
        private bool codeUsed;
        private int platesUsed;
        private int glyphsUsed;
        private int order;

        /// <summary>How wide and how tall the card stands as last shown.</summary>
        public Vector2 Size { get; private set; }

        /// <summary>Every label the card shows now, in the order it laid them: for checks.</summary>
        public List<TextMeshPro> Shown { get; } = new List<TextMeshPro>();

        /// <summary>The labels shown as an excerpt of outside words, their lead cut at a word (a row that opens them whole).</summary>
        public List<TextMeshPro> Excerpts { get; } = new List<TextMeshPro>();

        /// <summary>How many slots' values were wider than the card and wrapped whole in a well of their own.</summary>
        public int SlotsWrapped { get; private set; }

        /// <summary>Every tile, mark and plate the card shows now, with what it is: for checks.</summary>
        public List<(RoomShape Plate, string What)> Plates { get; } = new List<(RoomShape, string)>();

        /// <summary>
        /// Whether the card as last shown stands whole: every label it laid (the title, the line, each slot's value, each
        /// row, each line of reading, the foot line) shows all its words, none cut or ellipsed. XR's host reports a frame
        /// drawn only while this holds, so nothing counts as read that was cut (XR, 0750Z; the review before Start).
        /// </summary>
        public bool LaidWhole
        {
            get
            {
                if (!gameObject.activeInHierarchy) return false;
                foreach (var label in Shown)
                {
                    label.ForceMeshUpdate(true);
                    // A paged description runs on past its page by design; its page stands whole, broken between lines.
                    if (label == PagedDescription) continue;
                    if (label.isTextOverflowing) return false;
                    if (!string.IsNullOrEmpty(label.text) && label.textInfo.characterCount == 0) return false;
                }
                return true;
            }
        }

        /// <summary>The description's label where it pages (<see cref="RoomCard.DescriptionPage"/>); null where it stands whole.</summary>
        public TextMeshPro? PagedDescription { get; private set; }

        /// <summary>The pages the description made as last shown: 1 where it does not page. The flow pages the review by it.</summary>
        public int DescriptionPages { get; private set; } = 1;

        /// <summary>The labels of the card's page of reading now, a file's own lines: for checks, which read no separator in them.</summary>
        public List<TextMeshPro> ReadingShown { get; } = new List<TextMeshPro>();

        /// <summary>The code to scan the card shows now; null where it shows none. For checks.</summary>
        public RoomCodeView? CodeShown => codeUsed ? codeView : null;

        /// <summary>Every glyph the card shows now, with what it is: for checks.</summary>
        public List<(RoomGlyphView Glyph, string What)> Glyphs { get; } = new List<(RoomGlyphView, string)>();

        /// <summary>A card under <paramref name="parent"/>, hidden, its words drawn at <paramref name="order"/> and its tiles under them.</summary>
        public static RoomCardView Create(Transform parent, int order = KitType.Order.Words)
        {
            var go = new GameObject("Card");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<RoomCardView>();
            view.order = order;
            go.SetActive(false);
            return view;
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>Shows <paramref name="card"/> <paramref name="width"/> wide at the person's text size, and returns how tall it stands.</summary>
        public float Show(RoomCard card, float width)
        {
            gameObject.SetActive(true);
            // A card that is the way in lays its words a chevron's room narrower, so the chevron stands at the text's own inset
            // from the edge and never meets a word (the critic, 2026-11-12: it sat 7 px from the ring against a 36 px inset).
            var full = width;
            if (card.Press != null) width = Mathf.Max(S(StepDp), full - S(CardChevronDp + RowGapDp));
            foreach (var role in new List<KitRole>(textsUsed.Keys)) textsUsed[role] = 0;
            platesUsed = 0;
            glyphsUsed = 0;
            rowPressesUsed = 0;
            codeUsed = false;
            PagedDescription = null;
            DescriptionPages = 1;
            Shown.Clear();
            Excerpts.Clear();
            SlotsWrapped = 0;
            ReadingShown.Clear();
            Plates.Clear();
            Glyphs.Clear();
            var down = 0f;
            void Gap(float dp) { if (down > 0f) down += S(dp); }

            // A thing's name first where it has one, its state beneath it (the critic, 2026-11-11: a status over the name read as
            // a second title); a card with no name leads with its status.
            var named = card.Title != null || card.Head != RoomCardHead.None;
            if (!named && card.Status is RoomCardStatus lead) down = LayStatus(lead, card, width, down);
            if (named)
            {
                Gap(BlockGapDp);
                down = LayHead(card, width, down);
                if (card.Status is RoomCardStatus status)
                {
                    Gap(BlockGapDp);
                    down = LayStatus(status, card, width, down);
                }
            }
            // A card never says the same words twice: a description that only repeats the title is not drawn (an idea that is
            // also its name; the critic, 2026-11-12).
            var description = card.Description != null && card.Title != null && string.Equals(card.Description.Text.Trim(), card.Title.Text.Trim(), System.StringComparison.Ordinal)
                ? null : card.Description;
            if (description != null)
            {
                // Under a head or a status a block's gap, as every other block, so another's words never read as the status's
                // line (the critic, 2026-11-11); under a title alone, its own small gap.
                down += S(card.Head != RoomCardHead.None || card.Status != null ? BlockGapDp : DescriptionGapDp);
                if (card.DescriptionPage is int page)
                {
                    // Paged: four lines a page, broken between lines; the page asked for, or the last there is.
                    var (label, _) = Text(KitRole.Body, DescriptionTypeDp, DescriptionLineDp, White(0.80f), card.Description, 0f, -down, width, RoomCard.DescriptionPageLines);
                    label.overflowMode = TextOverflowModes.Page;
                    label.ForceMeshUpdate(true);
                    DescriptionPages = Mathf.Max(1, label.textInfo.pageCount);
                    label.pageToDisplay = Mathf.Min(page, DescriptionPages - 1) + 1;
                    label.ForceMeshUpdate(true);
                    PagedDescription = label;
                    var onPage = Mathf.Clamp(label.textInfo.lineCount - (label.pageToDisplay - 1) * RoomCard.DescriptionPageLines, 1, RoomCard.DescriptionPageLines);
                    down += onPage * S(DescriptionLineDp);
                }
                else
                {
                    var (_, tall) = Text(KitRole.Body, DescriptionTypeDp, DescriptionLineDp, White(0.80f), card.Description, 0f, -down, width, 4);
                    down += tall;
                }
            }
            if (card.Gauges.Count > 0)
            {
                Gap(BlockGapDp);
                down = LayGauges(card.Gauges, width, down);
            }
            if (card.Code is RoomCode code)
            {
                Gap(BlockGapDp);
                down = LayCode(code, width, down);
            }
            if (card.Slots.Count > 0)
            {
                Gap(BlockGapDp);
                down = LaySlots(card.Slots, width, down);
            }
            if (card.Reading.Count > 0)
            {
                Gap(BlockGapDp);
                down = LayReading(card.Reading, width, down);
            }
            if (card.Steps.Count > 0)
            {
                Gap(BlockGapDp);
                down = LaySteps(card.Steps, width, down);
            }
            if (card.List.Count > 0)
            {
                Gap(ListGapDp);
                down = LayList(card.List, width, down);
            }
            if (card.More != null)
            {
                // A block's own gap, as between every other block (the critic, 2026-11-11: the foot sat tight).
                Gap(BlockGapDp);
                var (_, tall) = Text(KitRole.Body, MoreDp, MoreLineDp, White(NoteAlpha), card.More, 0f, -down, width, 2);
                down += tall;
            }
            LayCardPress(card, full, down);
            HideUnused();
            Size = new Vector2(full, down);
            return down;
        }

        /// <summary>
        /// A card that is the way in (<see cref="RoomCard.Press"/>): its whole face a press target behind the parts that take
        /// their own, an accent face under its words that brightens while it will take the press.
        /// </summary>
        private void LayCardPress(RoomCard card, float width, float tall)
        {
            if (card.Press is KitControl next && (cardControl is not KitControl was || RoomButton.Doing(was) != RoomButton.Doing(next))) cardPress?.Unsettle();
            cardControl = card.Press;
            PanelRing = null;
            if (card.Press == null)
            {
                if (cardPress != null) cardPress.gameObject.SetActive(false);
                if (cardFace != null) cardFace.gameObject.SetActive(false);
                return;
            }
            // The face is the card: it fills the panel to its edge (the critic, 2026-11-12: a grey rim round an accent face read as
            // a card inside a card), and the chevron of a way in stands in the panel's right margin, clear of every word.
            var pad = S(RoomGeometry.PanelPadDp);
            var size = new Vector2(width + 2f * pad, tall + 2f * pad);
            if (cardPress == null)
            {
                var go = new GameObject("Card press");
                go.transform.SetParent(transform, false);
                cardPress = KitPress.Create(go, size);
                cardPress.Pressed += () => { if (cardControl is KitControl pressed) RowPressed?.Invoke(pressed); };
                cardFace = RoomShape.Create(transform, "Card face", order - 6);
            }
            cardPress.gameObject.SetActive(true);
            cardPress.Resize(size);
            cardPress.Available = true;
            // Behind the rows' and slots' own targets, so a part that takes its own press keeps it.
            cardPress.transform.localPosition = new Vector3(width / 2f, -tall / 2f, -RoomType.Units(0.4f));
            cardFace!.gameObject.SetActive(true);
            cardFace.transform.localPosition = new Vector3(width / 2f, -tall / 2f, -RoomType.Units(0.1f));
            cardFaceSize = size;
            // One hue on the card: the way in takes its status's own tone where it has one (a request that needs the person is
            // the attention amber its mark already wears), the accent where it has none (the critic, 2026-11-12: amber and cyan).
            cardFaceHue = card.Status is RoomCardStatus said && said.Tone == RoomTone.Attention ? Attention
                : card.Status is RoomCardStatus good && good.Tone == RoomTone.Good ? GoodFill : Accent;
            cardFaceLit = null;
            PaintCardFace();
            // The way on at the words' right edge, the panel's pad from its ring as the text is from the left, a capital's stem in weight.
            Glyph(RoomGlyph.Chevron, CardChevronDp, White(0.90f), new Vector2(width - S(CardChevronDp) / 2f, -tall / 2f), "card chevron", CardChevronStroke);
        }

        private void PaintCardFace()
        {
            if (cardFace == null || cardPress == null || !cardFace.gameObject.activeSelf) return;
            var lit = cardPress.Hovered || cardPress.Pressing;
            if (cardFaceLit == lit) return;
            cardFaceLit = lit;
            // Its edge the hue itself, firm enough to read over the bright room (at 0.55 it stood at 1.9:1 there), on the panel's
            // own corner so no plain glass shows outside it; its face a quiet wash of the hue.
            // The wash alone: the ring is the panel's own edge (PanelRing), drawn on the panel's own corner, so no glass shows
            // outside it (the critic, 2026-11-12: a round edge over the squircle left grey crescents at each corner).
            cardFace.Draw(cardFaceSize, RoomType.Units(RoomGeometry.PanelCornerDp), AsShown(Rgb(cardFaceHue, lit ? 0.22f : 0.14f)), Color.clear, 0f);
            PanelRing = (Rgb(cardFaceHue), 3f);
        }

        /// <summary>The chevron of a card that is the way in: as tall as a title's capitals, so the way on reads at a glance.</summary>
        public const float CardChevronDp = 28f;

        /// <summary>The chevron's stroke: a capital's stem, so it weighs as the words beside it.</summary>
        public const float CardChevronStroke = 2.6f;

        private void Update() => PaintCardFace();

        /// <summary>
        /// The ring the panel holding the card draws as its edge where the card is the way in (RoomSurface.Ring): the card's
        /// hue, firmer while it will take the press; null for a card that only says. Read after Show and each frame.
        /// </summary>
        public (Color Colour, float WidthDp)? PanelRing { get; private set; }

        /// <summary>Whether the shown card is itself a press target, and the control it raises.</summary>
        public KitControl? CardTarget => cardPress != null && cardPress.gameObject.activeSelf ? cardControl : null;

        private float LayStatus(RoomCardStatus status, RoomCard card, float width, float down)
        {
            var (ground, ink, glyph) = status.Tone switch
            {
                // Deep grounds of the tone's hue, near solid, the light ink over them: a faint tint vanished over the bright room
                // (the critic, 2026-11-11), where the recap's solid head held.
                RoomTone.Good => (Rgb(GoodFill), Color.white, RoomGlyph.Check),
                RoomTone.Bad => (Rgb(BadFill), Color.white, RoomGlyph.Alert),
                RoomTone.Working => (Rgb(WorkingFill), Color.white, RoomGlyph.Wrench),
                // Attention's amber is light: its glyph dark on it, so it reads as wanted, never as dirt.
                RoomTone.Attention => (Rgb(Attention), Rgb(AttentionInk), RoomGlyph.Bubble),
                _ => (Rgb(QuietFill), Color.white, RoomGlyph.Minus),
            };
            if (status.Mark != RoomKind.None) glyph = GlyphOf(status.Mark);
            var left = S(StatusMarkDp + StatusGapDp);
            var column = width - left;
            var top = down;
            var under = card.Title != null;
            // Under a name the state is a step smaller in the status's own weight; under a file's mono name, whose face is regular,
            // in the reading face, so the name stays the heavier of the two.
            var (titleLabel, titleTall) = Text(under && card.TitleMono ? KitRole.Body : KitRole.Title, under ? UnderTitleStatusDp : StatusTitleDp, under ? UnderTitleStatusLineDp : StatusTitleLineDp, Color.white,
                LineWords.Ours(status.Title), left, -down, column, 2);
            var tall = titleTall;
            TextMeshPro? lineLabel = null;
            if (status.Line != null)
            {
                var laidLine = Text(KitRole.Body, LineTypeDp, LineLineDp, White(NoteAlpha), LineWords.Ours(status.Line), left, -(down + tall + S(2f)), column, 3);
                lineLabel = laidLine.Label;
                tall += S(2f) + laidLine.Tall;
            }
            var block = Mathf.Max(S(StatusMarkDp), tall);
            // Words shorter than the mark stand centred on it, a title alone too (SCREENS's critic, 2026-11-11).
            var drop = (block - tall) / 2f;
            if (drop > 0f)
            {
                titleLabel.transform.localPosition += new Vector3(0f, -drop, 0f);
                if (lineLabel != null) lineLabel.transform.localPosition += new Vector3(0f, -drop, 0f);
            }
            // The mark beside the title: centred on words shorter than it, its top on the title's where they run taller.
            var middle = top + S(StatusMarkDp) / 2f;
            // A squircle, as the check nodes: a disc is only ever an answer.
            Plate(new Vector2(S(StatusMarkDp), S(StatusMarkDp)), S(StatusCornerDp), ground, new Vector2(S(StatusMarkDp) / 2f, -middle), "status mark");
            Glyph(glyph, StatusGlyphDp, ink, new Vector2(S(StatusMarkDp) / 2f, -middle), "status " + status.Tone, MarkStroke);
            if (card.TrackOf is int of)
            {
                var used = Mathf.Clamp(card.TrackAt is int at ? at > of ? of : at : 0, 0, of);
                Pips(used, of, status.Tone, new Vector2(S(StatusMarkDp) / 2f, -(middle + S(StatusMarkDp) / 2f + S(PipDropDp) - S(PipDp) / 2f)));
                block = Mathf.Max(block, S(StatusMarkDp) + S(PipDropDp));
            }
            return top + block;
        }

        /// <summary>
        /// The code to scan in the card's middle: a near-white well as wide as the card allows up to <see cref="CodeDp"/>, the
        /// host's cells inside it with <see cref="RoomCode.MarginCells"/> light cells round them. A cell is a whole share of
        /// the well, so every cell stands the same size.
        /// </summary>
        private float LayCode(RoomCode code, float width, float down)
        {
            // The card's own width, so the code shares every block's left edge (the critic, 2026-11-11), up to its most.
            var well = Mathf.Min(width, RoomType.Units(CodeDp));
            var cell = well / (code.Side + 2 * RoomCode.MarginCells);
            var centre = new Vector2(width / 2f, -(down + well / 2f));
            Plate(new Vector2(well, well), Mathf.Min(S(TileCornerDp), cell * CodeCornerCells), Rgb(CodeGround), centre, "code well");
            if (codeView == null) codeView = RoomCodeView.Create(transform, "Code", order - 3);
            codeView.Show(code, cell * code.Side, Rgb(CodeInk));
            codeView.transform.localPosition = new Vector3(centre.x, centre.y, -RoomType.Units(0.4f));
            codeUsed = true;
            return down + well;
        }

        /// <summary>
        /// A page of a file's lines (<see cref="RoomReading"/>'s measures): each row its line number at the gutter's right,
        /// quiet; an added line's plus in green and a removed line's minus in red, small and bare, over a faint band of its
        /// hue the card's width, so a run of changed lines reads as one block; the words mono, theirs, stood their indent in,
        /// a line the change left a step quieter; a continued row hung in under its start; a gap a hairline across.
        /// </summary>
        private float LayReading(IReadOnlyList<RoomReadingRow> rows, float width, float down)
        {
            // A dark well under the whole page, near solid, so a band keeps one colour whatever the room shows behind the
            // panel (the critic, 2026-11-11), as a diff view sets its code apart.
            var blockTall = 0f;
            foreach (var row in rows) blockTall += row.Kind == RoomLineKind.Gap && row.Number == null ? S(RoomReading.GapRowDp) : S(RoomReading.LineDp);
            Plate(new Vector2(width, blockTall + 2f * S(ReadingWellPadDp)), S(TileCornerDp), Rgb(ReadingWell, ReadingWellAlpha), new Vector2(width / 2f, -(down + blockTall / 2f + S(ReadingWellPadDp))), "reading well", under: true);
            down += S(ReadingWellPadDp);
            var advance = S(RoomReading.TypeDp) * RoomReading.AdvanceEm;
            var gutter = S(RoomReading.GutterDp);
            var pitch = S(RoomReading.LineDp);
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                if (row.Kind == RoomLineKind.Gap)
                {
                    // A hairline across, from the bands' own left edge, one dp at either size; where the gap says how many lines it
                    // skips, the count quiet in its middle, the hairline parted round it.
                    var gapTall = row.Number is int ? S(RoomReading.LineDp) : S(RoomReading.GapRowDp);
                    var gapMiddle = -(down + gapTall / 2f);
                    if (row.Number is int skipped)
                    {
                        var (count, _) = Text(KitRole.Mono, RoomReading.NumberDp, RoomReading.LineDp, White(NoteAlpha), LineWords.Ours(RoomReading.Skipped(skipped)), 0f, -down, width, 1);
                        var countWidth = RoomCursor.WordsWidth(count, 0f);
                        var countLeft = (width - countWidth) / 2f;
                        count.transform.localPosition = new Vector3(countLeft, -down, -RoomType.Units(0.5f));
                        var side = countLeft - S(10f);
                        Plate(new Vector2(side, RoomType.Units(1f)), 0f, White(0.18f), new Vector2(side / 2f, gapMiddle), "reading gap");
                        Plate(new Vector2(side, RoomType.Units(1f)), 0f, White(0.18f), new Vector2(width - side / 2f, gapMiddle), "reading gap part");
                    }
                    else Plate(new Vector2(width, RoomType.Units(1f)), 0f, White(0.18f), new Vector2(width / 2f, gapMiddle), "reading gap");
                    down += gapTall;
                    continue;
                }
                var middle = down + pitch / 2f;
                var changed = row.Kind == RoomLineKind.Added || row.Kind == RoomLineKind.Removed;
                if (changed && (index == 0 || rows[index - 1].Kind != row.Kind))
                {
                    // One band for the whole run of rows of this change, so no seam parts them; solid, the hue mixed into the
                    // well, and a rule of the hue at its left as a diff marks a run (the critic, 2026-11-11).
                    var run = 0;
                    for (var next = index; next < rows.Count && rows[next].Kind == row.Kind; next++) run++;
                    var runTall = run * pitch;
                    var added = row.Kind == RoomLineKind.Added;
                    var centreY = -(down + runTall / 2f);
                    Plate(new Vector2(width, runTall), 0f, added ? Rgb(AddedBand) : Rgb(RemovedBand), new Vector2(width / 2f, centreY), "reading band");
                    Plate(new Vector2(S(3f), runTall), 0f, added ? Rgb(Passed) : Rgb(RemovedRule), new Vector2(S(1.5f), centreY), "reading rule", front: true);
                }
                if (changed)
                {
                    if (!row.Continued)
                        Glyph(row.Kind == RoomLineKind.Added ? RoomGlyph.Plus : RoomGlyph.Minus, RoomReading.MarkDp, Rgb(row.Kind == RoomLineKind.Added ? Passed : Failed),
                            new Vector2(S(RoomReading.GutterDp + RoomReading.MarkGapDp + RoomReading.MarkDp / 2f), -middle), "reading mark");
                }
                if (row.Number is int number)
                {
                    var (label, _) = Text(KitRole.Mono, RoomReading.NumberDp, RoomReading.LineDp, White(NoteAlpha), LineWords.Ours(number.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        0f, -down, gutter, 1);
                    label.transform.localPosition = new Vector3(gutter - RoomCursor.WordsWidth(label, 0f), -(down + S(RoomReading.TypeDp - RoomReading.NumberDp) * 0.6f), -RoomType.Units(0.5f));
                }
                if (row.Text != null && !row.Text.IsEmpty)
                {
                    var left = S(RoomReading.TextLeftDp(1f, row.Continued)) + row.Indent * advance;
                    var (label, _) = Text(KitRole.Mono, RoomReading.TypeDp, RoomReading.LineDp, White(changed ? 0.96f : 0.80f), row.Text, left, -down,
                        Mathf.Max(width - left - S(RoomReading.RightPadDp), advance), 1);
                    ReadingShown.Add(label);
                }
                down += pitch;
            }
            return down + S(ReadingWellPadDp);
        }

        private float LayHead(RoomCard card, float width, float down)
        {
            var headed = card.Head != RoomCardHead.None;
            var left = headed ? S(HeadDp + HeadGapDp) : 0f;
            var column = width - left;
            var tall = 0f;
            var words = new List<(KitRole Role, float Dp, float LineDp, Color Colour, LineWords Words, int Lines, float Before)>();
            if (card.Title != null)
            {
                // A name is never cut: two lines at the title's size, else three a step smaller, else four smaller still (a long
                // task's name beside its picture at larger text, the gate's 38-result).
                (float Dp, float Line, int Lines) chosen = (FinalTitleTypeDp, FinalTitleLineDp, 4);
                // A file's name in the mono face, as the list of changes sets it.
                var titleRole = card.TitleMono ? KitRole.Mono : KitRole.Title;
                foreach (var (dp, line, lines) in new[] { (TitleTypeDp, TitleLineDp, 2), (LongTitleTypeDp, LongTitleLineDp, 3) })
                {
                    var (probe, _) = Text(titleRole, dp, line, Color.white, card.Title, left, 0f, column, lines);
                    var fits = !probe.isTextOverflowing;
                    probe.gameObject.SetActive(false);
                    Shown.Remove(probe);
                    textsUsed[titleRole]--;
                    if (fits) { chosen = (dp, line, lines); break; }
                }
                words.Add((titleRole, chosen.Dp, chosen.Line, Color.white, card.Title, chosen.Lines, 0f));
            }
            // Measure first, then stand the words in the middle of the head's height where it has one.
            var laid = new List<(TextMeshPro Label, float Tall, float Before)>();
            foreach (var each in words)
            {
                var (label, height) = Text(each.Role, each.Dp, each.LineDp, each.Colour, each.Words, left, 0f, column, each.Lines);
                // A name of the person's own words past even four lines (an idea taken as its name) shows its lead cut at a
                // word, the ellipsis theirs; Halcyonic's own names are never cut.
                if (each.Words.AllTheirs) height = Excerpt(label, each.Words, column, each.Lines, each.LineDp);
                laid.Add((label, height, S(each.Before)));
                tall += S(each.Before) + height;
            }
            var block = headed ? Mathf.Max(S(HeadDp), tall) : tall;
            var y = down + (block - tall) / 2f;
            foreach (var (label, height, before) in laid)
            {
                y += before;
                label.transform.localPosition = new Vector3(left, -y, -RoomType.Units(0.5f));
                y += height;
            }
            if (headed)
            {
                var centre = new Vector2(S(HeadDp) / 2f, -(down + block / 2f));
                var side = new Vector2(S(HeadDp), S(HeadDp));
                switch (card.Head)
                {
                    case RoomCardHead.Agent:
                        Plate(side, side.x / 2f, Rgb(AgentOrb), centre, "agent orb");
                        Glyph(RoomGlyph.Agent, HeadGlyphDp, Color.white, centre, "agent");
                        break;
                    case RoomCardHead.Picture:
                        Plate(side, S(14f), White(0.10f), centre, "picture", White(0.25f), S(1f));
                        Glyph(RoomGlyph.Cube, HeadGlyphDp, Rgb(TileInk), centre, "picture");
                        break;
                    default:
                        Plate(side, S(HeadCornerDp), Rgb(ProjectTile), centre, "project emblem");
                        Glyph(RoomGlyph.Cube, HeadGlyphDp, Color.white, centre, "project");
                        break;
                }
            }
            return down + block;
        }

        private float LayGauges(IReadOnlyList<RoomGauge> gauges, float width, float down)
        {
            // Each window a well of its own the card's width, one above the next: its first line the window's glyph, the
            // number large and the window's word quiet on its baseline; its second the bar, every bar ending at one point,
            // with when it resets right-aligned after it.
            var count = gauges.Count;
            var inner = width - 2f * S(GaugePadXDp);
            var noteWidth = 0f;
            var notes = new List<TextMeshPro?>();
            foreach (var gauge in gauges)
            {
                if (gauge.Note == null) { notes.Add(null); continue; }
                var (note, _) = Text(KitRole.Body, GaugeResetDp, GaugeResetLineDp, White(NoteAlpha), LineWords.Ours(gauge.Note), 0f, 0f, RoomType.Units(2000f), 1);
                notes.Add(note);
                noteWidth = Mathf.Max(noteWidth, RoomCursor.WordsWidth(note, 0f));
            }
            var bar = inner - (noteWidth > 0f ? noteWidth + S(10f) : 0f);
            var tall = S(GaugePadTopDp) + S(GaugeNumberLineDp) + S(14f) + Mathf.Max(S(TrackTallDp), S(GaugeResetLineDp)) + S(GaugePadFootDp);
            for (var index = 0; index < count; index++)
            {
                var gauge = gauges[index];
                var top = down + index * (tall + S(GaugeGapDp));
                Plate(new Vector2(width, tall), S(TileCornerDp), White(WellAlpha), new Vector2(width / 2f, -(top + tall / 2f)), "gauge tile", under: true);
                var x = S(GaugePadXDp);
                var line = top + S(GaugePadTopDp);
                Glyph(GlyphOf(gauge.Kind), SlotGlyphDp, Rgb(TileInk, 0.86f), new Vector2(x + S(SlotGlyphBoxDp) / 2f, -(line + S(GaugeNumberLineDp) / 2f)), "gauge " + gauge.Kind);
                x += S(SlotGlyphBoxDp) + S(10f);
                var (number, _) = Text(KitRole.Title, GaugeNumberDp, GaugeNumberLineDp, Color.white, LineWords.Ours(gauge.PercentLeft + "%"), x, -line, inner, 1);
                x += RoomCursor.WordsWidth(number, 0f) + S(10f);
                // The window's word on the number's baseline: its line's foot against the number's.
                Text(KitRole.Label, GaugeLabelDp, GaugeLabelLineDp, White(NoteAlpha), gauge.Label, x, -(line + S(GaugeNumberLineDp) - S(GaugeLabelLineDp) - S(4f)), inner, 1);
                var middle = line + S(GaugeNumberLineDp) + S(14f) + Mathf.Max(S(TrackTallDp), S(GaugeResetLineDp)) / 2f;
                Plate(new Vector2(bar, S(TrackTallDp)), S(TrackTallDp) / 2f, White(0.10f), new Vector2(S(GaugePadXDp) + bar / 2f, -middle), "gauge track");
                var filled = bar * gauge.PercentLeft / 100f;
                if (filled > 0f)
                    Plate(new Vector2(filled, S(TrackTallDp)), Mathf.Min(S(TrackTallDp) / 2f, filled / 2f), White(0.62f), new Vector2(S(GaugePadXDp) + filled / 2f, -middle), "gauge fill");
                if (notes[index] is TextMeshPro note)
                    note.transform.localPosition = new Vector3(width - S(GaugePadXDp) - RoomCursor.WordsWidth(note, 0f), -(middle - S(GaugeResetLineDp) / 2f), -RoomType.Units(0.5f));
            }
            return down + count * tall + (count - 1) * S(GaugeGapDp);
        }

        /// <summary>The most lines a slot's value wraps to where it is wider than the card: a long web address, whole.</summary>
        public const int SlotValueLines = 6;

        private float LaySlots(IReadOnlyList<RoomSlot> slots, float width, float down)
        {
            var count = Mathf.Min(slots.Count, RoomCard.MostSlots);
            // Each value on one line, never broken or cut: a well is at least as wide as its value and its room. The wells
            // fill a row edge to edge, sharing what is left; where a row cannot hold them (larger text), the next well starts
            // a row of its own, and every row still meets both edges.
            var values = new List<(TextMeshPro Label, float Tall)>();
            var least = new float[count];
            var extra = new int[count];
            for (var index = 0; index < count; index++)
            {
                var laid = slots[index].Mono
                    ? Text(KitRole.Mono, CaptionTypeDp, CaptionLineDp, Color.white, slots[index].Value, 0f, 0f, RoomType.Units(2000f), 1)
                    : Text(KitRole.Body, CaptionTypeDp, CaptionLineDp, Color.white, slots[index].Value, 0f, 0f, RoomType.Units(2000f), 1);
                values.Add(laid);
                least[index] = Mathf.Min(width, Mathf.Max(RoomCursor.WordsWidth(laid.Label, 0f), S(SlotGlyphBoxDp)) + 2f * S(SlotPadDp));
                if (RoomCursor.WordsWidth(laid.Label, 0f) + 2f * S(SlotPadDp) > width + 1e-4f)
                {
                    // A value wider than the card (a long web address) is shown whole, wrapped on the card's width in a well of
                    // its own, never cut (the critic, 2026-11-12; every value that goes out is shown whole, ADR 0060).
                    laid.Label.textWrappingMode = TextWrappingModes.Normal;
                    var wrapped = KitType.Lay(laid.Label, width - 2f * S(SlotPadDp), SlotValueLines);
                    extra[index] = Mathf.Max(wrapped.Lines, 1) - 1;
                    least[index] = width;
                    SlotsWrapped++;
                }
            }
            var rows = new List<List<int>> { new List<int>() };
            var used = 0f;
            // Four stand as two rows of two (a new folder from a starter) where each holds half the card; otherwise, and for
            // fewer, they fill a row, wrapping where it cannot hold them, so no well is ever narrower than its value (the parts
            // check, 2026-11-12: a long folder forced into half a row squeezed the level to "Neve...").
            var half = (width - S(TileGapDp)) / 2f;
            var pairs = count == 4;
            for (var index = 0; index < count; index++) pairs &= least[index] <= half + 1e-4f;
            if (pairs) rows = new List<List<int>> { new List<int> { 0, 1 }, new List<int> { 2, 3 } };
            for (var index = 0; index < (pairs ? 0 : count); index++)
            {
                var row = rows[rows.Count - 1];
                var need = least[index] + (row.Count > 0 ? S(TileGapDp) : 0f);
                if (row.Count > 0 && used + need > width + 1e-4f)
                {
                    rows.Add(row = new List<int>());
                    used = 0f;
                    need = least[index];
                }
                row.Add(index);
                used += need;
            }
            var well = S(SlotPadDp) + S(SlotGlyphBoxDp) + S(CaptionGapDp) + S(CaptionLineDp) + S(SlotFootDp);
            var top = down;
            // The first row's wells, by place: a row wrapped under it stands on the same columns, never stretched past them.
            var columns = new List<float>();
            var rowIndex = 0;
            foreach (var row in rows)
            {
                // A row under the first keeps the first row's columns only where they hold every value in it; else it lays its own.
                var aligned = rowIndex > 0 && row.Count > 1 && columns.Count >= row.Count;
                if (aligned)
                {
                    var need = 0f;
                    for (var at = 0; at < row.Count; at++) need += Mathf.Max(least[row[at]], columns[at]);
                    aligned = need + S(TileGapDp) * (row.Count - 1) <= width + 1e-4f;
                }
                var rowWell = well;
                foreach (var index in row) rowWell = Mathf.Max(rowWell, well + extra[index] * S(CaptionLineDp));
                var sum = 0f;
                foreach (var index in row) sum += least[index];
                var share = (width - S(TileGapDp) * (row.Count - 1) - sum) / row.Count;
                // Equal wells wherever every value fits one (SCREENS's critic, 2026-11-11); else each its value's room and a share.
                var even = (width - S(TileGapDp) * (row.Count - 1)) / row.Count;
                var evenFits = true;
                foreach (var index in row) evenFits &= least[index] <= even + 1e-4f;
                var x = 0f;
                var place = 0;
                foreach (var index in row)
                {
                    var slot = slots[index];
                    // The first row meets both edges. A card of one slot stands it at its own width: a fact, not a bar across
                    // the card (the critic, 2026-11-11). A wrapped row keeps the first row's columns, so its wells line up; a
                    // wrapped slot alone spans the card, never leaving a hole beside it.
                    var tile = count == 1 ? least[index]
                        : rowIndex == 0 ? (evenFits ? even : least[index] + Mathf.Max(0f, share))
                        : row.Count == 1 ? width
                        : aligned ? Mathf.Max(least[index], columns[row.IndexOf(index)])
                        : evenFits ? even : least[index] + Mathf.Max(0f, share);
                    if (columns.Count < rows[0].Count) columns.Add(tile);
                    place++;
                    values[index].Label.rectTransform.sizeDelta = new Vector2(tile - 2f * S(SlotPadDp), values[index].Label.rectTransform.sizeDelta.y);
                    Plate(new Vector2(tile, rowWell), S(TileCornerDp), White(WellAlpha), new Vector2(x + tile / 2f, -(top + rowWell / 2f)), "slot " + slot.Kind, under: true);
                    // A slot that changes its fact in place takes a press across its whole well, as a row picked from does.
                    if (slot.Press is KitControl change) RowTarget(change, new Vector2(x + tile / 2f, -(top + rowWell / 2f)), new Vector2(tile, rowWell));
                    // The glyph's drawn edge on the value's left edge: its box moved left by where its ink starts.
                    var glyphOf = slot.Kind == RoomKind.Level ? (RoomGlyph?)null : slot.Kind == RoomKind.Passed ? RoomGlyph.Check : slot.Kind == RoomKind.Failed ? RoomGlyph.Alert : GlyphOf(slot.Kind);
                    var ink = glyphOf is RoomGlyph drawn ? RoomGlyphs.InkLeft(drawn) / RoomGlyphs.Grid * S(SlotGlyphDp) : 0f;
                    var mark = new Vector2(x + S(SlotPadDp) + S(SlotGlyphDp) / 2f - ink, -(top + S(SlotPadDp) + S(SlotGlyphBoxDp) / 2f));
                    if (slot.Kind == RoomKind.Level) mark.x = x + S(SlotPadDp) + S(SlotLevelDp) / 2f;
                    switch (slot.Kind)
                    {
                        case RoomKind.Level:
                            // The chosen answer's own mark at a glyph's size, neutral at rest: its light disc, its dark check.
                            Plate(new Vector2(S(SlotLevelDp), S(SlotLevelDp)), S(SlotLevelDp) / 2f, Rgb(ChosenAtRest), mark, "level mark");
                            Glyph(RoomGlyph.Check, SlotLevelCheckDp, Rgb(Dark), mark, "level");
                            break;
                        case RoomKind.Passed:
                            Glyph(RoomGlyph.Check, SlotGlyphDp, Rgb(Passed), mark, "passed");
                            break;
                        case RoomKind.Failed:
                            Glyph(RoomGlyph.Alert, SlotGlyphDp, Rgb(Failed), mark, "failed");
                            break;
                        case RoomKind.Added:
                        case RoomKind.Removed:
                            // A count of lines added or removed carries the change's own hue, as its rows' marks do.
                            Glyph(GlyphOf(slot.Kind), SlotGlyphDp, Rgb(slot.Kind == RoomKind.Added ? Passed : Failed), mark, "slot " + slot.Kind);
                            break;
                        case RoomKind.None:
                            // A slot with no kind says its value alone: no mark (the critic, 2026-11-12).
                            break;
                        default:
                            Glyph(GlyphOf(slot.Kind), SlotGlyphDp, Rgb(TileInk, 0.86f), mark, "slot " + slot.Kind);
                            break;
                    }
                    // A slot with no mark stands its value in the middle of its well, never at the foot of a mark's empty place
                    // (the critic, 2026-11-13: a model's name low in a grey box read as a stray label).
                    var valueTop = slot.Kind == RoomKind.None ? top + (rowWell - S(CaptionLineDp) * (1 + extra[index])) / 2f
                        : top + S(SlotPadDp) + S(SlotGlyphBoxDp) + S(CaptionGapDp);
                    values[index].Label.transform.localPosition = new Vector3(x + S(SlotPadDp), -valueTop, -RoomType.Units(0.5f));
                    x += tile + S(TileGapDp);
                }
                rowIndex++;
                top += rowWell + S(TileGapDp);
            }
            return top - S(TileGapDp);
        }

        /// <summary>The steps as rows, each its mark at the left and its name beside it on one line, where a row of columns cannot hold them.</summary>
        private float LayStepRows(IReadOnlyList<RoomCardStep> steps, TextMeshPro[] labels, float width, float down)
        {
            var left = S(StepDp) + S(RowGapDp);
            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                var top = down + index * (S(StepDp) + S(StepRowGapDp));
                var centre = new Vector2(S(StepDp) / 2f, -(top + S(StepDp) / 2f));
                StepMark(step, centre);
                var label = labels[index];
                label.textWrappingMode = TextWrappingModes.Normal;
                label.alignment = TextAlignmentOptions.Left;
                var laid = KitType.Lay(label, width - left, 2);
                var tall = Mathf.Max(laid.Lines, 1) * S(StepNameLineDp);
                label.rectTransform.sizeDelta = new Vector2(width - left, label.rectTransform.sizeDelta.y);
                label.transform.localPosition = new Vector3(left, -(top + (S(StepDp) - tall) / 2f), -RoomType.Units(0.5f));
            }
            return down + steps.Count * S(StepDp) + (steps.Count - 1) * S(StepRowGapDp);
        }

        /// <summary>The gap between two steps standing as rows.</summary>
        public const float StepRowGapDp = 10f;

        private void StepMark(RoomCardStep step, Vector2 centre)
        {
            var (ground, ink, glyph, edge) = step.Tone switch
            {
                RoomTone.Good => (Rgb(GoodFill), Color.white, (RoomGlyph?)RoomGlyph.Check, Color.clear),
                RoomTone.Bad => (Rgb(BadFill), Color.white, RoomGlyph.Alert, Color.clear),
                RoomTone.Working => (Rgb(WorkingFill), Color.white, RoomGlyph.Wrench, Color.clear),
                // Not run: an outline that holds over the bright room, nothing in it.
                // At 0.46 the outline stood at 2.56:1 over the bright room (the critic, 2026-11-12); a mark needs 3:1.
                RoomTone.Skipped => (Color.clear, Color.clear, (RoomGlyph?)null, White(0.64f)),
                _ => (Rgb(QuietFill), Color.white, RoomGlyph.Wait, Color.clear),
            };
            Plate(new Vector2(S(StepDp), S(StepDp)), S(StepCornerDp), ground, centre, "step " + step.Name, edge, edge.a > 0f ? S(2f) : 0f);
            if (glyph is RoomGlyph shown) Glyph(shown, StepGlyphDp, ink, centre, "step " + step.Name, MarkStroke);
            if (step.TriesOf > 0) Pips(step.TriesUsed, step.TriesOf, step.Tone, new Vector2(centre.x, centre.y - S(StepDp) / 2f - S(PipDropDp) + S(PipDp) / 2f));
        }

        private float LaySteps(IReadOnlyList<RoomCardStep> steps, float width, float down)
        {
            // Each step as wide as its name or its mark, whichever is wider; the joins share what is left.
            var widths = new float[steps.Count];
            var labels = new TextMeshPro[steps.Count];
            var tallest = 0f;
            for (var index = 0; index < steps.Count; index++)
            {
                var (label, tall) = Text(KitRole.Label, StepNameDp, StepNameLineDp, steps[index].Tone == RoomTone.Skipped ? White(NoteAlpha) : White(0.90f),
                    steps[index].Words ?? LineWords.Ours(steps[index].Name ?? ""), 0f, 0f, width, 1, TextAlignmentOptions.Top);
                labels[index] = label;
                widths[index] = Mathf.Max(S(StepDp), RoomCursor.WordsWidth(label, 0f));
                tallest = Mathf.Max(tallest, tall);
            }
            // Equal columns, each as wide as the widest name or mark, so every node stands the same pitch from the next and
            // each join runs the same inset (the critic, 2026-11-11).
            var widest = 0f;
            foreach (var each in widths) widest = Mathf.Max(widest, each);
            // Where the columns would run past the card (four steps with a long name at larger text: "VR Offered" stood behind
            // Back, XR's critic of 06:24Z), each column takes its share of the card and its name wraps to two lines, centred.
            var least = (steps.Count - 1) * (S(10f) + 2f * S(StepJoinGapDp));
            if (widest * steps.Count + least > width + 1e-4f)
            {
                widest = Mathf.Max(S(StepDp), (width - least) / steps.Count);
                tallest = 0f;
                var whole = true;
                foreach (var label in labels)
                {
                    label.textWrappingMode = TextWrappingModes.Normal;
                    var wrapped = KitType.Lay(label, widest, 2);
                    label.ForceMeshUpdate(true);
                    whole &= !label.isTextOverflowing;
                    tallest = Mathf.Max(tallest, Mathf.Max(wrapped.Lines, 1) * S(StepNameLineDp));
                }
                // Not even two lines a column hold a name: the steps stand as rows, each its mark and its name beside it.
                if (!whole) return LayStepRows(steps, labels, width, down);
            }
            for (var index = 0; index < widths.Length; index++) widths[index] = widest;
            var sum = widest * widths.Length;
            var joins = steps.Count - 1;
            var join = joins > 0 ? Mathf.Max(S(10f), (width - sum - 2f * S(StepJoinGapDp) * joins) / joins) : 0f;
            var x = 0f;
            var middle = -(down + S(StepDp) / 2f);
            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                var centre = new Vector2(x + widths[index] / 2f, middle);
                StepMark(step, centre);
                // The name centred under its mark.
                var label = labels[index];
                label.rectTransform.sizeDelta = new Vector2(widths[index], label.rectTransform.sizeDelta.y);
                label.transform.localPosition = new Vector3(x, -(down + S(StepDp) + S(StepNameGapDp)), -RoomType.Units(0.5f));
                x += widths[index];
                if (index < joins)
                {
                    var joined = step.Tone == RoomTone.Good;
                    Plate(new Vector2(join, S(StepJoinDp)), S(StepJoinDp) / 2f, joined ? Rgb(GoodFill, 0.70f) : White(0.28f),
                        new Vector2(x + S(StepJoinGapDp) + join / 2f, middle), "join");
                    x += join + 2f * S(StepJoinGapDp);
                }
            }
            return down + S(StepDp) + S(StepNameGapDp) + tallest;
        }

        private float LayList(IReadOnlyList<RoomListRow> rows, float width, float down)
        {
            for (var index = 0; index < rows.Count && index < RoomCard.ListAPage; index++)
            {
                var row = rows[index];
                if (index > 0) Plate(new Vector2(width, S(1f)), 0f, White(0.10f), new Vector2(width / 2f, -(down + S(0.5f))), "hairline");
                var top = down + S(RowPadDp);
                var (tone, glyph) = ToneOf(row);
                // The note reads over any room: a quiet near-white whatever the kind, the kind's colour on its mark alone
                // (the lead, 2026-11-11: "Edited in Both" in the failure's tint read 2.89 to 1 over the bright room).
                var (ground, ink, note) = tone switch
                {
                    RoomTone.Good => (Rgb(PassedGround, ToneTileAlpha * 2f), Rgb(PassedInk), White(NoteAlpha)),
                    RoomTone.Bad => (Rgb(FailedGround, ToneTileAlpha * 2f), Rgb(FailedInk), White(NoteAlpha)),
                    RoomTone.Working => (Rgb(Accent, 0.16f), Rgb(Accent), White(NoteAlpha)),
                    RoomTone.Attention => (Rgb(Attention, 0.16f), Rgb(Attention), White(NoteAlpha)),
                    // A neutral row's mark in a quiet tile, as a slot's (the critic, 2026-11-11: a bare glyph read as no mark).
                    _ => (White(RowTileAlpha), White(0.85f), White(NoteAlpha)),
                };
                if (row.Chosen) (ground, ink) = (Rgb(Accent, 0.24f), Rgb(AccentNote));
                // The note at the right first, so the name knows how far it may run.
                var noteWidth = 0f;
                TextMeshPro? noteLabel = null;
                if (row.Note != null)
                {
                    (noteLabel, _) = Text(KitRole.Body, RowNoteDp, RowNoteLineDp, note, LineWords.Ours(row.Note), 0f, 0f, width / 2f, 1);
                    noteWidth = RoomCursor.WordsWidth(noteLabel, 0f);
                }
                // A row picked from carries a chevron at its right, the note before it, and takes a press across its whole width.
                var picked = row.Press != null;
                var chevronRoom = picked ? S(ChevronDp + ChevronGapDp) : 0f;
                var nameLeft = S(RowTileDp + RowGapDp);
                var nameWidth = width - nameLeft - chevronRoom - (noteWidth > 0f ? noteWidth + S(RowGapDp) : 0f);
                // A name is never cut: a path or a moment's name takes a second line where one is not enough (a long path at larger text).
                // A name of the person's own words (a task's title) takes a third line before anything is cut (XR's critic, 06:33Z).
                var nameLines = row.Name.AllTheirs && !row.Mono ? 3 : 2;
                var (nameLabel, nameTall) = row.Mono
                    ? Text(KitRole.Mono, RowNameDp - 3f, RowNameLineDp, Color.white, row.Name, nameLeft, 0f, nameWidth, nameLines)
                    : Text(KitRole.Body, RowNameDp, RowNameLineDp, Color.white, row.Name, nameLeft, 0f, nameWidth, nameLines);
                // The person's own words of any length, on a row that opens them whole: their lead, cut at a word.
                if (row.Excerpt) nameTall = Excerpt(nameLabel, row.Name, nameWidth, nameLines, RowNameLineDp);
                // What the row belongs to, a quieter line under its name.
                TextMeshPro? underLabel = null;
                var underTall = 0f;
                if (row.Under != null)
                {
                    // Outside words lead cut on one line; the product's own (a host's reason) are never cut, and take up to three.
                    (underLabel, underTall) = Text(KitRole.Body, RowNoteDp, RowNoteLineDp, White(NoteAlpha), row.Under, nameLeft, 0f, nameWidth, row.Under.AllTheirs ? 1 : 3);
                    if (row.Under.AllTheirs) underTall = Excerpt(underLabel, row.Under, nameWidth, 1, RowNoteLineDp);
                }
                var block = nameTall + (underLabel != null ? S(2f) + underTall : 0f);
                var rowTall = Mathf.Max(S(RowTileDp), block);
                var middle = top + rowTall / 2f;
                nameLabel.transform.localPosition = new Vector3(nameLeft, -(middle - block / 2f), -RoomType.Units(0.5f));
                if (underLabel != null) underLabel.transform.localPosition = new Vector3(nameLeft, -(middle - block / 2f + nameTall + S(2f)), -RoomType.Units(0.5f));
                if (noteLabel != null) noteLabel.transform.localPosition = new Vector3(width - chevronRoom - noteWidth, -(middle - S(RowNoteLineDp) / 2f), -RoomType.Units(0.5f));
                if (picked)
                {
                    Glyph(RoomGlyph.Chevron, ChevronDp, White(0.55f), new Vector2(width - S(ChevronDp) / 2f, -middle), "row chevron");
                    RowTarget(row.Press!, new Vector2(width / 2f, -middle), new Vector2(width, rowTall + 2f * S(RowPadDp)));
                }
                // A row with no kind draws no mark: a minus in a tile read as a control to collapse (the critic, 2026-11-12).
                if (row.Kind != RoomKind.None)
                {
                    var tileCentre = new Vector2(S(RowTileDp) / 2f, -middle);
                    Plate(new Vector2(S(RowTileDp), S(RowTileDp)), S(RowTileCornerDp), ground, tileCentre, "row tile " + row.Kind,
                        row.Chosen ? Rgb(Accent, 0.70f) : Color.clear, row.Chosen ? S(1.5f) : 0f);
                    Glyph(glyph, RowGlyphDp, ink, tileCentre, "row " + row.Kind);
                }
                down = top + rowTall + S(RowPadDp);
            }
            return down;
        }

        /// <summary>
        /// Lays outside words that may run long as their lead cut at a word (<see cref="LineWords.CutToFit"/>) where they do not
        /// fit <paramref name="lines"/>, and returns the height they take. Halcyonic's own runs are never cut.
        /// </summary>
        private float Excerpt(TextMeshPro label, LineWords words, float width, int lines, float lineDp)
        {
            label.ForceMeshUpdate(true);
            if (!label.isTextOverflowing) return Mathf.Max(label.textInfo.lineCount, 1) * S(lineDp);
            var runs = GlazeRuns.Of(label);
            var cut = words.CutToFit(tried =>
            {
                runs.Set(tried);
                label.ForceMeshUpdate(true);
                return !label.isTextOverflowing;
            });
            // At a word: where the cut fell inside one, back to the space before it, so no word is shown in part.
            var text = cut.Text;
            if (text.EndsWith("\u2026", System.StringComparison.Ordinal))
            {
                var body = text.Substring(0, text.Length - 1).TrimEnd();
                var whole = words.Text;
                var space = body.LastIndexOf(' ');
                if (body.Length < whole.Length && !char.IsWhiteSpace(whole[body.Length]) && space > 0) body = body.Substring(0, space);
                cut = LineWords.TheirName(body.TrimEnd(' ', ',', ';', ':') + "\u2026");
            }
            runs.Set(cut);
            var laid = KitType.Lay(label, width, lines);
            Excerpts.Add(label);
            return Mathf.Max(laid.Lines, 1) * S(lineDp);
        }

        /// <summary>
        /// The tone and the mark a list row takes from its kind, where its own tone says nothing more: green for new or
        /// passed, red for removed, failed or in conflict, and neutral for the rest (an edit, a file that reverts), so the
        /// accent stays the main action's and the one thing under way's.
        /// </summary>
        public static (RoomTone Tone, RoomGlyph Glyph) ToneOf(RoomListRow row)
        {
            var glyph = GlyphOf(row.Kind);
            if (row.Tone != RoomTone.Quiet) return (row.Tone, glyph);
            return row.Kind switch
            {
                RoomKind.Added or RoomKind.Passed => (RoomTone.Good, glyph),
                RoomKind.Removed or RoomKind.Conflict or RoomKind.Failed => (RoomTone.Bad, glyph),
                _ => (RoomTone.Quiet, glyph),
            };
        }

        /// <summary>The glyph of a kind, in a slot's tile or a list row's (the addendum's A17: one glyph, one meaning).</summary>
        public static RoomGlyph GlyphOf(RoomKind kind) => kind switch
        {
            RoomKind.Starter => RoomGlyph.Pin,
            RoomKind.Project => RoomGlyph.Cube,
            RoomKind.File => RoomGlyph.File,
            RoomKind.Agent => RoomGlyph.Agent,
            RoomKind.Level => RoomGlyph.Check,
            RoomKind.Folder => RoomGlyph.Folder,
            RoomKind.Passed => RoomGlyph.Check,
            RoomKind.Failed => RoomGlyph.Alert,
            RoomKind.NotRun => RoomGlyph.Wait,
            RoomKind.Waiting => RoomGlyph.Wait,
            RoomKind.Task => RoomGlyph.Flag,
            RoomKind.Says => RoomGlyph.Bubble,
            RoomKind.ToAdd => RoomGlyph.File,
            RoomKind.Changed => RoomGlyph.Wave,
            RoomKind.Added => RoomGlyph.Plus,
            RoomKind.Removed => RoomGlyph.Minus,
            RoomKind.Conflict => RoomGlyph.Swap,
            RoomKind.Started => RoomGlyph.Pin,
            RoomKind.Worked => RoomGlyph.Flag,
            RoomKind.Reverts => RoomGlyph.Rewind,
            RoomKind.Computer => RoomGlyph.Computer,
            RoomKind.Tools => RoomGlyph.Wrench,
            RoomKind.App => RoomGlyph.App,
            RoomKind.Session => RoomGlyph.Timer,
            RoomKind.Week => RoomGlyph.Calendar,
            RoomKind.Link => RoomGlyph.Link,
            RoomKind.Wifi => RoomGlyph.Wifi,
            _ => RoomGlyph.Minus,
        };

        /// <summary>
        /// Tries as pips centred at <paramref name="centre"/>: <paramref name="of"/> of them, the first <paramref name="used"/>
        /// lit in the tone's own hue, each ringed in the panel's dark so it reads hung on the mark.
        /// </summary>
        private void Pips(int used, int of, RoomTone tone, Vector2 centre)
        {
            // A try used is its tone's full fill inside a light ring: the red alone stood at 1.6:1 on the glass over the bright
            // room even at full fill, under 3:1 for a mark (the critics, 2026-11-12), so the ring carries it.
            var lit = tone switch { RoomTone.Good => Rgb(GoodFill), RoomTone.Bad => Rgb(BadFill), RoomTone.Attention => Rgb(Attention), _ => Rgb(WorkingFill) };
            var pitch = S(PipDp + PipGapDp);
            var from = centre.x - pitch * (of - 1) / 2f;
            for (var index = 0; index < of; index++)
            {
                var at = new Vector2(from + index * pitch, centre.y);
                var spent = index < used;
                // Filled where used, hollow where left: like a game's lives, in the node's own colour; a squircle, as the node.
                // A try left is a ring firm enough to read over the bright room, a try spent the fill.
                Plate(new Vector2(S(PipDp), S(PipDp)), S(PipCornerDp), spent ? lit : Color.clear, at, spent ? "pip used" : "pip",
                    // A try left is a light ring too: in its tone it stood at 2.0:1 over the bright room.
                    spent ? White(0.92f) : White(0.64f), S(PipLineDp), front: true);
            }
        }

        /// <summary>Lays words of <paramref name="role"/> at a size and line pitch of their own, their box's top left at (x, y); returns the label and how tall its lines stand.</summary>
        private (TextMeshPro Label, float Tall) Text(KitRole role, float dp, float lineDp, Color colour, LineWords words, float x, float y, float width, int lines,
            TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            if (!texts.TryGetValue(role, out var pool)) texts[role] = pool = new List<TextMeshPro>();
            textsUsed.TryGetValue(role, out var used);
            if (used >= pool.Count) pool.Add(KitType.Create(transform, role + " words", role, Color.white, order, TextAlignmentOptions.TopLeft, room: true));
            var label = pool[used];
            textsUsed[role] = used + 1;
            label.gameObject.SetActive(true);
            label.alignment = alignment;
            label.color = colour;
            label.fontSize = GlazeTokens.FontSize(S(dp) / RoomType.Units(1f) * RoomType.Units(1f));
            label.lineSpacing = KitType.LineSpacingFor(label, lineDp / dp);
            label.textWrappingMode = lines > 1 ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            var runs = GlazeRuns.Of(label);
            runs.Set(words);
            runs.SetProseMode(true);
            var laid = KitType.Lay(label, width, lines);
            label.transform.localPosition = new Vector3(x, y, -RoomType.Units(0.5f));
            Shown.Add(label);
            return (label, Mathf.Max(laid.Lines, 1) * S(lineDp));
        }

        private void Plate(Vector2 size, float radius, Color fill, Vector2 centre, string what, Color edge = default, float edgeWidth = 0f, bool under = false, bool front = false)
        {
            if (platesUsed >= plates.Count) plates.Add(RoomShape.Create(transform, "Tile", order - 4));
            var plate = plates[platesUsed++];
            plate.gameObject.SetActive(true);
            // A tile under the marks; a pip over the mark it hangs on, each laid after the one under it.
            plate.GetComponent<Renderer>().sortingOrder = under ? order - 5 : front ? order + 1 : order - 4;
            // The page's alphas are mixed in sRGB; the headset blends in linear light, where the same alpha shows far lighter.
            // Every faint fill and edge over the panel takes the alpha that shows as the page's (RoomRows.LightAlpha).
            plate.Draw(size, radius, AsShown(fill), AsShown(edge), edgeWidth);
            plate.transform.localPosition = new Vector3(centre.x, centre.y, -RoomType.Units(under ? 0.2f : front ? 0.6f : 0.3f));
            Plates.Add((plate, what));
        }

        private void Glyph(RoomGlyph glyph, float dp, Color colour, Vector2 centre, string what, float stroke = RoomGlyphs.Stroke)
        {
            if (glyphsUsed >= glyphs.Count) glyphs.Add(RoomGlyphView.Create(transform, "Mark", order));
            var view = glyphs[glyphsUsed++];
            view.Show(glyph, dp * GlazeText.Scale, colour, stroke);
            view.transform.localPosition = new Vector3(centre.x, centre.y, -RoomType.Units(0.5f));
            Glyphs.Add((view, what));
        }

        /// <summary>A row's press target, centred at <paramref name="centre"/>, as large as the row: it raises <see cref="RowPressed"/> with the row's control.</summary>
        private void RowTarget(KitControl control, Vector2 centre, Vector2 size)
        {
            if (rowPressesUsed >= rowPresses.Count)
            {
                var go = new GameObject("Row press");
                go.transform.SetParent(transform, false);
                var index = rowPresses.Count;
                var press = KitPress.Create(go, size);
                press.Pressed += () => { if (rowControls[index] is KitControl pressed) RowPressed?.Invoke(pressed); };
                rowPresses.Add((press, go));
                rowControls.Add(null);
            }
            var (target, host) = rowPresses[rowPressesUsed];
            // A row, slot or card press that now does something else arrives anew and settles again (AUDIT, 2026-11-12).
            if (rowControls[rowPressesUsed] is not KitControl was || RoomButton.Doing(was) != RoomButton.Doing(control)) target.Unsettle();
            rowControls[rowPressesUsed] = control;
            rowPressesUsed++;
            host.SetActive(true);
            target.Resize(size);
            target.Available = true;
            host.transform.localPosition = new Vector3(centre.x, centre.y, -RoomType.Units(0.7f));
        }

        private void HideUnused()
        {
            for (var index = rowPressesUsed; index < rowPresses.Count; index++)
            {
                rowPresses[index].Target.SetActive(false);
                rowControls[index] = null;
            }
            foreach (var pair in texts)
            {
                textsUsed.TryGetValue(pair.Key, out var used);
                for (var index = used; index < pair.Value.Count; index++) pair.Value[index].gameObject.SetActive(false);
            }
            for (var index = platesUsed; index < plates.Count; index++) plates[index].gameObject.SetActive(false);
            for (var index = glyphsUsed; index < glyphs.Count; index++) glyphs[index].Hide();
            if (!codeUsed && codeView != null) codeView.Hide();
        }

        /// <summary>A colour as the page shows it over the panel, its alpha the one a blend in linear light needs to look the same.</summary>
        public static Color AsShown(Color colour) =>
            colour.a <= 0f || colour.a >= 1f ? colour : new Color(colour.r, colour.g, colour.b, RoomRows.LightAlpha(colour.a, RoomRows.PanelGround));

        /// <summary>A length in dp at the person's text size, in the plane's units.</summary>
        private static float S(float dp) => RoomType.Scaled(dp);

        private static Color White(float alpha) => new Color(1f, 1f, 1f, alpha);

        private static Color Rgb(int rgb, float alpha = 1f) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
    }
}

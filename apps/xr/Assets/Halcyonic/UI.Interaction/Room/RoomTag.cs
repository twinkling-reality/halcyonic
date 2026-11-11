#nullable enable
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// A name tag (DESIGN 1.2): one line of words on the panel's own material, corner 14 dp, 14 dp of room beside the
    /// words and 6 dp above and below, as wide as its words. Under a character it names the task alone; where the row
    /// stood while the agent works it names the agent and its level (the addendum's A1), and is never pressed. Picked, it
    /// carries the 1.5 dp ring #E4E8EA. Where the task needs the person, or the plate names a level, a chip of its own
    /// floats beside the tag, never inside its plate (the addendum's A17: a state is never a suffix after a dot). Its
    /// origin is its plate's middle.
    /// </summary>
    public sealed class RoomTag : MonoBehaviour
    {
        public const float PadXDp = 14f;
        public const float PadYDp = 6f;
        public const float RingDp = 1.5f;
        public const int RingColour = 0xE4E8EA;

        /// <summary>
        /// Where the task needs the person: 10 dp after the plate, on its middle line, a chip of the state's own word
        /// (<see cref="StateLanguage.WordOf"/>, "Needs You") in the label face, 15 dp, in the attention colour of its own,
        /// never the accent the main action keeps: #FFD27A on #543C12 at 88 percent, a 1 dp edge of #F2B134 at 50 percent;
        /// 10 dp of room beside its word and 3 dp above and below, its corner 10 dp.
        /// </summary>
        public const float ChipGapDp = 10f;
        public const float ChipPadXDp = 10f;
        public const float ChipPadYDp = 3f;
        public const float ChipCornerDp = 10f;
        public const float ChipTypeDp = 15f;
        public const float ChipLineDp = 20f;
        public const int ChipColour = 0x543C12;
        public const float ChipAlpha = 0.88f;
        public const int ChipEdgeColour = 0xF2B134;
        public const float ChipEdgeAlpha = 0.50f;
        public const float ChipEdgeDp = 1f;
        public const int ChipWordsColour = 0xFFD27A;

        /// <summary>A quiet chip, as the plate's level is: white words on #282E33 at 78 percent, a 1 dp edge of white at 6 percent, so the name reads first.</summary>
        public const int QuietChipColour = 0x282E33;
        public const float QuietChipAlpha = 0.78f;
        public const float QuietEdgeAlpha = 0.06f;

        /// <summary>A level's chip holds a chosen fact: before its word, the chosen answer's small mark (as <see cref="RoomButton.AsideMarkDp"/>).</summary>
        public bool Marked => mark.gameObject.activeSelf;

        private RoomSurface plate = null!;
        private TextMeshPro words = null!;
        private Surface chip = null!;
        private TextMeshPro chipWords = null!;
        private Surface mark = null!;
        private RoomGlyphView check = null!;
        private RoomGlyphView agent = null!;
        private Surface ground = null!;

        /// <summary>The agent's plate's own quiet ground, where the tag's glass is hidden.</summary>
        public Surface Ground => ground;

        /// <summary>
        /// The agent's plate while it works (the addendum's A17): the agent's own glyph, 18 dp, 8 dp before its name, on a quiet
        /// finish of its own (#101316 at 62 percent, no ring, no edge), so it never reads as a task's tag or a press.
        /// </summary>
        public const float AgentGlyphDp = 18f;
        public const float AgentGlyphGapDp = 8f;
        public const int PlateGround = 0x101316;
        public const float PlateAlpha = 0.62f;

        /// <summary>Whether the tag is the agent's plate: its glyph before the name, its quiet finish.</summary>
        public bool IsPlate { get; private set; }

        public RoomGlyphView AgentGlyph => agent;

        /// <summary>How wide and tall the tag's plate stands as last shown; a chip floats beside it, outside.</summary>
        public Vector2 Size { get; private set; }

        public RoomSurface Plate => plate;

        public TextMeshPro Words => words;

        /// <summary>Whether the tag says its task needs the person: the cyan chip after the name.</summary>
        public bool NeedsYou { get; private set; }

        /// <summary>Whether a chip stands after the name: Needs You, or the plate's level.</summary>
        public bool Chipped => chip.gameObject.activeSelf;

        public Surface Chip => chip;

        public TextMeshPro ChipWords => chipWords;

        /// <summary>Whether the picked ring shows: the plate's own edge, 1.5 dp of #E4E8EA.</summary>
        public bool Picked => plate.Ring != null;

        /// <summary>A tag under <paramref name="parent"/>, hidden, its plate drawn at <paramref name="order"/> and its words over it.</summary>
        public static RoomTag Create(Transform parent, int order = KitType.Order.Plate)
        {
            var go = new GameObject("Tag");
            go.transform.SetParent(parent, false);
            var tag = go.AddComponent<RoomTag>();
            tag.plate = RoomSurface.Create(go.transform, "Plate", RoomSurfaceKind.Tag, order);
            tag.words = RoomType.Create(go.transform, "Words", Color.white, KitType.Order.Words);
            tag.chip = Surface.Create(go.transform, "Needs you", KitType.Order.Words);
            tag.chipWords = KitType.Create(go.transform, "Chip words", KitRole.Label, Hex(ChipWordsColour), KitType.Order.ControlWords, TextAlignmentOptions.TopLeft, room: true);
            tag.agent = RoomGlyphView.Create(go.transform, "Agent", KitType.Order.Words);
            tag.agent.Hide();
            tag.ground = Surface.Create(go.transform, "Plate ground", order);
            tag.ground.gameObject.SetActive(false);
            tag.mark = Surface.Create(go.transform, "Level mark", KitType.Order.ControlWords);
            tag.check = RoomGlyphView.Create(go.transform, "Level check", KitType.Order.ControlWords + 1);
            tag.mark.gameObject.SetActive(false);
            tag.check.Hide();
            tag.chip.gameObject.SetActive(false);
            RoomBand.Underlay(tag.words);
            go.SetActive(false);
            return tag;
        }

        /// <summary>
        /// Shows <paramref name="said"/> on one line at the person's text size, the tag as wide as the words need, with its
        /// ring where <paramref name="picked"/>, and where <paramref name="needsYou"/> the chip "Needs You" after the name;
        /// otherwise <paramref name="chip"/>'s words, as the plate's level, in a quiet chip there, or none.
        /// </summary>
        public void Show(LineWords said, bool picked = false, bool needsYou = false, string? chip = null, bool plate = false)
        {
            IsPlate = plate;
            gameObject.SetActive(true);
            RoomType.Size(words);
            var runs = GlazeRuns.Of(words);
            runs.Set(said);
            runs.SetProseMode(true);
            words.textWrappingMode = TextWrappingModes.NoWrap;
            words.rectTransform.sizeDelta = new Vector2(RoomType.Units(2000f), RoomType.Scaled(RoomGeometry.LineDp));
            words.ForceMeshUpdate(true);
            var wide = RoomCursor.WordsWidth(words, 0f);
            var line = RoomType.Scaled(RoomGeometry.LineDp);
            var chipSize = Vector2.zero;
            NeedsYou = needsYou;
            var chipped = needsYou || !string.IsNullOrWhiteSpace(chip);
            this.chip.gameObject.SetActive(chipped);
            if (chipped)
            {
                chipWords.text = needsYou ? StateLanguage.WordOf(WorkState.WaitingForYou) : chip!;
                chipWords.color = needsYou ? Hex(ChipWordsColour) : Color.white;
                chipWords.fontSize = GlazeTokens.FontSize(RoomType.Scaled(ChipTypeDp));
                chipWords.lineSpacing = KitType.LineSpacingFor(chipWords, ChipLineDp / ChipTypeDp);
                chipWords.textWrappingMode = TextWrappingModes.NoWrap;
                chipWords.rectTransform.sizeDelta = new Vector2(RoomType.Units(2000f), RoomType.Scaled(ChipLineDp));
                chipWords.ForceMeshUpdate(true);
                var chipWide = RoomCursor.WordsWidth(chipWords, 0f) + (needsYou ? 0f : RoomType.Scaled(RoomButton.AsideMarkDp + RoomButton.AsideMarkGapDp));
                chipSize = new Vector2(chipWide + 2f * RoomType.Scaled(ChipPadXDp), RoomType.Scaled(ChipLineDp + 2f * ChipPadYDp));
                this.chip.Draw(chipSize, RoomType.Scaled(ChipCornerDp), needsYou ? WithAlpha(Hex(ChipColour), ChipAlpha) : WithAlpha(Hex(QuietChipColour), QuietChipAlpha),
                    needsYou ? WithAlpha(Hex(ChipEdgeColour), ChipEdgeAlpha) : new Color(1f, 1f, 1f, QuietEdgeAlpha), RoomType.Scaled(ChipEdgeDp));
            }
            var glyphRoom = plate ? RoomType.Scaled(AgentGlyphDp + AgentGlyphGapDp) : 0f;
            Size = new Vector2(glyphRoom + wide + 2f * RoomType.Scaled(PadXDp), line + 2f * RoomType.Scaled(PadYDp));
            this.plate.InnerBar = null;
            this.plate.Ring = picked && !plate ? (Hex(RingColour), RingDp) : ((Color, float)?)null;
            this.plate.Draw(Size);
            // The agent's plate is no tag: its own quiet ground in the tag's place, the tag's glass hidden.
            this.plate.gameObject.SetActive(!plate);
            ground.gameObject.SetActive(plate);
            if (plate) ground.Draw(Size, RoomType.Scaled(14f), WithAlpha(Hex(PlateGround), PlateAlpha));
            // The words' box starts at their left; a line's letters stand in the middle of its height.
            var left = -Size.x / 2f + RoomType.Scaled(PadXDp);
            if (plate)
            {
                agent.Show(RoomGlyph.Agent, AgentGlyphDp * GlazeText.Scale, new Color(1f, 1f, 1f, 0.80f));
                agent.transform.localPosition = new Vector3(left + RoomType.Scaled(AgentGlyphDp) / 2f, 0f, -RoomType.Units(0.5f));
                left += glyphRoom;
            }
            else agent.Hide();
            words.transform.localPosition = new Vector3(left, line / 2f, -RoomType.Units(0.5f));
            chipWords.gameObject.SetActive(chipped);
            if (chipped)
            {
                var middle = Size.x / 2f + RoomType.Scaled(ChipGapDp) + chipSize.x / 2f;
                this.chip.transform.localPosition = new Vector3(middle, 0f, -RoomType.Units(0.5f));
                var lead = needsYou ? 0f : RoomType.Scaled(RoomButton.AsideMarkDp + RoomButton.AsideMarkGapDp);
                chipWords.transform.localPosition = new Vector3(middle - chipSize.x / 2f + RoomType.Scaled(ChipPadXDp) + lead, RoomType.Scaled(ChipLineDp) / 2f, -RoomType.Units(1f));
                if (!needsYou)
                {
                    var markAt = middle - chipSize.x / 2f + RoomType.Scaled(ChipPadXDp) + RoomType.Scaled(RoomButton.AsideMarkDp) / 2f;
                    mark.Draw(new Vector2(RoomType.Scaled(RoomButton.AsideMarkDp), RoomType.Scaled(RoomButton.AsideMarkDp)), RoomType.Scaled(RoomButton.AsideMarkDp) / 2f, Hex(RoomButton.AsideMarkColour));
                    mark.transform.localPosition = new Vector3(markAt, 0f, -RoomType.Units(1f));
                    check.Show(RoomGlyph.Check, RoomButton.AsideMarkCheckDp * GlazeText.Scale, RoomGlyphs.OnMain);
                    check.transform.localPosition = new Vector3(markAt, 0f, -RoomType.Units(1.1f));
                }
            }
            mark.gameObject.SetActive(chipped && !needsYou);
            if (!chipped || needsYou) check.Hide();
        }

        private static Color WithAlpha(Color colour, float alpha) => new Color(colour.r, colour.g, colour.b, alpha);

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        public void Hide() => gameObject.SetActive(false);
    }
}

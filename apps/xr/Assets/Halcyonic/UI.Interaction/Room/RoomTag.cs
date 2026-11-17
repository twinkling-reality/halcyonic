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
    /// carries the 1.5 dp ring #E4E8EA. A task's state, or the plate's level, stands under the name in the one language's
    /// label role (FIELD's sheet, tile f1), never in a chip and never a suffix after a dot (the addendum's A17). Its origin is
    /// its plate's middle.
    /// </summary>
    public sealed class RoomTag : MonoBehaviour
    {
        public const float PadXDp = 14f;
        public const float PadYDp = 6f;
        public const float RingDp = 1.5f;
        public const int RingColour = 0xE4E8EA;


        private RoomSurface plate = null!;
        private TextMeshPro words = null!;
        private TextMeshPro stateWords = null!;

        /// <summary>
        /// The task's state under its name in the label role (the one language, FIELD's sheet tile f1), as last shown; null where the
        /// tag says none. On the agent's plate it is the plate's level.
        /// </summary>
        public TextMeshPro? StateWords => stateWords.gameObject.activeSelf ? stateWords : null;

        /// <summary>The gap between the name and the state under it.</summary>
        public const float StateGapDp = 2f;
        private RoomGlyphView agent = null!;
        private RoomShape ground = null!;
        private RoomShape stem = null!;
        private RoomShape diamond = null!;

        /// <summary>The agent's plate's own quiet ground, where the tag's glass is hidden.</summary>
        public RoomShape Ground => ground;

        /// <summary>
        /// The agent's plate while it works (the addendum's A17): the agent's own glyph, 18 dp, 8 dp before its name, on the kit's
        /// one glass (<see cref="AskGlass"/>'s face, no ring, no edge), so it never reads as a task's tag or a press. Its first 62
        /// percent reached 3.59 to 1 over the bright room at the standard size (SCREENS's 36-work, 2026-11-16), 78 about 5.4 to 1;
        /// the glass's face holds white higher still there.
        /// </summary>
        public const float AgentGlyphDp = 18f;
        public const float AgentGlyphGapDp = 8f;
        public const int PlateGround = AskGlass.FaceColour;
        public const float PlateAlpha = AskGlass.FaceAlpha;

        /// <summary>
        /// The widest a name tag's words stand (the lead's 08:58Z, JUDGE's live walk: a long task's tag stood 27 degrees wide
        /// and met the bar's halo): past it the name breaks at a word onto more lines, laid whole, never cut (AUDIT and
        /// AUDIT2, 08:59Z: never an ellipsis on a task's name). Two lines hold a long title; a very long one takes a third.
        /// </summary>
        public const float NameWordsMostDp = 260f;

        /// <summary>
        /// The one language's world line (Death Stranding, the lead's 2026-11-17 00:54Z): from a tag's foot a thin line down to a small
        /// diamond where its thing stands, white at 55 percent, 1 dp wide; the diamond 9 dp, its edge 1.5 dp, white.
        /// </summary>
        public const float StemWidthDp = 1f;
        public const float StemAlpha = 0.55f;
        public const float DiamondDp = 9f;
        public const float DiamondEdgeDp = 1.5f;

        /// <summary>The line under the tag while it shows one (<see cref="Show"/>'s stemDp), and its diamond.</summary>
        public RoomShape? Stem => stem.gameObject.activeSelf ? stem : null;
        public RoomShape? Diamond => diamond.gameObject.activeSelf ? diamond : null;

        /// <summary>The lines the words took as last shown.</summary>
        public int Lines { get; private set; } = 1;

        /// <summary>Whether the tag is the agent's plate: its glyph before the name, its quiet finish.</summary>
        public bool IsPlate { get; private set; }

        public RoomGlyphView AgentGlyph => agent;

        /// <summary>How wide and tall the tag's plate stands as last shown, the line under the name included.</summary>
        public Vector2 Size { get; private set; }

        public RoomSurface Plate => plate;

        public TextMeshPro Words => words;

        /// <summary>Whether the tag's task needs the person, as last shown; its state under the name says so.</summary>
        public bool NeedsYou { get; private set; }

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
            tag.stateWords = RoomType.Create(go.transform, "State", Color.white, KitType.Order.Words);
            tag.stateWords.gameObject.SetActive(false);
            tag.agent = RoomGlyphView.Create(go.transform, "Agent", KitType.Order.Words);
            tag.agent.Hide();
            tag.ground = RoomShape.Create(go.transform, "Plate ground", order);
            tag.ground.gameObject.SetActive(false);
            tag.stem = RoomShape.Create(go.transform, "Stem", order);
            tag.diamond = RoomShape.Create(go.transform, "Diamond", KitType.Order.Words);
            tag.stem.gameObject.SetActive(false);
            tag.diamond.gameObject.SetActive(false);
            RoomBand.Underlay(tag.words);
            go.SetActive(false);
            return tag;
        }

        /// <summary>
        /// Shows <paramref name="said"/> on one line at the person's text size, the tag as wide as the words need, with its
        /// ring where <paramref name="picked"/>, and under the name, in the label role, <paramref name="state"/> (a task's) or
        /// <paramref name="level"/> (the agent's plate's), or nothing. With <paramref name="stemDp"/>, the world line hangs that far
        /// under the tag's foot to a diamond where its thing stands.
        /// </summary>
        /// <param name="state">The task's state word under the name in the label role (StateLanguage's word).</param>
        /// <param name="level">The plate's level (the agent's ask setting) under its name in the label role; only on the plate.</param>
        public void Show(LineWords said, bool picked = false, bool needsYou = false, string? level = null, bool plate = false, float? mostWordsDp = null, float stemDp = 0f,
            string? state = null)
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
            Lines = 1;
            if (mostWordsDp is float most && wide > RoomType.Scaled(most))
            {
                // Bounded: broken at a word onto as many lines as it needs, whole (no ellipsis: the overflow never cuts).
                words.textWrappingMode = TextWrappingModes.Normal;
                words.overflowMode = TextOverflowModes.Overflow;
                var (lines, laidWide, tall) = KitType.Lay(words, RoomType.Scaled(most), 12);
                Lines = lines;
                wide = laidWide;
                line = Mathf.Max(line, tall);
            }
            NeedsYou = needsYou;
            var under = plate ? level : state;
            var stated = !string.IsNullOrWhiteSpace(under);
            stateWords.gameObject.SetActive(stated);
            var stateWide = 0f;
            var stateLine = 0f;
            if (stated)
            {
                RoomType.Label(stateWords, under!);
                stateWords.textWrappingMode = TextWrappingModes.NoWrap;
                stateWords.rectTransform.sizeDelta = new Vector2(RoomType.Units(2000f), RoomType.Scaled(RoomGeometry.LineDp));
                stateWords.ForceMeshUpdate(true, true);
                stateWide = RoomCursor.WordsWidth(stateWords, 0f);
                stateLine = KitType.LinePitch(stateWords) + RoomType.Scaled(StateGapDp);
            }
            var glyphRoom = plate ? RoomType.Scaled(AgentGlyphDp + AgentGlyphGapDp) : 0f;
            Size = new Vector2(glyphRoom + Mathf.Max(wide, stateWide) + 2f * RoomType.Scaled(PadXDp), line + stateLine + 2f * RoomType.Scaled(PadYDp));
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
                // The glyph on the name's own line, where a level stands under it.
                agent.transform.localPosition = new Vector3(left + RoomType.Scaled(AgentGlyphDp) / 2f, stated ? Size.y / 2f - RoomType.Scaled(PadYDp) - line / 2f : 0f, -RoomType.Units(0.5f));
                left += glyphRoom;
            }
            else agent.Hide();
            if (stated)
            {
                // The name over its state, each centred on the tag (FIELD's sheet tile f1); on the plate, the name over its level, both
                // after the agent's glyph.
                var top = Size.y / 2f - RoomType.Scaled(PadYDp);
                words.transform.localPosition = new Vector3(plate ? left : -wide / 2f, top, -RoomType.Units(0.5f));
                stateWords.transform.localPosition = new Vector3(plate ? left : -stateWide / 2f, top - line - RoomType.Scaled(StateGapDp), -RoomType.Units(0.5f));
            }
            else words.transform.localPosition = new Vector3(left, line / 2f, -RoomType.Units(0.5f));
            var stemmed = stemDp > 0f;
            stem.gameObject.SetActive(stemmed);
            diamond.gameObject.SetActive(stemmed);
            if (stemmed)
            {
                var stemLong = RoomType.Scaled(stemDp);
                stem.Draw(new Vector2(RoomType.Scaled(StemWidthDp), stemLong), 0f, new Color(1f, 1f, 1f, StemAlpha));
                stem.transform.localPosition = new Vector3(0f, -Size.y / 2f - stemLong / 2f, 0f);
                var side = RoomType.Scaled(DiamondDp);
                diamond.Draw(new Vector2(side, side), RoomType.Scaled(1f), new Color(1f, 1f, 1f, 0f), Color.white, RoomType.Scaled(DiamondEdgeDp));
                diamond.transform.localPosition = new Vector3(0f, -Size.y / 2f - stemLong, -RoomType.Units(0.5f));
                diamond.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            }
            RoomInk.Follow(transform);
        }

        private static Color WithAlpha(Color colour, float alpha) => new Color(colour.r, colour.g, colour.b, alpha);

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        public void Hide() => gameObject.SetActive(false);
    }
}

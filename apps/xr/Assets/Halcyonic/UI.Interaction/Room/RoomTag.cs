#nullable enable
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// A name tag (DESIGN 1.2): one line of words on the panel's own material, corner 14 dp, 14 dp of room beside the
    /// words and 6 dp above and below, as wide as its words. Under a character it names the task; where the row stood
    /// while the agent works it names the agent and its level (the addendum's A1), and is never pressed. Picked, it
    /// carries the 1.5 dp ring #E4E8EA. Its origin is its middle.
    /// </summary>
    public sealed class RoomTag : MonoBehaviour
    {
        public const float PadXDp = 14f;
        public const float PadYDp = 6f;
        public const float RingDp = 1.5f;
        public const int RingColour = 0xE4E8EA;

        /// <summary>
        /// Where the task needs the person: a dot before the name, 8 dp, in the cursor's colour (the agent's own, #59CFF1),
        /// 8 dp from the words, and the state's own word after the name (<see cref="StateLanguage.WordOf"/>, "Needs You").
        /// </summary>
        public const float DotDp = 8f;
        public const float DotGapDp = 8f;
        public const int DotColour = 0x59CFF1;

        private RoomSurface plate = null!;
        private TextMeshPro words = null!;
        private Surface dot = null!;

        /// <summary>How wide and tall the tag stands as last shown.</summary>
        public Vector2 Size { get; private set; }

        public RoomSurface Plate => plate;

        public TextMeshPro Words => words;

        /// <summary>Whether the tag says its task needs the person: the dot before the name and the word after it.</summary>
        public bool NeedsYou => dot.gameObject.activeSelf;

        public Surface Dot => dot;

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
            tag.dot = Surface.Create(go.transform, "Needs you", KitType.Order.Words);
            tag.dot.gameObject.SetActive(false);
            RoomBand.Underlay(tag.words);
            go.SetActive(false);
            return tag;
        }

        /// <summary>
        /// Shows <paramref name="said"/> on one line at the person's text size, the tag as wide as the words need, with its
        /// ring where <paramref name="picked"/>, and where <paramref name="needsYou"/> the dot before the name and "Needs You"
        /// after it.
        /// </summary>
        public void Show(LineWords said, bool picked = false, bool needsYou = false)
        {
            gameObject.SetActive(true);
            RoomType.Size(words);
            var runs = GlazeRuns.Of(words);
            runs.Set(needsYou ? said + LineWords.Ours(KitMomentWords.Dot + StateLanguage.WordOf(WorkState.WaitingForYou)) : said);
            runs.SetProseMode(true);
            words.textWrappingMode = TextWrappingModes.NoWrap;
            words.rectTransform.sizeDelta = new Vector2(RoomType.Units(2000f), RoomType.Scaled(RoomGeometry.LineDp));
            words.ForceMeshUpdate(true);
            var wide = RoomCursor.WordsWidth(words, 0f);
            var line = RoomType.Scaled(RoomGeometry.LineDp);
            var lead = needsYou ? RoomType.Scaled(DotDp + DotGapDp) : 0f;
            Size = new Vector2(lead + wide + 2f * RoomType.Scaled(PadXDp), line + 2f * RoomType.Scaled(PadYDp));
            plate.InnerBar = null;
            plate.Ring = picked
                ? (new Color(((RingColour >> 16) & 0xFF) / 255f, ((RingColour >> 8) & 0xFF) / 255f, (RingColour & 0xFF) / 255f, 1f), RingDp)
                : ((Color, float)?)null;
            plate.Draw(Size);
            // The words' box starts at their left; a line's letters stand in the middle of its height.
            words.transform.localPosition = new Vector3(-wide / 2f + lead / 2f, line / 2f, -RoomType.Units(0.5f));
            dot.gameObject.SetActive(needsYou);
            if (needsYou)
            {
                var across = RoomType.Scaled(DotDp);
                dot.Draw(new Vector2(across, across), across / 2f,
                    new Color(((DotColour >> 16) & 0xFF) / 255f, ((DotColour >> 8) & 0xFF) / 255f, (DotColour & 0xFF) / 255f, 1f));
                dot.transform.localPosition = new Vector3(-Size.x / 2f + RoomType.Scaled(PadXDp) + across / 2f, 0f, -RoomType.Units(0.5f));
            }
        }

        public void Hide() => gameObject.SetActive(false);
    }
}

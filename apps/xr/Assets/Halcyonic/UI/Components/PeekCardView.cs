#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The peek (ADR 0023): a small card for one character while the person looks or points at it. Its
    /// state badge and marks, the reason in a sentence or two, what its marks mean, and what opening it
    /// is for. Built in units of the distance from the eyes, its top edge's middle on its origin; it
    /// reads seen along its parent's forward axis.
    /// </summary>
    public sealed class PeekCardView : MonoBehaviour
    {
        public const float WidthDegrees = 15f;
        private const float SideDegrees = 1f;
        private const float EndDegrees = 0.8f;
        private const float GapDegrees = 0.45f;
        private const float TagGapDegrees = 0.35f;
        private const float RadiusDegrees = 0.9f;
        private const float Opacity = 0.97f;

        private readonly List<MarkTag> tags = new List<MarkTag>();
        private Surface card = null!;
        private StateBadgeView badge = null!;
        private TextMeshPro reason = null!;
        private TextMeshPro markLine = null!;
        private TextMeshPro next = null!;
        private PeekCard? shown;
        private int shownScale = -1;
        private float opacity = 1f;

        public static float Width => GlazeTokens.Units(WidthDegrees);

        /// <summary>The card's height, in its parent's units.</summary>
        public float Height { get; private set; }

        public StateBadgeView Badge => badge;

        public TextMeshPro Reason => reason;

        public Surface Card => card;

        public static PeekCardView Create(Transform parent)
        {
            var go = new GameObject("Peek card");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<PeekCardView>();
            view.card = Surface.Create(go.transform, "Card", 0);
            view.badge = StateBadgeView.Create(go.transform, "Badge", 1);
            view.reason = GlazeText.Create(go.transform, "Reason", GlazeType.Body, GlazeTokens.Text, TextAlignmentOptions.TopLeft, 2, scaled: true);
            view.markLine = GlazeText.Create(go.transform, "Mark line", GlazeType.Caption, GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Simulated).Foreground),
                TextAlignmentOptions.TopLeft, 2, scaled: true);
            view.next = GlazeText.Create(go.transform, "Next", GlazeType.Caption, GlazeTokens.TextSecondary, TextAlignmentOptions.TopLeft, 2, scaled: true);
            return view;
        }

        public void Show(PeekCard peek)
        {
            if (shown != null && shown.Equals(peek) && shownScale == GlazeText.Version) return;
            shown = peek;
            shownScale = GlazeText.Version;
            var width = Width;
            var side = GlazeTokens.Units(SideDegrees);
            var gap = GlazeTokens.Units(GapDegrees);
            var left = -width / 2f + side;
            var content = width - 2f * side;
            var y = -GlazeTokens.Units(EndDegrees);

            // The badge and the marks, left to right; marks that do not fit beside the badge go under it.
            badge.Show(peek.Badge);
            badge.transform.localPosition = new Vector3(left + badge.Width / 2f, y - StateBadgeView.Height / 2f, -0.001f);
            while (tags.Count < peek.Marks.Count) tags.Add(MarkTag.Create(transform, "Mark " + tags.Count, 1));
            var tagGap = GlazeTokens.Units(TagGapDegrees);
            var x = left + badge.Width + tagGap;
            var rowCenter = y - StateBadgeView.Height / 2f;
            var rowBottom = y - StateBadgeView.Height;
            for (var index = 0; index < tags.Count; index++)
            {
                var shows = index < peek.Marks.Count;
                tags[index].gameObject.SetActive(shows);
                if (!shows) continue;
                var tag = tags[index];
                tag.Show(peek.Marks[index]);
                if (x + tag.Width > left + content)
                {
                    x = left;
                    rowCenter = rowBottom - tagGap - MarkTag.Height / 2f;
                    rowBottom = rowCenter - MarkTag.Height / 2f;
                }
                tag.transform.localPosition = new Vector3(x + tag.Width / 2f, rowCenter, -0.001f);
                x += tag.Width + tagGap;
            }
            y = rowBottom;

            y = Line(reason, peek.ReasonLine, PeekCard.ReasonLines, left, content, y, gap);
            y = Line(markLine, peek.MarkLine ?? "", 2, left, content, y, gap);
            y = Line(next, peek.Next ?? "", 1, left, content, y, gap);
            Height = -y + GlazeTokens.Units(EndDegrees);
            card.transform.localPosition = new Vector3(0f, -Height / 2f, 0f);
            card.Draw(new Vector2(width, Height), GlazeTokens.Units(RadiusDegrees), GlazeTokens.ColorOf(Glaze.Panel, Opacity),
                new Color(1f, 1f, 1f, 0.08f), GlazeTokens.Units(0.06f));
            Fade(opacity);
        }

        /// <summary>The card as visible as <paramref name="value"/>, from 0 to 1, as it fades in and out.</summary>
        public void Fade(float value)
        {
            opacity = value;
            card.Fade(value);
            badge.Fade(value);
            foreach (var tag in tags) tag.Fade(value);
            reason.alpha = value;
            markLine.alpha = value;
            next.alpha = value;
        }

        /// <summary>Lays a line of the card under <paramref name="y"/>, or hides it when it has nothing to say; returns the next line's top.</summary>
        private static float Line(TextMeshPro label, string text, int maxLines, float left, float width, float y, float gap)
        {
            label.gameObject.SetActive(text.Length > 0);
            if (text.Length == 0) return y;
            GlazeText.SetLiteral(label, text);
            var top = y - gap;
            var (lines, _) = GlazeText.Lay(label, width, maxLines);
            label.transform.localPosition = new Vector3(left + width / 2f, top, -0.001f);
            return top - lines * GlazeText.LineHeight(label);
        }
    }
}

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
        private readonly List<TextMeshPro> feet = new List<TextMeshPro>();
        private TextMeshPro probe = null!;

        /// <summary>The gap between the foot's quiet parts, in degrees.</summary>
        private const float FootGapDegrees = 0.5f;
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
            view.probe = GlazeText.Create(go.transform, "Foot measure", GlazeType.Caption, GlazeTokens.TextSecondary, TextAlignmentOptions.TopLeft, 2, scaled: true);
            view.probe.gameObject.SetActive(false);
            return view;
        }

        private void OnEnable() { Glaze.PaletteChanged += RefreshPalette; RefreshPalette(); }
        private void OnDisable() => Glaze.PaletteChanged -= RefreshPalette;
        private void RefreshPalette()
        {
            if (shown == null) return;
            var current = shown;
            shown = null;
            Show(current);
        }

        public void Show(PeekCard peek)
        {
            if (shown != null && shown.Equals(peek) && shownScale == GlazeText.Version) return;
            shown = peek;
            shownScale = GlazeText.Version;
            reason.color = GlazeTokens.Text;
            markLine.color = GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Simulated).Foreground);
            next.color = GlazeTokens.TextSecondary;
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
            // The foot row: what opening is for at its left, and what waits behind the reason apart at its right, each a quiet
            // part of its own, right to left (AUDIT's 11:31Z: never joined to the reason on a dot).
            var footTop = y - gap;
            y = Line(next, peek.Next ?? "", 1, left, content, y, gap);
            var parts = new List<string>();
            if (peek.LastKnownLine is string lastKnown) parts.Add(lastKnown);
            if (peek.MoreLine is string more) parts.Add(more);
            // Beside what opening is for where both fit with a gap between; else on a row of their own under it, never over it
            // (AUDIT's 11:42Z: "Open to Answer" ran into "1 More Waiting" at the larger size).
            var nextWide = next.gameObject.activeSelf ? Mathf.Min(content, next.textBounds.size.x) : 0f;
            var partsWide = 0f;
            foreach (var each in parts)
            {
                GlazeText.SetLiteral(probe, each);
                GlazeText.Lay(probe, content, 1);
                partsWide += Mathf.Min(content, probe.textBounds.size.x) + GlazeTokens.Units(FootGapDegrees);
            }
            probe.gameObject.SetActive(false);
            if (parts.Count > 0 && next.gameObject.activeSelf && nextWide + partsWide > content) footTop = y - gap;
            var right = left + content;
            var footBottom = y;
            for (var index = 0; index < feet.Count || index < parts.Count; index++)
            {
                while (feet.Count <= index)
                    feet.Add(GlazeText.Create(transform, "Foot " + feet.Count, GlazeType.Caption, GlazeTokens.TextSecondary, TextAlignmentOptions.TopRight, 2, scaled: true));
                var foot = feet[index];
                var part = parts.Count - 1 - index;
                foot.gameObject.SetActive(part >= 0);
                if (part < 0) continue;
                foot.color = GlazeTokens.TextSecondary;
                GlazeText.SetLiteral(foot, parts[part]);
                GlazeText.Lay(foot, content, 1);
                var wide = Mathf.Min(content, foot.textBounds.size.x);
                foot.transform.localPosition = new Vector3(right - content / 2f, footTop, -0.001f);
                right -= wide + GlazeTokens.Units(FootGapDegrees);
                footBottom = Mathf.Min(footBottom, footTop - GlazeText.LineHeight(foot));
            }
            y = footBottom;
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
            foreach (var foot in feet) foot.alpha = value;
        }

        /// <summary>How many lines past its own a peek's line may take so its words stand whole.</summary>
        private const int MoreLines = 3;

        /// <summary>Lays a line of the card under <paramref name="y"/>, or hides it when it has nothing to say; returns the next line's top.</summary>
        private static float Line(TextMeshPro label, string text, int maxLines, float left, float width, float y, float gap)
        {
            label.gameObject.SetActive(text.Length > 0);
            if (text.Length == 0) return y;
            GlazeText.SetLiteral(label, text);
            var top = y - gap;
            // Whole, never cut (SCREENS's 11:30Z crop: "+1 More ..." stood cut to "make migr..." at the larger size): the line takes
            // the lines its words need, up to a few more than its own; and no word alone on its last line, the measure narrowing a
            // twentieth at a time, the same lines kept, as the card and the band do. The words are never changed.
            var (lines, _) = GlazeText.Lay(label, width, maxLines);
            for (var more = maxLines + 1; (label.isTextOverflowing || label.isTextTruncated) && more <= maxLines + MoreLines; more++)
                (lines, _) = GlazeText.Lay(label, width, more);
            var laidLines = Mathf.Max(lines, maxLines);
            if (lines >= 2 && CharacterLabelView.LastLineIsOneWord(label) && text.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries).Length >= 4)
            {
                var mended = false;
                for (var step = 1; step <= 8 && !mended; step++)
                {
                    var tried = GlazeText.Lay(label, width * (1f - 0.05f * step), laidLines);
                    if (tried.Lines != lines || label.isTextTruncated) break;
                    mended = !CharacterLabelView.LastLineIsOneWord(label);
                }
                // Where no narrower measure keeps the lines, one more line rather than a word alone: the widest measure that
                // breaks the words onto it with two words or more on its last.
                for (var step = 1; step <= 8 && !mended; step++)
                {
                    var tried = GlazeText.Lay(label, width * (1f - 0.05f * step), laidLines + 1);
                    if (tried.Lines == lines + 1 && !label.isTextTruncated) mended = !CharacterLabelView.LastLineIsOneWord(label);
                }
                if (!mended) (lines, _) = GlazeText.Lay(label, width, laidLines);
                else lines = label.textInfo.lineCount;
            }
            // Its left edge on the card's text edge whatever measure it took: a narrowed line narrows from its right only (AUDIT's
            // 11:42Z: the demo line stood indented).
            label.transform.localPosition = new Vector3(left + label.rectTransform.sizeDelta.x / 2f, top, -0.001f);
            return top - lines * GlazeText.LineHeight(label);
        }
    }
}

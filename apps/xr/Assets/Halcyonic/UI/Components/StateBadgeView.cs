#nullable enable
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// A state badge (ADR 0023): the state's icon and word in a pill of the state's tone, solid, soft
    /// or outlined, with its edge, none, solid or dashed, so the state reads without its colour and
    /// without reading. Its height is fixed and its width follows the word. Where its icon would make
    /// it wider than its room (<see cref="MaxWidth"/>), as on a crowded stage, it shows its word alone;
    /// the word always shows. Starting and Working turn their icon slowly; Waiting for you breathes,
    /// slowly; a state that is only the last one known is ghosted into dots and stands still.
    /// </summary>
    public sealed class StateBadgeView : MonoBehaviour
    {
        public const float HeightDegrees = 1.75f;
        private const float PaddingDegrees = 0.75f;

        /// <summary>Before the icon: less than the padding after the word, since the icon's em has room of its own round the glyph.</summary>
        private const float IconSideDegrees = 0.45f;

        private const float IconGapDegrees = 0.3f;
        private const float EdgeDegrees = 0.1f;
        private const float StrongEdgeDegrees = 0.12f;
        private const float DashDegrees = 0.5f;
        private const float HalftoneDegrees = 0.3f;

        /// <summary>A soft badge with no edge of its own still has a faint one, so it holds its shape on a plate.</summary>
        private const float FaintEdge = 0.35f;

        private Surface pill = null!;
        private TextMeshPro word = null!;
        private TextMeshPro icon = null!;
        private StateBadge? shown;
        private float? maxWidth;
        private float turning;
        private Vector2 size;
        private Color fill;
        private Color edge;
        private float edgeWidth;
        private float dash;
        private float halftone;
        private float breathing;
        private float opacity = 1f;

        public static float Height => GlazeTokens.Units(HeightDegrees);

        /// <summary>The badge's width, in its parent's units.</summary>
        public float Width => size.x;

        public StateBadge? Shown => shown;

        /// <summary>The word's label, for checks.</summary>
        public TextMeshPro Word => word;

        /// <summary>The icon's label, for checks.</summary>
        public TextMeshPro Icon => icon;

        /// <summary>Whether the icon shows beside the word: false where the badge had no room for it.</summary>
        public bool ShowsIcon => icon.gameObject.activeSelf;

        /// <summary>How much wider the icon makes a badge, in its parent's units: its em and the room round it, less the padding it takes the place of.</summary>
        public static float IconWidth =>
            GlazeTokens.Units(IconSideDegrees) + GlazeTokens.Units(GlazeIcons.BadgeDegrees) + GlazeTokens.Units(IconGapDegrees) - GlazeTokens.Units(PaddingDegrees);

        /// <summary>The widest the badge may be with its icon, in its parent's units; with no room for it, the word shows alone.</summary>
        public float? MaxWidth
        {
            get => maxWidth;
            set
            {
                if (maxWidth == value) return;
                maxWidth = value;
                if (shown == null) return;
                var again = shown;
                shown = null;
                Show(again);
            }
        }

        public static StateBadgeView Create(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var badge = go.AddComponent<StateBadgeView>();
            badge.pill = Surface.Create(go.transform, "Pill", order);
            badge.word = GlazeText.Create(go.transform, "Word", GlazeType.Badge, GlazeTokens.Text, TextAlignmentOptions.Center, order + 1);
            badge.word.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            badge.word.textWrappingMode = TextWrappingModes.NoWrap;
            badge.word.transform.localPosition = new Vector3(0f, 0f, -0.0005f);
            badge.icon = GlazeIcons.Create(go.transform, "Icon", GlazeIcons.BadgeDegrees, GlazeTokens.Text, order + 1);
            return badge;
        }

        public void Show(StateBadge badge)
        {
            if (shown != null && shown.State == badge.State && shown.Text == badge.Text && shown.LastKnown == badge.LastKnown && shown.Icon == badge.Icon) return;
            shown = badge;
            var tone = Glaze.Tone(badge.Tone);
            Color text;
            edge = Color.clear;
            edgeWidth = 0f;
            dash = 0f;
            halftone = 0f;
            if (badge.LastKnown)
            {
                // Ghosted into faint dots, its word quieter: the state as last known, not as it is.
                fill = new Color(1f, 1f, 1f, 0.16f);
                text = GlazeTokens.TextSecondary;
                edge = new Color(1f, 1f, 1f, 0.25f);
                edgeWidth = GlazeTokens.Units(EdgeDegrees);
                halftone = GlazeTokens.Units(HalftoneDegrees);
            }
            else
            {
                switch (badge.Fill)
                {
                    case BadgeFill.Outline:
                        fill = Color.clear;
                        text = GlazeTokens.ColorOf(tone.Foreground);
                        break;
                    case BadgeFill.Solid:
                        fill = GlazeTokens.ColorOf(tone.Strong);
                        text = GlazeTokens.ColorOf(tone.OnStrong);
                        break;
                    default:
                        fill = GlazeTokens.ColorOf(tone.Container);
                        text = GlazeTokens.ColorOf(tone.Foreground);
                        break;
                }
                switch (badge.Edge)
                {
                    case BadgeEdge.Solid:
                        edge = badge.Fill == BadgeFill.Outline ? GlazeTokens.ColorOf(Glaze.Outline) : GlazeTokens.ColorOf(tone.Strong);
                        edgeWidth = GlazeTokens.Units(badge.Fill == BadgeFill.Outline ? EdgeDegrees : StrongEdgeDegrees);
                        break;
                    case BadgeEdge.Dashed:
                        edge = GlazeTokens.ColorOf(tone.Strong);
                        edgeWidth = GlazeTokens.Units(EdgeDegrees);
                        dash = GlazeTokens.Units(DashDegrees);
                        break;
                    default:
                        if (badge.Fill == BadgeFill.Soft)
                        {
                            edge = GlazeTokens.ColorOf(tone.Strong, FaintEdge);
                            edgeWidth = GlazeTokens.Units(EdgeDegrees) * 0.6f;
                        }
                        break;
                }
            }
            word.color = new Color(text.r, text.g, text.b, opacity);
            icon.color = word.color;
            GlazeText.SetLiteral(word, badge.Text);
            var (_, width) = GlazeText.Lay(word, GlazeTokens.Units(30f), 1);
            var padding = GlazeTokens.Units(PaddingDegrees);
            var side = GlazeTokens.Units(IconSideDegrees);
            var em = GlazeTokens.Units(GlazeIcons.BadgeDegrees);
            var gap = GlazeTokens.Units(IconGapDegrees);
            var withIcon = width + 2f * padding + IconWidth;
            var iconShows = maxWidth == null || withIcon <= maxWidth.Value + 1e-5f;
            icon.gameObject.SetActive(iconShows);
            if (iconShows)
            {
                GlazeIcons.Show(icon, badge.Icon);
                size = new Vector2(withIcon, Height);
                icon.transform.localPosition = new Vector3(-size.x / 2f + side + em / 2f, 0f, -0.0005f);
                word.transform.localPosition = new Vector3(-size.x / 2f + side + em + gap + width / 2f, 0f, -0.0005f);
            }
            else
            {
                size = new Vector2(width + 2f * padding, Height);
                word.transform.localPosition = new Vector3(0f, 0f, -0.0005f);
            }
            icon.transform.localRotation = Quaternion.identity;
            turning = 0f;
            breathing = 0f;
            Draw(0f);
        }

        private void Update()
        {
            if (shown == null) return;
            if (shown.Turns && icon.gameObject.activeSelf)
            {
                // Clockwise, one turn in the busy turn's time.
                turning = (turning + Time.deltaTime / Glaze.BusyTurnSeconds) % 1f;
                icon.transform.localRotation = Quaternion.Euler(0f, 0f, -360f * turning);
            }
            if (!shown.Breathes) return;
            breathing += Time.deltaTime;
            Draw(Mathf.Sin(breathing * 2f * Mathf.PI / Glaze.AttentionBreathSeconds) * 0.5f + 0.5f);
        }

        /// <summary>The badge as visible as <paramref name="value"/>, from 0 to 1, as when the peek it is on fades.</summary>
        public void Fade(float value)
        {
            opacity = value;
            word.alpha = value;
            icon.alpha = value;
            pill.Fade(value);
        }

        /// <summary>Draws the pill, brighter by <paramref name="breath"/>, from 0 to 1, of the breath's depth.</summary>
        private void Draw(float breath)
        {
            var lift = 1f + Glaze.AttentionBreathDepth * breath;
            var breathed = new Color(Mathf.Min(1f, fill.r * lift), Mathf.Min(1f, fill.g * lift), Mathf.Min(1f, fill.b * lift), fill.a);
            pill.Draw(size, size.y / 2f, breathed, edge, edgeWidth, dash, halftone);
        }
    }
}

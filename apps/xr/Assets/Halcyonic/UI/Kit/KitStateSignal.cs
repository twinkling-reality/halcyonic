#nullable enable
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// A task's state as the spatial kit shows it: the state's icon and its word, the same word everywhere
    /// (<see cref="StateLanguage.WordOf"/>), in the state's tone, so it never rests on colour alone. In a header it
    /// stands on a pill of the state's own fill and edge, as <see cref="StateBadgeView"/> draws it: outlined at
    /// rest, soft while moving, solid only for Waiting for you, edged red where something went wrong, dashed for
    /// the unknown, dotted for a state that is only the last one known. Inline, in a row or the compact presence,
    /// it is the icon and the word alone. Working's icon turns and Waiting for you breathes, unless things keep
    /// still; a last-known state stands still. Centred on its transform.
    /// </summary>
    public sealed class KitStateSignal : MonoBehaviour
    {
        private const float PadDegrees = 0.7f;
        private const float GapDegrees = 0.35f;

        private Surface pill = null!;
        private TextMeshPro icon = null!;
        private TextMeshPro word = null!;
        private StateBadge? shown;
        private bool asPill;
        private Vector2 size;
        private Color fill;
        private Color edge;
        private float edgeWidth;
        private float dash;
        private float halftone;
        private float turning;
        private float breathing;

        public StateBadge? Shown => shown;

        public TextMeshPro Word => word;

        public TextMeshPro Icon => icon;

        /// <summary>Its size, in its parent's units.</summary>
        public Vector2 Size => size;

        public static KitStateSignal Create(Transform parent, string name, int order = KitType.Order.Words)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var signal = go.AddComponent<KitStateSignal>();
            signal.pill = Surface.Create(go.transform, "Pill", order - 1);
            signal.word = KitType.Create(go.transform, "Word", KitRole.Strong, KitType.Text, order, TextAlignmentOptions.Left);
            signal.word.rectTransform.pivot = new Vector2(0f, 0.5f);
            signal.word.textWrappingMode = TextWrappingModes.NoWrap;
            signal.icon = GlazeIcons.Create(go.transform, "Icon", SpatialKit.IconDegrees, KitType.Text, order);
            return signal;
        }

        /// <summary>
        /// Shows <paramref name="badge"/>, on its pill when <paramref name="onPill"/>, and returns its size. The word
        /// says any count beside the state, as the badge's own text does.
        /// </summary>
        public Vector2 Show(StateBadge badge, bool onPill)
        {
            shown = badge;
            asPill = onPill;
            KitType.Size(word, KitRole.Strong);
            var iconEm = KitType.Units(SpatialKit.IconDegrees * GlazeText.Scale);
            icon.fontSize = GlazeTokens.FontSize(iconEm);
            icon.rectTransform.sizeDelta = new Vector2(iconEm, iconEm);
            GlazeIcons.Show(icon, badge.Icon);
            GlazeText.SetLiteral(word, badge.Text);
            var (_, width, _) = KitType.Lay(word, KitType.Units(40f), 1);
            var pad = onPill ? KitType.Scaled(PadDegrees) : 0f;
            var gap = KitType.Scaled(GapDegrees);
            var height = onPill ? KitType.LinePitch(word) + 2f * KitType.Scaled(0.35f) : Mathf.Max(iconEm, KitType.LinePitch(word));
            size = new Vector2(pad + iconEm + gap + width + pad * 1.15f, height);
            icon.transform.localPosition = new Vector3(-size.x / 2f + pad + iconEm / 2f, 0f, -0.0005f);
            word.transform.localPosition = new Vector3(-size.x / 2f + pad + iconEm + gap, 0f, -0.0005f);
            Colours(badge);
            icon.transform.localRotation = Quaternion.identity;
            turning = 0f;
            breathing = 0f;
            Draw(0f);
            return size;
        }

        private void Colours(StateBadge badge)
        {
            var tone = Glaze.Tone(badge.Tone);
            edge = Color.clear;
            edgeWidth = 0f;
            dash = 0f;
            halftone = 0f;
            Color text;
            if (badge.LastKnown)
            {
                fill = asPill ? KitType.Colour(SpatialKit.Raised) : Color.clear;
                text = KitType.Secondary;
                edge = KitType.Colour(SpatialKit.Edge);
                edgeWidth = KitType.Units(0.1f);
                halftone = KitType.Units(0.3f);
            }
            else if (!asPill)
            {
                fill = Color.clear;
                text = KitType.Colour(tone.Foreground);
            }
            else
            {
                switch (badge.Fill)
                {
                    case BadgeFill.Outline:
                        fill = Color.clear;
                        text = KitType.Colour(tone.Foreground);
                        break;
                    case BadgeFill.Solid:
                        fill = KitType.Colour(tone.Strong);
                        text = KitType.Colour(tone.OnStrong);
                        break;
                    default:
                        fill = KitType.Colour(tone.Container);
                        text = KitType.Colour(tone.Foreground);
                        break;
                }
                switch (badge.Edge)
                {
                    case BadgeEdge.Solid:
                        edge = badge.Fill == BadgeFill.Outline ? KitType.Colour(SpatialKit.Edge) : KitType.Colour(tone.Strong);
                        edgeWidth = KitType.Units(0.1f);
                        break;
                    case BadgeEdge.Dashed:
                        edge = KitType.Colour(tone.Strong);
                        edgeWidth = KitType.Units(0.1f);
                        dash = KitType.Units(0.5f);
                        break;
                    default:
                        if (badge.Fill == BadgeFill.Soft)
                        {
                            edge = KitType.Mix(tone.Container, tone.Strong, 0.45f);
                            edgeWidth = KitType.Units(0.07f);
                        }
                        break;
                }
            }
            word.color = text;
            icon.color = text;
        }

        /// <summary>Moves its motion on by <paramref name="seconds"/>: a turn for moving work, a breath for what waits; nothing under Keep things still.</summary>
        public void Advance(float seconds)
        {
            if (shown == null) return;
            if (GlazeMotion.Still || shown.LastKnown)
            {
                if (turning == 0f && breathing == 0f) return;
                turning = 0f;
                breathing = 0f;
                icon.transform.localRotation = Quaternion.identity;
                Draw(0f);
                return;
            }
            if (shown.Turns)
            {
                turning = (turning + seconds / Glaze.BusyTurnSeconds) % 1f;
                icon.transform.localRotation = Quaternion.Euler(0f, 0f, -360f * turning);
            }
            if (!shown.Breathes) return;
            breathing += seconds;
            Draw(Mathf.Sin(breathing * 2f * Mathf.PI / Glaze.AttentionBreathSeconds) * 0.5f + 0.5f);
        }

        private void Update() => Advance(Time.deltaTime);

        private void Draw(float breath)
        {
            var lift = 1f + Glaze.AttentionBreathDepth * breath;
            var breathed = new Color(Mathf.Min(1f, fill.r * lift), Mathf.Min(1f, fill.g * lift), Mathf.Min(1f, fill.b * lift), fill.a);
            pill.gameObject.SetActive(asPill || halftone > 0f);
            pill.Draw(size, size.y / 2f, breathed, edge, edgeWidth, dash, halftone);
        }
    }
}

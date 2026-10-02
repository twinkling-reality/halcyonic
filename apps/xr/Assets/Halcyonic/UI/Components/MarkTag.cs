#nullable enable
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// A mark (ADR 0023): Practice, Demo or Recorded, with its icon, in a small lilac pill with a
    /// dashed edge, near a task's state and never folded into its word, so work that is not real
    /// always says so. Its word is the badge's size, since it sits low under the stage, where text
    /// looks a little smaller.
    /// </summary>
    public sealed class MarkTag : MonoBehaviour
    {
        public const float HeightDegrees = 1.45f;
        private const float PaddingDegrees = 0.55f;
        private const float IconSideDegrees = 0.35f;
        private const float IconGapDegrees = 0.25f;
        private const float EdgeDegrees = 0.1f;
        private const float DashDegrees = 0.45f;

        private Surface pill = null!;
        private TextMeshPro word = null!;
        private TextMeshPro icon = null!;
        private WorkMark? shown;

        public static float Height => GlazeTokens.Units(HeightDegrees);

        public float Width { get; private set; }

        public TextMeshPro Word => word;

        public TextMeshPro Icon => icon;

        public static MarkTag Create(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tag = go.AddComponent<MarkTag>();
            tag.pill = Surface.Create(go.transform, "Pill", order);
            var tone = Glaze.Tone(GlazeTone.Simulated);
            tag.word = GlazeText.Create(go.transform, "Word", GlazeType.Badge, GlazeTokens.ColorOf(tone.Foreground), TextAlignmentOptions.Center, order + 1);
            tag.word.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            tag.word.textWrappingMode = TextWrappingModes.NoWrap;
            tag.word.transform.localPosition = new Vector3(0f, 0f, -0.0005f);
            tag.icon = GlazeIcons.Create(go.transform, "Icon", GlazeIcons.BadgeDegrees, GlazeTokens.ColorOf(tone.Foreground), order + 1);
            return tag;
        }

        /// <summary>The tag as visible as <paramref name="opacity"/>, from 0 to 1.</summary>
        public void Fade(float opacity)
        {
            word.alpha = opacity;
            icon.alpha = opacity;
            pill.Fade(opacity);
        }

        public void Show(WorkMark mark)
        {
            if (shown != null && shown.Word == mark.Word && shown.Icon == mark.Icon) return;
            shown = mark;
            GlazeText.SetLiteral(word, mark.Word);
            GlazeIcons.Show(icon, mark.Icon);
            var (_, width) = GlazeText.Lay(word, GlazeTokens.Units(20f), 1);
            var side = GlazeTokens.Units(IconSideDegrees);
            var em = GlazeTokens.Units(GlazeIcons.BadgeDegrees);
            var gap = GlazeTokens.Units(IconGapDegrees);
            Width = side + em + gap + width + GlazeTokens.Units(PaddingDegrees);
            icon.transform.localPosition = new Vector3(-Width / 2f + side + em / 2f, 0f, -0.0005f);
            word.transform.localPosition = new Vector3(-Width / 2f + side + em + gap + width / 2f, 0f, -0.0005f);
            var tone = Glaze.Tone(GlazeTone.Simulated);
            pill.Draw(new Vector2(Width, Height), Height / 2f, GlazeTokens.ColorOf(tone.Container), GlazeTokens.ColorOf(tone.Strong),
                GlazeTokens.Units(EdgeDegrees), GlazeTokens.Units(DashDegrees));
        }
    }
}

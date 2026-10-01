#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>The interface's type roles (ADR 0023), each one size, as an angle at the eye, and one weight.</summary>
    public enum GlazeType
    {
        /// <summary>A panel's title (24 dp), strong.</summary>
        Display,

        /// <summary>A task's title on the stage, a question over its answers (20 dp), strong.</summary>
        Title,

        /// <summary>Everything a person reads (18 dp).</summary>
        Body,

        /// <summary>A state badge's word (16 dp), strong.</summary>
        Badge,

        /// <summary>Supporting lines and tags (15 dp), the smallest text anywhere.</summary>
        Caption,
    }

    /// <summary>
    /// Text of the interface: TextMeshPro in Liberation Sans, by type role, from the one rule for text
    /// Halcyonic did not write (<see cref="LabelText"/>). No label interprets markup, every label parses
    /// escapes so a doubled backslash shows as one, and every label that can run out of room ends in an
    /// ellipsis. Strong text is drawn thicker by its material, never by TextMeshPro's bold style: this
    /// font has no bold typeface, so a bold label finds no ellipsis and cuts text short without one.
    /// </summary>
    public static class GlazeText
    {
        /// <summary>How much thicker strong text's glyphs are drawn, in TextMeshPro's face dilation.</summary>
        private const float StrongDilate = 0.22f;

        /// <summary>The room strong text's thicker glyphs need between them, in hundredths of an em.</summary>
        private const float StrongSpacing = 1.5f;

        private static readonly Dictionary<TMP_FontAsset, Material> strongMaterials = new Dictionary<TMP_FontAsset, Material>();

        public static float DegreesOf(GlazeType type) => type switch
        {
            GlazeType.Display => Glaze.DisplayDegrees,
            GlazeType.Title => Glaze.TitleDegrees,
            GlazeType.Body => Glaze.BodyDegrees,
            GlazeType.Badge => Glaze.BadgeDegrees,
            _ => Glaze.CaptionDegrees,
        };

        public static bool IsStrong(GlazeType type) => type == GlazeType.Display || type == GlazeType.Title || type == GlazeType.Badge;

        /// <summary>
        /// A label of a type role, in units of the distance from the eyes, its box's top centre on its
        /// transform. It reads seen along its parent's forward axis.
        /// </summary>
        public static TextMeshPro Create(Transform parent, string name, GlazeType type, Color color, TextAlignmentOptions alignment, int order,
            bool? strong = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshPro>();
            if (text.font == null) text.font = TMP_Settings.defaultFontAsset;
            text.rectTransform.pivot = new Vector2(0.5f, 1f);
            text.richText = false;
            text.parseCtrlCharacters = true;
            text.fontSize = GlazeTokens.FontSize(GlazeTokens.Units(DegreesOf(type)));
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.sortingOrder = order;
            if (strong ?? IsStrong(type))
            {
                text.fontSharedMaterial = StrongMaterial(text.font);
                text.characterSpacing = StrongSpacing;
            }
            var renderer = text.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return text;
        }

        /// <summary>
        /// Shows text on a label literally and completely, by the one rule for text Halcyonic did not
        /// write (<see cref="LabelText"/>): no markup, backslashes as they are, and what would not show
        /// as itself as its code point. Halcyonic's own words go in the same way, so every label does.
        /// </summary>
        public static void SetLiteral(TMP_Text label, string text)
        {
            label.richText = false;
            label.parseCtrlCharacters = true;
            label.text = LabelText.ForTextMeshPro(text);
        }

        /// <summary>The distance from one line to the next, in the label's units.</summary>
        public static float LineHeight(TMP_Text label)
        {
            var face = label.font.faceInfo;
            return label.fontSize * 0.1f * face.lineHeight / face.pointSize;
        }

        /// <summary>
        /// Lays a label out in a box <paramref name="width"/> wide with room for at most
        /// <paramref name="maxLines"/> lines, the last ending in an ellipsis when the text runs over,
        /// and says how many lines it takes and how wide its widest is.
        /// </summary>
        public static (int Lines, float Width) Lay(TMP_Text label, float width, int maxLines)
        {
            label.rectTransform.sizeDelta = new Vector2(width, maxLines * LineHeight(label));
            // Laid out whether it shows yet or not, so a card is measured before it appears.
            label.ForceMeshUpdate(true);
            var info = label.textInfo;
            if (info.characterCount == 0) return (0, 0f);
            return (Mathf.Clamp(info.lineCount, 1, maxLines), Mathf.Min(width, label.textBounds.size.x));
        }

        /// <summary>
        /// Leans a label's letters, as an agent's words lean so they never read as Halcyonic's or as
        /// fact: called from the label's OnPreRenderText. TextMeshPro's own italics would find no
        /// italic face in this font and cut text short without its ellipsis.
        /// </summary>
        public static void Lean(TMP_TextInfo info)
        {
            for (var index = 0; index < info.characterCount; index++)
            {
                var character = info.characterInfo[index];
                if (!character.isVisible) continue;
                // About the middle of a capital, as TextMeshPro shears, so the letter keeps its place.
                var middle = character.baseLine + 0.5f * character.fontAsset.faceInfo.capLine * character.scale;
                var vertices = info.meshInfo[character.materialReferenceIndex].vertices;
                for (var corner = character.vertexIndex; corner < character.vertexIndex + 4; corner++)
                {
                    vertices[corner].x += LeanShear * (vertices[corner].y - middle);
                }
            }
        }

        /// <summary>How far a leaning letter's top stands right of its foot, for each unit of its height.</summary>
        private const float LeanShear = 0.35f;

        /// <summary>
        /// The font's own material with thicker glyphs, for strong text. TextMeshPro keeps a glyph's
        /// room around it from the material, so the thicker glyphs are not clipped.
        /// </summary>
        private static Material StrongMaterial(TMP_FontAsset font)
        {
            if (strongMaterials.TryGetValue(font, out var strong) && strong != null) return strong;
            strong = new Material(font.material) { name = font.material.name + " (strong)" };
            strong.SetFloat(ShaderUtilities.ID_FaceDilate, StrongDilate);
            ShaderUtilities.UpdateShaderRatios(strong);
            strongMaterials[font] = strong;
            return strong;
        }

        /// <summary>Forgets cached objects when play mode starts without a domain reload.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => strongMaterials.Clear();
    }
}

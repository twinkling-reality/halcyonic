#nullable enable
using System;
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

        /// <summary>A column's subject on the menu (24 dp), drawn light, one a column (ADR 0026).</summary>
        Subject,
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

        /// <summary>How much thinner a subject's glyphs are drawn, in TextMeshPro's face dilation (ADR 0026).</summary>
        private const float LightDilate = -0.12f;

        private static readonly Dictionary<TMP_FontAsset, Material> lightMaterials = new Dictionary<TMP_FontAsset, Material>();

        /// <summary>Every label made to follow <see cref="Scale"/>, with its role, so a change reaches the ones that show.</summary>
        private static readonly List<(WeakReference<TMP_Text> Label, GlazeType Type)> made = new List<(WeakReference<TMP_Text>, GlazeType)>();

        /// <summary>
        /// How much larger than designed reading text is drawn (<see cref="Comfort.TextScale"/>). On the
        /// stage, labels made to follow it (<see cref="Create"/>'s scaled) take it, every role but the
        /// badge's, whose words stand where space is fixed; a foreground panel grows whole by it
        /// instead, so its layout stays as designed (<c>PanelFrame.Zoom</c>).
        /// </summary>
        public static float Scale { get; private set; } = 1f;

        /// <summary>Counts each change of <see cref="Scale"/>, so what keeps a measure can tell it no longer holds.</summary>
        public static int Version { get; private set; }

        /// <summary>The scale changed, and every label took it: what lays labels out does so again.</summary>
        public static event Action? ScaleChanged;

        public static float DegreesOf(GlazeType type) => type switch
        {
            GlazeType.Display => Glaze.DisplayDegrees,
            GlazeType.Title => Glaze.TitleDegrees,
            GlazeType.Body => Glaze.BodyDegrees,
            GlazeType.Badge => Glaze.BadgeDegrees,
            GlazeType.Subject => Glaze.Menu.TitleDegrees,
            _ => Glaze.CaptionDegrees,
        };

        /// <summary>A role's size as an angle at the eye for a label that follows <see cref="Scale"/>.</summary>
        public static float ScaledDegreesOf(GlazeType type) => type == GlazeType.Badge ? DegreesOf(type) : DegreesOf(type) * Scale;

        /// <summary>
        /// Draws reading text <paramref name="scale"/> times as large as designed, from now on and on
        /// every label already made to follow it, then raises <see cref="ScaleChanged"/>.
        /// </summary>
        public static void SetScale(float scale)
        {
            if (Mathf.Approximately(scale, Scale)) return;
            Scale = scale;
            Version++;
            Forget();
            foreach (var (reference, type) in made)
            {
                if (reference.TryGetTarget(out var label)) label.fontSize = GlazeTokens.FontSize(GlazeTokens.Units(ScaledDegreesOf(type)));
            }
            ScaleChanged?.Invoke();
        }

        /// <summary>Drops the labels destroyed since they were made from <see cref="made"/>.</summary>
        private static void Forget() => made.RemoveAll(entry => !entry.Label.TryGetTarget(out var label) || label == null);

        public static bool IsStrong(GlazeType type) => type == GlazeType.Display || type == GlazeType.Title || type == GlazeType.Badge;

        /// <summary>
        /// A label of a type role, in units of the distance from the eyes, its box's top centre on its
        /// transform. It reads seen along its parent's forward axis. A <paramref name="scaled"/> label,
        /// on the stage, follows <see cref="Scale"/>.
        /// </summary>
        public static TextMeshPro Create(Transform parent, string name, GlazeType type, Color color, TextAlignmentOptions alignment, int order,
            bool? strong = null, bool scaled = false)
        {
            var text = AddAwake(parent, name);
            if (text.font == null) text.font = TMP_Settings.defaultFontAsset;
            text.rectTransform.pivot = new Vector2(0.5f, 1f);
            text.richText = false;
            text.parseCtrlCharacters = true;
            text.fontSize = GlazeTokens.FontSize(GlazeTokens.Units(scaled ? ScaledDegreesOf(type) : DegreesOf(type)));
            if (scaled)
            {
                // Now and then the labels destroyed since are forgotten, so the list stays as long as the labels there are.
                if (made.Count > 0 && made.Count % 64 == 0) Forget();
                made.Add((new WeakReference<TMP_Text>(text), type));
            }
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.sortingOrder = order;
            if (type == GlazeType.Subject) text.fontSharedMaterial = LightMaterial(text.font);
            else if (strong ?? IsStrong(type)) SetStrong(text, true);
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
        /// A TextMeshPro label made awake, then put under <paramref name="parent"/>: made on an object of its
        /// own first, active, so TextMeshPro's Awake runs at once. Made straight under an inactive parent,
        /// as the stage's characters are before the stage is placed, it would not wake until shown, and
        /// <see cref="Lay"/> would measure it as empty; a badge or a mark sized then keeps a pill too
        /// narrow for its words, as no change of state lays it again.
        /// </summary>
        public static TextMeshPro AddAwake(Transform parent, string name)
        {
            var go = new GameObject(name);
            var text = go.AddComponent<TextMeshPro>();
            go.transform.SetParent(parent, false);
            return text;
        }

        /// <summary>
        /// Lays a label out in a box <paramref name="width"/> wide with room for at most
        /// <paramref name="maxLines"/> lines, the last ending in an ellipsis when the text runs over,
        /// and says how many lines it takes and how wide its widest is. A label whose words, look and
        /// box are as they were keeps the mesh it has: panels lay every label out again every half
        /// second, and building text meshes again is what that would cost on the headset.
        /// </summary>
        public static (int Lines, float Width) Lay(TMP_Text label, float width, int maxLines)
        {
            var box = new Vector2(width, maxLines * LineHeight(label));
            var resized = label.rectTransform.sizeDelta != box;
            if (resized) label.rectTransform.sizeDelta = box;
            // Laid out whether it shows yet or not, so a card is measured before it appears. Every
            // TextMeshPro setter marks a change only when the value changes, so an unchanged label
            // has nothing to build again.
            if (resized || label.havePropertiesChanged) label.ForceMeshUpdate(true);
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
        /// Draws <paramref name="text"/> heavier, by its material, or as it is: on the menu only the
        /// chosen section and the main action are drawn heavier (ADR 0026).
        /// </summary>
        public static void SetStrong(TMP_Text text, bool strong)
        {
            text.fontSharedMaterial = strong ? StrongMaterial(text.font) : text.font.material;
            text.characterSpacing = strong ? StrongSpacing : 0f;
        }

        private static readonly int FaceColorId = Shader.PropertyToID("_FaceColor");

        /// <summary>One block for every label's fade: set, handed to the renderer, and reused, so a fade allocates nothing.</summary>
        private static MaterialPropertyBlock? fadeBlock;

        /// <summary>
        /// Draws <paramref name="text"/> as far into view as <paramref name="shown"/>, from 0 to 1, as the column it
        /// is on opens or closes: by its material's face colour on its own renderer, so whatever colours its
        /// words, a press, a state or a wait's shimmer, still does. Whole, it drops the block again, so it
        /// draws exactly as before.
        /// </summary>
        public static void Show(TMP_Text text, float shown)
        {
            if (!text.TryGetComponent<Renderer>(out var renderer)) return;
            if (shown >= 1f)
            {
                if (renderer.HasPropertyBlock()) renderer.SetPropertyBlock(null);
                return;
            }
            var block = fadeBlock ??= new MaterialPropertyBlock();
            var face = renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty(FaceColorId) ? renderer.sharedMaterial.GetColor(FaceColorId) : Color.white;
            block.Clear();
            block.SetColor(FaceColorId, new Color(face.r, face.g, face.b, face.a * Mathf.Clamp01(shown)));
            renderer.SetPropertyBlock(block);
        }

        /// <summary>How far into view <paramref name="text"/> is drawn (<see cref="Show"/>), for checks: 1 unless a fade holds it back.</summary>
        public static float ShownOf(TMP_Text text)
        {
            if (!text.TryGetComponent<Renderer>(out var renderer) || !renderer.HasPropertyBlock()) return 1f;
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            var face = renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty(FaceColorId) ? renderer.sharedMaterial.GetColor(FaceColorId) : Color.white;
            return face.a <= 0f ? 1f : block.GetColor(FaceColorId).a / face.a;
        }

        /// <summary>The font's own material with thinner glyphs, for a subject drawn light.</summary>
        private static Material LightMaterial(TMP_FontAsset font)
        {
            if (lightMaterials.TryGetValue(font, out var light) && light != null) return light;
            light = new Material(font.material) { name = font.material.name + " (light)" };
            light.SetFloat(ShaderUtilities.ID_FaceDilate, LightDilate);
            ShaderUtilities.UpdateShaderRatios(light);
            lightMaterials[font] = light;
            return light;
        }

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
        private static void Reset()
        {
            strongMaterials.Clear();
            lightMaterials.Clear();
            made.Clear();
            Scale = 1f;
            ScaleChanged = null;
        }
    }
}

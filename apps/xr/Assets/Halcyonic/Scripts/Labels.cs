#nullable enable
using System.Collections.Generic;
using System.Text;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>
    /// World-space text with Unity's built-in font, so labels need no imported assets, and the
    /// plates that keep it legible over any background. Text is measured with the font's own glyph
    /// advances, so it wraps to a width instead of spilling into the next character's labels.
    ///
    /// Titles, attention notes and connection details come from outside, so a label never interprets
    /// markup (TextMesh's rich text is on by default, and would hide text in a transparent color),
    /// and every line goes through the one rule for text Halcyonic did not write
    /// (<see cref="LabelText.Plain"/>). TextMesh parses no backslash escapes, so backslashes stay single.
    /// </summary>
    internal static class Labels
    {
        /// <summary>
        /// The size glyphs are rasterized at. A label's text is about this many pixels tall on a
        /// Meta Quest 3, so glyphs are not shrunk far enough to shimmer.
        /// </summary>
        public const int FontSize = 48;

        /// <summary>TextMesh draws one font pixel as a tenth of a unit at a character size of one.</summary>
        private const float UnitsPerPixel = 0.1f;

        private const string Ellipsis = "…";

        private static Font? font;

        private static Font Font
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        /// <summary>
        /// Creates a label with a character size in TextMesh's own terms, rasterized at 96 pixels.
        /// Kept as it was for existing callers; characters use <see cref="CreateSized"/>.
        /// </summary>
        public static TextMesh Create(Transform parent, string name, Vector3 localPosition, float characterSize)
        {
            var text = CreateSized(parent, name, localPosition, characterSize * 96 * UnitsPerPixel);
            text.fontSize = 96;
            text.characterSize = characterSize;
            return text;
        }

        /// <summary>
        /// Creates a label whose font size, the height of an em, is <paramref name="emHeight"/> in the
        /// parent's units. The label reads correctly when seen along its parent's forward axis.
        /// </summary>
        public static TextMesh CreateSized(
            Transform parent,
            string name,
            Vector3 localPosition,
            float emHeight,
            FontStyle style = FontStyle.Normal,
            TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var label = new GameObject(name);
            label.transform.SetParent(parent, false);
            label.transform.localPosition = localPosition;
            var renderer = label.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Font.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var text = label.AddComponent<TextMesh>();
            text.font = Font;
            text.fontSize = FontSize;
            text.fontStyle = style;
            text.characterSize = emHeight / (FontSize * UnitsPerPixel);
            text.anchor = anchor;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            text.richText = false;
            return text;
        }

        /// <summary>The distance from one line to the next, in the label's parent units.</summary>
        public static float LineHeight(TextMesh label)
        {
            // Measured for the built-in font: TextMesh advances 1.15 font sizes per line.
            return 1.15f * label.fontSize * label.characterSize * UnitsPerPixel * label.lineSpacing;
        }

        /// <summary>The width of one line of text, in the label's parent units.</summary>
        public static float Width(TextMesh label, string line)
        {
            var font = label.font;
            font.RequestCharactersInTexture(line, label.fontSize, label.fontStyle);
            var pixels = 0;
            foreach (var character in line)
            {
                if (font.GetCharacterInfo(character, out var info, label.fontSize, label.fontStyle)) pixels += info.advance;
            }
            return pixels * label.characterSize * UnitsPerPixel;
        }

        /// <summary>The width of the widest line a label shows, in its parent's units.</summary>
        public static float WidestLine(TextMesh label)
        {
            var widest = 0f;
            foreach (var line in label.text.Split('\n')) widest = Mathf.Max(widest, Width(label, line));
            return widest;
        }

        /// <summary>
        /// Breaks text into lines no wider than <paramref name="width"/>, at spaces where it can, and
        /// keeps at most <paramref name="maxLines"/> lines, ending with an ellipsis when some text had
        /// to be left out. Each line of the text, as it separates them, shows by the one rule for text
        /// Halcyonic did not write.
        /// </summary>
        public static string Wrap(TextMesh label, string text, float width, int maxLines, out int lineCount)
        {
            var lines = new List<string>();
            foreach (var paragraph in text.Split('\n'))
            {
                var current = "";
                foreach (var word in LabelText.Plain(paragraph).Split(' '))
                {
                    if (word.Length == 0) continue;
                    var candidate = current.Length == 0 ? word : current + " " + word;
                    if (Width(label, candidate) <= width)
                    {
                        current = candidate;
                        continue;
                    }
                    if (current.Length > 0) lines.Add(current);
                    current = word;
                    // A single word wider than the line breaks where it must.
                    while (current.Length > 1 && Width(label, current) > width)
                    {
                        var fits = Fitting(label, current, width);
                        lines.Add(current.Substring(0, fits));
                        current = current.Substring(fits);
                    }
                }
                lines.Add(current);
            }

            if (lines.Count > maxLines)
            {
                var last = lines[maxLines - 1];
                while (last.Length > 0 && Width(label, last + Ellipsis) > width) last = last.Substring(0, last.Length - 1);
                lines[maxLines - 1] = last.TrimEnd() + Ellipsis;
                lines.RemoveRange(maxLines, lines.Count - maxLines);
            }
            lineCount = lines.Count;
            var wrapped = new StringBuilder();
            for (var i = 0; i < lines.Count; i++)
            {
                if (i > 0) wrapped.Append('\n');
                wrapped.Append(lines[i]);
            }
            return wrapped.ToString();
        }

        /// <summary>A rounded plate behind labels: a unit quad the caller scales and sizes through _Rect.</summary>
        public static MeshRenderer CreatePlate(Transform parent, string name)
        {
            var plate = new GameObject(name);
            plate.transform.SetParent(parent, false);
            plate.AddComponent<MeshFilter>().sharedMesh = CharacterMeshes.Quad();
            var renderer = plate.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CharacterMaterials.Plate;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return renderer;
        }

        /// <summary>How many leading characters of a word fit in the width; at least one.</summary>
        private static int Fitting(TextMesh label, string word, float width)
        {
            var count = 1;
            while (count < word.Length && Width(label, word.Substring(0, count + 1)) <= width) count++;
            return count;
        }
    }
}

#nullable enable
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The interface's icons (ADR 0023): Material Symbols Rounded, filled, weight 500, each a glyph of
    /// the static icon atlas named by what it means (<see cref="GlazeIcon"/>), in a label of its own
    /// beside the words it goes with and never in their text. No label falls back to the atlas, and
    /// text from outside shows every Private Use Area character as its code (<see cref="LabelText"/>),
    /// so only Halcyonic draws an icon.
    /// </summary>
    public static class GlazeIcons
    {
        /// <summary>An icon in a badge or a tag: its em, the 24 dp grid it is drawn on, as an angle at the eye.</summary>
        public const float BadgeDegrees = 1.2f;

        /// <summary>No icon is drawn smaller.</summary>
        public const float MinimumDegrees = 1f;

        /// <summary>The atlas, under a Resources folder (built by Halcyonic > Build the Icon Atlas).</summary>
        public const string ResourcePath = "HalcyonicUI/GlazeIcons";

        private static TMP_FontAsset? font;

        /// <summary>The icon atlas.</summary>
        public static TMP_FontAsset Font
        {
            get
            {
                if (font == null) font = Resources.Load<TMP_FontAsset>(ResourcePath);
                return font;
            }
        }

        /// <summary>Whether a label is an icon, drawn from the icon atlas.</summary>
        public static bool IsIcon(TMP_Text label) => label.font != null && label.font == Font;

        /// <summary>
        /// An icon whose em is <paramref name="degrees"/> across, in units of the distance from the
        /// eyes, its em centred on its transform so it can turn in place. It reads seen along its
        /// parent's forward axis.
        /// </summary>
        public static TextMeshPro Create(Transform parent, string name, float degrees, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var icon = go.AddComponent<TextMeshPro>();
            icon.font = Font;
            icon.richText = false;
            icon.parseCtrlCharacters = false;
            icon.textWrappingMode = TextWrappingModes.NoWrap;
            icon.overflowMode = TextOverflowModes.Overflow;
            // The middle of the em, which the glyphs are drawn round, on the transform.
            icon.alignment = TextAlignmentOptions.Center;
            var em = GlazeTokens.Units(degrees);
            icon.fontSize = GlazeTokens.FontSize(em);
            icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            icon.rectTransform.sizeDelta = new Vector2(em, em);
            icon.color = color;
            icon.sortingOrder = order;
            var renderer = icon.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return icon;
        }

        /// <summary>Shows <paramref name="which"/> on an icon; the same icon again builds nothing.</summary>
        public static void Show(TMP_Text icon, GlazeIcon which) => icon.text = GlazeIconGlyphs.Of(which);
    }
}

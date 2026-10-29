#nullable enable
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>The spatial kit's type roles (<see cref="SpatialKit"/>): two sizes of the instrument face for names, one reading size for everything read, and the room's Label and Mono.</summary>
    public enum KitRole
    {
        /// <summary>The selected work's or the place's name, once an instrument: 30 dp in the instrument face.</summary>
        Identity,

        /// <summary>A region's heading, and a project's name on the overview: 22 dp in the instrument face.</summary>
        Heading,

        /// <summary>Everything read: 18 dp in the reading face.</summary>
        Body,

        /// <summary>A control's words and a chosen row's words: Body, drawn heavier by its material.</summary>
        Strong,

        /// <summary>A fact beside a subject on the room's small surfaces (ADR 0040): 15 dp in the reading face.</summary>
        Label,

        /// <summary>
        /// Code, paths and commands, which are the runtime's: 16 dp. Drawn in the reading face until a monospace
        /// face is chosen, which needs a download the owner has not approved.
        /// </summary>
        Mono,
    }

    /// <summary>
    /// The spatial kit in Unity's terms: its colours as Unity colours, its sizes as lengths in units of the
    /// plane's distance, and its labels on the production fonts through <see cref="GlazeText"/>, so every
    /// kit label keeps the one rule for text Halcyonic did not write: no markup, escapes shown as written,
    /// and outside text leaning unless a component explicitly sets the attributed-prose or identity treatment.
    /// Reading sizes follow the person's text size (<see cref="GlazeText.Scale"/>) and the regions reflow;
    /// nothing is zoomed.
    /// </summary>
    public static class KitType
    {
        public static Color Colour(GlazeColor colour, float alpha = 1f) => GlazeTokens.ColorOf(colour, alpha);

        public static Color Text => Colour(SpatialKit.Text);

        public static Color Secondary => Colour(SpatialKit.TextSecondary);

        /// <summary>A state tone's words and icons on the kit's carriers.</summary>
        public static Color ToneText(GlazeTone tone) => Colour(Glaze.Tone(tone).Foreground);

        /// <summary>
        /// <paramref name="top"/> over <paramref name="under"/> at <paramref name="share"/>, mixed in linear light
        /// as the project blends, returned opaque: an edge drawn in the mix blends the same in any colour space.
        /// </summary>
        public static Color Mix(GlazeColor under, GlazeColor top, float share)
        {
            var mixed = Color.Lerp(Colour(under).linear, Colour(top).linear, Mathf.Clamp01(share)).gamma;
            mixed.a = 1f;
            return mixed;
        }

        /// <summary>A role's em as an angle at the eye, at the person's text size.</summary>
        public static float Degrees(KitRole role) => SpatialKit.Degrees(role switch
        {
            KitRole.Identity => SpatialKit.IdentityDp,
            KitRole.Heading => SpatialKit.HeadingDp,
            KitRole.Label => SpatialKit.LabelDp,
            KitRole.Mono => SpatialKit.MonoDp,
            _ => SpatialKit.BodyDp,
        }) * GlazeText.Scale;

        /// <summary>A role's em in units of the plane's distance.</summary>
        public static float Em(KitRole role) => GlazeTokens.Units(Degrees(role));

        /// <summary>An angle at the eye as a length on the plane, in units of its distance.</summary>
        public static float Units(float degrees) => GlazeTokens.Units(degrees);

        /// <summary>A gap that grows with the reading text, as a length on the plane.</summary>
        public static float Scaled(float degrees) => GlazeTokens.Units(degrees * GlazeText.Scale);

        /// <summary>
        /// A kit label of <paramref name="role"/>, its box's top left on its transform, reading seen along its
        /// parent's forward axis.
        /// </summary>
        public static TextMeshPro Create(Transform parent, string name, KitRole role, Color colour, int order,
            TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft, float leading = SpatialKit.ListLeading)
        {
            var instrument = role == KitRole.Identity || role == KitRole.Heading;
            var label = GlazeText.Create(parent, name, instrument ? GlazeType.Hero : GlazeType.Body, colour, alignment, order, strong: role == KitRole.Strong);
            label.rectTransform.pivot = new Vector2(0f, 1f);
            Size(label, role, leading);
            return label;
        }

        /// <summary>Sizes a label for its role at the person's text size, with a line pitch of <paramref name="leading"/> ems.</summary>
        public static void Size(TMP_Text label, KitRole role, float leading = SpatialKit.ListLeading)
        {
            var size = GlazeTokens.FontSize(Em(role));
            if (!Mathf.Approximately(label.fontSize, size)) label.fontSize = size;
            var spacing = LineSpacingFor(label, leading);
            if (!Mathf.Approximately(label.lineSpacing, spacing)) label.lineSpacing = spacing;
        }

        /// <summary>TextMeshPro's added line spacing, in hundredths of an em, that makes a pitch of <paramref name="leading"/> ems.</summary>
        public static float LineSpacingFor(TMP_Text label, float leading)
        {
            var face = label.font.faceInfo;
            return (leading - face.lineHeight / face.pointSize) * 100f;
        }

        /// <summary>From one line's top to the next line's, in the label's units.</summary>
        public static float LinePitch(TMP_Text label) => GlazeText.LineHeight(label) + label.lineSpacing * label.fontSize * 0.001f;

        /// <summary>
        /// Lays a label out <paramref name="width"/> wide with room for at most <paramref name="maxLines"/> lines, the
        /// last ending in an ellipsis inside outside text if the words run over, and says how many lines it took, how
        /// wide its widest is and how tall those lines stand, from the top of the first to the pitch below the last.
        /// </summary>
        public static (int Lines, float Width, float Height) Lay(TMP_Text label, float width, int maxLines)
        {
            var pitch = LinePitch(label);
            var box = new Vector2(Mathf.Max(width, 1e-4f), maxLines * pitch + GlazeText.LineHeight(label) * 0.25f);
            if (label.rectTransform.sizeDelta != box) label.rectTransform.sizeDelta = box;
            label.ForceMeshUpdate(true);
            var info = label.textInfo;
            if (info.characterCount == 0) return (0, 0f, 0f);
            var lines = Mathf.Clamp(info.lineCount, 1, maxLines);
            return (lines, Mathf.Min(width, label.textBounds.size.x), lines * pitch);
        }

        /// <summary>The kit's order of drawing, low to high, so each layer stands over the one below it on the shared transparent queue.</summary>
        public static class Order
        {
            public const int Carrier = 0;
            public const int Shade = 2;
            public const int Plate = 4;
            public const int Chip = 6;
            public const int Words = 10;
            public const int Control = 12;
            public const int ControlWords = 14;
        }
    }
}

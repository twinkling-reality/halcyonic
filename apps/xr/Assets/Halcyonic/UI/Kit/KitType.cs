#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The spatial kit's type roles (<see cref="SpatialKit"/>). On the room's parts (<see cref="KitType.Create"/> with
    /// <c>room</c>) each takes one of ART's six static faces (2026-10-30) and its own line pitch: Display in Archivo Expanded
    /// Regular; Title in Archivo Expanded SemiBold; Body in Inter Regular, Strong in Inter SemiBold, Label in Inter Medium,
    /// Mono in JetBrains Mono Regular. Identity and Heading are the instrument candidate's only. The instrument candidate (the workspace's spine,
    /// slate and rail, the overview, the presence) keeps the instrument face and Liberation Sans until the room replaces it.
    /// </summary>
    public enum KitRole
    {
        /// <summary>The selected work's or the place's name on the instrument candidate: 30 dp in the instrument face. The room's parts take Title or Display.</summary>
        Identity,

        /// <summary>A region's heading, and a project's name on the overview, on the instrument candidate: 22 dp in the instrument face. The room's parts take Title.</summary>
        Heading,

        /// <summary>Everything read: 18 dp in the reading face.</summary>
        Body,

        /// <summary>A control's words, a chosen row's words and names: Body's size in Inter SemiBold.</summary>
        Strong,

        /// <summary>A fact beside a subject on the room's small surfaces (ADR 0040): 15 dp in Inter Medium.</summary>
        Label,

        /// <summary>A picked project's name across its screen (ADR 0040): 48 dp in Archivo Expanded Regular, once a room.</summary>
        Display,

        /// <summary>Code, paths and commands, which are the runtime's: 16 dp in JetBrains Mono.</summary>
        Mono,

        /// <summary>A moment's and the reader's title, Projects, the lost plate: 28 dp in Archivo Expanded SemiBold.</summary>
        Title,
    }

    /// <summary>
    /// The spatial kit in Unity's terms: its colours as Unity colours, its sizes as lengths in units of the
    /// plane's distance, and its labels on the production fonts through <see cref="GlazeText"/>, so every
    /// kit label keeps the one rule for text Halcyonic did not write: no markup, escapes shown as written, and its
    /// runs kept. In the room's faces outside text stands upright, told by its place and the mono face, never by a slant.
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
            KitRole.Display => SpatialKit.DisplayDp,
            KitRole.Title => SpatialKit.TitleDp,
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
        /// parent's forward axis. A <paramref name="room"/> label, on one of the room's parts (the screen, its bar, a
        /// moment), is drawn in the room's faces and stays in them as it is sized again; any other keeps the instrument
        /// candidate's faces.
        /// </summary>
        public static TextMeshPro Create(Transform parent, string name, KitRole role, Color colour, int order,
            TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft, float leading = SpatialKit.ListLeading, bool room = false)
        {
            var instrument = role == KitRole.Identity || role == KitRole.Heading || role == KitRole.Display || role == KitRole.Title;
            var label = GlazeText.Create(parent, name, instrument ? GlazeType.Hero : GlazeType.Body, colour, alignment, order, strong: role == KitRole.Strong);
            label.rectTransform.pivot = new Vector2(0f, 1f);
            if (room) Dress(label, role);
            Size(label, role, leading);
            return label;
        }

        /// <summary>
        /// Sizes a label for its role at the person's text size, with a line pitch of <paramref name="leading"/> ems; a
        /// room label instead takes the role's face, tracking and own line pitch (<see cref="RoomLeading"/>).
        /// </summary>
        public static void Size(TMP_Text label, KitRole role, float leading = SpatialKit.ListLeading)
        {
            if (IsRoomFace(label.font))
            {
                Dress(label, role);
                leading = RoomLeading(role);
            }
            var size = GlazeTokens.FontSize(Em(role));
            if (!Mathf.Approximately(label.fontSize, size)) label.fontSize = size;
            var spacing = LineSpacingFor(label, leading);
            if (!Mathf.Approximately(label.lineSpacing, spacing)) label.lineSpacing = spacing;
        }

        /// <summary>Puts a room label in its role's face and tracking, where the faces are built.</summary>
        private static void Dress(TMP_Text label, KitRole role)
        {
            var face = Face(role);
            if (face == null || label.font == face) return;
            label.font = face;
            label.fontSharedMaterial = face.material;
            label.characterSpacing = Tracking(role);
        }

        /// <summary>The asset each role is drawn in on the room's parts (<c>KitFontAtlas</c> builds them).</summary>
        public static string FaceName(KitRole role) => role switch
        {
            KitRole.Display => "KitDisplay",
            KitRole.Title or KitRole.Identity or KitRole.Heading => "KitTitle",
            KitRole.Strong => "KitReadingStrong",
            KitRole.Label => "KitLabel",
            KitRole.Mono => "KitMono",
            _ => "KitReading",
        };

        private static readonly Dictionary<string, TMP_FontAsset?> faces = new();

        /// <summary>A role's static face in the room, or null where the faces are not built (the label keeps the reading fallback).</summary>
        public static TMP_FontAsset? Face(KitRole role)
        {
            var name = FaceName(role);
            if (!faces.TryGetValue(name, out var face)) faces[name] = face = Resources.Load<TMP_FontAsset>("HalcyonicUI/" + name);
            return face;
        }

        /// <summary>Whether <paramref name="font"/> is one of the room's faces, whose outside text stands upright.</summary>
        public static bool IsRoomFace(TMP_FontAsset? font)
        {
            if (font == null) return false;
            foreach (KitRole role in Enum.GetValues(typeof(KitRole)))
                if (Face(role) == font) return true;
            return false;
        }

        /// <summary>A role's line pitch in the room, in ems (ART's handoff): Display 1.06, Title 1.18, Body 1.40, Strong and Label 1.30, Mono 1.50.</summary>
        public static float RoomLeading(KitRole role) => role switch
        {
            KitRole.Display => SpatialKit.DisplayLeading,
            KitRole.Title or KitRole.Identity or KitRole.Heading => SpatialKit.TitleLeading,
            KitRole.Strong => SpatialKit.StrongLeading,
            KitRole.Label => SpatialKit.LabelLeading,
            KitRole.Mono => SpatialKit.MonoLeading,
            _ => SpatialKit.ReadingLeading,
        };

        /// <summary>A role's tracking, in hundredths of an em: Display -1, the title face -0.5, Label +0.5.</summary>
        public static float Tracking(KitRole role) => role switch
        {
            KitRole.Display => -1f,
            KitRole.Title or KitRole.Identity or KitRole.Heading => -0.5f,
            KitRole.Label => 0.5f,
            _ => 0f,
        };

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

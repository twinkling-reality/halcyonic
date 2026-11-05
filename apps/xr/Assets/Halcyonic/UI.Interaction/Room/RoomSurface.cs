#nullable enable
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>What a redesign surface is, which decides its colour and corner (DESIGN's SYSTEM-v4, 2026-11-05).</summary>
    public enum RoomSurfaceKind
    {
        /// <summary>The information panel: the computer's facts and the system's requests.</summary>
        Information,

        /// <summary>The deeper tray the choices stand in.</summary>
        Choices,

        /// <summary>The Type or Talk bar.</summary>
        Bar,

        /// <summary>A command's or exact text's well.</summary>
        Well,

        /// <summary>A quiet choice.</summary>
        Choice,

        /// <summary>The main choice.</summary>
        MainChoice,
    }

    /// <summary>
    /// The one surface every part of the redesign stands on. A plain placeholder until DESIGN's finish (frosted squircle
    /// glass) lands: this is the one place that changes then. Opaque, in SYSTEM-v4's top colours, with its 1 dp edge of
    /// white at 6 percent; corners per kind.
    /// </summary>
    public sealed class RoomSurface : MonoBehaviour
    {
        private Surface surface = null!;
        private RoomSurfaceKind kind;

        public RoomSurfaceKind Kind => kind;

        public Vector2 Size => surface.Size;

        public Surface Drawn => surface;

        public static RoomSurface Create(Transform parent, string name, RoomSurfaceKind kind, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var room = go.AddComponent<RoomSurface>();
            room.kind = kind;
            room.surface = Surface.Create(go.transform, "Surface", order);
            return room;
        }

        private (Vector2 Size, RoomSurfaceKind Kind, Color Fill)? drawn;

        /// <summary>A corner in dp in place of the kind's, as a round control's 22 dp squircle (DESIGN, 2026-11-05).</summary>
        public float? CornerOverrideDp { get; set; }

        /// <summary>Becomes another kind, as a choice becoming the main one; drawn as it at the next draw.</summary>
        public void SetKind(RoomSurfaceKind value) => kind = value;

        /// <summary>
        /// Draws the surface <paramref name="size"/> big, in the plane's units, centred on its transform, in its kind's colour
        /// or <paramref name="fill"/>. Drawn again only when something changed, so a still frame draws nothing.
        /// </summary>
        public void Draw(Vector2 size, Color? fill = null)
        {
            var colour = fill ?? Fill(kind);
            if (drawn is var (lastSize, lastKind, lastFill) && lastSize == size && lastKind == kind && lastFill == colour) return;
            drawn = (size, kind, colour);
            surface.Draw(size, Mathf.Min(RoomType.Units(CornerOverrideDp ?? CornerDp(kind)), size.y / 2f), colour, new Color(1f, 1f, 1f, 0.06f), RoomType.Units(1f));
        }

        /// <summary>The kind's corner in dp: panels 20, the bar 30 (DESIGN's numbers, 2026-11-05), a choice 30, a well 12.</summary>
        public static float CornerDp(RoomSurfaceKind kind) => kind switch
        {
            RoomSurfaceKind.Bar => 30f,
            RoomSurfaceKind.Choice or RoomSurfaceKind.MainChoice => 30f,
            RoomSurfaceKind.Well => 12f,
            _ => 20f,
        };

        /// <summary>The kind's colour: SYSTEM-v4's top colour, flat until the finish's gradient lands.</summary>
        public static Color Fill(RoomSurfaceKind kind) => kind switch
        {
            RoomSurfaceKind.Information => Hex(0x3A4A52),
            RoomSurfaceKind.Choices => Hex(0x20272A),
            RoomSurfaceKind.Bar => Hex(0x2A3236),
            RoomSurfaceKind.Well => Hex(0x26323A),
            RoomSurfaceKind.MainChoice => Hex(0xD5DADE),
            _ => Hex(0x66737A),
        };

        /// <summary>The words' colour on the kind: dark on the main choice, white elsewhere.</summary>
        public static Color Words(RoomSurfaceKind kind) => kind == RoomSurfaceKind.MainChoice ? Hex(0x172024) : Color.white;

        /// <summary>The muted grey for quieter words on a panel.</summary>
        public static Color Muted => Hex(0xC3CBCF);

        public static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }

    /// <summary>The redesign's one type size: every word Inter Regular 20 dp on a 28 dp line, larger text times 1.15.</summary>
    public static class RoomType
    {
        /// <summary>A length in dp as the plane's units (1 at its distance).</summary>
        public static float Units(float dp) => KitType.Units(SpatialKit.Degrees(dp));

        /// <summary>A length in dp that grows with the person's text size.</summary>
        public static float Scaled(float dp) => Units(dp) * GlazeText.Scale;

        /// <summary>A label in the one size, its box's top left on its transform.</summary>
        public static TextMeshPro Create(Transform parent, string name, Color colour, int order, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            var label = KitType.Create(parent, name, KitRole.Body, colour, order, alignment, SpatialKit.ReadingLeading, room: true);
            Size(label);
            return label;
        }

        /// <summary>Sizes a label to the one size at the person's text size, its line pitch 28 over 20.</summary>
        public static void Size(TMP_Text label)
        {
            var size = GlazeTokens.FontSize(Scaled(RoomGeometry.TypeDp));
            if (!Mathf.Approximately(label.fontSize, size)) label.fontSize = size;
            var spacing = KitType.LineSpacingFor(label, RoomGeometry.LineDp / RoomGeometry.TypeDp);
            if (!Mathf.Approximately(label.lineSpacing, spacing)) label.lineSpacing = spacing;
        }
    }
}

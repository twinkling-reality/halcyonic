#nullable enable
using System;
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>
    /// One column of a <see cref="PlaneComposition"/>: its width, which every part of it takes, so its
    /// left and right edges line up, and its parts' heights from the top, in units of the plane's
    /// distance (a length of 1 is as long as the plane is far).
    /// </summary>
    public sealed class PlaneColumn
    {
        public PlaneColumn(float width, params float[] heights)
        {
            if (!(width > 0f)) throw new ArgumentOutOfRangeException(nameof(width), width, "A column has a width.");
            if (heights.Length == 0) throw new ArgumentException("A column holds at least one part.", nameof(heights));
            foreach (var height in heights)
            {
                if (!(height > 0f)) throw new ArgumentOutOfRangeException(nameof(heights), height, "A part has a height.");
            }
            Width = width;
            Heights = heights;
        }

        public float Width { get; }

        /// <summary>Each part's height as asked, from the top; the last may be stretched (<see cref="PlaneComposition"/>).</summary>
        public IReadOnlyList<float> Heights { get; }
    }

    /// <summary>A part where its composition lays it: its column and place in it, its size, and its centre's offset from the composition's centre.</summary>
    public readonly struct PlanePart
    {
        public PlanePart(int column, int index, float width, float height, float right, float up)
        {
            Column = column;
            Index = index;
            Width = width;
            Height = height;
            Right = right;
            Up = up;
        }

        public int Column { get; }

        public int Index { get; }

        /// <summary>Its size, in units of the plane's distance: the last part of a shorter column is stretched down.</summary>
        public float Width { get; }

        public float Height { get; }

        /// <summary>Its centre's offset from the composition's centre along the plane, to the right and up, in units of the plane's distance.</summary>
        public float Right { get; }

        public float Up { get; }

        public float Left => Right - Width / 2f;

        public float Top => Up + Height / 2f;

        public float Bottom => Up - Height / 2f;
    }

    /// <summary>
    /// A composition on one plane facing the eyes (ADR 0026): columns of parts side by side, each part
    /// a shape of its own, such as a subject, a row of sections or a page; columns
    /// <see cref="ColumnGapMeters"/> apart and parts <see cref="PartGapDegrees"/> apart; every column
    /// starting on one top line and, its last part stretched down to the tallest column's bottom,
    /// ending on one bottom line. Grown whole by <paramref name="zoom"/>, as a panel is for larger
    /// text, its gaps with it. The plane faces the eyes at the composition's centre, never rolled,
    /// <see cref="Distance"/> away; the centre is placed as one panel of the composition's
    /// <see cref="Size"/> by <see cref="WorkspacePlacement.Place"/>, so the comfortable band, the
    /// floor for a panel taller than designed, the reading pitch, the clearance from labels and the
    /// measured field all hold; each part lies on the plane at its offset from the centre.
    /// </summary>
    public sealed class PlaneComposition
    {
        /// <summary>From the eyes to the plane's centre, at touch distance (ADR 0023).</summary>
        public const float Distance = 0.46f;

        /// <summary>Between columns on the plane.</summary>
        public const float ColumnGapMeters = 0.015f;

        /// <summary>Between parts on the plane, as an angle at its distance: 8 mm at 0.46 m.</summary>
        public const float PartGapDegrees = 1f;

        private readonly List<PlanePart> parts = new List<PlanePart>();

        /// <param name="zoom">How much larger than designed it is drawn, as <c>PanelFrame.Zoom</c>: 1, or the reading text's step.</param>
        public PlaneComposition(IReadOnlyList<PlaneColumn> columns, float zoom = 1f)
        {
            if (columns.Count == 0) throw new ArgumentException("A composition holds at least one column.", nameof(columns));
            if (!(zoom > 0f)) throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "A zoom is above nothing.");
            Columns = columns;
            Zoom = zoom;
            var columnGap = ColumnGapMeters / Distance * zoom;
            var partGap = Glaze.MetersAt(PartGapDegrees, 1f) * zoom;
            var width = -columnGap;
            var height = 0f;
            foreach (var column in columns)
            {
                width += column.Width * zoom + columnGap;
                height = MathF.Max(height, Stacked(column, zoom, partGap));
            }
            Width = width;
            Height = height;

            var left = -width / 2f;
            for (var c = 0; c < columns.Count; c++)
            {
                var column = columns[c];
                var columnWidth = column.Width * zoom;
                var stretch = height - Stacked(column, zoom, partGap);
                var top = height / 2f;
                for (var index = 0; index < column.Heights.Count; index++)
                {
                    var partHeight = column.Heights[index] * zoom + (index == column.Heights.Count - 1 ? stretch : 0f);
                    parts.Add(new PlanePart(c, index, columnWidth, partHeight, left + columnWidth / 2f, top - partHeight / 2f));
                    top -= partHeight + partGap;
                }
                left += columnWidth + columnGap;
            }
        }

        public IReadOnlyList<PlaneColumn> Columns { get; }

        public float Zoom { get; }

        /// <summary>The whole composition's width and height, in units of the plane's distance, its gaps included.</summary>
        public float Width { get; }

        public float Height { get; }

        /// <summary>Every part, column by column from the left, each from the top.</summary>
        public IReadOnlyList<PlanePart> Parts => parts;

        /// <summary>The composition as placement takes it: one panel of its size at the plane's distance.</summary>
        public PanelSize Size => new PanelSize(Distance, Width / 2f * Distance, Height / 2f * Distance);

        /// <summary>A length that spans <paramref name="degrees"/> at the eyes when centred in front of them, in units of the plane's distance.</summary>
        public static float Units(float degrees) => 2f * Glaze.MetersAt(degrees / 2f, 1f);

        /// <summary>
        /// Where the composition's centre goes, for the character <paramref name="opened"/> or the one
        /// nearest where the person looks: as one panel of its <see cref="Size"/>, by
        /// <see cref="WorkspacePlacement.Place"/>.
        /// </summary>
        public PanelDirection Place(float lookYaw, BodyInView opened, IReadOnlyList<BodyInView> bodies, float? surfaceDrop = null, ViewField? field = null) =>
            WorkspacePlacement.Place(lookYaw, opened, bodies, Size, surfaceDrop, field);

        /// <summary>
        /// Where a part's centre lies with the composition's centre at <paramref name="direction"/>, from
        /// the eyes, in meters: x to the right, y up, z forward, as Unity's axes. The plane faces the eyes
        /// at the centre and is never rolled: its right runs level, and its up leans back as it looks down.
        /// </summary>
        public static (float X, float Y, float Z) PointOf(PanelDirection direction, float right, float up)
        {
            var (forward, rightward, upward) = Axes(direction);
            return (
                (forward.X * Distance + rightward.X * right * Distance + upward.X * up * Distance),
                (forward.Y * Distance + rightward.Y * right * Distance + upward.Y * up * Distance),
                (forward.Z * Distance + rightward.Z * right * Distance + upward.Z * up * Distance));
        }

        /// <summary>The plane's forward, right and up for a centre at <paramref name="direction"/>, in Unity's axes.</summary>
        public static ((float X, float Y, float Z) Forward, (float X, float Y, float Z) Right, (float X, float Y, float Z) Up) Axes(PanelDirection direction)
        {
            var yaw = direction.Yaw * MathF.PI / 180f;
            var elevation = direction.Elevation * MathF.PI / 180f;
            var (sinYaw, cosYaw) = (MathF.Sin(yaw), MathF.Cos(yaw));
            var (sinElevation, cosElevation) = (MathF.Sin(elevation), MathF.Cos(elevation));
            return (
                (sinYaw * cosElevation, sinElevation, cosYaw * cosElevation),
                (cosYaw, 0f, -sinYaw),
                (-sinElevation * sinYaw, cosElevation, -sinElevation * cosYaw));
        }

        /// <summary>A column's height as asked, its parts and the gaps between them.</summary>
        private static float Stacked(PlaneColumn column, float zoom, float partGap)
        {
            var total = partGap * (column.Heights.Count - 1);
            foreach (var height in column.Heights) total += height * zoom;
            return total;
        }
    }
}

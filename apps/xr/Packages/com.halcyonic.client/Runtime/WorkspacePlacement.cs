#nullable enable
using System;
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>
    /// A character as the person sees it: the direction of its body's center from the eyes and how
    /// far around the center the body reaches, and how low and how wide it reaches with its label,
    /// in degrees. Yaw runs from the person's forward toward their right; elevation is up from eye
    /// level. Without a label, the body alone.
    /// </summary>
    public readonly struct BodyInView
    {
        public BodyInView(float yaw, float elevation, float radius, float? lowest = null, float? halfWidth = null)
        {
            Yaw = yaw;
            Elevation = elevation;
            Radius = radius;
            Lowest = lowest ?? elevation - radius;
            HalfWidth = halfWidth ?? radius;
        }

        public float Yaw { get; }

        public float Elevation { get; }

        public float Radius { get; }

        /// <summary>The lowest elevation the character reaches, its label plate included.</summary>
        public float Lowest { get; }

        /// <summary>Half the character's width with its label, the wider of the two.</summary>
        public float HalfWidth { get; }
    }

    /// <summary>Where the open workspace's center goes, seen from the eyes, and whether it clears every body on the stage.</summary>
    public readonly struct PanelDirection
    {
        public PanelDirection(float yaw, float elevation, bool clear, bool above)
        {
            Yaw = yaw;
            Elevation = elevation;
            Clear = clear;
            Above = above;
        }

        public float Yaw { get; }

        public float Elevation { get; }

        /// <summary>No character's body is behind the workspace.</summary>
        public bool Clear { get; }

        /// <summary>The workspace is above the characters it passes, rather than below them.</summary>
        public bool Above { get; }
    }

    /// <summary>The workspace's size and distance: it is scaled to keep its angular size wherever it opens.</summary>
    public readonly struct PanelSize
    {
        public PanelSize(float distance, float halfWidth, float halfHeight)
        {
            Distance = distance;
            HalfWidth = halfWidth;
            HalfHeight = halfHeight;
        }

        /// <summary>From the eyes to the workspace's center, in meters.</summary>
        public float Distance { get; }

        /// <summary>Half its width and height at that distance, in meters.</summary>
        public float HalfWidth { get; }

        public float HalfHeight { get; }

        public float HalfWidthDegrees => MathF.Atan2(HalfWidth, Distance) * 180f / MathF.PI;

        public float HalfHeightDegrees => MathF.Atan2(HalfHeight, Distance) * 180f / MathF.PI;
    }

    /// <summary>
    /// Where the workspace opens: within the person's reach, toward the character it belongs to, and
    /// clear of every character it passes, body and label, so the rest of the stage stays in view and
    /// readable (ADR 0023), either below those characters' labels or above their bodies, whichever
    /// keeps its center in the comfortable band. With the characters 2.4 m away, a little below the
    /// eyes, that is below their labels, over the lower part of the view; with the characters on a
    /// desk half a meter away and 30 degrees down, it is above them. Where it can clear neither way it
    /// moves the least into the band and may cover a character. It never goes into the surface the
    /// characters stand on.
    /// </summary>
    public static class WorkspacePlacement
    {
        /// <summary>
        /// The workspace's center stays between these elevations, in degrees from eye level: the
        /// comfortable middle of the view for a seated person. 31 below leaves a tall panel room under
        /// the deepest labels, a practice task's of two lines with its mark (ADR 0023); to be judged on
        /// the headset. A panel taller than designed goes lower (<see cref="Lowest"/>).
        /// </summary>
        public const float LowestDegrees = -31f;

        /// <summary>The tallest panel whose center goes no lower than <see cref="LowestDegrees"/>: a foreground panel's 26 degrees (ADR 0023).</summary>
        public const float DesignedHeightDegrees = 26f;

        public const float HighestDegrees = 2f;

        /// <summary>The workspace opens no farther than this to the side of where the person looks.</summary>
        public const float MaxSideDegrees = 15f;

        /// <summary>The angle between a body and the workspace's nearest edge: room for the body's hops and turns.</summary>
        public const float ClearanceDegrees = 1.5f;

        /// <summary>
        /// The angle between a label and the workspace's nearest corner: a little more than the degree
        /// kept between things, since labels stand still (ADR 0023).
        /// </summary>
        public const float LabelClearanceDegrees = 1.2f;

        /// <summary>Where a seated person looks at rest: of two places that fit, the workspace takes the one nearer this.</summary>
        public const float NaturalDegrees = -15f;

        /// <summary>How far above the surface the characters stand on the workspace's lowest edge stays, in meters.</summary>
        public const float SurfaceClearance = 0.05f;

        private const float DegreesPerRadian = 180f / MathF.PI;

        /// <summary>
        /// Chooses the direction of the workspace's center for the character <paramref name="opened"/>,
        /// with the person looking toward <paramref name="lookYaw"/> and the bodies on the stage,
        /// the opened one included, as <paramref name="bodies"/>. <paramref name="surfaceDrop"/> is how
        /// far below the eyes the surface under the characters is, in meters, when they stand on one.
        /// </summary>
        public static PanelDirection Place(float lookYaw, BodyInView opened, IReadOnlyList<BodyInView> bodies, PanelSize size,
            float? surfaceDrop = null, ViewField? field = null)
        {
            var yaw = lookYaw + Math.Clamp(DeltaAngle(lookYaw, opened.Yaw), -MaxSideDegrees, MaxSideDegrees);
            var halfHeight = size.HalfHeightDegrees;

            // The characters the workspace passes in front of, left to right, and the opened one
            // always: below their labels, or above their bodies.
            var lowest = opened.Lowest;
            var highest = opened.Elevation + opened.Radius;
            for (var index = 0; index < bodies.Count; index++)
            {
                var body = bodies[index];
                if (!Overlaps(yaw, size, body)) continue;
                lowest = Math.Min(lowest, body.Lowest);
                highest = Math.Max(highest, body.Elevation + body.Radius);
            }
            // A flat panel's corners stand farther than its edges' middles: below eye level they look
            // higher, above it lower. Its edges go where its corners clear what they pass.
            var halfWidth = size.HalfWidthDegrees;
            var top = Math.Min(lowest - LabelClearanceDegrees, EdgeForCorners(lowest - LabelClearanceDegrees, halfWidth));
            var bottom = Math.Max(highest + ClearanceDegrees, EdgeForCorners(highest + ClearanceDegrees, halfWidth));
            var below = top - halfHeight;
            var above = bottom + halfHeight;

            // Never into the surface: the lowest the center may go.
            var floor = Lowest(size);
            if (surfaceDrop.HasValue) floor = Math.Max(floor, LowestAboveSurface(size, surfaceDrop.Value));

            bool Fits(float elevation) => elevation >= floor - 1e-3f && elevation <= HighestDegrees + 1e-3f;
            var belowFits = Fits(below);
            var aboveFits = Fits(above);
            // Within the headset's measured field: the side that keeps the panel inside it wins, but
            // the field never pushes a panel into a label; below the labels is already as high as it goes.
            if (belowFits && aboveFits && below < Lowest(size, field) - 1e-3f) return new PanelDirection(yaw, above, true, true);
            if (belowFits && aboveFits)
            {
                var preferAbove = MathF.Abs(above - NaturalDegrees) < MathF.Abs(below - NaturalDegrees);
                return new PanelDirection(yaw, preferAbove ? above : below, true, preferAbove);
            }
            if (belowFits) return new PanelDirection(yaw, below, true, false);
            if (aboveFits) return new PanelDirection(yaw, above, true, true);

            // Neither clears: the side that needs less moving, moved into the band, inside the field.
            floor = Math.Max(floor, Lowest(size, field));
            float Moving(float elevation) => MathF.Abs(elevation - Math.Clamp(elevation, floor, HighestDegrees));
            var up = Moving(above) < Moving(below);
            var chosen = Math.Clamp(up ? above : below, Math.Min(floor, HighestDegrees), HighestDegrees);
            return new PanelDirection(yaw, chosen, false, up);
        }

        /// <summary>
        /// The lowest the center of a panel of <paramref name="size"/> may go, in degrees from eye
        /// level. <see cref="LowestDegrees"/> for one as tall as designed; a taller one, grown whole for
        /// the person's larger text or holding more, as Settings does, would not fit under the deepest
        /// labels above that, so its center may go lower by as much as it is taller: half of that keeps
        /// its upper edge where a designed panel's goes, and the other half leaves room for labels whose
        /// titles grew with the text (to be judged on the headset). With a measured
        /// <paramref name="field"/>, raised so that every corner stays
        /// <see cref="ViewField.EdgeMarginDegrees"/> inside it when the person looks toward it with the
        /// head level (<see cref="ViewField.LowestCenter"/>), but never above <see cref="HighestDegrees"/>.
        /// </summary>
        public static float Lowest(PanelSize size, ViewField? field = null)
        {
            var band = LowestDegrees - Math.Max(0f, 2f * size.HalfHeightDegrees - DesignedHeightDegrees);
            if (field is not ViewField known) return band;
            var inside = known.LowestCenter(size.HalfWidthDegrees, size.HalfHeightDegrees);
            return Math.Min(HighestDegrees, Math.Max(band, inside));
        }

        /// <summary>
        /// Where a flat panel's corners look, in degrees from eye level, when the middle of its edge is at
        /// <paramref name="edge"/> and it reaches <paramref name="halfWidthDegrees"/> to either side: its
        /// corners are farther away, so they look nearer eye level.
        /// </summary>
        public static float CornerElevation(float edge, float halfWidthDegrees) =>
            MathF.Atan(MathF.Tan(edge / DegreesPerRadian) * MathF.Cos(halfWidthDegrees / DegreesPerRadian)) * DegreesPerRadian;

        /// <summary>The edge's middle that puts a flat panel's corners at <paramref name="corner"/>: <see cref="CornerElevation"/> undone.</summary>
        public static float EdgeForCorners(float corner, float halfWidthDegrees) =>
            MathF.Atan(MathF.Tan(corner / DegreesPerRadian) / MathF.Cos(halfWidthDegrees / DegreesPerRadian)) * DegreesPerRadian;

        /// <summary>
        /// The lowest elevation of the workspace's center that keeps its lower edge
        /// <see cref="SurfaceClearance"/> above a surface <paramref name="drop"/> meters below the eyes.
        /// The workspace faces the eyes, so its lower edge sits at distance times the sine of the
        /// elevation, less half its height times the cosine.
        /// </summary>
        public static float LowestAboveSurface(PanelSize size, float drop)
        {
            var limit = -drop + SurfaceClearance;
            // The lower edge's height rises with the elevation; find where it meets the limit.
            float low = -89f, high = 89f;
            if (BottomEdge(size, high) < limit) return high;
            if (BottomEdge(size, low) >= limit) return low;
            for (var step = 0; step < 40; step++)
            {
                var middle = (low + high) / 2f;
                if (BottomEdge(size, middle) >= limit) high = middle;
                else low = middle;
            }
            return high;
        }

        /// <summary>The height of the workspace's lower edge relative to the eyes, in meters, with its center at an elevation.</summary>
        public static float BottomEdge(PanelSize size, float elevation)
        {
            var radians = elevation / DegreesPerRadian;
            return size.Distance * MathF.Sin(radians) - size.HalfHeight * MathF.Cos(radians);
        }

        /// <summary>
        /// Whether a character, its label included, lies within the workspace's width, with the
        /// clearance, measured around the vertical at the body's elevation: away from eye level the
        /// same width spans more yaw.
        /// </summary>
        private static bool Overlaps(float yaw, PanelSize size, BodyInView body)
        {
            var widening = 1f / MathF.Max(MathF.Cos(body.Elevation / DegreesPerRadian), 0.3f);
            return MathF.Abs(DeltaAngle(yaw, body.Yaw)) < (size.HalfWidthDegrees + body.HalfWidth) * widening + ClearanceDegrees;
        }

        /// <summary>The signed difference from one heading to another, between -180 and 180 degrees.</summary>
        public static float DeltaAngle(float from, float to)
        {
            var delta = (to - from) % 360f;
            if (delta > 180f) delta -= 360f;
            if (delta < -180f) delta += 360f;
            return delta;
        }
    }
}

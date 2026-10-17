#nullable enable
using System;
using System.Numerics;

namespace Halcyonic.Client
{
    /// <summary>
    /// The workspace's one gentle curve (ADR 0034): a vertical cylinder whose axis runs through the eyes, the
    /// forward region <see cref="Radius"/> from them. Layout stays flat; this maps it exactly. A point laid out
    /// <c>x</c> across the plane stands <c>x</c> of arc round the eyes, its height unchanged, and a band laid
    /// <c>z</c> beyond the plane stands at radius <see cref="Radius"/> + <c>z</c>, bent by the same angle, so
    /// recessed bands stay aligned. Coordinates are the eyes' frame: x right, y up, z forward, in any one unit.
    /// The mapping is the Interaction SDK's cylinder bend (CanvasCylinder.MeshTransform and
    /// MeshInverseTransform) measured from the eyes rather than the cylinder's front.
    /// </summary>
    public readonly struct PlaneCurve
    {
        private const float DegreesPerRadian = 180f / MathF.PI;

        /// <param name="radius">The forward region's distance from the axis through the eyes.</param>
        public PlaneCurve(float radius)
        {
            if (!(radius > 0f) || float.IsInfinity(radius)) throw new ArgumentOutOfRangeException(nameof(radius), radius, "The curve needs a positive radius.");
            Radius = radius;
        }

        public float Radius { get; }

        /// <summary>Where the curve draws a point laid out at <paramref name="plane"/>: x across, y up, z beyond the forward region.</summary>
        public Vector3 Map(Vector3 plane)
        {
            var angle = plane.X / Radius;
            var radius = Radius + plane.Z;
            if (!(radius > 0f)) throw new ArgumentOutOfRangeException(nameof(plane), plane, "A band must stand in front of the axis through the eyes.");
            if (!(MathF.Abs(angle) < MathF.PI)) throw new ArgumentOutOfRangeException(nameof(plane), plane, "Past half a turn the curve meets itself.");
            return new Vector3(radius * MathF.Sin(angle), plane.Y, radius * MathF.Cos(angle));
        }

        /// <summary>Where a point the curve draws was laid out: the inverse of <see cref="Map"/>, for hits and checks.</summary>
        public Vector3 Unmap(Vector3 seen)
        {
            var radius = MathF.Sqrt(seen.X * seen.X + seen.Z * seen.Z);
            if (!(radius > 0f)) throw new ArgumentOutOfRangeException(nameof(seen), seen, "A point on the axis has no angle.");
            return new Vector3(MathF.Atan2(seen.X, seen.Z) * Radius, seen.Y, radius - Radius);
        }

        /// <summary>The angle across, in degrees, that a laid-out width spreads at the eyes: arc over radius.</summary>
        public float SpreadDegrees(float width) => width / Radius * DegreesPerRadian;

        /// <summary>How many equal columns a laid-out width takes so that none spreads more than <paramref name="columnDegrees"/>; at least one.</summary>
        public int Columns(float width, float columnDegrees)
        {
            if (!(width >= 0f)) throw new ArgumentOutOfRangeException(nameof(width), width, "A width can't be negative.");
            if (!(columnDegrees > 0f)) throw new ArgumentOutOfRangeException(nameof(columnDegrees), columnDegrees, "A column needs a positive angle.");
            // A hair of tolerance, so a width of exactly n columns, rounded in single precision, takes n.
            return Math.Max(1, (int)MathF.Ceiling(SpreadDegrees(width) / columnDegrees - 1e-4f));
        }

        /// <summary>
        /// How far inside the curve a straight column <paramref name="columnDegrees"/> wide sags at its middle, at
        /// <paramref name="radius"/>: radius × (1 - cos(column / 2)).
        /// </summary>
        public static float ChordError(float columnDegrees, float radius)
        {
            if (!(radius > 0f)) throw new ArgumentOutOfRangeException(nameof(radius), radius, "The curve needs a positive radius.");
            return radius * (1f - MathF.Cos(columnDegrees * 0.5f / DegreesPerRadian));
        }

        /// <summary>
        /// The angles at the eyes, in degrees, of a glyph box laid out <paramref name="width"/> by <paramref name="height"/>
        /// and centred at <paramref name="centre"/>, where the curve draws it: between its drawn top and bottom, and
        /// between its drawn left and right, as the readability checks measure an em.
        /// </summary>
        public (float Tall, float Wide) SeenSize(Vector3 centre, float width, float height)
        {
            var up = new Vector3(0f, height / 2f, 0f);
            var across = new Vector3(width / 2f, 0f, 0f);
            return (Between(Map(centre + up), Map(centre - up)), Between(Map(centre + across), Map(centre - across)));
        }

        private static float Between(Vector3 a, Vector3 b) => MathF.Atan2(Vector3.Cross(a, b).Length(), Vector3.Dot(a, b)) * DegreesPerRadian;
    }
}

using System;

namespace Halcyonic.Client
{
    /// <summary>
    /// How far apart a place's characters stand and how many stand in its row (FIELD's places sheet, 2026-11-17): the pitch is the
    /// place's widest DRAWN tag plus half a degree, never under a body's width plus a degree and a half, so no two tags meet at any
    /// text size; a row holds as many as its span holds with its outer tags inside it. Angles in degrees as seen from the eyes,
    /// widths in metres, the distance in metres from the eyes.
    /// </summary>
    public static class PlacePitch
    {
        /// <summary>The least angle between two neighbouring tags as drawn (StageRender's floor).</summary>
        public const double TagsApartDegrees = 0.5;

        /// <summary>The least angle between two neighbouring bodies, where tags are narrower than the bodies.</summary>
        public const double BodiesApartDegrees = 1.5;

        /// <summary>
        /// A place's span, tags and all: a row of three at the longest names (a 200 dp name, 0.224 m with its pads) at the larger text
        /// size at 1.4 m, 2 x 11.03 + 10.53 = 32.58 degrees. Ring one's places so stand centred at 46.8 and 83.4 degrees each side:
        /// 30.5 to 63.1 and 67.1 to 99.7, four degrees apart, clear of the front's 30 and inside the seated reach of 100.
        /// </summary>
        public const double SpanDegrees = 32.6;

        /// <summary>The angle <paramref name="widthMetres"/> wide subtends at <paramref name="distanceMetres"/>.</summary>
        public static double DegreesOf(double widthMetres, double distanceMetres) =>
            2.0 * Math.Atan(Math.Max(0.0, widthMetres) / 2.0 / Math.Max(1e-6, distanceMetres)) * 180.0 / Math.PI;

        /// <summary>The width that subtends <paramref name="degrees"/> at <paramref name="distanceMetres"/>.</summary>
        public static double MetresOf(double degrees, double distanceMetres) =>
            2.0 * Math.Max(1e-6, distanceMetres) * Math.Tan(Math.Max(0.0, degrees) * Math.PI / 360.0);

        /// <summary>The pitch between neighbours: the widest drawn tag plus <see cref="TagsApartDegrees"/>, never under a body plus <see cref="BodiesApartDegrees"/>.</summary>
        public static double PitchDegrees(double widestTagDegrees, double bodyDegrees) =>
            Math.Max(widestTagDegrees + TagsApartDegrees, bodyDegrees + BodiesApartDegrees);

        /// <summary>
        /// How many stand in one row of a place <paramref name="spanDegrees"/> wide: (n - 1) pitches and the outer halves of the
        /// widest thing drawn (a tag or a body) inside the span; never fewer than one.
        /// </summary>
        public static int RowMost(double widestTagDegrees, double bodyDegrees, double spanDegrees = SpanDegrees)
        {
            var pitch = PitchDegrees(widestTagDegrees, bodyDegrees);
            var outer = Math.Max(widestTagDegrees, bodyDegrees);
            if (spanDegrees <= outer) return 1;
            return 1 + (int)Math.Floor((spanDegrees - outer) / pitch + 1e-9);
        }
    }
}

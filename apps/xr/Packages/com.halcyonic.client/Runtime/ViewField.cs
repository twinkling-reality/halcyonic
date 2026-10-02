#nullable enable
using System;
using System.Globalization;

namespace Halcyonic.Client
{
    /// <summary>
    /// How far a headset shows around where the person looks, in degrees from the view's forward:
    /// to the left, the right, up and down, each positive. Read from an eye's projection, so the app
    /// can log what the device in hand shows and decide what fits in it (a Quest 3S shows less than a
    /// Quest 3). Meta gives the whole field as 110 by 96 degrees for a Quest 3 and 96 by 90 for a
    /// Quest 3S, without the split around forward, which only the device tells.
    /// </summary>
    public readonly struct ViewField
    {
        private const double DegreesPerRadian = 180.0 / Math.PI;

        public ViewField(double left, double right, double up, double down)
        {
            Left = left;
            Right = right;
            Up = up;
            Down = down;
        }

        public double Left { get; }
        public double Right { get; }
        public double Up { get; }
        public double Down { get; }

        /// <summary>The field across, left to right.</summary>
        public double Across => Left + Right;

        /// <summary>The field from top to bottom.</summary>
        public double Tall => Up + Down;

        /// <summary>
        /// The field of an eye whose projection matrix has these entries, in Unity's convention
        /// (row, column): m00 = 2/(r−l), m02 = (r+l)/(r−l), m11 = 2/(t−b), m12 = (t+b)/(t−b), where
        /// l, r, b and t are the frustum's edges at unit distance.
        /// </summary>
        public static ViewField FromProjection(double m00, double m02, double m11, double m12)
        {
            if (!(m00 > 0) || !(m11 > 0)) throw new ArgumentOutOfRangeException(nameof(m00), "Not a perspective projection.");
            var right = (1 + m02) / m00;
            var left = (m02 - 1) / m00;
            var top = (1 + m12) / m11;
            var bottom = (m12 - 1) / m11;
            return new ViewField(
                -Math.Atan(left) * DegreesPerRadian,
                Math.Atan(right) * DegreesPerRadian,
                Math.Atan(top) * DegreesPerRadian,
                -Math.Atan(bottom) * DegreesPerRadian);
        }

        /// <summary>What both eyes show together: the left eye's left edge to the right eye's right.</summary>
        public static ViewField Both(ViewField leftEye, ViewField rightEye) =>
            new ViewField(leftEye.Left, rightEye.Right, Math.Min(leftEye.Up, rightEye.Up), Math.Min(leftEye.Down, rightEye.Down));

        /// <summary>
        /// Whether a direction <paramref name="yaw"/> degrees to the right of forward and
        /// <paramref name="elevation"/> degrees above it is in view. The field is a flat frustum, so off to
        /// the side it reaches less far up and down than straight ahead.
        /// </summary>
        public bool Shows(double yaw, double elevation)
        {
            if (Math.Abs(yaw) >= 90 || Math.Abs(elevation) >= 90) return false;
            var across = Math.Tan(yaw / DegreesPerRadian);
            if (across < -Math.Tan(Left / DegreesPerRadian) || across > Math.Tan(Right / DegreesPerRadian)) return false;
            var up = Math.Tan(elevation / DegreesPerRadian) / Math.Cos(yaw / DegreesPerRadian);
            return up >= -Math.Tan(Down / DegreesPerRadian) && up <= Math.Tan(Up / DegreesPerRadian);
        }

        /// <summary>The field in one log line of numbers: "left 52.0 right 43.0 up 48.0 down 50.0".</summary>
        public string Line() =>
            "left " + Left.ToString("0.0", CultureInfo.InvariantCulture)
            + " right " + Right.ToString("0.0", CultureInfo.InvariantCulture)
            + " up " + Up.ToString("0.0", CultureInfo.InvariantCulture)
            + " down " + Down.ToString("0.0", CultureInfo.InvariantCulture);
    }
}

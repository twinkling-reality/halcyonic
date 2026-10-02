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

        /// <summary>
        /// How far inside the field's edge what matters stays, in degrees: room for the lens's soft
        /// edge. A design decision; Meta gives no number for it. One and a half, because on a Quest 3S
        /// split evenly a workspace under the far lineup's labels, its top corners clear of them,
        /// has its lower corners 1.65 degrees inside the field: a larger margin would push it into
        /// the labels, which the layout never does.
        /// </summary>
        public const float EdgeMarginDegrees = 1.5f;

        /// <summary>
        /// The field of the headset in hand, both eyes together, once it has been measured; null
        /// before, and always in the editor's renders and the tests unless they set it, so the layout
        /// keeps its own angles until a device says otherwise.
        /// </summary>
        public static ViewField? Current
        {
            get => current;
            set
            {
                current = value;
                Version++;
            }
        }

        /// <summary>Counts each time <see cref="Current"/> is set, so a layout can tell, without comparing, that it changed.</summary>
        public static int Version { get; private set; }

        private static ViewField? current;

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

        /// <summary>
        /// The lowest elevation, in degrees from eye level, at which the center of a flat plate that
        /// faces the eyes, <paramref name="halfWidth"/> by <paramref name="halfHeight"/> degrees about
        /// its center, keeps every corner <see cref="EdgeMarginDegrees"/> inside this field, with the
        /// head level and turned toward it. Seen so, a facing plate's lower corners lie on the same edge
        /// of a flat field as its lower edge's middle. 0 when it does not fit even at eye level.
        /// </summary>
        public float LowestCenter(float halfWidth, float halfHeight)
        {
            var shrunk = new ViewField(Left - EdgeMarginDegrees, Right - EdgeMarginDegrees, Up - EdgeMarginDegrees, Down - EdgeMarginDegrees);
            if (!shrunk.Holds(0, halfWidth, halfHeight)) return 0f;
            double low = -89, high = 0;
            for (var step = 0; step < 40; step++)
            {
                var middle = (low + high) / 2;
                if (shrunk.Holds(middle, halfWidth, halfHeight)) high = middle;
                else low = middle;
            }
            return (float)high;
        }

        /// <summary>Whether every corner of a facing plate centered at <paramref name="elevation"/> is in view.</summary>
        private bool Holds(double elevation, double halfWidth, double halfHeight)
        {
            var e = elevation / DegreesPerRadian;
            var across = Math.Tan(halfWidth / DegreesPerRadian);
            var tall = Math.Tan(halfHeight / DegreesPerRadian);
            foreach (var x in new[] { -1.0, 1.0 })
            {
                foreach (var y in new[] { -1.0, 1.0 })
                {
                    // The center's direction, plus the plate's right and up, which face the eyes.
                    var px = x * across;
                    var py = Math.Sin(e) + y * tall * Math.Cos(e);
                    var pz = Math.Cos(e) - y * tall * Math.Sin(e);
                    var yaw = Math.Atan2(px, pz) * DegreesPerRadian;
                    var up = Math.Atan2(py, Math.Sqrt(px * px + pz * pz)) * DegreesPerRadian;
                    if (!Shows(yaw, up)) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// How far below eye level, in degrees, the middle of a plate <paramref name="halfWidth"/> by
        /// <paramref name="halfHeight"/> degrees goes, when it would go <paramref name="preferred"/>
        /// below: no lower than keeps every corner inside <paramref name="field"/> less the margin
        /// (<see cref="LowestCenter"/>), and no higher than <paramref name="highest"/> below, which
        /// keeps it clear of what stands above it. Without a field, <paramref name="preferred"/>.
        /// </summary>
        public static float BelowWithin(float preferred, float halfWidth, float halfHeight, ViewField? field, float highest)
        {
            if (field is not ViewField known) return preferred;
            var deepest = -known.LowestCenter(halfWidth, halfHeight);
            return Math.Max(highest, Math.Min(preferred, deepest));
        }

        /// <summary>The field in one log line of numbers: "left 52.0 right 43.0 up 48.0 down 50.0".</summary>
        public string Line() =>
            "left " + Left.ToString("0.0", CultureInfo.InvariantCulture)
            + " right " + Right.ToString("0.0", CultureInfo.InvariantCulture)
            + " up " + Up.ToString("0.0", CultureInfo.InvariantCulture)
            + " down " + Down.ToString("0.0", CultureInfo.InvariantCulture);
    }
}

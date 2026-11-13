#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>Where a placed shelf stands while the plane is open: its yaw, how far it moved from its own place, and why.</summary>
    public readonly struct ShelfAsidePlace
    {
        public ShelfAsidePlace(float yaw, float moved, bool clears, bool pastThirty)
        {
            Yaw = yaw; Moved = moved; Clears = clears; PastThirty = pastThirty;
        }

        /// <summary>The yaw it stands at, in degrees; its own where it need not move or where nothing inside the field clears.</summary>
        public float Yaw { get; }

        /// <summary>How far it turned from its own yaw, in degrees, signed as yaw is.</summary>
        public float Moved { get; }

        /// <summary>Whether it clears the plane's footprint where it stands. False is a finding: nothing inside the field cleared.</summary>
        public bool Clears { get; }

        /// <summary>Whether it had to go past <see cref="ShelfAside.AsideDegrees"/> to clear, which the log names.</summary>
        public bool PastThirty { get; }
    }

    /// <summary>
    /// The room yields to the plane (the lead's GO of 2026-11-13T19:34Z, pick B): a shelf the person placed, which the plane's
    /// footprint now meets, stands aside along its own arc while the plane is open, at the same distance and height, at the
    /// nearest yaw that clears, and returns when the plane closes or moves off it. Its own place is never rewritten: this is
    /// view placement only. Up to <see cref="AsideDegrees"/>, the small head turn the lead allows; as far as clears where that is
    /// not enough, said in the log; never past the field's edge from where the person faces, which is a finding, not a place.
    /// The footprint is the plane's largest at its width, never the bar laid now, so a bigger frame opening later at the same
    /// width never covers the shelf (AUDIT's condition 1, 19:34Z).
    /// </summary>
    public static class ShelfAside
    {
        /// <summary>The turn a shelf stands aside by at most before the log names it (the lead's pick B).</summary>
        public const float AsideDegrees = 30f;

        /// <summary>The search's step along the arc, in degrees.</summary>
        public const float StepDegrees = 1f;

        /// <summary>
        /// Where a shelf of <paramref name="halfYaw"/> degrees either side of its middle, standing from
        /// <paramref name="bottom"/> to <paramref name="top"/> degrees of elevation at <paramref name="ownYaw"/>, stands while a
        /// plane whose largest footprint is <paramref name="plane"/> (about the plane's heading <paramref name="planeYaw"/>) is open.
        /// It keeps <see cref="WorkspacePlacement.ClearanceDegrees"/> from the footprint, and its outer edge stays inside
        /// <paramref name="fieldHalfYaw"/> of <paramref name="facingYaw"/>, less the field's margin.
        /// </summary>
        public static ShelfAsidePlace For(float ownYaw, float halfYaw, float bottom, float top, AngularExtent plane, float planeYaw,
            float facingYaw, float fieldHalfYaw)
        {
            if (Clears(ownYaw, halfYaw, bottom, top, plane, planeYaw)) return new ShelfAsidePlace(ownYaw, 0f, true, false);
            var edge = fieldHalfYaw - ViewField.EdgeMarginDegrees;
            // The smaller turn first, either way; at equal turns the side away from the plane's middle.
            var away = Math.Sign(Wrap(ownYaw - planeYaw - (plane.Left + plane.Right) / 2f));
            var first = away == 0 ? 1 : away;
            for (var turn = StepDegrees; turn <= 180f; turn += StepDegrees)
            {
                var inField = false;
                foreach (var sign in new[] { first, -first })
                {
                    var yaw = ownYaw + sign * turn;
                    if (Math.Abs(Wrap(yaw - facingYaw)) + halfYaw > edge) continue;
                    inField = true;
                    if (Clears(yaw, halfYaw, bottom, top, plane, planeYaw)) return new ShelfAsidePlace(yaw, sign * turn, true, turn > AsideDegrees);
                }
                if (!inField) break;
            }
            return new ShelfAsidePlace(ownYaw, 0f, false, false);
        }

        /// <summary>Whether a shelf at <paramref name="yaw"/> keeps the clearance from the plane's footprint.</summary>
        private static bool Clears(float yaw, float halfYaw, float bottom, float top, AngularExtent plane, float planeYaw)
        {
            var middle = Wrap(yaw - planeYaw);
            return plane.Apart(new AngularExtent(middle - halfYaw, middle + halfYaw, bottom, top)) >= WorkspacePlacement.ClearanceDegrees;
        }

        /// <summary>An angle in (-180, 180].</summary>
        private static float Wrap(float degrees)
        {
            var wrapped = degrees % 360f;
            if (wrapped > 180f) wrapped -= 360f;
            if (wrapped <= -180f) wrapped += 360f;
            return wrapped;
        }
    }
}

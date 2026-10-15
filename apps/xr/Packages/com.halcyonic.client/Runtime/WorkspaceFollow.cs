#nullable enable
using System;
using System.Numerics;

namespace Halcyonic.Client
{
    /// <summary>Recover an out-of-view reading object once the wearer rests; never chase ordinary head motion.</summary>
    public sealed class WorkspaceFollow
    {
        public const double OutsideSeconds = PeekChoice.DwellSeconds;
        public const double SettleSeconds = 0.35; // Existing button settling interval.
        public const float CalmDegreesPerSecond = PeekChoice.TurningDegreesPerSecond;
        public const float CalmMetersPerSecond = 0.2f; // Candidate, requires wearer validation.
        private double? last, outsideSince, settledSince;
        private double began;
        private Vector3 position, forward;
        public bool Moving { get; private set; }

        /// <summary>True once requests a new content anchor. Moving stays true through animation and fresh settling.</summary>
        public bool Update(double now, Vector3 headPosition, Vector3 headForward, bool outside, bool blocked, bool animating)
        {
            var valid = Finite(now) && Finite(headPosition.X) && Finite(headPosition.Y) && Finite(headPosition.Z)
                && Finite(headForward.X) && Finite(headForward.Y) && Finite(headForward.Z) && headForward.LengthSquared() > 0.5f;
            if (!valid) { outsideSince = settledSince = last = null; return false; }
            headForward = Vector3.Normalize(headForward);
            var elapsed = last.HasValue ? now - last.Value : 0;
            var calm = elapsed > 0 && elapsed <= 0.25
                && Math.Acos(Math.Max(-1, Math.Min(1, Vector3.Dot(forward, headForward)))) * 180 / Math.PI / elapsed <= CalmDegreesPerSecond
                && Vector3.Distance(position, headPosition) / elapsed <= CalmMetersPerSecond;
            last = now; position = headPosition; forward = headForward;
            if (blocked) { outsideSince = settledSince = null; return false; }
            if (Moving)
            {
                if (animating || now - began < Glaze.SlideSeconds || !calm) settledSince = null;
                else if (!settledSince.HasValue) settledSince = now;
                else if (now - settledSince.Value >= SettleSeconds) { Moving = false; settledSince = null; }
                return false;
            }
            if (!outside || !calm) { outsideSince = null; return false; }
            if (!outsideSince.HasValue) outsideSince = now;
            if (now - outsideSince.Value < OutsideSeconds) return false;
            outsideSince = null; settledSince = null; began = now; Moving = true;
            return true;
        }
        public void Reset() { last = outsideSince = settledSince = null; Moving = false; }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

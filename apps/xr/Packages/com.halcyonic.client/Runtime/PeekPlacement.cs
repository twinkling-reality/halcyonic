#nullable enable
using System;
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>A character body relative to the peeked body's facing plane, in meters.</summary>
    public readonly struct PeekObstacle
    {
        public PeekObstacle(float x, float y, float depth, float radius)
        {
            X = x;
            Y = y;
            Depth = depth;
            Radius = radius;
        }

        public float X { get; }
        public float Y { get; }
        public float Depth { get; }
        public float Radius { get; }
    }

    /// <summary>Moves a peek plate toward the eyes until no character body can cover its words.</summary>
    public static class PeekPlacement
    {
        public const float Clearance = 0.02f;

        /// <summary>
        /// Returns the distance in front of the selected body's center. Only bodies whose projected
        /// circles touch the plate's rectangle affect it. Depth is positive away from the person.
        /// </summary>
        public static float FrontOffset(float selectedRadius, float preferredOffset, float left, float right,
            float halfHeight, IEnumerable<PeekObstacle> obstacles)
        {
            var offset = Math.Max(preferredOffset, selectedRadius + Clearance);
            foreach (var body in obstacles)
            {
                if (body.X + body.Radius < left || body.X - body.Radius > right
                    || Math.Abs(body.Y) > halfHeight + body.Radius) continue;
                offset = Math.Max(offset, body.Radius + Clearance - body.Depth);
            }
            return offset;
        }
    }
}

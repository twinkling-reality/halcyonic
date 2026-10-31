#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Halcyonic.Client
{
    /// <summary>
    /// Where the console stands from the eyes, as the shelf's designed home is placed beside it: its frame's left and right
    /// edges and lower edge in degrees (yaw in Unity's convention, 0 along +Z; elevation up from level), and its distance.
    /// </summary>
    public readonly struct ConsoleExtent
    {
        public ConsoleExtent(float leftYaw, float rightYaw, float bottomElevation, float distance)
        { LeftYaw = leftYaw; RightYaw = rightYaw; BottomElevation = bottomElevation; Distance = distance; }
        public float LeftYaw { get; }
        public float RightYaw { get; }
        public float BottomElevation { get; }
        public float Distance { get; }
    }

    /// <summary>
    /// The one search for an agent shelf's first preview, shared by the headset's placement and the Editor's measured
    /// search so the two can never drift. In ART's order (LARGER-SHELF-CALL addendum, 2026-10-31): first the designed home,
    /// just right of the console's right edge (its near edge <see cref="HomeGapDegrees"/> outside the frame), at the
    /// console's distance, its top just under the console's lower edge; then outward on the right, the smallest turn first
    /// in <see cref="TurnStepDegrees"/> steps; at each turn the home's distance, then nearer, then farther; at each distance
    /// the home's height, then lower, then higher; then the left side mirrored, beside the console's left edge; the far
    /// <see cref="MaximumTurnDegrees"/> turn last on each side. Never nearer than <see cref="MinimumDistance"/>, though the
    /// person's own moves may bring a shelf to the draft's 0.4 m. Each candidate is offered to the draft, whose own Allows
    /// predicate (the field and the workspace's clearance) keeps or refuses it: a refused candidate is never the result.
    /// </summary>
    public static class AgentShelfSearch
    {
        /// <summary>The nearest the automatic search ever offers a shelf, in metres from the eyes (ART's rule for characters).</summary>
        public const float MinimumDistance = 1.2f;

        /// <summary>The farthest, the draft's own bound.</summary>
        public const float MaximumDistance = 2f;

        /// <summary>The highest and lowest a shelf stands below the eyes, the draft's own bounds, in metres.</summary>
        public const float MinimumDrop = 0.15f, MaximumDrop = 0.9f;

        /// <summary>The widest turn either way of where the person faces, tried last.</summary>
        public const float MaximumTurnDegrees = 40f;

        /// <summary>The step outward from the home, in degrees.</summary>
        public const float TurnStepDegrees = 5f;

        /// <summary>How far outside the console's frame the shelf's near edge stands at its home, past the one degree clearance.</summary>
        public const float HomeGapDegrees = 1.5f;

        /// <summary>A shelf's top just under the console's lower edge, in metres below it.</summary>
        public const float HomeDropBelowConsole = 0.05f;

        /// <summary>Distances from the eyes tried after the home's, in metres.</summary>
        public static readonly IReadOnlyList<float> Distances = new[] { MinimumDistance, 1.5f, 1.8f, MaximumDistance };

        /// <summary>Drops below the eyes tried after the home's, in metres.</summary>
        public static readonly IReadOnlyList<float> Drops = new[] { MinimumDrop, 0.3f, 0.45f, 0.6f, 0.75f, 0.8f, MaximumDrop };

        /// <summary>
        /// Every candidate in ART's order. Without a console (none drawn), the home is straight ahead at 1.5 m and 0.45 m down,
        /// the right side first as with one.
        /// </summary>
        public static IEnumerable<AgentShelfPose> Candidates(Vector3 eyes, float facingYaw, ConsoleExtent? console = null)
        {
            var distance = Clamp(console?.Distance ?? 1.5f, MinimumDistance, MaximumDistance);
            var half = Degrees((float)Math.Atan(AgentShelfDraft.Width(1) / 2f / distance));
            var drop = console is ConsoleExtent below
                ? Clamp(-(distance * (float)Math.Tan(Radians(below.BottomElevation))) + HomeDropBelowConsole, MinimumDrop, MaximumDrop)
                : 0.45f;
            var rightHome = console is ConsoleExtent right ? right.RightYaw + HomeGapDegrees + half : facingYaw;
            var leftHome = console is ConsoleExtent left ? left.LeftYaw - HomeGapDegrees - half : facingYaw;
            foreach (var yaw in Side(rightHome, facingYaw, +1).Concat(Side(leftHome, facingYaw, -1)))
            foreach (var at in Nearer(distance, Distances))
            foreach (var down in Lower(drop, Drops))
                yield return new AgentShelfPose(Point(eyes, yaw, at, down), yaw);
        }

        /// <summary>
        /// Begins the draft on the first candidate it keeps. Returns false, with no candidate, when it refuses every one;
        /// <paramref name="tried"/> counts the candidates offered.
        /// </summary>
        public static bool Begin(AgentShelfDraft draft, Vector3 eyes, float facingYaw, out int tried, ConsoleExtent? console = null)
        {
            tried = 0;
            foreach (var candidate in Candidates(eyes, facingYaw, console))
            {
                tried++;
                draft.Begin(candidate, eyes);
                if (draft.Candidate != null) return true;
            }
            return false;
        }

        /// <summary>One side's yaws: the home, then outward by the step, the far turn last; none past it.</summary>
        private static IEnumerable<float> Side(float home, float facing, int sign)
        {
            var limit = facing + sign * MaximumTurnDegrees;
            var yaw = sign > 0 ? Math.Min(home, limit) : Math.Max(home, limit);
            for (; sign > 0 ? yaw < limit - 1e-3f : yaw > limit + 1e-3f; yaw += sign * TurnStepDegrees) yield return yaw;
            yield return limit;
        }

        /// <summary>The home's distance first, then nearer (smaller) ones from it, then farther ones.</summary>
        private static IEnumerable<float> Nearer(float home, IReadOnlyList<float> grid)
        {
            yield return home;
            foreach (var value in grid.Where(value => value < home - 1e-4f).OrderByDescending(value => value)) yield return value;
            foreach (var value in grid.Where(value => value > home + 1e-4f).OrderBy(value => value)) yield return value;
        }

        /// <summary>The home's height first, then lower (larger drops) from it, then higher (smaller drops).</summary>
        private static IEnumerable<float> Lower(float home, IReadOnlyList<float> grid)
        {
            yield return home;
            foreach (var value in grid.Where(value => value > home + 1e-4f).OrderBy(value => value)) yield return value;
            foreach (var value in grid.Where(value => value < home - 1e-4f).OrderByDescending(value => value)) yield return value;
        }

        private static Vector3 Point(Vector3 eyes, float yaw, float distance, float drop)
        {
            var radians = Radians(yaw);
            return eyes + new Vector3((float)Math.Sin(radians), 0f, (float)Math.Cos(radians)) * distance - Vector3.UnitY * drop;
        }

        private static float Clamp(float value, float low, float high) => Math.Max(low, Math.Min(high, value));
        private static float Radians(float degrees) => degrees * (float)Math.PI / 180f;
        private static float Degrees(float radians) => radians * 180f / (float)Math.PI;
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// A position on the floor plan, in meters: the horizontal coordinates of a frame whose Y axis
    /// points up, as Unity's world does. Headings are in degrees from +Z toward +X, which is to the
    /// person's right in Unity.
    /// </summary>
    public readonly struct PlanPoint : IEquatable<PlanPoint>
    {
        private const float DegreesPerRadian = 180f / MathF.PI;

        public PlanPoint(float x, float z)
        {
            X = x;
            Z = z;
        }

        public float X { get; }

        public float Z { get; }

        public float Length => MathF.Sqrt(X * X + Z * Z);

        /// <summary>This direction's heading in degrees, from +Z toward +X, between -180 and 180.</summary>
        public float Heading => MathF.Atan2(X, Z) * DegreesPerRadian;

        /// <summary>The unit direction at a heading, in degrees from +Z toward +X.</summary>
        public static PlanPoint Toward(float heading)
        {
            var radians = heading / DegreesPerRadian;
            return new PlanPoint(MathF.Sin(radians), MathF.Cos(radians));
        }

        public static PlanPoint operator +(PlanPoint a, PlanPoint b) => new PlanPoint(a.X + b.X, a.Z + b.Z);

        public static PlanPoint operator -(PlanPoint a, PlanPoint b) => new PlanPoint(a.X - b.X, a.Z - b.Z);

        public static PlanPoint operator *(PlanPoint a, float scale) => new PlanPoint(a.X * scale, a.Z * scale);

        public static float Distance(PlanPoint a, PlanPoint b) => (a - b).Length;

        public bool Equals(PlanPoint other) => X.Equals(other.X) && Z.Equals(other.Z);

        public override bool Equals(object? obj) => obj is PlanPoint other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Z);

        public override string ToString() => $"({X:0.###}, {Z:0.###})";
    }

    /// <summary>A position in the room, in meters, in a frame whose Y axis points up.</summary>
    public readonly struct RoomPoint
    {
        public RoomPoint(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }

        /// <summary>Where this point is on the floor plan.</summary>
        public PlanPoint Plan => new PlanPoint(X, Z);

        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
    }

    /// <summary>What a surface is, for choosing among them.</summary>
    public enum SurfaceKind
    {
        /// <summary>A desk or table top: where a seated person's work already is.</summary>
        Desk,

        /// <summary>The top of other furniture, such as a cabinet or a bed.</summary>
        Other,
    }

    /// <summary>
    /// A level surface that faces up, from the room's scene model: a desk or table top, or the top of
    /// other furniture. Its outline is where something can stand on it.
    /// </summary>
    public sealed class RoomSurface
    {
        public RoomSurface(SurfaceKind kind, float height, IEnumerable<PlanPoint> outline)
        {
            Kind = kind;
            Height = height;
            Outline = (outline ?? throw new ArgumentNullException(nameof(outline))).ToArray();
        }

        public SurfaceKind Kind { get; }

        /// <summary>The surface's height in the room, in meters.</summary>
        public float Height { get; }

        /// <summary>The outline on the floor plan, in order around it. Fewer than three points is no surface.</summary>
        public IReadOnlyList<PlanPoint> Outline { get; }
    }

    /// <summary>
    /// Something that stands on or over surfaces, such as a monitor or a lamp on a desk: its
    /// footprint on the floor plan and how high it reaches. Characters do not stand inside one.
    /// </summary>
    public sealed class RoomObstacle
    {
        public RoomObstacle(float bottom, float top, IEnumerable<PlanPoint> footprint)
        {
            Bottom = Math.Min(bottom, top);
            Top = Math.Max(bottom, top);
            Footprint = (footprint ?? throw new ArgumentNullException(nameof(footprint))).ToArray();
        }

        public float Bottom { get; }

        public float Top { get; }

        public IReadOnlyList<PlanPoint> Footprint { get; }
    }

    /// <summary>Where the person's eyes are, and which way they face on the level.</summary>
    public readonly struct Viewer
    {
        public Viewer(RoomPoint eyes, PlanPoint facing)
        {
            var length = facing.Length;
            if (!(length > 1e-4f)) throw new ArgumentException("The person must face some direction on the level.", nameof(facing));
            Eyes = eyes;
            Facing = facing * (1f / length);
        }

        public RoomPoint Eyes { get; }

        /// <summary>A unit direction on the floor plan.</summary>
        public PlanPoint Facing { get; }
    }

    /// <summary>
    /// A place for the stage on a surface: the point where the middle of the lineup stands, and how it
    /// sits relative to the person when it was chosen.
    /// </summary>
    public sealed class StageSpot
    {
        internal StageSpot(RoomSurface surface, RoomPoint point, float reach, float turn, float drop)
        {
            Surface = surface;
            Point = point;
            Reach = reach;
            Turn = turn;
            Drop = drop;
        }

        public RoomSurface Surface { get; }

        /// <summary>The middle of the lineup, on the surface.</summary>
        public RoomPoint Point { get; }

        /// <summary>The horizontal distance from the eyes to <see cref="Point"/>, in meters.</summary>
        public float Reach { get; }

        /// <summary>Degrees from where the person faced to <see cref="Point"/>, positive to the right.</summary>
        public float Turn { get; }

        /// <summary>How far the surface is below the eyes, in meters.</summary>
        public float Drop { get; }
    }

    /// <summary>
    /// Chooses where on a real surface the characters stand, for a person seated at a desk: a desk
    /// or table top in comfortable reach and view first, then the top of other furniture, with the
    /// whole lineup on the surface and clear of what stands on it.
    /// </summary>
    /// <remarks>
    /// The stage draws the lineup on an arc centered on the person's eyes whose radius is their
    /// horizontal distance to the chosen point, <see cref="LineupDegrees"/> between its outermost
    /// characters, and scales the characters with that radius (<c>CharacterStage</c>). A spot
    /// counts only when every point of that arc, widened by <see cref="Margin"/> for the characters'
    /// label plates, lies on the surface and outside every obstacle standing on it. Among the spots
    /// that fit, the chooser prefers the one nearest <see cref="IdealReach"/> straight ahead: ten
    /// centimeters off the ideal reach costs as much as ten degrees of turn, and as much as every
    /// <see cref="SteepnessDegreesPerCost"/> degrees of looking down more steeply than
    /// <see cref="ComfortableDownDegrees"/>, so the lineup moves farther out on a low table rather
    /// than make the person look steeply down.
    /// </remarks>
    public static class StageSurfaces
    {
        /// <summary>The angle between the outermost characters, as the stage draws them.</summary>
        public const float LineupDegrees = 60f;

        /// <summary>The nearest the middle of the lineup may be, horizontally; the stage's arc is never closer.</summary>
        public const float NearestReach = 0.4f;

        /// <summary>About where a seated person's forearm rests: close enough to poke, far enough to see the lineup whole.</summary>
        public const float IdealReach = 0.55f;

        /// <summary>The farthest a seated person reaches by leaning a little.</summary>
        public const float FarthestReach = 0.9f;

        /// <summary>A surface at least this far below the eyes, so a shelf at eye level is not a stage.</summary>
        public const float LeastDrop = 0.15f;

        /// <summary>A surface at most this far below the eyes: a coffee table for a seated person, a desk for a standing one.</summary>
        public const float MostDrop = 1.0f;

        /// <summary>The widest turn from where the person faced, to either side, in degrees.</summary>
        public const float WidestTurn = 45f;

        /// <summary>Looking down at the lineup more steeply than this costs extra.</summary>
        public const float ComfortableDownDegrees = 45f;

        /// <summary>How many degrees of looking down beyond the comfortable angle cost as much as ten centimeters of reach.</summary>
        public const float SteepnessDegreesPerCost = 2.5f;

        /// <summary>Never more steeply than this.</summary>
        public const float SteepestDownDegrees = 62f;

        private const float TurnStep = 5f;
        private const float ReachStep = 0.05f;
        private const float ArcStep = 5f;

        /// <summary>
        /// Tolerances for a placement kept from an earlier session, so that sitting a little
        /// differently keeps it, and only a different seat or room chooses again.
        /// </summary>
        private const float KeptNearerBy = 0.05f;
        private const float KeptFartherBy = 0.3f;
        private const float KeptTurnSlack = 25f;
        private const float KeptHeightSlack = 0.05f;

        private const float DegreesPerRadian = 180f / MathF.PI;

        /// <summary>
        /// How far every point of the arc must stay inside the surface's edge and outside obstacles:
        /// a quarter of a label plate, which the stage scales with the radius, and a centimeter. The
        /// plates may overhang an edge a little; half a plate left no room on a real desk, where the
        /// free strip in front of a monitor was about 18 cm deep (the first headset session in a
        /// scanned room, 2026-09-30).
        /// </summary>
        public static float Margin(float reach) => 0.01f + 0.05f * reach;

        /// <summary>
        /// The best spot for the stage, or null when no surface fits the lineup within comfortable
        /// reach and view. Desks and tables win over other furniture wherever both fit.
        /// </summary>
        public static StageSpot? Choose(IEnumerable<RoomSurface> surfaces, IEnumerable<RoomObstacle> obstacles, Viewer viewer)
        {
            if (surfaces == null) throw new ArgumentNullException(nameof(surfaces));
            if (obstacles == null) throw new ArgumentNullException(nameof(obstacles));
            var blocking = obstacles.ToList();
            StageSpot? best = null;
            var bestCost = float.PositiveInfinity;
            foreach (var surface in surfaces)
            {
                if (surface.Outline.Count < 3) continue;
                var drop = viewer.Eyes.Y - surface.Height;
                if (drop < LeastDrop || drop > MostDrop) continue;
                var onSurface = blocking.Where(obstacle => Stands(obstacle, surface.Height)).ToList();
                var (spot, cost) = BestOn(surface, onSurface, viewer, drop);
                if (spot == null) continue;
                if (best == null || Better(spot, cost, best, bestCost))
                {
                    best = spot;
                    bestCost = cost;
                }
            }
            return best;
        }

        /// <summary>
        /// Whether the lineup, drawn on the arc through <paramref name="middle"/> around the person,
        /// stands on <paramref name="surface"/> and clear of obstacles, keeping <see cref="Margin"/>
        /// times <paramref name="marginScale"/> from every edge.
        /// </summary>
        public static bool LineupFits(RoomSurface surface, IEnumerable<RoomObstacle> obstacles, Viewer viewer, PlanPoint middle, float marginScale = 1f)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (obstacles == null) throw new ArgumentNullException(nameof(obstacles));
            if (surface.Outline.Count < 3) return false;
            var eyes = viewer.Eyes.Plan;
            var toMiddle = middle - eyes;
            var reach = toMiddle.Length;
            if (!(reach > 1e-3f)) return false;
            var onSurface = obstacles.Where(obstacle => Stands(obstacle, surface.Height)).ToList();
            return ArcFits(surface, onSurface, eyes, toMiddle.Heading, reach, Margin(reach) * marginScale);
        }

        /// <summary>
        /// Whether a placement kept from an earlier session still suits the person where they are
        /// now: within a looser reach, turn and height than a new choice, and, when the room's
        /// surfaces are known, with the lineup still standing on one of them, at half the margin.
        /// Without surfaces, as when the room's layout may not be read, comfort alone decides.
        /// </summary>
        public static bool StillSuits(RoomPoint middle, Viewer viewer, IEnumerable<RoomSurface>? surfaces, IEnumerable<RoomObstacle>? obstacles)
        {
            var toMiddle = middle.Plan - viewer.Eyes.Plan;
            var reach = toMiddle.Length;
            if (reach < NearestReach - KeptNearerBy || reach > FarthestReach + KeptFartherBy) return false;
            if (MathF.Abs(TurnBetween(viewer.Facing.Heading, toMiddle.Heading)) > WidestTurn + KeptTurnSlack) return false;
            var drop = viewer.Eyes.Y - middle.Y;
            if (drop < LeastDrop - KeptHeightSlack || drop > MostDrop + KeptHeightSlack) return false;
            if (surfaces == null) return true;
            var blocking = (obstacles ?? Enumerable.Empty<RoomObstacle>()).ToList();
            return surfaces.Any(surface =>
                MathF.Abs(surface.Height - middle.Y) <= KeptHeightSlack &&
                LineupFits(surface, blocking, viewer, middle.Plan, 0.5f));
        }

        /// <summary>
        /// Why no spot was chosen, one line per surface, for the log: the surface's kind, how far below
        /// the eyes it is, how far away and how far to the side it reaches, how many objects stand on
        /// it, and the first rule the lineup breaks there. Numbers only; nothing from the room's labels.
        /// </summary>
        public static IReadOnlyList<string> Explain(IEnumerable<RoomSurface> surfaces, IEnumerable<RoomObstacle> obstacles, Viewer viewer)
        {
            if (surfaces == null) throw new ArgumentNullException(nameof(surfaces));
            if (obstacles == null) throw new ArgumentNullException(nameof(obstacles));
            var blocking = obstacles.ToList();
            var eyes = viewer.Eyes.Plan;
            var facing = viewer.Facing.Heading;
            var lines = new List<string>();
            foreach (var surface in surfaces)
            {
                var kind = surface.Kind == SurfaceKind.Desk ? "desk or table" : "other furniture";
                if (surface.Outline.Count < 3)
                {
                    lines.Add(kind + ": no outline");
                    continue;
                }
                var drop = viewer.Eyes.Y - surface.Height;
                var nearest = Polygon.Contains(surface.Outline, eyes) ? 0f : Polygon.DistanceToEdge(surface.Outline, eyes);
                var farthest = surface.Outline.Max(corner => PlanPoint.Distance(corner, eyes));
                var turns = surface.Outline.Select(corner => TurnBetween(facing, (corner - eyes).Heading)).ToList();
                var onSurface = blocking.Where(obstacle => Stands(obstacle, surface.Height)).ToList();
                // For the words only: the chooser checks every obstacle at this height against the arc
                // wherever it is, but the log names those whose footprint reaches over this surface.
                var over = onSurface.Where(obstacle => Overlaps(obstacle.Footprint, surface.Outline)).ToList();
                string reason;
                if (drop < LeastDrop || drop > MostDrop) reason = FormattableString.Invariant($"not {LeastDrop:0.00} to {MostDrop:0.00} m below the eyes");
                else if (BestOn(surface, onSurface, viewer, drop).Spot != null) reason = "the lineup fits";
                else if (BestOn(surface, new List<RoomObstacle>(), viewer, drop).Spot != null) reason = "the lineup would fit without the objects standing on it";
                else if (BestOn(surface, new List<RoomObstacle>(), viewer, drop, 0f).Spot != null) reason = "the lineup fits only without its margin from the edges";
                else reason = "the lineup's arc does not fit on it within reach and view";
                lines.Add(FormattableString.Invariant(
                    $"{kind}, {drop:0.00} m below the eyes, {nearest:0.00} to {farthest:0.00} m away, {turns.Min():0} to {turns.Max():0} degrees from where the person faces, {over.Count} objects on it: {reason}"));
                if (drop < LeastDrop || drop > MostDrop || nearest > FarthestReach) continue;
                foreach (var obstacle in over)
                {
                    var near = Polygon.Contains(obstacle.Footprint, eyes) ? 0f : Polygon.DistanceToEdge(obstacle.Footprint, eyes);
                    var far = obstacle.Footprint.Max(corner => PlanPoint.Distance(corner, eyes));
                    var sides = obstacle.Footprint.Select(corner => TurnBetween(facing, (corner - eyes).Heading)).ToList();
                    lines.Add(FormattableString.Invariant(
                        $"  object on it, {obstacle.Top - surface.Height:0.00} m tall, {near:0.00} to {far:0.00} m away, {sides.Min():0} to {sides.Max():0} degrees from where the person faces"));
                }
            }
            return lines;
        }

        /// <summary>The signed difference between two headings, in degrees between -180 and 180.</summary>
        public static float TurnBetween(float fromHeading, float toHeading)
        {
            var turn = (toHeading - fromHeading) % 360f;
            if (turn > 180f) turn -= 360f;
            if (turn <= -180f) turn += 360f;
            return turn;
        }

        private static (StageSpot? Spot, float Cost) BestOn(RoomSurface surface, List<RoomObstacle> obstacles, Viewer viewer, float drop, float marginScale = 1f)
        {
            var eyes = viewer.Eyes.Plan;
            var facing = viewer.Facing.Heading;
            StageSpot? best = null;
            var bestCost = float.PositiveInfinity;
            var turns = (int)MathF.Round(WidestTurn / TurnStep);
            var reaches = (int)MathF.Round((FarthestReach - NearestReach) / ReachStep);
            for (var t = -turns; t <= turns; t++)
            {
                var turn = t * TurnStep;
                for (var r = 0; r <= reaches; r++)
                {
                    var reach = NearestReach + r * ReachStep;
                    var down = MathF.Atan2(drop, reach) * DegreesPerRadian;
                    if (down > SteepestDownDegrees) continue;
                    var cost = MathF.Abs(reach - IdealReach) / 0.1f
                        + MathF.Abs(turn) / 10f
                        + MathF.Max(0f, down - ComfortableDownDegrees) / SteepnessDegreesPerCost;
                    if (cost >= bestCost) continue;
                    if (!ArcFits(surface, obstacles, eyes, facing + turn, reach, Margin(reach) * marginScale)) continue;
                    var middle = eyes + PlanPoint.Toward(facing + turn) * reach;
                    best = new StageSpot(surface, new RoomPoint(middle.X, surface.Height, middle.Z), reach, turn, drop);
                    bestCost = cost;
                }
            }
            return (best, bestCost);
        }

        private static bool Better(StageSpot spot, float cost, StageSpot best, float bestCost)
        {
            if (spot.Surface.Kind != best.Surface.Kind) return spot.Surface.Kind == SurfaceKind.Desk;
            return cost < bestCost;
        }

        /// <summary>Whether two outlines on the floor plan share any area, judged by their corners and centers.</summary>
        private static bool Overlaps(IReadOnlyList<PlanPoint> a, IReadOnlyList<PlanPoint> b)
        {
            if (a.Count < 3 || b.Count < 3) return false;
            return a.Any(corner => Polygon.Contains(b, corner)) || b.Any(corner => Polygon.Contains(a, corner))
                || Polygon.Contains(b, Center(a)) || Polygon.Contains(a, Center(b));
        }

        private static PlanPoint Center(IReadOnlyList<PlanPoint> corners) =>
            new PlanPoint(corners.Average(corner => corner.X), corners.Average(corner => corner.Z));

        /// <summary>An obstacle stands on a surface when it rises above it and starts no higher than a monitor's foot would.</summary>
        private static bool Stands(RoomObstacle obstacle, float height) =>
            obstacle.Footprint.Count >= 3 && obstacle.Top > height + 0.01f && obstacle.Bottom < height + 0.3f;

        private static bool ArcFits(RoomSurface surface, List<RoomObstacle> obstacles, PlanPoint eyes, float heading, float reach, float margin)
        {
            var samples = (int)MathF.Round(LineupDegrees / ArcStep);
            for (var i = 0; i <= samples; i++)
            {
                var point = eyes + PlanPoint.Toward(heading - LineupDegrees / 2f + i * ArcStep) * reach;
                if (!Polygon.Contains(surface.Outline, point) || Polygon.DistanceToEdge(surface.Outline, point) < margin) return false;
                foreach (var obstacle in obstacles)
                {
                    if (Polygon.Contains(obstacle.Footprint, point) || Polygon.DistanceToEdge(obstacle.Footprint, point) < margin) return false;
                }
            }
            return true;
        }
    }

    /// <summary>Polygons on the floor plan, given as their corners in order, either way around.</summary>
    internal static class Polygon
    {
        /// <summary>Whether a point is inside, by the even-odd rule, so outlines that are not convex work too.</summary>
        public static bool Contains(IReadOnlyList<PlanPoint> corners, PlanPoint point)
        {
            var inside = false;
            for (int i = 0, j = corners.Count - 1; i < corners.Count; j = i++)
            {
                var a = corners[i];
                var b = corners[j];
                if ((a.Z > point.Z) != (b.Z > point.Z) && point.X < (b.X - a.X) * (point.Z - a.Z) / (b.Z - a.Z) + a.X)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>The distance from a point to the nearest edge.</summary>
        public static float DistanceToEdge(IReadOnlyList<PlanPoint> corners, PlanPoint point)
        {
            var nearest = float.PositiveInfinity;
            for (int i = 0, j = corners.Count - 1; i < corners.Count; j = i++)
            {
                nearest = MathF.Min(nearest, DistanceToSegment(point, corners[j], corners[i]));
            }
            return nearest;
        }

        private static float DistanceToSegment(PlanPoint point, PlanPoint a, PlanPoint b)
        {
            var along = b - a;
            var lengthSquared = along.X * along.X + along.Z * along.Z;
            if (lengthSquared < 1e-12f) return PlanPoint.Distance(point, a);
            var t = ((point.X - a.X) * along.X + (point.Z - a.Z) * along.Z) / lengthSquared;
            t = MathF.Max(0f, MathF.Min(1f, t));
            return PlanPoint.Distance(point, a + along * t);
        }
    }
}

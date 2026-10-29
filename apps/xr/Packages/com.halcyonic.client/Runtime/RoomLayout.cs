#nullable enable
using System;
using System.Numerics;

namespace Halcyonic.Client
{
    /// <summary>
    /// Where a flat surface of the room stands (ADR 0040): its centre, the bearing from the eyes it stands on (degrees,
    /// 0 straight along +z, positive to the right), how far it tilts up to face the eyes, and its size in metres. It
    /// always faces back along its bearing. Coordinates are the room's: x right, y up, z forward, in metres.
    /// </summary>
    public readonly struct RoomPose : IEquatable<RoomPose>
    {
        public RoomPose(Vector3 centre, float bearing, float pitch, float width, float height)
        {
            Centre = centre;
            Bearing = RoomLayout.Wrap(bearing);
            Pitch = pitch;
            Width = width;
            Height = height;
        }

        public Vector3 Centre { get; }
        public float Bearing { get; }
        public float Pitch { get; }
        public float Width { get; }
        public float Height { get; }

        /// <summary>The way its face looks: back along its bearing, toward the eyes.</summary>
        public Vector3 Facing => -RoomLayout.Direction(Bearing, Pitch);

        /// <summary>Its face's up, square to <see cref="Facing"/>.</summary>
        public Vector3 Up
        {
            get
            {
                var (bearing, pitch) = (Bearing * RoomLayout.RadiansPerDegree, Pitch * RoomLayout.RadiansPerDegree);
                return new Vector3(-MathF.Sin(pitch) * MathF.Sin(bearing), MathF.Cos(pitch), -MathF.Sin(pitch) * MathF.Cos(bearing));
            }
        }

        /// <summary>The middle of its left and right edges as the person sees them.</summary>
        public Vector3 Left => Centre - Across * (Width / 2f);
        public Vector3 Right => Centre + Across * (Width / 2f);

        /// <summary>The heights of its top and bottom edges' middles.</summary>
        public float Top => Centre.Y + Up.Y * Height / 2f;
        public float Bottom => Centre.Y - Up.Y * Height / 2f;

        private Vector3 Across
        {
            get
            {
                var bearing = Bearing * RoomLayout.RadiansPerDegree;
                return new Vector3(MathF.Cos(bearing), 0f, -MathF.Sin(bearing));
            }
        }

        public bool Equals(RoomPose other) => Centre == other.Centre && Bearing == other.Bearing && Pitch == other.Pitch
            && Width == other.Width && Height == other.Height;

        public override bool Equals(object? obj) => obj is RoomPose other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Centre, Bearing, Pitch, Width, Height);

        public override string ToString() => $"{Centre} at {Bearing} degrees, tilted {Pitch}, {Width} by {Height} m";
    }

    /// <summary>
    /// The room's distances and poses (ADR 0040). Sizes are the kit's, in degrees, and come in as arguments
    /// (SpatialKit holds them); this holds only where each surface stands: a picked project's screen about 2 m away with
    /// its bar beneath, the moment slot beside the task's character, the reading surface about 1 m ahead, and where they
    /// fold. No surface stands nearer than Meta's ray-cast reach or farther (0.8 to 3 m).
    /// </summary>
    public static class RoomLayout
    {
        internal const float RadiansPerDegree = MathF.PI / 180f;

        /// <summary>A picked project's screen (ADR 0040 rule 2; visionOS's window distance).</summary>
        public const float ScreenDistance = 2f;

        /// <summary>How much nearer than the screen its bar stands: parallel, slightly in front (visionOS's ornament).</summary>
        public const float BarForward = 0.02f;

        /// <summary>The reading surface (ADR 0040 rule 5), from the eyes.</summary>
        public const float ReadingDistance = 1f;

        /// <summary>Meta's ray-cast reach for a surface the person points at.</summary>
        public const float NearestSurface = 0.8f;
        public const float FarthestSurface = 3f;

        /// <summary>How far the moment slot's middle stands above the character's own point. Candidate, requires wearer validation.</summary>
        public const float MomentAboveCharacter = 0.15f;

        /// <summary>The angle kept clear between the character and the slot's nearer edge, as the workspace keeps its own clearance.</summary>
        public const float MomentClearanceDegrees = WorkspacePlacement.ClearanceDegrees;

        /// <summary>
        /// A picked project's screen: <paramref name="widthDegrees"/> across at <paramref name="distance"/> on
        /// <paramref name="bearing"/>, its middle at eye height, <paramref name="aspect"/> wide to high.
        /// </summary>
        public static RoomPose Screen(Vector3 eyes, float bearing, float widthDegrees, float aspect, float distance = ScreenDistance)
        {
            Reach(distance, nameof(distance));
            if (!(aspect > 0f) || float.IsInfinity(aspect)) throw new ArgumentOutOfRangeException(nameof(aspect), aspect, "A screen needs a positive aspect.");
            var width = Span(widthDegrees, distance);
            return new RoomPose(eyes + Direction(bearing, 0f) * distance, bearing, 0f, width, width / aspect);
        }

        /// <summary>
        /// The screen's bar: its edges the screen's, <see cref="BarForward"/> nearer, its top
        /// <paramref name="gapDegrees"/> under the screen's bottom and <paramref name="heightDegrees"/> high, as the eyes see it.
        /// </summary>
        public static RoomPose Bar(Vector3 eyes, RoomPose screen, float heightDegrees, float gapDegrees)
        {
            if (!(gapDegrees >= 0f)) throw new ArgumentOutOfRangeException(nameof(gapDegrees), gapDegrees, "A gap can't be negative.");
            var screenDistance = Flat(screen.Centre - eyes);
            var distance = screenDistance - BarForward;
            Reach(distance, nameof(screen));
            var top = MathF.Atan2(screen.Bottom - eyes.Y, screenDistance) - gapDegrees * RadiansPerDegree;
            var bottom = top - Angle(heightDegrees, nameof(heightDegrees));
            var (topY, bottomY) = (eyes.Y + distance * MathF.Tan(top), eyes.Y + distance * MathF.Tan(bottom));
            var middle = eyes + Direction(screen.Bearing, 0f) * distance;
            return new RoomPose(new Vector3(middle.X, (topY + bottomY) / 2f, middle.Z), screen.Bearing, 0f, screen.Width, topY - bottomY);
        }

        /// <summary>
        /// The one slot a task's moments pop into: beside its character at the character's distance, on the side toward
        /// where the person looks (<paramref name="lookBearing"/>), its nearer edge <see cref="MomentClearanceDegrees"/>
        /// beyond the character's body and label, which reach <paramref name="characterHalfDegrees"/> either side of it
        /// (<see cref="BodyInView.HalfWidth"/>), tilted to face the eyes so it spans <paramref name="widthDegrees"/> as they see it. The same
        /// character always gets the same slot.
        /// </summary>
        public static RoomPose MomentSlot(Vector3 eyes, float lookBearing, Vector3 character, float characterHalfDegrees, float widthDegrees, float aspect)
        {
            if (!(characterHalfDegrees >= 0f && characterHalfDegrees < 90f))
                throw new ArgumentOutOfRangeException(nameof(characterHalfDegrees), characterHalfDegrees, "A character reaches 0 to 90 degrees either side.");
            if (!(aspect > 0f) || float.IsInfinity(aspect)) throw new ArgumentOutOfRangeException(nameof(aspect), aspect, "A slot needs a positive aspect.");
            var offset = character - eyes;
            var flat = Math.Min(FarthestSurface, Math.Max(NearestSurface, Flat(offset)));
            var rise = offset.Y + MomentAboveCharacter;
            var distance = MathF.Sqrt(flat * flat + rise * rise);
            var width = Span(widthDegrees, distance);
            var characterBearing = MathF.Atan2(offset.X, offset.Z) / RadiansPerDegree;
            // The bearing from its middle to its nearer edge, seen from above.
            var half = MathF.Atan2(width / 2f, flat) / RadiansPerDegree;
            var toward = Wrap(characterBearing - lookBearing) >= 0f ? -1f : 1f;
            var bearing = characterBearing + toward * (characterHalfDegrees + MomentClearanceDegrees + half);
            var pitch = MathF.Atan2(rise, flat) / RadiansPerDegree;
            return new RoomPose(eyes + Direction(bearing, pitch) * distance, bearing, pitch, width, width / aspect);
        }

        /// <summary>
        /// The reading surface: <see cref="ReadingDistance"/> along <paramref name="look"/>, its pitch held within the
        /// room's (<see cref="WorkspaceFollow.MaximumPitchDegrees"/>), tilted square to the eyes and
        /// <paramref name="widthDegrees"/> by <paramref name="heightDegrees"/> as they see it.
        /// </summary>
        public static RoomPose ReadingSurface(Vector3 eyes, Vector3 look, float widthDegrees, float heightDegrees)
        {
            if (!(look.LengthSquared() > 1e-6f) || !Finite(look)) throw new ArgumentOutOfRangeException(nameof(look), look, "The reading surface needs a direction to unfold along.");
            look = Vector3.Normalize(look);
            var bearing = MathF.Atan2(look.X, look.Z) / RadiansPerDegree;
            var pitch = MathF.Asin(Math.Max(-1f, Math.Min(1f, look.Y))) / RadiansPerDegree;
            pitch = Math.Max(-WorkspaceFollow.MaximumPitchDegrees, Math.Min(WorkspaceFollow.MaximumPitchDegrees, pitch));
            return new RoomPose(eyes + Direction(bearing, pitch) * ReadingDistance, bearing, pitch,
                Span(widthDegrees, ReadingDistance), Span(heightDegrees, ReadingDistance));
        }

        /// <summary>
        /// A pose <paramref name="fraction"/> of the way from <paramref name="from"/> to <paramref name="to"/>, held to
        /// the ends, turning the short way round: a surface unfolding from its moment and folding back into it.
        /// </summary>
        public static RoomPose Between(RoomPose from, RoomPose to, float fraction)
        {
            if (!(fraction > 0f)) return from;
            if (fraction >= 1f) return to;
            return new RoomPose(Vector3.Lerp(from.Centre, to.Centre, fraction), from.Bearing + Wrap(to.Bearing - from.Bearing) * fraction,
                from.Pitch + (to.Pitch - from.Pitch) * fraction, from.Width + (to.Width - from.Width) * fraction,
                from.Height + (to.Height - from.Height) * fraction);
        }

        /// <summary>The width that spans <paramref name="degrees"/> at the eyes, square on at <paramref name="distance"/>.</summary>
        public static float Span(float degrees, float distance) => 2f * distance * MathF.Tan(Angle(degrees, nameof(degrees)) / 2f);

        /// <summary>A unit direction on <paramref name="bearing"/>, raised <paramref name="pitch"/> degrees.</summary>
        public static Vector3 Direction(float bearing, float pitch)
        {
            var (b, p) = (bearing * RadiansPerDegree, pitch * RadiansPerDegree);
            return new Vector3(MathF.Cos(p) * MathF.Sin(b), MathF.Sin(p), MathF.Cos(p) * MathF.Cos(b));
        }

        /// <summary>A bearing in degrees, wrapped into (-180, 180].</summary>
        public static float Wrap(float degrees)
        {
            var wrapped = degrees % 360f;
            if (wrapped > 180f) wrapped -= 360f;
            else if (wrapped <= -180f) wrapped += 360f;
            return wrapped;
        }

        private static float Angle(float degrees, string name)
        {
            if (!(degrees > 0f && degrees < 180f)) throw new ArgumentOutOfRangeException(name, degrees, "An angle across must be between 0 and 180 degrees.");
            return degrees * RadiansPerDegree;
        }

        private static void Reach(float distance, string name)
        {
            if (!(distance >= NearestSurface && distance <= FarthestSurface))
                throw new ArgumentOutOfRangeException(name, distance, "A surface the person points at stands 0.8 to 3 m away.");
        }

        private static float Flat(Vector3 offset) => MathF.Sqrt(offset.X * offset.X + offset.Z * offset.Z);

        internal static bool Finite(Vector3 value) => !float.IsNaN(value.X) && !float.IsInfinity(value.X) && !float.IsNaN(value.Y)
            && !float.IsInfinity(value.Y) && !float.IsNaN(value.Z) && !float.IsInfinity(value.Z);
    }

    /// <summary>
    /// The leash for the room's heads-up items (ADR 0040 rule 8; Meta's ranking in validation/heads-up-guidance.md): at
    /// most two, small and low at the sides, kept with the person as they walk, turning with the body only once the head
    /// has turned past a dead zone, easing there, and fading while the person moves. Never locked to the head: a turn
    /// inside the dead zone moves nothing.
    /// </summary>
    public sealed class HeadsUpLeash
    {
        public const int Items = 2;

        /// <summary>From the eyes, as Meta's example follower stands.</summary>
        public const float Distance = 1f;

        /// <summary>How far the head turns before the leash follows. Candidate, requires wearer validation.</summary>
        public const float DeadZoneDegrees = 30f;

        /// <summary>Each item's bearing off the leash's, out of the central view. Candidate, requires wearer validation.</summary>
        public const float SideDegrees = 35f;

        /// <summary>How far below eye level the items stand. Candidate, requires wearer validation.</summary>
        public const float BelowDegrees = 20f;

        /// <summary>How quickly the leash eases toward the dead zone's edge, per second (the Core SDK's AlertViewHUD smoothing).</summary>
        public const float FollowPerSecond = 7f;

        /// <summary>How long the items take to fade out or back. Candidate, requires wearer validation.</summary>
        public const double FadeSeconds = 0.25;

        private double last;
        private Vector3 head;

        public bool Placed { get; private set; }

        /// <summary>The bearing the items hang from.</summary>
        public float Bearing { get; private set; }

        /// <summary>1 while the person stands still, 0 while they move faster than <see cref="WorkspaceFollow.CalmMetersPerSecond"/>.</summary>
        public float Opacity { get; private set; }

        /// <summary>Follows the head at <paramref name="now"/> seconds; a head that isn't finite is ignored.</summary>
        public void Update(double now, Vector3 headPosition, float headBearing)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || !RoomLayout.Finite(headPosition) || float.IsNaN(headBearing) || float.IsInfinity(headBearing)) return;
            if (!Placed)
            {
                (Placed, last, head, Bearing, Opacity) = (true, now, headPosition, RoomLayout.Wrap(headBearing), 1f);
                return;
            }
            var elapsed = now - last;
            if (!(elapsed > 0)) return;
            var moving = Vector3.Distance(head, headPosition) / elapsed > WorkspaceFollow.CalmMetersPerSecond;
            var step = (float)(elapsed / FadeSeconds);
            Opacity = moving ? Math.Max(0f, Opacity - step) : Math.Min(1f, Opacity + step);
            var turned = RoomLayout.Wrap(headBearing - Bearing);
            if (MathF.Abs(turned) > DeadZoneDegrees)
            {
                var edge = turned - MathF.Sign(turned) * DeadZoneDegrees;
                Bearing = RoomLayout.Wrap(Bearing + edge * (1f - MathF.Exp(-FollowPerSecond * (float)elapsed)));
            }
            (last, head) = (now, headPosition);
        }

        /// <summary>
        /// Where item <paramref name="index"/> stands, 0 on the left and 1 on the right, <paramref name="widthDegrees"/>
        /// by <paramref name="heightDegrees"/> as the eyes see it, tilted to face them.
        /// </summary>
        public RoomPose Item(int index, float widthDegrees, float heightDegrees)
        {
            if (index < 0 || index >= Items) throw new ArgumentOutOfRangeException(nameof(index), index, "The room has at most two heads-up items.");
            if (!Placed) throw new InvalidOperationException("The leash has not seen the head yet.");
            var bearing = Bearing + (index == 0 ? -SideDegrees : SideDegrees);
            return new RoomPose(head + RoomLayout.Direction(bearing, -BelowDegrees) * Distance, bearing, -BelowDegrees,
                RoomLayout.Span(widthDegrees, Distance), RoomLayout.Span(heightDegrees, Distance));
        }
    }
}

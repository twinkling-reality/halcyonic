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

        /// <summary>The angle between the screen's bottom edge and its bar's top, as DESIGN's storyboard stands them.</summary>
        public const float BarGapDegrees = 0.75f;

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
        /// A picked project's screen: <paramref name="widthDegrees"/> across and <paramref name="heightDegrees"/> high, as the
        /// eyes see it square on, at <paramref name="distance"/> on <paramref name="bearing"/> and <paramref name="elevation"/>,
        /// tilted square to the eyes. The caller stands it where the console stands, the look's elevation held within the
        /// room's pitch (<see cref="WorkspaceFollow.MaximumPitchDegrees"/>) plus the person's move: level at eye height
        /// whatever the look, it sat low in the view under a raised one and its bar was cut (DEVICE's D-XR-8).
        /// </summary>
        public static RoomPose Screen(Vector3 eyes, float bearing, float widthDegrees, float heightDegrees, float distance = ScreenDistance, float elevation = 0f)
        {
            Reach(distance, nameof(distance));
            if (!(MathF.Abs(elevation) < 90f)) throw new ArgumentOutOfRangeException(nameof(elevation), elevation, "A screen stands within 90 degrees of level.");
            return new RoomPose(eyes + Direction(bearing, elevation) * distance, bearing, elevation, Span(widthDegrees, distance), Span(heightDegrees, distance));
        }

        /// <summary>The least gap between two project screens standing side by side, in degrees at the eyes.</summary>
        public const float ScreensGapDegrees = 4f;

        /// <summary>
        /// How far round from straight ahead anything of the room's may stand (the lead's 2026-11-17T19:05Z): the reach of a seated
        /// person's turn of the head and shoulders, since they are often seated, in bed or with a show before them. Nothing goes behind.
        /// </summary>
        public const float SeatedReachDegrees = 100f;

        /// <summary>
        /// A project screen standing over its own team (FIELD's 2026-11-17T19:13Z places sheet, AUDIT's 19:13Z agreement): this wide
        /// at the eyes, so it stays within a seated person's comfortable upward look; the one pressed comes forward as the work.
        /// </summary>
        public const float TeamScreenDegrees = 24f;

        /// <summary>How far above level a team's screen's lower edge stands, clear of the team's highest lifted tag by 3 degrees at Larger (FIELD's 19:13Z).</summary>
        public const float TeamScreenLowestDegrees = 8f;

        /// <summary>
        /// A team's screen over its place on <paramref name="bearing"/>, <paramref name="distance"/> from the eyes: <see cref="TeamScreenDegrees"/>
        /// across at <paramref name="aspect"/>, its lower edge <see cref="TeamScreenLowestDegrees"/> above level, square to the eyes.
        /// </summary>
        public static RoomPose TeamScreen(Vector3 eyes, float bearing, float distance, float aspect)
        {
            if (!(aspect > 0f)) throw new ArgumentOutOfRangeException(nameof(aspect), aspect, "An aspect is above 0.");
            // Its height's angle is its width's over the aspect, as the kit lays a screen (SpatialKit.ScreenHeightDegreesAt); square on,
            // its lower edge stands half that under its centre's elevation.
            var tall = TeamScreenDegrees / aspect;
            return Screen(eyes, bearing, TeamScreenDegrees, tall, distance, TeamScreenLowestDegrees + tall / 2f);
        }

        /// <summary>
        /// How far either side of straight ahead stays clear for the work or the show the person watches (the lead's
        /// 2026-11-17T19:05Z; FIELD's places stand past it): a screen beside its place keeps its edges out of it.
        /// </summary>
        public const float ClearAheadDegrees = 30f;

        /// <summary>
        /// Where several project screens stand round the person (the lead's 2026-11-17T18:55Z (d)), as bearings: one for each of
        /// <paramref name="wantedBearings"/>, given left to right (RoomPlaces' order), each the bearing of its project's place.
        /// Each screen stands as near its own place's bearing as it can while every one stands at least
        /// <paramref name="widthDegrees"/> and <paramref name="gapDegrees"/> past the one on its left, so none covers another,
        /// and every screen's edges stay within <paramref name="reachDegrees"/> of <paramref name="ahead"/>: the least-squares
        /// fit under that order and that reach (pool-adjacent violators, then held to the reach), a room of windows round a
        /// seated person, never behind them. With <paramref name="clearAheadDegrees"/> above 0, no screen's edge stands within
        /// that much of ahead either: the screens asked left of ahead fit on the left, the rest on the right, a side that cannot
        /// hold its own passing its nearest to the other side, each side fitted the same way within its own bounds.
        /// </summary>
        public static float[] Screens(System.Collections.Generic.IReadOnlyList<float> wantedBearings, float widthDegrees, float ahead = 0f,
            float gapDegrees = ScreensGapDegrees, float reachDegrees = SeatedReachDegrees, float clearAheadDegrees = 0f)
        {
            if (wantedBearings == null) throw new ArgumentNullException(nameof(wantedBearings));
            Angle(widthDegrees, nameof(widthDegrees));
            if (!(gapDegrees >= 0f)) throw new ArgumentOutOfRangeException(nameof(gapDegrees), gapDegrees, "A gap can't be negative.");
            if (!(reachDegrees > 0f && reachDegrees <= 180f)) throw new ArgumentOutOfRangeException(nameof(reachDegrees), reachDegrees, "A reach is between 0 and 180 degrees each side.");
            if (!(clearAheadDegrees >= 0f && clearAheadDegrees < reachDegrees)) throw new ArgumentOutOfRangeException(nameof(clearAheadDegrees), clearAheadDegrees, "The clear front lies inside the reach.");
            if (float.IsNaN(ahead) || float.IsInfinity(ahead)) throw new ArgumentOutOfRangeException(nameof(ahead), ahead, "A bearing must be a number.");
            var count = wantedBearings.Count;
            var wanted = new float[count];
            for (var index = 0; index < count; index++)
            {
                var bearing = wantedBearings[index];
                if (float.IsNaN(bearing) || float.IsInfinity(bearing)) throw new ArgumentOutOfRangeException(nameof(wantedBearings), bearing, "A bearing must be a number.");
                wanted[index] = Wrap(bearing - ahead);
            }
            var apart = widthDegrees + gapDegrees;
            var stood = new float[count];
            if (clearAheadDegrees <= 0f)
            {
                if (!FitRow(wanted, 0, count, -reachDegrees + widthDegrees / 2f, reachDegrees - widthDegrees / 2f, apart, stood))
                    throw new ArgumentOutOfRangeException(nameof(wantedBearings), count, "That many screens can't stand within the person's reach without covering one another.");
            }
            else
            {
                // How many each side holds, then the split: those asked left of ahead, moved across where a side is full.
                var side = reachDegrees - clearAheadDegrees;
                var holds = side < widthDegrees ? 0 : 1 + (int)MathF.Floor((side - widthDegrees) / apart + 1e-4f);
                if (count > 2 * holds) throw new ArgumentOutOfRangeException(nameof(wantedBearings), count, "That many screens can't stand beside the clear front within the person's reach.");
                var left = 0;
                while (left < count && wanted[left] < 0f) left++;
                left = Math.Max(Math.Min(left, holds), count - holds);
                FitRow(wanted, 0, left, -reachDegrees + widthDegrees / 2f, -clearAheadDegrees - widthDegrees / 2f, apart, stood);
                FitRow(wanted, left, count, clearAheadDegrees + widthDegrees / 2f, reachDegrees - widthDegrees / 2f, apart, stood);
            }
            for (var index = 0; index < count; index++) stood[index] = Wrap(ahead + stood[index]);
            return stood;
        }

        /// <summary>
        /// Fits screens <paramref name="from"/> up to <paramref name="to"/> of <paramref name="wanted"/> (bearings about ahead) in a
        /// row, centres <paramref name="apart"/> or more, the first no lower than <paramref name="lowest"/> and the last no higher than
        /// <paramref name="highest"/>, into <paramref name="stood"/>; false where the row does not fit.
        /// </summary>
        private static bool FitRow(float[] wanted, int from, int to, float lowest, float highest, float apart, float[] stood)
        {
            var count = to - from;
            if (count <= 0) return true;
            var most = highest - (count - 1) * apart;
            if (lowest > most + 1e-4f) return false;
            // Each screen less its place in the row is non-decreasing: pool adjacent blocks that break it, each at its mean.
            var means = new float[count];
            var sizes = new int[count];
            var blocks = 0;
            for (var index = 0; index < count; index++)
            {
                means[blocks] = wanted[from + index] - index * apart;
                sizes[blocks] = 1;
                blocks++;
                while (blocks > 1 && means[blocks - 2] > means[blocks - 1])
                {
                    var joined = sizes[blocks - 2] + sizes[blocks - 1];
                    means[blocks - 2] = (means[blocks - 2] * sizes[blocks - 2] + means[blocks - 1] * sizes[blocks - 1]) / joined;
                    sizes[blocks - 2] = joined;
                    blocks--;
                }
            }
            // Held to the bounds: the fit, clamped to one bound for every screen, is still the least-squares fit within them.
            var at = 0;
            for (var block = 0; block < blocks; block++)
                for (var member = 0; member < sizes[block]; member++, at++)
                    stood[from + at] = MathF.Min(MathF.Max(means[block], lowest), MathF.Max(lowest, most)) + at * apart;
            return true;
        }

        /// <summary>
        /// The screen's bar: as wide as the screen, on its bearing and <see cref="BarForward"/> nearer, its top
        /// <paramref name="gapDegrees"/> under the screen's bottom and <paramref name="heightDegrees"/> high, as the eyes see
        /// it. It stands low in the view, so it tilts up to face the eyes there: upright, its targets would read short of
        /// the kit's floor at that slant and distance.
        /// </summary>
        public static RoomPose Bar(Vector3 eyes, RoomPose screen, float heightDegrees, float gapDegrees = BarGapDegrees)
        {
            if (!(gapDegrees >= 0f)) throw new ArgumentOutOfRangeException(nameof(gapDegrees), gapDegrees, "A gap can't be negative.");
            var reach = Vector3.Distance(screen.Centre, eyes);
            var distance = reach - BarForward;
            Reach(distance, nameof(screen));
            // The screen stands square to the eyes, so its bottom edge is half its height's angle under its own elevation.
            var screenPitch = screen.Pitch * RadiansPerDegree;
            var top = screenPitch - MathF.Atan2(screen.Height / 2f, reach) - gapDegrees * RadiansPerDegree;
            var tall = Angle(heightDegrees, nameof(heightDegrees));
            var middle = top - tall / 2f;
            // Its middle on the bearing, where that angle meets the screen's plane brought BarForward nearer: level, as far
            // across the floor as the screen less BarForward.
            var along = distance / MathF.Cos(middle - screenPitch);
            var pitch = middle / RadiansPerDegree;
            return new RoomPose(eyes + Direction(screen.Bearing, pitch) * along, screen.Bearing, pitch, screen.Width, 2f * along * MathF.Tan(tall / 2f));
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

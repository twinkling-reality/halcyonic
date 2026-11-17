#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>Where one project's place stands about the person (FIELD's places sheet v3, the lead's 2026-11-17T19:05Z seated rule).</summary>
    public readonly struct PlaceBearing
    {
        public PlaceBearing(float yawDegrees, float distance, float drop, int ring)
        {
            YawDegrees = yawDegrees;
            Distance = distance;
            Drop = drop;
            Ring = ring;
        }

        /// <summary>The place's middle, in degrees about the eyes from ahead: negative to the person's left.</summary>
        public float YawDegrees { get; }

        /// <summary>How far from the eyes the place's floor arc stands, in metres.</summary>
        public float Distance { get; }

        /// <summary>How much lower than ring one the place stands, in metres: a deeper ring stands nearer the floor.</summary>
        public float Drop { get; }

        /// <summary>The place's ring: 0 the nearest, each further one deeper and lower, never further round.</summary>
        public int Ring { get; }
    }

    /// <summary>
    /// The seated rule (the lead, 2026-11-17T19:05Z; FIELD's sheet v3): nothing past <see cref="ReachDegrees"/> either side, the front within
    /// <see cref="FrontClearDegrees"/> kept for the work or the show. Places fill both sides first, nearest first, in the order projects first
    /// had work (<see cref="RoomPlaces"/>' order); more than a ring holds go to the next ring at the same bearings, deeper and lower.
    /// </summary>
    public static class PlaceBearings
    {
        /// <summary>The bearings of a ring's places, in fill order: left then right, the nearest pair first.</summary>
        /// <remarks>FIELD's corrected numbers (19:11Z): a place's span holds its outer tags, the largest row of 3 at Larger, so the inner place stands
        /// 30.5 to 63 degrees and the outer 67 to 99.5: the front's 30 and the reach's 100 hold, with the lead's 4 degrees between.</remarks>
        public static readonly float[] Centres = { -46.75f, 46.75f, -83.25f, 83.25f };

        /// <summary>The least clear angle the lead keeps between neighbouring places (2026-11-17T19:08Z, a MUST).</summary>
        public const float GapDegrees = 4f;

        /// <summary>How wide a place stands, in degrees about the eyes.</summary>
        public const float SpanDegrees = 32.5f;

        /// <summary>The front kept clear of every place, either side of ahead.</summary>
        public const float FrontClearDegrees = 30f;

        /// <summary>The seated reach: no place's edge past it either side.</summary>
        public const float ReachDegrees = 100f;

        /// <summary>Ring one's distance from the eyes, in metres.</summary>
        public const float RingOneDistance = 1.4f;

        /// <summary>How much further each deeper ring stands, in metres (ring two at 2.0 m).</summary>
        public const float RingStep = 0.6f;

        /// <summary>How much lower each deeper ring stands, in metres (FIELD's suggestion, judged on the built frame).</summary>
        public const float RingDrop = 0.25f;

        /// <summary>The place <paramref name="index"/> in <see cref="RoomPlaces"/>' order stands here.</summary>
        public static PlaceBearing Of(int index)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index), "A place's index is never negative.");
            var ring = index / Centres.Length;
            return new PlaceBearing(Centres[index % Centres.Length], RingOneDistance + RingStep * ring, RingDrop * ring, ring);
        }

        /// <summary>The least clear angle between two neighbouring places' edges in a ring, in degrees.</summary>
        public static float LeastGapDegrees
        {
            get
            {
                var least = float.MaxValue;
                for (var a = 0; a < Centres.Length; a++)
                    for (var b = 0; b < Centres.Length; b++)
                        if (a != b && Math.Sign(Centres[a]) == Math.Sign(Centres[b]))
                            least = Math.Min(least, Math.Abs(Centres[a] - Centres[b]) - SpanDegrees);
                return least;
            }
        }
    }
}

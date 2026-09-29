#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace Halcyonic.Client
{
    /// <summary>The outline of a character's body, seen from the front.</summary>
    public enum BodyShape
    {
        Round,
        Clover,
        Blossom,
        Hexagon,
        Squircle,
        Pill,
        Drop,
        Trefoil,
    }

    /// <summary>
    /// Who a character is: its body shape and color, derived only from the workstream id, so a
    /// workstream looks the same in every session and on every device. Identity never shows state,
    /// and its hues keep away from the colors state cues use: amber for needs you, red for failed
    /// and green for a finished turn.
    /// </summary>
    public sealed class CharacterIdentity
    {
        /// <summary>The identity hues in degrees: teal, cyan, azure, blue, indigo, violet, purple and pink.</summary>
        public static readonly IReadOnlyList<double> Hues = new[] { 175.0, 197.0, 219.0, 241.0, 263.0, 285.0, 307.0, 329.0 };

        /// <summary>The hues of the state cues, which identity hues avoid.</summary>
        public static readonly IReadOnlyList<double> StateHues = new[] { NeedsYouHue, FailedHue, FinishedHue };

        /// <summary>The amber of a character that needs its person.</summary>
        public const double NeedsYouHue = 36.0;

        /// <summary>The red of a failed character.</summary>
        public const double FailedHue = 4.0;

        /// <summary>The green of a finished turn.</summary>
        public const double FinishedHue = 147.0;

        private const uint ShapeCount = 8;
        private const uint HueCount = 8;

        private CharacterIdentity(BodyShape shape, double hue, double saturation, double value, double phase)
        {
            Shape = shape;
            Hue = hue;
            Saturation = saturation;
            Value = value;
            Phase = phase;
        }

        public BodyShape Shape { get; }

        /// <summary>The body's hue in degrees, one of <see cref="Hues"/>.</summary>
        public double Hue { get; }

        /// <summary>The body color's saturation, from 0 to 1: a deep or a light tone of the hue.</summary>
        public double Saturation { get; }

        /// <summary>The body color's brightness, from 0 to 1.</summary>
        public double Value { get; }

        /// <summary>
        /// Where the character's motion starts in its cycle, from 0 to 1, so characters in the same
        /// state do not move in step.
        /// </summary>
        public double Phase { get; }

        /// <summary>The identity of the workstream with this id. The same id always gives the same identity.</summary>
        public static CharacterIdentity Of(string workstreamId)
        {
            if (workstreamId == null) throw new ArgumentNullException(nameof(workstreamId));
            var hash = Hash(workstreamId);
            var shape = (BodyShape)(hash % ShapeCount);
            var hue = Hues[(int)(hash / ShapeCount % HueCount)];
            var light = hash / (ShapeCount * HueCount) % 2 == 1;
            var phase = (hash >> 16) / 65536.0;
            return light
                ? new CharacterIdentity(shape, hue, 0.42, 0.98, phase)
                : new CharacterIdentity(shape, hue, 0.62, 0.92, phase);
        }

        /// <summary>
        /// FNV-1a over the id's UTF-8 bytes, then MurmurHash3's finalizer so that ids differing only
        /// in their last characters, as time-ordered ids do, still spread over every identity. The
        /// function is fixed: changing it would change the look of every workstream.
        /// </summary>
        private static uint Hash(string text)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var b in Encoding.UTF8.GetBytes(text))
                {
                    hash ^= b;
                    hash *= 16777619u;
                }
                hash ^= hash >> 16;
                hash *= 0x85ebca6bu;
                hash ^= hash >> 13;
                hash *= 0xc2b2ae35u;
                hash ^= hash >> 16;
                return hash;
            }
        }
    }
}

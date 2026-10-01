#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>A colour as authored, in sRGB, 0 to 255 a channel.</summary>
    public readonly struct GlazeColor : IEquatable<GlazeColor>
    {
        public GlazeColor(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public byte R { get; }

        public byte G { get; }

        public byte B { get; }

        /// <summary>From 0xRRGGBB.</summary>
        public static GlazeColor Hex(uint rgb) => new GlazeColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

        /// <summary>The relative luminance WCAG 2 defines, from 0 for black to 1 for white.</summary>
        public double Luminance => 0.2126 * Linear(R) + 0.7152 * Linear(G) + 0.0722 * Linear(B);

        /// <summary>The WCAG 2 contrast ratio between two colours, from 1 to 21, whichever is lighter.</summary>
        public static double Contrast(GlazeColor a, GlazeColor b)
        {
            var lighter = Math.Max(a.Luminance, b.Luminance);
            var darker = Math.Min(a.Luminance, b.Luminance);
            return (lighter + 0.05) / (darker + 0.05);
        }

        private static double Linear(byte channel)
        {
            var c = channel / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        public bool Equals(GlazeColor other) => R == other.R && G == other.G && B == other.B;

        public override bool Equals(object? obj) => obj is GlazeColor other && Equals(other);

        public override int GetHashCode() => (R << 16) | (G << 8) | B;

        public override string ToString() => "#" + R.ToString("X2") + G.ToString("X2") + B.ToString("X2");
    }

    /// <summary>
    /// What a colour means. Every tone has one meaning, and colour never carries it alone: a word, an
    /// icon or an edge always comes with it (docs/internal/decisions/0023-the-headset-interface-is-one-system-of-tokens-and-components.md).
    /// </summary>
    public enum GlazeTone
    {
        /// <summary>At rest: not started, stopped.</summary>
        Neutral,

        /// <summary>The work is moving: starting, working, checking its work.</summary>
        Active,

        /// <summary>Waiting for you, and only that.</summary>
        Attention,

        /// <summary>A round finished, or the agent confirmed what was sent.</summary>
        Success,

        /// <summary>Something went wrong: it couldn't finish, its checks failed, a request was refused.</summary>
        Failure,

        /// <summary>Halcyonic can't tell what the work is doing.</summary>
        Unknown,

        /// <summary>Practice, demo and recorded work, never real.</summary>
        Simulated,

        /// <summary>Where the person can act: the primary action, a selection, focus.</summary>
        Accent,
    }

    /// <summary>
    /// A tone's colours: its text and icons on a dark surface, the soft container behind them, and
    /// the solid fill with the text that goes on it.
    /// </summary>
    public readonly struct GlazeToneColors
    {
        public GlazeToneColors(GlazeColor foreground, GlazeColor container, GlazeColor strong, GlazeColor onStrong)
        {
            Foreground = foreground;
            Container = container;
            Strong = strong;
            OnStrong = onStrong;
        }

        public GlazeColor Foreground { get; }

        public GlazeColor Container { get; }

        public GlazeColor Strong { get; }

        public GlazeColor OnStrong { get; }
    }

    /// <summary>
    /// The Glaze interface's tokens (ADR 0023): every colour, size, radius, depth and duration the
    /// headset's interface uses, once, so every surface reads them from here. Sizes are angles at the
    /// eye, because panels and labels keep their angular size at any distance; one of Meta's dp is
    /// 0.0625 degrees (its 48 dp hit target is 3 degrees at 0.42 m). Engine-free, so the client core's
    /// tests hold the colours to their contrast and the sizes to Meta's minimums.
    /// </summary>
    public static class Glaze
    {
        /// <summary>One of Meta's dp as an angle at the eye.</summary>
        public const float DegreesPerDp = 0.0625f;

        // Surfaces: dark, but no darker than Meta's #1A1A1A, below which the Quest's display shows no detail.

        /// <summary>Every panel and label plate.</summary>
        public static readonly GlazeColor Panel = GlazeColor.Hex(0x1B222D);

        /// <summary>Rows, cards and choices inside a panel.</summary>
        public static readonly GlazeColor Raised = GlazeColor.Hex(0x242D3A);

        /// <summary>Secondary buttons at rest.</summary>
        public static readonly GlazeColor Control = GlazeColor.Hex(0x303C4E);

        public static readonly GlazeColor ControlHover = GlazeColor.Hex(0x3B4A61);

        public static readonly GlazeColor ControlPressed = GlazeColor.Hex(0x46566F);

        /// <summary>Fields, tab tracks and command text, sunk into a panel.</summary>
        public static readonly GlazeColor Well = GlazeColor.Hex(0x151B24);

        // Text.

        public static readonly GlazeColor Text = GlazeColor.Hex(0xEEF2F6);

        public static readonly GlazeColor TextSecondary = GlazeColor.Hex(0xAAB5C2);

        public static readonly GlazeColor TextDisabled = GlazeColor.Hex(0x808C9B);

        /// <summary>Outlines of neutral things: 3:1 or more on a panel and on a raised row.</summary>
        public static readonly GlazeColor Outline = GlazeColor.Hex(0x73839A);

        /// <summary>A tone's colours.</summary>
        public static GlazeToneColors Tone(GlazeTone tone) => tone switch
        {
            GlazeTone.Neutral => new GlazeToneColors(TextSecondary, Raised, Control, Text),
            GlazeTone.Active => new GlazeToneColors(GlazeColor.Hex(0xABDEF3), GlazeColor.Hex(0x173044), GlazeColor.Hex(0x8FD0EC), GlazeColor.Hex(0x0B1422)),
            GlazeTone.Attention => new GlazeToneColors(GlazeColor.Hex(0xF7CF70), GlazeColor.Hex(0x3D3216), GlazeColor.Hex(0xF3C04F), GlazeColor.Hex(0x1C1404)),
            GlazeTone.Success => new GlazeToneColors(GlazeColor.Hex(0x8ADBB1), GlazeColor.Hex(0x183528), GlazeColor.Hex(0x6CCB9A), GlazeColor.Hex(0x08170F)),
            GlazeTone.Failure => new GlazeToneColors(GlazeColor.Hex(0xFF9F94), GlazeColor.Hex(0x3F1D1A), GlazeColor.Hex(0xEE7F73), GlazeColor.Hex(0x1F0806)),
            GlazeTone.Unknown => new GlazeToneColors(GlazeColor.Hex(0xB7C0CB), GlazeColor.Hex(0x262D36), GlazeColor.Hex(0xB7C0CB), GlazeColor.Hex(0x1B222D)),
            GlazeTone.Simulated => new GlazeToneColors(GlazeColor.Hex(0xCBBBF6), GlazeColor.Hex(0x2B2442), GlazeColor.Hex(0xCBBBF6), GlazeColor.Hex(0x1B222D)),
            GlazeTone.Accent => new GlazeToneColors(GlazeColor.Hex(0xA3C3FF), GlazeColor.Hex(0x22324F), GlazeColor.Hex(0x7FA9F8), GlazeColor.Hex(0x0B1422)),
            _ => throw new ArgumentOutOfRangeException(nameof(tone), tone, "Unhandled tone."),
        };

        // Type: the em's angle at the eye. Meta asks for 14 dp at least and 18 or more to read comfortably.

        /// <summary>Panel titles (24 dp).</summary>
        public const float DisplayDegrees = 1.5f;

        /// <summary>A question heading its answer; a task's title on the stage (20 dp).</summary>
        public const float TitleDegrees = 1.25f;

        /// <summary>Everything a person reads (18 dp).</summary>
        public const float BodyDegrees = 1.125f;

        /// <summary>State badges, counts and tags (16 dp).</summary>
        public const float BadgeDegrees = 1f;

        /// <summary>Supporting lines, and the smallest text anywhere (15 dp).</summary>
        public const float CaptionDegrees = 0.9375f;

        /// <summary>Meta's minimum for any text (14 dp).</summary>
        public const float MinimumTextDegrees = 0.875f;

        // Targets: Meta asks for 60 dp primary hand targets, never under 48 dp, and 12 mm between targets.

        public const float TargetDegrees = 3.75f;

        public const float MinimumTargetDegrees = 3f;

        public const float TargetGapMeters = 0.012f;

        // Shape, as angles at the eye.

        public const float ButtonRadiusDegrees = 0.75f;

        public const float RowRadiusDegrees = 1f;

        public const float PanelRadiusDegrees = 1.5f;

        /// <summary>A task's title plate on the stage.</summary>
        public const float PlateRadiusDegrees = 0.7f;

        /// <summary>How opaque label plates are: enough that a bright room behind one cannot wash out its text.</summary>
        public const float PlateOpacity = 0.96f;

        // Motion, in seconds. Nothing bounces or overshoots, and only Waiting for you breathes.

        /// <summary>A badge changing state cross-fades this long.</summary>
        public const float StateSeconds = 0.25f;

        /// <summary>The Waiting for you badge's slow breath, one cycle.</summary>
        public const float AttentionBreathSeconds = 2.4f;

        /// <summary>How much brighter the Waiting for you badge gets at the top of its breath.</summary>
        public const float AttentionBreathDepth = 0.06f;

        /// <summary>One turn of a busy icon (Starting, Working).</summary>
        public const float BusyTurnSeconds = 1.2f;

        public const float AppearSeconds = 0.2f;

        public const float LeaveSeconds = 0.15f;

        public const float PressSeconds = 0.08f;

        public const float ReleaseSeconds = 0.1f;

        /// <summary>Hover labels and tooltips stay this long after the hand leaves (Meta).</summary>
        public const float LingerSeconds = 0.5f;

        /// <summary>The size in meters an angle spans at a distance, as a label of that angular size there.</summary>
        public static float MetersAt(float degrees, float distance) => distance * MathF.Tan(degrees * MathF.PI / 180f);

        /// <summary>The angle at the eye of a size in meters at a distance.</summary>
        public static float DegreesOf(float meters, float distance) => MathF.Atan2(meters, distance) * 180f / MathF.PI;
    }
}

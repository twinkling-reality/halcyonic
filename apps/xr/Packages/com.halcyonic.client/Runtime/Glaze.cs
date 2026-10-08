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

        /// <summary>This colour drawn <paramref name="opacity"/> opaque over <paramref name="under"/>, as a display blends them.</summary>
        public GlazeColor Over(GlazeColor under, double opacity)
        {
            byte Blend(byte top, byte bottom) => (byte)Math.Round(top * opacity + bottom * (1 - opacity));
            return new GlazeColor(Blend(R, under.R), Blend(G, under.G), Blend(B, under.B));
        }
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

        public static readonly GlazeColor TextDisabled = GlazeColor.Hex(0x8A96A5);

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

        /// <summary>A state badge's height, its word and the room round it.</summary>
        public const float BadgeHeightDegrees = 1.75f;

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

        // Motion, in seconds (ADR 0027): only what answers the person or shows a wait moves, nothing bounces
        // or overshoots, and "Keep things still" stops everything that keeps moving on its own.

        /// <summary>A badge changing state cross-fades this long.</summary>
        public const float StateSeconds = 0.25f;

        /// <summary>The Waiting for you badge's slow breath, one cycle.</summary>
        public const float AttentionBreathSeconds = 2.4f;

        /// <summary>How much brighter the Waiting for you badge gets at the top of its breath.</summary>
        public const float AttentionBreathDepth = 0.06f;

        /// <summary>One turn of a busy icon (Starting, Working).</summary>
        public const float BusyTurnSeconds = 1.2f;

        /// <summary>What arrives, as a column coming back from stepping aside or each part of a column opening, eases out (<see cref="EaseOut"/>) this long.</summary>
        public const float AppearSeconds = 0.2f;

        /// <summary>What leaves, as a column stepping aside, eases in (<see cref="EaseIn"/>) this long: quicker than what arrives.</summary>
        public const float LeaveSeconds = 0.15f;

        /// <summary>
        /// A part moving between two places on the plane, as the plane re-centres, eases in and out
        /// (<see cref="EaseInOut"/>) this long: the longest move, so a slide's time ends every move.
        /// </summary>
        public const float SlideSeconds = 0.25f;

        public const float PressSeconds = 0.08f;

        public const float ReleaseSeconds = 0.1f;

        /// <summary>Hover labels and tooltips stay this long after the hand leaves (Meta).</summary>
        public const float LingerSeconds = 0.5f;

        /// <summary>One sweep of a wait's shimmer across its words, and the pause before the next (ADR 0027).</summary>
        public const float ShimmerSeconds = 1.6f;

        /// <summary>The shimmer's bright band, as a share of the words' width, soft at both edges.</summary>
        public const float ShimmerWidth = 0.35f;

        /// <summary>How far the shimmer lifts the words toward white at the band's middle: brighter, never moved.</summary>
        public const float ShimmerDepth = 0.55f;

        /// <summary>One pulse of Hold to talk's microphone while it listens.</summary>
        public const float ListeningPulseSeconds = 1.0f;

        /// <summary>How much the listening microphone grows at the top of its pulse, as a share of its size.</summary>
        public const float ListeningPulseDepth = 0.18f;

        /// <summary>A light line drawing on from its character, as a file opens, eased out (ADR 0027).</summary>
        public const float DrawSeconds = 0.18f;

        /// <summary>Between one part of a column and the next as it assembles, top to bottom: subject, sections, page.</summary>
        public const float StaggerSeconds = 0.03f;

        /// <summary>How much of a file's light line has drawn when its first part starts to appear.</summary>
        public const float PartsAfterDraw = 0.55f;

        /// <summary>How much of what leaves the light line takes to draw back to its character, so it is gone before the file is.</summary>
        public const float UndrawShare = 0.9f;

        /// <summary>
        /// When part <paramref name="index"/> of a column opening starts to appear, in seconds from the
        /// opening's start: after <see cref="PartsAfterDraw"/> of the light line where one draws from a
        /// character (<paramref name="drawn"/>), else at once, each part <see cref="StaggerSeconds"/> after the last.
        /// </summary>
        public static float PartStarts(int index, bool drawn) => (drawn ? PartsAfterDraw * DrawSeconds : 0f) + index * StaggerSeconds;

        /// <summary>How visible part <paramref name="index"/> of a column opening is, <paramref name="seconds"/> into its opening: it fades in over <see cref="AppearSeconds"/>, eased out.</summary>
        public static float PartShown(float seconds, int index, bool drawn) => EaseOut((seconds - PartStarts(index, drawn)) / AppearSeconds);

        /// <summary>How long a column of <paramref name="parts"/> parts takes to open: until its last part shows whole.</summary>
        public static float OpeningSeconds(int parts, bool drawn) => PartStarts(Math.Max(parts, 1) - 1, drawn) + AppearSeconds;

        /// <summary>How much of a file's light line has drawn, <paramref name="seconds"/> into its opening.</summary>
        public static float LineShown(float seconds) => EaseOut(seconds / DrawSeconds);

        /// <summary>How visible what closes still is, <paramref name="seconds"/> into its closing: everything together, eased in, over <see cref="LeaveSeconds"/>.</summary>
        public static float LeftShown(float seconds) => 1f - EaseIn(seconds / LeaveSeconds);

        /// <summary>
        /// How visible what takes the place of something closing is, as the closed bar where a file stood,
        /// <paramref name="seconds"/> after the close: nothing until what closes has left, over
        /// <see cref="LeaveSeconds"/>, then fading in over <see cref="AppearSeconds"/>, eased out, so the two
        /// never stand over each other.
        /// </summary>
        public static float AfterLeaveShown(float seconds) => seconds <= LeaveSeconds ? 0f : EaseOut((seconds - LeaveSeconds) / AppearSeconds);

        /// <summary>How long what takes the place of something closing takes to show whole: <see cref="LeaveSeconds"/> then <see cref="AppearSeconds"/>.</summary>
        public const float AfterLeaveSeconds = LeaveSeconds + AppearSeconds;

        /// <summary>How much of a closing file's light line still stands, <paramref name="seconds"/> into its closing, drawing back over <see cref="UndrawShare"/> of <see cref="LeaveSeconds"/>.</summary>
        public static float LineLeft(float seconds) => 1f - EaseIn(seconds / (UndrawShare * LeaveSeconds));

        /// <summary>What arrives eases out, fast then settling (ADR 0027): <paramref name="t"/> from 0 to 1.</summary>
        public static float EaseOut(float t)
        {
            t = Clamp01(t);
            var rest = 1f - t;
            return 1f - rest * rest * rest;
        }

        /// <summary>What leaves eases in, slow then gone.</summary>
        public static float EaseIn(float t)
        {
            t = Clamp01(t);
            return t * t * t;
        }

        /// <summary>What moves between two places eases in and out.</summary>
        public static float EaseInOut(float t)
        {
            t = Clamp01(t);
            return t < 0.5f ? 4f * t * t * t : 1f - 4f * (1f - t) * (1f - t) * (1f - t);
        }

        /// <summary>A loop of <paramref name="seconds"/> at time <paramref name="now"/>, from 0 to 1 and back along a sine, which never jerks.</summary>
        public static float Loop(float now, float seconds) =>
            seconds <= 0f ? 0f : 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * (now % seconds) / seconds);

        private static float Clamp01(float t) => t < 0f ? 0f : t > 1f ? 1f : t;

        /// <summary>
        /// The menu's surfaces on one plane facing the eyes (ADR 0026): three sizes of type that only step
        /// down, one 8 dp grid, one corner radius, glass, and one selection treatment. The stage keeps
        /// its own labels' sizes above.
        /// </summary>
        public static class Menu
        {
            /// <summary>The subject, one a column, drawn light (24 dp).</summary>
            public const float TitleDegrees = 1.5f;

            /// <summary>The sections, the content, the prompts, a side panel's fact names, a source line, and the pill's word (18 dp).</summary>
            public const float BodyDegrees = 1.125f;

            /// <summary>Small facts inside a row (15 dp), never a line of its own; a side panel names its facts at <see cref="BodyDegrees"/>.</summary>
            public const float LabelDegrees = 0.9375f;

            /// <summary>The split header's state pill: the same badge as the stage's, its word at the content's size.</summary>
            public const float PillDegrees = BodyDegrees;

            /// <summary>The pill's height: the badge's, grown as its word is.</summary>
            public const float PillHeightDegrees = BadgeHeightDegrees * PillDegrees / BadgeDegrees;

            /// <summary>A subject's plate at its least, and the padding above and below its title in it.</summary>
            public const float SubjectDegrees = 3.2f;

            public const float SubjectPaddingDegrees = 0.75f;

            /// <summary>
            /// How tall a line of text is for each unit of its em: Liberation Sans's line height over its
            /// point size, 98.89 over 86, which the component render holds to the font.
            /// </summary>
            public const float LineSpacing = 1.15f;

            /// <summary>
            /// Each column's width, as an angle at the eyes when centred in front of them
            /// (<see cref="PlaneComposition.Units"/>): the menu's places, a task's file or New project's
            /// steps, and a side panel. One width a kind, wherever it stands, so a page's words wrap the
            /// same alone and beside the menu, and a request shown in parts never loses a word.
            /// </summary>
            public const float MenuColumnDegrees = 32f;

            public const float FileColumnDegrees = 36f;

            public const float SideColumnDegrees = 26f;

            /// <summary>One grid step, 8 dp.</summary>
            public const float GridDegrees = 0.5f;

            /// <summary>Inside a shape, from its edge to its content (24 dp).</summary>
            public const float PaddingDegrees = 1.5f;

            /// <summary>Between groups inside a shape (16 dp).</summary>
            public const float GroupGapDegrees = 1f;

            /// <summary>From a fact's name to its value (8 dp).</summary>
            public const float LabelToValueDegrees = 0.5f;

            /// <summary>The fixed column icons stand in, on the left content line (24 dp).</summary>
            public const float IconColumnDegrees = 1.5f;

            /// <summary>Between the parts of a column: a degree, as between any two things (<see cref="PlaneComposition.PartGapDegrees"/>).</summary>
            public const float PartGapDegrees = PlaneComposition.PartGapDegrees;

            /// <summary>Between columns, in meters on the plane (<see cref="PlaneComposition.ColumnGapMeters"/>).</summary>
            public const float ColumnGapMeters = PlaneComposition.ColumnGapMeters;

            /// <summary>The plane's distance from the eyes, at touch distance.</summary>
            public const float PlaneMeters = PlaneComposition.Distance;

            /// <summary>Every shape's corners.</summary>
            public const float RadiusDegrees = 0.9f;

            /// <summary>How far a shape round words, as an answer or a well, reaches past the content line; its words stay on it.</summary>
            public const float InsetDegrees = 0.7f;

            /// <summary>The glass: the panel colour this opaque, with no blur.</summary>
            public const float GlassOpacity = PlateOpacity;

            /// <summary>The light from the glass's top edge at its brightest, white at this opacity, fading out by <see cref="GlowReach"/> of its height.</summary>
            public const float GlowOpacity = 0.06f;

            public const float GlowReach = 1f / 3f;

            /// <summary>The sheen along the glass's top edge, white at this opacity and this thin.</summary>
            public const float SheenOpacity = 0.2f;

            public const float SheenDegrees = 0.06f;

            /// <summary>The glass's hairline edge, white at this opacity and this thin.</summary>
            public const float HairlineOpacity = 0.12f;

            public const float HairlineDegrees = 0.06f;

            /// <summary>Chosen: the shape lights up, white at this opacity, and gains a crisp white frame at <see cref="LitFrameOpacity"/>.</summary>
            public const float LitFillOpacity = 0.10f;

            public const float LitFrameOpacity = 0.78f;

            /// <summary>Pointed at: the frame alone, fainter.</summary>
            public const float PointedFrameOpacity = 0.42f;

            /// <summary>How thick the lit frame and the pointed frame are drawn.</summary>
            public const float LitFrameDegrees = 0.08f;

            public const float PointedFrameDegrees = 0.06f;

            /// <summary>A plain prompt's cap: an outline, white at this opacity and this thick; the main action's is filled with the accent.</summary>
            public const float CapOutlineOpacity = 0.6f;

            public const float CapOutlineDegrees = 0.07f;

            /// <summary>Round a prompt, before its cap and after its words, so its frame holds both: footers reach this far past the content lines.</summary>
            public const float PromptMarginDegrees = 0.6f;

            /// <summary>A prompt's round key cap, and the icon inside it: a degree, the least any icon is drawn (ADR 0023).</summary>
            public const float PromptCapDegrees = 1.45f;

            public const float PromptIconDegrees = 1f;

            /// <summary>
            /// Quiet words, of a prompt or a line that can't be taken now: the secondary colour, never
            /// <see cref="TextDisabled"/>, which reads only 3.5 to 1 on a chosen shape over a white wall.
            /// </summary>
            public static GlazeColor QuietText => TextSecondary;

            /// <summary>The glass as it reaches the eyes over the brightest room, a white wall in passthrough: what every word on it is held to.</summary>
            public static GlazeColor GlassOverWhite => Panel.Over(GlazeColor.Hex(0xFFFFFF), GlassOpacity);

            /// <summary>A chosen shape's lit fill over the glass over white.</summary>
            public static GlazeColor LitOverWhite => GlazeColor.Hex(0xFFFFFF).Over(GlassOverWhite, LitFillOpacity);
        }

        /// <summary>The size in meters an angle spans at a distance, as a label of that angular size there.</summary>
        public static float MetersAt(float degrees, float distance) => distance * MathF.Tan(degrees * MathF.PI / 180f);

        /// <summary>The angle at the eye of a size in meters at a distance.</summary>
        public static float DegreesOf(float meters, float distance) => MathF.Atan2(meters, distance) * 180f / MathF.PI;
    }
}

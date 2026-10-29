#nullable enable
using System;
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>
    /// The spatial kit's tokens. Its colours, type and motion are the room of projects' (ADR 0040), the instrument's
    /// roles repointed onto that ramp. The instrument below, a candidate that no production view draws yet: one opaque instrument
    /// whose regions follow what their words are. The context spine at the left says where the person
    /// is, the reading slate holds what was said and what the person says, the evidence rail at the
    /// right holds what was recorded, and the same pieces fold into one compact presence. Sizes are
    /// angles at the eye on one plane facing the eyes at the instrument's centre,
    /// <see cref="PlaneComposition.Distance"/> away, and every surface that carries words is opaque,
    /// so these tests can hold each word to its contrast and each size to its floor before anything
    /// is drawn.
    /// </summary>
    public static class SpatialKit
    {
        // Regions: widths as angles at the eyes when centred (PlaneComposition.Units makes them plane units).

        /// <summary>The reading slate: the selected work's identity, what the agent said, and the person's own words.</summary>
        public const float SlateDegrees = 36f;

        /// <summary>The overview's slate, which holds every project and needs no evidence rail beside it.</summary>
        public const float BoardDegrees = 44f;

        /// <summary>The context spine: the switch, the places, and the selected project's tasks.</summary>
        public const float SpineDegrees = 16f;

        /// <summary>The evidence rail: what the agent app was recorded doing, newest last.</summary>
        public const float RailDegrees = 16f;

        /// <summary>The compact presence: the switch, the retained task, its state and what waits.</summary>
        public const float PresenceDegrees = 30f;

        /// <summary>
        /// The tallest the slate may grow at the standard text size, as an angle at the eyes. Its top stays
        /// where it was placed and its height follows its content up to this; reading beyond it pages
        /// instead of shrinking.
        /// </summary>
        public const float SlateMostDegrees = 44f;

        /// <summary>The tallest the slate may grow when reading text is drawn <paramref name="textScale"/> times as large.</summary>
        public static float SlateMostAt(float textScale) => SlateMostDegrees * textScale;

        /// <summary>How far the spine and the rail stand behind the slate, in metres: recessed bands of one instrument, joined to it.</summary>
        public const float RecessMeters = 0.010f;

        /// <summary>Where the instrument rests: the top of its slate this far above eye level.</summary>
        public const float RestingTopDegrees = 4f;

        /// <summary>Where the compact presence rests: low and to the left, clear of where other work stands.</summary>
        public const float PresenceYawDegrees = -22f;

        public const float PresenceElevationDegrees = -28f;

        // Type, in Meta's dp. On the instrument nothing is smaller than Body; the room (ADR 0040) adds Label and Mono
        // for its small surfaces, a moment and a tile, where they still read at the floor.

        /// <summary>The selected work's or the place's name, once an instrument, in the instrument face.</summary>
        public const float IdentityDp = 30f;

        /// <summary>A region's heading, and a project's name on the overview, in the instrument face.</summary>
        public const float HeadingDp = 22f;

        /// <summary>Everything read: prose, rows, facts, chips and controls, in the reading face.</summary>
        public const float BodyDp = 18f;

        /// <summary>A fact beside a subject in the room's small surfaces (a moment, a tile); never a line of its own far out.</summary>
        public const float LabelDp = 15f;

        /// <summary>Code, paths and commands, which are the runtime's, in a monospace face.</summary>
        public const float MonoDp = 16f;

        /// <summary>A picked project's name across its screen while no picture stands: the largest type, once a room.</summary>
        public const float DisplayDp = 48f;

        /// <summary>From one line of reading prose to the next, in ems: room to read a long report.</summary>
        public const float ReadingLeading = 1.4f;

        /// <summary>From one line of a row or a fact to the next, in ems.</summary>
        public const float ListLeading = 1.2f;

        /// <summary>A size in dp as an angle at the eye.</summary>
        public static float Degrees(float dp) => dp * Glaze.DegreesPerDp;

        // Grid: one 8 dp step.

        public const float GridDegrees = 0.5f;

        /// <summary>Inside every region, from its edge to its words.</summary>
        public const float PaddingDegrees = 1.5f;

        /// <summary>Between groups inside a region.</summary>
        public const float GroupGapDegrees = 1f;

        /// <summary>The column an icon stands in, before its words.</summary>
        public const float IconColumnDegrees = 1.75f;

        /// <summary>An icon's em: above the icon atlas's one-degree floor.</summary>
        public const float IconDegrees = 1.25f;

        public const float InstrumentRadiusDegrees = 1.6f;

        public const float ControlRadiusDegrees = 0.9f;

        /// <summary>A thin edge: a region's outline and a control's rest boundary.</summary>
        public const float HairlineDegrees = 0.06f;

        /// <summary>How far across a recessed band the slate's shade reaches where the slate stands over it, in degrees.</summary>
        public const float RecessShadeDegrees = 1.4f;


        /// <summary>
        /// How far the slate stands past its recessed neighbours above and below: it covers their outline where it
        /// crosses them, the one depth cue a flat image keeps.
        /// </summary>
        public const float SlateRiseDegrees = 1f;

        // Targets: Meta's 60 dp hand target, never under 48 dp, and 12 mm between targets.

        public const float TargetDegrees = Glaze.TargetDegrees;

        public const float MinimumTargetDegrees = Glaze.MinimumTargetDegrees;

        /// <summary>The 12 mm between two targets, as an angle at the plane's distance.</summary>
        public static float TargetGapDegrees => Glaze.DegreesOf(Glaze.TargetGapMeters, PlaneComposition.Distance);

        // The connected two-position switch.

        /// <summary>Each position's own target, a whole ordinary target tall.</summary>
        public const float SwitchCellDegrees = TargetDegrees;

        /// <summary>Between the track's edge and the thumb inside it.</summary>
        public const float SwitchInsetDegrees = 0.3f;

        /// <summary>How long the thumb takes to reach a confirmed position; Still makes it immediate.</summary>
        public const float SwitchSeconds = 0.18f;

        /// <summary>
        /// The track's height: two whole targets and the 12 mm between them, which the thumb crosses but
        /// no press lands in, and the inset round them.
        /// </summary>
        public static float SwitchTrackDegrees => 2f * SwitchCellDegrees + TargetGapDegrees + 2f * SwitchInsetDegrees;

        // Carriers. Every surface that carries words is opaque: nothing of the room shows through a word.

        /// <summary>The reading slate, nearest the person: the room's carrier.</summary>
        public static GlazeColor Slate => Carrier;

        /// <summary>The recessed spine and rail: the room's well step, below the carrier.</summary>
        public static GlazeColor Side => Well;

        /// <summary>n2: a chip, a secondary control, and the lift under what is chosen.</summary>
        public static GlazeColor Raised => GlazeColor.Hex(0x272D37);

        /// <summary>n0: the person's own words sunk into the slate, code, a screen's edge.</summary>
        public static GlazeColor Well => GlazeColor.Hex(0x1B1E24);

        /// <summary>The switch's thumb, raised above its track.</summary>
        public static GlazeColor Thumb => Control;

        /// <summary>
        /// A quiet control's outline, an unavailable control's, and the trail's line: drawn opaque so it blends the same
        /// in any colour space. Regions carry no outline: tone, depth and the slate's shade on its neighbours separate them.
        /// </summary>
        public static GlazeColor Edge => Hairline;

        /// <summary>The slate's lit top edge: the one light the instrument carries.</summary>
        public static GlazeColor EdgeLight => TextTertiary;

        /// <summary>n7: primary words.</summary>
        public static GlazeColor Text => GlazeColor.Hex(0xEEF1F5);

        /// <summary>n6: secondary words.</summary>
        public static GlazeColor TextSecondary => GlazeColor.Hex(0xB7C0CC);

        // The room's ramp (ADR 0040): eight opaque steps by role, one accent, and colour only where it means something.

        /// <summary>n1: every surface in the room that carries words.</summary>
        public static GlazeColor Carrier => GlazeColor.Hex(0x1F242C);

        /// <summary>n3: a button's fill; what the pointer rests on.</summary>
        public static GlazeColor Control => GlazeColor.Hex(0x363E4B);

        /// <summary>n4: a hairline and the dotted gutter of code still only reported.</summary>
        public static GlazeColor Hairline => GlazeColor.Hex(0x4A5361);

        /// <summary>n5: a time, a count.</summary>
        public static GlazeColor TextTertiary => GlazeColor.Hex(0xA4AEBB);

        /// <summary>Kingfisher: the one primary action, and live work.</summary>
        public static GlazeColor Accent => GlazeColor.Hex(0x5BC4EE);

        /// <summary>Words on a coloured fill.</summary>
        public static GlazeColor AccentInk => GlazeColor.Hex(0x0A1B24);

        /// <summary>Needs the person: a question, an approval.</summary>
        public static GlazeColor Attention => GlazeColor.Hex(0xF2B54E);

        /// <summary>Failed.</summary>
        public static GlazeColor Failure => GlazeColor.Hex(0xF98F88);

        /// <summary>Passed, and lines added in a checkpoint diff.</summary>
        public static GlazeColor Passed => GlazeColor.Hex(0x62CF8F);

        /// <summary>The band under a line a checkpoint diff shows added.</summary>
        public static GlazeColor AddedBand => GlazeColor.Hex(0x1B3324);

        /// <summary>A pressed primary action: the accent deepened, mixed in linear light 18 percent toward black.</summary>
        public static GlazeColor PressedAccent => GlazeColor.Hex(0x53B3DA);

        /// <summary>A pointed primary action: the accent lifted, mixed in linear light 16 percent toward white.</summary>
        public static GlazeColor HoveredAccent => GlazeColor.Hex(0x88CFF1);

        /// <summary>
        /// A project's identity field: its screen's face while no picture stands, in the project's own hue (0 to 360) at a
        /// low chroma and the carrier's darkness, so it reads as whose screen it is and never as a meaning.
        /// </summary>
        public static GlazeColor IdentityField(double hue)
        {
            const double value = 0.17, saturation = 0.3;
            var h = ((hue % 360) + 360) % 360 / 60.0;
            var chroma = value * saturation;
            var x = chroma * (1 - Math.Abs(h % 2 - 1));
            var (r, g, b) = h < 1 ? (chroma, x, 0.0) : h < 2 ? (x, chroma, 0.0) : h < 3 ? (0.0, chroma, x)
                : h < 4 ? (0.0, x, chroma) : h < 5 ? (x, 0.0, chroma) : (chroma, 0.0, x);
            var m = value - chroma;
            byte Channel(double c) => (byte)Math.Round((c + m) * 255);
            return new GlazeColor(Channel(r), Channel(g), Channel(b));
        }

        // The room's parts (ADR 0040), as angles at the eye; where each stands is the placement's, never the part's.

        /// <summary>A picked project's screen, across.</summary>
        public const float ScreenDegrees = 56f;

        /// <summary>The screen's width over its height: a film's 16 by 9.</summary>
        public const float ScreenAspect = 16f / 9f;

        /// <summary>The screen's height, from its width and aspect.</summary>
        public static float ScreenHeightDegrees => ScreenDegrees / ScreenAspect;

        /// <summary>The bar beneath the screen, high: a whole target and the padding round it. Its width is the screen's.</summary>
        public const float BarDegrees = 5.4f;

        /// <summary>The bar folded while nothing is wanted: a handle this long and this thin.</summary>
        public const float HandleDegrees = 6f;

        public const float HandleThicknessDegrees = 0.45f;

        /// <summary>The moment slot beside the character, across; a moment's height follows its content.</summary>
        public const float MomentDegrees = 23f;

        /// <summary>The stacked moments behind the shown one: one edge this high above its top.</summary>
        public const float StackEdgeDegrees = 1.2f;

        // Motion (ADR 0040), only on events; Keep things still lands each state at once.

        /// <summary>A moment coming out of the character: a spring this long with <see cref="PopBounce"/>.</summary>
        public const float PopSeconds = 0.5f;

        /// <summary>The spring's bounce: a small overshoot, under Apple's caution of 0.4.</summary>
        public const float PopBounce = 0.15f;

        /// <summary>Read it turning a moment into the reading surface, on <see cref="UnfoldCurve"/>.</summary>
        public const float UnfoldSeconds = 0.5f;

        /// <summary>Fold returning it, on <see cref="FoldCurve"/>: leaving is quicker than arriving.</summary>
        public const float FoldSeconds = 0.25f;

        /// <summary>A tile growing into the screen, a part moving, on <see cref="MoveCurve"/>.</summary>
        public const float MoveSeconds = 0.6f;

        /// <summary>One breath of an idle character.</summary>
        public const float BreathSeconds = 3.2f;

        /// <summary>Material 3's emphasized decelerate, as a cubic Bezier's two control points.</summary>
        public static (float X1, float Y1, float X2, float Y2) UnfoldCurve => (0.05f, 0.7f, 0.1f, 1f);

        /// <summary>Material 3's emphasized accelerate.</summary>
        public static (float X1, float Y1, float X2, float Y2) FoldCurve => (0.3f, 0f, 0.8f, 0.15f);

        /// <summary>Material 3's standard.</summary>
        public static (float X1, float Y1, float X2, float Y2) MoveCurve => (0.2f, 0f, 0f, 1f);

        /// <summary>Every carrier the kit lays words on (the recessed <see cref="Side"/> is the well's step).</summary>
        public static IReadOnlyList<GlazeColor> Carriers { get; } = new[] { Slate, Raised, Well, Thumb };

        /// <summary>
        /// How large text of <paramref name="dp"/> reads as the eyes see it at a point on the plane
        /// <paramref name="right"/> across and <paramref name="up"/> up from where the plane faces them, in
        /// units of its distance (<see cref="PlaneComposition.ShrinkAt"/>).
        /// </summary>
        public static float SeenDegrees(float dp, float right, float up) => Degrees(dp) * PlaneComposition.ShrinkAt(right, up);

        /// <summary>Whether text of <paramref name="dp"/> at that point still reads at Meta's 14 dp floor.</summary>
        public static bool Reads(float dp, float right, float up) => SeenDegrees(dp, right, up) >= Glaze.MinimumTextDegrees;

        /// <summary>
        /// The working instrument's half-width as laid out, in units of the plane's distance: the spine, the
        /// slate and the rail joined, centred on the eyes' line. On the curve it is also the half-spread, in radians.
        /// </summary>
        public static float WorkHalfWidth => (PlaneComposition.Units(SpineDegrees) + PlaneComposition.Units(SlateDegrees) + PlaneComposition.Units(RailDegrees)) / 2f;

        /// <summary>The overview's half-width as laid out: the spine and the board, joined.</summary>
        public static float OverviewHalfWidth => (PlaneComposition.Units(SpineDegrees) + PlaneComposition.Units(BoardDegrees)) / 2f;

        /// <summary>The tallest instrument's half-height on its plane at a text scale.</summary>
        public static float MostHalfHeight(float textScale) => PlaneComposition.Units(SlateMostAt(textScale)) / 2f;

        /// <summary>A plane extent's angle at the eyes, from its centre to its edge.</summary>
        public static float DegreesTo(float units) => MathF.Atan(units) * 180f / MathF.PI;
    }
}

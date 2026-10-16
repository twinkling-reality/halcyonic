#nullable enable
using System;
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>
    /// The spatial kit's tokens, a candidate that no production view draws yet: one opaque instrument
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
        public const float SpineDegrees = 15f;

        /// <summary>The evidence rail: what the agent app was recorded doing, newest last.</summary>
        public const float RailDegrees = 15f;

        /// <summary>The compact presence: the switch, the retained task, its state and what waits.</summary>
        public const float PresenceDegrees = 30f;

        /// <summary>
        /// The tallest the slate may grow at the standard text size, as an angle at the eyes. Its top stays
        /// where it was placed and its height follows its content up to this; reading beyond it pages
        /// instead of shrinking.
        /// </summary>
        public const float SlateMostDegrees = 43f;

        /// <summary>The tallest the slate may grow when reading text is drawn <paramref name="textScale"/> times as large.</summary>
        public static float SlateMostAt(float textScale) => SlateMostDegrees * textScale;

        /// <summary>How far the spine and the rail stand behind the slate, in metres, parallel to it: one instrument at two depths.</summary>
        public const float RecessMeters = 0.010f;

        /// <summary>
        /// Between the slate and each region beside it, in units of the plane's distance: STYLE's 15 mm between
        /// columns (<see cref="PlaneComposition.ColumnGapMeters"/>), where the room shows through.
        /// </summary>
        public static float ColumnGapUnits => PlaneComposition.ColumnGapMeters / PlaneComposition.Distance;

        /// <summary>Where the instrument rests: the top of its slate this far above eye level.</summary>
        public const float RestingTopDegrees = 4f;

        /// <summary>Where the compact presence rests: low and to the left, clear of where other work stands.</summary>
        public const float PresenceYawDegrees = -22f;

        public const float PresenceElevationDegrees = -28f;

        // Type, in Meta's dp. Three sizes and one reading size: nothing anywhere is smaller than Body.

        /// <summary>The selected work's or the place's name, once an instrument, in the instrument face.</summary>
        public const float IdentityDp = 30f;

        /// <summary>A region's heading, and a project's name on the overview, in the instrument face.</summary>
        public const float HeadingDp = 22f;

        /// <summary>Everything read: prose, rows, facts, chips and controls, in the reading face.</summary>
        public const float BodyDp = 18f;

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

        /// <summary>The reading slate, nearest the person.</summary>
        public static GlazeColor Slate => GlazeColor.Hex(0x202936);

        /// <summary>The recessed spine and rail.</summary>
        public static GlazeColor Side => GlazeColor.Hex(0x171C23);

        /// <summary>A chip, a secondary control, and the lift under what is chosen.</summary>
        public static GlazeColor Raised => GlazeColor.Hex(0x2D3746);

        /// <summary>The person's own words, sunk into the slate.</summary>
        public static GlazeColor Well => GlazeColor.Hex(0x171D25);

        /// <summary>The switch's thumb, raised above its track.</summary>
        public static GlazeColor Thumb => GlazeColor.Hex(0x3D4859);

        /// <summary>
        /// A quiet control's outline, an unavailable control's, and the trail's line: drawn opaque so it blends the same
        /// in any colour space. Regions carry no outline: tone, depth and the slate's shade on its neighbours separate them.
        /// </summary>
        public static GlazeColor Edge => GlazeColor.Hex(0x3D4858);

        /// <summary>The slate's lit top edge: the one light the instrument carries.</summary>
        public static GlazeColor EdgeLight => GlazeColor.Hex(0x6B7889);

        public static GlazeColor Text => GlazeColor.Hex(0xEEF2F6);

        public static GlazeColor TextSecondary => GlazeColor.Hex(0xB0BBC8);

        /// <summary>Every carrier the kit lays words on.</summary>
        public static IReadOnlyList<GlazeColor> Carriers { get; } = new[] { Slate, Side, Raised, Well, Thumb };

        /// <summary>
        /// How large text of <paramref name="dp"/> reads as the eyes see it at a point on the plane
        /// <paramref name="right"/> across and <paramref name="up"/> up from where the plane faces them, in
        /// units of its distance (<see cref="PlaneComposition.ShrinkAt"/>).
        /// </summary>
        public static float SeenDegrees(float dp, float right, float up) => Degrees(dp) * PlaneComposition.ShrinkAt(right, up);

        /// <summary>Whether text of <paramref name="dp"/> at that point still reads at Meta's 14 dp floor.</summary>
        public static bool Reads(float dp, float right, float up) => SeenDegrees(dp, right, up) >= Glaze.MinimumTextDegrees;

        /// <summary>
        /// The working instrument's half-width on its plane, in units of the plane's distance: the spine,
        /// the slate and the rail side by side, a column's gap between each, centred on the eyes' line.
        /// </summary>
        public static float WorkHalfWidth => (PlaneComposition.Units(SpineDegrees) + PlaneComposition.Units(SlateDegrees) + PlaneComposition.Units(RailDegrees)) / 2f + ColumnGapUnits;

        /// <summary>The overview's half-width on its plane: the spine and the board, a column's gap between them.</summary>
        public static float OverviewHalfWidth => (PlaneComposition.Units(SpineDegrees) + PlaneComposition.Units(BoardDegrees) + ColumnGapUnits) / 2f;

        /// <summary>The tallest instrument's half-height on its plane at a text scale.</summary>
        public static float MostHalfHeight(float textScale) => PlaneComposition.Units(SlateMostAt(textScale)) / 2f;

        /// <summary>A plane extent's angle at the eyes, from its centre to its edge.</summary>
        public static float DegreesTo(float units) => MathF.Atan(units) * 180f / MathF.PI;
    }
}

using System;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The spatial kit's tokens hold every word to its contrast and every size to its floor.</summary>
public class SpatialKitTests
{
    private static readonly GlazeColor DarkestShown = GlazeColor.Hex(0x1A1A1A);

    private static readonly GlazeTone[] StateTones =
        { GlazeTone.Neutral, GlazeTone.Active, GlazeTone.Attention, GlazeTone.Success, GlazeTone.Failure, GlazeTone.Unknown, GlazeTone.Simulated, GlazeTone.Accent };

    [Test]
    public void EveryWordReadsOnEveryCarrier()
    {
        foreach (var carrier in SpatialKit.Carriers)
        {
            Assert.That(GlazeColor.Contrast(SpatialKit.Text, carrier), Is.GreaterThanOrEqualTo(7), $"text on {carrier}");
            Assert.That(GlazeColor.Contrast(SpatialKit.TextSecondary, carrier), Is.GreaterThanOrEqualTo(4.5), $"secondary text on {carrier}");
            foreach (var tone in StateTones)
            {
                Assert.That(GlazeColor.Contrast(Glaze.Tone(tone).Foreground, carrier), Is.GreaterThanOrEqualTo(4.5), $"{tone} words on {carrier}");
            }
        }
    }

    [Test]
    public void ToneContainersCarryTheirWords()
    {
        foreach (var tone in StateTones)
        {
            var colours = Glaze.Tone(tone);
            Assert.That(GlazeColor.Contrast(colours.Foreground, colours.Container), Is.GreaterThanOrEqualTo(4.5), $"{tone} words in its container");
            Assert.That(GlazeColor.Contrast(colours.OnStrong, colours.Strong), Is.GreaterThanOrEqualTo(4.5), $"words on {tone}'s fill");
        }
    }

    [Test]
    public void NoCarrierIsDarkerThanTheDisplayShows()
    {
        foreach (var carrier in SpatialKit.Carriers)
        {
            Assert.That(carrier.Luminance, Is.GreaterThanOrEqualTo(DarkestShown.Luminance), $"{carrier} is darker than {DarkestShown}");
            Assert.That(Math.Min(carrier.R, Math.Min(carrier.G, carrier.B)), Is.GreaterThan(13), $"{carrier} has a channel the display cannot separate");
        }
    }

    [Test]
    public void OpaqueCarriersHoldTheirContrastOverTheBrightestRoom()
    {
        // A carrier drawn whole lets nothing of a white wall through, so a word keeps its contrast over it.
        foreach (var carrier in SpatialKit.Carriers)
        {
            var overWhite = carrier.Over(GlazeColor.Hex(0xFFFFFF), 1.0);
            Assert.That(overWhite, Is.EqualTo(carrier), $"{carrier} lets the room through");
            Assert.That(GlazeColor.Contrast(SpatialKit.Text, overWhite), Is.GreaterThanOrEqualTo(7), $"text on {carrier} over a white wall");
        }
    }

    [Test]
    public void EdgesShowAgainstTheirRegionsButStayQuieterThanWords()
    {
        foreach (var region in new[] { SpatialKit.Slate, SpatialKit.Side })
        {
            Assert.That(GlazeColor.Contrast(SpatialKit.Edge, region), Is.GreaterThanOrEqualTo(1.5), $"edge against {region}");
            Assert.That(GlazeColor.Contrast(SpatialKit.Edge, region), Is.LessThan(GlazeColor.Contrast(SpatialKit.TextSecondary, region)), $"edge louder than words on {region}");
        }
        Assert.That(GlazeColor.Contrast(SpatialKit.EdgeLight, SpatialKit.Slate), Is.GreaterThan(GlazeColor.Contrast(SpatialKit.Edge, SpatialKit.Slate)), "the lit edge reads as the lit one");
        Assert.That(GlazeColor.Contrast(SpatialKit.Thumb, SpatialKit.Side), Is.GreaterThanOrEqualTo(1.5), "the thumb stands off its track");
    }

    [Test]
    public void ThreeSizesWithBodyTheSmallest()
    {
        Assert.That(SpatialKit.Degrees(SpatialKit.BodyDp), Is.EqualTo(Glaze.BodyDegrees).Within(1e-6), "the reading size is Meta's comfortable 18 dp");
        Assert.That(SpatialKit.HeadingDp, Is.GreaterThan(SpatialKit.BodyDp));
        Assert.That(SpatialKit.IdentityDp, Is.GreaterThan(SpatialKit.HeadingDp));
        Assert.That(SpatialKit.Degrees(SpatialKit.BodyDp), Is.GreaterThan(Glaze.MinimumTextDegrees));
        Assert.That(SpatialKit.IconDegrees, Is.GreaterThanOrEqualTo(1f), "an icon is never drawn under a degree");
        Assert.That(SpatialKit.ReadingLeading, Is.GreaterThanOrEqualTo(Glaze.Menu.LineSpacing), "reading prose has at least the font's own line height");
    }

    [Test]
    public void TheSmallestWordReadsAtTheFloorAcrossTheWholeInstrument()
    {
        // Text near a flat plane's edge shrinks as the eyes see it. The content lines stand a padding inside
        // each instrument's outer edge; Body must read at the floor out there, at the tallest the slate may
        // grow, at either text size.
        var padding = PlaneComposition.Units(SpatialKit.PaddingDegrees);
        foreach (var half in new[] { SpatialKit.WorkHalfWidth, SpatialKit.OverviewHalfWidth })
        {
            foreach (var scale in new[] { 1f, Comfort.LargerTextScale })
            {
                var right = half - padding;
                var up = SpatialKit.MostHalfHeight(scale) - padding;
                Assert.That(SpatialKit.Reads(SpatialKit.BodyDp * scale, right, up), Is.True,
                    $"Body x{scale} at the corner ({right:0.000}, {up:0.000}) reads {SpatialKit.SeenDegrees(SpatialKit.BodyDp * scale, right, up):0.000} degrees");
            }
        }
        // Anything smaller would fail out there: the kit has no smaller size on purpose.
        Assert.That(SpatialKit.Reads(16f, SpatialKit.WorkHalfWidth - padding, SpatialKit.MostHalfHeight(1f) - padding), Is.False);
    }

    [Test]
    public void TheInstrumentStaysInsideAQuest3SField()
    {
        // Meta's 96 by 90 degrees, less 1.5 at each edge, with the head turned to the instrument's centre.
        Assert.That(2f * SpatialKit.DegreesTo(SpatialKit.WorkHalfWidth), Is.LessThan(96f - 3f));
        Assert.That(2f * SpatialKit.DegreesTo(SpatialKit.OverviewHalfWidth), Is.LessThan(96f - 3f));
        Assert.That(SpatialKit.SlateMostAt(Comfort.LargerTextScale), Is.LessThan(90f - 3f));
        Assert.That(SpatialKit.PresenceDegrees, Is.LessThan(SpatialKit.SlateDegrees), "compact presence is smaller than the work it stands for");
    }

    [Test]
    public void TheSwitchHasTwoWholeTargetsTwelveMillimetresApart()
    {
        Assert.That(SpatialKit.SwitchCellDegrees, Is.GreaterThanOrEqualTo(SpatialKit.TargetDegrees));
        Assert.That(SpatialKit.TargetGapDegrees, Is.EqualTo(Glaze.DegreesOf(0.012f, 0.46f)).Within(1e-5));
        var cells = 2f * SpatialKit.SwitchCellDegrees + SpatialKit.TargetGapDegrees;
        Assert.That(SpatialKit.SwitchTrackDegrees, Is.GreaterThan(cells), "the inset stands round both cells and the gap");
        Assert.That(SpatialKit.SwitchSeconds, Is.LessThanOrEqualTo(Glaze.SlideSeconds), "the thumb is no slower than any other move");
    }

    [Test]
    public void TheCurveKeepsTheSmallestWordAtTheFloorAcrossItsWholeSpread()
    {
        // On one curve centred on the eyes every column faces them, so text shrinks only with its height: Body at the
        // tallest slate's far corners reads at the floor at either text size, however far across the corner stands.
        var padding = PlaneComposition.Units(SpatialKit.PaddingDegrees);
        foreach (var scale in new[] { 1f, Comfort.LargerTextScale })
        {
            var up = SpatialKit.MostHalfHeight(scale) - padding;
            Assert.That(SpatialKit.Reads(SpatialKit.BodyDp * scale, 0f, up), Is.True,
                $"Body x{scale} at height {up:0.000} reads {SpatialKit.SeenDegrees(SpatialKit.BodyDp * scale, 0f, up):0.000} degrees");
        }
    }

    [Test]
    public void TheCurveStaysInsideAQuest3SField()
    {
        // Bent round the eyes, the laid-out half-width becomes the half-spread in radians; it must stay inside
        // Meta's 96 degrees across less 1.5 at each edge, with the head turned to the instrument's centre.
        foreach (var half in new[] { SpatialKit.WorkHalfWidth, SpatialKit.OverviewHalfWidth })
            Assert.That(half * 180f / MathF.PI, Is.LessThan(48f - 1.5f));
    }

    [Test]
    public void TheRecessIsADepthNotAGap()
    {
        Assert.That(SpatialKit.RecessMeters, Is.GreaterThan(0f));
        Assert.That(SpatialKit.RecessMeters, Is.LessThan(Glaze.TargetGapMeters), "the side regions stay within a target's reach of the slate's plane");
        Assert.That(SpatialKit.Carriers.Distinct().Count(), Is.EqualTo(SpatialKit.Carriers.Count), "each carrier is its own colour");
    }
}

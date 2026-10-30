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
        // Anything smaller would fail out there: the instrument has no smaller size on purpose; Label lives only on the room's small surfaces.
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

    // The room of projects (ADR 0040).

    private static readonly GlazeColor BrightWall = GlazeColor.Hex(0xE4DFD6);

    private static GlazeColor[] RoomCarriers => new[] { SpatialKit.Well, SpatialKit.Carrier, SpatialKit.Raised, SpatialKit.Control };

    [Test]
    public void TheRampStepsByRole()
    {
        var carriers = RoomCarriers;
        for (var i = 1; i < carriers.Length; i++)
            Assert.That(carriers[i].Luminance, Is.GreaterThan(carriers[i - 1].Luminance), $"carrier step {i} is lighter than the one below");
        Assert.That(SpatialKit.Hairline.Luminance, Is.GreaterThan(SpatialKit.Control.Luminance), "a hairline stands above the controls");
        Assert.That(SpatialKit.TextTertiary.Luminance, Is.LessThan(SpatialKit.TextSecondary.Luminance));
        Assert.That(SpatialKit.TextSecondary.Luminance, Is.LessThan(SpatialKit.Text.Luminance));
        foreach (var carrier in carriers.Append(SpatialKit.AddedBand))
        {
            Assert.That(carrier.Luminance, Is.GreaterThanOrEqualTo(DarkestShown.Luminance), $"{carrier} is darker than the display shows");
            Assert.That(Math.Min(carrier.R, Math.Min(carrier.G, carrier.B)), Is.GreaterThan(13), $"{carrier} has a channel the display cannot separate");
        }
    }

    [Test]
    public void TheRampIsQuietGlass()
    {
        // ART's handoff (2026-10-30): eight neutral steps on one cool hue and four state tones. n3 is the kit's own: ART's
        // #3A3F45 leaves tertiary words at 4.29 and failed words at 4.35 on a control, so the kit takes OKLCH 0.35; ART's
        // value raises the switch's thumb, which carries only primary and secondary words.
        var ramp = new[] { SpatialKit.Well, SpatialKit.Carrier, SpatialKit.Raised, SpatialKit.Control, SpatialKit.Hairline, SpatialKit.TextTertiary, SpatialKit.TextSecondary, SpatialKit.Text };
        Assert.That(ramp, Is.EqualTo(new uint[] { 0x1B1E22, 0x21252A, 0x2B2F35, 0x363B41, 0x6D7279, 0xA0A5AC, 0xBABEC4, 0xDADDE0 }.Select(GlazeColor.Hex)));
        Assert.That(new[] { SpatialKit.Accent, SpatialKit.Attention, SpatialKit.Failure, SpatialKit.Passed },
            Is.EqualTo(new uint[] { 0x59CFF1, 0xFABA59, 0xF7857D, 0x69D98D }.Select(GlazeColor.Hex)), "live, needs, failed, passed");
        Assert.That(SpatialKit.OnFill, Is.EqualTo(SpatialKit.Well), "words on the primary capsule are n0");
        Assert.That(SpatialKit.Thumb, Is.EqualTo(GlazeColor.Hex(0x3A3F45)), "ART's n3 raises the switch's thumb");
        Assert.That((SpatialKit.FaceTop, SpatialKit.FaceBottom), Is.EqualTo((GlazeColor.Hex(0x262A30), GlazeColor.Hex(0x1E2226))), "the face's light from above");
    }

    [Test]
    public void ThePrimaryCapsuleCarriesItsWord()
    {
        // The one primary in view is an n7 capsule with its word in n0, pressed to n6.
        Assert.That(GlazeColor.Contrast(SpatialKit.OnFill, SpatialKit.Text), Is.GreaterThanOrEqualTo(12), "at rest (ART measured 12.3)");
        Assert.That(GlazeColor.Contrast(SpatialKit.OnFill, SpatialKit.TextSecondary), Is.GreaterThanOrEqualTo(7), "pressed");
        Assert.That(GlazeColor.Contrast(SpatialKit.Text, SpatialKit.Carrier), Is.GreaterThanOrEqualTo(7), "the capsule stands off its face as its word does");
    }

    [Test]
    public void EveryWordReadsAcrossTheFacesLight()
    {
        var words = new[] { SpatialKit.Text, SpatialKit.TextSecondary, SpatialKit.TextTertiary, SpatialKit.Accent, SpatialKit.Attention, SpatialKit.Failure, SpatialKit.Passed };
        foreach (var face in new[] { SpatialKit.FaceTop, SpatialKit.Carrier, SpatialKit.FaceBottom })
        {
            foreach (var word in words)
                Assert.That(GlazeColor.Contrast(word, face), Is.GreaterThanOrEqualTo(4.5), $"{word} on {face}");
            Assert.That(GlazeColor.Contrast(SpatialKit.Text, face), Is.GreaterThanOrEqualTo(7), $"primary words on {face}");
        }
        Assert.That(SpatialKit.FaceTop.Luminance, Is.GreaterThan(SpatialKit.Carrier.Luminance), "lit from above");
        Assert.That(SpatialKit.FaceBottom.Luminance, Is.LessThan(SpatialKit.Carrier.Luminance));
    }

    [Test]
    public void ALockedOutlineShowsWithoutReadingAsWords()
    {
        // n4 draws a locked answer's ring and the kit's hairlines: a boundary at WCAG's 3:1 for non-text, quieter than any word.
        Assert.That(GlazeColor.Contrast(SpatialKit.Hairline, SpatialKit.Carrier), Is.GreaterThanOrEqualTo(3), "a locked ring on its face");
        Assert.That(GlazeColor.Contrast(SpatialKit.Hairline, SpatialKit.Carrier), Is.LessThan(GlazeColor.Contrast(SpatialKit.TextTertiary, SpatialKit.Carrier)));
    }

    [Test]
    public void EveryWordInTheRoomReadsOnEveryCarrier()
    {
        var words = new[] { SpatialKit.Text, SpatialKit.TextSecondary, SpatialKit.TextTertiary, SpatialKit.Accent, SpatialKit.Attention, SpatialKit.Failure, SpatialKit.Passed };
        foreach (var carrier in RoomCarriers)
            foreach (var word in words)
                Assert.That(GlazeColor.Contrast(word, carrier), Is.GreaterThanOrEqualTo(4.5), $"{word} on {carrier}");
        foreach (var fill in new[] { SpatialKit.Accent, SpatialKit.Attention, SpatialKit.Failure, SpatialKit.Passed })
            Assert.That(GlazeColor.Contrast(SpatialKit.AccentInk, fill), Is.GreaterThanOrEqualTo(4.5), $"the ink on {fill}");
        Assert.That(GlazeColor.Contrast(SpatialKit.Text, SpatialKit.AddedBand), Is.GreaterThanOrEqualTo(7), "code on an added line's band");
        Assert.That(GlazeColor.Contrast(SpatialKit.Carrier, BrightWall), Is.GreaterThanOrEqualTo(7), "a carrier stands off a bright wall");
    }

    [Test]
    public void ButtonsKeepTheirWordsInEveryState()
    {
        // A secondary button presses to the control step, a primary one to the deepened accent (AUDIT's V1 note): the words a
        // button carries keep their contrast pointed and pressed, and the quieter words never stand on a button.
        foreach (var fill in new[] { SpatialKit.Raised, SpatialKit.Control })
            Assert.That(GlazeColor.Contrast(SpatialKit.Text, fill), Is.GreaterThanOrEqualTo(7), $"a button's words on {fill}");
        foreach (var fill in new[] { SpatialKit.Accent, SpatialKit.HoveredAccent, SpatialKit.PressedAccent })
            Assert.That(GlazeColor.Contrast(SpatialKit.AccentInk, fill), Is.GreaterThanOrEqualTo(7), $"a primary action's words on {fill}");
        Assert.That(SpatialKit.PressedAccent.Luminance, Is.LessThan(SpatialKit.Accent.Luminance), "pressed deepens");
        Assert.That(SpatialKit.HoveredAccent.Luminance, Is.GreaterThan(SpatialKit.Accent.Luminance), "pointed lifts");
    }

    [Test]
    public void AProjectsIdentityFieldIsQuietAndReadableInEveryHue()
    {
        for (var hue = 0; hue < 360; hue++)
        {
            var field = SpatialKit.IdentityField(hue);
            Assert.That(GlazeColor.Contrast(SpatialKit.Text, field), Is.GreaterThanOrEqualTo(7), $"the name on hue {hue}");
            Assert.That(GlazeColor.Contrast(SpatialKit.Raised, field), Is.LessThan(2), $"the mark's disc sits quietly on hue {hue}");
            Assert.That(field.Luminance, Is.GreaterThanOrEqualTo(DarkestShown.Luminance), $"hue {hue} is darker than the display shows");
            Assert.That(Math.Min(field.R, Math.Min(field.G, field.B)), Is.GreaterThan(13), $"hue {hue} has a channel the display cannot separate");
            Assert.That(Chroma(field), Is.LessThan(0.12), $"hue {hue} is a quiet field, not a colour that means something");
        }
        Assert.That(SpatialKit.IdentityField(0), Is.Not.EqualTo(SpatialKit.IdentityField(180)), "the hue shows");
        Assert.That(SpatialKit.Degrees(SpatialKit.DisplayDp), Is.GreaterThan(SpatialKit.Degrees(SpatialKit.IdentityDp)), "display type is the largest");
    }

    [Test]
    public void TheScreenAndItsBarHaveTheDecidedSizes()
    {
        Assert.That(SpatialKit.ScreenDegrees, Is.EqualTo(56f));
        Assert.That(SpatialKit.ScreenHeightDegrees, Is.EqualTo(31.5f).Within(1e-4));
        Assert.That(SpatialKit.BarDegrees, Is.GreaterThanOrEqualTo(SpatialKit.TargetDegrees + 2 * SpatialKit.GridDegrees), "the bar holds a whole target with room round it");
        Assert.That(SpatialKit.ScreenDegrees, Is.LessThan(110f - 2 * 15f), "the screen leaves the field's edges to the room");
        Assert.That(SpatialKit.MomentDegrees, Is.EqualTo(23f), "the moment slot's width (ADR 0040, rule 4)");
        Assert.That(SpatialKit.StackEdgeDegrees, Is.LessThan(SpatialKit.TargetDegrees), "the stacked edge is a hint, never a target");
    }

    [Test]
    public void OneAccentAndColourOnlyForMeaning()
    {
        var hues = new[] { SpatialKit.Accent, SpatialKit.Attention, SpatialKit.Failure, SpatialKit.Passed };
        Assert.That(hues.Distinct().Count(), Is.EqualTo(hues.Length), "each meaning has its own colour");
        foreach (var neutral in RoomCarriers.Concat(new[] { SpatialKit.Hairline, SpatialKit.TextTertiary, SpatialKit.TextSecondary, SpatialKit.Text }))
            Assert.That(Chroma(neutral), Is.LessThan(0.12), $"{neutral} is a neutral step");
        foreach (var hue in hues)
            Assert.That(Chroma(hue), Is.GreaterThan(0.3), $"{hue} reads as a colour");
    }

    private static double Chroma(GlazeColor c) => (Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B))) / 255.0;

    [Test]
    public void FiveTypeRolesStepDownToTheFloor()
    {
        Assert.That(SpatialKit.IdentityDp, Is.EqualTo(30f));
        Assert.That(SpatialKit.HeadingDp, Is.EqualTo(22f));
        Assert.That(SpatialKit.BodyDp, Is.EqualTo(18f));
        Assert.That(SpatialKit.MonoDp, Is.EqualTo(16f));
        Assert.That(SpatialKit.LabelDp, Is.EqualTo(15f));
        Assert.That(SpatialKit.DisplayDp, Is.EqualTo(48f), "the room's Display");
        Assert.That(SpatialKit.TitleDp, Is.EqualTo(28f), "the room's Title: a moment's, the reader's, Projects");
        Assert.That(SpatialKit.Degrees(SpatialKit.LabelDp), Is.GreaterThanOrEqualTo(Glaze.MinimumTextDegrees), "the smallest role is above Meta's 14 dp floor");
        // A moment card is 23 degrees wide where the person looks at it; Label must read at its corners at either text size.
        var right = PlaneComposition.Units(23f / 2f - SpatialKit.PaddingDegrees);
        var up = PlaneComposition.Units(10f);
        foreach (var scale in new[] { 1f, Comfort.LargerTextScale })
            Assert.That(SpatialKit.Reads(SpatialKit.LabelDp * scale, right, up), Is.True, $"Label x{scale} at a moment's corner");
    }

    [Test]
    public void MotionIsTheDecidedTokens()
    {
        Assert.That(SpatialKit.PopSeconds, Is.EqualTo(0.5f));
        Assert.That(SpatialKit.PopBounce, Is.EqualTo(0.15f), "a small overshoot, under Apple's caution of 0.4");
        Assert.That(SpatialKit.UnfoldSeconds, Is.EqualTo(0.5f));
        Assert.That(SpatialKit.FoldSeconds, Is.EqualTo(0.25f));
        Assert.That(SpatialKit.FoldSeconds, Is.LessThan(SpatialKit.UnfoldSeconds), "leaving is quicker than arriving");
        Assert.That(SpatialKit.MoveSeconds, Is.EqualTo(0.6f));
        Assert.That(SpatialKit.BreathSeconds, Is.EqualTo(3.2f));
        Assert.That(SpatialKit.UnfoldCurve, Is.EqualTo((0.05f, 0.7f, 0.1f, 1f)), "Material 3 emphasized decelerate");
        Assert.That(SpatialKit.FoldCurve, Is.EqualTo((0.3f, 0f, 0.8f, 0.15f)), "Material 3 emphasized accelerate");
        Assert.That(SpatialKit.MoveCurve, Is.EqualTo((0.2f, 0f, 0f, 1f)), "Material 3 standard");
    }

    [Test]
    public void TheRecessIsADepthNotAGap()
    {
        Assert.That(SpatialKit.RecessMeters, Is.GreaterThan(0f));
        Assert.That(SpatialKit.RecessMeters, Is.LessThan(Glaze.TargetGapMeters), "the side regions stay within a target's reach of the slate's plane");
        Assert.That(SpatialKit.Carriers.Distinct().Count(), Is.EqualTo(SpatialKit.Carriers.Count), "each carrier is its own colour");
    }
}

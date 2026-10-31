using System;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// ART8's window glass (2026-10-31, at the floor-safe value): the face opaque over the whole panel, its grey gradient and
/// soft top light, inner edge and top line as the prototype composites them, every word at its floor across the face, and
/// the shadow's fit for Unity's linear light over its reference room.
/// </summary>
public class KitGlassMaterialTests
{
    private static readonly GlazeColor[] Rooms = { KitGlassMaterial.DarkRoom, KitGlassMaterial.LitRoom, KitGlassMaterial.WhiteWall };

    private static readonly double[] Through = Enumerable.Range(0, 21).Select(i => i / 20.0).ToArray();

    private static double[] Hex(uint rgb) => KitGlassMaterial.Channels(GlazeColor.Hex(rgb));

    [Test]
    public void TheFaceIsTheWindowsGreyAndOpaque()
    {
        var face = KitGlassMaterial.Face;
        Assert.That(face.Select(stop => KitGlassMaterial.ToColour(stop.Colour)), Is.EqualTo(new uint[] { 0x494C50, 0x424549, 0x3B3E42 }.Select(GlazeColor.Hex)), "ART8's #7A7F86 to #62676E at the floor-safe 0.6");
        Assert.That(face.All(stop => stop.Alpha == 1), Is.True, "opaque at every stop: no part of a panel lets the room through");
        // The soft top light: white 10 percent at the top's middle, gone by 60 percent of the height.
        Assert.That(KitGlassMaterial.Deviation(KitGlassMaterial.LitFace(0), KitGlassMaterial.Prototype(Hex(0xFFFFFF), 0.10, GlazeColor.Hex(0x494C50))), Is.LessThan(1e-9));
        Assert.That(KitGlassMaterial.Deviation(KitGlassMaterial.LitFace(0.6), KitGlassMaterial.At(face, 0.6).Colour), Is.LessThan(0.5), "no top light left, within a byte's rounding");
        Assert.That((KitGlassMaterial.HighlightAlpha, KitGlassMaterial.InnerEdgeAlpha, KitGlassMaterial.GlowAlpha), Is.EqualTo((0.30, 0.06, 0.0)), "the top line, the outer ring, and no glow (ART8B)");
    }

    [Test]
    public void EveryWordReadsAcrossTheLitFace()
    {
        // The floor holds where the face is lightest: the top light's middle, and under the top line and the inner edge.
        foreach (var t in Through)
            foreach (var across in new[] { 0.0, 0.5, 1.0 })
            {
                var face = KitGlassMaterial.ToColour(KitGlassMaterial.LitFace(t, across));
                foreach (var word in new[] { SpatialKit.Text, SpatialKit.TextSecondary, SpatialKit.TextTertiary })
                    Assert.That(GlazeColor.Contrast(word, face), Is.GreaterThanOrEqualTo(4.5), $"{word} at {t:0.00}, {across:0.0} across");
                Assert.That(GlazeColor.Contrast(SpatialKit.Text, face), Is.GreaterThanOrEqualTo(6.5), $"white words at {t:0.00}");
            }
        var edged = KitGlassMaterial.ToColour(KitGlassMaterial.Prototype(Hex(0xFFFFFF), KitGlassMaterial.HighlightAlpha + (1 - KitGlassMaterial.HighlightAlpha) * KitGlassMaterial.InnerEdgeAlpha, KitGlassMaterial.ToColour(KitGlassMaterial.LitFace(0))));
        Assert.That(GlazeColor.Contrast(edged, KitGlassMaterial.ToColour(KitGlassMaterial.LitFace(0))), Is.GreaterThan(1.2), "the top line shows as a line");
    }

    [Test]
    public void TheShadowFitHoldsAtEveryAlpha()
    {
        var black = new double[] { 0, 0, 0 };
        for (var a = 0.0; a <= KitGlassMaterial.ShadowAlpha + 1e-9; a += 0.01)
        {
            var fitted = KitGlassMaterial.Drawn(black, KitGlassMaterial.ShadowLinear(a), KitGlassMaterial.LitRoom);
            Assert.That(KitGlassMaterial.Deviation(fitted, KitGlassMaterial.Prototype(black, a, KitGlassMaterial.LitRoom)), Is.LessThanOrEqualTo(1.5), $"shadow at {a:0.00}");
        }
    }

    [Test]
    public void EveryWordReadsOnTheBloomAtItsPeak()
    {
        // The bloom lands on the opaque face, mixed in sRGB as the prototype does; at the kit's peak every word keeps its
        // floor across the face's light, and a locked ring keeps 3:1 wherever an answer can stand (the lower 40 percent).
        var words = new[] { SpatialKit.Text, SpatialKit.TextSecondary, SpatialKit.TextTertiary };
        foreach (var tone in new[] { SpatialKit.Attention, SpatialKit.Passed, SpatialKit.Failure })
        {
            foreach (var t in Through)
            {
                var (face, _) = KitGlassMaterial.At(KitGlassMaterial.Face, t);
                var bloomed = KitGlassMaterial.ToColour(KitGlassMaterial.Prototype(KitGlassMaterial.Channels(tone), KitGlassMaterial.BloomAt(t), KitGlassMaterial.ToColour(face)));
                foreach (var word in words)
                    Assert.That(GlazeColor.Contrast(word, bloomed), Is.GreaterThanOrEqualTo(4.5), $"{word} on {tone}'s bloom at {t:0.00} ({bloomed})");
                Assert.That(GlazeColor.Contrast(SpatialKit.Text, bloomed), Is.GreaterThanOrEqualTo(7), $"primary words on {tone}'s bloom at {t:0.00}");
                if (t >= 0.6)
                    Assert.That(GlazeColor.Contrast(SpatialKit.Hairline, bloomed), Is.GreaterThanOrEqualTo(3), $"a locked ring on {tone}'s bloom at {t:0.00}");
            }
        }
    }

    [Test]
    public void TheQuadReachesPastEveryBlur()
    {
        // A Gaussian of blur b has sigma b / 2; three sigmas hold all but 0.14 percent of it.
        var (side, top, bottom) = KitGlassMaterial.Margins;
        Assert.That(side, Is.GreaterThanOrEqualTo(1.5f * KitGlassMaterial.ShadowBlurDp), "the far shadow at the sides");
        Assert.That(bottom, Is.GreaterThanOrEqualTo(KitGlassMaterial.ShadowDropDp + 1.5f * KitGlassMaterial.ShadowBlurDp), "below");
        Assert.That(top, Is.GreaterThanOrEqualTo(1.5f * KitGlassMaterial.ShadowBlurDp - KitGlassMaterial.ShadowDropDp), "above");
        // What it costs: ART8B's shadow makes the screen (896 by 504 dp) draw about 1.6 times its area; smaller panels pay more (the render reports each).
        Assert.That(KitGlassMaterial.Overdraw(896, 504), Is.LessThan(1.8));
    }

    [Test]
    public void TheShadowsBlurAsTheBrowserDoes()
    {
        // The shader's error function, against its tabled values, and a blurred edge half covering its own line.
        foreach (var (x, erf) in new[] { (0.25, 0.2763264), (0.5, 0.5204999), (1.0, 0.8427008), (2.0, 0.9953223), (-1.0, -0.8427008) })
            Assert.That(KitGlassMaterial.Erf(x), Is.EqualTo(erf).Within(2e-4), $"erf({x})");
        Assert.That(KitGlassMaterial.Blurred(0, KitGlassMaterial.ShadowBlurDp), Is.EqualTo(0.5).Within(1e-9));
        // Right under a panel's foot the far shadow and the near one darken the lit room; far out it is the room.
        var under = KitGlassMaterial.PrototypeOutside(0, -100 - 4, 200, 200, 24, KitGlassMaterial.LitRoom);
        Assert.That(GlazeColor.Contrast(KitGlassMaterial.ToColour(under), KitGlassMaterial.LitRoom), Is.GreaterThan(1.3), "the shadow shows under the panel");
        var far = KitGlassMaterial.PrototypeOutside(0, -100 - 90, 200, 200, 24, KitGlassMaterial.LitRoom);
        Assert.That(KitGlassMaterial.Deviation(far, KitGlassMaterial.Channels(KitGlassMaterial.LitRoom)), Is.LessThanOrEqualTo(1.0), "past the quad's reach");
    }

}

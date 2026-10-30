using System;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The quiet glass (the art direction's handoff, 2026-10-30, section 1): the prototype's sRGB composites, and the alphas
/// Unity must draw in linear light to land on them over each layer's reference room.
/// </summary>
public class KitGlassMaterialTests
{
    private static readonly GlazeColor[] Rooms = { KitGlassMaterial.DarkRoom, KitGlassMaterial.LitRoom, KitGlassMaterial.WhiteWall };

    private static readonly double[] Through = Enumerable.Range(0, 21).Select(i => i / 20.0).ToArray();

    private static double[] Hex(uint rgb) => KitGlassMaterial.Channels(GlazeColor.Hex(rgb));

    [Test]
    public void TheLayersAreTheArtDirectionsComposites()
    {
        // The handoff's own measures, from the prototype's values over its grounds: the data here is the prototype's.
        var rim = KitGlassMaterial.Rim;
        void Lands(double[] drawn, uint expected, string what) =>
            Assert.That(KitGlassMaterial.Deviation(drawn, Hex(expected)), Is.LessThanOrEqualTo(1.0), $"{what}: {KitGlassMaterial.ToColour(drawn)}");
        var top = KitGlassMaterial.At(rim, 0);
        var bottom = KitGlassMaterial.At(rim, 1);
        Lands(KitGlassMaterial.Prototype(top.Colour, top.Alpha, KitGlassMaterial.DarkRoom), 0x444342, "rim top, dark room");
        Lands(KitGlassMaterial.Prototype(top.Colour, top.Alpha, KitGlassMaterial.LitRoom), 0xC0B7AB, "rim top, lit room");
        Lands(KitGlassMaterial.Prototype(top.Colour, top.Alpha, KitGlassMaterial.WhiteWall), 0xEAEBEB, "rim top, white wall");
        Lands(KitGlassMaterial.Prototype(bottom.Colour, bottom.Alpha, KitGlassMaterial.LitRoom), 0x79746D, "rim bottom, lit room");
        var side = KitGlassMaterial.At(KitGlassMaterial.Edge, 1);
        Lands(KitGlassMaterial.Prototype(side.Colour, side.Alpha, KitGlassMaterial.DarkRoom), 0x696969, "edge, dark room");
        Lands(KitGlassMaterial.Prototype(side.Colour, side.Alpha, KitGlassMaterial.LitRoom), 0xC6C1B8, "edge, lit room");
        // The bloom at the art direction's 14 percent over n1 (the kit draws it at its own peak).
        Lands(KitGlassMaterial.Prototype(KitGlassMaterial.Channels(SpatialKit.Attention), 0.14, SpatialKit.Carrier), 0x3F3A31, "needs bloom on n1");
        Lands(KitGlassMaterial.Prototype(KitGlassMaterial.Channels(SpatialKit.Passed), 0.14, SpatialKit.Carrier), 0x2B3E38, "passed bloom on n1");
        Lands(KitGlassMaterial.Prototype(KitGlassMaterial.Channels(SpatialKit.Failure), 0.14, SpatialKit.Carrier), 0x3F3236, "failed bloom on n1");
    }

    [Test]
    public void TheRimLandsOnThePrototypeOverTheLitRoom()
    {
        foreach (var t in Through)
        {
            var (colour, alpha) = KitGlassMaterial.At(KitGlassMaterial.Rim, t);
            var drawn = KitGlassMaterial.Drawn(KitGlassMaterial.Solve(colour, alpha, KitGlassMaterial.LitRoom, KitGlassMaterial.RimNudge), KitGlassMaterial.LitRoom);
            var target = KitGlassMaterial.Prototype(colour, alpha, KitGlassMaterial.LitRoom);
            Assert.That(KitGlassMaterial.Deviation(drawn, target), Is.LessThanOrEqualTo(2.5), $"rim at {t:0.00}: {KitGlassMaterial.ToColour(drawn)} for {KitGlassMaterial.ToColour(target)}");
        }
    }

    [Test]
    public void TheRimCastsNoTintOverAnyRoom()
    {
        // The rim lets the room through; it may lighten a room but never tint it (ART, 2026-10-30: a nudged rim drew #19242D, a
        // blue line, where the prototype draws #1F2021). Its blue less its red stays within 5/255 of the prototype's (4.0 measured).
        double Cast(double[] srgb) => (srgb[2] - srgb[0]) * 255;
        foreach (var room in Rooms)
        {
            foreach (var t in Through)
            {
                var (colour, alpha) = KitGlassMaterial.At(KitGlassMaterial.Rim, t);
                var drawn = KitGlassMaterial.Drawn(KitGlassMaterial.Solve(colour, alpha, KitGlassMaterial.LitRoom, KitGlassMaterial.RimNudge), room);
                var prototype = KitGlassMaterial.Prototype(colour, alpha, room);
                Assert.That(Math.Abs(Cast(drawn) - Cast(prototype)), Is.LessThanOrEqualTo(5), $"the rim at {t:0.00} over {room}: {KitGlassMaterial.ToColour(drawn)} for {KitGlassMaterial.ToColour(prototype)}");
            }
        }
    }

    [Test]
    public void TheEdgeLandsOnThePrototypeOverTheDarkRoomAndKeepsItsBoundary()
    {
        // The edge stands over the rim; the two draw as one layer there, solved for the dark room, where the edge is the
        // panel's boundary (handoff 7.3: the dark room wins for the edge light).
        foreach (var edge in new[] { KitGlassMaterial.Edge }.Concat(new[] { SpatialKit.Attention, SpatialKit.Passed, SpatialKit.Failure }.Select(KitGlassMaterial.StateEdge)))
        {
            foreach (var t in Through)
            {
                var ring = KitGlassMaterial.Over(KitGlassMaterial.At(edge, t), KitGlassMaterial.At(KitGlassMaterial.Rim, t));
                var drawn = KitGlassMaterial.Drawn(KitGlassMaterial.Solve(ring.Colour, ring.Alpha, KitGlassMaterial.DarkRoom), KitGlassMaterial.DarkRoom);
                var target = KitGlassMaterial.Prototype(ring.Colour, ring.Alpha, KitGlassMaterial.DarkRoom);
                Assert.That(KitGlassMaterial.Deviation(drawn, target), Is.LessThanOrEqualTo(0.5), $"edge at {t:0.00}: {KitGlassMaterial.ToColour(drawn)} for {KitGlassMaterial.ToColour(target)}");
                Assert.That(GlazeColor.Contrast(KitGlassMaterial.ToColour(drawn), KitGlassMaterial.DarkRoom), Is.GreaterThanOrEqualTo(3), $"the edge at {t:0.00} against the dark room");
            }
        }
    }

    [Test]
    public void OverTheOtherRoomsTheGlassDriftsNoFurtherThanMeasured()
    {
        // Linear blending cannot match every room (handoff 7.3). Measured 2026-10-30: the rim, solved for the lit room,
        // stands up to 37.5/255 lighter than the prototype over the dark room and 5.8 off over the white wall; the edge,
        // solved for the dark room, up to 12.6 off over the lit room and 24.1 over the white wall, at its foot. These bounds
        // hold the drift where it was measured.
        double Worst(Func<double, (double[] Colour, double Alpha)> layer, GlazeColor solvedFor, GlazeColor over) => Through.Max(t =>
        {
            var (colour, alpha) = layer(t);
            var nudge = !solvedFor.Equals(KitGlassMaterial.LitRoom) || KitGlassMaterial.RimNudge;
            return KitGlassMaterial.Deviation(KitGlassMaterial.Drawn(KitGlassMaterial.Solve(colour, alpha, solvedFor, nudge), over), KitGlassMaterial.Prototype(colour, alpha, over));
        });
        (double[] Colour, double Alpha) Rim(double t) => KitGlassMaterial.At(KitGlassMaterial.Rim, t);
        (double[] Colour, double Alpha) Ring(double t) => KitGlassMaterial.Over(KitGlassMaterial.At(KitGlassMaterial.Edge, t), Rim(t));
        Assert.That(Worst(Rim, KitGlassMaterial.LitRoom, KitGlassMaterial.DarkRoom), Is.LessThanOrEqualTo(38), "the rim over the dark room");
        Assert.That(Worst(Rim, KitGlassMaterial.LitRoom, KitGlassMaterial.WhiteWall), Is.LessThanOrEqualTo(6), "the rim over the white wall");
        Assert.That(Worst(Ring, KitGlassMaterial.DarkRoom, KitGlassMaterial.LitRoom), Is.LessThanOrEqualTo(13), "the edge over the lit room");
        Assert.That(Worst(Ring, KitGlassMaterial.DarkRoom, KitGlassMaterial.WhiteWall), Is.LessThanOrEqualTo(25), "the edge over the white wall");
    }

    [Test]
    public void TheEdgeStaysALineOverTheRimInTheDarkRoom()
    {
        // The art direction's rule (2026-10-30): over the dark room the edge stands at least 1.4:1 above the rim beside it, at
        // every height, as drawn in linear light (measured: 2.36 at worst, 25 percent down; the prototype's 3.1 to 5.2).
        foreach (var t in Through)
        {
            var (rimColour, rimAlpha) = KitGlassMaterial.At(KitGlassMaterial.Rim, t);
            var rim = KitGlassMaterial.ToColour(KitGlassMaterial.Drawn(KitGlassMaterial.Solve(rimColour, rimAlpha, KitGlassMaterial.LitRoom, KitGlassMaterial.RimNudge), KitGlassMaterial.DarkRoom));
            var ring = KitGlassMaterial.Over(KitGlassMaterial.At(KitGlassMaterial.Edge, t), (rimColour, rimAlpha));
            var edge = KitGlassMaterial.ToColour(KitGlassMaterial.Drawn(KitGlassMaterial.Solve(ring.Colour, ring.Alpha, KitGlassMaterial.DarkRoom), KitGlassMaterial.DarkRoom));
            Assert.That(GlazeColor.Contrast(edge, rim), Is.GreaterThanOrEqualTo(1.4), $"at {t:0.00}: the edge {edge} over the rim {rim}");
        }
    }

    [Test]
    public void TheShadowAndGlowFitsHoldAtEveryAlpha()
    {
        var black = new double[] { 0, 0, 0 };
        for (var a = 0.0; a <= KitGlassMaterial.ShadowAlpha + 1e-9; a += 0.01)
        {
            var fitted = KitGlassMaterial.Drawn(black, KitGlassMaterial.ShadowLinear(a), KitGlassMaterial.LitRoom);
            Assert.That(KitGlassMaterial.Deviation(fitted, KitGlassMaterial.Prototype(black, a, KitGlassMaterial.LitRoom)), Is.LessThanOrEqualTo(1.5), $"shadow at {a:0.00}");
        }
        var n7 = KitGlassMaterial.Channels(SpatialKit.Text);
        for (var a = 0.005; a <= KitGlassMaterial.GlowAlpha + 1e-9; a += 0.005)
        {
            var fitted = KitGlassMaterial.Drawn(n7, KitGlassMaterial.GlowLinear(a), KitGlassMaterial.DarkRoom);
            Assert.That(KitGlassMaterial.Deviation(fitted, KitGlassMaterial.Prototype(n7, a, KitGlassMaterial.DarkRoom)), Is.LessThanOrEqualTo(1.0), $"glow at {a:0.000}");
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
    public void NothingStandsOnTheRim()
    {
        // Words, rings, hairlines and the trail sit on the opaque face only (AUDIT's A1B-S2): a panel's words start 24 dp in,
        // past the 10 dp rim, and every rim is narrower than that padding.
        foreach (var rim in new[] { KitGlassMaterial.RimDp, KitGlassMaterial.SmallRimDp, KitGlassMaterial.PlateRimDp })
            Assert.That(SpatialKit.Degrees(rim), Is.LessThan(SpatialKit.PaddingDegrees), $"a {rim} dp rim");
        Assert.That(SpatialKit.Degrees(KitGlassMaterial.RimDp) + SpatialKit.Degrees(14f), Is.EqualTo(SpatialKit.PaddingDegrees).Within(1e-6), "10 dp of rim and 14 of padding");
    }

    [Test]
    public void TheQuadReachesPastEveryBlur()
    {
        // A Gaussian of blur b has sigma b / 2; three sigmas hold all but 0.14 percent of it.
        var (side, top, bottom) = KitGlassMaterial.Margins;
        Assert.That(side, Is.GreaterThanOrEqualTo(1.5f * KitGlassMaterial.ShadowBlurDp), "the far shadow at the sides");
        Assert.That(bottom, Is.GreaterThanOrEqualTo(KitGlassMaterial.ShadowDropDp + 1.5f * KitGlassMaterial.ShadowBlurDp), "below");
        Assert.That(top, Is.GreaterThanOrEqualTo(Math.Max(1.5f * KitGlassMaterial.ShadowBlurDp - KitGlassMaterial.ShadowDropDp, 1.5f * KitGlassMaterial.GlowBlurDp)), "above, the glow included");
        Assert.That(side, Is.GreaterThanOrEqualTo(1.5f * KitGlassMaterial.GlowBlurDp), "the glow at the sides");
        // What it costs: the screen (896 by 504 dp) draws 1.34 times its area; smaller panels pay more (the render reports each).
        Assert.That(KitGlassMaterial.Overdraw(896, 504), Is.LessThan(1.5));
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
        var far = KitGlassMaterial.PrototypeOutside(0, -100 - 70, 200, 200, 24, KitGlassMaterial.LitRoom);
        Assert.That(KitGlassMaterial.Deviation(far, KitGlassMaterial.Channels(KitGlassMaterial.LitRoom)), Is.LessThanOrEqualTo(1.0), "past the quad's reach");
    }

    [Test]
    public void AStateEdgeStartsInItsToneAndEndsNeutral()
    {
        var edge = KitGlassMaterial.StateEdge(SpatialKit.Attention);
        Assert.That(KitGlassMaterial.ToColour(edge[0].Colour), Is.EqualTo(SpatialKit.Attention));
        Assert.That(edge[0].Alpha, Is.EqualTo(1));
        Assert.That(edge[1].At, Is.EqualTo(0.30));
        Assert.That(edge[1].Alpha, Is.EqualTo(0.76).Within(1e-9), "60 percent of the tone with 40 of n7 at 40");
        Assert.That(KitGlassMaterial.ToColour(edge[2].Colour), Is.EqualTo(SpatialKit.Text));
        Assert.That((edge[2].At, edge[2].Alpha), Is.EqualTo((0.62, 0.40)));
    }
}

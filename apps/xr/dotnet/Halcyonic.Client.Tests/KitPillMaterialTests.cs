using System;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The room's unified glass-capsule material (2026-10-31, <see cref="KitPillMaterial"/>): green, red, blue and neutral
/// smoked-slate five-stop vertical gradients, the inset upper specular crown, the 1 dp top rim catch-light, the bottom rim
/// bounce, and the 1.5 dp darker outer rim, with white words holding their contrast floor across the middle band in every state.
/// </summary>
public class KitPillMaterialTests
{
    private static readonly KitPillMeaning[] Meanings =
        { KitPillMeaning.Blue, KitPillMeaning.Green, KitPillMeaning.Red, KitPillMeaning.Neutral };

    [Test]
    public void EveryMeaningHoldsWhiteWordsAcrossItsMiddleBandInEveryState()
    {
        foreach (var meaning in Meanings)
        {
            foreach (var state in new[] { KitPillState.Rest, KitPillState.Pointed, KitPillState.Pressed })
            {
                for (var step = 0; step <= 26; step++)
                {
                    var t = 0.34 + step * 0.02;
                    var yDp = 30.0 - t * 60.0;
                    var sampled = KitGlassMaterial.ToColour(KitPillMaterial.Sample(meaning, 0, yDp, 240, 60, state));
                    Assert.That(GlazeColor.Contrast(SpatialKit.OnFill, sampled), Is.GreaterThanOrEqualTo(4.8), $"{meaning} {state} at t={t:0.00} ({sampled})");
                    if (state == KitPillState.Rest)
                        Assert.That(GlazeColor.Contrast(SpatialKit.OnFill, sampled), Is.GreaterThanOrEqualTo(5.2), $"{meaning} at rest at t={t:0.00} ({sampled})");
                }
            }
            var rest = KitGlassMaterial.ToColour(KitPillMaterial.Sample(meaning, 0, 0, 240, 60, KitPillState.Rest));
            var pointed = KitGlassMaterial.ToColour(KitPillMaterial.Sample(meaning, 0, 0, 240, 60, KitPillState.Pointed));
            var pressed = KitGlassMaterial.ToColour(KitPillMaterial.Sample(meaning, 0, 0, 240, 60, KitPillState.Pressed));
            Assert.That(pointed.Luminance, Is.GreaterThan(rest.Luminance), $"{meaning} pointed brightens");
            Assert.That(pressed.Luminance, Is.LessThan(rest.Luminance), $"{meaning} pressed deepens");
        }
    }

    [Test]
    public void TheTopGlossBandShinesAboveTheMiddleBody()
    {
        Assert.That((KitPillMaterial.GlossMidShare, KitPillMaterial.GlossEndShare, KitPillMaterial.GlossHeightShare), Is.EqualTo((0.36f, 0.50f, 0.50)));
        Assert.That(KitPillMaterial.CrownAlpha(KitPillMeaning.Blue), Is.EqualTo((0.34, 0.10)));
        Assert.That(KitPillMaterial.CrownAlpha(KitPillMeaning.Neutral), Is.EqualTo((0.22, 0.06)));
        Assert.That(KitPillMaterial.TopCatchAlpha(KitPillMeaning.Blue), Is.EqualTo(0.42));
        Assert.That(KitPillMaterial.TopCatchAlpha(KitPillMeaning.Neutral), Is.EqualTo(0.28));
        foreach (var meaning in Meanings)
        {
            var catchLight = KitGlassMaterial.ToColour(KitPillMaterial.Sample(meaning, 0, 30 - 2.0, 240, 60));
            var glossTop = KitGlassMaterial.ToColour(KitPillMaterial.Sample(meaning, 0, 20, 240, 60));
            var glossMid = KitGlassMaterial.ToColour(KitPillMaterial.Sample(meaning, 0, 10, 240, 60));
            var bodyBelow = KitGlassMaterial.ToColour(KitPillMaterial.Sample(meaning, 0, -18, 240, 60));
            Assert.That(glossTop.Luminance, Is.GreaterThan(glossMid.Luminance), $"{meaning} continuous crown fades smoothly downward");
            Assert.That(glossTop.Luminance, Is.GreaterThan(bodyBelow.Luminance * 1.5), $"{meaning} gloss crown shines above middle body");
            Assert.That(catchLight.Luminance, Is.GreaterThan(glossTop.Luminance), $"{meaning} 1.2 dp top catch-light shines inside top rim");
            if (meaning == KitPillMeaning.Green)
                Assert.That(glossTop.G, Is.GreaterThan(Math.Max(glossTop.R, glossTop.B)), "emerald body hue shines through the translucent top crown");
            else if (meaning == KitPillMeaning.Red)
                Assert.That(glossTop.R, Is.GreaterThan(Math.Max(glossTop.G, glossTop.B)), "crimson body hue shines through the translucent top crown");
            else if (meaning == KitPillMeaning.Blue)
                Assert.That(glossTop.B, Is.GreaterThan(Math.Max(glossTop.R, glossTop.G)), "sapphire body hue shines through the translucent top crown");
        }
    }

    [Test]
    public void TheBottomNeonGlowAndOuterRimFrameEachMeaningInItsOwnHue()
    {
        Assert.That(KitPillMaterial.RimDp, Is.EqualTo(1.5f));
        foreach (var meaning in Meanings)
        {
            var rim = KitPillMaterial.Rim(meaning);
            var middle = KitPillMaterial.Middle(meaning);
            Assert.That(rim.Luminance, Is.LessThan(middle.Luminance), $"{meaning} rim is darker than its middle fill");
            var sampledRim = KitGlassMaterial.ToColour(KitPillMaterial.Sample(meaning, 0, 30 - 0.5, 240, 60));
            Assert.That(sampledRim, Is.EqualTo(rim), $"{meaning} outer 1.5 dp carries the rim");

            var baseBottom = KitGlassMaterial.ToColour(KitPillMaterial.At(meaning, (30.0 - (-25.0)) / 60.0));
            var glowedBottom = KitGlassMaterial.ToColour(KitPillMaterial.Sample(meaning, 0, -25.0, 240, 60));
            Assert.That(glowedBottom.Luminance, Is.GreaterThan(baseBottom.Luminance), $"{meaning} bottom inner bounce lifts the lower edge");
            if (meaning == KitPillMeaning.Green)
                Assert.That(middle.G, Is.GreaterThan(Math.Max(middle.R, middle.B)), "green pill is green");
            else if (meaning == KitPillMeaning.Red)
                Assert.That(middle.R, Is.GreaterThan(Math.Max(middle.G, middle.B)), "red pill is red");
            else if (meaning == KitPillMeaning.Blue)
                Assert.That(middle.B, Is.GreaterThan(Math.Max(middle.R, middle.G)), "blue pill is blue");
            else
                Assert.That(Math.Max(middle.R, Math.Max(middle.G, middle.B)) - Math.Min(middle.R, Math.Min(middle.G, middle.B)), Is.LessThanOrEqualTo(12), "neutral pill is smoked-slate grey");
        }
    }
}

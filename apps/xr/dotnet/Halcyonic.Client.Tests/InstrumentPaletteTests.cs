using NUnit.Framework;

namespace Halcyonic.Client.Tests;

[TestFixture]
public class InstrumentPaletteTests
{
    [Test]
    public void NeutralRetainsItsKeyWithoutReassigningStateMeaning()
    {
        var study = GlazePalette.InstrumentStudy;
        Assert.That(GlazePalette.Find(study.Key), Is.SameAs(GlazePalette.Neutral));
        Assert.That(GlazePalette.Presets, Does.Contain(study));
        foreach (var tone in new[] { GlazeTone.Active, GlazeTone.Attention, GlazeTone.Success, GlazeTone.Failure, GlazeTone.Unknown, GlazeTone.Simulated })
        {
            var original = GlazePalette.Glaze.Tone(tone); var candidate = study.Tone(tone);
            Assert.That((candidate.Foreground, candidate.Container, candidate.Strong, candidate.OnStrong),
                Is.EqualTo((original.Foreground, original.Container, original.Strong, original.OnStrong)), tone.ToString());
        }
    }

    [Test]
    public void NeutralReadingAndControlLevelsAreAchromaticAndRemainOrdered()
    {
        var palette = GlazePalette.Neutral;
        var surfaces = new[] { palette.Well, palette.Panel, palette.Raised, palette.Control, palette.ControlHover, palette.ControlPressed };
        for (var index = 0; index < surfaces.Length; index++)
        {
            var hex = surfaces[index].ToString();
            Assert.That(hex.Substring(1, 2), Is.EqualTo(hex.Substring(3, 2)));
            Assert.That(hex.Substring(3, 2), Is.EqualTo(hex.Substring(5, 2)));
            if (index > 0) Assert.That(surfaces[index].Luminance, Is.GreaterThan(surfaces[index - 1].Luminance));
            Assert.That(GlazeColor.Contrast(palette.Text, surfaces[index]), Is.GreaterThanOrEqualTo(4.5));
        }
        Assert.That(GlazePalette.Default, Is.SameAs(palette));
        Assert.That(palette.Key, Is.EqualTo("neutral"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReadingHasContrastOnTheActualLinearWhiteComposite(bool candidate)
    {
        var palette = candidate ? GlazePalette.InstrumentStudy : GlazePalette.Glaze;
        var backing = palette.Panel.Luminance * 0.98 + 0.02 + 0.018;
        Assert.That((palette.Text.Luminance + 0.05) / (backing + 0.05), Is.GreaterThanOrEqualTo(4.5));
        Assert.That((palette.TextSecondary.Luminance + 0.05) / (backing + 0.05), Is.GreaterThanOrEqualTo(4.5));
        // The former .94 translucent heading is a real negative, not byte-space alpha math.
        var former = GlazePalette.Glaze.Panel.Luminance * 0.94 + 0.06;
        Assert.That((GlazePalette.Glaze.TextSecondary.Luminance + 0.05) / (former + 0.05), Is.LessThan(4.5));
    }
    [Test]
    public void NeutralMainActionUsesTheVerifiedBlueFamilyWithReadableWholeControlInk()
    {
        var accent = GlazePalette.Neutral.Tone(GlazeTone.Accent);
        var reference = GlazePalette.Glaze.Tone(GlazeTone.Accent);
        Assert.That((accent.Foreground, accent.Container, accent.Strong, accent.OnStrong),
            Is.EqualTo((reference.Foreground, reference.Container, reference.Strong, reference.OnStrong)));
        Assert.That((accent.Foreground.Luminance + .05) / (accent.Container.Luminance + .05), Is.GreaterThanOrEqualTo(4.5));
        Assert.That(accent.Container, Is.Not.EqualTo(GlazePalette.Neutral.Control));
        Assert.That(accent.Strong, Is.Not.EqualTo(GlazePalette.Neutral.Tone(GlazeTone.Attention).Strong));
    }

}

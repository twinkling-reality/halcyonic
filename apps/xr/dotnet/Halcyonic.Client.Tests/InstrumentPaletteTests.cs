using NUnit.Framework;

namespace Halcyonic.Client.Tests;

[TestFixture]
public class InstrumentPaletteTests
{
    [Test]
    public void NeutralRetainsStudyColoursWithoutReassigningStateMeaning()
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

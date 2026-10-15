using NUnit.Framework;

namespace Halcyonic.Client.Tests;

[TestFixture]
public class InstrumentPaletteTests
{
    [Test]
    public void StudyCannotReplaceTheSavedAppearanceAndDoesNotReassignStateMeaning()
    {
        var study = GlazePalette.InstrumentStudy;
        Assert.That(GlazePalette.Find(study.Key), Is.Null, "comparison material is not a silently persisted user choice");
        Assert.That(GlazePalette.Presets, Does.Not.Contain(study));
        foreach (var tone in new[] { GlazeTone.Active, GlazeTone.Attention, GlazeTone.Success, GlazeTone.Failure, GlazeTone.Unknown, GlazeTone.Simulated })
        {
            var original = GlazePalette.Default.Tone(tone); var candidate = study.Tone(tone);
            Assert.That((candidate.Foreground, candidate.Container, candidate.Strong, candidate.OnStrong),
                Is.EqualTo((original.Foreground, original.Container, original.Strong, original.OnStrong)), tone.ToString());
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReadingHasContrastOnTheActualLinearWhiteComposite(bool candidate)
    {
        var palette = candidate ? GlazePalette.InstrumentStudy : GlazePalette.Default;
        var backing = palette.Panel.Luminance * 0.98 + 0.02 + 0.018;
        Assert.That((palette.Text.Luminance + 0.05) / (backing + 0.05), Is.GreaterThanOrEqualTo(4.5));
        Assert.That((palette.TextSecondary.Luminance + 0.05) / (backing + 0.05), Is.GreaterThanOrEqualTo(4.5));
        // The former .94 translucent heading is a real negative, not byte-space alpha math.
        var former = GlazePalette.Default.Panel.Luminance * 0.94 + 0.06;
        Assert.That((GlazePalette.Default.TextSecondary.Luminance + 0.05) / (former + 0.05), Is.LessThan(4.5));
    }
}

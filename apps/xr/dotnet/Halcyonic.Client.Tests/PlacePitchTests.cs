using Halcyonic.Client;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public sealed class PlacePitchTests
{
    private const double Distance = 1.4;
    private const double Body = 0.1445; // the shelf's body, 0.17 m at 0.85 scale

    [Test]
    public void AngleAndWidthAreEachOthersInverse()
    {
        var degrees = PlacePitch.DegreesOf(0.224, Distance);
        Assert.That(degrees, Is.EqualTo(9.1).Within(0.05));
        Assert.That(PlacePitch.MetresOf(degrees, Distance), Is.EqualTo(0.224).Within(1e-9));
    }

    [Test]
    public void ThePitchIsTheWidestDrawnTagPlusHalfADegreeNeverUnderABodyPlusADegreeAndAHalf()
    {
        var body = PlacePitch.DegreesOf(Body, Distance);
        Assert.That(PlacePitch.PitchDegrees(9.1, body), Is.EqualTo(9.6).Within(1e-9), "a wide tag sets the pitch");
        Assert.That(PlacePitch.PitchDegrees(3.0, body), Is.EqualTo(body + 1.5).Within(1e-9), "a narrow tag never brings bodies closer than the floor");
    }

    [Test]
    public void AtTheLongestNamesARowHoldsThreeAtEitherSizeAndShortNamesStandFour()
    {
        var body = PlacePitch.DegreesOf(Body, Distance);
        // A 200 dp name with its pads: 0.224 m at Standard, 15 percent wider at Larger.
        Assert.That(PlacePitch.RowMost(PlacePitch.DegreesOf(0.224, Distance), body), Is.EqualTo(3), "Standard, longest names");
        Assert.That(PlacePitch.RowMost(PlacePitch.DegreesOf(0.224 * 1.15, Distance), body), Is.EqualTo(3), "Larger, longest names");
        Assert.That(PlacePitch.RowMost(PlacePitch.DegreesOf(0.12, Distance), body), Is.EqualTo(4), "short names stand more");
    }

    [Test]
    public void ARowsExtentWithItsOuterTagsStaysInsideTheSpan()
    {
        var body = PlacePitch.DegreesOf(Body, Distance);
        foreach (var tag in new[] { 0.10, 0.12, 0.16, 0.20, 0.224, 0.2576, 0.30, 0.40 })
        {
            var degrees = PlacePitch.DegreesOf(tag, Distance);
            var most = PlacePitch.RowMost(degrees, body);
            var pitch = PlacePitch.PitchDegrees(degrees, body);
            var extent = (most - 1) * pitch + System.Math.Max(degrees, body);
            Assert.That(most == 1 || extent <= PlacePitch.SpanDegrees + 1e-9, $"{most} of a {tag} m tag reach {extent:0.0} degrees");
            Assert.That(extent + pitch > PlacePitch.SpanDegrees - 1e-9, $"one more of a {tag} m tag would still fit");
        }
    }
}

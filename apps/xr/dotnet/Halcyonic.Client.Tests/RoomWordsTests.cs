using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The agent's words band (DESIGN's BUILD-SPEC-v4 1.6): parts of three lines with the product's way to the next one, the
/// band's height by its lines, the words made ready to lay, and the faded ground's numbers.
/// </summary>
public class RoomWordsTests
{
    [Test]
    public void ShortWordsAreOnePartWithNoWayOn()
    {
        foreach (var lines in new[] { 1, 2, 3 })
        {
            var words = RoomWords.Of(lines);
            Assert.That(words.Parts, Is.EqualTo(1));
            Assert.That(words.First, Is.EqualTo(0));
            Assert.That(words.Count, Is.EqualTo(lines));
            Assert.That(words.More, Is.False);
            Assert.That(words.Next, Is.Null);
        }
    }

    [Test]
    public void LongerWordsPageByPartsOfThreeLinesInTheProductsWords()
    {
        var first = RoomWords.Of(7);
        Assert.That(first.Parts, Is.EqualTo(3));
        Assert.That((first.First, first.Count), Is.EqualTo((0, 3)));
        Assert.That((first.Next, first.NextCount), Is.EqualTo(("Next Part", "2 of 3")), "the way and its count given apart, never joined on a dot");

        var second = first.Following();
        Assert.That((second.Part, second.First, second.Count), Is.EqualTo((1, 3, 3)));
        Assert.That((second.Next, second.NextCount), Is.EqualTo(("Next Part", "3 of 3")));

        var last = second.Following();
        Assert.That((last.Part, last.First, last.Count), Is.EqualTo((2, 6, 1)));
        Assert.That(last.Next, Is.Null);
        Assert.That(last.Following(), Is.SameAs(last), "the last part has no next one");
    }

    [Test]
    public void EveryLineStandsInExactlyOnePart()
    {
        for (var lines = 0; lines <= 40; lines++)
        {
            var seen = 0;
            var words = RoomWords.Of(lines);
            for (var part = 0; part < words.Parts; part++)
            {
                var at = RoomWords.Of(lines, part);
                Assert.That(at.First, Is.EqualTo(seen));
                Assert.That(at.Count, Is.InRange(1, RoomWords.LinesAPart));
                seen += at.Count;
            }
            Assert.That(seen, Is.EqualTo(lines));
        }
    }

    [Test]
    public void NoWordsMakeNoPartAndAPartStaysInsideTheParts()
    {
        var none = RoomWords.Of(0);
        Assert.That((none.Parts, none.Count, none.Next), Is.EqualTo((0, 0, (string?)null)));
        Assert.That(RoomWords.Of(4, 9).Part, Is.EqualTo(1));
        Assert.That(RoomWords.Of(4, -2).Part, Is.EqualTo(0));
    }

    [Test]
    public void TheBandStandsTwentySixAboveAndBelowItsLines()
    {
        Assert.That(RoomWords.HeightDp(1), Is.EqualTo(80f));
        Assert.That(RoomWords.HeightDp(3), Is.EqualTo(136f), "the spec's band of three lines");
        Assert.That(RoomWords.HeightDp(3, RoomGeometry.LargerScale), Is.EqualTo(136f * 1.15f).Within(1e-3f));
    }

    [Test]
    public void TheWordsKeepTheirOwnLinesAndReadAsWritten()
    {
        Assert.That(RoomWords.Laid("I added the golden balloon.\n\n  It pops\twith a gold burst.  \r\n"), Is.EqualTo("I added the golden balloon.\nIt pops with a gold burst."));
        Assert.That(RoomWords.Laid("<b>bold</b> C:\\game"), Is.EqualTo("<b>bold</b> C:\\\\game"), "no markup, and a backslash a label shows as itself");
        Assert.That(RoomWords.Laid(" \n\t\n"), Is.Empty);
        Assert.That(RoomWords.Laid(null), Is.Empty);
    }

    [Test]
    public void TheGroundFadesOverItsEdgesAndIsWholeUnderEveryWord()
    {
        const float Width = RoomWords.WidthDp, Height = 136f;
        Assert.That(RoomWords.Ground(0f, 68f, Width, Height), Is.EqualTo(0f));
        Assert.That(RoomWords.Ground(60f, 68f, Width, Height), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(RoomWords.Ground(Width - 60f, 68f, Width, Height), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(RoomWords.Ground(440f, 11f, Width, Height), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(RoomWords.Ground(440f, Height, Width, Height), Is.EqualTo(0f));
        Assert.That(RoomWords.Ground(60f, 11f, Width, Height), Is.EqualTo(0.25f).Within(1e-5f), "the corners take both fades");
        // The measure's corners, where the first and last words can stand: the full ground.
        var left = (Width - RoomWords.MeasureDp) / 2f;
        foreach (var x in new[] { left, Width - left })
        foreach (var y in new[] { RoomWords.PadDp, Height - RoomWords.PadDp })
            Assert.That(RoomWords.Ground(x, y, Width, Height), Is.EqualTo(1f));
    }

    [Test]
    public void ThePagesAlphaComesToAboutNinetyOnePercentInLinearLight()
    {
        var ground = 20f / 255f;
        Assert.That(RoomWords.LinearAlpha(RoomWords.GroundAlpha, ground), Is.EqualTo(0.91f).Within(0.01f));
        Assert.That(RoomWords.LinearAlpha(0f, ground), Is.EqualTo(0f).Within(1e-6f));
        Assert.That(RoomWords.LinearAlpha(1f, ground), Is.EqualTo(1f).Within(1e-6f));
        var last = 0f;
        for (var step = 1; step <= 20; step++)
        {
            var alpha = RoomWords.LinearAlpha(step / 20f, ground);
            Assert.That(alpha, Is.GreaterThan(last));
            last = alpha;
        }
    }
}

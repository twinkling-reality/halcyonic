using System;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The seated rule (the lead, 2026-11-17T19:05Z; FIELD's sheet v3): both sides first, nearest first, nothing behind, the front clear.</summary>
public class PlaceBearingsTests
{
    [Test]
    public void PlacesFillBothSidesNearestFirstThenADeeperRingAtTheSameBearings()
    {
        var first = Enumerable.Range(0, 4).Select(PlaceBearings.Of).ToList();
        Assert.That(first.Select(place => place.YawDegrees), Is.EqualTo(new[] { -46.75f, 46.75f, -83.25f, 83.25f }));
        Assert.That(first.All(place => place.Ring == 0 && place.Distance == PlaceBearings.RingOneDistance && place.Drop == 0f), Is.True);
        var second = PlaceBearings.Of(4);
        Assert.That((second.YawDegrees, second.Ring), Is.EqualTo((-46.75f, 1)));
        Assert.That(second.Distance, Is.EqualTo(2.0f).Within(1e-5f), "ring two at 2.0 m");
        Assert.That(second.Drop, Is.EqualTo(PlaceBearings.RingDrop), "and lower");
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaceBearings.Of(-1));
    }

    [Test]
    public void NoPlaceReachesBehindThePersonOrIntoTheFront()
    {
        for (var index = 0; index < 16; index++)
        {
            var place = PlaceBearings.Of(index);
            var near = Math.Abs(place.YawDegrees) - PlaceBearings.SpanDegrees / 2f;
            var far = Math.Abs(place.YawDegrees) + PlaceBearings.SpanDegrees / 2f;
            Assert.That(near, Is.GreaterThanOrEqualTo(PlaceBearings.FrontClearDegrees), "the front stays clear for the work or the show");
            Assert.That(far, Is.LessThanOrEqualTo(PlaceBearings.ReachDegrees), "nothing past the seated reach, never behind");
        }
    }

    [Test]
    public void NeighbouringPlacesKeepTheirGap()
    {
        Assert.That(PlaceBearings.LeastGapDegrees, Is.GreaterThanOrEqualTo(PlaceBearings.GapDegrees), "4 degrees clear between neighbouring teams");
    }
}

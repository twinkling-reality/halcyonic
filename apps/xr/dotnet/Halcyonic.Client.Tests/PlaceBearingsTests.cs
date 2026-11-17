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
        var first = Enumerable.Range(0, 4).Select(index => PlaceBearings.Of(index)).ToList();
        Assert.That(first.Select(place => place.YawDegrees), Is.EqualTo(new[] { -46.8f, 46.8f, -83.4f, 83.4f }));
        Assert.That(first.All(place => place.Ring == 0 && place.Distance == PlaceBearings.RingOneDistance && place.Drop == 0f), Is.True);
        var second = PlaceBearings.Of(4);
        Assert.That((second.YawDegrees, second.Ring), Is.EqualTo((-46.8f, 1)));
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
    public void PastFourPlacesOneRingSharesEachSidesSpanAndNoneStandsBehindAnother()
    {
        // Up to four, the ring one places unchanged.
        for (var count = 1; count <= 4; count++)
            for (var index = 0; index < count; index++)
                Assert.That(PlaceBearings.Of(index, count).YawDegrees, Is.EqualTo(PlaceBearings.Of(index).YawDegrees));
        foreach (var count in new[] { 5, 6, 8, 11 })
        {
            var places = Enumerable.Range(0, count).Select(index => PlaceBearings.Of(index, count)).ToList();
            Assert.That(places.All(place => place.Ring == 0 && place.Drop == 0f && place.Distance == PlaceBearings.RingOneDistance), Is.True, count + ": one ring");
            foreach (var place in places)
            {
                Assert.That(Math.Abs(place.YawDegrees) - place.SpanDegrees / 2f, Is.GreaterThanOrEqualTo(PlaceBearings.FrontClearDegrees - 1e-3f), count + ": the front clear");
                Assert.That(Math.Abs(place.YawDegrees) + place.SpanDegrees / 2f, Is.LessThanOrEqualTo(PlaceBearings.ReachDegrees + 1e-3f), count + ": inside the reach");
            }
            foreach (var side in new[] { -1, 1 })
            {
                var row = places.Where(place => Math.Sign(place.YawDegrees) == side).OrderBy(place => Math.Abs(place.YawDegrees)).ToList();
                for (var at = 1; at < row.Count; at++)
                    Assert.That(Math.Abs(row[at].YawDegrees) - row[at].SpanDegrees / 2f - (Math.Abs(row[at - 1].YawDegrees) + row[at - 1].SpanDegrees / 2f),
                        Is.GreaterThanOrEqualTo(PlaceBearings.GapDegrees - 1e-3f), count + ": the gap between neighbours");
            }
        }
    }

    [Test]
    public void NeighbouringPlacesKeepTheirGap()
    {
        Assert.That(PlaceBearings.LeastGapDegrees, Is.GreaterThanOrEqualTo(PlaceBearings.GapDegrees), "4 degrees clear between neighbouring teams");
    }
}

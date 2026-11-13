using System;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The shelf standing aside while the plane is open (the lead's GO of 2026-11-13T19:34Z, pick B; AUDIT's conditions 1 and 4).</summary>
public class ShelfAsideTests
{
    // A plane's largest footprint at its width, about its heading: 40 degrees across, from 18 below to 10 above eye level.
    private static readonly AngularExtent Plane = new(-20f, 20f, -18f, 10f);

    [Test]
    public void AShelfTheFootprintDoesNotMeetStaysInItsOwnPlace()
    {
        var aside = ShelfAside.For(40f, 8f, -30f, -15f, Plane, 0f, 0f, 48f);
        Assert.That((aside.Yaw, aside.Moved, aside.Clears, aside.PastThirty), Is.EqualTo((40f, 0f, true, false)));
        var below = ShelfAside.For(0f, 8f, -40f, -25f, Plane, 0f, 0f, 48f);
        Assert.That((below.Yaw, below.Moved, below.Clears), Is.EqualTo((0f, 0f, true)), "under the footprint by more than the clearance");
    }

    [Test]
    public void AShelfUnderTheGlassTakesTheSmallerTurnThatClears()
    {
        // At 5 degrees right of the plane's middle: 25 degrees right clears (its near edge 1.5 past the footprint), 35 left would.
        var aside = ShelfAside.For(5f, 8f, -30f, -15f, Plane, 0f, 0f, 48f);
        Assert.That((aside.Yaw, aside.Moved, aside.Clears, aside.PastThirty), Is.EqualTo((30f, 25f, true, false)));
        var left = ShelfAside.For(-5f, 8f, -30f, -15f, Plane, 0f, 0f, 48f);
        Assert.That((left.Yaw, left.Moved), Is.EqualTo((-30f, -25f)), "and to the left from the left");
        var turned = ShelfAside.For(95f, 8f, -30f, -15f, Plane, 90f, 90f, 48f);
        Assert.That((turned.Yaw, turned.Moved), Is.EqualTo((120f, 25f)), "about wherever the plane stands");
    }

    [Test]
    public void PastThirtyDegreesItGoesAsFarAsClearsAndSaysSo()
    {
        var wide = new AngularExtent(-40f, 40f, -18f, 10f);
        var aside = ShelfAside.For(0f, 8f, -30f, -15f, wide, 0f, 0f, 90f);
        Assert.That((aside.Yaw, aside.Clears, aside.PastThirty), Is.EqualTo((50f, true, true)));
    }

    /// <summary>
    /// The plane is the T it draws, the bar and the band under it, never their bounding box (AUDIT's 22:34Z): a shelf beside the bar
    /// above the band stays where it is; one under the band stands aside past the band's side.
    /// </summary>
    [Test]
    public void TheShelfClearsTheBarAndTheBandAsTheyAreDrawnNeverTheirBox()
    {
        var parts = new[] { new AngularExtent(-14f, 14f, -17.6f, 10f), new AngularExtent(-27.5f, 27.5f, -36f, -24.5f) };
        var beside = ShelfAside.For(20f, 3f, -15f, -5f, parts, 0f, 0f, 90f);
        Assert.That((beside.Yaw, beside.Moved, beside.Clears), Is.EqualTo((20f, 0f, true)), "beside the bar, above the band: no move");
        var box = new AngularExtent(-27.5f, 27.5f, -36f, 10f);
        Assert.That(ShelfAside.For(20f, 3f, -15f, -5f, box, 0f, 0f, 90f).Moved, Is.Not.EqualTo(0f), "the box would have moved it for nothing");
        var under = ShelfAside.For(0f, 3f, -32f, -28f, parts, 0f, 0f, 90f);
        Assert.That(under.Clears, Is.True);
        Assert.That(Math.Abs(under.Yaw), Is.GreaterThanOrEqualTo(27.5f + WorkspacePlacement.ClearanceDegrees + 3f - 1f), "past the band's side");
    }

    [Test]
    public void NothingIsEverPutPastTheFieldsEdgeAShelfThatCannotClearInsideItStaysAndIsAFinding()
    {
        var wide = new AngularExtent(-40f, 40f, -18f, 10f);
        var aside = ShelfAside.For(0f, 8f, -30f, -15f, wide, 0f, 0f, 48f);
        Assert.That((aside.Yaw, aside.Moved, aside.Clears), Is.EqualTo((0f, 0f, false)));
    }
}

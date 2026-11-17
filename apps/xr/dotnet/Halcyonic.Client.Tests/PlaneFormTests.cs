using System;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The plane's form: the widest that clears every shelf, decided again only on an event, never while a person reads.</summary>
public class PlaneFormTests
{
    /// <summary>
    /// The plane's form carries the kit's own line measure (the lead's 20:15Z: eight lines means eight lines): it survives the
    /// plane asking for fewer rows or actions, and the general reader pages by it.
    /// </summary>
    [Test]
    public void AMeasuredFormKeepsItsLineMeasureWhenThePlaneAsksForLessAndTheReaderPagesByIt()
    {
        var calls = 0;
        var form = RoomForm.Full.Measured((row, marked) => { calls++; return 3; });
        var frame = new MenuFrame("Settings", new Footer(new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close)),
            lines: new[] { new PageLine("One"), new PageLine("Two"), new PageLine("Three") });
        foreach (var asked in new[] { form, form.Fewer(), form.FewerActions(), form.Fewer().Fewer() })
        {
            Assert.That(asked.LinesOf, Is.Not.Null);
            calls = 0;
            var bar = RoomBar.Of(KitFileBinding.PlainOf(frame), VoiceStage.Idle, false, form: asked);
            Assert.That(calls, Is.GreaterThan(0), "the reader measures by the form's own function");
            Assert.That(bar.Whole, Is.False, "three rows of three lines pass eight lines a page, so they page");
        }
    }

    /// <summary>
    /// The width rule (the lead's 18:07Z and 19:34Z): the widest measured step at or above the floor that clears; the floor where none
    /// does, the room yielding; never under the floor, whatever clears there.
    /// </summary>
    [Test]
    public void ThePlaneTakesTheWidestStepThatClearsNeverUnderTheFloor()
    {
        var rule = new PlaneWidthRule();
        var floor = PlaneWidth.FloorDp(1f);
        Assert.That(rule.Reconsider(PlaneFormEvent.PlaneMoved, _ => true, floor), Is.False, "the full width clears: as it was");
        Assert.That(rule.WidthDp, Is.EqualTo(RoomGeometry.PanelDp));
        Assert.That(rule.Reconsider(PlaneFormEvent.ShelfChanged, width => width <= 400f, floor), Is.True);
        Assert.That((rule.WidthDp, rule.Cause), Is.EqualTo((400f, (PlaneFormEvent?)PlaneFormEvent.ShelfChanged)), "the widest that clears");
        Assert.That(rule.Reconsider(PlaneFormEvent.ShelfChanged, width => width <= 280f, floor), Is.True);
        Assert.That(rule.WidthDp, Is.EqualTo(floor), "only under the floor clears: the floor, the room yields");
        Assert.That(rule.Reconsider(PlaneFormEvent.TextSizeChanged, _ => false, PlaneWidth.FloorDp(Comfort.LargerTextScale)), Is.True);
        Assert.That(rule.WidthDp, Is.EqualTo(PlaneWidth.LargerFloorDp), "nothing clears at Larger: its own floor");
        Assert.That(rule.Reconsider(PlaneFormEvent.CharactersChanged, _ => true, PlaneWidth.FloorDp(Comfort.LargerTextScale)), Is.True);
        Assert.That((rule.WidthDp, rule.Cause), Is.EqualTo((RoomGeometry.PanelDp, (PlaneFormEvent?)PlaneFormEvent.CharactersChanged)));
    }

    /// <summary>The floors are FIELD's measured ones (59-width-table, F106, 2026-11-13T20:53Z), and every step the rule takes is measured.</summary>
    [Test]
    public void TheFloorsAreTheMeasuredOnesAndEveryStepIsInTheRecord()
    {
        Assert.That((PlaneWidth.StandardFloorDp, PlaneWidth.LargerFloorDp), Is.EqualTo((360f, 400f)), "F106: the recap card whole at and above these");
        Assert.That(PlaneWidth.Steps, Is.EqualTo(new[] { 440f, 400f, 360f, 320f, 280f, 240f }), "the widths FIELD's envelope records");
        Assert.That(PlaneWidth.Steps, Does.Contain(PlaneWidth.StandardFloorDp).And.Contain(PlaneWidth.LargerFloorDp));
        Assert.That(new RoomForm(false, 8, panelDp: 100f).PanelDp, Is.EqualTo(PlaneWidth.NarrowestDp), "never laid under the narrowest measured");
        Assert.That(RoomForm.Full.AtWidth(360f).Fewer().FewerActions().PanelDp, Is.EqualTo(360f), "the width kept as the plane asks for less");
    }

    /// <summary>A placing that ends waits only for its shelf to stand: never longer, never over another change, a cancel decided at once.</summary>
    [Test]
    public void APlacingThatEndsWaitsOnlyTheFramesItsShelfNeeds()
    {
        Assert.That(PlaneWidthRule.AwaitsPlacedShelf(true, false, shelfStands: false, otherChange: false, framesSincePlacingEnded: 0), Is.True, "the shelf stands a frame later");
        Assert.That(PlaneWidthRule.AwaitsPlacedShelf(true, false, false, false, PlaneWidthRule.PlacedShelfFrames), Is.False, "a cancelled placing: no shelf will come");
        Assert.That(PlaneWidthRule.AwaitsPlacedShelf(true, false, true, false, 0), Is.False, "the shelf stands: decide now, once");
        // XR's 21:34Z: a placing closed with nothing applied waits for no shelf, even where the room says one was placed before.
        Assert.That(PlaneWidthRule.AwaitsPlacedShelf(true, false, false, false, 0, shelfComing: true, cancelled: true), Is.False, "a cancel brings no shelf");
        Assert.That(PlaneWidthRule.AwaitsPlacedShelf(true, false, false, false, 0, shelfComing: true), Is.True, "an Apply's shelf is waited for");
        Assert.That(PlaneWidthRule.AwaitsPlacedShelf(true, false, false, otherChange: true, 0), Is.False, "the text size changed: decide now");
        Assert.That(PlaneWidthRule.AwaitsPlacedShelf(false, false, false, false, 0), Is.False, "no placing ended");
        Assert.That(PlaneWidthRule.AwaitsPlacedShelf(true, false, false, false, PlaneWidthRule.PlacedShelfFrames, shelfComing: true), Is.True,
            "the room applied a shelf the stage has not stood yet: wait past the plane's own draws");
        Assert.That(PlaneWidthRule.AwaitsPlacedShelf(true, false, false, false, PlaneWidthRule.ComingShelfFrames, shelfComing: true), Is.False,
            "never longer than its bound");
    }

    [Test]
    public void TheWidthIsHeldBetweenEventsWhateverTheCharactersDo()
    {
        var rule = new PlaneWidthRule();
        rule.Reconsider(PlaneFormEvent.ShelfChanged, width => width <= 360f, PlaneWidth.FloorDp(1f));
        // A character wandering is no event: nothing asks the rule again, so the width read a moment ago is the width now.
        Assert.That(rule.WidthDp, Is.EqualTo(360f));
        Assert.That(System.Enum.GetNames(typeof(PlaneFormEvent)), Does.Not.Contain("CharacterMoved"));
    }
}

public class AngularExtentTests
{
    /// <summary>
    /// A part reasoned through the plane's own geometry: a point u units up from the plane's middle stands atan(u) above it, and the part
    /// moves with the person's raising or lowering of the plane (AUDIT's 01:07Z, the drag the removed OfBar test pinned).
    /// </summary>
    [Test]
    public void APartOnThePlaneStandsWhereThePlaneLaysItAndMovesWithThePersonsDrag()
    {
        // Narrow, so its top and bottom are where it crosses the plane's own vertical, where a point u units up stands atan(u) above the middle.
        var level = new PanelDirection(0f, 3f, true, false);
        var band = AngularExtent.OnPlane(level, 0f, 0f, -1e-4f, 1e-4f, -0.6f, -0.45f);
        Assert.That(band.Top, Is.EqualTo(3f + MathF.Atan(-0.45f) * 180f / MathF.PI).Within(1e-3f), "atan of its units under the middle");
        var wide = AngularExtent.OnPlane(level, 0f, 0f, -0.5f, 0.5f, -0.6f, -0.45f);
        Assert.That(wide.Right, Is.EqualTo(-wide.Left).Within(1e-3f));
        Assert.That(wide.Top, Is.GreaterThan(band.Top), "below the eyes, a wide part's corners stand higher than its middle");
        var raised = AngularExtent.OnPlane(new PanelDirection(0f, 8f, true, false), 0f, 0f, -1e-4f, 1e-4f, -0.6f, -0.45f);
        Assert.That(raised.Top - band.Top, Is.EqualTo(5f).Within(1e-3f), "raised five degrees by the person: the part with it");
        Assert.That(raised.Bottom - band.Bottom, Is.EqualTo(5f).Within(1e-3f));
        var turned = AngularExtent.OnPlane(new PanelDirection(30f, 3f, true, false), 0f, 0f, -0.5f, 0.5f, -0.6f, -0.45f);
        Assert.That(turned.Left, Is.EqualTo(wide.Left).Within(1e-3f), "about the plane's own heading");
        Assert.That(turned.Right, Is.EqualTo(wide.Right).Within(1e-3f));
    }

    [Test]
    public void TwoExtentsAreApartByTheirGapOrPastACornerAndOverlapIsNoGap()
    {
        var plane = new AngularExtent(-10f, 10f, -20f, 0f);
        Assert.That(plane.Apart(new AngularExtent(13f, 20f, -20f, 0f)), Is.EqualTo(3f).Within(1e-4f), "beside it");
        Assert.That(plane.Apart(new AngularExtent(13f, 20f, 4f, 10f)), Is.EqualTo(5f).Within(1e-4f), "past its corner: three across, four up");
        Assert.That(plane.Apart(new AngularExtent(-5f, 5f, -5f, 5f)), Is.LessThanOrEqualTo(0f), "overlapping");
    }
}

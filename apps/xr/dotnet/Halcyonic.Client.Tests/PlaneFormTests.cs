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

    [Test]
    public void TheFullPlaneStandsWhereItClearsAndTheNarrowOneWhereOnlyItDoes()
    {
        var rule = new PlaneFormRule();
        Assert.That(rule.Reconsider(PlaneFormEvent.PlaneMoved, _ => true), Is.False, "full clears: full, as it was");
        Assert.That(rule.Form, Is.EqualTo(PlaneForm.Full));
        Assert.That(rule.Reconsider(PlaneFormEvent.ShelfChanged, form => form == PlaneForm.Narrow), Is.True);
        Assert.That((rule.Form, rule.Cause), Is.EqualTo((PlaneForm.Narrow, (PlaneFormEvent?)PlaneFormEvent.ShelfChanged)));
        Assert.That(rule.Reconsider(PlaneFormEvent.TextSizeChanged, _ => false), Is.False, "neither clears: narrow all the same");
        Assert.That(rule.Reconsider(PlaneFormEvent.CharactersChanged, _ => true), Is.True, "the shelf gone: full again");
        Assert.That((rule.Form, rule.Cause), Is.EqualTo((PlaneForm.Full, (PlaneFormEvent?)PlaneFormEvent.CharactersChanged)));
    }

    [Test]
    public void TheFormIsHeldBetweenEventsWhateverTheCharactersDo()
    {
        var rule = new PlaneFormRule();
        rule.Reconsider(PlaneFormEvent.ShelfChanged, form => form == PlaneForm.Narrow);
        // A character wandering is no event: nothing asks the rule again, so the form read a moment ago is the form now.
        Assert.That(rule.Form, Is.EqualTo(PlaneForm.Narrow));
        Assert.That(System.Enum.GetNames(typeof(PlaneFormEvent)), Does.Not.Contain("CharacterMoved"));
    }
}

public class AngularExtentTests
{
    /// <summary>The largest bar stands from its foot line up, and moves with the person's own raising or lowering of the plane.</summary>
    [Test]
    public void TheLargestBarStandsFromItsFootLineMovedByThePersonsDrag()
    {
        var level = AngularExtent.OfBar(0.8f, 0.2f, footPitch: -17.6f, placedPitch: -25f, standingPitch: -25f);
        Assert.That(level.Bottom, Is.EqualTo(-17.6f).Within(1e-4f));
        Assert.That(level.Top - level.Bottom, Is.EqualTo(MathF.Atan(0.2f) * 180f / MathF.PI).Within(1e-4f));
        Assert.That(level.Right, Is.EqualTo(-level.Left).Within(1e-4f));
        var raised = AngularExtent.OfBar(0.8f, 0.2f, footPitch: -17.6f, placedPitch: -25f, standingPitch: -20f);
        Assert.That(raised.Bottom, Is.EqualTo(-12.6f).Within(1e-4f), "raised five degrees by the person: its foot with it");
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

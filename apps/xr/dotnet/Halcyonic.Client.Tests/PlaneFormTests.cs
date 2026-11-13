using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The plane's form: the widest that clears every shelf, decided again only on an event, never while a person reads.</summary>
public class PlaneFormTests
{
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

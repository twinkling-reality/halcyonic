using System.Linq;
using NUnit.Framework;
using static Halcyonic.Client.Tests.HeadsetPlayTests;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The one list of a project's places, which a press and a spoken request both read: Play in Headset is in it exactly
/// where it stands, and an index the assistant picks maps back to the place it named.
/// </summary>
public class ProjectPlacesTests
{
    [Test]
    public void PlayInHeadsetIsAPlaceExactlyWhereItStands()
    {
        var running = View(Check(), Preview());
        var places = ProjectPlaces.Of(running, Cable, demonstration: false);
        Assert.That(places.Select(place => place.Kind), Is.EqualTo(new[] { ProjectPlaceKind.PlayInHeadset }));
        Assert.That(places[0].Label, Is.EqualTo(HeadsetPlay.ActionWords));
        Assert.That(places[0].ProjectId, Is.EqualTo(ProjectId));
        // No preview of the latest check yet: going there starts one, so it is still a place.
        Assert.That(ProjectPlaces.Of(View(Check()), Cable, demonstration: false).Select(place => place.Kind), Is.EqualTo(new[] { ProjectPlaceKind.PlayInHeadset }));
        // Over the pairing it stands only where the app can relay.
        Assert.That(ProjectPlaces.Of(running, Paired, demonstration: false), Is.Empty);
        Assert.That(ProjectPlaces.Of(running, Paired, demonstration: false, relays: true).Select(place => place.Kind), Is.EqualTo(new[] { ProjectPlaceKind.PlayInHeadset }));
    }

    [Test]
    public void NothingIsAPlaceWhereItCannotOpen()
    {
        Assert.That(ProjectPlaces.Of(View(Check(readsConfined: false), Preview()), Cable, demonstration: false), Is.Empty, "kept on the computer");
        Assert.That(ProjectPlaces.Of(View(Check(passed: false), Preview()), Cable, demonstration: false), Is.Empty, "a check that did not pass");
        Assert.That(ProjectPlaces.Of(View(), Cable, demonstration: false), Is.Empty, "no check");
        Assert.That(ProjectPlaces.Of(View(Check(), Preview()), Cable, demonstration: true), Is.Empty, "a demonstration");
        Assert.That(ProjectPlaces.Of(View(Check(), Preview()), null, demonstration: false), Is.Empty, "no computer");
        Assert.That(ProjectPlaces.Of(View(Check(), Preview()), Paired, demonstration: false, relays: false, relayBuilt: false), Is.Empty, "a build without the relay");
    }

    [Test]
    public void TheAssistantsIndexNamesThePlaceItsLabelSaid()
    {
        var places = ProjectPlaces.Of(View(Check(), Preview()), Cable, demonstration: false);
        Assert.That(ProjectPlaces.Labels(places), Is.EqualTo(new[] { HeadsetPlay.ActionWords }));
        Assert.That(ProjectPlaces.At(places, 0)?.Kind, Is.EqualTo(ProjectPlaceKind.PlayInHeadset));
        Assert.That(ProjectPlaces.At(places, 1), Is.Null);
        Assert.That(ProjectPlaces.At(places, -1), Is.Null);
        Assert.That(places.Count, Is.LessThanOrEqualTo(ProjectPlaces.Most));
    }
}

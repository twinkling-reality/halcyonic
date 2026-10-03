using System;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The menu's plane held by a file's subject and dragged round the eyes (ADR 0026).</summary>
[TestFixture]
public class MenuDragTests
{
    /// <summary>The menu and a file side by side, four rows a page.</summary>
    private static PlaneComposition MenuAndFile()
    {
        var subject = MenuPage.Subject(1, pill: true);
        var page = MenuPage.Content(MenuPage.Rows(4));
        return new PlaneComposition(new[]
        {
            new PlaneColumn(PlaneComposition.Units(Glaze.Menu.MenuColumnDegrees), subject, MenuPage.Sections, page),
            new PlaneColumn(PlaneComposition.Units(Glaze.Menu.FileColumnDegrees), subject, MenuPage.Sections, page),
        });
    }

    private static readonly PanelDirection Placed = new(0f, -25f, true, false);

    [Test]
    public void TheHeldPlaneTurnsRoundTheEyesWithTheHandAndTheNextHoldStartsWhereItWasLeft()
    {
        var composition = MenuAndFile();
        var drag = new MenuDrag(Placed, (0f, 0f), 0f, -20f, composition, Array.Empty<BodyInView>());
        Assert.That(drag.Follow(12f, -20f), Is.True);
        Assert.That(drag.Moved.Yaw, Is.EqualTo(12f).Within(1e-4));
        Assert.That(drag.Moved.Elevation, Is.EqualTo(0f).Within(1e-4));
        Assert.That(drag.Follow(12f, -23f), Is.True);
        Assert.That((drag.At.Yaw, drag.At.Elevation), Is.EqualTo((12f, -28f)).Within(1e-4));
        Assert.That(drag.Follow(12f, -23f), Is.False, "the hand stayed where it was");

        var next = new MenuDrag(Placed, drag.Moved, 12f, -23f, composition, Array.Empty<BodyInView>());
        Assert.That(next.Follow(2f, -23f), Is.True);
        Assert.That(next.Moved.Yaw, Is.EqualTo(2f).Within(1e-4), "the offset on where the stage placed it, kept from the last hold");
        Assert.That(next.Moved.Elevation, Is.EqualTo(-3f).Within(1e-4));
    }

    [Test]
    public void AStepInFrontOfACharactersLabelIsNotTakenAndTheRestOfTheDragGoesOn()
    {
        var composition = MenuAndFile();
        // A character straight ahead whose label reaches 9 degrees below eye level; the stage placed the plane under it.
        var character = new BodyInView(0f, -2f, 3f, lowest: -9f, halfWidth: 6f);
        var bodies = new[] { character };
        var placed = WorkspacePlacement.Place(0f, character, bodies, composition.Size);
        Assert.That(WorkspacePlacement.Clears(placed, bodies, composition.Size), Is.True, "where the stage placed it");
        var drag = new MenuDrag(placed, (0f, 0f), 0f, placed.Elevation, composition, bodies);
        Assert.That(drag.Follow(0f, placed.Elevation + 6f), Is.False, "up into the label: holding does nothing");
        Assert.That(drag.Moved, Is.EqualTo((0f, 0f)));
        Assert.That(drag.Follow(0f, placed.Elevation - 4f), Is.True, "down, away from it, it follows");
        Assert.That(drag.Moved.Elevation, Is.EqualTo(-4f).Within(1e-4));
    }

    [Test]
    public void AStepTheHostRefusesIsNotTakenAndItIsAskedLastWithTheOffset()
    {
        var asked = new System.Collections.Generic.List<(float, float)>();
        var drag = new MenuDrag(Placed, (0f, 0f), 0f, -20f, MenuAndFile(), Array.Empty<BodyInView>(), holds: moved =>
        {
            asked.Add(moved);
            return moved.Yaw < 5f;
        });
        Assert.That(drag.Follow(3f, -20f), Is.True);
        Assert.That(drag.Follow(8f, -20f), Is.False, "the light line would cross another character there");
        Assert.That(drag.Moved.Yaw, Is.EqualTo(3f).Within(1e-4), "it stays where it last was");
        Assert.That(asked.Count, Is.EqualTo(2));
        Assert.That(asked[1].Item1, Is.EqualTo(8f).Within(1e-4));
    }

    [Test]
    public void ADragFarDownStopsInsideTheHeadsetsField()
    {
        var composition = MenuAndFile();
        var quest3S = new ViewField(48, 48, 45, 45);
        var drag = new MenuDrag(Placed, (0f, 0f), 0f, -20f, composition, Array.Empty<BodyInView>(), field: quest3S);
        drag.Follow(0f, -90f);
        Assert.That(MenuPage.Inside(composition, drag.At, quest3S), Is.True, "it stops where its lowest corners still show");
        Assert.That(drag.At.Elevation, Is.LessThanOrEqualTo(Placed.Elevation));
    }

    [Test]
    public void TurnedWrapsRoundTheEyes()
    {
        var turned = MenuDrag.Turned(new PanelDirection(170f, -20f, true, true), (20f, -2f));
        Assert.That((turned.Yaw, turned.Elevation, turned.Clear, turned.Above), Is.EqualTo((-170f, -22f, true, true)));
    }
}

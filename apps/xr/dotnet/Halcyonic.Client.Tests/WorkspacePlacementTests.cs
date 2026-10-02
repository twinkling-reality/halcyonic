using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The stage's lineups as a seated person sees them, and the workspace's size.</summary>
internal static class Lineups
{
    /// <summary>The six slots, 12 degrees apart, 60 between the outermost.</summary>
    public static readonly float[] Slots = { -30f, -18f, -6f, 6f, 18f, 30f };

    /// <summary>The workspace 0.6 m from the eyes, scaled from its 0.80 by 0.62 m design at 1.3 m.</summary>
    public static readonly PanelSize Workspace = new(0.6f, 0.40f * 0.6f / 1.3f, 0.31f * 0.6f / 1.3f);

    /// <summary>
    /// Bodies at a horizontal distance, their centers a drop below the eyes, reaching 0.075 of the
    /// stage's scale around their centers; the scale is the distance.
    /// </summary>
    public static List<BodyInView> Arc(float distance, float drop, IDictionary<int, float>? risen = null)
    {
        var bodies = new List<BodyInView>();
        for (var slot = 0; slot < Slots.Length; slot++)
        {
            var bodyDrop = drop - (risen != null && risen.TryGetValue(slot, out var rise) ? rise : 0f);
            bodies.Add(Body(Slots[slot], distance, bodyDrop, 0.075f * distance));
        }
        return bodies;
    }

    public static BodyInView Body(float yaw, float distance, float drop, float reach)
    {
        var range = MathF.Sqrt(distance * distance + drop * drop);
        return new BodyInView(yaw, -MathF.Atan2(drop, distance) * 180f / MathF.PI, MathF.Asin(MathF.Min(1f, reach / range)) * 180f / MathF.PI);
    }

    /// <summary>
    /// A body with its label plate under it, the plate's bottom <paramref name="labelDrop"/> below the
    /// body's center and half as wide as <paramref name="labelHalfWidth"/>, in meters.
    /// </summary>
    public static BodyInView Labeled(float yaw, float distance, float drop, float reach, float labelDrop, float labelHalfWidth)
    {
        var body = Body(yaw, distance, drop, reach);
        var lowest = -MathF.Atan2(drop + labelDrop, distance) * 180f / MathF.PI;
        var halfWidth = MathF.Max(body.Radius, MathF.Atan2(labelHalfWidth, distance) * 180f / MathF.PI);
        return new BodyInView(body.Yaw, body.Elevation, body.Radius, lowest, halfWidth);
    }

    /// <summary>
    /// The raised stage (ADR 0023): bodies 0.17 m below the eyes 2.4 m away, their plates at most
    /// 10.5 degrees tall under them and 10.5 wide, as the stage's scale makes them at that distance.
    /// </summary>
    public static List<BodyInView> RaisedArc()
    {
        const float distance = 2.4f;
        const float radians = MathF.PI / 180f;
        return Slots.Select(yaw => Labeled(yaw, distance, 0.17f, 0.075f * distance, 10.5f * radians * distance, 5.25f * radians * distance)).ToList();
    }

    /// <summary>Whether the workspace, centered at a direction, covers any part of a character's label.</summary>
    public static bool CoversLabel(PanelDirection panel, BodyInView body)
    {
        var halfWidth = Workspace.HalfWidthDegrees / MathF.Cos(body.Elevation * MathF.PI / 180f);
        var sideways = MathF.Abs(WorkspacePlacement.DeltaAngle(panel.Yaw, body.Yaw)) < halfWidth + body.HalfWidth;
        var upright = panel.Elevation + Workspace.HalfHeightDegrees > body.Lowest && panel.Elevation - Workspace.HalfHeightDegrees < body.Elevation;
        return sideways && upright;
    }

    /// <summary>Whether the workspace, centered at a direction, covers any part of a body.</summary>
    public static bool Covers(PanelDirection panel, BodyInView body)
    {
        var halfWidth = Workspace.HalfWidthDegrees / MathF.Cos(body.Elevation * MathF.PI / 180f);
        var sideways = MathF.Abs(WorkspacePlacement.DeltaAngle(panel.Yaw, body.Yaw)) < halfWidth + body.Radius;
        var upright = MathF.Abs(panel.Elevation - body.Elevation) < Workspace.HalfHeightDegrees + body.Radius;
        return sideways && upright;
    }
}

public class WorkspacePlacementTests
{
    [Test]
    public void WithTheCharacters24MetersAwayTheWorkspaceOpensBelowThemInTheComfortableBand()
    {
        // The stage's default: 2.4 m away, their centers 0.45 m below the eyes, about 10.6 degrees.
        var bodies = Lineups.Arc(2.4f, 0.45f);
        foreach (var slot in new[] { 2, 3, 0, 5 })
        {
            var opened = bodies[slot];
            var panel = WorkspacePlacement.Place(opened.Yaw, opened, bodies, Lineups.Workspace);

            Assert.That(panel.Clear, Is.True, $"slot {slot}");
            Assert.That(panel.Above, Is.False);
            Assert.That(panel.Elevation, Is.InRange(WorkspacePlacement.LowestDegrees, WorkspacePlacement.HighestDegrees));
            Assert.That(bodies.Any(body => Lineups.Covers(panel, body)), Is.False, "every body stays in view");
            Assert.That(panel.Yaw, Is.EqualTo(opened.Yaw));
        }
    }

    [Test]
    public void OnTheRaisedStageTheWorkspaceOpensBelowEveryLabelPlate()
    {
        var characters = Lineups.RaisedArc();
        Assert.That(characters[0].Lowest, Is.InRange(-14.6f, -14f), "plates end about 14 degrees below the eyes");
        foreach (var slot in new[] { 2, 3, 0, 5 })
        {
            var opened = characters[slot];
            var panel = WorkspacePlacement.Place(opened.Yaw, opened, characters, Lineups.Workspace);

            Assert.That(panel.Clear, Is.True, $"slot {slot}");
            Assert.That(panel.Above, Is.False);
            Assert.That(panel.Elevation, Is.InRange(WorkspacePlacement.LowestDegrees, WorkspacePlacement.HighestDegrees));
            Assert.That(characters.Any(character => Lineups.CoversLabel(panel, character)), Is.False, "every title and badge stays readable");
        }
    }

    [Test]
    public void AFlatPanelsCornersClearTheLabelsToo()
    {
        var characters = Lineups.RaisedArc();
        var opened = characters[3];
        var panel = WorkspacePlacement.Place(opened.Yaw, opened, characters, Lineups.Workspace);
        var top = panel.Elevation + Lineups.Workspace.HalfHeightDegrees;
        var corners = WorkspacePlacement.CornerElevation(top, Lineups.Workspace.HalfWidthDegrees);
        Assert.That(corners, Is.GreaterThan(top), "below eye level, a flat panel's corners look higher than its edge's middle");
        Assert.That(corners, Is.LessThanOrEqualTo(characters.Min(character => character.Lowest) - 1f), "a degree or more under every label");
    }

    [Test]
    public void CornersAndEdgesConvertBothWays()
    {
        Assert.That(WorkspacePlacement.CornerElevation(0f, 20f), Is.EqualTo(0f).Within(1e-4f), "at eye level the edge is level");
        Assert.That(WorkspacePlacement.CornerElevation(-16f, 0f), Is.EqualTo(-16f).Within(1e-4f), "a panel no wider than a point has no corners");
        Assert.That(WorkspacePlacement.EdgeForCorners(WorkspacePlacement.CornerElevation(-16f, 17f), 17f), Is.EqualTo(-16f).Within(1e-3f));
        Assert.That(WorkspacePlacement.CornerElevation(10f, 17f), Is.LessThan(10f), "above eye level the corners look lower");
    }

    [Test]
    public void WithoutItsLabelABodyReachesOnlyItsOwnExtent()
    {
        var body = new BodyInView(10f, -4f, 4.3f);
        Assert.That(body.Lowest, Is.EqualTo(-8.3f).Within(1e-4f));
        Assert.That(body.HalfWidth, Is.EqualTo(4.3f));
    }

    [Test]
    public void ACharacterThatRoseTowardTheEyesStillHasItsWorkspaceBelowTheLineup()
    {
        // The middle characters' slots; the one that needs the person rose 0.2 m.
        var bodies = Lineups.Arc(2.4f, 0.45f, new Dictionary<int, float> { [3] = 0.204f });
        var panel = WorkspacePlacement.Place(bodies[3].Yaw, bodies[3], bodies, Lineups.Workspace);

        Assert.That(panel.Clear, Is.True);
        Assert.That(panel.Above, Is.False);
        Assert.That(bodies.Any(body => Lineups.Covers(panel, body)), Is.False);
    }

    [Test]
    public void WithTheCharactersOnADeskTheWorkspaceOpensAboveThemClearOfTheDesk()
    {
        // A desk 0.46 m below the eyes, the lineup 0.55 m ahead, its bodies about 0.1 m above it.
        const float desk = 0.46f;
        foreach (var risen in new[] { null, new Dictionary<int, float> { [2] = 0.047f } })
        {
            var bodies = Lineups.Arc(0.55f, 0.36f, risen);
            foreach (var slot in new[] { 2, 3, 0, 5 })
            {
                var opened = bodies[slot];
                var panel = WorkspacePlacement.Place(opened.Yaw, opened, bodies, Lineups.Workspace, desk);

                Assert.That(panel.Clear, Is.True, $"slot {slot}");
                Assert.That(panel.Above, Is.True);
                Assert.That(panel.Elevation, Is.InRange(WorkspacePlacement.LowestDegrees, WorkspacePlacement.HighestDegrees));
                Assert.That(bodies.Any(body => Lineups.Covers(panel, body)), Is.False, "every body stays in view");
                Assert.That(WorkspacePlacement.BottomEdge(Lineups.Workspace, panel.Elevation), Is.GreaterThan(-desk + WorkspacePlacement.SurfaceClearance - 1e-3f),
                    "above the desk");
            }
        }
    }

    [Test]
    public void TheWorkspaceStaysNearWhereThePersonLooks()
    {
        var bodies = Lineups.Arc(2.4f, 0.45f);
        var panel = WorkspacePlacement.Place(0f, bodies[5], bodies, Lineups.Workspace);
        Assert.That(panel.Yaw, Is.EqualTo(WorkspacePlacement.MaxSideDegrees), "toward the character at 30 degrees, no farther than 15");

        panel = WorkspacePlacement.Place(350f, bodies[0], bodies, Lineups.Workspace);
        Assert.That(WorkspacePlacement.DeltaAngle(350f, panel.Yaw), Is.EqualTo(-WorkspacePlacement.MaxSideDegrees).Within(1e-3f), "across the heading's wrap");
    }

    [Test]
    public void OnAHighSurfaceTheWorkspaceNeverGoesIntoIt()
    {
        // A shelf 0.2 m below the eyes, the lineup 0.9 m away on it: below would cut into the shelf,
        // above leaves the band, so it takes the top of the band and covers part of the lineup.
        const float shelf = 0.2f;
        var bodies = Lineups.Arc(0.9f, 0.033f);
        var panel = WorkspacePlacement.Place(bodies[2].Yaw, bodies[2], bodies, Lineups.Workspace, shelf);

        Assert.That(panel.Clear, Is.False);
        Assert.That(panel.Elevation, Is.EqualTo(WorkspacePlacement.HighestDegrees));
        Assert.That(WorkspacePlacement.BottomEdge(Lineups.Workspace, panel.Elevation), Is.GreaterThan(-shelf + WorkspacePlacement.SurfaceClearance));
    }

    [Test]
    public void WhereNeitherSideClearsItMovesTheLeastIntoTheBand()
    {
        var opened = new BodyInView(0f, 0f, 5f);
        var bodies = new List<BodyInView> { opened, new(10f, -20f, 5f) };
        var panel = WorkspacePlacement.Place(0f, opened, bodies, Lineups.Workspace);

        Assert.That(panel.Clear, Is.False);
        Assert.That(panel.Above, Is.False);
        Assert.That(panel.Elevation, Is.EqualTo(WorkspacePlacement.LowestDegrees));
    }

    [Test]
    public void WhereBothSidesClearItTakesTheOneNearerARestingGaze()
    {
        var opened = new BodyInView(0f, -14f, 0.5f);
        var panel = WorkspacePlacement.Place(0f, opened, new List<BodyInView> { opened }, Lineups.Workspace);

        Assert.That(panel.Clear, Is.True);
        Assert.That(panel.Above, Is.False, "14 degrees below the natural line of sight beats 16 above it");
    }

    [Test]
    public void TheLowestEdgeFollowsTheElevation()
    {
        var limit = WorkspacePlacement.LowestAboveSurface(Lineups.Workspace, 0.46f);
        Assert.That(WorkspacePlacement.BottomEdge(Lineups.Workspace, limit), Is.EqualTo(-0.46f + WorkspacePlacement.SurfaceClearance).Within(1e-3f));
        Assert.That(limit, Is.InRange(-30f, -26f), "a desk allows the workspace down to about 28 degrees");
        Assert.That(WorkspacePlacement.BottomEdge(Lineups.Workspace, limit + 5f), Is.GreaterThan(WorkspacePlacement.BottomEdge(Lineups.Workspace, limit)));
    }
}

/// <summary>A panel moved by hand while Move is held (ADR 0023).</summary>
public class PanelDragTests
{
    private static readonly PanelSize Size = new(0.46f, 0.186f, 0.106f);

    [Test]
    public void ThePanelTurnsRoundTheEyesWithThePointTheHandHolds()
    {
        var drag = new PanelDrag(panelYaw: 10f, panelElevation: -15f, grabYaw: 12f, grabElevation: -5f, Size);
        Assert.That(drag.Follow(12f, -5f), Is.EqualTo((10f, -15f)), "held still, it stays");
        var (yaw, elevation) = drag.Follow(22f, 0f);
        Assert.That(yaw, Is.EqualTo(20f).Within(1e-4f));
        Assert.That(elevation, Is.EqualTo(-10f).Within(1e-4f));
        var behind = new PanelDrag(170f, -15f, 170f, -15f, Size).Follow(-175f, -15f);
        Assert.That(behind.Yaw, Is.EqualTo(-175f).Within(1e-4f), "past straight behind, the yaw turns on round");
    }

    [Test]
    public void ItsCenterStaysInTheComfortableBandAndAboveTheSurface()
    {
        var drag = new PanelDrag(0f, -15f, 0f, -15f, Size);
        Assert.That(drag.Follow(0f, 40f).Elevation, Is.EqualTo(WorkspacePlacement.HighestDegrees));
        Assert.That(drag.Follow(0f, -80f).Elevation, Is.EqualTo(WorkspacePlacement.LowestDegrees));
        var desk = new PanelDrag(0f, -15f, 0f, -15f, Size, surfaceDrop: 0.3f);
        var floor = Math.Max(WorkspacePlacement.LowestDegrees, WorkspacePlacement.LowestAboveSurface(Size, 0.3f));
        Assert.That(desk.Follow(0f, -80f).Elevation, Is.EqualTo(floor).Within(1e-4f));
        Assert.That(floor, Is.GreaterThan(WorkspacePlacement.LowestDegrees), "a desk 0.3 m below the eyes holds the panel higher");
    }

    [Test]
    public void NothingMovesWhileAConfirmationIsArmed()
    {
        var model = new PanelModel("Check your project");
        Assert.That(model.CanMove, Is.True);
        model.Confirm = new ConfirmStep("Sure?", new PanelAction("yes", "Yes, start over", PanelActionRole.Destructive),
            new PanelAction("cancel", "Cancel", PanelActionRole.Secondary));
        Assert.That(model.CanMove, Is.False);
        Assert.That(new PanelModel("Usage left") { Movable = false }.CanMove, Is.False);
    }
}

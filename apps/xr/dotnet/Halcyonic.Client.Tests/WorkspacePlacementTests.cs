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

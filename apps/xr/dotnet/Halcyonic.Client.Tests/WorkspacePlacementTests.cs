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

    /// <summary>A foreground panel (ADR 0023): 44 by 26 degrees at 0.46 m.</summary>
    public static readonly PanelSize Frame = new(0.46f, 0.46f * MathF.Tan(22f * MathF.PI / 180f), 0.46f * MathF.Tan(13f * MathF.PI / 180f));

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
    /// <paramref name="plateDegrees"/> tall under them, 10.5 as designed, and 10.5 wide, as the
    /// stage's scale makes them at that distance.
    /// </summary>
    public static List<BodyInView> RaisedArc(float plateDegrees = 10.5f)
    {
        const float distance = 2.4f;
        const float radians = MathF.PI / 180f;
        return Slots.Select(yaw => Labeled(yaw, distance, 0.17f, 0.075f * distance, plateDegrees * radians * distance, 5.25f * radians * distance)).ToList();
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
            Assert.That(panel.Elevation, Is.InRange(WorkspacePlacement.Lowest(Lineups.Workspace), WorkspacePlacement.HighestDegrees));
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
            Assert.That(panel.Elevation, Is.InRange(WorkspacePlacement.Lowest(Lineups.Workspace), WorkspacePlacement.HighestDegrees));
            Assert.That(characters.Any(character => Lineups.CoversLabel(panel, character)), Is.False, "every title and badge stays readable");
        }
    }

    /// <summary>Where a label stands over a flat panel, its top edge as the eyes see it under the label's outer side, or the panel's corner where the label reaches past it.</summary>
    private static float EdgeUnder(PanelDirection panel, PanelSize size, BodyInView body)
    {
        var outer = MathF.Min(size.HalfWidthDegrees, MathF.Abs(WorkspacePlacement.DeltaAngle(panel.Yaw, body.Yaw)) + body.HalfWidth);
        return WorkspacePlacement.CornerElevation(panel.Elevation + size.HalfHeightDegrees, outer);
    }

    /// <summary>Whether a label stands over a panel of <paramref name="size"/>, across.</summary>
    private static bool Over(PanelDirection panel, PanelSize size, BodyInView body) =>
        MathF.Abs(WorkspacePlacement.DeltaAngle(panel.Yaw, body.Yaw)) - body.HalfWidth < size.HalfWidthDegrees;

    [Test]
    public void EveryLabelClearsThePanelWhereItStands()
    {
        var characters = Lineups.RaisedArc();
        foreach (var slot in new[] { 3, 0, 5 })
        {
            var opened = characters[slot];
            var panel = WorkspacePlacement.Place(opened.Yaw, opened, characters, Lineups.Workspace);
            var top = panel.Elevation + Lineups.Workspace.HalfHeightDegrees;
            Assert.That(WorkspacePlacement.CornerElevation(top, Lineups.Workspace.HalfWidthDegrees), Is.GreaterThan(top), "below eye level, a flat panel's corners look higher than its edge's middle");
            foreach (var body in characters.Where(body => Over(panel, Lineups.Workspace, body)))
            {
                Assert.That(EdgeUnder(panel, Lineups.Workspace, body), Is.LessThanOrEqualTo(body.Lowest - 1f), $"slot {slot}: a degree or more under the label at {body.Yaw}, where it stands");
            }
        }
    }

    [Test]
    public void AWidePlaneClearsALabelOverItsMiddleAndOneAtItsOuterEdgeEachWhereItStands()
    {
        // The menu and a file side by side, 64 by 33 degrees: the opened label over the middle, deepest,
        // and a higher one standing over the plane's outer edge.
        var wide = new PanelSize(0.46f, 0.46f * MathF.Tan(32f * MathF.PI / 180f), 0.46f * MathF.Tan(16.5f * MathF.PI / 180f));
        var middle = new BodyInView(0f, -9f, 2f, -17f, 5f);
        var outer = new BodyInView(30f, -8f, 2f, -15f, 5f);
        var panel = WorkspacePlacement.Place(0f, middle, new[] { middle, outer }, wide);
        Assert.That(panel.Clear, Is.True);
        foreach (var body in new[] { middle, outer })
        {
            Assert.That(EdgeUnder(panel, wide, body), Is.LessThanOrEqualTo(body.Lowest - WorkspacePlacement.LabelClearanceDegrees + 1e-3f), "each label cleared where it stands, by the same margin");
        }
        var tightest = new[] { middle, outer }.Max(body => EdgeUnder(panel, wide, body) - (body.Lowest - WorkspacePlacement.LabelClearanceDegrees));
        Assert.That(tightest, Is.EqualTo(0f).Within(0.01f), "and no lower than the label that binds it needs");
        // Cleared at the corners under the deepest label instead, its top would stand lower.
        var corners = WorkspacePlacement.EdgeForCorners(middle.Lowest - WorkspacePlacement.LabelClearanceDegrees, wide.HalfWidthDegrees);
        Assert.That(panel.Elevation + wide.HalfHeightDegrees, Is.GreaterThan(corners + 0.5f), "the plane stands higher than clearing every label at its corners");
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
                Assert.That(panel.Elevation, Is.InRange(WorkspacePlacement.Lowest(Lineups.Workspace), WorkspacePlacement.HighestDegrees));
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
        Assert.That(panel.Elevation, Is.EqualTo(WorkspacePlacement.Lowest(Lineups.Workspace)));
    }

    [Test]
    public void ATallerPanelMayGoLowerByAsMuchAsItIsTaller()
    {
        Assert.That(WorkspacePlacement.Lowest(Lineups.Frame), Is.EqualTo(WorkspacePlacement.LowestDegrees).Within(1e-3f), "a panel as tall as designed stays in the band");
        var larger = new PanelSize(Lineups.Frame.Distance, Lineups.Frame.HalfWidth * 1.15f, Lineups.Frame.HalfHeight * 1.15f);
        var taller = 2f * larger.HalfHeightDegrees - WorkspacePlacement.DesignedHeightDegrees;
        Assert.That(taller, Is.InRange(3.5f, 4f), "text a step larger makes a panel nearly 4 degrees taller");
        Assert.That(WorkspacePlacement.Lowest(larger), Is.EqualTo(WorkspacePlacement.LowestDegrees - taller).Within(1e-3f));
        var shorter = new PanelSize(Lineups.Frame.Distance, Lineups.Frame.HalfWidth, Lineups.Frame.HalfHeight * 0.7f);
        Assert.That(WorkspacePlacement.Lowest(shorter), Is.EqualTo(WorkspacePlacement.LowestDegrees), "a shorter panel goes no lower than the band");
    }

    [Test]
    public void TheHeadTipsAsMuchAsAPanelsBottomNeedsAndNeverMoreThanEight()
    {
        var quest3S = new ViewField(48, 48, 45, 45);
        var level = quest3S.LowestCenter(Lineups.Frame.HalfWidthDegrees, Lineups.Frame.HalfHeightDegrees);
        Assert.That(WorkspacePlacement.ReadingPitch(Lineups.Frame, level + 2f, quest3S), Is.Zero, "a panel the field holds with the head level is read level");
        Assert.That(WorkspacePlacement.ReadingPitch(Lineups.Frame, level - 3f, quest3S), Is.EqualTo(3f).Within(1e-3f), "3 degrees lower, the head tips 3");
        Assert.That(WorkspacePlacement.ReadingPitch(Lineups.Frame, level - 20f, quest3S), Is.EqualTo(WorkspacePlacement.MostReadingPitchDegrees), "never more than 8 degrees");
        Assert.That(WorkspacePlacement.Lowest(Lineups.Frame, quest3S), Is.EqualTo(Math.Max(WorkspacePlacement.LowestDegrees, level)),
            "placement keeps a designed panel where the field holds it with the head level");
        var larger = new PanelSize(Lineups.Frame.Distance, Lineups.Frame.HalfWidth * 1.15f, Lineups.Frame.HalfHeight * 1.15f);
        Assert.That(WorkspacePlacement.FloorPitch(larger), Is.EqualTo(1.5f * WorkspacePlacement.TallerBy(larger)).Within(1e-4f), "a taller one may go lower by half again as much");
        Assert.That(WorkspacePlacement.Lowest(larger, quest3S),
            Is.EqualTo(quest3S.LowestCenter(larger.HalfWidthDegrees, larger.HalfHeightDegrees) - WorkspacePlacement.FloorPitch(larger)).Within(1e-4f));

        // Every height fits from one top line, level or tipped: no height fits neither.
        for (var degrees = 20f; degrees <= 32.5f; degrees += 0.25f)
        {
            var size = new PanelSize(0.46f, 0.46f * MathF.Tan(18f * MathF.PI / 180f), 0.46f * MathF.Tan(degrees / 2f * MathF.PI / 180f));
            var elevation = -18.9f - degrees / 2f;
            var lowest = quest3S.LowestCenter(size.HalfWidthDegrees, size.HalfHeightDegrees);
            Assert.That(lowest - WorkspacePlacement.ReadingPitch(size, elevation, quest3S), Is.LessThanOrEqualTo(elevation + 1e-3f), degrees + " degrees tall, its top 18.9 below eye level");
        }
    }

    [Test]
    public void WithTextAStepLargerOnAQuest3SThePanelOpensUnderTheLabelsAndInsideTheFieldWithTheHeadTippedDown()
    {
        var quest3S = new ViewField(48, 48, 45, 45);
        var larger = new PanelSize(Lineups.Frame.Distance, Lineups.Frame.HalfWidth * 1.15f, Lineups.Frame.HalfHeight * 1.15f);
        var characters = Lineups.RaisedArc(plateDegrees: 11f);
        foreach (var slot in new[] { 2, 3, 0, 5 })
        {
            var opened = characters[slot];
            var panel = WorkspacePlacement.Place(opened.Yaw, opened, characters, larger, field: quest3S);

            Assert.That(panel.Clear, Is.True, $"slot {slot}");
            Assert.That(panel.Above, Is.False);
            Assert.That(panel.Elevation, Is.GreaterThanOrEqualTo(WorkspacePlacement.Lowest(larger, quest3S) - 1e-3f), "inside the field, the head tipped down");
            Assert.That(panel.Elevation, Is.LessThan(quest3S.LowestCenter(larger.HalfWidthDegrees, larger.HalfHeightDegrees)),
                "with the head level its lower corners would be outside: under the labels there is no higher place");
        }
    }

    [Test]
    public void WithTextAStepLargerAPanelStillOpensUnderEveryLabel()
    {
        // The panel grows whole by the text's step, and the titles above it reach a little deeper.
        var larger = new PanelSize(Lineups.Frame.Distance, Lineups.Frame.HalfWidth * 1.15f, Lineups.Frame.HalfHeight * 1.15f);
        var characters = Lineups.RaisedArc(plateDegrees: 11f);
        foreach (var slot in new[] { 2, 3, 0, 5 })
        {
            var opened = characters[slot];
            var panel = WorkspacePlacement.Place(opened.Yaw, opened, characters, larger);

            Assert.That(panel.Clear, Is.True, $"slot {slot}");
            Assert.That(panel.Above, Is.False);
            Assert.That(panel.Elevation, Is.InRange(WorkspacePlacement.Lowest(larger), WorkspacePlacement.HighestDegrees));
            Assert.That(panel.Elevation, Is.LessThan(WorkspacePlacement.LowestDegrees), "it fits only below the designed band");
            foreach (var body in characters.Where(body => Over(panel, larger, body)))
            {
                Assert.That(EdgeUnder(panel, larger, body), Is.LessThanOrEqualTo(body.Lowest - 1f), $"slot {slot}: a degree or more under the label at {body.Yaw}, where it stands");
            }
        }
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
        var larger = new PanelSize(Size.Distance, Size.HalfWidth * 1.15f, Size.HalfHeight * 1.15f);
        Assert.That(new PanelDrag(0f, -15f, 0f, -15f, larger).Follow(0f, -80f).Elevation, Is.EqualTo(WorkspacePlacement.Lowest(larger)).Within(1e-4f),
            "a panel grown with the text goes as low as it opens");
    }

    [Test]
    public void ADraggedPanelStaysInsideTheMeasuredField()
    {
        var narrow = new ViewField(48, 48, 45, 40);
        var drag = new PanelDrag(0f, -15f, 0f, -15f, Size, field: narrow);
        var lowest = WorkspacePlacement.Lowest(Size, narrow);
        Assert.That(lowest, Is.GreaterThan(WorkspacePlacement.LowestDegrees));
        Assert.That(drag.Follow(0f, -80f).Elevation, Is.EqualTo(lowest).Within(1e-4f));
        // Over a desk the higher of the two floors holds.
        var desk = new PanelDrag(0f, -15f, 0f, -15f, Size, surfaceDrop: 0.2f, field: narrow);
        var floor = Math.Max(lowest, WorkspacePlacement.LowestAboveSurface(Size, 0.2f));
        Assert.That(desk.Follow(0f, -80f).Elevation, Is.EqualTo(floor).Within(1e-4f));
    }

    [Test]
    public void HoldingMoveNeverLiftsAPanelThatOpenedUnderTheLabelsIntoThem()
    {
        // Text a step larger on a Quest 3S: the panel opens under the far lineup's labels.
        var quest3S = new ViewField(48, 48, 45, 45);
        var larger = new PanelSize(Lineups.Frame.Distance, Lineups.Frame.HalfWidth * 1.15f, Lineups.Frame.HalfHeight * 1.15f);
        var characters = Lineups.RaisedArc(plateDegrees: 11f);
        var opened = WorkspacePlacement.Place(characters[3].Yaw, characters[3], characters, larger, field: quest3S);
        var drag = new PanelDrag(opened.Yaw, opened.Elevation, opened.Yaw, opened.Elevation + 5f, larger, field: quest3S);
        Assert.That(drag.Follow(opened.Yaw, opened.Elevation + 5f).Elevation, Is.EqualTo(opened.Elevation).Within(1e-4f), "held still, it stays");
        Assert.That(drag.Follow(opened.Yaw, -80f).Elevation, Is.EqualTo(WorkspacePlacement.Lowest(larger, quest3S)).Within(1e-4f));

        // Even where the field would hold it higher than it opened, as a narrower one does, it stays where it is and goes no lower.
        var narrower = new ViewField(48, 48, 45, 38);
        Assert.That(WorkspacePlacement.Lowest(larger, narrower), Is.GreaterThan(opened.Elevation));
        var held = new PanelDrag(opened.Yaw, opened.Elevation, opened.Yaw, opened.Elevation, larger, field: narrower);
        Assert.That(held.Follow(opened.Yaw, opened.Elevation).Elevation, Is.EqualTo(opened.Elevation).Within(1e-4f), "not lifted into the labels");
        Assert.That(held.Follow(opened.Yaw, -80f).Elevation, Is.EqualTo(opened.Elevation).Within(1e-4f));
        Assert.That(held.Follow(opened.Yaw, opened.Elevation + 6f).Elevation, Is.EqualTo(opened.Elevation + 6f).Within(1e-4f), "it still rises with the hand");
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

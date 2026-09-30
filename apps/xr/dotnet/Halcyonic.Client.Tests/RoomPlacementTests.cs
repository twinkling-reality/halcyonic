using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>Rooms as the scene model describes them, in meters, Y up, the person facing +Z unless said.</summary>
internal static class Rooms
{
    /// <summary>A seated person's eyes, about 1.2 m above the floor.</summary>
    public static readonly Viewer Seated = new(new RoomPoint(0f, 1.2f, 0f), new PlanPoint(0f, 1f));

    /// <summary>An axis-aligned rectangle on the floor plan.</summary>
    public static PlanPoint[] Rectangle(float minX, float maxX, float minZ, float maxZ) => new[]
    {
        new PlanPoint(minX, minZ), new PlanPoint(maxX, minZ), new PlanPoint(maxX, maxZ), new PlanPoint(minX, maxZ),
    };

    /// <summary>A desk 1.2 m wide and 0.6 m deep at 0.74 m, its near edge 0.3 m in front of the eyes.</summary>
    public static RoomSurface Desk(float nearEdge = 0.3f, float width = 1.2f, float depth = 0.6f, float height = 0.74f) =>
        new(SurfaceKind.Desk, height, Rectangle(-width / 2f, width / 2f, nearEdge, nearEdge + depth));

    public static IEnumerable<RoomObstacle> None => Array.Empty<RoomObstacle>();

    /// <summary>The points of the lineup's arc, a degree apart, as the stage draws it around the eyes.</summary>
    public static IEnumerable<PlanPoint> Arc(Viewer viewer, PlanPoint middle)
    {
        var toMiddle = middle - viewer.Eyes.Plan;
        for (var degrees = -30; degrees <= 30; degrees++)
        {
            yield return viewer.Eyes.Plan + PlanPoint.Toward(toMiddle.Heading + degrees) * toMiddle.Length;
        }
    }

    /// <summary>Whether a point lies inside an axis-aligned rectangle with a margin to every edge.</summary>
    public static bool InsideRectangle(PlanPoint point, float minX, float maxX, float minZ, float maxZ, float margin) =>
        point.X >= minX + margin && point.X <= maxX - margin && point.Z >= minZ + margin && point.Z <= maxZ - margin;

    public static RoomObstacle Box(float minX, float maxX, float minZ, float maxZ, float bottom, float top) =>
        new(bottom, top, Rectangle(minX, maxX, minZ, maxZ));
}

public class StageSurfacesTests
{
    [Test]
    public void ADeskInFrontOfASeatedPersonTakesTheLineupOnItsNearHalfWithinReach()
    {
        var desk = Rooms.Desk();

        var spot = StageSurfaces.Choose(new[] { desk }, Rooms.None, Rooms.Seated);

        Assert.That(spot, Is.Not.Null);
        Assert.That(spot!.Surface, Is.SameAs(desk));
        Assert.That(spot.Point.Y, Is.EqualTo(0.74f), "the lineup stands on the desk");
        Assert.That(spot.Reach, Is.EqualTo(StageSurfaces.IdealReach).Within(1e-4f));
        Assert.That(spot.Turn, Is.EqualTo(0f), "straight ahead");
        Assert.That(spot.Point.Z, Is.InRange(0.3f, 0.6f), "on the desk's near half");
        Assert.That(spot.Drop, Is.EqualTo(0.46f).Within(1e-4f));
        var margin = StageSurfaces.Margin(spot.Reach);
        foreach (var point in Rooms.Arc(Rooms.Seated, spot.Point.Plan))
        {
            Assert.That(Rooms.InsideRectangle(point, -0.6f, 0.6f, 0.3f, 0.9f, margin - 1e-3f), Is.True, $"{point} is on the desk, clear of its edge");
        }
    }

    /// <summary>The cost the chooser documents: 0.1 m off the ideal reach, 10 degrees of turn, or a few degrees beyond a comfortable look down each cost one.</summary>
    private static float Cost(StageSpot spot)
    {
        var down = MathF.Atan2(spot.Drop, spot.Reach) * 180f / MathF.PI;
        return MathF.Abs(spot.Reach - StageSurfaces.IdealReach) / 0.1f + MathF.Abs(spot.Turn) / 10f
            + MathF.Max(0f, down - StageSurfaces.ComfortableDownDegrees) / StageSurfaces.SteepnessDegreesPerCost;
    }

    [Test]
    public void ADeskWinsOverOtherFurnitureEvenWhenTheOtherSitsBetter()
    {
        // A cabinet just in front of the person, nearer the ideal reach, and the desk beyond it.
        var cabinet = new RoomSurface(SurfaceKind.Other, 0.78f, Rooms.Rectangle(-0.5f, 0.5f, 0.2f, 0.55f));
        var desk = new RoomSurface(SurfaceKind.Desk, 0.74f, Rooms.Rectangle(-0.8f, 0.8f, 0.56f, 1.3f));

        var onCabinet = StageSurfaces.Choose(new[] { cabinet }, Rooms.None, Rooms.Seated)!;
        var spot = StageSurfaces.Choose(new[] { cabinet, desk }, Rooms.None, Rooms.Seated);

        Assert.That(spot, Is.Not.Null);
        Assert.That(spot!.Surface, Is.SameAs(desk));
        Assert.That(Cost(onCabinet), Is.LessThan(Cost(spot)), "the cabinet sat better");
        Assert.That(spot.Reach, Is.LessThanOrEqualTo(StageSurfaces.FarthestReach));
    }

    [Test]
    public void OtherFurnitureServesWhenThereIsNoDesk()
    {
        var cabinet = new RoomSurface(SurfaceKind.Other, 0.8f, Rooms.Rectangle(-0.6f, 0.6f, 0.3f, 0.9f));

        var spot = StageSurfaces.Choose(new[] { cabinet }, Rooms.None, Rooms.Seated);

        Assert.That(spot, Is.Not.Null);
        Assert.That(spot!.Surface.Kind, Is.EqualTo(SurfaceKind.Other));
    }

    [Test]
    public void AmongDesksTheOneThatSitsBestWins()
    {
        var near = new RoomSurface(SurfaceKind.Desk, 0.78f, Rooms.Rectangle(-0.5f, 0.5f, 0.2f, 0.55f));
        var beyond = new RoomSurface(SurfaceKind.Desk, 0.74f, Rooms.Rectangle(-0.8f, 0.8f, 0.56f, 1.3f));

        var spot = StageSurfaces.Choose(new[] { beyond, near }, Rooms.None, Rooms.Seated);

        Assert.That(spot!.Surface, Is.SameAs(near));
        Assert.That(Cost(spot), Is.LessThan(Cost(StageSurfaces.Choose(new[] { beyond }, Rooms.None, Rooms.Seated)!)));
    }

    [Test]
    public void LookingSteeplyDownCostsLikeReachingFarther()
    {
        // A surface low enough that its ideal reach would mean looking down at 60 degrees.
        var coffeeTable = new RoomSurface(SurfaceKind.Other, 0.25f, Rooms.Rectangle(-0.6f, 0.6f, 0.3f, 1.2f));

        var spot = StageSurfaces.Choose(new[] { coffeeTable }, Rooms.None, Rooms.Seated);

        Assert.That(spot, Is.Not.Null);
        Assert.That(spot!.Reach, Is.GreaterThan(StageSurfaces.IdealReach), "farther out, to look down less steeply");
    }

    [Test]
    public void TheFloorAHighShelfAndACeilingAreNoStage()
    {
        var floor = new RoomSurface(SurfaceKind.Other, 0f, Rooms.Rectangle(-3f, 3f, -3f, 3f));
        var shelf = new RoomSurface(SurfaceKind.Other, 1.15f, Rooms.Rectangle(-0.6f, 0.6f, 0.3f, 0.9f));
        var above = new RoomSurface(SurfaceKind.Desk, 1.4f, Rooms.Rectangle(-0.6f, 0.6f, 0.3f, 0.9f));

        Assert.That(StageSurfaces.Choose(new[] { floor, shelf, above }, Rooms.None, Rooms.Seated), Is.Null);
    }

    [Test]
    public void ATableOutOfReachOrBehindThePersonIsNoStage()
    {
        var across = Rooms.Desk(nearEdge: 1.3f);
        var behind = new RoomSurface(SurfaceKind.Desk, 0.74f, Rooms.Rectangle(-0.6f, 0.6f, -0.9f, -0.3f));
        var farToTheSide = new RoomSurface(SurfaceKind.Desk, 0.74f, Rooms.Rectangle(0.3f, 0.9f, -0.6f, 0.2f));

        Assert.That(StageSurfaces.Choose(new[] { across, behind, farToTheSide }, Rooms.None, Rooms.Seated), Is.Null);
    }

    [Test]
    public void ASurfaceTooSmallForTheWholeLineupIsNoStage()
    {
        var sideTable = new RoomSurface(SurfaceKind.Desk, 0.6f, Rooms.Rectangle(-0.2f, 0.2f, 0.35f, 0.75f));

        Assert.That(StageSurfaces.Choose(new[] { sideTable }, Rooms.None, Rooms.Seated), Is.Null);
    }

    [Test]
    public void WhenNothingFitsEachSurfaceSaysWhichRuleTheLineupBreaks()
    {
        var shelf = new RoomSurface(SurfaceKind.Other, 1.15f, Rooms.Rectangle(-0.6f, 0.6f, 0.3f, 0.9f));
        // A desk whose whole middle is taken by a laptop and a monitor side by side.
        var desk = Rooms.Desk(nearEdge: 0.2f, width: 1.0f, depth: 0.7f);
        var clutter = Rooms.Box(-0.5f, 0.5f, 0.35f, 0.9f, 0.74f, 1.2f);
        var narrow = new RoomSurface(SurfaceKind.Desk, 0.74f, Rooms.Rectangle(-0.2f, 0.2f, 0.35f, 0.75f));

        var lines = StageSurfaces.Explain(new[] { shelf, desk, narrow }, new[] { clutter }, Rooms.Seated);

        Assert.That(StageSurfaces.Choose(new[] { shelf, desk, narrow }, new[] { clutter }, Rooms.Seated), Is.Null);
        Assert.That(lines, Has.Count.EqualTo(3));
        Assert.That(lines[0], Does.StartWith("other furniture, 0.05 m below the eyes").And.EndWith("not 0.15 to 1.00 m below the eyes"));
        Assert.That(lines[1], Does.StartWith("desk or table, 0.46 m below the eyes, 0.20 to").And.Contain("1 objects on it")
            .And.EndWith("the lineup would fit without the objects standing on it"));
        Assert.That(lines[2], Does.EndWith("the lineup's arc does not fit on it within reach and view"));
    }

    [Test]
    public void AnOutlineOfFewerThanThreePointsIsNoSurface()
    {
        var line = new RoomSurface(SurfaceKind.Desk, 0.74f, new[] { new PlanPoint(-1f, 0.5f), new PlanPoint(1f, 0.5f) });

        Assert.That(StageSurfaces.Choose(new[] { line }, Rooms.None, Rooms.Seated), Is.Null);
        Assert.That(StageSurfaces.Choose(Array.Empty<RoomSurface>(), Rooms.None, Rooms.Seated), Is.Null);
    }

    [Test]
    public void TheLineupKeepsClearOfAMonitorStandingOnTheDesk()
    {
        var desk = Rooms.Desk();
        // A monitor on a stand, 60 cm wide, whose foot reaches to the middle of the desk.
        var monitor = Rooms.Box(-0.3f, 0.3f, 0.6f, 0.8f, 0.74f, 1.2f);

        var free = StageSurfaces.Choose(new[] { desk }, Rooms.None, Rooms.Seated)!;
        var spot = StageSurfaces.Choose(new[] { desk }, new[] { monitor }, Rooms.Seated);

        Assert.That(spot, Is.Not.Null);
        Assert.That(spot!.Reach, Is.LessThan(free.Reach), "the lineup stands nearer, in front of the monitor");
        var margin = StageSurfaces.Margin(spot.Reach);
        foreach (var point in Rooms.Arc(Rooms.Seated, spot.Point.Plan))
        {
            var clear = point.X < -0.3f - margin + 1e-3f || point.X > 0.3f + margin - 1e-3f
                || point.Z < 0.6f - margin + 1e-3f || point.Z > 0.8f + margin - 1e-3f;
            Assert.That(clear, Is.True, $"{point} keeps clear of the monitor");
        }
    }

    [Test]
    public void SomethingHangingHighAboveTheDeskIsNoObstacle()
    {
        // A shelf or a lamp's arm well above the desk, and an object on another surface lower down.
        var shelf = Rooms.Box(-0.6f, 0.6f, 0.3f, 0.9f, 1.3f, 1.35f);
        var underneath = Rooms.Box(-0.6f, 0.6f, 0.3f, 0.9f, 0f, 0.7f);

        var spot = StageSurfaces.Choose(new[] { Rooms.Desk() }, new[] { shelf, underneath }, Rooms.Seated);

        Assert.That(spot, Is.Not.Null);
        Assert.That(spot!.Reach, Is.EqualTo(StageSurfaces.IdealReach).Within(1e-4f));
    }

    [Test]
    public void AnLShapedDeskKeepsTheLineupOffItsNotch()
    {
        // An L: a long desk in front, and a return on the left whose corner cuts into the near right.
        var outline = new[]
        {
            new PlanPoint(-0.9f, -0.2f), new PlanPoint(-0.3f, -0.2f), new PlanPoint(-0.3f, 0.45f),
            new PlanPoint(0.9f, 0.45f), new PlanPoint(0.9f, 1.05f), new PlanPoint(-0.9f, 1.05f),
        };
        var desk = new RoomSurface(SurfaceKind.Desk, 0.74f, outline);

        var spot = StageSurfaces.Choose(new[] { desk }, Rooms.None, Rooms.Seated);

        Assert.That(spot, Is.Not.Null);
        foreach (var point in Rooms.Arc(Rooms.Seated, spot!.Point.Plan))
        {
            var onLong = Rooms.InsideRectangle(point, -0.9f, 0.9f, 0.45f, 1.05f, 0f);
            var onReturn = Rooms.InsideRectangle(point, -0.9f, -0.3f, -0.2f, 1.05f, 0f);
            Assert.That(onLong || onReturn, Is.True, $"{point} is on the desk, not in the notch");
        }
    }

    [Test]
    public void ARotatedDeskIsFoundWhereThePersonFaces()
    {
        // The person faces 30 degrees to the right, toward a desk turned the same way.
        var facing = PlanPoint.Toward(30f);
        var viewer = new Viewer(new RoomPoint(1f, 1.2f, 2f), facing);
        var across = PlanPoint.Toward(120f);
        PlanPoint At(float along, float side) => new PlanPoint(1f, 2f) + facing * along + across * side;
        var desk = new RoomSurface(SurfaceKind.Desk, 0.74f, new[] { At(0.3f, -0.6f), At(0.3f, 0.6f), At(0.9f, 0.6f), At(0.9f, -0.6f) });

        var spot = StageSurfaces.Choose(new[] { desk }, Rooms.None, viewer);

        Assert.That(spot, Is.Not.Null);
        Assert.That(spot!.Turn, Is.EqualTo(0f));
        var toMiddle = spot.Point.Plan - viewer.Eyes.Plan;
        Assert.That(toMiddle.Heading, Is.EqualTo(30f).Within(1e-3f), "straight ahead of the person");
        Assert.That(toMiddle.Length, Is.EqualTo(StageSurfaces.IdealReach).Within(1e-4f));
    }

    [Test]
    public void SittingBackFromTheDeskMovesTheLineupOutWithinReach()
    {
        var desk = Rooms.Desk(nearEdge: 0.6f);

        var spot = StageSurfaces.Choose(new[] { desk }, Rooms.None, Rooms.Seated);

        Assert.That(spot, Is.Not.Null);
        Assert.That(spot!.Reach, Is.GreaterThan(0.6f));
        Assert.That(spot.Reach, Is.LessThanOrEqualTo(StageSurfaces.FarthestReach));
        var margin = StageSurfaces.Margin(spot.Reach);
        foreach (var point in Rooms.Arc(Rooms.Seated, spot.Point.Plan))
        {
            Assert.That(Rooms.InsideRectangle(point, -0.6f, 0.6f, 0.6f, 1.2f, margin - 1e-3f), Is.True, $"{point} is on the desk");
        }
    }

    [Test]
    public void StandingAtADeskStillFindsItWithoutLookingTooSteeplyDown()
    {
        var standing = new Viewer(new RoomPoint(0f, 1.62f, 0f), new PlanPoint(0f, 1f));
        var tall = new Viewer(new RoomPoint(0f, 1.8f, 0f), new PlanPoint(0f, 1f));

        var spot = StageSurfaces.Choose(new[] { Rooms.Desk() }, Rooms.None, standing);

        Assert.That(spot, Is.Not.Null);
        var down = MathF.Atan2(spot!.Drop, spot.Reach) * 180f / MathF.PI;
        Assert.That(down, Is.LessThanOrEqualTo(StageSurfaces.SteepestDownDegrees));
        Assert.That(StageSurfaces.Choose(new[] { Rooms.Desk() }, Rooms.None, tall), Is.Null, "more than a meter below the eyes");
    }

    [Test]
    public void TheLineupThatFitsIsTheArcTheStageDraws()
    {
        var desk = Rooms.Desk();
        var middle = new PlanPoint(0f, 0.55f);

        Assert.That(StageSurfaces.LineupFits(desk, Rooms.None, Rooms.Seated, middle), Is.True);
        Assert.That(StageSurfaces.LineupFits(desk, Rooms.None, Rooms.Seated, new PlanPoint(0f, 0.35f)), Is.False, "the arc's ends would hang over the near edge");
        Assert.That(StageSurfaces.LineupFits(desk, new[] { Rooms.Box(-0.05f, 0.05f, 0.5f, 0.6f, 0.74f, 0.9f) }, Rooms.Seated, middle), Is.False, "a mug in the middle");
    }

    [Test]
    public void APersonMustFaceSomewhere()
    {
        Assert.Throws<ArgumentException>(() => new Viewer(new RoomPoint(0f, 1.2f, 0f), new PlanPoint(0f, 0f)));
        var viewer = new Viewer(new RoomPoint(0f, 1.2f, 0f), new PlanPoint(0f, 3f));
        Assert.That(viewer.Facing.Length, Is.EqualTo(1f).Within(1e-6f));
    }

    [Test]
    public void TurnsWrapAround()
    {
        Assert.That(StageSurfaces.TurnBetween(170f, -170f), Is.EqualTo(20f).Within(1e-4f));
        Assert.That(StageSurfaces.TurnBetween(-170f, 170f), Is.EqualTo(-20f).Within(1e-4f));
        Assert.That(StageSurfaces.TurnBetween(10f, 40f), Is.EqualTo(30f).Within(1e-4f));
    }
}

public class KeptPlacementTests
{
    private static readonly RoomPoint Kept = new(0f, 0.74f, 0.55f);

    [Test]
    public void TheSameSeatKeepsThePlacement()
    {
        Assert.That(StageSurfaces.StillSuits(Kept, Rooms.Seated, new[] { Rooms.Desk() }, Rooms.None), Is.True);
    }

    [Test]
    public void SittingALittleDifferentlyKeepsIt()
    {
        var leaningBack = new Viewer(new RoomPoint(0.08f, 1.15f, -0.12f), PlanPoint.Toward(12f));

        Assert.That(StageSurfaces.StillSuits(Kept, leaningBack, new[] { Rooms.Desk() }, Rooms.None), Is.True);
    }

    [Test]
    public void AnotherSeatInTheRoomChoosesAgain()
    {
        var acrossTheRoom = new Viewer(new RoomPoint(0f, 1.1f, 3f), new PlanPoint(0f, -1f));
        var turnedAway = new Viewer(Rooms.Seated.Eyes, new PlanPoint(0f, -1f));
        var standingUp = new Viewer(new RoomPoint(0f, 1.9f, 0f), new PlanPoint(0f, 1f));

        Assert.That(StageSurfaces.StillSuits(Kept, acrossTheRoom, new[] { Rooms.Desk() }, Rooms.None), Is.False);
        Assert.That(StageSurfaces.StillSuits(Kept, turnedAway, new[] { Rooms.Desk() }, Rooms.None), Is.False);
        Assert.That(StageSurfaces.StillSuits(Kept, standingUp, new[] { Rooms.Desk() }, Rooms.None), Is.False);
    }

    [Test]
    public void ARoomWhoseDeskMovedChoosesAgain()
    {
        var movedDesk = Rooms.Desk(nearEdge: 1.0f);
        var somethingInTheWay = Rooms.Box(-0.3f, 0.3f, 0.4f, 0.6f, 0.74f, 1.1f);

        Assert.That(StageSurfaces.StillSuits(Kept, Rooms.Seated, new[] { movedDesk }, Rooms.None), Is.False);
        Assert.That(StageSurfaces.StillSuits(Kept, Rooms.Seated, new[] { Rooms.Desk() }, new[] { somethingInTheWay }), Is.False);
    }

    [Test]
    public void WithoutTheRoomsLayoutComfortAloneDecides()
    {
        var acrossTheRoom = new Viewer(new RoomPoint(0f, 1.1f, 3f), new PlanPoint(0f, -1f));

        Assert.That(StageSurfaces.StillSuits(Kept, Rooms.Seated, null, null), Is.True);
        Assert.That(StageSurfaces.StillSuits(Kept, acrossTheRoom, null, null), Is.False);
    }
}

public class PlacementMemoryTests
{
    [Test]
    public void ARoomRecallsItsAnchor()
    {
        var memory = new PlacementMemory();

        Assert.That(memory.Remember("room-a", "anchor-1"), Is.Empty);
        Assert.That(memory.Remember("room-b", "anchor-2"), Is.Empty);

        Assert.That(memory.AnchorFor("room-a"), Is.EqualTo("anchor-1"));
        Assert.That(memory.AnchorFor("room-b"), Is.EqualTo("anchor-2"));
        Assert.That(memory.AnchorFor("room-c"), Is.Null);
        Assert.That(memory.MostRecentAnchor, Is.EqualTo("anchor-2"));
    }

    [Test]
    public void ANewAnchorForARoomReturnsTheOldOneToErase()
    {
        var memory = new PlacementMemory();
        memory.Remember("room-a", "anchor-1");

        Assert.That(memory.Remember("room-a", "anchor-2"), Is.EqualTo(new[] { "anchor-1" }));
        Assert.That(memory.Remember("room-a", "anchor-2"), Is.Empty, "the same anchor again replaces nothing");
        Assert.That(memory.AnchorFor("room-a"), Is.EqualTo("anchor-2"));
        Assert.That(memory.Count, Is.EqualTo(1));
    }

    [Test]
    public void TheLeastRecentlyUsedRoomIsForgottenBeyondTheLimit()
    {
        var memory = new PlacementMemory();
        for (var i = 0; i < PlacementMemory.MaxRooms; i++) memory.Remember($"room-{i}", $"anchor-{i}");
        memory.Touch("room-0");

        var dropped = memory.Remember("room-new", "anchor-new");

        Assert.That(dropped, Is.EqualTo(new[] { "anchor-1" }), "room 0 was used recently, so room 1 goes");
        Assert.That(memory.Count, Is.EqualTo(PlacementMemory.MaxRooms));
        Assert.That(memory.AnchorFor("room-0"), Is.EqualTo("anchor-0"));
        Assert.That(memory.AnchorFor("room-1"), Is.Null);
    }

    [Test]
    public void AForgottenAnchorIsNotRecalled()
    {
        var memory = new PlacementMemory();
        memory.Remember("room-a", "anchor-1");

        Assert.That(memory.Forget("anchor-1"), Is.True);
        Assert.That(memory.Forget("anchor-1"), Is.False);
        Assert.That(memory.AnchorFor("room-a"), Is.Null);
        Assert.That(memory.MostRecentAnchor, Is.Null);
    }

    [Test]
    public void AnAnchorKeepsOneRoomsPlace()
    {
        var memory = new PlacementMemory();
        memory.Remember("room-a", "anchor-1");

        memory.Remember("room-b", "anchor-1");

        Assert.That(memory.AnchorFor("room-a"), Is.Null);
        Assert.That(memory.AnchorFor("room-b"), Is.EqualTo("anchor-1"));
    }

    [Test]
    public void WhatIsSavedLoadsTheSame()
    {
        var memory = new PlacementMemory();
        memory.Remember("0b6f0c56-3f0c-4c47-b4b0-2a5b1e0f3a11", "a1d5c3a2-8e2b-4d3c-9b8a-7f6e5d4c3b2a");
        memory.Remember("room-b", "anchor-2");

        var loaded = PlacementMemory.Load(memory.Save());

        Assert.That(loaded.Save(), Is.EqualTo(memory.Save()));
        Assert.That(loaded.AnchorFor("0b6f0c56-3f0c-4c47-b4b0-2a5b1e0f3a11"), Is.EqualTo("a1d5c3a2-8e2b-4d3c-9b8a-7f6e5d4c3b2a"));
        Assert.That(loaded.MostRecentAnchor, Is.EqualTo("anchor-2"));
    }

    [Test]
    public void AnythingElseLoadsAsAnEmptyMemory()
    {
        Assert.That(PlacementMemory.Load(null).Count, Is.Zero);
        Assert.That(PlacementMemory.Load("").Count, Is.Zero);
        Assert.That(PlacementMemory.Load("{\"rooms\":[]}").Count, Is.Zero);
        Assert.That(PlacementMemory.Load("halcyonic-room-placements 2\nroom-a anchor-1\n").Count, Is.Zero, "a newer format is not guessed at");

        var partial = PlacementMemory.Load("halcyonic-room-placements 1\nroom-a anchor-1\nbroken\nroom-b anchor-2 extra\n\nroom-c anchor-3\n");
        Assert.That(partial.Count, Is.EqualTo(2), "malformed lines are skipped");
        Assert.That(partial.AnchorFor("room-c"), Is.EqualTo("anchor-3"));
    }

    [Test]
    public void IdsAreSingleWords()
    {
        var memory = new PlacementMemory();

        Assert.Throws<ArgumentException>(() => memory.Remember("room a", "anchor-1"));
        Assert.Throws<ArgumentException>(() => memory.Remember("room-a", ""));
        Assert.Throws<ArgumentNullException>(() => memory.Remember(null!, "anchor-1"));
    }
}

public class RoomStatusTests
{
    private static RoomStatus Room(RoomScan scan, StagePlacement placement, SurfaceKind? surface = null) =>
        new(RoomSpace.Room, PassthroughState.Running, scan, placement, surface);

    [Test]
    public void TheRealRoomIsShownWhereItWorksUnlessThePersonChoseTheVirtualSpace()
    {
        Assert.That(RoomStatus.Initial.Preferred, Is.EqualTo(RoomSpace.Room), "the real room is the default");
        Assert.That(RoomStatus.Initial.Shown, Is.EqualTo(RoomSpace.Room));
        Assert.That(RoomStatus.Initial.With(passthrough: PassthroughState.Unavailable).Shown, Is.EqualTo(RoomSpace.Virtual));
        Assert.That(RoomStatus.Initial.With(preferred: RoomSpace.Virtual).Shown, Is.EqualTo(RoomSpace.Virtual));
        Assert.That(RoomStatus.Initial.With(passthrough: PassthroughState.Unavailable).CanSwitch, Is.False);
        Assert.That(RoomStatus.Initial.With(preferred: RoomSpace.Virtual).CanSwitch, Is.True);
    }

    [Test]
    public void TheSwitchSaysWhereItTakesThePerson()
    {
        Assert.That(RoomStatus.Initial.SwitchLabel, Is.EqualTo("Show a virtual space"));
        Assert.That(RoomStatus.Initial.With(preferred: RoomSpace.Virtual).SwitchLabel, Is.EqualTo("Show my room"));
    }

    [Test]
    public void SpaceSetupIsOfferedOnlyWhereItWouldHelpAndTheHeadsetCanRunIt()
    {
        Assert.That(Room(RoomScan.NotSetUp, StagePlacement.InFront).Offer(canSetUpRoom: true), Is.EqualTo(RoomOffer.SetUpRoom));
        Assert.That(Room(RoomScan.OutsideRooms, StagePlacement.InFront).Offer(canSetUpRoom: true), Is.EqualTo(RoomOffer.SetUpRoom));
        Assert.That(Room(RoomScan.Read, StagePlacement.NoSurface).Offer(canSetUpRoom: true), Is.EqualTo(RoomOffer.SetUpRoom));
        Assert.That(Room(RoomScan.NotSetUp, StagePlacement.InFront).Offer(canSetUpRoom: false), Is.EqualTo(RoomOffer.None));
        Assert.That(Room(RoomScan.Unavailable, StagePlacement.InFront).Offer(canSetUpRoom: true), Is.EqualTo(RoomOffer.None));
        Assert.That(Room(RoomScan.Read, StagePlacement.OnSurface, SurfaceKind.Desk).Offer(canSetUpRoom: true), Is.EqualTo(RoomOffer.None));
        Assert.That(Room(RoomScan.Read, StagePlacement.Searching).Offer(canSetUpRoom: true), Is.EqualTo(RoomOffer.None));
        Assert.That(Room(RoomScan.NotSetUp, StagePlacement.InFront).With(preferred: RoomSpace.Virtual).Offer(canSetUpRoom: true),
            Is.EqualTo(RoomOffer.None), "nothing about the room is offered in the virtual space");
    }

    [Test]
    public void DeniedAccessCanBeAskedForAgainOnceAndThenOnlyTheSettingsCanAllowIt()
    {
        Assert.That(Room(RoomScan.NoAccess, StagePlacement.InFront).Offer(canSetUpRoom: true), Is.EqualTo(RoomOffer.AllowRoomAccess));
        Assert.That(Room(RoomScan.NoAccess, StagePlacement.InFront).Offer(canSetUpRoom: false), Is.EqualTo(RoomOffer.AllowRoomAccess));
        Assert.That(Room(RoomScan.AccessOff, StagePlacement.InFront).Offer(canSetUpRoom: true), Is.EqualTo(RoomOffer.None));
        Assert.That(Room(RoomScan.AccessOff, StagePlacement.InFront).Line, Does.Contain("settings"));
    }

    [Test]
    public void EveryPlacementInFrontOfThePersonSaysWhy()
    {
        var reasons = new[]
        {
            Room(RoomScan.NoAccess, StagePlacement.InFront),
            Room(RoomScan.NotSetUp, StagePlacement.InFront),
            Room(RoomScan.OutsideRooms, StagePlacement.InFront),
            Room(RoomScan.Unavailable, StagePlacement.InFront),
            Room(RoomScan.Read, StagePlacement.NoSurface),
            Room(RoomScan.Read, StagePlacement.LostSurface, SurfaceKind.Desk),
        };
        foreach (var status in reasons)
        {
            Assert.That(status.Line, Does.EndWith("stand in front of you."), status.ToString());
        }
        Assert.That(reasons.Select(status => status.Line).Distinct().Count(), Is.EqualTo(reasons.Length), "each reason reads differently");
    }

    [Test]
    public void APlacementOnASurfaceSaysWhereAndWhetherItIsKept()
    {
        Assert.That(Room(RoomScan.Read, StagePlacement.OnSurface, SurfaceKind.Desk).Line, Is.EqualTo("Your agents are on your desk."));
        Assert.That(Room(RoomScan.Read, StagePlacement.OnSurfaceThisSession, SurfaceKind.Desk).Line, Does.Contain("for this session"));
        Assert.That(Room(RoomScan.Read, StagePlacement.BackOnSurface, SurfaceKind.Desk).Line, Is.EqualTo("Your agents are back on your desk."));
        Assert.That(Room(RoomScan.NoAccess, StagePlacement.BackOnSurface).Line, Is.EqualTo("Your agents are back where you left them."),
            "a placement kept from before holds even where the room's layout cannot be read");
        Assert.That(Room(RoomScan.Read, StagePlacement.OnSurface, SurfaceKind.Desk).OnSurface, Is.True);
        Assert.That(Room(RoomScan.Read, StagePlacement.OnSurface, SurfaceKind.Desk).With(preferred: RoomSpace.Virtual).OnSurface, Is.False,
            "the virtual space never uses the room's surfaces");
    }

    [Test]
    public void TheVirtualSpaceSaysWhyItIsShown()
    {
        Assert.That(RoomStatus.Initial.With(preferred: RoomSpace.Virtual).Line, Is.EqualTo("You are in a virtual space."));
        Assert.That(RoomStatus.Initial.With(passthrough: PassthroughState.Unavailable).Line, Does.StartWith("Passthrough is unavailable"));
    }

    [Test]
    public void TheWordsAreShortAndNameNoBrand()
    {
        var brands = new[] { "Meta", "Quest", "Oculus", "Horizon", "Unity", "Claude", "Anthropic", "Codex", "OpenAI", "OpenCode", "MRUK" };
        var words = RoomStatus.AllWords().Distinct().ToList();
        Assert.That(words, Is.Not.Empty);
        foreach (var word in words)
        {
            Assert.That(word, Is.Not.Empty);
            Assert.That(word.Length, Is.LessThanOrEqualTo(80), $"one short line, never a wall of text: {word}");
            Assert.That(word, Does.Not.Contain("\u2014"), word);
            foreach (var brand in brands) Assert.That(word, Does.Not.Contain(brand), word);
        }
    }
}

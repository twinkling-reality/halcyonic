using System;
using System.Numerics;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

[TestFixture]
public class RoomLayoutTests
{
    private static readonly Vector3 Eyes = new(0.3f, 1.6f, -0.4f);

    // Sizes come from the kit (SpatialKit, DESIGN's); these are the test's own.
    private const float ScreenDegrees = 56f;
    private const float ScreenAspect = 16f / 9f;
    private const float BarDegrees = 5.4f;
    private const float GapDegrees = 0.8f;

    private static float Degrees(Vector3 a, Vector3 b) => MathF.Atan2(Vector3.Cross(a, b).Length(), Vector3.Dot(a, b)) * 180f / MathF.PI;

    /// <summary>The angle at the eyes between a surface's left and right edges, through its centre's height.</summary>
    private static float Across(RoomPose pose, Vector3 eyes) => Degrees(pose.Left - eyes, pose.Right - eyes);

    private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    private static float BearingOf(Vector3 point) => MathF.Atan2(point.X - Eyes.X, point.Z - Eyes.Z) * 180f / MathF.PI;

    [Test]
    public void APickedProjectsScreenSpansItsAngleAboutTwoMetresAhead()
    {
        foreach (var bearing in new[] { 0f, 35f, -120f })
        {
            var screen = RoomLayout.Screen(Eyes, bearing, ScreenDegrees, ScreenAspect);
            Assert.That(Flat(screen.Centre, Eyes), Is.EqualTo(RoomLayout.ScreenDistance).Within(1e-4f));
            Assert.That(screen.Centre.Y, Is.EqualTo(Eyes.Y).Within(1e-5f));
            Assert.That(screen.Bearing, Is.EqualTo(bearing).Within(1e-4f));
            Assert.That(Across(screen, Eyes), Is.EqualTo(ScreenDegrees).Within(0.01f), $"bearing {bearing}");
            Assert.That(screen.Width / screen.Height, Is.EqualTo(ScreenAspect).Within(1e-4f));
            Assert.That(Vector3.Dot(screen.Facing, Vector3.Normalize(Eyes - screen.Centre)), Is.GreaterThan(0.9999f), "faces the eyes");
        }
        Assert.That(RoomLayout.ScreenDistance, Is.EqualTo(2f), "ADR 0040 rule 2");
    }

    [Test]
    public void TheBarStandsBeneathTheScreenSlightlyInFrontWithItsEdges()
    {
        var screen = RoomLayout.Screen(Eyes, 20f, ScreenDegrees, ScreenAspect);
        var bar = RoomLayout.Bar(Eyes, screen, BarDegrees, GapDegrees);
        Assert.That(bar.Width, Is.EqualTo(screen.Width).Within(1e-5f), "the bar's edges are the screen's, never wider");
        Assert.That(Vector3.Distance(bar.Left, screen.Left with { Y = bar.Left.Y }), Is.LessThan(0.03f), "left edges aligned");
        Assert.That(Vector3.Distance(bar.Right, screen.Right with { Y = bar.Right.Y }), Is.LessThan(0.03f), "right edges aligned");
        Assert.That(bar.Bearing, Is.EqualTo(screen.Bearing).Within(1e-4f), "parallel to the screen");
        Assert.That(bar.Top, Is.LessThan(screen.Bottom), "beneath it");
        var closer = Flat(screen.Centre, Eyes) - Flat(bar.Centre, Eyes);
        Assert.That(closer, Is.EqualTo(RoomLayout.BarForward).Within(1e-4f), "slightly in front");
        Assert.That(closer, Is.GreaterThan(0f));
        var barTall = Degrees(bar.Centre with { Y = bar.Top } - Eyes, bar.Centre with { Y = bar.Bottom } - Eyes);
        Assert.That(barTall, Is.EqualTo(BarDegrees).Within(0.05f), "its height is the kit's angle where it stands");
        var gap = Degrees(screen.Centre with { Y = screen.Bottom } - Eyes, bar.Centre with { Y = bar.Top } - Eyes);
        Assert.That(gap, Is.EqualTo(GapDegrees).Within(0.05f));
    }

    [Test]
    public void AMomentPopsInOneSlotBesideItsCharacterTowardThePerson()
    {
        var right = Eyes + new Vector3(1.2f, -0.3f, 1.4f);
        var slot = RoomLayout.MomentSlot(Eyes, 0f, right, 23f, 4f / 3f);
        Assert.That(Flat(slot.Centre, Eyes), Is.EqualTo(Flat(right, Eyes)).Within(1e-4f), "as far as the character");
        Assert.That(slot.Centre.Y, Is.EqualTo(right.Y + RoomLayout.MomentAboveCharacter).Within(1e-5f));
        var characterBearing = BearingOf(right);
        Assert.That(slot.Bearing, Is.LessThan(characterBearing), "on the character's side toward where the person looks");
        Assert.That(Across(slot, Eyes), Is.EqualTo(23f).Within(0.01f));
        Assert.That(Vector3.Dot(slot.Facing, Vector3.Normalize(Eyes - slot.Centre)), Is.GreaterThan(0.9999f), "tilted to face the eyes");
        // Its nearer edge stands clear of the character, at the clearance the room keeps.
        Assert.That(characterBearing - BearingOf(slot.Right), Is.EqualTo(RoomLayout.MomentClearanceDegrees).Within(0.01f));

        var left = Eyes + new Vector3(-1.2f, -0.3f, 1.4f);
        var mirrored = RoomLayout.MomentSlot(Eyes, 0f, left, 23f, 4f / 3f);
        Assert.That(mirrored.Bearing, Is.EqualTo(-slot.Bearing).Within(1e-3f), "a character on the left pops it on its right");

        var again = RoomLayout.MomentSlot(Eyes, 0f, right, 23f, 4f / 3f);
        Assert.That(again, Is.EqualTo(slot), "one fixed slot: the same character, the same place");
    }

    [Test]
    public void AMomentSlotStaysWithinRayCastReach()
    {
        var near = RoomLayout.MomentSlot(Eyes, 0f, Eyes + new Vector3(0.1f, 0f, 0.3f), 23f, 1f);
        Assert.That(Flat(near.Centre, Eyes), Is.EqualTo(RoomLayout.NearestSurface).Within(1e-4f));
        var far = RoomLayout.MomentSlot(Eyes, 0f, Eyes + new Vector3(0f, 0f, 6f), 23f, 1f);
        Assert.That(Flat(far.Centre, Eyes), Is.EqualTo(RoomLayout.FarthestSurface).Within(1e-4f));
        Assert.That((RoomLayout.NearestSurface, RoomLayout.FarthestSurface), Is.EqualTo((0.8f, 3f)), "Meta's ray-cast range");
    }

    [Test]
    public void ReadingUnfoldsAboutAMetreAheadOfWhereThePersonLooks()
    {
        var look = Vector3.Normalize(new Vector3(0.5f, -0.1f, 1f));
        var surface = RoomLayout.ReadingSurface(Eyes, look, 44f, 33f);
        Assert.That(Vector3.Distance(surface.Centre, Eyes), Is.EqualTo(RoomLayout.ReadingDistance).Within(1e-4f));
        Assert.That(Vector3.Dot(Vector3.Normalize(surface.Centre - Eyes), look), Is.GreaterThan(0.9999f));
        Assert.That(Across(surface, Eyes), Is.EqualTo(44f).Within(0.05f));
        var tall = Degrees(surface.Centre + surface.Up * (surface.Height / 2f) - Eyes, surface.Centre - surface.Up * (surface.Height / 2f) - Eyes);
        Assert.That(tall, Is.EqualTo(33f).Within(0.05f));
        Assert.That(Vector3.Dot(surface.Facing, Vector3.Normalize(Eyes - surface.Centre)), Is.GreaterThan(0.9999f), "faces the eyes, tilted with the look");
        Assert.That(RoomLayout.ReadingDistance, Is.EqualTo(1f), "ADR 0040 rule 5");

        var up = RoomLayout.ReadingSurface(Eyes, Vector3.Normalize(new Vector3(0f, 3f, 1f)), 44f, 33f);
        var pitch = MathF.Asin((up.Centre.Y - Eyes.Y) / RoomLayout.ReadingDistance) * 180f / MathF.PI;
        Assert.That(pitch, Is.EqualTo(WorkspaceFollow.MaximumPitchDegrees).Within(0.01f), "a steep look is held to the pitch the room allows");
    }

    [Test]
    public void ReadingFoldsBackIntoTheMomentItCameFrom()
    {
        var slot = RoomLayout.MomentSlot(Eyes, 0f, Eyes + new Vector3(1.2f, -0.3f, 1.4f), 23f, 4f / 3f);
        var open = RoomLayout.ReadingSurface(Eyes, Vector3.UnitZ, 44f, 33f);
        Assert.That(RoomLayout.Between(open, slot, 0f), Is.EqualTo(open));
        Assert.That(RoomLayout.Between(open, slot, 1f), Is.EqualTo(slot));
        Assert.That(RoomLayout.Between(open, slot, -1f), Is.EqualTo(open), "held to the fold's ends");
        Assert.That(RoomLayout.Between(open, slot, 2f), Is.EqualTo(slot));
        var half = RoomLayout.Between(open, slot, 0.5f);
        Assert.That(half.Width, Is.EqualTo((open.Width + slot.Width) / 2f).Within(1e-5f));
        Assert.That(half.Bearing, Is.EqualTo((open.Bearing + slot.Bearing) / 2f).Within(1e-3f));
        var across = RoomLayout.Between(
            new RoomPose(Vector3.UnitZ, 170f, 0f, 1f, 1f), new RoomPose(Vector3.UnitZ, -170f, 0f, 1f, 1f), 0.5f);
        Assert.That(MathF.Abs(across.Bearing), Is.EqualTo(180f).Within(1e-3f), "turns the short way round");
    }

    [Test]
    public void SurfacesOutsideReachAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomLayout.Screen(Eyes, 0f, ScreenDegrees, ScreenAspect, distance: 0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomLayout.Screen(Eyes, 0f, ScreenDegrees, ScreenAspect, distance: 3.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomLayout.Screen(Eyes, 0f, 180f, ScreenAspect));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomLayout.Screen(Eyes, 0f, ScreenDegrees, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomLayout.ReadingSurface(Eyes, Vector3.Zero, 44f, 33f));
    }

    [Test]
    public void AtMostTwoHeadsUpItemsSmallAndLowAtTheSides()
    {
        var leash = new HeadsUpLeash();
        leash.Update(0, Eyes, 0f);
        var left = leash.Item(0, 6f, 2.8f);
        var right = leash.Item(1, 6f, 2.8f);
        Assert.That(left.Bearing, Is.EqualTo(-HeadsUpLeash.SideDegrees).Within(1e-4f));
        Assert.That(right.Bearing, Is.EqualTo(HeadsUpLeash.SideDegrees).Within(1e-4f));
        Assert.That(left.Centre.Y, Is.LessThan(Eyes.Y), "low");
        Assert.That(Vector3.Distance(left.Centre, Eyes), Is.EqualTo(HeadsUpLeash.Distance).Within(1e-4f));
        Assert.That(HeadsUpLeash.Items, Is.EqualTo(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => leash.Item(2, 6f, 2.8f), "a third item has no place");
        Assert.Throws<ArgumentOutOfRangeException>(() => leash.Item(-1, 6f, 2.8f));
    }

    [Test]
    public void TheLeashIgnoresTurnsInsideItsDeadZone()
    {
        var leash = new HeadsUpLeash();
        leash.Update(0, Eyes, 0f);
        var before = leash.Item(1, 6f, 2.8f);
        for (var step = 1; step <= 40; step++) leash.Update(step * 0.05, Eyes, HeadsUpLeash.DeadZoneDegrees - 1f);
        Assert.That(leash.Item(1, 6f, 2.8f), Is.EqualTo(before), "never locked to the head");
        Assert.That(leash.Bearing, Is.EqualTo(0f));
    }

    [Test]
    public void TheLeashTurnsWithTheBodyPastItsDeadZone()
    {
        var leash = new HeadsUpLeash();
        leash.Update(0, Eyes, 0f);
        leash.Update(0.05, Eyes, 90f);
        Assert.That(leash.Bearing, Is.GreaterThan(0f).And.LessThan(90f - HeadsUpLeash.DeadZoneDegrees), "eases, never jumps");
        for (var step = 2; step <= 100; step++) leash.Update(step * 0.05, Eyes, 90f);
        Assert.That(leash.Bearing, Is.EqualTo(90f - HeadsUpLeash.DeadZoneDegrees).Within(0.01f), "comes to rest at the dead zone's edge");

        var round = new HeadsUpLeash();
        round.Update(0, Eyes, 170f);
        for (var step = 1; step <= 100; step++) round.Update(step * 0.05, Eyes, -100f);
        Assert.That(round.Bearing, Is.EqualTo(-100f - HeadsUpLeash.DeadZoneDegrees).Within(0.01f).Or.EqualTo(260f - HeadsUpLeash.DeadZoneDegrees).Within(0.01f),
            "the short way round");
    }

    [Test]
    public void TheLeashFollowsWhereThePersonWalksAndFadesWhileTheyMove()
    {
        var leash = new HeadsUpLeash();
        leash.Update(0, Eyes, 0f);
        Assert.That(leash.Opacity, Is.EqualTo(1f));
        var walking = Eyes;
        for (var step = 1; step <= 10; step++)
        {
            walking += new Vector3(0f, 0f, 0.05f); // 1 m/s
            leash.Update(step * 0.05, walking, 0f);
        }
        Assert.That(leash.Opacity, Is.EqualTo(0f), "faded while walking");
        Assert.That(Vector3.Distance(leash.Item(0, 6f, 2.8f).Centre, walking), Is.EqualTo(HeadsUpLeash.Distance).Within(0.05f), "kept with the person");
        for (var step = 11; step <= 30; step++) leash.Update(step * 0.05, walking, 0f);
        Assert.That(leash.Opacity, Is.EqualTo(1f), "back once still");
    }

    [Test]
    public void TheLeashWaitsForAFirstFiniteHead()
    {
        var leash = new HeadsUpLeash();
        Assert.That(leash.Placed, Is.False);
        leash.Update(0, new Vector3(float.NaN, 0f, 0f), 0f);
        Assert.That(leash.Placed, Is.False);
        Assert.Throws<InvalidOperationException>(() => leash.Item(0, 6f, 2.8f));
        leash.Update(0.1, Eyes, 30f);
        Assert.That((leash.Placed, leash.Bearing), Is.EqualTo((true, 30f)));
    }
}

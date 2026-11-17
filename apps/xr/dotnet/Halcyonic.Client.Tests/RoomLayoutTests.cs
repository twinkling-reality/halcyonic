using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

[TestFixture]
public class RoomLayoutTests
{
    private static readonly Vector3 Eyes = new(0.3f, 1.6f, -0.4f);

    // Sizes come from the kit (SpatialKit, DESIGN's); these are the test's own.
    private const float ScreenDegrees = 56f;
    private const float ScreenHeightDegrees = 31.5f;
    private const float BarDegrees = 5.4f;
    private const float GapDegrees = 0.8f;
    // A character's half-width with its label plate, as the far-3s stage measures one.
    private const float CharacterHalfDegrees = 5.2f;

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
            var screen = RoomLayout.Screen(Eyes, bearing, ScreenDegrees, ScreenHeightDegrees);
            Assert.That(Flat(screen.Centre, Eyes), Is.EqualTo(RoomLayout.ScreenDistance).Within(1e-4f));
            Assert.That(screen.Centre.Y, Is.EqualTo(Eyes.Y).Within(1e-5f));
            Assert.That(screen.Bearing, Is.EqualTo(bearing).Within(1e-4f));
            Assert.That(Across(screen, Eyes), Is.EqualTo(ScreenDegrees).Within(0.01f), $"bearing {bearing}");
            var tall = Degrees(screen.Centre + screen.Up * (screen.Height / 2f) - Eyes, screen.Centre - screen.Up * (screen.Height / 2f) - Eyes);
            Assert.That(tall, Is.EqualTo(ScreenHeightDegrees).Within(0.01f), "as high as the kit's angle, which the kit draws it");
            Assert.That(Vector3.Dot(screen.Facing, Vector3.Normalize(Eyes - screen.Centre)), Is.GreaterThan(0.9999f), "faces the eyes");
        }
        Assert.That(RoomLayout.ScreenDistance, Is.EqualTo(2f), "ADR 0040 rule 2");
    }

    [Test]
    public void TheBarStandsBeneathTheScreenSlightlyInFrontWithItsEdges()
    {
        var screen = RoomLayout.Screen(Eyes, 20f, ScreenDegrees, ScreenHeightDegrees);
        var bar = RoomLayout.Bar(Eyes, screen, BarDegrees, GapDegrees);
        Assert.That(bar.Width, Is.EqualTo(screen.Width).Within(1e-5f), "the bar's edges are the screen's, never wider");
        Assert.That(Vector3.Distance(bar.Left, screen.Left with { Y = bar.Left.Y }), Is.LessThan(0.03f), "left edges aligned");
        Assert.That(Vector3.Distance(bar.Right, screen.Right with { Y = bar.Right.Y }), Is.LessThan(0.03f), "right edges aligned");
        Assert.That(bar.Bearing, Is.EqualTo(screen.Bearing).Within(1e-4f), "parallel to the screen");
        Assert.That(bar.Top, Is.LessThan(screen.Bottom), "beneath it");
        var closer = Flat(screen.Centre, Eyes) - Flat(bar.Centre, Eyes);
        Assert.That(closer, Is.EqualTo(RoomLayout.BarForward).Within(1e-4f), "slightly in front");
        Assert.That(closer, Is.GreaterThan(0f));
        var barTop = bar.Centre + bar.Up * (bar.Height / 2f);
        var barTall = Degrees(barTop - Eyes, bar.Centre - bar.Up * (bar.Height / 2f) - Eyes);
        Assert.That(barTall, Is.EqualTo(BarDegrees).Within(0.01f), "its height is the kit's angle where it stands");
        var gap = Degrees(screen.Centre - screen.Up * (screen.Height / 2f) - Eyes, barTop - Eyes);
        Assert.That(gap, Is.EqualTo(GapDegrees).Within(0.01f));
        Assert.That(Vector3.Dot(bar.Facing, Vector3.Normalize(Eyes - bar.Centre)), Is.GreaterThan(0.9999f), "tilted up to face the eyes, low in the view");
        Assert.That(bar.Pitch, Is.LessThan(-10f));
        Assert.That(RoomLayout.Bar(Eyes, screen, BarDegrees), Is.EqualTo(RoomLayout.Bar(Eyes, screen, BarDegrees, RoomLayout.BarGapDegrees)), "the room's own gap by default");
        Assert.That(RoomLayout.BarGapDegrees, Is.EqualTo(0.75f));
    }

    /// <summary>
    /// DEVICE's D-XR-8: level at eye height whatever the look, the screen sat low under a raised one and its bar was cut.
    /// Given the console's elevation it stands square to the eyes there, its bar the room's gap under its bottom edge as the
    /// eyes see it, in its plane brought BarForward nearer.
    /// </summary>
    [Test]
    public void AScreenStandsOnTheElevationItIsGivenWithItsBarBeneathItAsTheEyesSeeIt()
    {
        foreach (var elevation in new[] { -20f, 25f })
        {
            var screen = RoomLayout.Screen(Eyes, 30f, ScreenDegrees, ScreenHeightDegrees, elevation: elevation);
            Assert.That(Vector3.Distance(screen.Centre, Eyes), Is.EqualTo(RoomLayout.ScreenDistance).Within(1e-4f), "about 2 m along that elevation");
            Assert.That(MathF.Asin((screen.Centre.Y - Eyes.Y) / RoomLayout.ScreenDistance) * 180f / MathF.PI, Is.EqualTo(elevation).Within(1e-3f));
            Assert.That(screen.Pitch, Is.EqualTo(elevation));
            Assert.That(Vector3.Dot(screen.Facing, Vector3.Normalize(Eyes - screen.Centre)), Is.GreaterThan(0.9999f), "square to the eyes");
            Assert.That(Across(screen, Eyes), Is.EqualTo(ScreenDegrees).Within(0.01f), $"elevation {elevation}");
            var bottom = screen.Centre - screen.Up * (screen.Height / 2f);
            Assert.That(Degrees(screen.Centre + screen.Up * (screen.Height / 2f) - Eyes, bottom - Eyes), Is.EqualTo(ScreenHeightDegrees).Within(0.01f));
            var bar = RoomLayout.Bar(Eyes, screen, BarDegrees, GapDegrees);
            var barTop = bar.Centre + bar.Up * (bar.Height / 2f);
            Assert.That(Degrees(bottom - Eyes, barTop - Eyes), Is.EqualTo(GapDegrees).Within(0.01f), "the gap under the screen's bottom edge, as the eyes see it");
            Assert.That(Degrees(barTop - Eyes, bar.Centre - bar.Up * (bar.Height / 2f) - Eyes), Is.EqualTo(BarDegrees).Within(0.01f));
            Assert.That(Vector3.Dot(screen.Centre - bar.Centre, Vector3.Normalize(screen.Centre - Eyes)), Is.EqualTo(RoomLayout.BarForward).Within(1e-4f),
                "in the screen's plane, slightly in front");
            Assert.That((bar.Width, bar.Bearing), Is.EqualTo((screen.Width, screen.Bearing)));
            Assert.That(Vector3.Dot(bar.Facing, Vector3.Normalize(Eyes - bar.Centre)), Is.GreaterThan(0.9999f), "tilted to face the eyes");
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomLayout.Screen(Eyes, 0f, ScreenDegrees, ScreenHeightDegrees, elevation: 90f));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomLayout.Screen(Eyes, 0f, ScreenDegrees, ScreenHeightDegrees, elevation: float.NaN));
    }

    [Test]
    public void AMomentPopsInOneSlotBesideItsCharacterTowardThePerson()
    {
        var right = Eyes + new Vector3(1.2f, -0.3f, 1.4f);
        var slot = RoomLayout.MomentSlot(Eyes, 0f, right, CharacterHalfDegrees, 23f, 4f / 3f);
        Assert.That(Flat(slot.Centre, Eyes), Is.EqualTo(Flat(right, Eyes)).Within(1e-4f), "as far as the character");
        Assert.That(slot.Centre.Y, Is.EqualTo(right.Y + RoomLayout.MomentAboveCharacter).Within(1e-5f));
        var characterBearing = BearingOf(right);
        Assert.That(slot.Bearing, Is.LessThan(characterBearing), "on the character's side toward where the person looks");
        Assert.That(Across(slot, Eyes), Is.EqualTo(23f).Within(0.01f));
        Assert.That(Vector3.Dot(slot.Facing, Vector3.Normalize(Eyes - slot.Centre)), Is.GreaterThan(0.9999f), "tilted to face the eyes");
        // Its nearer edge stands clear of the character's body and label, at the clearance the room keeps.
        Assert.That(characterBearing - BearingOf(slot.Right), Is.EqualTo(CharacterHalfDegrees + RoomLayout.MomentClearanceDegrees).Within(0.01f));

        var left = Eyes + new Vector3(-1.2f, -0.3f, 1.4f);
        var mirrored = RoomLayout.MomentSlot(Eyes, 0f, left, CharacterHalfDegrees, 23f, 4f / 3f);
        Assert.That(mirrored.Bearing, Is.EqualTo(-slot.Bearing).Within(1e-3f), "a character on the left pops it on its right");

        var again = RoomLayout.MomentSlot(Eyes, 0f, right, CharacterHalfDegrees, 23f, 4f / 3f);
        Assert.That(again, Is.EqualTo(slot), "one fixed slot: the same character, the same place");
    }

    [Test]
    public void AWideLabelPushesTheMomentSlotOut()
    {
        var character = Eyes + new Vector3(1.2f, -0.3f, 1.4f);
        var narrow = RoomLayout.MomentSlot(Eyes, 0f, character, 4f, 23f, 4f / 3f);
        var wide = RoomLayout.MomentSlot(Eyes, 0f, character, 12f, 23f, 4f / 3f);
        Assert.That(narrow.Bearing - wide.Bearing, Is.EqualTo(8f).Within(0.01f), "as far again as the label is wider");
        Assert.That(BearingOf(character) - BearingOf(wide.Right), Is.EqualTo(12f + RoomLayout.MomentClearanceDegrees).Within(0.01f));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomLayout.MomentSlot(Eyes, 0f, character, -1f, 23f, 1f));
    }

    [Test]
    public void AMomentSlotStaysWithinRayCastReach()
    {
        var near = RoomLayout.MomentSlot(Eyes, 0f, Eyes + new Vector3(0.1f, 0f, 0.3f), CharacterHalfDegrees, 23f, 1f);
        Assert.That(Flat(near.Centre, Eyes), Is.EqualTo(RoomLayout.NearestSurface).Within(1e-4f));
        var far = RoomLayout.MomentSlot(Eyes, 0f, Eyes + new Vector3(0f, 0f, 6f), CharacterHalfDegrees, 23f, 1f);
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
        var slot = RoomLayout.MomentSlot(Eyes, 0f, Eyes + new Vector3(1.2f, -0.3f, 1.4f), CharacterHalfDegrees, 23f, 4f / 3f);
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
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomLayout.Screen(Eyes, 0f, ScreenDegrees, ScreenHeightDegrees, distance: 0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomLayout.Screen(Eyes, 0f, ScreenDegrees, ScreenHeightDegrees, distance: 3.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomLayout.Screen(Eyes, 0f, 180f, ScreenHeightDegrees));
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

    /// <summary>No two screens cover one another, and every one's edges stand within the seated reach of <paramref name="ahead"/>.</summary>
    private static void Apart(float[] bearings, float width, float ahead = 0f)
    {
        for (var index = 1; index < bearings.Length; index++)
            Assert.That(RoomLayout.Wrap(bearings[index] - bearings[index - 1]), Is.GreaterThanOrEqualTo(width + RoomLayout.ScreensGapDegrees - 1e-3f), $"screens {index - 1} and {index} cover one another");
        foreach (var bearing in bearings)
            Assert.That(MathF.Abs(RoomLayout.Wrap(bearing - ahead)) + width / 2f, Is.LessThanOrEqualTo(RoomLayout.SeatedReachDegrees + 1e-3f), "a screen stands past the seated reach");
    }

    [Test]
    public void OneScreenStandsWhereItsPlaceIs()
    {
        foreach (var bearing in new[] { 0f, 35f, -60f, 72f })
            Assert.That(RoomLayout.Screens(new[] { bearing }, ScreenDegrees)[0], Is.EqualTo(bearing).Within(1e-4f));
        Assert.That(RoomLayout.Screens(Array.Empty<float>(), ScreenDegrees), Is.Empty);
    }

    [Test]
    public void ScreensAskedAtOneBearingPartEvenlyAboutIt()
    {
        var two = RoomLayout.Screens(new[] { 10f, 10f }, ScreenDegrees);
        Assert.That(two[0], Is.EqualTo(10f - (ScreenDegrees + RoomLayout.ScreensGapDegrees) / 2f).Within(1e-3f));
        Assert.That(two[1], Is.EqualTo(10f + (ScreenDegrees + RoomLayout.ScreensGapDegrees) / 2f).Within(1e-3f));
        var three = RoomLayout.Screens(new[] { 0f, 0f, 0f }, ScreenDegrees);
        Assert.That(three, Is.EqualTo(new[] { -60f, 0f, 60f }).Within(1e-3f));
        Apart(three, ScreenDegrees);
    }

    [Test]
    public void ScreensFarEnoughApartStandAtTheirPlacesAndCloseOnesMoveOnlyAsFarAsTheyMust()
    {
        Assert.That(RoomLayout.Screens(new[] { -65f, 0f, 65f }, ScreenDegrees), Is.EqualTo(new[] { -65f, 0f, 65f }).Within(1e-3f), "far enough apart: each at its place");
        // The left two too close, the right one apart: only the left two part, about their middle.
        var parted = RoomLayout.Screens(new[] { -20f, 0f, 70f }, 40f);
        Assert.That(parted, Is.EqualTo(new[] { -32f, 12f, 70f }).Within(1e-3f));
        Apart(parted, 40f);
        // Three full screens fill most of the reach, so the row slides in as one.
        var full = RoomLayout.Screens(new[] { -20f, 0f, 70f }, ScreenDegrees);
        Assert.That(full, Is.EqualTo(new[] { -48f, 12f, 72f }).Within(1e-3f));
        Apart(full, ScreenDegrees);
    }

    [Test]
    public void NoScreenStandsPastASeatedPersonsReachNorBehindThem()
    {
        // A place far round, or behind: its screen stands at the reach's edge on its own side.
        Assert.That(RoomLayout.Screens(new[] { 150f }, ScreenDegrees)[0], Is.EqualTo(RoomLayout.SeatedReachDegrees - ScreenDegrees / 2f).Within(1e-3f));
        Assert.That(RoomLayout.Screens(new[] { -170f }, ScreenDegrees)[0], Is.EqualTo(-(RoomLayout.SeatedReachDegrees - ScreenDegrees / 2f)).Within(1e-3f));
        // Two on the right, the outer past the reach: the row slides in, never through behind.
        var right = RoomLayout.Screens(new[] { 70f, 95f }, ScreenDegrees);
        Assert.That(right, Is.EqualTo(new[] { 12f, 72f }).Within(1e-3f));
        // Measured from where the person faces, not the room's zero.
        var turned = RoomLayout.Screens(new[] { 30f + 150f }, ScreenDegrees, ahead: 30f);
        Assert.That(turned[0], Is.EqualTo(30f + RoomLayout.SeatedReachDegrees - ScreenDegrees / 2f).Within(1e-3f));
    }

    [Test]
    public void ScreensKeepThePlacesLeftToRightOrderAndNeverCoverOneAnother()
    {
        var random = new Random(17);
        foreach (var width in new[] { ScreenDegrees, 48f })
            for (var trial = 0; trial < 500; trial++)
            {
                var count = random.Next(1, 4);
                var ahead = (float)(random.NextDouble() * 360.0 - 180.0);
                var wanted = new float[count];
                for (var index = 0; index < count; index++) wanted[index] = (float)(random.NextDouble() * 360.0 - 180.0);
                var stood = RoomLayout.Screens(wanted, width, ahead);
                Assert.That(stood.Length, Is.EqualTo(count));
                Apart(stood, width, ahead);
                foreach (var bearing in stood) Assert.That(bearing, Is.GreaterThan(-180f).And.LessThanOrEqualTo(180f));
            }
    }

    [Test]
    public void MoreScreensThanTheReachHoldsAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomLayout.Screens(new float[4], ScreenDegrees));
        Assert.That(RoomLayout.Screens(new float[4], 40f), Has.Length.EqualTo(4), "four narrower ones fit");
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomLayout.Screens(new[] { float.NaN }, ScreenDegrees));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomLayout.Screens(new[] { 0f }, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomLayout.Screens(new[] { 0f }, ScreenDegrees, gapDegrees: -1f));
    }

    // FIELD's 19:11Z places: each 32.5 degrees wide, inner at 46.75, outer at 83.25, 4 degrees between, the front clear past 30.
    private const float PlaceDegrees = 32.5f;

    [Test]
    public void ScreensOverFieldsPlacesStandOverThemAndKeepTheFrontClear()
    {
        var places = new[] { -83.25f, -46.75f, 46.75f, 83.25f };
        var stood = RoomLayout.Screens(places, PlaceDegrees, clearAheadDegrees: RoomLayout.ClearAheadDegrees);
        Assert.That(stood, Is.EqualTo(places).Within(1e-3f), "each screen over its own place");
        Apart(stood, PlaceDegrees);
        foreach (var bearing in stood) Assert.That(MathF.Abs(bearing) - PlaceDegrees / 2f, Is.GreaterThanOrEqualTo(RoomLayout.ClearAheadDegrees - 1e-3f), "a screen stands in the clear front");
    }

    [Test]
    public void AScreenAskedInTheClearFrontStandsBesideItOnItsOwnSide()
    {
        var right = RoomLayout.Screens(new[] { 10f }, PlaceDegrees, clearAheadDegrees: RoomLayout.ClearAheadDegrees);
        Assert.That(right[0], Is.EqualTo(RoomLayout.ClearAheadDegrees + PlaceDegrees / 2f).Within(1e-3f));
        var left = RoomLayout.Screens(new[] { -5f }, PlaceDegrees, clearAheadDegrees: RoomLayout.ClearAheadDegrees);
        Assert.That(left[0], Is.EqualTo(-(RoomLayout.ClearAheadDegrees + PlaceDegrees / 2f)).Within(1e-3f));
        // Measured from where the person faces.
        var turned = RoomLayout.Screens(new[] { 40f }, PlaceDegrees, ahead: 40f, clearAheadDegrees: RoomLayout.ClearAheadDegrees);
        Assert.That(turned[0], Is.EqualTo(40f + RoomLayout.ClearAheadDegrees + PlaceDegrees / 2f).Within(1e-3f), "straight ahead goes right");
    }

    [Test]
    public void AFullSidePassesItsNearestScreenAcrossTheFront()
    {
        // Three asked on the right, which holds two: the leftmost of them crosses to the left side.
        var stood = RoomLayout.Screens(new[] { 40f, 60f, 90f }, PlaceDegrees, clearAheadDegrees: RoomLayout.ClearAheadDegrees);
        Assert.That(stood[0], Is.LessThan(0f));
        Assert.That(stood[1], Is.GreaterThan(0f));
        Apart(new[] { stood[1], stood[2] }, PlaceDegrees);
        foreach (var bearing in stood) Assert.That(MathF.Abs(bearing) - PlaceDegrees / 2f, Is.GreaterThanOrEqualTo(RoomLayout.ClearAheadDegrees - 1e-3f));
        // Five never fit four places' room; full-width screens fit one a side.
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomLayout.Screens(new float[5], PlaceDegrees, clearAheadDegrees: RoomLayout.ClearAheadDegrees));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomLayout.Screens(new float[3], ScreenDegrees, clearAheadDegrees: RoomLayout.ClearAheadDegrees));
        Assert.That(RoomLayout.Screens(new[] { -50f, 50f }, ScreenDegrees, clearAheadDegrees: RoomLayout.ClearAheadDegrees), Has.Length.EqualTo(2));
    }

    [Test]
    public void ScreensBesideTheClearFrontStayInOrderApartAndInReach()
    {
        var random = new Random(23);
        for (var trial = 0; trial < 500; trial++)
        {
            var count = random.Next(1, 5);
            var ahead = (float)(random.NextDouble() * 360.0 - 180.0);
            var wanted = new float[count];
            for (var index = 0; index < count; index++) wanted[index] = (float)(random.NextDouble() * 360.0 - 180.0);
            var stood = RoomLayout.Screens(wanted, PlaceDegrees, ahead, clearAheadDegrees: RoomLayout.ClearAheadDegrees);
            var about = stood.Select(bearing => RoomLayout.Wrap(bearing - ahead)).ToArray();
            for (var index = 1; index < count; index++) Assert.That(about[index] - about[index - 1], Is.GreaterThanOrEqualTo(PlaceDegrees + RoomLayout.ScreensGapDegrees - 1e-3f), "order or overlap");
            foreach (var bearing in about)
            {
                Assert.That(MathF.Abs(bearing) - PlaceDegrees / 2f, Is.GreaterThanOrEqualTo(RoomLayout.ClearAheadDegrees - 1e-3f), "in the clear front");
                Assert.That(MathF.Abs(bearing) + PlaceDegrees / 2f, Is.LessThanOrEqualTo(RoomLayout.SeatedReachDegrees + 1e-3f), "past the reach");
            }
        }
    }

    [Test]
    public void ATeamsScreenStandsOverItsPlaceItsLowerEdgeEightDegreesUp()
    {
        foreach (var bearing in new[] { -83.25f, 46.75f })
        {
            var screen = RoomLayout.TeamScreen(Eyes, bearing, 1.4f, 16f / 9f);
            Assert.That(screen.Bearing, Is.EqualTo(bearing).Within(1e-4f));
            Assert.That(Across(screen, Eyes), Is.EqualTo(RoomLayout.TeamScreenDegrees).Within(0.01f));
            var lower = screen.Centre - screen.Up * (screen.Height / 2f) - Eyes;
            var lowerUp = MathF.Atan2(lower.Y, Flat(lower + Eyes, Eyes)) * 180f / MathF.PI;
            Assert.That(lowerUp, Is.EqualTo(RoomLayout.TeamScreenLowestDegrees).Within(0.05f), "its lower edge over the team's highest tag");
            var tall = Degrees(screen.Centre + screen.Up * (screen.Height / 2f) - Eyes, screen.Centre - screen.Up * (screen.Height / 2f) - Eyes);
            Assert.That(tall, Is.EqualTo(RoomLayout.TeamScreenDegrees * 9f / 16f).Within(0.01f), "as high as the kit lays it");
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomLayout.TeamScreen(Eyes, 0f, 1.4f, 0f));
    }

    [TestCase(6)]
    [TestCase(9)]
    public void MoreTeamScreensThanStandAtOnceArePagedNeverThrown(int count)
    {
        // TS1: past what fits, the rest wait on further pages, every page apart, in reach and out of the clear front.
        var places = Enumerable.Range(0, count).Select(index => PlaceBearings.Of(index).YawDegrees).OrderBy(yaw => yaw).ToArray();
        var held = RoomLayout.ScreensHeld(RoomLayout.TeamScreenDegrees, clearAheadDegrees: RoomLayout.ClearAheadDegrees);
        Assert.That(held, Is.EqualTo(4), "two a side at 24 degrees between the clear front and the seated reach");
        var seen = new System.Collections.Generic.HashSet<int>();
        var first = RoomLayout.ScreensPaged(places, RoomLayout.TeamScreenDegrees, 0, clearAheadDegrees: RoomLayout.ClearAheadDegrees);
        Assert.That(first.Pages, Is.EqualTo((count + held - 1) / held));
        for (var page = 0; page < first.Pages; page++)
        {
            var shown = RoomLayout.ScreensPaged(places, RoomLayout.TeamScreenDegrees, page, clearAheadDegrees: RoomLayout.ClearAheadDegrees);
            Assert.That(shown.Shown.Length, Is.LessThanOrEqualTo(held));
            Assert.That(shown.Waiting, Is.EqualTo(count - shown.Shown.Length));
            foreach (var index in shown.Shown) Assert.That(seen.Add(index), Is.True, "a screen on two pages");
            for (var index = 1; index < shown.Bearings.Length; index++)
                Assert.That(shown.Bearings[index] - shown.Bearings[index - 1], Is.GreaterThanOrEqualTo(RoomLayout.TeamScreenDegrees + RoomLayout.ScreensGapDegrees - 1e-3f));
            foreach (var bearing in shown.Bearings)
            {
                Assert.That(MathF.Abs(bearing) - RoomLayout.TeamScreenDegrees / 2f, Is.GreaterThanOrEqualTo(RoomLayout.ClearAheadDegrees - 1e-3f));
                Assert.That(MathF.Abs(bearing) + RoomLayout.TeamScreenDegrees / 2f, Is.LessThanOrEqualTo(RoomLayout.SeatedReachDegrees + 1e-3f));
            }
        }
        Assert.That(seen.Count, Is.EqualTo(count), "every screen stands on some page");
        Assert.That(RoomLayout.ScreensPaged(places, RoomLayout.TeamScreenDegrees, first.Pages, clearAheadDegrees: RoomLayout.ClearAheadDegrees).Page, Is.EqualTo(0), "past the last page wraps to the first");
    }
}

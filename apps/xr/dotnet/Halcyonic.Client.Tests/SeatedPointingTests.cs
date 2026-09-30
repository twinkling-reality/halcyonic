using System;
using System.Numerics;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// A seated person, eyes 1.2 m above the floor facing +Z, and the two places characters stand: on
/// the arc 2.4 m away, 0.45 m below the eyes, and on a desk 0.46 m below the eyes, 0.55 m ahead.
/// Targets are the spheres the workspace gives each body for rays and the gaze.
/// </summary>
internal static class Seat
{
    public static readonly HeadPose Head = new(new Vector3(0f, 1.2f, 0f), new Vector3(0f, -0.1f, 1f));

    /// <summary>The character in the slot 6 degrees right of the middle of the arc 2.4 m away, and the radius of its ray target (the stage's scale times 0.1).</summary>
    public static readonly (Vector3 Center, float Radius) Far = (Slot(2.4f, 6f, 0.75f), 0.24f);

    /// <summary>The same slot of the lineup on a desk at 0.74 m, 0.55 m ahead, its body about 0.1 m above the desk.</summary>
    public static readonly (Vector3 Center, float Radius) Near = (Slot(0.55f, 6f, 0.84f), 0.055f);

    private static Vector3 Slot(float radius, float degrees, float height) =>
        new(radius * MathF.Sin(degrees * MathF.PI / 180f), height, radius * MathF.Cos(degrees * MathF.PI / 180f));

    public static readonly Vector3 Down = -Vector3.UnitY;

    /// <summary>A right hand, its palm turned to face left, toward the body's middle: relaxed, pointing.</summary>
    public static HandPosture Pointing(float drop, float forward = 0.38f, float side = 0.12f) =>
        new(true, true, new Vector3(side, 1.2f - drop, forward), new Vector3(side, 1.2f - drop - 0.02f, forward - 0.06f), -Vector3.UnitX);

    public static bool Hits(SeatedRay ray, (Vector3 Center, float Radius) target)
    {
        var toCenter = target.Center - ray.Origin;
        var along = Vector3.Dot(toCenter, ray.Direction);
        if (along < 0f) return false;
        var closest = ray.Origin + ray.Direction * along;
        return Vector3.Distance(closest, target.Center) <= target.Radius;
    }
}

public class SeatedPointingTests
{
    [Test]
    public void ARelaxedHandALittleAboveADeskPointsAtTheCharactersFarAndNear()
    {
        // Forearm resting at about 40 cm below the eyes, the palm turned sideways.
        var ray = SeatedPointing.Aim(Seat.Head, Seat.Pointing(drop: 0.40f));

        Assert.That(ray.Active, Is.True);
        Assert.That(Seat.Hits(ray, Seat.Far), Is.True, "the arc 2.4 m away");
        Assert.That(ray.Origin, Is.EqualTo(Seat.Pointing(0.40f).Knuckle), "the ray starts at the knuckle, which a pinch barely moves");

        // The lineup on the desk is within reach: the hand moves in front of the character it points at.
        var near = SeatedPointing.Aim(Seat.Head, Seat.Pointing(drop: 0.40f, side: 0.06f));
        Assert.That(near.Active, Is.True);
        Assert.That(Seat.Hits(near, Seat.Near), Is.True, "the lineup on the desk");
    }

    [Test]
    public void TheHandNeedNotRiseToTheShoulder()
    {
        // The headset's own ray runs from the shoulder, about 0.25 m below the eyes, through the
        // hand: to reach the arc 2.4 m away the hand must be held near that height.
        var shoulder = new Vector3(0.13f, 1.2f - 0.25f, -0.13f);
        var hand = Seat.Pointing(drop: 0.40f);
        var headsetRay = new SeatedRay(true, hand.Knuckle, Vector3.Normalize(hand.Knuckle - shoulder), false, false);
        Assert.That(Seat.Hits(headsetRay, Seat.Far), Is.False, "from a relaxed height the headset's ray points below the arc");

        Assert.That(Seat.Hits(SeatedPointing.Aim(Seat.Head, hand), Seat.Far), Is.True);
    }

    [Test]
    public void HandsRestingOrTypingPalmDownHaveNoRay()
    {
        foreach (var drop in new[] { 0.40f, 0.46f, 0.6f })
        {
            var typing = new HandPosture(true, true, new Vector3(0.12f, 1.2f - drop, 0.35f), new Vector3(0.12f, 1.2f - drop, 0.3f), Seat.Down);
            var ray = SeatedPointing.Aim(Seat.Head, typing);
            Assert.That(ray.Active, Is.False, $"{drop} m below the eyes");
            Assert.That(ray.PalmDown, Is.True);
        }

        // Tilted a little, as on a keyboard, still resting.
        var tilted = Vector3.Normalize(new Vector3(-0.3f, -1f, 0.2f));
        Assert.That(SeatedPointing.Aim(Seat.Head, new HandPosture(true, true, new Vector3(0.12f, 0.74f, 0.35f), new Vector3(0.12f, 0.74f, 0.3f), tilted)).Active,
            Is.False);
    }

    [Test]
    public void APalmTurnedHalfwayFromTheFloorPoints()
    {
        // A forearm resting half turned, palm down and in, as it falls naturally.
        var halfway = Vector3.Normalize(new Vector3(-1f, -1f, 0f));
        var ray = SeatedPointing.Aim(Seat.Head, new HandPosture(true, true, new Vector3(0.12f, 0.8f, 0.38f), new Vector3(0.12f, 0.78f, 0.32f), halfway));
        Assert.That(ray.Active, Is.True);
        Assert.That(Seat.Hits(ray, Seat.Far), Is.True);
    }

    [Test]
    public void HandsLowInTheLapPointAtTheFloorNotAtTheCharacters()
    {
        var lap = SeatedPointing.Aim(Seat.Head, Seat.Pointing(drop: 0.65f, forward: 0.25f));
        Assert.That(Seat.Hits(lap, Seat.Far), Is.False);
        Assert.That(Seat.Hits(lap, Seat.Near), Is.False);
        Assert.That(lap.Direction.Y, Is.LessThan(-0.4f), "steeply down");

        // Resting on the desk on its side: below the lineup standing on it.
        var desk = SeatedPointing.Aim(Seat.Head, Seat.Pointing(drop: 0.47f, forward: 0.3f));
        Assert.That(Seat.Hits(desk, Seat.Near), Is.False);
    }

    [Test]
    public void APalmFacingTheEyesIsTheSystemGestureAndHasNoRay()
    {
        var palm = new Vector3(0.1f, 0.95f, 0.3f);
        var towardEyes = Vector3.Normalize(Seat.Head.Position - palm);
        var ray = SeatedPointing.Aim(Seat.Head, new HandPosture(true, true, palm + new Vector3(0f, 0.04f, 0.02f), palm, towardEyes));

        Assert.That(ray.PalmFacesHead, Is.True);
        Assert.That(ray.Active, Is.False);
    }

    [Test]
    public void AHandBesideOrBehindTheEyesOrUntrackedHasNoRay()
    {
        Assert.That(SeatedPointing.Aim(Seat.Head, Seat.Pointing(drop: 0.4f, forward: 0.05f)).Active, Is.False, "beside the head");
        var untracked = Seat.Pointing(drop: 0.4f);
        untracked = new HandPosture(false, true, untracked.Knuckle, untracked.Palm, untracked.PalmNormal);
        Assert.That(SeatedPointing.Aim(Seat.Head, untracked).Active, Is.False);
    }

    [Test]
    public void TheLeftHandPivotsOnItsOwnSideAndEverythingTurnsWithTheHead()
    {
        var left = SeatedPointing.Pivot(Seat.Head, right: false, scale: 1f);
        var right = SeatedPointing.Pivot(Seat.Head, right: true, scale: 1f);
        Assert.That(left.X, Is.EqualTo(-SeatedPointing.PivotSide).Within(1e-5f));
        Assert.That(right.X, Is.EqualTo(SeatedPointing.PivotSide).Within(1e-5f));
        Assert.That(right.Y, Is.EqualTo(1.2f - SeatedPointing.PivotDown).Within(1e-5f));
        Assert.That(right.Z, Is.EqualTo(-SeatedPointing.PivotBack).Within(1e-5f));

        // Facing +X, the person's right is -Z.
        var turned = new HeadPose(Seat.Head.Position, Vector3.UnitX);
        var pivot = SeatedPointing.Pivot(turned, right: true, scale: 1f);
        Assert.That(pivot.Z, Is.EqualTo(-SeatedPointing.PivotSide).Within(1e-5f));
        Assert.That(pivot.X, Is.EqualTo(-SeatedPointing.PivotBack).Within(1e-5f));
    }
}

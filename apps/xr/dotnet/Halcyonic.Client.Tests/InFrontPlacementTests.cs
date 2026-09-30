using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>Drives an <see cref="InFrontPlacement"/> at 72 frames a second with a head the test moves.</summary>
internal sealed class PlacementFrames
{
    public const float Frame = 1f / 72f;

    public InFrontPlacement Placement { get; } = new();

    public float Now { get; private set; }

    public Vector3 Head = new(0.1f, 1.2f, -0.2f);

    public float Yaw = 20f;

    public bool Tracked = true;

    public List<PlacementDecision> Decisions { get; } = new();

    public PlacementDecision Step(float frame = Frame)
    {
        Now += frame;
        var decision = Placement.Poll(Now, frame, new HeadSample(Head, Yaw), Tracked);
        if (decision.Action != PlacementAction.None) Decisions.Add(decision);
        return decision;
    }

    public void Run(float seconds, float degreesPerSecond = 0f)
    {
        var frames = (int)MathF.Round(seconds / Frame);
        for (var index = 0; index < frames; index++)
        {
            Yaw += degreesPerSecond * Frame;
            Step();
        }
    }

    /// <summary>Past the first placement, with the log cleared.</summary>
    public static PlacementFrames Placed()
    {
        var frames = new PlacementFrames();
        frames.Run(1f);
        Assert.That(frames.Decisions, Has.Count.EqualTo(1));
        Assert.That(frames.Decisions[0].Reason, Is.EqualTo("the session started"));
        frames.Decisions.Clear();
        return frames;
    }

    public List<PlacementAction> Actions() => Decisions.ConvertAll(decision => decision.Action);
}

public class InFrontPlacementTests
{
    [Test]
    public void TheSessionStartPlacesTheStageOnceTheHeadIsTracked()
    {
        var frames = new PlacementFrames { Tracked = false };
        frames.Run(1f);
        Assert.That(frames.Decisions, Is.Empty, "waiting for tracking");

        frames.Tracked = true;
        frames.Step();
        Assert.That(frames.Actions(), Is.EqualTo(new[] { PlacementAction.PlaceInFront }));
        Assert.That(frames.Decisions[0].Reason, Is.EqualTo("the session started"));

        var untracked = new PlacementFrames { Tracked = false };
        untracked.Run(InFrontPlacement.TrackingTimeoutSeconds + 0.1f);
        Assert.That(untracked.Actions(), Is.EqualTo(new[] { PlacementAction.PlaceInFront }), "without tracking, from whatever pose there is");
    }

    [Test]
    public void ReferenceSpaceChangesDuringFocusChangesNeverMoveTheStage()
    {
        // Virtual Display's windows open over the app: focus flaps several times a second for half
        // a minute, each flap reporting a reference space change, while the head moves naturally.
        var frames = PlacementFrames.Placed();
        var random = new Random(7);
        for (var flap = 0; flap < 120; flap++)
        {
            frames.Placement.FocusChanged(frames.Now);
            frames.Placement.OriginChanged(frames.Now);
            frames.Run(0.1f + (float)random.NextDouble() * 0.3f, degreesPerSecond: (float)(random.NextDouble() - 0.5) * 120f);
        }
        frames.Run(2f);

        Assert.That(frames.Actions(), Has.No.Member(PlacementAction.PlaceInFront));
        Assert.That(frames.Actions(), Has.No.Member(PlacementAction.Follow), "nothing moved, so nothing follows");
        Assert.That(frames.Actions(), Has.All.EqualTo(PlacementAction.Kept));
        Assert.That(frames.Decisions, Is.Not.Empty, "the log says what was ignored");
        Assert.That(frames.Decisions.Count, Is.LessThan(20), "a few lines, not one per flap");
        Assert.That(frames.Decisions[0].Reason, Does.Contain("during focus changes and nothing moved"));
    }

    [Test]
    public void AChangeThatMovesNothingIsIgnoredEvenWithoutAFocusChange()
    {
        var frames = PlacementFrames.Placed();
        frames.Placement.OriginChanged(frames.Now);
        frames.Run(2f);

        Assert.That(frames.Actions(), Is.EqualTo(new[] { PlacementAction.Kept }));
        Assert.That(frames.Decisions[0].Reason, Is.EqualTo("the reference space changed once and nothing moved"));
    }

    [Test]
    public void ARecenterMovesTheStageWithTheSpaceAtOnceAndThenInFrontOfThePerson()
    {
        var frames = PlacementFrames.Placed();
        frames.Run(0.5f);
        frames.Placement.OriginChanged(frames.Now);
        frames.Step();
        // The recenter: the person, turned 20 degrees, becomes the new forward at the origin.
        frames.Head = new Vector3(0f, 1.2f, 0f);
        frames.Yaw = 0f;
        var jump = frames.Step();
        frames.Run(2f);

        Assert.That(jump.Action, Is.EqualTo(PlacementAction.Follow), "at once, so nothing jumps with the space");
        Assert.That(jump.Turn, Is.EqualTo(-20f).Within(1e-3f));
        Assert.That(frames.Actions(), Is.EqualTo(new[] { PlacementAction.Follow, PlacementAction.PlaceInFront }));
        Assert.That(frames.Decisions[1].Reason, Is.EqualTo("the person recentered: the tracking space moved, with no focus change"));
    }

    [Test]
    public void ASpaceThatMovesDuringAFocusChangeIsFollowedButNeverReplaced()
    {
        var frames = PlacementFrames.Placed();
        frames.Placement.FocusChanged(frames.Now);
        frames.Placement.OriginChanged(frames.Now);
        frames.Step();
        frames.Head += new Vector3(0.2f, 0f, 0.1f);
        frames.Yaw += 35f;
        frames.Step();
        frames.Run(2f);

        Assert.That(frames.Actions(), Is.EqualTo(new[] { PlacementAction.Follow, PlacementAction.Kept }));
        Assert.That(frames.Decisions[1].Reason, Is.EqualTo("the tracking space moved during a focus change, and the stage moved with it"));
    }

    [Test]
    public void AJumpJustBeforeItsAnnouncementIsFollowedWhenItArrives()
    {
        var frames = PlacementFrames.Placed();
        frames.Yaw -= 20f;
        frames.Step();
        Assert.That(frames.Decisions, Is.Empty, "a jump alone may be tracking correcting itself");

        frames.Step();
        frames.Placement.OriginChanged(frames.Now);
        var follow = frames.Step();
        Assert.That(follow.Action, Is.EqualTo(PlacementAction.Follow));
        Assert.That(follow.Turn, Is.EqualTo(-20f).Within(1e-3f));
    }

    [Test]
    public void AJumpNoHeadCanMakeWithoutAnyEventPlacesTheStageInFront()
    {
        var frames = PlacementFrames.Placed();
        frames.Yaw += 60f;
        frames.Step();
        frames.Run(1f);

        Assert.That(frames.Actions(), Is.EqualTo(new[] { PlacementAction.PlaceInFront }));
        Assert.That(frames.Decisions[0].Reason, Is.EqualTo("the head moved farther in one frame than a person can"));
    }

    [Test]
    public void NaturalHeadMovementIsNeverAJump()
    {
        var frames = PlacementFrames.Placed();
        frames.Placement.OriginChanged(frames.Now);
        // A quick look aside and back, at up to 250 degrees a second, and leaning in.
        frames.Run(0.3f, degreesPerSecond: 250f);
        frames.Run(0.3f, degreesPerSecond: -250f);
        for (var index = 0; index < 30; index++)
        {
            frames.Head += new Vector3(0f, 0f, 0.02f);
            frames.Step();
        }
        frames.Run(1f);

        Assert.That(frames.Actions(), Is.EqualTo(new[] { PlacementAction.Kept }));
    }

    [Test]
    public void ALongFrameIsNotJudged()
    {
        var frames = PlacementFrames.Placed();
        frames.Yaw += 80f;
        frames.Step(frame: 0.2f);
        frames.Run(1f);

        Assert.That(frames.Decisions, Is.Empty);
    }

    [Test]
    public void OnlyAResumeAfterARealPausePlacesTheStageAgain()
    {
        var frames = PlacementFrames.Placed();
        frames.Placement.Paused(false, frames.Now);
        frames.Run(1f);
        Assert.That(frames.Decisions, Is.Empty, "Unity reports resumes without a pause at start");

        frames.Placement.Paused(true, frames.Now);
        frames.Step(frame: 5f);
        frames.Placement.Paused(false, frames.Now);
        frames.Run(1f);
        Assert.That(frames.Actions(), Is.EqualTo(new[] { PlacementAction.PlaceInFront }));
        Assert.That(frames.Decisions[0].Reason, Is.EqualTo("the app resumed"));
    }

    [Test]
    public void FollowingKeepsTheStageWhereItWasAroundThePerson()
    {
        var frames = PlacementFrames.Placed();
        var beforeHead = frames.Head;
        var beforeYaw = frames.Yaw;
        var stage = new Vector3(0.4f, 0.75f, 2.1f);
        var aroundBefore = Around(stage, beforeHead, beforeYaw);

        frames.Placement.FocusChanged(frames.Now);
        frames.Placement.OriginChanged(frames.Now);
        frames.Head = new Vector3(-0.3f, 1.2f, 0.25f);
        frames.Yaw = -40f;
        var follow = frames.Step();

        Assert.That(follow.Action, Is.EqualTo(PlacementAction.Follow));
        var moved = follow.To + Turn(follow.Turn, stage - follow.From);
        var aroundAfter = Around(moved, frames.Head, frames.Yaw);
        Assert.That(Vector3.Distance(aroundBefore, aroundAfter), Is.LessThan(1e-4f), "the same place relative to the person");
    }

    /// <summary>A point in the frame of a head at a position and yaw.</summary>
    private static Vector3 Around(Vector3 point, Vector3 head, float yaw) => Turn(-yaw, point - head);

    /// <summary>Turns a vector about the vertical by degrees from +Z toward +X, as Unity's yaw does.</summary>
    private static Vector3 Turn(float degrees, Vector3 vector)
    {
        var radians = degrees * MathF.PI / 180f;
        return new Vector3(
            vector.X * MathF.Cos(radians) + vector.Z * MathF.Sin(radians),
            vector.Y,
            -vector.X * MathF.Sin(radians) + vector.Z * MathF.Cos(radians));
    }
}

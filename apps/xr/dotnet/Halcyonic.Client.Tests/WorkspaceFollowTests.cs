using System;
using System.Numerics;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class WorkspaceFollowTests
{
    private static Vector3 Forward(float degrees) => new((float)Math.Sin(degrees * Math.PI / 180), 0, (float)Math.Cos(degrees * Math.PI / 180));
    private static bool Rest(WorkspaceFollow follow, double from, double through, bool outside = true, bool blocked = false, bool animating = false, Vector3? position = null)
    {
        var requested = false;
        for (var now = from; now <= through + 0.00001; now += 0.05)
            requested |= follow.Update(now, position ?? Vector3.Zero, Vector3.UnitZ, outside, blocked, animating);
        return requested;
    }
    [Test]
    public void OrdinaryReadingNeverChasesSmallHeadMotion()
    {
        var follow = new WorkspaceFollow();
        for (var frame = 0; frame < 200; frame++)
            Assert.That(follow.Update(frame * 0.05, new Vector3((float)Math.Sin(frame * 0.1) * 0.03f, 0, 0), Forward((float)Math.Sin(frame * 0.1) * 6), false, false, false), Is.False);
        Assert.That(follow.Moving, Is.False);
    }
    [Test]
    public void OutsideReadingRequestsOneRecoveryAfterRestAndBlocksUntilAnimationAndSettlingEnd()
    {
        var follow = new WorkspaceFollow();
        Assert.That(Rest(follow, 0, 0.3), Is.False);
        Assert.That(Rest(follow, 0.35, 0.5), Is.True);
        Assert.That(follow.Moving, Is.True);
        Assert.That(Rest(follow, 0.55, 1.0, animating: true), Is.False);
        Rest(follow, 1.05, 1.3, outside: false);
        Assert.That(follow.Moving, Is.True);
        Rest(follow, 1.35, 1.5, outside: false);
        Assert.That(follow.Moving, Is.False);
        Assert.That(Rest(follow, 1.55, 3, outside: false), Is.False);
    }
    [Test]
    public void FastTurningAndWalkingRestartTheDwellUntilWearerRests()
    {
        var follow = new WorkspaceFollow();
        for (var frame = 0; frame <= 20; frame++)
            Assert.That(follow.Update(frame * 0.05, new Vector3(frame * 0.025f, 0, 0), Forward(frame * 4), true, false, false), Is.False);
        for (var frame = 21; frame <= 28; frame++)
            Assert.That(follow.Update(frame * 0.05, new Vector3(0.5f, 0, 0), Forward(80), true, false, false), Is.False);
        Assert.That(follow.Update(1.5, new Vector3(0.5f, 0, 0), Forward(80), true, false, false), Is.True);
    }
    [Test]
    public void FocusTrackingOrManualInteractionNeverAdmitsRecoveryAndRequiresFreshDwell()
    {
        var follow = new WorkspaceFollow();
        Assert.That(Rest(follow, 0, 2, blocked: true), Is.False);
        Assert.That(Rest(follow, 2.05, 2.35), Is.False);
        Assert.That(Rest(follow, 2.4, 2.6), Is.True);
    }
    [Test]
    public void ReturningInsideBeforeDwellKeepsTheReadingObjectStill()
    {
        var follow = new WorkspaceFollow();
        Rest(follow, 0, 0.25);
        Assert.That(Rest(follow, 0.3, 1, outside: false), Is.False);
        Assert.That(follow.Moving, Is.False);
    }
    [Test]
    public void InvalidPoseAndLongFrameCannotManufactureRest()
    {
        var follow = new WorkspaceFollow();
        Rest(follow, 0, 0.25);
        Assert.That(follow.Update(0.3, new Vector3(float.NaN, 0, 0), Vector3.UnitZ, true, false, false), Is.False);
        Assert.That(follow.Update(2, Vector3.Zero, Vector3.UnitZ, true, false, false), Is.False);
        Assert.That(Rest(follow, 2.05, 2.35), Is.False);
        Assert.That(Rest(follow, 2.4, 2.6), Is.True);
        follow.Reset(); Assert.That(follow.Moving, Is.False);
        Assert.That(follow.Update(3, Vector3.Zero, Vector3.UnitZ, true, false, false), Is.False);
    }
    [Test]
    public void ExtremePitchIsIneligibleAndNeverRepeatedlyMovesOrRevokesInput()
    {
        var follow = new WorkspaceFollow();
        var extreme = new Vector3(0, (float)Math.Sin(80 * Math.PI / 180), (float)Math.Cos(80 * Math.PI / 180));
        Assert.That(WorkspaceFollow.EligibleHead(extreme), Is.False);
        for (var frame = 0; frame < 200; frame++)
            Assert.That(follow.Update(frame * 0.05, Vector3.Zero, extreme, true, false, false,
                eligible: WorkspaceFollow.EligibleHead(extreme)), Is.False);
        Assert.That(follow.Moving, Is.False);
    }
    [Test]
    public void ModeratePitchRecoversAndAnExtremePoseDuringMotionCanSettleWithoutRetry()
    {
        var follow = new WorkspaceFollow();
        var moderate = new Vector3(0, (float)Math.Sin(20 * Math.PI / 180), (float)Math.Cos(20 * Math.PI / 180));
        Assert.That(WorkspaceFollow.EligibleHead(moderate), Is.True);
        var began = false;
        for (var frame = 0; frame <= 12; frame++)
            began |= follow.Update(frame * 0.05, Vector3.Zero, moderate, true, false, false, true);
        Assert.That(began, Is.True);
        var extreme = Vector3.UnitY;
        for (var frame = 13; frame <= 60; frame++)
            Assert.That(follow.Update(frame * 0.05, Vector3.Zero, extreme, true, false, frame < 20, false), Is.False);
        Assert.That(follow.Moving, Is.False, "unsupported pitch must not create a recurring settle/recovery loop");
    }

}

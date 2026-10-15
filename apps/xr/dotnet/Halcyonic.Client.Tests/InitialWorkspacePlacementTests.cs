using Halcyonic.Client;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class InitialWorkspacePlacementTests
{
    [Test]
    public void TrackedButUnwornNeverPlacesEvenAfterTimeout()
    {
        var placement = new InitialWorkspacePlacement();
        Assert.That(placement.Poll(0, false, true, true), Is.False);
        Assert.That(placement.Poll(60, false, true, true), Is.False);
        Assert.That(placement.Poll(61, true, true, true), Is.False);
        Assert.That(placement.Poll(61.31, true, true, true), Is.True);
    }

    [TestCase(false, true, true)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    public void LosingAnyReadinessRestartsTheFullWait(bool wearer, bool tracked, bool focused)
    {
        var placement = new InitialWorkspacePlacement();
        Assert.That(placement.Poll(0, true, true, true), Is.False);
        Assert.That(placement.Poll(0.2, wearer, tracked, focused), Is.False);
        Assert.That(placement.Poll(10, true, true, true), Is.False);
        Assert.That(placement.Poll(10.2, true, true, true), Is.False);
        Assert.That(placement.Poll(10.31, true, true, true), Is.True);
    }

    [Test]
    public void ValidLateTrackingEventuallyReleasesWithoutAHostStateChange()
    {
        var placement = new InitialWorkspacePlacement();
        Assert.That(placement.Poll(0, true, false, true), Is.False);
        Assert.That(placement.Poll(100, true, false, true), Is.False);
        Assert.That(placement.Poll(101, true, true, true), Is.False);
        Assert.That(placement.Poll(101.31, true, true, true), Is.True);
    }

    [Test]
    public void ClockResetDoesNotAdmitAnUnsettledPose()
    {
        var placement = new InitialWorkspacePlacement();
        Assert.That(placement.Poll(10, true, true, true), Is.False);
        Assert.That(placement.Poll(1, true, true, true), Is.False);
        Assert.That(placement.Poll(1.31, true, true, true), Is.True);
    }
}

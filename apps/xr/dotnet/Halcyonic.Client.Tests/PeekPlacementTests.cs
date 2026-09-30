using System.Collections.Generic;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class PeekPlacementTests
{
    [Test]
    public void ADeskNeighborBehindTheLabelMovesThePlateInFrontOfItsBody()
    {
        var bodies = new List<PeekObstacle>
        {
            new(0.09f, 0f, -0.03f, 0.055f),
        };
        var offset = PeekPlacement.FrontOffset(0.055f, 0.02f, 0.02f, 0.32f, 0.04f, bodies);
        Assert.That(offset, Is.GreaterThanOrEqualTo(0.105f));
    }

    [Test]
    public void ABodyOutsideThePlateDoesNotPullThePeekTowardTheEyes()
    {
        var bodies = new List<PeekObstacle>
        {
            new(-0.3f, 0f, -0.15f, 0.055f),
            new(0.1f, 0.3f, -0.15f, 0.055f),
        };
        var offset = PeekPlacement.FrontOffset(0.055f, 0.02f, 0.02f, 0.32f, 0.04f, bodies);
        Assert.That(offset, Is.EqualTo(0.055f + PeekPlacement.Clearance));
    }
}

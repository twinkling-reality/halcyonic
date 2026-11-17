using System.Numerics;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>A turn the shelf's place cannot hold steps it aside along its own length to the nearest place that does, never refused for that alone.</summary>
public class AgentShelfTurnTests
{
    private static AgentShelfDraft Draft(System.Func<AgentShelfPose, bool> allows)
    {
        var draft = new AgentShelfDraft();
        draft.Begin(new AgentShelfPose(new Vector3(0f, 0.7f, 1f), 0f), new Vector3(0f, 1.2f, 0f));
        draft.Allows = allows;
        return draft;
    }

    [Test]
    public void ATurnItsPlaceCannotHoldStepsAsideToTheNearestPlaceThatDoes()
    {
        // Turned, the shelf fits only 3 cm or more to the right of where it stood.
        var draft = Draft(pose => pose.Yaw == 0f || pose.Position.X >= 0.03f);
        Assert.That(draft.Move(Vector3.Zero, -5f), Is.True, "the turn is taken");
        var turned = draft.Candidate!.Value;
        Assert.That(turned.Yaw, Is.EqualTo(-5f), "turned by the press's 5 degrees");
        Assert.That(turned.Position.X, Is.GreaterThanOrEqualTo(0.03f).And.LessThan(0.05f), "stepped aside only as far as it must");
    }

    [Test]
    public void ATurnThatFitsWhereItStandsNeverMovesTheShelf()
    {
        var draft = Draft(_ => true);
        Assert.That(draft.Move(Vector3.Zero, 5f), Is.True);
        Assert.That(draft.Candidate!.Value.Position, Is.EqualTo(new Vector3(0f, 0.7f, 1f)));
    }

    [Test]
    public void ATurnNoNearPlaceHoldsIsRefusedAndTheShelfStays()
    {
        var draft = Draft(pose => pose.Yaw == 0f);
        Assert.That(draft.Move(Vector3.Zero, -5f), Is.False);
        Assert.That(draft.Candidate!.Value.Yaw, Is.EqualTo(0f));
    }

    [Test]
    public void AMoveWithoutATurnNeverStepsAside()
    {
        var draft = Draft(pose => pose.Position.Z <= 1f);
        Assert.That(draft.Move(new Vector3(0f, 0f, 0.05f)), Is.False, "a refused move stays refused");
        Assert.That(draft.Candidate!.Value.Position.Z, Is.EqualTo(1f));
    }
}

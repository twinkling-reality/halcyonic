using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// One frame object a draw (AUDIT's 2026-11-13T20:25Z and 20:33Z, the class behind Ask's room-20 and the no-place notice): no
/// column caches its frame; every one is BOUND BY THE HANDED FRAME, the navigator's single read per pass that both the plane's
/// binding and its draw use (XR's a70330950), so the object bound and the object drawn are one. This census lists every column
/// the client builds, so a new one cannot arrive without someone looking at how it is bound; the navigator-level test that the
/// bound object is the drawn object in one pass is XR's.
/// </summary>
public class FrameIdentityTests
{
    /// <summary>Every column, each bound by the frame the navigator or the placement hands for the pass.</summary>
    private static readonly IReadOnlyList<string> BoundByTheHandedFrame = new[]
    {
        nameof(AgentColumn), nameof(AgentShelfColumn), nameof(AppearanceColumn), nameof(AskMenuColumn), nameof(CharacterColourColumn),
        nameof(FileColumn), nameof(FirstQuestionColumn), nameof(NewProjectFlow), nameof(PairingColumn), nameof(PlacementColumn),
        nameof(ProjectsColumn), nameof(SettingsColumn), nameof(SettingsHubColumn), nameof(TaskResultsColumn), nameof(TasksColumn),
        nameof(UsageColumn), nameof(WorkspaceNoticeColumn), nameof(WorkspaceRecoveryColumn), nameof(WorkspaceRequestColumn),
    };

    [Test]
    public void EveryColumnTheClientBuildsIsKnownToTheBindingRule()
    {
        var columns = typeof(IMenuColumn).Assembly.GetTypes()
            .Where(type => typeof(IMenuColumn).IsAssignableFrom(type) && type.IsClass && !type.IsAbstract).Select(type => type.Name);
        Assert.That(columns, Is.EquivalentTo(BoundByTheHandedFrame),
            "a column the census does not know: add it here once its frame is read once a pass by the navigator or the placement, never again by the plane");
    }
}

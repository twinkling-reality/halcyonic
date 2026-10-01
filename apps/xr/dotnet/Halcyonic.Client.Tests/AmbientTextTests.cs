using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class AmbientTextTests
{
    [Test]
    public void CountsWhatNeedsThePersonInEveryProject()
    {
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(1, new[]
        {
            Samples.Workstream("waiting", WorkstreamStatus.WaitingForHuman, level: AttentionLevel.ActionRequired),
            Samples.Workstream("approval", WorkstreamStatus.Running, level: AttentionLevel.ActionRequired),
            Samples.Workstream("failed", WorkstreamStatus.Failed, level: AttentionLevel.Notice),
            Samples.Workstream("running", WorkstreamStatus.Running),
        }, new ExecutionView[0]), new StateChanges());
        Assert.That(AmbientText.NeedsYou(state), Is.EqualTo(2), "a failure is a notice, not a request");
        Assert.That(AmbientText.NeedsYouLine(2), Is.EqualTo("2 tasks are waiting for you"));
        Assert.That(AmbientText.NeedsYouLine(1), Is.EqualTo("1 task is waiting for you"));
        Assert.That(AmbientText.NeedsYouLine(0), Is.Null, "nothing to say when nothing waits");
    }
}

using System.Collections.Generic;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class WorkspacePresentationTests
{
    [Test]
    public void FullWorkspaceStartsWithoutAgentsAndSuspendsBeforeHiding()
    {
        var state = new WorkspacePresentationState();
        Assert.That((state.Minimized, state.AgentsEnabled, state.AgentsVisible), Is.EqualTo((false, false, false)));
        state.SetAgentsEnabled(true);
        var order = new List<string>();
        state.Suspending += () =>
        {
            Assert.That((state.Minimized, state.AgentsVisible), Is.EqualTo((false, true)));
            order.Add("suspended");
            Assert.That(state.Minimize(), Is.False, "reentrant callbacks cannot repeat the barrier");
        };
        state.Changed += () =>
        {
            Assert.That((state.Minimized, state.AgentsVisible), Is.EqualTo((true, false)));
            order.Add("hidden");
        };
        Assert.That(state.Minimize(), Is.True);
        Assert.That(state.Minimize(), Is.False);
        Assert.That(order, Is.EqualTo(new[] { "suspended", "hidden" }));
    }

    [Test]
    public void RestoreKeepsTheOptionalDisplayPreferenceWithoutRepeatingSuspension()
    {
        var state = new WorkspacePresentationState();
        var suspends = 0;
        state.Suspending += () => suspends++;
        state.SetAgentsEnabled(true);
        state.Minimize();
        Assert.That(state.Restore(), Is.True);
        Assert.That((state.Minimized, state.AgentsVisible, suspends), Is.EqualTo((false, true, 1)));
        Assert.That(state.Restore(), Is.False);
        state.SetAgentsEnabled(false);
        Assert.That(state.AgentsVisible, Is.False);
    }
}

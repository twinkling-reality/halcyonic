using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The lead's 2026-11-17T19:17Z limit #14: a project worked on by several agents shows each one on its screen, its task, its agent app and
/// its own state (the tag's word), each opening its own steps; one task's screen stays as before.
/// </summary>
public class KitScreenSwarmTests
{
    private static ClientProjection StateWith(params (WorkstreamView Work, ExecutionView? Execution)[] tasks)
    {
        var state = new ClientProjection();
        state.ApplyWelcome(Samples.Welcome(resumed: false, head: 1));
        state.ApplySnapshot(Samples.Snapshot(1, tasks.Select(each => each.Work), tasks.Where(each => each.Execution != null).Select(each => each.Execution!)),
            new StateChanges());
        return state;
    }

    [Test]
    public void ASwarmsScreenShowsEveryAgentWithItsOwnState()
    {
        var waiting = Samples.Execution("ex-1", "ws-1", ExecutionStatus.WaitingForHuman, synthetic: false);
        waiting.PendingApprovals.Add(new ApprovalView
        {
            ApprovalId = "ap-1", RequestedAt = Samples.Time, Approvable = true, Subject = new ToolUseSubject { ToolName = "bash", Summary = "npm test" },
        });
        var state = StateWith(
            (Samples.Workstream("ws-1", WorkstreamStatus.WaitingForHuman, "ex-1", AttentionLevel.ActionRequired,
                new ApprovalPendingReason { ExecutionId = "ex-1", ApprovalId = "ap-1" }), waiting),
            (Samples.Workstream("ws-2", WorkstreamStatus.Running, "ex-2"), Samples.Execution("ex-2", "ws-2", ExecutionStatus.Running)),
            (Samples.Workstream("ws-3", WorkstreamStatus.Completed, "ex-3"), Samples.Execution("ex-3", "ws-3", ExecutionStatus.Completed)));
        var project = state.Projects.Values.Single();
        var screen = WorkspacePresentationState.ScreenOf(project, state, barOpen: false);

        Assert.That(screen.Agents.Select(agent => agent.WorkstreamId), Is.EquivalentTo(new[] { "ws-1", "ws-2", "ws-3" }), "every agent of the project stands on its screen");
        Assert.That(screen.Agents.First().WorkstreamId, Is.EqualTo("ws-1"), "the one that needs you first, as on the shelf");
        Assert.That(screen.Agents.Single(agent => agent.WorkstreamId == "ws-1").NeedsYou, Is.True);
        foreach (var agent in screen.Agents)
        {
            var presented = CharacterPresenter.Present(state.Workstreams[agent.WorkstreamId], state, live: true);
            Assert.That(agent.State, Is.EqualTo(CharacterPresenter.TagWord(presented)), "the screen says the tag's own state: " + agent.WorkstreamId);
            Assert.That((agent.Task.Text, agent.Task.AllTheirs), Is.EqualTo(("Workstream " + agent.WorkstreamId, true)), "the task's name, theirs");
            Assert.That(agent.Agent, Is.Not.Null.And.Not.Empty, "a task with a run names its own run's app");
        }
        Assert.That(screen.Agents.Single(agent => agent.WorkstreamId == "ws-3").State, Is.EqualTo(RoomStanding.Finished));
    }

    /// <summary>AUDIT2's 19:23Z MUST: a task with no run names no app, never another runtime's name or "Agent".</summary>
    [Test]
    public void ATaskWithNoRunNamesNoApp()
    {
        var state = StateWith(
            (Samples.Workstream("ws-1", WorkstreamStatus.Running, "ex-1"), Samples.Execution("ex-1", "ws-1", ExecutionStatus.Running)),
            (Samples.Workstream("ws-2", WorkstreamStatus.Created), null));
        var screen = WorkspacePresentationState.ScreenOf(state.Projects.Values.Single(), state, barOpen: false);
        Assert.That(screen.Agents.Single(agent => agent.WorkstreamId == "ws-2").Agent, Is.Null);
        Assert.That(screen.Agents.Single(agent => agent.WorkstreamId == "ws-1").Agent, Is.Not.Null);
    }

    [Test]
    public void OneTasksScreenStaysAsBefore()
    {
        var state = StateWith((Samples.Workstream("ws-1", WorkstreamStatus.Running, "ex-1"), Samples.Execution("ex-1", "ws-1", ExecutionStatus.Running)));
        var screen = WorkspacePresentationState.ScreenOf(state.Projects.Values.Single(), state, barOpen: false);
        Assert.That(screen.Agents, Is.Empty);
    }
}

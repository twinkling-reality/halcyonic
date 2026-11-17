using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The lead's limits order #17: the project that needs you first, then working, then the rest, never hidden past a short list's end.</summary>
public class ProjectOrderTests
{
    private static ProjectView Project(string id, string name) => new() { ProjectId = id, Name = name, CreatedAt = Samples.Time, UpdatedAt = Samples.Time };

    private static WorkstreamView Work(string id, string project, WorkstreamStatus status, AttentionLevel attention = AttentionLevel.None) => new()
    {
        WorkstreamId = id, ProjectId = project, Title = id, Status = status, CreatedAt = Samples.Time, UpdatedAt = Samples.Time,
        Attention = new Attention { Level = attention },
    };

    [Test]
    public void TheProjectThatNeedsYouLeadsThenWorkingThenTheRestByName()
    {
        var projects = new[] { Project("a", "Aurora"), Project("b", "Beacon"), Project("c", "Comet"), Project("d", "Zephyr") };
        var work = new[]
        {
            Work("w1", "b", WorkstreamStatus.Running),
            Work("w2", "d", WorkstreamStatus.WaitingForHuman, AttentionLevel.ActionRequired),
            Work("w3", "c", WorkstreamStatus.Completed),
        };
        var order = ProjectOrder.Of(projects, work).Select(project => project.Name).ToList();
        Assert.That(order, Is.EqualTo(new[] { "Zephyr", "Beacon", "Aurora", "Comet" }), "needs you, then working, then the rest by name");
        Assert.That(order.Take(3), Has.Member("Zephyr"), "a project that needs you is inside the spine's first three, though it sorts last by name");
    }
}

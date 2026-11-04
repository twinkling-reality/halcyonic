using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>Reading tasks' histories into the activity when their files open (WorkspaceDirector.ReadHistory).</summary>
public class HistoryReadsTests
{
    [Test]
    public async Task ATasksHistoryIsKeptWhenOtherTasksReadsStartBeforeItArrives()
    {
        // The launch on 2026-11-04: the opened task's read went first, then the two restored panels' reads, and the
        // opened task's history arrived after they started. It read "Nothing Reported" where it had a report.
        var activity = new ActivityLog();
        var reads = new HistoryReads(activity);
        var opened = new TaskCompletionSource<IReadOnlyList<StoredEvent>>();
        var first = reads.ReadAsync("w-t00", () => opened.Task, () => true);
        var others = new[] { "t01", "t02" }
            .Select(task => reads.ReadAsync("w-" + task, () => Task.FromResult(History(task)), () => true))
            .ToList();
        await Task.WhenAll(others);

        opened.SetResult(History("t00"));
        Assert.That(await first, Is.Null);

        Assert.That(activity.For("t00").Select(entry => entry.Text), Is.EqualTo(new[] { "Round Started", "The build failed.", "Round Ended" }));
        Assert.That(activity.For("t01"), Has.Count.EqualTo(3));
        Assert.That(reads.NoteFor("w-t00"), Is.Empty, "read, so the file adds nothing to its time line");
    }

    [Test]
    public async Task OnlyATasksLatestReadCounts()
    {
        var activity = new ActivityLog();
        var reads = new HistoryReads(activity);
        var earlier = new TaskCompletionSource<IReadOnlyList<StoredEvent>>();
        var first = reads.ReadAsync("w-t00", () => earlier.Task, () => true);
        var again = reads.ReadAsync("w-t00", () => Task.FromException<IReadOnlyList<StoredEvent>>(new ControlPlaneRequestException("refused")), () => true);

        Assert.That(await again, Is.TypeOf<ControlPlaneRequestException>());
        earlier.SetResult(History("t00"));
        await first;

        Assert.That(activity.For("t00"), Is.Empty, "the earlier read was overtaken by the later one");
        Assert.That(reads.NoteFor("w-t00"), Is.EqualTo(HistoryReads.UnavailableNote));
    }

    private static long position;

    private static IReadOnlyList<StoredEvent> History(string executionId) => new[]
    {
        ActivityLogBoundTests.Journaled(++position, executionId, "runtime.turn.started", "{\"turn_id\":\"r0\"}"),
        ActivityLogBoundTests.Journaled(++position, executionId, "runtime.agent_message", "{\"text\":\"The build failed.\"}"),
        ActivityLogBoundTests.Journaled(++position, executionId, "runtime.turn.completed", "{\"turn_id\":\"r0\"}"),
    };
}

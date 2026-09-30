using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class ClientProjectionTests
{
    [Test]
    public void ASnapshotReplacesTheWholeState()
    {
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(3, new[] { Samples.Workstream("w1"), Samples.Workstream("w2") }), new StateChanges());

        var changes = new StateChanges();
        state.ApplySnapshot(Samples.Snapshot(9, new[] { Samples.Workstream("w3") }, journal: Samples.Journal(origin: JournalOrigin.Fixture)), changes);

        Assert.That(changes.Resynchronized, Is.True);
        Assert.That(state.Position, Is.EqualTo(9));
        Assert.That(state.Journal!.Origin, Is.EqualTo(JournalOrigin.Fixture));
        Assert.That(state.Workstreams.Keys, Is.EquivalentTo(new[] { "w3" }));
        Assert.That(state.Runtimes.Single().RuntimeId, Is.EqualTo("mock"));
    }

    [Test]
    public void ASnapshotBackAtAnEarlierPositionOfTheSameJournalRewinds()
    {
        var state = new ClientProjection();
        var first = new StateChanges();
        state.ApplySnapshot(Samples.Snapshot(12, new[] { Samples.Workstream("w1") }), first);
        Assert.That(first.Rewound, Is.False, "the first snapshot");

        var later = new StateChanges();
        state.ApplySnapshot(Samples.Snapshot(40, new[] { Samples.Workstream("w1") }), later);
        var same = new StateChanges();
        state.ApplySnapshot(Samples.Snapshot(40, new[] { Samples.Workstream("w1") }), same);
        Assert.That(later.Rewound || same.Rewound, Is.False, "forward, or where it already was");

        var rewound = new StateChanges();
        state.ApplySnapshot(Samples.Snapshot(12, new[] { Samples.Workstream("w1") }), rewound);
        Assert.That(rewound.Rewound && rewound.Resynchronized, Is.True);
        Assert.That(state.Position, Is.EqualTo(12));

        var another = new StateChanges();
        state.ApplySnapshot(Samples.Snapshot(3, journal: Samples.Journal("01a0dcf1-5a80-7000-8000-000000000002")), another);
        Assert.That(another.Rewound, Is.False, "another journal is a journal change, not a rewind");
    }

    [Test]
    public void AnEventReplacesTheEntitiesItChanged()
    {
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(3, new[] { Samples.Workstream("w1"), Samples.Workstream("w2") }), new StateChanges());

        var changes = new StateChanges();
        var applied = state.ApplyEvent(Samples.Event(5, Samples.Workstream("w1", WorkstreamStatus.Running)), changes);

        Assert.That(applied, Is.True);
        Assert.That(state.Position, Is.EqualTo(5));
        Assert.That(state.Workstreams["w1"].Status, Is.EqualTo(WorkstreamStatus.Running));
        Assert.That(state.Workstreams["w2"].Status, Is.EqualTo(WorkstreamStatus.Created));
        Assert.That(changes.Workstreams, Is.EquivalentTo(new[] { "w1" }));
        Assert.That(changes.Events, Has.Count.EqualTo(1));
        Assert.That(changes.Resynchronized, Is.False);
    }

    [Test]
    public void EventsAtOrBeforeTheCurrentPositionAreIgnored()
    {
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(5, new[] { Samples.Workstream("w1") }), new StateChanges());

        var changes = new StateChanges();
        Assert.That(state.ApplyEvent(Samples.Event(5, Samples.Workstream("w1", WorkstreamStatus.Failed)), changes), Is.False);
        Assert.That(state.ApplyEvent(Samples.Event(4, Samples.Workstream("w1", WorkstreamStatus.Failed)), changes), Is.False);

        Assert.That(state.Workstreams["w1"].Status, Is.EqualTo(WorkstreamStatus.Created));
        Assert.That(changes.IsEmpty, Is.True);
    }

    [Test]
    public void EventsBeforeAnySnapshotAreIgnored()
    {
        var state = new ClientProjection();
        Assert.That(state.ApplyEvent(Samples.Event(1, Samples.Workstream("w1")), new StateChanges()), Is.False);
        Assert.That(state.Workstreams, Is.Empty);
        Assert.That(state.Journal, Is.Null);
    }

    [Test]
    public void AWorkstreamLeadsToItsCurrentExecutionAndRuntime()
    {
        var state = new ClientProjection();
        var workstream = Samples.Workstream("w1", WorkstreamStatus.Running, "e1");
        state.ApplySnapshot(
            Samples.Snapshot(1, new[] { workstream, Samples.Workstream("w2") }, new[] { Samples.Execution("e1", "w1", ExecutionStatus.Running) }),
            new StateChanges());

        var execution = state.CurrentExecution(state.Workstreams["w1"]);
        Assert.That(execution!.ExecutionId, Is.EqualTo("e1"));
        Assert.That(state.RuntimeOf(execution)!.Capabilities.RespondToApproval, Is.True);
        Assert.That(state.CurrentExecution(state.Workstreams["w2"]), Is.Null);
    }
}

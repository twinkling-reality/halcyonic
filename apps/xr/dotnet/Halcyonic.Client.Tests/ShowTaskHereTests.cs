using System;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class ShowTaskHereTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void DemonstrationAndRetargetedFileCannotPlaceACharacter(bool demonstration)
    {
        const string first = "550e8400-e29b-41d4-a716-446655440001";
        const string second = "550e8400-e29b-41d4-a716-446655440002";
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(1, new[] { Samples.Workstream(first), Samples.Workstream(second) }), new StateChanges());
        var host = new FakeMenuHost { State = state, Demonstration = demonstration };
        var chosen = first; var opened = 0;
        var file = new FileColumn(host, () => WorkspacePresenter.Present(state.Workstreams[chosen], state, new ActivityLog(), true),
            new CommandFactory(Samples.Client), () => null, _ => null, () => "");
        file.OfferShowTaskHere(() => true, () => opened++);
        if (demonstration) Assert.That(file.Frame!.Lines.Any(line => line.Action == FileScreens.ShowTaskHere), Is.False);
        file.Drawn(file.Frame!, null);
        chosen = second;
        file.Act(FileScreens.ShowTaskHere, null);
        Assert.That(opened, Is.Zero);
        Assert.That(host.Sent, Is.Empty);
    }

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void OnlyCurrentDrawOfExactLiveTaskCanOpenPlacementAndItSendsNothing(TextSize size)
    {
        const string task = "550e8400-e29b-41d4-a716-446655440001";
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(1, new[] { Samples.Workstream(task) }), new StateChanges());
        var host = new FileMenuHost { Projection = state, TextSize = size };
        var file = new FileColumn(host, () => WorkspacePresenter.Present(state.Workstreams[task], state, new ActivityLog(), host.Live),
            new CommandFactory(Samples.Client), () => null, _ => null, () => "");
        var available = true; var opened = 0;
        Assert.That(file.Frame!.Lines.Any(line => line.Action == FileScreens.ShowTaskHere), Is.False, "unsupported hosts offer no placement action");
        file.OfferShowTaskHere(() => available, () => opened++);
        Assert.That(file.Frame!.Lines.Single(line => line.Action == FileScreens.ShowTaskHere).Words, Is.EqualTo("Show Task Here"));
        file.Act(FileScreens.ShowTaskHere, null); Assert.That(opened, Is.Zero);
        file.Drawn(file.Frame!, null); available = false;
        file.Act(FileScreens.ShowTaskHere, null); Assert.That(opened, Is.Zero, "support must still be available at press");
        available = true; file.Drawn(file.Frame!, null); host.Live = false;
        file.Act(FileScreens.ShowTaskHere, null); Assert.That(opened, Is.Zero, "disconnection expires authority");
        host.Live = true;
        file.Act(FileScreens.ShowTaskHere, null); Assert.That(opened, Is.Zero, "reconnect cannot revive an old draw");
        file.Drawn(file.Frame!, null);
        file.Act(FileScreens.ShowTaskHere, null); Assert.That(opened, Is.EqualTo(1));
        file.Act(FileScreens.ShowTaskHere, null); Assert.That(opened, Is.EqualTo(1), "one drawn press is consumed once");
        file.Drawn(file.Frame!, null); file.FocusLeft();
        file.Act(FileScreens.ShowTaskHere, null); Assert.That(opened, Is.EqualTo(1), "focus loss revokes the draw");
        file.Drawn(file.Frame!, null);
        state.ApplySnapshot(Samples.Snapshot(1, new[] { Samples.Workstream(task) }, journal: Samples.Journal("550e8400-e29b-41d4-a716-446655440099")), new StateChanges());
        file.Act(FileScreens.ShowTaskHere, null); Assert.That(opened, Is.EqualTo(1), "same task id in another journal cannot reuse the action");
        Assert.That(host.Sent, Is.Empty);
    }
}

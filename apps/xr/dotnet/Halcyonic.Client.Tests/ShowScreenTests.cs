using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// Show Screen on a task's Activity (ADR 0040: a picked project comes forward as its screen): offered only where the
/// director offers it and while its availability holds (the kit is on), while connected and while the task's project is
/// known; its press brings the task's project's screen forward and sends nothing.
/// </summary>
public class ShowScreenTests
{
    [Test]
    public void ShowScreenIsOfferedWhereTheDirectorOffersItAndSendsNothing()
    {
        const string task = "550e8400-e29b-41d4-a716-446655440001";
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(1, new[] { Samples.Workstream(task) }), new StateChanges());
        var host = new FileMenuHost { Projection = state };
        var file = new FileColumn(host, () => WorkspacePresenter.Present(state.Workstreams[task], state, new ActivityLog(), host.Live),
            new CommandFactory(Samples.Client), () => null, _ => null, () => "");
        Assert.That(file.Frame!.Lines.Any(line => line.Action == FileScreens.ShowScreen), Is.False, "no screen offered, no line");
        var shown = 0;
        var kitOn = true;
        file.OfferScreen(() => kitOn, () => shown++);
        file.Reflow();
        Assert.That(file.Frame!.Lines.Single(line => line.Action == FileScreens.ShowScreen).Words, Is.EqualTo(FileScreens.ShowScreenWords));
        file.Act(FileScreens.ShowScreen, null);
        Assert.That(shown, Is.EqualTo(1));
        Assert.That(host.Sent, Is.Empty, "showing a screen sends nothing");
        kitOn = false;
        file.Reflow();
        Assert.That(file.Frame!.Lines.Any(line => line.Action == FileScreens.ShowScreen), Is.False, "the kit off, the file's own views offer none");
        file.Act(FileScreens.ShowScreen, null);
        Assert.That(shown, Is.EqualTo(1));
        kitOn = true;
        host.Live = false;
        file.Reflow();
        Assert.That(file.Frame!.Lines.Any(line => line.Action == FileScreens.ShowScreen), Is.False, "disconnected, no screen to bring forward");
        file.Act(FileScreens.ShowScreen, null);
        Assert.That(shown, Is.EqualTo(1), "a press with no line offered does nothing");
    }

    [Test]
    public void ShowScreenIsNotOfferedWhileTheTasksProjectIsUnknown()
    {
        const string task = "550e8400-e29b-41d4-a716-446655440002";
        var snapshot = Samples.Snapshot(1, new[] { Samples.Workstream(task) });
        snapshot.Projects.Clear();
        var state = new ClientProjection();
        state.ApplySnapshot(snapshot, new StateChanges());
        var host = new FileMenuHost { Projection = state };
        var file = new FileColumn(host, () => WorkspacePresenter.Present(state.Workstreams[task], state, new ActivityLog(), host.Live),
            new CommandFactory(Samples.Client), () => null, _ => null, () => "");
        var shown = 0;
        file.OfferScreen(() => true, () => shown++);
        file.Reflow();
        Assert.That(file.Frame!.Lines.Any(line => line.Action == FileScreens.ShowScreen), Is.False, "no project to show, no line");
        file.Act(FileScreens.ShowScreen, null);
        Assert.That(shown, Is.EqualTo(0), "a press with no line offered does nothing");
    }
}

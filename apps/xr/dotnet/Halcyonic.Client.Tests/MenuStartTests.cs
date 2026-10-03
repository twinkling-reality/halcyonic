using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class MenuStartTests
{
    [Test]
    public void TheFirstVisitOpensProjectsOnceConnectedToTheComputerWithNothingOpen()
    {
        var visit = new FirstVisit(visited: false);
        Assert.That(visit.Due(live: false, demonstration: false, somethingOpen: false), Is.False, "not before the computer is connected");
        Assert.That(visit.Due(live: true, demonstration: true, somethingOpen: false), Is.False, "not in the demonstration");
        Assert.That(visit.Due(live: true, demonstration: false, somethingOpen: true), Is.False, "never over work already open");
        Assert.That(visit.Visited, Is.False, "none of those counts as the visit");
        Assert.That(visit.Due(live: true, demonstration: false, somethingOpen: false), Is.True);
        Assert.That(visit.Visited, Is.True);
        Assert.That(visit.Due(live: true, demonstration: false, somethingOpen: false), Is.False, "once only");
    }

    [Test]
    public void ADeviceThatWasWelcomedBeforeIsNotOpenedOnAgain()
    {
        Assert.That(new FirstVisit(visited: true).Due(live: true, demonstration: false, somethingOpen: false), Is.False);
    }

    private static ClientProjection State() => new Portfolio()
        .Project("a", "Alpha").Project("b", "Beta")
        .Work("w1", "a", WorkstreamStatus.WaitingForHuman)
        .Work("w2", "b", WorkstreamStatus.Running)
        .Apply();

    [Test]
    public void HidingAProjectWithdrawsTheWorkBroughtForwardOnlyWhenItIsThatProjects()
    {
        var state = State();
        var visibility = new StageVisibility();
        visibility.UseJournal(Samples.JournalId);
        var hidden = ProjectShowing.Apply(visibility, "a", shown: false, state, requested: "w1");
        Assert.That((hidden.Changed, hidden.WithdrawRequest, visibility.Shows("a"), visibility.Shows("b")), Is.EqualTo((true, true, false, true)));

        var other = ProjectShowing.Apply(visibility, "b", shown: false, state, requested: "w1");
        Assert.That((other.Changed, other.WithdrawRequest), Is.EqualTo((true, false)), "work of another project stays brought forward");
        Assert.That(ProjectShowing.Apply(visibility, "b", shown: false, state, requested: "gone").WithdrawRequest, Is.False, "nothing changes, nothing withdrawn");
    }

    [Test]
    public void ShowingAProjectChangesOnlyWhatIsNotAlreadyAsAskedAndWithdrawsNothing()
    {
        var state = State();
        var visibility = new StageVisibility();
        visibility.UseJournal(Samples.JournalId);
        var version = visibility.Version;
        var same = ProjectShowing.Apply(visibility, "a", shown: true, state, requested: "w1");
        Assert.That((same.Changed, same.WithdrawRequest, visibility.Version), Is.EqualTo((false, false, version)), "already shown");

        ProjectShowing.Apply(visibility, "a", shown: false, state, requested: null);
        var shown = ProjectShowing.Apply(visibility, "a", shown: true, state, requested: "w1");
        Assert.That((shown.Changed, shown.WithdrawRequest, visibility.Shows("a")), Is.EqualTo((true, false, true)));
        Assert.That(ProjectShowing.Apply(visibility, "c", shown: false, state, requested: "w2").WithdrawRequest, Is.False, "a request of no such project stays");
    }
}

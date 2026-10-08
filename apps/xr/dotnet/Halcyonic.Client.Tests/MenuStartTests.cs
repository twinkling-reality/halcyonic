using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class MenuStartTests
{
    private const string Journal = "0192f0c4-1a2b-7c3d-8e4f-5a6b7c8d9e0f";
    private const string Another = "0192f0c4-1a2b-7c3d-8e4f-5a6b7c8d9e10";

    [Test]
    public void TheFirstQuestionIsAskedOnlyOnceTheComputersStateIsKnownAndItHasHadNoTask()
    {
        var visit = new FirstVisit();
        Assert.That(visit.Asks(live: false, demonstration: false, Journal, anyTask: false), Is.Null, "not connected, or reconnecting on a stale state: nothing is decided");
        Assert.That(visit.Asks(live: true, demonstration: false, journalId: null, anyTask: false), Is.Null, "nothing is decided before the first snapshot");
        Assert.That(visit.Asks(live: true, demonstration: true, Journal, anyTask: false), Is.False, "the demonstration never asks: it opens ambient");
        Assert.That(visit.Asks(live: true, demonstration: false, Journal, anyTask: false), Is.True);
        Assert.That(visit.Started, Is.Empty, "asking keeps nothing");
    }

    [Test]
    public void AComputerThatHasHadATaskIsNeverAskedAgainOnThisHeadsetWhateverBecomesOfItsWork()
    {
        var visit = new FirstVisit();
        Assert.That(visit.Asks(live: true, demonstration: false, Journal, anyTask: true), Is.False);
        Assert.That(visit.Started, Is.EqualTo(Journal));
        Assert.That(visit.Asks(live: true, demonstration: false, Journal, anyTask: false), Is.False, "its work gone, it still had a task");
        Assert.That(visit.Asks(live: true, demonstration: false, Another, anyTask: false), Is.True, "another computer's journal is another first visit");

        var kept = new FirstVisit(visit.Started);
        Assert.That(kept.Asks(live: true, demonstration: false, Journal, anyTask: false), Is.False, "kept on the device");
        Assert.That(new FirstVisit("  ").Asks(live: true, demonstration: false, Journal, anyTask: false), Is.True, "nothing kept is no task");
    }

    [Test]
    public void TheDeviceRemembersTheMostRecentJournalsThatHadATask()
    {
        var visit = new FirstVisit();
        var journals = Enumerable.Range(0, FirstVisit.Journals + 1).Select(index => "journal-" + index).ToList();
        foreach (var journal in journals) visit.Asks(live: true, demonstration: false, journal, anyTask: true);
        Assert.That(visit.Started.Split(' '), Has.Length.EqualTo(FirstVisit.Journals));
        Assert.That(visit.Asks(live: true, demonstration: false, journals[0], anyTask: false), Is.True, "the oldest is forgotten");
        visit.Asks(live: true, demonstration: false, journals[1], anyTask: true);
        Assert.That(visit.Started.Split(' ')[0], Is.EqualTo(journals[1]), "seen again, it is the most recent");
    }

    [Test]
    public void TheQuestionOpensByItselfOnceAnAppStartWhenTheLiveStateIsFirstReady()
    {
        var visit = new FirstVisit();
        Assert.That(visit.Due(demonstration: false, somethingOpen: false, asks: null), Is.False, "not before the computer's state is known");
        Assert.That(visit.Due(demonstration: true, somethingOpen: false, asks: false), Is.False, "the demonstration opens closed");
        Assert.That(visit.Visited, Is.False, "neither counts as the visit");
        Assert.That(visit.Due(demonstration: false, somethingOpen: false, asks: true), Is.True);
        Assert.That(visit.Visited, Is.True);
        Assert.That(visit.Due(demonstration: false, somethingOpen: false, asks: true), Is.False, "a reconnect doesn't open it again");
        Assert.That(new FirstVisit().Due(demonstration: false, somethingOpen: false, asks: true), Is.True, "the next app start, still with no task, opens it again");
    }

    [Test]
    public void AFirstVisitWithWorkOrSomethingOpenCountsAndOpensNothing()
    {
        var withWork = new FirstVisit();
        Assert.That(withWork.Due(demonstration: false, somethingOpen: false, asks: false), Is.False, "it opens closed");
        Assert.That(withWork.Visited, Is.True);
        var busy = new FirstVisit();
        Assert.That(busy.Due(demonstration: false, somethingOpen: true, asks: true), Is.False, "never over work already open");
        Assert.That(busy.Due(demonstration: false, somethingOpen: false, asks: true), Is.False, "nor once it closes: the visit has come");
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

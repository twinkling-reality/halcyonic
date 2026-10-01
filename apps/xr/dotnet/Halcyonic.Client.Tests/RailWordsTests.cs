using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The rail says its counts in full where a chip has room, as a person would.</summary>
public class RailWordsTests
{
    private static ProjectSummary Project(bool shown, int work = 3, int active = 0, int needsYou = 0, int notice = 0) =>
        new("p1", "Storefront API", shown, work, active, needsYou, notice, 0);

    [Test]
    public void AChipInFullSaysItsWorkAsConnectProjectsDoes()
    {
        Assert.That(EntryText.ChipDetailInFull(Project(shown: true, needsYou: 1)), Is.EqualTo("1 task is waiting for you"));
        Assert.That(EntryText.ChipDetailInFull(Project(shown: true, active: 2)), Is.EqualTo("2 tasks running"));
        Assert.That(EntryText.ChipDetailInFull(Project(shown: true, needsYou: 1, notice: 1, active: 2)),
            Is.EqualTo("1 task waiting for you, 1 finished, 2 running"));
        Assert.That(EntryText.ChipDetailInFull(Project(shown: true, work: 0)), Is.EqualTo("no work yet"));
    }

    [Test]
    public void AHiddenProjectsChipSaysSoFirst()
    {
        Assert.That(EntryText.ChipDetailInFull(Project(shown: false, needsYou: 1)), Is.EqualTo("Hidden · 1 task is waiting for you"));
        Assert.That(EntryText.ChipDetail(Project(shown: false, needsYou: 1)), Is.EqualTo("Hidden · 1 waiting"), "the short form where it does not fit");
    }

    [Test]
    public void OtherTasksInFullSayWhatWaitsElseHowManyAreOffTheStage()
    {
        var state = new Portfolio()
            .Project("a", "Alpha")
            .Work("w1", "a", Halcyonic.Contracts.WorkstreamStatus.WaitingForHuman)
            .Work("w2", "a", Halcyonic.Contracts.WorkstreamStatus.Running)
            .Work("w3", "a", Halcyonic.Contracts.WorkstreamStatus.Running)
            .Apply();
        var visibility = new StageVisibility();
        visibility.UseJournal(Samples.JournalId);
        Assert.That(EntryText.MoreWorkDetailInFull(WorkOverview.Of(state, visibility, id => id == "w3")), Is.EqualTo("1 task is waiting for you"));
        Assert.That(EntryText.MoreWorkDetailInFull(WorkOverview.Of(state, visibility, id => id != "w2")), Is.EqualTo("1 task not on the stage"));
        Assert.That(EntryText.MoreWorkDetail(WorkOverview.Of(state, visibility, id => id != "w2")), Is.EqualTo("1 task"), "the short form where it does not fit");
    }

    [Test]
    public void TheButtonForOtherTasksBeginsWithAVerb()
    {
        Assert.That(EntryText.SeeOtherTasks, Is.EqualTo("See other tasks"));
        Assert.That(SettingsText.Settings, Is.EqualTo("Settings"));
    }
}

using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class AmbientTextTests
{
    [Test]
    public void CountsWhatNeedsThePersonInEveryProject()
    {
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(1, new[]
        {
            Samples.Workstream("waiting", WorkstreamStatus.WaitingForHuman, level: AttentionLevel.ActionRequired),
            Samples.Workstream("approval", WorkstreamStatus.Running, level: AttentionLevel.ActionRequired),
            Samples.Workstream("failed", WorkstreamStatus.Failed, level: AttentionLevel.Notice),
            Samples.Workstream("running", WorkstreamStatus.Running),
        }, new ExecutionView[0]), new StateChanges());
        Assert.That(AmbientText.NeedsYou(state), Is.EqualTo(2), "a failure is a notice, not a request");
        Assert.That(AmbientText.NeedsYouLine(2), Is.EqualTo("2 tasks need you"));
        Assert.That(AmbientText.NeedsYouLine(1), Is.EqualTo("1 task needs you"));
        Assert.That(AmbientText.NeedsYouLine(0), Is.Null, "nothing to say when nothing waits");
    }

    [Test]
    public void SaysHowMuchMoreWorkHasNoCharacter()
    {
        Assert.That(AmbientText.NotShown(1), Is.EqualTo("1 more task not shown here"));
        Assert.That(AmbientText.NotShown(3), Is.EqualTo("3 more tasks not shown here"), "never joined on a dot");
        Assert.That(AmbientText.NotShown(0), Is.Null, "nothing to say when every task stands on the stage");
    }

    [Test]
    public void SaysWhichPanelIsStillOpenItsNameWhole()
    {
        // Halcyonic's words upright and first, the panel's name leaning last (lane V's treatment); never a dot, never cut.
        Assert.That(AmbientText.StillOpen("New Project").ToString(), Is.EqualTo("Still Open: [New Project]"));
        var title = new string('T', 80);
        Assert.That(AmbientText.StillOpen(title).Text, Is.EqualTo("Still Open: " + title), "a long name stands whole: the banner wraps it");
        Assert.That(AmbientText.StillOpen(title).Text, Does.Not.Contain("…"));
        // Made plain first, so a character that would turn the line around never shows.
        var hidden = new string('T', 25) + "\u202E" + new string('T', 20);
        Assert.That(AmbientText.StillOpen(hidden).Text.Contains("\u202E", System.StringComparison.Ordinal), Is.False, "the override never reaches the screen raw");
        Assert.That(AmbientText.StillOpen(hidden).Text, Does.Contain("‹U+202E›"), "what cannot be seen is still read, by its code point");
        Assert.That(AmbientText.StillOpenName("New Project").Runs.All(run => run.Theirs), "the name apart, theirs");
    }

    [Test]
    public void TheDemonstrationsLinesAlwaysStandAboveTheStageAndALiveSessionsStepAsideForTheMenu()
    {
        Assert.That(BannerPlace.Of(demonstration: true, panelCovers: true, peekCovers: false), Is.EqualTo(BannerStand.AboveTheStage));
        Assert.That(BannerPlace.Of(demonstration: false, panelCovers: true, peekCovers: false), Is.EqualTo(BannerStand.Hidden),
            "a live session's banner never rises: the menu speaks for it");
        Assert.That(BannerPlace.Of(demonstration: true, panelCovers: false, peekCovers: false), Is.EqualTo(BannerStand.AboveTheStage), "raised over the closed bar too, which stands where it would hang");
        Assert.That(BannerPlace.Of(demonstration: false, panelCovers: false, peekCovers: false), Is.EqualTo(BannerStand.InPlace));
        foreach (var demonstration in new[] { true, false })
        {
            foreach (var panel in new[] { true, false })
            {
                Assert.That(BannerPlace.Of(demonstration, panel, peekCovers: true), Is.EqualTo(BannerStand.Hidden), "the peek always takes its place");
            }
        }
    }

    [Test]
    public void EveryArrangementSaysWhereTheCharactersStandAndThatTheWindowIsAssumed()
    {
        Assert.That(SettingsText.Arrangement(StageArrangement.InFront), Is.EqualTo("In Front of You"));
        Assert.That(SettingsText.Arrangement(StageArrangement.TurnedAside), Is.EqualTo("Right of Window"));
        Assert.That(SettingsText.Arrangement(StageArrangement.BesideAWindow), Is.EqualTo("Either Side of Window"));
        foreach (var arrangement in (StageArrangement[])System.Enum.GetValues(typeof(StageArrangement)))
        {
            Assert.That(SettingsText.Arrangement(arrangement).Length, Is.LessThanOrEqualTo(70), "one row of the Settings sheet");
        }
        Assert.That(SettingsText.ChangeTo(StageArrangement.InFront), Is.EqualTo("In Front"));
        Assert.That(SettingsText.ChangeTo(StageArrangement.TurnedAside), Is.EqualTo("Window Space"), "the turn aside keeps its words");
        Assert.That(SettingsText.ChangeTo(StageArrangement.BesideAWindow), Is.EqualTo("Window Sides"));
    }
}

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
        Assert.That(AmbientText.NeedsYouLine(2), Is.EqualTo("2 tasks are waiting for you"));
        Assert.That(AmbientText.NeedsYouLine(1), Is.EqualTo("1 task is waiting for you"));
        Assert.That(AmbientText.NeedsYouLine(0), Is.Null, "nothing to say when nothing waits");
    }

    [Test]
    public void SaysHowMuchMoreWorkHasNoCharacter()
    {
        Assert.That(AmbientText.NotShown(1), Is.EqualTo("1 more task not shown here"));
        Assert.That(AmbientText.NotShown(3), Is.EqualTo("3 more tasks not shown here"));
        Assert.That(AmbientText.NotShown(0), Is.Null, "nothing to say when every task stands on the stage");
    }

    [Test]
    public void SaysWhichPanelIsStillOpenItsNameCutShort()
    {
        Assert.That(AmbientText.StillOpen("Create a project"), Is.EqualTo("Still open: Create a project"));
        var title = new string('T', 80);
        Assert.That(AmbientText.StillOpen(title), Is.EqualTo("Still open: " + IntelligenceText.Truncate(title, AmbientText.StillOpenLimit)));
        Assert.That(AmbientText.StillOpen(title).Length, Is.LessThan(50));
        // Made plain before it is cut, so what it shows never runs past the limit or splits a code point.
        var hidden = new string('T', 25) + "\u202E" + new string('T', 20);
        Assert.That(AmbientText.StillOpen(hidden), Is.EqualTo("Still open: " + new string('T', 25) + "…"));
    }

    [Test]
    public void TheDemonstrationsLinesStandAboveTheStageWhileTheMenuIsOpenAndALiveSessionsStepAside()
    {
        Assert.That(BannerPlace.Of(demonstration: true, panelCovers: true, peekCovers: false), Is.EqualTo(BannerStand.AboveTheStage));
        Assert.That(BannerPlace.Of(demonstration: false, panelCovers: true, peekCovers: false), Is.EqualTo(BannerStand.Hidden),
            "a live session's banner never rises: the menu speaks for it");
        Assert.That(BannerPlace.Of(demonstration: true, panelCovers: false, peekCovers: false), Is.EqualTo(BannerStand.InPlace), "the closed bar leaves it be");
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
        Assert.That(SettingsText.Arrangement(StageArrangement.InFront), Is.EqualTo("The characters stand in front of you, where a window often opens."));
        Assert.That(SettingsText.Arrangement(StageArrangement.TurnedAside), Is.EqualTo("With a window straight ahead, the characters stand to its right."));
        Assert.That(SettingsText.Arrangement(StageArrangement.BesideAWindow), Is.EqualTo("With a window straight ahead, the characters stand either side of it."));
        foreach (var arrangement in (StageArrangement[])System.Enum.GetValues(typeof(StageArrangement)))
        {
            Assert.That(SettingsText.Arrangement(arrangement).Length, Is.LessThanOrEqualTo(70), "one row of the Settings sheet");
        }
        Assert.That(SettingsText.ChangeTo(StageArrangement.InFront), Is.EqualTo("Characters in front"));
        Assert.That(SettingsText.ChangeTo(StageArrangement.TurnedAside), Is.EqualTo("Make room for a window"), "the turn aside keeps its words");
        Assert.That(SettingsText.ChangeTo(StageArrangement.BesideAWindow), Is.EqualTo("Either side of a window"));
    }
}

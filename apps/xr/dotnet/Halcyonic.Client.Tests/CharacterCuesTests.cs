using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class CharacterCuesTests
{
    private static CharacterActivity[] Activities => (CharacterActivity[])Enum.GetValues(typeof(CharacterActivity));

    private static CharacterCues CuesOf(
        CharacterActivity activity,
        AttentionLevel attention = AttentionLevel.None,
        bool stale = false) =>
        CharacterCues.Of(new CharacterPresentation(
            "w1",
            "Fix the authentication regression",
            activity,
            CharacterPresenter.LabelOf(activity),
            attention,
            new List<string>(),
            0,
            synthetic: false,
            recorded: false,
            stale: stale));

    [Test]
    public void NoTwoActivitiesDifferOnlyInColor()
    {
        foreach (var a in Activities)
        {
            foreach (var b in Activities.Where(b => b != a))
            {
                var first = CuesOf(a);
                var second = CuesOf(b);
                Assert.That(
                    first.Eyes != second.Eyes || first.Motion != second.Motion,
                    Is.True,
                    $"{a} and {b} have the same eyes and motion");
            }
        }
    }

    [Test]
    public void WorkingHopsWithItsEyesOnTheTaskAndItsSurfaceFlowing()
    {
        var cues = CuesOf(CharacterActivity.Working);
        Assert.That(cues.Motion, Is.EqualTo(CharacterMotion.Hop));
        Assert.That(cues.Eyes, Is.EqualTo(CharacterEyes.OnTask));
        Assert.That(cues.Flowing, Is.True);
        Assert.That(cues.Ring, Is.False);
        Assert.That(cues.Halo, Is.EqualTo(CharacterHalo.None));
    }

    [Test]
    public void RunningTestsScansWhileARingSweepsAroundIt()
    {
        var cues = CuesOf(CharacterActivity.Verifying);
        Assert.That(cues.Eyes, Is.EqualTo(CharacterEyes.Scanning));
        Assert.That(cues.Ring, Is.True);
        Assert.That(cues.Flowing, Is.True);
    }

    [Test]
    public void NeedsYouTurnsToThePersonAndRisesInAWarmHalo()
    {
        var cues = CuesOf(CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired);
        Assert.That(cues.Eyes, Is.EqualTo(CharacterEyes.OnPerson));
        Assert.That(cues.Motion, Is.EqualTo(CharacterMotion.Rise));
        Assert.That(cues.FacesPerson, Is.True);
        Assert.That(cues.Halo, Is.EqualTo(CharacterHalo.NeedsYou));
    }

    [Test]
    public void AnyPendingDecisionGetsTheWarmHalo()
    {
        Assert.That(CuesOf(CharacterActivity.Unknown, AttentionLevel.ActionRequired).Halo, Is.EqualTo(CharacterHalo.NeedsYou));
    }

    [Test]
    public void AFinishedTurnSettlesWithClosedEyesAndNoCelebration()
    {
        var cues = CuesOf(CharacterActivity.TurnFinished);
        Assert.That(cues.Eyes, Is.EqualTo(CharacterEyes.Closed));
        Assert.That(cues.Motion, Is.EqualTo(CharacterMotion.Settle));
        Assert.That(cues.Flowing, Is.False);
        Assert.That(cues.Ring, Is.False);
        Assert.That(cues.FacesPerson, Is.False);
        Assert.That(cues.Halo, Is.EqualTo(CharacterHalo.Finished));
    }

    [Test]
    public void AFinishedTurnWithFailingTestsGlowsRed()
    {
        Assert.That(CuesOf(CharacterActivity.TurnFinished, AttentionLevel.Notice).Halo, Is.EqualTo(CharacterHalo.Failed));
    }

    [Test]
    public void FailedCracksAndSlumpsWithItsEyesCrossedOut()
    {
        var cues = CuesOf(CharacterActivity.Failed, AttentionLevel.Notice);
        Assert.That(cues.Eyes, Is.EqualTo(CharacterEyes.Crossed));
        Assert.That(cues.Motion, Is.EqualTo(CharacterMotion.Slump));
        Assert.That(cues.Cracked, Is.True);
        Assert.That(cues.Halo, Is.EqualTo(CharacterHalo.Failed));
    }

    [Test]
    public void UnknownIsFoggedOverWithUnfocusedEyes()
    {
        var cues = CuesOf(CharacterActivity.Unknown, AttentionLevel.Notice);
        Assert.That(cues.Eyes, Is.EqualTo(CharacterEyes.Unfocused));
        Assert.That(cues.Fogged, Is.True);
        Assert.That(cues.Halo, Is.EqualTo(CharacterHalo.Unknown));
    }

    [Test]
    public void StoppedIsFrozenMidMotionWithFlatEyes()
    {
        var cues = CuesOf(CharacterActivity.Interrupted);
        Assert.That(cues.Eyes, Is.EqualTo(CharacterEyes.Flat));
        Assert.That(cues.Motion, Is.EqualTo(CharacterMotion.Frozen));
        Assert.That(cues.Paused, Is.True);
        Assert.That(cues.Ghosted, Is.False, "stopped is current state, not stale");
    }

    [Test]
    public void TheLastKnownStateIsGhostedAndStillButKeepsItsCues()
    {
        foreach (var activity in Activities)
        {
            var live = CuesOf(activity, AttentionLevel.None);
            var stale = CuesOf(activity, AttentionLevel.None, stale: true);
            Assert.That(stale.Ghosted, Is.True, activity.ToString());
            Assert.That(stale.Paused, Is.True, activity.ToString());
            Assert.That(stale.Eyes, Is.EqualTo(live.Eyes), activity.ToString());
            Assert.That(stale.Motion, Is.EqualTo(live.Motion), activity.ToString());
            Assert.That(stale.Halo, Is.EqualTo(live.Halo), activity.ToString());
        }
    }

    [Test]
    public void OnlyCurrentStateMovesFreely()
    {
        foreach (var activity in Activities.Where(activity => activity != CharacterActivity.Interrupted))
        {
            var cues = CuesOf(activity);
            Assert.That(cues.Paused, Is.False, activity.ToString());
            Assert.That(cues.Ghosted, Is.False, activity.ToString());
        }
    }
}

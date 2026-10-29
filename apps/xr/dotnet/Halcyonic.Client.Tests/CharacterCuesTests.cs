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

    // The room's moments (ADR 0040, rule 4): the character answers the moment shown, never with an action of its own.
    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 10, 29, 10, 38, 0, TimeSpan.Zero);

    private static CharacterCues MomentCues(CharacterActivity activity, KitMoment? shown, bool stale = false) =>
        CharacterCues.Of(new CharacterPresentation("w1", "Make the towers wobble", activity, CharacterPresenter.LabelOf(activity),
            AttentionLevel.None, new List<string>(), 0, synthetic: false, recorded: false, stale: stale), shown);

    [Test]
    public void WithNoMomentTheCharacterIsAsItsStateSays()
    {
        foreach (var activity in Activities)
        {
            var plain = CuesOf(activity);
            var none = MomentCues(activity, null);
            Assert.That((none.Eyes, none.Motion, none.Halo, none.FacesPerson), Is.EqualTo((plain.Eyes, plain.Motion, plain.Halo, plain.FacesPerson)), activity.ToString());
        }
    }

    [Test]
    public void IdleBreathesAndGlancesNowAndThen()
    {
        var idle = MomentCues(CharacterActivity.Idle, null);
        Assert.That((idle.Motion, idle.Glances), Is.EqualTo((CharacterMotion.Breathe, true)));
        Assert.That(MomentCues(CharacterActivity.Working, null).Glances, Is.False, "a working character keeps its eyes on the task");
    }

    [Test]
    public void SentTurnsTheCharacterToThePersonAsIfListening()
    {
        var sent = MomentCues(CharacterActivity.Working, KitMoment.Sent("Codex"));
        Assert.That((sent.FacesPerson, sent.Eyes), Is.EqualTo((true, CharacterEyes.OnPerson)));
    }

    [Test]
    public void AQuestionWaitingTurnsItToThePersonWithTheWaitingLight()
    {
        var question = MomentCues(CharacterActivity.WaitingForHuman,
            KitMoment.Question("Codex", "npm install matter-js", KitSandbox.MayLeave, complete: true, At));
        Assert.That((question.FacesPerson, question.Eyes, question.Halo), Is.EqualTo((true, CharacterEyes.OnPerson, CharacterHalo.NeedsYou)));
    }

    [Test]
    public void AFailedCheckOfTheComputersOwnDroopsWithItsEyesOpen()
    {
        var failed = MomentCues(CharacterActivity.TurnFinished, KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: false, At));
        Assert.That((failed.Motion, failed.Eyes, failed.Cracked, failed.Halo), Is.EqualTo((CharacterMotion.Slump, CharacterEyes.Open, false, CharacterHalo.Failed)),
            "a failure is the task's state, not the character's end");
        var passed = MomentCues(CharacterActivity.TurnFinished, KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: true, At));
        Assert.That(passed.Motion, Is.Not.EqualTo(CharacterMotion.Slump), "only a failure droops");
    }

    [Test]
    public void ReadyHopsOnceThenFacesThePersonWithItsEyesOpen()
    {
        var check = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: true, At);
        var ready = MomentCues(CharacterActivity.TurnFinished, KitMoment.Ready("Tower Toss", TimeSpan.FromMinutes(4), check, tryRoute: true));
        Assert.That((ready.Motion, ready.FacesPerson, ready.Eyes, ready.Halo), Is.EqualTo((CharacterMotion.HopOnce, true, CharacterEyes.OnPerson, CharacterHalo.Finished)));
        Assert.That(ready.Eyes, Is.Not.EqualTo(CharacterEyes.Closed), "never a finished turn's closed eyes on a passed check");
    }

    [Test]
    public void AMomentNeverWakesAStaleOrStoppedCharacter()
    {
        var sent = KitMoment.Sent("Codex");
        var stale = MomentCues(CharacterActivity.Working, sent, stale: true);
        Assert.That((stale.Paused, stale.Ghosted, stale.FacesPerson), Is.EqualTo((true, true, false)), "the last known state stays as it was");
        var stopped = MomentCues(CharacterActivity.Interrupted, sent);
        Assert.That((stopped.Motion, stopped.Paused), Is.EqualTo((CharacterMotion.Frozen, true)));
    }

    [Test]
    public void OtherMomentsLeaveTheCharacterAsItsStateSays()
    {
        foreach (var moment in new[] { KitMoment.AgentSays("Codex", "I'll make the towers wobble.", At),
            KitMoment.HostCheck(KitCheckScope.Builds, ended: false, passed: null, At) })
        {
            var plain = CuesOf(CharacterActivity.Working);
            var cues = MomentCues(CharacterActivity.Working, moment);
            Assert.That((cues.Eyes, cues.Motion, cues.FacesPerson), Is.EqualTo((plain.Eyes, plain.Motion, plain.FacesPerson)), moment.Kind.ToString());
        }
    }

    [Test]
    public void ReadyAndAFailedCheckOnlyAnswerATurnThatHasStopped()
    {
        var check = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: true, At);
        var ready = KitMoment.Ready("Tower Toss", TimeSpan.FromMinutes(4), check, tryRoute: true);
        var failed = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: false, At);
        // The newest moment can be a Ready or a failed check while the runtime still works: the state wins then.
        foreach (var activity in new[] { CharacterActivity.Working, CharacterActivity.Starting, CharacterActivity.Verifying,
            CharacterActivity.WaitingForHuman, CharacterActivity.Unknown, CharacterActivity.Failed })
        foreach (var moment in new[] { ready, failed })
        {
            var plain = CuesOf(activity);
            var cues = MomentCues(activity, moment);
            Assert.That((cues.Eyes, cues.Motion, cues.Halo, cues.Cracked, cues.FacesPerson),
                Is.EqualTo((plain.Eyes, plain.Motion, plain.Halo, plain.Cracked, plain.FacesPerson)), activity + " with " + moment.Kind);
        }
        Assert.That(MomentCues(CharacterActivity.Idle, ready).Motion, Is.EqualTo(CharacterMotion.HopOnce), "a stopped turn answers it");
        Assert.That(MomentCues(CharacterActivity.Idle, failed).Motion, Is.EqualTo(CharacterMotion.Slump));
    }

    [Test]
    public void AnUnknownOrFailedCharacterKeepsItsOwnCuesWhateverTheMoment()
    {
        var moments = new[] { KitMoment.Sent("Codex"), KitMoment.Question("Codex", "npm install matter-js", KitSandbox.MayLeave, complete: true, At) };
        foreach (var activity in new[] { CharacterActivity.Unknown, CharacterActivity.Failed })
        foreach (var moment in moments)
        {
            var plain = CuesOf(activity);
            var cues = MomentCues(activity, moment);
            Assert.That((cues.Eyes, cues.Motion, cues.Halo, cues.Cracked, cues.FacesPerson),
                Is.EqualTo((plain.Eyes, plain.Motion, plain.Halo, plain.Cracked, plain.FacesPerson)), activity + " with " + moment.Kind);
        }
    }
}

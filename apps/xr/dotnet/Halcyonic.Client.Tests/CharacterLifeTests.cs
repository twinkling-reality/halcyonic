using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// A character's life (the art direction's character handoff, 2026-10-30, sections 2, 3 and 5; CHARACTER.md section 9),
/// independent of any figure's shape: floats, blinks, gaze, one-shots, the wander inside its home, arrival and departure,
/// and its truth rules as tests. The passed look only with the computer's own passed check observed for this round, its
/// happy eyes timed (AUDIT B1, B3); Last Known only when the control plane is lost, frozen, while an agent app lost is
/// Unknown and keeps moving (B2); nothing follows progress; no exit without its word first.
/// </summary>
public class CharacterLifeTests
{
    private const double Frame = 1.0 / 60;

    private static CharacterLife Life(WorkState state, string id = "ws_a", bool reachable = true)
    {
        var life = new CharacterLife(id, KitFigureFeature.Antenna);
        life.Show(state, passedCheckThisRound: false, controlPlaneReachable: reachable);
        return life;
    }

    private static List<CharacterPose> Run(CharacterLife life, double seconds)
    {
        var poses = new List<CharacterPose>();
        for (var t = 0.0; t < seconds - 1e-9; t += Frame) poses.Add(life.Step(Frame));
        return poses;
    }

    private static List<(T Value, double Seconds)> Runs<T>(IEnumerable<T> values)
    {
        var runs = new List<(T, double)>();
        foreach (var value in values)
        {
            if (runs.Count > 0 && EqualityComparer<T>.Default.Equals(runs[^1].Item1, value)) runs[^1] = (value, runs[^1].Item2 + Frame);
            else runs.Add((value, Frame));
        }
        return runs;
    }

    // B1: the passed look comes only from the computer's own check, observed, in this round.

    [Test]
    public void APassedCheckCountsOnlyWhenObservedInThisRound()
    {
        var round = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        var check = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: true, round.AddMinutes(3));
        var ready = KitMoment.Ready("Wind Gust", TimeSpan.FromMinutes(3), check, tryRoute: false);
        Assert.That(CharacterFacts.PassedCheckThisRound(ready, round), Is.True, "the computer's own passed check in this round");
        Assert.That(CharacterFacts.PassedCheckThisRound(ready, round.AddMinutes(4)), Is.False, "a check from before this round began");
        Assert.That(CharacterFacts.PassedCheckThisRound(ready, null), Is.False, "the round's start unknown: nothing claimed");
        Assert.That(CharacterFacts.PassedCheckThisRound(check, round), Is.False, "a check alone is not the Ready moment");
        Assert.That(CharacterFacts.PassedCheckThisRound(null, round), Is.False);
        var reported = KitMoment.ReportedEdit("Codex", KitFileChange.Changed, "src/towers.ts", round.AddMinutes(3));
        Assert.That(CharacterFacts.PassedCheckThisRound(reported, round), Is.False, "the agent's report never counts");
        var failed = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: false, round.AddMinutes(3));
        Assert.That(CharacterFacts.PassedCheckThisRound(failed, round), Is.False);
    }

    // B3: the happy eyes and the hop, once, timed, only on the passed check seen arriving.

    [Test]
    public void TheHappyEyesStandForOnePointTwoSecondsAndTheHopComesOnce()
    {
        var life = Life(WorkState.Working);
        Run(life, 1);
        life.TakeSounds();
        life.Show(WorkState.FinishedThisRound, passedCheckThisRound: true, controlPlaneReachable: true);
        var happy = new List<bool>();
        var lifts = new List<double>();
        for (var t = 0.0; t < 3; t += Frame)
        {
            life.Show(WorkState.FinishedThisRound, passedCheckThisRound: true, controlPlaneReachable: true);
            lifts.Add(life.Step(Frame).HopM);
            happy.Add(life.Look.Happy);
        }
        var happySeconds = happy.Count(h => h) * Frame;
        Assert.That(happySeconds, Is.EqualTo(KitFigureLook.HappyMs / 1000).Within(2 * Frame), "happy arcs for 1.2 s, then rest");
        Assert.That(happy.Skip(80), Has.All.False, "never happy after 1.2 s");
        Assert.That(life.Look.Eyes, Is.EqualTo(SpatialKit.Passed), "the passed eyes stay");
        Assert.That(lifts.Max(), Is.EqualTo(KitFigureLook.DpToM(10)).Within(KitFigureLook.DpToM(0.3)), "one hop, 10 dp");
        Assert.That(Runs(lifts.Select(l => l > 1e-6)).Count(r => r.Value), Is.EqualTo(1), "once, though shown every frame");
        Assert.That(life.TakeSounds(), Is.EqualTo(new[] { CharacterSound.Hop }));
    }

    [TestCase(WorkState.FinishedThisRound, false)]
    [TestCase(WorkState.CheckingItsWork, true)]
    [TestCase(WorkState.Working, true)]
    public void NoHopAndNoHappyEyesWithoutTheComputersPassedCheckInRoundEnded(WorkState state, bool passed)
    {
        var life = Life(WorkState.Working);
        Run(life, 1);
        life.TakeSounds();
        life.Show(state, passed, controlPlaneReachable: true);
        var poses = Run(life, 3);
        Assert.That(poses.Select(p => p.HopM), Has.All.EqualTo(0.0));
        Assert.That(life.Look.Happy, Is.False);
        Assert.That(life.Look.Eyes, Is.Not.EqualTo(SpatialKit.Passed));
        Assert.That(life.TakeSounds(), Does.Not.Contain(CharacterSound.Hop));
    }

    [Test]
    public void APassedCheckAlreadyThereWhenTheCharacterFirstShowsGivesTheColourButNoCelebration()
    {
        var life = new CharacterLife("ws_a", KitFigureFeature.Fins);
        life.Show(WorkState.FinishedThisRound, passedCheckThisRound: true, controlPlaneReachable: true);
        var poses = Run(life, 2);
        Assert.That(life.Look.Eyes, Is.EqualTo(SpatialKit.Passed));
        Assert.That(life.Look.Happy, Is.False, "a reconnect is not news");
        Assert.That(poses.Select(p => p.HopM), Has.All.EqualTo(0.0));
    }

    [Test]
    public void APassedCheckThatLandedWhileTheHeadsetWasAwayGivesTheColourButNoCelebration()
    {
        var life = Life(WorkState.FinishedThisRound);
        Run(life, 1);
        life.Show(WorkState.FinishedThisRound, passedCheckThisRound: false, controlPlaneReachable: false);
        Run(life, 1);
        life.TakeSounds();
        var lifts = new List<double>();
        var happy = new List<bool>();
        for (var t = 0.0; t < 3; t += Frame)
        {
            life.Show(WorkState.FinishedThisRound, passedCheckThisRound: true, controlPlaneReachable: true);
            lifts.Add(life.Step(Frame).HopM);
            happy.Add(life.Look.Happy);
        }
        Assert.That(lifts, Has.All.EqualTo(0.0), "no hop on reconnect (AUDIT M1, B3)");
        Assert.That(happy, Has.All.False, "no happy eyes on reconnect");
        Assert.That(life.TakeSounds(), Does.Not.Contain(CharacterSound.Hop));
        Assert.That(life.Look.Eyes, Is.EqualTo(SpatialKit.Passed), "the passed colour still shows");
    }

    // B2: Last Known only when the control plane is lost; an agent app lost is Unknown and still moves.

    [Test]
    public void TheControlPlaneLostFreezesTheLastPoseDimmed()
    {
        var life = Life(WorkState.Working);
        Run(life, 30);
        life.TakeSounds();
        var before = life.Look;
        life.Show(WorkState.Working, false, controlPlaneReachable: false);
        var first = life.Step(Frame);
        var poses = Run(life, 2);
        Assert.That(poses, Has.All.EqualTo(first), "no float, blink, glance or wander over 2 s");
        Assert.That(first.Lids, Is.EqualTo(0.0), "no blink held");
        Assert.That(life.Look, Is.EqualTo(KitFigureLook.Unreachable(before)));
        Assert.That(life.Look.Moves, Is.False);
        Assert.That(life.TakeSounds(), Is.Empty);
    }

    [Test]
    public void FirstShownWhileTheControlPlaneIsLostItStandsStillAtRest()
    {
        var life = new CharacterLife("ws_a", KitFigureFeature.Crest);
        life.Show(WorkState.Working, false, controlPlaneReachable: false);
        var first = life.Step(Frame);
        Assert.That((first.Scale, first.Squash, first.Lids, first.FloatYM), Is.EqualTo((1.0, 1.0, 0.0, 0.0)), "present, not folded, not moving");
        Assert.That(Run(life, 2), Has.All.EqualTo(first));
        Assert.That(life.Look.Moves, Is.False);
    }

    [Test]
    public void AnAgentAppLostIsUnknownAndKeepsItsCalmMotion()
    {
        var life = Life(WorkState.CantTellYet);
        var poses = Run(life, 4);
        Assert.That(life.Look, Is.EqualTo(KitFigureLook.Of(WorkState.CantTellYet, false)));
        Assert.That(poses.Select(p => p.FloatYM).Distinct().Count(), Is.GreaterThan(100), "it still floats");
        Assert.That(poses.Max(p => p.HeadRollDegrees), Is.GreaterThan(10), "the head roll once, about 10 degrees over its held 4");
        Assert.That(poses[^1].HeadRollDegrees, Is.EqualTo(4).Within(0.5), "then 4 degrees held");
    }

    [Test]
    public void ReachingTheControlPlaneAgainBringsTheLifeBack()
    {
        var life = Life(WorkState.Working);
        Run(life, 5);
        life.Show(WorkState.Working, false, controlPlaneReachable: false);
        Run(life, 2);
        life.Show(WorkState.Working, false, controlPlaneReachable: true);
        var poses = Run(life, 2);
        Assert.That(life.Look, Is.EqualTo(KitFigureLook.Of(WorkState.Working, false)));
        Assert.That(poses.Select(p => p.FloatYM).Distinct().Count(), Is.GreaterThan(50));
    }

    // Nothing follows progress: Running is one look for its whole run.

    [Test]
    public void RunningMovesTheSameAtMinuteOneAndMinuteForty()
    {
        var life = Life(WorkState.Working);
        var early = Run(life, 120);
        Run(life, 37 * 60);
        var late = Run(life, 120);
        Assert.That(late.Max(p => Math.Abs(p.FloatYM)), Is.EqualTo(early.Max(p => Math.Abs(p.FloatYM))).Within(KitFigureLook.DpToM(0.1)), "the float");
        Assert.That(late.Max(p => Math.Abs(p.RollDegrees)), Is.EqualTo(early.Max(p => Math.Abs(p.RollDegrees))).Within(0.1), "the roll");
        Assert.That(late.Select(p => p.Squash).Distinct(), Is.EqualTo(new[] { 1.0 }), "no squash without a hop");
        var earlyShots = Runs(early.Select(p => p.OneShot)).Count(r => r.Value != CharacterOneShot.None);
        var lateShots = Runs(late.Select(p => p.OneShot)).Count(r => r.Value != CharacterOneShot.None);
        Assert.That(lateShots, Is.InRange(earlyShots - 8, earlyShots + 8), "one-shots at the same rhythm");
    }

    // Floats, own to each character.

    [Test]
    public void TwoCharactersNeverFloatInUnison()
    {
        var a = new CharacterLife("ws_a", KitFigureFeature.Antenna);
        var b = new CharacterLife("ws_b", KitFigureFeature.Fins);
        foreach (var life in new[] { a, b })
        {
            var (vertical, sideways, roll) = life.FloatPeriods;
            Assert.That(vertical, Is.InRange(3.2 * 0.85, 3.2 * 1.15));
            Assert.That(sideways, Is.InRange(5.3 * 0.85, 5.3 * 1.15));
            Assert.That(roll, Is.InRange(7.1 * 0.85, 7.1 * 1.15));
        }
        Assert.That(a.FloatPeriods, Is.Not.EqualTo(b.FloatPeriods));
        a.Show(WorkState.Working, false, true);
        b.Show(WorkState.Working, false, true);
        var ya = Run(a, 60).Select(p => p.FloatYM).ToArray();
        var yb = Run(b, 60).Select(p => p.FloatYM).ToArray();
        Assert.That(Correlation(ya, yb), Is.LessThan(0.9));
        Assert.That(ya.Max(), Is.EqualTo(KitFigureLook.DpToM(2.5)).Within(KitFigureLook.DpToM(0.1)), "2.5 dp");
        Assert.That(new CharacterLife("ws_a", KitFigureFeature.Antenna).FloatPeriods, Is.EqualTo(a.FloatPeriods), "the same task floats the same each time");
    }

    [Test]
    public void TheHeadLagsTheBodyAcrossTheGap()
    {
        var poses = Run(Life(WorkState.Working), 10);
        Assert.That(poses.Max(p => Math.Abs(p.HeadLagM)), Is.GreaterThan(0));
        Assert.That(poses.Max(p => Math.Abs(p.HeadLagM)), Is.LessThan(KitFigureLook.DpToM(2.5) * 0.3));
    }

    [TestCase(WorkState.NotStarted, 0.6)]
    [TestCase(WorkState.FinishedThisRound, 0.6)]
    [TestCase(WorkState.ChecksFailed, 0.5)]
    [TestCase(WorkState.Working, 1.0)]
    public void QuietStatesFloatLess(WorkState state, double share)
    {
        var poses = Run(Life(state), 30);
        Assert.That(poses.Max(p => p.FloatYM), Is.EqualTo(KitFigureLook.DpToM(2.5) * share).Within(KitFigureLook.DpToM(0.15)));
    }

    // Blinks.

    [Test]
    public void BlinksComeAboutEveryOtherTwoPointSixSecondsAndLast180Ms()
    {
        var life = Life(WorkState.Working);
        var lids = Run(life, 520).Select(p => p.Lids).ToArray();
        var closes = Runs(lids.Select(l => l > 0.02)).Where(r => r.Value).ToList();
        Assert.That(closes.Count, Is.InRange(70, 150), "200 chances at one half, one in eight doubled");
        Assert.That(closes.Select(c => c.Seconds), Has.All.LessThanOrEqualTo(0.36 + 0.05), "180 ms, or two together");
        Assert.That(lids.Max(), Is.EqualTo(1).Within(0.05), "closing fully");
    }

    // Gaze.

    [Test]
    public void TheHeadTurnsAtMost35DegreesAndTheEyesTakeTheRest()
    {
        var (headYaw, headPitch, eyeX, eyeY) = CharacterLife.Aim(80, 80);
        Assert.That((headYaw, headPitch), Is.EqualTo((35.0, 35.0)));
        Assert.That(eyeX, Is.EqualTo(CharacterLife.EyeReachH));
        Assert.That(eyeY, Is.EqualTo(CharacterLife.EyeLiftH));
        var small = CharacterLife.Aim(-10, 0);
        Assert.That(small.HeadYawDegrees, Is.EqualTo(-10));
        Assert.That(small.EyeXH, Is.LessThan(0).And.GreaterThan(-CharacterLife.EyeReachH));
        Assert.That(CharacterLife.EyeReachH, Is.EqualTo(0.045));
    }

    [Test]
    public void ItHoldsYourGazeThenLooksAwayOnPurpose()
    {
        var gaze = Runs(Run(Life(WorkState.WaitingForYou), 240).Select(p => p.Gaze)).Skip(1).SkipLast(1).ToList();
        Assert.That(gaze.Select(g => g.Value).Distinct(), Is.EquivalentTo(new[] { CharacterGaze.Person, CharacterGaze.Away }));
        Assert.That(gaze.Where(g => g.Value == CharacterGaze.Person).Select(g => g.Seconds), Has.All.InRange(2.2 - Frame, 3.8 + 2 * Frame));
        Assert.That(gaze.Where(g => g.Value == CharacterGaze.Away).Select(g => g.Seconds), Has.All.InRange(0.8 - Frame, 1.6 + 2 * Frame));
    }

    [Test]
    public void RunningLooksAtItsScreenMostOfTheTime()
    {
        var poses = Run(Life(WorkState.Working), 900);
        var share = poses.Count(p => p.Gaze == CharacterGaze.Screen) / (double)poses.Count;
        Assert.That(share, Is.InRange(0.5, 0.8));
        Assert.That(poses.Any(p => p.Gaze == CharacterGaze.Person), Is.True);
        Assert.That(poses.Where(p => p.Gaze == CharacterGaze.Screen).Max(p => Math.Abs(p.EyeDartH)), Is.GreaterThan(0.007), "reading darts");
    }

    [TestCase(WorkState.NotStarted)]
    [TestCase(WorkState.FinishedThisRound)]
    public void QuietStatesLookAtYouAndAtTheirScreenInTurn(WorkState state)
    {
        var gaze = Run(Life(state), 120).Select(p => p.Gaze).ToList();
        Assert.That(gaze, Does.Contain(CharacterGaze.Person).And.Contain(CharacterGaze.Screen));
    }

    [TestCase(WorkState.ChecksFailed)]
    [TestCase(WorkState.CouldNotFinish)]
    [TestCase(WorkState.Stopped)]
    public void AfterAFailureOrAStopItLooksAtYourLap(WorkState state)
    {
        Assert.That(Run(Life(state), 60).Select(p => p.Gaze), Has.All.EqualTo(CharacterGaze.Lap));
    }

    // One-shots.

    [Test]
    public void OneShotsComeOneAtATimeEveryFourToNineSeconds()
    {
        var life = Life(WorkState.Working);
        life.HasNeighbour = true;
        var poses = Run(life, 600);
        var shots = new List<(CharacterOneShot Kind, double At, double Seconds)>();
        var t = 0.0;
        foreach (var run in Runs(poses.Select(p => p.OneShot)))
        {
            if (run.Value != CharacterOneShot.None) shots.Add((run.Value, t, run.Seconds));
            t += run.Seconds;
        }
        Assert.That(shots.Count, Is.InRange(600 / 9 - 10, 600 / 4));
        Assert.That(shots.Select(s => s.Kind).Distinct().Count(), Is.EqualTo(6), "all six kinds");
        for (var i = 1; i < shots.Count; i++) Assert.That(shots[i].At - shots[i - 1].At, Is.InRange(4 - Frame, 9 + Frame), "start to start");
        Assert.That(shots.Where(s => s.Kind != CharacterOneShot.EighthTurn).Select(s => s.Seconds), Has.All.LessThan(1.0));
        Assert.That(shots.Where(s => s.Kind == CharacterOneShot.EighthTurn).Select(s => s.Seconds), Has.All.EqualTo(1.6).Within(2 * Frame));
        Assert.That(poses.Max(p => Math.Abs(p.TurnDegrees)), Is.EqualTo(45).Within(0.5), "the eighth turn");
    }

    [Test]
    public void WithoutANeighbourItNeverGlancesAtOne()
    {
        var poses = Run(Life(WorkState.Working), 300);
        Assert.That(poses.Select(p => p.OneShot), Does.Not.Contain(CharacterOneShot.GlanceAtNeighbour));
        Assert.That(poses.Select(p => p.Gaze), Does.Not.Contain(CharacterGaze.Neighbour));
    }

    [TestCase(WorkState.NotStarted)]
    [TestCase(WorkState.WaitingForYou)]
    [TestCase(WorkState.ChecksFailed)]
    [TestCase(WorkState.Stopped)]
    [TestCase(WorkState.CantTellYet)]
    public void OnlyRunningCheckingAndRoundEndedHaveOneShots(WorkState state)
    {
        Assert.That(Run(Life(state), 120).Select(p => p.OneShot), Has.All.EqualTo(CharacterOneShot.None));
    }

    // Wander.

    [Test]
    public void RunningWandersInsideItsHome()
    {
        var poses = Run(Life(WorkState.Working), 180);
        Assert.That(poses.Select(p => (p.XM / 0.18) * (p.XM / 0.18) + (p.ZM / 0.08) * (p.ZM / 0.08)), Has.All.LessThanOrEqualTo(1 + 1e-9));
        Assert.That(poses.Select(p => (Math.Round(p.XM, 3), Math.Round(p.ZM, 3))).Distinct().Count(), Is.GreaterThan(30), "it moves");
        Assert.That(CharacterLife.MoveSeconds(0.30), Is.EqualTo(1.5 * 0.30 / 0.12).Within(1e-9));
        Assert.That(CharacterLife.MoveSeconds(0.01), Is.EqualTo(0.6));
    }

    [Test]
    public void AWanderSkipsSpotsThatWouldCrowdANeighbour()
    {
        var life = Life(WorkState.Working);
        life.SpotAllowed = (x, z) => x <= 0;
        var poses = Run(life, 240);
        Assert.That(poses.Select(p => p.XM), Has.All.LessThanOrEqualTo(1e-9));
        Assert.That(poses.Min(p => p.XM), Is.LessThan(-0.02));
        Assert.That(CharacterLife.ApartEnough((0, 1.2, 0), (-0.2, 0.9, -1.9), (0.2, 0.9, -1.9)), Is.False, "12 degrees");
        Assert.That(CharacterLife.ApartEnough((0, 1.2, 0), (-0.25, 0.9, -1.9), (0.25, 0.9, -1.9)), Is.True, "15 degrees");
    }

    [TestCase(WorkState.NotStarted)]
    [TestCase(WorkState.CheckingItsWork)]
    [TestCase(WorkState.WaitingForYou)]
    [TestCase(WorkState.FinishedThisRound)]
    public void OnlyRunningWanders(WorkState state)
    {
        var poses = Run(Life(state), 120);
        Assert.That(poses.Select(p => (p.XM, p.ZM)), Has.All.EqualTo((0.0, 0.0)));
    }

    [Test]
    public void LeavingRunningGoesBackHome()
    {
        var life = Life(WorkState.Working);
        Run(life, 60);
        life.Show(WorkState.WaitingForYou, false, true);
        var last = Run(life, 10)[^1];
        Assert.That((last.XM, last.ZM), Is.EqualTo((0.0, 0.0)));
    }

    // Keep Still.

    [Test]
    public void KeepStillStopsFloatsBlinksWanderOneShotsAndHops()
    {
        var life = Life(WorkState.Working);
        life.KeepStill = true;
        Run(life, 1);
        var poses = Run(life, 60);
        Assert.That(poses.Select(p => p.FloatYM).Max(Math.Abs), Is.LessThan(1e-6));
        Assert.That(poses.Select(p => (p.XM, p.ZM)).Distinct().Count(), Is.EqualTo(1));
        Assert.That(poses.Select(p => p.OneShot), Has.All.EqualTo(CharacterOneShot.None));
        Assert.That(poses.Select(p => p.Lids), Has.All.EqualTo(0.0), "no blinks (ADR 0027 item 6)");
        life.Show(WorkState.FinishedThisRound, passedCheckThisRound: true, controlPlaneReachable: true);
        Assert.That(Run(life, 2).Select(p => p.HopM), Has.All.EqualTo(0.0));
        Assert.That(life.TakeSounds(), Does.Not.Contain(CharacterSound.Hop));
    }

    // Arrival and departure: always deliberate, always in words.

    [Test]
    public void ArrivalGrowsFromASeedWithASpringAndOpensTheEyesLast()
    {
        var life = Life(WorkState.Starting);
        life.Arrive();
        var poses = Run(life, 1);
        Assert.That(poses[0].Scale, Is.LessThan(0.05));
        Assert.That(poses.Max(p => p.Scale), Is.GreaterThan(1), "the spring overshoots");
        Assert.That(poses[^1].Scale, Is.EqualTo(1));
        Assert.That(poses.Take(25).Select(p => p.Lids), Has.All.EqualTo(1.0), "the eyes shut while it grows");
        Assert.That(poses[45].Lids, Is.LessThan(0.05), "open by 0.6 s");
        Assert.That(life.TakeSounds(), Is.EqualTo(new[] { CharacterSound.Arrive }));
    }

    [Test]
    public void ACharacterAlreadyThereNeitherArrivesNorChimes()
    {
        var life = Life(WorkState.Working);
        Assert.That(life.Step(Frame).Scale, Is.EqualTo(1));
        Assert.That(life.TakeSounds(), Is.Empty);
    }

    [Test]
    public void PutAwayShowsItsWordFirstThenFoldsThenTheTagFades()
    {
        var life = Life(WorkState.FinishedThisRound);
        Run(life, 1);
        life.PutAway();
        var poses = Run(life, 2.5);
        Assert.That(life.TagWord, Is.Null.Or.EqualTo(CharacterLife.PutAwayWord));
        var shown = poses.Take(72).ToList();
        Assert.That(shown.Select(p => p.Scale), Has.All.EqualTo(1.0), "1.2 s with the word before anything leaves");
        Assert.That(shown.Select(p => p.Gaze), Has.All.EqualTo(CharacterGaze.Person), "turned to you");
        Assert.That(shown.Max(p => p.NodDegrees), Is.GreaterThan(10), "a nod");
        Assert.That(poses[96].Scale, Is.EqualTo(0).Within(1e-9), "folded by 1.6 s");
        Assert.That(poses[90].LiftM, Is.GreaterThan(KitFigureLook.DpToM(10)), "rising as it folds");
        Assert.That(life.Gone, Is.True);
        Assert.That(life.TagOpacity, Is.EqualTo(0));
        Assert.That(life.TakeSounds(), Is.EqualTo(new[] { CharacterSound.Fold }));
    }

    [Test]
    public void TheWordShowsWhileItIsBeingPutAway()
    {
        var life = Life(WorkState.FinishedThisRound);
        life.PutAway();
        Run(life, 1.0);
        Assert.That(life.TagWord, Is.EqualTo("Put Away"));
        Assert.That(life.TagOpacity, Is.EqualTo(1));
        Assert.That(life.Gone, Is.False);
    }

    [Test]
    public void StoppedIsAStateNotAnExit()
    {
        var life = Life(WorkState.Stopped);
        var poses = Run(life, 120);
        Assert.That(poses.Select(p => p.Scale), Has.All.EqualTo(1.0));
        Assert.That(life.Gone, Is.False);
        Assert.That(life.TagWord, Is.Null);
        Assert.That(life.Look.Lids, Is.EqualTo(1.0));
    }

    [Test]
    public void CharactersOffFoldEveryFigureButKeepTheTag()
    {
        var life = Life(WorkState.Working);
        Run(life, 1);
        life.CharactersOff(true);
        var poses = Run(life, 0.5);
        Assert.That(poses[^1].Scale, Is.EqualTo(0));
        Assert.That(life.TagOpacity, Is.EqualTo(1), "the tag stays as the task's mark");
        Assert.That(life.TagWord, Is.Null, "no word: the setting says it");
        Assert.That(life.Gone, Is.False);
        life.CharactersOff(false);
        Assert.That(Run(life, 1)[^1].Scale, Is.EqualTo(1));
    }

    [Test]
    public void AMomentLeavesWithALeanAnEyePulseAndItsVoiceThenItLooksAtTheCardAndAtYou()
    {
        var life = Life(WorkState.Working);
        Run(life, 1);
        life.TakeSounds();
        life.MomentOut();
        var poses = Run(life, 2);
        Assert.That(poses.Take(24).Max(p => p.LeanDegrees), Is.EqualTo(3).Within(0.6), "leans back");
        Assert.That(poses.Take(15).Max(p => p.Lids), Is.GreaterThanOrEqualTo(0.19), "the eye pulse");
        Assert.That(poses.Take(40).Select(p => p.Gaze), Has.All.EqualTo(CharacterGaze.Moment));
        Assert.That(poses[60].Gaze, Is.EqualTo(CharacterGaze.Person));
        Assert.That(life.TakeSounds(), Is.EqualTo(new[] { CharacterSound.Voice }));
    }

    // Posture by state.

    [Test]
    public void ThePostureHeldComesFromTheLook()
    {
        var life = Life(WorkState.WaitingForYou);
        var last = Run(life, 5)[^1];
        Assert.That(last.LiftM, Is.EqualTo(0.008).Within(1e-4));
        Assert.That(last.HeadRollDegrees, Is.EqualTo(8).Within(0.1));
        life.Show(WorkState.ChecksFailed, false, true);
        last = Run(life, 5)[^1];
        Assert.That(last.LiftM, Is.EqualTo(-KitFigureLook.DpToM(3)).Within(1e-4));
        Assert.That(last.HeadPitchDegrees, Is.EqualTo(8).Within(0.1));
    }

    [Test]
    public void ThePoolBreathesOnlyWhereTheStateBreathes()
    {
        var breathing = Run(Life(WorkState.Working), 5).Select(p => p.Breath).ToList();
        Assert.That(breathing.Min(), Is.EqualTo(0.55).Within(0.01));
        Assert.That(breathing.Max(), Is.EqualTo(1.0).Within(0.01));
        Assert.That(Run(Life(WorkState.ChecksFailed), 5).Select(p => p.Breath), Has.All.EqualTo(1.0), "steady");
    }

    // Its sounds: identity by voice, nothing loops.

    [Test]
    public void EachFeatureHasItsOwnVoiceAndTheCuesAreShort()
    {
        var features = Enum.GetValues<KitFigureFeature>();
        Assert.That(features.Select(CharacterSounds.VoicePair).Distinct().Count(), Is.EqualTo(features.Length));
        var voice = CharacterSounds.Tones(CharacterSound.Voice, KitFigureFeature.Antenna);
        Assert.That(voice.Select(t => (t.StartMs, t.DurationMs, Math.Round(t.FromHz, 1))), Is.EqualTo(new[] { (0.0, 70.0, 1760.0), (110.0, 70.0, 1318.5) }));
        Assert.That(CharacterSounds.Tones(CharacterSound.Arrive, KitFigureFeature.Fins).Select(t => (t.StartMs, Math.Round(t.FromHz))), Is.EqualTo(new[] { (0.0, 880.0), (90.0, 1319.0) }));
        var hop = CharacterSounds.Tones(CharacterSound.Hop, KitFigureFeature.Fins).Single();
        Assert.That((hop.DurationMs, Math.Round(hop.FromHz), Math.Round(hop.ToHz)), Is.EqualTo((120.0, 1319.0, 1760.0)));
        var fold = CharacterSounds.Tones(CharacterSound.Fold, KitFigureFeature.Fins).Single();
        Assert.That((fold.Noise, fold.DurationMs, fold.FromHz, fold.ToHz), Is.EqualTo((true, 160.0, 2400.0, 400.0)));
        Assert.That(Enum.GetValues<CharacterSound>().Select(CharacterSounds.LevelDb), Is.EqualTo(new[] { -30.0, -36.0, -32.0, -33.0 }));
        foreach (var sound in Enum.GetValues<CharacterSound>())
            Assert.That(CharacterSounds.Tones(sound, KitFigureFeature.Loop).Max(t => t.StartMs + t.DurationMs), Is.LessThanOrEqualTo(350), "short; nothing loops");
        Assert.That(CharacterSounds.Hz("C#6"), Is.EqualTo(1108.73).Within(0.01));
    }

    private static double Correlation(double[] a, double[] b)
    {
        double ma = a.Average(), mb = b.Average();
        double sab = 0, saa = 0, sbb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            sab += (a[i] - ma) * (b[i] - mb);
            saa += (a[i] - ma) * (a[i] - ma);
            sbb += (b[i] - mb) * (b[i] - mb);
        }
        return sab / Math.Sqrt(saa * sbb);
    }
}

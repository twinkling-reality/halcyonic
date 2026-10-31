using System;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// A Workstream's figure by its state (the art direction's character handoff, 2026-10-30, section 2; AUDIT C1 to C4): every
/// row of the state table, and its truth rules as tests: the passed look only with the computer's own passed check, never
/// from Checking or Round Ended alone; Last Known frozen, dimmed and pool-less, never a vanish; Status Unknown still moving;
/// and no two states told apart by colour alone.
/// </summary>
public class KitFigureLookTests
{
    private static readonly WorkState[] States = Enum.GetValues<WorkState>();

    private static (double Lids, double Shorter, double Roll, bool Happy, bool Pooled, bool Breathes, bool Wanders, double Float, double Rise, double HeadRoll, double Pitch, bool Moves) Shape(KitFigureLook look) =>
        (look.Lids, look.RightEyeShorter, look.RightEyeRollDegrees, look.Happy, look.Pool.HasValue, look.Breathes, look.Wanders, look.FloatSpeed, look.RiseM, look.HeadRollDegrees, look.HeadPitchDegrees, look.Moves);

    [TestCase(WorkState.NotStarted, 0xA0A5AC, 0.30, null, 0.0, false, false, 0.6)]
    [TestCase(WorkState.Starting, 0x59CFF1, 0.0, 0x59CFF1, 0.55, true, false, 1.0)]
    [TestCase(WorkState.Working, 0x59CFF1, 0.0, 0x59CFF1, 0.55, true, true, 1.0)]
    [TestCase(WorkState.CheckingItsWork, 0x59CFF1, 0.15, 0x59CFF1, 0.55, true, false, 1.0)]
    [TestCase(WorkState.WaitingForYou, 0xFABA59, 0.0, 0xFABA59, 0.55, true, false, 1.0)]
    [TestCase(WorkState.FinishedThisRound, 0xDADDE0, 0.25, 0xDADDE0, 0.40, false, false, 0.6)]
    [TestCase(WorkState.ChecksFailed, 0xF7857D, 0.35, 0xF7857D, 0.55, false, false, 0.5)]
    [TestCase(WorkState.CouldNotFinish, 0xF7857D, 0.35, 0xF7857D, 0.55, false, false, 0.5)]
    [TestCase(WorkState.Stopped, 0xA0A5AC, 1.0, null, 0.0, false, false, 0.6)]
    [TestCase(WorkState.CantTellYet, 0xA0A5AC, 0.10, null, 0.0, false, false, 1.0)]
    public void EachStateLooksAsTheTableSays(WorkState state, int eyes, double lids, int? pool, double poolLevel, bool breathes, bool wanders, double floatSpeed)
    {
        var look = KitFigureLook.Of(state, passedCheckObserved: false);
        Assert.That(look.Eyes, Is.EqualTo(GlazeColor.Hex((uint)eyes)), "eyes");
        Assert.That(look.Lids, Is.EqualTo(lids), "lids");
        Assert.That(look.Pool, Is.EqualTo(pool is int tone ? GlazeColor.Hex((uint)tone) : (GlazeColor?)null), "pool");
        Assert.That(look.PoolLevelShown, Is.EqualTo(poolLevel), "pool level");
        Assert.That((look.Breathes, look.Wanders, look.FloatSpeed), Is.EqualTo((breathes, wanders, floatSpeed)), "breath, wander, float");
        Assert.That(look.Glow, Is.EqualTo(KitFigureLook.GlowLevel));
        Assert.That((look.Lightness, look.Saturation, look.Moves, look.Happy), Is.EqualTo((1.0, 1.0, true, false)), "whole, moving, never happy without the check");
    }

    [Test]
    public void ThePostureEachStateHolds()
    {
        Assert.That((KitFigureLook.Of(WorkState.WaitingForYou, false).RiseM, KitFigureLook.Of(WorkState.WaitingForYou, false).HeadRollDegrees), Is.EqualTo((0.008, 8.0)), "Needs You: up 8 mm, head rolled 8 degrees");
        Assert.That(KitFigureLook.Of(WorkState.FinishedThisRound, false).RiseM, Is.EqualTo(-0.003), "Round Ended: down 3 mm");
        Assert.That(KitFigureLook.Of(WorkState.Stopped, false).RiseM, Is.EqualTo(-0.006), "Stopped: down 6 mm");
        var failed = KitFigureLook.Of(WorkState.ChecksFailed, false);
        Assert.That(failed.RiseM, Is.EqualTo(-Math.Tan(3.0 / 16 * Math.PI / 180) * 1.9).Within(1e-6), "Checks Failed: down 3 dp at 1.9 m");
        Assert.That(failed.HeadPitchDegrees, Is.EqualTo(8), "Checks Failed: the head 8 degrees lower");
        var unknown = KitFigureLook.Of(WorkState.CantTellYet, false);
        Assert.That((unknown.RightEyeShorter, unknown.RightEyeRollDegrees, unknown.HeadRollDegrees), Is.EqualTo((0.30, 11.0, 4.0)), "Status Unknown: the right eye 30 percent shorter, rolled 11 degrees, the head 4 degrees held");
    }

    [Test]
    public void ThePassedLookOnlyWithTheComputersOwnPassedCheck()
    {
        var passed = KitFigureLook.Of(WorkState.FinishedThisRound, passedCheckObserved: true);
        Assert.That((passed.Eyes, passed.Pool, passed.Happy, passed.Lids), Is.EqualTo((SpatialKit.Passed, (GlazeColor?)SpatialKit.Passed, true, 0.0)));
        var ended = KitFigureLook.Of(WorkState.FinishedThisRound, passedCheckObserved: false);
        Assert.That(ended.Eyes, Is.Not.EqualTo(SpatialKit.Passed), "Round Ended alone is neutral");
        Assert.That(ended.Happy, Is.False);
        foreach (var state in States.Where(state => state != WorkState.FinishedThisRound))
        {
            // The check changes nothing else: Checking is the agent's report and never the passed look.
            Assert.That(KitFigureLook.Of(state, true), Is.EqualTo(KitFigureLook.Of(state, false)), state + " looks different with a passed check");
            Assert.That(KitFigureLook.Of(state, true).Eyes, Is.Not.EqualTo(SpatialKit.Passed), state + " shows the passed eyes");
            Assert.That(KitFigureLook.Of(state, true).Happy, Is.False, state + " shows happy eyes");
        }
    }

    [Test]
    public void LastKnownIsFrozenDimmedAndPoolLessButStillStands()
    {
        foreach (var state in States)
            foreach (var check in new[] { false, true })
            {
                var last = KitFigureLook.Of(state, check);
                var lost = KitFigureLook.Unreachable(last);
                Assert.That(lost.Moves, Is.False, state + ": a figure moves while the computer is unreachable");
                Assert.That(lost.FloatSpeed, Is.Zero);
                Assert.That(lost.Pool, Is.Null, state + ": a pool while the computer is unreachable");
                Assert.That(lost.PoolLevelShown, Is.Zero);
                Assert.That((lost.Eyes, lost.Glow), Is.EqualTo((SpatialKit.Hairline, KitFigureLook.UnreachableGlowLevel)), state + ": eyes n4, glow low");
                Assert.That((lost.Lightness, lost.Saturation), Is.EqualTo((0.5, 0.3)), state + ": the body dimmed");
                Assert.That((lost.Lids, lost.RiseM, lost.HeadRollDegrees), Is.EqualTo((last.Lids, last.RiseM, last.HeadRollDegrees)), state + ": the last pose held");
                Assert.That(lost.Happy, Is.False);
                // Still a figure, never a vanish: its eyes still show (Stopped's held lids excepted, as it was) and its body stands.
                Assert.That(lost.Lightness, Is.GreaterThan(0));
            }
    }

    [Test]
    public void StatusUnknownStillMoves() =>
        Assert.That((KitFigureLook.Of(WorkState.CantTellYet, false).Moves, KitFigureLook.Of(WorkState.CantTellYet, false).FloatSpeed), Is.EqualTo((true, 1.0)));

    [Test]
    public void NoTwoStatesAreToldApartByColourAlone()
    {
        var looks = States.Select(state => (Name: state.ToString(), Look: KitFigureLook.Of(state, false)))
            .Append((Name: "Round Ended with the computer's passed check", Look: KitFigureLook.Of(WorkState.FinishedThisRound, true))).ToList();
        foreach (var a in looks)
            foreach (var b in looks.Where(b => b.Name != a.Name && !b.Look.Equals(a.Look)))
                Assert.That(Shape(a.Look), Is.Not.EqualTo(Shape(b.Look)), a.Name + " and " + b.Name + " differ by colour alone");
    }

    [Test]
    public void TopFeaturesGoInTaskOrderAndRepeat()
    {
        Assert.That(Enumerable.Range(0, 6).Select(KitFigureLook.FeatureFor), Is.EqualTo(Enum.GetValues<KitFigureFeature>()));
        Assert.That(KitFigureLook.FeatureFor(6), Is.EqualTo(KitFigureFeature.Antenna));
        Assert.That(KitFigureLook.FeatureFor(-1), Is.EqualTo(KitFigureFeature.Brim));
    }
}

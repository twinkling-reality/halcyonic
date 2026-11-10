using System;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The redesign's motion (DESIGN's BUILD-SPEC-v4 1.5) as formulas of the time since an event: the two curves' values, and
/// the agent's words band's arrival, its lines' stagger, a line leaving, the height easing and the band leaving (1.6).
/// </summary>
public class RoomMotionTests
{
    private const float Close = 1e-4f;

    /// <summary>cubic-bezier(x1, y1, x2, y2) at a time, found by walking the curve finely: a second way to the same value.</summary>
    private static float BezierByWalking(float x1, float y1, float x2, float y2, float time)
    {
        var best = 0.0;
        var nearest = double.MaxValue;
        for (var step = 0; step <= 200000; step++)
        {
            var s = step / 200000.0;
            var x = 3 * (1 - s) * (1 - s) * s * x1 + 3 * (1 - s) * s * s * x2 + s * s * s;
            if (Math.Abs(x - time) >= nearest) continue;
            nearest = Math.Abs(x - time);
            best = s;
        }
        return (float)(3 * (1 - best) * (1 - best) * best * y1 + 3 * (1 - best) * best * best * y2 + best * best * best);
    }

    [Test]
    public void EaseOutIsTheCubicFastFirstThenSettling()
    {
        Assert.That(RoomMotion.EaseOut(0f), Is.EqualTo(0f));
        Assert.That(RoomMotion.EaseOut(0.25f), Is.EqualTo(1f - 0.75f * 0.75f * 0.75f).Within(Close));
        Assert.That(RoomMotion.EaseOut(0.5f), Is.EqualTo(0.875f).Within(Close));
        Assert.That(RoomMotion.EaseOut(1f), Is.EqualTo(1f));
        Assert.That(RoomMotion.EaseOut(-3f), Is.EqualTo(0f), "before its start it is its start");
        Assert.That(RoomMotion.EaseOut(float.PositiveInfinity), Is.EqualTo(1f), "and after its end its end");
    }

    [Test]
    public void TheStandardCurveIsTheBezierOfTheSpec()
    {
        foreach (var time in new[] { 0.1f, 0.25f, 0.5f, 0.75f, 0.9f })
            Assert.That(RoomMotion.Standard(time), Is.EqualTo(BezierByWalking(0.2f, 0f, 0f, 1f, time)).Within(1e-3f), "at " + time);
        Assert.That(RoomMotion.Standard(0f), Is.EqualTo(0f));
        Assert.That(RoomMotion.Standard(1f), Is.EqualTo(1f));
        Assert.That(RoomMotion.Standard(-1f), Is.EqualTo(0f));
        Assert.That(RoomMotion.Standard(float.PositiveInfinity), Is.EqualTo(1f));
        Assert.That(RoomMotion.Standard(0.5f), Is.GreaterThan(0.8f), "it is most of the way by half its time");
        var last = 0f;
        for (var step = 1; step <= 50; step++)
        {
            var value = RoomMotion.Standard(step / 50f);
            Assert.That(value, Is.GreaterThanOrEqualTo(last));
            last = value;
        }
    }

    [Test]
    public void TheBandFadesInOverThreeHundredAndSixtyMilliseconds()
    {
        Assert.That(RoomMotion.BandArrives(0f), Is.EqualTo(0f));
        Assert.That(RoomMotion.BandArrives(0.18f), Is.EqualTo(0.875f).Within(Close));
        Assert.That(RoomMotion.BandArrives(0.36f), Is.EqualTo(1f).Within(Close));
        Assert.That(RoomMotion.BandArrives(5f), Is.EqualTo(1f));
    }

    [Test]
    public void EachLineRisesEightDpAndFadesInSixtyMillisecondsAfterTheOneAbove()
    {
        var start = RoomMotion.LineArrives(0f, 0);
        Assert.That((start.Shown, start.LowerDp), Is.EqualTo((0f, 8f)));
        var half = RoomMotion.LineArrives(0.12f, 0);
        Assert.That(half.Shown, Is.EqualTo(0.875f).Within(Close));
        Assert.That(half.LowerDp, Is.EqualTo(1f).Within(Close));
        var end = RoomMotion.LineArrives(0.24f, 0);
        Assert.That((end.Shown, end.LowerDp), Is.EqualTo((1f, 0f)));

        // The second line is where the first was 60 ms before, the third 120 ms before.
        Assert.That(RoomMotion.LineArrives(0.06f, 1).Shown, Is.EqualTo(0f));
        Assert.That(RoomMotion.LineArrives(0.18f, 1).Shown, Is.EqualTo(half.Shown).Within(Close));
        Assert.That(RoomMotion.LineArrives(0.24f, 2).Shown, Is.EqualTo(half.Shown).Within(Close));
        Assert.That(RoomMotion.LineArrives(0.36f, 2).Shown, Is.EqualTo(1f));
        Assert.That(RoomMotion.BandLinesAfterSeconds, Is.EqualTo(0.12f), "the first line starts 120 ms after the band");
    }

    [Test]
    public void ALineLeavesUpwardOverOneHundredAndTwentyMilliseconds()
    {
        var start = RoomMotion.LineLeaves(0f);
        Assert.That((start.Shown, start.LowerDp), Is.EqualTo((1f, 0f)));
        var middle = RoomMotion.LineLeaves(0.06f);
        var curve = BezierByWalking(0.2f, 0f, 0f, 1f, 0.5f);
        Assert.That(middle.Shown, Is.EqualTo(1f - curve).Within(1e-3f));
        Assert.That(middle.LowerDp, Is.EqualTo(-8f * curve).Within(1e-2f), "it rises above its place");
        var end = RoomMotion.LineLeaves(0.12f);
        Assert.That((end.Shown, end.LowerDp), Is.EqualTo((0f, -8f)));
    }

    [Test]
    public void TheHeightWaitsForTheOldLinesThenEasesAndTheNewLinesFollow()
    {
        Assert.That(RoomMotion.BandHeight(0f, 80f, 136f), Is.EqualTo(80f));
        Assert.That(RoomMotion.BandHeight(0.12f, 80f, 136f), Is.EqualTo(80f), "the old lines leave first");
        Assert.That(RoomMotion.BandHeight(0.24f, 80f, 136f), Is.EqualTo(80f + 56f * 0.875f).Within(1e-3f));
        Assert.That(RoomMotion.BandHeight(0.36f, 80f, 136f), Is.EqualTo(136f).Within(1e-3f));
        Assert.That(RoomMotion.BandHeight(0.24f, 136f, 80f), Is.EqualTo(136f - 56f * 0.875f).Within(1e-3f), "and shrinks the same way");
        Assert.That(RoomMotion.ReplaceLinesAfterSeconds, Is.EqualTo(0.36f).Within(Close));
    }

    [Test]
    public void TheWorkingFieldFadesInAndOutOverSixHundredMillisecondsOnTheStandardCurve()
    {
        var half = BezierByWalking(0.2f, 0f, 0f, 1f, 0.5f);
        Assert.That(RoomMotion.FieldArrives(0f), Is.EqualTo(0f));
        Assert.That(RoomMotion.FieldArrives(0.3f), Is.EqualTo(half).Within(1e-3f));
        Assert.That(RoomMotion.FieldArrives(0.6f), Is.EqualTo(1f));
        Assert.That(RoomMotion.FieldLeaves(0f), Is.EqualTo(1f));
        Assert.That(RoomMotion.FieldLeaves(0.3f), Is.EqualTo(1f - half).Within(1e-3f));
        Assert.That(RoomMotion.FieldLeaves(0.6f), Is.EqualTo(0f));
        Assert.That(RoomMotion.FieldLeaves(float.PositiveInfinity), Is.EqualTo(0f));
    }

    [Test]
    public void TheCursorGlidesOverSixHundredMillisecondsAndDriftsAtMostAThirdOfADegree()
    {
        Assert.That(RoomMotion.CursorGlides(0f), Is.EqualTo(0f));
        Assert.That(RoomMotion.CursorGlides(0.3f), Is.EqualTo(0.875f).Within(Close));
        Assert.That(RoomMotion.CursorGlides(0.6f), Is.EqualTo(1f));
        var widest = 0f;
        var tallest = 0f;
        var moved = false;
        var last = RoomMotion.CursorDrift(0f);
        for (var step = 1; step <= 2000; step++)
        {
            var drift = RoomMotion.CursorDrift(step * 0.05f);
            widest = Math.Max(widest, Math.Abs(drift.Right));
            tallest = Math.Max(tallest, Math.Abs(drift.Up));
            Assert.That(Math.Abs(drift.Right - last.Right), Is.LessThan(0.02f), "it wanders, it never jumps");
            moved |= drift.Right != last.Right;
            last = drift;
        }
        Assert.That(moved, Is.True);
        Assert.That(widest, Is.LessThanOrEqualTo(0.35f).And.GreaterThan(0.34f));
        Assert.That(tallest, Is.LessThanOrEqualTo(0.21f + 1e-5f).And.GreaterThan(0.2f));
    }

    [Test]
    public void ThePanelRisesADegreeAndFadesInThenLeavesOnTheStandardCurve()
    {
        var start = RoomMotion.PanelArrives(0f);
        Assert.That((start.Shown, start.LowerDp), Is.EqualTo((0f, 16f)));
        var half = RoomMotion.PanelArrives(0.18f);
        Assert.That(half.Shown, Is.EqualTo(0.875f).Within(Close));
        Assert.That(half.LowerDp, Is.EqualTo(2f).Within(1e-3f));
        var end = RoomMotion.PanelArrives(0.36f);
        Assert.That(end.Shown, Is.EqualTo(1f).Within(Close));
        Assert.That(end.LowerDp, Is.EqualTo(0f).Within(1e-3f));
        Assert.That(RoomMotion.PanelLeaves(0f), Is.EqualTo(1f));
        Assert.That(RoomMotion.PanelLeaves(0.18f), Is.EqualTo(1f - BezierByWalking(0.2f, 0f, 0f, 1f, 0.5f)).Within(1e-3f));
        Assert.That(RoomMotion.PanelLeaves(0.36f), Is.EqualTo(0f));
    }

    [Test]
    public void TheColumnsActionsArriveAfterTwoHundredAndTwentyMillisecondsSixtyApart()
    {
        Assert.That(RoomMotion.ActionArrives(0.22f, 0).Shown, Is.EqualTo(0f).Within(Close));
        Assert.That(RoomMotion.ActionArrives(0.22f, 0).LowerDp, Is.EqualTo(8f).Within(1e-3f));
        Assert.That(RoomMotion.ActionArrives(0.34f, 0).Shown, Is.EqualTo(0.875f).Within(Close));
        Assert.That(RoomMotion.ActionArrives(0.34f, 0).LowerDp, Is.EqualTo(1f).Within(1e-3f));
        Assert.That(RoomMotion.ActionArrives(0.46f, 0).Shown, Is.EqualTo(1f).Within(Close));
        Assert.That(RoomMotion.ActionArrives(0.34f, 2).Shown, Is.EqualTo(0f).Within(Close), "the third waits 120 ms more");
        Assert.That(RoomMotion.ActionArrives(0.46f, 2).Shown, Is.EqualTo(0.875f).Within(Close));
        Assert.That(RoomMotion.ActionArrives(0.64f, 3).Shown, Is.EqualTo(1f).Within(Close), "four actions are whole by 640 ms");
    }

    [Test]
    public void AnActionAnswersPointingOverOneHundredAndFiftyMillisecondsAndShrinksWhileHeld()
    {
        Assert.That(RoomMotion.Pointed(0f), Is.EqualTo(0f));
        Assert.That(RoomMotion.Pointed(0.075f), Is.EqualTo(0.875f).Within(Close));
        Assert.That(RoomMotion.Pointed(0.15f), Is.EqualTo(1f).Within(Close));
        Assert.That(RoomMotion.ActionScale(0f, false), Is.EqualTo(1f));
        Assert.That(RoomMotion.ActionScale(1f, false), Is.EqualTo(1.03f).Within(Close));
        Assert.That(RoomMotion.ActionScale(0.5f, false), Is.EqualTo(1.015f).Within(Close));
        Assert.That(RoomMotion.ActionScale(1f, true), Is.EqualTo(0.96f), "held, it is 0.96 at once, pointed or not");
    }

    [Test]
    public void AnAnswersMarkFillsOverTwoHundredAndFiftyMilliseconds()
    {
        Assert.That(RoomMotion.MarkFills(0f), Is.EqualTo(0f));
        Assert.That(RoomMotion.MarkFills(0.125f), Is.EqualTo(0.875f).Within(Close));
        Assert.That(RoomMotion.MarkFills(0.25f), Is.EqualTo(1f).Within(Close));
    }

    [Test]
    public void ConfirmsRingFillsInAStraightLineThenItsChipLights()
    {
        Assert.That(RoomMotion.ConfirmRing(0f), Is.EqualTo(0f));
        Assert.That(RoomMotion.ConfirmRing(0.35f), Is.EqualTo(0.5f).Within(Close));
        Assert.That(RoomMotion.ConfirmRing(0.7f), Is.EqualTo(1f).Within(Close));
        Assert.That(RoomMotion.ConfirmRing(3f), Is.EqualTo(1f));
        Assert.That(RoomMotion.ConfirmLights(0.7f), Is.EqualTo(0f).Within(Close), "the chip waits for the ring");
        Assert.That(RoomMotion.ConfirmLights(0.76f), Is.EqualTo(0.5f).Within(1e-3f));
        Assert.That(RoomMotion.ConfirmLights(0.82f), Is.EqualTo(1f).Within(1e-3f));
    }

    [Test]
    public void TheBandLeavesOnTheStandardCurve()
    {
        Assert.That(RoomMotion.BandLeaves(0f), Is.EqualTo(1f));
        Assert.That(RoomMotion.BandLeaves(0.18f), Is.EqualTo(1f - BezierByWalking(0.2f, 0f, 0f, 1f, 0.5f)).Within(1e-3f));
        Assert.That(RoomMotion.BandLeaves(0.36f), Is.EqualTo(0f));
        Assert.That(RoomMotion.BandLeaves(9f), Is.EqualTo(0f));
    }
}

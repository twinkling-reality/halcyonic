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
    public void TheBandLeavesOnTheStandardCurve()
    {
        Assert.That(RoomMotion.BandLeaves(0f), Is.EqualTo(1f));
        Assert.That(RoomMotion.BandLeaves(0.18f), Is.EqualTo(1f - BezierByWalking(0.2f, 0f, 0f, 1f, 0.5f)).Within(1e-3f));
        Assert.That(RoomMotion.BandLeaves(0.36f), Is.EqualTo(0f));
        Assert.That(RoomMotion.BandLeaves(9f), Is.EqualTo(0f));
    }
}

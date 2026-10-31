using System;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// A moment's arrival (the art direction's choreography, 2026-10-30): every value from the time alone, the spring as the
/// handoff gives it, the bloom brief and never driven by progress (AUDIT's A2-S2), the parts in reading order, and Keep Still.
/// </summary>
public class KitMomentTimelineTests
{
    private static readonly double[] Clock = Enumerable.Range(0, 2401).Select(ms => (double)ms).ToArray();

    [Test]
    public void TheSpringSettlesIn420MsWithAboutThreePercentOvershoot()
    {
        var peak = Clock.Max(KitMotion.Spring);
        Assert.That(peak, Is.InRange(1.02, 1.04), "about 3 percent overshoot");
        // Settled as a spring's settling time is read: inside the 2 percent band from 420 ms on (the handoff's "settles in 420 ms").
        Assert.That(Clock.Where(ms => ms >= 420).All(ms => Math.Abs(KitMotion.Spring(ms) - 1) <= 0.02), Is.True, "within 2 percent from 420 ms on");
        Assert.That(Clock.Where(ms => ms >= 300 && ms < 380).Any(ms => Math.Abs(KitMotion.Spring(ms) - 1) > 0.02), Is.True, "not settled much sooner");
        Assert.That(KitMotion.Spring(0), Is.EqualTo(0));
    }

    [Test]
    public void TheCurvesRunFromNothingToWholeAndNeverBack()
    {
        foreach (var curve in new Func<double, double>[] { KitMotion.Standard, KitMotion.Decelerate, KitMotion.Accelerate })
        {
            Assert.That((curve(0), curve(1)), Is.EqualTo((0.0, 1.0)));
            var samples = Enumerable.Range(0, 101).Select(i => curve(i / 100.0)).ToList();
            Assert.That(samples.Zip(samples.Skip(1), (a, b) => b >= a - 1e-9).All(up => up), Is.True, "monotone");
        }
        Assert.That(KitMotion.Decelerate(0.25), Is.GreaterThan(KitMotion.Accelerate(0.25)), "decelerate leads, accelerate lags");
    }

    [Test]
    public void TheBloomIsBriefAndReadFromTheClockAlone()
    {
        // From the open at 160 ms: full for a quarter of its 1400 ms, then out; nothing before the open or after it ends.
        Assert.That(Clock.Where(ms => ms < 160).All(ms => KitMomentTimeline.Bloom(ms, still: false) == 0), Is.True, "before the open");
        Assert.That(Clock.Where(ms => ms >= 160 && ms <= 510).All(ms => KitMomentTimeline.Bloom(ms, still: false) == 1), Is.True, "held at its peak");
        Assert.That(Clock.Where(ms => ms >= 1560).All(ms => KitMomentTimeline.Bloom(ms, still: false) == 0), Is.True, "gone after the pop");
        var fading = Clock.Where(ms => ms > 510 && ms < 1560).Select(ms => KitMomentTimeline.Bloom(ms, still: false)).ToList();
        Assert.That(fading.Zip(fading.Skip(1), (a, b) => b <= a + 1e-12).All(down => down), Is.True, "it only fades");
        // The frame's bloom is the clock's: a frame is a function of its time and Keep Still, and of nothing else.
        Assert.That(Clock.All(ms => KitMomentTimeline.Arrival(ms, still: false).Bloom == KitMomentTimeline.Bloom(ms, still: false)), Is.True);
        Assert.That(Clock.All(ms => KitMomentTimeline.Arrival(ms, still: true).Bloom == 0), Is.True, "no bloom under Keep Still");
        Assert.That(Clock.All(ms => KitMomentTimeline.Bloom(ms, still: true) == 0), Is.True, "nor from the clock under Keep Still");
        Assert.That(Clock.All(ms => KitMomentTimeline.Retirement(ms, still: false).Bloom == 0), Is.True, "no bloom as it retires");
    }

    [Test]
    public void ThePartsRiseIntoPlaceInReadingOrder()
    {
        const int parts = 5;
        foreach (var ms in Clock)
        {
            var frame = KitMomentTimeline.Arrival(ms, still: false);
            for (var i = 0; i + 1 < parts; i++)
                Assert.That(frame.Part(i).Opacity, Is.GreaterThanOrEqualTo(frame.Part(i + 1).Opacity), $"part {i} leads part {i + 1} at {ms} ms");
        }
        for (var i = 0; i < parts; i++)
        {
            var start = KitMomentTimeline.PartsAtMs + i * KitMomentTimeline.PartStepMs;
            Assert.That(KitMomentTimeline.Arrival(start, still: false).Part(i), Is.EqualTo((6.0, 0.0)), $"part {i} begins 6 dp low, unseen");
            Assert.That(KitMomentTimeline.Arrival(start + 200, still: false).Part(i), Is.EqualTo((0.0, 1.0)), $"part {i} lands in 200 ms");
        }
    }

    [Test]
    public void TheLineRisesFromTheCharacterBeforeTheCardOpens()
    {
        Assert.That(KitMomentTimeline.Arrival(59, still: false).LineOpacity, Is.EqualTo(0));
        Assert.That(KitMomentTimeline.Arrival(60, still: false).LineOpacity, Is.EqualTo(0.15).Within(1e-9), "the tone from 15 percent");
        Assert.That(KitMomentTimeline.Arrival(180, still: false).LineProgress, Is.EqualTo(1), "risen in 120 ms");
        Assert.That(KitMomentTimeline.Arrival(440, still: false).LineOpacity, Is.EqualTo(0), "faded 260 ms after 120");
        Assert.That(KitMomentTimeline.Arrival(159, still: false).CardOpacity, Is.EqualTo(0), "the card opens at 160 ms");
        Assert.That(KitMomentTimeline.Arrival(160, still: false).CardScale, Is.EqualTo(0.86).Within(1e-9), "from 86 percent");
    }

    [Test]
    public void AtRestEveryValueIsTheStaticCards()
    {
        foreach (var parts in new[] { 1, 3, 7 })
        {
            var end = KitMomentTimeline.ArrivalMs(parts);
            var frame = KitMomentTimeline.Arrival(end, still: false);
            Assert.That(frame.CardScale, Is.EqualTo(1).Within(0.005), $"{parts} parts: scale");
            Assert.That((frame.CardOpacity, frame.Bloom, frame.LineOpacity), Is.EqualTo((1.0, 0.0, 0.0)), $"{parts} parts");
            Assert.That(Enumerable.Range(0, parts).All(i => frame.Part(i) == (0.0, 1.0)), Is.True, $"{parts} parts in place");
        }
    }

    [Test]
    public void APassesCheckMarkDrawsItselfAfterTheCardSettles()
    {
        Assert.That(KitMomentTimeline.CheckDrawn(KitMomentTimeline.OpenAtMs + 419, still: false), Is.EqualTo(0));
        Assert.That(KitMomentTimeline.CheckDrawn(KitMomentTimeline.OpenAtMs + 720, still: false), Is.EqualTo(1));
        Assert.That(KitMomentTimeline.ArrivalMs(1), Is.GreaterThanOrEqualTo(KitMomentTimeline.CheckAtMs + KitMomentTimeline.CheckMs), "at rest it is drawn");
        Assert.That(KitMomentTimeline.CheckDrawn(120, still: true), Is.EqualTo(1), "at once under Keep Still");
    }

    [Test]
    public void KeepStillLandsEachChangeAsAShortFade()
    {
        foreach (var ms in Clock)
        {
            var frame = KitMomentTimeline.Arrival(ms, still: true);
            Assert.That((frame.CardScale, frame.LineOpacity, frame.Bloom, frame.Part(3).RiseDp), Is.EqualTo((1.0, 0.0, 0.0, 0.0)), $"nothing moves at {ms} ms");
        }
        Assert.That(KitMomentTimeline.Arrival(120, still: true).CardOpacity, Is.EqualTo(1), "a 120 ms fade");
        Assert.That(KitMomentTimeline.Retirement(120, still: true).CardOpacity, Is.EqualTo(0));
    }

    [Test]
    public void ItRetiresSmallAndGoneAsTheLineDropsBack()
    {
        var end = KitMomentTimeline.Retirement(KitMomentTimeline.RetireMs, still: false);
        Assert.That(end.CardScale, Is.EqualTo(0.2).Within(1e-9));
        Assert.That((end.CardOpacity, end.LineOpacity), Is.EqualTo((0.0, 0.0)));
        Assert.That(KitMomentTimeline.Retirement(KitMomentTimeline.LineDropMs, still: false).LineProgress, Is.EqualTo(0), "the line back first");
    }

    [Test]
    public void TheTakenAnswerStaysWhileTheOthersFade()
    {
        Assert.That((KitMomentTimeline.Answered(0, false).OthersOpacity, KitMomentTimeline.Answered(120, false).OthersOpacity), Is.EqualTo((1.0, 0.0)));
        Assert.That(Clock.Where(ms => ms < KitMomentTimeline.ShrinkAtMs).All(ms => KitMomentTimeline.Answered(ms, false).KeptOpacity == 1 && KitMomentTimeline.Answered(ms, false).BodyOpacity == 1), Is.True, "the taken answer and the card stand until the shrink");
        Assert.That(KitMomentTimeline.Answered(KitMomentTimeline.SentAtMs, false).KeptOpacity, Is.EqualTo(0.0), "gone before the receipt comes in");
    }

    [Test]
    public void TheCardShrinksToItsReceiptFrom200Ms()
    {
        var shrink = Clock.Select(ms => KitMomentTimeline.Answered(ms, false).Shrink).ToList();
        Assert.That((shrink[199], shrink[200], shrink[520]), Is.EqualTo((0.0, 0.0, 1.0)));
        Assert.That(shrink.Zip(shrink.Skip(1), (a, b) => b >= a - 1e-9).All(up => up), Is.True, "it only shrinks");
        Assert.That(Clock.All(ms => KitMomentTimeline.Answered(ms, false).Lit == ms < 200), Is.True, "lit while the question stands, then not");
    }

    [Test]
    public void TheReceiptComesInOnlyAfterTheShrinkBegins()
    {
        Assert.That((KitMomentTimeline.Answered(279, false).ReceiptOpacity, KitMomentTimeline.Answered(480, false).ReceiptOpacity), Is.EqualTo((0.0, 1.0)));
        Assert.That(Clock.All(ms => KitMomentTimeline.Answered(ms, false).ReceiptOpacity == 0 || KitMomentTimeline.Answered(ms, false).Shrink > 0), Is.True);
        Assert.That(KitMomentTimeline.SentAtMs, Is.EqualTo(280.0), "the Sent cue with the receipt");
        Assert.That(Clock.All(ms => KitMomentTimeline.Answered(ms, false).Retiring == ms >= 1600), Is.True, "it retires at 1600 ms");
    }

    [Test]
    public void AnAnswerKeptStillLandsAsFades()
    {
        Assert.That(Clock.All(ms => KitMomentTimeline.Answered(ms, true).Shrink == (ms >= KitMotion.StillFadeMs ? 1 : 0)), Is.True, "no shrinking motion: the receipt's size lands once the card's parts have faded");
        var gone = KitMomentTimeline.Answered(KitMotion.StillFadeMs, true);
        Assert.That((gone.OthersOpacity, gone.KeptOpacity, gone.BodyOpacity, gone.ReceiptOpacity), Is.EqualTo((0.0, 0.0, 0.0, 0.0)), "the parts out before the size lands");
        Assert.That(KitMomentTimeline.Answered(KitMotion.StillFadeMs / 2, true).KeptOpacity, Is.EqualTo(0.5).Within(1e-9), "a fade, not a jump");
        var settled = KitMomentTimeline.Answered(2 * KitMotion.StillFadeMs, true);
        Assert.That((settled.OthersOpacity, settled.KeptOpacity, settled.BodyOpacity, settled.ReceiptOpacity, settled.Lit), Is.EqualTo((0.0, 0.0, 0.0, 1.0, false)));
    }

    [Test]
    public void TheReceiptSaysSentNeverStarted()
    {
        Assert.That((KitMomentWords.Receipt(true), KitMomentWords.Receipt(false)), Is.EqualTo((("Sent", "Awaiting Start"), ("Sent", "Declined"))));
        foreach (var (lead, then) in new[] { KitMomentWords.Receipt(true), KitMomentWords.Receipt(false) })
            Assert.That((lead + then).Contains("Started", StringComparison.OrdinalIgnoreCase), Is.False);
    }
}

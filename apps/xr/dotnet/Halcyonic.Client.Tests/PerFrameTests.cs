using System;
using System.Numerics;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// What the headset calls every frame allocates nothing once warm, so a Quest 3 never collects
/// garbage because time passed: focus presence, the peek's choice, the stage's placement in front
/// of the person, and a section's read once it has its answer.
/// </summary>
public class PerFrameTests
{
    private const int Frames = 1000;

    /// <summary>The bytes <paramref name="frame"/> allocates over <see cref="Frames"/> frames, after two to warm up.</summary>
    private static long AllocatedBy(Action frame)
    {
        frame();
        frame();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < Frames; index++) frame();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Test]
    public void TheMeasureSeesAnAllocation()
    {
        object? kept = null;
        Assert.That(AllocatedBy(() => kept = new byte[64]), Is.GreaterThan(Frames * 64L), "else a zero below proves nothing");
        Assert.That(kept, Is.Not.Null);
    }

    [Test]
    public void FocusPresenceTicksWithoutAllocating()
    {
        var presence = new FocusPresence(TimeSpan.Zero);
        var milliseconds = 0;
        // Away and back, folding and restoring, as the frames pass.
        Assert.That(AllocatedBy(() =>
        {
            milliseconds += 14;
            if (milliseconds % 7000 == 14) presence.Report(milliseconds % 14000 != 14, TimeSpan.FromMilliseconds(milliseconds));
            presence.Tick(TimeSpan.FromMilliseconds(milliseconds));
        }), Is.Zero);
    }

    [Test]
    public void ThePeeksChoiceAllocatesNothingAFrame()
    {
        var choice = new PeekChoice();
        var now = 0f;
        var frame = 0;
        Assert.That(AllocatedBy(() =>
        {
            now += 0.014f;
            frame++;
            // The gaze rests on one character, moves to another, then the hand points at a third.
            var input = new PeekInput
            {
                Gazed = frame % 600 < 300 ? "w1" : "w2",
                Pointed = frame % 900 > 800 ? "w3" : null,
                HandOnTarget = frame % 900 > 800,
                GazedOffCenter = 2f,
                HeadForward = Vector3.UnitZ,
            };
            choice.Update(input, now);
        }), Is.Zero);
    }

    [Test]
    public void ThePlacementInFrontAllocatesNothingAFrame()
    {
        var placement = new InFrontPlacement();
        var now = 0f;
        Assert.That(AllocatedBy(() =>
        {
            now += 0.014f;
            placement.Poll(now, 0.014f, new HeadSample(new Vector3(0f, 1.2f, 0f), MathF.Sin(now) * 10f), headTracked: true);
        }), Is.Zero);
    }

    [Test]
    public void ASectionWithItsAnswerAllocatesNothingAFrame()
    {
        var answer = Task.FromResult(new IntelligenceRead<UnderstandingResponse>(
            new UnderstandingResponse { ExecutionId = "e1", Result = new UnavailableUnderstanding() }, DateTimeOffset.UnixEpoch, recorded: false));
        var feed = new IntelligenceFeed<UnderstandingResponse>((_, _) => answer);
        var now = DateTimeOffset.UnixEpoch;
        feed.Show("e1", "mark", now, TimeSpan.FromSeconds(2));
        feed.Poll();
        Assert.That(feed.Last, Is.Not.Null);
        // Shown every frame, as WorkspaceSections does, for an execution that does not change.
        Assert.That(AllocatedBy(() =>
        {
            now = now.AddMilliseconds(14);
            feed.Show("e1", "mark", now, TimeSpan.FromSeconds(2));
            feed.Poll();
        }), Is.Zero);
    }
}

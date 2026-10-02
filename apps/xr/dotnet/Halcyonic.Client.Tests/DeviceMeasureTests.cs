using System;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// What the headset logs about itself for a device session: the frame rate over each minute and the
/// field of view of the device in hand, in numbers only.
/// </summary>
public class DeviceMeasureTests
{
    [Test]
    public void AMinuteAt72HertzReadsAs72FramesASecond()
    {
        var tally = new FrameTally { RefreshHertz = 72 };
        var complete = false;
        for (var frame = 0; frame < 72 * 60 + 2 && !complete; frame++) complete = tally.Add(1.0 / 72);
        Assert.That(complete, Is.True);
        Assert.That(tally.Frames, Is.EqualTo(72 * 60).Within(1));
        Assert.That(tally.PerSecond, Is.EqualTo(72).Within(0.01));
        Assert.That(tally.BelowSixty, Is.Zero);
        Assert.That(tally.Missed, Is.Zero);
        Assert.That(tally.Line(), Does.StartWith("frames 432").And.Contains(" in 60.0 s, 72.0 a second at 72 Hz, slowest 13.9 ms, 0 below 60, 0 missed"));
    }

    [Test]
    public void SlowFramesCountBelowSixtyAndAsMissed()
    {
        var tally = new FrameTally(1.0) { RefreshHertz = 72 };
        tally.Add(1.0 / 72);
        tally.Add(0.016);  // late for 72 Hz, still above 60 frames a second
        tally.Add(0.020);  // below 60
        Assert.That(tally.BelowSixty, Is.EqualTo(1));
        Assert.That(tally.Missed, Is.EqualTo(2));
        Assert.That(tally.Slowest, Is.EqualTo(0.020).Within(1e-9));
        Assert.That(tally.Line(), Does.Contain("slowest 20.0 ms, 1 below 60, 2 missed"));
    }

    [Test]
    public void APauseEndsTheStretchWithoutCounting()
    {
        var tally = new FrameTally();
        Assert.That(tally.Add(5.0), Is.False, "a pause before any frame is not a stretch");
        Assert.That(tally.Interrupted, Is.False);
        tally.Add(0.014);
        Assert.That(tally.Add(30.0), Is.True, "the headset slept");
        Assert.That(tally.Frames, Is.EqualTo(1));
        Assert.That(tally.Line(), Does.EndWith(", ended by a pause"));
        tally.Reset();
        Assert.That(tally.Frames, Is.Zero);
        Assert.That(tally.Interrupted, Is.False);
        Assert.That(tally.Line(), Does.Not.Contain("pause"));
    }

    [Test]
    public void WithoutARefreshRateNothingCountsAsMissed()
    {
        var tally = new FrameTally();
        tally.Add(0.05);
        Assert.That(tally.Missed, Is.Zero);
        Assert.That(tally.Line(), Does.Not.Contain("Hz").And.Not.Contain("missed"));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new FrameTally(0));
    }

    [Test]
    public void TheLineHasNothingButNumbersAndFixedWords()
    {
        var tally = new FrameTally { RefreshHertz = 90 };
        tally.Add(0.011);
        var words = tally.Line().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            Assert.That(
                double.TryParse(word, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)
                || new[] { "frames", "in", "s", "a", "second", "at", "Hz", "slowest", "ms", "below", "60", "missed" }.Contains(word),
                Is.True, word);
        }
    }

    [Test]
    public void AFieldIsReadFromAnAsymmetricProjection()
    {
        // Edges at unit distance: left tan 50°, right tan 40°, up tan 45°, down tan 52°.
        double l = -Math.Tan(50 * Math.PI / 180), r = Math.Tan(40 * Math.PI / 180);
        double b = -Math.Tan(52 * Math.PI / 180), t = Math.Tan(45 * Math.PI / 180);
        var field = ViewField.FromProjection(2 / (r - l), (r + l) / (r - l), 2 / (t - b), (t + b) / (t - b));
        Assert.That(field.Left, Is.EqualTo(50).Within(1e-9));
        Assert.That(field.Right, Is.EqualTo(40).Within(1e-9));
        Assert.That(field.Up, Is.EqualTo(45).Within(1e-9));
        Assert.That(field.Down, Is.EqualTo(52).Within(1e-9));
        Assert.That(field.Line(), Is.EqualTo("left 50.0 right 40.0 up 45.0 down 52.0"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ViewField.FromProjection(0, 0, 1, 0));
    }

    [Test]
    public void BothEyesSpanFromTheLeftEyesLeftToTheRightEyesRight()
    {
        var both = ViewField.Both(new ViewField(52, 40, 46, 50), new ViewField(40, 52, 47, 49));
        Assert.That(both.Left, Is.EqualTo(52));
        Assert.That(both.Right, Is.EqualTo(52));
        Assert.That(both.Up, Is.EqualTo(46));
        Assert.That(both.Down, Is.EqualTo(49));
        Assert.That(both.Across, Is.EqualTo(104));
        Assert.That(both.Tall, Is.EqualTo(95));
    }

    [Test]
    public void ADirectionIsShownOnlyInsideTheField()
    {
        // A Quest 3S's published 96 by 90 degrees, split evenly about forward for the sake of the test.
        var narrow = new ViewField(48, 48, 45, 45);
        Assert.That(narrow.Shows(0, 0), Is.True);
        Assert.That(narrow.Shows(47, 0), Is.True);
        Assert.That(narrow.Shows(49, 0), Is.False);
        Assert.That(narrow.Shows(0, -44), Is.True);
        Assert.That(narrow.Shows(0, -46), Is.False);
        // Off to the side, a flat field reaches less far down: 45 degrees ahead is about 41 at 30 to the side.
        Assert.That(narrow.Shows(30, -40), Is.True);
        Assert.That(narrow.Shows(30, -42), Is.False);
        Assert.That(narrow.Shows(-120, 0), Is.False);
    }
}

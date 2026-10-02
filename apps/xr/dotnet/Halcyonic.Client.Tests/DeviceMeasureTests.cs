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

    [Test]
    public void WithoutAMeasuredFieldTheLayoutKeepsItsOwnAngles()
    {
        var panel = new PanelSize(0.46f, 0.186f, 0.106f);
        Assert.That(WorkspacePlacement.Lowest(panel, null), Is.EqualTo(WorkspacePlacement.LowestDegrees));
        Assert.That(ViewField.BelowWithin(44.5f, 24f, 4.5f, null, 25f), Is.EqualTo(44.5f));
    }

    /// <summary>The corners of a plate facing the eyes, its center at an elevation, as yaw and elevation from a level head.</summary>
    private static (double Yaw, double Up)[] CornersOf(double elevation, double halfWidth, double halfHeight)
    {
        double e = elevation * Math.PI / 180, across = Math.Tan(halfWidth * Math.PI / 180), tall = Math.Tan(halfHeight * Math.PI / 180);
        return new[] { -1.0, 1.0 }.SelectMany(x => new[] { -1.0, 1.0 }.Select(y =>
        {
            double px = x * across, py = Math.Sin(e) + y * tall * Math.Cos(e), pz = Math.Cos(e) - y * tall * Math.Sin(e);
            return (Math.Atan2(px, pz) * 180 / Math.PI, Math.Atan2(py, Math.Sqrt(px * px + pz * pz)) * 180 / Math.PI);
        })).ToArray();
    }

    [Test]
    public void APlatesLowestCenterPutsItsLowerCornersOnTheMargin()
    {
        var quest3S = new ViewField(48, 48, 45, 45);
        var margin = ViewField.EdgeMarginDegrees;
        var inside = new ViewField(48 - margin, 48 - margin, 45 - margin, 45 - margin);
        var lowest = quest3S.LowestCenter(22f, 13f);
        Assert.That(CornersOf(lowest + 0.01, 22, 13).All(corner => inside.Shows(corner.Yaw, corner.Up)), Is.True, "inside at its lowest");
        Assert.That(CornersOf(lowest - 0.1, 22, 13).All(corner => inside.Shows(corner.Yaw, corner.Up)), Is.False, "outside any lower");
        // Looked at with the head turned toward it, its lower corners lie on the field's edge with its lower edge's middle.
        Assert.That(lowest - 13, Is.EqualTo(-(45 - margin)).Within(1e-3));
        Assert.That(new ViewField(10, 10, 10, 10).LowestCenter(22f, 13f), Is.Zero, "too narrow to hold it at all");
    }

    [Test]
    public void ANarrowFieldLiftsThePanelsLowestCenter()
    {
        var panel = new PanelSize(0.46f, 0.186f, 0.106f);  // about 44 by 26 degrees
        var quest3S = new ViewField(48, 48, 45, 45);
        var lowest = WorkspacePlacement.Lowest(panel, quest3S);
        Assert.That(lowest, Is.EqualTo(quest3S.LowestCenter(panel.HalfWidthDegrees, panel.HalfHeightDegrees)).Within(1e-4));
        Assert.That(lowest, Is.GreaterThan(WorkspacePlacement.LowestDegrees));
        Assert.That(WorkspacePlacement.Lowest(panel, new ViewField(55, 55, 55, 70)), Is.EqualTo(WorkspacePlacement.LowestDegrees), "a deep field never lowers it");
        Assert.That(WorkspacePlacement.Lowest(panel, new ViewField(30, 30, 20, 20)), Is.LessThanOrEqualTo(WorkspacePlacement.HighestDegrees));
    }

    [Test]
    public void TheFieldPrefersTheSideInsideItButNeverPushesAPanelIntoALabel()
    {
        var panel = new PanelSize(0.46f, 0.186f, 0.106f);
        var narrow = new ViewField(48, 48, 45, 40);
        // A character just under where a seated person looks: room below its label and above its body.
        var level = new BodyInView(0f, -14f, 1f, lowest: -15f);
        var wide = WorkspacePlacement.Place(0f, level, new[] { level }, panel);
        Assert.That(wide.Above, Is.False, "below, nearer where a seated person looks");
        var inField = WorkspacePlacement.Place(0f, level, new[] { level }, panel, field: narrow);
        Assert.That(inField.Above, Is.True, "below would leave the field, so above");
        // Far characters a little below the eyes: only below fits, and stays under the label even
        // though the field would want it higher.
        var far = new BodyInView(0f, -4f, 2f, lowest: -15f);
        var under = WorkspacePlacement.Place(0f, far, new[] { far }, panel, field: narrow);
        Assert.That(under.Clear, Is.True);
        Assert.That(under.Elevation + panel.HalfHeightDegrees, Is.LessThanOrEqualTo(-15f - WorkspacePlacement.LabelClearanceDegrees + 1e-3f));
    }

    [Test]
    public void ANarrowFieldLiftsTheRailButNeverIntoWhatStandsAboveIt()
    {
        var even3S = new ViewField(48, 48, 45, 45);
        var lifted = ViewField.BelowWithin(44.5f, 24f, 4.5f, even3S, 25f);
        Assert.That(lifted, Is.EqualTo(-even3S.LowestCenter(24f, 4.5f)).Within(1e-4));
        Assert.That(lifted, Is.LessThan(44.5f).And.GreaterThan(25f));
        Assert.That(ViewField.BelowWithin(44.5f, 24f, 4.5f, new ViewField(55, 55, 50, 70), 25f), Is.EqualTo(44.5f), "a deep field keeps the rail where it is");
        Assert.That(ViewField.BelowWithin(44.5f, 24f, 4.5f, new ViewField(40, 40, 30, 20), 25f), Is.EqualTo(25f), "never above its highest");
    }

    [Test]
    public void SettingTheFieldCountsAVersion()
    {
        var before = ViewField.Version;
        var kept = ViewField.Current;
        try
        {
            ViewField.Current = new ViewField(48, 48, 45, 45);
            Assert.That(ViewField.Version, Is.EqualTo(before + 1));
            Assert.That(ViewField.Current?.Down, Is.EqualTo(45));
        }
        finally
        {
            ViewField.Current = kept;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>Drives a <see cref="PeekChoice"/> at 72 frames a second, as the headset renders.</summary>
internal sealed class PeekFrames
{
    public const float Frame = 1f / 72f;

    public PeekChoice Choice { get; } = new();

    public float Now { get; private set; }

    /// <summary>Where the head faces, as a yaw in degrees on the level.</summary>
    public float HeadYaw { get; set; }

    public PeekInput Input;

    /// <summary>Every character shown in any frame, in order, with how visible it was.</summary>
    public List<(float Time, string? Shown, float Opacity)> Seen { get; } = new();

    /// <summary>Runs frames for a while, turning the head at a steady speed.</summary>
    public void Run(float seconds, float degreesPerSecond = 0f)
    {
        var frames = (int)MathF.Round(seconds / Frame);
        for (var index = 0; index < frames; index++) Step(degreesPerSecond);
    }

    public void Step(float degreesPerSecond = 0f, float frame = Frame)
    {
        Now += frame;
        HeadYaw += degreesPerSecond * frame;
        var radians = HeadYaw * MathF.PI / 180f;
        Input.HeadForward = new Vector3(MathF.Sin(radians), -0.18f, MathF.Cos(radians));
        Choice.Update(Input, Now);
        Seen.Add((Now, Choice.Shown, Choice.Opacity));
    }
}

public class PeekChoiceTests
{
    private static PeekFrames Resting(string gazed, float offCenter = 1f)
    {
        var frames = new PeekFrames();
        frames.Input.Gazed = gazed;
        frames.Input.GazedOffCenter = offCenter;
        return frames;
    }

    [Test]
    public void TheGazePeeksOnlyAfterRestingOnACharacterForHalfASecond()
    {
        var frames = Resting("a");

        frames.Run(0.45f);
        Assert.That(frames.Choice.Shown, Is.Null, "not yet: the gaze has rested less than the dwell");

        frames.Run(0.1f);
        Assert.That(frames.Choice.Shown, Is.EqualTo("a"));
        Assert.That(frames.Choice.Source, Is.EqualTo(PeekSource.Gaze));
        Assert.That(frames.Choice.Opacity, Is.LessThan(1f), "it fades in");

        frames.Run(PeekChoice.FadeInSeconds);
        Assert.That(frames.Choice.Opacity, Is.EqualTo(1f));
    }

    [Test]
    public void TurningTheHeadAcrossTheStagePeeksNothing()
    {
        // Six characters 12 degrees apart; the head sweeps across them at 60 degrees a second, so
        // each stays under the gaze for about a fifth of a second longer than the gaze interactor's
        // own dwell.
        var frames = new PeekFrames { HeadYaw = -36f };
        foreach (var (characters, speed) in new[] { ("abcdef", 60f), ("fedcba", -60f) })
        {
            foreach (var character in characters)
            {
                frames.Input.Gazed = character.ToString();
                frames.Run(0.2f, speed);
                frames.Input.Gazed = null;
                frames.Run(0.05f, speed);
            }
        }

        Assert.That(frames.Seen.TrueForAll(frame => frame.Shown == null), Is.True, "no peek while the head turns");
    }

    [Test]
    public void AGazeThatLingersWhileTheHeadStillTurnsWaitsForTheHeadToSettle()
    {
        var frames = Resting("a");

        frames.Run(1f, degreesPerSecond: 30f);
        Assert.That(frames.Choice.Shown, Is.Null, "a slow turn is still a turn");
        Assert.That(frames.Choice.HeadSpeed, Is.EqualTo(30f).Within(1f));

        frames.Run(0.45f);
        Assert.That(frames.Choice.Shown, Is.Null, "the dwell starts when the head slows");
        frames.Run(0.25f);
        Assert.That(frames.Choice.Shown, Is.EqualTo("a"));
    }

    [Test]
    public void OnlyACharacterNearTheMiddleOfTheViewPeeks()
    {
        var aside = Resting("a", offCenter: PeekChoice.CenterDegrees + 2f);
        aside.Run(2f);
        Assert.That(aside.Choice.Shown, Is.Null);

        var middle = Resting("a", offCenter: PeekChoice.CenterDegrees - 2f);
        middle.Run(PeekChoice.DwellSeconds + 0.1f);
        Assert.That(middle.Choice.Shown, Is.EqualTo("a"));

        // Once showing, it stays while the character is a little farther out, and goes beyond that.
        middle.Input.GazedOffCenter = PeekChoice.KeepDegrees - 1f;
        middle.Run(1f);
        Assert.That(middle.Choice.Wanted, Is.EqualTo("a"));
        middle.Input.GazedOffCenter = PeekChoice.KeepDegrees + 1f;
        middle.Run(PeekChoice.LingerSeconds + 0.05f);
        Assert.That(middle.Choice.Wanted, Is.Null);
    }

    [Test]
    public void AGlanceAsideKeepsThePeekAndLookingAwayFadesIt()
    {
        var frames = Resting("a");
        frames.Run(1f);
        Assert.That(frames.Choice.Opacity, Is.EqualTo(1f));

        frames.Input.Gazed = null;
        frames.Run(PeekChoice.LingerSeconds - 0.1f);
        frames.Input.Gazed = "a";
        frames.Run(0.2f);
        Assert.That(frames.Choice.Shown, Is.EqualTo("a"));
        Assert.That(frames.Choice.Opacity, Is.EqualTo(1f), "a glance aside does not flicker it");

        frames.Input.Gazed = null;
        frames.Run(PeekChoice.LingerSeconds + 0.05f);
        Assert.That(frames.Choice.Wanted, Is.Null);
        frames.Run(PeekChoice.FadeOutSeconds);
        Assert.That(frames.Choice.Shown, Is.Null, "it faded out");
    }

    [Test]
    public void AHandPeeksAtOnceAndWinsOverTheGaze()
    {
        var frames = new PeekFrames();
        frames.Input.Pointed = "b";
        frames.Step();
        Assert.That(frames.Choice.Shown, Is.EqualTo("b"), "pointing needs no dwell");
        Assert.That(frames.Choice.Source, Is.EqualTo(PeekSource.Hand));

        frames.Input.Gazed = "a";
        frames.Run(2f);
        Assert.That(frames.Choice.Shown, Is.EqualTo("b"), "the hand wins while it points");
    }

    [Test]
    public void OnePeekShowsAtATimeTheLastFadingOutBeforeTheNextFadesIn()
    {
        var frames = Resting("a");
        frames.Run(1f);
        var switched = frames.Seen.Count;

        frames.Input.Pointed = "b";
        frames.Run(1f);

        var after = frames.Seen.GetRange(switched, frames.Seen.Count - switched);
        var firstB = after.FindIndex(frame => frame.Shown == "b");
        Assert.That(firstB, Is.GreaterThan(0), "a is still fading out first");
        Assert.That(after.GetRange(0, firstB).TrueForAll(frame => frame.Shown == "a"), Is.True);
        Assert.That(after[firstB - 1].Opacity, Is.LessThan(0.2f), "a had nearly faded out");
        Assert.That(after[firstB].Opacity, Is.LessThan(0.2f), "b starts from nothing");
        Assert.That(frames.Choice.Opacity, Is.EqualTo(1f));
    }

    [Test]
    public void AnOpenWorkspaceHidesItsCharactersPeekAndStopsTheGazePeeking()
    {
        var frames = Resting("a");
        frames.Run(1f);
        Assert.That(frames.Choice.Shown, Is.EqualTo("a"));

        frames.Input.Open = "a";
        frames.Run(0.5f);
        Assert.That(frames.Choice.Shown, Is.Null, "its peek went when its workspace opened");

        frames.Input.Pointed = "a";
        frames.Run(0.5f);
        Assert.That(frames.Choice.Shown, Is.Null, "pointing at the open character peeks nothing");

        frames.Input.Pointed = null;
        frames.Input.Gazed = "b";
        frames.Run(2f);
        Assert.That(frames.Choice.Shown, Is.Null, "reading the workspace brings up no peeks");

        frames.Input.Pointed = "b";
        frames.Run(0.5f);
        Assert.That(frames.Choice.Shown, Is.EqualTo("b"), "a hand still peeks at the others");
        Assert.That(frames.Choice.PinchTarget, Is.Null);
    }

    [Test]
    public void LosingFocusFadesThePeekAndPinchesNothing()
    {
        var frames = Resting("a");
        frames.Run(1f);
        Assert.That(frames.Choice.PinchTarget, Is.EqualTo("a"));

        frames.Input.Suspended = true;
        frames.Step();
        Assert.That(frames.Choice.PinchTarget, Is.Null);
        frames.Run(PeekChoice.FadeOutSeconds + 0.05f);
        Assert.That(frames.Choice.Shown, Is.Null);

        frames.Input.Suspended = false;
        frames.Run(PeekChoice.DwellSeconds - 0.1f);
        Assert.That(frames.Choice.Shown, Is.Null, "the dwell starts again when focus returns");
    }

    [Test]
    public void ALookAndPinchOpensOnlyAShowingGazePeekWithNoHandOnATarget()
    {
        var frames = Resting("a");
        frames.Run(PeekChoice.DwellSeconds + 0.05f);
        Assert.That(frames.Choice.Shown, Is.EqualTo("a"));
        Assert.That(frames.Choice.PinchTarget, Is.Null, "not until the peek is showing");

        frames.Run(PeekChoice.FadeInSeconds);
        Assert.That(frames.Choice.PinchTarget, Is.EqualTo("a"));

        frames.Input.HandOnTarget = true;
        frames.Step();
        Assert.That(frames.Choice.PinchTarget, Is.Null, "a hand ray on a button or a character pinches that instead");

        frames.Input.HandOnTarget = false;
        frames.Input.Pointed = "b";
        frames.Step();
        Assert.That(frames.Choice.PinchTarget, Is.Null, "a hand peek is the ray's to open");
    }

    [Test]
    public void ALongFrameStartsTheDwellAgain()
    {
        var frames = Resting("a");
        frames.Run(0.4f);
        frames.Step(frame: 0.6f);
        frames.Run(0.2f);
        Assert.That(frames.Choice.Shown, Is.Null, "a long frame says nothing about whether the head stayed");
        frames.Run(0.5f);
        Assert.That(frames.Choice.Shown, Is.EqualTo("a"));
    }
}

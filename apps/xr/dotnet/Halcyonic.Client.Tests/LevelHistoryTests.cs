using System;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The bar's level is the microphone's real input (the lead's truth rule, 2026-11-05): each bar is the RMS of what was
/// recorded in its 50 ms, newest at the right, and silence stays at the floor.
/// </summary>
public class LevelHistoryTests
{
    private static float[] Tone(float amplitude, int length = 800) =>
        Enumerable.Range(0, length).Select(index => amplitude * (float)Math.Sin(index * 0.3)).ToArray();

    [Test]
    public void TheBarsFollowTheInputNewestAtTheRight()
    {
        var history = new LevelHistory();
        Assert.That(Enumerable.Range(0, LevelHistory.Bars).Select(bar => history[bar]), Has.All.EqualTo(0f), "nothing recorded: no level");

        var quiet = Tone(0.01f);
        var loud = Tone(0.2f);
        history.Add(quiet, quiet.Length);
        history.Add(loud, loud.Length);
        Assert.That(history[LevelHistory.Bars - 1], Is.GreaterThan(history[LevelHistory.Bars - 2]), "the louder 50 ms stands higher, at the right");
        Assert.That(history[LevelHistory.Bars - 2], Is.GreaterThan(0f));
        Assert.That(history[0], Is.EqualTo(0f), "older than what was recorded: the floor");

        var silence = new float[800];
        history.Add(silence, silence.Length);
        Assert.That((history[LevelHistory.Bars - 1], history[LevelHistory.Bars - 2]), Is.EqualTo((0f, LevelHistory.Level(loud, loud.Length))), "each level moves one bar left");
    }

    [Test]
    public void LevelsRunFromTheFloorToTheTopAndRollAfterTwentySixSteps()
    {
        Assert.That(LevelHistory.Level(Tone(1f), 800), Is.EqualTo(1f));
        Assert.That(LevelHistory.Level(Tone(0.0001f), 800), Is.EqualTo(0f));
        Assert.That(LevelHistory.Level(new float[0], 0), Is.EqualTo(0f));

        var history = new LevelHistory();
        var loud = Tone(0.2f);
        for (var step = 0; step < LevelHistory.Bars + 5; step++) history.Add(loud, loud.Length);
        Assert.That(history.Count, Is.EqualTo(LevelHistory.Bars));
        Assert.That(history[0], Is.EqualTo(history[LevelHistory.Bars - 1]));
        history.Clear();
        Assert.That((history.Count, history[LevelHistory.Bars - 1]), Is.EqualTo((0, 0f)));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// What a judge sees in the first 60 seconds of the recorded demonstration, with hands in their lap,
/// as it opens closed (nothing opens by itself): the closed bar's words and each character's state
/// word, instant by instant at the recorded pace. The lines above the stage are the owner's words,
/// held by DemonstrationTests. The testing instructions and the video storyboard describe exactly
/// this (docs/internal/validation/competition-judge-build.md): three tasks not started, the two
/// watched ones finishing, then the directed one alone waiting for the judge, and nothing else for
/// the rest of the minute, since the recording holds at its question until it is answered.
/// </summary>
public class JudgeFirstMinuteTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromSeconds(60);

    /// <summary>One instant of the first minute: the closed bar's words and each task's state word, by title.</summary>
    private sealed record Moment(TimeSpan At, string Bar, IReadOnlyDictionary<string, string> Words)
    {
        public override string ToString() =>
            $"{At.TotalSeconds,5:0.0} s  {Bar}  |  " + string.Join("; ", Words.OrderBy(pair => pair.Key).Select(pair => pair.Key + ": " + pair.Value));
    }

    [Test]
    public void TheFirstMinuteShowsThreeTasksTheWatchedOnesFinishingThenTheDirectedOneAloneWaiting()
    {
        var recording = Demonstration.Recording();
        var moments = FirstMinute(recording);
        foreach (var moment in moments) TestContext.Out.WriteLine(moment);

        // As the stage appears: three tasks, none started, nothing waiting.
        var first = moments[0];
        Assert.That(first.At, Is.EqualTo(TimeSpan.Zero));
        Assert.That(first.Words.Values, Has.Count.EqualTo(3).And.All.EqualTo("Not started"));
        Assert.That(first.Bar, Is.EqualTo(TasksText.Waiting(0)));

        // The first instant anything waits: the directed task alone, within ten seconds, the two watched ones finished.
        var waits = moments.First(moment => moment.Bar != TasksText.Waiting(0));
        Assert.That(waits.At, Is.LessThanOrEqualTo(TimeSpan.FromSeconds(10)), "the first decision comes within ten seconds");
        Assert.That(waits.Bar, Is.EqualTo(TasksText.Waiting(1)));
        Assert.That(waits.Words[Demonstration.Directed], Is.EqualTo("Waiting for you"));
        Assert.That(waits.Words.Where(pair => pair.Key != Demonstration.Directed).Select(pair => pair.Value),
            Is.All.EqualTo("Finished this round"), "the watched tasks have finished their round by then");
        Assert.That(moments.TakeWhile(moment => moment != waits).Select(moment => moment.Bar), Is.All.EqualTo(TasksText.Waiting(0)),
            "nothing waits before the directed task");

        // From then to the minute's end nothing changes: the recording holds at its question.
        var beginning = recording.Nodes[0];
        Assert.That(beginning.Hold, Is.Null, "the beginning holds until an answer, never starting again by itself");
        Assert.That(beginning.Events.Last().At, Is.LessThan(Minute));
        Assert.That(moments.SkipWhile(moment => moment != waits).Skip(1)
            .Where(moment => moment.Bar != waits.Bar || !moment.Words.SequenceEqual(waits.Words)), Is.Empty,
            "after the directed task comes to wait, nothing on the stage or the bar changes for the rest of the minute");
    }

    /// <summary>
    /// The beginning's events applied in recorded order, one moment for each instant, and one more at
    /// the minute's end: the bar as the closed menu shows it and each task's state word.
    /// </summary>
    private static List<Moment> FirstMinute(DemonstrationRecording recording)
    {
        var state = new ClientProjection();
        state.ApplyWelcome(recording.Welcome);
        state.ApplySnapshot(recording.Snapshot.Snapshot, new StateChanges());
        var moments = new List<Moment> { Now(TimeSpan.Zero, state) };
        var events = recording.Nodes[0].Events;
        for (var i = 0; i < events.Count && events[i].At < Minute; i++)
        {
            state.ApplyEvent(events[i].Message, new StateChanges());
            if (i + 1 == events.Count || events[i + 1].At != events[i].At) moments.Add(Now(events[i].At, state));
        }
        moments.Add(Now(Minute, state));
        return moments;
    }

    private static Moment Now(TimeSpan at, ClientProjection state) => new(
        at,
        TasksColumn.Bar(MenuPlace.Tasks, state).ClosedLine,
        state.Workstreams.Values.ToDictionary(task => task.Title, task => CharacterPresenter.Present(task, state, live: true).StatusLabel));
}

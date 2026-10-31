using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// Characters meet only over a link the journal holds as observed for each task (the art direction's character handoff,
/// 2026-10-30, section 4; AUDIT C3): the same project, or an observed change of the same file attributed to each task;
/// never two tasks the computer cannot tell apart, never while either needs the person or the computer is unreachable, one
/// meeting in the room at a time, a pair at most once every 2 minutes. Both tags carry the same true line; nothing joins them.
/// </summary>
public class CharacterMeetingTests
{
    private const double Frame = 1.0 / 60;
    private static readonly string[] None = Array.Empty<string>();

    private static CharacterMeetingTask Task(string id, string project, double x, WorkState state = WorkState.Working, string[]? files = null,
        bool attributed = true, bool present = true) =>
        new CharacterMeetingTask(id, project, files ?? None, attributed, state, present, x, 0);

    [Test]
    public void TheSameProjectIsALink()
    {
        var meetings = new CharacterMeetings();
        meetings.Update(new[] { Task("a", "p1", -0.3), Task("b", "p1", 0.3) }, true, 0);
        Assert.That(meetings.Current, Is.Not.Null);
        Assert.That(meetings.PartOf("a", 0)!.Value.Line, Is.EqualTo("Same Project"));
        Assert.That(meetings.PartOf("b", 0)!.Value.Line, Is.EqualTo("Same Project"), "the same line on both tags");
    }

    [Test]
    public void AnObservedChangeOfTheSameFileIsALinkAndSaysWhichFile()
    {
        var a = Task("a", "p1", -0.3, files: new[] { "src/towers.ts", "src/a.ts" });
        var b = Task("b", "p2", 0.3, files: new[] { "src/towers.ts" });
        Assert.That(CharacterMeetings.LinkOf(a, b), Is.EqualTo("Shared · src/towers.ts"));
        var sameProject = Task("c", "p1", 0.3, files: new[] { "src/towers.ts" });
        Assert.That(CharacterMeetings.LinkOf(a, sameProject), Is.EqualTo("Shared · src/towers.ts"), "the file says more than the project");
    }

    [Test]
    public void TasksTheComputerCannotTellApartHaveNoFileLink()
    {
        var a = Task("a", "p1", -0.3, files: new[] { "src/towers.ts" }, attributed: false);
        var b = Task("b", "p2", 0.3, files: new[] { "src/towers.ts" });
        Assert.That(CharacterMeetings.LinkOf(a, b), Is.Null);
        Assert.That(CharacterMeetings.LinkOf(Task("a", "p1", 0, files: new[] { "x.ts" }), Task("b", "p2", 0, files: new[] { "y.ts" })), Is.Null);
    }

    [TestCase(WorkState.WaitingForYou, true)]
    [TestCase(WorkState.Working, false)]
    public void NeverWhileEitherNeedsYouOrTheComputerIsUnreachable(WorkState state, bool reachable)
    {
        var meetings = new CharacterMeetings();
        meetings.Update(new[] { Task("a", "p1", -0.3, state), Task("b", "p1", 0.3) }, reachable, 0);
        Assert.That(meetings.Current, Is.Null);
    }

    [Test]
    public void AFoldedOrPutAwayCharacterDoesNotMeet()
    {
        var meetings = new CharacterMeetings();
        meetings.Update(new[] { Task("a", "p1", -0.3, present: false), Task("b", "p1", 0.3) }, true, 0);
        Assert.That(meetings.Current, Is.Null);
    }

    [Test]
    public void NeedingYouMidMeetingSendsBothHome()
    {
        var meetings = new CharacterMeetings();
        meetings.Update(new[] { Task("a", "p1", -0.3), Task("b", "p1", 0.3) }, true, 0);
        meetings.Update(new[] { Task("a", "p1", -0.3, WorkState.WaitingForYou), Task("b", "p1", 0.3) }, true, 3);
        var a = meetings.PartOf("a", 3);
        Assert.That(a == null || (a.Value.Line == null && !a.Value.Facing), Is.True, "the line goes, the facing ends");
        for (var t = 3.0; t < 8; t += Frame) meetings.Update(new[] { Task("a", "p1", -0.3, WorkState.WaitingForYou), Task("b", "p1", 0.3) }, true, t);
        Assert.That(meetings.Current, Is.Null);
        Assert.That(meetings.PartOf("a", 8), Is.Null);
    }

    [Test]
    public void OneMeetingInTheRoomAtATime()
    {
        var meetings = new CharacterMeetings();
        var tasks = new[] { Task("a", "p1", -0.6), Task("b", "p1", 0), Task("c", "p1", 0.6), Task("d", "p1", 1.2) };
        meetings.Update(tasks, true, 0);
        Assert.That(tasks.Count(t => meetings.PartOf(t.Id, 0) != null), Is.EqualTo(2));
    }

    [Test]
    public void APairMeetsAtMostOnceEveryTwoMinutes()
    {
        var meetings = new CharacterMeetings();
        var tasks = new[] { Task("a", "p1", -0.3), Task("b", "p1", 0.3) };
        var starts = new List<double>();
        for (var t = 0.0; t < 300; t += 0.25)
        {
            var before = meetings.Current;
            meetings.Update(tasks, true, t);
            if (meetings.Current != null && !ReferenceEquals(before, meetings.Current)) starts.Add(t);
        }
        Assert.That(starts.Count, Is.EqualTo(3));
        for (var i = 1; i < starts.Count; i++) Assert.That(starts[i] - starts[i - 1], Is.GreaterThanOrEqualTo(CharacterMeetings.PairEverySeconds));
    }

    [Test]
    public void TheChoreography()
    {
        var meetings = new CharacterMeetings();
        var tasks = new[] { Task("a", "p1", -0.3), Task("b", "p1", 0.3) };
        meetings.Update(tasks, true, 0);
        var arrive = CharacterLife.MoveSeconds(0.16);
        Assert.That(arrive, Is.EqualTo(2.0).Within(1e-9));

        var a = meetings.PartOf("a", arrive)!.Value;
        var b = meetings.PartOf("b", arrive)!.Value;
        Assert.That(a.XM, Is.EqualTo(0.16).Within(1e-9), "one body width apart, 0.14 m each side of the midpoint");
        Assert.That(b.XM, Is.EqualTo(-0.16).Within(1e-9));
        Assert.That((a.TurnDegrees, b.TurnDegrees), Is.EqualTo((45.0, -45.0)), "halfway between each other and the person");
        Assert.That((a.LeanDegrees, b.LeanDegrees), Is.EqualTo((-4.0, -4.0)), "leaning in");
        Assert.That(meetings.PartOf("a", 1.0)!.Value.Facing, Is.False, "facing only once both have arrived");

        var turns = new[] { arrive + 0.35, arrive + 0.35 + 1.5, arrive + 0.35 + 3.0 };
        var speakers = turns.Select(t => meetings.PartOf("a", t + 0.01)!.Value.Speaking ? "a" : "b").ToArray();
        Assert.That(speakers, Is.EqualTo(new[] { "a", "b", "a" }));
        Assert.That(turns.Select(t => meetings.PartOf("b", t + 0.01)!.Value.Turn), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(meetings.PartOf("a", arrive + 0.2)!.Value.Turn, Is.EqualTo(-1));

        var parts = arrive + 0.35 + 4.5 + 0.5;
        Assert.That(meetings.PartOf("a", parts - 0.01)!.Value.Line, Is.EqualTo("Same Project"));
        var going = meetings.PartOf("a", parts + 0.01)!.Value;
        Assert.That((going.Line, going.Facing), Is.EqualTo(((string?)null, false)), "the line goes as they part");
        meetings.Update(tasks, true, parts + arrive + 0.01);
        Assert.That(meetings.Current, Is.Null, "home again");
    }

    [Test]
    public void ALifeInAMeetingSpeaksOnItsTurnsAndListensOnTheOthers()
    {
        var meetings = new CharacterMeetings();
        var tasks = new[] { Task("a", "p1", -0.3), Task("b", "p1", 0.3) };
        var a = new CharacterLife("a", KitFigureFeature.Antenna);
        var b = new CharacterLife("b", KitFigureFeature.Fins);
        a.Show(WorkState.Working, false, true);
        b.Show(WorkState.Working, false, true);
        var aPoses = new List<CharacterPose>();
        var bPoses = new List<CharacterPose>();
        for (var t = 0.0; t < 10; t += Frame)
        {
            meetings.Update(tasks, true, t);
            aPoses.Add(a.Step(Frame, meetings.PartOf("a", t)));
            bPoses.Add(b.Step(Frame, meetings.PartOf("b", t)));
        }
        Assert.That(a.TakeSounds(), Is.EqualTo(new[] { CharacterSound.Voice, CharacterSound.Voice }), "its voice on its two turns");
        Assert.That(b.TakeSounds(), Is.EqualTo(new[] { CharacterSound.Voice }));
        Assert.That(aPoses.Max(p => p.HopM), Is.EqualTo(KitFigureLook.DpToM(4)).Within(KitFigureLook.DpToM(0.2)), "a 4 dp hop, not the passed check's");
        Assert.That(bPoses.Max(p => p.HeadRollDegrees), Is.GreaterThan(7), "the listener's head roll");
        Assert.That(aPoses.Where(p => p.Gaze == CharacterGaze.Neighbour).Count(), Is.GreaterThan(200), "looking at each other");
        Assert.That(aPoses.Select(p => p.OneShot).Skip(1).Take(400), Has.All.EqualTo(CharacterOneShot.None), "no one-shots in a meeting");
        Assert.That(aPoses[^1].XM, Is.EqualTo(0).Within(1e-9), "home again");
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class SoundCueSelectorTests
{
    private static readonly ConnectionStatus Live = new(ConnectionPhase.Live);
    private readonly Dictionary<string, int> slots = new();
    private ClientProjection state = null!;
    private SoundCueSelector selector = null!;
    private long position;

    [SetUp]
    public void Create()
    {
        state = new ClientProjection();
        selector = new SoundCueSelector();
        slots.Clear();
        position = 0;
    }

    [TestCase(CharacterActivity.Working, CharacterActivity.WaitingForHuman, SoundCue.NeedsYou)]
    [TestCase(CharacterActivity.Verifying, CharacterActivity.WaitingForHuman, SoundCue.NeedsYou)]
    [TestCase(CharacterActivity.Working, CharacterActivity.TurnFinished, SoundCue.TurnFinished)]
    [TestCase(CharacterActivity.Verifying, CharacterActivity.TurnFinished, SoundCue.TurnFinished)]
    [TestCase(CharacterActivity.Working, CharacterActivity.Failed, SoundCue.Failed)]
    [TestCase(CharacterActivity.Starting, CharacterActivity.Failed, SoundCue.Failed)]
    [TestCase(CharacterActivity.Working, CharacterActivity.Unknown, SoundCue.Unknown)]
    [TestCase(CharacterActivity.WaitingForHuman, CharacterActivity.Interrupted, SoundCue.Stopped)]
    [TestCase(CharacterActivity.Working, CharacterActivity.Interrupted, SoundCue.Stopped)]
    [TestCase(CharacterActivity.Working, CharacterActivity.Verifying, SoundCue.Verifying)]
    [TestCase(CharacterActivity.WaitingForHuman, CharacterActivity.Verifying, SoundCue.Verifying)]
    [TestCase(CharacterActivity.Idle, CharacterActivity.Starting, SoundCue.Working)]
    [TestCase(CharacterActivity.Idle, CharacterActivity.Working, SoundCue.Working)]
    [TestCase(CharacterActivity.TurnFinished, CharacterActivity.Working, SoundCue.Working)]
    [TestCase(CharacterActivity.Interrupted, CharacterActivity.Working, SoundCue.Working)]
    [TestCase(CharacterActivity.Failed, CharacterActivity.Working, SoundCue.Working)]
    [TestCase(CharacterActivity.Unknown, CharacterActivity.Working, SoundCue.Working)]
    [TestCase(CharacterActivity.WaitingForHuman, CharacterActivity.Working, SoundCue.Working)]
    public void ArrivingAtAStateThePersonMightActOnOrStartingWorkHasItsCue(CharacterActivity before, CharacterActivity after, SoundCue cue)
    {
        Assert.That(SoundCueSelector.CueFor(before, after), Is.EqualTo(cue));
    }

    [TestCase(CharacterActivity.Starting, CharacterActivity.Working)]
    [TestCase(CharacterActivity.Verifying, CharacterActivity.Working)]
    [TestCase(CharacterActivity.Working, CharacterActivity.Starting)]
    [TestCase(CharacterActivity.Working, CharacterActivity.Working)]
    [TestCase(CharacterActivity.WaitingForHuman, CharacterActivity.WaitingForHuman)]
    [TestCase(CharacterActivity.TurnFinished, CharacterActivity.Idle)]
    [TestCase(CharacterActivity.Idle, CharacterActivity.Idle)]
    public void AStartCompletingATestRunEndingOrNothingChangingIsSilent(CharacterActivity before, CharacterActivity after)
    {
        Assert.That(SoundCueSelector.CueFor(before, after), Is.Null);
    }

    [Test]
    public void AWorkstreamSeenForTheFirstTimeSoundsWhereItArrived()
    {
        foreach (var activity in (CharacterActivity[])Enum.GetValues(typeof(CharacterActivity)))
        {
            Assert.That(SoundCueSelector.CueFor(null, activity), activity == CharacterActivity.Idle ? Is.Null : Is.Not.Null, activity.ToString());
        }
    }

    [Test]
    public void EachCueSoundsFromItsPlace()
    {
        foreach (var cue in GlazeSynthesizer.Cues)
        {
            var expected = cue == SoundCue.LastKnown ? CuePlace.Stage
                : cue >= SoundCue.Open ? CuePlace.Workspace
                : CuePlace.Character;
            Assert.That(SoundCueSelector.PlaceOf(cue), Is.EqualTo(expected), cue.ToString());
        }
    }

    [Test]
    public void AStateChangeSoundsFromItsCharacterOnItsNote()
    {
        Stand(("w1", 4));
        Assert.That(Observe(Snapshot(W("w1", WorkstreamStatus.Running)), 0), Is.Empty);

        var cues = Observe(Event(W("w1", WorkstreamStatus.WaitingForHuman)), 3);

        var cue = cues.Single();
        Assert.That(cue.Cue, Is.EqualTo(SoundCue.NeedsYou));
        Assert.That(cue.WorkstreamId, Is.EqualTo("w1"));
        Assert.That(cue.Bot, Is.EqualTo(4), "the note of the slot it stood in when it appeared");
        Assert.That(cue.Place, Is.EqualTo(CuePlace.Character));
        Assert.That(cue.At, Is.EqualTo(3));
    }

    [Test]
    public void SnapshotsAndResynchronizationsOnlySetWhatLaterChangesAreComparedWith()
    {
        Stand(("w1", 2), ("w2", 3));
        Assert.That(Observe(Snapshot(W("w1", WorkstreamStatus.Running), W("w2", WorkstreamStatus.WaitingForHuman)), 0), Is.Empty,
            "the first state heard is a snapshot, and says nothing about what changed");
        Assert.That(Cues(Observe(Event(W("w1", WorkstreamStatus.Completed)), 1)), Is.EqualTo(new[] { SoundCue.TurnFinished }));

        // Another journal, or the session after a reconnection that could not resume.
        Assert.That(Observe(Snapshot(W("w1", WorkstreamStatus.Failed), W("w2", WorkstreamStatus.Completed)), 20), Is.Empty,
            "everything changed, and none of it sounds");

        Assert.That(Observe(Event(W("w1", WorkstreamStatus.Failed)), 21), Is.Empty, "already failed in the snapshot");
        Assert.That(Cues(Observe(Event(W("w1", WorkstreamStatus.Running)), 22)), Is.EqualTo(new[] { SoundCue.Working }),
            "compared with the snapshot's state");
    }

    [Test]
    public void TheDemonstrationStartingAgainMakesNoSound()
    {
        Stand(("w1", 2));
        var changes = new StateChanges();
        state.ApplySnapshot(Samples.Snapshot(10, new[] { W("w1", WorkstreamStatus.Created) }), changes);
        position = 10;
        Observe(changes, 0);
        Assert.That(Cues(Observe(Event(W("w1", WorkstreamStatus.Running)), 1)), Is.EqualTo(new[] { SoundCue.Working }));
        Assert.That(Cues(Observe(Event(W("w1", WorkstreamStatus.WaitingForHuman)), 2)), Is.EqualTo(new[] { SoundCue.NeedsYou }));

        // The same journal at its beginning again, as the recorded demonstration starts over.
        changes = new StateChanges();
        state.ApplySnapshot(Samples.Snapshot(10, new[] { W("w1", WorkstreamStatus.Created) }), changes);
        position = 10;
        Assert.That(changes.Rewound, Is.True);
        Assert.That(Observe(changes, 30), Is.Empty);

        Assert.That(Cues(Observe(Event(W("w1", WorkstreamStatus.Running)), 31)), Is.EqualTo(new[] { SoundCue.Working }),
            "it starts from the beginning's state, as it did the first time");
    }

    [Test]
    public void CuesThatArriveTogetherStartAtLeast300MillisecondsApartMostPressingFirst()
    {
        Stand(("w1", 0), ("w2", 1), ("w3", 2), ("w4", 3), ("w5", 4));
        Observe(Snapshot(
            W("w1", WorkstreamStatus.Running),
            W("w2", WorkstreamStatus.Running),
            W("w3", WorkstreamStatus.Running),
            W("w4", WorkstreamStatus.Completed),
            W("w5", WorkstreamStatus.Running)), 0);

        var together = Observe(Event(
            W("w1", WorkstreamStatus.Completed),
            W("w2", WorkstreamStatus.WaitingForHuman),
            W("w3", WorkstreamStatus.Failed)), 5);

        Assert.That(together.Select(cue => (cue.Cue, cue.WorkstreamId)),
            Is.EqualTo(new[] { (SoundCue.NeedsYou, "w2"), (SoundCue.Failed, "w3"), (SoundCue.TurnFinished, "w1") }));
        Assert.That(together.Select(cue => cue.At), Is.EqualTo(new[] { 5, 5.3, 5.6 }).Within(1e-9));

        // The person's own action waits its turn too.
        var opened = selector.Act(WorkspaceAct.Open, "w2", 5.1, audible: true)!;
        Assert.That(opened.At, Is.EqualTo(5.9).Within(1e-9));

        // Later cues keep the gap from the last onset, and start at once when they can.
        Assert.That(Observe(Event(W("w4", WorkstreamStatus.Running)), 6.0).Single().At, Is.EqualTo(6.2).Within(1e-9));
        Assert.That(Observe(Event(W("w5", WorkstreamStatus.Verifying)), 7.0).Single().At, Is.EqualTo(7.0));
    }

    [Test]
    public void LastKnownIsOneCueForTheWholeRoomOncePerLossOfConnection()
    {
        Stand(("w1", 1), ("w2", 3));
        Observe(Snapshot(W("w1", WorkstreamStatus.Running), W("w2", WorkstreamStatus.WaitingForHuman)), 0);

        var lost = Observe(new StateChanges(), 1, Status(ConnectionPhase.WaitingToRetry)).Single();
        Assert.That(lost.Cue, Is.EqualTo(SoundCue.LastKnown));
        Assert.That(lost.Place, Is.EqualTo(CuePlace.Stage));
        Assert.That(lost.WorkstreamId, Is.Null);
        Assert.That(lost.Bot, Is.EqualTo(-1), "it plays every bot's note");

        // Retrying, again and again, is the same loss.
        Assert.That(Observe(new StateChanges(), 2, Status(ConnectionPhase.Connecting)), Is.Empty);
        Assert.That(Observe(new StateChanges(), 3, Status(ConnectionPhase.WaitingToRetry)), Is.Empty);
        Assert.That(Observe(new StateChanges(), 4, Status(ConnectionPhase.Connecting)), Is.Empty);
        Assert.That(Observe(new StateChanges(), 5, Status(ConnectionPhase.WaitingToRetry)), Is.Empty);

        // Live again, resumed without a snapshot: nothing sounds for coming back, and the next loss is new.
        Assert.That(Observe(new StateChanges(), 6, Live), Is.Empty);
        Assert.That(Cues(Observe(new StateChanges(), 20, Status(ConnectionPhase.WaitingToRetry))), Is.EqualTo(new[] { SoundCue.LastKnown }));
        Observe(new StateChanges(), 21, Live);
        Assert.That(Cues(Observe(new StateChanges(), 40, Status(ConnectionPhase.Refused))), Is.EqualTo(new[] { SoundCue.LastKnown }),
            "a refusal loses the connection too");
    }

    [Test]
    public void WhatHappenedBeforeTheConnectionDroppedSoundsBeforeTheRoomsCue()
    {
        Stand(("w1", 1));
        Observe(Snapshot(W("w1", WorkstreamStatus.Running)), 0);

        var cues = Observe(Event(W("w1", WorkstreamStatus.Completed)), 1, Status(ConnectionPhase.WaitingToRetry));

        Assert.That(Cues(cues), Is.EqualTo(new[] { SoundCue.TurnFinished, SoundCue.LastKnown }));
        Assert.That(cues[1].At - cues[0].At, Is.EqualTo(SoundCueSelector.MinimumGap).Within(1e-9));
    }

    [Test]
    public void ASessionTheApplicationStoppedWasNotLost()
    {
        Stand(("w1", 1));
        Observe(Snapshot(W("w1", WorkstreamStatus.Running)), 0);

        // The headset slept: the session stopped, and resumed later.
        Assert.That(Observe(new StateChanges(), 1, Status(ConnectionPhase.Stopped)), Is.Empty);
        Assert.That(Observe(new StateChanges(), 60, Status(ConnectionPhase.Connecting)), Is.Empty);
        Assert.That(Observe(new StateChanges(), 61, Live), Is.Empty);

        // Stopped and started again within one frame, as the first pump after waking sees it.
        Assert.That(Observe(new StateChanges(), 90, Status(ConnectionPhase.Connecting)), Is.Empty);
        Assert.That(Observe(new StateChanges(), 91, Status(ConnectionPhase.WaitingToRetry)), Is.Empty,
            "not live since the pause, so there was nothing to lose");
    }

    [Test]
    public void WithTheOptionOnlyNeedsYouSoundsWhileAwayAndOnlyOnce()
    {
        Stand(("w1", 1), ("w2", 2));
        Observe(Snapshot(W("w1", WorkstreamStatus.Running), W("w2", WorkstreamStatus.Running)), 0);

        // A browser video has focus, and the person chose to hear Needs you meanwhile.
        Assert.That(Observe(Event(W("w2", WorkstreamStatus.Completed)), 1, audible: false, whileAway: true), Is.Empty, "nothing but Needs you");
        Assert.That(Cues(Observe(Event(W("w1", WorkstreamStatus.WaitingForHuman)), 2, audible: false, whileAway: true)),
            Is.EqualTo(new[] { SoundCue.NeedsYou }));
        Assert.That(Observe(Event(W("w1", WorkstreamStatus.WaitingForHuman)), 3, audible: false, whileAway: true), Is.Empty, "never an alarm");
        Assert.That(Observe(new StateChanges(), 4, Status(ConnectionPhase.WaitingToRetry), audible: false, whileAway: true), Is.Empty,
            "a dropped connection stays silent while away");
    }

    [Test]
    public void NothingPlaysWhileItCannotBeHeardNorAfterwards()
    {
        Stand(("w1", 1), ("w2", 2));
        Observe(Snapshot(W("w1", WorkstreamStatus.Running), W("w2", WorkstreamStatus.Running)), 0);

        // The system menu or another window has focus.
        Assert.That(Observe(Event(W("w1", WorkstreamStatus.WaitingForHuman)), 1, audible: false), Is.Empty);
        Assert.That(Observe(new StateChanges(), 2, Status(ConnectionPhase.WaitingToRetry), audible: false), Is.Empty);
        Assert.That(selector.Act(WorkspaceAct.Approve, "w1", 3, audible: false), Is.Null);
        Observe(new StateChanges(), 4, Live, audible: false);

        // Focus returns: nothing missed plays late, and changes compare with what was seen meanwhile.
        Assert.That(Observe(new StateChanges(), 5), Is.Empty);
        Assert.That(Observe(Event(W("w1", WorkstreamStatus.WaitingForHuman)), 6), Is.Empty, "it already needed the person");
        Assert.That(Cues(Observe(Event(W("w2", WorkstreamStatus.Completed)), 7)), Is.EqualTo(new[] { SoundCue.TurnFinished }));
        Assert.That(Observe(new StateChanges(), 8, Status(ConnectionPhase.WaitingToRetry)).Single().At, Is.EqualTo(8),
            "nothing unheard held the gap");
    }

    [Test]
    public void TheSameCueFromTheSameCharacterWithinTenSecondsIsDropped()
    {
        Stand(("w1", 1), ("w2", 2));
        Observe(Snapshot(W("w1", WorkstreamStatus.Running), W("w2", WorkstreamStatus.Running)), 0);

        Assert.That(Cues(Observe(Event(W("w1", WorkstreamStatus.Verifying)), 1)), Is.EqualTo(new[] { SoundCue.Verifying }));
        Assert.That(Observe(Event(W("w1", WorkstreamStatus.Running)), 2), Is.Empty);
        Assert.That(Observe(Event(W("w1", WorkstreamStatus.Verifying)), 6), Is.Empty, "five seconds after the last test run's cue");
        Assert.That(Cues(Observe(Event(W("w2", WorkstreamStatus.Verifying)), 7)), Is.EqualTo(new[] { SoundCue.Verifying }),
            "another character's cue is its own");
        Observe(Event(W("w1", WorkstreamStatus.Running)), 8);
        Assert.That(Cues(Observe(Event(W("w1", WorkstreamStatus.Verifying)), 11.5)), Is.EqualTo(new[] { SoundCue.Verifying }));
    }

    [Test]
    public void TheResultOfThePersonsActIsNeverDroppedAsARepeat()
    {
        Stand(("w1", 3));
        Observe(Snapshot(W("w1", WorkstreamStatus.Created)), 0);
        Assert.That(Cues(Observe(Event(W("w1", WorkstreamStatus.Running)), 1)), Is.EqualTo(new[] { SoundCue.Working }));
        Assert.That(Cues(Observe(Event(W("w1", WorkstreamStatus.WaitingForHuman)), 4)), Is.EqualTo(new[] { SoundCue.NeedsYou }));

        Assert.That(selector.Act(WorkspaceAct.Approve, "w1", 6, audible: true)!.Cue, Is.EqualTo(SoundCue.Approve));

        // The approval's result, confirmed by the runtime: the character works again, its own cue.
        Assert.That(Cues(Observe(Event(W("w1", WorkstreamStatus.Running)), 7)), Is.EqualTo(new[] { SoundCue.Working }));
    }

    [Test]
    public void OnlyCharactersOnTheStageSound()
    {
        Stand(("w1", 2));
        Observe(Snapshot(W("w1", WorkstreamStatus.Running), W("w7", WorkstreamStatus.Running)), 0);

        Assert.That(Observe(Event(W("w7", WorkstreamStatus.WaitingForHuman)), 1), Is.Empty, "no character, so no place to sound from");

        // It comes to the stage, and sounds from there from then on.
        Stand(("w1", 2), ("w7", 4));
        var cue = Observe(Event(W("w7", WorkstreamStatus.Running)), 2).Single();
        Assert.That((cue.Cue, cue.WorkstreamId, cue.Bot), Is.EqualTo((SoundCue.Working, "w7", 4)));
    }

    [Test]
    public void EachCharacterKeepsItsOwnNoteForAsLongAsItIsShown()
    {
        Stand(("a", 2), ("b", 3));
        Observe(Snapshot(W("a", WorkstreamStatus.Running), W("b", WorkstreamStatus.Running), W("c", WorkstreamStatus.Created), W("d", WorkstreamStatus.Created)), 0);
        Assert.That((selector.BotOf("a"), selector.BotOf("b")), Is.EqualTo(((int?)2, (int?)3)), "the notes of their slots");

        // The lineup trades their places, to bring one that needs the person to the middle.
        Stand(("a", 3), ("b", 2), ("c", 1));
        Observe(Event(W("b", WorkstreamStatus.WaitingForHuman)), 1);
        Assert.That((selector.BotOf("a"), selector.BotOf("b"), selector.BotOf("c")), Is.EqualTo(((int?)2, (int?)3, (int?)1)),
            "a character keeps its note when it moves; a newcomer takes its slot's");

        // One leaves the stage and another takes a slot whose note is held: the nearest free note.
        Stand(("b", 2), ("c", 1), ("d", 3));
        Observe(Event(W("d", WorkstreamStatus.Running)), 2);
        Assert.That(selector.BotOf("a"), Is.Null, "gone from the stage");
        Assert.That(selector.BotOf("d"), Is.EqualTo(2), "note 3 is b's, so the free note nearest to it");

        Assert.That(Observe(Event(W("b", WorkstreamStatus.Running)), 3).Single().Bot, Is.EqualTo(3), "a cue plays its character's note");
    }

    [Test]
    public void ThePersonsActionsSoundInFrontOfThemOnTheirCharactersNote()
    {
        Stand(("w1", 5));
        Observe(Snapshot(W("w1", WorkstreamStatus.WaitingForHuman)), 0);
        var acts = new[] { WorkspaceAct.Open, WorkspaceAct.Approve, WorkspaceAct.Deny, WorkspaceAct.Instruct, WorkspaceAct.Interrupt, WorkspaceAct.Collapse };
        var expected = new[] { SoundCue.Open, SoundCue.Approve, SoundCue.Deny, SoundCue.Instruct, SoundCue.Interrupt, SoundCue.Collapse };

        for (var i = 0; i < acts.Length; i++)
        {
            var cue = selector.Act(acts[i], "w1", 10 + i, audible: true)!;
            Assert.That((cue.Cue, cue.Place, cue.WorkstreamId, cue.Bot), Is.EqualTo((expected[i], CuePlace.Workspace, "w1", 5)), acts[i].ToString());
        }
        Assert.That(selector.Act(WorkspaceAct.Approve, "w1", 11, audible: true), Is.Not.Null, "the person's own acts always answer them");
        Assert.That(selector.Act(WorkspaceAct.Open, "not on the stage", 20, audible: true)!.Bot, Is.EqualTo(2), "the middle note, should it happen");
    }

    [Test]
    public void AnActSentAsTheKeyboardClosesSoundsOnceFocusReturns()
    {
        Stand(("w1", 1));
        Observe(Snapshot(W("w1", WorkstreamStatus.Completed)), 0);

        // The system keyboard closes with the instruction a moment before the app has focus again.
        Assert.That(selector.Act(WorkspaceAct.Instruct, "w1", 10, audible: false), Is.Null);
        var cue = selector.HeardAgain(10.4)!;
        Assert.That((cue.Cue, cue.Place, cue.WorkstreamId, cue.Bot, cue.At), Is.EqualTo((SoundCue.Instruct, CuePlace.Workspace, "w1", 1, 10.4)));
        Assert.That(selector.HeardAgain(11), Is.Null, "it sounds once");

        // Focus back later than that is for something else, and an act heard at once waits for nothing.
        selector.Act(WorkspaceAct.Instruct, "w1", 20, audible: false);
        Assert.That(selector.HeardAgain(20 + SoundCueSelector.ActWaitsForFocus + 0.1), Is.Null);
        selector.Act(WorkspaceAct.Open, "w1", 30, audible: false);
        Assert.That(selector.Act(WorkspaceAct.Collapse, "w1", 31, audible: true), Is.Not.Null);
        Assert.That(selector.HeardAgain(31.5), Is.Null);
    }

    [Test]
    public void ACommandSentFromTheWorkspaceIsTheActItStandsFor()
    {
        var commands = new CommandFactory(Samples.Client);
        Assert.That(WorkspaceActs.Of(commands.RespondToApproval("e1", "a1", ApprovalDecision.Approve)), Is.EqualTo(WorkspaceAct.Approve));
        Assert.That(WorkspaceActs.Of(commands.RespondToApproval("e1", "a1", ApprovalDecision.Deny)), Is.EqualTo(WorkspaceAct.Deny));
        Assert.That(WorkspaceActs.Of(commands.SendInstruction("e1", "Add a test.")), Is.EqualTo(WorkspaceAct.Instruct));
        Assert.That(WorkspaceActs.Of(commands.Interrupt("e1")), Is.EqualTo(WorkspaceAct.Interrupt));
        Assert.That(WorkspaceActs.Of(commands.CreateProject("Sample")), Is.Null);
    }

    /// <summary>
    /// The bundled demonstration, walked event by event the way a live session could deliver it,
    /// with the stage's lineup choosing the slots: the selector hears the same data and flow as it
    /// would live, and nothing tells it the difference.
    /// </summary>
    [Test]
    public void TheDemonstrationSoundsAsTheLiveFlowWould()
    {
        var recording = Demonstration.Recording();
        var lineup = new CharacterLineup(GlazeSynthesizer.Bots);
        var heard = new List<(SoundCue Cue, string Title, double At)>();
        double now = 0;

        void Heard(IEnumerable<CueOnset> cues)
        {
            foreach (var cue in cues)
            {
                heard.Add((cue.Cue, cue.WorkstreamId == null ? "the room" : state.Workstreams[cue.WorkstreamId].Title, cue.At));
            }
        }

        void Pump(StateChanges changes)
        {
            // The stage draws the change first, then the sound follows it.
            lineup.Update(state.Workstreams.Values);
            Heard(selector.Observe(changes, state, Live, lineup.SlotOf, now, audible: true));
        }

        void Play(DemonstrationNode node)
        {
            var begun = now;
            foreach (var recorded in node.Events)
            {
                now = Math.Max(now, begun + recorded.At.TotalSeconds);
                var changes = new StateChanges();
                state.ApplyEvent(recorded.Message, changes);
                Pump(changes);
            }
            if (node.EndingSnapshot != null)
            {
                var changes = new StateChanges();
                state.ApplySnapshot(node.EndingSnapshot.Snapshot, changes);
                Pump(changes);
            }
        }

        void Answer(DemonstrationNode node, Func<DemonstrationBranch, bool> which, WorkspaceAct act)
        {
            var branch = node.BranchesAfter(node.Events.Count).First(which);
            var workstream = state.Executions[branch.Answer.ExecutionId].WorkstreamId;
            now += 3;
            Heard(new[] { selector.Act(WorkspaceAct.Open, workstream, now, audible: true)! });
            now += 4;
            Heard(new[] { selector.Act(act, workstream, now, audible: true)! });
            now += 1;
            Heard(new[] { selector.Act(WorkspaceAct.Collapse, workstream, now, audible: true)! });
            Play(recording.Nodes[branch.Node]);
        }

        state.ApplyWelcome(recording.Welcome);
        var beginning = new StateChanges();
        state.ApplySnapshot(recording.Snapshot.Snapshot, beginning);
        Pump(beginning);
        Assert.That(heard, Is.Empty, "the beginning arrives as a snapshot");

        var first = recording.Nodes[0];
        Play(first);
        Answer(first, branch => branch.Answer.Kind == DemonstrationAnswerKind.Approve, WorkspaceAct.Approve);
        var approved = recording.Nodes[Demonstration.AtApproval(DemonstrationAnswerKind.Approve).Node];
        Answer(approved, branch => branch.Answer.Kind == DemonstrationAnswerKind.Instruct, WorkspaceAct.Instruct);

        foreach (var cue in heard) TestContext.Out.WriteLine($"{cue.At,7:F2} s  {cue.Cue,-13} {cue.Title}");
        foreach (var watched in new[] { "Paginate the order history endpoint", "Send an order confirmation email" })
        {
            Assert.That(heard.Where(cue => cue.Title == watched).Select(cue => cue.Cue),
                Is.EqualTo(new[] { SoundCue.Working, SoundCue.Verifying, SoundCue.TurnFinished }), watched);
        }
        Assert.That(heard.Where(cue => cue.Title == Demonstration.Directed).Select(cue => cue.Cue), Is.EqualTo(new[]
        {
            SoundCue.Working, SoundCue.NeedsYou,
            SoundCue.Open, SoundCue.Approve, SoundCue.Collapse,
            // The approval's result, confirmed by the recording's runtime, as the character's own cues.
            SoundCue.Working, SoundCue.Verifying, SoundCue.TurnFinished,
            SoundCue.Open, SoundCue.Instruct, SoundCue.Collapse,
            SoundCue.Working, SoundCue.Verifying, SoundCue.TurnFinished,
        }));

        // Starting again: the same journal at its beginning, heard as nothing.
        var count = heard.Count;
        var again = new StateChanges();
        state.ApplySnapshot(recording.Snapshot.Snapshot, again);
        Assert.That(again.Rewound, Is.True);
        now += 20;
        Pump(again);
        Assert.That(heard, Has.Count.EqualTo(count));

        for (var i = 1; i < heard.Count; i++) Assert.That(heard[i].At - heard[i - 1].At, Is.GreaterThanOrEqualTo(SoundCueSelector.MinimumGap - 1e-9));
        Assert.That(heard.Select(cue => cue.Cue), Has.None.EqualTo(SoundCue.LastKnown), "it never disconnects");
    }

    private void Stand(params (string Id, int Slot)[] standing)
    {
        slots.Clear();
        foreach (var (id, slot) in standing) slots[id] = slot;
    }

    private int SlotOf(string workstreamId) => slots.TryGetValue(workstreamId, out var slot) ? slot : -1;

    private static WorkstreamView W(string id, WorkstreamStatus status) => Samples.Workstream(id, status);

    private static ConnectionStatus Status(ConnectionPhase phase) => new(phase, phase == ConnectionPhase.Live ? null : "for the test");

    private static IEnumerable<SoundCue> Cues(IEnumerable<CueOnset> cues) => cues.Select(cue => cue.Cue);

    private StateChanges Snapshot(params WorkstreamView[] workstreams)
    {
        var changes = new StateChanges();
        position += 100;
        state.ApplySnapshot(Samples.Snapshot(position, workstreams), changes);
        return changes;
    }

    private StateChanges Event(params WorkstreamView[] workstreams)
    {
        var changes = new StateChanges();
        state.ApplyEvent(Samples.Event(++position, workstreams), changes);
        return changes;
    }

    private IReadOnlyList<CueOnset> Observe(StateChanges changes, double now, ConnectionStatus? status = null, bool audible = true, bool whileAway = false) =>
        selector.Observe(changes, state, status ?? Live, SlotOf, now, audible, whileAway);
}

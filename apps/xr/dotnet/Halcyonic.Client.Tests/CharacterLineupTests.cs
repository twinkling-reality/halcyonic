using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class CharacterLineupTests
{
    /// <summary>A workstream last changed <paramref name="minute"/> minutes after nine.</summary>
    private static WorkstreamView Workstream(string id, WorkstreamStatus status, int minute, AttentionLevel attention = AttentionLevel.None)
    {
        var workstream = Samples.Workstream(id, status, level: attention);
        workstream.UpdatedAt = $"2026-09-26T09:{minute:00}:00.000Z";
        return workstream;
    }

    private static WorkstreamView Change(WorkstreamView workstream, WorkstreamStatus status, int minute, AttentionLevel attention = AttentionLevel.None)
    {
        workstream.Status = status;
        workstream.Attention = new Attention { Level = attention, Reasons = new List<AttentionReason>() };
        workstream.UpdatedAt = $"2026-09-26T09:{minute:00}:00.000Z";
        return workstream;
    }

    private static string?[] Slots(CharacterLineup lineup) => lineup.Slots.ToArray();

    [Test]
    public void ShowsAtMostItsCapacity()
    {
        var lineup = new CharacterLineup(6);
        var workstreams = Enumerable.Range(0, 10).Select(i => Workstream("w" + i, WorkstreamStatus.Completed, i)).ToList();

        Assert.That(lineup.Update(workstreams), Is.True);

        Assert.That(lineup.Capacity, Is.EqualTo(6));
        Assert.That(lineup.Slots, Has.Count.EqualTo(6));
        Assert.That(lineup.Slots, Is.All.Not.Null);
        Assert.That(lineup.Slots, Is.Unique);
    }

    [Test]
    public void AttentionComesFirstThenActiveThenTheMostRecentlyChanged()
    {
        var lineup = new CharacterLineup(6);
        lineup.Update(new[]
        {
            Workstream("resting-old", WorkstreamStatus.Completed, 1),
            Workstream("resting-new", WorkstreamStatus.Interrupted, 50),
            Workstream("resting-newer", WorkstreamStatus.Completed, 55),
            Workstream("created", WorkstreamStatus.Created, 58),
            Workstream("running", WorkstreamStatus.Running, 2),
            Workstream("testing", WorkstreamStatus.Verifying, 3),
            Workstream("unknown", WorkstreamStatus.Unknown, 4, AttentionLevel.Notice),
            Workstream("failed", WorkstreamStatus.Failed, 5, AttentionLevel.Notice),
            Workstream("needs-you", WorkstreamStatus.WaitingForHuman, 0, AttentionLevel.ActionRequired),
        });

        Assert.That(Slots(lineup), Is.EquivalentTo(new[] { "needs-you", "failed", "unknown", "running", "testing", "created" }));
    }

    [Test]
    public void TheMostImportantStandInTheMiddle()
    {
        var lineup = new CharacterLineup(6);
        lineup.Update(new[]
        {
            Workstream("resting", WorkstreamStatus.Completed, 1),
            Workstream("running", WorkstreamStatus.Running, 2),
            Workstream("needs-you", WorkstreamStatus.WaitingForHuman, 3, AttentionLevel.ActionRequired),
        });

        // Slots run from the person's left; the two middle ones fill first, the left one first.
        Assert.That(Slots(lineup), Is.EqualTo(new[] { null, "resting", "needs-you", "running", null, null }));
    }

    [Test]
    public void SlotsFillFromTheMiddleOutward()
    {
        var lineup = new CharacterLineup(6);
        var workstreams = Enumerable.Range(0, 6).Select(i => Workstream("w" + i, WorkstreamStatus.Completed, 50 - i)).ToList();

        lineup.Update(workstreams);

        // w0 changed most recently, so it ranks first.
        Assert.That(Slots(lineup), Is.EqualTo(new[] { "w4", "w2", "w0", "w1", "w3", "w5" }));
        Assert.That(lineup.SlotOf("w0"), Is.EqualTo(2));
        Assert.That(lineup.SlotOf("absent"), Is.EqualTo(-1));
    }

    [Test]
    public void ACharacterKeepsItsSlotWhileItStaysShown()
    {
        var lineup = new CharacterLineup(6);
        var workstreams = Enumerable.Range(0, 6).Select(i => Workstream("w" + i, WorkstreamStatus.Created, i)).ToList();
        lineup.Update(workstreams);
        var before = Slots(lineup);

        Change(workstreams[0], WorkstreamStatus.Running, 20);
        Change(workstreams[3], WorkstreamStatus.Verifying, 21);
        Change(workstreams[5], WorkstreamStatus.Completed, 22);
        Assert.That(lineup.Update(workstreams), Is.False, "nothing moved");

        Assert.That(Slots(lineup), Is.EqualTo(before));
    }

    [Test]
    public void ACharacterThatComesToNeedAttentionTradesPlacesWithTheMiddle()
    {
        var lineup = new CharacterLineup(6);
        var workstreams = Enumerable.Range(0, 6).Select(i => Workstream("w" + i, WorkstreamStatus.Running, 50 - i)).ToList();
        lineup.Update(workstreams);
        Assert.That(Slots(lineup), Is.EqualTo(new[] { "w4", "w2", "w0", "w1", "w3", "w5" }));

        // The outermost on the right comes to need its person.
        Change(workstreams[5], WorkstreamStatus.WaitingForHuman, 55, AttentionLevel.ActionRequired);
        Assert.That(lineup.Update(workstreams), Is.True);

        Assert.That(Slots(lineup), Is.EqualTo(new[] { "w4", "w2", "w5", "w1", "w3", "w0" }), "only the two that traded moved");
    }

    [Test]
    public void CharactersThatNeedAttentionDoNotTradePlacesAmongThemselves()
    {
        var lineup = new CharacterLineup(6);
        var workstreams = Enumerable.Range(0, 6).Select(i => Workstream("w" + i, WorkstreamStatus.Running, 50 - i)).ToList();
        lineup.Update(workstreams);

        // The one in the middle fails, and one farther out comes to need its person.
        Change(workstreams[0], WorkstreamStatus.Failed, 51, AttentionLevel.Notice);
        Change(workstreams[2], WorkstreamStatus.WaitingForHuman, 52, AttentionLevel.ActionRequired);
        lineup.Update(workstreams);

        // The failed one keeps the middle slot; the other takes the next one in.
        Assert.That(Slots(lineup), Is.EqualTo(new[] { "w4", "w1", "w0", "w2", "w3", "w5" }));
    }

    [Test]
    public void AMoreImportantWorkstreamTakesTheSlotOfTheLeastImportantOne()
    {
        var lineup = new CharacterLineup(6);
        var workstreams = Enumerable.Range(0, 7).Select(i => Workstream("w" + i, WorkstreamStatus.Completed, 10 + i)).ToList();
        lineup.Update(workstreams);
        Assert.That(lineup.SlotOf("w0"), Is.EqualTo(-1), "the least recently changed one waits");
        var before = Slots(lineup);
        var stalest = lineup.SlotOf("w1");

        Change(workstreams[0], WorkstreamStatus.Running, 30);
        Assert.That(lineup.Update(workstreams), Is.True);

        var after = Slots(lineup);
        Assert.That(after[stalest], Is.EqualTo("w0"), "the newcomer stands where the least important one stood");
        for (var slot = 0; slot < after.Length; slot++)
        {
            if (slot != stalest) Assert.That(after[slot], Is.EqualTo(before[slot]), "nobody else moves");
        }
    }

    [Test]
    public void ANewcomerThatNeedsAttentionStandsInTheMiddle()
    {
        var lineup = new CharacterLineup(6);
        var workstreams = Enumerable.Range(0, 7).Select(i => Workstream("w" + i, WorkstreamStatus.Completed, 10 + i)).ToList();
        lineup.Update(workstreams);
        var middle = Slots(lineup)[2];

        Change(workstreams[0], WorkstreamStatus.Failed, 30, AttentionLevel.Notice);
        lineup.Update(workstreams);

        var after = Slots(lineup);
        Assert.That(after[2], Is.EqualTo("w0"));
        Assert.That(lineup.SlotOf("w1"), Is.EqualTo(-1), "the least recently changed one made way");
        Assert.That(after, Does.Contain(middle), "the one it moved aside stays on the stage");
    }

    [Test]
    public void WorkingCharactersDoNotSwapInAndOutAsTheyChange()
    {
        var lineup = new CharacterLineup(6);
        var workstreams = Enumerable.Range(0, 8).Select(i => Workstream("w" + i, WorkstreamStatus.Running, i)).ToList();
        lineup.Update(workstreams);
        var before = Slots(lineup);
        Assert.That(before, Does.Not.Contain("w0").And.Not.Contain("w1"));

        // The two waiting ones report activity; the shown ones stay.
        Change(workstreams[0], WorkstreamStatus.Running, 40);
        Change(workstreams[1], WorkstreamStatus.Verifying, 41);
        Assert.That(lineup.Update(workstreams), Is.False);

        Assert.That(Slots(lineup), Is.EqualTo(before));
    }

    [Test]
    public void AtRestTheMostRecentlyChangedReplacesTheLeastRecentlyChanged()
    {
        var lineup = new CharacterLineup(6);
        var workstreams = Enumerable.Range(0, 7).Select(i => Workstream("w" + i, WorkstreamStatus.Completed, 10 + i)).ToList();
        lineup.Update(workstreams);
        var stalest = lineup.SlotOf("w1");

        workstreams.Add(Workstream("new", WorkstreamStatus.Created, 50));
        lineup.Update(workstreams);

        Assert.That(Slots(lineup)[stalest], Is.EqualTo("new"));
        Assert.That(lineup.SlotOf("w1"), Is.EqualTo(-1));
    }

    [Test]
    public void AWorkingCharacterThatFinishesMakesWayForWaitingWork()
    {
        var lineup = new CharacterLineup(2);
        var workstreams = new List<WorkstreamView>
        {
            Workstream("a", WorkstreamStatus.Running, 3),
            Workstream("b", WorkstreamStatus.Running, 2),
            Workstream("c", WorkstreamStatus.Running, 1),
        };
        lineup.Update(workstreams);
        Assert.That(Slots(lineup), Is.EqualTo(new[] { "a", "b" }));

        Change(workstreams[1], WorkstreamStatus.Completed, 10);
        lineup.Update(workstreams);

        Assert.That(Slots(lineup), Is.EqualTo(new[] { "a", "c" }));
    }

    [Test]
    public void AWorkstreamThatDisappearsFreesItsSlot()
    {
        var lineup = new CharacterLineup(6);
        lineup.Update(Enumerable.Range(0, 6).Select(i => Workstream("old" + i, WorkstreamStatus.Completed, i)));

        // A snapshot from another journal replaces every workstream.
        lineup.Update(new[] { Workstream("fresh", WorkstreamStatus.Running, 1) });

        Assert.That(Slots(lineup), Is.EqualTo(new[] { null, null, "fresh", null, null, null }));
    }

    [Test]
    public void TiesAreBrokenByIdSoTheLineupIsTheSameEveryTime()
    {
        var first = new CharacterLineup(3);
        var second = new CharacterLineup(3);
        var workstreams = new[] { "d", "b", "a", "c" }.Select(id => Workstream(id, WorkstreamStatus.Completed, 5)).ToList();

        first.Update(workstreams);
        workstreams.Reverse();
        second.Update(workstreams);

        Assert.That(Slots(first), Is.EqualTo(new[] { "b", "a", "c" }));
        Assert.That(Slots(second), Is.EqualTo(Slots(first)));
    }

    [TestCase(WorkstreamStatus.WaitingForHuman, AttentionLevel.ActionRequired, LineupTier.NeedsYou)]
    [TestCase(WorkstreamStatus.Unknown, AttentionLevel.ActionRequired, LineupTier.NeedsYou)]
    [TestCase(WorkstreamStatus.WaitingForHuman, AttentionLevel.None, LineupTier.NeedsYou)]
    [TestCase(WorkstreamStatus.Failed, AttentionLevel.Notice, LineupTier.Notice)]
    [TestCase(WorkstreamStatus.Unknown, AttentionLevel.Notice, LineupTier.Notice)]
    [TestCase(WorkstreamStatus.Completed, AttentionLevel.Notice, LineupTier.Notice)]
    [TestCase(WorkstreamStatus.Failed, AttentionLevel.None, LineupTier.Notice)]
    [TestCase(WorkstreamStatus.Starting, AttentionLevel.None, LineupTier.Active)]
    [TestCase(WorkstreamStatus.Running, AttentionLevel.None, LineupTier.Active)]
    [TestCase(WorkstreamStatus.Verifying, AttentionLevel.None, LineupTier.Active)]
    [TestCase(WorkstreamStatus.Created, AttentionLevel.None, LineupTier.AtRest)]
    [TestCase(WorkstreamStatus.Completed, AttentionLevel.None, LineupTier.AtRest)]
    [TestCase(WorkstreamStatus.Interrupted, AttentionLevel.None, LineupTier.AtRest)]
    public void TiersFollowAttentionThenStatus(WorkstreamStatus status, AttentionLevel attention, LineupTier tier)
    {
        Assert.That(CharacterLineup.TierOf(Workstream("w", status, 0, attention)), Is.EqualTo(tier));
    }

    [Test]
    public void EveryStatusHasATier()
    {
        foreach (WorkstreamStatus status in Enum.GetValues(typeof(WorkstreamStatus)))
        {
            Assert.DoesNotThrow(() => CharacterLineup.TierOf(Workstream("w", status, 0)));
        }
    }

    [Test]
    public void AStageNeedsASlot()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterLineup(0));
    }
}

using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The result's card and its Checks page say the same of a task's checks (JUDGE's ALERT 2026-11-16 05:04Z; the lead's ruling):
/// a check the computer ran, a check it is running now, a demonstration's simulated checks, or none; never "Nothing checked
/// yet" over a page that reads a check.
/// </summary>
public class ResultCardAgreesTests
{
    private static readonly LineWords Task = LineWords.TheirName("Make the game");

    private static RoomResult Result(bool? passed = null, RoomCardStatus? simulated = null, bool checkRunning = false) =>
        new(false, false, false, passed, null, null, simulated: simulated, checkRunning: checkRunning);

    [Test]
    public void ADemonstrationsSimulatedChecksStandOnTheCardAsThePageSaysThem()
    {
        foreach (var page in new[]
        {
            new RoomCardStatus(RoomPanelCards.AllPassing, "Simulated at 05:00:12", RoomTone.Good),
            new RoomCardStatus(RoomPanelCards.ChecksFailed, "Simulated at 05:00:12", RoomTone.Bad),
        })
        {
            var card = RoomPanelCards.Result(Result(simulated: page), Task);
            Assert.That(card, Is.Not.Null, "a simulated outcome is something to say: never the standing card's Nothing checked yet");
            Assert.That(card!.Status, Is.EqualTo((RoomCardStatus?)page), "the card's status is the page's own, word for word");
        }
    }

    [Test]
    public void ACheckUnderWayIsSaidAsChecking()
    {
        var card = RoomPanelCards.Result(Result(checkRunning: true), Task)!;
        Assert.That((card.Status?.Title, card.Status?.Tone), Is.EqualTo(((string?)TaskSteps.Checking, (RoomTone?)RoomTone.Working)));
    }

    [Test]
    public void TheComputersOwnCheckWinsOverASimulatedOneOrAQuietWait()
    {
        var simulated = new RoomCardStatus(RoomPanelCards.ChecksFailed, "Simulated", RoomTone.Bad);
        var result = Result(passed: true, simulated: simulated, checkRunning: true);
        Assert.That((result.Simulated, result.CheckRunning), Is.EqualTo(((RoomCardStatus?)null, false)), "the computer's own outcome stands alone");
        var card = RoomPanelCards.Result(result, Task)!;
        Assert.That(card.Slots.Select(slot => slot.Value.Text), Does.Contain(RoomPanelCards.AllPassing));
        Assert.That(card.Status, Is.Null);
    }

    [Test]
    public void WithNothingCheckedTheCardStillStandsDownToTheStandingCard()
    {
        Assert.That(RoomPanelCards.Result(Result(), Task), Is.Null, "nothing to say: the task's standing card says so, truly");
    }

    [Test]
    public void ACheckCommandStillAcceptedMakesTheResultSayChecking()
    {
        var project = new ProjectView { ProjectId = "p1", Name = "Balloon Pop", CreatedAt = Samples.Time, UpdatedAt = Samples.Time };
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(1, new WorkstreamView[0], new ExecutionView[0]);
        snapshot.Projects.Add(project);
        snapshot.Commands.Add(new CommandView
        {
            CommandId = "01a1a4c2-0000-7000-8000-0000000000c1", CommandType = CommandType.ProjectCheck, Status = CommandStatus.Accepted,
            ProjectId = "p1", IssuedAt = Samples.Time, UpdatedAt = Samples.Time,
        });
        state.ApplySnapshot(snapshot, new StateChanges());
        Assert.That(RoomResult.Of(project, state, null)!.CheckRunning, Is.True);

        var other = new ClientProjection();
        var ended = Samples.Snapshot(1, new WorkstreamView[0], new ExecutionView[0]);
        ended.Projects.Add(project);
        ended.Commands.Add(new CommandView
        {
            CommandId = "01a1a4c2-0000-7000-8000-0000000000c2", CommandType = CommandType.ProjectCheck, Status = CommandStatus.Completed,
            ProjectId = "p1", IssuedAt = Samples.Time, UpdatedAt = Samples.Time,
        });
        other.ApplySnapshot(ended, new StateChanges());
        Assert.That(RoomResult.Of(project, other, null)!.CheckRunning, Is.False, "an ended check is no longer under way");
    }

    [Test]
    public void AStarterOnlyCheckIsSaidAsStarterOnlyOnTheCardNeverFailed()
    {
        var project = new ProjectView
        {
            ProjectId = "p1", Name = "Playroom", CreatedAt = Samples.Time, UpdatedAt = Samples.Time,
            Check = new ProjectViewCheck
            {
                CheckId = "check-1", RecordedAt = "2026-11-16T14:00:00.000Z", Files = ProjectViewCheckFiles.Current,
                Findings = HalcyonicJson.Deserialize<CheckFindings>(
                    "{\"tsc\":{\"exit\":0,\"seconds\":1.5},\"build\":{\"exit\":0,\"seconds\":4.25}," +
                    "\"page\":{\"exceptions\":[],\"exception_count\":0,\"console_errors\":0,\"canvases\":1,\"edges_at_3s\":900,\"edges_at_10s\":950,\"drawn\":false,\"pictures\":[]}," +
                    "\"unknown\":null,\"passed\":false,\"verdict\":\"starter_only\",\"reads_confined\":true,\"vr\":{\"offered\":\"offered\",\"how\":\"offer_session\",\"exceptions\":0,\"ready\":true}}", strict: true),
            },
        };
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(1, new WorkstreamView[0], new ExecutionView[0]);
        snapshot.Projects.Add(project);
        state.ApplySnapshot(snapshot, new StateChanges());
        var result = RoomResult.Of(project, state, null)!;
        Assert.That(result.Passed, Is.Null, "a starter's picture alone claims no outcome");
        var card = RoomPanelCards.Result(result, Task)!;
        Assert.That((card.Status?.Title, card.Status?.Line, card.Status?.Tone),
            Is.EqualTo(((string?)"Starter Only", (string?)"Nothing new is drawn yet. Say what you want to see.", (RoomTone?)RoomTone.Quiet)));
        Assert.That(card.Slots.Select(slot => slot.Value.Text), Has.None.EqualTo(RoomPanelCards.ChecksFailed), "never Failed");
    }
}

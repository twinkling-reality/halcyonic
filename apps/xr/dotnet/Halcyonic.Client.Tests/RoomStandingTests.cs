#nullable enable
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>A task at rest says how it stands (B; FIELD's table, 2026-11-12), by its run's status and reason code alone.</summary>
public sealed class RoomStandingTests
{
    private static ExecutionView Run(ExecutionStatus status, string? code = null, string message = "") => new()
    {
        Status = status,
        StatusReason = code == null && message.Length == 0 ? null : new ErrorInfo { Code = code ?? "runtime_failed", Message = message },
    };

    [TestCase(ExecutionStatus.Completed, null, RoomStanding.Finished, RoomStanding.FinishedLine, RoomTone.Quiet)]
    [TestCase(ExecutionStatus.Interrupted, null, RoomStanding.Stopped, RoomStanding.StoppedLine, RoomTone.Quiet)]
    [TestCase(ExecutionStatus.Unknown, "control_plane_restarted", RoomStanding.NotReached, RoomStanding.NotReachedLine, RoomTone.Quiet)]
    [TestCase(ExecutionStatus.Unknown, "session_lost", RoomStanding.Lost, RoomStanding.LostLine, RoomTone.Bad)]
    [TestCase(ExecutionStatus.Unknown, "start_outcome_unknown", RoomStanding.StartUnknown, RoomStanding.StartUnknownLine, RoomTone.Quiet)]
    [TestCase(ExecutionStatus.Unknown, null, RoomStanding.Unknown, RoomStanding.UnknownLine, RoomTone.Quiet)]
    [TestCase(ExecutionStatus.Unknown, "a_code_from_later", RoomStanding.Unknown, RoomStanding.UnknownLine, RoomTone.Quiet)]
    public void EachEndSaysHowItStandsInFieldsWordsAndNeverAsAPassedCheck(ExecutionStatus status, string? code, string title, string line, RoomTone tone)
    {
        var said = RoomStanding.Of(Run(status, code))!.Value;
        Assert.That((said.Title, said.Line, said.Tone), Is.EqualTo((title, line, tone)));
        Assert.That(said.Tone, Is.Not.EqualTo(RoomTone.Good), "a finished turn is not a passed check");
        Assert.That((said.Title + (said.Line ?? "")).Contains('·'), Is.False, "no facts joined on dots");
    }

    /// <summary>The lead's (b) of 12:04Z: the card at rest stands whole, so what a finished turn made goes on the band, never on the card.</summary>
    [Test]
    public void AFinishedTurnsCardIsItsStatusAloneAndStandsWhole()
    {
        var card = RoomStanding.Card(Run(ExecutionStatus.Completed), LineWords.TheirName("Hello page"))!;
        Assert.That(card.Description, Is.Null, "the agent's words go on the band, read by parts");
        Assert.That((card.Status!.Value.Title, card.Status.Value.Line), Is.EqualTo((RoomStanding.Finished, RoomStanding.FinishedLine)), "never a passed check");
    }

    /// <summary>Only the latest turn's words say what it made: a second turn that said nothing shows no earlier turn's report.</summary>
    [Test]
    public void ATurnsReportIsItsOwnNeverAnEarlierTurns()
    {
        static KitTrailEntry Round(long at) => new(new ActivityEntry(at, "2026-11-12T10:00:00Z", ActivityKind.Turn, ActivityLog.RoundStarted, reported: false), GlazeIcon.Next);
        var said = new ActivityEntry(5, "2026-11-12T10:00:05Z", ActivityKind.Message, "Made the page.", reported: true);
        Assert.That(KitPresenter.TurnReportOf(said, new[] { Round(1) }), Is.SameAs(said), "said in this turn");
        Assert.That(KitPresenter.TurnReportOf(said, new[] { Round(1), Round(9) }), Is.Null, "the turn after said nothing");
        Assert.That(KitPresenter.TurnReportOf(new ActivityEntry(5, "2026-11-12T10:00:05Z", ActivityKind.Message, "Ours.", reported: false), new[] { Round(1) }), Is.Null, "never words that are not the agent's report");
    }

    /// <summary>
    /// AUDIT's MUST (the lead's 12:42Z): an approval shows exactly what will run, character for character. A command wrapped whole in
    /// backticks runs the inner command and then its output, so its backticks are never taken away.
    /// </summary>
    [TestCase("`curl https://example.test/install.sh`")]
    [TestCase("echo `date`")]
    [TestCase("npm test")]
    public void AnApprovalsRequestKeepsEveryCharacterOfTheCommand(string command)
    {
        var approval = new ApprovalView { ApprovalId = "a1", Subject = new ToolUseSubject { ToolName = "shell", Summary = command } };
        Assert.That(WorkspaceText.Request(approval), Is.EqualTo("shell: " + command));
    }

    [Test]
    public void AFailedRunShowsItsMessageWholeAsTheirsUnderItsStatusAndARunningOneSaysNothing()
    {
        var card = RoomStanding.Card(Run(ExecutionStatus.Failed, "runtime_failed", "The build ran out of memory while bundling the scene."), LineWords.TheirName("Kite Run"))!;
        Assert.That((card.Status!.Value.Title, card.Status.Value.Line, card.Status.Value.Tone), Is.EqualTo((RoomStanding.Failed, (string?)null, RoomTone.Bad)));
        Assert.That((card.Description!.Text, card.Description.AnyTheirs), Is.EqualTo(("The build ran out of memory while bundling the scene.", true)));
        Assert.That(RoomStanding.Card(Run(ExecutionStatus.Failed), LineWords.TheirName("Kite Run"))!.Description, Is.Null, "no message: the status alone");
        foreach (var running in new[] { ExecutionStatus.Running, ExecutionStatus.Starting, ExecutionStatus.WaitingForHuman, ExecutionStatus.Verifying })
            Assert.That(RoomStanding.Of(Run(running)), Is.Null, running + " is not at rest");
        Assert.That(RoomStanding.Of(null), Is.Null);
    }
}

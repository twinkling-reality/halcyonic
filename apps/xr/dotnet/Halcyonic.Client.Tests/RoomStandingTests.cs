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

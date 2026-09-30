using System;
using System.Collections.Generic;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class CharacterPresenterTests
{
    private static ClientProjection StateWith(WorkstreamView workstream, ExecutionView? execution = null, JournalOrigin origin = JournalOrigin.Live)
    {
        var state = new ClientProjection();
        state.ApplySnapshot(
            Samples.Snapshot(1, new[] { workstream }, execution == null ? null : new[] { execution }, Samples.Journal(origin: origin)),
            new StateChanges());
        return state;
    }

    [Test]
    public void EveryWorkstreamStatusHasAnActivityAndATextLabel()
    {
        var labels = new HashSet<string>();
        foreach (WorkstreamStatus status in Enum.GetValues(typeof(WorkstreamStatus)))
        {
            var label = CharacterPresenter.LabelOf(CharacterPresenter.ActivityOf(status));
            Assert.That(label, Is.Not.Empty);
            labels.Add(label);
        }
        Assert.That(labels, Has.Count.EqualTo(Enum.GetValues(typeof(WorkstreamStatus)).Length), "each status reads differently");
    }

    [Test]
    public void ACompletedTurnDoesNotClaimTheWorkIsCorrect()
    {
        Assert.That(CharacterPresenter.ActivityOf(WorkstreamStatus.Completed), Is.EqualTo(CharacterActivity.TurnFinished));
        Assert.That(CharacterPresenter.LabelOf(CharacterActivity.TurnFinished), Is.EqualTo("Turn finished"));
    }

    [Test]
    public void AnApprovalExplainsWhatNeedsApproving()
    {
        var execution = Samples.Execution("e1", "w1", ExecutionStatus.WaitingForHuman);
        execution.PendingApprovals.Add(new ApprovalView
        {
            ApprovalId = "approval-1",
            Subject = new ToolUseSubject { ToolName = "bash", Summary = "Run the migration" },
            RequestedAt = Samples.Time,
        });
        var workstream = Samples.Workstream(
            "w1",
            WorkstreamStatus.WaitingForHuman,
            "e1",
            AttentionLevel.ActionRequired,
            new ApprovalPendingReason { ExecutionId = "e1", ApprovalId = "approval-1" });

        var character = CharacterPresenter.Present(workstream, StateWith(workstream, execution), live: true);

        Assert.That(character.Activity, Is.EqualTo(CharacterActivity.WaitingForHuman));
        Assert.That(character.StatusLabel, Is.EqualTo("Needs you"));
        Assert.That(character.Attention, Is.EqualTo(AttentionLevel.ActionRequired));
        Assert.That(character.PendingApprovals, Is.EqualTo(1));
        Assert.That(character.AttentionNotes, Is.EqualTo(new[] { "Approval needed to use bash: Run the migration" }));
        Assert.That(character.Synthetic, Is.True);
        Assert.That(character.Stale, Is.False);
    }

    [Test]
    public void FailuresAndUnknownStatesCarryTheirReasons()
    {
        var failed = Samples.Execution("e1", "w1", ExecutionStatus.Failed);
        failed.StatusReason = new ErrorInfo { Code = "runtime_error", Message = "The model provider returned an error." };
        var failedStream = Samples.Workstream("w1", WorkstreamStatus.Failed, "e1", AttentionLevel.ActionRequired, new ExecutionFailedReason { ExecutionId = "e1" });
        Assert.That(
            CharacterPresenter.Present(failedStream, StateWith(failedStream, failed), live: true).AttentionNotes,
            Is.EqualTo(new[] { "Failed: The model provider returned an error." }));

        var unknown = Samples.Execution("e2", "w2", ExecutionStatus.Unknown);
        unknown.StatusReason = new ErrorInfo { Code = "control_plane_restarted", Message = "The control plane restarted." };
        var unknownStream = Samples.Workstream("w2", WorkstreamStatus.Unknown, "e2", AttentionLevel.Notice, new ExecutionStateUnknownReason { ExecutionId = "e2" });
        var character = CharacterPresenter.Present(unknownStream, StateWith(unknownStream, unknown), live: true);
        Assert.That(character.Activity, Is.EqualTo(CharacterActivity.Unknown));
        Assert.That(character.AttentionNotes, Is.EqualTo(new[] { "State unknown: The control plane restarted." }));
    }

    /// <summary>A character's words are shown by the one rule for text Halcyonic did not write.</summary>
    [Test]
    public void TitlesAndReasonsFromOutsideShowWhatWouldNotShowAsThemselves()
    {
        var failed = Samples.Execution("e1", "w1", ExecutionStatus.Failed);
        failed.StatusReason = new ErrorInfo { Code = "runtime_error", Message = "Exit 1\u0003 and the rest\r\nof the log" };
        var workstream = Samples.Workstream("w1", WorkstreamStatus.Failed, "e1", AttentionLevel.ActionRequired, new ExecutionFailedReason { ExecutionId = "e1" });
        workstream.Title = "<color=#00000000>Tidy</color>\tthe notes\\n";
        var character = CharacterPresenter.Present(workstream, StateWith(workstream, failed), live: true);
        Assert.That(character.Title, Is.EqualTo("<color=#00000000>Tidy</color> the notes\\n"));
        Assert.That(character.AttentionNotes, Is.EqualTo(new[] { "Failed: Exit 1‹U+0003› and the rest of the log" }));
    }

    [Test]
    public void FailedVerificationQuotesTheTestSummary()
    {
        var execution = Samples.Execution("e1", "w1", ExecutionStatus.Completed);
        execution.LastTestRun = new TestRunResultView
        {
            TestRunId = "tests-1",
            Label = "pnpm test",
            Outcome = TestOutcome.Failed,
            Summary = "2 of 40 failed",
            CompletedAt = Samples.Time,
        };
        var workstream = Samples.Workstream("w1", WorkstreamStatus.Completed, "e1", AttentionLevel.ActionRequired, new VerificationFailedReason { ExecutionId = "e1", TestRunId = "tests-1" });
        var character = CharacterPresenter.Present(workstream, StateWith(workstream, execution), live: true);
        Assert.That(character.Activity, Is.EqualTo(CharacterActivity.TurnFinished));
        Assert.That(character.AttentionNotes, Is.EqualTo(new[] { "Tests failed: 2 of 40 failed" }));
    }

    [Test]
    public void RecordedAndStaleStatesAreFlagged()
    {
        var workstream = Samples.Workstream("w1");
        var character = CharacterPresenter.Present(workstream, StateWith(workstream, origin: JournalOrigin.Fixture), live: false);
        Assert.That(character.Activity, Is.EqualTo(CharacterActivity.Idle));
        Assert.That(character.Recorded, Is.True);
        Assert.That(character.Stale, Is.True);
        Assert.That(character.Synthetic, Is.False, "no execution, so nothing simulated yet");
    }
}

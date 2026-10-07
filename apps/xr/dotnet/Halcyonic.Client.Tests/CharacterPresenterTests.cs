using System;
using System.Collections.Generic;
using System.Linq;
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
        Assert.That(CharacterPresenter.LabelOf(CharacterActivity.TurnFinished), Is.EqualTo("Finished this round"));
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
        Assert.That(character.StatusLabel, Is.EqualTo("Waiting for you"));
        Assert.That(character.Attention, Is.EqualTo(AttentionLevel.ActionRequired));
        Assert.That(character.PendingApprovals, Is.EqualTo(1));
        Assert.That(character.AttentionNotes, Is.EqualTo(new[] { "It wants to run: Run the migration" }));
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
            Is.EqualTo(new[] { "Couldn't finish this round. Tell it to try again, or what to do instead." }), "the way on, never the agent app's own error");

        var unknown = Samples.Execution("e2", "w2", ExecutionStatus.Unknown);
        unknown.StatusReason = new ErrorInfo { Code = "control_plane_restarted", Message = "The control plane restarted." };
        var unknownStream = Samples.Workstream("w2", WorkstreamStatus.Unknown, "e2", AttentionLevel.Notice, new ExecutionStateUnknownReason { ExecutionId = "e2" });
        var character = CharacterPresenter.Present(unknownStream, StateWith(unknownStream, unknown), live: true);
        Assert.That(character.Activity, Is.EqualTo(CharacterActivity.Unknown));
        Assert.That(character.AttentionNotes, Is.EqualTo(new[] { "Can't tell what it's doing: your computer restarted and lost touch with the agent app." }));
    }

    /// <summary>
    /// Why it can't tell is said by the reason's code, never its message: an agent app's diagnostic names
    /// the app, a request and a path, as an old task's did on the headset on 2026-10-04.
    /// </summary>
    [TestCase("runtime_connection_lost", "Can't tell what it's doing: your computer lost touch with the agent app.", "Your computer lost touch with the agent app.")]
    [TestCase("control_plane_restarted", "Can't tell what it's doing: your computer restarted and lost touch with the agent app.",
        "Your computer restarted and lost touch with the agent app.")]
    [TestCase("start_outcome_unknown", "Can't tell what it's doing: not sure it started.", "Not sure it started.")]
    [TestCase("a_code_from_later", "Can't tell what it's doing right now.", "")]
    public void WhyItCantTellIsSaidByTheReasonsCodeNeverTheAgentAppsDiagnostic(string code, string note, string why)
    {
        var unknown = Samples.Execution("e2", "w2", ExecutionStatus.Unknown);
        unknown.StatusReason = new ErrorInfo
        {
            Code = code,
            Message = "After the OpenCode event stream reconnected, the session could not be read (GET /api/session/ses_2f1a9c: fetch failed).",
        };
        var stream = Samples.Workstream("w2", WorkstreamStatus.Unknown, "e2", AttentionLevel.Notice, new ExecutionStateUnknownReason { ExecutionId = "e2" });
        var character = CharacterPresenter.Present(stream, StateWith(stream, unknown), live: true);
        Assert.That(character.AttentionNotes, Is.EqualTo(new[] { note }));
        Assert.That(StateLanguage.CantTellWhy(code) ?? "", Is.EqualTo(why));
        foreach (var leaked in new[] { "OpenCode", "GET", "/api/", "ses_", "stream" })
        {
            Assert.That(character.AttentionNotes.Single(), Does.Not.Contain(leaked));
        }
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
        Assert.That(character.AttentionNotes, Is.EqualTo(new[] { "Couldn't finish this round. Tell it to try again, or what to do instead." }), "the reason's message is never shown");
    }

    /// <summary>
    /// Every refusal your computer can give is said by its code, with the way on, and none by the control
    /// plane's message, which is written for developers; the demonstration speaks in its own words.
    /// </summary>
    [Test]
    public void EveryRefusalIsSaidByItsCodeNeverTheControlPlanesMessage()
    {
        foreach (RejectionCode code in Enum.GetValues(typeof(RejectionCode)))
        {
            var why = WorkspaceText.WhyRefused(code);
            if (code == RejectionCode.Demonstration)
            {
                Assert.That(why, Is.Null);
                continue;
            }
            Assert.That(why, Is.Not.Null.And.EndsWith("."), code.ToString());
            // Every refusal ends in a way on (WORDS.md rule 7): a second sentence saying what to do.
            Assert.That(why!.Split(new[] { ". " }, StringSplitOptions.None).Length, Is.GreaterThan(1), code.ToString());
            Assert.That(why, Does.Not.Contain("runtime").IgnoreCase.And.Not.Contain("control plane").IgnoreCase.And.Not.Contain("execution").IgnoreCase, code.ToString());
        }
        Assert.That(WorkspaceText.Couldnt("Couldn't connect", null), Is.EqualTo("Couldn't connect."), "no why: the lead alone, ended");
        Assert.That(WorkspaceText.Couldnt("Couldn't connect", WorkspaceText.WhyRefused(RejectionCode.ProjectNotFound)),
            Is.EqualTo("Couldn't connect: this project isn't on your computer any more. Choose another in Projects."));
    }

    /// <summary>
    /// Why a task couldn't start or finish is said from what Halcyonic knows, with the way on: a folder's
    /// problem by its code, anything else by what can be done next, never the agent app's own error.
    /// </summary>
    [Test]
    public void WhyItCouldntStartOrFinishIsSaidByWhatCanBeDoneNeverTheAgentAppsError()
    {
        const string Leak = "codex_internal_server_error: POST https://api.example/v1/responses 500 at /Users/someone/.codex/sessions/rollout.jsonl";
        string[] Notes(ExecutionView execution, ClientProjection? state = null)
        {
            var stream = Samples.Workstream("w1", WorkstreamStatus.Failed, "e1", AttentionLevel.ActionRequired, new ExecutionFailedReason { ExecutionId = "e1" });
            var character = CharacterPresenter.Present(stream, state ?? StateWith(stream, execution), live: true);
            foreach (var line in character.AttentionNotes.Concat(character.AttentionDetails)) Assert.That(line, Does.Not.Contain("codex").And.Not.Contain("POST").And.Not.Contain("/Users"));
            return character.AttentionNotes.ToArray();
        }
        ExecutionView Failed(string code, bool started)
        {
            var execution = Samples.Execution("e1", "w1", ExecutionStatus.Failed);
            execution.StatusReason = new ErrorInfo { Code = code, Message = Leak };
            if (!started)
            {
                execution.StartedAt = null;
                execution.TurnCount = 0;
            }
            return execution;
        }

        Assert.That(Notes(Failed("codex_internal_server_error", started: true)), Is.EqualTo(new[] { "Couldn't finish this round. Tell it to try again, or what to do instead." }), "Tell it is offered after a failed round");
        Assert.That(Notes(Failed("location_missing", started: false)), Is.EqualTo(new[]
        {
            "Couldn't start: your computer can't use that folder now. Choose it again, or fix it on your computer.",
        }), "a folder's problem, by its code");
        Assert.That(Notes(Failed("codex_internal_server_error", started: false)), Is.EqualTo(new[] { "Couldn't start. Add the task again in Projects to try again." }));

        // Where the agent app isn't on the computer now, nothing can be told to it.
        var gone = Failed("opencode_execution_failed", started: true);
        var stream = Samples.Workstream("w1", WorkstreamStatus.Failed, "e1", AttentionLevel.ActionRequired, new ExecutionFailedReason { ExecutionId = "e1" });
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(1, new[] { stream }, new[] { gone });
        snapshot.Runtimes.Clear();
        state.ApplySnapshot(snapshot, new StateChanges());
        Assert.That(Notes(gone, state), Is.EqualTo(new[] { "Couldn't finish this round. Add the task again in Projects to try again." }));
    }

    /// <summary>
    /// A title carrying the code points of Halcyonic's own icons, Waiting for you's and Finished this
    /// round's, spells them, so it can never draw a state's icon among its words (ADR 0023).
    /// </summary>
    [Test]
    public void ATitleCarryingAGlazeIconsCodePointSpellsIt()
    {
        var workstream = Samples.Workstream("w1", WorkstreamStatus.Running, null, AttentionLevel.None);
        workstream.Title = "\uE769 Fix login \uE153";
        var character = CharacterPresenter.Present(workstream, StateWith(workstream), live: true);
        Assert.That(character.Title, Is.EqualTo("‹U+E769› Fix login ‹U+E153›"));
        Assert.That(CharacterLabel.Of(character).Title, Is.EqualTo("‹U+E769› Fix login ‹U+E153›"));
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
        Assert.That(character.AttentionNotes, Is.EqualTo(new[] { "Checks: 2 of 40 failed" }));
    }

    [Test]
    public void RecordedAndStaleStatesAreFlagged()
    {
        var workstream = Samples.Workstream("w1");
        var character = CharacterPresenter.Present(workstream, StateWith(workstream, origin: JournalOrigin.Fixture), live: false);
        Assert.That(character.Activity, Is.EqualTo(CharacterActivity.Idle));
        Assert.That(character.Recorded, Is.True);
        Assert.That(character.Stale, Is.True);
        Assert.That(character.Synthetic, Is.True, "no execution yet, and only the mock runtime could run it");
        Assert.That(StateLanguage.MarksOf(character).Single().Word, Is.EqualTo("Demo"), "the same mark it takes once it starts");
    }

    [Test]
    public void ATaskNotYetStartedIsSimulatedOnlyWhenEveryRuntimeThatCouldRunItIs()
    {
        var workstream = Samples.Workstream("w1");
        ClientProjection With(params RuntimeDescriptor[] runtimes)
        {
            var snapshot = Samples.Snapshot(1, new[] { workstream });
            snapshot.Runtimes = runtimes.ToList();
            var state = new ClientProjection();
            state.ApplySnapshot(snapshot, new StateChanges());
            return state;
        }
        var real = Samples.MockRuntime();
        real.RuntimeId = "claude";
        real.Kind = "claude-agent";
        real.DisplayName = "Claude Code";
        real.Synthetic = false;

        Assert.That(CharacterPresenter.Present(workstream, With(Samples.MockRuntime()), live: true).Synthetic, Is.True);
        var mixed = CharacterPresenter.Present(workstream, With(Samples.MockRuntime(), real), live: true);
        Assert.That(mixed.Synthetic, Is.False, "a real runtime could run it, so nothing is claimed");
        Assert.That(StateLanguage.MarksOf(mixed), Is.Empty);
        var onlyReal = CharacterPresenter.Present(workstream, With(real), live: true);
        Assert.That((onlyReal.Synthetic, StateLanguage.MarksOf(onlyReal).Count), Is.EqualTo((false, 0)), "a live computer with one real runtime marks nothing");
        Assert.That(CharacterPresenter.Present(workstream, With(), live: true).Synthetic, Is.False, "with no runtime, nothing is known");

        // Once it has run, its own execution decides, though only the mock runtime is registered.
        var ran = Samples.Workstream("w1", WorkstreamStatus.Completed, "e1");
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(1, new[] { ran }, new[] { Samples.Execution("e1", "w1", ExecutionStatus.Completed, synthetic: false) }), new StateChanges());
        Assert.That(CharacterPresenter.Present(ran, state, live: true).Synthetic, Is.False);
    }
}

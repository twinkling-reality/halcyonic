using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>A workstream waiting for approval of one tool use, on the mock runtime.</summary>
internal sealed class WaitingWork
{
    public const string ApprovalId = "approval-1";

    public WaitingWork(bool welcomed = true, IEnumerable<CommandPolicy>? policies = null)
    {
        Workstream = Samples.Workstream(
            "w1",
            WorkstreamStatus.WaitingForHuman,
            "e1",
            AttentionLevel.ActionRequired,
            new ApprovalPendingReason { ExecutionId = "e1", ApprovalId = ApprovalId });
        Execution = Samples.Execution("e1", "w1", ExecutionStatus.WaitingForHuman);
        Execution.PendingApprovals.Add(Approval(ApprovalId, "Run the migration", Samples.Time));
        State.ApplySnapshot(Samples.Snapshot(1, new[] { Workstream }, new[] { Execution }), new StateChanges());
        if (welcomed)
        {
            var welcome = Samples.Welcome(resumed: false, head: 1);
            if (policies != null) welcome.CommandPolicies = policies.ToList();
            State.ApplyWelcome(welcome);
        }
    }

    public ClientProjection State { get; } = new();

    public ActivityLog Activity { get; } = new();

    public WorkstreamView Workstream { get; }

    public ExecutionView Execution { get; }

    public static ApprovalView Approval(string id, string summary, string requestedAt) => new()
    {
        ApprovalId = id,
        Subject = new ToolUseSubject { ToolName = "bash", Summary = summary },
        RequestedAt = requestedAt,
    };

    public WorkspacePresentation Present(bool live = true, CommandSubmissions? submissions = null) =>
        WorkspacePresenter.Present(Workstream, State, Activity, live, submissions);

    /// <summary>Replaces the execution and workstream the way an event's changes would.</summary>
    public void Change(Action<ExecutionView> execution, WorkstreamStatus? status = null)
    {
        execution(Execution);
        if (status != null)
        {
            Workstream.Status = status.Value;
            Workstream.Attention = new Attention { Level = AttentionLevel.None, Reasons = new List<AttentionReason>() };
        }
        State.ApplySnapshot(Samples.Snapshot(State.Position + 1, new[] { Workstream }, new[] { Execution }), new StateChanges());
    }
}

public class WorkspaceTextTests
{
    private static ActivityEntry Entry(long position, ActivityKind kind, string text, bool reported = false) =>
        new(position, "2026-09-26T09:00:0" + position + ".000Z", kind, text, reported);

    [Test]
    public void TheStatusLineWritesOutEveryQualifier()
    {
        var work = new WaitingWork();
        Assert.That(WorkspaceText.StatusLine(work.Present().Character), Is.EqualTo("Needs you · simulated"));
        Assert.That(WorkspaceText.StatusLine(work.Present(live: false).Character), Is.EqualTo("Needs you · simulated · last known"));

        work.Change(execution => execution.PendingApprovals.Add(WaitingWork.Approval("approval-2", "Drop the table", Samples.Time)));
        Assert.That(WorkspaceText.StatusLine(work.Present().Character), Is.EqualTo("Needs you (2 approvals) · simulated"));
    }

    [Test]
    public void TheExecutionLineNamesItsRuntimeAndSaysWhenItIsGone()
    {
        var work = new WaitingWork();
        Assert.That(WorkspaceText.Execution(work.Present()), Is.EqualTo("On Mock runtime, simulated work · 1 turn"));

        work.Change(execution => execution.TurnCount = 3);
        Assert.That(WorkspaceText.Execution(work.Present()), Is.EqualTo("On Mock runtime, simulated work · 3 turns"));

        var replayed = Samples.Snapshot(9, new[] { work.Workstream }, new[] { work.Execution });
        replayed.Runtimes.Clear();
        work.State.ApplySnapshot(replayed, new StateChanges());
        Assert.That(WorkspaceText.Execution(work.Present()), Is.EqualTo("On Mock runtime, simulated work · 3 turns · the runtime is not available here"));
        Assert.That(WorkspaceText.WhyNoActions(work.Present()), Is.EqualTo("Nothing can be sent: the runtime is not available here."),
            "a recording carries no runtime, as in a replay or the demonstration");

        var idle = WorkspacePresenter.Present(Samples.Workstream("w2"), work.State, work.Activity, live: true);
        Assert.That(WorkspaceText.Execution(idle), Is.EqualTo("No execution yet."));
        Assert.That(WorkspaceText.Objective(idle), Is.EqualTo("No objective was given."));
    }

    [Test]
    public void ActivityLinesCarryTheLocalTimeAndMarkClaims()
    {
        var utc = TimeZoneInfo.Utc;
        Assert.That(WorkspaceText.Activity(Entry(5, ActivityKind.Tool, "bash: Run the migration"), utc),
            Is.EqualTo("09:00:05  bash: Run the migration"));
        Assert.That(WorkspaceText.Activity(Entry(6, ActivityKind.Message, "All done.\n\nTests pass.", reported: true), utc),
            Is.EqualTo("09:00:06  Agent says: “All done. Tests pass.”"));

        var tokyo = TimeZoneInfo.CreateCustomTimeZone("plus-nine", TimeSpan.FromHours(9), "plus-nine", "plus-nine");
        Assert.That(WorkspaceText.Activity(Entry(7, ActivityKind.Turn, "Turn finished"), tokyo), Is.EqualTo("18:00:07  Turn finished"));
    }

    [Test]
    public void ThePeekSaysWhatTheWorkNeedsFirst()
    {
        var work = new WaitingWork();
        Assert.That(WorkspaceText.Peek(work.Present()), Is.EqualTo("Approval needed to use bash: Run the migration"));
        Assert.That(WorkspaceText.Peek(work.Present(live: false)), Is.EqualTo("Last known: Approval needed to use bash: Run the migration"));

        work.Workstream.Attention.Reasons.Add(new ExecutionFailedReason { ExecutionId = "e1" });
        Assert.That(WorkspaceText.Peek(work.Present()), Is.EqualTo("Approval needed to use bash: Run the migration (+1 more)"));
    }

    [Test]
    public void OtherwiseThePeekSaysWhatItDidLast()
    {
        var work = new WaitingWork();
        work.Change(execution =>
        {
            execution.Status = ExecutionStatus.Completed;
            execution.PendingApprovals.Clear();
        }, WorkstreamStatus.Completed);
        Assert.That(WorkspaceText.Peek(work.Present()), Is.EqualTo("Turn finished"), "nothing is known yet, so the status");

        var entries = new[]
        {
            Entry(1, ActivityKind.Tool, "bash succeeded"),
            Entry(2, ActivityKind.Message, "The migration ran\nand the tests pass.", reported: true),
            Entry(3, ActivityKind.Turn, "Turn finished"),
        };
        var presentation = new WorkspacePresentation(
            work.Present().Character, null, work.Execution, null, new WorkspaceAction[0], new WorkspaceAction[0],
            new CommandFeedback[0], entries);
        Assert.That(WorkspaceText.Peek(presentation), Is.EqualTo("Agent says: “The migration ran and the tests pass.”"),
            "turn boundaries are skipped and agent text is a claim");
        Assert.That(WorkspaceText.Peek(presentation, maxLength: 20), Is.EqualTo("Agent says: “The mi…"));
    }

    [Test]
    public void TruncationKeepsShortTextAndMarksCuts()
    {
        Assert.That(WorkspaceText.Truncate("Working", 7), Is.EqualTo("Working"));
        Assert.That(WorkspaceText.Truncate("Running tests", 8), Is.EqualTo("Running…"));
        Assert.That(WorkspaceText.OneLine("  one\r\n two\tthree  "), Is.EqualTo("one two three"));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkspaceText.Truncate("x", 0));
    }

    [Test]
    public void SaysWhyNothingCanBeDone()
    {
        var work = new WaitingWork();
        Assert.That(WorkspaceText.WhyNoActions(work.Present()), Is.Null);
        Assert.That(WorkspaceText.WhyNoActions(work.Present(live: false)), Is.EqualTo("Nothing can be sent until the connection is live again."));

        work.Change(execution =>
        {
            execution.Status = ExecutionStatus.Unknown;
            execution.PendingApprovals.Clear();
        }, WorkstreamStatus.Unknown);
        Assert.That(WorkspaceText.WhyNoActions(work.Present()), Is.EqualTo("Nothing can be sent while its state is unknown."));

        var idle = WorkspacePresenter.Present(Samples.Workstream("w2"), work.State, work.Activity, live: true);
        Assert.That(WorkspaceText.WhyNoActions(idle), Is.EqualTo("Nothing to steer until work starts."));
    }

    [Test]
    public void EveryActionHasALabelAPromptAndAConfirmation()
    {
        var approval = WaitingWork.Approval("a", "Run the migration", Samples.Time);
        foreach (var action in Enum.GetValues<WorkspaceAction>())
        {
            Assert.That(WorkspaceText.Label(action), Is.Not.Empty);
            Assert.That(WorkspaceText.ConfirmLabel(action), Does.StartWith("Yes, "));
            Assert.That(WorkspaceText.ConfirmationPrompt(action, approval, "Add a test."), Does.Contain("?"));
        }
        Assert.That(WorkspaceText.ConfirmationPrompt(WorkspaceAction.Approve, approval, null), Is.EqualTo("Approve this request? bash: Run the migration"));
        Assert.That(WorkspaceText.ConfirmationPrompt(WorkspaceAction.Instruct, null, "Add\na test."), Is.EqualTo("Send this instruction? “Add a test.”"));
    }

    [Test]
    public void TheAppsOwnWordsAreShortAndNameNoBrand()
    {
        Assert.That(WorkspaceText.OpenHint.Split(' '), Has.Length.LessThanOrEqualTo(3), "the first-time hint is no wall of text");

        var approval = WaitingWork.Approval("a", "", Samples.Time);
        var words = new List<string> { WorkspaceText.OpenHint, WorkspaceText.TypingPrompt, WorkspaceText.PresetPrompt };
        foreach (var action in Enum.GetValues<WorkspaceAction>())
        {
            words.Add(WorkspaceText.Label(action));
            words.Add(WorkspaceText.ConfirmLabel(action));
            words.Add(WorkspaceText.ConfirmationPrompt(action, approval, ""));
        }
        words.AddRange(WorkspaceText.PresetInstructions.SelectMany(preset => new[] { preset.Label, preset.Text }));
        var brands = new[] { "Meta", "Quest", "Oculus", "Horizon", "Unity", "Claude", "Anthropic", "Codex", "OpenAI", "OpenCode" };
        foreach (var word in words)
        {
            foreach (var brand in brands) Assert.That(word, Does.Not.Contain(brand), "data may name a runtime; the app's own words do not");
        }
    }

    [Test]
    public void PresetInstructionsAreShortButtonsForWholeSentences()
    {
        Assert.That(WorkspaceText.PresetInstructions, Is.Not.Empty);
        foreach (var preset in WorkspaceText.PresetInstructions)
        {
            Assert.That(preset.Label.Length, Is.InRange(1, 12), "a label fits a button");
            Assert.That(preset.Text, Does.EndWith("."), "what is sent is a whole instruction");
        }
    }
}

public class CommandSubmissionsTests
{
    private static CommandView Record(CommandEnvelope command, CommandStatus status, string executionId = "e1") => new()
    {
        CommandId = command.CommandId,
        CommandType = CommandType.ExecutionInterrupt,
        Status = status,
        ProjectId = Samples.ProjectId,
        WorkstreamId = "w1",
        ExecutionId = executionId,
        IssuedAt = command.IssuedAt,
        UpdatedAt = command.IssuedAt,
        Rejection = status == CommandStatus.Rejected ? new CommandRejection { Code = RejectionCode.InvalidState, Message = "Nothing is running." } : null,
        Failure = null,
        Result = null,
    };

    private static EventMessage Carrying(long position, CommandView command) => new()
    {
        Position = position,
        Event = HalcyonicJson.Deserialize<EventEnvelope>(Repository.Trace()[0]),
        Changes = new EntityChanges
        {
            Projects = new List<ProjectView>(),
            Workstreams = new List<WorkstreamView>(),
            Executions = new List<ExecutionView>(),
            Commands = new List<CommandView> { command },
        },
    };

    [Test]
    public void ACommandIsDescribedByThisClientUntilTheControlPlanesRecordTakesOver()
    {
        var work = new WaitingWork();
        var submissions = new CommandSubmissions();
        var command = new CommandFactory(Samples.Client).Interrupt("e1");

        submissions.Sending(command, "e1");
        var sending = work.Present(submissions: submissions).Commands.Single();
        Assert.That(sending.Text, Is.EqualTo("Sending to the control plane…"));
        Assert.That(sending.Status, Is.Null, "only this client knows about it");

        work.State.ApplyEvent(Carrying(2, Record(command, CommandStatus.Accepted)), new StateChanges());
        Assert.That(work.Present(submissions: submissions).Commands.Single().Text, Is.EqualTo("Stopping the turn…"), "accepted is not done");

        work.State.ApplyEvent(Carrying(3, Record(command, CommandStatus.Completed)), new StateChanges());
        var done = work.Present(submissions: submissions).Commands.Single();
        Assert.That(done.Text, Is.EqualTo("Turn stopped"));
        Assert.That(done.Status, Is.EqualTo(CommandStatus.Completed));

        work.State.ApplySnapshot(Samples.Snapshot(4, new[] { work.Workstream }, new[] { work.Execution }), new StateChanges());
        Assert.That(work.Present(submissions: submissions).Commands, Is.Empty,
            "once the record was seen, this client's older view never speaks again");
    }

    [Test]
    public void FeedbackMixesOtherClientsCommandsNewestFirstForThisExecutionOnly()
    {
        var work = new WaitingWork();
        var submissions = new CommandSubmissions();
        var clock = new DateTimeOffset(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);
        var factory = new CommandFactory(Samples.Client, () => clock = clock.AddSeconds(1));
        var byAnotherClient = factory.Interrupt("e1");
        var mine = factory.Interrupt("e1");
        var elsewhere = factory.Interrupt("e2");

        work.State.ApplyEvent(Carrying(2, Record(byAnotherClient, CommandStatus.Rejected)), new StateChanges());
        submissions.Sending(mine, "e1");
        submissions.NotSent(mine.CommandId, "not connected to the control plane.");
        submissions.Sending(elsewhere, "e2");

        Assert.That(submissions.FeedbackFor("e1", work.State, 5).Select(feedback => feedback.Text), Is.EqualTo(new[]
        {
            "Not sent: not connected to the control plane.",
            "Refused: Nothing is running.",
        }));
        Assert.That(submissions.FeedbackFor("e1", work.State, 1), Has.Count.EqualTo(1));
    }

    [Test]
    public void TheVersionChangesWithEverySubmissionChange()
    {
        var submissions = new CommandSubmissions();
        var command = new CommandFactory(Samples.Client).Interrupt("e1");
        var before = submissions.Version;
        submissions.Sending(command, "e1");
        submissions.OutcomeUnknown(command.CommandId, "The connection closed before the command was acknowledged.");
        Assert.That(submissions.Version, Is.EqualTo(before + 2));
        Assert.That(submissions.StateOf(command.CommandId), Is.EqualTo(SubmissionState.OutcomeUnknown));
        submissions.NotSent("never-sent", "unknown command");
        Assert.That(submissions.Version, Is.EqualTo(before + 2), "a command this client never sent changes nothing");
    }

    [Test]
    public void OnlyTheMostRecentSubmissionsAreKept()
    {
        var submissions = new CommandSubmissions(capacity: 2);
        var factory = new CommandFactory(Samples.Client);
        var commands = Enumerable.Range(0, 3).Select(_ => factory.Interrupt("e1")).ToList();
        foreach (var command in commands) submissions.Sending(command, "e1");
        Assert.That(submissions.StateOf(commands[0].CommandId), Is.Null);
        Assert.That(submissions.StateOf(commands[2].CommandId), Is.EqualTo(SubmissionState.Sending));
    }
}

/// <summary>Submissions through a real session against the in-memory server, so the exceptions are the real ones.</summary>
public class CommandSubmissionsSessionTests
{
    private FakeServer server = null!;
    private RealtimeSession session = null!;

    [SetUp]
    public void StartSession()
    {
        server = new FakeServer();
        session = new RealtimeSession(
            new RealtimeSessionOptions(new Uri("ws://127.0.0.1:1/realtime"), "test-token", Samples.Client)
            {
                PingInterval = TimeSpan.FromHours(1),
                CommandAckTimeout = TimeSpan.FromSeconds(2),
                InitialRetryDelay = TimeSpan.FromMilliseconds(10),
                MaxRetryDelay = TimeSpan.FromMilliseconds(40),
            },
            server.CreateTransport);
    }

    [TearDown]
    public async Task StopSession() => await session.StopAsync();

    private async Task<FakeConnection> ConnectLiveAsync()
    {
        session.Start();
        var connection = await server.AcceptAsync();
        await connection.WelcomeWithSnapshotAsync(Samples.Snapshot(1));
        await Pumping.Until(session, s => s.Status.IsLive, "the session is live");
        return connection;
    }

    [Test]
    public async Task AnAcceptedCommandIsInProgressNotDone()
    {
        var connection = await ConnectLiveAsync();
        var submissions = new CommandSubmissions();
        var command = new CommandFactory(Samples.Client).Interrupt("e1");

        var submitting = submissions.SubmitAsync(c => session.SubmitAsync(c), command, "e1");
        await connection.ReceiveFromClientAsync();
        Assert.That(submissions.StateOf(command.CommandId), Is.EqualTo(SubmissionState.Sending));
        connection.Send(new CommandAckMessage { CommandId = command.CommandId, Disposition = CommandAckDisposition.Accepted, Command = null });
        await submitting;

        Assert.That(submissions.StateOf(command.CommandId), Is.EqualTo(SubmissionState.Acknowledged));
        Assert.That(submissions.FeedbackFor("e1", session.State, 5).Single().Text, Is.EqualTo("Stopping the turn…"));
    }

    [Test]
    public async Task ARefusalIsWrittenOut()
    {
        var connection = await ConnectLiveAsync();
        var submissions = new CommandSubmissions();
        var command = new CommandFactory(Samples.Client).Interrupt("e1");

        var submitting = submissions.SubmitAsync(c => session.SubmitAsync(c), command, "e1");
        await connection.ReceiveFromClientAsync();
        connection.Send(new CommandAckMessage { CommandId = command.CommandId, Disposition = CommandAckDisposition.Rejected, Command = null });
        await submitting;

        Assert.That(submissions.FeedbackFor("e1", session.State, 5).Single().Text, Is.EqualTo("Refused by the control plane."));
    }

    [Test]
    public async Task ACommandCutOffByADisconnectHasAnUnknownOutcome()
    {
        var connection = await ConnectLiveAsync();
        var submissions = new CommandSubmissions();
        var command = new CommandFactory(Samples.Client).Interrupt("e1");

        var submitting = submissions.SubmitAsync(c => session.SubmitAsync(c), command, "e1");
        await connection.ReceiveFromClientAsync();
        connection.Close("1006 abnormal closure");
        await submitting;

        Assert.That(submissions.StateOf(command.CommandId), Is.EqualTo(SubmissionState.OutcomeUnknown));
        Assert.That(submissions.FeedbackFor("e1", session.State, 5).Single().Text, Is.EqualTo(
            "Outcome unknown: The connection closed before the command was acknowledged. If it arrived, it shows here after reconnecting."));
    }

    [Test]
    public async Task WithoutAConnectionNothingIsSent()
    {
        var submissions = new CommandSubmissions();
        var command = new CommandFactory(Samples.Client).Interrupt("e1");
        await submissions.SubmitAsync(c => session.SubmitAsync(c), command, "e1");
        Assert.That(submissions.StateOf(command.CommandId), Is.EqualTo(SubmissionState.NotSent));
        Assert.That(submissions.FeedbackFor("e1", session.State, 5).Single().Text, Is.EqualTo("Not sent: not connected to the control plane."));
    }
}

public class WorkspaceSteeringTests
{
    private readonly CommandFactory factory = new(Samples.Client);
    private DateTimeOffset now = new(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);

    private WorkspaceSteering Steering() => new(factory, () => now, TimeSpan.FromSeconds(15));

    [Test]
    public void ApprovingNeedsASecondDeliberatePressThatSendsExactlyThatAnswer()
    {
        var work = new WaitingWork();
        var steering = Steering();

        var first = steering.Press(WorkspaceAction.Approve, work.Present());
        Assert.That(first.Step, Is.EqualTo(SteeringStep.Confirm));
        Assert.That(steering.Armed, Is.EqualTo(WorkspaceAction.Approve));
        Assert.That(steering.Prompt(work.Present()), Is.EqualTo("Approve this request? bash: Run the migration"));

        var again = steering.Press(WorkspaceAction.Approve, work.Present());
        Assert.That(again.Step, Is.EqualTo(SteeringStep.Confirm), "pressing the same action again never sends");

        var confirmed = steering.Confirm(work.Present());
        Assert.That(confirmed.Step, Is.EqualTo(SteeringStep.Send));
        var command = (ExecutionRespondToApprovalCommand)confirmed.Command!;
        Assert.That(command.Payload.ExecutionId, Is.EqualTo("e1"));
        Assert.That(command.Payload.ApprovalId, Is.EqualTo(WaitingWork.ApprovalId));
        Assert.That(command.Payload.Decision, Is.EqualTo(ApprovalDecision.Approve));
        Assert.That(steering.Armed, Is.Null);
        Assert.That(steering.Confirm(work.Present()).Step, Is.EqualTo(SteeringStep.None), "a confirmation is used once");
    }

    [Test]
    public void DenyingAndStoppingAreConfirmedToo()
    {
        var work = new WaitingWork();
        var steering = Steering();
        steering.Press(WorkspaceAction.Deny, work.Present());
        var deny = (ExecutionRespondToApprovalCommand)steering.Confirm(work.Present()).Command!;
        Assert.That(deny.Payload.Decision, Is.EqualTo(ApprovalDecision.Deny));

        steering.Press(WorkspaceAction.Interrupt, work.Present());
        Assert.That(steering.Prompt(work.Present()), Does.StartWith("Stop the current turn?"));
        Assert.That(steering.Confirm(work.Present()).Command, Is.TypeOf<ExecutionInterruptCommand>());
    }

    [Test]
    public void TheOldestPendingApprovalIsTheOneAnswered()
    {
        var work = new WaitingWork();
        work.Change(execution => execution.PendingApprovals.Insert(0, WaitingWork.Approval("approval-0", "Read the schema", "2026-09-26T08:59:00.000Z")));
        var steering = Steering();
        steering.Press(WorkspaceAction.Approve, work.Present());
        Assert.That(steering.ArmedApprovalId, Is.EqualTo("approval-0"));
        Assert.That(steering.Prompt(work.Present()), Does.EndWith("bash: Read the schema"));
    }

    [Test]
    public void ALowConsequenceActionIsSentAtOnce()
    {
        var policies = new[]
        {
            new CommandPolicy { CommandType = CommandType.ExecutionRespondToApproval, Policy = PolicyCategory.ReviewRequired },
            new CommandPolicy { CommandType = CommandType.ExecutionInterrupt, Policy = PolicyCategory.LowConsequence },
        };
        var work = new WaitingWork(policies: policies);
        var outcome = Steering().Press(WorkspaceAction.Interrupt, work.Present());
        Assert.That(outcome.Step, Is.EqualTo(SteeringStep.Send));
        Assert.That(outcome.Command, Is.TypeOf<ExecutionInterruptCommand>());
    }

    [Test]
    public void UnknownPoliciesAreConfirmed()
    {
        var work = new WaitingWork(welcomed: false);
        Assert.That(Steering().Press(WorkspaceAction.Interrupt, work.Present()).Step, Is.EqualTo(SteeringStep.Confirm));
    }

    [Test]
    public void AnActionThatIsNotOfferedSendsNothingAndSaysWhy()
    {
        var work = new WaitingWork();
        var steering = Steering();
        var offline = steering.Press(WorkspaceAction.Approve, work.Present(live: false));
        Assert.That(offline.Step, Is.EqualTo(SteeringStep.Explain));
        Assert.That(offline.Message, Is.EqualTo("Nothing can be sent until the connection is live again."));

        var instruct = steering.Press(WorkspaceAction.Instruct, work.Present());
        Assert.That(instruct.Step, Is.EqualTo(SteeringStep.Explain), "the mock runtime takes no instruction while a turn runs");
        Assert.That(instruct.Message, Is.EqualTo("That is no longer possible, so nothing was sent."));
    }

    [Test]
    public void AConfirmationLapsesAfterItsWindow()
    {
        var work = new WaitingWork();
        var steering = Steering();
        steering.Press(WorkspaceAction.Approve, work.Present());
        now = now.AddSeconds(14);
        Assert.That(steering.Refresh(work.Present()), Is.Null);
        now = now.AddSeconds(2);
        var late = steering.Confirm(work.Present());
        Assert.That(late.Step, Is.EqualTo(SteeringStep.Explain));
        Assert.That(late.Message, Is.EqualTo("The confirmation timed out, so nothing was sent."));
        Assert.That(steering.Armed, Is.Null);
    }

    [Test]
    public void AConfirmationLapsesWhenItsApprovalWasAnsweredElsewhere()
    {
        var work = new WaitingWork();
        var steering = Steering();
        steering.Press(WorkspaceAction.Approve, work.Present());

        work.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.PendingApprovals.Add(WaitingWork.Approval("approval-2", "Drop the old table", "2026-09-26T09:05:00.000Z"));
        });
        Assert.That(steering.Refresh(work.Present()), Is.EqualTo("That approval was already answered, so nothing was sent."),
            "a new approval is a new question");
        Assert.That(steering.Armed, Is.Null);

        steering.Press(WorkspaceAction.Approve, work.Present());
        work.Change(execution =>
        {
            execution.Status = ExecutionStatus.Running;
            execution.PendingApprovals.Clear();
        }, WorkstreamStatus.Running);
        var stale = steering.Confirm(work.Present());
        Assert.That(stale.Step, Is.EqualTo(SteeringStep.Explain));
        Assert.That(stale.Message, Is.EqualTo("The state changed before the confirmation, so nothing was sent."));
    }

    [Test]
    public void AnInstructionIsTypedAndSentWhenThePolicyAllowsIt()
    {
        var work = new WaitingWork();
        work.Change(execution =>
        {
            execution.Status = ExecutionStatus.Completed;
            execution.PendingApprovals.Clear();
        }, WorkstreamStatus.Completed);
        var steering = Steering();

        Assert.That(steering.Press(WorkspaceAction.Instruct, work.Present()).Step, Is.EqualTo(SteeringStep.Type));
        Assert.That(steering.Typing, Is.True);
        Assert.That(steering.Press(WorkspaceAction.Instruct, work.Present()).Step, Is.EqualTo(SteeringStep.None), "one keyboard at a time");

        var empty = steering.Typed("   ", work.Present());
        Assert.That(empty.Step, Is.EqualTo(SteeringStep.Explain));
        Assert.That(steering.Typing, Is.False);

        steering.Press(WorkspaceAction.Instruct, work.Present());
        var sent = steering.Typed("  Add a test for the expiry.  ", work.Present());
        Assert.That(sent.Step, Is.EqualTo(SteeringStep.Send));
        var command = (ExecutionSendInstructionCommand)sent.Command!;
        Assert.That(command.Payload.Text, Is.EqualTo("Add a test for the expiry."));
        Assert.That(command.Payload.ExecutionId, Is.EqualTo("e1"));

        steering.Press(WorkspaceAction.Instruct, work.Present());
        var offline = steering.Typed("Add another test.", work.Present(live: false));
        Assert.That(offline.Step, Is.EqualTo(SteeringStep.Explain), "the connection dropped while typing");
        Assert.That(offline.Message, Is.EqualTo("Nothing can be sent until the connection is live again."));
    }

    [Test]
    public void AnInstructionIsConfirmedWhenThePolicyAsks()
    {
        var work = new WaitingWork(welcomed: false);
        work.Change(execution =>
        {
            execution.Status = ExecutionStatus.Completed;
            execution.PendingApprovals.Clear();
        }, WorkstreamStatus.Completed);
        var steering = Steering();
        steering.Press(WorkspaceAction.Instruct, work.Present());
        Assert.That(steering.Typed("Add a test.", work.Present()).Step, Is.EqualTo(SteeringStep.Confirm));
        Assert.That(steering.Prompt(work.Present()), Is.EqualTo("Send this instruction? “Add a test.”"));
        var command = (ExecutionSendInstructionCommand)steering.Confirm(work.Present()).Command!;
        Assert.That(command.Payload.Text, Is.EqualTo("Add a test."));
    }

    [Test]
    public void CancellingDropsWhatWasArmed()
    {
        var work = new WaitingWork();
        var steering = Steering();
        steering.Press(WorkspaceAction.Deny, work.Present());
        steering.Cancel();
        Assert.That(steering.Armed, Is.Null);
        Assert.That(steering.Prompt(work.Present()), Is.Null);
        Assert.That(steering.Confirm(work.Present()).Step, Is.EqualTo(SteeringStep.None));
    }
}

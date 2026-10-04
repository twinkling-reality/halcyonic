using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class ActivityLogTests
{
    private static List<StoredEvent> Load(string file) =>
        System.IO.File.ReadAllLines(Repository.PathTo("fixtures/traces/" + file))
            .Where(line => line.Length > 0)
            .Select((line, index) => new StoredEvent { Position = index + 1, Event = HalcyonicJson.Deserialize<EventEnvelope>(line) })
            .ToList();

    private static string ExecutionOf(List<StoredEvent> events, string workstreamTitle)
    {
        var workstreamId = events.Select(stored => stored.Event).OfType<WorkstreamCreatedEvent>()
            .Single(created => created.Payload.Title == workstreamTitle).WorkstreamId!;
        return events.Select(stored => stored.Event).OfType<ExecutionCreatedEvent>()
            .Single(created => created.WorkstreamId == workstreamId).ExecutionId!;
    }

    [Test]
    public void DescribesAnApprovalRoundTripInWords()
    {
        var events = Load("multiple_workstreams.jsonl");
        var log = new ActivityLog();
        log.Record(events);

        var texts = log.For(ExecutionOf(events, "Move sessions to their own table")).Select(entry => entry.Text).ToList();
        Assert.That(texts[0], Is.EqualTo("Asked Mock runtime (development fixture) to start"), "accepted is not started");
        Assert.That(texts[1], Is.EqualTo("Started on Mock runtime (development fixture)"), "started once the runtime says so");
        Assert.That(texts.Any(text => text.StartsWith("Approval requested to use bash: ", StringComparison.Ordinal)), Is.True);
        Assert.That(texts, Does.Contain("Asked to approve"), "a client's name is never shown as a person's");
        Assert.That(texts, Has.None.Contains("halcyonic"));
        Assert.That(texts, Does.Contain("Approved"));
        Assert.That(texts, Does.Contain("bash succeeded"));
        Assert.That(texts[^1], Is.EqualTo("Round finished"), "a round, never a turn");
    }

    [Test]
    public void AgentTextIsMarkedAsAClaim()
    {
        var events = Load("multiple_workstreams.jsonl");
        var log = new ActivityLog();
        log.Record(events);
        var entries = log.For(ExecutionOf(events, "Fix flaky checkout tests"));
        Assert.That(entries.Where(entry => entry.Kind == ActivityKind.Message).All(entry => entry.Reported), Is.True);
        Assert.That(entries.Where(entry => entry.Kind != ActivityKind.Message).Any(entry => entry.Reported), Is.False);
        Assert.That(entries.Any(entry => entry.Text.StartsWith("Tests failed", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void SaysWhyItCantTellByTheCodeAloneNeverTheMessage()
    {
        var execution = "01a0dcf1-5e68-7034-8b08-109e41ae25e9";
        StoredEvent Unknown(int position, string code) => new()
        {
            Position = position,
            Event = HalcyonicJson.Deserialize<EventEnvelope>("{\"schema_version\":1,\"event_id\":\"01a0dcf1-6e6c-74af-93ca-dea260db947" + position
                + "\",\"event_type\":\"execution.state_unknown\",\"project_id\":\"01a0dcf1-5a80-7295-a48c-b7b6d5c02b34\",\"workstream_id\":\"01a0dcf1-5d6e-7650-ab32-1d63927a8338\","
                + "\"execution_id\":\"" + execution + "\",\"source\":{\"kind\":\"control_plane\"},\"source_native_id\":null,\"sequence\":null,"
                + "\"occurred_at\":\"2026-09-26T09:00:05.100Z\",\"correlation_id\":null,\"causation_id\":null,\"provenance\":{\"epistemic\":\"observed\",\"native_type\":null},"
                + "\"payload\":{\"code\":\"" + code + "\",\"message\":\"The control plane restarted and is no longer connected to the runtime session of this execution.\"},"
                + "\"ingested_at\":\"2026-09-26T09:00:05.100Z\"}"),
        };
        var log = new ActivityLog();
        log.Record(new[] { Unknown(1, "control_plane_restarted"), Unknown(2, "start_outcome_unknown") });
        Assert.That(log.For(execution).Select(entry => entry.Text), Is.EqualTo(new[]
        {
            "Can't tell what it's doing: your computer restarted and lost touch with the agent app.",
            "Can't tell what it's doing: not sure it started.",
        }));
    }

    /// <summary>An event of <paramref name="type"/> with <paramref name="payload"/>, from the control plane or a runtime, as the journal holds it.</summary>
    private static StoredEvent Journaled(int position, string type, string payload, string execution, bool runtime = false) => new()
    {
        Position = position,
        Event = HalcyonicJson.Deserialize<EventEnvelope>("{\"schema_version\":1,\"event_id\":\"01a0dcf1-6e6c-74af-93ca-dea260db94" + (50 + position)
            + "\",\"event_type\":\"" + type + "\",\"project_id\":\"01a0dcf1-5a80-7295-a48c-b7b6d5c02b34\",\"workstream_id\":\"01a0dcf1-5d6e-7650-ab32-1d63927a8338\","
            + "\"execution_id\":\"" + execution + "\",\"source\":" + (runtime ? "{\"kind\":\"runtime\",\"runtime_id\":\"mock\"}" : "{\"kind\":\"control_plane\"}")
            + ",\"source_native_id\":null,\"sequence\":null,\"occurred_at\":\"2026-09-26T09:00:05.100Z\",\"correlation_id\":null,\"causation_id\":null,"
            + "\"provenance\":{\"epistemic\":\"observed\",\"native_type\":null},\"payload\":" + payload + ",\"ingested_at\":\"2026-09-26T09:00:05.100Z\"}"),
    };

    /// <summary>
    /// A failed command, start or round says what failed and, where it may have happened anyway, only
    /// that it may have; never the failure's message, an agent app's or the control plane's own words,
    /// which can name the app, an address or a path (the review's Q5 and Q5c, 2026-10-04).
    /// </summary>
    [Test]
    public void AFailedCommandStartOrRoundNeverShowsItsMessage()
    {
        const string Leak = "OpenCode could not be reached: connect ECONNREFUSED 127.0.0.1:4096 (/Users/someone/project)";
        var execution = "01a0dcf1-5e68-7034-8b08-109e41ae25e9";
        string Failed(string effect) => "{\"command_id\":\"01a0dcf1-6e6c-74af-93ca-dea260db9472\",\"command_type\":\"execution.send_instruction\","
            + "\"failure\":{\"code\":\"runtime_unreachable\",\"message\":\"" + Leak + "\",\"effect\":\"" + effect + "\"}}";
        var log = new ActivityLog();
        log.Record(new[]
        {
            Journaled(1, "command.failed", Failed("none"), execution),
            Journaled(2, "command.failed", Failed("unknown"), execution),
            Journaled(3, "execution.start_failed", "{\"error\":{\"code\":\"runtime_unreachable\",\"message\":\"" + Leak + "\"}}", execution),
            Journaled(4, "execution.start_failed", "{\"error\":{\"code\":\"location_missing\",\"message\":\"" + Leak + "\"}}", execution),
            Journaled(5, "runtime.turn.failed", "{\"turn_id\":null,\"error\":{\"code\":\"opencode_execution_failed\",\"message\":\"" + Leak + "\"}}", execution, runtime: true),
        });
        Assert.That(log.For(execution).Select(entry => entry.Text), Is.EqualTo(new[]
        {
            "Couldn't send an instruction: your computer lost touch with the agent app. See what it's doing, then try again.",
            "Not sure the instruction reached it. Check its activity before you try again.",
            "Couldn't start: your computer lost touch with the agent app. Check that the agent app is running on your computer, then try again.",
            "Couldn't start: your computer can't use that folder right now: it may have moved, or it can't be read. Choose it again, or fix it on your computer.",
            "Couldn't finish this round",
        }), "an effect that can't be ruled out is never said as Couldn't");
        Assert.That(log.For(execution).Select(entry => entry.Text), Has.None.Contains("ECONNREFUSED").And.None.Contains("OpenCode").And.None.Contains("/Users/"));
    }

    [Test]
    public void DescribesWhatWentWrong()
    {
        var events = Load("failure_modes.jsonl");
        var log = new ActivityLog();
        log.Record(events);

        Assert.That(log.For(ExecutionOf(events, "Speed up the dashboard render")).Last().Text, Is.EqualTo("Couldn't finish this round"), "never the agent app's own error");
        // The agent app's own reason for losing the session is a diagnostic, never shown.
        Assert.That(log.For(ExecutionOf(events, "Run the integration suite")).Last().Text, Is.EqualTo("Your computer lost touch with the agent app."));
        var refactor = log.For(ExecutionOf(events, "Refactor the session store")).Select(entry => entry.Text).ToList();
        Assert.That(refactor, Does.Contain("Couldn't send an instruction: its agent app can't do that. See what it's doing, then try something it offers."), "by the refusal's code, never its message");
        Assert.That(refactor, Has.None.Contains("instruct_while_running").And.None.Contains("Runtime mock"));
        Assert.That(refactor[^1], Is.EqualTo("Round stopped"));
        Assert.That(log.For(ExecutionOf(events, "Drop the legacy sessions table")).Select(entry => entry.Text), Does.Contain("Denied"));
    }

    [Test]
    public void ToolNamesStayWithTheirExecution()
    {
        var events = Load("multiple_workstreams.jsonl");
        var log = new ActivityLog();
        log.Record(events);
        foreach (var title in new[] { "Add password reset", "Move sessions to their own table" })
        {
            var entries = log.For(ExecutionOf(events, title)).Where(entry => entry.Kind == ActivityKind.Tool).ToList();
            bool Finished(ActivityEntry entry) =>
                entry.Text.EndsWith(" succeeded", StringComparison.Ordinal) || entry.Text.EndsWith(" failed", StringComparison.Ordinal);
            var started = entries.Where(entry => !Finished(entry)).Select(entry => entry.Text.Split(':')[0]).ToList();
            var finished = entries.Where(Finished).Select(entry => entry.Text.Split(' ')[0]).ToList();
            Assert.That(finished, Is.EqualTo(started), title);
        }
    }

    [Test]
    public void RecordingTheSameEventsAgainChangesNothingAndHistoryFitsInOrder()
    {
        var events = Load("multiple_workstreams.jsonl");
        var log = new ActivityLog();
        log.Record(events.Skip(events.Count / 2));
        log.Record(events.Take(events.Count / 2));
        Assert.That(log.Record(events), Is.Empty);

        var fresh = new ActivityLog();
        fresh.Record(events);
        var execution = ExecutionOf(events, "Move sessions to their own table");
        Assert.That(log.For(execution).Select(entry => entry.Position), Is.EqualTo(fresh.For(execution).Select(entry => entry.Position)));
        Assert.That(log.For(execution).Select(entry => entry.Position), Is.Ordered);
    }

    [Test]
    public void OnlyTheMostRecentEntriesAreKept()
    {
        var events = Load("multiple_workstreams.jsonl");
        var log = new ActivityLog(capacityPerExecution: 3);
        log.Record(events);
        var execution = ExecutionOf(events, "Move sessions to their own table");
        var full = new ActivityLog();
        full.Record(events);
        Assert.That(log.For(execution).Select(entry => entry.Text), Is.EqualTo(full.For(execution).Skip(full.For(execution).Count - 3).Select(entry => entry.Text)));
    }
}

public class WorkspacePresenterTests
{
    private static ExecutionView Execution(ExecutionStatus status, bool pendingApproval = false)
    {
        var execution = Samples.Execution("e1", "w1", status);
        if (pendingApproval)
        {
            execution.PendingApprovals.Add(new ApprovalView
            {
                ApprovalId = "approval-1",
                Subject = new ToolUseSubject { ToolName = "bash", Summary = "Run it" },
                RequestedAt = Samples.Time,
            });
        }
        return execution;
    }

    [Test]
    public void OffersOnlyWhatTheRuntimeSupportsInTheCurrentState()
    {
        var mock = Samples.MockRuntime();
        Assert.That(WorkspacePresenter.ActionsFor(Execution(ExecutionStatus.WaitingForHuman, pendingApproval: true), mock),
            Is.EqualTo(new[] { WorkspaceAction.Approve, WorkspaceAction.Deny, WorkspaceAction.Interrupt }));
        Assert.That(WorkspacePresenter.ActionsFor(Execution(ExecutionStatus.Running), mock), Is.EqualTo(new[] { WorkspaceAction.Interrupt }),
            "the mock runtime cannot be instructed while running");
        Assert.That(WorkspacePresenter.ActionsFor(Execution(ExecutionStatus.Completed), mock), Is.EqualTo(new[] { WorkspaceAction.Instruct }));
        Assert.That(WorkspacePresenter.ActionsFor(Execution(ExecutionStatus.Unknown), mock), Is.Empty);
        Assert.That(WorkspacePresenter.ActionsFor(Execution(ExecutionStatus.Starting), mock), Is.Empty);

        var steerable = Samples.MockRuntime();
        steerable.Capabilities.InstructWhileRunning = true;
        Assert.That(WorkspacePresenter.ActionsFor(Execution(ExecutionStatus.Running), steerable),
            Is.EqualTo(new[] { WorkspaceAction.Interrupt, WorkspaceAction.Instruct }));

        var observeOnly = Samples.MockRuntime();
        observeOnly.Capabilities = new RuntimeCapabilities();
        Assert.That(WorkspacePresenter.ActionsFor(Execution(ExecutionStatus.WaitingForHuman, pendingApproval: true), observeOnly), Is.Empty);
    }

    [Test]
    public void NothingIsOfferedWhileNotLiveOrWithoutTheRuntime()
    {
        var workstream = Samples.Workstream("w1", WorkstreamStatus.WaitingForHuman, "e1");
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(1, new[] { workstream }, new[] { Execution(ExecutionStatus.WaitingForHuman, pendingApproval: true) }), new StateChanges());

        Assert.That(WorkspacePresenter.Present(workstream, state, new ActivityLog(), live: true).Actions, Is.Not.Empty);
        Assert.That(WorkspacePresenter.Present(workstream, state, new ActivityLog(), live: false).Actions, Is.Empty);

        var replayed = Samples.Snapshot(1, new[] { workstream }, new[] { Execution(ExecutionStatus.WaitingForHuman, pendingApproval: true) });
        replayed.Runtimes.Clear();
        state.ApplySnapshot(replayed, new StateChanges());
        var presentation = WorkspacePresenter.Present(workstream, state, new ActivityLog(), live: true);
        Assert.That(presentation.Runtime, Is.Null);
        Assert.That(presentation.Actions, Is.Empty);
    }

    [Test]
    public void ReviewRequiredActionsNeedADeliberateConfirmation()
    {
        var workstream = Samples.Workstream("w1", WorkstreamStatus.WaitingForHuman, "e1");
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(1, new[] { workstream }, new[] { Execution(ExecutionStatus.WaitingForHuman, pendingApproval: true) }), new StateChanges());

        var beforeWelcome = WorkspacePresenter.Present(workstream, state, new ActivityLog(), live: true);
        Assert.That(beforeWelcome.RequiresConfirmation(WorkspaceAction.Approve), Is.True, "unknown policies count as needing one");

        state.ApplyWelcome(Samples.Welcome(resumed: false, head: 1));
        var workspace = WorkspacePresenter.Present(workstream, state, new ActivityLog(), live: true);
        Assert.That(workspace.RequiresConfirmation(WorkspaceAction.Approve), Is.True);
        Assert.That(workspace.RequiresConfirmation(WorkspaceAction.Deny), Is.True);
        Assert.That(workspace.RequiresConfirmation(WorkspaceAction.Interrupt), Is.True);
        Assert.That(state.RequiresConfirmation(CommandType.ExecutionSendInstruction), Is.False);
    }

    [Test]
    public void CommandFeedbackSaysWhereEachRequestStands()
    {
        CommandView Command(CommandStatus status) => new()
        {
            CommandId = Guid.NewGuid().ToString("D"),
            CommandType = CommandType.ExecutionInterrupt,
            Status = status,
            ProjectId = Samples.ProjectId,
            WorkstreamId = "w1",
            ExecutionId = "e1",
            IssuedAt = Samples.Time,
            UpdatedAt = Samples.Time,
            Rejection = status == CommandStatus.Rejected ? new CommandRejection { Code = RejectionCode.InvalidState, Message = "Nothing is running." } : null,
            Failure = status == CommandStatus.Failed ? new CommandFailure { Code = "timeout", Message = "The runtime did not confirm.", Effect = FailureEffect.Unknown } : null,
            Result = null,
        };
        Assert.That(WorkspacePresenter.Feedback(Command(CommandStatus.Accepted)).Text, Is.EqualTo("Sent. Waiting for it to stop…"));
        Assert.That(WorkspacePresenter.Feedback(Command(CommandStatus.Completed)).Text, Is.EqualTo("Confirmed: stopped."));
        Assert.That(WorkspacePresenter.Feedback(Command(CommandStatus.Rejected)).Text, Is.EqualTo("Couldn't do that: it can't take that right now. See what it's doing, then try again."),
            "by the refusal's code, never the control plane's message");
        Assert.That(WorkspacePresenter.Feedback(Command(CommandStatus.Failed)).Text,
            Is.EqualTo("Not sure it stopped. Check its activity before you try again."), "what may have happened, named, never Couldn't");
        foreach (CommandType type in Enum.GetValues(typeof(CommandType)))
        {
            Assert.That(WorkspaceText.NotSure(type), Does.StartWith("Not sure ").And.Not.Contain("Couldn't"), type.ToString());
        }
        var noEffect = Command(CommandStatus.Failed);
        noEffect.Failure!.Effect = FailureEffect.None;
        noEffect.Failure.Code = "codex_other";
        Assert.That(WorkspacePresenter.Feedback(noEffect).Text, Is.EqualTo("Couldn't do that: nothing changed. See what it's doing, then try again."),
            "trying again at once can fail the same way, so the way on looks at the task first");
        // A cause every agent app's adapter shares is said in words already used for it, with the way on.
        foreach (var (code, words) in new[]
        {
            ("approval_not_pending", "Couldn't do that: it no longer waits for that decision. See what it's doing now."),
            ("question_not_pending", "Couldn't do that: it's no longer waiting for this answer. See what it's doing now."),
            ("no_running_turn", "Couldn't do that: it can't take that right now. See what it's doing, then try again."),
            ("runtime_unreachable", "Couldn't do that: your computer lost touch with the agent app. See what it's doing, then try again."),
            ("runtime_closed", "Couldn't do that: your computer lost touch with the agent app. See what it's doing, then try again."),
            ("execution_unknown_to_runtime", "Couldn't do that: this work isn't on your computer any more. Add the task again in Projects to try again."),
            ("model_unavailable", "Couldn't do that: its agent app needs a model. Choose one, then try again."),
            ("capability_unimplemented", "Couldn't do that: its agent app can't do that. See what it's doing, then try something it offers."),
        })
        {
            noEffect.Failure.Code = code;
            Assert.That(WorkspacePresenter.Feedback(noEffect).Text, Is.EqualTo(words), code);
        }

        // The recorded demonstration's answer is its own words: the recording continues with the
        // answer it recorded, so "Refused" would contradict what plays next.
        var demonstration = Command(CommandStatus.Rejected);
        demonstration.Rejection = new CommandRejection
        {
            Code = RejectionCode.Demonstration,
            Message = "Not sent to any agent; the recording continues as recorded for stopping it.",
        };
        Assert.That(WorkspacePresenter.Feedback(demonstration).Text, Is.EqualTo(demonstration.Rejection.Message));
        Assert.That(WorkspacePresenter.Feedback(demonstration).Status, Is.EqualTo(CommandStatus.Rejected));
    }
}

public class EventHistoryTests
{
    private sealed class PagedHistory : IEventHistory
    {
        private readonly int total;
        private readonly string journalId;
        public readonly List<long> Requests = new();

        public PagedHistory(int total, string journalId = Samples.JournalId)
        {
            this.total = total;
            this.journalId = journalId;
        }

        public Task<EventsResponse> ReadAsync(string workstreamId, long after, int limit, CancellationToken cancellationToken)
        {
            Requests.Add(after);
            var template = HalcyonicJson.Deserialize<EventEnvelope>(Repository.Trace()[0]);
            var events = Enumerable.Range((int)after + 1, (int)Math.Max(0, Math.Min(limit, total - after)))
                .Select(position => new StoredEvent { Position = position, Event = template })
                .ToList();
            return Task.FromResult(new EventsResponse { Journal = Samples.Journal(journalId), Head = total, Events = events });
        }
    }

    [Test]
    public async Task ReadsEveryPage()
    {
        var history = new PagedHistory(2500);
        var events = await history.ReadAllAsync("w1", Samples.JournalId);
        Assert.That(events, Has.Count.EqualTo(2500));
        Assert.That(history.Requests, Is.EqualTo(new long[] { 0, 1000, 2000 }));
    }

    [Test]
    public void RefusesHistoryFromAnotherJournal()
    {
        var history = new PagedHistory(10, journalId: "01a0dcf1-5a80-7000-8000-0000000000ff");
        Assert.ThrowsAsync<ControlPlaneRequestException>(() => history.ReadAllAsync("w1", Samples.JournalId));
    }

    [Test]
    public void TheRestAddressFollowsTheRealtimeEndpoint()
    {
        Assert.That(ControlPlaneApi.BaseUriFor(new Uri("ws://127.0.0.1:47800/realtime")), Is.EqualTo(new Uri("http://127.0.0.1:47800/")));
        Assert.That(ControlPlaneApi.BaseUriFor(new Uri("wss://example.test/realtime")), Is.EqualTo(new Uri("https://example.test/")));
    }
}

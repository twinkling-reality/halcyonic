using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The spatial kit's models take production's words and records unchanged: the kit draws them, it never words them.</summary>
public class KitPresenterTests
{
    private const string At = "2026-10-28T09:00:00.000Z";
    private const string Command = "pnpm test --filter sign-in";

    [Test]
    public void AnApprovalCarriesNeedFromYouWhole()
    {
        var workspace = Waiting(Approval("shell", Command));
        var expected = WorkspaceText.NeedFromYou(workspace)!;
        var scope = WorkspaceText.ApprovedCommandNote(workspace.Runtime);
        var need = KitPresenter.NeedOf(workspace)!;
        Assert.That(need, Is.Not.Null);
        Assert.That((need.Asks.Text, need.Request), Is.EqualTo((expected.AsksRuns.Text, expected.Request)));
        Assert.That(need.Request, Is.EqualTo(Command), "the request is literal and whole");
        // Where an approved command runs stands apart; every other note is production's, in its order.
        Assert.That(scope, Is.Not.Null);
        Assert.That(need.ScopeNote, Is.EqualTo(scope));
        Assert.That(need.Notes, Is.EqualTo(expected.Notes.Where(note => note != scope)));
        Assert.That((need.DenyWords, need.ApproveWords), Is.EqualTo((WorkspaceText.Label(WorkspaceAction.Deny), WorkspaceText.Label(WorkspaceAction.Approve))));
        Assert.That(need.Confirming, Is.Null);
    }

    [Test]
    public void TheScopeNoteStandsOnlyWhereProductionSaysIt()
    {
        var workspace = Waiting(Approval("edit", "src/auth/rate-limit.ts"));
        var expected = WorkspaceText.NeedFromYou(workspace)!;
        var need = KitPresenter.NeedOf(workspace)!;
        Assert.That(need.ScopeNote, Is.Null, "an edit runs no command, so where a command runs is not said");
        Assert.That(need.Notes, Is.EqualTo(expected.Notes));
        Assert.That(need.Asks.AnyTheirs, Is.True, "the tool's name leans inside Halcyonic's sentence");
    }

    [Test]
    public void NothingIsAskedWhereNoRequestWaits()
    {
        Assert.That(KitPresenter.NeedOf(Waiting()), Is.Null);
        Assert.That(KitPresenter.AskOf(Waiting()), Is.Null);
    }

    [Test]
    public void AConfirmationIsProductionsQuestionAndYes()
    {
        var confirming = KitPresenter.ConfirmationOf(WorkspaceAction.Approve, read: false, parts: 2);
        Assert.That((confirming.Approving, confirming.Ask, confirming.Confirm, confirming.Read, confirming.ReadFirst), Is.EqualTo((true,
            WorkspaceText.ConfirmationPrompt(WorkspaceAction.Approve, null), WorkspaceText.ConfirmLabel(WorkspaceAction.Approve), false, EntryText.ReadToPart(2))));
        var denying = KitPresenter.ConfirmationOf(WorkspaceAction.Deny, read: true, parts: 1);
        Assert.That((denying.Approving, denying.Ask, denying.Confirm), Is.EqualTo((false,
            WorkspaceText.ConfirmationPrompt(WorkspaceAction.Deny, null), WorkspaceText.ConfirmLabel(WorkspaceAction.Deny))));
        var need = KitPresenter.NeedOf(Waiting(Approval("shell", Command)), confirming)!;
        Assert.That(need.Confirming, Is.SameAs(confirming));
    }

    [Test]
    public void AQuestionKeepsProductionsHeadingAndTheAgentsWords()
    {
        const string asked = "Should I reset the rate limit counters in staging too?";
        var question = Question(new QuestionPrompt { Key = "reset", Text = asked, FreeText = true });
        var workspace = Waiting(question: question);
        var ask = KitPresenter.AskOf(workspace)!;
        Assert.That(ask, Is.Not.Null);
        Assert.That(ask.Heading.Text, Is.EqualTo(WorkspaceText.PromptHeadingRuns(question, 0).Text));
        Assert.That(ask.Heading.Text, Is.EqualTo("Agent · Question"));
        Assert.That(ask.Question, Is.EqualTo(asked), "the agent's question as it wrote it");
    }

    [Test]
    public void AQuestionTheKitCannotDrawWholeIsNotDrawnAtAll()
    {
        var options = Question(new QuestionPrompt { Key = "pick", Text = "Which one?", Options = new List<QuestionOption> { new() { Label = "This" }, new() { Label = "That" } } });
        var secret = Question(new QuestionPrompt { Key = "key", Text = "Paste the key.", FreeText = true, Secret = true });
        var several = Question(new QuestionPrompt { Key = "a", Text = "First?", FreeText = true }, new QuestionPrompt { Key = "b", Text = "Second?", FreeText = true });
        foreach (var question in new[] { options, secret, several })
        {
            Assert.That(KitPresenter.AskOf(Waiting(question: question)), Is.Null, question.Prompts[0].Text);
        }
    }

    [Test]
    public void AnOutcomeThatCantBeToldIsSaidInNotSuresWords()
    {
        var unknown = new CommandView
        {
            CommandId = "command-unknown",
            CommandType = CommandType.ExecutionRespondToApproval,
            Status = CommandStatus.Failed,
            ExecutionId = "execution",
            IssuedAt = At,
            UpdatedAt = At,
            Failure = new CommandFailure { Code = "runtime_unreachable", Message = "lost", Effect = FailureEffect.Unknown },
        };
        var notice = KitPresenter.NoticeOf(Waiting(commands: new[] { unknown }))!;
        Assert.That(notice, Is.Not.Null);
        Assert.That(notice.Words.Text, Is.EqualTo(WorkspaceText.NotSure(CommandType.ExecutionRespondToApproval)));
        Assert.That(notice.Words.AnyTheirs, Is.False, "Halcyonic's own sentence");
        Assert.That((notice.Icon, notice.Tone), Is.EqualTo((GlazeIcon.CantTellYet, GlazeTone.Unknown)));

        var done = new CommandView { CommandId = "command-done", CommandType = CommandType.ExecutionSendInstruction, Status = CommandStatus.Completed, ExecutionId = "execution", IssuedAt = At, UpdatedAt = At };
        Assert.That(KitPresenter.NoticeOf(Waiting(commands: new[] { done })), Is.Null);
    }

    [Test]
    public void SendSaysWhyInConnectionTextsWords()
    {
        Assert.That(KitPresenter.WhyNotSendOf(new ConnectionStatus(ConnectionPhase.Live)), Is.Null);
        var retrying = new ConnectionStatus(ConnectionPhase.WaitingToRetry);
        Assert.That(KitPresenter.WhyNotSendOf(retrying), Is.EqualTo(ConnectionText.Retrying(retrying)));
        var connecting = new ConnectionStatus(ConnectionPhase.Connecting);
        Assert.That(KitPresenter.WhyNotSendOf(connecting), Is.EqualTo(ConnectionText.Phase(connecting)));
    }

    [Test]
    public void TheSlateReadsTheReportAndTheRailHoldsWhatWasRecorded()
    {
        var activity = new[]
        {
            new ActivityEntry(1, At, ActivityKind.Turn, "Round started", false),
            new ActivityEntry(2, At, ActivityKind.Message, "First look", true),
            new ActivityEntry(3, At, ActivityKind.Tool, "Used read", false),
            new ActivityEntry(4, At, ActivityKind.TestRun, "Tests failed: 1 failed, 23 passed", false),
            new ActivityEntry(5, At, ActivityKind.Message, "I found the cause.", true),
        };
        var report = KitPresenter.ReportOf(activity)!;
        Assert.That(report, Is.SameAs(activity[4]));
        Assert.That(report.Reported, Is.True, "an agent's message stays a reported claim");
        var trail = KitPresenter.TrailOf(activity);
        Assert.That(trail.Select(entry => entry.Entry), Is.EqualTo(new[] { activity[0], activity[2], activity[3] }), "recorded events only, oldest first, unchanged");
        Assert.That(trail.Select(entry => entry.Icon), Is.EqualTo(new[] { KitPresenter.IconOf(ActivityKind.Turn), KitPresenter.IconOf(ActivityKind.Tool), KitPresenter.IconOf(ActivityKind.TestRun) }));
        // An entry carries no outcome of its own, so none is coloured as one: its words say it.
        Assert.That(trail.All(entry => entry.Tone == GlazeTone.Neutral), Is.True);
        Assert.That(KitPresenter.ReportOf(new[] { activity[0] }), Is.Null, "nothing reported yet");
    }

    [Test]
    public void ATaskRowAndThePresenceTakeTheBadgeProductionDerives()
    {
        var task = Task(CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired, 1);
        var badge = StateLanguage.BadgeOf(task);
        var row = KitPresenter.TaskRowOf(task, selected: true, next: "Review request");
        Assert.That((row.Key, row.Words.Text, row.IsName, row.Words.AllTheirs), Is.EqualTo(("task:" + task.WorkstreamId, task.Title, true, true)), "the spine's key for a task");
        Assert.That((row.Icon, row.IconTone, row.Detail, row.DetailTone), Is.EqualTo(((GlazeIcon?)badge.Icon, badge.Tone, badge.Text, (GlazeTone?)badge.Tone)));
        Assert.That((row.End, row.EndTone, row.Selected), Is.EqualTo(("Review request", (GlazeTone?)GlazeTone.Attention, true)));
        var presence = KitPresenter.PresenceOf(task, waiting: 2);
        Assert.That((presence.TaskTitle, presence.Badge!.Text, presence.WaitingWords), Is.EqualTo((task.Title, badge.Text, "2 tasks are waiting for you.")));
        Assert.That(presence.Identity.AllTheirs, Is.True, "a task's name is theirs");
    }

    [Test]
    public void ThePresenceNamesWhatTheClosedBarNames()
    {
        // No task beside the menu: the place's own word, Halcyonic's, and no state to show.
        var place = KitPresenter.PresenceOf(LineWords.Ours("Projects"), null, waiting: 0);
        Assert.That((place.Identity.Text, place.Identity.AnyTheirs, place.Badge, place.WaitingWords), Is.EqualTo(("Projects", false, (StateBadge?)null, "Nothing is waiting for you.")));
        // A task beside the menu: its name as theirs, the state production derives, the count the director counts.
        var task = Task(CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired, 1);
        var badge = StateLanguage.BadgeOf(task);
        var retained = KitPresenter.PresenceOf(LineWords.TheirName(task.Title), badge, waiting: 1);
        Assert.That((retained.Identity.Text, retained.Identity.AllTheirs, retained.TaskTitle, retained.WaitingWords), Is.EqualTo((task.Title, true, task.Title, "1 task is waiting for you.")));
        Assert.That(retained.Badge, Is.SameAs(badge));
    }

    [Test]
    public void TheFocusedWorkIsAssembledFromThePresentation()
    {
        var spine = new[] { new KitSpineSection(new KitRowModel[0]) };
        var live = KitPresenter.WorkOf(Waiting(), "Storefront", "Run the tests again.", voiceOffered: true, new ConnectionStatus(ConnectionPhase.Live), spine);
        Assert.That((live.ProjectName, live.TaskTitle, live.Draft, live.VoiceOffered), Is.EqualTo(("Storefront", "Fix the sign-in rate limit", "Run the tests again.", true)));
        Assert.That(live.Badge.Text, Is.EqualTo(StateLanguage.BadgeOf(Waiting().Character).Text));
        Assert.That((live.Sendable, live.WhyNotSend), Is.EqualTo((true, (string?)null)));
        Assert.That(live.Spine, Is.SameAs(spine));
        var retrying = new ConnectionStatus(ConnectionPhase.WaitingToRetry);
        var away = KitPresenter.WorkOf(Waiting(), "Storefront", "", voiceOffered: false, retrying, spine);
        Assert.That((away.Sendable, away.WhyNotSend), Is.EqualTo((false, ConnectionText.Retrying(retrying))));
        var asking = KitPresenter.WorkOf(Waiting(Approval("shell", Command)), "Storefront", "", voiceOffered: false, new ConnectionStatus(ConnectionPhase.Live), spine);
        Assert.That((asking.Need?.Request, asking.Ask), Is.EqualTo(((string?)Command, (KitAsk?)null)));
    }

    [Test]
    public void TheKitOffersTheAnswersProductionOffers()
    {
        var workspace = Waiting(Approval("shell", Command));
        Assert.That(workspace.Actions, Does.Contain(WorkspaceAction.Approve).And.Contain(WorkspaceAction.Deny));
        var need = KitPresenter.NeedOf(workspace)!;
        Assert.That((need.DenyOffered, need.ApproveOffered), Is.EqualTo((true, true)));
    }

    [Test]
    public void ARequestThatCanOnlyBeDeniedOffersNoApprove()
    {
        var workspace = Waiting(Approval("shell", "pnpm test --filter sign-in --reporter [truncated]", approvable: false));
        Assert.That(workspace.Actions, Does.Not.Contain(WorkspaceAction.Approve));
        var need = KitPresenter.NeedOf(workspace)!;
        Assert.That((need.DenyOffered, need.ApproveOffered), Is.EqualTo((true, false)));
        Assert.That(need.Notes, Does.Contain(WorkspaceText.TooLongToShow), "production says why it can only be denied");
        // A confirmation of an answer production doesn't offer is never drawn; Deny's still is.
        Assert.That(KitPresenter.NeedOf(workspace, KitPresenter.ConfirmationOf(WorkspaceAction.Approve, read: true, parts: 1))!.Confirming, Is.Null);
        var denying = KitPresenter.ConfirmationOf(WorkspaceAction.Deny, read: true, parts: 1);
        Assert.That(KitPresenter.NeedOf(workspace, denying)!.Confirming, Is.SameAs(denying));
    }

    [Test]
    public void NoAnswerIsOfferedWhileTheSessionIsNotLive()
    {
        var workspace = Waiting(Approval("shell", Command), live: false);
        Assert.That(workspace.Actions, Is.Empty);
        var need = KitPresenter.NeedOf(workspace, KitPresenter.ConfirmationOf(WorkspaceAction.Deny, read: true, parts: 1))!;
        Assert.That((need.DenyOffered, need.ApproveOffered, need.Confirming), Is.EqualTo((false, false, (KitConfirmation?)null)));
        Assert.That(need.Request, Is.EqualTo(Command), "the request still reads whole");
    }

    [Test]
    public void SendSaysWhyInProductionsWordsWhileLive()
    {
        var live = new ConnectionStatus(ConnectionPhase.Live);
        var spine = new KitSpineSection[0];
        foreach (var workspace in new[] { Waiting(Approval("shell", Command)), Waiting(status: ExecutionStatus.Completed, instructAtRest: false) })
        {
            Assert.That(workspace.Actions.Contains(WorkspaceAction.Instruct) && WorkspaceText.NeedFromYou(workspace) == null, Is.False);
            var screen = new FileScreen();
            screen.Instruction.Restore(workspace.Execution!.ExecutionId, "Run the tests again.", false, 0);
            var production = FileScreens.InstructionSendReason(workspace, screen);
            Assert.That(production, Is.Not.Null);
            Assert.That(FileScreens.WhyNoInstruction(workspace), Is.EqualTo(production));
            var work = KitPresenter.WorkOf(workspace, "Storefront", "Run the tests again.", voiceOffered: false, live, spine);
            Assert.That((work.Sendable, work.WhyNotSend), Is.EqualTo((false, production)));
        }
        Assert.That(FileScreens.WhyNoInstruction(Waiting()), Is.Null, "running and taking instructions: no reason");
    }

    [Test]
    public void TheSpinesNoteCarriesTheConnectionsStateInProductionsWords()
    {
        foreach (var status in new[] { new ConnectionStatus(ConnectionPhase.Live), new ConnectionStatus(ConnectionPhase.Connecting), new ConnectionStatus(ConnectionPhase.WaitingToRetry) })
        {
            var words = ConnectionText.Phase(status);
            var section = new KitSpineSection(new KitRowModel[0], "Projects", note: words);
            Assert.That((section.Heading, section.Note), Is.EqualTo(("Projects", (string?)words)));
        }
        Assert.That(new KitSpineSection(new KitRowModel[0], "Projects").Note, Is.Null, "a section says nothing more where it is given nothing");
    }

    private static ApprovalView Approval(string tool, string summary, bool approvable = true) => new()
    {
        ApprovalId = "approval",
        Subject = new ToolUseSubject { ToolName = tool, Summary = summary },
        Approvable = approvable,
        RequestedAt = At,
    };

    private static QuestionView Question(params QuestionPrompt[] prompts) => new()
    {
        QuestionId = "question",
        Answerable = true,
        AskedAt = At,
        Prompts = prompts.ToList(),
    };

    private static CharacterPresentation Task(CharacterActivity activity, AttentionLevel attention = AttentionLevel.None, int approvals = 0) =>
        new("task", "Fix the sign-in rate limit", activity, StateLanguage.WordOf(StateLanguage.StateOf(activity, attention)), attention,
            new string[0], approvals, false, false, false);

    /// <summary>The task as the production presenter presents it, live unless said otherwise, from a projection holding what it waits on and its commands.</summary>
    private static WorkspacePresentation Waiting(ApprovalView? approval = null, QuestionView? question = null, IEnumerable<CommandView>? commands = null,
        bool live = true, ExecutionStatus? status = null, bool instructAtRest = true)
    {
        var runtime = new RuntimeDescriptor
        {
            RuntimeId = "runtime",
            Kind = "synthetic",
            DisplayName = "Synthetic agent app",
            Synthetic = true,
            ApprovalMayLeaveSandbox = true,
            Capabilities = new RuntimeCapabilities { StartExecution = true, InstructAtRest = instructAtRest, InstructWhileRunning = true, RespondToApproval = true, Interrupt = true, AnswerQuestion = true },
        };
        var waits = approval != null || question != null;
        var workstream = new WorkstreamView
        {
            WorkstreamId = "task",
            ProjectId = "project",
            Title = "Fix the sign-in rate limit",
            Status = waits ? WorkstreamStatus.WaitingForHuman : status == ExecutionStatus.Completed ? WorkstreamStatus.Completed : WorkstreamStatus.Running,
            Attention = new Attention { Level = waits ? AttentionLevel.ActionRequired : AttentionLevel.None },
            CurrentExecutionId = "execution",
            ExecutionIds = new List<string> { "execution" },
            CreatedAt = At,
            UpdatedAt = At,
        };
        var execution = new ExecutionView
        {
            ExecutionId = "execution",
            WorkstreamId = "task",
            ProjectId = "project",
            Runtime = new RuntimeRef { RuntimeId = runtime.RuntimeId, Kind = runtime.Kind, DisplayName = runtime.DisplayName, Synthetic = true },
            Instruction = "Do the work.",
            Status = status ?? (waits ? ExecutionStatus.WaitingForHuman : ExecutionStatus.Running),
            TurnCount = 1,
            CreatedAt = At,
            StartedAt = At,
            UpdatedAt = At,
        };
        if (approval != null) execution.PendingApprovals.Add(approval);
        if (question != null) execution.PendingQuestions.Add(question);
        var state = new ClientProjection();
        state.ApplySnapshot(new Snapshot
        {
            Journal = new JournalInfo { JournalId = "journal", Origin = JournalOrigin.Live },
            Position = 1,
            Projects = new List<ProjectView> { new() { ProjectId = "project", Name = "Storefront", CreatedAt = At, UpdatedAt = At } },
            Workstreams = new List<WorkstreamView> { workstream },
            Executions = new List<ExecutionView> { execution },
            Commands = (commands ?? new CommandView[0]).ToList(),
            Runtimes = new List<RuntimeDescriptor> { runtime },
        }, new StateChanges());
        return WorkspacePresenter.Present(workstream, state, new ActivityLog(), live);
    }
}

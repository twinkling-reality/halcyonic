using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The spatial kit bound to the frames a task's file draws: every press the frame offers takes a kit place with the
/// frame's own action, words, availability and reason; nothing the frame doesn't offer; and no binding at all where the
/// kit can't draw the frame whole.
/// </summary>
public class KitFileBindingTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-28T09:00:00Z");
    private static readonly ConnectionStatus Live = new(ConnectionPhase.Live);
    private static readonly KitSpineSection[] Spine = { new(new KitRowModel[0]) };
    private readonly CommandFactory factory = new(Samples.Client);

    private WorkspaceSteering Steering() => new(factory, () => Now);

    private static MenuFrame Screen(WorkspacePresentation workspace, WorkspaceSteering steering, FileScreen screen, int rows = 4) =>
        FileScreens.Screen(workspace, steering, screen, new AnswerRoom(rows));

    private static KitFileBinding? Bind(MenuFrame frame, WorkspaceSteering steering, WorkspacePresentation workspace, string draft = "", ConnectionStatus? connection = null) =>
        KitFileBinding.Of(frame, steering.Armed, workspace, draft, connection ?? Live, "Storefront", Spine);

    private static readonly ActivityEntry[] Recorded =
    {
        new(1, "2026-10-28T09:00:01.000Z", ActivityKind.Turn, "Round started", false),
        new(2, "2026-10-28T09:00:04.000Z", ActivityKind.Message, "I found the cause and I am fixing it.", true),
    };

    /// <summary>The task running with two recorded entries, offering exactly <paramref name="offered"/>, or what its agent app offers where none are given.</summary>
    private static WorkspacePresentation Running(WaitingWork work, params WorkspaceAction[] offered) => Running(work, Recorded, offered);

    private static WorkspacePresentation Running(WaitingWork work, IReadOnlyList<ActivityEntry> activity, params WorkspaceAction[] offered)
    {
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Running; }, WorkstreamStatus.Running);
        var running = work.Present();
        var actions = offered.Length == 0 ? running.Actions : offered;
        return new WorkspacePresentation(running.Character, running.Objective, running.Execution, running.Runtime, actions,
            actions.Where(running.RequiresConfirmation).ToList(), running.Commands, activity);
    }

    /// <summary>Every footer prompt, every line that acts and every section has exactly one control, raising what the frame raises.</summary>
    private static void EveryPressIsBound(MenuFrame frame, KitFileBinding binding)
    {
        var presses = frame.Footer.All.Select(each => (each.Prompt.Id, (string?)null, each.Prompt.Words, each.Prompt.Available, each.Prompt.Reason))
            .Concat(frame.Lines.Where(line => line.Action != null).Select(line => (line.Action!, line.Key, line.Words, line.Available, (string?)null)))
            .Concat(frame.Sections.Select(section => (MenuFrame.ChooseSection, (string?)section.Key, section.Words, true, (string?)null)))
            .ToList();
        Assert.That(binding.Controls.Select(control => (control.Action, control.Key, control.Words, control.Available, control.Reason)), Is.EquivalentTo(presses));
        Assert.That(binding.Controls.All(control => ReferenceEquals(control.Frame, frame)), Is.True, "each press belongs to the frame drawn");
    }

    private static void IsPrompt(KitControl? control, Prompt? prompt)
    {
        Assert.That(control, Is.Not.Null);
        Assert.That(prompt, Is.Not.Null);
        Assert.That((control!.Action, control.Words, control.Icon, control.Available, control.Reason, control.Holds),
            Is.EqualTo((prompt!.Id, prompt.Words, (GlazeIcon?)prompt.Icon, prompt.Available, prompt.Reason, prompt.Holds)));
    }

    [Test]
    public void AnApprovalPageBindsItsAnswersAsTheFileDrewThem()
    {
        var work = new WaitingWork();
        var workspace = work.Present();
        var steering = Steering();
        var frame = Screen(workspace, steering, new FileScreen { Section = FileSection.Waiting });
        var binding = Bind(frame, steering, workspace)!;
        Assert.That(binding, Is.Not.Null);
        EveryPressIsBound(frame, binding);
        IsPrompt(binding[KitPlace.Deny], frame.Footer[PromptSlot.Secondary]);
        IsPrompt(binding[KitPlace.Approve], frame.Footer[PromptSlot.FarRight]);
        IsPrompt(binding[KitPlace.Close], frame.Footer[PromptSlot.Close]);
        Assert.That(binding.Sections.Select(section => (section.Key, section.Chosen)), Is.EqualTo(frame.Sections.Select(section => (section.Key, section.Chosen))));
        // What it asks reaches the kit in NeedFromYou's words, both answers offered as the frame offers them.
        var expected = WorkspaceText.NeedFromYou(workspace)!;
        var need = binding.Model.Need!;
        Assert.That((need.Asks.Text, need.Request, need.DenyOffered, need.ApproveOffered, need.Confirming), Is.EqualTo((expected.AsksRuns.Text, expected.Request, true, true, (KitConfirmation?)null)));
        Assert.That((need.DenyWords, need.ApproveWords), Is.EqualTo((frame.Footer[PromptSlot.Secondary]!.Words, frame.Footer[PromptSlot.FarRight]!.Words)));
    }

    [Test]
    public void ARequestThatCanOnlyBeDeniedBindsNoApprove()
    {
        var work = new WaitingWork();
        work.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.PendingApprovals.Add(new ApprovalView
            {
                ApprovalId = WaitingWork.ApprovalId,
                Subject = new ToolUseSubject { ToolName = "bash", Summary = "pnpm test --reporter [truncated]" },
                Approvable = false,
                RequestedAt = Samples.Time,
            });
        });
        var workspace = work.Present();
        var steering = Steering();
        var frame = Screen(workspace, steering, new FileScreen { Section = FileSection.Waiting });
        var binding = Bind(frame, steering, workspace)!;
        EveryPressIsBound(frame, binding);
        Assert.That(binding[KitPlace.Approve], Is.Null, "nothing the frame doesn't offer");
        Assert.That((binding.Model.Need!.DenyOffered, binding.Model.Need.ApproveOffered), Is.EqualTo((true, false)));
    }

    [Test]
    public void AnArmedApprovalConfirmsInTheFramesOwnWordsAndYesWaitsForTheWholeRequest()
    {
        var work = new WaitingWork();
        var workspace = work.Present();
        var steering = Steering();
        var screen = new FileScreen { Section = FileSection.Waiting };
        steering.Press(WorkspaceAction.Approve, workspace);
        var unread = Screen(workspace, steering, screen);
        var first = Bind(unread, steering, workspace)!;
        EveryPressIsBound(unread, first);
        IsPrompt(first[KitPlace.Cancel], unread.Footer[PromptSlot.FarRight]);
        Assert.That(first[KitPlace.Yes], Is.Null);
        var confirming = first.Model.Need!.Confirming!;
        Assert.That((confirming.Approving, confirming.Read, confirming.ReadFirst), Is.EqualTo((true, false, unread.Lines[unread.Lines.Count - 1].Words)));

        screen.ReadRequest(steering.ToRead(workspace)!, 1, 4, steering);
        screen.RequestDrawn(0, steering, Now);
        var read = Screen(workspace, steering, screen);
        var second = Bind(read, steering, workspace)!;
        EveryPressIsBound(read, second);
        IsPrompt(second[KitPlace.Yes], read.Footer[PromptSlot.Free]);
        var yes = second.Model.Need!.Confirming!;
        Assert.That((yes.Read, yes.Confirm, yes.Ask), Is.EqualTo((true, WorkspaceText.ConfirmLabel(WorkspaceAction.Approve), read.Lines[read.Lines.Count - 1].Words)));
    }

    [Test]
    public void AnArmedDenialConfirmsWhereDenyWasPressed()
    {
        var work = new WaitingWork();
        var workspace = work.Present();
        var steering = Steering();
        steering.Press(WorkspaceAction.Deny, workspace);
        var frame = Screen(workspace, steering, new FileScreen { Section = FileSection.Waiting });
        var binding = Bind(frame, steering, workspace)!;
        EveryPressIsBound(frame, binding);
        IsPrompt(binding[KitPlace.Cancel], frame.Footer[PromptSlot.Secondary]);
        Assert.That(binding.Model.Need!.Confirming!.Approving, Is.False);
        Assert.That(binding.Model.Need.Confirming.Ask, Is.EqualTo(frame.Lines[frame.Lines.Count - 1].Words));
    }

    [Test]
    public void TheActivityPageBindsStopTalkAndWriteInstructionAndOffersNoSend()
    {
        var workspace = Running(new WaitingWork(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var steering = Steering();
        var frame = Screen(workspace, steering, new FileScreen { Section = FileSection.Activity, Speak = true });
        var binding = Bind(frame, steering, workspace)!;
        EveryPressIsBound(frame, binding);
        IsPrompt(binding[KitPlace.Stop], frame.Footer[PromptSlot.Rare]);
        IsPrompt(binding[KitPlace.Talk], frame.Footer[PromptSlot.Secondary]);
        IsPrompt(binding[KitPlace.Field], frame.Footer[PromptSlot.FarRight]);
        Assert.That(binding[KitPlace.Send], Is.Null);
        Assert.That((binding.Model.Sendable, binding.Model.WhyNotSend, binding.Model.VoiceOffered, binding.Model.Need), Is.EqualTo((false, (string?)null, true, (KitNeed?)null)));
        // With no Send on the frame, only the connection's own words say why nothing can be sent.
        var retrying = new ConnectionStatus(ConnectionPhase.WaitingToRetry);
        Assert.That(Bind(frame, steering, workspace, connection: retrying)!.Model.WhyNotSend, Is.EqualTo(ConnectionText.Retrying(retrying)));
    }

    [Test]
    public void TheComposerBindsTheInstructionItsSendAndHoldToTalkInTheFramesWords()
    {
        var workspace = Running(new WaitingWork(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var steering = Steering();
        var screen = new FileScreen { Section = FileSection.Activity, Speak = true };
        screen.Instruction.Restore(workspace.Execution!.ExecutionId, "Run the tests again.", false, 0);
        var frame = Screen(workspace, steering, screen);
        var binding = Bind(frame, steering, workspace, screen.Instruction.Words)!;
        EveryPressIsBound(frame, binding);
        var field = frame.Lines.Single(line => line.Action == FileScreens.EditInstruction);
        Assert.That((binding[KitPlace.Field]!.Action, binding[KitPlace.Field]!.Available), Is.EqualTo((FileScreens.EditInstruction, field.Available)));
        Assert.That(binding.Model.Draft, Is.EqualTo(field.Words), "the draft is the instruction the frame shows");
        IsPrompt(binding[KitPlace.Close], frame.Footer[PromptSlot.Close]);
        IsPrompt(binding[KitPlace.Talk], frame.Footer[PromptSlot.Secondary]);
        IsPrompt(binding[KitPlace.Send], frame.Footer[PromptSlot.FarRight]);
        Assert.That((binding.Model.Sendable, binding.Model.WhyNotSend), Is.EqualTo((false, FileScreens.InstructionNotRead)));
    }

    [Test]
    public void SendSaysWhyNoInstructionCanBeSentInProductionsWords()
    {
        var workspace = Running(new WaitingWork());
        var steering = Steering();
        var screen = new FileScreen { Section = FileSection.Activity, Speak = true };
        screen.Instruction.Restore(workspace.Execution!.ExecutionId, "Run the tests again.", false, 0);
        var frame = Screen(workspace, steering, screen);
        var binding = Bind(frame, steering, workspace, screen.Instruction.Words)!;
        EveryPressIsBound(frame, binding);
        Assert.That(FileScreens.WhyNoInstruction(workspace), Is.Not.Null);
        Assert.That((binding.Model.Sendable, binding.Model.WhyNotSend), Is.EqualTo((false, FileScreens.WhyNoInstruction(workspace))));
        Assert.That(binding[KitPlace.Field]!.Available, Is.False, "the instruction's own line is unavailable for the same reason");
    }

    [Test]
    public void ATaskWithNothingRecordedBindsAndItsRailSaysSoInProductionsWords()
    {
        var workspace = Running(new WaitingWork(), new ActivityEntry[0], WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var steering = Steering();
        var frame = Screen(workspace, steering, new FileScreen { Section = FileSection.Activity });
        Assert.That(frame.Lines.Select(line => line.Words), Does.Contain(FileScreens.NoActivityYet));
        var binding = Bind(frame, steering, workspace)!;
        Assert.That(binding, Is.Not.Null);
        EveryPressIsBound(frame, binding);
        Assert.That((binding.Model.Trail.Count, binding.Model.Report, binding.Model.Said.Count), Is.EqualTo((0, (ActivityEntry?)null, 0)));
    }

    [Test]
    public void AnApprovalSeenFromActivitySaysWhereItWaitsInTheFramesOwnLine()
    {
        var workspace = new WaitingWork().Present();
        var steering = Steering();
        var frame = Screen(workspace, steering, new FileScreen { Section = FileSection.Activity });
        var binding = Bind(frame, steering, workspace)!;
        Assert.That(binding, Is.Not.Null);
        EveryPressIsBound(frame, binding);
        var doing = frame.Lines.First();
        Assert.That(binding.Model.Said, Is.EqualTo(new[] { doing }), "the frame's own line, its words and tone");
        Assert.That((doing.Tone, binding.Model.Need), Is.EqualTo((LineTone.Waiting, (KitNeed?)null)), "answered on Waiting, not here");
        Assert.That(binding.SectionRows.Single(row => row.Key == KitFileBinding.SectionRowKey + FileScreens.Key(FileSection.Waiting)).Selected, Is.False);
    }

    /// <summary>The running task finished its round with <paramref name="notes"/> needing the person's attention.</summary>
    private static WorkspacePresentation Failed(WorkspacePresentation running, params string[] notes)
    {
        var failed = new CharacterPresentation(running.Character.WorkstreamId, running.Character.Title, CharacterActivity.TurnFinished,
            StateLanguage.WordOf(StateLanguage.StateOf(CharacterActivity.TurnFinished, AttentionLevel.Notice)), AttentionLevel.Notice,
            notes, 0, false, false, false);
        return new WorkspacePresentation(failed, running.Objective, running.Execution, running.Runtime, running.Actions,
            running.Actions.Where(running.RequiresConfirmation).ToList(), running.Commands, running.Activity);
    }

    [Test]
    public void AFailedTasksAttentionNotesStandUnderItsStateAsTheFileSaysThem()
    {
        var workspace = Failed(Running(new WaitingWork(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt), "Checks failed: 1 failed, 23 passed.");
        var steering = Steering();
        var frame = Screen(workspace, steering, new FileScreen { Section = FileSection.Activity });
        var binding = Bind(frame, steering, workspace)!;
        Assert.That(binding, Is.Not.Null);
        EveryPressIsBound(frame, binding);
        Assert.That(binding.Model.Said.Select(line => (line.Words, line.Tone)), Is.EqualTo(new[] { ("Checks failed: 1 failed, 23 passed.", LineTone.Problem) }));
    }

    [Test]
    public void WhyNothingCanBeSentStandsUnderTheStateInProductionsWords()
    {
        // Its agent app offers nothing now, so the file says why nothing can be sent.
        var running = Running(new WaitingWork());
        var workspace = new WorkspacePresentation(running.Character, running.Objective, running.Execution, running.Runtime, new WorkspaceAction[0],
            new WorkspaceAction[0], running.Commands, running.Activity);
        Assert.That(WorkspaceText.WhyNoActions(workspace), Is.Not.Null);
        var steering = Steering();
        var frame = Screen(workspace, steering, new FileScreen { Section = FileSection.Activity });
        var binding = Bind(frame, steering, workspace)!;
        Assert.That(binding, Is.Not.Null);
        EveryPressIsBound(frame, binding);
        Assert.That(binding.Model.Said.Select(line => line.Words), Is.EqualTo(new[] { WorkspaceText.WhyNoActions(workspace) }));
    }

    [Test]
    public void ALongActivityBindsItsPagerAsTheFileDrewIt()
    {
        // Two notes of two rows each ahead of the recent entries are more than three rows hold: the page turns.
        var workspace = Failed(Running(new WaitingWork(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt),
            "Checks failed: 1 failed, 23 passed.", "It stopped before the tests finished.");
        var steering = Steering();
        var screen = new FileScreen { Section = FileSection.Activity, Speak = true };
        var frame = Screen(workspace, steering, screen, rows: 3);
        Assert.That(screen.Pages, Is.GreaterThan(1));
        var binding = Bind(frame, steering, workspace)!;
        Assert.That(binding, Is.Not.Null);
        EveryPressIsBound(frame, binding);
        var pager = binding[KitPlace.Pager]!;
        Assert.That((pager.Action, pager.Available), Is.EqualTo((Footer.NextPage, true)));
        Assert.That(pager.Words, Is.EqualTo(frame.Lines.Concat(new PageLine[0]).Where(line => line.Action == Footer.NextPage).Select(line => line.Words)
            .Concat(frame.Footer.All.Where(each => each.Prompt.Kind == PromptKind.NextPage).Select(each => each.Prompt.Words)).Single()));
    }

    [Test]
    public void TheSectionsStandAsSpineRowsTheOneShowingLit()
    {
        var workspace = Running(new WaitingWork(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var steering = Steering();
        var frame = Screen(workspace, steering, new FileScreen { Section = FileSection.Activity });
        var binding = Bind(frame, steering, workspace)!;
        Assert.That(binding.SectionRows.Select(row => (row.Key, row.Words.Text, row.Selected)),
            Is.EqualTo(frame.Sections.Select(section => (KitFileBinding.SectionRowKey + section.Key, section.Words, section.Chosen))));
        Assert.That(binding.SectionRows.All(row => !row.Words.AnyTheirs), Is.True, "Halcyonic's own names for its sections");
    }

    /// <summary>The running task with <paramref name="commands"/> as its recent commands, newest first.</summary>
    private static WorkspacePresentation Commanded(WorkspacePresentation running, params CommandFeedback[] commands) =>
        new(running.Character, running.Objective, running.Execution, running.Runtime, running.Actions,
            running.Actions.Where(running.RequiresConfirmation).ToList(), commands, running.Activity);

    [Test]
    public void AfterAnInstructionIsSentItsFeedbackStandsUnderTheStateInTheFilesOwnLine()
    {
        var sent = new CommandFeedback("command-1", CommandType.ExecutionSendInstruction, CommandStatus.Accepted, "Sent. Waiting for the agent…", waits: true);
        var workspace = Commanded(Running(new WaitingWork(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt), sent);
        var steering = Steering();
        var frame = Screen(workspace, steering, new FileScreen { Section = FileSection.Activity });
        var binding = Bind(frame, steering, workspace)!;
        Assert.That(binding, Is.Not.Null);
        EveryPressIsBound(frame, binding);
        var line = frame.Lines.Single(each => each.Words == sent.Text);
        Assert.That(binding.Model.Said, Is.EqualTo(new[] { line }), "the file's own line, its words and its wait");
    }

    [Test]
    public void ANoticeStandsFirstThenWhatActivityLeadsWithThenTheFeedback()
    {
        var refused = new CommandFeedback("command-1", CommandType.ExecutionSendInstruction, CommandStatus.Rejected, "Couldn't do that: it was busy.");
        var workspace = Commanded(Failed(Running(new WaitingWork(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt), "Checks failed: 1 failed, 23 passed."), refused);
        var steering = Steering();
        var frame = Screen(workspace, steering, new FileScreen { Section = FileSection.Activity, Notice = FileScreens.InstructionChanged }, rows: 8);
        var binding = Bind(frame, steering, workspace)!;
        Assert.That(binding, Is.Not.Null);
        EveryPressIsBound(frame, binding);
        Assert.That(binding.Model.Said.Select(line => line.Words),
            Is.EqualTo(new[] { FileScreens.InstructionChanged, "Checks failed: 1 failed, 23 passed.", refused.Text }));
    }

    [Test]
    public void AnOutcomeThatCantBeToldStandsOnceAsTheKitsNotice()
    {
        var unknown = new CommandFeedback("command-1", CommandType.ExecutionRespondToApproval, CommandStatus.Failed, WorkspaceText.NotSure(CommandType.ExecutionRespondToApproval));
        var workspace = Commanded(Running(new WaitingWork(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt), unknown);
        var steering = Steering();
        var frame = Screen(workspace, steering, new FileScreen { Section = FileSection.Activity });
        var binding = Bind(frame, steering, workspace)!;
        Assert.That(binding, Is.Not.Null);
        EveryPressIsBound(frame, binding);
        Assert.That(binding.Model.Notice!.Words.Text, Is.EqualTo(unknown.Text));
        Assert.That(binding.Model.Said, Is.Empty, "said once, as the notice over the activity it points to");
    }

    [Test]
    public void AnActivityEntryIsNeverTakenForANotice()
    {
        var workspace = Running(new WaitingWork(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var steering = Steering();
        var frame = Screen(workspace, steering, new FileScreen { Section = FileSection.Activity, Speak = true });
        Assert.That(frame.Lines.Select(line => line.Words), Does.Contain(Recorded[0].Text), "the page leads with a recorded entry, as a notice would stand");
        var binding = Bind(frame, steering, workspace)!;
        Assert.That(binding, Is.Not.Null);
        Assert.That(binding.Model.Said, Is.Empty, "no notice, no attention, nothing waiting, nothing sent: nothing said");
    }

    [Test]
    public void TheHistoryNoteIsNeverTakenForANotice()
    {
        var workspace = Running(new WaitingWork(), new ActivityEntry[0], WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var steering = Steering();
        const string note = "Earlier activity isn't loaded yet.";
        var frame = Screen(workspace, steering, new FileScreen { Section = FileSection.Activity, ActivityNote = note });
        Assert.That(frame.Lines.Select(line => line.Words), Does.Contain(note));
        var binding = Bind(frame, steering, workspace);
        // The kit has no place for the file's history note, so the file draws the page as before.
        Assert.That(binding?.Model.Said.Select(line => line.Words) ?? new string[0], Does.Not.Contain(note));
        Assert.That(binding, Is.Null);
    }

    [Test]
    public void NothingIsBoundWhereTheKitCantDrawTheFrameWhole()
    {
        var steering = Steering();
        // Changes: answers opening their full text beside the page.
        var changes = FileScreensTests.Read(FileSection.Changes);
        var running = Running(new WaitingWork(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        Assert.That(Bind(Screen(running, steering, changes), steering, running), Is.Null, "Changes");
        // The composer offering the results beside the instruction.
        var results = new FileScreen { Section = FileSection.Activity, ResultsOffered = true, InstructionResultsOffered = true };
        results.Instruction.Restore(running.Execution!.ExecutionId, "Run the tests again.", false, 0);
        Assert.That(Bind(Screen(running, steering, results), steering, running), Is.Null, "results");
        // A confirmation other than an approval's or a denial's: Stop.
        var stopping = Steering();
        stopping.Press(WorkspaceAction.Interrupt, running);
        Assert.That(Bind(Screen(running, stopping, new FileScreen { Section = FileSection.Activity }), stopping, running), Is.Null, "Stop's confirmation");
        // Questions: answers to choose, a secret, several prompts.
        foreach (var prompts in new[]
        {
            new[] { new QuestionPrompt { Key = "pick", Text = "Which one?", Options = new List<QuestionOption> { new() { Label = "This" }, new() { Label = "That" } } } },
            new[] { new QuestionPrompt { Key = "key", Text = "Paste the key.", FreeText = true, Secret = true } },
            new[] { new QuestionPrompt { Key = "a", Text = "First?", FreeText = true }, new QuestionPrompt { Key = "b", Text = "Second?", FreeText = true } },
        })
        {
            var work = new WaitingWork();
            work.Change(execution =>
            {
                execution.PendingApprovals.Clear();
                execution.PendingQuestions.Add(new QuestionView { QuestionId = "q1", Answerable = true, AskedAt = Samples.Time, Prompts = prompts.ToList() });
            });
            var asked = work.Present();
            var frame = Screen(asked, steering, new FileScreen { Section = FileSection.Waiting });
            Assert.That(Bind(frame, steering, asked), Is.Null, prompts[0].Text);
        }
    }
}

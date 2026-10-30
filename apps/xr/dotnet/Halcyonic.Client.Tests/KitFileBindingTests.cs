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
        new(1, "2026-10-28T09:00:01.000Z", ActivityKind.Turn, "Round Started", false),
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

    /// <summary>The task's own routes Activity offers under its log, each the line that opens it.</summary>
    private static readonly string[] Routes = { FileScreens.ShowTaskHere, FileScreens.OpenWorkspaceRequest, FileScreens.OpenResults, FileScreens.OpenCharacterColour };

    /// <summary>The routes the binding holds, against the frame's lines that open them, both in the frame's order.</summary>
    private static void RoutesAreTheFramesLines(MenuFrame frame, KitFileBinding binding)
    {
        var lines = frame.Lines.Where(line => line.Action != null && Routes.Contains(line.Action)).ToList();
        Assert.That(lines, Is.Not.Empty, "the frame offers routes");
        Assert.That(binding.Routes.Select(route => (route.Place, route.Action, route.Key, route.Words, route.Icon, route.Available, route.Reason, route.Holds)),
            Is.EqualTo(lines.Select(line => (KitPlace.Route, line.Action!, line.Key, line.Words, line.Icon, line.Available, (string?)null, false))));
    }

    [Test]
    public void ThePlainRunningActivityPageBindsItsRoutesInTheFramesOrder()
    {
        var workspace = Running(new WaitingWork(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var steering = Steering();
        var screen = new FileScreen { Section = FileSection.Activity, Speak = true, ShowTaskHereOffered = true, WorkspaceRequestOffered = true, ResultsOffered = true,
            CharacterColourOffered = true };
        var frame = Screen(workspace, steering, screen, rows: 12);
        Assert.That(frame.Lines.Where(line => line.Action != null).Select(line => line.Action), Is.EqualTo(Routes), "the frame offers every route, on one page");
        var binding = Bind(frame, steering, workspace)!;
        Assert.That(binding, Is.Not.Null);
        EveryPressIsBound(frame, binding);
        RoutesAreTheFramesLines(frame, binding);
        Assert.That(binding[KitPlace.Route]!.Action, Is.EqualTo(FileScreens.ShowTaskHere), "the first route, as the frame lays it");
    }

    [Test]
    public void EachRouteAloneBindsAsItsLine()
    {
        var workspace = Running(new WaitingWork(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        foreach (var route in Routes)
        {
            var steering = Steering();
            var screen = new FileScreen
            {
                Section = FileSection.Activity, Speak = true, ShowTaskHereOffered = route == FileScreens.ShowTaskHere,
                WorkspaceRequestOffered = route == FileScreens.OpenWorkspaceRequest, ResultsOffered = route == FileScreens.OpenResults,
                CharacterColourOffered = route == FileScreens.OpenCharacterColour,
            };
            var frame = Screen(workspace, steering, screen, rows: 12);
            var binding = Bind(frame, steering, workspace);
            Assert.That(binding, Is.Not.Null, route);
            EveryPressIsBound(frame, binding!);
            RoutesAreTheFramesLines(frame, binding!);
        }
    }

    [Test]
    public void RoutesTurnWithActivitysPage()
    {
        // Two notes of two rows each and every route are more than three rows hold: the routes stand on the pages the file gives them.
        var workspace = Failed(Running(new WaitingWork(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt),
            "Checks failed: 1 failed, 23 passed.", "It stopped before the tests finished.");
        var steering = Steering();
        var screen = new FileScreen { Section = FileSection.Activity, Speak = true, ShowTaskHereOffered = true, WorkspaceRequestOffered = true, ResultsOffered = true,
            CharacterColourOffered = true };
        var seen = new List<string>();
        var pages = 0;
        do
        {
            var frame = Screen(workspace, steering, screen, rows: 3);
            var binding = Bind(frame, steering, workspace)!;
            Assert.That(binding, Is.Not.Null, "page " + (screen.Page + 1));
            EveryPressIsBound(frame, binding);
            Assert.That(binding[KitPlace.Pager], Is.Not.Null);
            if (binding.Routes.Any()) RoutesAreTheFramesLines(frame, binding);
            seen.AddRange(binding.Routes.Select(route => route.Action));
            screen.NextPage();
            pages++;
        }
        while (screen.Page != 0 && pages < 10);
        Assert.That(seen, Is.EqualTo(Routes), "every route once, in the frame's order, across the pages");
    }

    /// <summary>A file column on an agent's question, as the workspace opens it on Waiting (AnswerFlowTests' host).</summary>
    private static (FileMenuHost Host, FileColumn Column, AskingWork Work) Asking(QuestionView? question = null, IEnumerable<CommandPolicy>? policies = null)
    {
        var host = new FileMenuHost { Submissions = new CommandSubmissions() };
        var work = new AskingWork(question, policies);
        var column = new FileColumn(host, () => work.Present(host.Live, host.Submissions), new CommandFactory(Samples.Client), () => null, _ => null, () => "");
        return (host, column, work);
    }

    /// <summary>The frame drawn whole and a moment passed, as the plane reports a draw.</summary>
    private static void Drawn(FileMenuHost host, FileColumn column)
    {
        column.Drawn(column.Frame!, null);
        host.Wait(1);
        column.Tick();
    }

    /// <summary>The column's frame bound as the workspace binds it: its steering's armed action, the work as it stands.</summary>
    private static KitFileBinding? BindColumn(FileMenuHost host, FileColumn column, AskingWork work) =>
        KitFileBinding.Of(column.Frame!, column.Steering.Armed, work.Present(host.Live, host.Submissions), "", Live, "Storefront", Spine);

    /// <summary>The frame binds, press for press, and its question's words are the frame's own lines, in order.</summary>
    private static KitFileBinding BindsWhole(FileMenuHost host, FileColumn column, AskingWork work, string where)
    {
        var binding = BindColumn(host, column, work);
        Assert.That(binding, Is.Not.Null, where);
        EveryPressIsBound(column.Frame!, binding!);
        Assert.That(binding!.Model.Ask, Is.Not.Null, where);
        Assert.That(binding.Model.Ask!.Lines.Select(line => (line.Words, line.Rows, line.FromRow)),
            Is.EqualTo(column.Frame!.Lines.Where(line => line.Action == null).Select(line => (line.Words, line.Rows, line.FromRow))), where + ": the question's lines");
        return binding;
    }

    /// <summary>Takes the frame's main forward press, after its draw, as a person reading on does.</summary>
    private static void Forward(FileMenuHost host, FileColumn column)
    {
        Drawn(host, column);
        var next = column.Frame!.Footer[PromptSlot.FarRight]!;
        Assert.That(next.Available, Is.True, next.Words);
        column.Act(next.Id, null);
    }

    [Test]
    public void EveryFrameOfAnAnswerBindsPressForPressInTheFramesOrder()
    {
        var (host, column, work) = Asking();
        var frames = 0;
        // The question, in its parts where it takes several, then its answers.
        for (var turns = 0; turns < 30 && column.Screen.Question.QuestionPart != null; turns++, frames++)
        {
            BindsWhole(host, column, work, "a part of the question");
            Forward(host, column);
        }
        Drawn(host, column);
        var first = BindsWhole(host, column, work, "the first question");
        var answers = column.Frame!.Lines.Where(line => line.Action == FileScreens.Choose).ToList();
        Assert.That(answers, Is.Not.Empty);
        Assert.That(first.Controls.Where(control => control.Place == KitPlace.Choice).Select(control => (control.Key, control.Words, control.Chosen)),
            Is.EqualTo(answers.Select(line => (line.Key, line.Words, line.Chosen))), "each answer, in the frame's order");
        Assert.That(first[KitPlace.Forward]!.Action, Is.EqualTo(FileScreens.NextQuestion));
        Assert.That(first[KitPlace.Back], Is.Null, "no way back from the first question");
        Assert.That(first.Controls.Any(control => control.Place == KitPlace.Step && control.Action == FileScreens.TypeAnswer), Is.True, "Type my answer, as the frame offers it");
        // Choosing lights the answer chosen.
        var dark = answers.First(line => line.Words.StartsWith("Dark", StringComparison.Ordinal));
        column.Act(dark.Action!, dark.Key);
        Drawn(host, column);
        var chosen = BindsWhole(host, column, work, "an answer chosen");
        Assert.That(chosen.Controls.Single(control => control.Place == KitPlace.Choice && control.Key == dark.Key).Chosen, Is.True);
        column.Act(FileScreens.NextQuestion, null);
        for (var turns = 0; turns < 30 && column.Screen.Question.QuestionPart != null; turns++) Forward(host, column);
        Drawn(host, column);
        var second = BindsWhole(host, column, work, "the second question");
        Assert.That(second[KitPlace.Back]!.Action, Is.EqualTo(FileScreens.BackQuestion));
        var orders = column.Frame!.Lines.First(line => line.Action == FileScreens.Choose && line.Words.StartsWith("Orders", StringComparison.Ordinal));
        column.Act(orders.Action!, orders.Key);
        Drawn(host, column);
        // On to the person's answers, every page of them, each answer a row back to its question.
        column.Act(column.Frame!.Footer[PromptSlot.FarRight]!.Id, null);
        Drawn(host, column);
        Assert.That(column.Screen.Question.Reviewing, Is.True);
        for (var turns = 0; turns < 30; turns++, frames++)
        {
            var review = BindsWhole(host, column, work, "your answers");
            Assert.That(review.Controls.Where(control => control.Place == KitPlace.Step).Select(control => control.Action).Distinct(), Is.EqualTo(new[] { FileScreens.GoToQuestion }),
                "each answer a row back to its question");
            if (review[KitPlace.Forward]!.Action == FileScreens.SendAnswer) break;
            Forward(host, column);
        }
        Assert.That(BindColumn(host, column, work)![KitPlace.Forward]!.Action, Is.EqualTo(FileScreens.SendAnswer));
    }

    [Test]
    public void SendingAnswersConfirmsInTheFramesOwnWords()
    {
        var policies = new[] { new CommandPolicy { CommandType = CommandType.ExecutionAnswerQuestion, Policy = PolicyCategory.ReviewRequired } };
        var (host, column, work) = Asking(policies: policies);
        for (var turns = 0; turns < 30 && column.Screen.Question.QuestionPart != null; turns++) Forward(host, column);
        Drawn(host, column);
        foreach (var label in new[] { "Dark", "Orders" })
        {
            var line = column.Frame!.Lines.First(each => each.Action == FileScreens.Choose && each.Words.StartsWith(label, StringComparison.Ordinal));
            column.Act(line.Action!, line.Key);
            Forward(host, column);
            for (var turns = 0; turns < 30 && column.Screen.Question.QuestionPart != null; turns++) Forward(host, column);
            Drawn(host, column);
        }
        for (var turns = 0; turns < 30 && column.Frame!.Footer[PromptSlot.FarRight]!.Id != FileScreens.SendAnswer; turns++) Forward(host, column);
        Forward(host, column);
        Drawn(host, column);
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Answer));
        var confirming = BindsWhole(host, column, work, "the answers' confirmation");
        Assert.That(confirming.Model.Ask!.Confirming, Is.True);
        IsPrompt(confirming[KitPlace.Cancel], column.Frame!.Footer.All.Select(each => each.Prompt).Single(prompt => prompt.Kind == PromptKind.Cancel));
        Assert.That(host.Sent, Is.Empty, "nothing sent before Yes");
    }

    [Test]
    public void ASecretPromptBindsOnlyTheWayOnAndNoWayToTypeIt()
    {
        var question = AskingWork.Scripted();
        question.Prompts.Add(new QuestionPrompt { Key = "token", Header = "Token", Text = "Paste the deploy token.", FreeText = true, Secret = true });
        var (host, column, work) = Asking(question);
        Drawn(host, column);
        var binding = BindsWhole(host, column, work, "a question asking for a secret");
        // Halcyonic never takes a secret: no answer, no typing, nothing forward, only what it can't answer and the way on.
        Assert.That(binding.Controls.Where(control => control.Place is KitPlace.Choice or KitPlace.Step or KitPlace.Forward or KitPlace.Field or KitPlace.Send), Is.Empty);
        Assert.That(binding.Model.Ask!.Lines.Select(line => line.Words), Does.Contain(WorkspaceText.CannotAnswer(question)));
        foreach (var control in binding.Controls.Where(control => control.Place != KitPlace.Section && control.Place != KitPlace.Close))
        {
            column.Act(control.Action, control.Key);
            Assert.That(host.Keyboard, Is.Null, control.Words + " opened a way to type");
        }
    }

    [Test]
    public void AnApprovalsRequestInPartsBindsItsNextPartAndYesOnlyAfterTheLast()
    {
        var work = new WaitingWork();
        var workspace = work.Present();
        var steering = Steering();
        var screen = new FileScreen { Section = FileSection.Waiting };
        steering.Press(WorkspaceAction.Approve, workspace);
        screen.ReadRequest(steering.ToRead(workspace)!, 9, 3, steering);
        for (var part = 0; part < 3; part++)
        {
            var frame = Screen(workspace, steering, screen);
            var binding = Bind(frame, steering, workspace)!;
            Assert.That(binding, Is.Not.Null, "part " + (part + 1));
            EveryPressIsBound(frame, binding);
            var next = binding.Controls.Single(control => control.Place == KitPlace.Step);
            Assert.That((next.Action, next.Key, next.Words), Is.EqualTo((FileScreens.NextPart, (string?)FileScreens.RequestKey, FileScreens.NextPartWords(part, 3))));
            Assert.That(binding[KitPlace.Yes] != null, Is.EqualTo(frame.Footer.All.Any(each => each.Prompt.Kind == PromptKind.Yes)), "Yes as the frame offers it");
            var drawnAt = Now.AddMinutes(part);
            screen.RequestDrawn(part, steering, drawnAt);
            screen.NextRequestPart(steering, drawnAt + FileScreen.TurnGuard);
        }
    }

    [Test]
    public void TypingAnAnswerAndAnUnavailableChoiceStayTheFilesOwn()
    {
        var (host, column, work) = Asking();
        for (var turns = 0; turns < 30 && column.Screen.Question.QuestionPart != null; turns++) Forward(host, column);
        Drawn(host, column);
        column.Act(FileScreens.TypeAnswer, null);
        Drawn(host, column);
        Assert.That(column.Screen.Question.Writing, Is.True);
        Assert.That(BindColumn(host, column, work), Is.Null, "typing an answer: its own keyboard and input");

        var alike = AskingWork.Scripted();
        alike.Prompts[0].Options = new List<QuestionOption> { new() { Label = "Yes" }, new() { Label = " Yes " }, new() { Label = "No" } };
        var (host2, column2, work2) = Asking(alike);
        for (var turns = 0; turns < 30 && column2.Screen.Question.QuestionPart != null; turns++) Forward(host2, column2);
        Drawn(host2, column2);
        var why = column2.Frame!.Lines.First(line => line.Action == FileScreens.ReadWhy);
        column2.Act(why.Action!, why.Key);
        Drawn(host2, column2);
        Assert.That(column2.Screen.Question.ExplainingAmbiguity, Is.True);
        Assert.That(BindColumn(host2, column2, work2), Is.Null, "why an answer can't be chosen");
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
    }

    [Test]
    public void AQuestionNotYetLaidOutBindsAsTheFileDrewIt()
    {
        var steering = Steering();
        // Answers to choose, a secret, several prompts: before the question is laid out, what it asks and Send answer waiting, with why.
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
            var binding = Bind(frame, steering, asked);
            Assert.That(binding, Is.Not.Null, prompts[0].Text);
            EveryPressIsBound(frame, binding!);
            Assert.That(binding!.Model.Ask!.Lines.Select(line => line.Words), Is.EqualTo(frame.Lines.Where(line => line.Action == null).Select(line => line.Words)), prompts[0].Text);
        }
    }
}

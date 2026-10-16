using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>A host for one column: a clock to move, words wrapped forty to a row, and every send kept.</summary>
internal sealed class FileMenuHost : IMenuHost
{
    public List<CommandEnvelope> Sent { get; } = new();

    public CommandSubmissions? Submissions { get; set; }

    public bool Live { get; set; } = true;

    public bool KeyboardOffered { get; set; } = true;

    public (string Text, string Prompt, Action<string> Done)? Keyboard { get; private set; }

    public Action? KeyboardDismissed { get; private set; }

    public ClientProjection? Projection { get; set; }
    public ClientProjection? State => Projection;

    public bool Connected => Live;

    public bool Demonstration => false;

    public double Now { get; set; } = 100;

    public DateTimeOffset Clock { get; set; } = DateTimeOffset.Parse("2026-10-02T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    public TimeZoneInfo Zone => TimeZoneInfo.Utc;

    public TextSize TextSize { get; set; } = TextSize.Standard;

    public bool VoiceOffered => true;

    public ControlPlaneApi? Api { get; set; }

    public Task<CommandAckMessage>? Submit(CommandEnvelope command)
    {
        if (!Live) return null;
        Task<CommandAckMessage> Send(CommandEnvelope sent)
        {
            Sent.Add(sent);
            return Task.FromResult(new CommandAckMessage { CommandId = sent.CommandId, Disposition = CommandAckDisposition.Accepted });
        }
        return Submissions == null ? Send(command) : Submissions.Submit(Send, command, (command as ExecutionAnswerQuestionCommand)?.Payload.ExecutionId ?? "");
    }

    public void OpenKeyboard(string text, string prompt, Action<string> done, Action? dismissed = null) { Keyboard = (text, prompt, done); KeyboardDismissed = dismissed; }

    public int RowsOf(string words, float columnDegrees) => Math.Max(1, (words.Length + 39) / 40);

    public int RowsOf(PageLine line, float columnDegrees) => RowsOf(line.Words, columnDegrees);

    public bool FitsHalf(PageLine answer, float columnDegrees) => false;

    public int TitleRows(string subject, float columnDegrees) => 1;

    public int PageRows(bool sourceLine) => MenuFrame.RowsAPage(TextSize, sourceLine);

    /// <summary>The page's height for a subject in this many rows, where it is not the stage's at the text size now, as one that comes out the same at both sizes.</summary>
    public Func<int, float>? Height { get; set; }

    public float PageHeight(int subjectRows, bool besideMenu, bool focused = false) => Height?.Invoke(subjectRows) ?? MenuPage.Height(TextSize, subjectRows, focused: focused);

    public void OpenFile(string workstreamId)
    {
    }

    public void OpenNewProject(string? projectId, string? projectName)
    {
    }

    /// <summary>Moves both clocks on.</summary>
    public void Wait(double seconds)
    {
        Now += seconds;
        Clock += TimeSpan.FromSeconds(seconds);
    }
}

public class FileColumnTests
{
    private readonly CommandFactory factory = new(Samples.Client);

    private (FileMenuHost Host, WaitingWork Work, FileColumn Column) ComposingTask()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Running; }, WorkstreamStatus.Running);
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Instruct));
        column.ConfigureActivityComposer(() => new RowBudget(8), () => new RowBudget(8));
        column.HoldStarted(FileScreens.HoldToTalk);
        column.Heard("Carry on carefully");
        return (host, work, column);
    }

    [Test]
    public void PassiveActivityNeverReadsTheEditorOrItsFrozenReview()
    {
        var (host, _, column) = ComposingTask();
        var context = column.ActivityContextFrame!;
        Assert.That(context, Is.Not.Null.And.Not.SameAs(column.Frame));
        Assert.That(context.Source, Is.EqualTo("Activity"));
        Assert.That(context.Sections, Is.Empty);
        Assert.That(context.Footer.All, Is.Empty);
        Assert.That(context.Side, Is.Null);
        Assert.That(context.Lines.All(line => line.Action == null && line.Control == LineControl.None), Is.True);
        column.Drawn(context, null);
        host.Wait(1);
        column.Act(FileScreens.SendInstruction, null);
        Assert.That(column.Screen.Instruction.Read, Is.False);
        Assert.That(column.Steering.Armed, Is.Null);
        Assert.That(host.Sent, Is.Empty);
        ReadInstructionDraft(host, column);
        column.Act(FileScreens.SendInstruction, null);
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Instruct));
        column.Drawn(column.ActivityContextFrame!, null);
        host.Wait(1);
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent, Is.Empty, "context drawing is not the frozen review drawing");
        Draw(host, column);
        column.Act(FileScreens.Yes, null);
        Assert.That(((ExecutionSendInstructionCommand)host.Sent.Single()).Payload.Text, Is.EqualTo("Carry on carefully"));
    }

    [Test]
    public void HidingKeepsOneScopedDraftButDropsAuthorityAndLateKeyboardInput()
    {
        var (host, _, column) = ComposingTask();
        ReadInstructionDraft(host, column);
        column.Act(FileScreens.EditInstruction, null);
        var late = host.Keyboard!.Value.Done;
        Draw(host, column);
        Assert.That(column.Frame!.Footer[PromptSlot.Close]?.Words, Is.EqualTo("Hide composer"));
        column.Act(FileScreens.HideInstruction, null);
        Assert.That(column.Screen.Instruction.Hidden, Is.True);
        Assert.That(column.Screen.Instruction.Read, Is.False);
        Assert.That(column.ActivityContextFrame, Is.Null);
        Assert.That(column.ContinuationDraft()!.Text, Is.EqualTo("Carry on carefully"));
        late("Outdated keyboard words");
        column.Act(FileScreens.CancelInstruction, null);
        Assert.That(column.ContinuationDraft()!.Text, Is.EqualTo("Carry on carefully"), "a stale discard is not offered while hidden");
        var keyboard = host.Keyboard;
        column.Compact();
        column.FocusLeft();
        column.Act(FileScreens.TellIt, null);
        Assert.That(host.Keyboard, Is.EqualTo(keyboard), "reopening does not summon a keyboard");
        Assert.That((column.Screen.Instruction.Open, column.Screen.Instruction.InputFocused, column.Screen.Instruction.Words), Is.EqualTo((true, false, "Carry on carefully")));
        Assert.That(column.Screen.Instruction.Read, Is.False);
        Assert.That(host.Sent, Is.Empty);
        ReadInstructionDraft(host, column);
        column.Act(FileScreens.CancelInstruction, null);
        Assert.That(column.ContinuationDraft(), Is.Null, "explicit discard clears the same buffer");
    }

    [Test]
    public void ContextConfigurationIsOptInAndStableUntilContentOrBudgetChanges()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Running; }, WorkstreamStatus.Running);
        var current = FileScreensTests.Offering(work.Present(), WorkspaceAction.Instruct);
        var column = Column(host, () => current);
        Assert.That(column.ActivityContextFrame, Is.Null);
        Func<PageBudget> activity = () => new RowBudget(8);
        Func<PageBudget> composer = () => new RowBudget(8);
        column.ConfigureActivityComposer(activity, composer);
        column.HoldStarted(FileScreens.HoldToTalk);
        column.Heard("Keep these words");
        var context = column.ActivityContextFrame;
        column.ConfigureActivityComposer(activity, composer);
        Assert.That(column.ActivityContextFrame, Is.SameAs(context));
        host.Wait(1);
        column.Tick();
        Assert.That(column.ActivityContextFrame, Is.SameAs(context));
        var entry = new ActivityEntry(1, "2026-09-26T09:00:01.000Z", ActivityKind.Message, "I checked the tests", true);
        current = new WorkspacePresentation(current.Character, current.Objective, current.Execution, current.Runtime, current.Actions,
            current.Actions.Where(current.RequiresConfirmation).ToList(), current.Commands, new[] { entry });
        host.Wait(1);
        column.Tick();
        Assert.That(column.ActivityContextFrame, Is.Not.SameAs(context));
        var reported = column.ActivityContextFrame!.Lines.Single(line => line.Words == "I checked the tests");
        Assert.That((reported.Chip, reported.Runs.AllTheirs), Is.EqualTo(("Agent says", true)));
        column.ConfigureActivityComposer(() => new HeightBudget(0), () => new RowBudget(3));
        Assert.That(column.Screen.Instruction.Words, Is.EqualTo("Keep these words"));
        Assert.That(column.ActivityContextFrame!.Lines.Select(line => line.Words), Has.Some.Contains("make room for Activity"));
        Assert.That(column.ActivityContextFrame.Footer.All, Is.Empty);
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void HiddenDraftCannotRetargetAnExecutionChange()
    {
        var (host, work, column) = ComposingTask();
        Draw(host, column);
        column.Act(FileScreens.HideInstruction, null);
        var previous = column.Screen.Instruction.ExecutionId;
        WorkspaceInstructionDraft? retired = null;
        column.OrdinaryDraftLeaving += (execution, draft) => { Assert.That(execution, Is.EqualTo(previous)); retired = draft; };
        work.Change(execution => execution.ExecutionId = "next-execution");
        host.Wait(1);
        column.Tick();
        Assert.That(retired!.Text, Is.EqualTo("Carry on carefully"));
        Assert.That(column.ContinuationDraft(), Is.Null);
        Assert.That(column.Screen.Instruction.ExecutionId, Is.Null);
        Assert.That(column.ActivityContextFrame, Is.Null);
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void PassiveContextKeepsActivityPageAndCannotTurnItOrAuthorizeAfterCompact()
    {
        var (host, _, column) = ComposingTask();
        Assert.That(column.RestoreContinuation(new WorkspaceContinuation
        {
            WorkstreamId = column.Now!.Character.WorkstreamId, ExecutionId = column.Now.Execution!.ExecutionId,
            Section = WorkspaceSection.Activity, Page = 2,
            Instruction = new WorkspaceInstructionDraft { Text = new string('a', 600), Spoken = true, Part = 0 },
        }), Is.True);
        column.Reflow();
        Assert.That(column.Screen.Page, Is.EqualTo(2), "context packing never mutates the underlying Activity page");
        var context = column.ActivityContextFrame!;
        column.Compact();
        column.Drawn(context, null);
        column.Act(FileScreens.SendInstruction, null);
        column.Act(FileScreens.Yes, null);
        Assert.That(column.Screen.Instruction.Words, Is.EqualTo(new string('a', 600)));
        Assert.That(column.Screen.Instruction.Read, Is.False);
        Assert.That(column.Steering.Armed, Is.Null);
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void HideRetainsExactPartButRequiresOrderedReadingAgain()
    {
        var instruction = new FileInstruction();
        var now = DateTimeOffset.UtcNow;
        instruction.Begin("run-a");
        instruction.Changed(new string('x', 200), spoken: true);
        instruction.Measure(20, new RowBudget(8), TextSize.Standard, pairedPager: false);
        Assert.That(instruction.Parts, Is.GreaterThan(1));
        instruction.Drawn(now);
        instruction.Next(now.AddSeconds(1));
        Assert.That(instruction.Part, Is.EqualTo(1));
        instruction.Hide();
        instruction.Begin("run-a");
        instruction.Measure(20, new RowBudget(8), TextSize.Standard, pairedPager: false);
        Assert.That((instruction.Part, instruction.Words, instruction.Spoken, instruction.InputFocused), Is.EqualTo((1, new string('x', 200), true, false)));
        instruction.Drawn(now.AddSeconds(2));
        Assert.That(instruction.Read, Is.False, "the retained part does not inherit its old read prefix");
        instruction.Begin("run-b");
        Assert.That((instruction.Part, instruction.Words, instruction.Spoken), Is.EqualTo((0, "", false)));
    }

    [Test]
    public void SameExecutionIdOnAnotherTaskCannotReceiveAnOldEditorsInputOrAction()
    {
        var host = new FileMenuHost();
        var first = new WaitingWork();
        first.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Running; }, WorkstreamStatus.Running);
        var current = FileScreensTests.Offering(first.Present(), WorkspaceAction.Instruct);
        var column = Column(host, () => current);
        column.ConfigureActivityComposer(() => new RowBudget(8), () => new RowBudget(8));
        column.Act(FileScreens.TellIt, null);
        var oldInput = host.Keyboard!.Value.Done;
        var second = new WaitingWork();
        second.Workstream.WorkstreamId = "different-task";
        second.Change(execution => { execution.WorkstreamId = "different-task"; execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Running; }, WorkstreamStatus.Running);
        current = FileScreensTests.Offering(second.Present(), WorkspaceAction.Instruct);
        oldInput("Words for the old task");
        column.Act(FileScreens.SendInstruction, null);
        Assert.That(column.Screen.Instruction.ExecutionId, Is.Null);
        Assert.That(column.ActivityContextFrame, Is.Null);
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void ApprovalReviewDoesNotBorrowTheOrdinaryComposerContextOrBudget()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Running; }, WorkstreamStatus.Running);
        var column = Column(host, () =>
        {
            var current = work.Present();
            return new WorkspacePresentation(current.Character, current.Objective, current.Execution, current.Runtime,
                new[] { WorkspaceAction.Instruct, WorkspaceAction.Approve, WorkspaceAction.Deny }, new[] { WorkspaceAction.Approve }, current.Commands, current.Activity);
        });
        var composerMeasures = 0;
        column.ConfigureActivityComposer(() => new RowBudget(8), () => { composerMeasures++; return new RowBudget(8); });
        column.HoldStarted(FileScreens.HoldToTalk);
        column.Heard("Retain ordinary words");
        work.Change(execution => execution.PendingApprovals.Add(WaitingWork.Approval("new-approval", "Run a check", Samples.Time)));
        host.Wait(1);
        column.Tick();
        column.Steering.StopTyping();
        column.Steering.Press(WorkspaceAction.Approve, column.Now!);
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Approve));
        var measured = composerMeasures;
        host.Wait(1);
        column.Tick();
        Assert.That(column.ActivityContextFrame, Is.Null);
        Assert.That(composerMeasures, Is.EqualTo(measured));
        Assert.That(column.Screen.Instruction.Words, Is.EqualTo("Retain ordinary words"));
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void AStaleActivityPagerCannotTurnThePageBehindTheComposer()
    {
        var host = new FileMenuHost { Height = _ => MenuPage.Words(4) + MenuPage.SourceLine };
        var work = new WaitingWork();
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Running; }, WorkstreamStatus.Running);
        var current = FileScreensTests.Offering(work.Present(), WorkspaceAction.Instruct);
        var activity = Enumerable.Range(1, 20).Select(index => new ActivityEntry(index, "2026-09-26T09:00:01.000Z", ActivityKind.Message, "Activity " + index, true)).ToArray();
        current = new WorkspacePresentation(current.Character, current.Objective, current.Execution, current.Runtime, current.Actions,
            current.Actions.Where(current.RequiresConfirmation).ToList(), current.Commands, activity);
        var column = Column(host, () => current);
        column.ConfigureActivityComposer(() => new RowBudget(3), () => new RowBudget(8));
        Assert.That(column.Screen.Pages, Is.GreaterThan(1));
        column.Act(Footer.NextPage, null);
        var page = column.Screen.Page;
        Assert.That(page, Is.EqualTo(1));
        column.HoldStarted(FileScreens.HoldToTalk);
        column.Heard("Keep the page");
        column.Drawn(column.ActivityContextFrame!, null);
        column.Act(Footer.NextPage, null);
        Assert.That(column.Screen.Page, Is.EqualTo(page));
        Assert.That(column.Screen.Instruction.Words, Is.EqualTo("Keep the page"));
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void PassiveActivityKeepsTheActualTaskBadgeAndRefreshesLastKnownState()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Running; }, WorkstreamStatus.Running);
        var column = Column(host, () => FileScreensTests.Offering(work.Present(live: host.Live), WorkspaceAction.Instruct));
        column.ConfigureActivityComposer(() => new RowBudget(8), () => new RowBudget(8));
        column.HoldStarted(FileScreens.HoldToTalk);
        column.Heard("Keep working carefully");
        static object Badge(StateBadge badge) => (badge.State, badge.Word, badge.Tone, badge.Icon, badge.Fill, badge.Edge,
            badge.Turns, badge.Breathes, badge.Count, badge.LastKnown);
        var live = column.ActivityContextFrame!;
        Assert.That(Badge(live.Pill!), Is.EqualTo(Badge(StateLanguage.BadgeOf(column.Now!.Character))));
        Assert.That(live.Pill!.LastKnown, Is.False);
        host.Live = false;
        host.Wait(1);
        column.Tick();
        var disconnected = column.ActivityContextFrame!;
        Assert.That(disconnected, Is.Not.SameAs(live), "badge-only changes replace the passive frame");
        Assert.That(Badge(disconnected.Pill!), Is.EqualTo(Badge(StateLanguage.BadgeOf(column.Now!.Character))));
        Assert.That((disconnected.Pill!.LastKnown, disconnected.Pill.Turns, disconnected.Pill.Breathes), Is.EqualTo((true, false, false)));
        host.Live = true;
        work.Change(execution => execution.Status = ExecutionStatus.Completed, WorkstreamStatus.Completed);
        host.Wait(1);
        column.Tick();
        Assert.That(column.ActivityContextFrame, Is.Not.SameAs(disconnected));
        Assert.That(Badge(column.ActivityContextFrame!.Pill!), Is.EqualTo(Badge(StateLanguage.BadgeOf(column.Now!.Character))));
        Assert.That(column.ActivityContextFrame.Pill!.State, Is.Not.EqualTo(live.Pill.State));
        Assert.That(column.Screen.Instruction.Words, Is.EqualTo("Keep working carefully"));
        Assert.That(host.Sent, Is.Empty);
    }

    private FileColumn Column(FileMenuHost host, Func<WorkspacePresentation?> present, IReadOnlyList<PresetInstruction>? recorded = null) =>
        new(host, present, factory, () => null, _ => recorded, () => "");

    /// <summary>The director draws the column's frame, and a second later it is read.</summary>
    private static void Draw(FileMenuHost host, FileColumn column)
    {
        column.Drawn(column.Frame!, sidePanel: null);
        host.Wait(1);
    }

    /// <summary>Chooses only from a currently drawn option page, navigating by the visible paging action.</summary>
    private static void ChooseVisible(FileMenuHost host, FileColumn column, int option)
    {
        ReadQuestion(host, column);
        var pages = column.Screen.Question.Pages;
        for (var page = 0; page < pages; page++)
        {
            Draw(host, column);
            var shown = column.Frame!.Lines.FirstOrDefault(line => line.Action == FileScreens.Choose && line.Key == option.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (shown != null)
            {
                Assert.That(shown.Available, Is.True);
                column.Act(shown.Action!, shown.Key);
                return;
            }
            var more = column.Frame.Lines.Single(line => line.Action == FileScreens.MoreAnswers);
            Assert.That(more.Available, Is.True);
            column.Act(more.Action!, more.Key);
        }
        Assert.Fail("The offered answer was not reachable through its visible pages.");
    }

    /// <summary>Follows visible primary controls to the final review without choosing, editing or sending.</summary>
    private static void Review(FileMenuHost host, FileColumn column)
    {
        for (var page = 0; page < 50; page++)
        {
            Draw(host, column);
            var primary = column.Frame!.Footer[PromptSlot.FarRight]!;
            if (primary.Id == FileScreens.SendAnswer)
            {
                Assert.That(column.Screen.Question.Reviewing, Is.True);
                return;
            }
            Assert.That(primary.Id, Is.EqualTo(column.Screen.Question.Reviewing ? FileScreens.MoreAnswers : FileScreens.NextQuestion), "only the visible question/review navigation advances this driver");
            Assert.That(primary.Available, Is.True, primary.Reason);
            column.Act(primary.Id, null);
        }
        Assert.Fail("The final answer review was not reachable through visible primary controls.");
    }

    /// <summary>Reads every part of the distinct editor, then explicitly commits Use this answer.</summary>
    private static void UseInput(FileMenuHost host, FileColumn column)
    {
        for (var part = 0; part < 100; part++)
        {
            Draw(host, column);
            var primary = column.Frame!.Footer[PromptSlot.FarRight]!;
            Assert.That(column.Screen.Question.Writing, Is.True);
            Assert.That(primary.Available, Is.True, primary.Reason);
            column.Act(primary.Id, null);
            if (primary.Id == FileScreens.UseInput) return;
            Assert.That(primary.Id, Is.EqualTo(FileScreens.NextInputPart));
        }
        Assert.Fail("The editor's complete answer was not reachable through visible controls.");
    }

    private static WorkspacePresentation Approving(WaitingWork work) =>
        FileScreensTests.Offering(work.Present(), WorkspaceAction.Approve, WorkspaceAction.Deny, WorkspaceAction.Interrupt);

    [Test]
    public void ItOpensOnWhatWaitsAndRaisesChangedForEachNewFrame()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        var column = Column(host, () => Approving(work));
        Assert.That(column.Screen.Section, Is.EqualTo(FileSection.Waiting));
        Assert.That(column.Frame!.Lines[0].Words, Is.EqualTo("It wants to run a command:"));
        var changes = 0;
        column.Changed += () => changes++;
        column.Act(MenuFrame.ChooseSection, "activity");
        Assert.That((changes, column.Screen.Section), Is.EqualTo((1, FileSection.Activity)));
        var closed = false;
        column.Closed += () => closed = true;
        column.Act(Footer.Close, null);
        Assert.That(closed, Is.True);
        Assert.That(host.Sent, Is.Empty, "nothing sent by looking");
    }

    [Test]
    public void ApprovingSendsOnlyThroughTheHostOnceTheWholeRequestWasDrawnAndYesOnlyOnce()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        var column = Column(host, () => Approving(work));
        column.Act(FileScreens.Approve, null);
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Approve));
        Assert.That(column.Frame!.Footer.Confirming, Is.True);

        // The request's one part drawn: Yes shows, and sends once.
        Draw(host, column);
        Assert.That(column.Frame!.Footer[PromptSlot.Free]?.Id, Is.EqualTo(FileScreens.Yes));
        column.Act(FileScreens.Yes, null);
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent.Count, Is.EqualTo(1));
        Assert.That(host.Sent[0], Is.InstanceOf<ExecutionRespondToApprovalCommand>());
    }

    [Test]
    public void DrawingAnArmedRequestBuildsAgainOnlyWhenYesMayShowSoAPressStandsOnTheFrameDrawn()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        var column = Column(host, () => Approving(work));
        column.Act(FileScreens.Approve, null);
        var changes = 0;
        column.Changed += () => changes++;
        Draw(host, column);
        Assert.That((changes, column.Frame!.Footer[PromptSlot.Free]?.Id), Is.EqualTo((1, FileScreens.Yes)), "Yes may show: built once");
        var shown = column.Frame;
        for (var draw = 0; draw < 3; draw++) Draw(host, column);
        Assert.That((changes, column.Frame), Is.EqualTo((1, shown)), "drawn again, it stands as it is");
        column.Act(FileScreens.Yes, null);
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent.Count, Is.EqualTo(1), "Yes is taken, once");
    }

    [Test]
    public void ARequestInPartsDrawnOverAndOverTurnsByNextPartAndCancelsOrConfirmsAsPressed()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        var command = string.Join(" && ", Enumerable.Range(1, 30).Select(step => "psql -c 'ALTER TABLE t" + step + " DROP COLUMN legacy'"));
        work.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.PendingApprovals.Add(WaitingWork.Approval("approval-long", command, Samples.Time));
        });
        var column = Column(host, () => Approving(work));
        column.Act(FileScreens.Approve, null);
        Assert.That(column.Screen.RequestParts, Is.GreaterThan(2), "a request of several parts");
        var changes = 0;
        column.Changed += () => changes++;
        for (var part = 0; part < column.Screen.RequestParts; part++)
        {
            var shown = column.Frame;
            for (var draw = 0; draw < 3; draw++) Draw(host, column);
            var last = part == column.Screen.RequestParts - 1;
            Assert.That(column.Frame, last ? Is.Not.SameAs(shown) : Is.SameAs(shown), "part " + part + ": built again only once Yes may show");
            if (last) break;
            Assert.That(column.Frame!.Footer[PromptSlot.Free]?.Id, Is.Not.EqualTo(FileScreens.Yes), "no Yes before the last part");
            var before = changes;
            column.Act(FileScreens.NextPart, FileScreens.RequestKey);
            Assert.That((changes - before, column.Screen.RequestPart), Is.EqualTo((1, part + 1)), "Next part is taken");
        }
        Assert.That(column.Frame!.Footer[PromptSlot.Free]?.Id, Is.EqualTo(FileScreens.Yes));
        var settled = changes;
        for (var draw = 0; draw < 3; draw++) Draw(host, column);
        Assert.That(changes, Is.EqualTo(settled), "a confirmation drawn again and again raises nothing more");
        column.Act(FileScreens.Cancel, null);
        Assert.That((column.Steering.Armed, host.Sent.Count), Is.EqualTo(((WorkspaceAction?)null, 0)), "Cancel is taken and sends nothing");

        column.Act(FileScreens.Approve, null);
        for (var part = 0; part < column.Screen.RequestParts; part++)
        {
            Draw(host, column);
            Draw(host, column);
            if (part < column.Screen.RequestParts - 1) column.Act(FileScreens.NextPart, FileScreens.RequestKey);
        }
        column.Act(FileScreens.Yes, null);
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent.Count, Is.EqualTo(1), "Yes is taken and sends once");
    }

    [Test]
    public void YesIsJudgedAgainstTheWorkAsItIsWhenPressedNotAsTheLastRebuildReadIt()
    {
        var host = new FileMenuHost();
        var current = Approving(new WaitingWork());
        var column = Column(host, () => current);
        column.Act(FileScreens.Approve, null);
        Draw(host, column);
        Assert.That(column.Frame!.Footer[PromptSlot.Free]?.Id, Is.EqualTo(FileScreens.Yes));
        // The state moves on to another request between rebuilds; the column has not ticked since,
        // so what it last read is a snapshot of the request before.
        var later = new WaitingWork();
        later.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.PendingApprovals.Add(WaitingWork.Approval("approval-2", "Drop the table", Samples.Time));
        });
        current = Approving(later);
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent, Is.Empty, "a Yes to the request read before is not a Yes to this one");
        Assert.That(column.Steering.Armed, Is.Null);
    }

    [Test]
    public void ARequestsPartsStayPutWhenItsLastIsReadAndTheQuestionBelowChangesSoNoReadingIsLost()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        var command = string.Join(" && ", Enumerable.Range(1, 30).Select(step => "psql -c 'ALTER TABLE t" + step + " DROP COLUMN legacy'"));
        work.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.PendingApprovals.Add(WaitingWork.Approval("approval-long", command, Samples.Time));
        });
        var column = Column(host, () => Approving(work));
        column.Act(FileScreens.Approve, null);
        // The question below the request is two rows before it is read and one after, as at larger text.
        Assert.That(host.RowsOf(WorkspaceText.ReadRequestFirst, 0f), Is.GreaterThan(host.RowsOf(WorkspaceText.ConfirmationPrompt(WorkspaceAction.Approve, null), 0f)));
        var (parts, rows) = (column.Screen.RequestParts, column.Screen.RequestPartRows);
        Assert.That(parts, Is.GreaterThan(1));
        for (var part = 0; part < parts; part++)
        {
            Draw(host, column);
            if (part < parts - 1) column.Act(FileScreens.NextPart, FileScreens.RequestKey);
        }
        Assert.That(column.Steering.Prompt(column.Now!), Is.EqualTo("Approve the request above?"), "the question below has changed");
        Assert.That((column.Screen.RequestParts, column.Screen.RequestPartRows, column.Screen.RequestPart), Is.EqualTo((parts, rows, parts - 1)),
            "the parts stay as read, on the last one, not back at the first");
        Assert.That((column.Steering.CanConfirm, column.Frame!.Footer[PromptSlot.Free]?.Id), Is.EqualTo((true, (string?)FileScreens.Yes)));
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent.Count, Is.EqualTo(1));
    }

    /// <summary>A long request to approve, armed: its parts to read before Yes.</summary>
    private FileColumn ApprovingALongRequest(FileMenuHost host)
    {
        var work = new WaitingWork();
        var command = string.Join(" && ", Enumerable.Range(1, 30).Select(step => "psql -c 'ALTER TABLE t" + step + " DROP COLUMN legacy'"));
        work.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.PendingApprovals.Add(WaitingWork.Approval("approval-long", command, Samples.Time));
        });
        var column = Column(host, () => Approving(work));
        column.Act(FileScreens.Approve, null);
        return column;
    }

    [Test]
    public void ItsPageIsReadAgainWhenTheTextSizeChangesAndAPartlyReadRequestIsReadAgainFromItsStart()
    {
        var host = new FileMenuHost();
        var column = ApprovingALongRequest(host);
        var rows = column.Screen.RequestPartRows;
        Draw(host, column);
        column.Act(FileScreens.NextPart, FileScreens.RequestKey);
        Assert.That(column.Screen.RequestPart, Is.EqualTo(1));

        // Text a step larger, in Settings: at the next tick the page packs again, smaller in its rows.
        // Laid just now, so only the size change lays the page again at the next tick, the clock unmoved.
        column.Tick();
        host.TextSize = TextSize.Larger;
        column.Tick();
        Assert.That(column.Screen.RequestPartRows, Is.LessThan(rows), "the page read again at the larger text, its parts fewer rows");
        Assert.That((column.Screen.RequestPart, column.Steering.CanConfirm), Is.EqualTo((0, false)), "a request not yet read whole is read again from its first part");
    }

    [Test]
    public void ARequestReadWholeIsReadAgainWhenTheTextSizeChangesAndYesWaitsUntilItIs()
    {
        var host = new FileMenuHost();
        var column = ApprovingALongRequest(host);
        void ReadEveryPart()
        {
            var parts = column.Screen.RequestParts;
            for (var part = 0; part < parts; part++)
            {
                Draw(host, column);
                if (part < parts - 1) column.Act(FileScreens.NextPart, FileScreens.RequestKey);
            }
        }
        ReadEveryPart();
        Assert.That((column.Steering.CanConfirm, column.Frame!.Footer[PromptSlot.Free]?.Id), Is.EqualTo((true, (string?)FileScreens.Yes)));

        // Its parts were drawn at the other size: at the new one it is read again from its first, no Yes meanwhile.
        // Laid just now, so only the size change lays the page again at the next tick, the clock unmoved.
        column.Tick();
        host.TextSize = TextSize.Larger;
        column.Tick();
        Assert.That((column.Steering.CanConfirm, column.Screen.RequestPart, column.Frame!.Footer[PromptSlot.Free]), Is.EqualTo((false, 0, (Prompt?)null)));
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent, Is.Empty, "a Yes pressed before the request is read at the new size sends nothing");

        ReadEveryPart();
        Assert.That(column.Steering.CanConfirm, Is.True, "read whole at the new size");
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent, Has.Count.EqualTo(1));
    }

    [Test]
    public void AQuestionReadWholeIsReadAgainWhenTheTextSizeChangesAndSendAnswerWaitsUntilItIs()
    {
        var (host, column) = Asking(new QuestionPrompt
        {
            Key = "q0", Header = "Lockout", Text = string.Join(" ", Enumerable.Range(1, 14).Select(step => "How long should a lockout last after failed sign-in number " + step + "?")), Multiple = false, FreeText = false,
            Options = new List<QuestionOption> { new() { Label = "15 minutes" }, new() { Label = "1 hour" } },
        });
        var draft = column.Screen.Question.Draft!;
        Assert.That(draft.WasShownWhole(0), Is.True, "read to its last part");
        column.Act(FileScreens.Choose, "0");
        Assert.That(draft.Problem, Is.Null, "chosen and read, it can be sent");

        // Text a step larger: the question was read at the other size, so it is read again from its first part, and Send answer waits.
        // Laid just now, so only the size change lays the page again at the next tick, the clock unmoved.
        column.Tick();
        host.TextSize = TextSize.Larger;
        column.Tick();
        Assert.That((draft.WasShownWhole(0), column.Screen.Question.QuestionPart), Is.EqualTo((false, (int?)0)));
        Assert.That(draft.Problem, Is.Not.Null);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(host.Sent, Is.Empty, "Send answer pressed before the question is read at the new size sends nothing");

        for (var step = 0; step < 8 && column.Screen.Question.QuestionPart != null; step++)
        {
            Draw(host, column);
            column.Act(FileScreens.NextPart, FileScreens.QuestionKey);
        }
        Draw(host, column);
        if (!draft.IsAnswered(0)) column.Act(FileScreens.Choose, "0");
        Assert.That(draft.Problem, Is.Null, "read whole at the new size");
        Review(host, column);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(host.Sent.OfType<ExecutionAnswerQuestionCommand>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void ADrawOfAFrameTheColumnNoLongerStandsByCountsForNothing()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        var column = Column(host, () => Approving(work));
        column.Act(FileScreens.Approve, null);
        var stale = column.Frame!;
        column.Act(MenuFrame.ChooseSection, "activity");
        column.Drawn(stale, sidePanel: null);
        Assert.That(column.Steering.WholeRequestShown, Is.False, "a frame drawn after the column built another is not what shows");
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void FocusLeavingLapsesAnArmedConfirmationAndSaysSo()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        var column = Column(host, () => Approving(work));
        column.Act(FileScreens.Approve, null);
        Draw(host, column);
        column.FocusLeft();
        Assert.That(column.Steering.Armed, Is.Null);
        Assert.That(column.Frame!.Lines.Last().Words, Is.EqualTo(WorkspaceText.ConfirmAfresh));
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void FocusLeavingResetsWhatWasReadWithinAnArmingButNotAQuestionsReads()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        var column = Column(host, () => Approving(work));
        column.Act(FileScreens.Approve, null);
        Draw(host, column);
        Assert.That(column.Steering.CanConfirm, Is.True);
        column.FocusLeft();
        column.Act(FileScreens.Approve, null);
        Assert.That(column.Steering.CanConfirm, Is.False, "the request is read again from its first part");
        Assert.That(column.Frame!.Footer[PromptSlot.Free]?.Id, Is.Not.EqualTo(FileScreens.Yes));

        var asking = new AskingWork();
        var answering = Column(host, () => FileScreensTests.Offering(asking.Present(), WorkspaceAction.Answer));
        for (var step = 0; step < 5 && answering.Screen.Question.QuestionPart != null; step++)
        {
            Draw(host, answering);
            answering.Act(FileScreens.NextPart, FileScreens.QuestionKey);
        }
        Draw(host, answering);
        Assert.That(answering.Screen.Question.Draft!.WasShownWhole(0), Is.True);
        answering.FocusLeft();
        Assert.That((answering.Screen.Question.Draft!.WasShownWhole(0), answering.Screen.Question.QuestionPart), Is.EqualTo((true, (int?)null)),
            "a question read outside an arming stays read, and stays on its answers");
    }

    [Test]
    public void WithoutASessionNothingIsSentAndThePageSaysWhy()
    {
        var host = new FileMenuHost { Live = false };
        var work = new WaitingWork();
        var column = Column(host, () => Approving(work));
        column.Act(FileScreens.Approve, null);
        Draw(host, column);
        column.Act(FileScreens.Yes, null);
        Assert.That(column.Frame!.Lines.Last().Words, Does.StartWith("Couldn't send"));
    }

    /// <summary>A question taking a typed answer, its answers' page in view, with an unsaved editor buffer long enough to take several parts.</summary>
    private (FileMenuHost Host, FileColumn Column) TypedLongAnswer()
    {
        var host = new FileMenuHost();
        var work = new AskingWork(new QuestionView
        {
            QuestionId = "question-1",
            Answerable = true,
            AskedAt = Samples.Time,
            Prompts = new List<QuestionPrompt>
            {
                new()
                {
                    Key = "q0", Header = "Lockout", Text = "How long should a lockout last?", Multiple = false, FreeText = true,
                    Options = new List<QuestionOption> { new() { Label = "15 minutes" }, new() { Label = "1 hour" } },
                },
            },
        });
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer));
        for (var step = 0; step < 5 && column.Screen.Question.QuestionPart != null; step++)
        {
            Draw(host, column);
            column.Act(FileScreens.NextPart, FileScreens.QuestionKey);
        }
        Draw(host, column);
        column.Act(FileScreens.TypeAnswer, null);
        var words = string.Join(" ", Enumerable.Range(1, 120).Select(step => "Lock it for a minute after the " + step + "th failed try"));
        host.Keyboard!.Value.Done(words);
        Assert.That((column.Screen.Question.Writing, column.Screen.Question.InputReading), Is.EqualTo((true, true)), "long input opens a dedicated read-through editor");
        Assert.That(column.Screen.Question.InputParts, Is.GreaterThan(2), "in several parts");
        Assert.That(column.Screen.Question.Draft!.Typed(0), Is.Null, "keyboard Done updates only the editor buffer");
        Assert.That(column.Frame!.Side, Is.Null, "input is not an offered choice's detail panel");
        return (host, column);
    }

    [Test]
    public void ALongTypedAnswerIsReadPartByPartUsedThenReviewedBeforeItSends()
    {
        var (host, column) = TypedLongAnswer();
        var words = column.Screen.Question.Input;
        var parts = column.Screen.Question.InputParts;
        for (var part = 0; part < parts; part++)
        {
            Assert.That(column.Screen.Question.InputPart, Is.EqualTo(part), "a rebuild never sends it back to the first part");
            column.Act(FileScreens.UseInput, null);
            Assert.That(column.Screen.Question.Draft!.Typed(0), Is.Null, "unread editor content cannot be saved");
            Draw(host, column);
            column.Tick();
            if (part < parts - 1) column.Act(FileScreens.NextInputPart, null);
        }
        Assert.That(column.Screen.Question.InputRead, Is.True, "every editor part was drawn");
        column.Act(FileScreens.UseInput, null);
        Assert.That(column.Screen.Question.Draft!.Typed(0), Is.EqualTo(words));
        Assert.That(column.Screen.Question.AnswersRead(0), Is.True);
        Assert.That(host.Sent, Is.Empty, "Use saves a draft and sends nothing");
        Review(host, column);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(((ExecutionAnswerQuestionCommand)host.Sent.Single()).Payload.Answers.Single().Text, Is.EqualTo(words));
    }

    [Test]
    public void CancellingALongEditorKeepsTheSavedDraftUnchangedAndClosedUntilExplicitEdit()
    {
        var (host, column) = TypedLongAnswer();
        column.Act(FileScreens.CancelInput, null);
        Assert.That((column.Screen.Question.Writing, column.Screen.Question.Draft!.Typed(0)), Is.EqualTo((false, (string?)null)), "Cancel saves no buffer");
        Draw(host, column);
        column.Tick();
        host.Wait(1);
        column.Tick();
        Assert.That((column.Screen.Question.Writing, column.Frame!.Side), Is.EqualTo((false, (SidePanel?)null)), "rebuilds leave input closed");
        column.Act(FileScreens.TypeAnswer, null);
        host.Keyboard!.Value.Done("Ten minutes, then a day after the fifth time, then a reset by email only, with every lockout written to the audit log and the person told why");
        Assert.That((column.Screen.Question.Writing, column.Screen.Question.InputPart), Is.EqualTo((true, 0)), "new entry starts at its own first part");
        Assert.That(column.Screen.Question.Draft!.Typed(0), Is.Null);
    }

    private static string LongWords(string what) =>
        string.Join(" ", Enumerable.Range(1, 120).Select(step => what + " " + step));

    /// <summary>A file asking <paramref name="prompts"/>, its first answers' page in view.</summary>
    /// <summary>A file on <paramref name="host"/> asking <paramref name="prompts"/>, under <paramref name="policies"/>, its question read to its answers.</summary>
    private FileColumn AskingOn(FileMenuHost host, IEnumerable<CommandPolicy>? policies, params QuestionPrompt[] prompts)
    {
        var work = new AskingWork(new QuestionView { QuestionId = "question-1", Answerable = true, AskedAt = Samples.Time, Prompts = prompts.ToList() }, policies);
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer));
        for (var step = 0; step < 8 && column.Screen.Question.QuestionPart != null; step++)
        {
            Draw(host, column);
            column.Act(FileScreens.NextPart, FileScreens.QuestionKey);
        }
        Draw(host, column);
        return column;
    }

    /// <summary>The side panel showing read through its every part, each drawn and stood a second.</summary>
    private static void ReadSide(FileMenuHost host, FileColumn column)
    {
        var parts = column.Screen.Question.SideParts;
        for (var part = 0; part < parts; part++)
        {
            column.Drawn(column.Frame!, SidePanel.Footer);
            host.Wait(1);
            column.Tick();
            if (part < parts - 1) column.Act(Footer.NextPage, null);
        }
    }

    private const string CutAnswer = "Lock the account for fifteen minutes, then email its owner a link that unlocks it at once and resets the password";

    [Test]
    public void AChosenCutAnswerReadInItsPanelIsReadAgainAtANewSizeAndOnePressOfItsRowReopensIt()
    {
        var host = new FileMenuHost();
        var column = AskingOn(host, null, new QuestionPrompt
        {
            Key = "q0", Header = "Lockout", Text = "How long should a lockout last?", Multiple = false, FreeText = false,
            Options = new List<QuestionOption> { new() { Label = CutAnswer }, new() { Label = "1 hour" } },
        });
        var draft = column.Screen.Question.Draft!;
        column.Act(FileScreens.Choose, "0");
        Assert.That(column.Screen.Question.SideOption, Is.EqualTo(0), "cut, its words open beside the page");
        ReadSide(host, column);
        column.Act(SidePanel.Close, null);
        Draw(host, column);
        Assert.That(FileScreens.WhyNextWaits(column.Screen.Question), Is.Null, "read in its panel, it can be sent");

        column.Tick();
        host.TextSize = TextSize.Larger;
        column.Tick();
        Assert.That(column.Screen.Question.AnswersRead(0), Is.False, "read at the other size, it is read again");
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(host.Sent, Is.Empty, "Send answer waits until it is");

        // One press of its row opens its panel again, the answer still chosen.
        ReadQuestion(host, column);
        column.Act(FileScreens.Choose, "0");
        Assert.That((column.Screen.Question.SideOption, draft.IsChosen(0, CutAnswer)), Is.EqualTo(((int?)0, true)));
        ReadSide(host, column);
        column.Act(SidePanel.Close, null);
        Draw(host, column);
        Review(host, column);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(host.Sent.OfType<ExecutionAnswerQuestionCommand>().Count(), Is.EqualTo(1), "read again, it is sent once");
    }

    [Test]
    public void ALongEditorReadAtOneSizeMustBeReadAgainBeforeUseAtTheNewSize()
    {
        var (host, column) = TypedLongAnswer();
        var words = column.Screen.Question.Input;
        var parts = column.Screen.Question.InputParts;
        for (var part = 0; part < parts; part++)
        {
            Draw(host, column);
            if (part < parts - 1) column.Act(FileScreens.NextInputPart, null);
        }
        Assert.That(column.Screen.Question.InputRead, Is.True);
        column.Tick();
        host.TextSize = TextSize.Larger;
        column.Tick();
        Assert.That((column.Screen.Question.InputRead, column.Screen.Question.InputPart), Is.EqualTo((false, 0)), "parts read at the other size cannot certify this layout");
        column.Act(FileScreens.UseInput, null);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That((column.Screen.Question.Draft!.Typed(0), host.Sent.Count), Is.EqualTo(((string?)null, 0)));
        UseInput(host, column);
        ReadQuestion(host, column);
        Review(host, column);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(((ExecutionAnswerQuestionCommand)host.Sent.Single()).Payload.Answers.Single().Text, Is.EqualTo(words));
    }

    [Test]
    public void ASavedLongAnswerSurvivesResizeButRequiresEditorRereadUseAndReviewToSend()
    {
        var (host, column) = TypedLongAnswer();
        var words = column.Screen.Question.Input;
        UseInput(host, column);
        var draft = column.Screen.Question.Draft!;
        Assert.That((draft.Typed(0), column.Screen.Question.AnswersRead(0)), Is.EqualTo((words, true)));
        column.Tick();
        host.TextSize = TextSize.Larger;
        column.Tick();
        Assert.That((draft.Typed(0), column.Screen.Question.AnswersRead(0)), Is.EqualTo((words, false)), "resizing preserves the saved words but invalidates their reading proof");
        ReadQuestion(host, column);
        Assert.That(column.Frame!.Footer[PromptSlot.FarRight]!.Available, Is.False, "even after rereading the question, Next waits for the saved answer to be reread");
        column.Act(FileScreens.NextQuestion, null);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That((column.Screen.Question.Reviewing, host.Sent.Count), Is.EqualTo((false, 0)));
        column.Act(FileScreens.TypeAnswer, null);
        Assert.That((column.Screen.Question.Input, column.Screen.Question.InputPart, column.Screen.Question.InputRead), Is.EqualTo((words, 0, false)), "explicit Edit starts a fresh reading of the saved words");
        column.Act(FileScreens.UseInput, null);
        Assert.That(column.Screen.Question.Writing, Is.True, "unread saved words cannot borrow the old Use proof");
        UseInput(host, column);
        Assert.That((draft.Typed(0), column.Screen.Question.AnswersRead(0), host.Sent.Count), Is.EqualTo((words, true, 0)));
        Review(host, column);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(((ExecutionAnswerQuestionCommand)host.Sent.Single()).Payload.Answers.Single().Text, Is.EqualTo(words));
    }

    [Test]
    public void WhereThePageComesOutTheSameAtTheNewSizeARequestReadWholeIsStillReadAgain()
    {
        var host = new FileMenuHost { Height = rows => MenuPage.Height(TextSize.Standard, rows) };
        var column = ApprovingALongRequest(host);
        var (parts, rows) = (column.Screen.RequestParts, column.Screen.RequestPartRows);
        for (var part = 0; part < parts; part++)
        {
            Draw(host, column);
            if (part < parts - 1) column.Act(FileScreens.NextPart, FileScreens.RequestKey);
        }
        Assert.That(column.Steering.CanConfirm, Is.True);
        column.Tick();
        host.TextSize = TextSize.Larger;
        column.Tick();
        Assert.That(column.Screen.RequestPartRows, Is.EqualTo(rows), "measured the same at the new size");
        Assert.That((column.Steering.CanConfirm, column.Screen.RequestPart), Is.EqualTo((false, 0)), "read again from its first part all the same");
    }

    [Test]
    public void WhereThePageComesOutTheSameAtTheNewSizeAQuestionReadWholeShowsItsFirstPartToReadAgain()
    {
        var host = new FileMenuHost { Height = rows => MenuPage.Height(TextSize.Standard, rows) };
        var column = AskingOn(host, null, new QuestionPrompt
        {
            Key = "q0", Header = "Lockout", Text = string.Join(" ", Enumerable.Range(1, 14).Select(step => "How long should a lockout last after failed sign-in number " + step + "?")),
            Multiple = false, FreeText = false,
            Options = new List<QuestionOption> { new() { Label = "15 minutes" }, new() { Label = "1 hour" } },
        });
        var draft = column.Screen.Question.Draft!;
        Assert.That(draft.WasShownWhole(0), Is.True);
        column.Act(FileScreens.Choose, "0");
        column.Tick();
        host.TextSize = TextSize.Larger;
        column.Tick();
        Assert.That((draft.WasShownWhole(0), column.Screen.Question.QuestionPart), Is.EqualTo((false, (int?)0)),
            "unread, and shown from its first part, so a row leads on through it");
        for (var step = 0; step < 8 && column.Screen.Question.QuestionPart != null; step++)
        {
            Draw(host, column);
            column.Act(FileScreens.NextPart, FileScreens.QuestionKey);
        }
        Draw(host, column);
        if (!draft.IsAnswered(0)) column.Act(FileScreens.Choose, "0");
        Review(host, column);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(host.Sent.OfType<ExecutionAnswerQuestionCommand>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void BetweenASizeChangeAndTheNextTickAPressAndADrawAreJudgedAtTheNewSize()
    {
        var host = new FileMenuHost();
        var column = ApprovingALongRequest(host);
        var parts = column.Screen.RequestParts;
        for (var part = 0; part < parts; part++)
        {
            Draw(host, column);
            if (part < parts - 1) column.Act(FileScreens.NextPart, FileScreens.RequestKey);
        }
        column.Tick();
        Assert.That(column.Steering.CanConfirm, Is.True);

        // No tick yet: Yes pressed at once is judged against the request as read at the new size.
        host.TextSize = TextSize.Larger;
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent, Is.Empty, "Yes after a size change, before any tick, sends nothing");

        // And a draw of the frame laid at the other size counts for nothing: the page is laid again instead.
        var request = ApprovingALongRequest(host = new FileMenuHost());
        for (var part = 0; part < request.Screen.RequestParts; part++)
        {
            Draw(host, request);
            if (part < request.Screen.RequestParts - 1) request.Act(FileScreens.NextPart, FileScreens.RequestKey);
        }
        request.Tick();
        var before = request.Frame!;
        host.TextSize = TextSize.Larger;
        request.Drawn(before, sidePanel: null);
        Assert.That((request.Frame == before, request.Steering.CanConfirm), Is.EqualTo((false, false)), "laid again at the new size, and read again");
    }

    [Test]
    public void AnAnswerArmedToSendIsCancelledWhenTheTextSizeChanges()
    {
        var host = new FileMenuHost();
        var column = AskingOn(host, new[] { new CommandPolicy { CommandType = CommandType.ExecutionAnswerQuestion, Policy = PolicyCategory.ReviewRequired } },
            new QuestionPrompt
            {
                Key = "q0", Header = "Lockout", Text = "How long should a lockout last?", Multiple = false, FreeText = false,
                Options = new List<QuestionOption> { new() { Label = "15 minutes" }, new() { Label = "1 hour" } },
            });
        column.Act(FileScreens.Choose, "0");
        Draw(host, column);
        Review(host, column);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That((column.Steering.Armed, column.Frame!.Footer[PromptSlot.Free]?.Id), Is.EqualTo(((WorkspaceAction?)WorkspaceAction.Answer, (string?)FileScreens.Yes)));
        column.Tick();
        host.TextSize = TextSize.Larger;
        column.Tick();
        Assert.That(column.Steering.Armed, Is.Null, "Yes was for answers read at the other size");
        Assert.That(column.Screen.Notice, Is.EqualTo(WorkspaceText.TextSizeChanged));
        Assert.That(column.Frame!.Footer[PromptSlot.Free], Is.Null);
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent, Is.Empty);
    }

    private (FileMenuHost Host, FileColumn Column) Asking(params QuestionPrompt[] prompts)
    {
        var host = new FileMenuHost();
        var work = new AskingWork(new QuestionView { QuestionId = "question-1", Answerable = true, AskedAt = Samples.Time, Prompts = prompts.ToList() });
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer));
        for (var step = 0; step < 5 && column.Screen.Question.QuestionPart != null; step++)
        {
            Draw(host, column);
            column.Act(FileScreens.NextPart, FileScreens.QuestionKey);
        }
        Draw(host, column);
        return (host, column);
    }

    [Test]
    public void CancelledUnreadInputCannotReuseItsPartsWhenEnteredAgainAndOnlyUseThenReviewSends()
    {
        var (host, column) = TypedLongAnswer();
        var words = column.Screen.Question.Input;
        Draw(host, column);
        column.Act(FileScreens.NextInputPart, null);
        Assert.That(column.Screen.Question.InputPart, Is.EqualTo(1));
        var previous = host.Keyboard!.Value.Done;
        column.Act(FileScreens.CancelInput, null);
        Assert.That(column.Screen.Question.Draft!.Typed(0), Is.Null);
        Draw(host, column);
        column.Act(FileScreens.TypeAnswer, null);
        host.Keyboard!.Value.Done(words);
        Assert.That((column.Screen.Question.InputPart, column.Screen.Question.InputRead), Is.EqualTo((0, false)), "a new input session starts its own reading");
        previous("Late words from the cancelled editor");
        Assert.That(column.Screen.Question.Input, Is.EqualTo(words), "late callbacks cannot change the new editor");
        column.Act(FileScreens.UseInput, null);
        Assert.That(column.Screen.Question.Draft!.Typed(0), Is.Null);
        UseInput(host, column);
        Review(host, column);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(((ExecutionAnswerQuestionCommand)host.Sent.Single()).Payload.Answers.Single().Text, Is.EqualTo(words));
    }

    [Test]
    public void WithSeveralAnswersAllowedCloseDetailsOnACutAnswerBringsThePageBackNotTheTypedOne()
    {
        var (host, column) = Asking(new QuestionPrompt
        {
            Key = "q0", Header = "Checks", Text = "Which checks should run?", Multiple = true, FreeText = true,
            Options = new List<QuestionOption>
            {
                new() { Label = "Unit tests, then the end to end suite against a fresh database, then a load test of the sign-in endpoint for ten minutes" },
                new() { Label = "Lint" },
            },
        });
        column.Act(FileScreens.TypeAnswer, null);
        host.Keyboard!.Value.Done(LongWords("Also the accessibility audit, step"));
        Assert.That(column.Screen.Question.Writing, Is.True, "the typed answer is in its distinct editor");
        UseInput(host, column);
        ChooseVisible(host, column, 0);
        Assert.That(column.Screen.Question.SideOption, Is.EqualTo(0), "the cut answer's panel in its place");
        column.Act(SidePanel.Close, null);
        Assert.That(column.Frame!.Side, Is.Null, "Close details always brings the page back");
    }

    [Test]
    public void BackOnAPromptWhoseLongTypedAnswerIsUnreadItsPanelOpensAtThePartToReadNext()
    {
        var (host, column) = Asking(
            new QuestionPrompt
            {
                Key = "q0", Header = "Lockout", Text = "How long should a lockout last?", Multiple = false, FreeText = true,
                Options = new List<QuestionOption> { new() { Label = "15 minutes" } },
            },
            new QuestionPrompt
            {
                Key = "q1", Header = "Notice", Text = "Should the person be told?", Multiple = false, FreeText = false,
                Options = new List<QuestionOption> { new() { Label = "Yes" }, new() { Label = "No" } },
            });
        column.Act(FileScreens.TypeAnswer, null);
        host.Keyboard!.Value.Done(LongWords("Lock it for a minute after try"));
        Assert.That(column.Screen.Question.InputParts, Is.GreaterThan(2));
        UseInput(host, column);
        var saved = column.Screen.Question.Draft!.Typed(0);
        Draw(host, column);
        column.Act(FileScreens.NextQuestion, null);
        Assert.That(column.Screen.Question.Prompt, Is.EqualTo(1));
        for (var step = 0; step < 5 && column.Screen.Question.QuestionPart != null; step++)
        {
            Draw(host, column);
            column.Act(FileScreens.NextPart, FileScreens.QuestionKey);
        }
        Draw(host, column);
        ChooseVisible(host, column, 0);
        Draw(host, column);
        column.Act(FileScreens.NextQuestion, null);
        Assert.That(column.Screen.Question.Reviewing, Is.True, "on the person's answers");
        column.Act(FileScreens.GoToQuestion, "0");
        for (var step = 0; step < 5 && column.Screen.Question.QuestionPart != null; step++)
        {
            Draw(host, column);
            column.Act(FileScreens.NextPart, FileScreens.QuestionKey);
        }
        Assert.That((column.Screen.Question.Prompt, column.Screen.Question.Draft!.Typed(0), column.Frame!.Side), Is.EqualTo((0, saved, (SidePanel?)null)), "Back/Edit keeps the saved answer without silently opening an input panel");
        Draw(host, column);
        column.Act(FileScreens.TypeAnswer, null);
        Assert.That((column.Screen.Question.Input, column.Screen.Question.InputPart, column.Screen.Question.InputRead), Is.EqualTo((saved, 0, false)), "explicit editing has its own current-session reading");
    }

    /// <summary>The prompt showing read through its question's parts to its answers, each part drawn.</summary>
    private static void ReadQuestion(FileMenuHost host, FileColumn column)
    {
        var parts = column.Screen.Question.QuestionParts(column.Screen.Question.Prompt);
        for (var part = 0; part < parts && column.Screen.Question.QuestionPart != null; part++)
        {
            Draw(host, column);
            Assert.That(column.Frame!.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(FileScreens.NextPart));
            column.Act(FileScreens.NextPart, FileScreens.QuestionKey);
        }
        Draw(host, column);
    }

    [Test]
    public void AQuestionOfTwoPromptsIsSentFromYourAnswersOnceThatPageIsDrawn()
    {
        var (host, column) = Asking(
            new QuestionPrompt
            {
                Key = "q0", Header = "Lockout", Text = "How long should a lockout last?", Multiple = false, FreeText = true,
                Options = new List<QuestionOption> { new() { Label = "15 minutes" }, new() { Label = "1 hour" } },
            },
            new QuestionPrompt
            {
                Key = "q1", Header = "Notice", Text = "Should the person be told?", Multiple = false, FreeText = false,
                Options = new List<QuestionOption> { new() { Label = "Yes" }, new() { Label = "No" } },
            });
        column.Act(FileScreens.Choose, "0");
        Draw(host, column);
        column.Act(FileScreens.NextQuestion, null);
        ReadQuestion(host, column);
        column.Act(FileScreens.Choose, "0");
        Draw(host, column);
        column.Act(FileScreens.NextQuestion, null);
        Assert.That(column.Screen.Question.Reviewing, Is.True, "on the person's answers");
        Assert.That(column.Frame!.Footer.All.Any(each => each.Prompt.Id == FileScreens.SpeakAnswer), Is.False, "no one question to answer by voice here");

        // Drawn, the page of answers counts as read, and nothing throws for want of a prompt to quote.
        Assert.DoesNotThrow(() => Draw(host, column));
        Assert.That(FileScreens.WhySendWaits(column.Screen), Is.Null, "every answer read on Your answers");
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(host.Sent.OfType<ExecutionAnswerQuestionCommand>().Count(), Is.EqualTo(1), "and sent");
    }

    [Test]
    public void AHoldToTalkOnYourAnswersTypesIntoNoQuestion()
    {
        var (host, column) = Asking(
            new QuestionPrompt
            {
                Key = "q0", Header = "Lockout", Text = "How long should a lockout last?", Multiple = false, FreeText = true,
                Options = new List<QuestionOption> { new() { Label = "15 minutes" } },
            },
            new QuestionPrompt
            {
                Key = "q1", Header = "Notice", Text = "Should the person be told?", Multiple = false, FreeText = true,
                Options = new List<QuestionOption> { new() { Label = "Yes" } },
            });
        column.Act(FileScreens.Choose, "0");
        Draw(host, column);
        column.Act(FileScreens.NextQuestion, null);
        ReadQuestion(host, column);
        column.Act(FileScreens.Choose, "0");
        Draw(host, column);
        column.Act(FileScreens.NextQuestion, null);
        Assert.That(column.Screen.Question.Reviewing, Is.True);
        // A hold that reached the column anyway, as from a frame drawn before: what is heard types into no
        // question, nor becomes an instruction to the agent.
        column.HoldStarted(FileScreens.SpeakAnswer);
        Assert.DoesNotThrow(() => column.Heard("Ten minutes"));
        Assert.That(column.Screen.Question.Draft!.Typed(0), Is.Null);
        Assert.That(column.Screen.Question.Draft!.Typed(1), Is.Null);
        Assert.That((column.Steering.Instruction, column.Steering.Armed), Is.EqualTo(((string?)null, (WorkspaceAction?)null)));
        Assert.That(column.Screen.Notice, Is.EqualTo(VoiceText.QuestionChangedWhileSpeaking));

        // Nor does the keyboard open there: its row is on a question's own page only.
        Assert.DoesNotThrow(() => column.Act(FileScreens.TypeAnswer, null));
        Assert.That(host.Keyboard, Is.Null);
    }

    /// <summary>Two questions an agent may ask one after the other, the second with as many prompts as given.</summary>
    private static QuestionView Questioned(string id, int prompts) => new()
    {
        QuestionId = id,
        Answerable = true,
        AskedAt = Samples.Time,
        Prompts = Enumerable.Range(0, prompts).Select(prompt => new QuestionPrompt
        {
            Key = "q" + prompt, Header = "Part " + (prompt + 1), Text = id + ", part " + (prompt + 1) + "?", Multiple = false, FreeText = true,
            Options = new List<QuestionOption> { new() { Label = "Yes" }, new() { Label = "No" } },
        }).ToList(),
    };

    /// <summary>A file asking <paramref name="question"/> under a policy that asks Yes before an answer is sent, its first answer chosen and read.</summary>
    private FileColumn AnsweringWithYes(FileMenuHost host, QuestionView question, bool reviewing = true)
    {
        var work = new AskingWork(question, new[] { new CommandPolicy { CommandType = CommandType.ExecutionAnswerQuestion, Policy = PolicyCategory.ReviewRequired } });
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer));
        ReadQuestion(host, column);
        column.Act(FileScreens.Choose, "0");
        Draw(host, column);
        if (reviewing) Review(host, column);
        return column;
    }

    [Test]
    public void LateWordsFromACancelledEditorCannotChangeTheReviewedAnswerOrItsYes()
    {
        var host = new FileMenuHost();
        var column = AnsweringWithYes(host, Questioned("question-1", 1), reviewing: false);
        column.Act(FileScreens.TypeAnswer, null);
        Draw(host, column);
        column.HoldStarted(FileScreens.SpeakAnswer);
        column.HoldEnded(FileScreens.SpeakAnswer, letGo: true);
        column.Act(FileScreens.CancelInput, null);
        Review(host, column);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Answer));
        column.Heard(LongWords("Lock it for a minute after try"));
        Assert.That((column.Steering.Armed, column.Screen.Question.Draft!.Typed(0)), Is.EqualTo(((WorkspaceAction?)WorkspaceAction.Answer, (string?)null)), "late unread words cannot change the exact answer being confirmed");
        Assert.That(column.Screen.Notice, Is.EqualTo(VoiceText.QuestionChangedWhileSpeaking));
        Assert.That(host.Sent, Is.Empty);
        column.Act(FileScreens.Yes, null);
        Assert.That(((ExecutionAnswerQuestionCommand)host.Sent.Single()).Payload.Answers.Single().Selected, Is.EqualTo(new[] { "Yes" }));
        Assert.That(column.Steering.Instruction, Is.Null, "an answer callback never becomes an instruction");
    }

    [Test]
    public void ALateChoicePressCannotChangeAReviewedAnswerOrItsPendingYes()
    {
        var host = new FileMenuHost();
        var column = AnsweringWithYes(host, Questioned("question-1", 1));
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Answer));
        column.Act(SidePanel.Close, null);
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Answer), "a press changing no answer preserves its Yes");
        column.Act(FileScreens.Choose, "1");
        Assert.That((column.Steering.Armed, column.Screen.Question.Draft!.Build().Single().Selected), Is.EqualTo(((WorkspaceAction?)WorkspaceAction.Answer, new[] { "Yes" })), "the review has no current choice page, so a late choice changes nothing");
        Assert.That(host.Sent, Is.Empty);
        column.Act(FileScreens.Yes, null);
        Assert.That(((ExecutionAnswerQuestionCommand)host.Sent.Single()).Payload.Answers.Single().Selected, Is.EqualTo(new[] { "Yes" }));
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent.Count, Is.EqualTo(1));
    }

    [Test]
    public void ALateChoicePagerCannotClearTheReviewedDraftOrSendAnything()
    {
        var host = new FileMenuHost();
        var question = Questioned("question-1", 1);
        question.Prompts[0].Options = Enumerable.Range(1, 12).Select(index => new QuestionOption { Label = "Answer " + index }).ToList();
        var column = AnsweringWithYes(host, question, reviewing: false);
        Assert.That(column.Screen.Question.Pages, Is.GreaterThan(1), "the choice stage has a real pager");
        column.Act(FileScreens.MoreAnswers, null);
        Assert.That(column.Screen.Question.Draft!.IsChosen(0, "Answer 1"), Is.True, "paging retains the drawn choice");
        Review(host, column);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Answer));
        column.Act(FileScreens.MoreAnswers, null);
        Assert.That(column.Screen.Question.Draft!.IsChosen(0, "Answer 1"), Is.True, "an old choice pager cannot clear the reviewed choice");
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Answer));
        Assert.That(host.Sent, Is.Empty, "paging itself sends nothing");
        column.Act(FileScreens.Yes, null);
        Assert.That(((ExecutionAnswerQuestionCommand)host.Sent.Single()).Payload.Answers.Single().Selected, Is.EqualTo(new[] { "Answer 1" }));
    }

    [Test]
    public void LateKeyboardWordsCannotChangeTheReviewedDraftOrItsPendingYes()
    {
        var host = new FileMenuHost();
        var column = AnsweringWithYes(host, Questioned("question-1", 1), reviewing: false);
        column.Act(FileScreens.TypeAnswer, null);
        Assert.That(host.Keyboard, Is.Not.Null);
        var late = host.Keyboard!.Value.Done;
        column.Act(FileScreens.CancelInput, null);
        Review(host, column);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Answer));
        late(LongWords("Lock it for a minute after try"));
        Assert.That((column.Steering.Armed, column.Screen.Question.Draft!.Typed(0)), Is.EqualTo(((WorkspaceAction?)WorkspaceAction.Answer, (string?)null)));
        Assert.That(host.Sent, Is.Empty);
        column.Act(FileScreens.Yes, null);
        Assert.That(((ExecutionAnswerQuestionCommand)host.Sent.Single()).Payload.Answers.Single().Selected, Is.EqualTo(new[] { "Yes" }));
    }

    [Test]
    public void YesTellsAnswersApartWhateverCharactersTheirLabelsHold()
    {
        // Three answers, one whose label holds a control character between the other two's labels: chosen
        // alone, it could pass for the other two chosen together were the answers compared as one string.
        var host = new FileMenuHost();
        var column = AnsweringWithYes(host, new QuestionView
        {
            QuestionId = "question-1",
            Answerable = true,
            AskedAt = Samples.Time,
            Prompts = new List<QuestionPrompt>
            {
                new()
                {
                    Key = "q0", Header = "Which", Text = "Which should it do?", Multiple = true, FreeText = false,
                    Options = new List<QuestionOption> { new() { Label = "A" }, new() { Label = "B" }, new() { Label = "A\u0003B" } },
                },
            },
        });
        var draft = column.Screen.Question.Draft!;
        column.Act(FileScreens.BackQuestion, null);
        ReadQuestion(host, column);
        draft.Choose(0, "A");
        draft.Choose(0, "A\u0003B");
        Assert.That(draft.AnswersNow[0].Chosen, Is.EqualTo(new[] { "A\u0003B" }));
        column.Tick();
        var pages = column.Screen.Question.Pages;
        for (var page = 0; page < pages; page++)
        {
            Draw(host, column);
            if (pages > 1) column.Act(FileScreens.MoreAnswers, null);
        }
        Review(host, column);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Answer));
        draft.Choose(0, "A\u0003B");
        draft.Choose(0, "A");
        draft.Choose(0, "B");
        Assert.That(draft.AnswersNow[0].Chosen, Is.EqualTo(new[] { "A", "B" }));
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent, Is.Empty, "A and B are not the answer Yes was asked for");
        Assert.That(column.Screen.Notice, Is.EqualTo(WorkspaceText.AnswerChanged));
    }

    [Test]
    public void ALateUnchangedKeyboardReturnLeavesTheReviewedAnswerAndItsYesUntouched()
    {
        var host = new FileMenuHost();
        var column = AnsweringWithYes(host, Questioned("question-1", 1), reviewing: false);
        column.Act(FileScreens.TypeAnswer, null);
        var done = host.Keyboard!.Value.Done;
        column.Act(FileScreens.CancelInput, null);
        Review(host, column);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Answer));
        done("");
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Answer));
        Assert.That(column.Screen.Notice, Is.Not.EqualTo(WorkspaceText.AnswerChanged));
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent.OfType<ExecutionAnswerQuestionCommand>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void FixedOptionsOfferNoVoiceAndForgedHeardWordsCannotChangeTheAnswerOrItsYes()
    {
        var host = new FileMenuHost();
        var question = Questioned("question-1", 1);
        question.Prompts[0].FreeText = false;
        var column = AnsweringWithYes(host, question, reviewing: false);
        Assert.That(column.Frame!.Footer.All.Select(each => each.Prompt.Id), Has.None.EqualTo(FileScreens.SpeakAnswer));
        Assert.That(column.Frame.Lines.Select(line => line.Action), Has.None.EqualTo(FileScreens.TypeAnswer));
        column.HoldStarted(FileScreens.SpeakAnswer);
        Review(host, column);
        column.Act(FileScreens.SendAnswer, null);
        column.Heard("Ten minutes");
        Assert.That((column.Steering.Armed, column.Screen.Question.Draft!.Typed(0)), Is.EqualTo(((WorkspaceAction?)WorkspaceAction.Answer, (string?)null)), "the unsupported input does not change the answer being confirmed");
        Assert.That(column.Screen.Notice, Is.EqualTo(VoiceText.QuestionChangedWhileSpeaking));
        Assert.That(host.Sent, Is.Empty);
        Assert.That(column.Steering.Instruction, Is.Null);
    }

    [Test]
    public void AChoiceClearedUnderYesSaysTheAnswerChanged()
    {
        var host = new FileMenuHost();
        var column = AnsweringWithYes(host, Questioned("question-1", 1));
        column.Act(FileScreens.SendAnswer, null);
        column.Screen.Question.Draft!.ClearChosen(0);
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent, Is.Empty);
        Assert.That(column.Screen.Notice, Is.EqualTo(WorkspaceText.AnswerChanged), "the answer changed, not the question");
    }

    [Test]
    public void YesSendsOnlyTheAnswersItWasAskedFor()
    {
        // However the answers changed after Send answer asked its Yes, Yes refuses them.
        var host = new FileMenuHost();
        var column = AnsweringWithYes(host, Questioned("question-1", 1));
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Answer));
        column.Screen.Question.Draft!.Type(0, "Something else entirely");
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent, Is.Empty);
        Assert.That(column.Screen.Notice, Is.EqualTo(WorkspaceText.AnswerChanged));
    }

    [Test]
    public void RepeatedFreshActivityFeedbackIsVisibleEvenWhenItsWordsMatch()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt));
        column.Screen.Section = FileSection.Activity;
        column.Screen.WorkspaceRequestOffered = true;
        column.Screen.ResultsOffered = true;
        column.Said(VoiceText.NothingHeard);
        Assert.That(column.Frame!.Lines.First().Words, Is.EqualTo(VoiceText.NothingHeard));
        Assert.That(column.Screen.Pages, Is.GreaterThan(1));
        column.Act(Footer.NextPage, null);
        Assert.That(column.Screen.Page, Is.GreaterThan(0));
        column.Said(VoiceText.NothingHeard);
        Assert.That(column.Screen.Page, Is.Zero);
        Assert.That(column.Frame!.Lines.First().Words, Is.EqualTo(VoiceText.NothingHeard));
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void WhereTheVoiceStandsNeverGrowsThePageAndOnlyOtherWordsAreNotices()
    {
        // Listening and writing down show on Hold to talk itself, drawn by the plane from the menu's one voice (Prompt.Voiced).
        var host = new FileMenuHost();
        var showing = new AskingWork(Questioned("question-1", 1));
        var column = Column(host, () => FileScreensTests.Offering(showing.Present(), WorkspaceAction.Answer));
        ReadQuestion(host, column);
        column.Act(FileScreens.TypeAnswer, null);
        Draw(host, column);
        Prompt? Held() => column.Frame!.Footer.All.Select(each => each.Prompt).FirstOrDefault(prompt => prompt.Id == FileScreens.SpeakAnswer);
        var lines = column.Frame!.Lines.Select(line => line.Words).ToList();
        Assert.That(Held()?.Words, Is.EqualTo(VoiceText.HoldToTalk));
        Assert.That(Held()!.AlsoReads, Is.EquivalentTo(new[] { VoiceText.ListeningWords, VoiceText.WritingDownWords }), "laid at the widest of every word it shows, so its width never changes under the hand");

        column.HoldStarted(FileScreens.SpeakAnswer);
        column.Said(VoiceText.Listening);
        Assert.That(column.Frame!.Lines.Select(line => line.Words), Is.EqualTo(lines), "no line for it: the page stays as it was");
        column.HoldEnded(FileScreens.SpeakAnswer, letGo: true);
        column.Said(VoiceText.Hearing);
        Assert.That(column.Frame!.Lines.Select(line => line.Words), Is.EqualTo(lines));
        Assert.That(column.Screen.Notice, Is.Null);
        Assert.That(Held()?.Words, Is.EqualTo(VoiceText.HoldToTalk), "the column's own footer keeps Hold to talk; the plane voices it");

        column.Said(VoiceText.NothingHeard);
        Assert.That(column.Frame!.Lines.Any(line => line.Words == VoiceText.NothingHeard), Is.True, "what the voice says otherwise is a notice");
    }

    [Test]
    public void WordsHeardAfterTheQuestionWasReplacedTypeIntoNone()
    {
        // Held on the first question; while the words are worked out, the agent asks another instead.
        var host = new FileMenuHost();
        var showing = new AskingWork(Questioned("question-1", 1));
        var column = Column(host, () => FileScreensTests.Offering(showing.Present(), WorkspaceAction.Answer));
        Draw(host, column);
        column.HoldStarted(FileScreens.SpeakAnswer);
        showing = new AskingWork(Questioned("question-2", 1));
        column.Tick();
        host.Wait(1);
        column.Tick();
        Assert.That(column.Screen.Question.Draft!.QuestionId, Is.EqualTo("question-2"));
        column.Heard("Ten minutes");
        Assert.That(column.Screen.Question.Draft!.Typed(0), Is.Null, "the words were for the question before");
        Assert.That(column.Screen.Notice, Is.EqualTo(VoiceText.QuestionChangedWhileSpeaking));
    }

    [Test]
    public void WordsHeardAfterThePersonMovedToAnotherPromptTypeIntoNeither()
    {
        var host = new FileMenuHost();
        var showing = new AskingWork(Questioned("question-1", 2));
        var column = Column(host, () => FileScreensTests.Offering(showing.Present(), WorkspaceAction.Answer));
        ReadQuestion(host, column);
        column.Act(FileScreens.Choose, "0");
        Draw(host, column);
        column.HoldStarted(FileScreens.SpeakAnswer);
        column.Act(FileScreens.NextQuestion, null);
        Assert.That(column.Screen.Question.Prompt, Is.EqualTo(1));
        column.Heard("Ten minutes");
        var draft = column.Screen.Question.Draft!;
        Assert.That((draft.Typed(0), draft.Typed(1)), Is.EqualTo(((string?)null, (string?)null)), "held for the first prompt, heard on the second");
        Assert.That(column.Screen.Notice, Is.EqualTo(VoiceText.QuestionChangedWhileSpeaking));
    }

    [Test]
    public void WordsHeardForALaterPromptOfAQuestionReplacedByOneWithFewerTypeIntoNone()
    {
        var host = new FileMenuHost();
        var showing = new AskingWork(Questioned("question-1", 2));
        var column = Column(host, () => FileScreensTests.Offering(showing.Present(), WorkspaceAction.Answer));
        ReadQuestion(host, column);
        column.Act(FileScreens.Choose, "0");
        Draw(host, column);
        column.Act(FileScreens.NextQuestion, null);
        ReadQuestion(host, column);
        Assert.That(column.Screen.Question.Prompt, Is.EqualTo(1));
        column.HoldStarted(FileScreens.SpeakAnswer);
        showing = new AskingWork(Questioned("question-2", 1));
        column.Tick();
        host.Wait(1);
        column.Tick();
        Assert.DoesNotThrow(() => column.Heard("Ten minutes"));
        Assert.That(column.Screen.Question.Draft!.Typed(0), Is.Null, "nothing typed into the new question's only prompt");
        Assert.That(column.Screen.Notice, Is.EqualTo(VoiceText.QuestionChangedWhileSpeaking));
    }

    [Test]
    public void NewEditorWordsOfTheSameLengthRequireTheirOwnReadingBeforeUse()
    {
        var (host, column) = TypedLongAnswer();
        UseInput(host, column);
        var saved = column.Screen.Question.Draft!.Typed(0);
        Draw(host, column);
        column.Act(FileScreens.TypeAnswer, null);
        var same = string.Join(" ", Enumerable.Range(1, 120).Select(step => "Lock it for a moment after the " + step + "th failed try"));
        host.Keyboard!.Value.Done(same);
        Assert.That((column.Screen.Question.InputPart, column.Screen.Question.InputRead), Is.EqualTo((0, false)));
        Draw(host, column);
        Assert.That(column.Screen.Question.InputRead, Is.False, "one part of new words is not the whole of them");
        column.Act(FileScreens.UseInput, null);
        Assert.That(column.Screen.Question.Draft!.Typed(0), Is.EqualTo(saved), "incomplete new words leave the saved answer unchanged");
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void NextPartOnALongEditorAdvancesAndRebuildsKeepThePartAndTheBuffer()
    {
        var (host, column) = TypedLongAnswer();
        var words = column.Screen.Question.Input;
        Draw(host, column);
        column.Act(FileScreens.NextInputPart, null);
        Assert.That(column.Screen.Question.InputPart, Is.EqualTo(1));
        column.Tick();
        host.Wait(1);
        column.Tick();
        Assert.That((column.Screen.Question.InputPart, column.Screen.Question.Input), Is.EqualTo((1, words)), "rebuilds preserve the current part and buffer");
        UseInput(host, column);
        Assert.That(column.Screen.Question.Draft!.Typed(0), Is.EqualTo(words));
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void CloseDetailsOnAChosenAnswersSidePanelBringsThePageBack()
    {
        var host = new FileMenuHost();
        var work = new AskingWork(new QuestionView
        {
            QuestionId = "question-1",
            Answerable = true,
            AskedAt = Samples.Time,
            Prompts = new List<QuestionPrompt>
            {
                new()
                {
                    Key = "q0", Header = "Database", Text = "Which database should it use?", Multiple = false, FreeText = false,
                    Options = new List<QuestionOption>
                    {
                        new() { Label = "Postgres, with read replicas in two regions, a nightly snapshot kept for thirty days and point in time recovery" },
                        new() { Label = "SQLite" },
                    },
                },
            },
        });
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer));
        for (var step = 0; step < 5 && column.Screen.Question.QuestionPart != null; step++)
        {
            Draw(host, column);
            column.Act(FileScreens.NextPart, FileScreens.QuestionKey);
        }
        Draw(host, column);
        column.Act(FileScreens.Choose, "0");
        Draw(host, column);
        Assert.That(column.Frame!.Side, Is.Not.Null, "the long answer's whole words beside the page");
        column.Act(SidePanel.Close, null);
        Assert.That(column.Frame!.Side, Is.Null, "Close details brings the page back, where Send answer sends with everything in view");
        Assert.That(column.Frame!.Lines.Any(line => line.Chosen), Is.True, "the answer stays chosen");
    }

    [Test]
    public void AnAnswerGoesOnlyByChoosingOnThePageThenSendAnswerOnceTheQuestionWasDrawn()
    {
        var host = new FileMenuHost();
        var work = new AskingWork(new QuestionView
        {
            QuestionId = "question-1",
            Answerable = true,
            AskedAt = Samples.Time,
            Prompts = new List<QuestionPrompt>
            {
                new() { Key = "q0", Header = "Lockout", Text = "How long should a lockout last?", Options = new List<QuestionOption> { new() { Label = "15 minutes" }, new() { Label = "1 hour" } }, Multiple = false, FreeText = true },
            },
        });
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer));
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(host.Sent, Is.Empty, "nothing chosen, nothing read: nothing sent");
        // Where the page packs the question onto its own page first, it is read there, then its answers.
        for (var step = 0; step < 5 && column.Screen.Question.QuestionPart != null; step++)
        {
            Draw(host, column);
            column.Act(FileScreens.NextPart, FileScreens.QuestionKey);
        }
        ChooseVisible(host, column, 1);
        Draw(host, column);
        Review(host, column);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(host.Sent.Count, Is.EqualTo(1), column.Frame!.Reason);
        Assert.That(host.Sent.Single(), Is.InstanceOf<ExecutionAnswerQuestionCommand>());
        Assert.That(((ExecutionAnswerQuestionCommand)host.Sent[0]).Payload.Answers.Single().Selected, Is.EqualTo(new[] { "1 hour" }));
        Assert.That(column.Screen.Section, Is.EqualTo(FileSection.Activity), "an answer sent shows how it goes with the activity");
    }

    [Test]
    public void HoldToTalkUnderTheQuestionDraftsAnAnswerAndForAnInstructionAsksBeforeSending()
    {
        var host = new FileMenuHost();
        var work = new AskingWork();
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer, WorkspaceAction.Instruct));
        ReadQuestion(host, column);
        column.Act(FileScreens.TypeAnswer, null);
        Draw(host, column);
        column.HoldStarted(FileScreens.SpeakAnswer);
        column.HoldEnded(FileScreens.SpeakAnswer, letGo: true);
        column.Heard("Teal");
        Assert.That((column.Screen.Question.Draft!.Typed(0), column.Screen.Question.Input), Is.EqualTo(((string?)null, "Teal")), "heard words are buffered until Use");
        UseInput(host, column);
        Assert.That(column.Screen.Question.Draft!.Typed(0), Is.EqualTo("Teal"));
        Assert.That(host.Sent, Is.Empty, "and nothing is sent");

        column.Act(MenuFrame.ChooseSection, "activity");
        Draw(host, column);
        column.HoldStarted(FileScreens.HoldToTalk);
        column.Heard("Carry on");
        Assert.That((column.Screen.Instruction.Words, column.Screen.Instruction.Spoken, column.Steering.Armed), Is.EqualTo(("Carry on", true, (WorkspaceAction?)null)), "transcription saves only a local protected draft");
        ReadInstructionDraft(host, column);
        column.Act(FileScreens.SendInstruction, null);
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Instruct), "explicit Send of a heard instruction waits for Yes");
        Assert.That(host.Sent, Is.Empty);
    }

    /// <summary>A synthetic instruction long enough for more than two focused confirmation parts; a mishearing can be anywhere.</summary>
    // The instruction editor and its confirmation now omit sections; 54 steps preserve the original
    // intermediate-part coverage at that actual larger budget without changing any physical bound.
    private static readonly string LongInstruction = string.Join(" ", Enumerable.Range(1, 54).Select(step => "then step " + step + " ok"));

    /// <summary>Reads only the visible local instruction pages, never sending or borrowing confirmation proof.</summary>
    private static void ReadInstructionDraft(FileMenuHost host, FileColumn column)
    {
        var sent = host.Sent.Count;
        var instruction = column.Screen.Instruction;
        Assert.That(instruction.Open, Is.True);
        var parts = instruction.Parts;
        for (var part = instruction.Part; part < parts && !instruction.Read; part++)
        {
            var field = column.Frame!.Lines.Single(line => line.Control == LineControl.Input);
            Assert.That((field.Words, field.FromRow), Is.EqualTo((instruction.Words, (int?)(part * instruction.PartRows))), "the current input part shows exactly its own rows of the saved words");
            Assert.That(field.Runs.AnyTheirs, Is.True, "the instruction's words retain outside-text provenance");
            column.Act(FileScreens.SendInstruction, null);
            Assert.That((host.Sent.Count, column.Steering.Armed), Is.EqualTo((sent, (WorkspaceAction?)null)), "an unread or undrawn draft cannot send or arm");
            var before = column.Frame;
            Draw(host, column);
            // Becoming ready rebuilds the frame. The actual new enabled frame must also be drawn.
            if (column.Frame != before) Draw(host, column);
            if (instruction.Read) break;
            var next = column.Frame!.Lines.Single(line => line.Action == FileScreens.NextInstructionPart);
            Assert.That((next.Control, next.Available), Is.EqualTo((LineControl.Action, true)));
            column.Act(next.Action!, next.Key);
            Assert.That(instruction.Part, Is.EqualTo(part + 1), "the visible pager advances one read part after its guard");
        }
        Assert.That(instruction.Read, Is.True, "every current local instruction part was drawn");
        Draw(host, column);
        Assert.That((host.Sent.Count, column.Steering.Armed), Is.EqualTo((sent, (WorkspaceAction?)null)), "reading alone neither sends nor arms");
        Assert.That((column.Frame!.Footer[PromptSlot.FarRight]!.Id, column.Frame.Footer[PromptSlot.FarRight]!.Available), Is.EqualTo((FileScreens.SendInstruction, true)), "only the explicit visible Send instruction offers submission");
    }

    /// <summary>
    /// Reads an armed instruction's words through to its last part, as the director draws each, checking
    /// that no Yes shows and none sends before then; then Yes sends exactly the words, once.
    /// </summary>
    private static void ReadsTheWordsThenSends(FileMenuHost host, FileColumn column, string words)
    {
        // The person's words lean as everything Halcyonic didn't write does, never quoted (lane V's ruling, 2026-10-08).
        var quoted = words;
        Assert.That(column.Screen.RequestParts, Is.GreaterThan(2), "the words take several parts");
        for (var part = 0; part < column.Screen.RequestParts; part++)
        {
            Assert.That(column.Frame!.Lines.Count(line => line.Words == quoted && line.FromRow == part * column.Screen.RequestPartRows), Is.EqualTo(1),
                "part " + part + " shows the words from its own row");
            Assert.That(column.Frame!.Lines.Any(line => line.Words.Contains("then step")), Is.True);
            Assert.That(column.Frame!.Lines.Where(line => line.Words != quoted).Any(line => line.Words.Contains("then step")), Is.False,
                "the words show only in their parts, never whole in a line cut at its rows");
            Assert.That(column.Frame!.Footer[PromptSlot.Free], Is.Null, "no Yes before the last part, part " + part);
            column.Act(FileScreens.Yes, null);
            Assert.That(host.Sent, Is.Empty, "a Yes pressed before the last part sends nothing");
            Draw(host, column);
            if (part < column.Screen.RequestParts - 1)
            {
                Assert.That(column.Frame!.Footer[PromptSlot.Free], Is.Null);
                column.Act(FileScreens.NextPart, FileScreens.RequestKey);
            }
        }
        Assert.That(column.Frame!.Footer[PromptSlot.Free]?.Id, Is.EqualTo(FileScreens.Yes), "every part drawn: Yes shows");
        column.Act(FileScreens.Yes, null);
        column.Act(FileScreens.Yes, null);
        Assert.That(((ExecutionSendInstructionCommand)host.Sent.Single()).Payload.Text, Is.EqualTo(words), "all of it, once");
    }

    [Test]
    public void AHeardInstructionLongerThanItsConfirmationIsReadInPartsAndSentOnlyAfterItsLast()
    {
        var host = new FileMenuHost();
        var running = new WaitingWork();
        running.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.Status = ExecutionStatus.Running;
        }, WorkstreamStatus.Running);
        var column = Column(host, () => FileScreensTests.Offering(running.Present(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt));
        column.HoldStarted(FileScreens.HoldToTalk);
        column.Heard(LongInstruction);
        Assert.That((column.Screen.Instruction.Words, column.Steering.Armed, host.Sent.Count), Is.EqualTo((LongInstruction, (WorkspaceAction?)null, 0)));
        ReadInstructionDraft(host, column);
        column.Act(FileScreens.SendInstruction, null);
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Instruct));
        Assert.That(column.Frame!.Lines.Any(line => line.Words == EntryText.ReadToPart(column.Screen.RequestParts)), Is.True, "it asks to read to the last part first");
        ReadsTheWordsThenSends(host, column, LongInstruction);
        Assert.That(column.Frame!.Lines.Any(line => line.Words == WorkspaceText.SendWordsAbove(heard: true)), Is.False, "sent, it asks nothing more");
    }

    [Test]
    public void ATypedInstructionAPolicyReviewsIsReadInPartsTooSinceYesSendsOnlyWordsShown()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork(welcomed: false);
        work.Change(execution =>
        {
            execution.Status = ExecutionStatus.Completed;
            execution.PendingApprovals.Clear();
        }, WorkstreamStatus.Completed);
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Instruct));
        column.Act(FileScreens.TellIt, null);
        host.Keyboard!.Value.Done(LongInstruction);
        Assert.That((column.Screen.Instruction.Words, column.Steering.Armed, host.Sent.Count), Is.EqualTo((LongInstruction, (WorkspaceAction?)null, 0)), "keyboard completion never submits or arms");
        ReadInstructionDraft(host, column);
        column.Act(FileScreens.SendInstruction, null);
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Instruct), "explicit Send applies the same policy review");
        ReadsTheWordsThenSends(host, column, LongInstruction);
    }

    /// <summary>
    /// The review's probe Q1a (2026-10-04): a heard instruction read whole at one text size is read again at
    /// the next, from its first part, and a Yes pressed on the frame laid at the old size sends nothing.
    /// </summary>
    [Test]
    public void AHeardInstructionReadWholeLocksAgainAtANewTextSize()
    {
        var host = new FileMenuHost();
        var running = new WaitingWork();
        running.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.Status = ExecutionStatus.Running;
        }, WorkstreamStatus.Running);
        var column = Column(host, () => FileScreensTests.Offering(running.Present(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt));
        column.HoldStarted(FileScreens.HoldToTalk);
        column.Heard(LongInstruction);
        ReadInstructionDraft(host, column);
        column.Act(FileScreens.SendInstruction, null);
        void ReadAll()
        {
            for (var guard = 0; guard < 60 && !column.Steering.CanConfirm; guard++)
            {
                Draw(host, column);
                if (!column.Steering.CanConfirm) column.Act(FileScreens.NextPart, FileScreens.RequestKey);
            }
        }
        ReadAll();
        Assert.That(column.Frame!.Footer[PromptSlot.Free]?.Id, Is.EqualTo(FileScreens.Yes), "read whole at the standard size");

        host.TextSize = TextSize.Larger;
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent, Is.Empty, "a Yes on the frame laid at the old size sends nothing");
        Assert.That((column.Steering.Armed, column.Steering.CanConfirm, column.Screen.RequestPart), Is.EqualTo(((WorkspaceAction?)WorkspaceAction.Instruct, false, 0)),
            "still armed, locked again, from its first part");
        Assert.That(column.Frame!.Footer[PromptSlot.Free], Is.Null);
        Draw(host, column);
        Assert.That(column.Steering.CanConfirm, Is.False, "the first part at the new size alone does not unlock it");
        ReadAll();
        column.Act(FileScreens.Yes, null);
        Assert.That(((ExecutionSendInstructionCommand)host.Sent.Single()).Payload.Text, Is.EqualTo(LongInstruction));
    }

    /// <summary>
    /// The review's probe Q5 (2026-10-04): the file's line of what this headset sent last never shows a
    /// refusal's or a failure's message, whichever code or effect, on Activity or anywhere it is presented.
    /// </summary>
    [Test]
    public void NoRefusalOrFailureMessageReachesTheFilesLineOfWhatWasSent()
    {
        const string Leak = "OpenCode could not be reached: connect ECONNREFUSED 127.0.0.1:4096 (/Users/someone/project)";
        var recorded = new List<CommandView>();
        foreach (RejectionCode code in Enum.GetValues(typeof(RejectionCode)))
        {
            if (code == RejectionCode.Demonstration) continue;
            recorded.Add(new CommandView
            {
                CommandId = "c-" + code, CommandType = CommandType.ExecutionSendInstruction, Status = CommandStatus.Rejected, ExecutionId = "e1",
                IssuedAt = Samples.Time, UpdatedAt = Samples.Time, Rejection = new CommandRejection { Code = code, Message = Leak },
            });
        }
        foreach (FailureEffect effect in Enum.GetValues(typeof(FailureEffect)))
        {
            foreach (var type in new[] { CommandType.ExecutionSendInstruction, CommandType.ExecutionAnswerQuestion, CommandType.ExecutionInterrupt })
            {
                recorded.Add(new CommandView
                {
                    CommandId = "c-" + effect + type, CommandType = type, Status = CommandStatus.Failed, ExecutionId = "e1",
                    IssuedAt = Samples.Time, UpdatedAt = Samples.Time, Failure = new CommandFailure { Code = "runtime_unreachable", Message = Leak, Effect = effect },
                });
            }
        }
        foreach (var command in recorded)
        {
            Assert.That(WorkspacePresenter.Feedback(command).Text, Does.Not.Contain("ECONNREFUSED").And.Not.Contain("OpenCode").And.Not.Contain("/Users/"),
                command.CommandId);
            var work = new AskingWork();
            work.Record(command);
            var host = new FileMenuHost();
            var column = Column(host, () => work.Present());
            column.Act(MenuFrame.ChooseSection, "activity");
            Assert.That(column.Frame!.Lines.Select(line => line.Words), Has.None.Contains("ECONNREFUSED").And.None.Contains("OpenCode"), command.CommandId);
            Assert.That(column.Frame!.Lines.Select(line => line.Words), Has.Some.EqualTo(WorkspacePresenter.Feedback(command).Text), "the line shows, in its own words");
        }
    }

    [Test]
    public void AShortHeardInstructionShowsWholeAndItsYesWaitsOnlyForItsDrawing()
    {
        var host = new FileMenuHost();
        var running = new WaitingWork();
        running.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.Status = ExecutionStatus.Running;
        }, WorkstreamStatus.Running);
        var column = Column(host, () => FileScreensTests.Offering(running.Present(), WorkspaceAction.Instruct));
        column.HoldStarted(FileScreens.HoldToTalk);
        column.Heard("Carry on");
        Assert.That((column.Screen.Instruction.Words, column.Screen.Instruction.Spoken, column.Steering.Armed, host.Sent.Count), Is.EqualTo(("Carry on", true, (WorkspaceAction?)null, 0)));
        Assert.That(column.Frame!.Lines.Select(line => line.Words), Is.EqualTo(new[] { "Your instruction", "Carry on" }));
        ReadInstructionDraft(host, column);
        column.Act(FileScreens.SendInstruction, null);
        Assert.That((column.Screen.RequestParts, column.Frame!.Footer[PromptSlot.Free]), Is.EqualTo((1, (Prompt?)null)), "one part, not drawn yet");
        Assert.That(column.Frame!.Lines.Select(line => line.Words), Is.EqualTo(new[] { "Carry on", "Read to part 1 first" }));
        Draw(host, column);
        Assert.That(column.Frame!.Lines.Select(line => line.Words), Is.EqualTo(new[] { "Carry on", "Review the transcribed instruction above. Send it?" }));
        column.Act(FileScreens.Yes, null);
        column.Act(FileScreens.Yes, null);
        Assert.That(((ExecutionSendInstructionCommand)host.Sent.Single()).Payload.Text, Is.EqualTo("Carry on"), "the same confirmation sends exactly once");
    }

    [Test]
    public void AnApprovalWhoseRequestReadsAsThePromptNeverCountsAsThatPromptsPage()
    {
        var host = new FileMenuHost();
        var work = new AskingWork();
        var quoted = "“" + work.Question.Prompts[0].Text + "”";
        // The agent's tool summary is data: it can read as the very prompt waiting behind it.
        work.Change(execution => execution.PendingApprovals.Add(WaitingWork.Approval("approval-1", quoted, Samples.Time)));
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Approve, WorkspaceAction.Deny, WorkspaceAction.Answer));
        Assert.That(column.Frame!.Footer[PromptSlot.FarRight]?.Id, Is.EqualTo(FileScreens.Approve), "the approval's page shows");
        Assert.That(column.Frame!.Lines.Any(line => line.WordsAreData && line.Words == quoted), Is.True, "with the prompt's words, quoted, as its request");
        for (var draw = 0; draw < 3; draw++) Draw(host, column);
        var draft = column.Screen.Question.Draft!;
        Assert.That(draft.WasShownWhole(0), Is.False, "an approval's page drawn is not the question read");

        // Answered, the approval goes, and the question's own page counts once drawn.
        work.Change(execution => execution.PendingApprovals.Clear());
        column.Tick();
        Assert.That((column.Frame!.Footer[PromptSlot.FarRight]?.Id, column.Screen.Question.Draft), Is.EqualTo((FileScreens.NextQuestion, draft)), "the short question now fits beside its answers but still needs its own visible draw");
        Assert.That(draft.WasShownWhole(0), Is.False);
        Draw(host, column);
        Assert.That(draft.WasShownWhole(0), Is.True, "the question's page drawn");
    }

    [Test]
    public void AQuestionOfferingTwoAnswersByOneLabelIsShownAsOneThatCantBeAnsweredHere()
    {
        var question = AskingWork.Scripted();
        question.Prompts.RemoveAt(1);
        question.Prompts[0].Text = "The old table is in the way. What should I do with its data?";
        question.Prompts[0].Options = new List<QuestionOption>
        {
            new() { Label = "Yes", Description = "Keep the data" },
            new() { Label = "Yes", Description = "Delete the data" },
        };
        // Nothing to type either, so no way to answer is left.
        question.Prompts[0].FreeText = false;
        var work = new AskingWork(question);
        Assert.That(work.Present().Actions, Does.Not.Contain(WorkspaceAction.Answer), "no answer is offered");

        // Even where an adapter offers one, nothing is answered, chosen or spoken here: Stop is the way on.
        var host = new FileMenuHost();
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer, WorkspaceAction.Interrupt));
        for (var draw = 0; draw < 3; draw++) Draw(host, column);
        var frame = column.Frame!;
        Assert.That(frame.Lines.Select(line => line.Words), Is.EqualTo(new[]
        {
            "The old table is in the way. What should I do with its data?",
            "Two of its answers read the same, so your choice can't be sent from here. Press Stop to go on.",
            WorkspaceText.AgentWaits,
        }), "what it asks, why, and that it waits, as for a secret");
        Assert.That(frame.Lines.Any(line => line.Action == FileScreens.Choose || line.Action == FileScreens.TypeAnswer), Is.False);
        Assert.That(frame.Footer[PromptSlot.Rare]?.Id, Is.EqualTo(FileScreens.Stop));
        Assert.That(frame.Footer[PromptSlot.Secondary], Is.Null, "no Hold to talk under a question that can't be answered");
        Assert.That(frame.Footer[PromptSlot.FarRight], Is.Null, "an unanswerable question offers no false Send control");
        column.Act(FileScreens.Choose, "1");
        column.Act(FileScreens.SendAnswer, null);
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent, Is.Empty, "never [\"Yes\", \"Yes\"], which the control plane refuses");
        Assert.That(column.Screen.Question.Draft!.Problem, Is.EqualTo(WorkspaceText.SameAnswersTwice));

        // Two labels, two answers.
        question.Prompts[0].Options[1].Label = "Yes, delete it";
        Assert.That(WorkspaceText.Answerable(question), Is.True);
        // Labels differing only in case are two answers to admission, which compares them as written.
        question.Prompts[0].Options[1].Label = "yes";
        Assert.That((WorkspaceText.OffersALabelTwice(question.Prompts[0]), WorkspaceText.Answerable(question)), Is.EqualTo((false, true)));

        // Labels are compared as their rows show them: what a person can't see tells no two apart.
        foreach (var (first, second) in new[]
        {
            ("Yes", "Yes "),
            ("Keep it", "Keep\tit"),
            ("Keep it", "Keep\n it"),
            ("Caf\u00E9", "Cafe\u0301"),
            ("Delete all", "Delete  all"),
        })
        {
            question.Prompts[0].Options[0].Label = first;
            question.Prompts[0].Options[1].Label = second;
            Assert.That((WorkspaceText.OffersALabelTwice(question.Prompts[0]), WorkspaceText.Answerable(question)), Is.EqualTo((true, false)),
                "\"" + first + "\" and \"" + second + "\" read the same");
        }
        // What shows differently stays two answers: case, width, a ligature, a character shown by its code point.
        foreach (var (first, second) in new[]
        {
            ("Yes", "YES"),
            ("Yes", "\uFF39es"),
            ("file", "\uFB01le"),
            ("Yes", "Ye\u200Bs"),
            ("Yes", "Yes\u2800"),
        })
        {
            question.Prompts[0].Options[0].Label = first;
            question.Prompts[0].Options[1].Label = second;
            Assert.That(WorkspaceText.OffersALabelTwice(question.Prompts[0]), Is.False, "\"" + first + "\" and \"" + second + "\" show apart");
        }
    }

    /// <summary>
    /// Where only some answers read the same, their rows navigate to Read why without selecting; the
    /// explanation offers the ordinary confirmed Stop escape. The rest are answered as ever, and
    /// nothing, pressed, typed or heard, sends a label that reads alike (the review's L2, settled 2026-10-07).
    /// </summary>
    [Test]
    public void AnswersThatReadTheSameShowButCantBeChosenAndStopIsTheWayToTheOneMeant()
    {
        var question = AskingWork.Scripted();
        question.Prompts.RemoveAt(1);
        question.Prompts[0].Text = "The old table is in the way. What should I do with its data?";
        question.Prompts[0].Options = new List<QuestionOption>
        {
            new() { Label = "Yes", Description = "Keep the data" },
            new() { Label = "Yes ", Description = "Delete the data" },
            new() { Label = "Ask me later", Description = null },
        };
        Assert.That(WorkspaceText.Answerable(question), Is.True, "an answer that reads like no other, and words to type, are ways to answer");
        var work = new AskingWork(question);
        var host = new FileMenuHost();
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer, WorkspaceAction.Interrupt));
        ReadQuestion(host, column);
        // Alike rows remain visible as navigation, never selectable answer marks.
        var draft = column.Screen.Question.Draft!;
        var rows = new List<(string Words, bool Available)>();
        for (var page = 0; page < column.Screen.Question.Pages; page++)
        {
            if (page > 0)
            {
                column.Act(FileScreens.MoreAnswers, null);
                Draw(host, column);
            }
            var shown = column.Frame!.Lines.ToList();
            var answers = shown.Where(line => line.Action == FileScreens.Choose || line.Action == FileScreens.ReadWhy).ToList();
            rows.AddRange(answers.Select(line => (line.Words, line.Action == FileScreens.Choose && line.Available)));
            Assert.That(shown.Any(line => line.Action == FileScreens.Stop), Is.False, "Stop is offered only in its read explanation, never as a competing body action");
            Assert.That(column.Frame!.Footer[PromptSlot.Secondary], Is.Null, "voice belongs only inside the dedicated editor");
            foreach (var row in answers.Where(line => line.Action == FileScreens.ReadWhy))
            {
                Assert.That((row.Available, row.Control, row.Choice, row.AnswerMark, row.Fact, row.Opens),
                    Is.EqualTo((true, LineControl.Action, false, AnswerMark.None, "Unavailable · Read why", true)), "an unavailable answer offers readable navigation, no selection mark");
                column.Act(FileScreens.Choose, row.Key);
                Assert.That(draft.IsAnswered(0), Is.False, row.Words + " cannot be forged into a selection");
                column.Act(FileScreens.ReadWhy, row.Key);
                Assert.That(column.Screen.Question.ExplainingAmbiguity, Is.True);
                Assert.That((draft.IsAnswered(0), host.Sent.Count), Is.EqualTo((false, 0)), "Read why neither selects nor sends");
                for (var part = 0; part < column.Screen.Question.AmbiguityParts; part++)
                {
                    Draw(host, column);
                    if (column.Screen.Question.AmbiguityPart + 1 >= column.Screen.Question.AmbiguityParts) break;
                    column.Act(FileScreens.NextAmbiguityPart, null);
                }
                Assert.That((column.Frame!.Footer[PromptSlot.FarRight]!.Id, column.Frame.Footer[PromptSlot.FarRight]!.Available), Is.EqualTo((FileScreens.Stop, true)), "the whole explanation read offers the ordinary Stop escape");
                column.Act(FileScreens.BackAmbiguity, null);
                Draw(host, column);
                Assert.That((column.Screen.Question.Page, draft.IsAnswered(0)), Is.EqualTo((page, false)), "Back restores the same choices without an answer");
            }
        }
        Assert.That(rows, Is.EqualTo(new[]
        {
            ("Yes · Keep the data", false),
            ("Yes · Delete the data", false),
            ("Ask me later", true),
        }));
        Assert.That(draft.Problem, Is.EqualTo("Choose or type an answer first."));
        // Typed or heard alike words are refused; the editor has no Stop action, so its refusal promises none.
        const string typedAlike = "Your answer reads the same as answers that can't be chosen here. Answer another way.";
        column.Act(FileScreens.TypeAnswer, "3");
        host.Keyboard!.Value.Done("Yes\t");
        Assert.That((draft.Typed(0), column.Frame!.Footer[PromptSlot.FarRight]!.Reason), Is.EqualTo(((string?)null, typedAlike)), "alike text is not usable and says why beside Use, without promising Stop absent from the editor");
        column.Act(FileScreens.UseInput, null);
        Assert.That(draft.Typed(0), Is.Null);
        column.HoldStarted(FileScreens.SpeakAnswer);
        column.Heard(" Yes");
        Assert.That(draft.Typed(0), Is.Null, "heard alike, the same");
        Assert.That(draft.Type(0, "Yes", stopOnPage: false), Is.EqualTo("Your answer reads the same as answers that can't be chosen here. Answer another way."));
        Assert.That(draft.Type(0, "Yes, but keep a copy"), Is.Null, "other words are an answer");
        // Refused more loosely than answers are told apart: speech writes "Yes." for "Yes", and the agent reads words as it likes.
        foreach (var heard in new[] { " Yes.", "yes", "YES!", "Yes ?", "yes,", "Yes \t" })
        {
            Assert.That(draft.Type(0, heard), Is.EqualTo("Your answer reads the same as answers that can't be chosen here. Answer another way."), heard);
        }
        Assert.That(draft.Type(0, "Yes please"), Is.Null, "other words are an answer");

        // The answer that reads like no other is chosen and sent as ever.
        column.Act(FileScreens.CancelInput, null);
        draft.Type(0, null);
        ReadQuestion(host, column);
        for (var turns = 0; turns < 5 && !column.Screen.Question.Answers.Contains(2); turns++)
        {
            column.Act(FileScreens.MoreAnswers, null);
            Draw(host, column);
        }
        column.Act(FileScreens.Choose, "2");
        Assert.That(draft.Build().Single().Selected, Is.EqualTo(new[] { "Ask me later" }));

        // Without Stop to offer, the line and the refusal say only what can be done.
        var other = new FileMenuHost();
        var unstoppable = Column(other, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer));
        ReadQuestion(other, unstoppable);
        var unavailable = unstoppable.Frame!.Lines.First(line => line.Action == FileScreens.ReadWhy);
        unstoppable.Act(unavailable.Action!, unavailable.Key);
        for (var part = 0; part < unstoppable.Screen.Question.AmbiguityParts; part++)
        {
            Draw(other, unstoppable);
            if (unstoppable.Screen.Question.AmbiguityPart + 1 >= unstoppable.Screen.Question.AmbiguityParts) break;
            unstoppable.Act(FileScreens.NextAmbiguityPart, null);
        }
        Assert.That(unstoppable.Frame!.Footer.All.Any(each => each.Prompt.Id == FileScreens.Stop), Is.False, "the explanation never offers Stop without the capability");
        Assert.That(unstoppable.Screen.Question.Draft!.IsAnswered(0), Is.False);
        unstoppable.Act(FileScreens.BackAmbiguity, null);
        Draw(other, unstoppable);
        unstoppable.Act(FileScreens.TypeAnswer, "3");
        other.Keyboard!.Value.Done("Yes.");
        Assert.That(unstoppable.Frame!.Footer[PromptSlot.FarRight]!.Reason, Is.EqualTo("Your answer reads the same as answers that can't be chosen here. Answer another way."),
            "never Stop where the page offers none");
        Assert.That(unstoppable.Screen.Question.Draft!.Typed(0), Is.Null);

        var stopWork = new AskingWork(question, new[] { new CommandPolicy { CommandType = CommandType.ExecutionInterrupt, Policy = PolicyCategory.ReviewRequired } });
        var stopHost = new FileMenuHost();
        var stopping = Column(stopHost, () => FileScreensTests.Offering(stopWork.Present(), WorkspaceAction.Answer, WorkspaceAction.Interrupt));
        ReadQuestion(stopHost, stopping);
        var why = stopping.Frame!.Lines.First(line => line.Action == FileScreens.ReadWhy);
        stopping.Act(why.Action!, why.Key);
        for (var part = 0; part < stopping.Screen.Question.AmbiguityParts; part++)
        {
            Draw(stopHost, stopping);
            if (stopping.Screen.Question.AmbiguityPart + 1 >= stopping.Screen.Question.AmbiguityParts) break;
            stopping.Act(FileScreens.NextAmbiguityPart, null);
        }
        stopping.Act(FileScreens.Stop, null);
        Assert.That((stopping.Steering.Armed, stopHost.Sent.Count), Is.EqualTo(((WorkspaceAction?)WorkspaceAction.Interrupt, 0)), "the escape still requires ordinary policy confirmation");
    }

    /// <summary>
    /// A label holding what a normalizer refuses, as a noncharacter copied from a file, never stops the task's file
    /// being drawn: the question stays answerable and stoppable (the review of lane W's labels, M1).
    /// </summary>
    [Test]
    public void ALabelANormalizerRefusesNeverStopsTheFileBeingDrawn()
    {
        var question = AskingWork.Scripted();
        question.Prompts.RemoveAt(1);
        question.Prompts[0].Options = new List<QuestionOption> { new() { Label = "Yes\uFFFE" }, new() { Label = "No" } };
        Assert.That(() => WorkspaceText.AsShown("Yes\uFFFE"), Throws.Nothing);
        Assert.That(WorkspaceText.AsShown("Yes\uFFFE"), Is.EqualTo("Yes\u2039U+FFFE\u203A"));
        var work = new AskingWork(question);
        Assert.That(work.Present().Actions, Does.Contain(WorkspaceAction.Answer).And.Contain(WorkspaceAction.Interrupt), "built, answerable and stoppable");
        var host = new FileMenuHost();
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer, WorkspaceAction.Interrupt));
        ReadQuestion(host, column);
        Assert.That(column.Frame!.Lines.Where(line => line.Action == FileScreens.Choose).Select(line => (line.Words, line.Available)),
            Is.EqualTo(new[] { ("Yes\u2039U+FFFE\u203A", true), ("No", true) }));
        column.Act(FileScreens.Choose, "0");
        Assert.That(column.Screen.Question.Draft!.Build().Single().Selected, Is.EqualTo(new[] { "Yes\uFFFE" }), "the raw label is what is sent");
    }

    /// <summary>A prompt whose every answer reads like another, with nothing to type, leaves no way to answer: as before L2.</summary>
    [Test]
    public void APromptLeftWithNoWayToAnswerKeepsTheQuestionUnanswerable()
    {
        var question = AskingWork.Scripted();
        question.Prompts[0].Options = new List<QuestionOption> { new() { Label = "Yes" }, new() { Label = "Yes " } };
        question.Prompts[0].FreeText = false;
        Assert.That(WorkspaceText.HasAWayToAnswer(question.Prompts[0]), Is.False);
        Assert.That((WorkspaceText.Answerable(question), WorkspaceText.CannotAnswer(question)), Is.EqualTo((false, WorkspaceText.SameAnswersTwice)));
        question.Prompts[0].FreeText = true;
        Assert.That(WorkspaceText.Answerable(question), Is.True, "typing is a way");
        question.Prompts[0].FreeText = false;
        question.Prompts[0].Options.Add(new QuestionOption { Label = "No" });
        Assert.That(WorkspaceText.Answerable(question), Is.True, "an answer that reads like no other is a way");
    }

    [Test]
    public void TellItOffersTheRecordedInstructionsToChooseAndSendsExactlyTheChosenWords()
    {
        var host = new FileMenuHost();
        var running = new WaitingWork();
        running.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.Status = ExecutionStatus.Running;
        }, WorkstreamStatus.Running);
        var recorded = new[] { new PresetInstruction("Add a test", "Add a test for refunds."), new PresetInstruction("Wrap up", "Wrap up and summarise.") };
        var column = Column(host, () => FileScreensTests.Offering(running.Present(), WorkspaceAction.Instruct), recorded);
        column.Act(FileScreens.TellIt, null);
        Assert.That(column.Frame!.Lines.Select(line => line.Words), Is.EqualTo(new[] { "Add a test for refunds.", "Wrap up and summarise." }));
        Assert.That(host.Keyboard, Is.Null, "the demonstration's instructions stand in for the keyboard");
        column.Act(FileScreens.TellIt, null);
        Assert.That(host.Sent, Is.Empty, "nothing chosen, nothing sent");
        column.Drawn(column.Frame!, null);
        column.Act(FileScreens.Preset, "1");
        Assert.That(host.Sent, Is.Empty, "choosing sends nothing");
        column.Act(FileScreens.TellIt, null);
        Assert.That((column.Screen.Instruction.Words, host.Sent.Count), Is.EqualTo(("Wrap up and summarise.", 0)), "Review instruction copies only the selected preset into a local draft");
        ReadInstructionDraft(host, column);
        column.Act(FileScreens.SendInstruction, null);
        column.Act(FileScreens.SendInstruction, null);
        Assert.That(((ExecutionSendInstructionCommand)host.Sent.Single()).Payload.Text, Is.EqualTo("Wrap up and summarise."));
    }

    [Test]
    public void WhereNoKeyboardOpensTellItOffersTheInstructionsAndNoRowOnlyOpensIt()
    {
        var host = new FileMenuHost { KeyboardOffered = false };
        var running = new WaitingWork();
        running.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.Status = ExecutionStatus.Running;
        }, WorkstreamStatus.Running);
        var column = Column(host, () => FileScreensTests.Offering(running.Present(), WorkspaceAction.Instruct));
        column.Act(FileScreens.TellIt, null);
        Assert.That(host.Keyboard, Is.Null, "no keyboard is asked for");
        Assert.That(column.Frame!.Lines.Select(line => line.Words), Is.EqualTo(WorkspaceText.PresetInstructions.Select(preset => preset.Text)));

        var asking = new AskingWork();
        var answering = Column(host, () => FileScreensTests.Offering(asking.Present(), WorkspaceAction.Answer));
        for (var step = 0; step < 5 && answering.Screen.Question.QuestionPart != null; step++)
        {
            Draw(host, answering);
            answering.Act(FileScreens.NextPart, FileScreens.QuestionKey);
        }
        Assert.That(answering.Frame!.Lines.Where(line => line.Action == FileScreens.Choose), Is.Not.Empty, "the answers show");
        Assert.That(answering.Frame!.Lines.Single(line => line.Action == FileScreens.TypeAnswer).Words, Is.EqualTo(FileScreens.WriteAnswer), "a distinct input action is available because voice is offered");
        answering.Act(FileScreens.TypeAnswer, null);
        Assert.That(host.Keyboard, Is.Null, "a voice-only editor never opens the unavailable keyboard");
        Draw(host, answering);
        answering.HoldStarted(FileScreens.SpeakAnswer);
        answering.Heard("Teal");
        Assert.That((answering.Screen.Question.Input, answering.Screen.Question.Draft!.Typed(0)), Is.EqualTo(("Teal", (string?)null)), "voice remains an unsaved buffer");
        UseInput(host, answering);
        var typed = answering.Frame!.Lines.Single(line => line.Action == FileScreens.TypeAnswer);
        Assert.That((typed.Words, typed.Chosen, typed.Fact), Is.EqualTo((FileScreens.EditAnswer, true, "Selected")), "used words return to a visibly selected edit action");
        Assert.That(typed.Runs.AnyTheirs, Is.False, "the Edit action is Halcyonic's words, not a disguised data run");
        answering.Act(FileScreens.TypeAnswer, null);
        Assert.That(answering.Frame!.Lines.Single(line => line.Control == LineControl.Input).Runs.ToString(), Is.EqualTo("[Teal]"), "the actual heard words lean in their explicit editor");
        Assert.That(host.Keyboard, Is.Null, "pressing it opens no keyboard");
    }

    [Test]
    public void DrawingAnApprovalOnWaitingCountsNothingTowardTheQuestionDraftedMeanwhile()
    {
        var host = new FileMenuHost();
        var work = new AskingWork();
        work.Change(execution => execution.PendingApprovals.Add(WaitingWork.Approval("approval-1", "Run the migration", Samples.Time)));
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Approve, WorkspaceAction.Deny, WorkspaceAction.Answer));
        Assert.That(column.Screen.Question.Draft, Is.Not.Null, "the question is drafted while the approval shows");
        Assert.That(column.Frame!.Lines.Select(line => line.Action), Has.None.EqualTo(FileScreens.Choose), "Waiting shows the approval");
        for (var draw = 0; draw < 3; draw++) Draw(host, column);
        Assert.That(column.Screen.Question.Draft!.WasShownWhole(0), Is.False, "the approval's page counts nothing toward the question");
    }

    [Test]
    public void TellItWithoutRecordedInstructionsTakesTheKeyboardsWordsOnlyWhenFinished()
    {
        var host = new FileMenuHost();
        var running = new WaitingWork();
        running.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.Status = ExecutionStatus.Running;
        }, WorkstreamStatus.Running);
        var column = Column(host, () => FileScreensTests.Offering(running.Present(), WorkspaceAction.Instruct));
        column.Act(FileScreens.TellIt, null);
        Assert.That(host.Keyboard, Is.Not.Null);
        // The system keyboard draws its prompt with no lean, so it holds Halcyonic's words alone, never the task's title.
        Assert.That(host.Keyboard!.Value.Prompt, Is.EqualTo(WorkspaceText.TellItPrompt));
        Assert.That(host.Keyboard!.Value.Prompt, Does.Not.Contain(running.Present().Character.Title));
        Assert.That((column.Screen.Instruction.Open, column.Frame!.Lines.Single(line => line.Control == LineControl.Input).InputFocused), Is.EqualTo((true, true)));
        host.Keyboard!.Value.Done("Add a test");
        Assert.That((column.Screen.Instruction.Words, column.Steering.Armed, host.Sent.Count), Is.EqualTo(("Add a test", (WorkspaceAction?)null, 0)), "completion saves the words without submission");
        ReadInstructionDraft(host, column);
        column.Act(FileScreens.SendInstruction, null);
        column.Act(FileScreens.SendInstruction, null);
        Assert.That(((ExecutionSendInstructionCommand)host.Sent.Single()).Payload.Text, Is.EqualTo("Add a test"));
    }
    [Test]
    public void ManualCompactKeepsLongEditorPageAndBufferButExpiresKeyboardAndReadingAuthorization()
    {
        var (host, column) = TypedLongAnswer();
        var words = column.Screen.Question.Input;
        var keyboard = host.Keyboard!.Value;
        Draw(host, column);
        column.Act(FileScreens.NextInputPart, null);
        Draw(host, column);
        column.Act(FileScreens.NextInputPart, null);
        var part = column.Screen.Question.InputPart;
        var session = column.Screen.Question.InputSession;
        var draft = column.Screen.Question.Draft;
        Assert.That(part, Is.EqualTo(2));
        var navigator = new MenuNavigator(MenuBar.Places.ToDictionary(place => place,
            _ => (Func<IMenuColumn>)(() => throw new InvalidOperationException("Resuming a compact file must not open another menu place."))));
        navigator.ShowBeside(column, "w1");
        var oldFrame = navigator.Frames(new MenuBar(MenuPlace.Tasks, "1 task is waiting for you", MenuPlace.Tasks)).Beside;
        navigator.Drawn(MenuColumn.File, oldFrame, null);
        navigator.Compact();
        navigator.FocusLeft();
        Assert.That(navigator.IsCompact, Is.True);
        Assert.That(navigator.TryExpandTask("w1"), Is.True);
        Assert.That(navigator.Beside, Is.SameAs(column));
        Assert.That(navigator.Act(MenuColumn.File, FileScreens.UseInput, null, oldFrame, null), Is.False, "the old frame cannot act after focus return and expansion");
        Assert.That((column.Screen.Question.Writing, column.Screen.Question.InputPart, column.Screen.Question.Input), Is.EqualTo((true, part, words)));
        Assert.That(column.Screen.Question.Draft, Is.SameAs(draft));
        Assert.That(column.Screen.Question.InputSession, Is.GreaterThan(session));
        Assert.That(column.Screen.Question.InputRead, Is.False);
        keyboard.Done("Expired keyboard words");
        Assert.That((column.Screen.Question.Input, draft!.Typed(0), host.Sent.Count), Is.EqualTo((words, (string?)null, 0)), "an old keyboard cannot change or save the retained buffer");
        column.Act(FileScreens.UseInput, null);
        Assert.That(draft.Typed(0), Is.Null);
        UseInput(host, column);
        Assert.That((draft.Typed(0), host.Sent.Count), Is.EqualTo((words, 0)), "new whole-reading evidence and Use save exactly the retained words");
        Review(host, column);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(((ExecutionAnswerQuestionCommand)host.Sent.Single()).Payload.Answers.Single().Text, Is.EqualTo(words));
    }

    [Test]
    public void ManualCompactRetainsSavedDraftAndReviewPageButReviewMustBeDrawnAgainBeforeSending()
    {
        var (host, column) = TypedLongAnswer();
        var words = column.Screen.Question.Input;
        UseInput(host, column);
        Review(host, column);
        var draft = column.Screen.Question.Draft;
        var page = column.Screen.Question.Page;
        Assert.That(column.Screen.Question.ReviewRead, Is.True);
        column.Compact();
        Assert.That(column.Screen.Question.Draft, Is.SameAs(draft));
        Assert.That((column.Screen.Question.Reviewing, column.Screen.Question.Page, draft!.Typed(0)), Is.EqualTo((true, page, words)));
        Assert.That(column.Screen.Question.AnswersRead(0), Is.True, "valid saved-word evidence survives unchanged semantic content and layout");
        Assert.That(column.Screen.Question.ReviewRead, Is.False);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(host.Sent, Is.Empty);
        Draw(host, column);
        Assert.That(column.Screen.Question.ReviewRead, Is.True, "only the current review page needs a fresh draw; prior unchanged parts remain read");
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(((ExecutionAnswerQuestionCommand)host.Sent.Single()).Payload.Answers.Single().Text, Is.EqualTo(words));
    }

    [Test]
    public void ManualCompactCancelsArmedAnswerAndOldYesCannotSendIt()
    {
        var host = new FileMenuHost();
        var column = AnsweringWithYes(host, Questioned("question-1", 1));
        Draw(host, column);
        column.Act(FileScreens.SendAnswer, null);
        var draft = column.Screen.Question.Draft;
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Answer));
        column.Compact();
        Assert.That(column.Steering.Armed, Is.Null);
        Assert.That(column.Screen.Question.Draft, Is.SameAs(draft));
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent, Is.Empty);
        Assert.That(column.Screen.Question.ReviewRead, Is.False);
    }

    [Test]
    public void ManualCompactExpiresInstructionKeyboardEvenAfterAReplacementKeyboardOpens()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Running; }, WorkstreamStatus.Running);
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Instruct));
        column.Act(FileScreens.TellIt, null);
        var expired = host.Keyboard!.Value.Done;
        Assert.That(column.Screen.Instruction.InputFocused, Is.True);
        column.Compact();
        expired("Expired words");
        Assert.That((column.Steering.Armed, column.Screen.Instruction.InputFocused, host.Sent.Count), Is.EqualTo(((WorkspaceAction?)null, false, 0)));
        Draw(host, column);
        Assert.That(column.Frame!.Lines.Any(line => line.Action == FileScreens.EditInstruction && line.Available), Is.True);
        column.Act(FileScreens.EditInstruction, null);
        var fresh = host.Keyboard!.Value.Done;
        expired("Expired words after a new editor");
        Assert.That((column.Steering.Armed, column.Screen.Instruction.InputFocused, host.Sent.Count), Is.EqualTo(((WorkspaceAction?)null, true, 0)), "old keyboard cannot consume the new authorization");
        fresh("Fresh deliberate instruction");
        Assert.That((column.Screen.Instruction.Words, column.Steering.Armed, host.Sent.Count), Is.EqualTo(("Fresh deliberate instruction", (WorkspaceAction?)null, 0)));
        ReadInstructionDraft(host, column);
        column.Act(FileScreens.SendInstruction, null);
        Assert.That(((ExecutionSendInstructionCommand)host.Sent.Single()).Payload.Text, Is.EqualTo("Fresh deliberate instruction"));
    }

    [Test]
    public void ChangedQuestionAndResizeWhileCompactExpireOnlyTheAffectedDraftOrLayoutProof()
    {
        var host = new FileMenuHost();
        var question = Questioned("same-question", 1);
        var work = new AskingWork(question);
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer));
        ChooseVisible(host, column, 0);
        var before = column.Screen.Question.Draft!;
        Assert.That(column.Screen.Question.AnswersRead(0), Is.True);
        column.Compact();
        host.TextSize = TextSize.Larger;
        column.Tick();
        Assert.That(column.Screen.Question.Draft, Is.SameAs(before));
        Assert.That(before.IsChosen(0, "Yes"), Is.True);
        Assert.That(column.Screen.Question.AnswersRead(0), Is.False, "a kept choice cannot borrow old layout reading");
        work.Change(execution => execution.PendingQuestions[0].Prompts[0].Text = "A different decision with the same question id?");
        host.Wait(2);
        column.Tick();
        Assert.That(column.Screen.Question.Draft, Is.Not.SameAs(before), "same-id semantic changes replace the invalid draft");
        Assert.That(column.Screen.Question.Draft!.IsAnswered(0), Is.False);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(host.Sent, Is.Empty);
    }

}

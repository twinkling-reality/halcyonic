using System;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class InstructionEditorTests
{
    private static (FileMenuHost Host, WaitingWork Work, FileColumn Column) Setup(bool welcomed = true)
    {
        var host = new FileMenuHost();
        var work = new WaitingWork(welcomed);
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Completed; }, WorkstreamStatus.Completed);
        var column = new FileColumn(host, () => work.Present(live: host.Live), new CommandFactory(Samples.Client), () => null, _ => null, () => "");
        return (host, work, column);
    }
    private static void Draw(FileMenuHost host, FileColumn column)
    {
        for (var step = 0; step < 3; step++) column.Drawn(column.Frame!, null);
        host.Wait(0.5);
    }
    private static void Read(FileMenuHost host, FileColumn column)
    {
        for (var part = 0; part < 100 && !column.Screen.Instruction.Read; part++)
        {
            Draw(host, column);
            if (!column.Screen.Instruction.Read) column.Act(FileScreens.NextInstructionPart, null);
        }
        Draw(host, column);
    }
    private static void Confirm(FileMenuHost host, FileColumn column)
    {
        for (var part = 0; part < 100 && !column.Steering.CanConfirm; part++)
        {
            Draw(host, column);
            if (!column.Steering.CanConfirm) column.Act(FileScreens.NextPart, FileScreens.RequestKey);
        }
        Draw(host, column);
        column.Act(FileScreens.Yes, null);
    }

    [Test]
    public void KeyboardDoneOnlySavesDraftAndExactDrawnExplicitSendSendsOnce()
    {
        var (host, _, column) = Setup();
        Assert.That(column.Frame!.Footer.All.Any(each => each.Prompt.Id == FileScreens.TellIt), Is.True);
        column.Act(FileScreens.TellIt, null);
        Assert.That(column.Frame!.Focused, Is.True);
        Assert.That(column.Frame.Lines.Any(line => line.Control == LineControl.Input && line.InputFocused), Is.True);
        host.Keyboard!.Value.Done("Add a test");
        Assert.That((host.Sent.Count, column.Steering.Armed, column.Screen.Instruction.Words), Is.EqualTo((0, (WorkspaceAction?)null, "Add a test")));
        column.Act(FileScreens.SendInstruction, null);
        Assert.That(host.Sent, Is.Empty, "a never-drawn completion cannot send");
        Read(host, column);
        Assert.That(column.Frame.Footer.All.Count(), Is.LessThanOrEqualTo(3));
        column.Act(FileScreens.SendInstruction, null);
        column.Act(FileScreens.SendInstruction, null);
        Assert.That(((ExecutionSendInstructionCommand)host.Sent.Single()).Payload.Text, Is.EqualTo("Add a test"));
    }

    [Test]
    public void SpokenDraftKeepsItsConfirmationProtectionAcrossUnchangedKeyboardEdit()
    {
        var (host, _, column) = Setup();
        column.HoldStarted(FileScreens.HoldToTalk);
        column.HoldEnded(FileScreens.HoldToTalk, true);
        column.Heard("Carry on");
        Assert.That((host.Sent.Count, column.Steering.Armed, column.Screen.Instruction.Spoken), Is.EqualTo((0, (WorkspaceAction?)null, true)));
        Draw(host, column);
        column.Act(FileScreens.EditInstruction, null);
        host.Keyboard!.Value.Done("Carry on");
        Read(host, column);
        column.Act(FileScreens.SendInstruction, null);
        Assert.That((host.Sent.Count, column.Steering.Armed, column.Steering.Heard), Is.EqualTo((0, (WorkspaceAction?)WorkspaceAction.Instruct, true)));
        Confirm(host, column);
        Assert.That(host.Sent.Count, Is.EqualTo(1));
    }

    [Test]
    public void LongDraftCanMoveBackwardFromEveryMiddlePartAndReadDoesNotSend()
    {
        var (host, _, column) = Setup();
        Assert.That(column.Frame!.Footer.All.Any(each => each.Prompt.Id == FileScreens.TellIt), Is.True);
        column.Act(FileScreens.TellIt, null);
        host.Keyboard!.Value.Done(string.Join(" ", Enumerable.Range(0, 80).Select(index => "detail" + index)));
        Assert.That(column.Screen.Instruction.Parts, Is.GreaterThan(3));
        Read(host, column);
        Assert.That(host.Sent, Is.Empty);
        column.Act(FileScreens.PreviousInstructionPart, null);
        Draw(host, column);
        Assert.That(column.Frame!.Lines.Any(line => line.Action == FileScreens.PreviousInstructionPart), Is.True);
        column.Act(FileScreens.PreviousInstructionPart, null);
        Draw(host, column);
        Assert.That(column.Screen.Instruction.Part, Is.EqualTo(column.Screen.Instruction.Parts - 3));
    }

    [Test]
    public void CompactRetainsDraftPageButRejectsOldKeyboardAndRequiresResumedExactDraw()
    {
        var (host, _, column) = Setup();
        Assert.That(column.Frame!.Footer.All.Any(each => each.Prompt.Id == FileScreens.TellIt), Is.True);
        column.Act(FileScreens.TellIt, null);
        var old = host.Keyboard!.Value.Done;
        old("Current words");
        Read(host, column);
        var part = column.Screen.Instruction.Part;
        column.Compact();
        old("Late overwrite");
        column.Act(FileScreens.SendInstruction, null);
        Assert.That((column.Screen.Instruction.Words, column.Screen.Instruction.Part, host.Sent.Count), Is.EqualTo(("Current words", part, 0)));
        Draw(host, column);
        column.Act(FileScreens.SendInstruction, null);
        Assert.That(host.Sent.Count, Is.EqualTo(1));
    }

    [Test]
    public void CancelAndFocusExpireInputCallbacksWithoutSending()
    {
        var (host, _, column) = Setup();
        Assert.That(column.Frame!.Footer.All.Any(each => each.Prompt.Id == FileScreens.TellIt), Is.True);
        column.Act(FileScreens.TellIt, null);
        var old = host.Keyboard!.Value.Done;
        column.FocusLeft();
        old("Late text");
        Assert.That(column.Screen.Instruction.Words, Is.Empty);
        Draw(host, column);
        column.Act(FileScreens.EditInstruction, null);
        old = host.Keyboard!.Value.Done;
        column.Act(FileScreens.CancelInstruction, null);
        old("Late after cancel");
        Assert.That((column.Screen.Instruction.Open, host.Sent.Count), Is.EqualTo((false, 0)));
    }

    [Test]
    public void EditingRevokesPolicyConfirmationAndNoLateInputCanOverwriteTheArmedDraft()
    {
        var (host, _, column) = Setup(welcomed: false);
        Assert.That(column.Frame!.Footer.All.Any(each => each.Prompt.Id == FileScreens.TellIt), Is.True);
        column.Act(FileScreens.TellIt, null);
        var old = host.Keyboard!.Value.Done;
        old("Add test");
        Read(host, column);
        column.Act(FileScreens.SendInstruction, null);
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Instruct));
        old("Old callback");
        Assert.That(column.Steering.Instruction, Is.EqualTo("Add test"));
        Assert.That(column.Frame!.Footer.All.Any(each => each.Prompt.Id == FileScreens.Cancel), Is.True);
        column.Act(FileScreens.Cancel, null);
        Draw(host, column);
        Assert.That(column.Frame!.Lines.Any(line => line.Action == FileScreens.EditInstruction), Is.True);
        column.Act(FileScreens.EditInstruction, null);
        Assert.That(column.Steering.Armed, Is.Null);
        host.Keyboard!.Value.Done("A different test");
        column.Act(FileScreens.SendInstruction, null);
        Assert.That(host.Sent, Is.Empty);
        Read(host, column);
        column.Act(FileScreens.SendInstruction, null);
        Confirm(host, column);
        Assert.That(((ExecutionSendInstructionCommand)host.Sent.Single()).Payload.Text, Is.EqualTo("A different test"));
    }
    [TestCase("capability")]
    [TestCase("disconnect")]
    [TestCase("approval")]
    public void AnInterveningLossOfAuthorityPermanentlyExpiresTheOldInputAndVoice(string change)
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Completed; }, WorkstreamStatus.Completed);
        var offered = true;
        var column = new FileColumn(host, () => FileScreensTests.Offering(work.Present(live: host.Live), offered ? new[] { WorkspaceAction.Instruct } : Array.Empty<WorkspaceAction>()),
            new CommandFactory(Samples.Client), () => null, _ => null, () => "");
        void Lose()
        {
            if (change == "capability") offered = false;
            else if (change == "disconnect") host.Live = false;
            else work.Change(execution => execution.PendingApprovals.Add(WaitingWork.Approval("new", "A new request", Samples.Time)));
            host.Wait(1); column.Tick();
        }
        void Return()
        {
            offered = true; host.Live = true;
            work.Change(execution => execution.PendingApprovals.Clear());
            host.Wait(1); column.Tick();
        }
        column.Act(FileScreens.TellIt, null);
        var late = host.Keyboard!.Value.Done;
        Lose(); Return();
        late("Late keyboard after authority returned");
        Assert.That(column.Screen.Instruction.Words, Is.Empty);
        Draw(host, column);
        column.Act(FileScreens.CancelInstruction, null);
        column.HoldStarted(FileScreens.HoldToTalk);
        Lose(); Return();
        column.Heard("Late voice after authority returned");
        Assert.That((column.Screen.Instruction.Open, host.Sent.Count), Is.EqualTo((false, 0)));
    }

    [Test]
    public void ANewlyDrawnPartCannotAdvanceUntilTheExistingTurnGuardAndResumeRestartsThatGuard()
    {
        var (host, _, column) = Setup();
        column.Act(FileScreens.TellIt, null);
        host.Keyboard!.Value.Done(string.Join(" ", Enumerable.Range(0, 80).Select(index => "step" + index)));
        column.Act(FileScreens.NextInstructionPart, null);
        Assert.That(column.Screen.Instruction.Part, Is.Zero);
        column.Drawn(column.Frame!, null);
        column.Act(FileScreens.NextInstructionPart, null);
        Assert.That(column.Screen.Instruction.Part, Is.Zero, "a rapid same-label press cannot skip words");
        column.Drawn(column.Frame!, null);
        host.Wait(0.41);
        column.Act(FileScreens.NextInstructionPart, null);
        Assert.That(column.Screen.Instruction.Part, Is.EqualTo(1));
        column.Compact();
        column.Drawn(column.Frame!, null);
        column.Act(FileScreens.PreviousInstructionPart, null);
        Assert.That(column.Screen.Instruction.Part, Is.EqualTo(1));
        column.Drawn(column.Frame!, null);
        host.Wait(0.41);
        column.Act(FileScreens.PreviousInstructionPart, null);
        Assert.That(column.Screen.Instruction.Part, Is.Zero);
    }

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void LongNoticeHasItsOwnMeasuredPagesAndBackAcknowledgesItWithoutReadingTheDraft(TextSize size)
    {
        var (host, _, column) = Setup();
        host.TextSize = size;
        column.Tick();
        column.Act(FileScreens.TellIt, null);
        var words = string.Join(" ", Enumerable.Range(0, 100).Select(index => "instruction" + index));
        host.Keyboard!.Value.Done(words);
        Draw(host, column);
        column.Act(FileScreens.NextInstructionPart, null);
        Assert.That(column.Screen.Instruction.Part, Is.EqualTo(1));
        var notice = string.Join(" ", Enumerable.Range(0, 80).Select(index => "explanation" + index));
        column.Said(notice);
        Assert.That(column.Screen.Instruction.ExplainingNotice, Is.True);
        Assert.That(column.Screen.Instruction.Part, Is.Zero, "new packing must restart its ordered reading proof");
        Assert.That(column.Frame!.Lines.First().Words, Is.EqualTo("What Happened"));
        Assert.That(column.Frame.Lines.Any(line => line.Words == notice && !line.Runs.AnyTheirs), Is.True);
        var page = new HeightBudget(host.PageHeight(1, besideMenu: false, focused: true));
        var height = column.Frame.Lines.Sum(line => page.Words(line.Rows)) + page.LineGap;
        Assert.That(height, Is.LessThanOrEqualTo(page.Room + 0.000001f));
        Draw(host, column);
        column.Act(FileScreens.NextInstructionNotice, null);
        Assert.That(column.Screen.Instruction.NoticePart, Is.EqualTo(1));
        Draw(host, column);
        Assert.That(column.Screen.Instruction.NoticePart, Is.EqualTo(1), "an unchanged redraw preserves explanation position");
        Assert.That(column.Screen.Instruction.Read, Is.False, "explanation pages are no instruction reading evidence");
        Assert.That(column.Frame.Footer.All.Any(each => each.Prompt.Id == FileScreens.BackInstructionNotice), Is.True);
        column.Act(FileScreens.BackInstructionNotice, null);
        Assert.That((column.Screen.Instruction.ExplainingNotice, column.Screen.Notice, column.Screen.Instruction.Words), Is.EqualTo((false, (string?)null, words)));
        column.Said(notice);
        Assert.That(column.Screen.Instruction.ExplainingNotice, Is.True, "a new occurrence of identical error must remain visible");
        Draw(host, column); column.Act(FileScreens.BackInstructionNotice, null);
        column.Act(FileScreens.SendInstruction, null);
        Assert.That(host.Sent, Is.Empty);
        Read(host, column);
        column.Act(FileScreens.SendInstruction, null);
        Assert.That(((ExecutionSendInstructionCommand)host.Sent.Single()).Payload.Text, Is.EqualTo(words));
    }

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void NoticeAndSizeReflowRestartTheDraftAtAReadableFirstPart(TextSize size)
    {
        var (host, _, column) = Setup();
        host.TextSize = size; column.Tick();
        column.Act(FileScreens.TellIt, null);
        host.Keyboard!.Value.Done(string.Join(" ", Enumerable.Range(0, 100).Select(index => "step" + index)));
        Draw(host, column); column.Act(FileScreens.NextInstructionPart, null);
        column.Said("Nothing was sent.");
        Assert.That(column.Screen.Instruction.Part, Is.Zero);
        Assert.That(column.Frame!.Lines.Any(line => line.Words == "Nothing was sent."), Is.True);
        if (column.Screen.Instruction.ExplainingNotice)
        {
            Draw(host, column); column.Act(FileScreens.BackInstructionNotice, null);
        }
        Draw(host, column); column.Act(FileScreens.NextInstructionPart, null);
        host.TextSize = size == TextSize.Standard ? TextSize.Larger : TextSize.Standard;
        host.Wait(1); column.Tick();
        Assert.That(column.Screen.Instruction.Part, Is.Zero);
        if (column.Screen.Instruction.ExplainingNotice)
        {
            Draw(host, column); column.Act(FileScreens.BackInstructionNotice, null);
        }
        Draw(host, column); column.Act(FileScreens.NextInstructionPart, null);
        Assert.That(column.Screen.Instruction.Part, Is.EqualTo(1), "the restarted part can establish fresh ordered proof");
        Assert.That(host.Sent, Is.Empty);
    }

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void AnInlineEditorNoticeUsesExistingConfirmationNoticeRowsAfterExplicitSend(TextSize size)
    {
        var (host, _, column) = Setup(welcomed: false);
        host.TextSize = size; column.Tick();
        column.Act(FileScreens.TellIt, null);
        host.Keyboard!.Value.Done("Add a test");
        column.Said("Nothing was sent.");
        Read(host, column);
        Assert.That(column.Screen.Instruction.InlineNotice, Is.True);
        Assert.That(column.Screen.Instruction.NoticeRows, Is.EqualTo(1));
        column.Act(FileScreens.SendInstruction, null);
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Instruct));
        Assert.That(column.Frame!.Lines.Single(line => line.Words == "Nothing was sent.").Rows, Is.EqualTo(2),
            "the confirmation retains its existing notice contract rather than editor-only packing");
        Assert.That(host.Sent, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ContinuationRestoresOnlyOrdinaryDraftWithFreshReadAndSpokenProtection(bool spoken)
    {
        var (host, _, original) = Setup();
        original.Act(FileScreens.TellIt, null);
        host.Keyboard!.Value.Done("Continue with a regression test");
        if (spoken)
        {
            original.HoldStarted(FileScreens.HoldToTalk);
            original.HoldEnded(FileScreens.HoldToTalk, true);
            original.Heard("Continue with a regression test");
        }
        Read(host, original);
        Assert.That(original.Screen.Instruction.Read, Is.True);
        var draft = original.ContinuationDraft();
        Assert.That(draft, Is.Not.Null);
        var record = new WorkspaceContinuation
        {
            WorkstreamId = original.Now!.Character.WorkstreamId, ExecutionId = original.Now.Execution!.ExecutionId,
            Section = WorkspaceSection.Activity, Page = 0, Instruction = draft,
        };
        var (restoredHost, _, restored) = Setup();
        Assert.That(restored.RestoreContinuation(record), Is.True);
        Assert.That((restored.Screen.Instruction.Words, restored.Screen.Instruction.Spoken, restored.Screen.Instruction.Read, restored.Screen.Instruction.InputFocused, restored.Steering.Armed),
            Is.EqualTo((draft!.Text, spoken, false, false, (WorkspaceAction?)null)));
        restored.Act(FileScreens.SendInstruction, null);
        Assert.That((host.Sent.Count, restoredHost.Sent.Count), Is.EqualTo((0, 0)));
        Assert.That(draft!.GetType().GetProperties().Select(property => property.Name), Is.EquivalentTo(new[] { "Text", "Spoken", "Part" }), "no question answer or read/armed field is exported");
        Read(restoredHost, restored);
        restored.Act(FileScreens.SendInstruction, null);
        Assert.That((restoredHost.Sent.Count, restored.Steering.Armed), Is.EqualTo(spoken ? (0, (WorkspaceAction?)WorkspaceAction.Instruct) : (1, (WorkspaceAction?)null)), "only explicit current Send may send; spoken words still require reviewed Yes");
        record.ExecutionId = "other-execution";
        Assert.That(restored.RestoreContinuation(record), Is.False, "the draft never retargets another execution");
    }

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void ContinuationRestoresAstralInstructionAndRequestWithinTheCanonicalCodepointBound(TextSize size)
    {
        var (host, _, file) = Setup();
        host.TextSize = size; file.Reflow();
        var words = string.Concat(Enumerable.Repeat("\U0001F680", 16001));
        var record = new WorkspaceContinuation
        {
            HandoffId = Guid.NewGuid().ToString("D"), ConnectionKey = new string('a', 64), JournalId = Samples.JournalId,
            ProjectId = Samples.ProjectId, WorkstreamId = file.Now!.Character.WorkstreamId, ExecutionId = file.Now.Execution!.ExecutionId,
            Surface = WorkspaceSurface.Request, Section = WorkspaceSection.Activity,
            Instruction = new WorkspaceInstructionDraft { Text = words, Spoken = true },
            Request = new WorkspaceRequestContinuation { Text = words, Spoken = true },
        };
        Assert.That(file.RestoreContinuation(record), Is.True, "valid astral text is bounded in codepoints, never shortened by UTF16 length");
        Assert.That(file.Screen.Instruction.Words, Is.EqualTo(words));
        Assert.That(file.Screen.Instruction.Read, Is.False);
        Assert.That(file.ContinuationRequest()!.Text, Is.EqualTo(words));
        var request = file.RequestFor(record, () => true);
        Assert.That(request.Draft.Text, Is.EqualTo(words));
        Assert.That((file.Surface, request.Draft.Spoken, request.Draft.RequestId), Is.EqualTo((WorkspaceSurface.Request, true, (string?)null)));
        Assert.That(host.Sent, Is.Empty);
        record.Request.Text = string.Concat(Enumerable.Repeat("\U0001F680", 32001));
        Assert.That(file.RestoreContinuation(record), Is.False);
        Assert.That(request.Draft.Text, Is.EqualTo(words), "failed import preserves the existing local words");
    }

}

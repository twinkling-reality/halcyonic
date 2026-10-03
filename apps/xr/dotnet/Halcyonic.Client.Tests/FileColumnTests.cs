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

    public bool Live { get; set; } = true;

    public bool KeyboardOffered { get; set; } = true;

    public (string Text, string Prompt, Action<string> Done)? Keyboard { get; private set; }

    public ClientProjection? State => null;

    public bool Connected => Live;

    public bool Demonstration => false;

    public double Now { get; set; } = 100;

    public DateTimeOffset Clock { get; set; } = DateTimeOffset.Parse("2026-10-02T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    public TimeZoneInfo Zone => TimeZoneInfo.Utc;

    public TextSize TextSize => TextSize.Standard;

    public bool VoiceOffered => true;

    public ControlPlaneApi? Api => null;

    public Task<CommandAckMessage>? Submit(CommandEnvelope command)
    {
        if (!Live) return null;
        Sent.Add(command);
        return Task.FromResult(new CommandAckMessage { CommandId = command.CommandId, Disposition = CommandAckDisposition.Accepted });
    }

    public void OpenKeyboard(string text, string prompt, Action<string> done) => Keyboard = (text, prompt, done);

    public int RowsOf(string words, float columnDegrees) => Math.Max(1, (words.Length + 39) / 40);

    public int RowsOf(PageLine line, float columnDegrees) => RowsOf(line.Words, columnDegrees);

    public bool FitsHalf(PageLine answer, float columnDegrees) => false;

    public int TitleRows(string subject, float columnDegrees) => 1;

    public int PageRows(bool sourceLine) => MenuFrame.RowsAPage(TextSize.Standard, sourceLine);

    public float PageHeight(int subjectRows, bool besideMenu) => MenuPage.Height(TextSize.Standard, subjectRows);

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

    private FileColumn Column(FileMenuHost host, Func<WorkspacePresentation?> present, IReadOnlyList<PresetInstruction>? recorded = null) =>
        new(host, present, factory, () => null, _ => recorded, () => "");

    /// <summary>The director draws the column's frame, and a second later it is read.</summary>
    private static void Draw(FileMenuHost host, FileColumn column)
    {
        column.Drawn(column.Frame!, sidePanel: false);
        host.Wait(1);
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
    public void ADrawOfAFrameTheColumnNoLongerStandsByCountsForNothing()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        var column = Column(host, () => Approving(work));
        column.Act(FileScreens.Approve, null);
        var stale = column.Frame!;
        column.Act(MenuFrame.ChooseSection, "activity");
        column.Drawn(stale, sidePanel: false);
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
        column.Act(FileScreens.Choose, "1");
        Draw(host, column);
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
        column.HoldStarted(FileScreens.SpeakAnswer);
        column.HoldEnded(FileScreens.SpeakAnswer, letGo: true);
        column.Heard("Teal");
        Assert.That(column.Screen.Question.Draft!.Typed(0), Is.EqualTo("Teal"), "what was heard becomes the typed answer");
        Assert.That(host.Sent, Is.Empty, "and nothing is sent");

        column.HoldStarted(FileScreens.HoldToTalk);
        column.Heard("Carry on");
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Instruct), "a heard instruction waits for Yes");
        Assert.That(host.Sent, Is.Empty);
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
        column.Act(FileScreens.Preset, "1");
        Assert.That(host.Sent, Is.Empty, "choosing sends nothing");
        column.Act(FileScreens.TellIt, null);
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
        Assert.That(answering.Frame!.Lines.Select(line => line.Action), Has.None.EqualTo(FileScreens.TypeAnswer),
            "a row that would only open the keyboard is left off");
        answering.HoldStarted(FileScreens.SpeakAnswer);
        answering.Heard("Teal");
        var typed = answering.Frame!.Lines.Single(line => line.Action == FileScreens.TypeAnswer);
        Assert.That((typed.Words, typed.Chosen), Is.EqualTo(("Your answer: “Teal”", true)), "words heard stay as their choice");
        answering.Act(FileScreens.TypeAnswer, null);
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
        Assert.That(column.Frame!.Lines.Single().Words, Is.EqualTo(WorkspaceText.TypingPrompt));
        host.Keyboard!.Value.Done("Add a test");
        Assert.That(((ExecutionSendInstructionCommand)host.Sent.Single()).Payload.Text, Is.EqualTo("Add a test"));
    }
}

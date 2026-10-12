using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class AnswerFlowTests
{
    private static readonly CommandFactory Commands = new(Samples.Client);

    private static (FileMenuHost Host, FileColumn Column, AskingWork Work) Open(QuestionView? question = null)
    {
        var host = new FileMenuHost { Submissions = new CommandSubmissions() };
        var work = new AskingWork(question);
        var column = new FileColumn(host, () => work.Present(host.Live, host.Submissions), Commands, () => null, _ => null, () => "");
        return (host, column, work);
    }

    private static void Draw(FileMenuHost host, FileColumn column)
    {
        column.Drawn(column.Frame!, null);
        host.Wait(1);
        column.Tick();
    }

    private static void Primary(FileMenuHost host, FileColumn column, string words)
    {
        Draw(host, column);
        var prompt = column.Frame!.Footer[PromptSlot.FarRight]!;
        Assert.That((prompt.Words, prompt.Available), Is.EqualTo((words, true)), "the visible primary action must name and permit the next step");
        column.Act(prompt.Id, null);
    }

    private static void ReadQuestion(FileMenuHost host, FileColumn column)
    {
        for (var tries = 0; tries < 30 && column.Screen.Question.QuestionPart != null; tries++)
        {
            Draw(host, column);
            var next = column.Frame!.Footer[PromptSlot.FarRight]!;
            Assert.That(next.Available, Is.True);
            column.Act(next.Id, null);
        }
        Draw(host, column);
        Assert.That(column.Screen.Question.QuestionPart, Is.Null);
    }

    private static void Choose(FileMenuHost host, FileColumn column, string label)
    {
        ReadQuestion(host, column);
        for (var page = 0; page < 30; page++)
        {
            var line = column.Frame!.Lines.FirstOrDefault(line => line.Action == FileScreens.Choose && (line.Words == label || line.Words.StartsWith(label + " · ", StringComparison.Ordinal)));
            if (line != null)
            {
                Assert.That(line.AnswerMark, Is.Not.EqualTo(AnswerMark.None));
                column.Act(line.Action!, line.Key);
                return;
            }
            var more = column.Frame.Lines.Single(line => line.Action == FileScreens.MoreAnswers);
            Assert.That(more.Control, Is.EqualTo(LineControl.Action));
            column.Act(more.Action!, more.Key);
            Draw(host, column);
        }
        Assert.Fail("The offered answer was not reachable through visible choices.");
    }

    private static void Review(FileMenuHost host, FileColumn column)
    {
        Primary(host, column, FileScreens.ReviewAnswers);
        for (var page = 0; page < 30; page++)
        {
            Draw(host, column);
            var next = column.Frame!.Footer[PromptSlot.FarRight]!;
            if (next.Id == FileScreens.SendAnswer) return;
            Assert.That(next.Available, Is.True);
            column.Act(next.Id, null);
        }
        Assert.Fail("The final review was not reachable through its primary controls.");
    }

    [Test]
    public void DarkAndOrdersReachSendThroughVisiblePrimaryLabels()
    {
        var (host, column, _) = Open();
        Assert.That(column.Frame!.Focused, Is.True);
        Assert.That(column.Frame.Sections, Is.Empty);
        Assert.That(column.Frame.Footer.All.Any(each => each.Prompt.Id is FileScreens.SendAnswer or FileScreens.SpeakAnswer), Is.False);
        Choose(host, column, "Dark");
        Primary(host, column, "Next question");
        Assert.That(column.Frame!.Footer.All.Any(each => each.Prompt.Id == FileScreens.SpeakAnswer), Is.False, "a non-free-text prompt has no false microphone affordance");
        Choose(host, column, "Orders");
        Review(host, column);
        Assert.That(column.Screen.Question.Draft!.AnswersNow.Select(answer => string.Join("+", answer.Chosen)), Is.EqualTo(new[] { "Dark", "Orders" }));
        Primary(host, column, WorkspaceText.Label(WorkspaceAction.Answer));
        var sent = host.Sent.OfType<ExecutionAnswerQuestionCommand>().Single();
        Assert.That(sent.Payload.Answers.Select(answer => string.Join("+", answer.Selected)), Is.EqualTo(new[] { "Dark", "Orders" }));
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(host.Sent.OfType<ExecutionAnswerQuestionCommand>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void OnePromptAlsoRequiresReadReviewBeforeSend()
    {
        var question = AskingWork.Scripted();
        question.Prompts.RemoveAt(1);
        var (host, column, _) = Open(question);
        Choose(host, column, "Dark");
        Assert.That(FileScreens.WhySendWaits(column.Screen), Is.Not.Null);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(host.Sent, Is.Empty);
        Review(host, column);
        Primary(host, column, WorkspaceText.Label(WorkspaceAction.Answer));
        Assert.That(host.Sent.OfType<ExecutionAnswerQuestionCommand>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void KeyboardEditsBufferOnlyAndOldSessionCannotAlterNewEditorOrSavedAnswer()
    {
        var (host, column, _) = Open();
        Choose(host, column, "Dark");
        column.Act(FileScreens.TypeAnswer, null);
        var old = host.Keyboard!.Value.Done;
        Assert.That(column.Screen.Question.Writing, Is.True);
        old("Teal");
        Assert.That(column.Screen.Question.Input, Is.EqualTo("Teal"));
        Assert.That(column.Screen.Question.Draft!.Typed(0), Is.Null);
        column.Act(FileScreens.CancelInput, null);
        Assert.That(column.Screen.Question.Draft.IsChosen(0, "Dark"), Is.True);
        Draw(host, column);
        column.Act(FileScreens.TypeAnswer, null);
        old("Late words");
        Assert.That(column.Screen.Question.Input, Is.Empty);
        host.Keyboard!.Value.Done("Green");
        Primary(host, column, "Use this answer");
        Assert.That(column.Screen.Question.Draft.Typed(0), Is.EqualTo("Green"));
        Assert.That(host.Sent, Is.Empty);
        Draw(host, column);
        column.Act(FileScreens.TypeAnswer, null);
        host.Keyboard!.Value.Done("Discard me");
        column.Act(FileScreens.CancelInput, null);
        Assert.That(column.Screen.Question.Draft.Typed(0), Is.EqualTo("Green"));
    }

    [Test]
    public void FocusLossExpiresKeyboardAndVoiceInsteadOfWritingAnotherAnswerOrInstruction()
    {
        var (host, column, _) = Open();
        ReadQuestion(host, column);
        column.Act(FileScreens.TypeAnswer, null);
        var late = host.Keyboard!.Value.Done;
        column.HoldStarted(FileScreens.SpeakAnswer);
        column.FocusLeft();
        late("Old keyboard");
        column.Heard("Old microphone");
        Assert.That(column.Screen.Question.Input, Is.Empty);
        Assert.That(column.Screen.Question.Draft!.Typed(0), Is.Null);
        Assert.That(column.Steering.Instruction, Is.Null);
        Assert.That(host.Sent, Is.Empty);
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
    public void SameIdSemanticChangesInvalidateDraftIncludingMutatedDtos(int field)
    {
        var asked = AskingWork.Scripted();
        var draft = new QuestionDraft("e", asked);
        var changed = asked.Prompts[0];
        switch (field)
        {
            case 0: changed.Text += " Really?"; break;
            case 1: changed.Header = "Different"; break;
            case 2: changed.Options[0].Label = "Different"; break;
            case 3: changed.Options[0].Description = "Different"; break;
            case 4: changed.Options.Reverse(); break;
            case 5: changed.Multiple = !changed.Multiple; break;
            case 6: changed.FreeText = !changed.FreeText; break;
            case 7: changed.Secret = !changed.Secret; break;
        }
        Assert.That(draft.Answers("e", asked), Is.False);
    }

    [Test]
    public void CapturedOldVoiceCannotBecomeANewInstructionRecording()
    {
        var (host, column, work) = Open();
        ReadQuestion(host, column);
        column.Act(FileScreens.TypeAnswer, null);
        var busy = false;
        var voice = new MenuVoice(() => busy, () => busy = true, () => { }, () => busy = false);
        Assert.That(voice.Hold(column, FileScreens.SpeakAnswer), Is.True);
        var old = voice.CaptureHeard();
        voice.Ended(column, FileScreens.SpeakAnswer, false);
        column.Act(FileScreens.CancelInput, null);
        work.Change(execution => { execution.Status = ExecutionStatus.Completed; execution.StartedAt = Samples.Time; execution.PendingQuestions.Clear(); });
        column.Act(MenuFrame.ChooseSection, FileScreens.Key(FileSection.Activity));
        Assert.That(voice.Hold(column, FileScreens.HoldToTalk), Is.True);
        var instruction = voice.CaptureHeard();
        old("Never an instruction");
        Assert.That(column.Steering.Instruction, Is.Null);
        instruction("Add pagination");
        Assert.That(column.Steering.Instruction, Is.EqualTo("Add pagination"));
        Assert.That(host.Sent, Is.Empty);
    }
}

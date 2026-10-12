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
    [TestCase("free_text")] [TestCase("secret")] [TestCase("approval")]
    public void AQuestionChangedBetweenDrawAndPressNeverOpensAnIncompatibleKeyboard(string change)
    {
        var (host, column, work) = Open();
        ReadQuestion(host, column);
        Assert.That(column.Frame!.Lines.Any(line => line.Action == FileScreens.TypeAnswer), Is.True);
        work.Change(execution =>
        {
            if (change == "free_text") execution.PendingQuestions[0].Prompts[0].FreeText = false;
            if (change == "secret") execution.PendingQuestions[0].Prompts[0].Secret = true;
            if (change == "approval") execution.PendingApprovals.Add(WaitingWork.Approval("new-approval", "Run migration", Samples.Time));
        });
        column.Act(FileScreens.TypeAnswer, null);
        Assert.That(host.Keyboard, Is.Null, "stale visible input must be revalidated before opening native UI");
        Assert.That(column.Screen.Question.Writing, Is.False);
        Assert.That(host.Sent, Is.Empty);
    }

    [TestCase("approval")] [TestCase("disconnect")]
    public void TemporarilyPreemptedEditorExpiresItsOldCallbackEvenAfterItBecomesAvailableAgain(string change)
    {
        var (host, column, work) = Open();
        ReadQuestion(host, column);
        column.Act(FileScreens.TypeAnswer, null);
        var late = host.Keyboard!.Value.Done;
        if (change == "approval") work.Change(execution => execution.PendingApprovals.Add(WaitingWork.Approval("new-approval", "Run migration", Samples.Time)));
        else host.Live = false;
        host.Wait(1); column.Tick();
        if (change == "approval") work.Change(execution => execution.PendingApprovals.Clear());
        else host.Live = true;
        host.Wait(1); column.Tick();
        ReadQuestion(host, column);
        column.Act(FileScreens.TypeAnswer, null);
        late("Never current");
        Assert.That(column.Screen.Question.Input, Is.Empty);
        Assert.That(column.Screen.Question.Draft!.Typed(0), Is.Null);
    }

    private static string LongInput => string.Join(" ", Enumerable.Range(0, 90).Select(index => "word" + index));

    private static void ReadInput(FileMenuHost host, FileColumn column)
    {
        for (var part = 0; part < 60; part++)
        {
            Draw(host, column);
            var primary = column.Frame!.Footer[PromptSlot.FarRight]!;
            if (primary.Id == FileScreens.UseInput) return;
            Assert.That(primary.Id, Is.EqualTo(FileScreens.NextInputPart));
            Assert.That(primary.Available, Is.True);
            column.Act(primary.Id, null);
        }
        Assert.Fail("Long input must remain navigable.");
    }

    [TestCase(false)] [TestCase(true)]
    public void EditingLongInputUnchangedOrDismissingKeyboardKeepsReadThroughNavigable(bool dismissed)
    {
        var (host, column, _) = Open();
        ReadQuestion(host, column);
        column.Act(FileScreens.TypeAnswer, null);
        host.Keyboard!.Value.Done(LongInput);
        Assert.That(column.Screen.Question.InputReading, Is.True);
        ReadInput(host, column);
        column.Act(FileScreens.EditInput, null);
        if (dismissed) host.KeyboardDismissed!(); else host.Keyboard!.Value.Done(LongInput);
        Assert.That(column.Screen.Question.InputFocused, Is.False);
        Assert.That(column.Screen.Question.InputReading, Is.True);
        ReadInput(host, column);
        Primary(host, column, "Use this answer");
        Assert.That(column.Screen.Question.Draft!.Typed(0), Is.EqualTo(LongInput));
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void VoiceOnlyLongEditorReturnsFromReadingToVisibleRecordingWithoutPromisingKeyboard()
    {
        var (host, column, _) = Open();
        host.KeyboardOffered = false;
        host.Wait(1); column.Tick();
        ReadQuestion(host, column);
        column.Act(FileScreens.TypeAnswer, null);
        Assert.That(column.Frame!.Lines.Single(line => line.Control == LineControl.Input).Words, Is.EqualTo("Hold to talk below"));
        column.HoldStarted(FileScreens.SpeakAnswer);
        column.Heard(LongInput);
        Assert.That(column.Screen.Question.InputReading, Is.True);
        column.Act(FileScreens.EditInput, null);
        Assert.That(column.Frame!.Footer[PromptSlot.Secondary]?.Id, Is.EqualTo(FileScreens.SpeakAnswer));
        Assert.That(host.Keyboard, Is.Null);
    }

    [Test]
    public void MultipleChoicesAcrossPagesAreRetainedReviewedAndSentButHiddenChoiceCannotBeInjected()
    {
        var asked = AskingWork.Scripted();
        asked.Prompts.RemoveAt(1);
        asked.Prompts[0].Multiple = true;
        asked.Prompts[0].FreeText = false;
        asked.Prompts[0].Options = Enumerable.Range(0, 14).Select(index => new QuestionOption { Label = "Option " + index }).ToList();
        var (host, column, _) = Open(asked);
        ReadQuestion(host, column);
        Assert.That(column.Screen.Question.Pages, Is.GreaterThan(1));
        Assert.That(column.Screen.Question.Answers, Does.Not.Contain(13));
        column.Act(FileScreens.Choose, "13");
        Assert.That(column.Screen.Question.Draft!.IsChosen(0, "Option 13"), Is.False);
        Choose(host, column, "Option 0");
        Choose(host, column, "Option 13");
        Assert.That(column.Screen.Question.Draft.AnswersNow[0].Chosen, Is.EquivalentTo(new[] { "Option 0", "Option 13" }));
        Review(host, column);
        Assert.That(string.Join(" ", column.Frame!.Lines.Select(line => line.Words)), Does.Contain("Option 0").And.Contain("Option 13"));
        Primary(host, column, "Send answer");
        Assert.That(host.Sent.OfType<ExecutionAnswerQuestionCommand>().Single().Payload.Answers[0].Selected, Is.EquivalentTo(new[] { "Option 0", "Option 13" }));
    }

    [Test]
    public void ResizingAfterUseBeforeMeasuringCannotTransferOldReadEvidenceToNewLayout()
    {
        var (host, column, _) = Open();
        ReadQuestion(host, column);
        column.Act(FileScreens.TypeAnswer, null);
        host.Keyboard!.Value.Done(LongInput);
        ReadInput(host, column);
        Primary(host, column, "Use this answer");
        host.TextSize = TextSize.Larger;
        column.Tick();
        Assert.That(column.Screen.Question.Draft!.Typed(0), Is.EqualTo(LongInput), "size change retains the saved draft");
        Assert.That(column.Screen.Question.AnswersRead(0), Is.False, "read-through at the old size cannot certify the resized text");
        column.Act(FileScreens.NextQuestion, null);
        Assert.That(column.Screen.Question.Prompt, Is.EqualTo(0));
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void EditorReadEvidenceCannotBeConsumedByAnotherPromptWithIdenticalSavedWords()
    {
        var asked = AskingWork.Scripted();
        asked.Prompts[1].FreeText = true;
        var (host, column, _) = Open(asked);
        ReadQuestion(host, column);
        // Model an existing unread saved answer on prompt 2, then read the same exact text in prompt 1.
        Assert.That(column.Screen.Question.Draft!.Type(1, LongInput), Is.Null);
        column.Act(FileScreens.TypeAnswer, null);
        host.Keyboard!.Value.Done(LongInput);
        ReadInput(host, column);
        Primary(host, column, "Use this answer");
        Assert.That(column.Screen.Question.AnswersRead(0), Is.True);
        Assert.That(column.Screen.Question.AnswersRead(1), Is.False, "same words do not grant another prompt's reading");
    }

    [Test]
    public void LongInputAndReviewPrimaryRemainPressableThroughActualNavigatorDrawHandles()
    {
        var (host, column, _) = Open();
        var navigator = new MenuNavigator(MenuBar.Places.ToDictionary(place => place, _ => (Func<IMenuColumn>)(() => column)));
        navigator.ShowBeside(column, "w1");
        var bar = new MenuBar(MenuPlace.Tasks, "A question is waiting");
        bool Forward()
        {
            MenuFrame? frame = null;
            for (var draw = 0; draw < 3; draw++)
            {
                frame = navigator.Frames(bar).Beside;
                navigator.Drawn(MenuColumn.File, frame, null);
                host.Wait(1); column.Tick();
            }
            frame = navigator.Frames(bar).Beside;
            navigator.Drawn(MenuColumn.File, frame, null);
            var prompt = frame!.Footer[PromptSlot.FarRight]!;
            Assert.That(prompt.Available, Is.True);
            return navigator.Act(MenuColumn.File, prompt.Id, null, frame, null);
        }
        ReadQuestion(host, column);
        column.Act(FileScreens.TypeAnswer, null);
        host.Keyboard!.Value.Done(LongInput);
        Assert.That(column.Screen.Question.InputParts, Is.GreaterThan(1));
        Assert.That(Forward(), Is.True, "drawing an intermediate input page must not perpetually clear its own press handles");
        Assert.That(column.Screen.Question.InputPart, Is.EqualTo(1));
        ReadInput(host, column);
        Primary(host, column, "Use this answer");
        Primary(host, column, "Next question");
        Choose(host, column, "Orders");
        Primary(host, column, FileScreens.ReviewAnswers);
        Assert.That(column.Screen.Question.Pages, Is.GreaterThan(1));
        Assert.That(Forward(), Is.True, "an intermediate review draw must enable its visible primary and preserve the current handle");
        Assert.That(column.Screen.Question.Page, Is.EqualTo(1));
        Assert.That(host.Sent, Is.Empty);
    }

    [TestCase(false)] [TestCase(true)]
    public void RepeatedUnanswerableDrawsPreserveNavigatorStopAndCloseHandles(bool secret)
    {
        var question = AskingWork.Scripted();
        if (secret) question.Prompts[0].Secret = true; else question.Answerable = false;
        foreach (var action in new[] { FileScreens.Stop, Footer.Close })
        {
            var (host, column, _) = Open(question);
            var navigator = new MenuNavigator(MenuBar.Places.ToDictionary(place => place, _ => (Func<IMenuColumn>)(() => column)));
            navigator.ShowBeside(column, "w1");
            var bar = new MenuBar(MenuPlace.Tasks, "A question is waiting");
            MenuFrame? frame = null;
            for (var draw = 0; draw < 4; draw++)
            {
                frame = navigator.Frames(bar).Beside;
                navigator.Drawn(MenuColumn.File, frame, null);
                host.Wait(1); column.Tick();
            }
            frame = navigator.Frames(bar).Beside;
            navigator.Drawn(MenuColumn.File, frame, null);
            Assert.That(frame!.Footer.All.Any(each => each.Prompt.Id == action), Is.True);
            Assert.That(navigator.Act(MenuColumn.File, action, null, frame, null), Is.True, "repeated draws without a primary must leave actual Stop/Close handles usable");
            if (action == Footer.Close) Assert.That(navigator.Beside, Is.Null);
            else Assert.That(column.Steering.Armed == WorkspaceAction.Interrupt || host.Sent.OfType<ExecutionInterruptCommand>().Any(), Is.True);
        }
    }

    [TestCase(TextSize.Standard)] [TestCase(TextSize.Larger)]
    public void FocusedGeometryUsesOnlyActualPartsAndRemainsInsideTheSameField(TextSize size)
    {
        var ordinary = MenuPage.Height(size, 1);
        var focused = MenuPage.Height(size, 1, focused: true);
        Assert.That(focused, Is.GreaterThan(ordinary));
        var zoom = size == TextSize.Larger ? Comfort.LargerTextScale : 1f;
        PlaneComposition Actual(float page) => new(new[]
        {
            new PlaneColumn(PlaneComposition.Units(Glaze.Menu.FileColumnDegrees), MenuPage.Subject(1, pill: true), MenuPage.Content(page)),
        }, zoom);
        Assert.That(MenuPage.Fits(Actual(focused)), Is.True);
        Assert.That(MenuPage.Fits(Actual(focused + PlaneComposition.Units(0.2f))), Is.False, "a focused page still obeys the existing field bound");
    }

    [Test]
    public void FocusedBudgetCacheChangesForApprovalAndOrdinarySectionsThenReturns()
    {
        var (host, column, work) = Open();
        int Rows(bool focused)
        {
            var budget = HeightBudget.Of(host.TextSize, 1, focused);
            return budget.WordsIn(budget.Room - budget.Words(1) - budget.LineGap - budget.Reason);
        }
        Assert.That(Rows(true), Is.GreaterThan(Rows(false)));
        Assert.That(column.Screen.Question.QuestionPartRows, Is.EqualTo(Rows(true)));
        work.Change(execution => execution.PendingApprovals.Add(WaitingWork.Approval("approval", "Run migration", Samples.Time)));
        host.Wait(1); column.Tick();
        Assert.That(column.Frame!.Focused, Is.False);
        Assert.That(column.Screen.Question.QuestionPartRows, Is.EqualTo(Rows(false)));
        work.Change(execution => execution.PendingApprovals.Clear());
        host.Wait(1); column.Tick();
        Assert.That(column.Frame!.Focused, Is.True);
        Assert.That(column.Screen.Question.QuestionPartRows, Is.EqualTo(Rows(true)));
        column.Act(MenuFrame.ChooseSection, FileScreens.Key(FileSection.Activity));
        Assert.That(column.Frame!.Focused, Is.False);
        Assert.That(column.Screen.Question.QuestionPartRows, Is.EqualTo(Rows(false)));
        column.Act(MenuFrame.ChooseSection, FileScreens.Key(FileSection.Waiting));
        Assert.That(column.Frame!.Focused, Is.True);
        Assert.That(column.Screen.Question.QuestionPartRows, Is.EqualTo(Rows(true)));
    }

    [Test]
    public void LongQuestionCanBeRereadFromBoundedContextThenReturnToSameChoicesAndDraft()
    {
        var question = AskingWork.Scripted();
        question.Prompts.RemoveAt(1);
        question.Prompts[0].Text = string.Join(" ", Enumerable.Repeat("Explain this part of the requested decision.", 12));
        question.Prompts[0].Options.AddRange(Enumerable.Range(0, 10).Select(index => new QuestionOption { Label = "Extra " + index }));
        var (host, column, _) = Open(question);
        Choose(host, column, "Extra 7");
        var page = column.Screen.Question.Page;
        var context = column.Frame!.Lines.Single(line => line.Action == FileScreens.ReadQuestion);
        Assert.That((context.Control, context.Fact, context.Opens), Is.EqualTo((LineControl.Action, "Read question", true)));
        column.Act(context.Action!, context.Key);
        ReadQuestion(host, column);
        Assert.That(column.Screen.Question.Page, Is.EqualTo(page));
        Assert.That(column.Screen.Question.Draft!.IsChosen(0, "Extra 7"), Is.True);
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void BodyStopConfirmationUsesOrdinaryBudgetAndEditorDoesNotPromiseAbsentStop()
    {
        var question = AskingWork.Scripted();
        question.Prompts[0].Options = new List<QuestionOption>
        {
            new() { Label = "Yes" }, new() { Label = " Yes " }, new() { Label = "No" },
        };
        var (host, column, _) = Open(question);
        ReadQuestion(host, column);
        var stop = column.Frame!.Lines.Single(line => line.Action == FileScreens.Stop);
        Assert.That((stop.Words, stop.Fact, stop.Control), Is.EqualTo(("Stop task", "Alike answers cannot be chosen", LineControl.Action)));
        column.Act(FileScreens.TypeAnswer, null);
        host.Keyboard!.Value.Done("Yes.");
        Draw(host, column);
        Assert.That(column.Frame!.Footer[PromptSlot.FarRight]!.Reason, Does.Not.Contain("press Stop"));
        Assert.That(column.Frame.Footer[PromptSlot.FarRight]!.Available, Is.False);
        column.Act(FileScreens.CancelInput, null);
        ReadQuestion(host, column);
        column.Act(stop.Action!, stop.Key);
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Interrupt));
        Assert.That(column.Frame!.Focused, Is.False);
        var ordinary = HeightBudget.Of(host.TextSize, 1);
        Assert.That(column.Screen.Question.QuestionPartRows, Is.EqualTo(ordinary.WordsIn(ordinary.Room - ordinary.Words(1) - ordinary.LineGap - ordinary.Reason)));
        Assert.That(host.Sent, Is.Empty, "Stop still requires current policy confirmation");
    }

    [Test]
    public void DrawingCutOptionCannotPromoteItWhenQuestionReadingReleasesReasonSpace()
    {
        var question = AskingWork.Scripted();
        question.Prompts.RemoveAt(1);
        question.Prompts[0].FreeText = false;
        question.Prompts[0].Options = new List<QuestionOption> { new() { Label = "A long label", Description = "A second row" } };
        var draft = new QuestionDraft("e1", question);
        var flow = new FileQuestion();
        flow.Show(draft, new[] { new PromptMeasure(1, new[] { 2 }, new[] { 2 }) }, new RowBudget(4), new RowBudget(3));
        Assert.That(flow.AnswerShows(0, 0), Is.EqualTo(1), "the captured initial frame only shows its first row");
        Assert.That(flow.QuestionCut(0), Is.False);
        flow.Drawn(DateTimeOffset.Parse(Samples.Time));
        flow.Choose(0);
        Assert.That(flow.AnswersRead(0), Is.False, "released layout space cannot certify a second row absent from the frame just drawn");
        Assert.That(flow.AnswerShows(0, 0), Is.EqualTo(1), "choosing the unread option reinstates its nontrivial reason budget");
    }

}

using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>Work whose agent asks the mock's scripted question (fixtures/scenarios/question_asked.json).</summary>
internal sealed class AskingWork
{
    public AskingWork(QuestionView? question = null, IEnumerable<CommandPolicy>? policies = null)
    {
        Question = question ?? Scripted();
        Workstream = Samples.Workstream("w1", WorkstreamStatus.WaitingForHuman, "e1", AttentionLevel.ActionRequired,
            new QuestionPendingReason { ExecutionId = "e1", QuestionId = Question.QuestionId });
        Execution = Samples.Execution("e1", "w1", ExecutionStatus.WaitingForHuman);
        Execution.PendingQuestions.Add(Question);
        State.ApplySnapshot(Samples.Snapshot(1, new[] { Workstream }, new[] { Execution }), new StateChanges());
        var welcome = Samples.Welcome(resumed: false, head: 1);
        if (policies != null) welcome.CommandPolicies = policies.ToList();
        State.ApplyWelcome(welcome);
    }

    public QuestionView Question { get; }

    public ClientProjection State { get; } = new();

    public WorkstreamView Workstream { get; }

    public ExecutionView Execution { get; }

    public WorkspacePresentation Present(bool live = true) => WorkspacePresenter.Present(Workstream, State, new ActivityLog(), live);

    /// <summary>Replaces the execution the way an event's changes would.</summary>
    public void Change(Action<ExecutionView> execution)
    {
        execution(Execution);
        State.ApplySnapshot(Samples.Snapshot(State.Position + 1, new[] { Workstream }, new[] { Execution }), new StateChanges());
    }

    public static QuestionView Scripted() => new()
    {
        QuestionId = "question-1",
        Answerable = true,
        AskedAt = Samples.Time,
        Prompts = new List<QuestionPrompt>
        {
            new()
            {
                Key = "q0", Header = "Colour scheme", Text = "Which colour scheme should the dashboard use?",
                Options = new List<QuestionOption>
                {
                    new() { Label = "Light", Description = "Dark text on a light background" },
                    new() { Label = "Dark", Description = "Light text on a dark background" },
                },
                Multiple = false, FreeText = true, Secret = false,
            },
            new()
            {
                Key = "q1", Header = "Pages", Text = "Which pages should change?",
                Options = new List<QuestionOption>
                {
                    new() { Label = "Sign in", Description = null },
                    new() { Label = "Orders", Description = null },
                    new() { Label = "Settings", Description = null },
                },
                Multiple = true, FreeText = false, Secret = false,
            },
        },
    };
}

public class QuestionTests
{
    private readonly CommandFactory factory = new(Samples.Client);

    private static QuestionDraft Draft(AskingWork work) => new(work.Execution.ExecutionId, work.Question);

    [Test]
    public void AQuestionNeedsThePersonAndOpensOnWhatTheyAreAsked()
    {
        var work = new AskingWork();
        var workspace = work.Present();
        Assert.That(workspace.Character.StatusLabel, Is.EqualTo("Needs you"));
        Assert.That(WorkspaceText.Peek(workspace), Is.EqualTo("Asks you 2 questions: Colour scheme; Pages"),
            "the whole question, never only the prompt the body may not show");
        Assert.That(WorkspaceText.FirstQuestion(workspace), Is.EqualTo(WorkspaceQuestion.NeedFromYou));
        Assert.That(workspace.QuestionToAnswer, Is.SameAs(work.Question));
        Assert.That(workspace.Actions, Does.Contain(WorkspaceAction.Answer).And.Contain(WorkspaceAction.Interrupt));
        Assert.That(WorkspaceText.QuestionLead(workspace), Is.Empty, "the answer line already names one question");
        Assert.That(WorkspaceText.PromptHeading(work.Question, 0), Is.EqualTo("Question 1 of 2 · Colour scheme"));
        Assert.That(WorkspaceText.PromptHow(work.Question.Prompts[0]), Is.EqualTo("Choose one, or type your own."));
        Assert.That(WorkspaceText.PromptHow(work.Question.Prompts[1]), Is.EqualTo("Choose any that apply."));
        Assert.That(WorkspaceText.Label(WorkspaceAction.Answer), Is.EqualTo("Send answer"));
    }

    [Test]
    public void OneAnswerTakesALabelOrTextNeverBoth()
    {
        var draft = Draft(new AskingWork());
        draft.Choose(0, "Light");
        Assert.That(draft.Type(0, "Solarized, please"), Is.Null);
        Assert.That(draft.IsChosen(0, "Light"), Is.False, "typing replaces the chosen answer");
        draft.Choose(0, "Dark");
        Assert.That(draft.Typed(0), Is.Null, "choosing replaces the typed answer");
        draft.Choose(0, "Light");
        Assert.That(draft.IsChosen(0, "Dark"), Is.False, "one answer at a time");
        draft.Choose(0, "Light");
        Assert.That(draft.IsAnswered(0), Is.False, "pressing the chosen answer again unchooses it");
    }

    [Test]
    public void SeveralAnswersToggleAndTypedTextOnlyWhereAllowed()
    {
        var draft = Draft(new AskingWork());
        draft.Choose(1, "Settings");
        draft.Choose(1, "Sign in");
        draft.Choose(1, "Orders");
        draft.Choose(1, "Orders");
        Assert.That(draft.Type(1, "Billing"), Is.EqualTo("This question takes only the answers offered."));
        Assert.Throws<ArgumentException>(() => draft.Choose(1, "Billing"), "only labels offered");
        draft.Choose(0, "Dark");
        draft.ShownWhole(0);
        draft.ShownWhole(1);
        var answers = draft.Build();
        Assert.That(answers.Select(answer => answer.Key), Is.EqualTo(new[] { "q0", "q1" }));
        Assert.That(answers[1].Selected, Is.EqualTo(new[] { "Sign in", "Settings" }), "in the order offered");
        Assert.That(answers[1].Text, Is.Null);
    }

    [Test]
    public void NothingIsSentUntilEveryPromptIsAnsweredAndShownWhole()
    {
        var draft = Draft(new AskingWork());
        Assert.That(draft.Problem, Is.EqualTo("Answer every question first: 0 of 2 answered."));
        draft.Choose(0, "Dark");
        Assert.That(draft.Problem, Is.EqualTo("Answer every question first: 1 of 2 answered."));
        draft.Choose(1, "Orders");
        Assert.That(draft.Problem, Is.EqualTo("Read each question to its end first."));
        draft.ShownWhole(0);
        draft.ShownWhole(1);
        Assert.That(draft.Problem, Is.Null);
    }

    [Test]
    public void TypedTextThatCannotBeSentIsRefusedInWords()
    {
        var draft = Draft(new AskingWork());
        Assert.That(draft.Type(0, "Dark\uD800"), Does.StartWith("The typed answer has a character that cannot be sent"));
        Assert.That(draft.Typed(0), Is.Null);
        Assert.That(draft.Type(0, "Dark 🌙"), Is.Null, "whole characters outside the first plane are fine");
        Assert.That(draft.Type(0, "   "), Is.Null);
        Assert.That(draft.Typed(0), Is.Null, "blank clears it");
    }

    [Test]
    public void SendAnswerSendsOnlyOnItsOwnPressWithTheAnswersChosen()
    {
        var work = new AskingWork();
        var steering = new WorkspaceSteering(factory);
        var draft = Draft(work);
        Assert.That(steering.SendAnswer(draft, work.Present()).Message, Is.EqualTo("Answer every question first: 0 of 2 answered."));
        Assert.That(steering.Press(WorkspaceAction.Answer, work.Present()).Step, Is.EqualTo(SteeringStep.Explain), "no answer without the answers");
        draft.Type(0, "Solarized");
        draft.Choose(1, "Sign in");
        draft.ShownWhole(0);
        draft.ShownWhole(1);
        var sent = steering.SendAnswer(draft, work.Present());
        Assert.That(sent.Step, Is.EqualTo(SteeringStep.Send), "low consequence: one deliberate press");
        var command = (ExecutionAnswerQuestionCommand)sent.Command!;
        Assert.That(command.Payload.ExecutionId, Is.EqualTo("e1"));
        Assert.That(command.Payload.QuestionId, Is.EqualTo("question-1"));
        Assert.That(command.Payload.Answers[0].Text, Is.EqualTo("Solarized"));
        Assert.That(command.Payload.Answers[0].Selected, Is.Empty);
        Assert.That(command.Payload.Answers[1].Selected, Is.EqualTo(new[] { "Sign in" }));
    }

    [Test]
    public void APolicyThatAsksForReviewAddsASecondPressWhichFocusLossDrops()
    {
        var work = new AskingWork(policies: new[] { new CommandPolicy { CommandType = CommandType.ExecutionAnswerQuestion, Policy = PolicyCategory.ReviewRequired } });
        var steering = new WorkspaceSteering(factory);
        var draft = Draft(work);
        draft.Choose(0, "Light");
        draft.Choose(1, "Orders");
        draft.ShownWhole(0);
        draft.ShownWhole(1);
        Assert.That(steering.SendAnswer(draft, work.Present()).Step, Is.EqualTo(SteeringStep.Confirm));
        Assert.That(steering.Prompt(work.Present()), Is.EqualTo("Send these answers to the agent?"));
        Assert.That(steering.FocusLeft().Message, Is.EqualTo(WorkspaceText.ConfirmAfresh));
        Assert.That(steering.Confirm(work.Present()).Step, Is.EqualTo(SteeringStep.None), "the press that returns focus sends nothing");
        Assert.That(draft.IsChosen(0, "Light"), Is.True, "the answers chosen are kept");
        Assert.That(steering.SendAnswer(draft, work.Present()).Step, Is.EqualTo(SteeringStep.Confirm));
        Assert.That(steering.Confirm(work.Present()).Command, Is.TypeOf<ExecutionAnswerQuestionCommand>());
    }

    [Test]
    public void AnAnswerForAQuestionNoLongerShownIsNotSent()
    {
        var work = new AskingWork();
        var draft = Draft(work);
        draft.Choose(0, "Light");
        draft.Choose(1, "Orders");
        draft.ShownWhole(0);
        draft.ShownWhole(1);
        work.Change(execution => execution.PendingQuestions.Clear());
        var outcome = new WorkspaceSteering(factory).SendAnswer(draft, work.Present());
        Assert.That(outcome.Step, Is.EqualTo(SteeringStep.Explain));
        Assert.That(outcome.Command, Is.Null);
    }

    [TestCase(true, false, "The agent asks for something secret. Halcyonic can't send it; stop the turn to go on.")]
    [TestCase(false, true, "This question was too long to show whole, so Halcyonic can't answer it. Stop the turn to go on.")]
    [TestCase(false, false, "Halcyonic can't send an answer to this question. Stop the turn to go on.")]
    public void AQuestionHalcyonicCannotAnswerShowsTheWayOn(bool secret, bool cut, string words)
    {
        var question = AskingWork.Scripted();
        question.Answerable = false;
        question.Prompts[0].Secret = secret;
        if (cut) question.Prompts[0].Text = "Which colour scheme [truncated]";
        var work = new AskingWork(question);
        var workspace = work.Present();
        Assert.That(workspace.Actions, Does.Not.Contain(WorkspaceAction.Answer));
        Assert.That(workspace.Actions, Does.Contain(WorkspaceAction.Interrupt), "stopping the turn stays available");
        Assert.That(WorkspaceText.CannotAnswer(question), Is.EqualTo(words));
        Assert.That(new QuestionDraft("e1", question).Problem, Is.EqualTo(words));
    }

    [Test]
    public void ThreeQuestionsShownSayMoreMayFollow()
    {
        var work = new AskingWork();
        work.Change(execution =>
        {
            for (var i = 2; i <= 3; i++)
            {
                var more = AskingWork.Scripted();
                more.QuestionId = "question-" + i;
                execution.PendingQuestions.Add(more);
            }
        });
        Assert.That(WorkspaceText.QuestionLead(work.Present()), Is.EqualTo("First of 3 questions shown; more may follow."));
    }

    [Test]
    public void AnswerFeedbackSaysSentUntilTheRuntimeTakesItAndNeverClaimsAnUnconfirmedOne()
    {
        CommandView Command(CommandStatus status, CommandFailure? failure = null, CommandRejection? rejection = null) => new()
        {
            CommandId = "c1", CommandType = CommandType.ExecutionAnswerQuestion, Status = status, ExecutionId = "e1",
            IssuedAt = Samples.Time, Failure = failure, Rejection = rejection,
        };
        Assert.That(WorkspacePresenter.Feedback(Command(CommandStatus.Accepted)).Text, Is.EqualTo("Sent, waiting for the result…"));
        Assert.That(WorkspacePresenter.Feedback(Command(CommandStatus.Completed)).Text, Is.EqualTo("The runtime took the answer"));
        var unconfirmed = new CommandFailure { Code = "question_unconfirmed", Message = "Codex did not confirm.", Effect = FailureEffect.Unknown };
        Assert.That(WorkspacePresenter.Feedback(Command(CommandStatus.Failed, unconfirmed)).Text, Is.EqualTo(WorkspaceText.AnswerNotConfirmed));
        var gone = new CommandRejection { Code = RejectionCode.QuestionNotFound, Message = "Question question-1 is not pending." };
        Assert.That(WorkspacePresenter.Feedback(Command(CommandStatus.Rejected, rejection: gone)).Text, Is.EqualTo("Refused: the agent no longer waits for this answer."));
    }

    [Test]
    public void FocusGoingAwayKeepsTheChoicesAndSendsNothing()
    {
        var work = new AskingWork();
        var steering = new WorkspaceSteering(factory);
        var draft = Draft(work);
        draft.Choose(0, "Dark");
        Assert.That(steering.FocusLeft().Step, Is.EqualTo(SteeringStep.None), "nothing was armed");
        Assert.That(draft.IsChosen(0, "Dark"), Is.True);
    }

    [Test]
    public void TheAnswerLineNamesTheWholeQuestionWhicheverPromptShows()
    {
        var question = AskingWork.Scripted();
        Assert.That(CharacterPresenter.AsksYou(question), Is.EqualTo("Asks you 2 questions: Colour scheme; Pages"));
        question.Prompts[1].Header = null;
        Assert.That(CharacterPresenter.AsksYou(question), Is.EqualTo("Asks you 2 questions."), "no header named unless every prompt has one");
        question.Prompts.RemoveAt(1);
        Assert.That(CharacterPresenter.AsksYou(question), Is.EqualTo("Asks you: Colour scheme: Which colour scheme should the dashboard use?"));
        var work = new AskingWork();
        Assert.That(WorkspaceText.Answer(work.Present()), Is.EqualTo(new[] { "Asks you 2 questions: Colour scheme; Pages" }));
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The agent's question on a file's Waiting page (ADR 0026, lane V's call of 2026-10-02): one prompt at
/// a time, its answers paged by a row, the person's answers the only page a question of several
/// prompts sends from, and nothing sent that is not in view or was not read whole.
/// </summary>
public class FileQuestionTests
{
    private readonly CommandFactory factory = new(Samples.Client);

    private static readonly AnswerRoom Room = new(4);

    private MenuFrame Screen(WorkspacePresentation workspace, FileScreen screen, WorkspaceSteering? steering = null) =>
        FileScreens.Screen(workspace, steering ?? new WorkspaceSteering(factory), screen, Room);

    /// <summary>Every prompt's question in one row and every answer in one.</summary>
    private static IReadOnlyList<PromptMeasure> Short(QuestionView question) =>
        question.Prompts.Select(prompt => new PromptMeasure(1, prompt.Options.Select(_ => 1).ToList())).ToList();

    private static QuestionView OnePrompt(params string[] options) => new()
    {
        QuestionId = "question-2",
        Answerable = true,
        AskedAt = Samples.Time,
        Prompts = new List<QuestionPrompt>
        {
            new()
            {
                Key = "q0", Header = "Database", Text = "Which database should the service use for its orders, given the load you expect next quarter?",
                Options = options.Select(label => new QuestionOption { Label = label }).ToList(),
                Multiple = false, FreeText = true, Secret = false,
            },
        },
    };

    private static (AskingWork Work, WorkspacePresentation Workspace, FileScreen Screen, QuestionDraft Draft) Asking(QuestionView? question = null,
        IReadOnlyList<PromptMeasure>? measured = null, int rows = 8, bool speak = false)
    {
        var work = new AskingWork(question);
        var workspace = FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer, WorkspaceAction.Interrupt);
        var screen = new FileScreen { Speak = speak };
        var draft = new QuestionDraft("e1", work.Question);
        screen.ReadQuestion(draft, measured ?? Short(work.Question), rows);
        return (work, workspace, screen, draft);
    }

    [Test]
    public void OnePromptAtATimeItsQuestionQuotedItsAnswersAsRowsThenTypeMyAnswerThenTheNextQuestion()
    {
        var (_, workspace, screen, draft) = Asking(speak: true);
        var frame = Screen(workspace, screen);
        Assert.That(frame.Lines.Select(line => line.Words), Is.EqualTo(new[]
        {
            "“Which colour scheme should the dashboard use?”",
            "Light · Dark text on a light background",
            "Dark · Light text on a dark background",
            FileScreens.TypeMyAnswer,
            "Next question, 2 of 2",
        }));
        Assert.That(frame.Lines.Where(line => line.Choice).Select(line => line.Action).Distinct(), Is.EqualTo(new[] { FileScreens.Choose, FileScreens.TypeAnswer }));
        Assert.That(frame.Lines.Any(line => line.Chosen), Is.False, "nothing is chosen until the person chooses");
        Assert.That(FileScreensTests.Slots(frame.Footer), Is.EqualTo(new[] { Footer.Close, FileScreens.Stop, null, FileScreens.SpeakAnswer, FileScreens.SendAnswer }));
        Assert.That(FileScreens.SpeakAnswer, Is.Not.EqualTo(FileScreens.HoldToTalk), "a spoken answer is never raised as an instruction");
        var send = frame.Footer[PromptSlot.FarRight]!;
        Assert.That((send.Available, send.Reason), Is.EqualTo((false, FileScreens.SendFromYourAnswers)),
            "a question of several prompts sends only from the person's answers");
        Assert.That(frame.Reason, Is.EqualTo(FileScreens.SendFromYourAnswers), "the reason is the page's last line");

        draft.Choose(0, "Dark");
        var chosen = Screen(workspace, screen);
        Assert.That(chosen.Lines.Where(line => line.Chosen).Select(line => line.Key), Is.EqualTo(new[] { "1" }), "choosing lights the row, and sends nothing");
    }

    [Test]
    public void AQuestionOfSeveralPromptsSendsOnlyFromYourAnswersOnceEveryPromptIsAnswered()
    {
        var (_, workspace, screen, draft) = Asking();
        Screen(workspace, screen);
        draft.Choose(0, "Dark");
        screen.Question.NextQuestion();
        var second = Screen(workspace, screen);
        Assert.That(second.Lines[0].Words, Is.EqualTo("“Which pages should change?”"));
        Assert.That(second.Lines.Last().Words, Is.EqualTo(FileScreens.YourAnswers));
        Assert.That(draft.IsChosen(0, "Dark"), Is.True, "an earlier prompt's choice is kept");

        screen.Question.NextQuestion();
        var answers = Screen(workspace, screen);
        Assert.That(answers.Lines.Select(line => (line.Words, line.Fact, line.Action, line.Key)), Is.EqualTo(new[]
        {
            ("Colour scheme", "Dark", FileScreens.GoToQuestion, "0"),
            ("Pages", "Not answered", FileScreens.GoToQuestion, "1"),
        }));
        Assert.That(answers.Footer[PromptSlot.FarRight]!.Reason, Is.EqualTo("Answer every question first: 1 of 2 answered."));

        screen.Question.GoTo(1);
        draft.Choose(1, "Orders");
        Screen(workspace, screen);
        screen.Question.NextQuestion();
        var ready = Screen(workspace, screen);
        Assert.That(ready.Footer[PromptSlot.FarRight]!.Available, Is.True);
        Assert.That(ready.Lines[1].Fact, Is.EqualTo("Orders"));
        var sent = new WorkspaceSteering(factory).SendAnswer(draft, workspace);
        Assert.That(sent.Step, Is.EqualTo(SteeringStep.Send));
        var answered = ((ExecutionAnswerQuestionCommand)sent.Command!).Payload.Answers;
        Assert.That(answered.Select(answer => string.Join("+", answer.Selected)), Is.EqualTo(new[] { "Dark", "Orders" }), "exactly what the page listed");
    }

    [Test]
    public void TurningAPromptsAnswersClearsOnlyItsChoiceKeepsTheTypedAnswerAndLeavesNoAnswerOut()
    {
        var labels = new[] { "Postgres", "SQLite", "MySQL", "DynamoDB", "Redis" };
        var question = OnePrompt(labels);
        var measured = new[] { new PromptMeasure(1, labels.Select(_ => 2).ToList()) };
        // 7 rows: the question 1, Type my answer 1, the reason 1, the row for more answers 1, leaves 3, one 2-row answer a page.
        var (_, workspace, screen, draft) = Asking(question, measured, rows: 7);
        Assert.That(screen.Question.Pages, Is.EqualTo(5));
        var seen = new List<int>();
        for (var page = 0; page < screen.Question.Pages; page++)
        {
            var frame = Screen(workspace, screen);
            seen.AddRange(screen.Question.Answers);
            Assert.That(frame.Lines.Sum(line => line.Rows) + 1, Is.LessThanOrEqualTo(7), "the page and its reason line fit its rows");
            Assert.That(frame.Lines[frame.Lines.Count - 2].Words, Is.EqualTo(FileScreens.TypeMyAnswer), "Type my answer stands last among the answers");
            Assert.That(frame.Lines.Last().Words, Is.EqualTo(FileScreens.MoreAnswersWords(page, 5)));
            screen.Question.MoreAnswers();
        }
        Assert.That(seen, Is.EqualTo(Enumerable.Range(0, labels.Length)), "every answer the agent offered, in its order");
        Assert.That(FileScreens.MoreAnswersWords(4, 5), Is.EqualTo("First answers, 1 of 5"));

        draft.Choose(0, "Postgres");
        draft.Type(0, null);
        screen.Question.MoreAnswers();
        Assert.That(draft.IsChosen(0, "Postgres"), Is.False, "turning the page clears what was chosen on it");

        draft.Type(0, "CockroachDB");
        screen.Question.MoreAnswers();
        Assert.That(draft.Typed(0), Is.EqualTo("CockroachDB"), "the typed answer's row is on every page, so it stays");
        var typed = Screen(workspace, screen).Lines.Single(line => line.Action == FileScreens.TypeAnswer);
        Assert.That((typed.Words, typed.Chosen, typed.WordsAreData), Is.EqualTo(("Your answer: “CockroachDB”", true, true)));
    }

    [Test]
    public void ACutQuestionCountsAsReadOnlyOnceItsSidePanelHasShownAllOfIt()
    {
        var question = OnePrompt("Postgres", "SQLite");
        var measured = new[] { new PromptMeasure(4, new[] { 1, 1 }) };
        var (_, workspace, screen, draft) = Asking(question, measured);
        var frame = Screen(workspace, screen);
        var head = frame.Lines[0];
        Assert.That((head.Rows, head.Opens, head.Action, head.Key), Is.EqualTo((2, true, FileScreens.Open, FileScreens.QuestionKey)),
            "a question longer than two rows is cut, and opens all of it beside the page");
        Assert.That(head.FromRow, Is.Null.Or.EqualTo(0));
        draft.Choose(0, "Postgres");
        var unread = Screen(workspace, screen).Footer[PromptSlot.FarRight]!;
        Assert.That((unread.Available, unread.Reason), Is.EqualTo((false, FileScreens.OpenToRead)));
        Assert.That(new WorkspaceSteering(factory).SendAnswer(draft, workspace).Step, Is.EqualTo(SteeringStep.Explain), "the send rule itself refuses too");

        screen.Chosen = FileScreens.QuestionKey;
        var opened = Screen(workspace, screen);
        Assert.That(opened.Side!.Lines.Single().Rows, Is.EqualTo(4), "the side panel shows all of it");
        Assert.That(opened.Side.Source, Is.EqualTo(FileScreens.AgentSource));
        Assert.That(opened.Footer[PromptSlot.FarRight]!.Available, Is.True, "read whole, it can be sent from its own page");
        Assert.That(new WorkspaceSteering(factory).SendAnswer(draft, workspace).Step, Is.EqualTo(SteeringStep.Send));
    }

    [Test]
    public void AQuestionOfOnePromptSendsFromItsOwnPageWithNoYourAnswers()
    {
        var (_, workspace, screen, draft) = Asking(OnePrompt("Postgres", "SQLite"));
        var frame = Screen(workspace, screen);
        Assert.That(frame.Lines.Select(line => line.Words), Has.None.EqualTo(FileScreens.YourAnswers));
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Reason, Is.EqualTo("Choose or type an answer first."));
        draft.Choose(0, "SQLite");
        Assert.That(Screen(workspace, screen).Footer[PromptSlot.FarRight]!.Available, Is.True);
        screen.Question.NextQuestion();
        Assert.That(screen.Question.Reviewing, Is.False);
    }

    [Test]
    public void AQuestionAskingForASecretCannotBeAnsweredHereWhateverItsAdapterSays()
    {
        var question = AskingWork.Scripted();
        question.Prompts[0].Secret = true;
        Assert.That(question.Answerable, Is.True, "the adapter says it can be answered");
        var work = new AskingWork(question);
        Assert.That(work.Present().Actions, Has.None.EqualTo(WorkspaceAction.Answer), "nothing offers to send it");
        var screen = new FileScreen { Speak = true };
        var draft = new QuestionDraft("e1", work.Question);
        screen.ReadQuestion(draft, Short(work.Question), 8);
        var frame = Screen(work.Present(), screen);
        Assert.That(FileScreensTests.Slots(frame.Footer).Skip(3), Is.EqualTo(new string?[] { null, null }), "no Hold to talk and no Send answer");
        Assert.That(frame.Lines.Any(line => line.Choice), Is.False, "no answers to choose or type");
        Assert.That(frame.Lines.Select(line => line.Words), Has.Some.EqualTo(WorkspaceText.CannotAnswer(question)));
        draft.Choose(1, "Orders");
        Assert.That(draft.Problem, Is.EqualTo(WorkspaceText.CannotAnswer(question)), "and the draft refuses to build");
    }

    [Test]
    public void WhileAnAnswerSentMayStillTakeEffectSendAnswerKeepsItsPlaceTakingNoPress()
    {
        var work = new AskingWork();
        var draft = Answered(work);
        var submissions = new CommandSubmissions();
        var command = new WorkspaceSteering(factory).SendAnswer(draft, work.Present()).Command!;
        _ = submissions.SubmitAsync(_ => new TaskCompletionSource<CommandAckMessage>().Task, command, "e1");
        var workspace = WorkspacePresenter.Present(work.Workstream, work.State, new ActivityLog(), true, submissions);
        Assert.That(workspace.AnswerInFlight, Is.True);
        var screen = new FileScreen();
        screen.ReadQuestion(draft, Short(work.Question), 8);
        var frame = Screen(workspace, screen);
        var sent = frame.Footer[PromptSlot.FarRight]!;
        Assert.That((sent.Words, sent.Available, sent.DrawnAsMain), Is.EqualTo((WorkspaceText.Sent, false, false)));
        Assert.That(frame.Reason, Is.EqualTo(FileScreens.SentWaiting));
    }

    private static QuestionDraft Answered(AskingWork work)
    {
        var draft = new QuestionDraft("e1", work.Question);
        draft.Choose(0, "Dark");
        draft.Choose(1, "Orders");
        for (var prompt = 0; prompt < draft.Prompts.Count; prompt++) draft.ShownWhole(prompt);
        return draft;
    }

    [Test]
    public void SendingAnswersThePolicyReviewsAsksWithTheAnswersAndCancelInSendAnswersPlace()
    {
        var policies = new[] { new CommandPolicy { CommandType = CommandType.ExecutionAnswerQuestion, Policy = PolicyCategory.ReviewRequired } };
        var work = new AskingWork(policies: policies);
        var workspace = FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer);
        var steering = new WorkspaceSteering(factory);
        var screen = new FileScreen();
        var draft = Answered(work);
        screen.ReadQuestion(draft, Short(work.Question), 8);
        screen.Question.NextQuestion();
        screen.Question.NextQuestion();
        Assert.That(steering.SendAnswer(draft, workspace).Step, Is.EqualTo(SteeringStep.Confirm));
        var frame = Screen(workspace, screen, steering);
        Assert.That(frame.Lines.Select(line => line.Words), Is.EqualTo(new[] { "Question 1 of 2 · Colour scheme: Dark", "Question 2 of 2 · Pages: Orders", "Send these answers?" }));
        Assert.That(FileScreensTests.Slots(frame.Footer), Is.EqualTo(new[] { Footer.Close, null, FileScreens.Yes, null, FileScreens.Cancel }));
        Assert.That(steering.Confirm(workspace).Step, Is.EqualTo(SteeringStep.Send));
        Assert.That(steering.Confirm(workspace).Step, Is.EqualTo(SteeringStep.None), "Yes sends once");
    }
}

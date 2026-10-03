using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The agent's question on a file's Waiting page (ADR 0026, lane V's calls of 2026-10-02): one prompt
/// at a time, a long question first on pages of its own, its answers paged by a row, the person's
/// answers the only page a question of several prompts sends from, and nothing sent that is not in
/// view or was not drawn whole.
/// </summary>
public class FileQuestionTests
{
    private readonly CommandFactory factory = new(Samples.Client);

    private static readonly AnswerRoom Room = new(4);

    private DateTimeOffset clock = DateTimeOffset.Parse("2026-10-02T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    private MenuFrame Screen(WorkspacePresentation workspace, FileScreen screen, WorkspaceSteering? steering = null) =>
        FileScreens.Screen(workspace, steering ?? new WorkspaceSteering(factory), screen, Room);

    /// <summary>The view draws the page showing, now.</summary>
    private void Draw(FileScreen screen) => screen.Question.Drawn(clock);

    /// <summary>The view draws the page showing, and a second later the person presses a row on it.</summary>
    private DateTimeOffset Later(FileScreen screen)
    {
        Draw(screen);
        clock += TimeSpan.FromSeconds(1);
        return clock;
    }

    /// <summary>Every prompt's question in one row and every answer in one.</summary>
    private static IReadOnlyList<PromptMeasure> Short(QuestionView question) =>
        question.Prompts.Select(prompt => new PromptMeasure(1, prompt.Options.Select(_ => 1).ToList(), prompt.Options.Select(_ => 1).ToList())).ToList();

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
        screen.ReadQuestion(draft, measured ?? Short(work.Question), new RowBudget(rows), new RowBudget(3));
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
        Assert.That(FileScreensTests.Slots(frame.Footer), Is.EqualTo(new[] { Footer.Close, null, null, FileScreens.SpeakAnswer, FileScreens.SendAnswer }),
            "Stop stands on Activity while the question can be answered here");
        Assert.That(FileScreens.SpeakAnswer, Is.Not.EqualTo(FileScreens.HoldToTalk), "a spoken answer is never raised as an instruction");
        var send = frame.Footer[PromptSlot.FarRight]!;
        Assert.That((send.Available, send.Reason), Is.EqualTo((false, FileScreens.SendFromYourAnswers)),
            "a question of several prompts sends only from the person's answers");
        Assert.That(frame.Reason, Is.EqualTo(FileScreens.SendFromYourAnswers), "the reason is the page's last line");

        screen.Question.Choose(1);
        var chosen = Screen(workspace, screen);
        Assert.That(chosen.Lines.Where(line => line.Chosen).Select(line => line.Key), Is.EqualTo(new[] { "1" }), "choosing lights the row, and sends nothing");
        Assert.That(draft.IsChosen(0, "Dark"), Is.True);
    }

    [Test]
    public void AQuestionOfSeveralPromptsSendsOnlyFromYourAnswersEachWholeOnceEveryPageWasDrawn()
    {
        var (_, workspace, screen, draft) = Asking();
        Screen(workspace, screen);
        screen.Question.Choose(1);
        screen.Question.NextQuestion(Later(screen));
        var second = Screen(workspace, screen);
        Assert.That(second.Lines[0].Words, Is.EqualTo("“Which pages should change?”"));
        Assert.That(second.Lines.Last().Words, Is.EqualTo(FileScreens.YourAnswers));
        Assert.That(draft.IsChosen(0, "Dark"), Is.True, "an earlier prompt's choice is kept");

        screen.Question.NextQuestion(Later(screen));
        screen.Question.MeasureReview(new[] { 1, 1 });
        var answers = Screen(workspace, screen);
        Assert.That(answers.Lines.Select(line => (line.Words, line.Action, line.Key, line.Rows)), Is.EqualTo(new[]
        {
            ("Colour scheme: Dark", FileScreens.GoToQuestion, "0", 1),
            ("Pages: Not answered", FileScreens.GoToQuestion, "1", 1),
        }), "each answer whole, by its prompt's name");
        Assert.That(answers.Footer[PromptSlot.FarRight]!.Reason, Is.EqualTo("Answer every question first: 1 of 2 answered."));

        screen.Question.GoTo(1);
        screen.Question.Choose(1);
        screen.Question.NextQuestion(Later(screen));
        screen.Question.MeasureReview(new[] { 1, 1 });
        var unread = Screen(workspace, screen);
        Assert.That(unread.Footer[PromptSlot.FarRight]!.Reason, Is.EqualTo(FileScreens.ReadYourAnswers), "until the view has drawn them");
        Draw(screen);
        var ready = Screen(workspace, screen);
        Assert.That(ready.Footer[PromptSlot.FarRight]!.Available, Is.True);
        Assert.That(ready.Lines[1].Words, Is.EqualTo("Pages: Orders"));
        var sent = new WorkspaceSteering(factory).SendAnswer(draft, workspace);
        var answered = ((ExecutionAnswerQuestionCommand)sent.Command!).Payload.Answers;
        Assert.That(answered.Select(answer => string.Join("+", answer.Selected)), Is.EqualTo(new[] { "Dark", "Orders" }), "exactly what the page listed");
    }

    [Test]
    public void YourAnswersPageWhereTheyDontFitAndSendOnlyOnceEveryPageWasDrawn()
    {
        var (_, workspace, screen, draft) = Asking(rows: 6);
        for (var prompt = 0; prompt < 2; prompt++)
        {
            Screen(workspace, screen);
            screen.Question.Choose(prompt == 0 ? 1 : 0);
            screen.Question.NextQuestion(Later(screen));
        }
        screen.Question.MeasureReview(new[] { 3, 3 });
        var first = Screen(workspace, screen);
        Assert.That(first.Lines.Select(line => line.Words), Is.EqualTo(new[] { "Colour scheme: Dark", "Your answers, 2 of 2" }));
        Assert.That(first.Lines[0].Rows, Is.EqualTo(3), "an answer shows whole, in the rows it takes");
        Draw(screen);
        Assert.That(Screen(workspace, screen).Footer[PromptSlot.FarRight]!.Reason, Is.EqualTo(FileScreens.ReadYourAnswers));
        screen.Question.MoreAnswers(Later(screen));
        var last = Screen(workspace, screen);
        Assert.That(last.Lines.Select(line => line.Words), Is.EqualTo(new[] { "Pages: Sign in", "Your answers, 1 of 2" }));
        Assert.That(draft.IsChosen(1, "Sign in"), Is.True, "turning the person's answers clears nothing");
        Draw(screen);
        Assert.That(Screen(workspace, screen).Footer[PromptSlot.FarRight]!.Available, Is.True);
    }

    [Test]
    public void TurningAPromptsAnswersClearsOnlyItsChoiceKeepsTheTypedAnswerAndLeavesNoAnswerOut()
    {
        var labels = new[] { "Postgres", "SQLite", "MySQL", "DynamoDB", "Redis" };
        var question = OnePrompt(labels);
        var measured = new[] { new PromptMeasure(1, labels.Select(_ => 2).ToList(), labels.Select(_ => 2).ToList()) };
        // 5 rows: the question 1, Type my answer beside the row for more answers 1, no reason while only a
        // choice is wanted, leaves 3, one 2-row answer a page.
        var (_, workspace, screen, draft) = Asking(question, measured, rows: 5);
        Assert.That(screen.Question.Pages, Is.EqualTo(5));
        var seen = new List<int>();
        for (var page = 0; page < screen.Question.Pages; page++)
        {
            var frame = Screen(workspace, screen);
            seen.AddRange(screen.Question.Answers);
            Assert.That(Height(frame, new RowBudget(5)), Is.LessThanOrEqualTo(5), "the page fits its rows, Type my answer beside the row for more answers");
            Assert.That(frame.Lines[frame.Lines.Count - 2].BesideNext, Is.True);
            Assert.That(frame.Lines[frame.Lines.Count - 2].Words, Is.EqualTo(FileScreens.TypeMyAnswer), "Type my answer stands last among the answers");
            Assert.That(frame.Lines.Last().Words, Is.EqualTo(FileScreens.MoreAnswersWords(page, 5)));
            screen.Question.MoreAnswers(Later(screen));
        }
        Assert.That(seen, Is.EqualTo(Enumerable.Range(0, labels.Length)), "every answer the agent offered, in its order");
        Assert.That(FileScreens.MoreAnswersWords(4, 5), Is.EqualTo("First answers, 1 of 5"));

        screen.Question.Choose(0);
        screen.Question.MoreAnswers(Later(screen));
        Assert.That(draft.IsChosen(0, "Postgres"), Is.False, "turning the page clears what was chosen on it");

        draft.Type(0, "CockroachDB");
        screen.Question.MoreAnswers(Later(screen));
        Assert.That(draft.Typed(0), Is.EqualTo("CockroachDB"), "the typed answer's row is on every page, so it stays");
        var typed = Screen(workspace, screen).Lines.Single(line => line.Action == FileScreens.TypeAnswer);
        Assert.That((typed.Words, typed.Chosen, typed.WordsAreData), Is.EqualTo(("Your answer: “CockroachDB”", true, true)));
    }

    /// <summary>A page's height as its budget prices it: words, targets, the gaps between them, and its reason.</summary>
    internal static float Height(MenuFrame frame, PageBudget budget)
    {
        var total = 0f;
        PageLine? before = null;
        foreach (var line in frame.Lines)
        {
            // A line beside the one before it shares that one's row and target.
            if (before?.BesideNext == true) continue;
            var target = line.Action != null;
            // As the view lays them: 12 mm between two targets, a grid step between any other two.
            if (before != null) total += target && before.Action != null ? budget.TargetGap : budget.LineGap;
            total += target ? budget.Target(line.Rows) : budget.Words(line.Rows);
            before = line;
        }
        return total + (frame.Reason != null ? budget.Reason : 0f);
    }

    [Test]
    public void OnAHeadsetPageFourAnswersOfTwoRowsPackByHeightWithinTheField()
    {
        foreach (var text in new[] { TextSize.Standard, TextSize.Larger })
        {
            var budget = HeightBudget.Of(text, subjectRows: 1);
            var labels = new[] { "15 minutes", "1 hour", "Until reset", "Grows each time" };
            var measured = new[] { new PromptMeasure(1, labels.Select(_ => 2).ToList(), labels.Select(_ => 3).ToList()) };
            var (_, workspace, screen, _) = Asking(OnePrompt(labels), measured);
            screen.ReadQuestion(screen.Question.Draft!, measured, budget, budget);
            // Where the question can't share a page with an answer, it shows first on its own.
            while (screen.Question.QuestionPart != null)
            {
                var part = Screen(workspace, screen);
                Assert.That(Height(part, budget), Is.LessThanOrEqualTo(budget.Room + 1e-5f), text + ": the question's own page");
                screen.Question.NextPart(Later(screen));
            }
            var seen = new List<int>();
            var pages = screen.Question.Pages;
            for (var page = 0; page < pages; page++)
            {
                var frame = Screen(workspace, screen);
                seen.AddRange(screen.Question.Answers);
                Assert.That(Height(frame, budget), Is.LessThanOrEqualTo(budget.Room + 1e-5f), text + ", page " + page);
                Assert.That(screen.Question.Answers, Is.Not.Empty, "every page holds an answer");
                screen.Question.MoreAnswers(Later(screen));
            }
            Assert.That(seen, Is.EqualTo(Enumerable.Range(0, labels.Length)), text + ": every answer, in the agent's order");
        }
    }

    [Test]
    public void AtTheStandardSizeAQuestionAndTwoAnswersOfTwoRowsShareAPageForTheFileAlone()
    {
        var budget = HeightBudget.Of(TextSize.Standard, subjectRows: 1);
        var labels = new[] { "15 minutes", "1 hour", "Until reset", "Grows each time" };
        var measured = new[] { new PromptMeasure(1, labels.Select(_ => 2).ToList(), labels.Select(_ => 3).ToList()) };
        var (_, workspace, screen, _) = Asking(OnePrompt(labels), measured);
        screen.ReadQuestion(screen.Question.Draft!, measured, budget, budget);
        Assert.That(screen.Question.QuestionPart, Is.Null, "a question of one row heads its answers");
        Assert.That(screen.Question.Answers, Is.EqualTo(new[] { 0, 1 }), "the question and its answers are one group, 8 dp apart, so two answers fit");
        Assert.That(Height(Screen(workspace, screen), budget), Is.LessThanOrEqualTo(budget.Room + 1e-5f));
    }

    [Test]
    public void ATwoRowQuestionTakesItsOwnPageWhereThatLeavesFewerPagesInAllAndStaysWithItsAnswersOnATie()
    {
        var budget = HeightBudget.Of(TextSize.Standard, subjectRows: 1);
        var four = new[] { "15 minutes", "1 hour", "Until reset", "Grows each time" };
        var measured = new[] { new PromptMeasure(2, four.Select(_ => 2).ToList(), four.Select(_ => 3).ToList()) };
        var (_, workspace, screen, _) = Asking(OnePrompt(four), measured);
        screen.ReadQuestion(screen.Question.Draft!, measured, budget, budget);
        Assert.That((screen.Question.QuestionParts(0), screen.Question.Pages), Is.EqualTo((1, 2)),
            "a question page, then two answers a page under its first line: 3 pages rather than 4");
        Assert.That(screen.Question.HeadRows(0), Is.EqualTo(1));
        var shown = 0;
        while (screen.Question.QuestionPart != null)
        {
            Assert.That(Height(Screen(workspace, screen), budget), Is.LessThanOrEqualTo(budget.Room + 1e-5f), "the question's own page");
            shown++;
            screen.Question.NextPart(Later(screen));
        }
        for (var page = 0; page < screen.Question.Pages; page++)
        {
            var frame = Screen(workspace, screen);
            Assert.That(Height(frame, budget), Is.LessThanOrEqualTo(budget.Room + 1e-5f), "page " + page);
            Assert.That(screen.Question.Answers, Has.Count.EqualTo(2));
            shown++;
            screen.Question.MoreAnswers(Later(screen));
        }
        Assert.That(shown, Is.EqualTo(3));

        var three = four.Take(3).ToArray();
        var tied = new[] { new PromptMeasure(2, three.Select(_ => 2).ToList(), three.Select(_ => 3).ToList()) };
        var (_, _, beside, _) = Asking(OnePrompt(three), tied);
        beside.ReadQuestion(beside.Question.Draft!, tied, budget, budget);
        Assert.That((beside.Question.QuestionParts(0), beside.Question.HeadRows(0), beside.Question.Pages), Is.EqualTo((0, 2, 3)),
            "3 pages either way, so the question stays whole beside its answers");
    }

    [Test]
    public void AChoiceIsTakenOnlyFromThePageInViewAndALayoutAnewLandsOnItOrClearsIt()
    {
        var labels = new[] { "Postgres", "SQLite", "MySQL", "DynamoDB", "Redis" };
        var question = OnePrompt(labels);
        var (_, workspace, screen, draft) = Asking(question, new[] { new PromptMeasure(1, labels.Select(_ => 1).ToList(), labels.Select(_ => 1).ToList()) }, rows: 4);
        Assert.That(screen.Question.Answers, Is.EqualTo(new[] { 0, 1 }));
        screen.Question.Choose(4);
        Assert.That(draft.IsChosen(0, "Redis"), Is.False, "an answer not on the page in view can't be chosen");

        Screen(workspace, screen);
        screen.Question.MoreAnswers(Later(screen));
        screen.Question.MoreAnswers(Later(screen));
        Assert.That(screen.Question.Answers, Is.EqualTo(new[] { 4 }));
        screen.Question.Choose(4);
        Assert.That(draft.IsChosen(0, "Redis"), Is.True);

        // The text grows a size: a row fewer a page.
        screen.ReadQuestion(draft, new[] { new PromptMeasure(1, labels.Select(_ => 1).ToList(), labels.Select(_ => 1).ToList()) }, new RowBudget(3), new RowBudget(3));
        Assert.That(screen.Question.Answers, Does.Contain(4), "laid out anew, the page shows what was chosen");
        Assert.That(FileScreens.WhySendWaits(screen), Is.Null.Or.Not.EqualTo(FileScreens.ReadTheAnswer));
        var frame = Screen(workspace, screen);
        Assert.That(frame.Lines.Where(line => line.Chosen).Select(line => line.Words), Is.EqualTo(new[] { "Redis" }), "the chosen answer is in view");
    }

    [Test]
    public void EveryPromptAndEveryAnswerIsMeasuredOrNothingIsShown()
    {
        var work = new AskingWork();
        var screen = new FileScreen();
        var draft = new QuestionDraft("e1", work.Question);
        Assert.Throws<ArgumentException>(() => screen.ReadQuestion(draft, Array.Empty<PromptMeasure>(), new RowBudget(8), new RowBudget(3)), "no measures: no prompt counts as read");
        Assert.Throws<ArgumentException>(() => screen.ReadQuestion(draft, new[] { new PromptMeasure(1, new[] { 1, 1 }, new[] { 1, 1 }) }, new RowBudget(8), new RowBudget(3)), "one prompt of two");
        Assert.Throws<ArgumentException>(() => screen.ReadQuestion(draft, new[] { new PromptMeasure(1, new[] { 1 }, new[] { 1 }), new PromptMeasure(1, new[] { 1, 1, 1 }, new[] { 1, 1, 1 }) }, new RowBudget(8), new RowBudget(3)),
            "an answer unmeasured");
        Assert.That(draft.WasShownWhole(0), Is.False);
    }

    [Test]
    public void ALongQuestionShowsFirstInPartsAndIsReadOnlyOnceTheViewDrewEveryPart()
    {
        var question = OnePrompt("Postgres", "SQLite");
        var measured = new[] { new PromptMeasure(9, new[] { 1, 1 }, new[] { 1, 1 }) };
        // 6 rows: a part holds 4, beside its row and the reason; 9 rows take 3 parts.
        var (_, workspace, screen, draft) = Asking(question, measured, rows: 6);
        Assert.That(screen.Question.QuestionPart, Is.EqualTo(0));
        var first = Screen(workspace, screen);
        Assert.That((first.Lines[0].Rows, first.Lines[0].FromRow), Is.EqualTo((4, (int?)0)));
        Assert.That((first.Lines[1].Words, first.Lines[1].Action, first.Lines[1].Key), Is.EqualTo(("Next part, 2 of 3", FileScreens.NextPart, FileScreens.QuestionKey)));
        Assert.That(first.Side, Is.Null, "the question has pages of its own, not a side panel");
        Assert.That(first.Lines.Any(line => line.Choice), Is.False, "nothing to answer before it is read");

        screen.Question.NextPart(clock);
        Assert.That(screen.Question.QuestionPart, Is.EqualTo(0), "the next part waits until this one has been drawn");
        Draw(screen);
        screen.Question.NextPart(clock + TimeSpan.FromSeconds(0.2));
        Assert.That(screen.Question.QuestionPart, Is.EqualTo(0), "and has stood 0.4 seconds");
        screen.Question.NextPart(Later(screen));
        Assert.That(Screen(workspace, screen).Lines[0].FromRow, Is.EqualTo(4));
        screen.Question.NextPart(Later(screen));
        var last = Screen(workspace, screen);
        Assert.That((last.Lines[0].FromRow, last.Lines[1].Words), Is.EqualTo(((int?)8, FileScreens.OnToTheAnswers)));
        Assert.That(draft.WasShownWhole(0), Is.False, "the last part, but not yet drawn");
        screen.Question.NextPart(Later(screen));
        Assert.That(draft.WasShownWhole(0), Is.True, "every part drawn");

        var answers = Screen(workspace, screen);
        Assert.That((answers.Lines[0].Rows, answers.Lines[0].FromRow), Is.EqualTo((1, (int?)null)), "its answers are headed by its first row, cut with an ellipsis");
        screen.Question.Choose(0);
        Assert.That(Screen(workspace, screen).Footer[PromptSlot.FarRight]!.Available, Is.True);
    }

    [Test]
    public void MeasuredLongerBeforeItWasDrawnAQuestionShowsItsFirstPart()
    {
        var question = OnePrompt("Postgres", "SQLite");
        var (_, workspace, screen, draft) = Asking(question, new[] { new PromptMeasure(1, new[] { 1, 1 }, new[] { 1, 1 }) }, rows: 6);
        Assert.That(screen.Question.QuestionPart, Is.Null, "short, it heads its answers");
        screen.ReadQuestion(draft, new[] { new PromptMeasure(9, new[] { 1, 1 }, new[] { 1, 1 }) }, new RowBudget(6), new RowBudget(3));
        Assert.That(screen.Question.QuestionPart, Is.EqualTo(0), "laid out long before it was read, its first part shows");
        var frame = Screen(workspace, screen);
        Assert.That(frame.Lines[1].Words, Is.EqualTo("Next part, 2 of 3"), "with the way on through it");
        for (var part = 0; part < 3; part++) screen.Question.NextPart(Later(screen));
        Assert.That(draft.WasShownWhole(0), Is.True);
        screen.ReadQuestion(draft, new[] { new PromptMeasure(12, new[] { 1, 1 }, new[] { 1, 1 }) }, new RowBudget(6), new RowBudget(3));
        Assert.That(screen.Question.QuestionPart, Is.Null, "read whole, laid out anew, it stays read");
    }

    [Test]
    public void ATypedAnswerOrYourAnswersUnmeasuredCountsAsCutAndUnread()
    {
        var (_, workspace, screen, draft) = Asking(OnePrompt("Postgres", "SQLite"));
        Draw(screen);
        draft.Type(0, new string('x', 419));
        Assert.That(screen.Question.TypedCut(0), Is.True, "unmeasured, a typed answer is cut");
        Assert.That(FileScreens.WhySendWaits(screen), Is.EqualTo(FileScreens.ReadTheAnswer));
        Assert.That(Screen(workspace, screen).Lines.Single(line => line.Action == FileScreens.TypeAnswer).Rows, Is.EqualTo(FileQuestion.AnswerRows));
        screen.Question.MeasureTyped(0, 1, 1);
        Assert.That(FileScreens.WhySendWaits(screen), Is.Null, "measured as one row, it shows whole");
        draft.Type(0, new string('y', 419));
        Assert.That(FileScreens.WhySendWaits(screen), Is.EqualTo(FileScreens.ReadTheAnswer), "a measurement of other words counts for nothing");

        var (_, several, review, answers) = Asking();
        for (var prompt = 0; prompt < 2; prompt++)
        {
            Screen(several, review);
            review.Question.Choose(prompt == 0 ? 1 : 0);
            review.Question.NextQuestion(Later(review));
        }
        Draw(review);
        Assert.That(review.Question.ReviewRead, Is.False, "drawn, but never measured");
        Assert.That(FileScreens.WhySendWaits(review), Is.EqualTo(FileScreens.ReadYourAnswers));
        review.Question.MeasureReview(new[] { 1, 1 });
        Draw(review);
        Assert.That(FileScreens.WhySendWaits(review), Is.Null);
        review.Question.GoTo(0);
        review.Question.Choose(0);
        Assert.That(answers.IsChosen(0, "Light"), Is.True);
        review.Question.NextQuestion(Later(review));
        review.Question.NextQuestion(Later(review));
        Draw(review);
        Assert.That(FileScreens.WhySendWaits(review), Is.EqualTo(FileScreens.ReadYourAnswers), "changed since it was measured, it is unread");
    }

    [Test]
    public void ALongQuestionUnreadKeepsSendAnswerWaiting()
    {
        var question = OnePrompt("Postgres", "SQLite");
        var (_, workspace, screen, draft) = Asking(question, new[] { new PromptMeasure(9, new[] { 1, 1 }, new[] { 1, 1 }) }, rows: 6);
        Draw(screen);
        draft.Choose(0, "Postgres");
        Assert.That(FileScreens.WhySendWaits(screen), Is.EqualTo(FileScreens.ReadTheQuestion));
        Assert.That(new WorkspaceSteering(factory).SendAnswer(draft, workspace).Step, Is.EqualTo(SteeringStep.Explain), "the send rule itself refuses too");
        Assert.That(new WorkspaceSteering(factory).SendAnswer(draft, workspace, FileScreens.WhySendWaits(screen)).Message, Is.EqualTo(FileScreens.ReadTheQuestion));
    }

    [Test]
    public void ClosingAChosenAnswersSidePanelBringsThePageBackTheAnswerStillChosen()
    {
        var question = OnePrompt("Postgres, with read replicas in two regions and a nightly snapshot kept for thirty days", "SQLite");
        var (_, workspace, screen, _) = Asking(question, new[] { new PromptMeasure(1, new[] { 5, 1 }, new[] { 7, 1 }) }, speak: true);
        Draw(screen);
        screen.Question.Choose(0);
        Assert.That(Screen(workspace, screen).Side, Is.Not.Null);
        screen.Question.CloseSide();
        var page = Screen(workspace, screen);
        Assert.That(page.Side, Is.Null, "the page, where Send answer sends with everything in view");
        Assert.That(page.Lines[1].Chosen, Is.True, "the answer stays chosen");
    }

    [Test]
    public void AChosenCutAnswerOrALongTypedOneShowsWholeBesideThePageAndIsSentOnlyOnceEveryPartWasDrawn()
    {
        var question = OnePrompt("Postgres, with read replicas in two regions and a nightly snapshot kept for thirty days", "SQLite");
        // Five rows across the page, cut to two; seven across the narrower side panel, in parts of three.
        var (_, workspace, screen, draft) = Asking(question, new[] { new PromptMeasure(1, new[] { 5, 1 }, new[] { 7, 1 }) }, speak: true);
        Draw(screen);
        var frame = Screen(workspace, screen);
        Assert.That(frame.Lines[1].Rows, Is.EqualTo(FileQuestion.AnswerRows), "the long answer shows two rows, cut");
        Assert.That(frame.Side, Is.Null);

        screen.Question.Choose(0);
        var chosen = Screen(workspace, screen);
        var side = chosen.Side!;
        Assert.That(side.Lines.Single().Words, Is.EqualTo("Postgres, with read replicas in two regions and a nightly snapshot kept for thirty days"));
        Assert.That((side.Lines.Single().Rows, side.Lines.Single().FromRow, side.Parts), Is.EqualTo((3, (int?)0, ((int, int)?)(0, 3))),
            "all its words beside the page, in parts worked out from its own measurement");
        Assert.That(chosen.Lines[1].Chosen, Is.True, "the answer stays chosen, and brings its side panel");
        Assert.That(FileScreensTests.Slots(chosen.Footer), Is.EqualTo(new[] { Footer.Close, null, null, Footer.NextPage, FileScreens.SendAnswer }),
            "Next page turns its parts, in Hold to talk's place");
        Assert.That(FileScreens.WhySendWaits(screen), Is.EqualTo(FileScreens.ReadTheAnswer), "chosen, but not all of it seen");

        var steering = new WorkspaceSteering(factory);
        screen.Question.SideDrawn(clock);
        screen.Question.NextSidePart(clock + TimeSpan.FromSeconds(0.2));
        Assert.That(screen.Question.SidePart, Is.Zero, "a part stands 0.4 seconds before it turns");
        for (var part = 1; part < 3; part++)
        {
            clock += TimeSpan.FromSeconds(1);
            screen.Question.NextSidePart(clock);
            Assert.That(steering.SendAnswer(screen, workspace).Step, Is.EqualTo(SteeringStep.Explain), "not before every part was drawn");
            screen.Question.SideDrawn(clock);
        }
        Assert.That(FileScreens.WhySendWaits(screen), Is.Null, "every part drawn beside the page");
        Assert.That(steering.SendAnswer(screen, workspace).Step, Is.EqualTo(SteeringStep.Send));

        draft.Type(0, "MariaDB with a long story about why it fits best for our case");
        screen.Question.MeasureTyped(0, 4, 3);
        Assert.That(FileScreens.WhySendWaits(screen), Is.EqualTo(FileScreens.ReadTheAnswer), "a long typed answer too");
        var typed = Screen(workspace, screen);
        Assert.That(typed.Side!.Lines.Single().Words, Is.EqualTo("“MariaDB with a long story about why it fits best for our case”"));
        Assert.That(typed.Side.Parts, Is.Null, "three rows fit one part");
        screen.Question.SideDrawn(clock);
        Assert.That(FileScreens.WhySendWaits(screen), Is.Null);
        draft.Type(0, "MariaDB, changed a great deal since it was read beside the page");
        screen.Question.MeasureTyped(0, 4, 3);
        Assert.That(FileScreens.WhySendWaits(screen), Is.EqualTo(FileScreens.ReadTheAnswer), "changed words are read again");
    }

    [Test]
    public void TheSameTypedWordsLaidIntoFewerPartsAreReadAgainFromTheFirst()
    {
        var (_, workspace, screen, draft) = Asking();
        Draw(screen);
        draft.Type(0, string.Join(" ", Enumerable.Range(1, 60).Select(step => "Lock it for a minute after try " + step)));
        screen.Question.MeasureTyped(0, 4, 12);
        Screen(workspace, screen);
        Assert.That(screen.Question.SideParts, Is.EqualTo(4), "twelve rows in parts of three");
        for (var part = 0; part < 3; part++)
        {
            screen.Question.SideDrawn(clock);
            clock += TimeSpan.FromSeconds(1);
            screen.Question.NextSidePart(clock);
        }
        screen.Question.MeasureTyped(0, 4, 6);
        Screen(workspace, screen);
        Assert.That((screen.Question.SideParts, screen.Question.SidePart), Is.EqualTo((2, 0)), "the same words in two parts, from the first");
        screen.Question.SideDrawn(clock);
        Assert.That(screen.Question.AnswersRead(0), Is.False, "three parts drawn of the old four count for none of the new two");
        clock += TimeSpan.FromSeconds(1);
        screen.Question.NextSidePart(clock);
        screen.Question.SideDrawn(clock);
        Assert.That(screen.Question.AnswersRead(0), Is.True);
    }

    private static string LongTyped(string what) => string.Join(" ", Enumerable.Range(1, 60).Select(step => what + " " + step));

    [Test]
    public void ALongTypedAnswerUnreadOpensAgainAtThePartToReadNextOnComingBackByNextQuestion()
    {
        var question = new QuestionView
        {
            QuestionId = "question-3",
            Answerable = true,
            AskedAt = Samples.Time,
            Prompts = new List<QuestionPrompt>
            {
                new() { Key = "q0", Header = "Notice", Text = "Should the person be told?", Options = new List<QuestionOption> { new() { Label = "Yes" } }, Multiple = false, FreeText = false },
                new() { Key = "q1", Header = "Lockout", Text = "How long should a lockout last?", Options = new List<QuestionOption> { new() { Label = "15 minutes" } }, Multiple = false, FreeText = true },
            },
        };
        var (_, _, screen, draft) = Asking(question);
        screen.Question.NextQuestion(Later(screen));
        draft.Type(1, LongTyped("Lock it for a minute after try"));
        screen.Question.MeasureTyped(1, 4, 12);
        Assert.That((screen.Question.SideOption, screen.Question.SideParts), Is.EqualTo(((int?)1, 4)));
        screen.Question.SideDrawn(clock);
        screen.Question.GoTo(0);
        Assert.That(screen.Question.SideOption, Is.Null);
        screen.Question.NextQuestion(Later(screen));
        Assert.That((screen.Question.Prompt, screen.Question.SideOption, screen.Question.SidePart), Is.EqualTo((1, (int?)1, 1)),
            "Next question back to it opens its panel at the first part not yet drawn");
    }

    [Test]
    public void ALongTypedAnswerGivenWhileTheQuestionsPartsShowOpensOnceTheLastPartEnds()
    {
        var (_, _, screen, draft) = Asking(OnePrompt("Postgres", "SQLite"), new[] { new PromptMeasure(6, new[] { 1, 1 }, new[] { 1, 1 }) }, rows: 4);
        Assert.That(screen.Question.QuestionPart, Is.EqualTo(0), "the long question shows in parts first");
        draft.Type(0, LongTyped("MariaDB, for the reason"));
        screen.Question.MeasureTyped(0, 4, 12);
        Assert.That(screen.Question.SideOption, Is.Null, "nothing beside a part of the question");
        for (var step = 0; step < 5 && screen.Question.QuestionPart != null; step++) screen.Question.NextPart(Later(screen));
        Assert.That((screen.Question.QuestionPart, screen.Question.SideOption, screen.Question.SidePart), Is.EqualTo(((int?)null, (int?)2, 0)),
            "once its last part ends, the unread typed answer's panel opens");
    }

    [Test]
    public void ATypedAnswerEditedToFitClosesItsPanelAndItStaysClosedLaidOutLongerAgain()
    {
        var (_, _, screen, draft) = Asking();
        Draw(screen);
        draft.Type(0, LongTyped("Teal and grey, for the reason"));
        screen.Question.MeasureTyped(0, 4, 12);
        Assert.That(screen.Question.SideOption, Is.EqualTo(2));
        draft.Type(0, "Teal");
        screen.Question.MeasureTyped(0, 1, 1);
        Assert.That(screen.Question.SideOption, Is.Null, "words that fit their row need no panel");
        screen.Question.MeasureTyped(0, 4, 12);
        Assert.That(screen.Question.SideOption, Is.Null, "the same words laid out longer stay closed until the row opens them");
    }

    [Test]
    public void TypedWordsChangedForAnotherPromptOpenNothingBesideThePromptInView()
    {
        var (_, _, screen, draft) = Asking();
        screen.Question.NextQuestion(Later(screen));
        Assert.That(screen.Question.Prompt, Is.EqualTo(1));
        screen.Question.Choose(2);
        draft.Type(0, LongTyped("Teal and grey, for the reason"));
        screen.Question.MeasureTyped(0, 4, 12);
        Assert.That(screen.Question.SideOption, Is.Null, "the first prompt's typed answer opens no panel over the second prompt's answers");
    }

    [Test]
    public void OnYourAnswersTheTypedRowReopensNothing()
    {
        var (_, _, screen, draft) = Asking();
        Draw(screen);
        draft.Type(0, LongTyped("Teal and grey, for the reason"));
        screen.Question.MeasureTyped(0, 4, 12);
        screen.Question.NextQuestion(Later(screen));
        screen.Question.NextQuestion(Later(screen));
        Assert.That(screen.Question.Reviewing, Is.True);
        Assert.That(screen.Question.ReopenTyped(), Is.False, "nothing to reopen on the person's answers");
        Assert.That(screen.Question.SideOption, Is.Null);
    }

    [Test]
    public void TheViewCannotCountAnAnswerReadByReportingFewerParts()
    {
        var question = OnePrompt("Postgres, with read replicas in two regions and a nightly snapshot kept for thirty days", "SQLite");
        var (_, workspace, screen, _) = Asking(question, new[] { new PromptMeasure(1, new[] { 9, 1 }, new[] { 9, 1 }) });
        Draw(screen);
        screen.Question.Choose(0);
        Screen(workspace, screen);
        screen.Question.SideDrawn(clock);
        Assert.That(screen.Question.SideParts, Is.EqualTo(3), "nine rows in parts of three, from the measurement");
        Assert.That(FileScreens.WhySendWaits(screen), Is.EqualTo(FileScreens.ReadTheAnswer), "one part drawn is one part read");
    }

    [Test]
    public void AQuestionOfOnePromptSendsFromItsOwnPageWithNoYourAnswers()
    {
        var (_, workspace, screen, draft) = Asking(OnePrompt("Postgres", "SQLite"));
        var frame = Screen(workspace, screen);
        Assert.That(frame.Lines.Select(line => line.Words), Has.None.EqualTo(FileScreens.YourAnswers));
        var waiting = frame.Footer[PromptSlot.FarRight]!;
        Assert.That((waiting.Available, waiting.Reason, waiting.PageExplains), Is.EqualTo((false, (string?)"Choose or type an answer first.", true)),
            "waiting only for a choice, Send answer keeps its reason undrawn: the answers above say what to do");
        Assert.That(frame.Reason, Is.Null, "no reason line on the page");
        Draw(screen);
        screen.Question.Choose(1);
        Assert.That(Screen(workspace, screen).Footer[PromptSlot.FarRight]!.Available, Is.True);
        screen.Question.NextQuestion(Later(screen));
        Assert.That(screen.Question.Reviewing, Is.False);
    }

    [Test]
    public void AQuestionNotYetLaidOutShowsWhatItAsksNeverThatNothingWaits()
    {
        var work = new AskingWork();
        var workspace = FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer, WorkspaceAction.Interrupt);
        var screen = new FileScreen { Section = FileSection.Waiting };
        var frame = Screen(workspace, screen);
        Assert.That(frame.Lines.Select(line => line.Words), Is.EqualTo(new[] { "“Which colour scheme should the dashboard use?”" }));
        Assert.That(frame.Lines.Select(line => line.Words), Has.None.EqualTo(FileScreens.NothingWaits));
        var send = frame.Footer[PromptSlot.FarRight]!;
        Assert.That((send.Id, send.Available, send.Reason), Is.EqualTo((FileScreens.SendAnswer, false, (string?)FileScreens.QuestionNotReady)));
        Assert.That(new WorkspaceSteering(factory).SendAnswer(screen, workspace).Message, Is.EqualTo(FileScreens.QuestionNotReady), "and nothing can be sent");
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
        screen.ReadQuestion(draft, Short(work.Question), new RowBudget(8), new RowBudget(3));
        var frame = Screen(work.Present(), screen);
        Assert.That(FileScreensTests.Slots(frame.Footer).Skip(3), Is.EqualTo(new string?[] { null, null }), "no Hold to talk and no Send answer");
        Assert.That(frame.Footer[PromptSlot.Rare]?.Id, Is.EqualTo(FileScreens.Stop), "stopping is the way on, so Stop stands here");
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
        screen.ReadQuestion(draft, Short(work.Question), new RowBudget(8), new RowBudget(3));
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
        screen.ReadQuestion(draft, Short(work.Question), new RowBudget(8), new RowBudget(3));
        screen.Question.NextQuestion(Later(screen));
        screen.Question.NextQuestion(Later(screen));
        Assert.That(steering.SendAnswer(draft, workspace).Step, Is.EqualTo(SteeringStep.Confirm));
        var frame = Screen(workspace, screen, steering);
        Assert.That(frame.Lines.Select(line => line.Words), Is.EqualTo(new[] { "Question 1 of 2 · Colour scheme: Dark", "Question 2 of 2 · Pages: Orders", "Send these answers?" }));
        Assert.That(FileScreensTests.Slots(frame.Footer), Is.EqualTo(new[] { Footer.Close, null, FileScreens.Yes, null, FileScreens.Cancel }));
        Assert.That(steering.Confirm(workspace).Step, Is.EqualTo(SteeringStep.Send));
        Assert.That(steering.Confirm(workspace).Step, Is.EqualTo(SteeringStep.None), "Yes sends once");
    }

    [Test]
    public void AnswersAwaitingTheirYesLapseWhenTheQuestionChanges()
    {
        var policies = new[] { new CommandPolicy { CommandType = CommandType.ExecutionAnswerQuestion, Policy = PolicyCategory.ReviewRequired } };
        var work = new AskingWork(policies: policies);
        var steering = new WorkspaceSteering(factory);
        var screen = new FileScreen();
        var draft = Answered(work);
        screen.ReadQuestion(draft, Short(work.Question), new RowBudget(8), new RowBudget(3));
        Assert.That(steering.SendAnswer(draft, FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer)).Step, Is.EqualTo(SteeringStep.Confirm));

        var other = AskingWork.Scripted();
        other.QuestionId = "question-9";
        work.Change(execution => execution.PendingQuestions[0] = other);
        var workspace = FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer);
        var frame = Screen(workspace, screen, steering);
        Assert.That(frame.Footer.Confirming, Is.False, "no Yes over answers to a question no longer asked");
        Assert.That(frame.Lines.Last().Words, Is.EqualTo("Nothing was sent: the question changed. Read it again."));
        Assert.That(steering.Armed, Is.Null);
    }
}

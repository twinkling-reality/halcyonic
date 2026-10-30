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


    /// <summary>Reads the visible question parts to the choice page, without choosing or reviewing.</summary>
    private void ShowAnswers(FileScreen screen)
    {
        var parts = screen.Question.QuestionParts(screen.Question.Prompt);
        for (var part = 0; part < parts && screen.Question.QuestionPart != null; part++)
            screen.Question.NextPart(Later(screen));
        Draw(screen);
    }

    /// <summary>Reads every measured editor part without saving or sending.</summary>
    private void ReadInput(FileScreen screen)
    {
        var parts = screen.Question.InputParts;
        for (var part = screen.Question.InputPart; part < parts; part++)
        {
            Draw(screen);
            if (part < parts - 1) screen.Question.NextInputPart(clock += TimeSpan.FromSeconds(1));
        }
    }

    private void OpenInput(FileScreen screen, string words, int rows)
    {
        ShowAnswers(screen);
        Assert.That(screen.Question.BeginInput(), Is.True);
        screen.Question.ChangeInput(words);
        screen.Question.MeasureInput(rows);
    }

    private void UseInput(FileScreen screen, string words, int rows)
    {
        OpenInput(screen, words, rows);
        ReadInput(screen);
        Assert.That(screen.Question.UseInput(stopOnPage: false), Is.True);
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
    public void OnePromptAtATimeSeparatesOfferedChoicesFromWritingAndOffersTheVisibleNextStep()
    {
        var (_, workspace, screen, draft) = Asking(speak: true);
        var frame = Screen(workspace, screen);
        Assert.That(frame.Lines.Select(line => line.Words), Is.EqualTo(new[]
        {
            "Question 1 of 2 · Choose One",
            "Which colour scheme should the dashboard use?",
            "Light · Dark text on a light background",
            "Dark · Light text on a dark background",
            FileScreens.WriteAnswer,
        }));
        Assert.That(frame.Lines.Select(line => (line.Runs.AllTheirs, line.Icon == GlazeIcon.Quote)),
            Is.EqualTo(new[] { (false, false), (true, true), (true, true), (true, true), (false, false) }));
        Assert.That(frame.Lines.Where(line => line.Choice).Select(line => line.Action).Distinct(), Is.EqualTo(new[] { FileScreens.Choose }));
        Assert.That(frame.Lines.Single(line => line.Action == FileScreens.TypeAnswer).Control, Is.EqualTo(LineControl.Action), "input entry is a distinct action, never a choice");
        Assert.That(frame.Lines.Any(line => line.Chosen), Is.False);
        Assert.That(FileScreensTests.Slots(frame.Footer), Is.EqualTo(new[] { Footer.Close, null, null, null, FileScreens.NextQuestion }));
        Assert.That(FileScreens.SpeakAnswer, Is.Not.EqualTo(FileScreens.HoldToTalk), "a spoken answer is never raised as an instruction");
        var next = frame.Footer[PromptSlot.FarRight]!;
        Assert.That((next.Words, next.Available, next.Reason), Is.EqualTo(("Next Question", false, FileScreens.ReadTheQuestion)));
        Assert.That(frame.Reason, Is.EqualTo(FileScreens.ReadTheQuestion), "the immediate missing step is shown adjacent to its quiet control");
        Draw(screen);
        screen.Question.Choose(1);
        var chosen = Screen(workspace, screen);
        Assert.That(chosen.Lines.Where(line => line.Chosen).Select(line => line.Key), Is.EqualTo(new[] { "1" }));
        Assert.That(draft.IsChosen(0, "Dark"), Is.True);
        Assert.That(chosen.Footer.All.Select(each => each.Prompt.Id), Has.None.EqualTo(FileScreens.SendAnswer), "choosing sends nothing and review comes later");
    }

    [Test]
    public void AQuestionOfSeveralPromptsSendsOnlyFromYourAnswersEachWholeOnceEveryPageWasDrawn()
    {
        var (_, workspace, screen, draft) = Asking();
        Screen(workspace, screen);
        Draw(screen);
        screen.Question.Choose(1);
        screen.Question.NextQuestion(Later(screen));
        var second = Screen(workspace, screen);
        Assert.That(second.Lines.Single(line => line.Runs.AllTheirs && line.Action == null).Words, Is.EqualTo("Which pages should change?"));
        Assert.That(second.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo(FileScreens.ReviewAnswers));
        Assert.That(draft.IsChosen(0, "Dark"), Is.True, "an earlier prompt's choice is kept");

        screen.Question.NextQuestion(Later(screen));
        screen.Question.MeasureReview(new[] { 1, 1 });
        var answers = Screen(workspace, screen);
        Assert.That(answers.Lines.Where(line => line.Action == FileScreens.GoToQuestion).Select(line => (line.Words, line.Action, line.Key, line.Rows)), Is.EqualTo(new[]
        {
            ("Colour scheme: Dark", FileScreens.GoToQuestion, "0", 1),
            ("Pages: Not Answered", FileScreens.GoToQuestion, "1", 1),
        }), "each answer whole, by its prompt's name");
        Assert.That(answers.Footer[PromptSlot.FarRight]!.Reason, Is.EqualTo("Answer All Questions · 1 of 2 Answered"));

        screen.Question.GoTo(1);
        Draw(screen);
        screen.Question.Choose(1);
        screen.Question.NextQuestion(Later(screen));
        screen.Question.MeasureReview(new[] { 1, 1 });
        var unread = Screen(workspace, screen);
        Assert.That(unread.Footer[PromptSlot.FarRight]!.Reason, Is.EqualTo(FileScreens.ReadYourAnswers), "until the view has drawn them");
        Draw(screen);
        var ready = Screen(workspace, screen);
        Assert.That(ready.Footer[PromptSlot.FarRight]!.Available, Is.True);
        Assert.That(ready.Lines.Single(line => line.Key == "1").Words, Is.EqualTo("Pages: Orders"));
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
            ShowAnswers(screen);
            Screen(workspace, screen);
            var option = prompt == 0 ? 1 : 0;
            while (!screen.Question.Answers.Contains(option))
                screen.Question.MoreAnswers(Later(screen));
            Draw(screen);
            screen.Question.Choose(option);
            screen.Question.NextQuestion(Later(screen));
        }
        screen.Question.MeasureReview(new[] { 3, 3 });
        var first = Screen(workspace, screen);
        Assert.That(first.Lines.Select(line => line.Words), Is.EqualTo(new[] { FileScreens.YourAnswers, "Colour scheme: Dark" }));
        Assert.That(first.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Next Part · 2 of 2"));
        Assert.That(first.Lines.Single(line => line.Action == FileScreens.GoToQuestion).Rows, Is.EqualTo(3), "an answer shows whole, in the rows it takes");
        Draw(screen);
        Assert.That(Screen(workspace, screen).Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(FileScreens.MoreAnswers), "review navigation is the only forward action before its final part");
        screen.Question.MoreAnswers(Later(screen));
        var last = Screen(workspace, screen);
        Assert.That(last.Lines.Select(line => line.Words), Is.EqualTo(new[] { FileScreens.YourAnswers, "Pages: Sign in" }));
        Assert.That(last.Footer[PromptSlot.Rare]!.Words, Is.EqualTo("Previous Part"));
        Assert.That(draft.IsChosen(1, "Sign in"), Is.True, "turning the person's answers clears nothing");
        Draw(screen);
        Assert.That(Screen(workspace, screen).Footer[PromptSlot.FarRight]!.Available, Is.True);
    }

    [Test]
    public void TurningAPromptsAnswersKeepsTheDraftAndEveryOfferedAnswerReachable()
    {
        var labels = new[] { "Postgres", "SQLite", "MySQL", "DynamoDB", "Redis" };
        var question = OnePrompt(labels);
        var measured = new[] { new PromptMeasure(1, labels.Select(_ => 2).ToList(), labels.Select(_ => 2).ToList()) };
        // Six rows include the compact Question/Choose caption, the full question, a two-row option,
        // the paired Write/pager target and the initial unread-question reason. No choose-first reason is needed.
        var (_, workspace, screen, draft) = Asking(question, measured, rows: 6);
        Assert.That(screen.Question.AnswerShows(0, 0), Is.EqualTo(2), "one complete measured option fits even before the question is drawn");
        ShowAnswers(screen);
        Assert.That(screen.Question.Pages, Is.EqualTo(5));
        var seen = new List<int>();
        for (var page = 0; page < screen.Question.Pages; page++)
        {
            var frame = Screen(workspace, screen);
            seen.AddRange(screen.Question.Answers);
            Assert.That(Height(frame, new RowBudget(6)), Is.LessThanOrEqualTo(6), "the page fits its rows, Type my answer beside the row for more answers");
            Assert.That(frame.Lines.Single(line => line.Action == FileScreens.TypeAnswer).BesideNext, Is.True);
            Assert.That(frame.Lines.Single(line => line.Action == FileScreens.TypeAnswer).Words, Is.EqualTo(FileScreens.WriteAnswer), "Type my answer stands last among the answers");
            Assert.That(frame.Lines.Last().Words, Is.EqualTo(FileScreens.MoreAnswersWords(page, 5)));
            screen.Question.MoreAnswers(Later(screen));
        }
        Assert.That(seen, Is.EqualTo(Enumerable.Range(0, labels.Length)), "every answer the agent offered, in its order");
        Assert.That(FileScreens.MoreAnswersWords(4, 5), Is.EqualTo("First Choices"));

        Draw(screen);
        screen.Question.Choose(0);
        screen.Question.MoreAnswers(Later(screen));
        Assert.That(draft.IsChosen(0, "Postgres"), Is.True, "turning the page retains the chosen answer and its own read binding");

        UseInput(screen, "CockroachDB", 1);
        screen.Question.MoreAnswers(Later(screen));
        Assert.That(draft.Typed(0), Is.EqualTo("CockroachDB"));
        var typed = Screen(workspace, screen).Lines.Single(line => line.Action == FileScreens.TypeAnswer);
        Assert.That((typed.Words, typed.Chosen, typed.Runs.AnyTheirs, typed.Fact), Is.EqualTo((FileScreens.EditAnswer, true, false, "Selected")), "the bounded Edit action identifies a saved answer, never impersonates the person's words");
        OpenInput(screen, "CockroachDB", 1);
        Assert.That(Screen(workspace, screen).Lines.Single(line => line.Control == LineControl.Input).Runs.ToString(), Is.EqualTo("[CockroachDB]"));
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

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void GenericPageBoundsKeepTheirOriginalSectionsReservation(TextSize text)
    {
        var budget = HeightBudget.Of(text, subjectRows: 1);
        var zoom = text == TextSize.Larger ? Comfort.LargerTextScale : 1f;
        PlaneComposition WithSections(float room) => new(new[]
        {
            new PlaneColumn(PlaneComposition.Units(Glaze.Menu.FileColumnDegrees), MenuPage.Subject(1, pill: true),
                MenuPage.Sections, MenuPage.Content(room + MenuPage.SourceLine)),
        }, zoom);
        Assert.That(MenuPage.Fits(WithSections(budget.Room)), Is.True, "the default budget still reserves the real sections row and source line");
        Assert.That(MenuPage.Fits(WithSections(budget.Room + Glaze.MetersAt(0.2f, 1f))), Is.False, "a taller page still passes the original physical field bound");
        Assert.That(HeightBudget.Of(text, subjectRows: 1, focused: true).Room, Is.GreaterThan(budget.Room), "only the genuinely omitted sections free this room");
    }

    [Test]
    public void OnAHeadsetPageFourAnswersOfTwoRowsPackByHeightWithinTheField()
    {
        foreach (var text in new[] { TextSize.Standard, TextSize.Larger })
        {
            var budget = HeightBudget.Of(text, subjectRows: 1, focused: true);
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
        var budget = HeightBudget.Of(TextSize.Standard, subjectRows: 1, focused: true);
        var labels = new[] { "15 minutes", "1 hour", "Until reset", "Grows each time" };
        var measured = new[] { new PromptMeasure(1, labels.Select(_ => 2).ToList(), labels.Select(_ => 3).ToList()) };
        var (_, workspace, screen, _) = Asking(OnePrompt(labels), measured);
        screen.ReadQuestion(screen.Question.Draft!, measured, budget, budget);
        Assert.That(screen.Question.QuestionPart, Is.Null, "a question of one row heads its answers");
        Assert.That(screen.Question.Answers, Is.EqualTo(new[] { 0, 1 }), "the question and its answers are one group, 8 dp apart, so two answers fit");
        Assert.That(Height(Screen(workspace, screen), budget), Is.LessThanOrEqualTo(budget.Room + 1e-5f));
    }

    [Test]
    public void ATwoRowQuestionStaysWholeWhenBoundedContextCannotSaveAnyPages()
    {
        var budget = HeightBudget.Of(TextSize.Standard, subjectRows: 1, focused: true);
        var four = new[] { "15 minutes", "1 hour", "Until reset", "Grows each time" };
        var measured = new[] { new PromptMeasure(2, four.Select(_ => 2).ToList(), four.Select(_ => 3).ToList()) };
        var (_, workspace, screen, _) = Asking(OnePrompt(four), measured);
        screen.ReadQuestion(screen.Question.Draft!, measured, budget, budget);
        var wholeHead = budget.Words(2) + budget.LineGap;
        var contextHead = budget.Target(1) + budget.TargetGap;
        Assert.That(contextHead, Is.GreaterThanOrEqualTo(wholeHead), "the bounded Read question target and its target gap cost at least the complete two-row question");
        var below = budget.Words(1) + budget.LineGap + budget.Target() + budget.TargetGap + budget.Reason;
        int Capacity(float head)
        {
            var count = 0;
            while (count < four.Length && budget.Targets(Enumerable.Repeat(2, count + 1).ToArray()) <= budget.Room - below - head + 1e-6f) count++;
            return count;
        }
        var wholeCapacity = Capacity(wholeHead);
        var contextCapacity = Capacity(contextHead);
        Assert.That((wholeCapacity, contextCapacity), Is.EqualTo((1, 1)), "the actual initial measured room holds one two-row choice under either head");
        var wholePages = (int)Math.Ceiling(four.Length / (double)wholeCapacity);
        var cutPages = (int)Math.Ceiling(2d / screen.Question.QuestionPartRows) + (int)Math.Ceiling(four.Length / (double)contextCapacity);
        Assert.That(wholePages, Is.LessThan(cutPages), "four whole-question pages beat one question-only part plus four bounded-context choice pages");
        Assert.That((screen.Question.QuestionParts(0), screen.Question.HeadRows(0), screen.Question.Pages), Is.EqualTo((0, 2, Math.Min(wholePages, cutPages))));
        var seen = new List<int>();
        for (var page = 0; page < screen.Question.Pages; page++)
        {
            var frame = Screen(workspace, screen);
            Assert.That(Height(frame, budget), Is.LessThanOrEqualTo(budget.Room + 1e-5f), "page " + page);
            Assert.That(screen.Question.Answers, Has.Count.EqualTo(1));
            seen.AddRange(screen.Question.Answers);
            screen.Question.MoreAnswers(Later(screen));
        }
        Assert.That(seen, Is.EqualTo(Enumerable.Range(0, four.Length)), "every offered choice remains reachable in its original order");

        var three = four.Take(3).ToArray();
        var tied = new[] { new PromptMeasure(2, three.Select(_ => 2).ToList(), three.Select(_ => 3).ToList()) };
        var (_, _, beside, _) = Asking(OnePrompt(three), tied);
        beside.ReadQuestion(beside.Question.Draft!, tied, budget, budget);
        Assert.That((beside.Question.QuestionParts(0), beside.Question.HeadRows(0), beside.Question.Pages), Is.EqualTo((0, 2, 3)),
            "the bounded context cannot increase capacity, so three whole pages beat an extra question-only part");
    }

    [Test]
    public void AChoiceIsTakenOnlyFromThePageInViewAndALayoutAnewLandsOnItOrClearsIt()
    {
        var labels = new[] { "Postgres", "SQLite", "MySQL", "DynamoDB", "Redis" };
        var question = OnePrompt(labels);
        var (_, workspace, screen, draft) = Asking(question, new[] { new PromptMeasure(1, labels.Select(_ => 1).ToList(), labels.Select(_ => 1).ToList()) }, rows: 6);
        ShowAnswers(screen);
        Assert.That(screen.Question.Answers, Is.EqualTo(new[] { 0, 1 }), "the compact Question/Choose caption and initial unread-question reason add two rows to the original four-row fixture; the redundant choose-first reason is omitted");
        Draw(screen);
        screen.Question.Choose(4);
        Assert.That(draft.IsChosen(0, "Redis"), Is.False, "an answer not on the page in view can't be chosen");

        Screen(workspace, screen);
        for (var page = 0; page < screen.Question.Pages && !screen.Question.Answers.Contains(4); page++)
            screen.Question.MoreAnswers(Later(screen));
        Assert.That(screen.Question.Answers, Is.EqualTo(new[] { 4 }));
        Draw(screen);
        screen.Question.Choose(4);
        Assert.That(draft.IsChosen(0, "Redis"), Is.True);

        // The text grows a size: a row fewer a page.
        screen.ReadQuestion(draft, new[] { new PromptMeasure(1, labels.Select(_ => 1).ToList(), labels.Select(_ => 1).ToList()) }, new RowBudget(5), new RowBudget(3));
        Assert.That(screen.Question.AnswersRead(0), Is.False, "a relayout invalidates the option's old read binding");
        ShowAnswers(screen);
        Assert.That(screen.Question.Answers, Does.Contain(4), "laid out anew, the page shows what was chosen");
        Assert.That(screen.Question.AnswersRead(0), Is.True, "the chosen option was drawn in its new layout");
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
        Assert.That((first.Lines.Single(line => line.Runs.AllTheirs).Rows, first.Lines.Single(line => line.Runs.AllTheirs).FromRow), Is.EqualTo((4, (int?)0)));
        Assert.That((first.Footer[PromptSlot.FarRight]!.Words, first.Footer[PromptSlot.FarRight]!.Id), Is.EqualTo(("Next Part · 2 of 3", FileScreens.NextPart)));
        Assert.That(first.Side, Is.Null, "the question has pages of its own, not a side panel");
        Assert.That(first.Lines.Any(line => line.Choice), Is.False, "nothing to answer before it is read");

        screen.Question.NextPart(clock);
        Assert.That(screen.Question.QuestionPart, Is.EqualTo(0), "the next part waits until this one has been drawn");
        Draw(screen);
        screen.Question.NextPart(clock + TimeSpan.FromSeconds(0.2));
        Assert.That(screen.Question.QuestionPart, Is.EqualTo(0), "and has stood 0.4 seconds");
        screen.Question.NextPart(Later(screen));
        Assert.That(Screen(workspace, screen).Lines.Single(line => line.Runs.AllTheirs).FromRow, Is.EqualTo(4));
        screen.Question.NextPart(Later(screen));
        var last = Screen(workspace, screen);
        Assert.That((last.Lines.Single(line => line.Runs.AllTheirs).FromRow, last.Footer[PromptSlot.FarRight]!.Words), Is.EqualTo(((int?)8, "See Answers")));
        Assert.That(draft.WasShownWhole(0), Is.False, "the last part, but not yet drawn");
        screen.Question.NextPart(Later(screen));
        Assert.That(draft.WasShownWhole(0), Is.True, "every part drawn");

        var answers = Screen(workspace, screen);
        Assert.That((answers.Lines.Single(line => line.Action == FileScreens.ReadQuestion || line.Runs.AllTheirs && line.Action == null).Rows, answers.Lines.Single(line => line.Action == FileScreens.ReadQuestion || line.Runs.AllTheirs && line.Action == null).FromRow), Is.EqualTo((1, (int?)null)), "its answers are headed by its first row, cut with an ellipsis");
        Draw(screen);
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
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Next Part · 2 of 3"), "with a visible way on through the complete question");
        for (var part = 0; part < 3; part++) screen.Question.NextPart(Later(screen));
        Assert.That(draft.WasShownWhole(0), Is.True);
        screen.ReadQuestion(draft, new[] { new PromptMeasure(12, new[] { 1, 1 }, new[] { 1, 1 }) }, new RowBudget(6), new RowBudget(3));
        Assert.That(screen.Question.QuestionPart, Is.Null, "read whole, laid out anew, it stays read");
    }

    [Test]
    public void TypedWordsRequireEditorReadingAndReviewsRequireTheirOwnCurrentMeasurements()
    {
        var (_, workspace, screen, draft) = Asking(OnePrompt("Postgres", "SQLite"));
        ShowAnswers(screen);
        var words = new string('x', 419);
        draft.Type(0, words);
        Assert.That(screen.Question.TypedCut(0), Is.True);
        Assert.That(FileScreens.WhyNextWaits(screen.Question), Is.EqualTo(FileScreens.ReadTheAnswer));
        screen.Question.MeasureTyped(0, 1, 1);
        Assert.That(FileScreens.WhyNextWaits(screen.Question), Is.EqualTo(FileScreens.ReadTheAnswer), "measurement alone never certifies words absent from the choice card");
        UseInput(screen, words, 1);
        Assert.That(FileScreens.WhyNextWaits(screen.Question), Is.Null);
        draft.Type(0, new string('y', 419));
        Assert.That(FileScreens.WhyNextWaits(screen.Question), Is.EqualTo(FileScreens.ReadTheAnswer), "reading other words cannot certify the changed answer");
        var (_, several, review, answers) = Asking();
        for (var prompt = 0; prompt < 2; prompt++)
        {
            ShowAnswers(review);
            review.Question.Choose(prompt == 0 ? 1 : 0);
            review.Question.NextQuestion(Later(review));
        }
        Draw(review);
        Assert.That(review.Question.ReviewRead, Is.False, "drawn without review measurement still counts for nothing");
        Assert.That(FileScreens.WhySendWaits(review), Is.EqualTo(FileScreens.ReadYourAnswers));
        review.Question.MeasureReview(new[] { 1, 1 });
        Draw(review);
        Assert.That(FileScreens.WhySendWaits(review), Is.Null);
        review.Question.GoTo(0);
        ShowAnswers(review);
        review.Question.Choose(0);
        Assert.That(answers.IsChosen(0, "Light"), Is.True);
        review.Question.NextQuestion(Later(review));
        Draw(review);
        Assert.That(FileScreens.WhySendWaits(review), Is.EqualTo(FileScreens.ReadYourAnswers), "a changed answer must be measured and read again on review");
    }

    [Test]
    public void ALongQuestionUnreadKeepsSendAnswerWaiting()
    {
        var question = OnePrompt("Postgres", "SQLite");
        var (_, workspace, screen, draft) = Asking(question, new[] { new PromptMeasure(9, new[] { 1, 1 }, new[] { 1, 1 }) }, rows: 6);
        Draw(screen);
        draft.Choose(0, "Postgres");
        Assert.That(FileScreens.WhyNextWaits(screen.Question), Is.EqualTo(FileScreens.ReadTheQuestion));
        Assert.That(FileScreens.WhySendWaits(screen), Is.EqualTo("Review Answers First"));
        Assert.That(new WorkspaceSteering(factory).SendAnswer(draft, workspace).Step, Is.EqualTo(SteeringStep.Explain), "the send rule itself refuses too");
        Assert.That(new WorkspaceSteering(factory).SendAnswer(draft, workspace, FileScreens.WhySendWaits(screen)).Message, Is.EqualTo("Review Answers First"));
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
        Assert.That(page.Lines.Single(line => line.Action == FileScreens.Choose && line.Key == "0").Chosen, Is.True, "the answer stays chosen");
    }

    [Test]
    public void AChosenCutAnswerOrALongTypedOneShowsWholeBesideThePageAndIsSentOnlyOnceEveryPartWasDrawn()
    {
        var question = OnePrompt("Postgres, with read replicas in two regions and a nightly snapshot kept for thirty days", "SQLite");
        // Five rows across the page, cut to two; seven across the narrower side panel, in parts of three.
        var (_, workspace, screen, draft) = Asking(question, new[] { new PromptMeasure(1, new[] { 5, 1 }, new[] { 7, 1 }) }, speak: true);
        Draw(screen);
        var frame = Screen(workspace, screen);
        Assert.That(frame.Lines.Single(line => line.Action == FileScreens.Choose && line.Key == "0").Rows, Is.EqualTo(FileQuestion.AnswerRows), "the long answer shows two rows, cut");
        Assert.That(frame.Side, Is.Null);

        Draw(screen);
        screen.Question.Choose(0);
        var chosen = Screen(workspace, screen);
        var side = chosen.Side!;
        Assert.That(side.Lines.Single().Words, Is.EqualTo("Postgres, with read replicas in two regions and a nightly snapshot kept for thirty days"));
        Assert.That((side.Lines.Single().Rows, side.Lines.Single().FromRow, side.Parts), Is.EqualTo((3, (int?)0, ((int, int)?)(0, 3))),
            "all its words beside the page, in parts worked out from its own measurement");
        Assert.That(chosen.Lines.Single(line => line.Action == FileScreens.Choose && line.Key == "0").Chosen, Is.True, "the answer stays chosen, and brings its side panel");
        Assert.That(FileScreensTests.Slots(chosen.Footer), Is.EqualTo(new[] { Footer.Close, null, null, Footer.NextPage, FileScreens.NextQuestion }),
            "Next page turns its parts, in Hold to talk's place");
        Assert.That(FileScreens.WhyNextWaits(screen.Question), Is.EqualTo(FileScreens.ReadTheAnswer), "chosen, but not all of it seen");

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
        Assert.That(FileScreens.WhyNextWaits(screen.Question), Is.Null, "every part drawn beside the page");
        Assert.That(steering.SendAnswer(screen, workspace).Step, Is.EqualTo(SteeringStep.Explain), "whole option detail never skips review");
        screen.Question.NextQuestion(Later(screen));
        screen.Question.MeasureReview(new[] { 3 });
        Draw(screen);
        Assert.That(steering.SendAnswer(screen, workspace).Step, Is.EqualTo(SteeringStep.Send));
        screen.Question.GoTo(0);
        var words = "MariaDB with a long story about why it fits best for our case";
        OpenInput(screen, words, 3);
        var typed = Screen(workspace, screen);
        Assert.That(typed.Side, Is.Null);
        Assert.That(typed.Lines.Single(line => line.Control == LineControl.Input).Runs.AllTheirs, Is.True);
        Assert.That(screen.Question.UseInput(stopOnPage: false), Is.False, "typed words require their own reading");
        ReadInput(screen);
        Assert.That(screen.Question.UseInput(stopOnPage: false), Is.True);
        Assert.That(FileScreens.WhyNextWaits(screen.Question), Is.Null);
        OpenInput(screen, "MariaDB, changed a great deal since it was read in the editor", 3);
        Assert.That(screen.Question.UseInput(stopOnPage: false), Is.False, "changed words require new editor evidence");
        Assert.That(draft.Typed(0), Is.EqualTo(words));
    }

    [Test]
    public void TheSameEditorWordsLaidIntoFewerPartsAreReadAgainFromTheirFirstPart()
    {
        var (_, workspace, screen, draft) = Asking();
        OpenInput(screen, LongTyped("Lock it for a minute after try"), 12);
        Assert.That(screen.Question.InputParts, Is.EqualTo(3), "twelve rows in the focused editor's five-row parts");
        for (var part = 0; part < 2; part++)
        {
            Draw(screen);
            screen.Question.NextInputPart(clock += TimeSpan.FromSeconds(1));
        }
        screen.Question.MeasureInput(6);
        Screen(workspace, screen);
        Assert.That((screen.Question.InputParts, screen.Question.InputPart), Is.EqualTo((2, 0)));
        Draw(screen);
        Assert.That(screen.Question.InputRead, Is.False, "two old-layout parts certify none of the two new parts");
        Assert.That(screen.Question.UseInput(stopOnPage: false), Is.False);
        screen.Question.NextInputPart(clock += TimeSpan.FromSeconds(1));
        Draw(screen);
        Assert.That(screen.Question.InputRead, Is.True);
        Assert.That(screen.Question.UseInput(stopOnPage: false), Is.True);
        Assert.That(draft.Typed(0), Is.EqualTo(LongTyped("Lock it for a minute after try")));
    }

    private static string LongTyped(string what) => string.Join(" ", Enumerable.Range(1, 60).Select(step => what + " " + step));

    [Test]
    public void BackToAPromptKeepsItsSavedWordsButExplicitEditRequiresCurrentSessionReading()
    {
        var question = new QuestionView
        {
            QuestionId = "question-3", Answerable = true, AskedAt = Samples.Time,
            Prompts = new List<QuestionPrompt>
            {
                new() { Key = "q0", Header = "Notice", Text = "Should the person be told?", Options = new List<QuestionOption> { new() { Label = "Yes" } }, FreeText = false },
                new() { Key = "q1", Header = "Lockout", Text = "How long should a lockout last?", Options = new List<QuestionOption> { new() { Label = "15 minutes" } }, FreeText = true },
            },
        };
        var (_, _, screen, draft) = Asking(question);
        ShowAnswers(screen);
        screen.Question.Choose(0);
        screen.Question.NextQuestion(Later(screen));
        var words = LongTyped("Lock it for a minute after try");
        UseInput(screen, words, 12);
        screen.Question.GoTo(0);
        Assert.That(screen.Question.SideOption, Is.Null);
        screen.Question.NextQuestion(Later(screen));
        Assert.That((screen.Question.Prompt, draft.Typed(1), screen.Question.Writing, screen.Question.SideOption), Is.EqualTo((1, words, false, (int?)null)), "navigation keeps saved words without opening input automatically");
        Assert.That(screen.Question.BeginInput(), Is.True);
        screen.Question.MeasureInput(12);
        Assert.That((screen.Question.Input, screen.Question.InputPart, screen.Question.InputRead), Is.EqualTo((words, 0, false)));
        Assert.That(screen.Question.UseInput(stopOnPage: false), Is.False);
    }

    [Test]
    public void TheAnswerEditorCannotOpenBeforeEveryQuestionPartHasShown()
    {
        var (_, _, screen, draft) = Asking(OnePrompt("Postgres", "SQLite"), new[] { new PromptMeasure(6, new[] { 1, 1 }, new[] { 1, 1 }) }, rows: 4);
        Assert.That(screen.Question.QuestionPart, Is.EqualTo(0));
        Assert.That(screen.Question.BeginInput(), Is.False, "the input control cannot skip the complete question");
        Assert.That(screen.Question.Writing, Is.False);
        Assert.That(draft.Typed(0), Is.Null);
        ShowAnswers(screen);
        Assert.That(draft.WasShownWhole(0), Is.True);
        Assert.That(screen.Question.BeginInput(), Is.True);
        screen.Question.ChangeInput(LongTyped("MariaDB, for the reason"));
        screen.Question.MeasureInput(12);
        Assert.That((screen.Question.QuestionPart, screen.Question.InputPart, draft.Typed(0)), Is.EqualTo(((int?)null, 0, (string?)null)), "only the explicit editor opens, never an automatic saved typed side panel");
    }

    [Test]
    public void AShortenedEditorBufferKeepsEditingUntilExplicitUseAndHasNoAutomaticSidePanel()
    {
        var (_, _, screen, draft) = Asking();
        OpenInput(screen, LongTyped("Teal and grey, for the reason"), 12);
        Assert.That(screen.Question.InputReading, Is.True);
        screen.Question.ChangeInput("Teal");
        screen.Question.MeasureInput(1);
        Assert.That((screen.Question.InputParts, screen.Question.SideOption), Is.EqualTo((1, (int?)null)));
        Assert.That(screen.Question.InputRead, Is.False, "the changed short words must also be drawn");
        Draw(screen);
        Assert.That(screen.Question.UseInput(stopOnPage: false), Is.True);
        Assert.That(draft.Typed(0), Is.EqualTo("Teal"));
        screen.Question.MeasureTyped(0, 4, 12);
        Assert.That(screen.Question.SideOption, Is.Null, "measuring saved words differently never opens an unsolicited side panel");
    }

    [Test]
    public void TypedWordsChangedForAnotherPromptOpenNothingBesideThePromptInView()
    {
        var (_, _, screen, draft) = Asking();
        screen.Question.NextQuestion(Later(screen));
        Assert.That(screen.Question.Prompt, Is.EqualTo(1));
        Draw(screen);
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
        Assert.That(FileScreens.WhyNextWaits(screen.Question), Is.EqualTo(FileScreens.ReadTheAnswer), "one part drawn is one part read");
    }

    [Test]
    public void AQuestionOfOnePromptSendsOnlyFromItsDistinctReadReview()
    {
        var (_, workspace, screen, draft) = Asking(OnePrompt("Postgres", "SQLite"));
        var frame = Screen(workspace, screen);
        Assert.That(frame.Lines.Select(line => line.Words), Has.None.EqualTo(FileScreens.YourAnswers));
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(FileScreens.NextQuestion));
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Available, Is.False);
        Assert.That(frame.Reason, Is.EqualTo(FileScreens.ReadTheQuestion));
        ShowAnswers(screen);
        screen.Question.Choose(1);
        frame = Screen(workspace, screen);
        Assert.That((frame.Footer[PromptSlot.FarRight]!.Words, frame.Footer[PromptSlot.FarRight]!.Available), Is.EqualTo((FileScreens.ReviewAnswers, true)));
        var steering = new WorkspaceSteering(factory);
        Assert.That(steering.SendAnswer(screen, workspace).Step, Is.EqualTo(SteeringStep.Explain), "choosing does not bypass review even for one prompt");
        screen.Question.NextQuestion(Later(screen));
        Assert.That(screen.Question.Reviewing, Is.True);
        screen.Question.MeasureReview(new[] { 1 });
        Assert.That(FileScreens.WhySendWaits(screen), Is.EqualTo(FileScreens.ReadYourAnswers));
        Draw(screen);
        Assert.That(FileScreens.WhySendWaits(screen), Is.Null);
        var sent = (ExecutionAnswerQuestionCommand)steering.SendAnswer(screen, workspace).Command!;
        Assert.That(sent.Payload.Answers.Single().Selected, Is.EqualTo(new[] { "SQLite" }));
    }

    [Test]
    public void AQuestionNotYetLaidOutShowsWhatItAsksNeverThatNothingWaits()
    {
        var work = new AskingWork();
        var workspace = FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer, WorkspaceAction.Interrupt);
        var screen = new FileScreen { Section = FileSection.Waiting };
        var frame = Screen(workspace, screen);
        Assert.That(frame.Lines.Select(line => line.Words), Is.EqualTo(new[] { "Which colour scheme should the dashboard use?" }));
        Assert.That(frame.Lines.Single().Runs.AllTheirs, Is.True);
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
    public void ASecretPromptBeginsNoTypingEvenWhereItTakesFreeText()
    {
        var question = AskingWork.Scripted();
        question.Prompts[0].Secret = true;
        Assert.That(question.Prompts[0].FreeText, Is.True, "the adapter says it takes typed words");
        var work = new AskingWork(question);
        var screen = new FileScreen();
        screen.ReadQuestion(new QuestionDraft("e1", work.Question), Short(work.Question), new RowBudget(8), new RowBudget(3));
        ShowAnswers(screen);
        // Typed words would be journaled (ADR 0022), so the question refuses them itself, whatever a frame offers.
        Assert.That(screen.Question.BeginInput(), Is.False);
        Assert.That(screen.Question.Writing, Is.False);
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
        Assert.That(frame.ReasonWaits, Is.True, "waiting for the agent, the page's last line shimmers");
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
        Assert.That(frame.Lines.Select(line => line.Words), Is.EqualTo(new[] { "Question 1 of 2 · Colour scheme: Dark", "Question 2 of 2 · Pages: Orders", "Send Answers?" }));
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
        Assert.That(frame.Lines.Last().Words, Is.EqualTo("Not Sent · Question Changed · Reread"));
        Assert.That(steering.Armed, Is.Null);
    }
}

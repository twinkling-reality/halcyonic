using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The redesign's question through a real file column, as AUDIT probed it (2026-11-10): each answer sent under its own
/// prompt's key as chosen, a changed mind, and a question replaced or withdrawn under its confirmation never answered.
/// </summary>
public class RoomQuestionTests
{
    private readonly CommandFactory factory = new(Samples.Client);

    private FileColumn Column(FileMenuHost host, Func<WorkspacePresentation?> present) => new(host, present, factory, () => null, _ => null, () => "");

    private static void Draw(FileMenuHost host, FileColumn column)
    {
        column.Drawn(column.Frame!, sidePanel: null);
        host.Wait(1);
    }

    private static RoomBar Bar(FileColumn column) => RoomBar.Of(KitFileBinding.Of(column.Frame!, column.Steering.Armed, column.Now!, "", new ConnectionStatus(ConnectionPhase.Live), "Storefront",
        new[] { new KitSpineSection(new KitRowModel[0]) }, roomDesign: true)!, VoiceStage.Idle, false);

    private static string Show(RoomBar bar) => bar.State + " | panel: " + bar.Body.Text + " | says: " + bar.Sentence + " | band: " + bar.Band + " | " +
        string.Join(", ", bar.Column.Select(action => action.Words + (action.Chosen ? "[x]" : "") + (action.Main ? "*" : "")));

    private static void Press(FileColumn column, RoomAction action) => column.Act(action.Control.Action, action.Control.Key);

    private static RoomAction Named(RoomBar bar, string words) => bar.Column.Single(action => action.Words == words);

    private static QuestionView Two(string id, string first = "Which colour scheme should the dashboard use?") => new()
    {
        QuestionId = id, Answerable = true, AskedAt = Samples.Time,
        Prompts = new List<QuestionPrompt>
        {
            new() { Key = "q0", Header = "Colour scheme", Text = first, Multiple = false, FreeText = false, Secret = false,
                Options = new List<QuestionOption> { new() { Label = "Light" }, new() { Label = "Dark" } } },
            new() { Key = "q1", Header = "Pages", Text = "Which pages should change?", Multiple = true, FreeText = false, Secret = false,
                Options = new List<QuestionOption> { new() { Label = "Sign in" }, new() { Label = "Orders" }, new() { Label = "Settings" } } },
        },
    };

    private (FileMenuHost, AskingWork, FileColumn) Asking(QuestionView question, bool review = false)
    {
        var host = new FileMenuHost();
        var work = review
            ? new AskingWork(question, new[] { new CommandPolicy { CommandType = CommandType.ExecutionAnswerQuestion, Policy = PolicyCategory.ReviewRequired } })
            : new AskingWork(question);
        var column = Column(host, () => work.Present());
        column.QuestionBudgetOf = () => new ChoiceBudget(RoomBar.ChoicesAPage);
        column.Reflow();
        return (host, work, column);
    }

    [Test]
    public void TwoQuestionsAnswersAreSentEachUnderItsOwnKeyAsChosen()
    {
        var was = RoomDesign.Enabled; RoomDesign.Enabled = true;
        try
        {
            var (host, work, column) = Asking(Two("question-1"));
            Draw(host, column);
            // Press Send before anything is answered: refused.
            column.Act(FileScreens.SendAnswer, null);
            Assert.That(host.Sent.Count, Is.EqualTo(0), "Send Answer before every question is answered");
            Press(column, Named(Bar(column), "Light"));
            Draw(host, column);
            // Change the mind: Dark. The mark must follow.
            Press(column, Named(Bar(column), "Dark"));
            Draw(host, column);
            var first = Bar(column);
            TestContext.Out.WriteLine("q1 after Light then Dark: " + Show(first));
            Assert.That(first.Column.Where(a => a.Chosen).Select(a => a.Words), Is.EqualTo(new[] { "Dark" }));
            Press(column, first.Column.Single(a => a.Main));
            Draw(host, column);
            column.Act(FileScreens.SendAnswer, null);
            Assert.That(host.Sent.Count, Is.EqualTo(0), "Send Answer with the second question unanswered");
            Press(column, Named(Bar(column), "Orders"));
            Draw(host, column);
            Press(column, Named(Bar(column), "Settings"));
            Draw(host, column);
            Press(column, Named(Bar(column), "Settings"));
            Draw(host, column);
            var second = Bar(column);
            TestContext.Out.WriteLine("q2 after Orders, Settings, Settings: " + Show(second));
            Assert.That(second.Column.Where(a => a.Chosen).Select(a => a.Words), Is.EqualTo(new[] { "Orders" }));
            for (var step = 0; step < 6 && host.Sent.Count == 0; step++)
            {
                var bar = Bar(column);
                TestContext.Out.WriteLine("  step: " + Show(bar));
                Press(column, bar.Column.Single(a => a.Main));
                Draw(host, column);
            }
            var sent = (ExecutionAnswerQuestionCommand)host.Sent.Single();
            var answers = sent.Payload.Answers.Select(a => a.Key + "=" + string.Join("+", a.Selected) + (a.Text != null ? " text " + a.Text : "")).ToList();
            TestContext.Out.WriteLine("sent: " + sent.Payload.QuestionId + " " + string.Join("; ", answers));
            Assert.That(answers, Is.EqualTo(new[] { "q0=Dark", "q1=Orders" }));
        }
        finally { RoomDesign.Enabled = was; }
    }

    [Test]
    public void AReplacedOrWithdrawnQuestionIsNeverAnsweredWithTheOldChoices()
    {
        var was = RoomDesign.Enabled; RoomDesign.Enabled = true;
        try
        {
            // Under its confirmation the question is replaced by another with the same prompts: Confirm sends nothing.
            var (host, work, column) = Asking(Two("question-1"), review: true);
            Draw(host, column);
            Press(column, Named(Bar(column), "Dark")); Draw(host, column);
            Press(column, Bar(column).Column.Single(a => a.Main)); Draw(host, column);
            Press(column, Named(Bar(column), "Orders")); Draw(host, column);
            for (var step = 0; step < 6 && column.Steering.Armed == null; step++) { Press(column, Bar(column).Column.Single(a => a.Main)); Draw(host, column); }
            Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Answer));
            var confirm = Bar(column).Column.First(a => a.Control.Place == KitPlace.Yes);
            work.Change(execution => { execution.PendingQuestions.Clear(); execution.PendingQuestions.Add(Two("question-2")); });
            Press(column, confirm);
            column.Reflow();
            TestContext.Out.WriteLine("replaced under confirm: sent " + host.Sent.Count + " | " + Show(Bar(column)));
            Assert.That(host.Sent.Count, Is.EqualTo(0));
            Assert.That(Bar(column).Column.Any(a => a.Chosen), Is.False, "the new question starts unanswered");

            // Withdrawn while its answers stand (before Send): Send sends nothing.
            (host, work, column) = Asking(Two("question-3"));
            Draw(host, column);
            Press(column, Named(Bar(column), "Light")); Draw(host, column);
            Press(column, Bar(column).Column.Single(a => a.Main)); Draw(host, column);
            Press(column, Named(Bar(column), "Sign in")); Draw(host, column);
            work.Change(execution => execution.PendingQuestions.Clear());
            column.Act(FileScreens.SendAnswer, null);
            column.Act(FileScreens.Yes, null);
            column.Reflow();
            TestContext.Out.WriteLine("withdrawn: sent " + host.Sent.Count);
            Assert.That(host.Sent.Count, Is.EqualTo(0));
        }
        finally { RoomDesign.Enabled = was; }
    }

    [Test]
    public void APageNeverOffersMoreActionsThanTheColumnHolds()
    {
        var was = RoomDesign.Enabled; RoomDesign.Enabled = true;
        try
        {
            // The most a page can ask for: more answers than a page holds, a written answer allowed, and a question before it to go back to.
            QuestionOption Option(string label) => new QuestionOption { Label = label };
            var question = new QuestionView
            {
                QuestionId = "question-1", Answerable = true, AskedAt = Samples.Time,
                Prompts = new List<QuestionPrompt>
                {
                    new() { Key = "q0", Header = "First", Text = "Which one first?", Options = new List<QuestionOption> { Option("One"), Option("Two") } },
                    new()
                    {
                        Key = "q1", Header = "Colour", Text = "Which colour?", FreeText = true,
                        Options = new List<QuestionOption> { Option("Red"), Option("Green"), Option("Blue"), Option("Gold"), Option("Silver"), Option("Black") },
                    },
                },
            };
            var host = new FileMenuHost();
            var work = new AskingWork(question);
            var column = new FileColumn(host, () => work.Present(), factory, () => null, _ => null, () => "");
            column.QuestionBudgetOf = () => new ChoiceBudget(RoomBar.ChoicesAPage);
            column.Reflow();
            RoomBar Bar() => RoomBar.Of(KitFileBinding.Of(column.Frame!, column.Steering.Armed, column.Now!, "", new ConnectionStatus(ConnectionPhase.Live), "Storefront",
                new[] { new KitSpineSection(new KitRowModel[0]) }, roomDesign: true)!, VoiceStage.Idle, false);
            void Draw() { column.Drawn(column.Frame!, sidePanel: null); host.Wait(1); }
            Draw();
            column.Act(Bar().Column[0].Control.Action, Bar().Column[0].Control.Key);
            Draw();
            var on = Bar().Column.Single(action => action.Main);
            column.Act(on.Control.Action, on.Control.Key);
            Draw();
            // The second question, an answer chosen: answers, Write Answer and More Choices share the page's four places; then the way on and Back.
            var second = Bar();
            column.Act(second.Column[0].Control.Action, second.Column[0].Control.Key);
            Draw();
            var full = Bar();
            var words = full.Column.Select(action => action.Words).ToList();
            Assert.That(words, Does.Contain("Write Answer").And.Contain("More Choices").And.Contain("Back"), string.Join(", ", words));
            Assert.That(full.Column.Count(action => action.Mark != AnswerMark.None) + 2, Is.EqualTo(RoomBar.ChoicesAPage), "the written answer's row and the row to more choices each take a place");
            Assert.That(full.Column.Count, Is.LessThanOrEqualTo(6), string.Join(", ", words));
        }
        finally { RoomDesign.Enabled = was; }
    }

    private static RoomBar BarOf(FileColumn column) => RoomBar.Of(KitFileBinding.Of(column.Frame!, column.Steering.Armed, column.Now!, "", new ConnectionStatus(ConnectionPhase.Live), "Storefront",
        new[] { new KitSpineSection(new KitRowModel[0]) }, roomDesign: true,
        asking: column.Screen.Question.Draft != null && !column.Screen.Question.Reviewing ? column.Screen.Question.Prompt : (int?)null,
        writing: column.Screen.Question.Writing ? column.Screen.Question.Input : null)!, VoiceStage.Idle, false);

    [Test]
    public void AWrittenAnswerIsTheTypedDraftsMomentAndGoesAsWritten()
    {
        var was = RoomDesign.Enabled; RoomDesign.Enabled = true;
        try
        {
            var question = new QuestionView
            {
                QuestionId = "question-1", Answerable = true, AskedAt = Samples.Time,
                Prompts = new List<QuestionPrompt>
                {
                    new() { Key = "q0", Header = "Colour", Text = "Which colour should the balloon be?", FreeText = true,
                        Options = new List<QuestionOption> { new() { Label = "Red" }, new() { Label = "Gold" } } },
                },
            };
            var host = new FileMenuHost();
            var work = new AskingWork(question);
            var column = new FileColumn(host, () => work.Present(), factory, () => null, _ => null, () => "");
            column.QuestionBudgetOf = () => new ChoiceBudget(RoomBar.ChoicesAPage);
            column.Reflow();
            void Draw() { column.Drawn(column.Frame!, sidePanel: null); host.Wait(1); }
            void Press(RoomAction action) => column.Act(action.Control.Action, action.Control.Key);
            Draw();
            Press(BarOf(column).Column.Single(action => action.Words == "Write Answer"));
            Assert.That(column.Screen.Question.Writing, Is.True);

            // The panel is the field: empty, the caret, only the way out; the prompt stays on the band.
            var empty = BarOf(column);
            Assert.That((empty.State, empty.Caret, empty.Body.Text, empty.Wide, empty.Band), Is.EqualTo((RoomBarState.Question, true, "", false, (string?)"Which colour should the balloon be?")));
            Assert.That(empty.Field!.Action, Is.EqualTo(FileScreens.EditInput), "a press on the panel opens the keyboard again");
            Assert.That(empty.Column.Any(action => action.Main), Is.False, "no Use Answer before a word");
            Assert.That(empty.Column.Select(action => action.Control.Action), Does.Contain(FileScreens.CancelInput));

            // Typed, and drawn: Use Answer arrives, the main action.
            host.Keyboard!.Value.Done("Teal with a gold string");
            Draw();
            var typed = BarOf(column);
            Assert.That((typed.Body.Text, typed.Caret), Is.EqualTo(("Teal with a gold string", true)));
            var use = typed.Column.Single(action => action.Main);
            Assert.That((use.Words, use.Control.Action), Is.EqualTo(("Use Answer", FileScreens.UseInput)));
            Press(use);
            Assert.That(column.Screen.Question.Writing, Is.False);

            // Then the file's own way on, to the one command, carrying the words as written.
            for (var step = 0; step < 6 && host.Sent.Count == 0; step++)
            {
                Draw();
                Press(BarOf(column).Column.Single(action => action.Main));
            }
            var sent = host.Sent.OfType<ExecutionAnswerQuestionCommand>().Single();
            Assert.That(sent.Payload.Answers.Single().Text, Is.EqualTo("Teal with a gold string"));
        }
        finally { RoomDesign.Enabled = was; }
    }

    [Test]
    public void AnswersThatReadAlikeStandOnTheBandAndThePanelHoldsOnlyTheProductsWords()
    {
        var was = RoomDesign.Enabled; RoomDesign.Enabled = true;
        try
        {
            var question = new QuestionView
            {
                QuestionId = "question-1", Answerable = true, AskedAt = Samples.Time,
                Prompts = new List<QuestionPrompt>
                {
                    new() { Key = "q0", Header = "Colour", Text = "Which colour?",
                        Options = new List<QuestionOption> { new() { Label = "Red", Description = "The bright one" }, new() { Label = "Red", Description = "The dark one" }, new() { Label = "Gold" } } },
                },
            };
            var host = new FileMenuHost();
            var work = new AskingWork(question);
            var column = new FileColumn(host, () => work.Present(), factory, () => null, _ => null, () => "");
            column.QuestionBudgetOf = () => new ChoiceBudget(RoomBar.ChoicesAPage);
            column.Reflow();
            void Draw() { column.Drawn(column.Frame!, sidePanel: null); host.Wait(1); }
            Draw();
            var asked = BarOf(column);
            // An answer that reads as another cannot be chosen: it stands with no mark, and its press says why.
            var alike = asked.Column.First(action => action.Control.Action == FileScreens.ReadWhy);
            Assert.That(alike.Mark, Is.EqualTo(AnswerMark.None));
            column.Act(alike.Control.Action, alike.Control.Key);
            Assert.That(column.Screen.Question.ExplainingAmbiguity, Is.True);

            var why = BarOf(column);
            Assert.That((why.State, why.Body.Text, why.Sentence, why.Wide), Is.EqualTo((RoomBarState.Question, "Same Name · Not Selectable", (string?)"Duplicate Choices", false)),
                "only the product's own words in the panel");
            Assert.That(why.Band, Does.Contain("Red").And.Contain("/"), "the answers, the agent's words, on the band");
            Assert.That(why.Body.Text, Does.Not.Contain("Red"));
            Assert.That(why.Column.Select(action => action.Control.Action), Does.Contain(FileScreens.BackAmbiguity));
            Draw();
            column.Act(FileScreens.BackAmbiguity, null);
            Assert.That(column.Screen.Question.ExplainingAmbiguity, Is.False);
        }
        finally { RoomDesign.Enabled = was; }
    }

    [Test]
    public void OnlyStopIsTakenFromAFrameThatIsShownButNotYetReportedDrawn()
    {
        var was = RoomDesign.Enabled;
        RoomDesign.Enabled = true;
        try
        {
            // A question that cannot be answered here (it asks for a secret): its frame offers Stop and Close. The host
            // holds back the frame's draw while the band's later parts are unread, so nothing here is reported drawn.
            var secret = AskingWork.Scripted();
            secret.Prompts[0].Secret = true;
            var host = new FileMenuHost { Submissions = new CommandSubmissions() };
            var work = new AskingWork(secret);
            var column = new FileColumn(host, () => work.Present(host.Live, host.Submissions), factory, () => null, _ => null, () => "");
            var navigator = new MenuNavigator(MenuBar.Places.ToDictionary(place => place, _ => (Func<IMenuColumn>)(() => column)));
            navigator.ShowBeside(column, "w1");
            var bar = new MenuBar(MenuPlace.Tasks, "A question is waiting");
            var frame = navigator.Frames(bar).Beside!;
            Assert.That(frame.Footer.All.Select(each => each.Prompt.Id), Does.Contain(FileScreens.Stop).And.Contain(Footer.Close));

            Assert.That(navigator.Act(MenuColumn.File, Footer.Close, null, frame, null), Is.False, "Close waits for the draw");
            Assert.That(navigator.Beside, Is.SameAs(column));
            Assert.That(navigator.Act(MenuColumn.File, FileScreens.Stop, null, null, null), Is.False, "Stop from no frame");
            Assert.That(navigator.Act(MenuColumn.File, FileScreens.Stop, null, frame, null), Is.True, "Stop never waits for reading");
            Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Interrupt), "and it only asks for its confirmation");
            Assert.That(host.Sent, Is.Empty);

            // The confirmation is a new frame: the old frame's Stop is gone, and its Yes waits for its own draw.
            var confirm = navigator.Frames(bar).Beside!;
            Assert.That(confirm, Is.Not.SameAs(frame));
            Assert.That(navigator.Act(MenuColumn.File, FileScreens.Stop, null, frame, null), Is.False, "a frame no longer shown gives nothing");
            Assert.That(navigator.Act(MenuColumn.File, FileScreens.Yes, null, confirm, null), Is.False, "the confirmation is read before it is answered");
            Assert.That(host.Sent, Is.Empty);
            navigator.Drawn(MenuColumn.File, confirm, null);
            host.Wait(1); column.Tick();
            confirm = navigator.Frames(bar).Beside!;
            navigator.Drawn(MenuColumn.File, confirm, null);
            Assert.That(navigator.Act(MenuColumn.File, FileScreens.Yes, null, confirm, null), Is.True);
            Assert.That(host.Sent.Single(), Is.InstanceOf<ExecutionInterruptCommand>());

            // An answerable question: no answer is chosen from a frame not reported drawn.
            var asking = new AskingWork(Two("question-2"));
            var answers = new FileColumn(host, () => asking.Present(host.Live, host.Submissions), factory, () => null, _ => null, () => "");
            var second = new MenuNavigator(MenuBar.Places.ToDictionary(place => place, _ => (Func<IMenuColumn>)(() => answers)));
            second.ShowBeside(answers, "w1");
            var asked = second.Frames(bar).Beside!;
            var choice = asked.Lines.First(line => line.Choice && line.Available);
            Assert.That(second.Act(MenuColumn.File, choice.Action!, choice.Key, asked, null), Is.False, "an answer waits for the draw");
            Assert.That(answers.Screen.Question.Draft!.AnswersNow.SelectMany(answer => answer.Chosen), Is.Empty);
        }
        finally { RoomDesign.Enabled = was; }
    }
}

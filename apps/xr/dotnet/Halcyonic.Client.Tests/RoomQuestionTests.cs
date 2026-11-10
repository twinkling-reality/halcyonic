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
}

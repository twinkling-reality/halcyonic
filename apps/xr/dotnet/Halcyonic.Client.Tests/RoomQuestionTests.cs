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

    /// <summary>The answers chosen, wherever they stand: the column's buttons, or the card's rows.</summary>
    private static IEnumerable<string> Chosen(RoomBar bar) => bar.Column.Where(action => action.Chosen).Select(action => action.Words)
        .Concat(bar.Card?.List.Where(row => row.Press != null && row.Chosen).Select(row => row.Name.Text) ?? Enumerable.Empty<string>());

    /// <summary>The action named <paramref name="words"/>: a column's button, or an answer's row on the card (where the column would pass four in all).</summary>
    private static RoomAction Named(RoomBar bar, string words) => bar.Column.SingleOrDefault(action => action.Words == words)
        ?? new RoomAction(bar.Card!.List.Single(row => row.Press != null && row.Name.Text == words).Press!, main: false);

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

    private static QuestionView Seven(string id, bool described) => new()
    {
        QuestionId = id, Answerable = true, AskedAt = Samples.Time,
        Prompts = new List<QuestionPrompt>
        {
            new() { Key = "q0", Header = "Scene approach", Text = "Which approach should the scene take?", Multiple = false, FreeText = true, Secret = false,
                Options = Enumerable.Range(1, 7).Select(index => new QuestionOption
                {
                    Label = "Option " + index, Description = described ? "What option " + index + " does, in a sentence the agent wrote." : null,
                }).ToList() },
        },
    };

    /// <summary>
    /// One pager (the lead's 22:04Z; SCREENS's and XR's 22:21Z): a question whose answers carry descriptions comes from the file as ONE
    /// part, every answer in it, and the panel's card pages them by the kit's own line count; plain answers stay in the column, paged by
    /// the file's places as before.
    /// </summary>
    [Test]
    public void ADescribedQuestionComesAsOnePartAndAPlainOneIsPagedByPlaces()
    {
        var was = RoomDesign.Enabled; RoomDesign.Enabled = true;
        try
        {
            var (_, _, described) = Asking(Seven("question-described", described: true));
            Assert.That(described.Frame!.Lines.Count(line => line.Choice), Is.EqualTo(7), "every described answer in the one part");
            Assert.That(described.Frame.Lines.Select(line => line.Words), Has.None.EqualTo("More Choices"));
            var (_, _, plain) = Asking(Seven("question-plain", described: false));
            Assert.That(plain.Frame!.Lines.Count(line => line.Choice), Is.LessThan(7), "plain answers paged by the column's places");
            // A mixed question (every agent app's options may come with or without a description): one part all the same, one pager.
            var mixed = Seven("question-mixed", described: true);
            mixed.Prompts.Insert(0, new QuestionPrompt { Key = "q-plain", Header = "Pages", Text = "Which pages?", Multiple = false, FreeText = false, Secret = false,
                Options = Enumerable.Range(1, 7).Select(index => new QuestionOption { Label = "Page " + index }).ToList() });
            var (_, _, both) = Asking(mixed);
            Assert.That(both.Frame!.Lines.Count(line => line.Choice), Is.EqualTo(7), "the plain prompt too, in one part");
            Assert.That(both.Frame.Lines.Select(line => line.Words), Has.None.EqualTo("More Choices"));
        }
        finally { RoomDesign.Enabled = was; }
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
            Assert.That(Chosen(first), Is.EqualTo(new[] { "Dark" }));
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
            Assert.That(Chosen(second), Is.EqualTo(new[] { "Orders" }));
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
            // The second question, an answer chosen: its answers would pass four in all beside Write Answer, More Choices, the way on and
            // Back (the lead's 2026-11-14T00:24Z), so they stand on the card; the file's places still share the page (answers, Write
            // Answer and More Choices); the column holds the prompts, four in all.
            var second = Bar();
            Assert.That(second.Card, Is.Not.Null, Show(second));
            var pick = second.Card!.List[0].Press!;
            column.Act(pick.Action, pick.Key);
            Draw();
            var full = Bar();
            var words = full.Column.Select(action => action.Words).ToList();
            Assert.That(words, Does.Contain("Write Answer").And.Contain("More Choices").And.Contain("Back"), string.Join(", ", words));
            Assert.That(full.Card!.List.Count + 2, Is.EqualTo(RoomBar.ChoicesAPage), "the written answer's row and the row to more choices each take a place");
            Assert.That(full.Column.Any(action => action.Control.Place == KitPlace.Choice), Is.False, string.Join(", ", words));
            Assert.That(full.Column.Count, Is.LessThanOrEqualTo(RoomForm.SpecActions), string.Join(", ", words));
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
            Assert.That((why.State, why.Body.Text, why.Sentence, why.Wide), Is.EqualTo((RoomBarState.Question, "These share a name, so none can be chosen", (string?)"Duplicate Choices", false)),
                "only the product's own words in the panel, one plain phrase, never two facts on a dot (AUDIT2's 2026-11-14T05:45Z)");
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
    public void OnlyStopAndTheWaysOutAreTakenFromAFrameThatIsShownButNotYetReportedDrawn()
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

            Assert.That(navigator.Act(MenuColumn.File, FileScreens.Stop, null, null, null), Is.False, "Stop from no frame");
            Assert.That(navigator.Act(MenuColumn.File, FileScreens.Stop, null, frame, null), Is.True, "Stop never waits for reading");
            Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Interrupt), "and it only asks for its confirmation");
            Assert.That(host.Sent, Is.Empty);

            // The confirmation is a new frame: the old frame's Stop is gone, and its Yes, which decides, waits for its own draw.
            var confirm = navigator.Frames(bar).Beside!;
            Assert.That(confirm, Is.Not.SameAs(frame));
            Assert.That(navigator.Act(MenuColumn.File, FileScreens.Stop, null, frame, null), Is.False, "a frame no longer shown gives nothing");
            Assert.That(navigator.Act(MenuColumn.File, FileScreens.Yes, null, confirm, null), Is.False, "the confirmation is read before it is answered");
            Assert.That(host.Sent, Is.Empty);
            // A way out never waits (the lead, 2026-11-13T19:34Z): its Cancel, by its declared kind, is taken undrawn and sends nothing.
            var cancel = confirm.Footer.All.Single(each => each.Prompt.Kind == PromptKind.Cancel).Prompt.Id;
            Assert.That(navigator.Act(MenuColumn.File, cancel, null, confirm, null), Is.True, "Cancel never waits for the draw");
            Assert.That(column.Steering.Armed, Is.Null);
            Assert.That(host.Sent, Is.Empty);
            frame = navigator.Frames(bar).Beside!;
            Assert.That(navigator.Act(MenuColumn.File, FileScreens.Stop, null, frame, null), Is.True);
            confirm = navigator.Frames(bar).Beside!;
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

            // A person can always leave: the file's Close, by its declared kind, is taken from a frame not reported drawn, where a
            // frame that could not be laid whole would otherwise hold the person and the recording's holds (AUDIT, 2026-11-13T18:05Z).
            var leaving = new FileColumn(host, () => new AskingWork(secret).Present(host.Live, host.Submissions), factory, () => null, _ => null, () => "");
            var third = new MenuNavigator(MenuBar.Places.ToDictionary(place => place, _ => (Func<IMenuColumn>)(() => leaving)));
            third.ShowBeside(leaving, "w1");
            var undrawn = third.Frames(bar).Beside!;
            Assert.That(undrawn.Footer.All.Single(each => each.Prompt.Id == Footer.Close).Prompt.Kind, Is.EqualTo(PromptKind.Close));
            Assert.That(third.Act(MenuColumn.File, Footer.Close, null, null, null), Is.False, "Close from no frame");
            third.Moved();
            Assert.That(third.Act(MenuColumn.File, Footer.Close, null, undrawn, null), Is.True, "Close waits neither for the draw nor for a redraw after a move");
            Assert.That(third.Beside, Is.Null);
        }
        finally { RoomDesign.Enabled = was; }
    }

    /// <summary>
    /// An agent's answers that carry a description are choices read in the PANEL (the lead's 2026-11-13T22:04Z; the shape of the
    /// paid run's question of 16:44Z: a header, two options each with a sentence, one marked recommended in its own words, free
    /// text allowed): each a pressed row of the card with its mark and its description under it, at the panel's width, the
    /// column keeping the prompts and never an answer, so it never widens with the agent's words; a press of a row chooses.
    /// </summary>
    [Test]
    public void DescribedAnswersAreChosenInThePanelAndTheColumnKeepsThePrompts()
    {
        // Under the redesign the file hands an answer's description apart, as its quieter line (FileScreens.AnswerRow).
        var was = RoomDesign.Enabled;
        RoomDesign.Enabled = true;
        try { Described(); }
        finally { RoomDesign.Enabled = was; }
    }

    private void Described()
    {
        var question = new QuestionView
        {
            QuestionId = "described", Answerable = true, AskedAt = Samples.Time,
            Prompts = new List<QuestionPrompt>
            {
                new() { Key = "q0", Header = "Storage", Text = "Where should the saved games live?", Multiple = false, FreeText = true, Secret = false,
                    Options = new List<QuestionOption>
                    {
                        new() { Label = "Local file (Recommended)", Description = "Keeps every save on this computer, with nothing to set up." },
                        new() { Label = "Cloud sync", Description = "Keeps saves in step across devices, and needs an account." },
                    } },
            },
        };
        var (host, _, column) = Asking(question);
        Draw(host, column);
        var bar = Bar(column);
        Assert.That(bar.Card, Is.Not.Null, Show(bar));
        Assert.That(bar.Card!.List.Select(row => (row.Name.Text, row.Under?.Text, row.Mark, row.Press != null)), Is.EqualTo(new[]
        {
            ("Local file (Recommended)", (string?)"Keeps every save on this computer, with nothing to set up.", AnswerMark.Single, true),
            ("Cloud sync", "Keeps saves in step across devices, and needs an account.", AnswerMark.Single, true),
        }), "each answer its row, the agent's own words, its description under it");
        var built = RoomBar.AnswerRow(LineWords.Theirs("Local file (Recommended)"), LineWords.TheirName("Keeps every save on this computer, with nothing to set up."), AnswerMark.Single, false);
        Assert.That((bar.Card.List[0].Name.Text, bar.Card.List[0].Under!.Text, bar.Card.List[0].Mark, bar.Card.List[0].Label, bar.Card.List[0].Note),
            Is.EqualTo((built.Name.Text, built.Under!.Text, built.Mark, built.Label, built.Note)), "the row the file's budget measures is the row the reader draws");
        Assert.That(bar.Column.Any(action => action.Control.Place == KitPlace.Choice), Is.False, "no answer in the column: " + Show(bar));
        Assert.That(bar.Column.Count, Is.LessThanOrEqualTo(RoomForm.SpecActions), Show(bar));

        // A press of a row chooses it, as a column answer did.
        column.Act(bar.Card.List[1].Press!.Action, bar.Card.List[1].Press!.Key);
        Draw(host, column);
        Assert.That(Bar(column).Card!.List.Select(row => row.Chosen), Is.EqualTo(new[] { false, true }), Show(Bar(column)));

        // Plain short answers stay in the column.
        var (plainHost, _, plain) = Asking(Two("plain"));
        Draw(plainHost, plain);
        Assert.That(Bar(plain).Column.Count(action => action.Control.Place == KitPlace.Choice), Is.EqualTo(2), Show(Bar(plain)));
    }

    /// <summary>
    /// A described question's card pages ITSELF (XR's and AUDIT's 22:21Z: one pager, the kit's count): seven described answers handed
    /// in one part lay eight lines a page by the plane's measure of a list row, every answer reached once and in the file's order; on
    /// every page Write Answer, the pager before the ways out, and no answer in the column; the bar whole, and the press that sends
    /// held, only once every page was shown.
    /// </summary>
    [Test]
    public void ADescribedQuestionsCardPagesItselfWithItsWaysOnEveryPage()
    {
        var was = RoomDesign.Enabled;
        RoomDesign.Enabled = true;
        try
        {
            var question = new QuestionView
            {
                QuestionId = "seven", Answerable = true, AskedAt = Samples.Time,
                Prompts = new List<QuestionPrompt>
                {
                    new() { Key = "q0", Header = "Scene", Text = "Which scene should the game open on?", Multiple = false, FreeText = true, Secret = false,
                        Options = Enumerable.Range(1, 7).Select(n => new QuestionOption { Label = "Scene " + n, Description = "What scene " + n + " shows first, in a sentence." }).ToList() },
                },
            };
            var host = new FileMenuHost();
            var work = new AskingWork(question);
            var column = Column(host, () => work.Present());
            column.QuestionBudgetOf = () => new ChoiceBudget(7);
            column.Reflow();
            Draw(host, column);
            var form = RoomForm.Full.Measured((_, _) => 1).MeasuredList(row => 1 + (row.Under == null ? 0 : 2));
            var bar = RoomBar.Of(KitFileBinding.Of(column.Frame!, column.Steering.Armed, column.Now!, "", new ConnectionStatus(ConnectionPhase.Live), "Storefront",
                new[] { new KitSpineSection(new KitRowModel[0]) }, roomDesign: true)!, VoiceStage.Idle, false, form: form);
            Assert.That(bar.GeneralPart.Pages, Is.GreaterThan(1), Show(bar));
            var reached = new List<string>();
            for (var page = 0; page < bar.GeneralPart.Pages; page++)
            {
                Assert.That(bar.Whole, Is.EqualTo(page == bar.GeneralPart.Pages - 1 && page > 0 || bar.GeneralPart.Pages == 1), "whole only once every page was shown (page " + (page + 1) + ")");
                Assert.That((bar.Card!.Title == null ? 0 : 1) + bar.Card.List.Sum(row => 1 + (row.Under == null ? 0 : 2)), Is.LessThanOrEqualTo(RoomForm.SpecLines), "page " + (page + 1));
                Assert.That(bar.Column.Any(action => action.Control.Place == KitPlace.Choice), Is.False, Show(bar));
                Assert.That(bar.Column.Any(action => action.Words == "Write Answer"), Is.True, "Write Answer on page " + (page + 1) + ": " + Show(bar));
                Assert.That(bar.Column.Any(action => action.Control.Action == RoomBar.TurnPage), Is.True, "the pager on page " + (page + 1));
                reached.AddRange(bar.Card.List.Select(row => row.Name.Text));
                bar.Turn();
            }
            // Every answer the file handed in this part, once, in its order (all seven once the file hands a described prompt in one part,
            // XR's half of the 22:21Z, pinned by its own test).
            Assert.That(reached, Is.EqualTo(column.Frame!.Lines.Where(line => line.Choice).Select(line => line.Words)), "every answer once, in the file's order");
            Assert.That(reached.Count, Is.GreaterThan(RoomCard.ListAPage - 1), "more than one page of described answers");
        }
        finally { RoomDesign.Enabled = was; }
    }

    /// <summary>
    /// Which question this is stands apart on the described answers' card, never joined on a dot (FIELD's 03:32Z KitRender.Check:
    /// "Question 2 of 2 ·" ended a line): the number its title, how many may be chosen under it; the choices' count is the pager's own.
    /// </summary>
    [Test]
    public void SeveralQuestionsCardSaysWhichQuestionAndHowManyApartNeverOnADot()
    {
        var was = RoomDesign.Enabled;
        RoomDesign.Enabled = true;
        try
        {
            var question = new QuestionView
            {
                QuestionId = "two", Answerable = true, AskedAt = Samples.Time,
                Prompts = new List<QuestionPrompt>
                {
                    new() { Key = "q0", Header = "Scene", Text = "Which scene should the game open on?", Multiple = true, FreeText = false, Secret = false,
                        Options = new List<QuestionOption> { new() { Label = "Beach", Description = "Sand and waves" }, new() { Label = "Forest", Description = "Tall trees" } } },
                    new() { Key = "q1", Header = "Sound", Text = "Should it play music?", Multiple = false, FreeText = false, Secret = false,
                        Options = new List<QuestionOption> { new() { Label = "Yes" }, new() { Label = "No" } } },
                },
            };
            var host = new FileMenuHost();
            var work = new AskingWork(question);
            var column = Column(host, () => work.Present());
            Draw(host, column);
            var model = KitFileBinding.Of(column.Frame!, column.Steering.Armed, column.Now!, "", new ConnectionStatus(ConnectionPhase.Live), "Storefront",
                new[] { new KitSpineSection(new KitRowModel[0]) }, roomDesign: true)!;
            // The two facts come apart from the page itself, never by a reader splitting a sentence on its dot (AUDIT's 03:45Z).
            Assert.That((model.Model.Ask!.Caption, model.Model.Ask.CaptionHow), Is.EqualTo(("Question 1 of 2", (string?)"Choose Any")));
            var bar = RoomBar.Of(model, VoiceStage.Idle, false);
            Assert.That(bar.Card, Is.Not.Null, Show(bar));
            Assert.That((bar.Card!.Title?.Text, bar.Card.Description?.Text), Is.EqualTo(("Question 1 of 2", (string?)"Choose Any")), Show(bar));
            Assert.That(bar.Card.Title!.Text + (bar.Card.Description?.Text ?? "") + bar.Card.List.Aggregate("", (said, row) => said + row.Name.Text), Does.Not.Contain("·"));
        }
        finally { RoomDesign.Enabled = was; }
    }

    /// <summary>
    /// The mixed question (XR's 22:26Z: descriptions are optional in every agent app, so one prompt may be plain and another described;
    /// the file then hands the whole question in one part): the plain prompt's seven answers would pass the kit's column, so they
    /// stand on the card too, never a column past it and never a second pager (no More Choices beside the card's Next Page).
    /// </summary>
    [Test]
    public void AMixedQuestionsPlainSevenAnswersStandOnTheCardWithOnePager()
    {
        var was = RoomDesign.Enabled;
        RoomDesign.Enabled = true;
        try
        {
            var question = new QuestionView
            {
                QuestionId = "mixed", Answerable = true, AskedAt = Samples.Time,
                Prompts = new List<QuestionPrompt>
                {
                    new() { Key = "q0", Header = "Level", Text = "Which level comes first?", Multiple = false, FreeText = false, Secret = false,
                        Options = Enumerable.Range(1, 7).Select(n => new QuestionOption { Label = "Level " + n }).ToList() },
                    new() { Key = "q1", Header = "Scene", Text = "Which scene should open it?", Multiple = false, FreeText = false, Secret = false,
                        Options = new List<QuestionOption> { new() { Label = "Beach", Description = "Opens on the beach at dawn." }, new() { Label = "Forest", Description = "Opens in the forest at dusk." } } },
                },
            };
            var host = new FileMenuHost();
            var work = new AskingWork(question);
            var column = Column(host, () => work.Present());
            column.QuestionBudgetOf = () => new ChoiceBudget(RoomBar.ChoicesAPage);
            column.Reflow();
            Draw(host, column);
            var bar = Bar(column);
            Assert.That(bar.Card, Is.Not.Null, "the plain prompt's seven answers stand on the card: " + Show(bar));
            Assert.That(bar.Column.Any(action => action.Control.Place == KitPlace.Choice), Is.False, Show(bar));
            Assert.That(bar.Column.Count, Is.LessThanOrEqualTo(RoomBar.ColumnMost), Show(bar));
            Assert.That(bar.Column.Count(action => action.Control.Action is RoomBar.TurnPage || action.Words == "More Choices"), Is.LessThanOrEqualTo(1), "one pager: " + Show(bar));
            var reached = new List<string>();
            for (var page = 0; page < bar.GeneralPart.Pages; page++)
            {
                reached.AddRange(bar.Card!.List.Select(row => row.Name.Text));
                bar.Turn();
            }
            Assert.That(reached, Is.EqualTo(Enumerable.Range(1, 7).Select(n => "Level " + n)), "every answer once, in order");
        }
        finally { RoomDesign.Enabled = was; }
    }
}

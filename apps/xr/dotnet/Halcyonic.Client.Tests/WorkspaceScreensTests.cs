using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class QuestionPlaceTests
{
    private static QuestionDraft Draft(QuestionView? question = null) => new("e1", question ?? AskingWork.Scripted());

    [Test]
    public void TheQuestionShowsAStepAtATimeTextFirstThenItsAnswersTwoToAPage()
    {
        var place = new QuestionPlace();
        var draft = Draft();
        place.Show(draft);
        place.Measured(new[] { 2, 1 });
        // Colour scheme: two parts of text, then a second page for Type an answer; Pages: one part, two pages of three labels.
        Assert.That(place.Steps, Is.EqualTo(5));
        Assert.That((place.Prompt, place.TextPart, place.AnswerPage), Is.EqualTo((0, 0, 0)));
        Assert.That(place.Answers, Is.EqualTo(new[] { 0, 1 }), "the answers stand under every part of the text");
        Assert.That(draft.WasShownWhole(0), Is.False, "the first of two parts is not the whole question");

        place.Turn(1);
        Assert.That((place.Prompt, place.TextPart, place.AnswerPage), Is.EqualTo((0, 1, 0)));
        Assert.That(draft.WasShownWhole(0), Is.True, "its last part showed");
        place.Turn(1);
        Assert.That((place.Prompt, place.TextPart, place.AnswerPage), Is.EqualTo((0, 1, 1)), "the last part stays while the answers page");
        Assert.That(place.Answers, Is.EqualTo(new[] { 2 }), "Type an answer, after the two labels");
        place.Turn(1);
        Assert.That((place.Prompt, place.TextPart, place.AnswerPage), Is.EqualTo((1, 0, 0)), "on to the next prompt");
        Assert.That(draft.WasShownWhole(1), Is.True, "a prompt of one part shows whole at once");
        place.Turn(5);
        Assert.That((place.Step, place.AnswerPage, place.Answers), Is.EqualTo((4, 1, new[] { 2 })), "kept within the question");
        place.Turn(-9);
        Assert.That(place.Step, Is.Zero);
    }

    [Test]
    public void TheSameDraftKeepsThePersonsPlaceAndAnotherStartsAfresh()
    {
        var place = new QuestionPlace();
        var draft = Draft();
        place.Show(draft);
        place.Measured(new[] { 1, 1 });
        place.Turn(2);
        place.Show(draft);
        place.Measured(new[] { 1, 1 });
        Assert.That(place.Step, Is.EqualTo(2), "redrawn, the place is kept");
        place.Measured(new[] { 1, 1 });
        place.Show(Draft());
        Assert.That(place.Step, Is.Zero, "another draft is another question, or the same asked again");
        Assert.That(place.Draft, Is.Not.SameAs(draft));
    }

    [Test]
    public void AQuestionHalcyonicCannotAnswerPagesOnlyThroughItsText()
    {
        var question = AskingWork.Scripted();
        question.Answerable = false;
        var place = new QuestionPlace();
        place.Show(Draft(question));
        place.Measured(new[] { 3, 1 });
        Assert.That(place.Steps, Is.EqualTo(4));
        Assert.That(place.Answers, Is.Empty);
        place.Turn(3);
        Assert.That((place.Prompt, place.Answers.Count), Is.EqualTo((1, 0)));
    }
}

public class WorkspaceScreensTests
{
    private readonly CommandFactory factory = new(Samples.Client);

    private static PanelModel Screen(WorkspacePresentation workspace, WorkspaceSteering steering, WorkspaceScreen? screen = null) =>
        WorkspaceScreens.Screen(workspace, steering, screen ?? new WorkspaceScreen());

    private static WaitingWork Running()
    {
        var work = new WaitingWork();
        work.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.Status = ExecutionStatus.Running;
        }, WorkstreamStatus.Running);
        return work;
    }

    private static string[] Titles(PanelModel model) => model.Rows.Select(row => row.Title).ToArray();

    /// <summary>The work as an agent app that takes instructions while it runs would offer it, as OpenCode does; the mock takes them only at rest.</summary>
    private static WorkspacePresentation Offering(WorkspacePresentation workspace, params WorkspaceAction[] actions) =>
        new(workspace.Character, workspace.Objective, workspace.Execution, workspace.Runtime, actions,
            actions.Where(action => workspace.RequiresConfirmation(action)).ToList(), workspace.Commands, workspace.Activity);

    [Test]
    public void TheHeaderNamesTheWorkItsGoalAndItsStateAndOnlyCloseStandsBesideIt()
    {
        var work = new WaitingWork();
        work.Workstream.Objective = "Migrate the database";
        var model = Screen(work.Present(), new WorkspaceSteering(factory));
        Assert.That((model.Title, model.TitleIsData), Is.EqualTo((work.Workstream.Title, true)));
        Assert.That((model.Context, model.ContextIsData), Is.EqualTo(("Goal: Migrate the database", true)));
        Assert.That(model.Badge!.Word, Is.EqualTo("Waiting for you"));
        Assert.That(model.Marks.Select(mark => mark.Word), Is.EqualTo(new[] { "Practice" }), "simulated work says so beside its state");
        Assert.That((model.Movable, model.Notice), Is.EqualTo((false, (string?)null)), "it stays beside its character: Close only");

        var screen = new WorkspaceScreen { Notice = "Nothing was sent: you didn't confirm in time. Press it again." };
        Assert.That(Screen(work.Present(), new WorkspaceSteering(factory), screen).Notice, Is.EqualTo(screen.Notice));
    }

    [Test]
    public void WaitingForYouIsTheFirstTabAndOnlyWhileSomethingWaits()
    {
        var work = new WaitingWork();
        var screen = new WorkspaceScreen { Question = WorkspaceQuestion.NeedFromYou };
        var model = Screen(work.Present(), new WorkspaceSteering(factory), screen);
        Assert.That(model.Tabs.Select(tab => (tab.Id, tab.Label, tab.Chosen, tab.Attention)), Is.EqualTo(new[]
        {
            ("waiting", "Waiting for you", true, true),
            ("doing", "Doing", false, false),
            ("understand", "Understand", false, false),
            ("checked", "Checked", false, false),
        }));
        foreach (var tab in model.Tabs) Assert.That(WorkspaceScreens.QuestionOf(tab.Id), Is.Not.Null, tab.Id);

        work.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.Status = ExecutionStatus.Running;
        }, WorkstreamStatus.Running);
        model = Screen(work.Present(), new WorkspaceSteering(factory), screen);
        Assert.That(model.Tabs.Select(tab => tab.Label), Is.EqualTo(new[] { "Doing", "Understand", "Checked" }));
        Assert.That(model.Tabs.Single(tab => tab.Chosen).Label, Is.EqualTo("Doing"), "the answered request's tab gives way to Doing");
        Assert.That(model.Heading, Is.EqualTo(WorkspaceText.WhatIsItDoing));
    }

    [Test]
    public void EachTabsWholeQuestionHeadsItsAnswer()
    {
        var work = new WaitingWork();
        foreach (var (question, heading) in new[]
        {
            (WorkspaceQuestion.Doing, "What is it doing?"),
            (WorkspaceQuestion.Understand, "Help me understand"),
            (WorkspaceQuestion.Checked, "What was checked?"),
            (WorkspaceQuestion.NeedFromYou, "What do you need from me?"),
        })
        {
            var model = Screen(work.Present(), new WorkspaceSteering(factory), new WorkspaceScreen { Question = question });
            Assert.That(model.Heading, Is.EqualTo(heading), question.ToString());
            var section = question == WorkspaceQuestion.Understand || question == WorkspaceQuestion.Checked;
            Assert.That(model.CustomBody && model.CustomBodyIsText, Is.EqualTo(section), "a section draws its own lines, only words");
            Assert.That(model.HeadingAction?.Label, Is.EqualTo(section ? "Refresh" : null), "Refresh reads a section's source again");
        }
    }

    [Test]
    public void WhatIsItDoingAnswersThenSaysWhatWasSentThenLogsWhatItDid()
    {
        var work = Running();
        var present = work.Present();
        var activity = Enumerable.Range(1, 6)
            .Select(index => new ActivityEntry(index, "2026-09-26T09:00:0" + index + ".000Z", ActivityKind.Message, "line " + index, reported: index == 5))
            .ToList();
        var commands = new[] { "Sent. Waiting for it to stop…", "Confirmed: it has your instruction.", "Confirmed: started." }
            .Select((text, index) => new CommandFeedback("c" + index, CommandType.ExecutionInterrupt, null, text))
            .ToList();
        var workspace = new WorkspacePresentation(present.Character, present.Objective, present.Execution, present.Runtime,
            present.Actions, Array.Empty<WorkspaceAction>(), commands, activity);
        var model = Screen(workspace, new WorkspaceSteering(factory), new WorkspaceScreen { ActivityNote = " (times in UTC)" });
        Assert.That(model.Heading, Is.EqualTo(WorkspaceText.WhatIsItDoing));
        Assert.That(Titles(model).Take(2), Is.EqualTo(WorkspaceText.Answer(workspace)), "one plain answer first");
        Assert.That(model.Rows.Take(2).Select(row => row.Tone), Is.All.EqualTo(GlazeTone.Neutral), "quiet while nothing waits");
        Assert.That(Titles(model).Skip(2).Take(2), Is.EqualTo(commands.Take(2).Select(command => command.Text)), "what this headset sent, newest first");
        Assert.That(Titles(model)[4], Is.EqualTo("Recent activity (times in UTC)"));
        var log = model.Rows.Skip(5).ToList();
        Assert.That(log.Select(row => row.Title), Is.EqualTo(new[] { "09:00:03  line 3", "09:00:04  line 4", "09:00:05  It says: “line 5”", "09:00:06  line 6" }),
            "the newest four, oldest first, the newest last");
        Assert.That(log.Select(row => row.Claim), Is.EqualTo(new[] { false, false, true, false }), "the agent's words lean");
        Assert.That(model.Rows.Skip(4).All(row => row.Droppable && row.Line), Is.True, "older lines give way where there is no room; the log never pages");
        Assert.That(model.Rows.Take(4).Any(row => row.Droppable), Is.False, "the answer and what was sent always stay");
        Assert.That(model.Rows.Select(row => row.Continues), Is.EqualTo(new[] { false, true, false, true, false, true, true, true, true }));

        var quiet = Screen(Running().Present(), new WorkspaceSteering(factory));
        Assert.That(Titles(quiet), Does.Contain(WorkspaceText.NothingSentYet));
    }

    [Test]
    public void TheApprovalSaysWhatItWantsTheRequestAndWhatEachAnswerDoes()
    {
        var work = new WaitingWork();
        var model = Screen(work.Present(), new WorkspaceSteering(factory), new WorkspaceScreen { Question = WorkspaceQuestion.NeedFromYou });
        Assert.That(Titles(model), Is.EqualTo(new[] { "It wants to run a command:", "Run the migration" }.Concat(WorkspaceText.NeedFromYou(work.Present())!.Notes)));
        Assert.That(model.Rows[0].Tone, Is.EqualTo(GlazeTone.Attention));
        Assert.That((model.Rows[1].TitleIsData, model.Rows[1].TitleLines, model.Rows[1].Continues), Is.EqualTo((true, 3, true)));
        Assert.That(model.Rows.All(row => row.Line && !row.Pressable), Is.True, "only words; Approve and Deny are on the bar");
    }

    [Test]
    public void TheBarHoldsStopAtItsLeftAndWhatTheWorkLeadsToAtItsRight()
    {
        var steering = new WorkspaceSteering(factory);
        var mock = Screen(new WaitingWork().Present(), steering, new WorkspaceScreen { Speak = true }).Actions;
        Assert.That(mock.All.Select(action => action.Label), Is.EqualTo(new[] { "Stop", "Deny", "Approve" }), "the mock takes no instruction while it waits");
        var all = new[] { WorkspaceAction.Approve, WorkspaceAction.Deny, WorkspaceAction.Interrupt, WorkspaceAction.Instruct };
        var waiting = Screen(Offering(new WaitingWork().Present(), all), steering, new WorkspaceScreen { Speak = true }).Actions;
        Assert.That(waiting.All.Select(action => (action.Id, action.Label, action.Role)), Is.EqualTo(new[]
        {
            (WorkspaceScreens.Stop, "Stop", PanelActionRole.Destructive),
            (WorkspaceScreens.Deny, "Deny", PanelActionRole.Secondary),
            (WorkspaceScreens.TellIt, "Tell it", PanelActionRole.Secondary),
            (WorkspaceScreens.Approve, "Approve", PanelActionRole.Primary),
        }), "hold to talk is left out where Deny and Tell it fill the bar");

        var instructable = Offering(Running().Present(), WorkspaceAction.Interrupt, WorkspaceAction.Instruct);
        var running = Screen(instructable, steering, new WorkspaceScreen { Speak = true }).Actions;
        Assert.That(running.All.Select(action => action.Label), Is.EqualTo(new[] { "Stop", "Hold to talk", "Tell it" }));
        Assert.That((running.Primary!.Label, running.Secondary.Single().Holds), Is.EqualTo(("Tell it", true)), "Tell it leads while nothing waits");
        Assert.That(Screen(instructable, steering).Actions.All.Select(action => action.Label), Is.EqualTo(new[] { "Stop", "Tell it" }),
            "hold to talk only where it is offered");

        var away = Screen(new WaitingWork().Present(live: false), steering);
        Assert.That(away.Actions.All, Is.Empty);
        Assert.That(away.BarNote, Is.EqualTo("Nothing can be sent until your Mac reconnects."));
        foreach (var action in waiting.All) Assert.That(WorkspaceScreens.ActionOf(action.Id), Is.Not.Null, action.Id);
        Assert.That(WorkspaceScreens.ActionOf(WorkspaceScreens.HoldToTalk), Is.Null);
    }

    [Test]
    public void AnAnswerInFlightLeavesAnInertSentWhereSendAnswerStood()
    {
        var work = new AskingWork();
        var submissions = new CommandSubmissions();
        var draft = new QuestionDraft("e1", work.Question);
        draft.Choose(0, "Dark");
        draft.Choose(1, "Orders");
        draft.ShownWhole(0);
        draft.ShownWhole(1);
        var answer = new WorkspaceSteering(factory).SendAnswer(draft, work.Present()).Command!;
        _ = submissions.SubmitAsync(_ => new System.Threading.Tasks.TaskCompletionSource<CommandAckMessage>().Task, answer, "e1");
        var workspace = WorkspacePresenter.Present(work.Workstream, work.State, new ActivityLog(), true, submissions);
        var bar = Screen(workspace, new WorkspaceSteering(factory), new WorkspaceScreen { Speak = true }).Actions;
        Assert.That(bar.All.Select(action => action.Label), Is.EqualTo(new[] { "Stop", "Sent…" }));
        Assert.That((bar.Primary!.Id, bar.Primary.Available, bar.Primary.Reason), Is.EqualTo((WorkspaceScreens.Sent, false, (string?)null)), "it takes no press");
        Assert.That(WorkspaceScreens.ActionOf(WorkspaceScreens.Sent), Is.Null);

        var open = Screen(work.Present(), new WorkspaceSteering(factory)).Actions;
        Assert.That(open.All.Select(action => action.Label), Is.EqualTo(new[] { "Stop", "Send answer" }));
        var instructable = Offering(work.Present(), WorkspaceAction.Answer, WorkspaceAction.Interrupt, WorkspaceAction.Instruct);
        Assert.That(Screen(instructable, new WorkspaceSteering(factory), new WorkspaceScreen { Speak = true }).Actions.All.Select(action => action.Label),
            Is.EqualTo(new[] { "Stop", "Hold to talk", "Tell it", "Send answer" }), "Tell it gives way to the answer, hold to talk beside it");
    }

    [Test]
    public void AnArmedApprovalShowsTheWholeRequestInPartsAndYesOnlyOnceItShowed()
    {
        var work = new WaitingWork();
        var steering = new WorkspaceSteering(factory);
        Assert.That(steering.Press(WorkspaceAction.Approve, work.Present()).Step, Is.EqualTo(SteeringStep.Confirm));
        var screen = new WorkspaceScreen { Question = WorkspaceQuestion.NeedFromYou, RequestParts = new[] { "bash: Run the ", "migration" }, RequestLines = 5 };
        var model = Screen(work.Present(), steering, screen);
        Assert.That(model.Tabs, Is.Empty, "the request shows in the tabs' place");
        Assert.That((Titles(model).Single(), model.Rows[0].TitleLines), Is.EqualTo(("bash: Run the ", 5)));
        Assert.That((model.Parts, model.PartsCaption), Is.EqualTo(((0, 2), "The whole request, part 1 of 2")));
        Assert.That(model.Confirm!.Question, Is.EqualTo(WorkspaceText.ReadRequestFirst));
        Assert.That((model.Confirm.Yes.Label, model.Confirm.Yes.Available), Is.EqualTo(("Read to part 2 first", false)));
        Assert.That((model.Confirm.Cancel.Id, model.Confirm.Cancel.Label), Is.EqualTo((WorkspaceScreens.Cancel, "Cancel")));
        Assert.That(model.Actions.All, Is.Empty, "the confirm step stands in the bar's place");

        screen.RequestPart = 1;
        steering.RequestShown(2, 2);
        model = Screen(work.Present(), steering, screen);
        Assert.That(Titles(model).Single(), Is.EqualTo("migration"));
        Assert.That((model.Confirm!.Question, model.Confirm.Yes.Label, model.Confirm.Yes.Available, model.Confirm.Yes.Role),
            Is.EqualTo(("Approve the request above?", "Yes, approve", true, PanelActionRole.Primary)));

        steering.Cancel();
        Assert.That(steering.Press(WorkspaceAction.Deny, work.Present()).Step, Is.EqualTo(SteeringStep.Confirm));
        Assert.That(Screen(work.Present(), steering, screen).Confirm!.Yes.Available, Is.True, "Deny needs no reading");
    }

    [Test]
    public void StoppingAsksInTheBarAndItsYesIsSolidRed()
    {
        var work = Running();
        var steering = new WorkspaceSteering(factory);
        Assert.That(steering.Press(WorkspaceAction.Interrupt, work.Present()).Step, Is.EqualTo(SteeringStep.Confirm));
        var model = Screen(work.Present(), steering);
        Assert.That(model.Tabs, Is.Not.Empty, "nothing pages, so the tabs stay");
        Assert.That((model.Confirm!.Yes.Label, model.Confirm.Yes.Role), Is.EqualTo(("Yes, stop", PanelActionRole.Destructive)));
        Assert.That(model.Confirm.Question, Does.StartWith("Stop what it's doing now?"));
        Assert.That(model.Parts, Is.Null);
    }

    [Test]
    public void TypingSaysWhatToDoAndPresetsShowInPlaceOfTheTab()
    {
        var work = Offering(Running().Present(), WorkspaceAction.Interrupt, WorkspaceAction.Instruct);
        var steering = new WorkspaceSteering(factory);
        Assert.That(steering.Press(WorkspaceAction.Instruct, work).Step, Is.EqualTo(SteeringStep.Type));
        var typing = Screen(work, steering);
        Assert.That((typing.BarNote, typing.Actions.All.Single().Label), Is.EqualTo((WorkspaceText.TypingPrompt, "Cancel")));

        steering.StopTyping();
        var presets = Screen(work, steering, new WorkspaceScreen { Presets = WorkspaceText.PresetInstructions });
        Assert.That(Titles(presets), Is.EqualTo(new[] { "Continue", "Summarize", "Run checks again" }));
        Assert.That(presets.Rows.All(row => row.Pressable && !row.TitleIsData && row.Action == WorkspaceScreens.Preset), Is.True);
        Assert.That((presets.Columns, presets.Heading, presets.BarNote), Is.EqualTo((2, (string?)null, (string?)null)), "shown without a line");
        Assert.That(presets.Actions.All.Single().Id, Is.EqualTo(WorkspaceScreens.Cancel));

        var recorded = Screen(work, steering, new WorkspaceScreen { Presets = new[] { new PresetInstruction("Add a test", "Add a test.") } });
        Assert.That(recorded.Rows.Single().TitleIsData, Is.True, "the recording's own instructions are its text, not Halcyonic's");
    }

    [Test]
    public void TheAgentsQuestionShowsAPartOfItsTextOverAPageOfItsAnswers()
    {
        var work = new AskingWork();
        var draft = new QuestionDraft("e1", work.Question);
        var screen = new WorkspaceScreen { Question = WorkspaceQuestion.NeedFromYou, Speak = true };
        screen.ReadQuestion(draft, new[] { new[] { "Which colour scheme should the dashboard use?" }, new[] { "Which pages should change?" } });
        var model = Screen(work.Present(), new WorkspaceSteering(factory), screen);
        Assert.That(model.Heading, Is.Null, "no room for the tab's question: the pager's heading names the prompt");
        Assert.That(Titles(model), Is.EqualTo(new[]
        {
            "Which colour scheme should the dashboard use?",
            "Light · Dark text on a light background",
            "Dark · Light text on a dark background",
        }));
        Assert.That((model.Columns, model.Rows[0].TitleLines, model.Rows[0].TitleIsData), Is.EqualTo((2, WorkspaceScreens.QuestionLines, true)));
        Assert.That(model.Rows.Skip(1).All(row => row.Action == WorkspaceScreens.Choose && row.TitleLines == 2), Is.True);
        Assert.That((model.PartsHeading, model.PartsNote), Is.EqualTo(("Question 1 of 2 · Colour scheme", "Choose one, or type your own.")));
        Assert.That((model.Parts, model.PartsCaption), Is.EqualTo(((0, 4), "1 of 4")));

        draft.Choose(0, "Dark");
        screen.Place.Turn(1);
        model = Screen(work.Present(), new WorkspaceSteering(factory), screen);
        var typed = model.Rows.Last();
        Assert.That((typed.Title, typed.Action, typed.Side!.Label, typed.Side.Holds), Is.EqualTo(("Type an answer", WorkspaceScreens.TypeAnswer, "Hold to talk", true)));

        // One Hold to talk a screen: beside the answers it speaks the answer, so the bar offers none of its own.
        var instructable = Offering(work.Present(), WorkspaceAction.Answer, WorkspaceAction.Interrupt, WorkspaceAction.Instruct);
        var asking = Screen(instructable, new WorkspaceSteering(factory), screen);
        Assert.That(asking.Actions.All.Select(action => action.Label), Is.EqualTo(new[] { "Stop", "Tell it", "Send answer" }));
        Assert.That(asking.Rows.Count(row => row.Side?.Holds == true), Is.EqualTo(1));
        screen.Question = WorkspaceQuestion.Doing;
        Assert.That(Screen(instructable, new WorkspaceSteering(factory), screen).Actions.All.Select(action => action.Label),
            Is.EqualTo(new[] { "Stop", "Hold to talk", "Tell it", "Send answer" }), "away from the question, hold to talk speaks an instruction");
        screen.Question = WorkspaceQuestion.NeedFromYou;
        screen.Place.Turn(-1);
        model = Screen(work.Present(), new WorkspaceSteering(factory), screen);
        Assert.That((model.Rows[2].Title, model.Rows[2].Chosen), Is.EqualTo(("Chosen: Dark · Light text on a dark background", true)), "chosen in words as well");
    }

    [Test]
    public void AQuestionHalcyonicCannotAnswerSaysWhyInsteadOfOfferingAnswers()
    {
        var question = AskingWork.Scripted();
        question.Answerable = false;
        question.Prompts[0].Secret = true;
        var work = new AskingWork(question);
        var screen = new WorkspaceScreen { Question = WorkspaceQuestion.NeedFromYou };
        screen.ReadQuestion(new QuestionDraft("e1", question), new[] { new[] { "Which colour scheme should the dashboard use?" }, new[] { "Which pages should change?" } });
        var model = Screen(work.Present(), new WorkspaceSteering(factory), screen);
        Assert.That(Titles(model), Is.EqualTo(new[] { "Which colour scheme should the dashboard use?", WorkspaceText.CannotAnswer(question) }));
        Assert.That(model.PartsNote, Is.EqualTo(WorkspaceText.AgentWaits));
        Assert.That(model.Actions.All.Select(action => action.Label), Is.EqualTo(new[] { "Stop" }), "Stop is the way on");

        // An agent app that takes instructions while it waits: nothing invites typing or saying the secret.
        var instructable = Offering(work.Present(), WorkspaceAction.Interrupt, WorkspaceAction.Instruct);
        Assert.That(Screen(instructable, new WorkspaceSteering(factory), new WorkspaceScreen { Question = WorkspaceQuestion.NeedFromYou, Speak = true })
            .Actions.All.Select(action => action.Label), Is.EqualTo(new[] { "Stop" }), "no Tell it or Hold to talk while a secret is asked for");
        var cut = AskingWork.Scripted();
        cut.Answerable = false;
        cut.Prompts[0].Text = "Which colour scheme [truncated]";
        var cutWork = Offering(new AskingWork(cut).Present(), WorkspaceAction.Interrupt, WorkspaceAction.Instruct);
        Assert.That(Screen(cutWork, new WorkspaceSteering(factory)).Actions.All.Select(action => action.Label), Is.EqualTo(new[] { "Stop", "Tell it" }),
            "a question cut to fit asks for nothing secret");
    }

    [Test]
    public void AnArmedAnswerShowsTheAnswersAboutToBeSent()
    {
        var work = new AskingWork(policies: new[] { new CommandPolicy { CommandType = CommandType.ExecutionAnswerQuestion, Policy = PolicyCategory.ReviewRequired } });
        var draft = new QuestionDraft("e1", work.Question);
        draft.Choose(0, "Dark");
        draft.Type(0, null);
        draft.Choose(1, "Sign in");
        draft.Choose(1, "Settings");
        var screen = new WorkspaceScreen { Question = WorkspaceQuestion.NeedFromYou };
        screen.ReadQuestion(draft, new[] { new[] { "Which colour scheme should the dashboard use?" }, new[] { "Which pages should change?" } });
        screen.Place.Turn(3);
        var steering = new WorkspaceSteering(factory);
        Assert.That(steering.SendAnswer(draft, work.Present()).Step, Is.EqualTo(SteeringStep.Confirm));
        var model = Screen(work.Present(), steering, screen);
        Assert.That(Titles(model), Is.EqualTo(new[] { "Question 1 of 2 · Colour scheme: Dark", "Question 2 of 2 · Pages: Sign in, Settings" }));
        Assert.That((model.Confirm!.Question, model.Confirm.Yes.Label), Is.EqualTo(("Send these answers?", "Yes, send answer")));
    }

    /// <summary>Every action, its confirmation's Yes and Cancel, the heading's Refresh and a row's hold to talk.</summary>
    private static IEnumerable<PanelAction> ActionsOf(PanelModel model)
    {
        foreach (var action in model.Actions.All) yield return action;
        if (model.Confirm != null)
        {
            yield return model.Confirm.Yes;
            yield return model.Confirm.Cancel;
        }
        if (model.HeadingAction != null) yield return model.HeadingAction;
        foreach (var row in model.Rows)
        {
            if (row.Side != null) yield return row.Side;
        }
    }

    [Test]
    public void EachActionShowsItsIconBesideItsWordsAndOnlyHoldToTalkTheMicrophone()
    {
        var steering = new WorkspaceSteering(factory);
        var all = new[] { WorkspaceAction.Approve, WorkspaceAction.Deny, WorkspaceAction.Interrupt, WorkspaceAction.Instruct };
        var waiting = Screen(Offering(new WaitingWork().Present(), all), steering, new WorkspaceScreen { Speak = true }).Actions;
        Assert.That(waiting.All.Select(action => (action.Label, action.Icon)), Is.EqualTo(new (string, GlazeIcon?)[]
        {
            ("Stop", GlazeIcon.Stop), ("Deny", GlazeIcon.Deny), ("Tell it", GlazeIcon.TellIt), ("Approve", GlazeIcon.Approve),
        }));
        var instructable = Offering(Running().Present(), WorkspaceAction.Interrupt, WorkspaceAction.Instruct);
        var running = Screen(instructable, steering, new WorkspaceScreen { Speak = true }).Actions;
        Assert.That(running.All.Select(action => (action.Label, action.Icon)), Is.EqualTo(new (string, GlazeIcon?)[]
        {
            ("Stop", GlazeIcon.Stop), ("Hold to talk", GlazeIcon.HoldToTalk), ("Tell it", GlazeIcon.TellIt),
        }));

        // Each confirmation's Yes shows its own action's icon, or the lock while the request is unread, and Cancel the close icon.
        var models = new List<PanelModel>();
        var work = new WaitingWork();
        var request = new WorkspaceScreen { Question = WorkspaceQuestion.NeedFromYou, RequestParts = new[] { "bash: Run the ", "migration" }, RequestLines = 5 };
        steering.Press(WorkspaceAction.Approve, work.Present());
        models.Add(Screen(work.Present(), steering, request));
        Assert.That((models[^1].Confirm!.Yes.Label, models[^1].Confirm!.Yes.Icon), Is.EqualTo(("Read to part 2 first", (GlazeIcon?)GlazeIcon.Locked)));
        steering.RequestShown(2, 2);
        request.RequestPart = 1;
        models.Add(Screen(work.Present(), steering, request));
        Assert.That(models[^1].Confirm!.Yes.Icon, Is.EqualTo(GlazeIcon.Approve));
        steering.Cancel();
        steering.Press(WorkspaceAction.Deny, work.Present());
        models.Add(Screen(work.Present(), steering, request));
        Assert.That(models[^1].Confirm!.Yes.Icon, Is.EqualTo(GlazeIcon.Deny));
        steering.Cancel();
        var stopping = new WorkspaceSteering(factory);
        stopping.Press(WorkspaceAction.Interrupt, Running().Present());
        models.Add(Screen(Running().Present(), stopping));
        Assert.That(models[^1].Confirm!.Yes.Icon, Is.EqualTo(GlazeIcon.Stop));
        var hearing = new WorkspaceSteering(factory);
        Assert.That(hearing.Spoken("Add a test", instructable).Step, Is.EqualTo(SteeringStep.Confirm));
        models.Add(Screen(instructable, hearing, new WorkspaceScreen { Speak = true }));
        Assert.That((models[^1].Confirm!.Yes.Label, models[^1].Confirm!.Yes.Icon), Is.EqualTo(("Yes, tell it", (GlazeIcon?)GlazeIcon.TellIt)),
            "an instruction the Mac heard is confirmed with Tell it's icon, not the microphone");
        foreach (var model in models) Assert.That(model.Confirm!.Cancel.Icon, Is.EqualTo(GlazeIcon.Close));

        // The microphone only on hold to talk, which is always held, and on nothing that approves, denies, stops or confirms.
        var asking = new AskingWork();
        models.Add(Screen(Offering(asking.Present(), WorkspaceAction.Answer, WorkspaceAction.Interrupt, WorkspaceAction.Instruct), new WorkspaceSteering(factory),
            new WorkspaceScreen { Question = WorkspaceQuestion.NeedFromYou, Speak = true }));
        models.Add(Screen(Offering(Running().Present(), WorkspaceAction.Interrupt, WorkspaceAction.Instruct), new WorkspaceSteering(factory),
            new WorkspaceScreen { Question = WorkspaceQuestion.Understand, Speak = true }));
        Assert.That(models[^1].HeadingAction!.Icon, Is.EqualTo(GlazeIcon.Refresh));
        var actions = models.SelectMany(ActionsOf).Concat(waiting.All).Concat(running.All).ToList();
        Assert.That(actions.Where(action => action.Holds), Is.Not.Empty);
        foreach (var action in actions)
        {
            Assert.That(action.Icon == GlazeIcon.HoldToTalk, Is.EqualTo(action.Holds), action.Label);
            Assert.That(action.Label, Is.Not.Empty, "an icon never stands in for the words");
        }
    }
}

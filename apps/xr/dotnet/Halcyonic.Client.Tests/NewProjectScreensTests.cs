using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class NewProjectScreensTests
{
    private static ProjectIdea Asked(out CompanionExchange exchange, CompanionReply? reply = null)
    {
        var idea = new ProjectIdea();
        idea.UseIdea("something for my running club");
        exchange = idea.BeginCompanion(CompanionStart.Idea);
        exchange.Ask(CompanionWant.Next);
        exchange.Replied(exchange.Generation, Companions.Response(reply ?? Companions.Ask()));
        return idea;
    }

    private static MenuFrame Questions(ProjectIdea idea, bool voice = true, double waited = 0, CompanionRecording? recording = null) =>
        NewProjectScreens.Questions(idea, startReached: false, voice: voice, said: null, waitedSeconds: waited, recording: recording);

    private static IEnumerable<PageLine> Answers(MenuFrame frame) => frame.Lines.Where(line => line.Choice);

    /// <summary>Lane V's measure: at 18 dp a footer about 38 degrees wide holds Close, one other prompt and the main action.</summary>
    private static void HoldsThreePrompts(MenuFrame frame)
    {
        var prompts = frame.Footer.All.ToList();
        Assert.That(prompts, Has.Count.InRange(1, 3));
        Assert.That(prompts[0].Prompt.Kind, Is.EqualTo(PromptKind.Close), "Close is always far left");
        Assert.That(prompts.Any(each => each.Prompt.Id == EntryScreens.Back), Is.False, "the steps are the way back");
    }

    [Test]
    public void TheStepsAreReachedInOrderAndTheShownOneIsLit()
    {
        var idea = new ProjectIdea();
        var steps = NewProjectScreens.Sections(NewProjectStep.YourIdea, idea, startReached: false);
        Assert.That(steps.Select(step => step.Words), Is.EqualTo(new[] { "Your idea", "Questions", "Recap", "Start building" }));
        Assert.That(steps.Select(step => step.Reached), Is.EqualTo(new[] { true, false, false, false }));
        Assert.That(steps.Single(step => step.Chosen).Key, Is.EqualTo(NewProjectScreens.Key(NewProjectStep.YourIdea)));
        Assert.That(steps.Any(step => step.Waits), Is.False, "New project waits for no one; the Tasks place keeps its own dot");

        idea.UseIdea("something for my running club");
        steps = NewProjectScreens.Sections(NewProjectStep.Recap, idea, startReached: true);
        Assert.That(steps.Select(step => step.Reached), Is.EqualTo(new[] { true, false, true, true }), "no questions were begun, so there are none to go back to");
        idea.BeginCompanion(CompanionStart.Idea);
        Assert.That(NewProjectScreens.Sections(NewProjectStep.Recap, idea, startReached: false).Select(step => step.Reached),
            Is.EqualTo(new[] { true, true, true, false }), "Start building only once nothing stops it");
        foreach (var step in NewProjectScreens.Steps)
            Assert.That(NewProjectScreens.StepOf(NewProjectScreens.Key(step)), Is.EqualTo(step));
        Assert.That(NewProjectScreens.StepOf("tasks"), Is.Null);
    }

    [Test]
    public void TheSubjectIsTheIdeaInThePersonsWordsOrTheProjectATaskIsFor()
    {
        Assert.That(NewProjectScreens.Subject(new ProjectIdea()), Is.EqualTo((EntryText.CreateProject, false)));
        var idea = new ProjectIdea();
        idea.UseIdea("A page for my running club's race times");
        Assert.That(NewProjectScreens.Subject(idea), Is.EqualTo(("A page for my running club's race times", true)));
        Assert.That(NewProjectScreens.Subject(new ProjectIdea("proj_1", "Race Times")), Is.EqualTo(("New task in Race Times", true)));
    }

    [Test]
    public void YourIdeaOffersTypingAndOneWayToFigureItOutWithHoldToTalkBesideTheMainAction()
    {
        var available = new AvailableCompanion { Companion = new CompanionModel { Name = "local-model:tag", Served = "this_mac" }, MaxQuestions = 4 };
        var idea = new ProjectIdea();
        var frame = NewProjectScreens.YourIdea(idea, IdeaRow.None, startReached: false, voice: true, said: null, companion: available);
        HoldsThreePrompts(frame);
        Assert.That(frame.Lines[0].Words, Is.EqualTo(EntryText.IdeaPrompt));
        Assert.That(Answers(frame).Select(line => line.Words), Is.EqualTo(new[] { EntryText.TypeIdea, CompanionText.TalkItThrough }));
        Assert.That(frame.Footer[PromptSlot.Secondary]!.Holds, Is.True);
        Assert.That(Answers(frame).Single(line => line.Chosen).Action, Is.EqualTo(NewProjectScreens.ChooseCompanion),
            "before there is an idea, the way to figure it out is chosen, so the main action can be taken");
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.BeginCompanion));
        Assert.That(frame.Footer.All.All(each => each.Prompt.Available), Is.True);
        Assert.That(NewProjectScreens.YourIdea(idea, IdeaRow.Typed, startReached: false, voice: true, said: null, companion: available)
            .Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.BeginCompanion), "no recap from an idea not typed yet");
        Assert.That(frame.Source, Is.Null, "nothing here came from outside");

        frame = NewProjectScreens.YourIdea(idea, IdeaRow.Companion, startReached: false, voice: false, said: null, companion: available);
        Assert.That(frame.Footer[PromptSlot.Secondary], Is.Null, "no hold to talk where voice isn't offered");
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo(CompanionText.TalkItThroughShort));
        Assert.That(Answers(frame).Single(line => line.Chosen).Action, Is.EqualTo(NewProjectScreens.ChooseCompanion));

        idea.UseIdea("something for my running club");
        frame = NewProjectScreens.YourIdea(idea, IdeaRow.None, startReached: false, voice: true, said: VoiceText.HeardNote, companion: available);
        var typed = Answers(frame).First();
        Assert.That(typed.Words, Is.EqualTo("something for my running club"));
        Assert.That(typed.WordsAreData && typed.Chosen, Is.True, "the idea, once typed or heard, is chosen while nothing else is");
        Assert.That(frame.Lines.Any(line => line.Words == VoiceText.HeardNote), Is.True);
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Available, Is.True);
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo(CompanionText.MakeTheRecap));
    }

    [Test]
    public void WithoutTheCompanionYourIdeaOffersTheFixedQuestionsAndSaysWhy()
    {
        var unavailable = new UnavailableCompanion { Reason = new ErrorInfo { Code = "companion_not_set_up", Message = "x" } };
        var frame = NewProjectScreens.YourIdea(new ProjectIdea(), IdeaRow.FixedQuestions, startReached: false, voice: true, said: null, companion: unavailable);
        Assert.That(Answers(frame).Select(line => line.Words), Is.EqualTo(new[] { EntryText.TypeIdea, EntryText.AnswerQuestions }));
        Assert.That(frame.Lines.Any(line => line.Words == CompanionText.NotSetUp), Is.True);
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.BeginQuestions));

        var available = new AvailableCompanion { Companion = new CompanionModel { Name = "local-model:tag", Served = "this_mac" }, MaxQuestions = 4 };
        var task = NewProjectScreens.YourIdea(new ProjectIdea("proj_1", "Race Times"), IdeaRow.Companion, startReached: false, voice: true, said: null,
            companion: available);
        Assert.That(task.Lines[0].Words, Is.EqualTo(EntryText.WorkPrompt));
        Assert.That(Answers(task).Any(line => line.Action == NewProjectScreens.ChooseCompanion), Is.False, "the companion shapes new projects only");
        Assert.That(task.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.BeginQuestions), "a row not on the page is never acted on");
    }

    [Test]
    public void TheQuestionQuotesTheCompanionItsSuggestionsAreAnswersAndGoOnWithoutItIsTheLast()
    {
        var frame = Questions(Asked(out _));
        HoldsThreePrompts(frame);
        Assert.That(frame.Source, Is.EqualTo(CompanionText.Note), "the note that it is an AI is the page's source line");
        Assert.That(frame.Sections.Single(step => step.Chosen).Words, Is.EqualTo("Questions"));
        var quote = frame.Lines[0];
        Assert.That(quote.Claim && quote.WordsAreData, Is.True);
        Assert.That(quote.Words, Does.StartWith("The companion says: “").And.EndWith("”"));
        Assert.That(quote.Rows, Is.EqualTo(NewProjectScreens.QuoteRows));
        Assert.That(Answers(frame).Select(line => line.Words),
            Is.EqualTo(new[] { "Each runner", "One organiser", CompanionText.TypeAnswer, CompanionText.GoOnWithout }));
        Assert.That(Answers(frame).Take(2).Select(line => line.Key), Is.EqualTo(new[] { "0", "1" }));
        Assert.That(Answers(frame).Any(line => line.Chosen), Is.False);
        Assert.That(frame.Lines.Any(line => line.Icon == GlazeIcon.HoldToTalk), Is.False, "the microphone is only in the footer");
        Assert.That(frame.Footer[PromptSlot.Secondary]!.Holds, Is.True);
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo(CompanionText.MakeTheRecap));
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Available, Is.True, "the idea was said, so the companion has something to work from");
    }

    [Test]
    public void ChoosingAnAnswerSetsTheMainActionAndOnlySendAnswerSendsIt()
    {
        var idea = Asked(out var exchange);
        exchange.Choose(1);
        var frame = Questions(idea);
        Assert.That(Answers(frame).Single(line => line.Chosen).Words, Is.EqualTo("One organiser"));
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.SendAnswer));
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Send answer"));
        Assert.That(exchange.Turns, Has.Count.EqualTo(2), "drawing the page sent nothing");

        exchange.Write("Only me", heard: true);
        frame = Questions(idea);
        var written = Answers(frame).Single(line => line.Chosen);
        Assert.That(written.Words, Is.EqualTo("Only me"));
        Assert.That(written.WordsAreData && written.Action == NewProjectScreens.TypeAnswer, Is.True);
        Assert.That(frame.Lines.Any(line => line.Words == VoiceText.HeardAnswer), Is.True, "what the computer heard is the person's to check");
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.SendAnswer));

        exchange.ChooseWithoutIt();
        frame = Questions(idea);
        Assert.That(Answers(frame).Single(line => line.Chosen).Words, Is.EqualTo(CompanionText.GoOnWithout));
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.MakeRecapFromMyWords));
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Make the recap from my words"));
        Assert.That(frame.Lines.Any(line => line.Words == VoiceText.HeardAnswer), Is.False);
    }

    [Test]
    public void TheQuestionIsNeverCutTheLineDropsFirstAndOnlyNotBuildableIsSaid()
    {
        var shortReply = Companions.Ask("Who enters?");
        shortReply.Line = "Fits a page.";
        Assert.That(NewProjectScreens.Asked(shortReply), Is.EqualTo("The companion says: “Fits a page. Who enters?”"));
        var longLine = Companions.Ask("Who enters the times?");
        longLine.Line = new string('l', 120);
        Assert.That(NewProjectScreens.Asked(longLine), Is.EqualTo("The companion says: “Who enters the times?”"));
        var longest = Companions.Ask(new string('q', 160));
        var frame = Questions(Asked(out _, longest));
        Assert.That(frame.Lines[0].Words, Does.Contain(new string('q', 160)));
        Assert.That(frame.Lines[0].Rows, Is.GreaterThan(NewProjectScreens.QuoteRows), "a question that needs a third row takes it");

        var unclear = Questions(Asked(out _));
        Assert.That(unclear.Lines.Any(line => line.Words == CompanionText.ThinksUnclear), Is.False, "asking already says it is unclear");
        var notBuildable = Companions.Ask("Is this for a computer?");
        notBuildable.View = CompanionView.NotBuildable;
        frame = Questions(Asked(out _, notBuildable));
        Assert.That(frame.Lines[0].Words, Is.EqualTo(CompanionText.ThinksNotBuildable));
        Assert.That(frame.Lines[0].Tone, Is.EqualTo(LineTone.Secondary));
        Assert.That(frame.Lines[1].Words, Is.EqualTo("The companion says: “Is this for a computer?”"), "with its view, the question alone");
    }

    [Test]
    public void WaitingAndFailingKeepThePromptsInTheirPlaces()
    {
        var idea = Asked(out var exchange);
        exchange.Choose(0);
        exchange.SendAnswer();
        var frame = Questions(idea);
        HoldsThreePrompts(frame);
        Assert.That(Answers(frame).Select(line => line.Words), Is.EqualTo(new[] { CompanionText.GoOnWithout }), "only going on without it while a reply comes");
        Assert.That(frame.Footer[PromptSlot.Secondary]!.Available, Is.False);
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Available, Is.False);
        Assert.That(frame.Reason, Is.EqualTo(CompanionText.Waiting));
        Assert.That(Questions(idea, waited: CompanionText.WaitingLongSeconds).Reason, Is.EqualTo(CompanionText.Waiting + " " + CompanionText.WaitingLong));

        exchange.Failed(exchange.Generation, "companion_too_slow");
        frame = Questions(idea);
        HoldsThreePrompts(frame);
        Assert.That(frame.Footer[PromptSlot.Secondary], Is.Null, "nothing to answer after a failure: Try again, or go on without it");
        Assert.That(frame.Lines[0].Words, Is.EqualTo(CompanionText.TooSlow));
        Assert.That(frame.Lines[0].Tone, Is.EqualTo(LineTone.Problem));
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.TryAgain));
        exchange.ChooseWithoutIt();
        Assert.That(Questions(idea).Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.MakeRecapFromMyWords));
    }

    [Test]
    public void TheRecapWaitsForAnAnswerWhenThePersonHasSaidNothingYet()
    {
        var idea = new ProjectIdea();
        var exchange = idea.BeginCompanion(CompanionStart.Help);
        exchange.Ask(CompanionWant.Next);
        exchange.Replied(exchange.Generation, Companions.Response(Companions.Ask()));
        var frame = Questions(idea);
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Available, Is.False);
        Assert.That(frame.Reason, Is.EqualTo(CompanionText.AnswerFirst));
        Assert.That(NewProjectScreens.Subject(idea).Subject, Is.EqualTo(EntryText.CreateProject));
    }

    [Test]
    public void TheRecordedExchangeSaysSoAndOffersOnlyTheRecordedAnswer()
    {
        var recording = CompanionRecording.Parse(CompanionRecordingTests.Sample);
        var idea = new ProjectIdea();
        var exchange = recording.Begin(idea);
        var frame = Questions(idea, recording: recording);
        HoldsThreePrompts(frame);
        Assert.That(frame.Source, Is.EqualTo(CompanionText.Recorded));
        Assert.That(frame.Footer[PromptSlot.Secondary], Is.Null, "no hold to talk in the demonstration");
        Assert.That(Answers(frame).Where(line => line.Pressable).Select(line => line.Words), Is.EqualTo(new[] { "One organiser" }));
        Assert.That(Answers(frame).Any(line => line.Action == NewProjectScreens.TypeAnswer || line.Action == NewProjectScreens.GoOnWithout), Is.False);
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Available, Is.False, "the recording asks for the recap later");
        Assert.That(frame.Reason, Is.EqualTo(CompanionText.ChooseOne));
        Assert.That(exchange.Choose(Answers(frame).ToList().FindIndex(line => line.Pressable)), Is.True);
        Assert.That(Questions(idea, recording: recording).Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.SendAnswer));
        recording.Press(exchange, exchange.Answer!);
        Assert.That(Questions(idea, recording: recording).Footer[PromptSlot.FarRight]!.Available, Is.True, "here the recording asked for the recap");
    }

    [Test]
    public void AFullExchangeSaysSoAndKeepsTheRecap()
    {
        var idea = Asked(out var exchange);
        while (exchange.CanSay)
        {
            exchange.Choose(0);
            exchange.SendAnswer();
            exchange.Replied(exchange.Generation, Companions.Response(Companions.Ask()));
        }
        var frame = Questions(idea);
        Assert.That(frame.Footer[PromptSlot.Secondary]!.Available, Is.False);
        Assert.That(frame.Reason, Is.EqualTo(CompanionText.Full), "hold to talk says why it can't be held");
        Assert.That(frame.Lines.Any(line => line.Words == CompanionText.Full), Is.False, "said once");
        Assert.That(Answers(frame).Where(line => line.Action != NewProjectScreens.GoOnWithout).All(line => !line.Available), Is.True);
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Available, Is.True, "the recap adds no words");
        Assert.That(Questions(idea, voice: false).Lines.Any(line => line.Words == CompanionText.Full), Is.True, "without hold to talk, a line says it");
    }

    [Test]
    public void TheNewWordsSpeakAsAPersonWithoutADashOrAProduct()
    {
        foreach (var words in new[]
                 {
                     CompanionText.TalkItThroughShort, CompanionText.AnswerFirst, CompanionText.MakeTheRecapFromMyWords, CompanionText.RecapFromMyWords,
                     EntryText.AnswerQuestions, EntryText.StartQuestions, EntryText.TypeMyOwn, CompanionText.ChooseOne, CompanionText.SuggestedShort, CompanionText.YourOwnWords,
                 })
        {
            Assert.That(words, Does.Not.Contain("—").And.Not.Contain("!"), words);
            foreach (var brand in new[] { "Mac", "Ollama", "qwen", "OpenCode", "Codex", "Claude" })
                Assert.That(words, Does.Not.Contain(brand), words);
        }
    }
}

public class NewProjectRecapTests
{
    private static readonly CommandFactory Commands = new(Samples.Client);

    private static RuntimeDescriptor Listing()
    {
        var runtime = Samples.MockRuntime();
        runtime.RuntimeId = "local";
        runtime.DisplayName = "Local agent";
        runtime.Synthetic = false;
        runtime.ModelChoice = ModelChoice.Listed;
        runtime.UsesProjectLocation = true;
        return runtime;
    }

    private static RuntimeModel Model(string reference, ModelServed served) =>
        new() { ModelRef = reference, DisplayName = reference, Served = served, ToolCalling = ModelToolCalling.Declared };

    private static NewWorkDraft Draft(params RuntimeModel[] models)
    {
        var draft = new NewWorkDraft(Commands);
        draft.ChooseRuntime(Listing());
        if (models.Length > 0)
        {
            draft.SetModels(new RuntimeModelsResponse { RuntimeId = "local", Result = new AvailableModels { Models = models.ToList() } });
        }
        return draft;
    }

    private static ProjectIdea Proposed()
    {
        var idea = new ProjectIdea();
        idea.UseIdea("something for my running club");
        var exchange = idea.BeginCompanion(CompanionStart.Idea);
        exchange.Ask(CompanionWant.Proposal);
        exchange.Replied(exchange.Generation, Companions.Response(Companions.Propose()));
        idea.UseProposal(exchange.Proposal!.Proposal);
        return idea;
    }

    private static void HoldsThreePrompts(MenuFrame frame)
    {
        var prompts = frame.Footer.All.ToList();
        Assert.That(prompts, Has.Count.InRange(1, 3), "Close, one other prompt and the main action");
        Assert.That(prompts[0].Prompt.Kind, Is.EqualTo(PromptKind.Close));
    }

    private static PageLine Fact(MenuFrame frame, RecapFact fact) =>
        frame.Lines.Single(line => line.Key == NewProjectScreens.FactKey(fact));

    [Test]
    public void EachFactIsARowWithSuggestedBesideWhatTheCompanionSuggested()
    {
        var idea = Proposed();
        var frame = NewProjectScreens.Recap(idea, Draft(), null, live: true, notice: null, problem: EntryText.ChooseWhereFilesLive);
        HoldsThreePrompts(frame);
        Assert.That(frame.Sections.Single(step => step.Chosen).Words, Is.EqualTo("Recap"));
        Assert.That(frame.Sections.Last().Reached, Is.False, "something is missing, so Start building can't be chosen");
        Assert.That(frame.Lines[0].Claim && frame.Lines[0].WordsAreData, Is.True, "the companion's proposal, quoted as its own");
        Assert.That(frame.Lines[0].Words, Does.Contain("The companion says: “That is clear enough to start.”"));
        Assert.That(frame.Source, Is.EqualTo(CompanionText.Note), "its words show, so the note that it is an AI is the source");
        Assert.That(Fact(frame, RecapFact.Name).Words, Is.EqualTo("Race Times"));
        Assert.That(Fact(frame, RecapFact.Name).Fact, Is.EqualTo(CompanionText.SuggestedShort));
        Assert.That(Fact(frame, RecapFact.FirstTask).Fact, Is.EqualTo(CompanionText.SuggestedShort));
        Assert.That(Fact(frame, RecapFact.Folder).Fact, Is.Null);
        Assert.That(frame.Lines.Where(line => line.Key != null).All(line => line.Opens && !line.Choice), Is.True, "each fact opens its side panel");
        Assert.That(frame.Side, Is.Null, "no fact chosen");
        Assert.That(frame.Footer[PromptSlot.Rare]!.Id, Is.EqualTo(NewProjectScreens.StartOver));
        var start = frame.Footer[PromptSlot.FarRight]!;
        Assert.That((start.Id, start.Main, start.Available), Is.EqualTo((NewProjectScreens.StartBuilding, true, false)));
        Assert.That(frame.Reason, Is.EqualTo(EntryText.ChooseWhereFilesLive), "the reason is the page's last line");

        idea.UseOwnWords();
        frame = NewProjectScreens.Recap(idea, Draft(), null, live: true, notice: null, problem: null);
        Assert.That(frame.Lines.Any(line => line.Fact == CompanionText.SuggestedShort), Is.False, "nothing once the value is the person's own");
        Assert.That(frame.Source, Is.Null);
        Assert.That(frame.Lines[0].Words, Is.EqualTo(EntryText.RecapLine));
        Assert.That(frame.Sections.Last().Reached, Is.True);
    }

    [Test]
    public void ChoosingAFactShowsItWholeBesideAndPutsItsChangeBesideClose()
    {
        var idea = Proposed();
        var frame = NewProjectScreens.Recap(idea, Draft(), null, live: true, notice: null, problem: null, chosen: RecapFact.FirstTask);
        HoldsThreePrompts(frame);
        Assert.That(Fact(frame, RecapFact.FirstTask).Chosen, Is.True);
        Assert.That(frame.Side!.Subject, Is.EqualTo(EntryText.FirstTask));
        Assert.That(frame.Side.Facts.Select(fact => (fact.Name, fact.Value)), Is.EqualTo(new[]
        {
            (CompanionText.Suggested, idea.FirstTask), (CompanionText.YourOwnWords, "something for my running club"),
        }));
        Assert.That(frame.Side.Source, Is.EqualTo(CompanionText.Note));
        Assert.That(frame.Footer[PromptSlot.Rare]!.Id, Is.EqualTo(NewProjectScreens.ChangeTask));
        Assert.That(frame.Footer[PromptSlot.Secondary], Is.Null, "no prompt beyond Close, the change and Start building");

        var changes = new Dictionary<RecapFact, (string Id, string Words)>
        {
            [RecapFact.Name] = (NewProjectScreens.Rename, EntryText.Change),
            [RecapFact.Folder] = (NewProjectScreens.ChooseWhere, EntryText.ChooseAnotherFolder),
            [RecapFact.HowItRuns] = (NewProjectScreens.MoreOptions, EntryText.MoreOptions),
        };
        foreach (var (fact, change) in changes)
        {
            var chosen = NewProjectScreens.Recap(idea, Draft(), null, live: true, notice: null, problem: null, chosen: fact);
            Assert.That((chosen.Footer[PromptSlot.Rare]!.Id, chosen.Footer[PromptSlot.Rare]!.Words), Is.EqualTo(change), fact.ToString());
            Assert.That(chosen.Side, Is.Not.Null, fact.ToString());
            Assert.That(NewProjectScreens.FactOf(NewProjectScreens.FactKey(fact)), Is.EqualTo(fact));
        }

        var task = new ProjectIdea("proj_1", "Race Times");
        task.UseIdea("Add a page of results.");
        var forTask = NewProjectScreens.Recap(task, Draft(), null, live: true, notice: null, problem: null, chosen: RecapFact.Name);
        Assert.That(forTask.Lines.Single(line => line.Words == "Race Times").Action, Is.Null, "an existing project's name is not changed here");
        Assert.That(forTask.Side, Is.Null);
        Assert.That(forTask.Footer[PromptSlot.Rare]!.Id, Is.EqualTo(NewProjectScreens.StartOver));
    }

    [Test]
    public void StartOverIsConfirmedInPlaceWithYesWhereNothingStood()
    {
        var frame = NewProjectScreens.Recap(Proposed(), Draft(), null, live: true, notice: null, problem: null, confirmingStartOver: true);
        HoldsThreePrompts(frame);
        Assert.That(frame.Footer.Confirming, Is.True);
        Assert.That(frame.Footer[PromptSlot.Rare]!.Kind, Is.EqualTo(PromptKind.Cancel), "Cancel where Start over was pressed");
        Assert.That(frame.Footer[PromptSlot.Free]!.Id, Is.EqualTo(NewProjectScreens.ConfirmStartOver));
        Assert.That(frame.Footer[PromptSlot.FarRight], Is.Null, "Start building steps aside until it is answered");
        Assert.That(frame.Lines.Last().Words, Is.EqualTo(EntryText.StartOverQuestion));
    }

    [Test]
    public void ASuggestedFirstTaskChangesAmongItsAnswers()
    {
        var idea = Proposed();
        var frame = NewProjectScreens.RecapTask(idea, startReached: false);
        HoldsThreePrompts(frame);
        var answers = frame.Lines.Where(line => line.Choice).ToList();
        Assert.That(answers.Select(line => line.Action), Is.EqualTo(new[] { NewProjectScreens.UseSuggestedTask, NewProjectScreens.UseMyWords, NewProjectScreens.TypeTask }));
        Assert.That(answers[0].Claim && answers[0].Chosen, Is.True, "the companion's suggestion stands, quoted as its own");
        Assert.That(answers[1].Words, Is.EqualTo("something for my running club"));
        Assert.That(answers[2].Words, Is.EqualTo(EntryText.TypeMyOwn));
        Assert.That(frame.Source, Is.EqualTo(CompanionText.Note));
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.Done));

        idea.UseOwnWords();
        Assert.That(NewProjectScreens.RecapTask(idea, startReached: false).Lines.Single(line => line.Chosen).Action, Is.EqualTo(NewProjectScreens.UseMyWords));
        idea.Rewrite("Make one page of race times.");
        var typed = NewProjectScreens.RecapTask(idea, startReached: false).Lines.Single(line => line.Chosen);
        Assert.That((typed.Action, typed.Words, typed.WordsAreData), Is.EqualTo((NewProjectScreens.TypeTask, "Make one page of race times.", true)));
    }

    [Test]
    public void FoldersAreAnswersAndAPlaceNotThereTakesNoPress()
    {
        var root = new LocationRoot
        {
            Path = "/Users/person/Projects", Name = "Projects", Status = LocationRootStatus.Available, FoldersTruncated = false,
            Folders = new List<LocationFolder> { new() { Name = "shop", Path = "/Users/person/Projects/shop" } },
        };
        var listing = new LocationsResponse
        {
            Roots = new List<LocationRoot>
            {
                root,
                new() { Path = "/Volumes/Old", Name = "Old", Status = LocationRootStatus.Missing, Folders = new List<LocationFolder>(), FoldersTruncated = false },
            },
        };
        var idea = new ProjectIdea();
        idea.UseIdea("something for my running club");
        idea.ChooseFolder(ProjectFolder.Existing(root, root.Folders[0]));
        var frame = NewProjectScreens.RecapFolder(idea, startReached: false, listing, problem: null, notice: null);
        HoldsThreePrompts(frame);
        Assert.That(frame.Sections.Single(step => step.Chosen).Words, Is.EqualTo("Recap"), "changing a fact stays on the recap");
        Assert.That(frame.Lines.Where(line => line.Choice).Select(line => (line.Words, line.Chosen, line.Available)), Is.EqualTo(new[]
        {
            ("New folder in Projects", false, true), ("Directly in Projects", false, true), ("shop", true, true), ("Old", false, false),
        }));
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.Done));
        var unread = NewProjectScreens.RecapFolder(idea, startReached: false, null, problem: "timeout", notice: null);
        Assert.That(unread.Lines.Single().Words, Does.Contain("timeout"));
        Assert.That(unread.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.ReadFolders));
        var refused = NewProjectScreens.RecapFolder(idea, startReached: false, listing, problem: null, notice: EntryText.NewFolderRule);
        Assert.That((refused.Lines[0].Words, refused.Lines[0].Tone), Is.EqualTo((EntryText.NewFolderRule, LineTone.Problem)));
    }

    [Test]
    public void HowItRunsListsAgentAppsThenModelsAndAModelElsewhereTakesASecondPress()
    {
        var local = Model("ollama/qwen", ModelServed.ThisMac);
        var remote = Model("hosted/x", ModelServed.Remote);
        var draft = Draft(remote, local);
        var idea = new ProjectIdea();
        var models = NewProjectScreens.RecapOptions(idea, startReached: false, draft, new[] { Listing() }, showModels: true, live: true);
        HoldsThreePrompts(models);
        Assert.That(models.Lines.Where(line => line.Choice).Select(line => line.Key), Is.EqualTo(new[] { "ollama/qwen", "hosted/x" }));
        Assert.That(models.Lines.Single(line => line.Chosen).Fact, Does.StartWith(EntryText.ChosenForYou));
        Assert.That(models.Footer[PromptSlot.Rare]!.Id, Is.EqualTo(NewProjectScreens.ChangeRuntime));
        Assert.That(draft.ChooseModel(remote), Is.False);
        var pending = NewProjectScreens.RecapOptions(idea, startReached: false, draft, new[] { Listing() }, showModels: true, live: true);
        Assert.That(pending.Lines.Single(line => line.Key == "hosted/x").Fact, Is.EqualTo(EntryText.ConfirmElsewhere(remote)));
        Assert.That(pending.Lines.Single(line => line.Chosen).Key, Is.EqualTo("ollama/qwen"), "the first press chooses nothing");

        var runtimes = NewProjectScreens.RecapOptions(idea, startReached: false, new NewWorkDraft(Commands), new[] { Samples.MockRuntime(), Listing() },
            showModels: false, live: true);
        Assert.That(runtimes.Lines.Where(line => line.Choice).Select(line => (line.Words, line.Fact)), Is.EqualTo(new[]
        {
            ("Local agent", EntryText.ListsModels), (EntryText.PracticeRun, EntryText.PracticeDetail),
        }));
        Assert.That(runtimes.Footer[PromptSlot.Rare], Is.Null);
    }
}

public class NewProjectStartTests
{
    private static readonly CommandFactory Commands = new(Samples.Client);

    private static LocationRoot Root() => new()
    {
        Path = "/Users/person/Projects", Name = "Projects", Status = LocationRootStatus.Available, FoldersTruncated = false,
        Folders = new List<LocationFolder>(),
    };

    private static NewWorkDraft Draft()
    {
        var runtime = Samples.MockRuntime();
        runtime.RuntimeId = "local";
        runtime.Synthetic = false;
        runtime.ModelChoice = ModelChoice.None;
        runtime.UsesProjectLocation = true;
        var draft = new NewWorkDraft(Commands);
        draft.ChooseRuntime(runtime);
        draft.Objective = "Add a page.";
        return draft;
    }

    private static ClientProjection With(CommandView command)
    {
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(1);
        snapshot.Commands = new List<CommandView> { command };
        state.ApplySnapshot(snapshot, new StateChanges());
        return state;
    }

    private static ProjectIdea Idea()
    {
        var idea = new ProjectIdea();
        idea.UseIdea("Add a page.");
        return idea;
    }

    private static void HoldsThreePrompts(MenuFrame frame)
    {
        var prompts = frame.Footer.All.ToList();
        Assert.That(prompts, Has.Count.InRange(1, 3), "Close, one other prompt and the main action");
        Assert.That(prompts[0].Prompt.Kind, Is.EqualTo(PromptKind.Close));
    }

    [Test]
    public void StartingOffersTheNextActionItsOutcomeAllows()
    {
        var folder = ProjectFolder.New(Root(), "recipes")!;
        var sequence = new BuildSequence(Draft(), Commands, "Recipes", folder.ToContract());
        var create = sequence.Begin();
        var waiting = NewProjectScreens.Starting(Idea(), sequence, folder);
        HoldsThreePrompts(waiting);
        Assert.That(waiting.Sections.Single(step => step.Chosen).Words, Is.EqualTo(EntryText.StartBuilding));
        Assert.That(waiting.Footer.All.Select(each => each.Prompt.Kind), Is.EqualTo(new[] { PromptKind.Close }), "nothing to press while the computer answers");
        Assert.That(waiting.Lines.Any(line => line.Tone == LineTone.Good), Is.False, "sent is not done");

        sequence.Advance(With(new CommandView
        {
            CommandId = create.CommandId, Status = CommandStatus.Rejected, IssuedAt = Samples.Time, UpdatedAt = Samples.Time,
            Rejection = new CommandRejection { Code = RejectionCode.LocationExists, Message = "There is already a folder named recipes." },
        }));
        var exists = NewProjectScreens.Starting(Idea(), sequence, folder);
        HoldsThreePrompts(exists);
        Assert.That(exists.Lines[2].Tone, Is.EqualTo(LineTone.Problem));
        Assert.That(exists.Footer.All.Select(each => each.Prompt.Id), Is.EqualTo(new[] { Footer.Close, NewProjectScreens.ChooseAnotherFolder, NewProjectScreens.UseThatFolder }));
        Assert.That(NewProjectScreens.Starting(Idea(), sequence, null).Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.TryAgainStart));

        var unknown = new BuildSequence(Draft(), Commands, "Recipes");
        var made = unknown.Begin();
        unknown.AcknowledgementLost(new CommandOutcomeUnknownException(made.CommandId, "The socket closed."));
        unknown.Advance(new ClientProjection());
        var check = NewProjectScreens.Starting(Idea(), unknown, null);
        HoldsThreePrompts(check);
        Assert.That(check.Lines[2].Tone, Is.EqualTo(LineTone.Secondary), "unknown is neither good nor a problem; its words say so");
        Assert.That(check.Footer.All.Select(each => each.Prompt.Id), Is.EqualTo(new[] { Footer.Close, NewProjectScreens.CheckFirst }), "only checking the work first");
    }

    [Test]
    public void ClearingAStartThatMayHaveRunTakesTwoPressesInTwoPlaces()
    {
        var waiting = NewProjectScreens.Unresolved(Idea(), "c-1", null, armed: false, live: true);
        HoldsThreePrompts(waiting);
        Assert.That(waiting.Lines.Select(line => line.Words), Does.Contain(EntryText.Recorded(null)).And.Contain("Reference: c-1"));
        Assert.That(waiting.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.Clear));
        var armed = NewProjectScreens.Unresolved(Idea(), "c-1", new CommandView { CommandId = "c-1", Status = CommandStatus.Failed }, armed: true, live: true);
        HoldsThreePrompts(armed);
        Assert.That(armed.Footer[PromptSlot.Free]!.Id, Is.EqualTo(NewProjectScreens.ConfirmClear), "Yes, clear, in the middle, where nothing stood");
        Assert.That(armed.Footer[PromptSlot.FarRight]!.Kind, Is.EqualTo(PromptKind.Cancel), "Cancel where Clear was pressed");
        Assert.That(armed.Lines.Any(line => line.Words.Contains("may have changed something")), Is.True);
        Assert.That(NewProjectScreens.Unresolved(Idea(), "c-1", null, armed: true, live: false).Footer.All.Count(), Is.EqualTo(1), "nothing to clear while not connected");
    }

    private static IEnumerable<string> Ids(MenuFrame frame) =>
        frame.Footer.All.Select(each => each.Prompt.Id).Concat(frame.Lines.Where(line => line.Action != null).Select(line => line.Action!));

    [Test]
    public void YesStartBuildingCanNeitherAppearNorBePressedBeforeTheLastPartHasShown()
    {
        var review = new NewWorkReview("Project", "Title", "Agent", "Model", "on your computer", "ref", "Objective");
        review.Paginate(review.Items.Select(_ => 2).ToList(), 6);
        Assert.That(review.PageCount, Is.GreaterThan(2));
        for (var page = 0; page < review.PageCount - 1; page++)
        {
            var frame = NewProjectScreens.Review(Idea(), review, problem: null);
            HoldsThreePrompts(frame);
            Assert.That(frame.Sections.Single(step => step.Chosen).Words, Is.EqualTo(EntryText.StartBuilding));
            Assert.That(Ids(frame), Does.Not.Contain(NewProjectScreens.ConfirmStart), "no Yes anywhere before the last part: part " + (page + 1));
            Assert.That(frame.Footer[PromptSlot.Free], Is.Null);
            Assert.That(frame.Footer.Confirming, Is.True);
            Assert.That(frame.Footer[PromptSlot.FarRight]!.Kind, Is.EqualTo(PromptKind.Cancel), "Cancel where Start building was pressed");
            var next = frame.Lines.Last();
            Assert.That((next.Action, next.Words), Is.EqualTo((NewProjectScreens.NextPart, EntryText.NextPart(page + 2, review.PageCount))));
            Assert.That(review.CanConfirm, Is.False, "the panel's own check refuses Yes too");
            review.Next();
        }

        var last = NewProjectScreens.Review(Idea(), review, problem: null);
        HoldsThreePrompts(last);
        Assert.That(last.Lines.Any(line => line.Action == NewProjectScreens.NextPart), Is.False);
        var yes = last.Footer[PromptSlot.Free]!;
        Assert.That((yes.Id, yes.Kind, yes.Available, yes.Main), Is.EqualTo((NewProjectScreens.ConfirmStart, PromptKind.Yes, true, false)),
            "Yes stands in the middle, where nothing stood, drawn plain");
        Assert.That(last.Footer.All.Select(each => each.Slot), Is.EqualTo(new[] { PromptSlot.Close, PromptSlot.Free, PromptSlot.FarRight }));
        var gone = NewProjectScreens.Review(Idea(), review, problem: EntryText.WaitingForMac);
        Assert.That((gone.Footer[PromptSlot.Free]!.Available, gone.Reason), Is.EqualTo((false, EntryText.WaitingForMac)));
    }

    [Test]
    public void TheReviewShowsEachPartWholeFromWhereItsItemWasSplit()
    {
        var review = new NewWorkReview("Project", "Title", "Agent", "Model", "on your computer", "ref", "Objective");
        // The first task, last, wraps to more rows than a part holds, so it is split across parts.
        review.Paginate(review.Items.Select((_, index) => index == review.Items.Count - 1 ? 7 : 1).ToList(), 4);
        var split = false;
        for (var page = 0; page < review.PageCount; page++)
        {
            var frame = NewProjectScreens.Review(Idea(), review, problem: null);
            var parts = frame.Lines.Skip(1).Where(line => line.Action == null).ToList();
            Assert.That(parts.Select(line => (line.Words, line.Rows, line.FromRow)),
                Is.EqualTo(review.Parts.Select(part => (review.Items[part.Item].Text, part.Lines, part.FirstLine))));
            Assert.That(parts.All(line => line.WordsAreData), Is.True);
            split |= parts.Any(line => line.FromRow > 0);
            review.Next();
        }
        Assert.That(split, Is.True, "an item split across parts starts its later part at the row it left off");
        var unmeasured = new NewWorkReview("Project", "Title", "Agent", "Model", "on your computer", "ref", "Objective");
        var waiting = NewProjectScreens.Review(Idea(), unmeasured, problem: null);
        Assert.That(Ids(waiting), Does.Not.Contain(NewProjectScreens.ConfirmStart), "nothing to confirm before the request is measured");
    }
}

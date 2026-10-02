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
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.UseIdea));
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Available, Is.False, "nothing to make a recap from yet");
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
        Assert.That(task.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.UseIdea), "a row not on the page is never acted on");
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
        Assert.That(frame.Lines.Any(line => line.Words == CompanionText.Full), Is.True);
        Assert.That(Answers(frame).Where(line => line.Action != NewProjectScreens.GoOnWithout).All(line => !line.Available), Is.True);
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Available, Is.True, "the recap adds no words");
    }

    [Test]
    public void TheNewWordsSpeakAsAPersonWithoutADashOrAProduct()
    {
        foreach (var words in new[]
                 {
                     CompanionText.TalkItThroughShort, CompanionText.AnswerFirst, CompanionText.MakeTheRecapFromMyWords, CompanionText.RecapFromMyWords,
                     EntryText.AnswerQuestions, EntryText.StartQuestions,
                 })
        {
            Assert.That(words, Does.Not.Contain("—").And.Not.Contain("!"), words);
            foreach (var brand in new[] { "Mac", "Ollama", "qwen", "OpenCode", "Codex", "Claude" })
                Assert.That(words, Does.Not.Contain(brand), words);
        }
    }
}

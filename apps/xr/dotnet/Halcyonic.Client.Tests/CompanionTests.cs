using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

internal static class Companions
{
    public static AskReply Ask(string question = "Who enters the times?", params string[] choices) => new()
    {
        Line = "A tracker for race times fits a small web page.",
        View = CompanionView.Unclear,
        Question = new CompanionQuestion
        {
            Text = question,
            Choices = (choices.Length == 0 ? new[] { "Each runner", "One organiser" } : choices).ToList(),
        },
    };

    public static ProposeReply Propose(string name = "Race Times", string task = "Create one web page where an organiser types a name and a time.",
        CompanionView view = CompanionView.Clear) => new()
    {
        Line = "That is clear enough to start.",
        View = view,
        Proposal = new CompanionProposal { ProjectName = name, FirstTask = task },
    };

    public static CompanionReplyResponse Response(CompanionReply reply) => new()
    {
        Reply = reply,
        Provenance = "reported",
        Companion = new CompanionModel { Name = "local-model:tag", Served = "this_mac" },
    };
}

public class CompanionExchangeTests
{
    [Test]
    public void AnIdeaIsTheFirstMessageAndEachReplyFollowsThePersonsWords()
    {
        var idea = new ProjectIdea();
        idea.UseIdea("something for my running club");
        var exchange = idea.BeginCompanion(CompanionStart.Idea);
        Assert.That(exchange.Turns.Single(), Is.InstanceOf<PersonTurn>().With.Property("Text").EqualTo("something for my running club"));
        Assert.That(exchange.CanSay, Is.False, "the companion answers the idea first");
        var request = exchange.Ask(CompanionWant.Next)!;
        Assert.That(request.Start, Is.EqualTo(CompanionStart.Idea));
        Assert.That(request.Messages, Has.Count.EqualTo(1));
        Assert.That(exchange.Waiting, Is.True);
        Assert.That(exchange.Ask(CompanionWant.Next), Is.Null, "one reply at a time");
        Assert.That(exchange.Replied(exchange.Generation, Companions.Response(Companions.Ask())), Is.True);
        Assert.That(exchange.Latest, Is.InstanceOf<AskReply>());
        Assert.That(exchange.Ask(CompanionWant.Next), Is.Null, "nothing new for the companion to answer");
        Assert.That(exchange.Say("One organiser"), Is.True);
        Assert.That(exchange.Ask(CompanionWant.Next)!.Messages, Has.Count.EqualTo(3));
    }

    [Test]
    public void HelpMeFigureItOutLetsTheCompanionAskFirst()
    {
        var exchange = new ProjectIdea().BeginCompanion(CompanionStart.Help);
        Assert.That(exchange.Turns, Is.Empty);
        Assert.That(exchange.CanAskForRecap, Is.False, "nothing said yet to make a recap from");
        Assert.That(exchange.Ask(CompanionWant.Proposal), Is.Null);
        var request = exchange.Ask(CompanionWant.Next)!;
        Assert.That(request.Start, Is.EqualTo(CompanionStart.Help));
        Assert.That(request.Messages, Is.Empty);
    }

    [Test]
    public void TheRecapCanBeAskedForOnceThePersonHasSaidSomething()
    {
        var exchange = new ProjectIdea().BeginCompanion(CompanionStart.Help);
        exchange.Ask(CompanionWant.Next);
        exchange.Replied(exchange.Generation, Companions.Response(Companions.Ask()));
        Assert.That(exchange.CanAskForRecap, Is.False);
        exchange.Say("Each runner");
        Assert.That(exchange.CanAskForRecap, Is.True);
        var request = exchange.Ask(CompanionWant.Proposal)!;
        Assert.That(request.Want, Is.EqualTo(CompanionWant.Proposal));
        exchange.Replied(exchange.Generation, Companions.Response(Companions.Propose()));
        Assert.That(exchange.Proposal, Is.Not.Null);
        Assert.That(exchange.CanAskForRecap, Is.False, "it has proposed already");
        Assert.That(exchange.CanSay, Is.False, "once it has proposed, the recap is where things change");
        // The recap asked for right after a question: the companion's two turns in a row come back as they were.
        var askedThenProposed = new CompanionExchange(CompanionStart.Help);
        askedThenProposed.Ask(CompanionWant.Next);
        askedThenProposed.Replied(askedThenProposed.Generation, Companions.Response(Companions.Ask()));
        askedThenProposed.Say("Each runner");
        askedThenProposed.Ask(CompanionWant.Next);
        askedThenProposed.Replied(askedThenProposed.Generation, Companions.Response(Companions.Ask("Where should it run?")));
        askedThenProposed.Ask(CompanionWant.Proposal);
        askedThenProposed.Replied(askedThenProposed.Generation, Companions.Response(Companions.Propose()));
        var back = CompanionExchange.Restore(CompanionStart.Help, 4, askedThenProposed.Turns);
        Assert.That(back.Turns, Has.Count.EqualTo(askedThenProposed.Turns.Count));
        Assert.That(back.Proposal, Is.Not.Null);
    }

    [Test]
    public void AReplyToARequestLeftBehindChangesNothing()
    {
        var exchange = new ProjectIdea().BeginCompanion(CompanionStart.Help);
        exchange.Ask(CompanionWant.Next);
        var asked = exchange.Generation;
        exchange.Leave();
        Assert.That(exchange.Waiting, Is.False);
        Assert.That(exchange.Replied(asked, Companions.Response(Companions.Ask())), Is.False);
        Assert.That(exchange.Failed(asked, "companion_too_slow"), Is.False);
        Assert.That(exchange.Turns, Is.Empty);
        Assert.That(exchange.Left, Is.True, "a reopened Create goes where the person was, not back to the companion");
        exchange.Ask(CompanionWant.Next);
        Assert.That(exchange.Left, Is.False, "asking again takes it up again");
    }

    [Test]
    public void AFailureKeepsTheExchangeAndTryAgainAsksTheSame()
    {
        var exchange = new ProjectIdea().BeginCompanion(CompanionStart.Help);
        exchange.Ask(CompanionWant.Next);
        exchange.Failed(exchange.Generation, null);
        Assert.That(exchange.Failure, Is.EqualTo(CompanionExchange.Unreachable));
        var again = exchange.Retry()!;
        Assert.That(again.Want, Is.EqualTo(CompanionWant.Next));
        Assert.That(exchange.Failure, Is.Null);
        exchange.Failed(exchange.Generation, "companion_unreadable");
        Assert.That(exchange.Failure, Is.EqualTo("companion_unreadable"));
    }

    [Test]
    public void ThePersonsWordsStayInsideTheRequestsBounds()
    {
        var exchange = new ProjectIdea().BeginCompanion(CompanionStart.Help);
        exchange.Ask(CompanionWant.Next);
        exchange.Replied(exchange.Generation, Companions.Response(Companions.Ask()));
        Assert.That(exchange.Say("   "), Is.False);
        Assert.That(exchange.Say(new string('x', CompanionExchange.PersonLimit + 1)), Is.False);
        Assert.That(exchange.Turns, Has.Count.EqualTo(1));
        // Answer until the exchange is full: never past the request's message limit.
        while (exchange.CanSay)
        {
            Assert.That(exchange.Say("Each runner"), Is.True);
            exchange.Ask(CompanionWant.Next);
            exchange.Replied(exchange.Generation, Companions.Response(Companions.Ask()));
        }
        Assert.That(exchange.Turns.Count, Is.LessThanOrEqualTo(CompanionExchange.MaxMessages));
        Assert.That(exchange.Say("one more"), Is.False);
        Assert.That(exchange.CanAskForRecap, Is.True, "the recap is always left: it adds no words");
        Assert.That(exchange.Ask(CompanionWant.Proposal)!.Messages.Count, Is.LessThanOrEqualTo(CompanionExchange.MaxMessages));
    }

    [Test]
    public void TheExchangesCharactersStayUnderTheMacsLimit()
    {
        var exchange = new ProjectIdea().BeginCompanion(CompanionStart.Help);
        // Replies as long as the contract lets them be.
        var longest = new AskReply
        {
            Line = new string('l', 300),
            View = CompanionView.Unclear,
            Question = new CompanionQuestion { Text = new string('q', 160), Choices = Enumerable.Repeat(new string('c', 48), 4).ToList() },
        };
        var said = 0;
        for (var turn = 0; turn < 9; turn++)
        {
            exchange.Ask(CompanionWant.Next);
            exchange.Replied(exchange.Generation, Companions.Response(longest));
            if (exchange.Say(new string('y', CompanionExchange.PersonLimit))) said++;
        }
        var characters = exchange.Turns.Sum(turn => turn is PersonTurn person ? person.Text.Length : HalcyonicJson.Serialize(((CompanionTurn)turn).Reply).Length);
        Assert.That(characters, Is.LessThanOrEqualTo(CompanionExchange.CharacterLimit));
        Assert.That(said, Is.LessThan(9), "the limit refused words past it");
    }
}

public class CompanionRecapTests
{
    [Test]
    public void AProposalFillsTheRecapMarkedAsTheCompanionsAndTheTypedIdeaStaysOnePressAway()
    {
        var idea = new ProjectIdea();
        idea.UseIdea("something for my running club");
        idea.UseProposal(Companions.Propose().Proposal);
        Assert.That(idea.Name, Is.EqualTo("Race Times"));
        Assert.That(idea.FirstTask, Does.StartWith("Create one web page"));
        Assert.That(idea.NameSuggested && idea.TaskSuggested, Is.True);
        Assert.That(idea.UseOwnWords(), Is.True);
        Assert.That(idea.FirstTask, Is.EqualTo("something for my running club"));
        Assert.That(idea.Name, Is.EqualTo("something for my running club"));
        Assert.That(idea.NameSuggested || idea.TaskSuggested, Is.False);
    }

    [Test]
    public void ChangingAFactMakesItThePersonsAndATypedNameIsNeverReplaced()
    {
        var idea = new ProjectIdea();
        idea.Rename("Club Times");
        idea.UseProposal(Companions.Propose().Proposal);
        Assert.That(idea.Name, Is.EqualTo("Club Times"));
        Assert.That(idea.NameSuggested, Is.False);
        Assert.That(idea.TaskSuggested, Is.True);
        idea.Rewrite("Make a page of race times.");
        Assert.That(idea.TaskSuggested, Is.False);
        Assert.That(idea.UseOwnWords(), Is.False, "nothing typed to go back to");
    }

    [Test]
    public void AnExistingProjectKeepsItsName()
    {
        var idea = new ProjectIdea("01a0dcf1-5a80-7000-8000-0000000000a1", "Storefront API");
        idea.UseProposal(Companions.Propose().Proposal);
        Assert.That(idea.Name, Is.EqualTo("Storefront API"));
        Assert.That(idea.NameSuggested, Is.False);
    }
}

public class CompanionWordsTests
{
    [Test]
    public void TheCompanionsWordsAreQuotedAsItsOwnAndShownByTheOneRule()
    {
        Assert.That(CompanionText.Says("Fits a page."), Is.EqualTo("The companion says: “Fits a page.”"));
        Assert.That(CompanionText.Says("a‮b"), Is.EqualTo("The companion says: “" + LabelText.Plain("a‮b") + "”"));
        Assert.That(CompanionText.View(CompanionView.Clear), Is.Null);
        Assert.That(CompanionText.View(CompanionView.NotBuildable), Does.StartWith("The companion thinks"));
    }

    [Test]
    public void EveryFailureIsSaidFromItsCodeWithANextStep()
    {
        foreach (var code in new[]
                 {
                     "companion_not_set_up", "companion_not_running", "companion_model_missing", "companion_model_not_local",
                     "companion_too_slow", "companion_unreadable", "companion_busy", "companion_busy_on_mac", "rate_limited",
                     CompanionExchange.Unreachable, "something_new",
                 })
        {
            var words = CompanionText.Failure(code);
            Assert.That(words, Is.Not.Null.And.Not.Empty, code);
            Assert.That(words, Does.Contain("Try again").Or.Contain("Type your idea").Or.Contain("try again"), code);
        }
        Assert.That(CompanionText.Failure("companion_cancelled"), Is.Null);
    }

    [Test]
    public void TheHostIsCalledAsHostTextSaysAndNoWordNamesAProductOrUsesAnEmDash()
    {
        var all = new[]
        {
            CompanionText.Note, CompanionText.NotSetUp, CompanionText.CantRun, CompanionText.TooSlow, CompanionText.Unreadable,
            CompanionText.Busy, CompanionText.Unreached, CompanionText.TooMany, CompanionText.CouldNotAsk, CompanionText.Full,
            CompanionText.WaitingLong, CompanionText.Recorded, CompanionText.TalkItThrough, CompanionText.Suggested,
            CompanionText.MakeTheRecap, CompanionText.GoOnWithout, CompanionText.UseMyWords, CompanionText.TypeAnswer, CompanionText.TooLong(2000),
        };
        foreach (var words in all)
        {
            Assert.That(words, Does.Not.Contain("—"), words);
            Assert.That(words, Does.Not.Contain("!"), words);
            foreach (var brand in new[] { "Mac", "Ollama", "qwen", "OpenCode", "Codex", "Claude" })
                Assert.That(words, Does.Not.Contain(brand), words);
        }
        Assert.That(CompanionText.NotSetUp, Does.Contain(HostText.Your));
        Assert.That(CompanionText.WaitingLong, Does.StartWith(HostText.YourStart));
        foreach (var button in new[] { CompanionText.MakeTheRecap, CompanionText.GoOnWithout, CompanionText.UseMyWords, CompanionText.TypeAnswer })
            Assert.That(button.Split(' ').Length, Is.InRange(1, 4), button);
    }
}

public class CompanionScreensTests
{
    private static ProjectIdea Asked(out CompanionExchange exchange)
    {
        var idea = new ProjectIdea();
        idea.UseIdea("something for my running club");
        exchange = idea.BeginCompanion(CompanionStart.Idea);
        exchange.Ask(CompanionWant.Next);
        exchange.Replied(exchange.Generation, Companions.Response(Companions.Ask()));
        return idea;
    }

    [Test]
    public void TheQuestionShowsTheCompanionsLineQuotedItsChoicesAndOneHoldToTalkBesideTypeMyAnswer()
    {
        var model = EntryScreens.Companion(Asked(out _), voice: true, said: null, waitedSeconds: 0);
        Assert.That(model.Lead, Is.EqualTo(CompanionText.Note));
        Assert.That(model.Context, Is.EqualTo(CompanionText.ThinksUnclear));
        var said = model.Rows[0];
        Assert.That(said.Line && said.Claim && said.TitleIsData, Is.True);
        Assert.That(said.Title, Does.StartWith("The companion says: “"));
        Assert.That(model.Rows.Where(row => row.Action == EntryScreens.CompanionChoice).Select(row => row.Title),
            Is.EqualTo(new[] { "Each runner", "One organiser" }));
        var typed = model.Rows.Single(row => row.Action == EntryScreens.CompanionType);
        Assert.That(typed.Side!.Holds, Is.True);
        Assert.That(model.Actions.All.Any(action => action.Holds), Is.False, "no hold to talk on the bar");
        Assert.That(model.Rows.Count(row => row.Side?.Holds == true), Is.EqualTo(1));
        Assert.That(model.Actions.Back!.Id, Is.EqualTo(EntryScreens.Back));
        Assert.That(model.Actions.Secondary.Single().Id, Is.EqualTo(EntryScreens.GoOnWithout));
        Assert.That(model.Actions.Primary!.Id, Is.EqualTo(EntryScreens.MakeRecap));
        Assert.That(model.Actions.Primary.Icon, Is.Null, "no icon stands for making the recap");
    }

    [Test]
    public void WaitingAndFailingKeepEveryActionInItsPlace()
    {
        var idea = Asked(out var exchange);
        exchange.Say("One organiser");
        exchange.Ask(CompanionWant.Next);
        var waiting = EntryScreens.Companion(idea, voice: true, said: null, waitedSeconds: 1);
        Assert.That(waiting.Rows.Select(row => row.Title), Is.EqualTo(new[] { CompanionText.Waiting }));
        Assert.That(waiting.Actions.Primary!.Available, Is.False);
        var long_ = EntryScreens.Companion(idea, voice: true, said: null, waitedSeconds: 6);
        Assert.That(long_.Rows.Select(row => row.Title), Does.Contain(CompanionText.WaitingLong));
        exchange.Failed(exchange.Generation, "companion_too_slow");
        var failed = EntryScreens.Companion(idea, voice: true, said: null, waitedSeconds: 0);
        Assert.That(failed.Rows.Single().Title, Is.EqualTo(CompanionText.TooSlow));
        Assert.That(failed.Rows.Single().Tone, Is.EqualTo(GlazeTone.Failure));
        Assert.That(failed.Actions.Primary!.Id, Is.EqualTo(EntryScreens.CompanionRetry));
        Assert.That(failed.Actions.Primary.Icon, Is.EqualTo(GlazeIcon.Refresh));
        foreach (var model in new[] { waiting, long_, failed })
        {
            Assert.That(model.Actions.Back!.Id, Is.EqualTo(EntryScreens.Back));
            Assert.That(model.Actions.Secondary.Single().Id, Is.EqualTo(EntryScreens.GoOnWithout));
        }
    }

    [Test]
    public void TheRecordedExchangeSaysSoAndOffersOnlyTheRecordedAnswer()
    {
        var recording = CompanionRecording.Parse(CompanionRecordingTests.Sample);
        var idea = new ProjectIdea();
        var exchange = recording.Begin(idea);
        var model = EntryScreens.Companion(idea, voice: true, said: null, waitedSeconds: 0, recording: recording);
        Assert.That(model.Lead, Is.EqualTo(CompanionText.Recorded));
        Assert.That(model.Rows.Any(row => row.Action == EntryScreens.CompanionType), Is.False);
        Assert.That(model.Rows.Any(row => row.Side != null), Is.False);
        Assert.That(model.Actions.Secondary, Is.Empty);
        var pressable = model.Rows.Where(row => row.Action == EntryScreens.CompanionChoice).Select(row => row.Title);
        Assert.That(pressable, Is.EqualTo(new[] { "One organiser" }));
        Assert.That(model.Rows.Count(row => row.Key != null && !row.Available), Is.EqualTo(1));
        Assert.That(model.Actions.Primary!.Available, Is.False, "the recording asks for the recap later");
        recording.Press(exchange, "One organiser");
        model = EntryScreens.Companion(idea, voice: true, said: null, waitedSeconds: 0, recording: recording);
        Assert.That(model.Actions.Primary!.Available, Is.True, "here the recording asked for the recap");
    }

    [Test]
    public void HelpMeFigureItOutSaysWhichHelperItOpensAndWhyTheCompanionCant()
    {
        var available = new AvailableCompanion { Companion = new CompanionModel { Name = "m", Served = "this_mac" }, MaxQuestions = 4 };
        var start = EntryScreens.CreateStart(new ProjectIdea(), voice: false, said: null, companion: available);
        Assert.That(start.Rows.Single(row => row.Action == EntryScreens.HelpMe).Detail, Is.EqualTo(CompanionText.TalkItThrough));
        var unavailable = new UnavailableCompanion { Reason = new ErrorInfo { Code = "companion_not_set_up", Message = "x" } };
        start = EntryScreens.CreateStart(new ProjectIdea(), voice: false, said: null, companion: unavailable);
        Assert.That(start.Rows.Single(row => row.Action == EntryScreens.HelpMe).Detail, Is.EqualTo(EntryText.HelpMeInvite));
        Assert.That(start.Rows.Last().Title, Is.EqualTo(CompanionText.NotSetUp));
        var task = EntryScreens.CreateStart(new ProjectIdea("01a0dcf1-5a80-7000-8000-0000000000a1", "Shop"), voice: false, said: null, companion: available);
        Assert.That(task.Rows.Single(row => row.Action == EntryScreens.HelpMe).Detail, Is.EqualTo(EntryText.HelpMeInvite),
            "the companion helps with a new project only");
    }

    [Test]
    public void TheRecapMarksWhatTheCompanionSuggestedAndOffersTheTypedWordsBack()
    {
        var idea = new ProjectIdea();
        idea.UseIdea("something for my running club");
        idea.UseProposal(Companions.Propose().Proposal);
        var recap = EntryScreens.Recap(idea, new NewWorkDraft(CommandFactoryFor()), null, live: true, notice: null, problem: null);
        Assert.That(recap.Rows[0].Detail, Is.EqualTo(CompanionText.Suggested));
        Assert.That(recap.Rows[1].Detail, Is.EqualTo(CompanionText.Suggested));
        Assert.That(recap.Rows[1].Side!.Id, Is.EqualTo(EntryScreens.UseMyWords));
        Assert.That(recap.Actions.Secondary, Is.Empty, "the bar keeps its places");
        idea.Rewrite("Make a page of race times.");
        recap = EntryScreens.Recap(idea, new NewWorkDraft(CommandFactoryFor()), null, live: true, notice: null, problem: null);
        Assert.That(recap.Rows[1].Detail, Is.Null);
        Assert.That(recap.Rows[1].Side, Is.Null);
        Assert.That(EntryScreens.Proposed(Companions.Propose(view: CompanionView.NotBuildable)),
            Is.EqualTo(CompanionText.ThinksNotBuildable + " The companion says: “That is clear enough to start.”"));
    }

    internal static CommandFactory CommandFactoryFor() => new(new ClientInfo { Name = "test", Version = null, DeviceLabel = null });
}

public class CreationDraftTests
{
    private const string Journal = "01a0dcf1-5a80-7000-8000-00000000j001";
    private string directory = null!;

    [SetUp]
    public void SetUp() => directory = Directory.CreateTempSubdirectory("halcyonic-drafts-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(directory, recursive: true);

    private string FilePath => Path.Combine(directory, "creation-drafts.json");

    [Test]
    public void ADraftComesBackAsItWasWithItsExchange()
    {
        var idea = new ProjectIdea();
        idea.UseIdea("something for my running club");
        var exchange = idea.BeginCompanion(CompanionStart.Idea);
        exchange.Ask(CompanionWant.Next);
        exchange.Replied(exchange.Generation, Companions.Response(Companions.Ask()));
        exchange.Say("One organiser");
        exchange.Ask(CompanionWant.Next);
        exchange.Replied(exchange.Generation, Companions.Response(Companions.Propose()));
        idea.UseProposal(exchange.Proposal!.Proposal);
        idea.ChooseFolder(ProjectFolder.Restore("/Users/someone/Projects", "Projects", "race-times", isNew: true));
        var draft = new NewWorkDraft(CompanionScreensTests.CommandFactoryFor());
        var now = DateTimeOffset.Parse("2026-10-02T12:00:00Z");
        var store = new CreationDrafts(new FileCreationDraftStore(FilePath), () => now);
        Assert.That(store.Keep(Journal, new[] { CreationDraft.Of(Journal, "", idea, null, draft, now)! }), Is.True);

        var kept = new CreationDrafts(new FileCreationDraftStore(FilePath), () => now.AddDays(1)).For(Journal).Single();
        var back = kept.ToIdea()!;
        Assert.That(back.Name, Is.EqualTo("Race Times"));
        Assert.That(back.FirstTask, Is.EqualTo(idea.FirstTask));
        Assert.That(back.NameSuggested && back.TaskSuggested, Is.True);
        Assert.That(back.OwnWords, Is.EqualTo("something for my running club"));
        Assert.That(back.Folder!.Describe(), Is.EqualTo("a new folder, race-times, in Projects"));
        Assert.That(back.Companion!.Turns.Select(turn => turn.GetType()),
            Is.EqualTo(new[] { typeof(PersonTurn), typeof(CompanionTurn), typeof(PersonTurn), typeof(CompanionTurn) }));
        Assert.That(back.Companion.Waiting, Is.False);
        Assert.That(back.Companion.Proposal!.Proposal.ProjectName, Is.EqualTo("Race Times"));
    }

    [Test]
    public void TheFixedAnswersComeBackWhereThePersonWas()
    {
        var idea = new ProjectIdea();
        idea.BeginGuide();
        idea.Answer("An app");
        idea.Answer("My team");
        var back = ProjectIdea.Restore(idea.Keep());
        Assert.That(back.Guided, Is.True);
        Assert.That(back.Question, Is.EqualTo(ProjectIdea.FirstStepQuestion));
        Assert.That(back.AnswerTo(ProjectIdea.KindQuestion), Is.EqualTo("An app"));
        Assert.That(back.AnswerTo(ProjectIdea.AudienceQuestion), Is.EqualTo("My team"));
    }

    [Test]
    public void DraftsLastSevenDaysWithoutAChangeAndNeverCrossToAnotherComputer()
    {
        var now = DateTimeOffset.Parse("2026-10-02T12:00:00Z");
        var idea = new ProjectIdea();
        idea.UseIdea("a tide table");
        var draft = new NewWorkDraft(CompanionScreensTests.CommandFactoryFor());
        var other = "01a0dcf1-5a80-7000-8000-00000000j002";
        new CreationDrafts(new FileCreationDraftStore(FilePath), () => now).Keep(other, new[] { CreationDraft.Of(other, "", idea, null, draft, now)! });
        new CreationDrafts(new FileCreationDraftStore(FilePath), () => now).Keep(Journal, new[] { CreationDraft.Of(Journal, "", idea, null, draft, now)! });
        Assert.That(new CreationDrafts(new FileCreationDraftStore(FilePath), () => now).For(Journal), Has.Count.EqualTo(1));
        Assert.That(new CreationDrafts(new FileCreationDraftStore(FilePath), () => now).For(other), Has.Count.EqualTo(1), "the other computer's draft was left as it was");
        Assert.That(new CreationDrafts(new FileCreationDraftStore(FilePath), () => now.AddDays(7).AddMinutes(1)).For(Journal), Is.Empty);
    }

    [Test]
    public void NothingWorthKeepingIsNotKeptAndAnUnchangedDraftIsNotWrittenAgain()
    {
        var now = DateTimeOffset.Parse("2026-10-02T12:00:00Z");
        var draft = new NewWorkDraft(CompanionScreensTests.CommandFactoryFor());
        Assert.That(CreationDraft.Of(Journal, "", new ProjectIdea(), null, draft, now), Is.Null);
        var idea = new ProjectIdea();
        idea.UseIdea("a tide table");
        var store = new CreationDrafts(new FileCreationDraftStore(FilePath), () => now);
        Assert.That(store.Keep(Journal, new[] { CreationDraft.Of(Journal, "", idea, null, draft, now)! }), Is.True);
        Assert.That(store.Keep(Journal, new[] { CreationDraft.Of(Journal, "", idea, null, draft, now.AddMinutes(5))! }), Is.False);
        Assert.That(store.Keep(Journal, Array.Empty<CreationDraft>()), Is.True);
        Assert.That(File.Exists(FilePath), Is.False, "no drafts, no file");
    }

    [Test]
    public void AnUnreadableFileReadsAsNoDrafts()
    {
        File.WriteAllText(FilePath, "{ not json");
        Assert.That(new FileCreationDraftStore(FilePath).Load(), Is.Empty);
        File.WriteAllText(FilePath, "{\"version\": 99, \"drafts\": []}");
        Assert.That(new FileCreationDraftStore(FilePath).Load(), Is.Empty);
    }

    [Test]
    public void AKeptExchangeOutOfOrderIsCutWhereItBreaks()
    {
        var kept = new CreationDraft
        {
            FirstTask = "a tide table",
            Companion = new KeptExchange
            {
                Start = CompanionStart.Idea,
                Turns = new List<CompanionExchangeTurn>
                {
                    new PersonTurn { Text = "a tide table" },
                    new CompanionTurn { Reply = Companions.Ask() },
                    new CompanionTurn { Reply = Companions.Ask() },
                    new PersonTurn { Text = "after the break" },
                },
            },
        };
        var back = ProjectIdea.Restore(kept);
        Assert.That(back.Companion!.Turns, Has.Count.EqualTo(2));
        var unsafeName = new CreationDraft { FirstTask = "x", Folder = new KeptFolder { RootPath = "/r", RootName = "r", FolderName = "../up", IsNew = true } };
        Assert.That(ProjectIdea.Restore(unsafeName).Folder, Is.Null, "a name the host would refuse is not kept");
    }

    [Test]
    public void AProjectTheMacAlreadyMadeComesBackAsATaskForItSoNothingIsMadeTwice()
    {
        var commands = CompanionScreensTests.CommandFactoryFor();
        var draft = new NewWorkDraft(commands) { Objective = "Make a page of race times." };
        draft.ChooseRuntime(new RuntimeDescriptor
        {
            RuntimeId = "mock", DisplayName = "Mock runtime", Kind = "mock", Synthetic = true, ModelChoice = ModelChoice.None,
            Capabilities = new RuntimeCapabilities { StartExecution = true },
        });
        var idea = new ProjectIdea();
        idea.UseIdea("Make a page of race times.");
        idea.Rename("Race Times");
        var sequence = new BuildSequence(draft, commands, idea.Name);
        var create = sequence.Begin();
        var projectId = "01a0dcf1-5a80-7000-8000-0000000000a9";
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(1);
        snapshot.Commands = new List<CommandView>
        {
            new() { CommandId = create.CommandId, Status = CommandStatus.Completed, Result = new ProjectCreatedResult { ProjectId = projectId } },
        };
        state.ApplySnapshot(snapshot, new StateChanges());
        sequence.Advance(state);
        var kept = CreationDraft.Of(Journal, "", idea, sequence, draft, DateTimeOffset.UtcNow)!;
        Assert.That(kept.MadeProjectId, Is.EqualTo(projectId));
        var back = kept.ToIdea()!;
        Assert.That(back.ExistingProjectId, Is.EqualTo(projectId));
        Assert.That(back.Name, Is.EqualTo("Race Times"));
        Assert.That(back.FirstTask, Is.EqualTo("Make a page of race times."));

        var resumed = BuildSequence.Resume(new NewWorkDraft(commands), commands, projectId, "01a0dcf1-5a80-7000-8000-0000000000b9");
        Assert.That(resumed.CanRetry, Is.True);
        Assert.That(resumed.StoppedAt!.Kind, Is.EqualTo(BuildStepKind.StartWork));
        var fresh = BuildSequence.Resume(new NewWorkDraft(commands), commands, projectId, null);
        Assert.That(fresh.StoppedAt!.Kind, Is.EqualTo(BuildStepKind.CreateWorkstream));
        Assert.That(fresh.Steps.Any(step => step.Kind == BuildStepKind.CreateProject), Is.False);
    }
}

public class CompanionRecordingTests
{
    /// <summary>A recording as pnpm companion:record writes it: the idea, two questions answered and the recap asked for after the second.</summary>
    internal const string Sample = """
        {
          "version": 1,
          "source": "Recorded once from the companion on the owner computer; replayed on the headset, never asked.",
          "model": "local-model:tag",
          "recorded_at": "2026-10-02T12:00:00.000Z",
          "start": "idea",
          "turns": [
            { "from": "person", "text": "A page where my running club keeps everyone's race times" },
            { "from": "companion", "reply": { "next": "ask", "line": "A page of race times fits well.", "view": "unclear",
              "question": { "text": "Who enters the times?", "choices": ["Each runner", "One organiser"] } } },
            { "from": "person", "text": "One organiser" },
            { "from": "companion", "reply": { "next": "ask", "line": "One organiser keeps it simple.", "view": "unclear",
              "question": { "text": "How should the times be sorted?", "choices": ["Fastest first", "By date"] } } },
            { "from": "companion", "reply": { "next": "propose", "line": "That is clear enough to start.", "view": "clear",
              "proposal": { "project_name": "Club Race Times", "first_task": "Create one web page where an organiser adds a runner's name and time." } } }
          ]
        }
        """;

    [Test]
    public void PlaysOnlyWhatWasRecordedInItsOrder()
    {
        var recording = CompanionRecording.Parse(Sample);
        var idea = new ProjectIdea();
        var exchange = recording.Begin(idea);
        Assert.That(idea.FirstTask, Is.EqualTo("A page where my running club keeps everyone's race times"));
        Assert.That(((AskReply)exchange.Latest!).Question.Text, Is.EqualTo("Who enters the times?"));
        Assert.That(recording.RecordedAnswer(exchange), Is.EqualTo("One organiser"));
        Assert.That(recording.Press(exchange, "Each runner"), Is.False, "an answer that was not recorded plays nothing");
        Assert.That(recording.RecapHere(exchange), Is.False);
        Assert.That(recording.Press(exchange, "One organiser"), Is.True);
        Assert.That(((AskReply)exchange.Latest!).Question.Text, Is.EqualTo("How should the times be sorted?"));
        Assert.That(recording.RecordedAnswer(exchange), Is.Null);
        Assert.That(recording.AskForRecap(exchange), Is.True);
        Assert.That(exchange.Proposal!.Proposal.ProjectName, Is.EqualTo("Club Race Times"));
        Assert.That(exchange.Model, Is.EqualTo(CompanionRecording.RecordedModel), "the model's name is never played");
        idea.UseProposal(exchange.Proposal.Proposal);
        Assert.That(idea.Name, Is.EqualTo(recording.Proposal.ProjectName));
    }

    [Test]
    public void RefusesARecordingThatBreaksItsRules()
    {
        var broken = new[]
        {
            Sample.Replace("\"version\": 1", "\"version\": 2"),
            Sample.Replace("{ \"from\": \"person\", \"text\": \"One organiser\" }", "{ \"from\": \"person\", \"text\": \"Someone else\" }"),
            Sample.Replace("\"next\": \"propose\"", "\"next\": \"ask\"").Replace("\"proposal\": {", "\"question\": { \"text\": \"x\", \"choices\": [] }, \"x\": {"),
            "{ not json",
        };
        foreach (var json in broken)
            Assert.Throws<FormatException>(() => CompanionRecording.Parse(json), json.Length > 60 ? json.Substring(0, 60) : json);
    }
}

public class CompanionApiTests
{
    [Test]
    public async Task AsksForAReplyWithTheExchangeAsJsonAndReadsItBack()
    {
        var body = """
            {"reply":{"next":"ask","line":"Fits a page.","view":"unclear","question":{"text":"Who enters the times?","choices":["Me"]}},
             "provenance":"reported","companion":{"name":"local-model:tag","served":"this_mac"}}
            """;
        var handler = new ControlPlaneApiTests.CannedHandler(HttpStatusCode.OK, body);
        using var api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", handler);
        var exchange = new ProjectIdea().BeginCompanion(CompanionStart.Help);
        var answer = await api.AskCompanionAsync(exchange.Ask(CompanionWant.Next)!);
        Assert.That(((AskReply)answer.Reply).Question.Choices, Is.EqualTo(new[] { "Me" }));
        var request = handler.Requests.Single();
        Assert.That(request.RequestUri!.AbsolutePath, Is.EqualTo("/api/companion/replies"));
        var sent = JObject.Parse(await request.Content!.ReadAsStringAsync());
        Assert.That(sent.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("{\"start\":\"help\",\"want\":\"next\",\"messages\":[]}"));
    }

    [Test]
    public void ARefusalCarriesItsCode()
    {
        var handler = new ControlPlaneApiTests.CannedHandler((HttpStatusCode)504,
            """{"error":{"code":"companion_too_slow","message":"The companion took too long.","issues":[]}}""");
        using var api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", handler);
        var refused = Assert.ThrowsAsync<ControlPlaneRequestException>(() =>
            api.AskCompanionAsync(new CompanionRepliesRequest { Start = CompanionStart.Help, Want = CompanionWant.Next }))!;
        Assert.That(refused.Code, Is.EqualTo("companion_too_slow"));
    }
}

/// <summary>
/// The headset's companion calls through a real control plane, which asks a stand-in for Ollama on
/// loopback: the request this client writes is one the control plane takes, and its reply reads back.
/// </summary>
public class LiveCompanionTests
{
    private string dataDir = null!;

    [SetUp]
    public void SetUp() => dataDir = Directory.CreateTempSubdirectory("halcyonic-companion-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(dataDir, recursive: true);

    [Test]
    public async Task WithoutAModelNamedTheMacSaysTheCompanionIsNotSetUp()
    {
        using var controlPlane = await ControlPlaneProcess.StartAsync(dataDir, ControlPlaneProcess.FreePort());
        using var api = new ControlPlaneApi(ControlPlaneApi.BaseUriFor(controlPlane.RealtimeEndpoint), controlPlane.AccessToken);
        var status = (UnavailableCompanion)await api.GetCompanionAsync();
        Assert.That(status.Reason.Code, Is.EqualTo("companion_not_set_up"));
        Assert.That(CompanionText.Unavailable(status.Reason.Code), Is.EqualTo(CompanionText.NotSetUp));
    }

    [Test]
    public async Task AnExchangeTheHeadsetKeepsGetsTheCompanionsReplyThroughARealControlPlane()
    {
        using var ollama = new StandInOllama(
            """{"say":"A tracker fits a small web page.","assessment":"unclear","next":"ask","question":{"text":"Who enters the times?","choices":["Each runner","One organiser"]},"proposal":null}""",
            """{"say":"That is clear enough to start.","assessment":"clear","next":"propose","question":null,"proposal":{"name":"Race Times","first_task":"Create one web page of race times."}}""");
        var environment = new Dictionary<string, string>
        {
            ["HALCYONIC_COMPANION_MODEL"] = "local-model:tag",
            ["HALCYONIC_COMPANION_OLLAMA_URL"] = ollama.Address,
        };
        using var controlPlane = await ControlPlaneProcess.StartAsync(dataDir, ControlPlaneProcess.FreePort(), environment: environment);
        using var api = new ControlPlaneApi(ControlPlaneApi.BaseUriFor(controlPlane.RealtimeEndpoint), controlPlane.AccessToken);
        var status = (AvailableCompanion)await api.GetCompanionAsync();
        Assert.That(status.MaxQuestions, Is.EqualTo(4));

        var idea = new ProjectIdea();
        idea.UseIdea("something for my “running” club");
        var exchange = idea.BeginCompanion(CompanionStart.Idea, (int)status.MaxQuestions);
        var asked = exchange.Ask(CompanionWant.Next)!;
        exchange.Replied(exchange.Generation, await api.AskCompanionAsync(asked));
        var ask = (AskReply)exchange.Latest!;
        Assert.That(ask.View, Is.EqualTo(CompanionView.Unclear));
        Assert.That(ask.Question.Choices, Is.EqualTo(new[] { "Each runner", "One organiser" }));
        exchange.Say(ask.Question.Choices[1]);
        var response = await api.AskCompanionAsync(exchange.Ask(CompanionWant.Proposal)!);
        Assert.That(response.Provenance, Is.EqualTo("reported"));
        exchange.Replied(exchange.Generation, response);
        idea.UseProposal(exchange.Proposal!.Proposal);
        Assert.That(idea.Name, Is.EqualTo("Race Times"));
        Assert.That(idea.TaskSuggested, Is.True);
        // The control plane sent the person's words in their tags, and the companion's own reply back in its shape.
        var last = ollama.Requests.Last()["messages"]!;
        Assert.That(last[1]!["content"]!.ToString(), Is.EqualTo("<person>something for my “running” club</person>"));
        Assert.That(last[2]!["role"]!.ToString(), Is.EqualTo("assistant"));
        Assert.That(JObject.Parse(last[2]!["content"]!.ToString())["say"]!.ToString(), Is.EqualTo("A tracker fits a small web page."));
    }

    /// <summary>Answers <c>GET /api/tags</c> and, in order, each <c>POST /api/chat</c> with one canned reply streamed as Ollama streams it.</summary>
    private sealed class StandInOllama : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly Queue<string> replies;
        private readonly Task serving;

        public StandInOllama(params string[] replies)
        {
            this.replies = new Queue<string>(replies);
            var port = ControlPlaneProcess.FreePort();
            Address = "http://127.0.0.1:" + port;
            listener.Prefixes.Add(Address + "/");
            listener.Start();
            serving = Task.Run(ServeAsync);
        }

        public string Address { get; }

        public List<JObject> Requests { get; } = new();

        private async Task ServeAsync()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception) when (!listener.IsListening)
                {
                    return;
                }
                using var response = context.Response;
                string body;
                if (context.Request.Url!.AbsolutePath == "/api/tags")
                {
                    body = """{"models":[{"name":"local-model:tag","model":"local-model:tag","size":1}]}""";
                }
                else
                {
                    using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                    var sent = JObject.Parse(await reader.ReadToEndAsync());
                    lock (Requests) Requests.Add(sent);
                    var content = replies.Count > 0 ? replies.Dequeue() : "{}";
                    body = new JObject { ["message"] = new JObject { ["role"] = "assistant", ["content"] = content }, ["done"] = false }.ToString(Newtonsoft.Json.Formatting.None)
                        + "\n" + """{"message":{"role":"assistant","content":""},"done":true,"prompt_eval_count":10,"eval_count":5}""" + "\n";
                }
                var bytes = Encoding.UTF8.GetBytes(body);
                response.ContentType = "application/x-ndjson";
                await response.OutputStream.WriteAsync(bytes);
            }
        }

        public void Dispose()
        {
            listener.Stop();
            listener.Close();
            try
            {
                serving.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }
        }
    }
}

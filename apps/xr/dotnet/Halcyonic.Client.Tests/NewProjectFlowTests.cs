using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class NewProjectFlowTests
{
    private static readonly CommandFactory Commands = new(Samples.Client);

    /// <summary>The director's side, as a test plays it: what it reads, what it sends, and the keyboard's answers.</summary>
    private sealed class Host : IMenuHost
    {
        public ClientProjection? State { get; set; } = With();

        public bool Connected { get; set; } = true;

        public bool Demonstration { get; set; }

        public double Now { get; set; }

        public bool VoiceOffered { get; set; } = true;

        public ControlPlaneApi? Api { get; set; }

        public List<CommandEnvelope> Sent { get; } = new();

        /// <summary>How each send is acknowledged; by default never, so the projection decides.</summary>
        public Func<CommandEnvelope, Task<CommandAckMessage>> Acknowledge { get; set; } = _ => new TaskCompletionSource<CommandAckMessage>().Task;

        public Queue<string> Typed { get; } = new();

        public List<string> Prompts { get; } = new();

        public Task<CommandAckMessage>? Submit(CommandEnvelope command)
        {
            if (!Connected) return null;
            Sent.Add(command);
            return Acknowledge(command);
        }

        public void OpenKeyboard(string text, string prompt, Action<string> done)
        {
            Prompts.Add(prompt);
            if (Typed.Count > 0) done(Typed.Dequeue());
        }

        public DateTimeOffset Clock => DateTimeOffset.Parse(Samples.Time).AddSeconds(Now);

        public TimeZoneInfo Zone => TimeZoneInfo.Utc;

        public TextSize TextSize { get; set; } = TextSize.Standard;

        public int RowsOf(string words, float columnDegrees) => Math.Max(1, (words.Length + 59) / 60);

        public int RowsOf(PageLine line, float columnDegrees) => RowsOf(line.Words, columnDegrees);

        public bool FitsHalf(PageLine answer, float columnDegrees) => answer.Words.Length <= 24;

        public int TitleRows(string subject, float columnDegrees) => subject.Length <= 36 ? 1 : 2;

        public int PageRows(bool sourceLine) => MenuFrame.RowsAPage(TextSize, sourceLine);

        public float PageHeight(int subjectRows, bool besideMenu) => MenuPage.Height(TextSize, subjectRows, besideMenu: besideMenu);

        public void OpenFile(string workstreamId)
        {
        }

        public void OpenNewProject(string? projectId, string? projectName)
        {
        }
    }

    private sealed class Kept : IKeptCommand
    {
        public string? Id { get; set; }
    }

    private sealed class Memory : ICreationDraftStore
    {
        public List<CreationDraft> Drafts { get; private set; } = new();

        public IReadOnlyList<CreationDraft> Load() => Drafts;

        public void Save(IReadOnlyList<CreationDraft> drafts) => Drafts = drafts.ToList();
    }

    /// <summary>The control plane's HTTP API, answering each path with what a test gave it, and counting what was asked.</summary>
    private sealed class Routes : HttpMessageHandler
    {
        public Dictionary<string, Func<string>> Answers { get; } = new();

        public List<string> Asked { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.Method.Method + " " + request.RequestUri!.AbsolutePath;
            Asked.Add(key);
            var found = Answers.TryGetValue(key, out var answer);
            return Task.FromResult(new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(found ? answer!() : "{}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private static ClientProjection With(params CommandView[] commands)
    {
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(1);
        snapshot.Commands = commands.ToList();
        state.ApplySnapshot(snapshot, new StateChanges());
        return state;
    }

    private static CommandView Completed(CommandEnvelope command, CommandResult result) => new()
    {
        CommandId = command.CommandId, Status = CommandStatus.Completed, Result = result, IssuedAt = Samples.Time, UpdatedAt = Samples.Time,
    };

    private static NewProjectFlow Flow(Host host, Kept? kept = null, ICreationDraftStore? store = null) =>
        new(host, Commands, kept ?? new Kept(), store);

    /// <summary>Waits for what the flow asked the control plane, as its director's frames would.</summary>
    private static async Task Until(NewProjectFlow flow, Func<bool> done)
    {
        for (var tries = 0; tries < 200 && !done(); tries++)
        {
            await Task.Delay(10);
            flow.Tick();
        }
        Assert.That(done(), Is.True, "the flow got its answer");
    }

    /// <summary>A new project's idea typed, an agent app chosen, and the recap showing.</summary>
    private static NewProjectFlow Recapped(Host host, Kept? kept = null, ICreationDraftStore? store = null)
    {
        var flow = Flow(host, kept, store);
        flow.Open(null, null);
        host.Typed.Enqueue("A page of race times for my running club");
        flow.Act(NewProjectScreens.TypeIdea, null);
        flow.Act(NewProjectScreens.UseIdea, null);
        flow.Act(NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
        flow.Act(NewProjectScreens.MoreOptions, null);
        flow.Act(NewProjectScreens.ChooseRuntime, "mock");
        flow.Act(NewProjectScreens.Done, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap));
        Assert.That(flow.Idea!.HasRecap, Is.True);
        return flow;
    }

    /// <summary>The review read part by part, as the director draws each and the person presses Next part a second later.</summary>
    private static void ReadToTheEnd(NewProjectFlow flow, Host host)
    {
        flow.Drawn(flow.Frame!, false);
        for (var part = 0; part < 50 && flow.Review?.CanConfirm != true; part++)
        {
            host.Now += 1;
            flow.Act(NewProjectScreens.NextPart, null);
            flow.Drawn(flow.Frame!, false);
        }
    }

    private static IEnumerable<string> Ids(MenuFrame frame) =>
        frame.Footer.All.Select(each => each.Prompt.Id).Concat(frame.Lines.Where(line => line.Action != null).Select(line => line.Action!));

    [Test]
    public void NothingIsSentUntilTheReviewIsReadToItsEndAndItsYesPressed()
    {
        var host = new Host();
        var flow = Recapped(host);
        flow.Act(NewProjectScreens.StartBuilding, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Build));
        Assert.That(Ids(flow.Frame!), Does.Not.Contain(NewProjectScreens.ConfirmStart));
        flow.Act(NewProjectScreens.ConfirmStart, null);
        Assert.That(host.Sent, Is.Empty, "a Yes pressed before the review was read sends nothing");

        ReadToTheEnd(flow, host);
        Assert.That(flow.Review!.CanConfirm, Is.True);
        Assert.That(Ids(flow.Frame!), Does.Contain(NewProjectScreens.ConfirmStart));
        flow.Act(NewProjectScreens.ConfirmStart, null);
        Assert.That(host.Sent.Single(), Is.InstanceOf<ProjectCreateCommand>());
        Assert.That(((ProjectCreateCommand)host.Sent[0]).Payload.Name, Is.EqualTo(flow.Idea!.Name));
        flow.Act(NewProjectScreens.ConfirmStart, null);
        Assert.That(host.Sent, Has.Count.EqualTo(1), "a review confirms one send");
    }

    [Test]
    public void OnlyWhatTheFrameShowingOffersActs()
    {
        var host = new Host();
        var flow = Recapped(host);
        flow.Act(NewProjectScreens.ConfirmStartOver, null);
        Assert.That(flow.Idea!.HasRecap, Is.True, "Yes, start over stands nowhere until Start over is pressed");
        flow.Act(NewProjectScreens.Rename, null);
        Assert.That(flow.Frame!.Lines.Any(line => line.Action == NewProjectScreens.TypeWords), Is.False, "Change stands only beside a chosen fact");
        flow.Act(MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.Questions));
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap), "a step not reached takes no press");
        flow.Act(NewProjectScreens.ConfirmStart, null);
        flow.Act(NewProjectScreens.ChooseSuggestion, "0");
        Assert.That(host.Sent, Is.Empty);
        flow.Close();
        flow.Act(NewProjectScreens.StartBuilding, null);
        Assert.That(flow.Review, Is.Null, "closed, nothing acts");
    }

    [Test]
    public void ChoosingAStepThrowsTheReviewAwaySoTheRequestIsReadAgain()
    {
        var host = new Host();
        var flow = Recapped(host);
        flow.Act(NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Assert.That(flow.Review!.CanConfirm, Is.True);

        flow.Act(MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.Recap));
        Assert.That(flow.Review, Is.Null);
        host.Typed.Enqueue("Race Times, renamed on the way back");
        flow.Act(NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.Name));
        flow.Act(NewProjectScreens.Rename, null);
        flow.Act(NewProjectScreens.TypeWords, null);
        flow.Act(NewProjectScreens.Done, null);
        flow.Act(MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.Build));
        Assert.That(flow.Review!.CanConfirm, Is.False, "a fresh review, read again");
        flow.Act(NewProjectScreens.ConfirmStart, null);
        Assert.That(host.Sent, Is.Empty);
        ReadToTheEnd(flow, host);
        flow.Act(NewProjectScreens.ConfirmStart, null);
        Assert.That(((ProjectCreateCommand)host.Sent.Single()).Payload.Name, Is.EqualTo("Race Times, renamed on the way back"));
    }

    [Test]
    public void WhileABuildIsOnItsWayNothingMoreStartsAndStartBuildingSaysWhy()
    {
        var host = new Host();
        var flow = Recapped(host);
        flow.Act(NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        flow.Act(NewProjectScreens.ConfirmStart, null);
        Assert.That(flow.Sequence!.InFlight, Is.True);

        flow.Act(MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.Recap));
        var recap = flow.Frame!;
        Assert.That((recap.Footer[PromptSlot.FarRight]!.Available, recap.Reason), Is.EqualTo((false, EntryText.AlreadyStarting)));
        flow.Act(NewProjectScreens.StartBuilding, null);
        Assert.That(flow.Review, Is.Null, "no review while the build is on its way");
        Assert.That(host.Sent, Has.Count.EqualTo(1));
    }

    [Test]
    public void TryAgainAfterARefusalReadsTheRequestAgainBeforeAnythingIsSent()
    {
        var host = new Host();
        var flow = Recapped(host);
        flow.Act(NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        flow.Act(NewProjectScreens.ConfirmStart, null);
        var create = host.Sent.Single();
        host.State = With(new CommandView
        {
            CommandId = create.CommandId, Status = CommandStatus.Rejected, IssuedAt = Samples.Time, UpdatedAt = Samples.Time,
            Rejection = new CommandRejection { Code = RejectionCode.InvalidRuntimeOptions, Message = "Not now." },
        });
        flow.Tick();
        Assert.That(flow.Sequence!.CanRetry, Is.True);
        Assert.That(Ids(flow.Frame!), Does.Contain(NewProjectScreens.TryAgainStart));

        flow.Act(NewProjectScreens.TryAgainStart, null);
        Assert.That(host.Sent, Has.Count.EqualTo(1), "Try again opens the review; it sends nothing");
        Assert.That(flow.Review!.CanConfirm, Is.False);
        ReadToTheEnd(flow, host);
        flow.Act(NewProjectScreens.ConfirmStart, null);
        Assert.That(host.Sent, Has.Count.EqualTo(2));
        Assert.That(host.Sent[1], Is.InstanceOf<ProjectCreateCommand>());
    }

    [Test]
    public void AnUnknownOutcomeIsKeptOnTheDeviceAndComesFirstUntilCleared()
    {
        var host = new Host
        {
            Acknowledge = command => Task.FromException<CommandAckMessage>(new CommandOutcomeUnknownException(command.CommandId, "The socket closed.")),
        };
        var kept = new Kept();
        var flow = Recapped(host, kept);
        flow.Act(NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        flow.Act(NewProjectScreens.ConfirmStart, null);
        host.State = With();
        flow.Tick();
        Assert.That(kept.Id, Is.EqualTo(host.Sent.Single().CommandId), "kept, so a restart still blocks a blind retry");

        var restarted = Flow(new Host(), kept);
        restarted.Open(null, null);
        Assert.That(restarted.Step, Is.EqualTo(NewProjectStep.Build));
        var unresolved = restarted.Frame!;
        Assert.That(unresolved.Lines.Any(line => line.Words == "Reference: " + kept.Id), Is.True);
        restarted.Act(NewProjectScreens.ConfirmClear, null);
        Assert.That(kept.Id, Is.Not.Null, "one press never clears it");
        restarted.Act(NewProjectScreens.Clear, null);
        restarted.Act(NewProjectScreens.ConfirmClear, null);
        Assert.That(kept.Id, Is.Null);
        Assert.That(restarted.Step, Is.EqualTo(NewProjectStep.YourIdea));
    }

    [Test]
    public async Task TheCompanionIsAskedOnlyBySendAnswerAndItsProposalFillsTheRecap()
    {
        var routes = new Routes();
        routes.Answers["GET /api/companion"] = () => HalcyonicJson.Serialize(new AvailableCompanion
        {
            Companion = new CompanionModel { Name = "local-model:tag", Served = "this_mac" }, MaxQuestions = 4,
        });
        var replies = new Queue<CompanionReply>(new CompanionReply[] { Companions.Ask(), Companions.Propose() });
        routes.Answers["POST /api/companion/replies"] = () => HalcyonicJson.Serialize(Companions.Response(replies.Dequeue()));
        var host = new Host { Api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes) };
        var flow = Flow(host);
        flow.Open(null, null);
        await Until(flow, () => flow.Frame!.Lines.Any(line => line.Action == NewProjectScreens.ChooseCompanion));

        flow.Act(NewProjectScreens.BeginCompanion, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Questions));
        await Until(flow, () => flow.Idea!.Companion!.Latest is AskReply);
        Assert.That(routes.Asked.Count(asked => asked == "POST /api/companion/replies"), Is.EqualTo(1));

        flow.Act(NewProjectScreens.ChooseSuggestion, "1");
        Assert.That(routes.Asked.Count(asked => asked == "POST /api/companion/replies"), Is.EqualTo(1), "choosing sends nothing");
        flow.Act(NewProjectScreens.SendAnswer, null);
        await Until(flow, () => flow.Step == NewProjectStep.Recap);
        Assert.That(routes.Asked.Count(asked => asked == "POST /api/companion/replies"), Is.EqualTo(2));
        Assert.That(flow.Idea!.TaskSuggested, Is.True, "the proposal fills the recap, marked as the companion's");
        Assert.That(flow.Frame!.Source, Is.EqualTo(CompanionText.Note));
    }

    [Test]
    public void WithoutTheCompanionTheFixedQuestionsMakeTheRecap()
    {
        var host = new Host();
        var flow = Flow(host);
        flow.Open(null, null);
        var start = flow.Frame!;
        Assert.That(start.Lines.Any(line => line.Action == NewProjectScreens.ChooseQuestions), Is.True, "no companion was offered");
        flow.Act(NewProjectScreens.BeginQuestions, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Questions));
        flow.Act(NewProjectScreens.NextQuestion, null);
        Assert.That(flow.Idea!.Question, Is.EqualTo(0), "nothing chosen, nothing given");
        flow.Act(NewProjectScreens.ChooseFixedAnswer, "1");
        flow.Act(NewProjectScreens.NextQuestion, null);
        flow.Act(NewProjectScreens.ChooseFixedAnswer, "0");
        flow.Act(NewProjectScreens.NextQuestion, null);
        flow.Act(NewProjectScreens.ChooseFixedAnswer, "0");
        flow.Act(NewProjectScreens.NextQuestion, null);
        flow.Act(NewProjectScreens.SkipFixedQuestion, null);
        flow.Act(NewProjectScreens.NextQuestion, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap));
        Assert.That(flow.Idea.FirstTask, Is.EqualTo("Make an app for me. First, do its main job on one screen."));
    }

    [Test]
    public void LeavingForAnotherWindowSendsTheReviewBackToTheRecap()
    {
        var host = new Host();
        var flow = Recapped(host);
        flow.Act(NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        flow.FocusLeft();
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap));
        Assert.That(flow.Review, Is.Null);
        Assert.That(flow.Frame!.Lines[0].Words, Is.EqualTo(EntryText.ReviewAfresh));
        flow.Act(NewProjectScreens.ConfirmStart, null);
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void HeardWordsLandWhereWordsAreGivenAndAreNeverSentUnchecked()
    {
        var host = new Host();
        var flow = Flow(host);
        flow.Open(null, null);
        flow.Heard("A page of race times");
        Assert.That(flow.Idea!.OwnWords, Is.EqualTo("A page of race times"));
        Assert.That(flow.Frame!.Lines.Any(line => line.Words == VoiceText.HeardNote), Is.True);
        flow.Act(NewProjectScreens.UseIdea, null);
        flow.Act(NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.Name));
        flow.Act(NewProjectScreens.Rename, null);
        flow.Heard("Race Times");
        Assert.That(flow.Idea.Name, Is.Not.EqualTo("Race Times"), "heard words wait for Done");
        flow.Act(NewProjectScreens.Done, null);
        Assert.That(flow.Idea.Name, Is.EqualTo("Race Times"));
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void ADraftComesBackAfterARestartForTheSameComputer()
    {
        var store = new Memory();
        var host = new Host();
        var flow = Recapped(host, store: store);
        Assert.That(store.Drafts, Is.Not.Empty, "kept after the change");
        flow.Close();

        var restarted = Flow(new Host(), store: store);
        restarted.Open(null, null);
        Assert.That(restarted.Idea!.OwnWords, Is.EqualTo("A page of race times for my running club"));
        Assert.That(restarted.Step, Is.EqualTo(NewProjectStep.Recap));
        var elsewhere = new Host { State = new ClientProjection() };
        var other = Flow(elsewhere, store: store);
        other.Open(null, null);
        Assert.That(other.Idea!.HasRecap, Is.False, "nothing for a computer whose journal isn't live yet");
    }

    [Test]
    public async Task AKeptFolderShowsItsPlaceAsTheComputerListsItNowAndAGonePlaceSendsNothing()
    {
        static LocationsResponse Listing(params LocationRoot[] roots) => new() { Roots = roots.ToList() };
        static LocationRoot Place(string path, string label, LocationRootStatus status = LocationRootStatus.Available) => new()
        {
            Path = path, Name = "Projects", Label = label, Status = status, FoldersTruncated = false,
            Folders = new List<LocationFolder> { new() { Name = "race-times", Path = path + "/race-times" } },
        };
        var routes = new Routes();
        var listing = Listing(Place("/Users/person/Projects", "Projects"));
        routes.Answers["GET /api/locations"] = () => HalcyonicJson.Serialize(listing);
        var host = new Host { Api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes) };
        var flow = Recapped(host);
        flow.Act(NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.Folder));
        flow.Act(NewProjectScreens.ChooseWhere, null);
        await Until(flow, () => flow.Frame!.Lines.Any(line => line.Action == NewProjectScreens.ChooseFolder));
        var index = flow.Frame!.Lines.Where(line => line.Action == NewProjectScreens.ChooseFolder).First(line => line.Words == "race-times").Key;
        flow.Act(NewProjectScreens.ChooseFolder, index);
        flow.Act(NewProjectScreens.Done, null);
        Assert.That(flow.Idea!.Folder!.Describe(), Is.EqualTo("race-times in Projects"));

        // Another place now takes the label, and this one is gone: read again when the draft opens.
        listing = Listing(Place("/Projects", "Projects"), Place("/Users/person/Projects", "Projects (person)", LocationRootStatus.Missing));
        flow.Close();
        flow.Open(null, null);
        await Until(flow, () => flow.Idea!.Folder!.PlaceGone);
        Assert.That(flow.Idea!.Folder!.Describe(), Does.Contain("no longer lists"), "never the label now another place's");
        var recap = flow.Frame!;
        Assert.That((recap.Footer[PromptSlot.FarRight]!.Available, recap.Reason), Is.EqualTo((false, EntryText.ChooseWhereFilesLive)));
        flow.Act(NewProjectScreens.StartBuilding, null);
        Assert.That(flow.Review, Is.Null);

        // A read that fails changes nothing; the place back unblocks Start building by itself.
        routes.Answers.Remove("GET /api/locations");
        flow.Close();
        flow.Open(null, null);
        await Until(flow, () => routes.Asked.Count(asked => asked == "GET /api/locations") == 3);
        for (var tick = 0; tick < 5; tick++)
        {
            await Task.Delay(10);
            flow.Tick();
        }
        Assert.That(flow.Idea!.Folder!.PlaceGone, Is.True, "only a listing read marks a place, either way");
        listing = Listing(Place("/Projects", "Projects"), Place("/Users/person/Projects", "Projects (person)"));
        routes.Answers["GET /api/locations"] = () => HalcyonicJson.Serialize(listing);
        flow.Close();
        flow.Open(null, null);
        await Until(flow, () => !flow.Idea!.Folder!.PlaceGone);
        Assert.That(flow.Idea!.Folder!.Describe(), Is.EqualTo("race-times in Projects (person)"));
        Assert.That(flow.Frame!.Footer[PromptSlot.FarRight]!.Available, Is.True);
    }
}

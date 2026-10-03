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

        public bool KeyboardOffered { get; set; } = true;

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

    /// <summary>
    /// A press as the person makes one: on the frame the director drew for them, turning the page by its
    /// row first, as they would, while the keyed line they press stands on another page of it. A
    /// prompt stands on every page, so none is looked for elsewhere.
    /// </summary>
    private static void Press(NewProjectFlow flow, string id, string? key)
    {
        for (var turned = 0; turned < 10 && key != null && flow.Frame is MenuFrame frame && !Shows(frame, id, key)
            && frame.Lines.LastOrDefault() is PageLine { Action: NewProjectScreens.NextPage } turn && turn.Key != null; turned++)
        {
            flow.Drawn(frame, false);
            flow.Act(NewProjectScreens.NextPage, turn.Key);
            if (Shows(flow.Frame!, id, key)) break;
        }
        if (flow.Frame is MenuFrame drawn) flow.Drawn(drawn, false);
        flow.Act(id, key);
    }

    /// <summary>
    /// Every page of what shows, as the person turns them by the row at each page's end, from the one
    /// showing back round to it; one page when everything fits.
    /// </summary>
    private static List<MenuFrame> Pages(NewProjectFlow flow)
    {
        var pages = new List<MenuFrame> { flow.Frame! };
        while (pages.Count < 10 && pages[^1].Lines.LastOrDefault() is PageLine { Action: NewProjectScreens.NextPage } turn)
        {
            flow.Drawn(pages[^1], false);
            flow.Act(NewProjectScreens.NextPage, turn.Key);
            if (flow.Frame!.Lines.Last().Key == pages[0].Lines.Last().Key) break;
            pages.Add(flow.Frame!);
        }
        return pages;
    }

    /// <summary>Whether a frame shows a line or prompt raising <paramref name="id"/> with <paramref name="key"/>, whatever it allows now.</summary>
    private static bool Shows(MenuFrame frame, string id, string? key) =>
        frame.Footer.All.Any(each => each.Prompt.Id == id) || frame.Lines.Any(line => line.Action == id && line.Key == key) || id == MenuFrame.ChooseSection
        || id == Footer.Close;

    /// <summary>A suggestion's key, on the companion's question showing now.</summary>
    private static string Suggestion(NewProjectFlow flow, string words) => NewProjectScreens.AnswerKey(flow.Idea!.Companion!.Generation, words);

    /// <summary>A fixed answer's key, on the fixed question showing now.</summary>
    private static string Fixed(NewProjectFlow flow, string words) => NewProjectScreens.AnswerKey(flow.Idea!.Question, words);

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
        Press(flow, NewProjectScreens.TypeIdea, null);
        Press(flow, NewProjectScreens.UseIdea, null);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
        Press(flow, NewProjectScreens.MoreOptions, null);
        Press(flow, NewProjectScreens.ChooseRuntime, "mock");
        Press(flow, NewProjectScreens.Done, null);
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
            Press(flow, NewProjectScreens.NextPart, null);
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
        Press(flow, NewProjectScreens.StartBuilding, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Build));
        Assert.That(Ids(flow.Frame!), Does.Not.Contain(NewProjectScreens.ConfirmStart));
        Press(flow, NewProjectScreens.ConfirmStart, null);
        Assert.That(host.Sent, Is.Empty, "a Yes pressed before the review was read sends nothing");

        ReadToTheEnd(flow, host);
        Assert.That(flow.Review!.CanConfirm, Is.True);
        Assert.That(Ids(flow.Frame!), Does.Contain(NewProjectScreens.ConfirmStart));
        Press(flow, NewProjectScreens.ConfirmStart, null);
        Assert.That(host.Sent.Single(), Is.InstanceOf<ProjectCreateCommand>());
        Assert.That(((ProjectCreateCommand)host.Sent[0]).Payload.Name, Is.EqualTo(flow.Idea!.Name));
        Press(flow, NewProjectScreens.ConfirmStart, null);
        Assert.That(host.Sent, Has.Count.EqualTo(1), "a review confirms one send");
    }

    [Test]
    public void OnlyWhatTheFrameShowingOffersActs()
    {
        var host = new Host();
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.ConfirmStartOver, null);
        Assert.That(flow.Idea!.HasRecap, Is.True, "Yes, start over stands nowhere until Start over is pressed");
        Press(flow, NewProjectScreens.Rename, null);
        Assert.That(flow.Frame!.Lines.Any(line => line.Action == NewProjectScreens.TypeWords), Is.False, "Change stands only beside a chosen fact");
        Press(flow, MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.Questions));
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap), "a step not reached takes no press");
        Press(flow, NewProjectScreens.ConfirmStart, null);
        Press(flow, NewProjectScreens.ChooseSuggestion, "Each runner");
        Assert.That(host.Sent, Is.Empty);
        flow.Close();
        Press(flow, NewProjectScreens.StartBuilding, null);
        Assert.That(flow.Review, Is.Null, "closed, nothing acts");
    }

    [Test]
    public void ChoosingAStepThrowsTheReviewAwaySoTheRequestIsReadAgain()
    {
        var host = new Host();
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Assert.That(flow.Review!.CanConfirm, Is.True);

        Press(flow, MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.Recap));
        Assert.That(flow.Review, Is.Null);
        host.Typed.Enqueue("Race Times, renamed on the way back");
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.Name));
        Press(flow, NewProjectScreens.Rename, null);
        Press(flow, NewProjectScreens.TypeWords, null);
        Press(flow, NewProjectScreens.Done, null);
        Press(flow, MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.Build));
        Assert.That(flow.Review!.CanConfirm, Is.False, "a fresh review, read again");
        Press(flow, NewProjectScreens.ConfirmStart, null);
        Assert.That(host.Sent, Is.Empty);
        ReadToTheEnd(flow, host);
        Press(flow, NewProjectScreens.ConfirmStart, null);
        Assert.That(((ProjectCreateCommand)host.Sent.Single()).Payload.Name, Is.EqualTo("Race Times, renamed on the way back"));
    }

    [Test]
    public void WhileABuildIsOnItsWayNothingMoreStartsAndStartBuildingSaysWhy()
    {
        var host = new Host();
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Press(flow, NewProjectScreens.ConfirmStart, null);
        Assert.That(flow.Sequence!.InFlight, Is.True);

        Press(flow, MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.Recap));
        var recap = flow.Frame!;
        Assert.That((recap.Footer[PromptSlot.FarRight]!.Available, recap.Reason), Is.EqualTo((false, EntryText.AlreadyStarting)));
        Press(flow, NewProjectScreens.StartBuilding, null);
        Assert.That(flow.Review, Is.Null, "no review while the build is on its way");
        Assert.That(host.Sent, Has.Count.EqualTo(1));
    }

    [Test]
    public void TryAgainAfterARefusalReadsTheRequestAgainBeforeAnythingIsSent()
    {
        var host = new Host();
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Press(flow, NewProjectScreens.ConfirmStart, null);
        var create = host.Sent.Single();
        host.State = With(new CommandView
        {
            CommandId = create.CommandId, Status = CommandStatus.Rejected, IssuedAt = Samples.Time, UpdatedAt = Samples.Time,
            Rejection = new CommandRejection { Code = RejectionCode.InvalidRuntimeOptions, Message = "Not now." },
        });
        flow.Tick();
        Assert.That(flow.Sequence!.CanRetry, Is.True);
        Assert.That(Ids(flow.Frame!), Does.Contain(NewProjectScreens.TryAgainStart));

        Press(flow, NewProjectScreens.TryAgainStart, null);
        Assert.That(host.Sent, Has.Count.EqualTo(1), "Try again opens the review; it sends nothing");
        Assert.That(flow.Review!.CanConfirm, Is.False);
        ReadToTheEnd(flow, host);
        Press(flow, NewProjectScreens.ConfirmStart, null);
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
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Press(flow, NewProjectScreens.ConfirmStart, null);
        host.State = With();
        flow.Tick();
        Assert.That(kept.Id, Is.EqualTo(host.Sent.Single().CommandId), "kept, so a restart still blocks a blind retry");

        var restarted = Flow(new Host(), kept);
        restarted.Open(null, null);
        Assert.That(restarted.Step, Is.EqualTo(NewProjectStep.Build));
        var unresolved = restarted.Frame!;
        Assert.That(unresolved.Lines.Any(line => line.Words == "Reference: " + kept.Id), Is.True);
        Press(restarted, NewProjectScreens.ConfirmClear, null);
        Assert.That(kept.Id, Is.Not.Null, "one press never clears it");
        Press(restarted, NewProjectScreens.Clear, null);
        Press(restarted, NewProjectScreens.ConfirmClear, null);
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

        Press(flow, NewProjectScreens.BeginCompanion, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Questions));
        await Until(flow, () => flow.Idea!.Companion!.Latest is AskReply);
        Assert.That(routes.Asked.Count(asked => asked == "POST /api/companion/replies"), Is.EqualTo(1));

        Press(flow, NewProjectScreens.ChooseSuggestion, Suggestion(flow, "One organiser"));
        Assert.That(routes.Asked.Count(asked => asked == "POST /api/companion/replies"), Is.EqualTo(1), "choosing sends nothing");
        Press(flow, NewProjectScreens.SendAnswer, null);
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
        Press(flow, NewProjectScreens.BeginQuestions, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Questions));
        Press(flow, NewProjectScreens.NextQuestion, null);
        Assert.That(flow.Idea!.Question, Is.EqualTo(0), "nothing chosen, nothing given");
        Press(flow, NewProjectScreens.ChooseFixedAnswer, Fixed(flow, "An app"));
        Press(flow, NewProjectScreens.NextQuestion, null);
        Press(flow, NewProjectScreens.ChooseFixedAnswer, Fixed(flow, "Just me"));
        Press(flow, NewProjectScreens.NextQuestion, null);
        Press(flow, NewProjectScreens.ChooseFixedAnswer, Fixed(flow, "Do its main job on one screen"));
        Press(flow, NewProjectScreens.NextQuestion, null);
        Press(flow, NewProjectScreens.SkipFixedQuestion, null);
        Press(flow, NewProjectScreens.NextQuestion, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap));
        Assert.That(flow.Idea.FirstTask, Is.EqualTo("Make an app for me. First, do its main job on one screen."));
    }

    [Test]
    public void LeavingForAnotherWindowSendsTheReviewBackToTheRecap()
    {
        var host = new Host();
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        flow.FocusLeft();
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap));
        Assert.That(flow.Review, Is.Null);
        Assert.That(flow.Frame!.Lines[0].Words, Is.EqualTo(EntryText.ReviewAfresh));
        Press(flow, NewProjectScreens.ConfirmStart, null);
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
        Press(flow, NewProjectScreens.UseIdea, null);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.Name));
        Press(flow, NewProjectScreens.Rename, null);
        flow.Heard("Race Times");
        Assert.That(flow.Idea.Name, Is.Not.EqualTo("Race Times"), "heard words wait for Done");
        Press(flow, NewProjectScreens.Done, null);
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
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.Folder));
        Press(flow, NewProjectScreens.ChooseWhere, null);
        await Until(flow, () => flow.Frame!.Lines.Any(line => line.Action == NewProjectScreens.ChooseFolder));
        var index = flow.Frame!.Lines.Where(line => line.Action == NewProjectScreens.ChooseFolder).First(line => line.Words == "race-times").Key;
        Press(flow, NewProjectScreens.ChooseFolder, index);
        Press(flow, NewProjectScreens.Done, null);
        Assert.That(flow.Idea!.Folder!.Describe(), Is.EqualTo("race-times in Projects"));

        // Another place now takes the label, and this one is gone: read again when the draft opens.
        listing = Listing(Place("/Projects", "Projects"), Place("/Users/person/Projects", "Projects (person)", LocationRootStatus.Missing));
        flow.Close();
        flow.Open(null, null);
        await Until(flow, () => flow.Idea!.Folder!.PlaceGone);
        Assert.That(flow.Idea!.Folder!.Describe(), Does.Contain("no longer lists"), "never the label now another place's");
        var recap = flow.Frame!;
        Assert.That((recap.Footer[PromptSlot.FarRight]!.Available, recap.Reason), Is.EqualTo((false, EntryText.ChooseWhereFilesLive)));
        Press(flow, NewProjectScreens.StartBuilding, null);
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

    /// <summary>A build sent whose acknowledgement was lost: its outcome unknown, its id kept.</summary>
    private static NewProjectFlow Unknown(Host host, Kept kept)
    {
        host.Acknowledge = command => Task.FromException<CommandAckMessage>(new CommandOutcomeUnknownException(command.CommandId, "The socket closed."));
        var flow = Recapped(host, kept);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Press(flow, NewProjectScreens.ConfirmStart, null);
        host.State = With();
        flow.Tick();
        Assert.That(kept.Id, Is.EqualTo(host.Sent.Single().CommandId));
        return flow;
    }

    [Test]
    public void AfterARestartAKeptUnknownStartHoldsEveryNewStartAndIsNeverReplaced()
    {
        var kept = new Kept { Id = "01a0dcf1-5a80-7000-8000-0000000000c1" };
        var host = new Host();
        var flow = Flow(host, kept);
        flow.Open(null, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Build), "the unknown start comes first");

        // The steps still lead to Your idea, and a new idea reaches the recap.
        Press(flow, MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.YourIdea));
        host.Typed.Enqueue("Another page");
        Press(flow, NewProjectScreens.TypeIdea, null);
        Press(flow, NewProjectScreens.UseIdea, null);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
        Press(flow, NewProjectScreens.MoreOptions, null);
        Press(flow, NewProjectScreens.ChooseRuntime, "mock");
        Press(flow, NewProjectScreens.Done, null);
        var recap = flow.Frame!;
        Assert.That((recap.Footer[PromptSlot.FarRight]!.Available, recap.Reason), Is.EqualTo((false, EntryText.PreviousRequestLine)));
        Press(flow, NewProjectScreens.StartBuilding, null);
        Assert.That(flow.Review, Is.Null, "nothing starts while an earlier start may have run");
        Assert.That(recap.Sections.Single(step => step.Key == NewProjectScreens.Key(NewProjectStep.Build)).Reached, Is.True);
        Press(flow, MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.Build));
        Assert.That(flow.Frame!.Lines.Any(line => line.Words == "Reference: " + kept.Id), Is.True, "Build shows the start to check");
        Assert.That(host.Sent, Is.Empty);
        Assert.That(kept.Id, Is.EqualTo("01a0dcf1-5a80-7000-8000-0000000000c1"), "never replaced");
    }

    [Test]
    public void InASessionAnUnknownStartCannotBeStartedOverOrStartedAgain()
    {
        var host = new Host();
        var kept = new Kept();
        var flow = Unknown(host, kept);
        var first = kept.Id;
        Press(flow, MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.Recap));
        var recap = flow.Frame!;
        Assert.That(recap.Footer[PromptSlot.Rare]!.Available, Is.False, "Start over waits too");
        Press(flow, NewProjectScreens.StartOver, null);
        Press(flow, NewProjectScreens.ConfirmStartOver, null);
        Assert.That(flow.Sequence, Is.Not.Null, "the build is still held");
        Press(flow, NewProjectScreens.StartBuilding, null);
        Assert.That(flow.Review, Is.Null);
        Assert.That(host.Sent, Has.Count.EqualTo(1), "no second project.create");
        Assert.That(kept.Id, Is.EqualTo(first));
    }

    [Test]
    public void ABuildConfirmedGoesOnAfterCloseAndOpeningAgainShowsWhereItStands()
    {
        var host = new Host();
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Press(flow, NewProjectScreens.ConfirmStart, null);
        var create = host.Sent.Single();
        Press(flow, Footer.Close, null);
        Assert.That(flow.IsOpen, Is.False);

        host.State = With(Completed(create, new ProjectCreatedResult { ProjectId = "p1" }));
        flow.Tick();
        Assert.That(host.Sent.Last(), Is.InstanceOf<WorkstreamCreateCommand>(), "the Yes confirmed the whole build");
        Press(flow, NewProjectScreens.StartBuilding, null);
        Assert.That(host.Sent, Has.Count.EqualTo(2), "closed, no new press acts");
        flow.Open(null, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Build));
        Assert.That(flow.Frame!.Lines.Any(line => line.Words == EntryText.StepName(BuildStepKind.CreateWorkstream, true)), Is.True);
    }

    private static ClientProjection OnJournal(string id, JournalOrigin origin = JournalOrigin.Live, IEnumerable<CommandView>? commands = null)
    {
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(1, journal: Samples.Journal(id, origin));
        snapshot.Commands = (commands ?? Array.Empty<CommandView>()).ToList();
        state.ApplySnapshot(snapshot, new StateChanges());
        return state;
    }

    [Test]
    public void ABuildBegunOnOneJournalSendsNothingMoreOnceTheSessionIsOnAnother()
    {
        var host = new Host { State = OnJournal("journal-1") };
        var flow = Flow(host);
        flow.Open(null, null);
        host.Typed.Enqueue("A page of race times for my running club");
        Press(flow, NewProjectScreens.TypeIdea, null);
        Press(flow, NewProjectScreens.UseIdea, null);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
        Press(flow, NewProjectScreens.MoreOptions, null);
        Press(flow, NewProjectScreens.ChooseRuntime, "mock");
        Press(flow, NewProjectScreens.Done, null);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Press(flow, NewProjectScreens.ConfirmStart, null);
        var create = host.Sent.Single();
        Press(flow, Footer.Close, null);

        // The session is now on another journal, and the first one's record of the create comes late.
        host.State = OnJournal("journal-2", commands: new[] { Completed(create, new ProjectCreatedResult { ProjectId = "p1" }) });
        flow.Tick();
        Assert.That(host.Sent, Has.Count.EqualTo(1), "no workstream.create for the first journal's project goes to the second");
        Assert.That(flow.ForAnotherSession, Is.True);
        flow.Open(null, null);
        Assert.That(flow.IsOpen, Is.False);
    }

    [Test]
    public void NewProjectMadeInTheDemonstrationNeverReadsOrSendsOnceARealSessionTakesItsPlace()
    {
        var routes = new Routes();
        var host = new Host { Demonstration = true, State = OnJournal("demonstration", JournalOrigin.Fixture) };
        var flow = Flow(host);
        flow.Open(null, null);
        host.Typed.Enqueue("A page of race times for my running club");
        Press(flow, NewProjectScreens.TypeIdea, null);
        Press(flow, NewProjectScreens.UseIdea, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap));

        // The computer answers, and its session takes the demonstration's place.
        host.Demonstration = false;
        host.State = OnJournal("journal-1");
        host.Api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes);
        Assert.That(flow.ForAnotherSession, Is.True);
        flow.Tick();
        Assert.That(flow.IsOpen, Is.False);
        Press(flow, NewProjectScreens.StartBuilding, null);
        Assert.That(flow.Review, Is.Null, "a draft made in the demonstration never starts on the computer");
        flow.Open(null, null);
        Assert.That(flow.IsOpen, Is.False, "the director makes New project afresh for this session");
        Assert.That(host.Sent, Is.Empty);
        Assert.That(routes.Asked, Is.Empty, "nothing was read from the computer either");
    }

    [Test]
    public void TheDemonstrationPlaysTheRecordedExchangeToARecapThatCanNotStartAndSendsNothing()
    {
        var recording = CompanionRecording.Parse(System.IO.File.ReadAllText(Repository.PathTo(
            "apps/xr/Assets/Halcyonic/Resources/" + CompanionRecording.ResourceName + ".json")));
        var host = new Host { Demonstration = true, KeyboardOffered = false, State = OnJournal("demonstration", JournalOrigin.Fixture) };
        var flow = new NewProjectFlow(host, Commands, new Kept(), null, recording);
        flow.Open(null, null);

        // Your idea: talking it through is the main action, and the recording brings its own idea.
        var yourIdea = flow.Frame!;
        Assert.That(yourIdea.Lines.Any(line => line.Action == NewProjectScreens.TypeIdea), Is.False, "no keyboard opens here");
        Assert.That(yourIdea.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.BeginCompanion));
        Assert.That(yourIdea.Footer[PromptSlot.Secondary], Is.Null, "no Hold to talk in the demonstration");
        Press(flow, NewProjectScreens.BeginCompanion, null);
        Assert.That((flow.Step, flow.Idea!.OwnWords), Is.EqualTo((NewProjectStep.Questions, recording.Idea)));

        // Questions: the companion's words quoted as its own, said to be recorded, and only the recorded answer offered.
        var questions = flow.Frame!;
        Assert.That(questions.Source, Is.EqualTo(CompanionText.Recorded));
        Assert.That(questions.Lines.Any(line => line.Claim && line.Words.StartsWith("The companion says: “", StringComparison.Ordinal)), Is.True);
        var exchange = flow.Idea.Companion!;
        var answer = NewProjectScreens.AnswerKey(exchange.Generation, recording.RecordedAnswer(exchange)!);
        Assert.That(questions.Lines.Where(line => line.Action == NewProjectScreens.ChooseSuggestion && line.Pressable).Select(line => line.Key),
            Is.EqualTo(new[] { answer }));
        Press(flow, NewProjectScreens.ChooseSuggestion, answer);
        Press(flow, NewProjectScreens.SendAnswer, null);

        // The recording's proposal fills the recap, marked as the companion's, and the demonstration can't start it.
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap));
        var recap = flow.Frame!;
        var pages = Pages(flow);
        Assert.That(pages.All(page => page.Source == CompanionText.Note), Is.True, "the note that it is an AI that can be wrong, on every page");
        Assert.That(pages.All(page => page.Reason == EntryText.DemoCannotStart), Is.True);
        Assert.That(pages.SelectMany(page => page.Lines).Any(line => line.Fact == CompanionText.SuggestedShort), Is.True);
        var start = recap.Footer[PromptSlot.FarRight]!;
        Assert.That((start.Id, start.Available, recap.Reason), Is.EqualTo((NewProjectScreens.StartBuilding, false, EntryText.DemoCannotStart)));
        Assert.That(recap.Sections.Single(step => step.Key == NewProjectScreens.Key(NewProjectStep.Build)).Reached, Is.False);
        Press(flow, NewProjectScreens.StartBuilding, null);
        Assert.That((flow.Step, flow.Review), Is.EqualTo((NewProjectStep.Recap, (NewWorkReview?)null)));
        Assert.That(host.Sent, Is.Empty, "nothing is sent");
    }

    [Test]
    public void ALiveNewProjectActsOnlyForTheJournalItWasMadeFor()
    {
        var host = new Host { State = OnJournal("journal-1") };
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Assert.That(flow.Review!.CanConfirm, Is.True);

        host.State = OnJournal("journal-2");
        Press(flow, NewProjectScreens.ConfirmStart, null);
        Assert.That(host.Sent, Is.Empty, "a review read for one computer is never sent to another");
        flow.Tick();
        Assert.That((flow.IsOpen, flow.ForAnotherSession), Is.EqualTo((false, true)));
        host.State = OnJournal("journal-1");
        Assert.That(flow.ForAnotherSession, Is.True, "for good: the director makes it afresh");
    }

    [Test]
    public void WhereTheKeyboardCantOpenNoRowOpensItAndHeardWordsStayAChoice()
    {
        var host = new Host { KeyboardOffered = false };
        var flow = Flow(host);
        flow.Open(null, null);
        Assert.That(flow.Frame!.Lines.Any(line => line.Action == NewProjectScreens.TypeIdea), Is.False);
        flow.Heard("A page of race times for my running club");
        Press(flow, NewProjectScreens.ChooseQuestions, null);
        Press(flow, NewProjectScreens.TypeIdea, null);
        Assert.That(host.Prompts, Is.Empty, "no keyboard was asked to open");
        Assert.That(flow.Frame!.Lines.Single(line => line.Action == NewProjectScreens.TypeIdea).Chosen, Is.True, "the heard idea is chosen again");
        Press(flow, NewProjectScreens.UseIdea, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap));
    }

    [Test]
    public void WithNeitherTheKeyboardNorHoldToTalkRenamingIsNotOfferedAndItsPressDoesNothing()
    {
        var host = new Host();
        var flow = Recapped(host);
        host.KeyboardOffered = false;
        host.VoiceOffered = false;
        flow.Tick();
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.Name));
        Assert.That(Ids(flow.Frame!), Does.Not.Contain(NewProjectScreens.Rename));
        Press(flow, NewProjectScreens.Rename, null);
        Assert.That(flow.Frame!.Lines.Any(line => line.Words == EntryText.ProjectName), Is.False, "no page of words opens");

        host.VoiceOffered = true;
        flow.Tick();
        Assert.That(Ids(flow.Frame!), Does.Contain(NewProjectScreens.Rename), "Hold to talk can give the name");
    }

    // ---------------------------------------------------------------------------------------------
    // Pages that need more than the stage gives (ADR 0026): each packed into a Quest 3S's page.

    private const float Column = Glaze.Menu.FileColumnDegrees;

    /// <summary>What a page's lines take as the view lays them, measured on its own here, to check the flow's packing against.</summary>
    private static float LinesHeight(Host host, IReadOnlyList<PageLine> lines)
    {
        var total = 0f;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (index > 0) total += lines[index - 1].Action != null && line.Action != null ? MenuPage.TargetGap : MenuPage.Grid;
            var next = index + 1 < lines.Count ? lines[index + 1] : null;
            if (next != null && ((line.Choice && next.Choice) || line.BesideNext) && host.FitsHalf(line, Column) && host.FitsHalf(next, Column))
            {
                total += MenuPage.Target();
                index++;
                continue;
            }
            var rows = Math.Max(1, Math.Min(line.Rows, host.RowsOf(line, Column)));
            total += line.Action != null ? MenuPage.Target(rows) : MenuPage.Words(rows);
        }
        return total;
    }

    /// <summary>
    /// Every page of what shows, at both text sizes, inside the page a Quest 3S gives it beside its
    /// reason and source line, each ending in the row that turns it where there are several; returns
    /// every line shown, the turning rows left out.
    /// </summary>
    private static List<PageLine> FitsEveryPage(NewProjectFlow flow, Host host, string what)
    {
        var shown = new List<PageLine>();
        foreach (var size in new[] { TextSize.Standard, TextSize.Larger })
        {
            host.TextSize = size;
            flow.Tick();
            var pages = Pages(flow);
            for (var index = 0; index < pages.Count; index++)
            {
                var page = pages[index];
                var room = host.PageHeight(Math.Max(1, host.TitleRows(page.Subject, Column)), besideMenu: true);
                if (page.Source != null) room -= MenuPage.GroupGap + MenuPage.Words(Math.Max(1, host.RowsOf(page.Source, Column)));
                if (page.Reason != null) room -= MenuPage.GroupGap + MenuPage.Words(Math.Max(1, host.RowsOf(page.Reason, Column)));
                var name = what + " at " + size + ", page " + (index + 1) + " of " + pages.Count;
                Assert.That(LinesHeight(host, page.Lines), Is.LessThanOrEqualTo(room + 1e-5f), name + " fits");
                if (pages.Count > 1)
                {
                    Assert.That((page.Lines[^1].Words, page.Lines[^1].Key), Is.EqualTo((EntryText.NextPage(index, pages.Count), index.ToString())), name + " turns");
                }
                if (size == TextSize.Standard) shown.AddRange(page.Lines.Where(line => line.Action != NewProjectScreens.NextPage));
            }
        }
        host.TextSize = TextSize.Standard;
        flow.Tick();
        return shown;
    }

    private static ClientProjection WithRuntimes(params RuntimeDescriptor[] runtimes)
    {
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(1);
        snapshot.Runtimes = runtimes.ToList();
        state.ApplySnapshot(snapshot, new StateChanges());
        return state;
    }

    private static RuntimeDescriptor Runtime(string id, ModelChoice choice = ModelChoice.None)
    {
        var runtime = Samples.MockRuntime();
        runtime.RuntimeId = id;
        runtime.DisplayName = "Agent app " + id;
        runtime.ModelChoice = choice;
        return runtime;
    }

    [Test]
    public void TheRecapPagesItsFactsAndKeepsItsFirstLineOnItsFirstPage()
    {
        var host = new Host();
        var flow = Recapped(host);
        var shown = FitsEveryPage(flow, host, "the recap");
        Assert.That(shown.Where(line => line.Action == NewProjectScreens.ChooseFact).Select(line => line.Key), Is.EquivalentTo(new[]
        {
            NewProjectScreens.FactKey(RecapFact.Name), NewProjectScreens.FactKey(RecapFact.FirstTask),
            NewProjectScreens.FactKey(RecapFact.Folder), NewProjectScreens.FactKey(RecapFact.HowItRuns),
        }), "every fact once");
        Assert.That(flow.Frame!.Lines[0].Action, Is.Null, "the recap's line first");

        // A fact chosen on a later page keeps that page, and its side panel slides out from it.
        host.TextSize = TextSize.Larger;
        flow.Tick();
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
        var chosen = flow.Frame!;
        Assert.That(chosen.Side, Is.Not.Null);
        Assert.That(chosen.Lines.Single(line => line.Chosen).Key, Is.EqualTo(NewProjectScreens.FactKey(RecapFact.HowItRuns)));

        // Turning the page closes it, and the pages turn round as before.
        var turn = chosen.Lines[^1];
        Press(flow, NewProjectScreens.NextPage, turn.Key);
        Assert.That((flow.Frame!.Side, flow.Frame.Lines[^1].Key == turn.Key), Is.EqualTo(((SidePanel?)null, false)));
    }

    [Test]
    public void ALongListOfFoldersPagesAndEveryFolderShowsOnce()
    {
        var root = new LocationRoot
        {
            Path = "/Users/person/Projects", Name = "Projects", Status = LocationRootStatus.Available, FoldersTruncated = false,
            Folders = Enumerable.Range(1, 12).Select(index => new LocationFolder { Name = "folder-" + index, Path = "/Users/person/Projects/folder-" + index }).ToList(),
        };
        var routes = new Routes();
        routes.Answers["GET /api/locations"] = () => HalcyonicJson.Serialize(new LocationsResponse { Roots = new List<LocationRoot> { root } });
        var host = new Host { Api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes) };
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.Folder));
        Press(flow, NewProjectScreens.ChooseWhere, null);
        Until(flow, () => flow.Frame!.Lines.Any(line => line.Action == NewProjectScreens.ChooseFolder)).Wait();
        var shown = FitsEveryPage(flow, host, "where its files live");
        Assert.That(shown.Count(line => line.Action == NewProjectScreens.ChooseFolder), Is.EqualTo(14), "a new folder, the place itself and 12 folders, each once");
        Press(flow, NewProjectScreens.ChooseFolder, shown.Single(line => line.Words == "folder-12").Key);
        Press(flow, NewProjectScreens.Done, null);
        Assert.That(flow.Idea!.Folder!.Describe(), Is.EqualTo("folder-12 in Projects"), "a folder on a later page is chosen like any other");
    }

    [Test]
    public void ManyAgentAppsAndModelsPage()
    {
        var local = Runtime("local", ModelChoice.Listed);
        var host = new Host { State = WithRuntimes(new[] { Runtime("mock"), local }.Concat(Enumerable.Range(1, 6).Select(index => Runtime("app-" + index))).ToArray()) };
        var routes = new Routes();
        routes.Answers["GET /api/runtimes/local/models"] = () => HalcyonicJson.Serialize(new RuntimeModelsResponse
        {
            RuntimeId = "local",
            Result = new AvailableModels
            {
                Models = Enumerable.Range(1, 10).Select(index => new RuntimeModel
                {
                    ModelRef = "ollama/model-" + index, DisplayName = "Model " + index, Served = ModelServed.ThisMac, ToolCalling = ModelToolCalling.Declared,
                }).ToList(),
            },
        });
        host.Api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes);
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
        Press(flow, NewProjectScreens.MoreOptions, null);
        var apps = FitsEveryPage(flow, host, "how it runs, its agent apps");
        Assert.That(apps.Count(line => line.Action == NewProjectScreens.ChooseRuntime), Is.EqualTo(8), "every agent app once");

        Press(flow, NewProjectScreens.ChooseRuntime, "local");
        Until(flow, () => flow.Frame!.Lines.Count(line => line.Action == NewProjectScreens.ChooseModel) > 0).Wait();
        var models = FitsEveryPage(flow, host, "how it runs, its models");
        Assert.That(models.Count(line => line.Action == NewProjectScreens.ChooseModel), Is.EqualTo(10), "every model once");
    }

    [Test]
    public void StartingPagesItsStepsAtTheLargerSize()
    {
        var host = new Host();
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Press(flow, NewProjectScreens.ConfirmStart, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Build));
        var shown = FitsEveryPage(flow, host, "starting");
        Assert.That(shown.Count(line => line.Words == EntryText.StepName(BuildStepKind.CreateProject, true)), Is.EqualTo(1), "every step once");
        Assert.That(host.Sent, Has.Count.EqualTo(1), "turning a page sends nothing");
    }

    [Test]
    public void APageTurnsOnlyFromThePageItWasPressedOn()
    {
        var host = new Host { TextSize = TextSize.Larger };
        var flow = Recapped(host);
        var first = flow.Frame!;
        flow.Drawn(first, false);
        var turn = first.Lines[^1];
        Assert.That(turn.Action, Is.EqualTo(NewProjectScreens.NextPage));
        flow.Act(NewProjectScreens.NextPage, turn.Key);
        var second = flow.Frame!;
        flow.Act(NewProjectScreens.NextPage, turn.Key);
        Assert.That(flow.Frame!.Lines.Select(line => line.Key), Is.EqualTo(second.Lines.Select(line => line.Key)), "a second press on the page left behind turns nothing");
    }

    [Test]
    public void AfterAReconnectMidBuildTheSameFlowSettlesTheStartFromItsRecordAndNeverCreatesTwice()
    {
        var drop = new TaskCompletionSource<CommandAckMessage>();
        var host = new Host { Acknowledge = _ => drop.Task };
        var kept = new Kept();
        var flow = Recapped(host, kept);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Press(flow, NewProjectScreens.ConfirmStart, null);
        var create = host.Sent.Single();

        // The socket drops with the create on its way, so its outcome is unknown; New project closes meanwhile.
        host.Connected = false;
        drop.SetException(new CommandOutcomeUnknownException(create.CommandId, "The socket closed."));
        Press(flow, Footer.Close, null);
        flow.Tick();
        Assert.That(kept.Id, Is.EqualTo(create.CommandId));

        // Reconnected to the same journal: its record settles the create, and the build goes on.
        host.Connected = true;
        host.State = With(Completed(create, new ProjectCreatedResult { ProjectId = "p1" }));
        flow.Tick();
        Assert.That(host.Sent.Count(each => each is ProjectCreateCommand), Is.EqualTo(1), "never a second project.create");
        Assert.That(host.Sent.Last(), Is.InstanceOf<WorkstreamCreateCommand>());
        Assert.That(kept.Id, Is.EqualTo(host.Sent.Last().CommandId), "the kept id follows the build's own step");
        flow.Open(null, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Build));
    }

    [Test]
    public async Task APressOnAFrameDrawnBeforeANewReplyOrListingNeverTakesAnotherItem()
    {
        var routes = new Routes();
        routes.Answers["GET /api/companion"] = () => HalcyonicJson.Serialize(new AvailableCompanion
        {
            Companion = new CompanionModel { Name = "local-model:tag", Served = "this_mac" }, MaxQuestions = 4,
        });
        var replies = new Queue<CompanionReply>(new CompanionReply[] { Companions.Ask(), Companions.Ask("Who keeps the page up to date?", "One organiser", "Each runner") });
        routes.Answers["POST /api/companion/replies"] = () => HalcyonicJson.Serialize(Companions.Response(replies.Dequeue()));
        var host = new Host { Api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes) };
        var flow = Flow(host);
        flow.Open(null, null);
        await Until(flow, () => flow.Frame!.Lines.Any(line => line.Action == NewProjectScreens.ChooseCompanion));
        Press(flow, NewProjectScreens.BeginCompanion, null);
        await Until(flow, () => flow.Idea!.Companion!.Latest is AskReply);
        flow.Act(NewProjectScreens.ChooseSuggestion, Suggestion(flow, "Each runner"));
        Assert.That(flow.Idea!.Companion!.Chosen, Is.EqualTo(CompanionAnswerRow.None), "a frame never drawn takes no press");

        Press(flow, NewProjectScreens.ChooseSuggestion, Suggestion(flow, "Each runner"));
        var earlier = Suggestion(flow, "One organiser");
        Press(flow, NewProjectScreens.SendAnswer, null);
        await Until(flow, () => flow.Idea.Companion.Latest is AskReply asked && asked.Question.Text == "Who keeps the page up to date?");
        // The person's press lands on the earlier frame, the one last drawn: the same words answer another question now.
        flow.Act(NewProjectScreens.ChooseSuggestion, earlier);
        Assert.That(flow.Idea.Companion.Chosen, Is.EqualTo(CompanionAnswerRow.None), "never the same words on the next question");
        Press(flow, NewProjectScreens.ChooseSuggestion, Suggestion(flow, "One organiser"));
        Assert.That(flow.Idea.Companion.Chosen, Is.EqualTo(CompanionAnswerRow.Suggestion), "a press on the question drawn now takes it");
    }

    [Test]
    public void AKeptAgentAppListsItsModelsOnlyWhenThePersonOpensHowItRuns()
    {
        var listing = Samples.MockRuntime();
        listing.RuntimeId = "local";
        listing.ModelChoice = ModelChoice.Listed;
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(1);
        snapshot.Runtimes = new List<RuntimeDescriptor> { listing };
        state.ApplySnapshot(snapshot, new StateChanges());
        var routes = new Routes();
        routes.Answers["GET /api/runtimes/local/models"] = () => HalcyonicJson.Serialize(new RuntimeModelsResponse
        {
            RuntimeId = "local", Result = new AvailableModels { Models = new List<RuntimeModel>() },
        });
        var store = new Memory();
        var idea = new ProjectIdea();
        idea.UseIdea("A page of race times");
        var draft = new NewWorkDraft(Commands);
        draft.ChooseRuntime(listing);
        store.Drafts.Add(CreationDraft.Of(Samples.JournalId, "", idea, null, draft, DateTimeOffset.UtcNow)!);

        var host = new Host { State = state, Api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes) };
        var flow = Flow(host, store: store);
        flow.Open(null, null);
        flow.Tick();
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap));
        Assert.That(routes.Asked.Any(asked => asked.EndsWith("/models", StringComparison.Ordinal)), Is.False, "opening reads no models: listing may start the agent app");
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
        Press(flow, NewProjectScreens.MoreOptions, null);
        Assert.That(routes.Asked.Count(asked => asked == "GET /api/runtimes/local/models"), Is.EqualTo(1), "on the person's press");
    }
}

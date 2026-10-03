using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class EntryScreensTests
{
    private static readonly CommandFactory Commands = new(Samples.Client);

    private static WorkOverview Overview(out ClientProjection state)
    {
        state = new Portfolio()
            .Project("a", "Alpha").Project("b", "Beta")
            .Work("w1", "a", WorkstreamStatus.WaitingForHuman)
            .Work("w2", "a", WorkstreamStatus.Running)
            .Work("w3", "b", WorkstreamStatus.Completed)
            .Apply();
        var visibility = new StageVisibility();
        visibility.UseJournal(Samples.JournalId);
        visibility.Hide("b", state.Projects.Keys);
        return WorkOverview.Of(state, visibility, id => id == "w2");
    }

    private static RuntimeDescriptor Listing(string id = "local", string name = "Local agent")
    {
        var runtime = Samples.MockRuntime();
        runtime.RuntimeId = id;
        runtime.DisplayName = name;
        runtime.Synthetic = false;
        runtime.ModelChoice = ModelChoice.Listed;
        runtime.UsesProjectLocation = true;
        return runtime;
    }

    private static RuntimeModel Model(string reference, ModelServed served) =>
        new() { ModelRef = reference, DisplayName = reference, Served = served, ToolCalling = ModelToolCalling.Declared };

    private static NewWorkDraft Draft(RuntimeDescriptor runtime, params RuntimeModel[] models)
    {
        var draft = new NewWorkDraft(Commands);
        draft.ChooseRuntime(runtime);
        if (models.Length > 0)
        {
            draft.SetModels(new RuntimeModelsResponse { RuntimeId = runtime.RuntimeId, Result = new AvailableModels { Models = models.ToList() } });
        }
        return draft;
    }

    private static LocationRoot Root() => new()
    {
        Path = "/Users/person/Projects", Name = "Projects", Status = LocationRootStatus.Available, FoldersTruncated = false,
        Folders = new List<LocationFolder> { new() { Name = "shop", Path = "/Users/person/Projects/shop" } },
    };

    private static ClientProjection WithRuntimes(params RuntimeDescriptor[] runtimes)
    {
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(1);
        snapshot.Runtimes = runtimes.ToList();
        state.ApplySnapshot(snapshot, new StateChanges());
        return state;
    }

    [Test]
    public void TheWelcomeOffersTwoChoicesAndNotNowInClosesPlace()
    {
        var welcome = EntryScreens.Welcome();
        Assert.That(welcome.CloseLabel, Is.EqualTo(EntryText.NotNow));
        Assert.That(welcome.Rows.Select(row => (row.Card, row.Action)), Is.EqualTo(new[] { (true, EntryScreens.Connect), (true, EntryScreens.Create) }));
        Assert.That(welcome.Actions.All, Is.Empty, "the choices are content, not actions");
    }

    [Test]
    public void ConnectProjectsShowsEachProjectAsAFilterWithItsWork()
    {
        var overview = Overview(out _);
        var live = EntryScreens.ConnectProjects(overview, connected: true, demonstration: false);
        Assert.That(live.Lead, Is.EqualTo(EntryText.ConnectLine));
        Assert.That(live.Rows.Select(row => (row.Title, row.Filter, row.Chosen, row.Key)), Is.EqualTo(new[] { ("Alpha", true, true, "a"), ("Beta", true, false, "b") }));
        Assert.That(live.Rows[0].DetailTone, Is.EqualTo(GlazeTone.Attention), "a project with work waiting for the person says so in its tone and its words");
        Assert.That(live.Rows[0].Detail, Does.Contain("waiting for you"));
        Assert.That(live.Rows[1].Detail, Does.StartWith("Hidden"), "never colour alone");
        Assert.That(live.Rows.All(row => row.Side?.Id == EntryScreens.AddTask), Is.True);
        Assert.That(live.Actions.Secondary.Select(action => action.Id), Is.EqualTo(new[] { EntryScreens.ShowAll }));
        Assert.That(live.Actions.Primary!.Id, Is.EqualTo(EntryScreens.Done));

        var away = EntryScreens.ConnectProjects(overview, connected: false, demonstration: false);
        Assert.That(away.Lead, Does.EndWith(EntryText.LastKnownProjects));
        Assert.That(away.Rows.All(row => row.Side == null), Is.True, "no new task while the Mac is away");
        var demo = EntryScreens.ConnectProjects(overview, connected: true, demonstration: true);
        Assert.That(demo.Lead, Is.EqualTo(EntryText.ExampleProjects));
        Assert.That(demo.Rows.All(row => row.Side == null), Is.True, "the demonstration starts nothing");

        var none = EntryScreens.ConnectProjects(WorkOverview.Of(new ClientProjection(), new StageVisibility(), _ => false), connected: true, demonstration: false);
        Assert.That(none.Rows.Select(row => row.Line ? row.Title : row.Action), Is.EqualTo(new[] { EntryText.NoProjects, EntryScreens.Create }));
        Assert.That(EntryScreens.ConnectProjects(null, connected: false, demonstration: false).Rows.Single().Title, Is.EqualTo(EntryText.WaitingForMac));
    }

    [Test]
    public void MoreTasksListsTheTasksWithoutACharacterWhatWaitsFirst()
    {
        var overview = Overview(out _);
        var more = EntryScreens.MoreTasks(overview, connected: true);
        Assert.That(more.Rows.Select(row => row.Key), Is.EqualTo(overview.OffStage.Select(off => off.Workstream.WorkstreamId)));
        Assert.That(more.Rows.First().DetailTone, Is.EqualTo(GlazeTone.Attention));
        Assert.That(more.Rows.All(row => row.Action == EntryScreens.OpenWork && row.TitleIsData), Is.True);
        Assert.That(EntryScreens.MoreTasks(null, connected: false).Rows.Single().Title, Is.EqualTo(EntryText.WaitingForMac));
    }

    [Test]
    public void CreateOffersTypingWithHoldToTalkBesideItAndTheFixedQuestions()
    {
        var spoken = EntryScreens.CreateStart(new ProjectIdea(), voice: true, said: VoiceText.Listening);
        Assert.That(spoken.Context, Is.EqualTo(EntryText.IdeaPrompt));
        Assert.That(spoken.Rows[0].Action, Is.EqualTo(EntryScreens.TypeIdea));
        Assert.That(spoken.Rows[0].Side, Is.Not.Null);
        Assert.That((spoken.Rows[0].Side!.Id, spoken.Rows[0].Side!.Holds), Is.EqualTo((EntryScreens.HoldToTalk, true)));
        Assert.That((spoken.Rows[1].Line, spoken.Rows[1].Title), Is.EqualTo((true, VoiceText.Listening)), "hold to talk's words, under the field");
        Assert.That(spoken.Rows[2].Action, Is.EqualTo(EntryScreens.HelpMe));

        var typed = EntryScreens.CreateStart(new ProjectIdea("p", "Storefront"), voice: false, said: null);
        Assert.That(typed.Title, Is.EqualTo("New task in Storefront"));
        Assert.That(typed.TitleIsData, Is.True);
        Assert.That(typed.Context, Is.EqualTo(EntryText.WorkPrompt));
        Assert.That(typed.Rows.Select(row => row.Action), Is.EqualTo(new[] { EntryScreens.TypeIdea, EntryScreens.HelpMe }));
        Assert.That(typed.Rows[0].Side, Is.Null, "no hold to talk where it isn't offered");
    }

    [Test]
    public void AFixedQuestionMarksTheAnswerGivenAndOffersBackTypingAndSkipping()
    {
        var idea = new ProjectIdea();
        idea.BeginGuide();
        idea.Answer("A website");
        idea.Back();
        var first = EntryScreens.Guide(idea);
        Assert.That(first.Context, Is.EqualTo("Question 1 of 4"));
        Assert.That(first.Lead, Does.Contain("not an AI"));
        Assert.That(first.Rows[0].Title, Is.EqualTo("What kind of thing is it?"));
        Assert.That(first.Rows.Skip(1).Select(row => (row.Title, row.Chosen)), Is.EqualTo(new[] { ("A website", true), ("An app", false), ("A tool or script", false) }));
        Assert.That(first.Rows[1].Detail, Is.EqualTo(EntryText.Chosen), "a choice says Chosen, never colour alone");
        Assert.That(first.Actions.All.Select(action => action.Id), Is.EqualTo(new[] { EntryScreens.Back, EntryScreens.TypeAnswer }));

        idea.Answer("A website");
        idea.Answer("My team");
        idea.Answer("Let visitors leave their email");
        var name = EntryScreens.Guide(idea);
        Assert.That(name.Rows.Single().Title, Is.EqualTo("What should it be called?"));
        Assert.That(name.Actions.All.Select(action => action.Label), Is.EqualTo(new[] { EntryText.Back, "Name it later", "Type a name" }));
    }

    [Test]
    public void TheRecapLetsEachFactChangeAndStartBuildingSaysWhatIsMissing()
    {
        var idea = new ProjectIdea();
        idea.UseIdea("A recipe tracker that suggests dinners.");
        var draft = Draft(Listing(), Model("ollama/qwen", ModelServed.ThisMac));
        var recap = EntryScreens.Recap(idea, draft, null, live: true, notice: null, problem: EntryText.ChooseWhereFilesLive);
        Assert.That(recap.Title, Is.EqualTo(EntryText.RecapTitle));
        Assert.That(recap.Lead, Is.EqualTo(EntryText.RecapLine));
        Assert.That(recap.Rows.Select(row => (row.Overline, row.Action, row.End)), Is.EqualTo(new[]
        {
            (EntryText.ProjectName, EntryScreens.Rename, EntryText.Change),
            (EntryText.FirstTask, EntryScreens.Rewrite, EntryText.Change),
            (EntryText.FolderTitle, EntryScreens.ChooseWhere, EntryText.ChooseFolder),
            (EntryText.HowItRuns, EntryScreens.MoreOptions, EntryText.MoreOptions),
        }));
        Assert.That(recap.Rows[3].Title, Is.EqualTo("On your computer"));
        Assert.That(recap.Rows[3].Detail, Is.EqualTo("Chosen for you. Change it in More options."));
        var start = recap.Actions.Primary!;
        Assert.That((start.Id, start.Available, start.Reason), Is.EqualTo((EntryScreens.StartBuilding, false, EntryText.ChooseWhereFilesLive)),
            "Start building stays in its place, unavailable, saying what is missing");
        Assert.That(recap.Actions.Destructive!.Id, Is.EqualTo(EntryScreens.StartOver));

        var heard = EntryScreens.Recap(idea, draft, null, live: true, notice: VoiceText.HeardNote, problem: null);
        Assert.That(heard.Lead, Is.EqualTo(VoiceText.HeardNote));
        Assert.That(heard.Actions.Primary!.Available, Is.True);

        var confirming = EntryScreens.Recap(idea, draft, null, live: true, notice: null, problem: null, confirmingStartOver: true);
        Assert.That(confirming.Confirm, Is.Not.Null);
        Assert.That((confirming.Confirm!.Yes.Id, confirming.Confirm.Yes.Role, confirming.Confirm.Cancel.Id),
            Is.EqualTo((EntryScreens.ConfirmStartOver, PanelActionRole.Destructive, EntryScreens.Cancel)), "Start over is confirmed in place");

        var moving = new ProjectIdea("p-store", "Storefront API");
        moving.UseIdea("Add a search page.");
        moving.ChooseFolder(ProjectFolder.New(Root(), "storefront-v2"));
        var current = new ProjectLocation { Path = "/Users/person/Projects/shop", Name = "shop", Created = false };
        var move = EntryScreens.Recap(moving, draft, current, live: true, notice: null, problem: null);
        Assert.That(move.Title, Is.EqualTo(EntryText.WorkRecapTitle));
        Assert.That(move.Lead, Is.EqualTo(EntryText.RebindWarning), "moving a project says what that means before the review");
        Assert.That((move.Rows[0].Action, move.Rows[0].End), Is.EqualTo(((string?)null, (string?)null)), "an existing project keeps its name");
        Assert.That(move.Rows[2].Title, Is.EqualTo("shop now, a new folder, storefront-v2, in Projects from now on"));
    }

    [Test]
    public void StartBuildingChecksEachThingInTurn()
    {
        var runtime = Listing();
        var state = WithRuntimes(runtime);
        var idea = new ProjectIdea();
        idea.UseIdea("A recipe tracker.");
        var empty = new NewWorkDraft(Commands);
        string? Problem(bool demo, ClientProjection? now, bool connected, ProjectIdea? which, NewWorkDraft draft, ProjectLocation? folder = null) =>
            EntryScreens.StartProblem(demo, now, connected, which, draft, folder);

        Assert.That(Problem(true, state, true, idea, empty), Is.EqualTo(EntryText.DemoCannotStart));
        Assert.That(Problem(false, state, false, idea, empty), Is.EqualTo(EntryText.WaitingForMac));
        Assert.That(Problem(false, state, true, new ProjectIdea(), empty), Is.EqualTo("Name the project in at most 200 characters."));
        Assert.That(Problem(false, state, true, idea, empty), Is.EqualTo(EntryText.ChooseHowItRuns));
        Assert.That(Problem(false, WithRuntimes(Listing("other")), true, idea, Draft(runtime)), Is.EqualTo(EntryText.ChooseAgain));
        Assert.That(Problem(false, state, true, idea, Draft(runtime)), Is.EqualTo(EntryText.FinishChoosing));
        var ready = Draft(runtime, Model("ollama/qwen", ModelServed.ThisMac));
        Assert.That(Problem(false, state, true, new ProjectIdea("gone", "Gone"), ready), Is.Not.Null.And.Not.EqualTo(EntryText.ProjectGone),
            "an existing project's idea needs its first task first");
        var gone = new ProjectIdea("gone", "Gone");
        gone.UseIdea("Add a page.");
        Assert.That(Problem(false, state, true, gone, ready), Is.EqualTo(EntryText.ProjectGone));
        Assert.That(Problem(false, state, true, idea, ready), Is.EqualTo(EntryText.ChooseWhereFilesLive));
        idea.ChooseFolder(ProjectFolder.New(Root(), "recipes"));
        Assert.That(Problem(false, state, true, idea, ready), Is.Null);
    }

    [Test]
    public void HowItRunsListsTheMacsModelsThenALineThenTheRest()
    {
        var local = Model("ollama/qwen", ModelServed.ThisMac);
        var remote = Model("hosted/x", ModelServed.Remote);
        var draft = Draft(Listing(), remote, local);
        var models = EntryScreens.Options(draft, new[] { Listing() }, showModels: true, live: true);
        Assert.That(models.Rows.Select(row => row.Line ? "line" : row.Key), Is.EqualTo(new[] { "ollama/qwen", "line", "hosted/x" }));
        Assert.That(models.Rows[1].Title, Is.EqualTo("Runs on a remote service: your code and instructions go there."));
        Assert.That(models.Rows[0].Detail, Does.StartWith(EntryText.ChosenForYou + " · Runs on your computer"));
        Assert.That(models.Rows[0].Chosen, Is.True);
        Assert.That(draft.ChooseModel(remote), Is.False);
        var pending = EntryScreens.Options(draft, new[] { Listing() }, showModels: true, live: true);
        Assert.That(pending.Rows[2].Detail, Is.EqualTo(EntryText.ConfirmElsewhere(remote)), "the first press says where it runs and chooses nothing");
        Assert.That(pending.Rows[0].Chosen, Is.True);
        Assert.That(models.Actions.All.Select(action => action.Id), Is.EqualTo(new[] { EntryScreens.ChangeRuntime, EntryScreens.Done }));

        var mock = Samples.MockRuntime();
        var runtimes = EntryScreens.Options(new NewWorkDraft(Commands), new[] { mock, Listing() }, showModels: false, live: true);
        Assert.That(runtimes.Rows.Select(row => (row.Title, row.Detail)), Is.EqualTo(new[]
        {
            ("Local agent", EntryText.ListsModels), (EntryText.PracticeRun, EntryText.PracticeDetail),
        }), "real agent apps first, the practice run named for what it does");
        Assert.That(runtimes.Actions.All.Select(action => action.Id), Is.EqualTo(new[] { EntryScreens.Done }));
    }

    [Test]
    public void WhereItsFilesLiveMarksTheChoiceAndOffersNothingFromAMissingPlace()
    {
        var listing = new LocationsResponse
        {
            Roots = new List<LocationRoot>
            {
                Root(),
                new() { Path = "/Volumes/Old", Name = "Old", Status = LocationRootStatus.Missing, Folders = new List<LocationFolder>(), FoldersTruncated = false },
            },
        };
        var idea = new ProjectIdea();
        idea.ChooseFolder(ProjectFolder.Existing(listing.Roots[0], listing.Roots[0].Folders[0]));
        var folders = EntryScreens.Folder(idea, listing, problem: null, notice: null);
        Assert.That(folders.Rows.Select(row => (row.Title, row.Chosen, row.Available)), Is.EqualTo(new[]
        {
            ("New folder in Projects", false, true), ("Directly in Projects", false, true), ("shop", true, true), ("Old", false, false),
        }));
        Assert.That(folders.Rows[2].Detail, Is.EqualTo(EntryText.Chosen + " · In Projects"));
        Assert.That(folders.Actions.All.Select(action => action.Id), Is.EqualTo(new[] { EntryScreens.Back }));

        var refused = EntryScreens.Folder(idea, listing, problem: null, notice: EntryText.NewFolderRule);
        Assert.That((refused.Lead, refused.LeadTone), Is.EqualTo((EntryText.NewFolderRule, (GlazeTone?)GlazeTone.Failure)));
        var unread = EntryScreens.Folder(idea, null, problem: "timeout", notice: null);
        Assert.That(unread.Rows.Single().Title, Does.Contain("timeout"));
        Assert.That(unread.Actions.Primary!.Id, Is.EqualTo(EntryScreens.ReadFolders));
        Assert.That(EntryScreens.Folder(idea, null, problem: null, notice: null).Actions.Primary, Is.Null, "nothing to try again while reading");
    }

    [Test]
    public void TheWholeRequestIsGivenNamesAsTheyAreAndShowsAMoveBothWays()
    {
        var moving = new ProjectIdea("p-store", "Storefront‮ API");
        moving.UseIdea("Add a search page.");
        moving.ChooseFolder(ProjectFolder.New(Root(), "storefront-v2"));
        var draft = Draft(Listing("local", "Local​ agent"), Model("ollama/qwen", ModelServed.ThisMac));
        var current = new ProjectLocation { Path = "/Users/person/Projects/shop", Name = "shop", Created = false };
        var review = EntryScreens.ReviewOf(moving, draft, current, live: true);
        Assert.That(review.Items.Select(item => item.Text), Is.EqualTo(new[]
        {
            "Project: Storefront\\u{202E} API",
            "Folder now: shop",
            "Folder from now on: a new folder, storefront-v2, in Projects",
            "Task name: Add a search page.",
            "Agent app: Local\\u{200B} agent",
            "Model: ollama/qwen",
            "Where the model runs: on your computer; can use tools",
            "Model id: ollama/qwen",
            "First task: Add a search page.",
        }), "every name spelled once, by the review");
    }

    [Test]
    public void YesStartBuildingIsLockedUntilTheLastPartAndNeverStandsWhereStartBuildingDid()
    {
        var review = new NewWorkReview("Project", "Title", "Agent", "Model", "on your computer", "ref", "Objective");
        review.Paginate(review.Items.Select(_ => 1).ToList(), 3);
        var first = EntryScreens.Review(review, problem: null);
        Assert.That(first.Parts, Is.EqualTo((0, 3)));
        Assert.That(first.Actions.All, Is.Empty, "the confirm step takes the bar's place");
        var yes = first.Confirm!.Yes;
        Assert.That((yes.Label, yes.Available), Is.EqualTo((EntryText.ReadToPart(3), false)), "locked in its place, saying what is left");
        Assert.That(first.Confirm.Cancel.Label, Is.EqualTo(EntryText.Change), "Change stands at the right end, where Start building stood");

        Samples.ReadThrough(review);
        var last = EntryScreens.Review(review, problem: null);
        Assert.That((last.Confirm!.Yes.Label, last.Confirm.Yes.Available), Is.EqualTo((EntryText.ConfirmStart, true)));
        var gone = EntryScreens.Review(review, problem: EntryText.WaitingForMac);
        Assert.That((gone.Confirm!.Yes.Available, gone.Confirm.Yes.Reason), Is.EqualTo((false, EntryText.WaitingForMac)));
    }

    [Test]
    public void StartingYourWorkOffersTheNextActionItsOutcomeAllows()
    {
        var draft = Draft(Listing(), Model("ollama/qwen", ModelServed.ThisMac));
        draft.Objective = "Add a page.";
        var folder = ProjectFolder.New(Root(), "recipes")!;
        var sequence = new BuildSequence(draft, Commands, "Recipes", folder.ToContract());
        var create = sequence.Begin();
        var waiting = EntryScreens.Sending(sequence, folder);
        Assert.That(waiting.Rows.Select(row => row.DetailTone), Is.EqualTo(new GlazeTone?[] { GlazeTone.Active, null, null }));
        Assert.That(waiting.Actions.All, Is.Empty, "nothing to press while the Mac answers");

        sequence.Advance(With(new CommandView
        {
            CommandId = create.CommandId, Status = CommandStatus.Rejected, IssuedAt = Samples.Time, UpdatedAt = Samples.Time,
            Rejection = new CommandRejection { Code = RejectionCode.LocationExists, Message = "There is already a folder named recipes." },
        }));
        var exists = EntryScreens.Sending(sequence, folder);
        Assert.That(exists.Rows[0].DetailTone, Is.EqualTo(GlazeTone.Failure));
        Assert.That(exists.Actions.All.Select(action => action.Id), Is.EqualTo(new[] { EntryScreens.ChooseAnotherFolder, EntryScreens.UseThatFolder }));
        Assert.That(EntryScreens.Sending(sequence, null).Actions.Primary!.Id, Is.EqualTo(EntryScreens.TryAgain));

        var unknown = new BuildSequence(draft, Commands, "Recipes");
        var made = unknown.Begin();
        unknown.AcknowledgementLost(new CommandOutcomeUnknownException(made.CommandId, "The socket closed."));
        unknown.Advance(new ClientProjection());
        var check = EntryScreens.Sending(unknown, null);
        Assert.That(check.Rows[0].DetailTone, Is.EqualTo(GlazeTone.Unknown));
        Assert.That(check.Actions.All.Select(action => action.Id), Is.EqualTo(new[] { EntryScreens.CheckFirst }), "only checking the work first");
        Assert.That(check.Actions.Primary!.Icon, Is.EqualTo(GlazeIcon.Next), "it says Next, so it shows Next's icon");
    }

    [Test]
    public void ClearingAStartThatMayHaveRunTakesTwoPressesInTwoPlaces()
    {
        var waiting = EntryScreens.Previous("c-1", null, armed: false, live: true);
        Assert.That(waiting.Rows.Select(row => row.Title), Is.EqualTo(new[] { EntryText.Recorded(null), EntryText.ClearOnceChecked, "Reference: c-1" }));
        Assert.That(waiting.Actions.Primary!.Id, Is.EqualTo(EntryScreens.Clear));
        var armed = EntryScreens.Previous("c-1", new CommandView { CommandId = "c-1", Status = CommandStatus.Failed }, armed: true, live: true);
        Assert.That(armed.Rows[0].Title, Does.Contain("may have changed something"), "a failure may still have changed something");
        Assert.That((armed.Confirm!.Yes.Id, armed.Confirm.Cancel.Id), Is.EqualTo((EntryScreens.ConfirmClear, EntryScreens.Cancel)),
            "the second press is Yes, clear, left of Cancel, which stands where Clear did");
        Assert.That(EntryScreens.Previous("c-1", null, armed: false, live: false).Actions.All, Is.Empty, "nothing to clear while not connected");
    }

    [Test]
    public void WorkThatComesToWaitIsOfferedNeverOpenedByItself()
    {
        var work = Samples.Workstream("w1", WorkstreamStatus.WaitingForHuman, level: AttentionLevel.ActionRequired);
        work.Title = "Import‮ recipes";
        var banner = EntryScreens.WaitingBanner(work);
        Assert.That(banner.Tone, Is.EqualTo(GlazeTone.Attention));
        Assert.That(banner.Text, Is.EqualTo("“Import‹U+202E› recipes” is waiting for you."));
        Assert.That(banner.Actions.Select(action => (action.Id, action.Role)), Is.EqualTo(new[]
        {
            (EntryScreens.OpenNow, PanelActionRole.Attention), (EntryScreens.KeepCreating, PanelActionRole.Secondary),
        }));
    }

    [Test]
    public void EveryScreensOwnWordsArePlainAndNameNoRuntime()
    {
        var overview = Overview(out _);
        var idea = new ProjectIdea();
        idea.UseIdea("A recipe tracker.");
        idea.BeginGuide();
        var draft = Draft(Listing(), Model("ollama/qwen", ModelServed.ThisMac), Model("hosted/x", ModelServed.Remote));
        var review = new NewWorkReview("Project", "Title", "Agent", "Model", "on your computer", "ref", "Objective");
        review.Paginate(review.Items.Select(_ => 1).ToList(), 20);
        var screens = new[]
        {
            EntryScreens.Welcome(), EntryScreens.ConnectProjects(overview, true, false), EntryScreens.MoreTasks(overview, true),
            EntryScreens.CreateStart(idea, true, null), EntryScreens.Guide(idea),
            EntryScreens.Recap(idea, draft, null, true, null, EntryText.ChooseWhereFilesLive),
            EntryScreens.Recap(idea, draft, null, true, null, null, confirmingStartOver: true),
            EntryScreens.Options(draft, new[] { Listing() }, true, true), EntryScreens.Options(draft, new[] { Listing() }, false, true),
            EntryScreens.Review(review, null), EntryScreens.Previous("c-1", null, true, true),
        };
        foreach (var screen in screens)
        {
            var words = new List<string> { screen.Title, screen.CloseLabel };
            if (screen.Context != null) words.Add(screen.Context);
            if (screen.Lead != null) words.Add(screen.Lead);
            words.AddRange(screen.Actions.All.Select(action => action.Label));
            if (screen.Confirm != null) words.AddRange(new[] { screen.Confirm.Yes.Label, screen.Confirm.Cancel.Label });
            words.AddRange(screen.Rows.Where(row => !row.TitleIsData).Select(row => row.Title));
            words.AddRange(screen.Rows.SelectMany(row => new[] { row.Overline, row.End, row.Side?.Label }).OfType<string>());
            foreach (var word in words)
            {
                Assert.That(word, Does.Not.Contain("\u2014"), "no em dash");
                Assert.That(Regex.IsMatch(word, @"\b(runtime|workstream|control plane)\b", RegexOptions.IgnoreCase), Is.False, screen.Title + ": " + word);
            }
        }
    }

    [Test]
    public void EachActionShowsItsIconBesideItsWordsAndOnlyHoldToTalkTheMicrophone()
    {
        var overview = Overview(out _);
        var idea = new ProjectIdea();
        idea.UseIdea("A recipe tracker.");
        var guided = new ProjectIdea();
        guided.UseIdea("A recipe tracker.");
        guided.BeginGuide();
        var draft = Draft(Listing(), Model("ollama/qwen", ModelServed.ThisMac));
        var review = new NewWorkReview("Project", "Title", "Agent", "Model", "on your computer", "ref", "Objective");
        review.Paginate(review.Items.Select(_ => 1).ToList(), 3);
        var locked = EntryScreens.Review(review, problem: null);
        Samples.ReadThrough(review);
        var unlocked = EntryScreens.Review(review, problem: null);
        var recap = EntryScreens.Recap(idea, draft, null, live: true, notice: null, problem: null);
        var startingOver = EntryScreens.Recap(idea, draft, null, live: true, notice: null, problem: null, confirmingStartOver: true);
        var connect = EntryScreens.ConnectProjects(overview, connected: true, demonstration: false);
        var create = EntryScreens.CreateStart(idea, voice: true, said: null);
        var guide = EntryScreens.Guide(guided);
        var previous = EntryScreens.Previous("c-1", null, armed: false, live: true);
        var options = EntryScreens.Options(draft, new[] { Listing() }, showModels: true, live: true);

        Assert.That((EntryScreens.Welcome().CloseLabel, EntryScreens.Welcome().CloseIcon), Is.EqualTo((EntryText.NotNow, GlazeIcon.NotNow)));
        Assert.That(recap.CloseIcon, Is.EqualTo(GlazeIcon.Close));
        Assert.That((PanelModel.MoveIcon, PanelModel.ResetPositionIcon), Is.EqualTo((GlazeIcon.Move, GlazeIcon.ResetPosition)));
        Assert.That(recap.Actions.All.Select(action => (action.Label, action.Icon)), Is.EqualTo(new (string, GlazeIcon?)[]
        {
            (EntryText.StartOver, GlazeIcon.StartOver), (EntryText.StartBuilding, GlazeIcon.StartBuilding),
        }));
        Assert.That((startingOver.Confirm!.Yes.Icon, startingOver.Confirm.Cancel.Icon), Is.EqualTo(((GlazeIcon?)GlazeIcon.StartOver, (GlazeIcon?)GlazeIcon.Close)));
        Assert.That((locked.Confirm!.Yes.Icon, unlocked.Confirm!.Yes.Icon, unlocked.Confirm.Cancel.Icon),
            Is.EqualTo(((GlazeIcon?)GlazeIcon.Locked, (GlazeIcon?)GlazeIcon.StartBuilding, (GlazeIcon?)GlazeIcon.Change)), "locked while unread, then Start building's");
        Assert.That(guide.Actions.All.Select(action => (action.Label, action.Icon)), Does.Contain((EntryText.Back, (GlazeIcon?)GlazeIcon.Back)));
        Assert.That(guide.Actions.All.Last().Icon, Is.EqualTo(GlazeIcon.Type));
        Assert.That(connect.Actions.All.Select(action => (action.Label, action.Icon)), Is.EqualTo(new (string, GlazeIcon?)[]
        {
            (EntryText.ShowAll, GlazeIcon.ShowAll), (EntryText.Done, null),
        }), "Done has no icon in the set, so its word stands alone");
        Assert.That(connect.Rows.Select(row => row.Side?.Icon).OfType<GlazeIcon>().Distinct(), Is.EqualTo(new[] { GlazeIcon.AddTask }));
        Assert.That((previous.Actions.Primary!.Label, previous.Actions.Primary.Icon), Is.EqualTo((EntryText.Clear, (GlazeIcon?)null)), "Clear has no icon in the set");
        Assert.That(EntryScreens.WaitingBanner(Samples.Workstream("w1", WorkstreamStatus.WaitingForHuman)).Actions.Select(action => action.Icon),
            Is.EqualTo(new GlazeIcon?[] { GlazeIcon.OpenNow, GlazeIcon.KeepCreating }));

        // The microphone only on hold to talk, which is always held, and on no confirmation.
        var actions = new[] { recap, startingOver, locked, unlocked, connect, create, guide, previous, options }.SelectMany(screen =>
            screen.Actions.All
                .Concat(screen.Confirm == null ? Enumerable.Empty<PanelAction>() : new[] { screen.Confirm.Yes, screen.Confirm.Cancel })
                .Concat(screen.Rows.Select(row => row.Side).OfType<PanelAction>())).ToList();
        Assert.That(actions.Where(action => action.Holds).Select(action => action.Icon), Is.EqualTo(new GlazeIcon?[] { GlazeIcon.HoldToTalk }));
        foreach (var action in actions)
        {
            Assert.That(action.Icon == GlazeIcon.HoldToTalk, Is.EqualTo(action.Holds), action.Label);
            Assert.That(action.Label, Is.Not.Empty, "an icon never stands in for the words");
        }
    }

    private static ClientProjection With(CommandView command)
    {
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(1);
        snapshot.Commands = new List<CommandView> { command };
        state.ApplySnapshot(snapshot, new StateChanges());
        return state;
    }
}

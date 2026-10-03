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
    public void TheWelcomeOffersConnectProjectsAndNotNowInClosesPlace()
    {
        var welcome = EntryScreens.Welcome();
        Assert.That(welcome.CloseLabel, Is.EqualTo(EntryText.NotNow));
        Assert.That(welcome.Rows.Select(row => (row.Card, row.Action)), Is.EqualTo(new[] { (true, EntryScreens.Connect) }), "New project is the menu's");
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
        Assert.That(live.Rows.All(row => row.Side == null), Is.True, "a task is added from the menu's Projects");
        Assert.That(live.Actions.Secondary.Select(action => action.Id), Is.EqualTo(new[] { EntryScreens.ShowAll }));
        Assert.That(live.Actions.Primary!.Id, Is.EqualTo(EntryScreens.Done));

        var away = EntryScreens.ConnectProjects(overview, connected: false, demonstration: false);
        Assert.That(away.Lead, Does.EndWith(EntryText.LastKnownProjects));
        var demo = EntryScreens.ConnectProjects(overview, connected: true, demonstration: true);
        Assert.That(demo.Lead, Is.EqualTo(EntryText.ExampleProjects));

        var none = EntryScreens.ConnectProjects(WorkOverview.Of(new ClientProjection(), new StageVisibility(), _ => false), connected: true, demonstration: false);
        Assert.That(none.Rows.Select(row => row.Line ? row.Title : row.Action), Is.EqualTo(new[] { EntryText.NoProjects }));
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
    public void EveryScreensOwnWordsArePlainAndNameNoRuntime()
    {
        var overview = Overview(out _);
        var screens = new[]
        {
            EntryScreens.Welcome(), EntryScreens.ConnectProjects(overview, true, false), EntryScreens.ConnectProjects(overview, false, false),
            EntryScreens.ConnectProjects(overview, true, true), EntryScreens.MoreTasks(overview, true),
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
    public void EachActionShowsItsIconBesideItsWordsAndNothingIsHeld()
    {
        var overview = Overview(out _);
        var connect = EntryScreens.ConnectProjects(overview, connected: true, demonstration: false);
        Assert.That((EntryScreens.Welcome().CloseLabel, EntryScreens.Welcome().CloseIcon), Is.EqualTo((EntryText.NotNow, GlazeIcon.NotNow)));
        Assert.That(connect.CloseIcon, Is.EqualTo(GlazeIcon.Close));
        Assert.That((PanelModel.MoveIcon, PanelModel.ResetPositionIcon), Is.EqualTo((GlazeIcon.Move, GlazeIcon.ResetPosition)));
        Assert.That(connect.Actions.All.Select(action => (action.Label, action.Icon)), Is.EqualTo(new (string, GlazeIcon?)[]
        {
            (EntryText.ShowAll, GlazeIcon.ShowAll), (EntryText.Done, null),
        }), "Done has no icon in the set, so its word stands alone");

        var actions = new[] { EntryScreens.Welcome(), connect, EntryScreens.MoreTasks(overview, connected: true) }
            .SelectMany(screen => screen.Actions.All.Concat(screen.Rows.Select(row => row.Side).OfType<PanelAction>())).ToList();
        foreach (var action in actions)
        {
            Assert.That((action.Holds, action.Icon == GlazeIcon.HoldToTalk), Is.EqualTo((false, false)), action.Label);
            Assert.That(action.Label, Is.Not.Empty, "an icon never stands in for the words");
        }
    }
}

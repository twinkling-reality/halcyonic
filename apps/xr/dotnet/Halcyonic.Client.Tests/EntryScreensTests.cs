using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class EntryScreensTests
{
    private static readonly CommandFactory Commands = new(Samples.Client);

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
        Assert.That(Problem(false, state, true, new ProjectIdea(), empty), Is.EqualTo("Name · Up to 200 Characters"));
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
            "Project · Storefront\\u{202E} API",
            "Folder Now · shop",
            "Folder Next · New Folder · storefront-v2 in Projects",
            "Task Name · Add a search page.",
            "Agent App · Local\\u{200B} agent",
            // The level the start will use, after the model (a real runtime; this draft asks).
            "Model · ollama/qwen · Ask First",
            "Model Location · On Your Computer · Uses Tools",
            "Model ID · ollama/qwen",
            "First Task · Add a search page.",
        }), "every name spelled once, by the review");
    }

    [Test]
    public void ReviewItemsCarryTheirKindAndValueApart()
    {
        // A17: a card lays the project as its title, the task as its line, the rest as slots, from each item's kind.
        var review = new NewWorkReview("Recipes", "Recipes", "Codex", "a model", "on your computer", "ollama/m", "Make a page of recipes.");
        Assert.That(review.Items.Take(7).Select(item => item.Kind), Is.EqualTo(new[]
        {
            ReviewItemKind.Project, ReviewItemKind.TaskName, ReviewItemKind.AgentApp, ReviewItemKind.Model,
            ReviewItemKind.ModelLocation, ReviewItemKind.ModelId, ReviewItemKind.FirstTask,
        }));
        var project = review.Items.First();
        Assert.That((project.ValueRuns.Text, project.ValueRuns.AnyTheirs), Is.EqualTo(("Recipes", true)), "the value alone, leaning, no label");
        var moved = new NewWorkReview("Recipes", "Recipes", "Codex", "a model", "on your computer", "ollama/m", "Make a page.", folder: "next", folderBefore: "before");
        Assert.That(moved.Items.Where(item => item.Kind is ReviewItemKind.FolderNow or ReviewItemKind.Folder).Select(item => (item.Kind, item.ValueRuns.Text)),
            Is.EqualTo(new[] { (ReviewItemKind.FolderNow, "before"), (ReviewItemKind.Folder, "next") }));
    }

    [Test]
    public void TheFolderFactGivesItsNameAndWhetherItIsNewApart()
    {
        var made = ProjectFolder.Restore("/Users/someone/Projects", "Projects", "kite-run", isNew: true);
        var parts = EntryText.FolderFactPartsOf(null, made, needed: true);
        Assert.That((parts.Kind, parts.IsNew, parts.Name?.Text, parts.Place?.Text, parts.Now), Is.EqualTo((FolderFactKind.NewFolder, true, "kite-run", "Projects", (LineWords?)null)));
        var starter = EntryText.FolderFactPartsOf(null, ProjectFolder.Restore("/p", "Projects", "vr-run", isNew: true, starterId: "starter"), needed: true);
        Assert.That((starter.Kind, starter.IsNew), Is.EqualTo((FolderFactKind.NewStarter, true)));
        var existing = EntryText.FolderFactPartsOf(new ProjectLocation { Path = "/p/old", Name = "old", Created = false },
            ProjectFolder.Restore("/p", "Projects", "shop", isNew: false), needed: true);
        Assert.That((existing.Kind, existing.IsNew, existing.Name?.Text, existing.Now?.Text), Is.EqualTo((FolderFactKind.Existing, false, "shop", "old")));
        Assert.That(EntryText.FolderFactPartsOf(new ProjectLocation { Path = "/p/old", Name = "old", Created = false }, null, needed: true).Kind, Is.EqualTo(FolderFactKind.ProjectFolder));
        Assert.That(EntryText.FolderFactPartsOf(null, null, needed: true).Kind, Is.EqualTo(FolderFactKind.NotChosen));
        Assert.That(EntryText.FolderFactPartsOf(null, null, needed: false).Kind, Is.EqualTo(FolderFactKind.NotNeeded));
    }
}

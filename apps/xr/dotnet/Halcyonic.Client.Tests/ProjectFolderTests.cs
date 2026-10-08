using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class ProjectFolderTests
{
    private static LocationRoot Root(string name = "Projects", LocationRootStatus status = LocationRootStatus.Available, params string[] folders) => new()
    {
        Path = "/Users/person/" + name,
        Name = name,
        Status = status,
        Folders = folders.Select(folder => new LocationFolder { Name = folder, Path = "/Users/person/" + name + "/" + folder }).ToList(),
        FoldersTruncated = false,
    };

    [Test]
    public void ASuggestedNameAlwaysKeepsTheHostsRule()
    {
        var names = new[]
        {
            "Recipe tracker", "Café crème", "🙂🙂", "", "   ", "...leading dots", ".hidden", "-dash", "Ünïcödé", "a/b\\c", "日本語のアプリ",
            new string('x', 200), "x " + new string('y', 70), "Tab\tand\nline", "‮right to left", "UPPER case 42",
        };
        foreach (var name in names)
        {
            var suggested = ProjectFolder.SuggestName(name);
            Assert.That(ProjectFolder.IsValidNewName(suggested), Is.True, name + " gave " + suggested);
            Assert.That(suggested, Does.Not.StartWith("."), "never a hidden folder");
            Assert.That(suggested.Length, Is.InRange(1, 64));
        }
        Assert.That(ProjectFolder.SuggestName("Recipe tracker"), Is.EqualTo("recipe-tracker"));
        Assert.That(ProjectFolder.SuggestName("Café crème"), Is.EqualTo("caf-cr-me"));
        Assert.That(ProjectFolder.SuggestName("🙂🙂"), Is.EqualTo("project"), "a plain fallback when nothing fits");
        Assert.That(ProjectFolder.SuggestName("...leading dots"), Is.EqualTo("leading-dots"));
    }

    [Test]
    public void ANewNameFollowsTheHostsRule()
    {
        foreach (var good in new[] { "greeting-card", "a", "A1.b_c-d", new string('z', 64) }) Assert.That(ProjectFolder.IsValidNewName(good), Is.True, good);
        foreach (var bad in new[] { "", ".git", "-x", "_x", "a b", "a/b", "é", new string('z', 65), "..", "a​b" })
        {
            Assert.That(ProjectFolder.IsValidNewName(bad), Is.False, bad);
            Assert.That(ProjectFolder.New(Root(), bad), Is.Null);
        }
        Assert.That(ProjectFolder.New(Root(), "  greeting-card ")!.FolderName, Is.EqualTo("greeting-card"));
    }

    [Test]
    public void AChoiceSendsBackExactlyWhatTheHostListed()
    {
        var root = Root("Projects", LocationRootStatus.Available, "Storefront API");
        var made = (NewFolderChoice)ProjectFolder.New(root, "greeting-card")!.ToContract();
        Assert.That((made.Root, made.FolderName), Is.EqualTo((root.Path, "greeting-card")));
        var existing = (ExistingFolderChoice)ProjectFolder.Existing(root, root.Folders[0]).ToContract();
        Assert.That((existing.Root, existing.FolderName), Is.EqualTo((root.Path, "Storefront API")), "an existing folder's name is sent as listed");
        var itself = (ExistingFolderChoice)ProjectFolder.Existing(root, null).ToContract();
        Assert.That(itself.FolderName, Is.Null, "the root itself");
        var taken = ProjectFolder.New(root, "recipes")!;
        var reuse = (ExistingFolderChoice)ProjectFolder.Existing(taken).ToContract();
        Assert.That((reuse.Root, reuse.FolderName), Is.EqualTo((root.Path, "recipes")), "Use that folder names the same one");
    }

    [Test]
    public void NamesFromTheFileSystemShowByTheOneRule()
    {
        var root = Root("Pro‮jects", LocationRootStatus.Available, "shop<b>x</b>​");
        Assert.That(ProjectFolder.Existing(root, root.Folders[0]).Describe(), Is.EqualTo("shop<b>x</b>‹U+200B› in Pro‹U+202E›jects"));
        Assert.That(ProjectFolder.Existing(root, null).Describe(), Is.EqualTo("directly in Pro‹U+202E›jects"));
        Assert.That(ProjectFolder.New(Root(), "cards")!.Describe(), Is.EqualTo("a new folder, cards, in Projects"));
        var options = ProjectFolder.Options(new LocationsResponse { Roots = new List<LocationRoot> { root } });
        Assert.That(options.Select(option => option.Label), Has.Some.EqualTo("shop<b>x</b>‹U+200B›"));
    }

    /// <summary>
    /// A name of only white space, which the contracts allow for a folder, a place and an agent app, shows each of
    /// its characters by code point, so New project's lists are drawn and the name is something to see (the
    /// outside-text audit's gap 1).
    /// </summary>
    [Test]
    public void ANameOfOnlyWhiteSpaceShowsByItsCodePointsAndNeverStopsAListBeingDrawn()
    {
        var root = Root(" ", LocationRootStatus.Available, "  ");
        var listing = new LocationsResponse { Roots = new List<LocationRoot> { root } };
        Assert.That(ProjectFolder.Options(listing).Select(option => option.Label),
            Is.EqualTo(new[] { "New folder in \u2039U+0020\u203A", "Directly in \u2039U+0020\u203A", "\u2039U+0020\u203A\u2039U+0020\u203A" }));
        Assert.That(ProjectFolder.Existing(root, root.Folders[0]).Describe(), Is.EqualTo("\u2039U+0020\u203A\u2039U+0020\u203A in \u2039U+0020\u203A"));
        var idea = new ProjectIdea();
        idea.UseIdea("something for my running club");
        Assert.That(() => NewProjectScreens.RecapFolder(idea, startReached: false, listing, problem: null, notice: null), Throws.Nothing);
        Assert.That(EntryText.RuntimeName(new RuntimeDescriptor { RuntimeId = "blank", DisplayName = " " }), Is.EqualTo("\u2039U+0020\u203A"));
    }

    [Test]
    public void TheListingOffersANewFolderTheRootAndItsFolders()
    {
        var listing = new LocationsResponse
        {
            Roots = new List<LocationRoot> { Root("Projects", LocationRootStatus.Available, "recipes", "shop"), Root("Old", LocationRootStatus.Missing) },
        };
        var options = ProjectFolder.Options(listing);
        Assert.That(options.Select(option => option.Kind), Is.EqualTo(new[]
        {
            FolderOptionKind.NewFolder, FolderOptionKind.Root, FolderOptionKind.Folder, FolderOptionKind.Folder, FolderOptionKind.MissingRoot,
        }));
        Assert.That(options.Last().Choosable, Is.False, "a root not on the Mac offers nothing");
        Assert.That(ProjectFolder.Options(new LocationsResponse()), Is.Empty);
    }

    [Test]
    public void TheRecapSaysWhereTheFilesLive()
    {
        var chosen = ProjectFolder.New(Root(), "recipes")!;
        var current = new ProjectLocation { Path = "/Users/person/Projects/old", Name = "old", Created = false };
        Assert.That(EntryText.FolderFact(null, null, needed: true), Is.EqualTo("Not chosen yet"));
        Assert.That(EntryText.FolderFact(null, null, needed: false), Is.EqualTo("Not needed"));
        Assert.That(EntryText.FolderFact(null, chosen, needed: true), Is.EqualTo("A new folder, recipes, in Projects"));
        Assert.That(EntryText.FolderFact(current, null, needed: true), Is.EqualTo("old, the project's folder"));
        Assert.That(EntryText.FolderFact(current, chosen, needed: true), Is.EqualTo("old now, a new folder, recipes, in Projects from now on"));
        var shop = Root("Projects", LocationRootStatus.Available, "shop");
        Assert.That(EntryText.FolderFact(null, ProjectFolder.Existing(shop, shop.Folders[0]), needed: true), Is.EqualTo("shop in Projects"),
            "a name from the file system starts the line as it is");
    }

    [Test]
    public void TheReviewShowsTheFolderAndAMoveInFull()
    {
        var review = new NewWorkReview("Project", "Title", "Runtime", "Model", "on your computer", "ref", "Objective", folder: "recipes in Projects");
        Assert.That(review.Items.Select(item => item.Text), Has.Some.EqualTo("Where its files live: recipes in Projects"));
        var move = new NewWorkReview("Project", "Title", "Runtime", "Model", "on your computer", "ref", "Objective", folder: "shop in Projects", folderBefore: "none");
        Assert.That(move.Items.Select(item => item.Text).Take(3), Is.EqualTo(new[] { "Project: Project", "Folder now: none", "Folder from now on: shop in Projects" }));
    }
}

public class FolderRefusalTests
{
    private static readonly CommandFactory Commands = new(new ClientInfo { Name = "halcyonic-xr", Version = "test", DeviceLabel = "Quest" });

    private static NewWorkDraft Draft(string? project = null)
    {
        var draft = new NewWorkDraft(Commands) { ProjectId = project, Objective = "Plan the week's dinners." };
        draft.ChooseRuntime(new RuntimeDescriptor
        {
            RuntimeId = "opencode", DisplayName = "Local agent", Kind = "opencode", ModelChoice = ModelChoice.None, UsesProjectLocation = true,
            Capabilities = new RuntimeCapabilities { StartExecution = true },
        });
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

    private static CommandView Done(CommandEnvelope command, CommandResult? result) =>
        new() { CommandId = command.CommandId, Status = CommandStatus.Completed, Result = result };

    private static CommandView Refused(CommandEnvelope command, RejectionCode code, string message) =>
        new() { CommandId = command.CommandId, Status = CommandStatus.Rejected, Rejection = new CommandRejection { Code = code, Message = message } };

    private static ProjectLocationChoice Folder(string name) => new NewFolderChoice { Root = "/Users/person/Projects", FolderName = name };

    [Test]
    public void ANewProjectIsCreatedInItsFolder()
    {
        var sequence = new BuildSequence(Draft(), Commands, "Recipes", Folder("recipes"));
        var create = (ProjectCreateCommand)sequence.Begin(Samples.Reviewed(sequence));
        Assert.That(((NewFolderChoice)create.Payload.Location!).FolderName, Is.EqualTo("recipes"));
    }

    [Test]
    public void AnExistingProjectMovesToItsFolderFirst()
    {
        var sequence = new BuildSequence(Draft("p1"), Commands, null, Folder("shop"));
        Assert.That(sequence.Steps.Select(step => step.Kind), Is.EqualTo(new[] { BuildStepKind.BindFolder, BuildStepKind.CreateWorkstream, BuildStepKind.StartWork }));
        var bind = (ProjectSetLocationCommand)sequence.Begin(Samples.Reviewed(sequence));
        Assert.That(bind.Payload.ProjectId, Is.EqualTo("p1"));
        Assert.That(sequence.Advance(With(Done(bind, null))), Is.InstanceOf<WorkstreamCreateCommand>(), "binding completes with no result");
        Assert.That(new BuildSequence(Draft("p1"), Commands, null).Steps.Select(step => step.Kind),
            Is.EqualTo(new[] { BuildStepKind.CreateWorkstream, BuildStepKind.StartWork }), "a project keeps its folder when none is chosen");
    }

    [Test]
    public void AStartWithoutAFolderIsBoundAndStartedAgainOnTheSameWork()
    {
        var sequence = new BuildSequence(Draft("p1"), Commands, null);
        var workstream = sequence.Begin(Samples.Reviewed(sequence));
        var start = sequence.Advance(With(Done(workstream, new WorkstreamCreatedResult { WorkstreamId = "w1" })))!;
        sequence.Advance(With(Refused(start, RejectionCode.LocationRequired, "The project has no folder <b>to work in</b>.")));
        var step = sequence.StoppedAt!;
        Assert.That(step.Refusal, Is.EqualTo(RejectionCode.LocationRequired));
        Assert.That(EntryText.AboutFolder(step), Is.True);
        Assert.That(EntryText.StepStatus(step), Is.EqualTo("Couldn't do that: this project has no folder yet. Choose where its files live, then try again."),
            "the next action comes from the code, not the message");

        var bind = sequence.Retry(Samples.Reviewed(sequence, folder: Folder("recipes")), folder: Folder("recipes"));
        Assert.That(bind, Is.InstanceOf<ProjectSetLocationCommand>());
        var again = (ExecutionStartCommand)sequence.Advance(With(Done(bind, null)))!;
        Assert.That(again.Payload.WorkstreamId, Is.EqualTo("w1"), "the work already created is started, not made again");
        Assert.That(sequence.Steps.Count(each => each.Kind == BuildStepKind.CreateWorkstream), Is.EqualTo(1));
    }

    [Test]
    public void AFolderAlreadyThereOffersToUseIt()
    {
        var sequence = new BuildSequence(Draft(), Commands, "Recipes", Folder("recipes"));
        var create = sequence.Begin(Samples.Reviewed(sequence));
        sequence.Advance(With(Refused(create, RejectionCode.LocationExists, "There is already a folder named recipes.")));
        Assert.That(EntryText.StepStatus(sequence.StoppedAt!), Does.Contain("Use that folder"));
        var existing = new ExistingFolderChoice { Root = "/Users/person/Projects", FolderName = "recipes" };
        var use = (ProjectCreateCommand)sequence.Retry(Samples.Reviewed(sequence, folder: existing), folder: existing);
        Assert.That(use.Payload.Location, Is.InstanceOf<ExistingFolderChoice>());
        Assert.That(use.CommandId, Is.Not.EqualTo(create.CommandId));
    }

    /// <summary>
    /// The review's probe Q5 (2026-10-04): a step of Start building, refused with any code or failed with
    /// either effect, never shows the refusal's or the failure's message.
    /// </summary>
    [Test]
    public void NoRefusalOrFailureMessageReachesAStepOfStartBuilding()
    {
        const string Leak = "OpenCode could not be reached: connect ECONNREFUSED 127.0.0.1:4096 (/Users/someone/project)";
        foreach (RejectionCode code in Enum.GetValues(typeof(RejectionCode)))
        {
            var sequence = new BuildSequence(Draft("p1"), Commands, null);
            var workstream = sequence.Begin(Samples.Reviewed(sequence));
            sequence.Advance(With(Refused(workstream, code, Leak)));
            Assert.That(EntryText.StepStatus(sequence.StoppedAt!), Does.Not.Contain("ECONNREFUSED").And.Not.Contain("OpenCode"), code.ToString());
            Assert.That(EntryText.StepStatus(sequence.StoppedAt!).Length, Is.LessThanOrEqualTo(110), code + ": New project's two rows");
        }
        foreach (FailureEffect effect in Enum.GetValues(typeof(FailureEffect)))
        {
            var sequence = new BuildSequence(Draft("p1"), Commands, null);
            var workstream = sequence.Begin(Samples.Reviewed(sequence));
            var start = sequence.Advance(With(Done(workstream, new WorkstreamCreatedResult { WorkstreamId = "w1" })))!;
            sequence.Advance(With(new CommandView
            {
                CommandId = start.CommandId, Status = CommandStatus.Failed,
                Failure = new CommandFailure { Code = "runtime_unreachable", Message = Leak, Effect = effect },
            }));
            var step = sequence.Steps.Single(each => each.Kind == BuildStepKind.StartWork);
            Assert.That(EntryText.StepStatus(step), Does.Not.Contain("ECONNREFUSED").And.Not.Contain("OpenCode"), effect.ToString());
            if (effect == FailureEffect.None)
            {
                Assert.That(EntryText.StepStatus(step), Is.EqualTo("Couldn't do that: your computer lost touch with the agent app. Check it's running there, then try again."),
                    "a cause every adapter shares is said by its code, and with no task running, the way on is the computer");
            }
        }
        // A step's way on is what New project offers, never adding the task again from inside it (2026-10-07).
        foreach (var (code, words) in new[]
        {
            ("runtime_unavailable", "Couldn't do that: the agent app isn't available on your computer. Check its setup, then try again."),
            ("runtime_version_unsupported", "Couldn't do that: this agent app's version isn't supported yet. Choose another in How it runs."),
            ("capability_unimplemented", "Couldn't do that: its agent app doesn't support it. Choose another agent app in How it runs, then try again."),
        })
        {
            var sequence = new BuildSequence(Draft("p1"), Commands, null);
            var workstream = sequence.Begin(Samples.Reviewed(sequence));
            var start = sequence.Advance(With(Done(workstream, new WorkstreamCreatedResult { WorkstreamId = "w1" })))!;
            sequence.Advance(With(new CommandView
            {
                CommandId = start.CommandId, Status = CommandStatus.Failed,
                Failure = new CommandFailure { Code = code, Message = Leak, Effect = FailureEffect.None },
            }));
            Assert.That(EntryText.StepStatus(sequence.Steps.Single(each => each.Kind == BuildStepKind.StartWork)), Is.EqualTo(words), code);
        }
    }

    [Test]
    public void EveryFolderCodeHasANextActionAndOthersAreSaidByTheirCodeNeverTheirMessage()
    {
        foreach (var code in new[] { RejectionCode.LocationRequired, RejectionCode.LocationMissing, RejectionCode.LocationNotAllowed, RejectionCode.LocationExists })
        {
            Assert.That(EntryText.FolderProblem(code, null), Is.Not.Null, code.ToString());
        }
        Assert.That(EntryText.FolderProblem(null, "location_not_created"), Does.Contain("nothing changed"));
        Assert.That(EntryText.FolderProblem(null, "location_missing"), Is.EqualTo("Your computer can't use that folder now. Choose it again, or fix it on your computer."),
            "the host also answers location_missing for a folder it cannot read, so the words never claim it is gone");
        Assert.That(EntryText.FolderProblem(RejectionCode.InvalidState, "other"), Is.Null);

        var sequence = new BuildSequence(Draft("p1"), Commands, null);
        var workstream = sequence.Begin(Samples.Reviewed(sequence));
        sequence.Advance(With(Refused(workstream, RejectionCode.InvalidState, "Not now‮.")));
        Assert.That(EntryText.StepStatus(sequence.StoppedAt!), Is.EqualTo("Couldn't do that: it can't take that right now. See what it's doing, then try again."));
        Assert.That(EntryText.AboutFolder(sequence.StoppedAt!), Is.False);
    }

    [Test]
    public void AFailureThatMayHaveMadeTheFolderNeverSaysNothingHappened()
    {
        var sequence = new BuildSequence(Draft(), Commands, "Recipes", Folder("recipes"));
        var create = sequence.Begin(Samples.Reviewed(sequence));
        sequence.Advance(With(new CommandView
        {
            CommandId = create.CommandId, Status = CommandStatus.Failed,
            Failure = new CommandFailure { Code = "location_not_created", Message = "Made, then unusable.", Effect = FailureEffect.Unknown },
        }));
        var step = sequence.StoppedAt!;
        Assert.That(EntryText.StepStatus(step), Does.Not.Contain("nothing changed"));
        Assert.That(EntryText.StepStatus(step), Is.EqualTo(EntryText.NotSureItHappened));
        Assert.That(EntryText.AboutFolder(step), Is.False);
        Assert.That(sequence.CanRetry, Is.False);
    }

    [Test]
    public void BindingAFolderCompletesWithNoResult()
    {
        var bind = Commands.SetProjectLocation("p1", Folder("shop"));
        var submission = new NewWorkSubmission(bind);
        submission.Observe(Done(bind, null));
        Assert.That(submission.HasExpectedResult, Is.True);
        var odd = new NewWorkSubmission(bind);
        odd.Observe(Done(bind, new ProjectCreatedResult { ProjectId = "p2" }));
        Assert.That(odd.HasExpectedResult, Is.False);
    }

    [Test]
    public void APlaceShowsTheHostsLabelAndAKeptChoiceReadsItAgain()
    {
        var root = new LocationRoot
        {
            Path = "/Users/person/Projects", Name = "Projects", Status = LocationRootStatus.Available, UsedBy = new List<string>(),
            Folders = new List<LocationFolder> { new() { Name = "shop", Path = "/Users/person/Projects/shop", UsedBy = new List<string>() } },
        };
        root.Label = "Projects (person)";
        var chosen = ProjectFolder.Existing(root, root.Folders[0]);
        Assert.That(chosen.Describe(), Is.EqualTo("shop in Projects (person)"));
        Assert.That(((ExistingFolderChoice)chosen.ToContract()).Root, Is.EqualTo(root.Path), "the label is shown, never sent");
        Assert.That(ProjectFolder.New(root, "site")!.Describe(), Is.EqualTo("a new folder, site, in Projects (person)"));
        Assert.That(ProjectFolder.NewFolderLabel(root), Is.EqualTo("New folder in Projects (person)"));

        // Kept across a restart, then the roots changed: the label is read again by the root's path.
        var kept = ProjectFolder.Restore(chosen.RootPath, chosen.RootName, chosen.FolderName, isNew: false)!;
        root.Label = "Projects";
        Assert.That(kept.Current(new LocationsResponse { Roots = new List<LocationRoot> { root } }).Describe(), Is.EqualTo("shop in Projects"));
        var gone = kept.Current(new LocationsResponse { Roots = new List<LocationRoot>() });
        Assert.That(gone.PlaceGone, Is.True);
        Assert.That(gone.Describe(), Is.EqualTo("shop, in a place your computer no longer lists"), "never the old label");
        root.Status = LocationRootStatus.Missing;
        Assert.That(kept.Current(new LocationsResponse { Roots = new List<LocationRoot> { root } }).PlaceGone, Is.True);
    }

    [Test]
    public void AHostWithoutLabelsStillNamesItsPlaces()
    {
        var root = new LocationRoot
        {
            Path = "/Users/person/Projects", Name = "Projects", Status = LocationRootStatus.Available, UsedBy = new List<string>(),
            Folders = new List<LocationFolder> { new() { Name = "shop", Path = "/Users/person/Projects/shop", UsedBy = new List<string>() } },
        };
        root.Label = null!;
        Assert.That(ProjectFolder.LabelOf(root), Is.EqualTo("Projects"));
    }
}

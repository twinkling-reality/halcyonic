using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class ProjectsScreensTests
{
    private static readonly CommandFactory Commands = new(new ClientInfo { Name = "halcyonic-xr", Version = "test", DeviceLabel = "Quest" });
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private const string ProjectA = "0192a7a0-0000-7000-8000-00000000000a";

    private static WorkOverview Overview()
    {
        var state = new Portfolio()
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

    private static LocationFolder Folder(string name, bool? repository = true, string? changed = "2026-09-29T11:00:00.000Z", params string[] usedBy) => new()
    {
        Name = name,
        Path = "/Users/person/Projects/" + name,
        Repository = repository,
        ChangedAt = repository == null ? null : changed,
        UsedBy = usedBy.ToList(),
    };

    private static LocationRoot Root(string name, params LocationFolder[] folders) => new()
    {
        Path = "/Users/person/" + name,
        Name = name,
        Status = LocationRootStatus.Available,
        Repository = false,
        ChangedAt = "2026-09-01T00:00:00.000Z",
        UsedBy = new List<string>(),
        Folders = folders.ToList(),
        FoldersTruncated = false,
    };

    private static LocationsResponse Listing(params LocationRoot[] roots) => new() { Roots = roots.ToList() };

    private static ProjectsScreens.State State(LocationsResponse? listing = null, WorkOverview? overview = null) => new()
    {
        Overview = overview ?? Overview(),
        Listing = listing ?? Listing(Root("Projects", Folder("shop"))),
        Live = true,
        Now = Now,
        Zone = TimeZoneInfo.Utc,
    };

    [Test]
    public void ProjectsListsKnownProjectsThenFreeFoldersAndOffersNewProject()
    {
        var frame = ProjectsScreens.Projects(State(Listing(Root("Projects", Folder("shop"), Folder("used", true, "2026-10-02T11:00:00.000Z", ProjectA)))));
        Assert.That(frame.Subject, Is.EqualTo("What would you like to work on?"), "the place's purpose; the lit place already says Projects");
        Assert.That(frame.Lines.Select(line => (line.Words, line.Fact, line.Action)), Is.EqualTo(new (string, string?, string?)[]
        {
            ("Alpha", "1 task waiting", ProjectsScreens.ChooseProject),
            ("Beta", "Hidden · 1 task paused", ProjectsScreens.ChooseProject),
            (ProjectsText.FoldersHeading, null, null),
            ("shop", "Repository · changed 3 days ago", ProjectsScreens.ChooseFolder),
        }), "a folder a project uses is not offered");
        Assert.That(frame.Lines.Where(line => line.Action != null).All(line => line.Opens && line.WordsAreData), Is.True, "every row opens its side panel, and names are text from outside");
        Assert.That(frame.Side, Is.Null);
        Assert.That(frame.Footer.All.Select(each => (each.Slot, each.Prompt.Id, each.Prompt.Main)), Is.EqualTo(new[]
        {
            (PromptSlot.Close, Footer.Close, false),
            (PromptSlot.FarRight, ProjectsScreens.NewProject, true),
        }));
    }

    [Test]
    public void WithNoProjectsYetTheFoldersAndNewProjectAnswerTheQuestion()
    {
        var state = State(overview: WorkOverview.Of(new ClientProjection(), new StageVisibility(), _ => false));
        var frame = ProjectsScreens.Projects(state);
        Assert.That(frame.Subject, Is.EqualTo("What would you like to work on?"));
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(ProjectsScreens.NewProject));
        Assert.That(frame.Lines.First().Words, Is.EqualTo(ProjectsText.FoldersHeading), "no projects yet: the folders answer it");
    }

    [Test]
    public void AChosenProjectSlidesOutItsWorkAndTheFooterCarriesItsActions()
    {
        var state = State();
        state.ChosenProject = "a";
        var frame = ProjectsScreens.Projects(state);
        Assert.That(frame.Lines.Single(line => line.Chosen).Key, Is.EqualTo("a"));
        Assert.That(frame.Side!.Subject, Is.EqualTo("Alpha"));
        Assert.That(frame.Side.SubjectIsData, Is.True);
        Assert.That(frame.Side.Facts.Select(fact => (fact.Name, fact.Value)), Is.EqualTo(new[]
        {
            (ProjectsText.ItsWork, "1 task waiting for you, 1 running"),
            (ProjectsText.OnTheStage, ProjectsText.Shown),
        }));
        Assert.That((frame.Footer[PromptSlot.Secondary]!.Id, frame.Footer[PromptSlot.Secondary]!.Words), Is.EqualTo((ProjectsScreens.HideProject, ProjectsText.HideFromStage)));
        Assert.That((frame.Footer[PromptSlot.FarRight]!.Id, frame.Footer[PromptSlot.FarRight]!.DrawnAsMain), Is.EqualTo((ProjectsScreens.AddTask, true)));

        state.ChosenProject = "b";
        Assert.That(ProjectsScreens.Projects(state).Footer[PromptSlot.Secondary]!.Words, Is.EqualTo(ProjectsText.ShowOnStage), "worded by its current state");

        state.Demonstration = true;
        var demo = ProjectsScreens.Projects(state);
        Assert.That(demo.Footer[PromptSlot.FarRight]!.Available, Is.False, "the demonstration starts nothing");
        Assert.That(demo.Reason, Is.EqualTo(EntryText.DemoCannotStart));
    }

    [Test]
    public void AChosenFolderShowsItsFactsAndWhatConnectingDoesWithConnectAtTheFarRight()
    {
        var state = State(Listing(Root("Projects", Folder("shop"))));
        var offer = FolderConnect.Offers(state.Listing!).Single();
        state.ChosenFolder = offer.Key;
        var frame = ProjectsScreens.Projects(state);
        Assert.That(frame.Side!.Facts.Select(fact => fact.Name), Is.EqualTo(new[]
        {
            ProjectsText.Place, ProjectsText.Repository, ProjectsText.Changed, ProjectsText.Connecting,
        }));
        Assert.That(frame.Side.Facts.Select(fact => fact.Value), Is.EqualTo(new[]
        {
            "Projects", ProjectsText.Yes, "3 days ago", ConnectText.WhatConnectingDoes(offer),
        }).Using<string>((a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase)));
        Assert.That(frame.Side.Facts[0].ValueIsData, Is.True, "a place's name is text from outside");
        var connect = frame.Footer[PromptSlot.FarRight]!;
        Assert.That((connect.Id, connect.DrawnAsMain), Is.EqualTo((ProjectsScreens.Connect, true)), "the main action takes the far right slot, apart from the rows");
        Assert.That(frame.Footer[PromptSlot.Secondary], Is.Null);

        state.Live = false;
        var away = ProjectsScreens.Projects(state);
        Assert.That(away.Side, Is.Null, "away, no folder is offered, so none is chosen");
    }

    [Test]
    public void EachOutcomeKeepsItsNextAction()
    {
        var state = State(Listing(Root("Projects", Folder("shop"), Folder("notes", false))));
        var offers = FolderConnect.Offers(state.Listing!);
        var shop = offers.Single(offer => offer.RawName == "shop");
        state.ChosenFolder = shop.Key;

        var connection = new FolderConnection(shop, Commands);
        var command = connection.Begin();
        connection.Advance(With());
        state.Connection = connection;
        var waiting = ProjectsScreens.Projects(state);
        Assert.That(waiting.Footer[PromptSlot.FarRight], Is.Null, "nothing but Close while it may be on its way");
        Assert.That(waiting.Side!.Facts.Last().Value, Is.EqualTo(ConnectText.Connecting));

        var notes = offers.Single(offer => offer.RawName == "notes");
        state.ChosenFolder = notes.Key;
        var other = ProjectsScreens.Projects(state);
        Assert.That(other.Footer[PromptSlot.FarRight]!.Available, Is.False);
        Assert.That(other.Reason, Is.EqualTo(ConnectText.WaitingOn(shop)), "the reason is the page's last content line");

        state.ChosenFolder = shop.Key;
        connection.Advance(With(new CommandView { CommandId = command.CommandId, Status = CommandStatus.Completed, Result = new ProjectCreatedResult { ProjectId = ProjectA } }));
        var connected = ProjectsScreens.Projects(state);
        Assert.That(connected.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(ProjectsScreens.AddTask));
        Assert.That(connected.Side!.Facts.Last().Name, Is.EqualTo(ProjectsText.WhatHappened));

        var unknown = new FolderConnection(notes, Commands);
        var third = unknown.Begin();
        unknown.Advance(With(new CommandView
        {
            CommandId = third.CommandId, Status = CommandStatus.Failed,
            Failure = new CommandFailure { Code = "timeout", Message = "No answer.", Effect = FailureEffect.Unknown },
        }));
        state.Connection = unknown;
        state.ChosenFolder = shop.Key;
        var blocked = ProjectsScreens.Projects(state);
        Assert.That(blocked.Reason, Does.StartWith("Not sure whether"), "an unknown outcome is said as unknown, not as still coming");
        Assert.That(blocked.Reason, Does.Contain("in Projects"));
        state.ChosenFolder = notes.Key;
        var own = ProjectsScreens.Projects(state);
        Assert.That(own.Footer[PromptSlot.FarRight], Is.Null, "a connection that may have run is never offered again");
        Assert.That(own.Side!.Facts.Last().Value, Does.StartWith("Not sure whether"));
        state.ChosenFolder = shop.Key;
        state.Connection = connection;

        var refused = new FolderConnection(shop, Commands);
        var second = refused.Begin();
        refused.Advance(With(new CommandView
        {
            CommandId = second.CommandId, Status = CommandStatus.Rejected,
            Rejection = new CommandRejection { Code = RejectionCode.LocationMissing, Message = "Gone." },
        }));
        state.Connection = refused;
        Assert.That(ProjectsScreens.Projects(state).Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(ProjectsScreens.ChooseAnother));

        var notSent = new FolderConnection(shop, Commands);
        notSent.Begin();
        notSent.AcknowledgementLost(new SessionUnavailableException("Not connected."));
        notSent.Advance(null);
        state.Connection = notSent;
        Assert.That(ProjectsScreens.Projects(state).Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(ProjectsScreens.TryAgain));
    }

    [Test]
    public void TheFolderListSaysWhatIsHappeningWhenItHasNoRows()
    {
        var state = State(overview: WorkOverview.Of(new ClientProjection(), new StageVisibility(), _ => false));
        state.Listing = null;
        Assert.That(ProjectsScreens.Projects(state).Lines.Last().Words, Is.EqualTo(EntryText.ReadingFolders));
        state.ListingProblem = ConnectionText.PairingRefused;
        var unread = ProjectsScreens.Projects(state).Lines.Last();
        Assert.That((unread.Words, unread.Action, unread.Opens, unread.Tone), Is.EqualTo(("Couldn't read your computer's folders", (string?)ProjectsScreens.ChooseProblem, true, LineTone.Problem)),
            "a row only opens its side panel; Try again stands in the footer");
        Assert.That(ProjectsScreens.Projects(state).Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(ProjectsScreens.NewProject));
        state.ChosenProblem = true;
        var why = ProjectsScreens.Projects(state);
        Assert.That(why.Side!.Subject, Is.EqualTo("Couldn't read your computer's folders"));
        Assert.That(why.Side.Facts.Select(fact => (fact.Name, fact.Value, fact.ValueIsData)), Is.EqualTo(new[] { (ProjectsText.WhatHappened, ConnectionText.PairingRefused, false) }),
            "the why in Halcyonic's own words, never data");
        state.ListingProblem = EntryText.PressTryAgain;
        var unknown = ProjectsScreens.Projects(state).Side!;
        Assert.That((unknown.Facts.Count, unknown.Lines.Single().Words), Is.EqualTo((0, EntryText.PressTryAgain)), "where nothing more is known, only what to do");
        state.ListingProblem = ConnectionText.PairingRefused;
        Assert.That(why.Footer.All.Select(each => (each.Slot, each.Prompt.Id, each.Prompt.Words)), Is.EqualTo(new[]
        {
            (PromptSlot.Close, Footer.Close, ProjectsText.Close), (PromptSlot.FarRight, ProjectsScreens.ReadAgain, "Try again"),
        }));
        state.Listing = Listing();
        var none = ProjectsScreens.Projects(state);
        Assert.That(none.Lines.Last().Words, Is.EqualTo("Your computer doesn't allow any folder yet"));
        Assert.That(none.Side!.Lines.Single().Words, Is.EqualTo("Allow a folder on your computer, then try again."));
        Assert.That(none.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(ProjectsScreens.ReadAgain));
        Assert.That(new[] { unread.Words, none.Lines.Last().Words }.Any(words => words.Contains("Press", StringComparison.Ordinal)), Is.False);
        state.ChosenProblem = false;
        state.Listing = Listing(Root("Projects", Folder("used", true, null, ProjectA)));
        Assert.That(ProjectsScreens.Projects(state).Lines.Last().Words, Is.EqualTo(ConnectText.NoFreeFolders));
        state.Live = false;
        Assert.That(ProjectsScreens.Projects(state).Lines.Last().Words, Is.EqualTo(ConnectText.NotConnectedYet));
        state.Demonstration = true;
        Assert.That(ProjectsScreens.Projects(state).Lines.Select(line => line.Words), Does.Not.Contain(ProjectsText.FoldersHeading), "the demonstration lists no folders");
    }

    [Test]
    public void ALongListPagesWithNextPageAloneOutOfTheAccentAndAHeadingNeverEndsAPage([Values] TextSize size)
    {
        var folders = Enumerable.Range(0, 9).Select(index => Folder("folder-" + index, false, "2026-09-" + (10 + index) + "T00:00:00.000Z")).ToArray();
        var state = State(Listing(Root("Projects", folders)));
        state.TextSize = size;
        Assert.That(MenuFrame.RowsAPage(size, sourceLine: false), Is.EqualTo(size == TextSize.Larger ? 3 : 4), "3 rows a page at the larger size, inside a Quest 3S's field");
        var first = ProjectsScreens.Projects(state);
        Assert.That(first.Lines.Sum(line => line.Rows), Is.LessThanOrEqualTo(MenuFrame.RowsAPage(size, sourceLine: false)));
        Assert.That(first.Lines.Last().Words, Is.Not.EqualTo(ProjectsText.FoldersHeading));
        Assert.That(first.Footer.All.Select(each => each.Slot), Is.EqualTo(new[] { PromptSlot.Close, PromptSlot.Secondary, PromptSlot.FarRight }),
            "three prompts fit the column: Close, Next page, New project");
        Assert.That((first.Footer[PromptSlot.Secondary]!.Kind, first.Footer[PromptSlot.Secondary]!.Words), Is.EqualTo((PromptKind.NextPage, "Next page")));
        Assert.That(first.Footer.All.Count(each => each.Prompt.DrawnAsMain), Is.EqualTo(1));

        var seen = new List<string>();
        var page = 0;
        for (; ; page++)
        {
            state.Page = page;
            var frame = ProjectsScreens.Projects(state);
            Assert.That(frame.Lines.Sum(line => line.Rows), Is.LessThanOrEqualTo(MenuFrame.RowsAPage(size, sourceLine: false)));
            Assert.That(frame.Lines.Last().Words, Is.Not.EqualTo(ProjectsText.FoldersHeading), "a heading never ends a page");
            seen.AddRange(frame.Lines.Select(line => line.Words));
            if (frame.Footer[PromptSlot.Secondary]!.Words == "First page") break;
        }
        Assert.That(seen, Is.Unique, "each row on one page");
        Assert.That(seen.Count, Is.EqualTo(2 + 1 + 9), "every row on some page");
        state.Page = page + 1;
        Assert.That(ProjectsScreens.Projects(state).Lines.Select(line => line.Words), Is.EqualTo(first.Lines.Select(line => line.Words)), "First page starts again");

        state.Page = 0;
        state.ChosenFolder = FolderConnect.Offers(state.Listing!).Last().Key;
        var chosen = ProjectsScreens.Projects(state);
        Assert.That(chosen.Lines.Any(line => line.Chosen), Is.True, "a chosen row shows on its own page");
        Assert.That(chosen.Footer.All.Any(each => each.Prompt.Kind == PromptKind.NextPage), Is.False, "paging waits while a row is chosen");
    }

    [Test]
    public void FactsKeepTheirNounAndNeverReadAsFragments()
    {
        foreach (var project in Overview().Projects)
        {
            var fact = ProjectsText.ProjectFact(project);
            Assert.That(Regex.IsMatch(fact, @"\d+ (waiting|running|finished|paused|checking)"), Is.False, fact + ": a count keeps its noun");
        }
        var failed = new Portfolio().Project("f", "Failing").Work("x", "f", WorkstreamStatus.Failed).Apply();
        var failing = WorkOverview.Of(failed, new StageVisibility(), _ => true).Projects.Single();
        Assert.That(ProjectsText.ProjectFact(failing), Is.EqualTo("1 task to look at"), "failed or unknown work is never called finished");
        Assert.That(ProjectsText.Work(failing), Is.EqualTo("1 task to look at"));

        var zone = TimeZoneInfo.Utc;
        (string? Text, bool IsData) Fact(LocationsResponse listing) => ProjectsText.FolderFact(FolderConnect.Offers(listing).First(), Now, zone,
            FolderConnect.Offers(listing).Select(offer => offer.Root.Path).Distinct().Count() > 1);
        Assert.That(Fact(Listing(Root("Projects", Folder("a", false, "2026-10-02T09:00:00.000Z")))), Is.EqualTo(((string?)"Changed 3 hours ago", false)));
        Assert.That(Fact(Listing(Root("Projects", Folder("a", true, "2026-10-02T09:00:00.000Z")), Root("Work", Folder("b", false, "2026-09-01T00:00:00.000Z")))),
            Is.EqualTo(((string?)"In Projects · changed 3 hours ago", true)), "a place's name makes the fact text from outside");
        Assert.That(Fact(Listing(Root("Projects", Folder("a", null)))).Text, Is.EqualTo(ProjectsText.CantTell));
        Assert.That(Fact(Listing(Root("Projects", Folder("app", true, "2026-10-02T09:00:00.000Z"), Folder("APP", false, "2026-09-01T00:00:00.000Z")))).Text,
            Is.EqualTo(ConnectText.LooksLikeAnother + " · Repository · changed 3 hours ago"), "a look-alike keeps the facts that tell it apart");
    }

    [Test]
    public void NamesThatShowAsBlankStillBuildAndShowTheirCodePoints()
    {
        var state = new Portfolio().Project("p", "\u0085").Work("w", "p", WorkstreamStatus.Running).Apply();
        var overview = WorkOverview.Of(state, new StageVisibility(), _ => true);
        var projects = State(Listing(Root("Projects", Folder(" "), Folder("\u00A0"), Folder("\u3000"), Folder("\t"))), overview);
        var frame = ProjectsScreens.Projects(projects);
        Assert.That(frame.Lines.First().Words, Is.EqualTo("‹U+0085›"));
        var names = new List<string>();
        for (var page = 0; page < 3; page++)
        {
            projects.Page = page;
            names.AddRange(ProjectsScreens.Projects(projects).Lines.Where(line => line.Action == ProjectsScreens.ChooseFolder).Select(line => line.Words));
        }
        Assert.That(names.Distinct(), Is.EquivalentTo(new[] { "‹U+0020›", "‹U+00A0›", "‹U+3000›", "‹U+0009›" }));
        projects.ChosenFolder = FolderConnect.Offers(projects.Listing!).First().Key;
        Assert.That(ProjectsScreens.Projects(projects).Side!.Subject, Does.StartWith("‹U+"));
        projects.ChosenFolder = null;
        projects.ChosenProject = "p";
        Assert.That(ProjectsScreens.Projects(projects).Side!.Subject, Is.EqualTo("‹U+0085›"));
    }

    [Test]
    public void AFolderThatImitatesAProjectAndTheProjectAreBothMarked()
    {
        var state = new Portfolio().Project("p", "acme-api").Work("w", "p", WorkstreamStatus.Running).Apply();
        var projects = State(Listing(Root("Projects", Folder("Acme-API"), Folder("notes", false))), WorkOverview.Of(state, new StageVisibility(), _ => true));
        var frame = ProjectsScreens.Projects(projects);
        Assert.That(frame.Lines.Single(line => line.Words == "acme-api").Fact, Does.StartWith(ConnectText.LooksLikeAnother));
        Assert.That(frame.Lines.Single(line => line.Words == "Acme-API").Fact, Does.StartWith(ConnectText.LooksLikeAnother));
        Assert.That(frame.Lines.Single(line => line.Words == "notes").Fact, Does.Not.StartWith(ConnectText.LooksLikeAnother));
        projects.ChosenProject = "p";
        Assert.That(ProjectsScreens.Projects(projects).Side!.Facts.Select(fact => fact.Value), Does.Contain(ProjectsText.ProjectLooksAlike));

        var used = Listing(Root("Projects", Folder("shop", true, "2026-10-01T00:00:00.000Z", ProjectA), Folder("SHOP")));
        Assert.That(FolderConnect.Offers(used).Single().LooksLikeAnother, Is.True, "a free folder imitating one in use is marked");
    }

    [Test]
    public void EveryPressActsOnTheRowTheFrameShows()
    {
        var state = State(Listing(Root("Projects", Folder("shop"))));
        Assert.That(ProjectsScreens.TargetOf(state).AddTaskTo, Is.Null, "nothing chosen: New project, nothing to add to");

        state.ChosenProject = "a";
        var project = ProjectsScreens.TargetOf(state);
        Assert.That((project.AddTaskTo, project.AddTaskName, project.Folder), Is.EqualTo(("a", "Alpha", (ConnectableFolder?)null)));

        var shop = FolderConnect.Offers(state.Listing!).Single();
        var connection = new FolderConnection(shop, Commands);
        var command = connection.Begin();
        connection.Advance(With(new CommandView { CommandId = command.CommandId, Status = CommandStatus.Completed, Result = new ProjectCreatedResult { ProjectId = ProjectA } }));
        state.Connection = connection;
        // The chosen project wins over the connection made a moment ago.
        Assert.That(ProjectsScreens.TargetOf(state).AddTaskTo, Is.EqualTo("a"));
        state.ChosenProject = null;
        Assert.That(ProjectsScreens.TargetOf(state).AddTaskTo, Is.Null, "a connection for a folder not chosen adds nothing");
        state.ChosenFolder = shop.Key;
        var folder = ProjectsScreens.TargetOf(state);
        Assert.That((folder.Folder?.Key, folder.AddTaskTo, folder.AddTaskName), Is.EqualTo((shop.Key, ProjectA, "shop")));
        Assert.That(ProjectsScreens.Projects(state).Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(ProjectsScreens.AddTask));
    }

    [Test]
    public void AFolderBoundSinceTheListingWasReadIsNotOfferedAgain()
    {
        var state = State(Listing(Root("Projects", Folder("shop"), Folder("notes", false))));
        state.BoundPaths = new[] { "/Users/person/Projects/notes" };
        Assert.That(ProjectsScreens.Projects(state).Lines.Select(line => line.Words), Does.Not.Contain("notes"));
        var shop = FolderConnect.Offers(state.Listing!).Single(offer => offer.RawName == "shop");
        var connection = new FolderConnection(shop, Commands);
        var command = connection.Begin();
        connection.Advance(With(new CommandView { CommandId = command.CommandId, Status = CommandStatus.Completed, Result = new ProjectCreatedResult { ProjectId = ProjectA } }));
        state.Connection = connection;
        state.ChosenFolder = shop.Key;
        state.BoundPaths = new[] { "/Users/person/Projects/notes", "/Users/person/Projects/shop" };
        Assert.That(ProjectsScreens.Projects(state).Side!.Facts.Last().Name, Is.EqualTo(ProjectsText.WhatHappened), "the folder just connected keeps its outcome while chosen");
        state.ChosenFolder = null;
        Assert.That(ProjectsScreens.Projects(state).Lines.Select(line => line.Words), Does.Not.Contain("shop"), "and is not offered once let go");
    }

    [Test]
    public void AChosenFolderMeansNothingInTheDemonstration()
    {
        var state = State(Listing(Root("Projects", Folder("shop"))));
        state.ChosenFolder = FolderConnect.Offers(state.Listing!).Single().Key;
        state.Demonstration = true;
        var frame = ProjectsScreens.Projects(state);
        Assert.That(frame.Side, Is.Null);
        Assert.That(frame.Lines.Any(line => line.Action == ProjectsScreens.ChooseFolder), Is.False);
        Assert.That(ProjectsScreens.TargetOf(state).Folder, Is.Null);
    }

    [Test]
    public void TheWordsNameNoBrandAndUseNoEmDashOrPath()
    {
        var state = State(Listing(Root("Projects", Folder("shop"))));
        state.ChosenFolder = FolderConnect.Offers(state.Listing!).Single().Key;
        var frames = new[] { ProjectsScreens.Projects(State()), ProjectsScreens.Projects(state) };
        foreach (var frame in frames)
        {
            var words = new List<string> { frame.Subject };
            // No path reaches any word, ours or from outside.
            var everything = frame.Lines.Select(line => line.Words).Concat(frame.Lines.Select(line => line.Fact).OfType<string>())
                .Concat(frame.Side?.Facts.SelectMany(fact => new[] { fact.Name, fact.Value }) ?? Array.Empty<string>()).Append(frame.Side?.Subject ?? "");
            Assert.That(everything.Where(word => word.Contains('/', StringComparison.Ordinal)), Is.Empty);
            words.AddRange(frame.Lines.Where(line => !line.WordsAreData).Select(line => line.Words));
            words.AddRange(frame.Lines.Select(line => line.Fact).OfType<string>());
            words.AddRange(frame.Footer.All.Select(each => each.Prompt.Words));
            if (frame.Side != null) words.AddRange(frame.Side.Facts.Where(fact => !fact.ValueIsData).SelectMany(fact => new[] { fact.Name, fact.Value }));
            foreach (var word in words)
            {
                Assert.That(word.Contains('\u2014', StringComparison.Ordinal) || word.Contains("Mac", StringComparison.Ordinal), Is.False, word);
                Assert.That(Regex.IsMatch(word, @"\b(path|directory|workstream|runtime)\b", RegexOptions.IgnoreCase), Is.False, word);
            }
        }
    }

    [Test]
    public void APressActsOnlyWhereTheFrameOffersIt()
    {
        var state = State(Listing(Root("Projects", Folder("shop"), Folder("notes", false))));
        Assert.That(ProjectsScreens.Allows(state, ProjectsScreens.NewProject), Is.True);
        Assert.That(ProjectsScreens.Allows(state, ProjectsScreens.Connect), Is.False, "no folder chosen");
        Assert.That(ProjectsScreens.Allows(state, ProjectsScreens.HideProject), Is.False, "a stale press for a project no longer chosen");

        var offers = FolderConnect.Offers(state.Listing!);
        var shop = offers.Single(offer => offer.RawName == "shop");
        state.ChosenFolder = shop.Key;
        Assert.That(ProjectsScreens.Allows(state, ProjectsScreens.Connect), Is.True);
        var connection = new FolderConnection(shop, Commands);
        connection.Begin();
        connection.Advance(With());
        state.Connection = connection;
        Assert.That(ProjectsScreens.Allows(state, ProjectsScreens.Connect), Is.False, "never a second Connect once the first was sent");

        state.ChosenFolder = offers.Single(offer => offer.RawName == "notes").Key;
        Assert.That(ProjectsScreens.TargetOf(state).Folder, Is.Not.Null, "the target says what is chosen");
        Assert.That(ProjectsScreens.Allows(state, ProjectsScreens.Connect), Is.False, "Allows says whether it may act: not while another is unresolved");

        state.ChosenFolder = null;
        state.ChosenProject = "a";
        state.Demonstration = true;
        Assert.That(ProjectsScreens.Allows(state, ProjectsScreens.AddTask), Is.False, "the demonstration adds nothing");
        Assert.That(ProjectsScreens.Allows(state, ProjectsScreens.ChooseProject), Is.True, "a row still takes the person somewhere");
        Assert.That(ProjectsScreens.Allows(state, SidePanel.Close), Is.True, "the side panel's own Close, while it shows");
        state.ChosenProject = null;
        Assert.That(ProjectsScreens.Allows(state, SidePanel.Close), Is.False, "no side panel, no Close for it");
    }

    [Test]
    public void AddTaskNamesTheNewProjectByLabelTextsRule()
    {
        var hostile = "shop\u202Eexe\nvia\uE769";
        var state = State(Listing(Root("Projects", Folder(hostile))));
        var offer = FolderConnect.Offers(state.Listing!).Single();
        var connection = new FolderConnection(offer, Commands);
        var command = connection.Begin();
        connection.Advance(With(new CommandView { CommandId = command.CommandId, Status = CommandStatus.Completed, Result = new ProjectCreatedResult { ProjectId = ProjectA } }));
        state.Connection = connection;
        state.ChosenFolder = offer.Key;
        var name = ProjectsScreens.TargetOf(state).AddTaskName!;
        foreach (var raw in new[] { '\u202E', '\n', '\uE769' }) Assert.That(name.Contains(raw, StringComparison.Ordinal), Is.False, "never raw: U+" + ((int)raw).ToString("X4"));
        Assert.That(name, Is.EqualTo(LabelText.Name(offer.ProjectName)));
    }

    [Test]
    public void ARootListedTwiceGivesEachFolderOneRow()
    {
        var root = Root("Projects", Folder("shop"));
        var state = State(Listing(root, root));
        Assert.That(FolderConnect.Offers(state.Listing!).Count, Is.EqualTo(1));
        state.ChosenFolder = FolderConnect.Offers(state.Listing!).Single().Key;
        Assert.That(ProjectsScreens.Projects(state).Lines.Count(line => line.Chosen), Is.EqualTo(1));
    }

    [Test]
    public void AFolderJustConnectedIsNotALookAlikeOfItsProjectAndANeverSentOneIsNotKept()
    {
        var listing = Listing(Root("Projects", Folder("shop"), Folder("notes", false)));
        var shop = FolderConnect.Offers(listing).Single(offer => offer.RawName == "shop");
        var connection = new FolderConnection(shop, Commands);
        var command = connection.Begin();
        connection.Advance(With(new CommandView { CommandId = command.CommandId, Status = CommandStatus.Completed, Result = new ProjectCreatedResult { ProjectId = ProjectA } }));
        var portfolio = new Portfolio().Project(ProjectA, "shop").Work("w", ProjectA, WorkstreamStatus.Running).Apply();
        var state = State(listing, WorkOverview.Of(portfolio, new StageVisibility(), _ => true));
        state.Connection = connection;
        state.ChosenFolder = shop.Key;
        state.BoundPaths = new[] { "/Users/person/Projects/shop" };
        var frame = ProjectsScreens.Projects(state);
        Assert.That(frame.Lines.Where(line => line.Fact != null).Any(line => line.Fact!.StartsWith(ConnectText.LooksLikeAnother, StringComparison.Ordinal)), Is.False,
            "the folder and the project it became are one thing");

        var notes = FolderConnect.Offers(listing).Single(offer => offer.RawName == "notes");
        var notSent = new FolderConnection(notes, Commands);
        notSent.Begin();
        notSent.AcknowledgementLost(new SessionUnavailableException("Not connected."));
        notSent.Advance(null);
        state.Connection = notSent;
        state.ChosenFolder = notes.Key;
        state.BoundPaths = new[] { "/Users/person/Projects/shop", "/Users/person/Projects/notes" };
        Assert.That(ProjectsScreens.Projects(state).Lines.Select(line => line.Words), Does.Not.Contain("notes"),
            "a folder another project bound is not kept for a connection that never left");
    }

    [Test]
    public void AProjectLookingLikeAFolderAnotherProjectUsesIsMarkedButNotLikeItsOwn()
    {
        const string Other = "0192a7a0-0000-7000-8000-00000000000b";
        var portfolio = new Portfolio().Project(ProjectA, "SVC").Project(Other, "svc").Apply();
        var listing = Listing(Root("Projects", Folder("svc", true, "2026-10-01T00:00:00.000Z", Other)));
        var frame = ProjectsScreens.Projects(State(listing, WorkOverview.Of(portfolio, new StageVisibility(), _ => true)));
        Assert.That(frame.Lines.Single(line => line.Words == "SVC").Fact, Does.StartWith(ConnectText.LooksLikeAnother));

        var alone = new Portfolio().Project(Other, "svc").Apply();
        var own = ProjectsScreens.Projects(State(listing, WorkOverview.Of(alone, new StageVisibility(), _ => true)));
        Assert.That(own.Lines.Single(line => line.Words == "svc").Fact, Does.Not.StartWith(ConnectText.LooksLikeAnother), "a project is not a look-alike of its own folder");
    }

    private static ClientProjection With(params CommandView[] commands)
    {
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(1);
        snapshot.Commands = commands.ToList();
        state.ApplySnapshot(snapshot, new StateChanges());
        return state;
    }
}

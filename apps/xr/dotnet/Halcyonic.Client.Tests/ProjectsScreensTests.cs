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
        Assert.That((connect.Id, connect.DrawnAsMain), Is.EqualTo((ProjectsScreens.Connect, true)), "never where the row's press landed: rows are on the left, the main action far right");
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
        state.ListingProblem = "timed out";
        var unread = ProjectsScreens.Projects(state).Lines.Last();
        Assert.That((unread.Action, unread.Tone), Is.EqualTo(((string?)ProjectsScreens.ReadAgain, LineTone.Problem)));
        state.Listing = Listing();
        Assert.That(ProjectsScreens.Projects(state).Lines.Last().Words, Is.EqualTo(EntryText.NoFolders));
        state.Listing = Listing(Root("Projects", Folder("used", true, null, ProjectA)));
        Assert.That(ProjectsScreens.Projects(state).Lines.Last().Words, Is.EqualTo(ConnectText.NoFreeFolders));
        state.Live = false;
        Assert.That(ProjectsScreens.Projects(state).Lines.Last().Words, Is.EqualTo(ConnectText.NotConnectedYet));
        state.Demonstration = true;
        Assert.That(ProjectsScreens.Projects(state).Lines.Select(line => line.Words), Does.Not.Contain(ProjectsText.FoldersHeading), "the demonstration lists no folders");
    }

    [Test]
    public void ALongListPagesWithNextPageAloneOutOfTheAccentAndAHeadingNeverEndsAPage()
    {
        var folders = Enumerable.Range(0, 9).Select(index => Folder("folder-" + index, false, "2026-09-" + (10 + index) + "T00:00:00.000Z")).ToArray();
        var state = State(Listing(Root("Projects", folders)));
        var first = ProjectsScreens.Projects(state);
        Assert.That(first.Lines.Sum(line => line.Rows), Is.LessThanOrEqualTo(ProjectsScreens.Rows));
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
        var zone = TimeZoneInfo.Utc;
        string? Fact(LocationsResponse listing) => ProjectsText.FolderFact(FolderConnect.Offers(listing).First(), Now, zone,
            FolderConnect.Offers(listing).Select(offer => offer.Root.Path).Distinct().Count() > 1);
        Assert.That(Fact(Listing(Root("Projects", Folder("a", false, "2026-10-02T09:00:00.000Z")))), Is.EqualTo("Changed 3 hours ago"));
        Assert.That(Fact(Listing(Root("Projects", Folder("a", true, "2026-10-02T09:00:00.000Z")), Root("Work", Folder("b", false, "2026-09-01T00:00:00.000Z")))),
            Is.EqualTo("In Projects · changed 3 hours ago"));
        Assert.That(Fact(Listing(Root("Projects", Folder("a", null)))), Is.EqualTo(ProjectsText.CantTell));
        Assert.That(Fact(Listing(Root("Projects", Folder("app", true), Folder("APP", false)))), Is.EqualTo(ConnectText.LooksLikeAnother));
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

    private static ClientProjection With(params CommandView[] commands)
    {
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(1);
        snapshot.Commands = commands.ToList();
        state.ApplySnapshot(snapshot, new StateChanges());
        return state;
    }
}

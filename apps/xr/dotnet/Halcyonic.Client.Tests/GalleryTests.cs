using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The projects gallery's core (ADR 0039): Halcyonic's projects and the catalog, as one ordered list.</summary>
public class GalleryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-29T15:00:00Z");

    private static CatalogProject Catalog(
        string id,
        string name,
        string state,
        string fullName = "harbor-labs/tide-web",
        string? lastActivity = "2026-10-29T10:00:00Z",
        string? groupId = null,
        params string[] linked) =>
        new()
        {
            CatalogId = id,
            Name = name,
            GroupId = groupId,
            GroupOrder = groupId == null ? null : 0,
            Repositories = new List<CatalogRepository>
            {
                new()
                {
                    RepositoryId = 4100001,
                    FullName = fullName,
                    Primary = true,
                    Visibility = CatalogRepositoryVisibility.Private,
                    Fork = false,
                    Archived = false,
                    Language = null,
                    Description = null,
                    PushedAt = null,
                    Access = CatalogRepositoryAccess.Ok,
                },
            },
            State = new CatalogState
            {
                State = state,
                Active = true,
                LastActivityAt = lastActivity,
                Sessions = new CatalogStateSessions { Running = 0, AwaitingInput = 0 },
                Unknown = new List<string>(),
            },
            Attention = new List<CatalogAttention>(),
            Local = new CatalogLocal { ProjectIds = linked.ToList(), Suggestions = new List<CatalogLocalSuggestionsItem>() },
        };

    private static IntelligenceRead<CatalogResponse> Read(
        IEnumerable<CatalogProject> projects,
        bool sample = false,
        bool synthetic = false,
        string? checkedAt = "2026-10-29T14:50:00Z",
        bool failed = false,
        IEnumerable<CatalogGroup>? groups = null) =>
        new(
            new AvailableCatalog
            {
                Source = new CatalogSource { System = "phat-porcupine", ApiVersion = "1", Synthetic = synthetic, Sample = sample },
                GeneratedAt = "2026-10-29T14:59:00Z",
                Sync = new AvailableSync { RepositoriesAt = checkedAt, RepositoriesFailed = failed, ActivityAt = checkedAt, ActivityFailed = failed },
                Groups = (groups ?? Array.Empty<CatalogGroup>()).ToList(),
                Projects = projects.ToList(),
                Truncated = null,
            },
            Now,
            recorded: false);

    private static (ClientProjection State, WorkOverview Overview) Halcyonic(Action<Portfolio>? build = null)
    {
        var portfolio = new Portfolio();
        build?.Invoke(portfolio);
        var state = portfolio.Apply();
        var visibility = new StageVisibility();
        visibility.UseJournal(Samples.JournalId);
        return (state, WorkOverview.Of(state, visibility, _ => true));
    }

    private static Gallery Build(
        (ClientProjection State, WorkOverview Overview) halcyonic,
        IntelligenceRead<CatalogResponse>? read = null,
        string? readError = null,
        string? search = null) =>
        Gallery.Build(halcyonic.State, halcyonic.Overview, read, readError, Now, TimeZoneInfo.Utc, search);

    [Test]
    public void WhatNeedsThePersonComesFirstThenWhatIsWorkingThenRecentThenQuiet()
    {
        var halcyonic = Halcyonic(portfolio => portfolio
            .Project("p-wait", "Fish friends")
            .Project("p-run", "Bouncy blocks")
            .Work("w1", "p-wait", WorkstreamStatus.WaitingForHuman)
            .Work("w2", "p-run", WorkstreamStatus.Running));
        var gallery = Build(halcyonic, Read(new[]
        {
            Catalog("c-quiet", "Old notes", "inactive", lastActivity: "2026-06-01T10:00:00Z"),
            Catalog("c-review", "Tide", "review_requested"),
            Catalog("c-recent", "Garden", "active"),
            Catalog("c-agent", "Moles", "agent_running"),
        }));

        // Within a group, work on this computer with a character comes before the catalog's, then the newest.
        Assert.That(gallery.Items.Select(item => item.Key), Is.EqualTo(new[]
        {
            "h:p-wait", "c:c-review", "h:p-run", "c:c-agent", "c:c-recent", "c:c-quiet",
        }));
        Assert.That(gallery.Items.Select(item => item.Section), Is.Ordered);
        Assert.That(gallery.Items.Single(item => item.Key == "c:c-review").Line.Text, Is.EqualTo("Review Requested"));
        Assert.That(gallery.Items.Single(item => item.Key == "c:c-quiet").Line.Text, Is.EqualTo("Quiet"));
    }

    [Test]
    public void EveryStateOfTheCatalogHasItsLineAndGroup()
    {
        var expected = new Dictionary<string, (GallerySection, string)>
        {
            ["awaiting_input"] = (GallerySection.NeedsYou, "Agent Waiting"),
            ["session_failed"] = (GallerySection.NeedsYou, "Agent Failed"),
            ["checks_failing"] = (GallerySection.NeedsYou, "Checks Failing"),
            ["review_requested"] = (GallerySection.NeedsYou, "Review Requested"),
            ["changes_requested"] = (GallerySection.NeedsYou, "Changes Requested"),
            ["ready_to_merge"] = (GallerySection.NeedsYou, "Ready to Merge"),
            ["agent_running"] = (GallerySection.WorkingNow, "Agent Running"),
            ["session_complete"] = (GallerySection.Recent, "Agent Finished"),
            ["active"] = (GallerySection.Recent, "Changed Recently"),
            ["inactive"] = (GallerySection.Quiet, "Quiet"),
            ["archived"] = (GallerySection.Quiet, "Archived"),
            ["access_lost"] = (GallerySection.Quiet, "Unreachable"),
        };
        foreach (var (state, (section, line)) in expected)
        {
            Assert.That(GalleryText.State(state), Is.EqualTo((section, line)), state);
        }
        // A reported state never borrows the words of a task's own observed state.
        foreach (var ours in new[] { "Needs You", "Checks Failed", "Running", "Round Ended", "Failed" })
        {
            Assert.That(expected.Values.Select(value => value.Item2), Has.None.EqualTo(ours), ours);
        }
    }

    [Test]
    public void AStateHalcyonicDoesNotKnowYetNeedsALook()
    {
        var gallery = Build(Halcyonic(), Read(new[] { Catalog("c1", "Tide", "deploy_failed") }));
        var item = gallery.Items.Single();
        Assert.That(item.Section, Is.EqualTo(GallerySection.NeedsYou));
        Assert.That(item.Line.Text, Is.EqualTo("Needs a Look"));
    }

    [Test]
    public void ALinkedCatalogProjectAndItsHalcyonicProjectAreOneItemOnYourComputer()
    {
        var halcyonic = Halcyonic(portfolio => portfolio
            .Project("p1", "My tide app")
            .Work("w1", "p1", WorkstreamStatus.Running));
        halcyonic.State.Projects["p1"].Location = new ProjectLocation { Path = "/work/tide-web", Name = "tide-web", Created = false };
        var gallery = Build(halcyonic, Read(new[] { Catalog("c1", "Tide", "review_requested", linked: "p1") }));

        var item = gallery.Items.Single();
        Assert.That(item.Key, Is.EqualTo("h:p1"));
        Assert.That(item.CatalogId, Is.EqualTo("c1"));
        Assert.That(item.Name.Text, Is.EqualTo("My tide app"), "the person's own name for it");
        Assert.That(item.Owner?.Text, Is.EqualTo("harbor-labs"));
        Assert.That(item.Section, Is.EqualTo(GallerySection.NeedsYou), "the more urgent of the two");
        Assert.That(item.SectionFrom, Is.EqualTo(GallerySectionSource.Catalog));
        Assert.That(item.ObservedLine?.Text, Is.EqualTo("Running · 1"), "this computer's own tasks keep their line");
        Assert.That(item.ReportedLine?.Text, Is.EqualTo("Review Requested"));
        Assert.That(item.Line.Text, Is.EqualTo("Running · 1"));
        Assert.That(item.LineReported, Is.False);
        Assert.That(item.HasCharacter, Is.True);
        Assert.That(item.OnYourComputer, Is.True);
        Assert.That(item.Place.Text, Is.EqualTo("On Your Computer"));
        Assert.That(item.PlaceInferred, Is.False);
    }

    [Test]
    public void WhenThisComputersTaskIsTheMoreUrgentItPlacesTheItemAndTheReportStaysBesideIt()
    {
        var halcyonic = Halcyonic(portfolio => portfolio
            .Project("p1", "My tide app")
            .Work("w1", "p1", WorkstreamStatus.WaitingForHuman));
        var item = Build(halcyonic, Read(new[] { Catalog("c1", "Tide", "agent_running", linked: "p1") })).Items.Single();
        Assert.That(item.Section, Is.EqualTo(GallerySection.NeedsYou));
        Assert.That(item.SectionFrom, Is.EqualTo(GallerySectionSource.Halcyonic));
        Assert.That(item.ObservedLine?.Text, Is.EqualTo("Needs You · 1"));
        Assert.That(item.ReportedLine?.Text, Is.EqualTo("Agent Running"));
        Assert.That(item.Place.Text, Is.EqualTo("No Folder Yet"), "a project with no folder says so");
    }

    [Test]
    public void OnATieThisComputerPlacesTheItem()
    {
        var halcyonic = Halcyonic(portfolio => portfolio
            .Project("p1", "My tide app")
            .Work("w1", "p1", WorkstreamStatus.Running));
        var item = Build(halcyonic, Read(new[] { Catalog("c1", "Tide", "agent_running", linked: "p1") })).Items.Single();
        Assert.That(item.Section, Is.EqualTo(GallerySection.WorkingNow));
        Assert.That(item.SectionFrom, Is.EqualTo(GallerySectionSource.Halcyonic));
    }

    [Test]
    public void ACatalogOnlyItemsLineIsMarkedReported()
    {
        var item = Build(Halcyonic(), Read(new[] { Catalog("c1", "Tide", "review_requested") })).Items.Single();
        Assert.That(item.ObservedLine, Is.Null);
        Assert.That(item.ReportedLine?.Text, Is.EqualTo("Review Requested"));
        Assert.That(item.Line.Text, Is.EqualTo("Review Requested"));
        Assert.That(item.LineReported, Is.True);
        Assert.That(item.SectionFrom, Is.EqualTo(GallerySectionSource.Catalog));
    }

    [Test]
    public void AFolderTheComputerMatchedIsAMaybeAndLeans()
    {
        var project = Catalog("c1", "Tide", "active");
        project.Local.Suggestions.Add(new CatalogLocalSuggestionsItem
        {
            Root = "/work",
            FolderName = "tide-web",
            Rule = CatalogLocalSuggestionsItemRule.GithubOriginFullName,
        });
        var item = Build(Halcyonic(), Read(new[] { project })).Items.Single();
        Assert.That(item.Place.Text, Is.EqualTo("Possible Match · tide-web"));
        Assert.That(item.Place.Runs.Select(run => run.Theirs), Is.EqualTo(new[] { false, true }), "the folder's name leans");
        Assert.That(item.PlaceInferred, Is.True);
        Assert.That(item.OnYourComputer, Is.False, "a match is never a link");
    }

    [Test]
    public void ACatalogProjectHasNoCharacterEvenWhileAnAgentWorksOnIt()
    {
        var gallery = Build(Halcyonic(), Read(new[] { Catalog("c1", "Moles", "agent_running") }));
        var item = gallery.Items.Single();
        Assert.That(item.HasCharacter, Is.False);
        Assert.That(item.OnYourComputer, Is.False);
        Assert.That(item.Place.Text, Is.EqualTo("Not Linked"), "this client can't know it isn't on the computer");
        Assert.That(item.PlaceInferred, Is.False);
    }

    [Test]
    public void ASampleCatalogIsKeptApartLabelledAndNeverMergedWithRealProjects()
    {
        var halcyonic = Halcyonic(portfolio => portfolio.Project("p1", "Real one"));
        foreach (var read in new[]
        {
            Read(new[] { Catalog("c1", "Tide", "active", linked: "p1") }, sample: true),
            Read(new[] { Catalog("c1", "Tide", "active", linked: "p1") }, synthetic: true),
        })
        {
            var gallery = Build(halcyonic, read);
            Assert.That(gallery.Items.Select(item => item.Key), Is.EqualTo(new[] { "h:p1" }));
            Assert.That(gallery.Samples.Select(item => item.Key), Is.EqualTo(new[] { "c:c1" }));
            Assert.That(gallery.Samples.All(item => item.Sample), Is.True);
            Assert.That(gallery.SampleLabel, Is.EqualTo("Sample Projects"));
        }
        Assert.That(Build(halcyonic, Read(Array.Empty<CatalogProject>())).SampleLabel, Is.Null);
    }

    [Test]
    public void CatalogTextLeansAsOutsideWordsAndKeepsLabelTextsRule()
    {
        var project = Catalog("c1", "Tide\u202Eevil", "review_requested");
        project.Attention.Add(new CatalogAttention
        {
            Kind = "review_requested",
            RepositoryId = 4100001,
            PullRequest = new CatalogAttentionPullRequest { Number = 12, Title = "Add a chart" },
            At = null,
        });
        var gallery = Build(Halcyonic(), Read(new[] { project }, groups: null));
        var item = gallery.Items.Single();
        Assert.That(item.Name.AllTheirs, Is.True);
        Assert.That(item.Name.Text, Is.EqualTo(LabelText.Name("Tide\u202Eevil")));
        Assert.That(item.Name.Text.IndexOf('\u202E'), Is.EqualTo(-1), "an ordinal search: a culture's would skip the mark");
        Assert.That(item.Detail?.AllTheirs, Is.True);
        Assert.That(item.Detail?.Text, Is.EqualTo("Add a chart"));
        Assert.That(item.Line.AnyTheirs, Is.False, "the state's line is Halcyonic's own");
    }

    [Test]
    public void AGroupThePersonMadeIsNamed()
    {
        var gallery = Build(
            Halcyonic(),
            Read(new[] { Catalog("c1", "Tide", "active", groupId: "g1") }, groups: new[] { new CatalogGroup { GroupId = "g1", Name = "Ocean", Order = 0 } }));
        Assert.That(gallery.Items.Single().Group?.Text, Is.EqualTo("Ocean"));
    }

    [Test]
    public void SearchFindsByNameAndOwnerInEitherCase()
    {
        var halcyonic = Halcyonic(portfolio => portfolio.Project("p1", "Fish friends"));
        var read = Read(new[]
        {
            Catalog("c1", "Tide", "active", fullName: "harbor-labs/tide-web"),
            Catalog("c2", "Garden", "active", fullName: "rowan/garden"),
        });
        Assert.That(Build(halcyonic, read, search: "FISH").Items.Select(item => item.Key), Is.EqualTo(new[] { "h:p1" }));
        Assert.That(Build(halcyonic, read, search: "harbor").Items.Select(item => item.Key), Is.EqualTo(new[] { "c:c1" }));
        Assert.That(Build(halcyonic, read, search: "  ").Items, Has.Count.EqualTo(3));
    }

    [Test]
    public void WithoutTheCatalogTheGalleryHoldsHalcyonicsProjectsAndSaysWhy()
    {
        var halcyonic = Halcyonic(portfolio => portfolio.Project("p1", "Fish friends"));
        var cases = new (CatalogResponse Response, string Note)[]
        {
            (new UnavailableCatalog { Reason = new ErrorInfo { Code = "not_running", Message = "x" } },
                "Phat Porcupine · Not Running"),
            (new UnavailableCatalog { Reason = new ErrorInfo { Code = "not_configured", Message = "x" } },
                "Phat Porcupine · Not Set Up · Set Up on Computer"),
            (new UnavailableCatalog { Reason = new ErrorInfo { Code = "not_ready", Message = "x" } },
                "Phat Porcupine · Gathering · Refresh"),
            (new UnavailableCatalog { Reason = new ErrorInfo { Code = "something_new", Message = "x" } },
                "Projects Unreadable · Refresh"),
            (new UnauthorizedCatalog { Reason = new ErrorInfo { Code = "unauthorized", Message = "x" } },
                "Phat Porcupine · Access Refused · Set Up Again"),
            (new IncompatibleCatalog { Reason = new ErrorInfo { Code = "unsupported_contract", Message = "x" } },
                "Phat Porcupine · Version Unsupported"),
        };
        foreach (var (response, note) in cases)
        {
            var gallery = Build(halcyonic, new IntelligenceRead<CatalogResponse>(response, Now, recorded: false));
            Assert.That(gallery.Items.Select(item => item.Key), Is.EqualTo(new[] { "h:p1" }));
            Assert.That(gallery.Note, Is.EqualTo(note));
            Assert.That(gallery.Age, Is.Null);
        }
        var failed = Build(halcyonic, readError: "No Answer · Refresh");
        Assert.That(failed.Note, Is.EqualTo("Projects Unreadable · No Answer · Refresh"));
        Assert.That(Build(halcyonic).Note, Is.Null, "nothing asked yet");
    }

    [Test]
    public void TheSourceIsNamedOnlyWhereARealControlPlaneAnswered()
    {
        var halcyonic = Halcyonic(portfolio => portfolio.Project("p1", "Fish friends"));
        var answers = new CatalogResponse[]
        {
            new UnavailableCatalog { Reason = new ErrorInfo { Code = "not_running", Message = "x" } },
            new UnavailableCatalog { Reason = new ErrorInfo { Code = "not_configured", Message = "x" } },
            new UnavailableCatalog { Reason = new ErrorInfo { Code = "not_ready", Message = "x" } },
            new UnauthorizedCatalog { Reason = new ErrorInfo { Code = "unauthorized", Message = "x" } },
            new IncompatibleCatalog { Reason = new ErrorInfo { Code = "unsupported_contract", Message = "x" } },
        };
        foreach (var answer in answers)
        {
            // The demonstration, the judge build and a recorded run play recorded answers.
            var recorded = Build(halcyonic, new IntelligenceRead<CatalogResponse>(answer, Now, recorded: true));
            Assert.That(recorded.Note, Is.EqualTo("Not in Recording"), answer.GetType().Name);
        }
        var samples = new[]
        {
            Read(new[] { Catalog("c1", "Tide", "active") }, sample: true),
            Read(new[] { Catalog("c1", "Tide", "active") }, synthetic: true),
            new IntelligenceRead<CatalogResponse>(Read(new[] { Catalog("c1", "Tide", "active") }, synthetic: true).Response, Now, recorded: true),
        };
        foreach (var read in samples)
        {
            var gallery = Build(halcyonic, read);
            var words = new[] { gallery.Note, gallery.Age, gallery.SampleLabel }
                .Concat(gallery.Samples.SelectMany(item => new[] { item.Line.Text, item.Place.Text, item.ReportedLine?.Text }))
                .Where(text => text != null);
            foreach (var text in words)
            {
                Assert.That(text!.IndexOf(GalleryText.Source, StringComparison.OrdinalIgnoreCase), Is.EqualTo(-1), text);
                Assert.That(text.IndexOf("GitHub", StringComparison.OrdinalIgnoreCase), Is.EqualTo(-1), text);
            }
            Assert.That(gallery.Note, Is.Null);
        }
    }

    [Test]
    public void TheCatalogSaysHowOldItIs()
    {
        var halcyonic = Halcyonic();
        Assert.That(Build(halcyonic, Read(Array.Empty<CatalogProject>())).Age, Is.EqualTo("Checked · Today 14:50"));
        Assert.That(
            Build(halcyonic, Read(Array.Empty<CatalogProject>(), checkedAt: "2026-10-27T09:05:00Z")).Age,
            Is.EqualTo("Checked · 27 Oct 09:05"));
        Assert.That(
            Build(halcyonic, Read(Array.Empty<CatalogProject>(), failed: true)).Age,
            Is.EqualTo("Refresh Failed · Last · Today 14:50"));
        Assert.That(Build(halcyonic, Read(Array.Empty<CatalogProject>(), checkedAt: null)).Age, Is.EqualTo("Not Checked"));
    }

    [Test]
    public void ANewcomersGalleryHoldsWhatTheyMadeAndNothingElse()
    {
        var gallery = Build(Halcyonic(portfolio => portfolio.Project("p1", "My first game")));
        var item = gallery.Items.Single();
        Assert.That(item.Line.Text, Is.EqualTo("No Work"));
        Assert.That(item.HasCharacter, Is.False);
        Assert.That(gallery.Samples, Is.Empty);
    }

    [Test]
    public void ThePersonLinksAndUnlinksWithCommandsTheContractAccepts()
    {
        var factory = new CommandFactory(Samples.Client);
        var link = factory.LinkCatalog(Samples.ProjectId, 4100001, "harbor-labs/tide-web");
        var json = Newtonsoft.Json.Linq.JObject.Parse(HalcyonicJson.Serialize(link));
        Assert.That(json["command_type"]?.ToString(), Is.EqualTo("project.link_catalog"));
        Assert.That(json["payload"]?["catalog"]?.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo(
            "{\"source\":\"phat-porcupine\",\"repository_id\":4100001,\"full_name\":\"harbor-labs/tide-web\"}"));
        var unlink = Newtonsoft.Json.Linq.JObject.Parse(HalcyonicJson.Serialize(factory.UnlinkCatalog(Samples.ProjectId)));
        Assert.That(unlink["command_type"]?.ToString(), Is.EqualTo("project.unlink_catalog"));
        Assert.That(unlink["payload"]?["project_id"]?.ToString(), Is.EqualTo(Samples.ProjectId));
    }

    [Test]
    public void FourHundredProjectsAreOrderedTheSameWayEveryTime()
    {
        var states = new[] { "active", "inactive", "review_requested", "agent_running", "archived" };
        var projects = Enumerable.Range(0, 400)
            .Select(index => Catalog(
                "c" + index.ToString("000"),
                "Project " + (index % 37).ToString("00"),
                states[index % states.Length],
                fullName: "owner" + (index % 9) + "/repo-" + index,
                lastActivity: index % 7 == 0 ? null : $"2026-10-{1 + index % 28:00}T10:00:00Z"))
            .ToList();
        var first = Build(Halcyonic(), Read(projects));
        projects.Reverse();
        var again = Build(Halcyonic(), Read(projects));
        Assert.That(first.Items, Has.Count.EqualTo(400));
        Assert.That(again.Items.Select(item => item.Key), Is.EqualTo(first.Items.Select(item => item.Key)));
        Assert.That(first.Items.Select(item => item.Section), Is.Ordered);
    }
}

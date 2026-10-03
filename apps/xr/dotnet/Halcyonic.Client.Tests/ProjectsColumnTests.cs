using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class ProjectsColumnTests
{
    private static readonly CommandFactory Commands = new(new ClientInfo { Name = "halcyonic-xr", Version = "test", DeviceLabel = "Quest" });
    private const string ProjectA = "0192a7a0-0000-7000-8000-00000000000a";

    /// <summary>A director that records what a column asks of it.</summary>
    private sealed class Host : IMenuHost
    {
        public ClientProjection? State { get; set; } = Empty();
        public bool Connected { get; set; } = true;
        public bool Demonstration { get; set; }
        public double Now => 0;
        public DateTimeOffset Clock { get; set; } = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        public TimeZoneInfo Zone => TimeZoneInfo.Utc;
        public TextSize TextSize { get; set; }
        public bool VoiceOffered => false;
        public ControlPlaneApi? Api => null;
        public List<CommandEnvelope> Sent { get; } = new();
        public List<(string? Project, string? Name)> Opened { get; } = new();
        public TaskCompletionSource<CommandAckMessage>? Ack { get; set; }
        public bool NoSession { get; set; }

        public Task<CommandAckMessage>? Submit(CommandEnvelope command)
        {
            if (NoSession) return null;
            Sent.Add(command);
            Ack = new TaskCompletionSource<CommandAckMessage>();
            return Ack.Task;
        }

        public void OpenKeyboard(string text, string prompt, Action<string> done) => throw new InvalidOperationException("Projects types nothing.");
        public int RowsOf(string words, float columnDegrees) => 1;
        public int RowsOf(PageLine line, float columnDegrees) => line.Rows;
        public bool FitsHalf(PageLine answer, float columnDegrees) => false;
        public int TitleRows(string subject, float columnDegrees) => 1;
        public int PageRows(bool sourceLine) => MenuFrame.RowsAPage(TextSize, sourceLine);
        public float PageHeight(int subjectRows, bool besideMenu) => 1f;
        public void OpenFile(string workstreamId) => throw new InvalidOperationException("Projects opens no file.");
        public void OpenNewProject(string? projectId, string? projectName) => Opened.Add((projectId, projectName));

        public static ClientProjection Empty()
        {
            var state = new ClientProjection();
            state.ApplySnapshot(Samples.Snapshot(1), new StateChanges());
            return state;
        }
    }

    private static LocationsResponse Listing(params string[] folders) => new()
    {
        Roots = new List<LocationRoot>
        {
            new()
            {
                Path = "/Users/person/Projects", Name = "Projects", Status = LocationRootStatus.Available, Repository = false,
                ChangedAt = "2026-09-01T00:00:00.000Z", UsedBy = new List<string>(), FoldersTruncated = false,
                Folders = folders.Select(name => new LocationFolder
                {
                    Name = name, Path = "/Users/person/Projects/" + name, Repository = true, ChangedAt = "2026-09-30T12:00:00.000Z", UsedBy = new List<string>(),
                }).ToList(),
            },
        },
    };

    private static (ProjectsColumn Column, Host Host, List<(string, bool)> Shown, int[] Reads) Open(LocationsResponse listing, WorkOverview? overview = null)
    {
        var host = new Host();
        var shown = new List<(string, bool)>();
        var reads = new int[1];
        // The stage gives the same overview until something changes, as the director's does.
        var stage = overview ?? WorkOverview.Of(new ClientProjection(), new StageVisibility(), _ => false);
        var column = new ProjectsColumn(host, Commands, () => stage,
            (project, show) => shown.Add((project, show)),
            _ =>
            {
                reads[0]++;
                return Task.FromResult(listing);
            });
        column.Tick();
        return (column, host, shown, reads);
    }

    private static string KeyOf(ProjectsColumn column, string name) =>
        column.Frame!.Lines.Single(line => line.Words == name).Key!;

    [Test]
    public void OpeningReadsTheFoldersOnceAndShowsThem()
    {
        var (column, _, _, reads) = Open(Listing("shop"));
        Assert.That(reads[0], Is.EqualTo(1), "read when the column opens, never on a timer");
        for (var tick = 0; tick < 5; tick++) column.Tick();
        Assert.That(reads[0], Is.EqualTo(1));
        Assert.That(column.Frame!.Lines.Select(line => line.Words), Does.Contain("shop"));
        Assert.That(column.Frame.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(ProjectsScreens.NewProject));
    }

    [Test]
    public void ConnectSendsOneProjectCreateThroughTheHostAndNeverASecond()
    {
        var (column, host, _, _) = Open(Listing("shop"));
        column.Act(ProjectsScreens.Connect, null);
        Assert.That(host.Sent, Is.Empty, "no folder chosen: the frame offers no Connect");

        column.Act(ProjectsScreens.ChooseFolder, KeyOf(column, "shop"));
        Assert.That(column.Frame!.Side, Is.Not.Null);
        column.Act(ProjectsScreens.Connect, null);
        Assert.That(host.Sent.Single(), Is.InstanceOf<ProjectCreateCommand>());
        column.Act(ProjectsScreens.Connect, null);
        column.Tick();
        column.Act(ProjectsScreens.Connect, null);
        Assert.That(host.Sent.Count, Is.EqualTo(1), "a second press after the first was sent does nothing");
    }

    [Test]
    public void ASessionThatIsGoneMeansTheConnectNeverLeftAndCanBeTriedAgain()
    {
        var (column, host, _, _) = Open(Listing("shop"));
        column.Act(ProjectsScreens.ChooseFolder, KeyOf(column, "shop"));
        host.NoSession = true;
        column.Act(ProjectsScreens.Connect, null);
        column.Tick();
        Assert.That(column.Frame!.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(ProjectsScreens.TryAgain));
        host.NoSession = false;
        column.Act(ProjectsScreens.TryAgain, null);
        Assert.That(host.Sent.Count, Is.EqualTo(1));
    }

    [Test]
    public void APressForARowNoLongerShownDoesNothing()
    {
        var (column, _, _, _) = Open(Listing("shop"));
        column.Act(ProjectsScreens.ChooseFolder, "/Users/person/Projects\u0000/gone");
        Assert.That(column.Frame!.Side, Is.Null);
        column.Act(ProjectsScreens.ChooseProject, "no-such-project");
        Assert.That(column.Frame!.Side, Is.Null);
    }

    [Test]
    public void NewProjectAndAddATaskOpenNewProjectBesideTheMenu()
    {
        var state = new Portfolio().Project("p", "Alpha").Work("w", "p", WorkstreamStatus.Running).Apply();
        var overview = WorkOverview.Of(state, new StageVisibility(), _ => true);
        var (column, host, shown, _) = Open(Listing("shop"), overview);
        column.Act(ProjectsScreens.NewProject, null);
        Assert.That(host.Opened.Single(), Is.EqualTo(((string?)null, (string?)null)));

        column.Act(ProjectsScreens.ChooseProject, "p");
        column.Act(ProjectsScreens.AddTask, null);
        Assert.That(host.Opened.Last(), Is.EqualTo(((string?)"p", (string?)"Alpha")));
        column.Act(ProjectsScreens.HideProject, null);
        Assert.That(shown.Single(), Is.EqualTo(("p", false)), "hiding changes the stage on this device and sends nothing");
        Assert.That(host.Sent, Is.Empty);

        host.Demonstration = true;
        column.Act(ProjectsScreens.AddTask, null);
        Assert.That(host.Opened.Count, Is.EqualTo(2), "the demonstration adds nothing");
    }

    [Test]
    public void AFolderAProjectIsBoundToInTheProjectionIsNotOffered()
    {
        var (column, host, _, _) = Open(Listing("shop", "notes"));
        var snapshot = Samples.Snapshot(2);
        snapshot.Projects = new List<ProjectView>
        {
            new()
            {
                ProjectId = ProjectA, Name = "notes", CreatedAt = Samples.Time, UpdatedAt = Samples.Time,
                Location = new ProjectLocation { Path = "/Users/person/Projects/notes", Name = "notes", Created = false },
            },
        };
        var state = new ClientProjection();
        state.ApplySnapshot(snapshot, new StateChanges());
        host.State = state;
        column.Tick();
        Assert.That(column.Frame!.Lines.Select(line => line.Words), Does.Not.Contain("notes"));
        Assert.That(column.Frame.Lines.Select(line => line.Words), Does.Contain("shop"));
    }

    [Test]
    public void CloseClosesTheColumnAndTheSidePanelsCloseOnlyTheSidePanel()
    {
        var (column, _, _, _) = Open(Listing("shop"));
        var closed = 0;
        column.Closed += () => closed++;
        column.Act(ProjectsScreens.ChooseFolder, KeyOf(column, "shop"));
        column.Act(SidePanel.Close, null);
        Assert.That((column.Frame!.Side, closed), Is.EqualTo(((SidePanel?)null, 0)));
        column.Act(Footer.Close, null);
        Assert.That(closed, Is.EqualTo(1));
    }

    [Test]
    public void ItDrawsAgainOnlyWhenWhatItShowsCouldChange()
    {
        var (column, host, _, _) = Open(Listing("shop"));
        var changes = 0;
        column.Changed += () => changes++;
        column.Tick();
        column.Tick();
        Assert.That(changes, Is.EqualTo(0), "nothing changed");
        host.Clock = host.Clock.AddMinutes(1);
        column.Tick();
        Assert.That(changes, Is.EqualTo(1), "a minute on, \"changed 5 minutes ago\" may read differently");
        host.Connected = false;
        column.Tick();
        Assert.That(changes, Is.EqualTo(2));
        Assert.That(column.Frame!.Lines.Select(line => line.Words), Does.Not.Contain("shop"), "away, no folder is offered");
    }
}

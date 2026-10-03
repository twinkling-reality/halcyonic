using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>What the menu keeps for the app's run on one journal, as its director holds it (ADR 0026).</summary>
[TestFixture]
public class MenuMemoryTests
{
    private static readonly CommandFactory Commands = new(new ClientInfo { Name = "halcyonic-xr", Version = "test", DeviceLabel = "Quest" });

    private static ClientProjection Journal(string id)
    {
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(1, journal: Samples.Journal(id)), new StateChanges());
        return state;
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

    /// <summary>Projects as the director opens it: made afresh, over the memory it keeps for the journal.</summary>
    private static ProjectsColumn Projects(FakeMenuHost host, MenuMemory memory)
    {
        var overview = WorkOverview.Of(new ClientProjection(), new StageVisibility(), _ => false);
        var column = new ProjectsColumn(host, Commands, memory.ProjectsFor(host.State?.Journal?.JournalId), () => overview, (_, _) => { },
            _ => Task.FromResult(Listing("shop", "notes")));
        column.Tick();
        return column;
    }

    private static string KeyOf(ProjectsColumn column, string name) => column.Frame!.Lines.Single(line => line.Words == name).Key!;

    [Test]
    public void AConnectWhoseOutcomeIsUnknownWhenTheSocketDropsIsNeverSentAgainAfterAReconnect()
    {
        var memory = new MenuMemory();
        var host = new FakeMenuHost { State = Journal("journal-1"), Ack = new TaskCompletionSource<CommandAckMessage>() };
        var first = Projects(host, memory);
        first.Act(ProjectsScreens.ChooseFolder, KeyOf(first, "shop"));
        first.Act(ProjectsScreens.Connect, null);
        Assert.That(host.Sent, Has.Count.EqualTo(1));

        // The socket drops with the Connect in flight: its outcome is unknown.
        host.Connected = false;
        host.Ack.SetException(new CommandOutcomeUnknownException(host.Sent[0].CommandId, "The socket closed."));
        first.Tick();
        first.Act(Footer.Close, null);

        // Reconnected to the same journal, Projects opens again, made afresh over the same memory.
        host.Connected = true;
        host.State = Journal("journal-1");
        host.Ack = new TaskCompletionSource<CommandAckMessage>();
        var again = Projects(host, memory);
        again.Act(ProjectsScreens.ChooseFolder, KeyOf(again, "shop"));
        again.Act(ProjectsScreens.Connect, null);
        again.Act(ProjectsScreens.ChooseFolder, KeyOf(again, "notes"));
        again.Act(ProjectsScreens.Connect, null);
        Assert.That(host.Sent, Has.Count.EqualTo(1), "no second project.create, for that folder or another, while the first may have run");
    }

    /// <summary>A column standing in for New project's flow.</summary>
    private sealed class Flow : IMenuColumn
    {
        public MenuFrame? Frame => null;
#pragma warning disable CS0067
        public event System.Action? Changed;
        public event System.Action? Closed;
#pragma warning restore CS0067
        public void Act(string id, string? key) { }
        public void Drawn(MenuFrame drawn, bool sidePanel) { }
        public void HoldStarted(string id) { }
        public void HoldEnded(string id, bool letGo) { }
        public void Heard(string text) { }
        public void Said(string words) { }
        public void Tick() { }
        public void FocusLeft() { }
    }

    [Test]
    public void NewProjectsFlowIsMadeOnceAndKeptAcrossReconnectsToTheSameJournal()
    {
        var memory = new MenuMemory();
        var made = 0;
        Flow Make()
        {
            made++;
            return new Flow();
        }
        Assert.That(memory.NewProject, Is.Null, "before New project first opens, nothing is kept or ticked");
        var flow = memory.NewProjectFor("journal-1", Make);
        Assert.That(memory.NewProjectFor("journal-1", Make), Is.SameAs(flow), "opened again, the same flow: its draft and its build");
        memory.Journal(null);
        Assert.That(memory.NewProjectFor(null, Make), Is.SameAs(flow), "a reconnect before its first snapshot keeps it");
        Assert.That((made, memory.NewProject), Is.EqualTo((1, (IMenuColumn)flow)));
    }

    [Test]
    public void AnotherJournalOrARepairingLetsNewProjectsFlowGoAndSaysSo()
    {
        var memory = new MenuMemory();
        var dropped = new System.Collections.Generic.List<IMenuColumn>();
        memory.NewProjectDropped += dropped.Add;
        var demonstration = memory.NewProjectFor("demonstration", () => new Flow());
        memory.Journal("live-journal");
        Assert.That(dropped, Is.EqualTo(new[] { demonstration }), "the demonstration giving way to a live session is another journal");
        Assert.That(memory.NewProject, Is.Null);
        var live = memory.NewProjectFor("live-journal", () => new Flow());
        Assert.That(live, Is.Not.SameAs(demonstration));
        memory.Forget();
        Assert.That(dropped, Is.EqualTo(new IMenuColumn[] { demonstration, live }), "a re-pairing lets it go too");
    }

    [Test]
    public void TheSameJournalKeepsItsMemoryAndAnotherJournalOrARepairingStartsAfresh()
    {
        var memory = new MenuMemory();
        var kept = memory.ProjectsFor("journal-1");
        Assert.That(memory.ProjectsFor("journal-1"), Is.SameAs(kept), "across reconnects to the same journal");
        Assert.That(memory.ProjectsFor(null), Is.SameAs(kept), "before the first snapshot after a reconnect, the journal is the one it was");
        var other = memory.ProjectsFor("journal-2");
        Assert.That(other, Is.Not.SameAs(kept), "another journal starts afresh");
        memory.Forget();
        Assert.That(memory.ProjectsFor("journal-2"), Is.Not.SameAs(other), "a re-pairing starts afresh");
    }
}

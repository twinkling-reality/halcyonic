using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>What the menu keeps for the app's run on one session and journal, as its director holds it (ADR 0026).</summary>
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

    private sealed class Kept : IKeptCommand
    {
        public string? Id { get; set; }
    }

    /// <summary>New project's flow over a host, keeping nothing on the device.</summary>
    private static NewProjectFlow Flow(FakeMenuHost? host = null) => new(host ?? new FakeMenuHost(), Commands, new Kept());

    [Test]
    public void NewProjectsFlowIsMadeOnceAndKeptAcrossReconnectsToTheSameJournal()
    {
        var memory = new MenuMemory();
        var made = 0;
        NewProjectFlow Make()
        {
            made++;
            return Flow();
        }
        Assert.That(memory.NewProject, Is.Null, "before New project first opens, nothing is kept or ticked");
        var flow = memory.NewProjectFor("journal-1", Make);
        Assert.That(memory.NewProjectFor("journal-1", Make), Is.SameAs(flow), "opened again, the same flow: its draft and its build");
        memory.Journal(null);
        Assert.That(memory.NewProjectFor(null, Make), Is.SameAs(flow), "a reconnect before its first snapshot keeps it");
        Assert.That((made, memory.NewProject), Is.EqualTo((1, flow)));
        Assert.That(new MenuMemory().NewProjectFor("journal-1", () => null), Is.Null, "none can be made, as without the host's commands");
    }

    [Test]
    public void AnotherJournalOrARepairingLetsNewProjectsFlowGoAndSaysSo()
    {
        var memory = new MenuMemory();
        var dropped = new List<NewProjectFlow>();
        memory.NewProjectDropped += dropped.Add;
        var demonstration = memory.NewProjectFor("demonstration", () => Flow());
        memory.Journal("live-journal");
        Assert.That(dropped, Is.EqualTo(new[] { demonstration }), "the demonstration giving way to a live session is another journal");
        Assert.That(memory.NewProject, Is.Null);
        var live = memory.NewProjectFor("live-journal", () => Flow());
        Assert.That(live, Is.Not.SameAs(demonstration));
        memory.Forget();
        Assert.That(dropped, Is.EqualTo(new[] { demonstration, live }), "a re-pairing lets it go too");
    }

    private static RealtimeSession Session() =>
        new(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "not-a-token", new ClientInfo { Name = "halcyonic-xr", Version = "test", DeviceLabel = "Quest" }));

    [Test]
    public void AnotherSessionLetsGoOfWhatWasKeptEvenOnAJournalOfTheSameIdAndAReconnectDoesNot()
    {
        var memory = new MenuMemory();
        var demonstration = Session();
        var live = Session();
        var dropped = new List<NewProjectFlow>();
        memory.NewProjectDropped += dropped.Add;
        Assert.That(memory.Session(demonstration), Is.False, "the first session shown changes nothing");
        var flow = memory.NewProjectFor("journal", () => Flow());
        var projects = memory.ProjectsFor("journal");
        Assert.That(memory.Session(demonstration), Is.False, "the same session, as across a reconnect");
        Assert.That((memory.NewProject, memory.ProjectsFor("journal")), Is.EqualTo((flow, projects)));

        Assert.That(memory.Session(live), Is.True, "the computer's live session takes the demonstration's place");
        Assert.That(dropped, Is.EqualTo(new[] { flow }), "the demonstration's flow ticks and sends no more");
        Assert.That(memory.NewProject, Is.Null);
        Assert.That(memory.ProjectsFor("journal"), Is.Not.SameAs(projects), "even on a journal of the same id");
        Assert.That(memory.Journal("journal"), Is.False);
        Assert.That(memory.Journal("another"), Is.True, "another journal says it let go too");
    }

    [Test]
    public void AKeptFlowThatSaysItIsForAnotherSessionIsMadeAfreshEvenWhereTheMemorySawNoChange()
    {
        var memory = new MenuMemory();
        var dropped = new List<NewProjectFlow>();
        memory.NewProjectDropped += dropped.Add;
        var host = new FakeMenuHost { Demonstration = true };
        var demonstration = memory.NewProjectFor("journal", () => Flow(host));
        // A real session took the demonstration's place on a journal of the same id, unseen by the memory.
        host.Demonstration = false;
        var live = memory.NewProjectFor("journal", () => Flow(host));
        Assert.That(live, Is.Not.SameAs(demonstration));
        Assert.That(dropped, Is.EqualTo(new[] { demonstration }), "let go, so what showed it takes it off the plane");
        Assert.That(live!.ForAnotherSession, Is.False);
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

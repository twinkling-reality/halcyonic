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

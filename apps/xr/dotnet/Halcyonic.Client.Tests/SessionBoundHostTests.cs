using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>A column's host bound to the session it was made in: nothing made in one session sends to another (ADR 0026, ADR 0012).</summary>
[TestFixture]
public class SessionBoundHostTests
{
    private static readonly ClientInfo Client = new() { Name = "halcyonic-xr", Version = "test", DeviceLabel = "Quest" };
    private static readonly CommandFactory Commands = new(Client);

    private static RealtimeSession Session(string? journal = null)
    {
        var session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "not-a-token", Client));
        if (journal != null) session.State.ApplySnapshot(Samples.Snapshot(1, journal: Samples.Journal(journal)), new StateChanges());
        return session;
    }

    /// <summary>The host's side: the session shown now, and every send as it reached the host's submissions, with the session it was for.</summary>
    private sealed class Sessions
    {
        public RealtimeSession? Shown { get; set; }

        public List<(RealtimeSession Session, CommandEnvelope Command)> Sent { get; } = new();

        public TaskCompletionSource<CommandAckMessage> Ack { get; private set; } = new();

        public SessionBoundHost Bind(IMenuHost director) => new(director, () => Shown, (session, command) =>
        {
            Sent.Add((session, command));
            Ack = new TaskCompletionSource<CommandAckMessage>();
            return Ack.Task;
        });
    }

    /// <summary>A build as New project runs one: its first command on a press, the next once the first is acknowledged.</summary>
    private sealed class Build : IMenuColumn
    {
        private readonly IMenuHost host;
        private Task<CommandAckMessage>? first;
        private bool next;

        public Build(IMenuHost host) => this.host = host;

        public Task<CommandAckMessage>? Second { get; private set; }

        public MenuFrame? Frame => null;

#pragma warning disable CS0067
        public event Action? Changed;

        public event Action? Closed;
#pragma warning restore CS0067

        public void Act(string id, string? key) => first = host.Submit(Commands.CreateProject("Shop"));

        public void Tick()
        {
            if (next || first?.IsCompleted != true) return;
            next = true;
            Second = host.Submit(Commands.SendInstruction("execution-1", "Begin"));
        }

        public void Drawn(MenuFrame drawn, bool sidePanel)
        {
        }

        public void HoldStarted(string id)
        {
        }

        public void HoldEnded(string id, bool letGo)
        {
        }

        public void Heard(string text)
        {
        }

        public void Said(string words)
        {
        }

        public void FocusLeft()
        {
        }
    }

    private static CommandAckMessage Accepted(CommandEnvelope command) => new() { CommandId = command.CommandId, Disposition = CommandAckDisposition.Accepted };

    [Test]
    public void AConnectArmedOnOneSessionSendsNothingOnceAnotherShows()
    {
        var sessions = new Sessions { Shown = Session("journal-1") };
        var director = new FakeMenuHost { State = sessions.Shown.State };
        var overview = WorkOverview.Of(new ClientProjection(), new StageVisibility(), _ => false);
        var projects = new ProjectsColumn(sessions.Bind(director), Commands, new ProjectsMemory(), () => overview, (_, _) => { },
            _ => Task.FromResult(new LocationsResponse
            {
                Roots = new List<LocationRoot>
                {
                    new()
                    {
                        Path = "/Users/person/Projects", Name = "Projects", Status = LocationRootStatus.Available, Repository = false,
                        ChangedAt = "2026-09-01T00:00:00.000Z", UsedBy = new List<string>(), FoldersTruncated = false,
                        Folders = new List<LocationFolder>
                        {
                            new() { Name = "shop", Path = "/Users/person/Projects/shop", Repository = true, ChangedAt = "2026-09-30T12:00:00.000Z", UsedBy = new List<string>() },
                        },
                    },
                },
            }));
        projects.Tick();
        projects.Act(ProjectsScreens.ChooseFolder, projects.Frame!.Lines.Single(line => line.Words == "shop").Key);
        Assert.That(projects.Frame!.Side, Is.Not.Null, "armed: the folder's Connect waits for its press");

        // The headset is paired again: another session shows in the same frame as the press.
        sessions.Shown = Session("journal-1");
        director.State = sessions.Shown.State;
        projects.Act(ProjectsScreens.Connect, null);
        projects.Tick();
        Assert.That(sessions.Sent, Is.Empty, "a column made on one session never reaches another, even on a journal of the same id");
        Assert.That(director.Sent, Is.Empty, "nor the director, which sends nothing itself");
    }

    [Test]
    public void ABuildBegunInTheDemonstrationSendsNothingMoreOnceTheLiveSessionShows()
    {
        var demonstration = Session("recorded");
        var sessions = new Sessions { Shown = demonstration };
        var build = new Build(sessions.Bind(new FakeMenuHost { Demonstration = true }));
        build.Act("build", null);
        Assert.That(sessions.Sent.Single().Session, Is.SameAs(demonstration), "sent to the demonstration, which answers as a live one would");

        sessions.Shown = Session("live");
        sessions.Ack.SetResult(Accepted(sessions.Sent[0].Command));
        build.Tick();
        Assert.That(build.Second, Is.Null, "refused: the next step never left");
        Assert.That(sessions.Sent, Has.Count.EqualTo(1));
    }

    [Test]
    public void AReconnectKeepsTheSameSessionAndSoSendsGoOn()
    {
        var live = Session("journal-1");
        var sessions = new Sessions { Shown = live };
        var build = new Build(sessions.Bind(new FakeMenuHost()));
        build.Act("build", null);
        // The socket dropped and came back: the same session object, the same journal.
        sessions.Ack.SetResult(Accepted(sessions.Sent[0].Command));
        build.Tick();
        Assert.That(sessions.Sent.Select(sent => sent.Session), Is.EqualTo(new[] { live, live }));
    }

    [Test]
    public void AnotherJournalOnTheSameSessionOrNoSessionSendsNothing()
    {
        var live = Session("journal-1");
        var sessions = new Sessions { Shown = live };
        var host = sessions.Bind(new FakeMenuHost());
        Assert.That((host.Current, host.Connected), Is.EqualTo((true, true)));
        live.State.ApplySnapshot(Samples.Snapshot(2, journal: Samples.Journal("journal-2")), new StateChanges());
        Assert.That(host.Submit(Commands.SendInstruction("execution-1", "Begin")), Is.Null);
        Assert.That((host.Current, host.Connected, host.Api, host.VoiceOffered), Is.EqualTo((false, false, (ControlPlaneApi?)null, false)), "it reads as away");

        var none = new Sessions().Bind(new FakeMenuHost());
        Assert.That(none.Submit(Commands.SendInstruction("execution-1", "Begin")), Is.Null, "made with no session, it never sends");

        var early = Session();
        var beforeSnapshot = new Sessions { Shown = early }.Bind(new FakeMenuHost());
        early.State.ApplySnapshot(Samples.Snapshot(1, journal: Samples.Journal("journal-1")), new StateChanges());
        Assert.That(beforeSnapshot.Current, Is.True, "made before the first snapshot, it belongs to the first journal shown");
        early.State.ApplySnapshot(Samples.Snapshot(2, journal: Samples.Journal("journal-2")), new StateChanges());
        Assert.That(beforeSnapshot.Current, Is.False);
        Assert.That(sessions.Sent, Is.Empty);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// One web-preview relay per open project screen (the limits audit's #9, the lead's 2026-11-17 19:17Z): several projects'
/// live previews stand at once, each served on its own port; closing one screen stops only its own; a Play with no screen open
/// keeps the one-relay rule, ending when Halora comes back to the front; a change of computer stops them all.
/// </summary>
public class PreviewRelaysTests
{
    private static readonly ControlPlaneTarget Mac = ControlPlaneTarget.Local(new Uri("http://127.0.0.1:47800"), "token-a");
    private static readonly ControlPlaneTarget OtherMac = ControlPlaneTarget.Local(new Uri("http://127.0.0.1:47801"), "token-b");

    private sealed class Started
    {
        public readonly List<string> Projects = new();
        public readonly List<string> Stopped = new();
        private int next = 41000;

        public PreviewRelays.Relayed Start(ControlPlaneTarget target, string project)
        {
            Projects.Add(project);
            var port = next++;
            return new PreviewRelays.Relayed(port, new Stop(() => Stopped.Add(project)));
        }

        private sealed class Stop : IDisposable
        {
            private readonly Action stop;
            public Stop(Action stop) => this.stop = stop;
            public void Dispose() => stop();
        }
    }

    [Test]
    public void ThreeOpenScreensEachKeepTheirOwnLivePreview()
    {
        var started = new Started();
        var relays = new PreviewRelays(started.Start);
        var open = new[] { "a", "b", "c" };
        var ports = open.Select(project => relays.PortFor(Mac, project, open)).ToList();
        Assert.That(ports, Is.All.Not.Null);
        Assert.That(ports.Distinct().Count(), Is.EqualTo(3), "each preview on its own port");
        Assert.That(started.Stopped, Is.Empty, "opening another never stops the first (#9)");
        Assert.That(relays.PortFor(Mac, "a", open), Is.EqualTo(ports[0]), "the same project's relay is reused, not started again");
        Assert.That(started.Projects, Is.EqualTo(open));
    }

    [Test]
    public void ClosingOneScreenStopsOnlyItsOwnRelay()
    {
        var started = new Started();
        var relays = new PreviewRelays(started.Start);
        var open = new List<string> { "a", "b", "c" };
        foreach (var project in open) relays.PortFor(Mac, project, open);
        open.Remove("b");
        relays.Keep(open);
        Assert.That(started.Stopped, Is.EqualTo(new[] { "b" }));
        Assert.That(relays.Projects, Is.EquivalentTo(new[] { "a", "c" }), "open A, B and C, close B: A and C stay");
    }

    [Test]
    public void ComingBackToTheFrontEndsOnlyAPlayWithNoScreenOpen()
    {
        var started = new Started();
        var relays = new PreviewRelays(started.Start);
        var open = new[] { "a" };
        relays.PortFor(Mac, "a", open);
        relays.PortFor(Mac, "loose", open);
        relays.Paused(true, open);
        relays.Paused(false, open);
        Assert.That(started.Stopped, Is.EqualTo(new[] { "loose" }), "the browser left a screenless Play; an open screen's preview stays live");
        Assert.That(relays.Projects, Is.EqualTo(new[] { "a" }));
        relays.Paused(false, open);
        Assert.That(started.Stopped, Has.Count.EqualTo(1), "back in front without having gone away stops nothing");
    }

    [Test]
    public void ANewScreenlessPlayTakesThePlaceOfTheLastOne()
    {
        var started = new Started();
        var relays = new PreviewRelays(started.Start);
        var none = Array.Empty<string>();
        relays.PortFor(Mac, "x", none);
        relays.PortFor(Mac, "y", none);
        Assert.That(started.Stopped, Is.EqualTo(new[] { "x" }), "with no screen open the one-relay rule holds as before");
        Assert.That(relays.Projects, Is.EqualTo(new[] { "y" }));
    }

    [Test]
    public void AnotherComputerStopsEveryRelay()
    {
        var started = new Started();
        var relays = new PreviewRelays(started.Start);
        var open = new[] { "a", "b" };
        relays.PortFor(Mac, "a", open);
        relays.PortFor(Mac, "b", open);
        relays.PortFor(OtherMac, "a", open);
        Assert.That(started.Stopped, Is.EquivalentTo(new[] { "a", "b" }), "no relay outlives the computer it was opened for");
        Assert.That(relays.Projects, Is.EqualTo(new[] { "a" }));
    }

    [Test]
    public void NeverMoreThanTheOpenScreensAndOnePlay()
    {
        var started = new Started();
        var relays = new PreviewRelays(started.Start);
        var open = new[] { "a", "b", "c" };
        foreach (var project in open) relays.PortFor(Mac, project, open);
        relays.PortFor(Mac, "loose", open);
        Assert.That(relays.Projects, Has.Count.EqualTo(WorkspacePresentationState.ScreensMost + 1));
        relays.Keep(Array.Empty<string>());
        Assert.That(relays.Projects, Is.EqualTo(new[] { "loose" }), "screens closed, their relays stop; the Play's waits for the front");
    }

    [Test]
    public void ARelayThatCannotStartServesNothingAndHoldsNothing()
    {
        var relays = new PreviewRelays((_, _) => throw new InvalidOperationException("no port"));
        Assert.That(relays.PortFor(Mac, "a", new[] { "a" }), Is.Null);
        Assert.That(relays.Any, Is.False);
        relays.StopAll();
        Assert.That(relays.Any, Is.False);
    }
}

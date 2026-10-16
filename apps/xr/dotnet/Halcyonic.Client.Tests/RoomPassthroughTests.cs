using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

[TestFixture]
public class RoomPassthroughTests
{
    /// <summary>A headset that records, in order, what passthrough and the boundary were asked to do.</summary>
    private sealed class FakeDevice : IPassthroughDevice
    {
        public readonly List<string> Calls = new();
        public string? Problem;
        public bool Initialized { get; set; }
        public bool InitFailed { get; set; }
        public bool LayerShown { get; private set; }
        public BoundaryAnswer Answer = BoundaryAnswer.Done;

        public string? CannotStart() => Problem;

        public void TurnOn()
        {
            LayerShown = true;
            Calls.Add("on");
        }

        public void TurnOff()
        {
            LayerShown = false;
            Calls.Add("off");
        }

        public BoundaryAnswer AskBoundary(bool hidden)
        {
            Calls.Add(hidden ? "ask hidden" : "ask shown");
            return Answer;
        }
    }

    private static (RoomPassthrough Passthrough, FakeDevice Device, List<string> Log) Running(bool offered = true)
    {
        var device = new FakeDevice();
        var log = new List<string>();
        var passthrough = new RoomPassthrough(device, log.Add, offered);
        passthrough.Start(0f);
        device.Initialized = true;
        passthrough.Poll(0.5f);
        Assert.That(passthrough.State, Is.EqualTo(PassthroughState.Running));
        device.Calls.Clear();
        return (passthrough, device, log);
    }

    private static (RoomPassthrough Passthrough, FakeDevice Device, List<string> Log) Hidden()
    {
        var running = Running();
        running.Passthrough.ChooseBoundaryHidden(true);
        running.Passthrough.Poll(1f);
        Assert.That(running.Device.Calls, Is.EqualTo(new[] { "ask hidden" }));
        running.Device.Calls.Clear();
        return running;
    }

    [Test]
    public void PassthroughStartsRunsAndStopsAsItDidInTheView()
    {
        var device = new FakeDevice();
        var passthrough = new RoomPassthrough(device, _ => { }, boundaryOffered: false);
        passthrough.Start(0f);
        Assert.That((passthrough.State, device.Calls.Single()), Is.EqualTo((PassthroughState.Starting, "on")));
        Assert.That(passthrough.Poll(1f), Is.EqualTo(PassthroughState.Starting));
        device.Initialized = true;
        Assert.That(passthrough.Poll(2f), Is.EqualTo(PassthroughState.Running));
        passthrough.Stop();
        Assert.That((passthrough.State, device.Calls.Last()), Is.EqualTo((PassthroughState.Off, "off")));

        var slow = new FakeDevice();
        var timedOut = new RoomPassthrough(slow, _ => { }, boundaryOffered: false);
        timedOut.Start(0f);
        Assert.That(timedOut.Poll(RoomPassthrough.StartSeconds + 0.1f), Is.EqualTo(PassthroughState.Unavailable));
        Assert.That((timedOut.Problem, slow.Calls.Last()), Is.EqualTo(("it did not start within 6 seconds", "off")));
        timedOut.Start(10f);
        Assert.That(timedOut.State, Is.EqualTo(PassthroughState.Unavailable), "unavailable stays unavailable");

        var unsupported = new FakeDevice { Problem = "this headset does not support it" };
        var never = new RoomPassthrough(unsupported, _ => { }, boundaryOffered: false);
        never.Start(0f);
        Assert.That((never.State, never.Problem), Is.EqualTo((PassthroughState.Unavailable, "this headset does not support it")));
        Assert.That(unsupported.Calls, Does.Not.Contain("on"));

        var (failing, failingDevice, _) = Running(offered: false);
        failingDevice.InitFailed = true;
        Assert.That((failing.Poll(3f), failing.Problem), Is.EqualTo((PassthroughState.Unavailable, "it stopped working")));
    }

    [Test]
    public void TheBoundaryIsNeverAskedHiddenUnlessThePersonChoseItWhileTheRoomRuns()
    {
        var (unchosen, device, _) = Running();
        unchosen.Poll(1f);
        Assert.That(device.Calls, Is.Empty, "off by default");

        var (release, releaseDevice, _) = Running(offered: false);
        release.ChooseBoundaryHidden(true);
        release.Poll(1f);
        Assert.That(releaseDevice.Calls, Is.Empty, "a build that doesn't offer it never asks");
        Assert.That(release.Boundary, Is.Null);

        var starting = new FakeDevice();
        var early = new RoomPassthrough(starting, _ => { }, boundaryOffered: true);
        early.Start(0f);
        early.ChooseBoundaryHidden(true);
        early.Poll(0.1f);
        Assert.That(starting.Calls, Does.Not.Contain("ask hidden"), "not while passthrough is only starting");

        var (paused, pausedDevice, _) = Running();
        paused.Paused(true);
        paused.ChooseBoundaryHidden(true);
        paused.Poll(1f);
        Assert.That(pausedDevice.Calls, Does.Not.Contain("ask hidden"), "not while paused");
        paused.Paused(false);
        paused.Focused(false);
        paused.Poll(2f);
        Assert.That(pausedDevice.Calls, Does.Not.Contain("ask hidden"), "not without focus");
        paused.Focused(true);
        paused.Poll(3f);
        Assert.That(pausedDevice.Calls.Count(call => call == "ask hidden"), Is.EqualTo(1), "once every condition holds, asked once");
        paused.Poll(4f);
        Assert.That(pausedDevice.Calls.Count(call => call == "ask hidden"), Is.EqualTo(1), "and not asked again each frame");
    }

    private static readonly (string Exit, Action<RoomPassthrough, FakeDevice> Take, bool TurnsOff)[] Exits =
    {
        ("stop, as for the virtual space or the scene's teardown", (passthrough, _) => passthrough.Stop(), true),
        ("passthrough stopped working", (passthrough, device) => { device.InitFailed = true; passthrough.Poll(5f); }, true),
        ("pause", (passthrough, _) => passthrough.Paused(true), false),
        ("focus loss", (passthrough, _) => passthrough.Focused(false), false),
        ("quit", (passthrough, _) => passthrough.Quitting(), false),
        ("Show boundary", (passthrough, _) => passthrough.ChooseBoundaryHidden(false), false),
    };

    private static IEnumerable<TestCaseData> ExitCases() => Exits.Select(exit => new TestCaseData(exit.Exit).SetName("EveryExitAsksForTheBoundaryFirst: " + exit.Exit));

    [TestCaseSource(nameof(ExitCases))]
    public void EveryExitAsksForTheBoundaryBeforePassthroughGoesOff(string name)
    {
        var exit = Exits.Single(candidate => candidate.Exit == name);
        var (passthrough, device, _) = Hidden();
        exit.Take(passthrough, device);
        passthrough.Poll(6f);
        Assert.That(device.Calls, Is.EqualTo(exit.TurnsOff ? new[] { "ask shown", "off" } : new[] { "ask shown" }),
            "the boundary is asked back directly, and only then does passthrough go off");
    }

    [Test]
    public void AfterAPauseOrFocusLossTheBoundaryIsAskedHiddenAgainOnlyOnceEverythingHolds()
    {
        var (passthrough, device, _) = Hidden();
        passthrough.Paused(true);
        passthrough.Paused(false);
        passthrough.Focused(false);
        passthrough.Poll(2f);
        Assert.That(device.Calls, Is.EqualTo(new[] { "ask shown" }));
        passthrough.Focused(true);
        passthrough.Poll(3f);
        Assert.That(device.Calls, Is.EqualTo(new[] { "ask shown", "ask hidden" }));
    }

    [Test]
    public void StoppingTheRoomClearsTheChoiceForTheRestOfTheSession()
    {
        var (passthrough, device, _) = Hidden();
        passthrough.Stop();
        passthrough.Start(10f);
        passthrough.Poll(11f);
        Assert.That(device.Calls, Is.EqualTo(new[] { "ask shown", "off", "on" }), "the room shows again with the boundary on");
        Assert.That(passthrough.Boundary!.Step, Is.EqualTo(BoundaryStep.Off));
    }

    [Test]
    public void HiddenIsSaidOnlyOnceTheHeadsetReportsItAndTheNoticeComesOncePerSession()
    {
        var (passthrough, _, log) = Hidden();
        Assert.That(passthrough.Boundary!.Step, Is.EqualTo(BoundaryStep.Asked), "asked is not hidden");
        Assert.That(passthrough.HeardBoundary(true), Is.True, "the first report of it hidden brings the notice");
        Assert.That(passthrough.Boundary!.Step, Is.EqualTo(BoundaryStep.Hidden));
        Assert.That(passthrough.HeardBoundary(false), Is.False);
        Assert.That(passthrough.HeardBoundary(true), Is.False, "once a session");
        Assert.That(log, Has.Some.Contains("asked the headset to hide its boundary"));
        Assert.That(log, Has.Some.Contains("the headset says its boundary is hidden"));
    }

    [Test]
    public void ARefusalIsSaidAndAskedBackOnExit()
    {
        var (passthrough, device, log) = Running();
        device.Answer = BoundaryAnswer.NotAllowed;
        passthrough.ChooseBoundaryHidden(true);
        passthrough.Poll(1f);
        Assert.That(passthrough.Boundary!.Step, Is.EqualTo(BoundaryStep.KeptOn));
        Assert.That(log, Has.Some.Contains("NotAllowed"));
        device.Calls.Clear();
        passthrough.Stop();
        Assert.That(device.Calls, Is.EqualTo(new[] { "ask shown", "off" }));
    }

    [Test]
    public void ABoundaryHiddenWhileHalcyonicIsNotAskingIsAskedBack()
    {
        var (passthrough, device, _) = Running();
        passthrough.HeardBoundary(true);
        Assert.That(device.Calls, Is.EqualTo(new[] { "ask shown" }));
    }

    [Test]
    public void TheRowCanAskOnlyWhileTheRoomRuns()
    {
        var device = new FakeDevice();
        var passthrough = new RoomPassthrough(device, _ => { }, boundaryOffered: true);
        Assert.That(passthrough.Boundary!.RoomShows, Is.False);
        passthrough.Start(0f);
        device.Initialized = true;
        passthrough.Poll(1f);
        Assert.That(passthrough.Boundary!.RoomShows, Is.True);
    }
}

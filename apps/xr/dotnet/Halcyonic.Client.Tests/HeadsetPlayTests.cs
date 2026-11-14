using System;
using System.Collections.Generic;
using Halcyonic.Contracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// Play in Headset (gap 1): offered only on the host's own facts (a current, passed, confined check and its running
/// preview), at a loopback address that differs by the way the headset reaches the computer, and refused in the
/// product's words where the check is not confined or no way reaches the headset.
/// </summary>
public class HeadsetPlayTests
{
    internal const string ProjectId = "01a19000-0000-7000-8000-000000000001";
    private const string CheckId = "01a19000-0000-7000-8000-0000000000c1";
    private const string OlderCheckId = "01a19000-0000-7000-8000-0000000000c0";

    internal static readonly ControlPlaneTarget Cable =
        ControlPlaneTarget.Local(new Uri("ws://127.0.0.1:47800/realtime"), "a-test-access-token");

    internal static readonly ControlPlaneTarget Paired = ControlPlaneTarget.Paired(new PairedControlPlane(
        "192.168.1.23", 47801, new string('c', 64), "01a0f0a0-0000-7000-8000-000000000001", "hlcd_" + new string('A', 43)));

    internal static ProjectView View(JObject? check = null, JObject? preview = null)
    {
        var json = new JObject
        {
            ["project_id"] = ProjectId,
            ["name"] = "Night Sky",
            ["location"] = null,
            ["starter"] = "iwsdk-vr",
            ["created_at"] = "2026-11-10T18:00:00.000Z",
            ["updated_at"] = "2026-11-10T18:00:00.000Z",
        };
        if (check != null) json["check"] = check;
        if (preview != null) json["preview"] = preview;
        return HalcyonicJson.Deserialize<ProjectView>(json.ToString(), strict: true);
    }

    internal static JObject Check(bool passed = true, bool readsConfined = true, string files = "current", string checkId = CheckId) =>
        new()
        {
            ["check_id"] = checkId,
            ["recorded_at"] = "2026-11-10T18:10:00.000Z",
            ["position"] = 4,
            ["files"] = files,
            ["findings"] = new JObject
            {
                ["tsc"] = new JObject { ["exit"] = 0, ["seconds"] = 1 },
                ["build"] = new JObject { ["exit"] = 0, ["seconds"] = 2 },
                ["page"] = null,
                ["unknown"] = null,
                ["passed"] = passed,
                ["reads_confined"] = readsConfined,
            },
        };

    internal static JObject Preview(string checkId = CheckId, long port = 47930) =>
        new() { ["check_id"] = checkId, ["port"] = port, ["files"] = "current" };

    [Test]
    public void OverTheCableItOpensThePreviewsOwnPortOnLoopback()
    {
        var project = View(Check(), Preview(port: 47931));
        Assert.That(HeadsetPlay.For(project, Cable, demonstration: false), Is.EqualTo(PlayInHeadset.Offered));
        Assert.That(HeadsetPlay.Stands(PlayInHeadset.Offered), Is.True);
        var address = HeadsetPlay.AddressOf(project, Cable, demonstration: false);
        Assert.That(address, Is.EqualTo(new Uri("http://127.0.0.1:47931/")));
        Assert.That(HeadsetPlay.IsOpenable(address), Is.True);
        Assert.That(HeadsetPlay.CommandFor(project, Cable, demonstration: false, new CommandFactory(Samples.Client)), Is.Null, "an offered press opens, it sends nothing");
    }

    [Test]
    public void OverThePairingItOpensTheRelaysPortAndWithoutARelayAsksForTheCable()
    {
        var project = View(Check(), Preview(port: 47931));
        Assert.That(HeadsetPlay.For(project, Paired, demonstration: false), Is.EqualTo(PlayInHeadset.NoWayToHeadset));
        Assert.That(HeadsetPlay.Refusal(PlayInHeadset.NoWayToHeadset), Is.EqualTo("Connect by Cable"));
        Assert.That(HeadsetPlay.AddressOf(project, Paired, demonstration: false), Is.Null);
        Assert.That(HeadsetPlay.For(project, Paired, demonstration: false, relayPort: 47935), Is.EqualTo(PlayInHeadset.Offered));
        Assert.That(HeadsetPlay.AddressOf(project, Paired, demonstration: false, relayPort: 47935), Is.EqualTo(new Uri("http://127.0.0.1:47935/")));
    }

    [Test]
    public void ACheckThatDidNotConfineItsReadsKeepsThePreviewOnTheComputer()
    {
        var project = View(Check(readsConfined: false), Preview());
        Assert.That(HeadsetPlay.For(project, Cable, demonstration: false), Is.EqualTo(PlayInHeadset.NotConfined));
        Assert.That(HeadsetPlay.Stands(PlayInHeadset.NotConfined), Is.False);
        Assert.That(HeadsetPlay.Refusal(PlayInHeadset.NotConfined), Is.EqualTo("Preview on Your Computer"));
        Assert.That(HeadsetPlay.AddressOf(project, Cable, demonstration: false), Is.Null);
        Assert.That(HeadsetPlay.CommandFor(project, Cable, demonstration: false, new CommandFactory(Samples.Client)), Is.Null);
    }

    [Test]
    public void WithoutAPreviewOfTheLatestCheckAPressStartsOne()
    {
        foreach (var project in new[] { View(Check()), View(Check(), Preview(checkId: OlderCheckId)) })
        {
            Assert.That(HeadsetPlay.For(project, Cable, demonstration: false), Is.EqualTo(PlayInHeadset.NoPreviewOfCheck));
            Assert.That(HeadsetPlay.Stands(PlayInHeadset.NoPreviewOfCheck), Is.True);
            Assert.That(HeadsetPlay.AddressOf(project, Cable, demonstration: false), Is.Null);
            var command = HeadsetPlay.CommandFor(project, Cable, demonstration: false, new CommandFactory(Samples.Client));
            Assert.That(command, Is.TypeOf<ProjectPreviewStartCommand>());
            Assert.That(((ProjectPreviewStartCommand)command!).Payload.ProjectId, Is.EqualTo(ProjectId));
        }
    }

    [Test]
    public void EveryOtherFactHidesTheActionWithoutWords()
    {
        var cases = new (ProjectView Project, ControlPlaneTarget? Target, PlayInHeadset Expected)[]
        {
            (View(), Cable, PlayInHeadset.NoCheck),
            (View(Check(files: "changed"), Preview()), Cable, PlayInHeadset.FilesNotChecked),
            (View(Check(files: "unknown"), Preview()), Cable, PlayInHeadset.FilesNotChecked),
            (View(Check(passed: false), Preview()), Cable, PlayInHeadset.CheckNotPassed),
            (View(Check(), Preview()), null, PlayInHeadset.NoControlPlane),
        };
        foreach (var (project, target, expected) in cases)
        {
            var play = HeadsetPlay.For(project, target, demonstration: false);
            Assert.That(play, Is.EqualTo(expected));
            Assert.That(HeadsetPlay.Stands(play), Is.False);
            Assert.That(HeadsetPlay.Refusal(play), Is.Null);
            Assert.That(HeadsetPlay.AddressOf(project, target, demonstration: false), Is.Null);
        }
    }

    [Test]
    public void NothingOfADemonstrationOpens()
    {
        // AUDIT's case: the recorded run's projects carry a preview port too; over any demonstration (recorded, or for a
        // computer that cannot be reached) Play in Headset is neither offered nor started, and a wait it began ends.
        var project = View(Check(), Preview(port: 47931));
        var factory = new CommandFactory(Samples.Client);
        Assert.That(HeadsetPlay.For(project, Cable, demonstration: true), Is.EqualTo(PlayInHeadset.Demonstration));
        Assert.That(HeadsetPlay.Stands(PlayInHeadset.Demonstration), Is.False);
        Assert.That(HeadsetPlay.Refusal(PlayInHeadset.Demonstration), Is.Null);
        Assert.That(HeadsetPlay.AddressOf(project, Cable, demonstration: true), Is.Null);
        Assert.That(HeadsetPlay.For(View(Check()), Cable, demonstration: true), Is.EqualTo(PlayInHeadset.Demonstration));
        Assert.That(HeadsetPlay.CommandFor(View(Check()), Cable, demonstration: true, factory), Is.Null, "no preview is started for it");

        var wait = new HeadsetPlayWait();
        wait.Sent(factory.StartPreview(ProjectId));
        Assert.That(wait.Advance(State(project), Cable, demonstration: true), Is.Null);
        Assert.That(wait.ProjectId, Is.Null, "a demonstration that began during the wait ends it");
    }

    [Test]
    public void OnlyARootLoopbackHttpAddressWithAPortIsOpenable()
    {
        Assert.That(HeadsetPlay.IsOpenable(new Uri("http://127.0.0.1:47930/")), Is.True);
        foreach (var other in new[]
        {
            "https://127.0.0.1:47930/", "http://localhost:47930/", "http://192.168.1.23:47930/", "http://127.0.0.1/",
            "http://127.0.0.1:47930/other", "http://127.0.0.1:47930/?a=1", "http://127.0.0.1:47930/#a",
            "http://someone@127.0.0.1:47930/", "file:///sdcard/a.html",
        })
            Assert.That(HeadsetPlay.IsOpenable(new Uri(other)), Is.False, other);
        Assert.That(HeadsetPlay.IsOpenable(null), Is.False);
    }

    [Test]
    public void APressThatStartedThePreviewOpensItOnceWhenItRuns()
    {
        var factory = new CommandFactory(Samples.Client);
        var wait = new HeadsetPlayWait();
        wait.Sent(factory.OpenOnMac(ProjectId));
        Assert.That(wait.ProjectId, Is.Null, "only a preview's start begins a wait");
        wait.Sent(HeadsetPlay.CommandFor(View(Check()), Cable, demonstration: false, factory));
        Assert.That(wait.ProjectId, Is.EqualTo(ProjectId));

        Assert.That(wait.Advance(State(View(Check())), Cable, demonstration: false), Is.Null, "still starting");
        Assert.That(wait.ProjectId, Is.EqualTo(ProjectId));
        Assert.That(wait.Advance(State(View(Check(), Preview(port: 47932))), Cable, demonstration: false), Is.EqualTo(new Uri("http://127.0.0.1:47932/")));
        Assert.That(wait.ProjectId, Is.Null);
        Assert.That(wait.Advance(State(View(Check(), Preview(port: 47932))), Cable, demonstration: false), Is.Null, "opened once");
    }

    [Test]
    public void AWaitEndsWithoutOpeningWhenTheProjectGoesOrIsRefused()
    {
        var factory = new CommandFactory(Samples.Client);
        var wait = new HeadsetPlayWait();
        wait.Sent(factory.StartPreview(ProjectId));
        Assert.That(wait.Advance(State(), Cable, demonstration: false), Is.Null);
        Assert.That(wait.ProjectId, Is.Null, "the project is gone");

        wait.Sent(factory.StartPreview(ProjectId));
        Assert.That(wait.Advance(State(View(Check(files: "changed"), Preview())), Cable, demonstration: false), Is.Null);
        Assert.That(wait.ProjectId, Is.Null, "the files changed while it started");
    }

    [Test]
    public void OnAPairedComputerTheActionStandsWhereTheAppCanRelayAndNotOtherwise()
    {
        var project = View(Check(), Preview(port: 47931));
        Assert.That(HeadsetPlay.For(project, Paired, demonstration: false, relays: true), Is.EqualTo(PlayInHeadset.Offered));
        Assert.That(HeadsetPlay.For(View(Check()), Paired, demonstration: false, relays: true), Is.EqualTo(PlayInHeadset.NoPreviewOfCheck),
            "a press starts the preview first, as over the cable");
        Assert.That(HeadsetPlay.CommandFor(View(Check()), Paired, demonstration: false, new CommandFactory(Samples.Client), relays: true),
            Is.InstanceOf<ProjectPreviewStartCommand>());
        Assert.That(HeadsetPlay.AddressOf(project, Paired, demonstration: false), Is.Null, "no relay port yet, nothing opens");
        Assert.That(HeadsetPlay.AddressOf(project, Paired, demonstration: false, relayPort: 51234), Is.EqualTo(new Uri("http://127.0.0.1:51234/")));
        // The host's facts still decide first: a relay never serves an unconfined or failed check, or a demonstration.
        Assert.That(HeadsetPlay.For(View(Check(readsConfined: false), Preview()), Paired, demonstration: false, relays: true), Is.EqualTo(PlayInHeadset.NotConfined));
        Assert.That(HeadsetPlay.For(View(Check(passed: false), Preview()), Paired, demonstration: false, relays: true), Is.EqualTo(PlayInHeadset.CheckNotPassed));
        // A check whose verdict is not known is served by the host, so the headset plays it too (VRIFY); a failed verdict is not.
        var partly = Check(passed: false);
        partly["findings"]!["verdict"] = "not_known";
        Assert.That(HeadsetPlay.For(View(partly, Preview()), Paired, demonstration: false, relays: true), Is.EqualTo(PlayInHeadset.Offered));
        var failed = Check(passed: false);
        failed["findings"]!["verdict"] = "failed";
        Assert.That(HeadsetPlay.For(View(failed, Preview()), Paired, demonstration: false, relays: true), Is.EqualTo(PlayInHeadset.CheckNotPassed));
        Assert.That(HeadsetPlay.For(project, Paired, demonstration: true, relays: true), Is.EqualTo(PlayInHeadset.Demonstration));
    }

    [Test]
    public void ABuildWithoutTheRelaySaysPlayingWithoutACableIsNotInIt()
    {
        var project = View(Check(), Preview(port: 47931));
        Assert.That(HeadsetPlay.For(project, Paired, demonstration: false, relayBuilt: false), Is.EqualTo(PlayInHeadset.NotInThisBuild));
        Assert.That(HeadsetPlay.Stands(PlayInHeadset.NotInThisBuild), Is.False, "no service to start, so no action");
        Assert.That(HeadsetPlay.Refusal(PlayInHeadset.NotInThisBuild), Is.EqualTo(HeadsetPlay.NotInThisBuildWords));
        Assert.That(HeadsetPlay.AddressOf(project, Paired, demonstration: false), Is.Null);
        Assert.That(HeadsetPlay.For(project, Cable, demonstration: false, relayBuilt: false), Is.EqualTo(PlayInHeadset.Offered),
            "over the cable the build's relay plays no part");
    }

    [Test]
    public void AWaitOnAPairedComputerAsksTheRelayOnlyAsTheAddressOpens()
    {
        var factory = new CommandFactory(Samples.Client);
        var asked = new List<string>();
        int? Relay(string project)
        {
            asked.Add(project);
            return 51234;
        }
        var wait = new HeadsetPlayWait();
        wait.Sent(factory.StartPreview(ProjectId));
        Assert.That(wait.Advance(State(View(Check())), Paired, demonstration: false, relay: Relay), Is.Null, "still starting");
        Assert.That(asked, Is.Empty, "no relay while the preview starts");
        Assert.That(wait.Advance(State(View(Check(), Preview(port: 47932))), Paired, demonstration: false, relay: Relay), Is.EqualTo(new Uri("http://127.0.0.1:51234/")));
        Assert.That(asked, Is.EqualTo(new[] { ProjectId }));

        wait.Sent(factory.StartPreview(ProjectId));
        Assert.That(wait.Advance(State(View(Check(readsConfined: false), Preview())), Paired, demonstration: false, relay: Relay), Is.Null);
        Assert.That(asked.Count, Is.EqualTo(1), "a refused preview asks no relay");

        wait.Sent(factory.StartPreview(ProjectId));
        Assert.That(wait.Advance(State(View(Check(), Preview())), Paired, demonstration: false), Is.Null, "without a relay a paired wait ends, as before");
        Assert.That(wait.ProjectId, Is.Null);

        wait.Sent(factory.StartPreview(ProjectId));
        Assert.That(wait.Advance(State(View(Check(), Preview())), Paired, demonstration: false, relay: _ => null), Is.Null, "a relay that could not start opens nothing");
    }

    private static ClientProjection State(params ProjectView[] projects)
    {
        var snapshot = Samples.Snapshot(1);
        snapshot.Projects = new List<ProjectView>(projects);
        var state = new ClientProjection();
        state.ApplySnapshot(snapshot, new StateChanges());
        return state;
    }
}

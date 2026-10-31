using Halcyonic.Contracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The facts a starter project's screen shows about its check, its preview and Play on Laptop
/// (M1.8e): each read from the project's view as the host sent it, nothing derived from events, an
/// absent member never read as false, and "offered" only on the host's own admission facts.
/// </summary>
public class ProjectPlayFactsTests
{
    private const string CheckId = "01a19000-0000-7000-8000-0000000000c1";
    private const string OlderCheckId = "01a19000-0000-7000-8000-0000000000c0";

    /// <summary>A project's view as the host serializes it, with only the members given.</summary>
    private static ProjectView View(JObject? check = null, JObject? preview = null, JObject? macPlay = null)
    {
        var json = new JObject
        {
            ["project_id"] = "01a19000-0000-7000-8000-000000000001",
            ["name"] = "Garden",
            ["location"] = null,
            ["starter"] = "iwsdk-vr",
            ["created_at"] = "2026-10-31T05:00:00.000Z",
            ["updated_at"] = "2026-10-31T05:00:00.000Z",
        };
        if (check != null) json["check"] = check;
        if (preview != null) json["preview"] = preview;
        if (macPlay != null) json["mac_play"] = macPlay;
        return HalcyonicJson.Deserialize<ProjectView>(json.ToString(), strict: true);
    }

    /// <summary>A recorded check: passed, confined and ready on a laptop unless told otherwise.</summary>
    private static JObject Check(
        bool passed = true,
        bool readsConfined = true,
        bool? laptopReady = true,
        bool? ownPermissions = null,
        string checkId = CheckId)
    {
        var findings = new JObject
        {
            ["tsc"] = new JObject { ["exit"] = 0, ["seconds"] = 1 },
            ["build"] = new JObject { ["exit"] = 0, ["seconds"] = 2 },
            ["page"] = null,
            ["unknown"] = null,
            ["passed"] = passed,
            ["reads_confined"] = readsConfined,
        };
        if (laptopReady != null)
        {
            findings["laptop"] = new JObject
            {
                ["layer"] = "starter",
                ["targets"] = 1,
                ["mouse"] = "reached",
                ["key"] = "reached",
                ["screen_changed"] = true,
                ["response_seen"] = null,
                ["ready"] = laptopReady.Value,
            };
        }
        if (ownPermissions != null) findings["own_permissions"] = ownPermissions.Value;
        return new JObject { ["check_id"] = checkId, ["findings"] = findings };
    }

    private static JObject Preview(string checkId = CheckId) => new() { ["check_id"] = checkId, ["port"] = 47931 };

    private static JObject Play(string checkId = CheckId) => new() { ["check_id"] = checkId };

    [Test]
    public void WithNothingRecordedEveryFactIsAbsentAndPlayIsNotOffered()
    {
        var project = View();
        Assert.That(ProjectPlayFacts.LatestCheck(project), Is.Null);
        Assert.That(ProjectPlayFacts.PreviewOfLatestCheck(project), Is.Null);
        Assert.That(ProjectPlayFacts.IsPlaying(project), Is.False);
        Assert.That(ProjectPlayFacts.BrowserPermissions(project), Is.EqualTo(CheckPermissions.Unknown));
        Assert.That(ProjectPlayFacts.PlayOnLaptopFor(project), Is.EqualTo(PlayOnLaptop.NoCheck));
    }

    [Test]
    public void PlayOnLaptopIsOfferedOnlyWhenEveryFactTheHostAdmitsItOnHolds()
    {
        Assert.That(
            ProjectPlayFacts.PlayOnLaptopFor(View(Check(), Preview())),
            Is.EqualTo(PlayOnLaptop.Offered));
        // Each missing fact is said by itself, in the order the host refuses on.
        Assert.That(ProjectPlayFacts.PlayOnLaptopFor(View(Check(passed: false), Preview())), Is.EqualTo(PlayOnLaptop.CheckNotPassed));
        Assert.That(ProjectPlayFacts.PlayOnLaptopFor(View(Check(laptopReady: false), Preview())), Is.EqualTo(PlayOnLaptop.NotReadyOnLaptop));
        Assert.That(ProjectPlayFacts.PlayOnLaptopFor(View(Check(laptopReady: null), Preview())), Is.EqualTo(PlayOnLaptop.NotReadyOnLaptop), "a laptop step not recorded is not ready");
        Assert.That(ProjectPlayFacts.PlayOnLaptopFor(View(Check(readsConfined: false), Preview())), Is.EqualTo(PlayOnLaptop.NotConfined));
        Assert.That(ProjectPlayFacts.PlayOnLaptopFor(View(Check())), Is.EqualTo(PlayOnLaptop.NoPreviewOfCheck));
        Assert.That(ProjectPlayFacts.PlayOnLaptopFor(View(Check(), Preview(OlderCheckId))), Is.EqualTo(PlayOnLaptop.NoPreviewOfCheck), "a preview of an older check");
        Assert.That(ProjectPlayFacts.PlayOnLaptopFor(View(Check(), Preview(), Play())), Is.EqualTo(PlayOnLaptop.Playing));
    }

    [Test]
    public void AHostWithNoBrowserForItMakesPlayUnavailableNotAnError()
    {
        var project = View(Check(), Preview());
        var refused = new CommandView
        {
            CommandId = "c1",
            CommandType = CommandType.ProjectOpenOnMac,
            Status = CommandStatus.Rejected,
            IssuedAt = "2026-10-31T05:00:00.000Z",
            UpdatedAt = "2026-10-31T05:00:00.000Z",
            Rejection = new CommandRejection { Code = RejectionCode.CapabilityUnsupported, Message = "Playing on this Mac is not set up." },
        };
        Assert.That(ProjectPlayFacts.PlayOnLaptopFor(project, refused), Is.EqualTo(PlayOnLaptop.Unavailable));
        // Any other refusal, or another command, leaves the facts to say.
        refused.Rejection.Code = RejectionCode.InvalidState;
        Assert.That(ProjectPlayFacts.PlayOnLaptopFor(project, refused), Is.EqualTo(PlayOnLaptop.Offered));
        refused.Rejection.Code = RejectionCode.CapabilityUnsupported;
        refused.CommandType = CommandType.ProjectCheck;
        Assert.That(ProjectPlayFacts.PlayOnLaptopFor(project, refused), Is.EqualTo(PlayOnLaptop.Offered));
    }

    [Test]
    public void TheCheckIsReadAsRecordedNeverWidened()
    {
        var unconfined = View(Check(readsConfined: false, ownPermissions: false));
        Assert.That(ProjectPlayFacts.LatestCheck(unconfined)!.Findings.ReadsConfined, Is.False);
        Assert.That(ProjectPlayFacts.BrowserPermissions(unconfined), Is.EqualTo(CheckPermissions.AppPermissions));
        Assert.That(ProjectPlayFacts.BrowserPermissions(View(Check(ownPermissions: true))), Is.EqualTo(CheckPermissions.OwnPermissions));
        Assert.That(ProjectPlayFacts.BrowserPermissions(View(Check())), Is.EqualTo(CheckPermissions.Unknown), "absent is not known, never false");
        var preview = ProjectPlayFacts.PreviewOfLatestCheck(View(Check(), Preview()));
        Assert.That(preview!.Port, Is.EqualTo(47931));
        Assert.That(ProjectPlayFacts.PreviewOfLatestCheck(View(Check(), Preview(OlderCheckId))), Is.Null);
    }
}

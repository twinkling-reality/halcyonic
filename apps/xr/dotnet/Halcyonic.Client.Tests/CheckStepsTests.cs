using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The client keeps a host check's steps (ADR 0037), each with its result as the contract gives it: the type check, the
/// build, the page drawn and VR offered, never only passed or failed (lead 2026-11-05). The host check's moment carries them.
/// </summary>
public class CheckStepsTests
{
    private static CheckFindings Findings(string json) => HalcyonicJson.Deserialize<CheckFindings>(json, strict: true);

    private const string Passing =
        "{\"tsc\":{\"exit\":0,\"seconds\":1.5},\"build\":{\"exit\":0,\"seconds\":4.25}," +
        "\"page\":{\"exceptions\":[],\"exception_count\":0,\"console_errors\":0,\"canvases\":1,\"edges_at_3s\":900,\"edges_at_10s\":950,\"drawn\":true,\"pictures\":[]}," +
        "\"unknown\":null,\"passed\":true,\"reads_confined\":true,\"vr\":{\"offered\":\"offered\",\"how\":\"offer_session\",\"exceptions\":0,\"ready\":true}}";

    [Test]
    public void APassingChecksStepsEachPassWithTheirSeconds()
    {
        var steps = CheckSteps.Of(Findings(Passing));
        Assert.That(steps.Select(step => (step.Step, step.Result, step.Seconds)), Is.EqualTo(new (CheckStepKind, CheckStepResult, double?)[]
        {
            (CheckStepKind.TypeCheck, CheckStepResult.Passed, 1.5),
            (CheckStepKind.Build, CheckStepResult.Passed, 4.25),
            (CheckStepKind.PageDrawn, CheckStepResult.Passed, null),
            (CheckStepKind.VrOffered, CheckStepResult.Passed, null),
        }));
    }

    [Test]
    public void EachStepSaysWhatHappenedToIt()
    {
        var failed = CheckSteps.Of(Findings(
            "{\"tsc\":{\"exit\":2,\"seconds\":1},\"build\":{\"exit\":null,\"seconds\":120},\"page\":null,\"unknown\":null,\"passed\":false," +
            "\"reads_confined\":true,\"vr\":{\"offered\":\"unknown\",\"how\":null,\"exceptions\":0,\"ready\":false}}"));
        Assert.That(failed.Select(step => step.Result), Is.EqualTo(new[]
        {
            CheckStepResult.Failed, CheckStepResult.StoppedAtLimit, CheckStepResult.NotRun, CheckStepResult.Unknown,
        }), "type check failed, build stopped at its limit, no page, VR not known");

        var older = CheckSteps.Of(Findings(
            "{\"tsc\":{\"exit\":0,\"seconds\":1},\"build\":{\"exit\":0,\"seconds\":2}," +
            "\"page\":{\"exceptions\":[],\"exception_count\":0,\"console_errors\":0,\"canvases\":1,\"edges_at_3s\":0,\"edges_at_10s\":0,\"drawn\":false,\"pictures\":[]}," +
            "\"unknown\":null,\"passed\":false,\"reads_confined\":true}"));
        Assert.That(older.Select(step => step.Result), Is.EqualTo(new[]
        {
            CheckStepResult.Passed, CheckStepResult.Passed, CheckStepResult.Failed, CheckStepResult.NotRun,
        }), "nothing drawn fails its step; a check recorded before VR was checked never says VR was offered");

        var notOffered = CheckSteps.Of(Findings(Passing.Replace("\"offered\":\"offered\",\"how\":\"offer_session\"", "\"offered\":\"not_offered\",\"how\":null")));
        Assert.That(notOffered.Last().Result, Is.EqualTo(CheckStepResult.Failed));
    }

    [Test]
    public void ThePageStepReadsDrawnTrueFalseAndNotKnownEachItsOwnWay()
    {
        CheckStepResult Page(string drawn) => CheckSteps.Of(Findings(Passing.Replace("\"drawn\":true", "\"drawn\":" + drawn)))
            .Single(step => step.Step == CheckStepKind.PageDrawn).Result;
        Assert.That(Page("true"), Is.EqualTo(CheckStepResult.Passed));
        Assert.That(Page("false"), Is.EqualTo(CheckStepResult.Failed));
        var notKnown = Page("null");
        Assert.That(notKnown, Is.EqualTo(CheckStepResult.Unknown), "the starter's picture could not be made, so whether it draws its own look is not known");
        Assert.That(notKnown, Is.Not.EqualTo(CheckStepResult.Passed));
        Assert.That(notKnown, Is.Not.EqualTo(CheckStepResult.Failed));
    }

    [Test]
    public void TheHostChecksMomentCarriesItsSteps()
    {
        var moment = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: true, at: null, steps: CheckSteps.Of(Findings(Passing)));
        Assert.That(moment.Steps.Select(step => step.Step), Is.EqualTo(new[]
        {
            CheckStepKind.TypeCheck, CheckStepKind.Build, CheckStepKind.PageDrawn, CheckStepKind.VrOffered,
        }));
        Assert.That(KitMoment.HostCheck(KitCheckScope.Builds, ended: false, passed: null, at: null).Steps, Is.Empty, "a check still running has none");
    }

    private const string NotKnownFinding =
        "Not known whether it draws its own look: the check could not make the starter's picture to compare it with. Press Check to try again.";

    /// <summary>The passing check with its page's own look not known, as the host records it (VRIFY's f1ffe578c).</summary>
    private static string Verdict(bool passed, string? verdict, string drawn = "true") =>
        Passing.Replace("\"drawn\":true", "\"drawn\":" + drawn).Replace("\"passed\":true", "\"passed\":" + (passed ? "true" : "false"))
            .TrimEnd('}') + (verdict == null ? "" : ",\"verdict\":\"" + verdict + "\"") + "}";

    [Test]
    public void ANotKnownVerdictIsPartlyCheckedAndServedAsTheHostServesIt()
    {
        var partly = Findings(Verdict(false, "not_known", "null"));
        Assert.That((CheckSteps.PartlyChecked(partly), CheckSteps.Servable(partly)), Is.EqualTo((true, true)), "served as the host's checkServable serves it");
        var passed = Findings(Verdict(true, "passed"));
        Assert.That((CheckSteps.PartlyChecked(passed), CheckSteps.Servable(passed)), Is.EqualTo((false, true)));
        var failed = Findings(Verdict(false, "failed", "false"));
        Assert.That((CheckSteps.PartlyChecked(failed), CheckSteps.Servable(failed)), Is.EqualTo((false, false)), "a failure stays a failure, whatever else is not known");
        // A check recorded before the verdict reads by passed alone, as the host's does.
        var olderFailed = Findings(Verdict(false, null, "null"));
        Assert.That((CheckSteps.PartlyChecked(olderFailed), CheckSteps.Servable(olderFailed)), Is.EqualTo((false, false)));
        var olderPassed = Findings(Verdict(true, null));
        Assert.That((CheckSteps.PartlyChecked(olderPassed), CheckSteps.Servable(olderPassed)), Is.EqualTo((false, true)));
    }

    [Test]
    public void APartlyCheckedMomentSaysItsLabelAndItsLineAndCarriesTheHostsFindingNeverFailed()
    {
        var findings = Findings(Verdict(false, "not_known", "null").Replace("\"unknown\":null", "\"unknown\":\"" + NotKnownFinding + "\""));
        var moment = KitMoment.PartlyCheckedHost(KitCheckScope.Builds, findings.Unknown, at: null, steps: CheckSteps.Of(findings));
        Assert.That(moment.Title!.Text, Is.EqualTo("Partly Checked"));
        Assert.That(moment.Meta.Select(line => line.Text), Is.EqualTo(new[] { "What was checked passed; one thing could not be checked." }));
        Assert.That(moment.Fact, Is.Null, "the moment beside the character draws no finding: it would be cut there");
        Assert.That(moment.Finding, Is.EqualTo(NotKnownFinding), "carried for the Checks page, which lays it whole");
        Assert.That((moment.Passed, moment.PartlyChecked, moment.Tone), Is.EqualTo(((bool?)null, true, KitMomentTone.Neutral)),
            "never passed and never the failure's tone");
        Assert.That(moment.Kind, Is.EqualTo(KitMomentKind.HostCheck));
        Assert.That(moment.Actions, Is.EqualTo(new[] { KitMomentAction.SeeTheOutput }));
        Assert.That(moment.Steps.Single(step => step.Step == CheckStepKind.PageDrawn).Result, Is.EqualTo(CheckStepResult.Unknown));
        Assert.That(KitMoment.PartlyCheckedHost(KitCheckScope.Builds, null, at: null).Finding, Is.Null, "no finding, none carried");
        Assert.That(KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: false, at: null).PartlyChecked, Is.False);
    }

    /// <summary>The page as a build before f1ffe578c read it: drawn a plain boolean, always required.</summary>
    private sealed class PageBeforeNotKnown
    {
        [Newtonsoft.Json.JsonProperty("drawn", Required = Newtonsoft.Json.Required.Always)]
        public bool Drawn { get; set; }
    }

    /// <summary>
    /// AUDIT3's point 2: a headset built before the verdict, handed a not-known page, cannot read it. Its contract read
    /// drawn as a required boolean, so null is a JSON error, and the realtime protocol stayed at the same version, so the
    /// welcome does not turn it away first: the message holding the check is unreadable, and that headset ends each
    /// connection with Message Unreadable until both run the same version (RealtimeSession's JsonException ending).
    /// </summary>
    [Test]
    public void AHeadsetBuiltBeforeTheVerdictCannotReadANotKnownPageAndSaysItIsUnreadable()
    {
        const string NotKnownPage = "{\"drawn\":null}";
        Assert.That(() => HalcyonicJson.Deserialize<PageBeforeNotKnown>(NotKnownPage), Throws.InstanceOf<Newtonsoft.Json.JsonException>());
        Assert.That(HalcyonicJson.Deserialize<PageBeforeNotKnown>("{\"drawn\":true}").Drawn, Is.True, "a known page it still reads");
        Assert.That(ConnectionText.Unreadable, Does.StartWith("Message Unreadable"));
        // This build reads it as not known.
        var page = Findings(Verdict(false, "not_known", "null")).Page!;
        Assert.That(page.Drawn, Is.Null);
    }
}

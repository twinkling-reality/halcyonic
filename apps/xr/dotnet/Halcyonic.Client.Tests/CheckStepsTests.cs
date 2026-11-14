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
}

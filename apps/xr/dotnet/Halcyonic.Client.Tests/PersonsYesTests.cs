using System.Collections.Generic;
using Halcyonic.Contracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The person's own yes the host offers on an assistant's proposal (ADR 0055, amended 2026-11-16; AUDIT3's O1 to O5): today
/// Allow Test Servers on the own-check step. The client reads the host's option and its words, never its own, and its
/// press carries the option's digest with the yes, or the plain digest without it, never a mix.
/// </summary>
public class PersonsYesTests
{
    private static readonly CommandFactory Commands = new(Samples.Client);
    private const string Plain = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string WithYes = "2222222222222222222222222222222222222222222222222222222222222222";

    private static WorkspaceRequestStepProposal OwnCheck(bool offered = true) => new()
    {
        CommandType = CommandType.ProjectOwnCheckChoose,
        Confirm = JToken.FromObject("review"),
        WhatHappens = "These project commands become its own check. They run closed off from the rest of your computer.",
        Review = Plain,
        Options = offered
            ? new List<WorkspaceRequestOption>
            {
                new() { Field = "open_ports", Label = "Host's Label", Line = "The host's own line, said beside the switch.", Review = WithYes },
            }
            : null,
    };

    [Test]
    public void TheOptionIsTheHostsOwnWithItsLabelAndLine()
    {
        var yes = PersonsYes.Of(OwnCheck());
        Assert.That(yes, Is.Not.Null);
        Assert.That((yes!.Label, yes.Line), Is.EqualTo(("Host's Label", "The host's own line, said beside the switch.")));
        Assert.That(yes.On, Is.False, "never on until the person turns it on");
    }

    [Test]
    public void AProposalWithNoOptionOrAnotherFieldOffersNone()
    {
        Assert.That(PersonsYes.Of(OwnCheck(offered: false)), Is.Null);
        var other = OwnCheck();
        other.Options![0].Field = "something_else";
        Assert.That(PersonsYes.Of(other), Is.Null);
        Assert.That(PersonsYes.Of(null), Is.Null);
    }

    [Test]
    public void ThePressCarriesTheOptionsDigestWithTheYesAndThePlainOneWithout()
    {
        var proposal = OwnCheck();
        var yes = PersonsYes.Of(proposal)!;
        var plain = JObject.Parse(HalcyonicJson.Serialize(yes.Confirm(Commands, proposal, "r", "s")));
        var expectedPlain = new JObject { ["request_id"] = "r", ["text"] = null, ["confirm"] = new JObject { ["step_id"] = "s", ["review"] = Plain } };
        Assert.That(JToken.DeepEquals(plain["payload"], expectedPlain), Is.True, plain.ToString());
        yes.Toggle();
        Assert.That(yes.On, Is.True);
        var on = JObject.Parse(HalcyonicJson.Serialize(yes.Confirm(Commands, proposal, "r", "s")));
        var expectedOn = new JObject
        {
            ["request_id"] = "r", ["text"] = null,
            ["confirm"] = new JObject { ["step_id"] = "s", ["review"] = WithYes, ["open_ports"] = true },
        };
        Assert.That(JToken.DeepEquals(on["payload"], expectedOn), Is.True, on.ToString());
        yes.Toggle();
        var off = JObject.Parse(HalcyonicJson.Serialize(yes.Confirm(Commands, proposal, "r", "s")));
        Assert.That(JToken.DeepEquals(off["payload"], plain["payload"]), Is.True, "turned off again, the plain press");
    }

    [Test]
    public void ThePlainConfirmStillCarriesNoYes()
    {
        var confirm = JObject.Parse(HalcyonicJson.Serialize(Commands.ConfirmRequest("r", "s", "rv")));
        Assert.That(JToken.DeepEquals(confirm["payload"], JObject.Parse("""{"request_id":"r","text":null,"confirm":{"step_id":"s","review":"rv"}}""")), Is.True);
    }
}

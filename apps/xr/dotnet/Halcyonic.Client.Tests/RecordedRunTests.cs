using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// A recorded run (<c>pnpm judge:record</c>) is one real run played on the headset as it happened:
/// labeled recorded and never simulated, holding nothing simulated, never mixed with the
/// demonstration, and answering a person only with what was done when it ran. These tests make one
/// from the bundled demonstration by marking its runtimes real and dropping its stand-ins: synthetic
/// work in a recorded run's shape, so the reader's rules are tested without a real run.
/// </summary>
public class RecordedRunTests
{
    private readonly CommandFactory commands = new(Samples.Client);

    /// <summary>The demonstration in a recorded run's shape, then changed by <paramref name="edit"/>.</summary>
    private static string AsRecordedRun(Action<JObject>? edit = null) =>
        Demonstration.Edit(document =>
        {
            document["version"] = DemonstrationRecording.RecordedRunVersion;
            document["kind"] = DemonstrationRecording.RecordedRunKind;
            document.Remove("understanding");
            document.Remove("evaluation");
            document.Remove("usage_limits");
            foreach (var runtime in Runtimes(document)) runtime["synthetic"] = false;
            edit?.Invoke(document);
        });

    /// <summary>Every runtime the recording names: those its snapshots register and those its work starts on.</summary>
    private static IEnumerable<JObject> Runtimes(JObject document) =>
        document.Descendants().OfType<JObject>().Where(item => item["runtime_id"] != null && item["synthetic"] != null).ToList();

    private static JObject FirstStart(JObject document) =>
        document.Descendants().OfType<JObject>()
            .First(item => item.Value<string>("event_type") == "execution.created")["payload"]!["runtime"]!.Value<JObject>()!;

    private static void AssertRefused(string json, string because)
    {
        var error = Assert.Throws<InvalidDataException>(() => DemonstrationRecording.Parse(json));
        Assert.That(error!.Message, Does.Contain(because));
    }

    [Test]
    public void ARecordedRunIsReadAsOneAndHoldsNothingSimulated()
    {
        var run = DemonstrationRecording.Parse(AsRecordedRun());
        Assert.That(run.IsRecordedRun, Is.True);
        Assert.That(run.UsageLimits, Is.Null);
        Assert.That(run.Understanding, Is.Empty);
        Assert.That(run.Evaluation, Is.Empty);
        Assert.That(run.Snapshot.Snapshot.Runtimes.Select(runtime => runtime.Synthetic), Has.All.False);
        Assert.That(Demonstration.Recording().IsRecordedRun, Is.False, "the bundled demonstration stays a demonstration");
    }

    [Test]
    public void ARecordedRunNeverRegistersASyntheticRuntime()
    {
        AssertRefused(AsRecordedRun(document => document["snapshot"]!["snapshot"]!["runtimes"]![0]!["synthetic"] = true),
            "registers a synthetic runtime");
    }

    [Test]
    public void ARecordedRunNeverStartsWorkOnASyntheticRuntime()
    {
        AssertRefused(AsRecordedRun(document => FirstStart(document)["synthetic"] = true), "starts work on a synthetic runtime");
    }

    [TestCase("understanding")]
    [TestCase("evaluation")]
    [TestCase("usage_limits")]
    public void ARecordedRunCarriesNoStandInAnswersAndNoUsageLimits(string key)
    {
        var demonstration = (JObject)Json.Parse(Demonstration.Text());
        AssertRefused(AsRecordedRun(document => document[key] = demonstration[key]!.DeepClone()), "holds nothing simulated");
    }

    [Test]
    public void ItsVersionAndKindMustAgree()
    {
        AssertRefused(AsRecordedRun(document => document.Remove("kind")), "can't be of kind none");
        AssertRefused(AsRecordedRun(document => document["kind"] = "demonstration"), "can't be of kind demonstration");
        AssertRefused(Demonstration.Edit(document => document["kind"] = DemonstrationRecording.RecordedRunKind), "can't be of kind recorded_run");
        AssertRefused(Demonstration.Edit(document => document["version"] = 4), "plays version 2 and recorded runs of version 3");
    }

    [Test]
    public void TheDemonstrationNeverStartsWorkOnARealRuntime()
    {
        AssertRefused(Demonstration.Edit(document => FirstStart(document)["synthetic"] = false),
            "starts work on a runtime that is not synthetic");
        AssertRefused(Demonstration.Edit(document => document["snapshot"]!["snapshot"]!["runtimes"]![0]!["synthetic"] = false),
            "registers a runtime that is not synthetic");
    }

    [Test]
    public void TheLineAboveTheStageSaysTheWorkWasRealAndIsPlayedBack()
    {
        var line = DemonstrationFallback.Describe(DemonstrationReason.NotConfigured, null, ended: false, recordedRun: true);
        Assert.That(line, Is.EqualTo(
            "Recorded: real agent work, played back on this headset. Nothing here is live.\n"
            + "It waits for you where the agent waited. Nothing reaches an agent."));
        Assert.That(DemonstrationFallback.Describe(DemonstrationReason.NotConfigured, null), Does.StartWith("Demo: "),
            "the demonstration keeps its own line");
        Assert.That(DemonstrationFallback.Describe(DemonstrationReason.NotConfigured, null, ended: true, recordedRun: true),
            Does.EndWith("This recording has ended and starts again shortly."));
    }

    [Test]
    public async Task ARecordedRunAnswersOnlyWithWhatWasDoneWhenItRan()
    {
        var run = DemonstrationRecording.Parse(AsRecordedRun());
        using var transport = new DemonstrationTransport(run, Demonstration.Fast());
        await SendAsync(transport, new HelloMessage { Client = Samples.Client, Resume = null });
        await ReceiveAsync(transport, 2 + run.Nodes[0].Events.Count);

        var asked = Demonstration.AtQuestion().Answer;
        var typed = commands.AnswerQuestion(asked.ExecutionId, asked.QuestionId!, new[] { new QuestionAnswer { Key = "lockout", Selected = new List<string>(), Text = "Two hours" } });
        await SendAsync(transport, typed);
        AssertAnsweredInWords(await ReceiveAsync(transport), typed, DemonstrationTransport.NothingRecordedInRun);

        var chosen = Demonstration.AnswerCommand(commands);
        await SendAsync(transport, chosen);
        AssertAnsweredInWords(await ReceiveAsync(transport), chosen, "Not sent to any agent. The run goes on as it happened.");
        var answered = run.Nodes[Demonstration.AtQuestion().Node];
        await ReceiveAsync(transport, answered.Events.Count);

        var approve = Demonstration.AtApproval(DemonstrationAnswerKind.Approve).Answer;
        var command = commands.RespondToApproval(approve.ExecutionId, approve.ApprovalId!, ApprovalDecision.Approve);
        await SendAsync(transport, command);
        AssertAnsweredInWords(await ReceiveAsync(transport), command,
            "Not sent to any agent. This was approved when it ran, so the run goes on as it happened.");
        Assert.That(DemonstrationTransport.AnsweredInRun(Demonstration.AtApproval(DemonstrationAnswerKind.Deny).Answer),
            Is.EqualTo("Not sent to any agent. This was denied when it ran, so the run goes on as it happened."));
    }

    private static Task SendAsync(DemonstrationTransport transport, ClientMessage message) =>
        transport.SendAsync(HalcyonicJson.Serialize(message), CancellationToken.None);

    private static Task SendAsync(DemonstrationTransport transport, CommandEnvelope command) =>
        SendAsync(transport, new CommandMessage { Command = command });

    private static async Task<List<ServerMessage>> ReceiveAsync(DemonstrationTransport transport, int count)
    {
        var received = new List<ServerMessage>();
        for (var index = 0; index < count; index++) received.Add(await ReceiveAsync(transport));
        return received;
    }

    private static async Task<ServerMessage> ReceiveAsync(DemonstrationTransport transport)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var text = await transport.ReceiveAsync(timeout.Token);
        Assert.That(text, Is.Not.Null, "the connection ended");
        return Json.AssertRoundTrips<ServerMessage>(text!);
    }

    private static void AssertAnsweredInWords(ServerMessage message, CommandEnvelope command, string words)
    {
        var ack = (CommandAckMessage)message;
        Assert.That(ack.CommandId, Is.EqualTo(command.CommandId));
        Assert.That(ack.Disposition, Is.EqualTo(CommandAckDisposition.Rejected), "never accepted");
        Assert.That(ack.Command!.Rejection!.Code, Is.EqualTo(RejectionCode.Demonstration));
        Assert.That(ack.Command.Rejection.Message, Is.EqualTo(words));
    }
}

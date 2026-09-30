using System;
using System.Linq;
using Halcyonic.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class ContractTests
{
    private static readonly string[] Trace = Repository.Trace();

    private static string Edit(string json, Action<JObject> edit)
    {
        var document = (JObject)Json.Parse(json);
        edit(document);
        return document.ToString(Formatting.None);
    }

    [Test]
    public void EveryEventInTheRecordedTracesRoundTrips()
    {
        var events = Repository.AllTraces();
        Assert.That(events, Has.Length.GreaterThan(Trace.Length), "all traces, not only the demo");
        foreach (var line in events)
        {
            var envelope = Json.AssertRoundTrips<EventEnvelope>(line);
            Assert.That(envelope.EventType, Is.EqualTo((string?)Json.Parse(line)["event_type"]));
        }
    }

    [Test]
    public void EventsBecomeTheirVariantsWithTypedPayloads()
    {
        var events = Trace.Select(line => HalcyonicJson.Deserialize<EventEnvelope>(line, strict: true)).ToList();

        var approval = events.OfType<RuntimeApprovalRequestedEvent>().Single();
        Assert.That(approval.Source, Is.TypeOf<RuntimeSource>());
        Assert.That(((RuntimeSource)approval.Source).RuntimeId, Is.EqualTo("mock"));
        Assert.That(approval.Payload.Subject, Is.TypeOf<ToolUseSubject>());

        var accepted = events.OfType<CommandAcceptedEvent>().First();
        Assert.That(accepted.Source, Is.TypeOf<ControlPlaneSource>());
        Assert.That(accepted.Payload.Command, Is.TypeOf<ProjectCreateCommand>());
        Assert.That(accepted.Payload.ReceivedVia, Is.EqualTo(ReceivedVia.Internal).Or.EqualTo(ReceivedVia.Websocket));

        var message = events.OfType<RuntimeAgentMessageEvent>().First();
        Assert.That(message.Provenance, Is.TypeOf<ReportedProvenance>());

        var completed = events.OfType<CommandCompletedEvent>().First();
        Assert.That(completed.Payload.Result, Is.TypeOf<ProjectCreatedResult>());
    }

    [Test]
    public void StrictReadingRejectsUnknownPropertiesAndTolerantReadingIgnoresThem()
    {
        var top = Edit(Trace[0], document => document["added_later"] = 1);
        var nested = Edit(Trace[0], document => document["provenance"]!["added_later"] = true);
        foreach (var json in new[] { top, nested })
        {
            Assert.Throws<JsonSerializationException>(() => HalcyonicJson.Deserialize<EventEnvelope>(json, strict: true));
            Assert.That(HalcyonicJson.Deserialize<EventEnvelope>(json), Is.InstanceOf<CommandAcceptedEvent>());
        }
    }

    [Test]
    public void MissingPropertiesAndNullsWhereNoneAreAllowedAreRejected()
    {
        var missing = Edit(Trace[0], document => document.Remove("occurred_at"));
        var nulled = Edit(Trace[0], document => document["event_id"] = null);
        var nullNested = Edit(Trace[0], document => document["payload"]!["policy"] = null);
        foreach (var json in new[] { missing, nulled, nullNested })
        {
            Assert.Throws<JsonSerializationException>(() => HalcyonicJson.Deserialize<EventEnvelope>(json));
        }
    }

    [Test]
    public void UnknownOrMissingDiscriminatorsAreRejected()
    {
        var unknown = Edit(Trace[0], document => document["event_type"] = "project.renamed");
        var error = Assert.Throws<JsonSerializationException>(() => HalcyonicJson.Deserialize<EventEnvelope>(unknown));
        Assert.That(error!.Message, Does.Contain("project.renamed"));

        var missing = Edit(Trace[0], document => document.Remove("event_type"));
        Assert.Throws<JsonSerializationException>(() => HalcyonicJson.Deserialize<EventEnvelope>(missing));

        var wrongType = Edit(Trace[0], document => document["event_type"] = 7);
        Assert.Throws<JsonSerializationException>(() => HalcyonicJson.Deserialize<EventEnvelope>(wrongType));
    }

    [Test]
    public void AVariantIsReadOnlyAsItself()
    {
        var error = Assert.Throws<JsonSerializationException>(() => HalcyonicJson.Deserialize<RuntimeAgentMessageEvent>(Trace[0]));
        Assert.That(error!.Message, Does.Contain("command.accepted"));
    }

    [Test]
    public void EnumsUseTheirWireValues()
    {
        Assert.That(HalcyonicJson.Serialize(ExecutionStatus.WaitingForHuman), Is.EqualTo("\"waiting_for_human\""));
        Assert.That(HalcyonicJson.Serialize(CommandType.ExecutionRespondToApproval), Is.EqualTo("\"execution.respond_to_approval\""));
        Assert.That(JsonConvert.DeserializeObject<AttentionLevel>("\"action_required\"", HalcyonicJson.Strict), Is.EqualTo(AttentionLevel.ActionRequired));
        Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<ExecutionStatus>("\"paused\"", HalcyonicJson.Strict));
    }

    [Test]
    public void CommandsSerializeToTheWireShape()
    {
        var factory = new CommandFactory(
            new ClientInfo { Name = "test", Version = null, DeviceLabel = null },
            () => new DateTimeOffset(2026, 9, 26, 12, 0, 0, 5, TimeSpan.FromHours(2)));
        var command = factory.RespondToApproval("0192f1a0-0000-7000-8000-000000000001", "approval-1", ApprovalDecision.Approve);

        var written = Json.Parse(HalcyonicJson.Serialize(new CommandMessage { Command = command }));
        var expected = new JObject
        {
            ["type"] = "command",
            ["command"] = new JObject
            {
                ["schema_version"] = 1,
                ["command_id"] = command.CommandId,
                ["command_type"] = "execution.respond_to_approval",
                ["issued_at"] = "2026-09-26T10:00:00.005Z",
                ["client"] = new JObject { ["name"] = "test", ["version"] = null, ["device_label"] = null },
                ["payload"] = new JObject
                {
                    ["execution_id"] = "0192f1a0-0000-7000-8000-000000000001",
                    ["approval_id"] = "approval-1",
                    ["decision"] = "approve",
                    ["message"] = null,
                },
            },
        };
        Assert.That(JToken.DeepEquals(written, expected), Is.True, written.ToString());
        Assert.That(command.CommandId, Does.Match("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"));
        Assert.That(factory.Interrupt("x").CommandId, Is.Not.EqualTo(command.CommandId));
    }

    [Test]
    public void AStartCarriesTheChosenModelOrAnExplicitNull()
    {
        var factory = new CommandFactory(new ClientInfo { Name = "test", Version = null, DeviceLabel = null });
        const string workstreamId = "0192f1a0-0000-7000-8000-000000000002";

        var unchosen = Json.Parse(HalcyonicJson.Serialize(factory.StartExecution(workstreamId, "opencode", "Fix the build.")));
        var payload = (JObject)unchosen["payload"]!;
        Assert.That(payload.ContainsKey("model_ref"), Is.True, "the choice is sent as an explicit null, never left out");
        Assert.That(payload["model_ref"]!.Type, Is.EqualTo(JTokenType.Null));

        var chosen = Json.Parse(HalcyonicJson.Serialize(
            factory.StartExecution(workstreamId, "opencode", "Fix the build.", modelRef: "ollama/gpt-4o:latest")));
        Assert.That((string?)chosen["payload"]!["model_ref"], Is.EqualTo("ollama/gpt-4o:latest"));
        Assert.That(((JObject)chosen["payload"]!["options"]!).Count, Is.EqualTo(0));
    }

    [Test]
    public void ClientMessagesRoundTrip()
    {
        var hello = new HelloMessage
        {
            Client = Samples.Client,
            Resume = new ResumeCursor { JournalId = Samples.JournalId, Position = 42 },
        };
        var written = HalcyonicJson.Serialize(hello);
        Assert.That(Json.AssertRoundTrips<ClientMessage>(written), Is.TypeOf<HelloMessage>());
        Assert.That(Json.Parse(written)["protocol"]!.Value<int>(), Is.EqualTo(ContractVersions.RealtimeProtocol));
        Assert.That(Json.AssertRoundTrips<ClientMessage>(HalcyonicJson.Serialize(new PingMessage())), Is.TypeOf<PingMessage>());
    }
}

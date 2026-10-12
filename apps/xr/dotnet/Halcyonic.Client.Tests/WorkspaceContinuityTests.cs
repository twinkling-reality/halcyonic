using System;
using System.IO;
using System.Linq;
using Halcyonic.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class WorkspaceContinuityTests
{
    private static JObject Fixture => JObject.Parse(File.ReadAllText(Repository.PathTo("fixtures/workspace/continuation.json")));
    private static string ValidJson => Fixture["cases"]![0]!["value"]!.ToString(Formatting.None);

    [Test]
    public void SharedFixturesMatchTheStrictReader()
    {
        foreach (var item in Fixture["cases"]!)
            Assert.That(WorkspaceContinuity.TryParse(item["value"]!.ToString(Formatting.None), out _),
                Is.EqualTo((bool)item["valid"]!), (string?)item["name"]);
    }

    [Test]
    public void IdentityMatchesNativeVectorsAndChangesWithCredentials()
    {
        foreach (var item in Fixture["identities"]!)
        {
            var target = Target(item);
            var expected = (string)item["connection_key"]!;
            Assert.That(WorkspaceContinuity.ConnectionKey(target), Is.EqualTo(expected), (string?)item["name"]);
            var changed = (JObject)item.DeepClone();
            changed["credential"] = (string)item["credential"]! + "changed";
            Assert.That(WorkspaceContinuity.ConnectionKey(Target(changed)), Is.Not.EqualTo(expected));
        }
    }

    [Test]
    public void UnsafeOrUnsharedEndpointsCannotCreateContinuity()
    {
        foreach (var endpoint in new[] { "ws://example.com:47800/realtime", "ws://127.0.0.1/realtime?redirect=elsewhere", "ws://127.0.0.1/other", "wss://127.0.0.1/realtime" })
            Assert.Throws<ArgumentException>(() => WorkspaceContinuity.ConnectionKey(ControlPlaneTarget.Local(new Uri(endpoint), "fixture")));
        var pairing = new PairedControlPlane("not a host", 47801, new string('b', 64), "device", "hlcd_fixture");
        Assert.Throws<UriFormatException>(() => WorkspaceContinuity.ConnectionKey(ControlPlaneTarget.Paired(pairing)));
    }

    [Test]
    public void MalformedDuplicateTrailingAndOversizedDocumentsAreRejected()
    {
        Assert.That(WorkspaceContinuity.TryParse(null, out _), Is.False);
        Assert.That(WorkspaceContinuity.TryParse(ValidJson + "{}", out _), Is.False);
        Assert.That(WorkspaceContinuity.TryParse(ValidJson.Replace("\"version\":1", "\"version\":1,\"version\":1"), out _), Is.False);
        Assert.That(WorkspaceContinuity.TryParse(ValidJson.Replace("\"version\":1", "\"version\":999999999999999999999999999999"), out _), Is.False);
        Assert.That(WorkspaceContinuity.TryParse(new string(' ', WorkspaceContinuity.MaximumBytes + 1), out _), Is.False);
        var value = JObject.Parse(ValidJson);
        value["instruction"]!["text"] = new string('x', 32001);
        Assert.That(WorkspaceContinuity.TryParse(value.ToString(Formatting.None), out _), Is.False);
    }

    [Test]
    public void OnlyCurrentRelatedSelectionCanRestoreADraft()
    {
        WorkspaceContinuity.TryParse(ValidJson, out var saved);
        var continuation = saved!;
        continuation.JournalId = Samples.JournalId;
        continuation.ProjectId = Samples.ProjectId;
        var work = Samples.Workstream(continuation.WorkstreamId!, executionId: continuation.ExecutionId);
        var execution = Samples.Execution(continuation.ExecutionId!, work.WorkstreamId, ExecutionStatus.Running);
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(1, new[] { work }, new[] { execution }), new StateChanges());
        Assert.That(WorkspaceContinuity.MatchesCurrent(continuation, continuation.ConnectionKey, state), Is.True);
        Assert.That(WorkspaceContinuity.MatchesCurrent(continuation, new string('b', 64), state), Is.False);
        Assert.That(WorkspaceContinuity.MatchesCurrent(continuation, continuation.ConnectionKey, new ClientProjection()), Is.False);

        execution.ProjectId = "0192f1a0-0000-7000-8000-000000000099";
        Assert.That(WorkspaceContinuity.MatchesCurrent(continuation, continuation.ConnectionKey, state), Is.False, "foreign execution");
        execution.ProjectId = Samples.ProjectId;
        work.CurrentExecutionId = "0192f1a0-0000-7000-8000-000000000099";
        Assert.That(WorkspaceContinuity.MatchesCurrent(continuation, continuation.ConnectionKey, state), Is.False, "do not move the draft to a new run");
        continuation.ExecutionId = null;
        Assert.That(WorkspaceContinuity.MatchesCurrent(continuation, continuation.ConnectionKey, state), Is.False, "draft without its execution");
        continuation.Instruction = null;
        Assert.That(WorkspaceContinuity.MatchesCurrent(continuation, continuation.ConnectionKey, state), Is.True, "task selection alone");
        continuation.JournalId = "0192f1a0-0000-7000-8000-000000000099";
        Assert.That(WorkspaceContinuity.MatchesCurrent(continuation, continuation.ConnectionKey, state), Is.False, "changed journal");
    }

    private static ControlPlaneTarget Target(JToken item) => (string)item["scheme"]! == "ws"
        ? ControlPlaneTarget.Local(new Uri($"ws://{item["host"]}:{item["port"]}/realtime"), (string)item["credential"]!)
        : ControlPlaneTarget.Paired(new PairedControlPlane((string)item["host"]!, (int)item["port"]!,
            (string)item["certificate_sha256"]!, (string)item["device_id"]!, (string)item["credential"]!));
}

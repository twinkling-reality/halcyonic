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
    public void SharedUnicodeLimitsUseCodePointsOnBothIndependentDrafts()
    {
        foreach (var sample in Fixture["text_limits"]!)
        foreach (var field in new[] { "instruction", "request" })
        {
            var value = JObject.Parse(ValidJson);
            value[field] = new JObject { ["text"] = string.Concat(Enumerable.Repeat((string)sample["symbol"]!, (int)sample["count"]!)), ["spoken"] = false, ["part"] = 0 };
            if (field == "request") value[field]!["request_id"] = null;
            Assert.That(WorkspaceContinuity.TryParse(value.ToString(Formatting.None), out _), Is.EqualTo((bool)sample["valid"]!), field);
        }
    }

    [Test]
    public void LegacyV1MigratesOnlyMissingRequestAndSurfaceAndKeepsOrdinaryWords()
    {
        var legacy = JObject.Parse(ValidJson);
        legacy.Remove("request");
        legacy.Remove("surface");
        Assert.That(WorkspaceContinuity.TryParse(legacy.ToString(), out var restored), Is.True);
        Assert.That(restored!.Request, Is.Null);
        Assert.That(restored.Surface, Is.EqualTo(WorkspaceSurface.Task));
        Assert.That(restored.Instruction!.Text, Is.EqualTo((string)legacy["instruction"]!["text"]!));
        legacy.Remove("instruction");
        Assert.That(WorkspaceContinuity.TryParse(legacy.ToString(), out _), Is.False);
    }

    [Test]
    public void RequestDraftAndIdentityRoundTripSeparatelyWithNoRestoredAuthority()
    {
        Assert.That(WorkspaceContinuity.TryParse(ValidJson, out var saved), Is.True);
        saved!.Request = new WorkspaceRequestContinuation { RequestId = Samples.ProjectId,
            PendingCommandId = "550e8400-e29b-41d4-a716-446655440000",
            Text = "New request words", Spoken = true, Part = 3 };
        var json = HalcyonicJson.Serialize(saved);
        Assert.That(WorkspaceContinuity.TryParse(json, out var restored), Is.True);
        Assert.That(restored!.Request!.Text, Is.EqualTo("New request words"));
        Assert.That(restored.Request.RequestId, Is.EqualTo(Samples.ProjectId));
        Assert.That(restored.Request.PendingCommandId, Is.EqualTo(saved.Request.PendingCommandId));
        Assert.That(restored.Request.Spoken, Is.True);
        Assert.That(restored.Request.Part, Is.EqualTo(3));
        Assert.That(restored.Instruction!.Text, Is.EqualTo(saved.Instruction!.Text));
        Assert.That(restored.Surface, Is.EqualTo(WorkspaceSurface.Task), "an inactive request does not select its view");
        saved.Surface = WorkspaceSurface.Request;
        Assert.That(WorkspaceContinuity.TryParse(HalcyonicJson.Serialize(saved), out var requestView), Is.True);
        Assert.That(requestView!.Surface, Is.EqualTo(WorkspaceSurface.Request));
        Assert.That(requestView.Instruction!.Text, Is.EqualTo(saved.Instruction.Text));
        Assert.That(requestView.Request!.Text, Is.EqualTo(saved.Request.Text));
        Assert.That(WorkspaceContinuity.TryParse(json.Replace("\"request_id\":", "\"request_id\":null,\"request_id\":"), out _), Is.False);
        foreach (var field in new[] { "read", "armed", "auto_send", "answers", "credential", "approval_id" })
        {
            var value = JObject.Parse(json); value["request"]![field] = true;
            Assert.That(WorkspaceContinuity.TryParse(value.ToString(), out _), Is.False, field);
        }
        saved.Request.Text = new string('x', 32001);
        Assert.That(WorkspaceContinuity.TryParse(HalcyonicJson.Serialize(saved), out _), Is.False);
    }

    [Test]
    public void LegacyRequestMigratesOnlyTheMissingPendingCommandReference()
    {
        var value = JObject.Parse(ValidJson);
        value["request"] = new JObject { ["request_id"] = null, ["text"] = "Retained words", ["spoken"] = false, ["part"] = 0 };
        Assert.That(WorkspaceContinuity.TryParse(value.ToString(), out var restored), Is.True);
        Assert.That(restored!.Request!.PendingCommandId, Is.Null);
        ((JObject)value["request"]!).Remove("request_id");
        Assert.That(WorkspaceContinuity.TryParse(value.ToString(), out _), Is.False);
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
        foreach (var credential in new[] { "hlcd_", "hlcd_bad\r\nvalue", "hlcd_fixture\n", "hlcd_" + new string('a', 4097) })
        {
            var invalid = new PairedControlPlane("computer.local", 47801, new string('b', 64), Samples.ProjectId, credential);
            Assert.Throws<ArgumentException>(() => WorkspaceContinuity.ConnectionKey(ControlPlaneTarget.Paired(invalid)));
        }
    }

    [Test]
    public void JavaScriptExtensionsAndUnescapedControlsCannotCrossTheNativeBoundary()
    {
        foreach (var json in new[]
        {
            ValidJson.Replace("\"version\":1", "version:1"),
            ValidJson.Replace("\"version\":1", "'version':1"),
            ValidJson.Replace("\"version\":1", "\"version\":01"),
            ValidJson.Replace("\"version\":1", "\"version\":0x1"),
            ValidJson.Replace("\"version\":1", "\"version\":1/*comment*/"),
            ValidJson.Insert(ValidJson.Length - 1, ","),
            ValidJson.Replace("\"version\":1", "\"version\":1\v"),
            ValidJson.Replace("\"spoken\":true", "\"spoken\":true,"),
        }) Assert.That(WorkspaceContinuity.TryParse(json, out _), Is.False, json);

        var value = JObject.Parse(ValidJson);
        value["instruction"]!["text"] = "Line one\nLine two: \"quoted\" \\ path, λ 🚀";
        Assert.That(WorkspaceContinuity.TryParse(value.ToString(Formatting.Indented), out _), Is.True);
        Assert.That(WorkspaceContinuity.TryParse(value.ToString(Formatting.None).Replace("\\n", "\n"), out _), Is.False);
        foreach (var key in new[] { "handoff_id", "connection_key", "journal_id" })
        {
            var malformed = (JObject)value.DeepClone();
            malformed[key] = (string)malformed[key]! + "\n";
            Assert.That(WorkspaceContinuity.TryParse(malformed.ToString(Formatting.None), out _), Is.False, key);
        }
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
        continuation.Request = new WorkspaceRequestContinuation { Text = "Ordinary request" };
        Assert.That(WorkspaceContinuity.MatchesCurrent(continuation, continuation.ConnectionKey, state), Is.False, "request requires its exact execution");
        continuation.Request = null;
        continuation.Surface = WorkspaceSurface.Request;
        Assert.That(WorkspaceContinuity.MatchesCurrent(continuation, continuation.ConnectionKey, state), Is.False, "even an empty request view requires its execution");
        continuation.Surface = WorkspaceSurface.Task;
        continuation.JournalId = "0192f1a0-0000-7000-8000-000000000099";
        Assert.That(WorkspaceContinuity.MatchesCurrent(continuation, continuation.ConnectionKey, state), Is.False, "changed journal");
    }

    private static ControlPlaneTarget Target(JToken item) => (string)item["scheme"]! == "ws"
        ? ControlPlaneTarget.Local(new Uri($"ws://{item["host"]}:{item["port"]}/realtime"), (string)item["credential"]!)
        : ControlPlaneTarget.Paired(new PairedControlPlane((string)item["host"]!, (int)item["port"]!,
            (string)item["certificate_sha256"]!, (string)item["device_id"]!, (string)item["credential"]!));
}

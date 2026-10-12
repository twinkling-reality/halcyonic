using System;
using System.IO;
using System.Linq;
using System.Text;
using Halcyonic.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class WorkspaceViewStorageTests
{
    private static WorkspaceContinuation Record(int number = 1)
    {
        var fixture = JObject.Parse(File.ReadAllText(Repository.PathTo("fixtures/workspace/continuation.json")));
        Assert.That(WorkspaceContinuity.TryParse(fixture["cases"]![0]!["value"]!.ToString(Formatting.None), out var record), Is.True);
        record!.HandoffId = $"00000000-0000-4000-8000-{number:x12}";
        return record;
    }

    private static WorkspaceViewStore Store(int count = 1)
    {
        var result = new WorkspaceViewStore();
        for (var i = 1; i <= count; i++) result.Views.Add(Record(i));
        result.Selected = result.Views.LastOrDefault()?.HandoffId;
        return result;
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(33)]
    public void AllViewsAndSelectedDraftRoundTripWithoutACountLimit(int count)
    {
        var original = Store(count);
        if (count > 0) original.Views[0].Instruction!.Text = "Line one\nLine two: \"quoted\" \\ path, λ 🚀";
        Assert.That(WorkspaceViewStorage.TrySerialize(original, out var json), Is.True);
        Assert.That(WorkspaceViewStorage.TryParse(json, out var restored), Is.True);
        Assert.That(HalcyonicJson.Serialize(restored!), Is.EqualTo(HalcyonicJson.Serialize(original)));
        Assert.That(restored, Is.Not.SameAs(original));
        if (count > 0) Assert.That(restored!.Views[0].Instruction, Is.Not.SameAs(original.Views[0].Instruction));
    }

    [Test]
    public void DuplicateIdsAndForeignOrMalformedSelectedIdsAreRejected()
    {
        var original = Store(2);
        original.Views[1].HandoffId = original.Views[0].HandoffId;
        Assert.That(WorkspaceViewStorage.TryParse(HalcyonicJson.Serialize(original), out _), Is.False);
        foreach (var selected in new[] { Record(9).HandoffId, "", original.Selected! + "\n", original.Selected!.ToUpperInvariant() + "A" })
        {
            var value = Store(); value.Selected = selected;
            Assert.That(WorkspaceViewStorage.TrySerialize(value, out var json), Is.False);
            Assert.That(json, Is.Null);
        }
        original = Store(); original.Selected = null;
        Assert.That(WorkspaceViewStorage.TrySerialize(original, out _), Is.True);
    }

    [Test]
    public void EnvelopeRequiresExactFieldsTypesAndPlainJson()
    {
        var json = HalcyonicJson.Serialize(Store());
        foreach (var invalid in new[]
        {
            json + "{}", json.Insert(json.Length - 1, ","), json.Replace("\"version\":1", "\"version\":1,\"version\":1"),
            json.Replace("\"version\":1", "'version':1"), json.Replace("\"version\":1", "\"version\":1/*comment*/"),
            json.Replace("\"version\":1", "\"version\":01"), json.Replace("\"version\":1", "\"version\":1.0"),
            json.Replace("\"version\":1", "\"version\":999999999999999999999999999999"),
            json.Replace("]}", ",]}"),
        }) Assert.That(WorkspaceViewStorage.TryParse(invalid, out var rejected), Is.False, invalid);
        foreach (var field in new[] { "version", "selected", "views" })
        {
            var value = JObject.Parse(json); value.Remove(field);
            Assert.That(WorkspaceViewStorage.TryParse(value.ToString(), out _), Is.False, field);
        }
        foreach (var selected in new JToken[] { true, 1, new JArray(), new JObject() })
        {
            var value = JObject.Parse(json); value["selected"] = selected;
            Assert.That(WorkspaceViewStorage.TryParse(value.ToString(), out _), Is.False);
        }
        var extra = JObject.Parse(json); extra["armed"] = true;
        Assert.That(WorkspaceViewStorage.TryParse(extra.ToString(), out _), Is.False);
        extra = JObject.Parse(json); extra["views"]![0]!["instruction"]!["approved"] = true;
        Assert.That(WorkspaceViewStorage.TryParse(extra.ToString(), out _), Is.False);
        extra = JObject.Parse(json); extra["views"] = new JObject();
        Assert.That(WorkspaceViewStorage.TryParse(extra.ToString(), out _), Is.False);
        Assert.That(WorkspaceViewStorage.TryParse(null, out var missing), Is.False);
        Assert.That(missing, Is.Null);
    }

    [Test]
    public void ExactUtf8AggregateBoundAppliesEvenWithValidIndividualRecords()
    {
        var original = Store(45);
        foreach (var record in original.Views) record.Instruction!.Text = new string('界', 32000);
        var json = HalcyonicJson.Serialize(original);
        Assert.That(json.Length, Is.LessThan(WorkspaceViewStorage.MaximumBytes));
        Assert.That(Encoding.UTF8.GetByteCount(json), Is.GreaterThan(WorkspaceViewStorage.MaximumBytes));
        Assert.That(WorkspaceViewStorage.TryParse(json, out _), Is.False);
        Assert.That(WorkspaceViewStorage.TrySerialize(original, out var rejected), Is.False);
        Assert.That(rejected, Is.Null);
        original.Views.RemoveAt(44); original.Views.RemoveAt(43); original.Selected = null;
        Assert.That(WorkspaceViewStorage.TrySerialize(original, out _), Is.True);
        var empty = HalcyonicJson.Serialize(Store(0));
        var boundary = empty + new string(' ', WorkspaceViewStorage.MaximumBytes - Encoding.UTF8.GetByteCount(empty));
        Assert.That(WorkspaceViewStorage.TryParse(boundary, out _), Is.True);
        Assert.That(WorkspaceViewStorage.TryParse(boundary + " ", out _), Is.False);
    }

    [Test]
    public void FailedSerializationDoesNotEvictOrRewriteCallerDrafts()
    {
        var original = Store(140);
        foreach (var record in original.Views) record.Instruction!.Text = new string('x', 32000);
        var before = HalcyonicJson.Serialize(original);
        Assert.That(WorkspaceViewStorage.TrySerialize(original, out var json), Is.False);
        Assert.That(json, Is.Null);
        Assert.That(HalcyonicJson.Serialize(original), Is.EqualTo(before));
        original = Store(); original.Views[0].Instruction!.Text = new string('x', 32001);
        Assert.That(WorkspaceViewStorage.TrySerialize(original, out _), Is.False, "individual instruction limit remains enforced");
        Assert.That(original.Views[0].Instruction!.Text.Length, Is.EqualTo(32001));
        original.Views[0].Instruction!.Text = "keep this draft"; original.Views[0].Section = (WorkspaceSection)999;
        Assert.That(WorkspaceViewStorage.TrySerialize(original, out _), Is.False);
        Assert.That(original.Views[0].Instruction!.Text, Is.EqualTo("keep this draft"));
    }

    [Test]
    public void StaleExecutionIsRecoverableDataWithoutCurrentAuthority()
    {
        var original = Store(); var saved = original.Views[0];
        saved.JournalId = Samples.JournalId; saved.ProjectId = Samples.ProjectId;
        var work = Samples.Workstream(saved.WorkstreamId!, executionId: saved.ExecutionId);
        var execution = Samples.Execution(saved.ExecutionId!, work.WorkstreamId, ExecutionStatus.Running);
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(1, new[] { work }, new[] { execution }), new StateChanges());
        Assert.That(WorkspaceViewStorage.TrySerialize(original, out var json), Is.True);
        Assert.That(WorkspaceViewStorage.TryParse(json, out var restored), Is.True);
        Assert.That(WorkspaceContinuity.MatchesCurrent(restored!.Views[0], saved.ConnectionKey, state), Is.True);
        work.CurrentExecutionId = "0192f1a0-0000-7000-8000-000000000099";
        Assert.That(WorkspaceViewStorage.TryParse(json, out restored), Is.True);
        Assert.That(WorkspaceContinuity.MatchesCurrent(restored!.Views[0], saved.ConnectionKey, state), Is.False);
        Assert.That(restored.Views[0].Instruction!.Text, Is.EqualTo(saved.Instruction!.Text));
    }
}

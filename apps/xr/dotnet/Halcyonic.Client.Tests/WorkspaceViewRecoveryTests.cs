using System;
using System.IO;
using System.Linq;
using Halcyonic.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class WorkspaceViewRecoveryTests
{
    private string directory = null!;
    private string path = null!;
    [SetUp] public void SetUp() { directory = Path.Combine(Path.GetTempPath(), "halcyonic-workspace-" + Guid.NewGuid()); Directory.CreateDirectory(directory); path = Path.Combine(directory, "unity-views.json"); }
    [TearDown] public void TearDown() => Directory.Delete(directory, true);
    // Controlled temporary fixture IO, not the Android privacy/no-follow proof supplied by PanelFiles tests.
    private WorkspaceViewRecovery Recovery() => new(() => (true, File.Exists(path) ? File.ReadAllText(path) : null), json =>
    {
        if (Directory.Exists(path + ".new")) return false;
        var temporary = path + ".fixture-new";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, true);
        return true;
    });

    private static WorkspaceContinuation Record()
    {
        var fixture = JObject.Parse(File.ReadAllText(Repository.PathTo("fixtures/workspace/continuation.json")));
        WorkspaceContinuity.TryParse(fixture["cases"]![0]!["value"]!.ToString(Formatting.None), out var record);
        record!.JournalId = Samples.JournalId; record.ProjectId = Samples.ProjectId;
        return record;
    }
    private static ClientProjection State(WorkspaceContinuation record)
    {
        var work = Samples.Workstream(record.WorkstreamId!, executionId: record.ExecutionId);
        var execution = Samples.Execution(record.ExecutionId!, work.WorkstreamId, ExecutionStatus.Running);
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(1, new[] { work }, new[] { execution }), new StateChanges());
        return state;
    }

    [Test]
    public void SavedComparisonIncludesRequestReferenceWordsOriginPartAndSelectedSurface()
    {
        var record = Record(); record.Request = new WorkspaceRequestContinuation { Text = "Keep the request", Spoken = true, Part = 1 };
        var recovery = Recovery(); Assert.That(recovery.Keep(new[] { record }, record.HandoffId), Is.True);
        Assert.That(recovery.IsSaved(record), Is.True);
        record.Surface = WorkspaceSurface.Request; Assert.That(recovery.IsSaved(record), Is.False); record.Surface = WorkspaceSurface.Task;
        record.Request.Text = "New request"; Assert.That(recovery.IsSaved(record), Is.False); record.Request.Text = "Keep the request";
        record.Request.Spoken = false; Assert.That(recovery.IsSaved(record), Is.False); record.Request.Spoken = true;
        record.Request.Part = 0; Assert.That(recovery.IsSaved(record), Is.False); record.Request.Part = 1;
        record.Request.RequestId = "0192f1a0-0000-7000-8000-000000000104"; Assert.That(recovery.IsSaved(record), Is.False);
        record.Request.RequestId = null; Assert.That(recovery.IsSaved(record), Is.True);
    }

    [Test]
    public void AtomicOrdinaryStoreRoundTripsAllViewsAndPreservesPreviousBytesOnFailure()
    {
        var recovery = Recovery();
        var records = Enumerable.Range(1, 65).Select(number => { var record = Record(); record.HandoffId = $"00000000-0000-4000-8000-{number:x12}"; return record; }).ToArray();
        Assert.That(recovery.Keep(records, records[64].HandoffId), Is.True);
        var before = File.ReadAllBytes(path);
        var restored = Recovery();
        Assert.That(restored.Views.Count, Is.EqualTo(65));
        Assert.That(restored.Selected, Is.EqualTo(records[64].HandoffId));
        records[0].Instruction!.Text = new string('x', 32001);
        Assert.That(recovery.Keep(records, null), Is.False);
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        Assert.That(recovery.Views[0].Instruction!.Text, Is.Not.EqualTo(records[0].Instruction!.Text));
        records[0] = Record();
        Directory.CreateDirectory(path + ".new");
        Assert.That(recovery.Keep(new[] { records[0] }, null), Is.False);
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
    }

    [TestCase("not JSON")]
    [TestCase("{\"version\":1,\"selected\":null,\"views\":[],}")]
    public void FailedReadCannotReplaceAnExistingPrivateFile(string content)
    {
        File.WriteAllText(path, content);
        var recovery = Recovery();
        Assert.That(recovery.Available, Is.False);
        Assert.That(recovery.Keep(new[] { Record() }, null), Is.False);
        Assert.That(File.ReadAllText(path), Is.EqualTo(content));
    }

    [Test]
    public void FailedNativeReadIsNeverTreatedAsAnAbsentStore()
    {
        var writes = 0;
        var recovery = new WorkspaceViewRecovery(() => (false, null), _ => { writes++; return true; });
        Assert.That(recovery.Available, Is.False);
        Assert.That(recovery.Keep(new[] { Record() }, null), Is.False);
        Assert.That(writes, Is.Zero);
        recovery = new WorkspaceViewRecovery(() => (true, null), _ => { writes++; return true; });
        Assert.That(recovery.Available, Is.True, "only a verified absent result starts empty");
        Assert.That(recovery.Keep(new[] { Record() }, null), Is.True);
        Assert.That(writes, Is.EqualTo(1));
    }

    [Test]
    public void NativeReturnWaitsForFreshSnapshotAndSavesAppliedDraftBeforeExactAcknowledgement()
    {
        var record = Record();
        var json = HalcyonicJson.Serialize(record);
        var state = State(record);
        var recovery = Recovery();
        var applied = 0; var acknowledged = 0;
        bool Apply(WorkspaceContinuation view) { applied++; Assert.That(view.Instruction!.Text, Is.EqualTo(record.Instruction!.Text)); return true; }
        bool Ack(string id)
        {
            acknowledged++;
            Assert.That(id, Is.EqualTo(record.HandoffId));
            var disk = Recovery();
            Assert.That(disk.Selected, Is.EqualTo(id));
            Assert.That(disk.Views.Single().Instruction!.Text, Is.EqualTo(record.Instruction!.Text));
            return true;
        }
        Assert.That(recovery.Return(json, record.ConnectionKey, null, false, Apply, Ack), Is.EqualTo(WorkspaceReturnResult.WaitingForSnapshot));
        Assert.That(recovery.Return(json, record.ConnectionKey, state, false, Apply, Ack), Is.EqualTo(WorkspaceReturnResult.WaitingForSnapshot), "cached matching state is not a current watermark");
        Assert.That((applied, acknowledged), Is.EqualTo((0, 0)));
        Assert.That(recovery.Return(json, record.ConnectionKey, state, true, Apply, Ack), Is.EqualTo(WorkspaceReturnResult.Acknowledged));
        Assert.That((applied, acknowledged), Is.EqualTo((1, 1)));
    }

    [Test]
    public void FailedSaveNeverAcknowledgesAndFailedAcknowledgementNeverReplaysOverNewerWords()
    {
        var record = Record(); var state = State(record); var json = HalcyonicJson.Serialize(record);
        var recovery = Recovery();
        Directory.CreateDirectory(path + ".new");
        var applied = 0; var acks = 0;
        bool Apply(WorkspaceContinuation _) { applied++; return true; }
        bool Ack(string _) { acks++; return false; }
        Assert.That(recovery.Return(json, record.ConnectionKey, state, true, Apply, Ack), Is.EqualTo(WorkspaceReturnResult.StorageFailed));
        Assert.That(acks, Is.Zero);
        Directory.Delete(path + ".new");
        Assert.That(recovery.Return(json, record.ConnectionKey, state, true, Apply, Ack), Is.EqualTo(WorkspaceReturnResult.Applied));
        var newer = Record(); newer.Instruction!.Text = "Newer ordinary words";
        Assert.That(recovery.Keep(new[] { newer }, newer.HandoffId), Is.True);
        Assert.That(recovery.Return(json, record.ConnectionKey, state, true, Apply, Ack), Is.EqualTo(WorkspaceReturnResult.Applied));
        Assert.That(applied, Is.EqualTo(1), "neither failed save nor failed acknowledgement replays the return within this process");
        Assert.That(Recovery().Views.Single().Instruction!.Text, Is.EqualTo("Newer ordinary words"));
        var recreated = Recovery();
        Assert.That(recreated.Return(json, record.ConnectionKey, state, true,
            _ => throw new AssertionException("recreation must not replay old native words over the durable newer draft"),
            id => id == record.HandoffId), Is.EqualTo(WorkspaceReturnResult.Acknowledged));
    }

    [Test]
    public void FailedImportSaveRetriesCurrentWordsWithoutReapplyingNativeText()
    {
        var record = Record(); var state = State(record); var writesSucceed = false; var applied = 0; var acks = 0;
        var local = Record();
        var recovery = new WorkspaceViewRecovery(() => (true, null), _ => writesSucceed);
        bool Apply(WorkspaceContinuation view) { applied++; local.Instruction!.Text = view.Instruction!.Text; return true; }
        bool Ack(string _) { acks++; return true; }
        var json = HalcyonicJson.Serialize(record);
        Assert.That(recovery.Return(json, record.ConnectionKey, state, true, Apply, Ack, _ => local), Is.EqualTo(WorkspaceReturnResult.StorageFailed));
        local.Instruction!.Text = "The wearer edited these local words after the failed save.";
        Assert.That(recovery.Return(json, record.ConnectionKey, state, true, Apply, Ack, _ => local), Is.EqualTo(WorkspaceReturnResult.StorageFailed));
        Assert.That(applied, Is.EqualTo(1)); Assert.That(acks, Is.Zero);
        writesSucceed = true;
        Assert.That(recovery.Return(json, record.ConnectionKey, state, true, Apply, Ack, _ => local), Is.EqualTo(WorkspaceReturnResult.Acknowledged));
        Assert.That(recovery.Views.Single().Instruction!.Text, Is.EqualTo(local.Instruction!.Text));
        Assert.That(applied, Is.EqualTo(1)); Assert.That(acks, Is.EqualTo(1));
        recovery = new WorkspaceViewRecovery(() => (false, null), _ => throw new AssertionException("failed read forbids write"));
        Assert.That(recovery.Return(json, record.ConnectionKey, state, true,
            _ => throw new AssertionException("unavailable store must not apply"), _ => false), Is.EqualTo(WorkspaceReturnResult.StorageFailed));
    }

    [Test]
    public void AReceiptWithAnotherContextNeverDiscardsAnUnappliedReturn()
    {
        var record = Record(); var state = State(record);
        var recovery = Recovery();
        var foreign = Record(); foreign.ConnectionKey = new string('b', 64);
        Assert.That(recovery.Keep(new[] { foreign }, foreign.HandoffId), Is.True);
        Assert.That(recovery.Return(HalcyonicJson.Serialize(record), record.ConnectionKey, state, true,
            _ => throw new AssertionException("same ID with another context cannot be applied"),
            _ => throw new AssertionException("foreign receipt cannot discard a return")), Is.EqualTo(WorkspaceReturnResult.NeedsReview));
    }

    [Test]
    public void ForeignOrStaleExecutionRemainsRecoveryDataWithoutRetargetingOrAcknowledgement()
    {
        var record = Record(); var state = State(record);
        var recovery = Recovery();
        Assert.That(recovery.Keep(new[] { record }, record.HandoffId), Is.True);
        var before = File.ReadAllBytes(path);
        state.Workstreams[record.WorkstreamId!].CurrentExecutionId = "0192f1a0-0000-7000-8000-000000000099";
        Assert.That(recovery.Return(HalcyonicJson.Serialize(record), record.ConnectionKey, state, true,
            _ => throw new AssertionException("stale run must not restore"), _ => throw new AssertionException("stale return must not be consumed")), Is.EqualTo(WorkspaceReturnResult.NeedsReview));
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        Assert.That(recovery.Views.Single().ExecutionId, Is.EqualTo(record.ExecutionId));
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    public enum WorkspaceReturnResult { WaitingForSnapshot, NeedsReview, StorageFailed, Applied, Acknowledged }

    /// <summary>Durable ordinary views. Recovery data never grants command, reading or confirmation authority.</summary>
    public sealed class WorkspaceViewRecovery
    {
        private readonly Func<string, bool> write;
        private WorkspaceViewStore store = new WorkspaceViewStore();
        private readonly Dictionary<string, WorkspaceContinuation> pendingApplied = new Dictionary<string, WorkspaceContinuation>(StringComparer.Ordinal);
        public bool Available { get; }
        public IReadOnlyList<WorkspaceContinuation> Views => store.Views;
        public string? Selected => store.Selected;
        public bool IsSaved(WorkspaceContinuation record) => Available && store.Views.Any(view =>
            view.HandoffId == record.HandoffId && SameContext(view, record) && view.Surface == record.Surface && view.Section == record.Section && view.Page == record.Page
            && (view.Instruction == null && record.Instruction == null || view.Instruction != null && record.Instruction != null
                && view.Instruction.Text == record.Instruction.Text && view.Instruction.Spoken == record.Instruction.Spoken && view.Instruction.Part == record.Instruction.Part)
            && (view.Request == null && record.Request == null || view.Request != null && record.Request != null
                && view.Request.RequestId == record.Request.RequestId && view.Request.Text == record.Request.Text
                && view.Request.Spoken == record.Request.Spoken && view.Request.Part == record.Request.Part));
        public WorkspaceViewRecovery(Func<(bool Succeeded, string? Json)> read, Func<string, bool> write)
        {
            this.write = write;
            var loaded = read();
            if (!loaded.Succeeded) return;
            if (loaded.Json == null) { Available = true; return; }
            Available = WorkspaceViewStorage.TryParse(loaded.Json, out var parsed);
            if (parsed != null) store = parsed;
        }

        public bool Keep(IEnumerable<WorkspaceContinuation> records, string? selected)
        {
            if (!Available) return false;
            var candidate = new WorkspaceViewStore { Selected = selected, Views = store.Views.ToList() };
            foreach (var record in records)
            {
                if (!WorkspaceContinuity.TryParse(HalcyonicJson.Serialize(record), out var copy) || copy == null) return false;
                var index = candidate.Views.FindIndex(view => view.HandoffId == record.HandoffId);
                if (index < 0) candidate.Views.Add(copy); else candidate.Views[index] = copy;
            }
            if (!WorkspaceViewStorage.TrySerialize(candidate, out var serialized)) return false;
            if (serialized == HalcyonicJson.Serialize(store)) return true;
            if (!write(serialized!)) return false;
            store = candidate;
            return true;
        }

        public bool Forget(string handoffId)
        {
            if (!Available) return false;
            var candidate = new WorkspaceViewStore
            {
                Selected = store.Selected == handoffId ? null : store.Selected,
                Views = store.Views.Where(view => view.HandoffId != handoffId).ToList(),
            };
            if (!WorkspaceViewStorage.TrySerialize(candidate, out var serialized) || !write(serialized!)) return false;
            store = candidate;
            return true;
        }

        /// <summary>Apply only after a fresh watermark, durably save, then acknowledge the exact native return.</summary>
        public WorkspaceReturnResult Return(string json, string key, ClientProjection? state, bool snapshotCurrent,
            Func<WorkspaceContinuation, bool> apply, Func<string, bool> acknowledge,
            Func<WorkspaceContinuation, WorkspaceContinuation?>? capture = null)
        {
            if (!snapshotCurrent || state?.Journal == null) return WorkspaceReturnResult.WaitingForSnapshot;
            if (!Available) return WorkspaceReturnResult.StorageFailed;
            if (!WorkspaceContinuity.TryParse(json, out var record) || record == null
                || !WorkspaceContinuity.MatchesCurrent(record, key, state)) return WorkspaceReturnResult.NeedsReview;
            var receipt = store.Views.FirstOrDefault(view => view.HandoffId == record.HandoffId);
            if (receipt != null && SameContext(receipt, record))
                return acknowledge(record.HandoffId) ? WorkspaceReturnResult.Acknowledged : WorkspaceReturnResult.Applied;
            if (receipt != null) return WorkspaceReturnResult.NeedsReview;
            if (pendingApplied.TryGetValue(record.HandoffId, out var pending))
            {
                if (!SameContext(pending, record)) return WorkspaceReturnResult.NeedsReview;
            }
            else
            {
                if (!apply(record)) return WorkspaceReturnResult.NeedsReview;
                pending = record;
                pendingApplied.Add(record.HandoffId, pending);
            }
            var applied = capture == null ? pending : capture(pending);
            if (applied == null || applied.HandoffId != record.HandoffId || !SameContext(applied, record)) return WorkspaceReturnResult.NeedsReview;
            if (!Keep(new[] { applied }, record.HandoffId)) return WorkspaceReturnResult.StorageFailed;
            pendingApplied.Remove(record.HandoffId);
            return acknowledge(record.HandoffId) ? WorkspaceReturnResult.Acknowledged : WorkspaceReturnResult.Applied;
        }

        private static bool SameContext(WorkspaceContinuation first, WorkspaceContinuation second) =>
            first.ConnectionKey == second.ConnectionKey && first.JournalId == second.JournalId
            && first.ProjectId == second.ProjectId && first.WorkstreamId == second.WorkstreamId && first.ExecutionId == second.ExecutionId;
    }
}

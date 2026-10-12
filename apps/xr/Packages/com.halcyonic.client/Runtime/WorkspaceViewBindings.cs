#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>Retained ordinary views, including unsaved snapshots of an execution that has left its editor.</summary>
    public sealed class WorkspaceViewBindings
    {
        private readonly Func<FileColumn, WorkspaceContinuation?> make;
        private readonly Action changed;
        private readonly Dictionary<FileColumn, Binding> files = new Dictionary<FileColumn, Binding>();
        private readonly Dictionary<string, WorkspaceContinuation> retired = new Dictionary<string, WorkspaceContinuation>(StringComparer.Ordinal);
        public WorkspaceViewBindings(Func<FileColumn, WorkspaceContinuation?> make, Action changed)
        { this.make = make; this.changed = changed; }
        public int Count => files.Count + retired.Count;
        public bool ContainsKey(FileColumn file) => files.ContainsKey(file);
        public bool TryGetValue(FileColumn file, out WorkspaceContinuation record)
        {
            if (files.TryGetValue(file, out var binding)) { record = Copy(binding.Record); return true; }
            record = null!; return false;
        }
        public void Bind(FileColumn file, WorkspaceContinuation origin)
        {
            if (files.ContainsKey(file)) return;
            var binding = new Binding(Copy(origin));
            files.Add(file, binding);
            binding.Changed = changed;
            binding.Leaving = (execution, draft) =>
            {
                if (!files.TryGetValue(file, out var current) || current != binding || binding.Record.ExecutionId != execution) return;
                // This memory copy is independent of whether the subsequent durable write succeeds.
                binding.Record.Instruction = Copy(draft);
                changed();
            };
            binding.RequestLeaving = (execution, draft) =>
            {
                if (!files.TryGetValue(file, out var current) || current != binding || binding.Record.ExecutionId != execution) return;
                binding.Record.Request = Copy(draft);
                changed();
            };
            file.RequestDraftLeaving += binding.RequestLeaving;
            file.Changed += binding.Changed;
            file.OrdinaryDraftLeaving += binding.Leaving;
        }
        public void Set(FileColumn file, WorkspaceContinuation origin)
        {
            if (!files.TryGetValue(file, out var binding)) { Bind(file, origin); return; }
            if (binding.Record.HandoffId != origin.HandoffId) retired[binding.Record.HandoffId] = Copy(binding.Record);
            binding.Record = Copy(origin);
        }
        public void Drop(FileColumn file, bool preserve)
        {
            if (!files.TryGetValue(file, out var binding)) return;
            files.Remove(file);
            if (preserve) retired[binding.Record.HandoffId] = Copy(binding.Record);
            file.Changed -= binding.Changed;
            file.OrdinaryDraftLeaving -= binding.Leaving;
            file.RequestDraftLeaving -= binding.RequestLeaving;
        }
        public IReadOnlyList<WorkspaceContinuation> Capture()
        {
            foreach (var pair in files.ToList())
            {
                if (!files.TryGetValue(pair.Key, out var binding) || binding != pair.Value) continue;
                var file = pair.Key;
                if (binding.Record.ExecutionId != file.Now?.Execution?.ExecutionId)
                {
                    retired[binding.Record.HandoffId] = Copy(binding.Record);
                    var next = make(file);
                    if (next == null) continue;
                    binding.Record = Copy(next);
                }
                binding.Record.Surface = file.Surface;
                binding.Record.Section = (WorkspaceSection)file.Screen.Section;
                binding.Record.Page = file.Screen.Page;
                binding.Record.Instruction = file.ContinuationDraft();
                binding.Record.Request = file.ContinuationRequest();
            }
            return retired.Values.Concat(files.Values.Select(binding => binding.Record)).GroupBy(record => record.HandoffId)
                .Select(group => Copy(group.Last())).ToList();
        }
        public WorkspaceContinuation? Captured(FileColumn file)
        {
            Capture();
            return TryGetValue(file, out var record) ? record : null;
        }
        private sealed class Binding
        {
            public WorkspaceContinuation Record;
            public Action Changed = null!;
            public Action<string, WorkspaceInstructionDraft> Leaving = null!;
            public Action<string, WorkspaceRequestContinuation> RequestLeaving = null!;
            public Binding(WorkspaceContinuation record) => Record = record;
        }
        private static WorkspaceInstructionDraft Copy(WorkspaceInstructionDraft draft) => new WorkspaceInstructionDraft
        { Text = draft.Text, Spoken = draft.Spoken, Part = draft.Part };
        private static WorkspaceRequestContinuation Copy(WorkspaceRequestContinuation draft) => new WorkspaceRequestContinuation
        { RequestId = draft.RequestId, Text = draft.Text, Spoken = draft.Spoken, Part = draft.Part };
        private static WorkspaceContinuation Copy(WorkspaceContinuation record) => new WorkspaceContinuation
        {
            Version = record.Version, HandoffId = record.HandoffId, ConnectionKey = record.ConnectionKey, JournalId = record.JournalId,
            ProjectId = record.ProjectId, WorkstreamId = record.WorkstreamId, ExecutionId = record.ExecutionId, Section = record.Section,
            Surface = record.Surface, Page = record.Page, Instruction = record.Instruction == null ? null : Copy(record.Instruction),
            Request = record.Request == null ? null : Copy(record.Request),
        };
    }
}

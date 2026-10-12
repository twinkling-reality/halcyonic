#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    public sealed partial class MenuDirector
    {
        private WorkspaceViewRecovery? recovery;
        private readonly Dictionary<FileColumn, WorkspaceContinuation> viewBindings = new Dictionary<FileColumn, WorkspaceContinuation>();
        private string? recoveredScope;
        private WorkspaceContinuation? pendingReturn;
        private string? pendingReturnJson;
        private bool nativeReturnPending;
        private double nextReturnRead;
        private bool storageNoticeShown;
        private bool savingViews;
        private bool viewsDirty;
        private bool renewingViews;
        private bool storageFailed;
        private int SavedViewCount => (recovery?.Views.Count ?? 0) + (pendingReturn == null ? 0 : 1);
        private bool CurrentSnapshot => !Demonstration && Connected && setup.Session() is RealtimeSession session
            && session.SnapshotCurrent && ReferenceEquals(State, session.State);

        private bool TryKey(out string key)
        {
            key = "";
            try
            {
                if (setup.ConnectionTarget() is not ControlPlaneTarget target) return false;
                key = WorkspaceContinuity.ConnectionKey(target);
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        private void LoadViews()
        {
            // Renderer fixtures and demo playback never read or replace the person's private drafts.
            if (!Application.isPlaying || Demonstration) return;
            recovery = new WorkspaceViewRecovery(() =>
            {
                var read = AndroidPanelBridge.ReadWorkspaceViews();
                return (read.Status == AndroidPanelBridge.WorkspaceViewsStatus.Absent || read.Status == AndroidPanelBridge.WorkspaceViewsStatus.Available && read.Json != null, read.Status == AndroidPanelBridge.WorkspaceViewsStatus.Available ? read.Json : null);
            }, json => AndroidPanelBridge.WriteWorkspaceViews(json).Succeeded);
            storageFailed = !recovery.Available;
        }

        private void BindView(IMenuColumn column, WorkspaceContinuation? origin = null)
        {
            if (column is not FileColumn file || viewBindings.ContainsKey(file)) return;
            var record = origin ?? RecordFor(file);
            if (record == null) return;
            viewBindings.Add(file, record);
            viewsDirty = true;
            file.Changed += ViewsChanged;
            file.OrdinaryDraftLeaving += (execution, draft) =>
            {
                if (!viewBindings.TryGetValue(file, out var bound) || bound.ExecutionId != execution || recovery == null) return;
                var preserved = Copy(bound);
                preserved.Instruction = draft;
                if (recovery.Keep(new[] { preserved }, recovery.Selected)) viewBindings[file] = preserved;
                else storageFailed = true;
            };
        }

        private WorkspaceContinuation? RecordFor(FileColumn? file)
        {
            if (!TryKey(out var key) || State?.Journal == null) return null;
            var task = file?.Now?.Character.WorkstreamId;
            if (task != null && !State.Workstreams.ContainsKey(task)) return null;
            var record = new WorkspaceContinuation
            {
                HandoffId = Guid.NewGuid().ToString("D"), ConnectionKey = key, JournalId = State.Journal.JournalId,
                ProjectId = task == null ? null : State.Workstreams[task].ProjectId, WorkstreamId = task,
                ExecutionId = file?.Now?.Execution?.ExecutionId,
                Section = file == null ? WorkspaceSection.Activity : (WorkspaceSection)file.Screen.Section,
                Page = file?.Screen.Page ?? 0, Instruction = file?.ContinuationDraft(),
            };
            if (!WorkspaceContinuity.TryParse(HalcyonicJson.Serialize(record), out _)) { storageFailed = true; return null; }
            return record;
        }

        private static WorkspaceContinuation Copy(WorkspaceContinuation record)
        {
            if (!WorkspaceContinuity.TryParse(HalcyonicJson.Serialize(record), out var copy) || copy == null)
                throw new InvalidOperationException("Workspace view does not satisfy its local contract.");
            return copy;
        }

        private WorkspaceContinuation Captured(FileColumn file, WorkspaceContinuation origin)
        {
            var record = Copy(origin);
            // An old ordinary draft stays bound to its original run, even after that run disappears.
            if (file.Now?.Execution?.ExecutionId != origin.ExecutionId) return record;
            record.Section = (WorkspaceSection)file.Screen.Section;
            record.Page = file.Screen.Page;
            record.Instruction = file.ContinuationDraft();
            return record;
        }

        private void ViewsChanged() { viewsDirty = true; KeepViews(); }

        private void KeepViews()
        {
            if (!viewsDirty || savingViews || recovery == null || Demonstration) return;
            savingViews = true;
            try
            {
                foreach (var column in memory.Files.Values.ToList()) BindView(column);
                var records = viewBindings.Select(pair => Captured(pair.Key, pair.Value)).ToList();
                var selected = navigator.Beside is FileColumn file && viewBindings.TryGetValue(file, out var origin) ? origin.HandoffId : recoveredScope == null ? recovery.Selected : null;
                if (!recovery.Keep(records, selected)) { storageFailed = true; return; }
                foreach (var pair in viewBindings.ToList()) viewBindings[pair.Key] = records.First(record => record.HandoffId == pair.Value.HandoffId);
                storageFailed = false;
                viewsDirty = false;
            }
            finally { savingViews = false; }
        }

        private void ViewDropped(IMenuColumn column)
        {
            if (column is not FileColumn file || !viewBindings.TryGetValue(file, out var origin)) return;
            file.Changed -= ViewsChanged;
            viewBindings.Remove(file);
            viewsDirty = true;
            // A deliberate Close in the same current context discards its view. Renewal retains recovery data.
            if (!renewingViews && recovery != null && CurrentSnapshot && TryKey(out var key) && State != null
                && WorkspaceContinuity.MatchesCurrent(origin, key, State))
                storageFailed = !recovery.Forget(origin.HandoffId);
        }

        private bool IsCurrent(WorkspaceContinuation record) => CurrentSnapshot && TryKey(out var key)
            && State != null && WorkspaceContinuity.MatchesCurrent(record, key, State);

        private bool RestoreView(WorkspaceContinuation record, bool select)
        {
            if (!IsCurrent(record)) return false;
            if (record.WorkstreamId == null) { if (select) Open(MenuPlace.Projects); return true; }
            var column = memory.FileFor(record.JournalId, record.WorkstreamId, () => setup.File(Bound(), record.WorkstreamId));
            if (column is not FileColumn file || !file.RestoreContinuation(record)) return false;
            if (viewBindings.ContainsKey(file)) viewBindings[file] = Copy(record); else BindView(file, Copy(record));
            if (select)
            {
                if (setup.Presentation.Minimized) setup.Presentation.Restore();
                navigator.ShowBeside(file, record.WorkstreamId);
            }
            return true;
        }

        private void RecoverViews()
        {
            if (recovery == null || !CurrentSnapshot || !TryKey(out var key) || State?.Journal == null) return;
            var scope = key + State.Journal.JournalId;
            if (recoveredScope != scope)
            {
                recoveredScope = scope;
                foreach (var record in recovery.Views.OrderBy(record => record.HandoffId != recovery.Selected).ToList())
                {
                    if (!IsCurrent(record) || record.WorkstreamId == null || memory.Files.ContainsKey(record.WorkstreamId)) continue;
                    RestoreView(record, record.HandoffId == recovery.Selected);
                }
            }
            if (Now < nextReturnRead) return;
            nextReturnRead = Now + 0.5;
            nativeReturnPending = AndroidPanelBridge.TryPeekPendingReturn(out var json);
            if (nativeReturnPending)
            {
                var result = recovery.Return(json, key, State, CurrentSnapshot, record => RestoreView(record, select: true),
                    AndroidPanelBridge.AcknowledgePendingReturn, record =>
                    {
                        if (record.WorkstreamId == null) return record;
                        if (!memory.Files.TryGetValue(record.WorkstreamId, out var column) || column is not FileColumn file
                            || !viewBindings.TryGetValue(file, out var origin) || origin.HandoffId != record.HandoffId) return null;
                        return Captured(file, origin);
                    });
                if (result == WorkspaceReturnResult.NeedsReview)
                {
                    if (pendingReturnJson != json)
                    {
                        pendingReturnJson = json;
                        WorkspaceContinuity.TryParse(json, out pendingReturn);
                        dirty = true;
                    }
                }
                else if (result == WorkspaceReturnResult.StorageFailed) storageFailed = true;
                else if (result == WorkspaceReturnResult.Acknowledged) { pendingReturn = null; pendingReturnJson = null; nativeReturnPending = false; dirty = true; }
            }
            if (storageFailed && !storageNoticeShown && placement == null && !Away)
            {
                storageNoticeShown = ReportLocal("The workspace drafts could not be saved. Existing saved drafts remain untouched. Keep this workspace open before switching apps.");
            }
        }

        private bool CanOpenOtherApps
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return CurrentSnapshot && recovery?.Available == true && !storageFailed && !nativeReturnPending && placement == null;
#else
                return false;
#endif
            }
        }

        private void OpenOtherApps()
        {
            if (!OrganizationStanding(MenuColumn.Menu, null, null) || !CanOpenOtherApps) return;
            KeepViews();
            if (storageFailed) { ReportLocal("Save your workspace drafts before switching apps."); return; }
            var record = navigator.Beside is FileColumn file && viewBindings.TryGetValue(file, out var origin) ? Captured(file, origin) : RecordFor(null);
            if (record == null || !IsCurrent(record)) { ReportLocal("Wait for the current workspace to reconnect before switching apps."); return; }
            // This UUID is persisted in Unity only after return application, making it a durable idempotency receipt.
            record.HandoffId = Guid.NewGuid().ToString("D");
            var result = AndroidPanelBridge.OpenPanel(HalcyonicJson.Serialize(record), inHome: false);
            if (!result.Succeeded) { ReportLocal(result.Reason); return; }
            SuspendWorkspace();
        }

        public void OpenSavedViews()
        {
            if (recovery == null) return;
            var records = recovery.Views.ToList();
            if (pendingReturn != null)
            {
                records.RemoveAll(record => record.HandoffId == pendingReturn.HandoffId);
                records.Add(pendingReturn);
            }
            OpenLocal(new WorkspaceRecoveryColumn(this, records, IsCurrent, record => RestoreView(record, select: true),
                pendingReturn == null ? null : record =>
                {
                    if (record.HandoffId != pendingReturn?.HandoffId || !recovery.Keep(new[] { record }, recovery.Selected)) return false;
                    if (!AndroidPanelBridge.AcknowledgePendingReturn(record.HandoffId)) return false;
                    pendingReturn = null;
                    pendingReturnJson = null;
                    nativeReturnPending = false;
                    return true;
                }, pendingReturn?.HandoffId));
        }

        private void OnApplicationPause(bool paused) { if (paused) KeepViews(); }
    }
}

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
        private WorkspaceViewBindings viewBindings = null!;
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
        private int SavedViewCount => (recovery?.Views.Count ?? 0) + viewBindings.Count + (pendingReturn == null ? 0 : 1);
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
            if (column is not FileColumn file) return;
            file.OfferWorkspaceRequests(() => OpenWorkspaceRequest(file));
            if (viewBindings.ContainsKey(file)) return;
            var record = origin ?? RecordFor(file);
            if (record == null) return;
            viewBindings.Bind(file, record);
            viewsDirty = true;
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
                Surface = file?.Surface ?? WorkspaceSurface.Task, Page = file?.Screen.Page ?? 0,
                Instruction = file?.ContinuationDraft(), Request = file?.ContinuationRequest(),
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

        private WorkspaceContinuation Captured(FileColumn file, WorkspaceContinuation origin) => viewBindings.Captured(file) ?? origin;

        private void ViewsChanged() { viewsDirty = true; KeepViews(); }

        private void KeepViews()
        {
            if (!viewsDirty || savingViews || recovery == null || Demonstration) return;
            savingViews = true;
            try
            {
                foreach (var column in memory.Files.Values.ToList()) BindView(column);
                var records = viewBindings.Capture();
                var selected = SelectedFile() is FileColumn file && viewBindings.TryGetValue(file, out var origin) ? origin.HandoffId : recoveredScope == null ? recovery.Selected : null;
                if (!recovery.Keep(records, selected)) { storageFailed = true; return; }
                storageFailed = false;
                viewsDirty = false;
            }
            finally { savingViews = false; }
        }

        private void ViewDropped(IMenuColumn column)
        {
            if (column is not FileColumn file || !viewBindings.TryGetValue(file, out var origin)) return;
            var discard = !renewingViews && CurrentSnapshot && TryKey(out var key) && State?.Journal != null
                && origin.ConnectionKey == key && origin.JournalId == State.Journal.JournalId && origin.WorkstreamId != null
                && State.Workstreams.TryGetValue(origin.WorkstreamId, out var work) && work.ProjectId == origin.ProjectId
                && work.CurrentExecutionId == origin.ExecutionId;
            viewBindings.Drop(file, preserve: !discard);
            viewsDirty = true;
            // A deliberate Close in the same current context discards its view. Renewal retains recovery data.
            if (discard && recovery != null) storageFailed = !recovery.Forget(origin.HandoffId);
        }

        private bool IsCurrent(WorkspaceContinuation record) => CurrentSnapshot && TryKey(out var key)
            && State != null && WorkspaceContinuity.MatchesCurrent(record, key, State);

        private bool RestoreView(WorkspaceContinuation record, bool select, bool reopen = false)
        {
            if (!IsCurrent(record)) return false;
            if (record.WorkstreamId == null) { if (select) navigator.RecoverMenu(MenuPlace.Projects, setup.Presentation, reopen); return true; }
            var column = memory.FileFor(record.JournalId, record.WorkstreamId, () => setup.File(Bound(), record.WorkstreamId));
            if (column is not FileColumn file || !file.RestoreContinuation(record)) return false;
            if (viewBindings.ContainsKey(file)) viewBindings.Set(file, Copy(record)); else BindView(file, Copy(record));
            if (select)
            {
                if (record.Surface == WorkspaceSurface.Request)
                    navigator.RecoverBeside(RequestViewFor(file, record), record.WorkstreamId, setup.Presentation, reopen);
                else navigator.RecoverBeside(file, record.WorkstreamId, setup.Presentation, reopen);
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
                storageNoticeShown = ReportLocal(recovery.Available
                    ? "The latest draft could not be saved. Keep this workspace open before switching apps."
                    : "Saved drafts could not be read. Keep this workspace open before switching apps.");
            }
        }

        private FileColumn? SelectedFile() => navigator.Beside as FileColumn
            ?? (navigator.Beside is WorkspaceRequestColumn request && navigator.BesideTask != null
                && memory.Files.TryGetValue(navigator.BesideTask, out var column) && column is FileColumn file && file.RequestView == request ? file : null);

        private WorkspaceRequestColumn RequestViewFor(FileColumn file, WorkspaceContinuation record)
        {
            var view = file.RequestFor(record, () => IsCurrent(record) && State?.Executions.TryGetValue(record.ExecutionId!, out var execution) == true
                && !execution.PendingQuestions.Any(question => question.Prompts.Any(prompt => prompt.Secret)));
            view.InputContextLeft = () => voice?.Left(view);
            view.BeforeSubmit = _ =>
            {
                ViewsChanged();
                var captured = viewBindings.Captured(file);
                return CurrentSnapshot && !storageFailed && captured != null && recovery?.IsSaved(captured) == true;
            };
            view.BackToTask = () => { file.Surface = WorkspaceSurface.Task; navigator.ShowBeside(file, record.WorkstreamId); ViewsChanged(); };
            return view;
        }

        private void OpenWorkspaceRequest(FileColumn file)
        {
            var record = viewBindings.Captured(file) ?? RecordFor(file);
            if (record == null || record.WorkstreamId == null || record.ExecutionId == null)
            { ReportLocal("Reconnect to this task before opening its workspace request."); return; }
            file.Surface = WorkspaceSurface.Request;
            record.Surface = WorkspaceSurface.Request;
            navigator.ShowBeside(RequestViewFor(file, record), record.WorkstreamId);
            ViewsChanged();
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
            var record = SelectedFile() is FileColumn file && viewBindings.TryGetValue(file, out var origin) ? Captured(file, origin) : RecordFor(null);
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
            var records = recovery.Views.Concat(viewBindings.Capture()).GroupBy(record => record.HandoffId)
                .Select(group => group.Last()).ToList();
            if (pendingReturn != null)
            {
                records.RemoveAll(record => record.HandoffId == pendingReturn.HandoffId);
                records.Add(pendingReturn);
            }
            OpenLocal(new WorkspaceRecoveryColumn(this, records, IsCurrent, record => RestoreView(record, select: true, reopen: true),
                pendingReturn == null ? null : record =>
                {
                    if (record.HandoffId != pendingReturn?.HandoffId || !recovery.Keep(new[] { record }, recovery.Selected)) return false;
                    if (!AndroidPanelBridge.AcknowledgePendingReturn(record.HandoffId)) return false;
                    pendingReturn = null;
                    pendingReturnJson = null;
                    nativeReturnPending = false;
                    return true;
                }, pendingReturn?.HandoffId, recovery.IsSaved));
        }

        private void OnApplicationPause(bool paused) { if (paused) KeepViews(); }
    }
}

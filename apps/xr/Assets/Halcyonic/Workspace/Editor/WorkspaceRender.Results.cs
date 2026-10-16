#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>Production Results navigation and actual TMP paging, with explicit synthetic session/HTTP seams.</summary>
        public static void ResultsCheck()
        {
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "ResultsRenders", variant));
                Directory.CreateDirectory(folder);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var found = new List<string>();
                try
                {
                    ViewField.Current = FieldChecks.Quest3S;
                    foreach (var shelf in new[] { false, true })
                    foreach (var desk in new[] { false, true })
                        found.AddRange(RenderTaskResults((shelf ? "shelf" : "normal") + (desk ? " desk" : " far"), folder, shelf, desk));
                }
                finally { ViewField.Current = null; KeepFontAssetsAsCommitted(); }
                foreach (var failure in found) Debug.LogError("Halcyonic: results render: " + failure);
                if (found.Count == 0) Debug.Log("Halcyonic: results render: every check passed; " + folder);
                return found;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        /// <summary>Retained task selection through the actual spatial director and scoped result reader.</summary>
        public static void RetainedTaskSurfaceCheck()
        {
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "RetainedTaskRenders", variant));
                Directory.CreateDirectory(folder);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                try
                {
                    ViewField.Current = FieldChecks.Quest3S;
                    var found = RenderTaskResults("retained task", folder, false, false, spatialWorkspace: true).ToList();
                    foreach (var failure in found) Debug.LogError("Halcyonic: retained task: " + failure);
                    if (found.Count == 0) Debug.Log("Halcyonic: retained task: every check passed; " + folder);
                    return found;
                }
                finally { ViewField.Current = null; KeepFontAssetsAsCommitted(); }
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private const string ResultsJournal = "0192f1a0-0000-7000-8000-000000000001", ResultsProject = "0192f1a0-0000-7000-8000-000000000101",
            ResultsTask = "0192f1a0-0000-7000-8000-000000000102", ResultsRun = "0192f1a0-0000-7000-8000-000000000103",
            ResultsEvent = "0192f1a0-0000-7000-8000-000000000104";
        private const string ResultsPatchEvent = "0192f1a0-0000-7000-8000-000000000106";
        private const string OtherResultsTask = "0192f1a0-0000-7000-8000-000000000107", OtherResultsRun = "0192f1a0-0000-7000-8000-000000000108";

        private sealed class ResultsHttpWitness : HttpMessageHandler
        {
            public readonly List<Uri> Reads = new List<Uri>();
            public ToolResultsAvailability Availability = ToolResultsAvailability.Available;
            public ToolResultResponse? Patch;
            public ToolResultResponse Result = new ToolResultResponse
            {
                JournalId = ResultsJournal, ProjectId = ResultsProject, WorkstreamId = ResultsTask, ExecutionId = ResultsRun,
                Summary = new ToolResultSummary { EventId = ResultsEvent, Position = 5, OccurredAt = Time, RuntimeId = "render",
                    Kind = ToolResultSummaryKind.Command, Completion = ToolResultCompletion.Completed, ExitCode = 0,
                    BodyAvailable = true, RetainedComplete = true, Title = "node check.js" },
                Result = new CommandToolResult { ToolCallId = "simulated-command", TurnId = "simulated-turn", Completion = ToolResultCompletion.Completed,
                    ExitCode = 0, Redaction = new ToolResultRedaction(), Output = new ToolResultBody
                    { Availability = ToolResultBodyAvailability.Available, Complete = true, Truncation = ToolResultBodyTruncation.None,
                        Text = "\n  first\r\n\n    <b>literal</b>\\n\n  \u001B[31m\u202E\té\n" + string.Concat(Enumerable.Repeat("    preserved indentation\n", 60)) + "\n" } },
            };
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                if (request.Method != HttpMethod.Get || request.RequestUri == null) throw new InvalidOperationException("The Results witness only permits GET.");
                Reads.Add(request.RequestUri);
                if (request.RequestUri.AbsolutePath == "/api/workspace/agent")
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(HalcyonicJson.Serialize(new AvailableWorkspaceAgent { Model = "Synthetic local agent" })) });
                if (request.RequestUri.AbsolutePath.StartsWith("/api/workspace/requests/", StringComparison.Ordinal)
                    || request.RequestUri.AbsolutePath.StartsWith("/api/workspace/commands/", StringComparison.Ordinal))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                var expected = "?journal_id=" + ResultsJournal + "&project_id=" + ResultsProject + "&workstream_id=" + ResultsTask;
                if (request.RequestUri.Query != expected) throw new InvalidOperationException("Results lost its original scope.");
                var path = "/api/executions/" + ResultsRun + "/results";
                object value = request.RequestUri.AbsolutePath == path ? new ToolResultsResponse
                { JournalId = ResultsJournal, ProjectId = ResultsProject, WorkstreamId = ResultsTask, ExecutionId = ResultsRun,
                    Availability = Availability, Total = Patch == null ? 1 : 2, Earlier = 0,
                    Results = Patch == null ? new List<ToolResultSummary> { Result.Summary } : new List<ToolResultSummary> { Patch.Summary, Result.Summary } }
                    : request.RequestUri.AbsolutePath == path + "/" + ResultsEvent ? (object)Result
                    : Patch != null && request.RequestUri.AbsolutePath == path + "/" + ResultsPatchEvent ? (object)Patch
                    : throw new InvalidOperationException("Results retargeted its original event.");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(HalcyonicJson.Serialize(value)) });
            }
        }

        private static ToolResultResponse ResultsPatchWitness()
        {
            ToolResultBody Kept(string text) => new ToolResultBody { Text = text, Availability = ToolResultBodyAvailability.Available, Complete = true, Truncation = ToolResultBodyTruncation.None };
            ToolResultFile File(ToolResultFileKind kind, string path, string patch) => new ToolResultFile
            { Kind = kind, Path = path, PathScope = ToolResultPathScope.ProjectRelative, PathComplete = true, Patch = Kept(patch) };
            var files = new List<ToolResultFile>
            {
                File(ToolResultFileKind.Add, "src/new.cs", "+ first\n+  <b>literal patch</b>\\n\r\n\n" + string.Concat(Enumerable.Repeat("+    preserved patch indentation\n", 20)) + "\n"),
                File(ToolResultFileKind.Update, "src/current.cs", "- old\n+ new\n"),
                File(ToolResultFileKind.Delete, "src/removed.cs", "- removed\n"),
                File(ToolResultFileKind.Update, "src/before.cs", "- before\n+ after\n"),
                new ToolResultFile { Kind = ToolResultFileKind.Update, PathScope = ToolResultPathScope.Unknown, PathComplete = false,
                    Patch = new ToolResultBody { Availability = ToolResultBodyAvailability.OmittedScope, Complete = false, Truncation = ToolResultBodyTruncation.Scope } },
                new ToolResultFile { Kind = ToolResultFileKind.Update, PathScope = ToolResultPathScope.OutsideProject, PathComplete = false,
                    Patch = new ToolResultBody { Availability = ToolResultBodyAvailability.OmittedScope, Complete = false, Truncation = ToolResultBodyTruncation.Scope } },
            };
            files[3].MovePath = "src/after.cs"; files[3].MovePathScope = ToolResultPathScope.ProjectRelative; files[3].MovePathComplete = true;
            return new ToolResultResponse { JournalId = ResultsJournal, ProjectId = ResultsProject, WorkstreamId = ResultsTask, ExecutionId = ResultsRun,
                Summary = new ToolResultSummary { EventId = ResultsPatchEvent, Position = 6, OccurredAt = Time, RuntimeId = "render",
                    Kind = ToolResultSummaryKind.FileChanges, Completion = ToolResultCompletion.Completed, FilesTotal = files.Count,
                    BodyAvailable = true, RetainedComplete = false, Title = "Recorded file changes" },
                Result = new FileChangesToolResult { ToolCallId = "simulated-patch", TurnId = "simulated-turn", Completion = ToolResultCompletion.Completed,
                    Redaction = new ToolResultRedaction(), FilesTotal = files.Count, ListComplete = true, Files = files } };
        }

        private static IEnumerable<string> RenderTaskResults(string name, string folder, bool withShelf, bool desk, bool spatialWorkspace = false)
        {
            var failures = new List<string>();
            var root = new GameObject("Synthetic Results consumer " + name);
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
            using var session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
            var handler = new ResultsHttpWitness();
            using var api = new ControlPlaneApi(new Uri("http://127.0.0.1:9/"), "render", handler);
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f); var camera = MakeCamera(root.transform, eyes, texture);
                var prototype = Work.Running();
                var work = HalcyonicJson.Deserialize<WorkstreamView>(HalcyonicJson.Serialize(prototype.Workstream));
                var execution = HalcyonicJson.Deserialize<ExecutionView>(HalcyonicJson.Serialize(prototype.Execution));
                work.WorkstreamId = ResultsTask; work.ProjectId = ResultsProject; work.CurrentExecutionId = ResultsRun; work.ExecutionIds = new List<string> { ResultsRun };
                execution.ExecutionId = ResultsRun; execution.WorkstreamId = ResultsTask; execution.ProjectId = ResultsProject;
                var otherWork = HalcyonicJson.Deserialize<WorkstreamView>(HalcyonicJson.Serialize(work));
                var otherExecution = HalcyonicJson.Deserialize<ExecutionView>(HalcyonicJson.Serialize(execution));
                otherWork.WorkstreamId = OtherResultsTask; otherWork.Title = "Review cache behavior";
                otherWork.CurrentExecutionId = OtherResultsRun; otherWork.ExecutionIds = new List<string> { OtherResultsRun };
                otherExecution.ExecutionId = OtherResultsRun; otherExecution.WorkstreamId = OtherResultsTask;
                var snapshot = new Snapshot { Journal = new JournalInfo { JournalId = ResultsJournal, Origin = JournalOrigin.Live }, Position = 5,
                    Projects = new List<ProjectView> { new ProjectView { ProjectId = ResultsProject, Name = "Storefront API", CreatedAt = Time, UpdatedAt = Time } },
                    Workstreams = new List<WorkstreamView> { work, otherWork }, Executions = new List<ExecutionView> { execution, otherExecution }, Runtimes = prototype.State.Runtimes.ToList() };
                var inbox = (ConcurrentQueue<object>)typeof(RealtimeSession).GetField("inbox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
                // Explicit synthetic transport seam: production Pump applies a real welcome/snapshot
                // and Live status. No socket, private store, model, native launcher or executor is used.
                inbox.Enqueue(new WelcomeMessage { Journal = snapshot.Journal, Head = snapshot.Position, ServerTime = Time });
                inbox.Enqueue(new SnapshotMessage { Snapshot = snapshot }); inbox.Enqueue(new ConnectionStatus(ConnectionPhase.Live)); session.Pump();
                if (!session.SnapshotCurrent) failures.Add(name + ": synthetic welcome/snapshot did not reach the actual current watermark");
                var targets = new List<CharacterTarget>(); var characters = new List<(CharacterView View, CharacterTarget Target)>();
                FileColumn? file = null; var sends = 0; var commands = new CommandFactory(client); var presentation = new WorkspacePresentationState();
                var target = ControlPlaneTarget.Local(new Uri("ws://127.0.0.1:9/realtime"), "render");
                WorkspacePresentation Present() => WorkspacePresenter.Present(work, session.State, new ActivityLog(), session.Status.IsLive);
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    SpatialWorkspace = spatialWorkspace,
                    Commands = commands, Presentation = presentation, ConnectionTarget = () => target, Session = () => session,
                    State = () => session.State, Connected = () => session.Status.IsLive, Api = () => api,
                    Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    Overview = () => WorkOverview.Of(session.State, new StageVisibility(), _ => true), Bar = place => TasksColumn.Bar(place, session.State),
                    Submit = (_, command) => { sends++; return Task.FromResult(new CommandAckMessage { CommandId = command.CommandId, Disposition = CommandAckDisposition.Accepted }); },
                    File = (host, id) => id == ResultsTask ? file = new FileColumn(host, Present, commands, () => null, _ => null, () => "")
                        : id == OtherResultsTask ? new FileColumn(host, () => WorkspacePresenter.Present(otherWork, session.State, new ActivityLog(), session.Status.IsLive), commands, () => null, _ => null, () => "") : null,
                    CharacterOf = id => targets.FirstOrDefault(item => item.View.WorkstreamId == id),
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, targets, desk ? EyeHeight - 0.46f : (float?)null, false),
                });
                void Draw()
                { for (var turn = 0; turn < 4; turn++) { director.UpdateForRender(); director.DrawNow(); director.Plane.Advance(MenuPlane.OpeningSeconds); } }
                void Shot(string step)
                {
                    Draw();
                    failures.AddRange(spatialWorkspace && director.Plane.Composition != null
                        ? SpatialState(name + " results " + step, folder, root, camera, texture, director, characters, eyes)
                        : PlaneState(name + " results " + step, folder, camera, texture, director.Plane, characters, eyes, null, lightLine: characters.Count > 0));
                }
                bool Press(string action, string? key = null)
                {
                    Draw();
                    var shown = director.Plane.Shown.LastOrDefault(each => each.View.Frame is MenuFrame frame
                        && (frame.Footer.All.Any(slot => slot.Prompt.Id == action && slot.Prompt.Available)
                        || frame.Lines.Any(line => line.Action == action && line.Key == key && line.Available)));
                    if (shown.View == null || !director.PressForRender(shown.Kind, action, key, shown.View.Frame, null))
                    { failures.Add(name + ": missing or rejected actual Results control " + action); return false; }
                    Draw(); return true;
                }
                bool TaskRoute(string action)
                {
                    for (var turn = 0; turn < 24; turn++)
                    {
                        Draw(); var frame = director.Plane.Showing(MenuColumn.File);
                        if (frame?.Lines.Any(line => line.Action == action && line.Available) == true) return Press(action);
                        if (frame?.Footer.All.Any(slot => slot.Prompt.Id == Footer.NextPage) != true
                            && frame?.Lines.Any(line => line.Action == Footer.NextPage) != true) break;
                        Press(Footer.NextPage);
                    }
                    failures.Add(name + ": task route was not reachable " + action); return false;
                }
                void Chrome(GlazeButton button, int pointer)
                { button.SettleForRender(); button.Target.PointerForRender(pointer, Oculus.Interaction.PointerEventType.Select, button.transform.position); button.Target.PointerForRender(pointer, Oculus.Interaction.PointerEventType.Unselect, button.transform.position); }
                Shot("projects before optional shelf");
                if (withShelf)
                { var agent = ConfirmedDirectorAgent(root.transform, director, eyes, Present().Character, failures, name); targets.Add(agent.Target); characters.Add(agent); Shot("confirmed shelf projects"); }
                director.OpenFile(ResultsTask); Shot("activity");
                var retained = file;
                var context = new WorkspaceContinuation { HandoffId = Guid.NewGuid().ToString("D"), ConnectionKey = WorkspaceContinuity.ConnectionKey(target),
                    JournalId = ResultsJournal, ProjectId = ResultsProject, WorkstreamId = ResultsTask, ExecutionId = ResultsRun, Section = WorkspaceSection.Activity,
                    Request = new WorkspaceRequestContinuation { Text = "Keep the exact request words", Spoken = true, Part = 2,
                        RequestId = "0192f1a0-0000-7000-8000-000000000105", PendingCommandId = "550e8400-e29b-41d4-a716-446655440000" } };
                if (retained == null || !retained.RestoreContinuation(context))
                { failures.Add(name + ": retained request fixture could not restore its exact draft"); return failures; }
                context.Instruction = new WorkspaceInstructionDraft { Text = string.Join(" ", Enumerable.Repeat("Preserve these ordinary instruction words while reading the recorded output.", 20)), Spoken = true, Part = 2 };
                if (!retained.RestoreContinuation(context))
                { failures.Add(name + ": ordinary instruction fixture could not restore its exact draft"); return failures; }
                Shot("retained ordinary instruction");
                var instructionBefore = retained.ContinuationDraft();
                if (instructionBefore == null)
                { failures.Add(name + ": restored ordinary instruction had no retained draft"); return failures; }
                // The spatial composer deliberately has no duplicate Results route. Hide preserves
                // its scoped words/page; only the full Activity route opens the actual reader.
                if (spatialWorkspace)
                {
                    if (!Press(FileScreens.HideInstruction)) return failures;
                    Shot("hidden ordinary instruction before results");
                    var hidden = retained.ContinuationDraft();
                    if (!retained.Screen.Instruction.Hidden || hidden == null || hidden.Text != instructionBefore.Text
                        || hidden.Spoken != instructionBefore.Spoken || hidden.Part != instructionBefore.Part)
                    { failures.Add(name + ": Hide composer changed its exact ordinary draft"); return failures; }
                }
                if (!TaskRoute(FileScreens.OpenResults)) return failures;
                Shot("list");
                if (director.Navigator.Beside is not TaskResultsColumn)
                { failures.Add(name + ": Activity did not open the production Results column"); return failures; }
                if (!Press(TaskResultsColumn.Read, ResultsEvent)) return failures;
                Shot("output first part");
                if (director.Navigator.Beside is not TaskResultsColumn drawingReader
                    || !ReferenceEquals(director.Plane.Showing(MenuColumn.File), drawingReader.Frame))
                { failures.Add(name + ": drawn output did not belong to the active Results reader"); return failures; }
                string DrawnBody()
                {
                    var labels = director.Plane.GetComponentsInChildren<TMP_Text>(false).Where(label => label.name == "Line 0 words").ToList();
                    if (labels.Count != 1) { failures.Add(name + ": expected one actual recorded body label"); return ""; }
                    labels[0].ForceMeshUpdate(); return labels[0].GetParsedText();
                }
                var expectedOutput = director.Plane.Showing(MenuColumn.File)?.Lines.Single().Runs.ForTextMeshPro().Text.Replace("\\\\", "\\");
                var drawnOutput = DrawnBody();
                var parts = 0;
                while (director.Plane.Showing(MenuColumn.File)?.Footer.All.Any(slot => slot.Prompt.Id == TaskResultsColumn.Next) == true && parts++ < 100)
                { Press(TaskResultsColumn.Next); Shot("output part " + parts); drawnOutput += DrawnBody(); }
                if (parts == 0 || parts >= 100) failures.Add(name + ": structured output never exercised a bounded full pager");
                if (drawnOutput != expectedOutput) failures.Add(name + ": actual output pages omitted, duplicated or changed recorded display text");
                if (!drawnOutput.Contains("<b>literal</b>\\n") || !drawnOutput.Contains("‹U+000D›")) failures.Add(name + ": actual TMP lost literal markup, backslash-n or CR from recorded CRLF");
                var finalFrame = director.Plane.Showing(MenuColumn.File); var finalRow = finalFrame?.Lines.Single().FromRow;
                if (finalFrame?.Lines.Single().Words.EndsWith("\n\n") != true) failures.Add(name + ": body construction lost blank/trailing LF");
                if (spatialWorkspace)
                {
                    Chrome(director.Plane.Context.gameObject.activeInHierarchy ? director.Plane.Context.WorkspaceTarget
                        : director.Plane.Chrome.Targets.Single(button => button.Label.text == "Workspace"), 881); Draw();
                    for (var turn = 0; turn < 20 && director.Plane.Front?.Lines.Any(line => line.Action == PlacementColumn.Minimize) != true; turn++)
                        if (!Press(Footer.NextPage)) break;
                    Press(PlacementColumn.Minimize);
                }
                else Chrome(director.Plane.Chrome.Targets.Single(button => button.Label.text == WorkspaceText.Minimize), 881);
                Shot("minimized");
                if (finalFrame != null && director.PressForRender(MenuColumn.File, TaskResultsColumn.Previous, null, finalFrame, null)) failures.Add(name + ": minimized result retained old-frame authority");
                if (director.Plane.Bar is MenuBarView dock) Chrome(dock.Target, 882); else failures.Add(name + ": global Results restore target missing");
                Shot("restored exact part");
                if (director.Plane.Showing(MenuColumn.File)?.Lines.Single().FromRow != finalRow) failures.Add(name + ": Minimize lost exact result page");
                var retainedReader = director.Navigator.Beside;
                director.OpenFile(OtherResultsTask); Draw();
                if (director.Navigator.Beside is not FileColumn otherFile || otherFile.Now?.Character.WorkstreamId != OtherResultsTask)
                    failures.Add(name + ": task switch did not select the actual second task");
                if (finalFrame != null && director.PressForRender(MenuColumn.File, TaskResultsColumn.Previous, null, finalFrame, null))
                    failures.Add(name + ": task switch retained the previous result's action authority");
                var readsBeforeReopen = handler.Reads.Count;
                director.OpenFile(ResultsTask); Shot("task switch restored exact part");
                if (!ReferenceEquals(director.Navigator.Beside, retainedReader)
                    || director.Plane.Showing(MenuColumn.File)?.Lines.Single().FromRow != finalRow
                    || handler.Reads.Count <= readsBeforeReopen)
                    failures.Add(name + ": task switch lost its reader/page or skipped fresh reads");
                director.OpenFile(OtherResultsTask); Draw();
                inbox.Enqueue(new ConnectionStatus(ConnectionPhase.WaitingToRetry)); session.Pump(); Draw();
                var readsBeforeOfflineReturn = handler.Reads.Count;
                director.OpenFile(ResultsTask); Shot("offline return retains navigation only");
                if (!ReferenceEquals(director.Navigator.Beside, retainedReader) || handler.Reads.Count != readsBeforeOfflineReturn
                    || director.Plane.Showing(MenuColumn.File)?.Lines.Any(line => line.Words.Contains("preserved indentation")) == true)
                    failures.Add(name + ": offline selection lost navigation, fetched data or published old body");
                inbox.Enqueue(new ConnectionStatus(ConnectionPhase.Synchronizing)); session.Pump(); Draw();
                if (handler.Reads.Count != readsBeforeOfflineReturn) failures.Add(name + ": synchronizing result reader fetched before a current snapshot");
                inbox.Enqueue(new WelcomeMessage { Journal = snapshot.Journal, Head = snapshot.Position, ServerTime = Time });
                inbox.Enqueue(new SnapshotMessage { Snapshot = snapshot }); inbox.Enqueue(new ConnectionStatus(ConnectionPhase.Live)); session.Pump();
                Shot("reconnected reader freshly restores exact part");
                if (!ReferenceEquals(director.Navigator.Beside, retainedReader) || handler.Reads.Count <= readsBeforeOfflineReturn
                    || director.Plane.Showing(MenuColumn.File)?.Lines.Single().FromRow != finalRow)
                    failures.Add(name + ": reconnect failed to freshly restore retained result navigation");
                Press(Footer.Close); handler.Availability = ToolResultsAvailability.Unavailable; Press(TaskResultsColumn.Refresh); Shot("saved results offline app");
                handler.Patch = ResultsPatchWitness(); Press(TaskResultsColumn.Refresh); Shot("recorded file change list");
                Press(TaskResultsColumn.Read, ResultsPatchEvent); Shot("recorded file choices");
                bool ChooseFile(string index)
                {
                    for (var turn = 0; turn < 32; turn++)
                    {
                        var frame = director.Plane.Showing(MenuColumn.File);
                        if (frame?.Lines.Any(line => line.Action == TaskResultsColumn.ReadFile && line.Key == index) == true) return Press(TaskResultsColumn.ReadFile, index);
                        if (frame?.Footer.All.Any(slot => slot.Prompt.Id == TaskResultsColumn.Next) != true) break;
                        Press(TaskResultsColumn.Next); Shot("file choice page " + turn);
                    }
                    failures.Add(name + ": selected file patch was not reachable through actual file choices"); return false;
                }
                ChooseFile("0"); Shot("recorded patch first part");
                var patchParts = 0; var drawnPatch = "";
                var expectedPatch = director.Plane.Showing(MenuColumn.File)?.Lines.Single().Runs.ForTextMeshPro().Text.Replace("\\\\", "\\");
                do
                {
                    drawnPatch += DrawnBody();
                    if (director.Plane.Showing(MenuColumn.File)?.Footer.All.Any(slot => slot.Prompt.Id == TaskResultsColumn.Next) != true) break;
                    Press(TaskResultsColumn.Next); Shot("recorded patch part " + ++patchParts);
                } while (patchParts < 100);
                if (drawnPatch != expectedPatch) failures.Add(name + ": actual patch pages omitted, duplicated or changed recorded display text");
                if (!drawnPatch.Contains("<b>literal patch</b>\\n") || patchParts == 0 || patchParts >= 100) failures.Add(name + ": actual TMP patch text did not exercise literal bounded paging");
                var retainedPatch = director.Navigator.Beside;
                var patchFrame = director.Plane.Showing(MenuColumn.File);
                var patchRow = patchFrame?.Lines.Single().FromRow;
                director.OpenFile(OtherResultsTask); Draw();
                var readsBeforePatchReturn = handler.Reads.Count(uri => uri.AbsolutePath.EndsWith("/" + ResultsPatchEvent));
                director.OpenFile(ResultsTask); Shot("task switch restored file patch");
                var returnedPatch = director.Plane.Showing(MenuColumn.File);
                if (!ReferenceEquals(director.Navigator.Beside, retainedPatch) || returnedPatch?.Lines.Single().FromRow != patchRow
                    || returnedPatch?.Lines.Single().Words != patchFrame?.Lines.Single().Words
                    || handler.Reads.Count(uri => uri.AbsolutePath.EndsWith("/" + ResultsPatchEvent)) <= readsBeforePatchReturn)
                    failures.Add(name + ": task switch lost selected file/page or skipped its fresh read");
                Press(Footer.Close);
                ChooseFile("3"); Shot("recorded rename");
                if (director.Plane.Showing(MenuColumn.File)?.Lines.Single().Words.Contains("Moved to: src/after.cs") != true) failures.Add(name + ": selected rename lost its destination");
                Press(Footer.Close); ChooseFile("4"); Shot("unknown project scope");
                if (director.Plane.Showing(MenuColumn.File)?.Lines.Single().Words.Contains("project scope is unknown") != true) failures.Add(name + ": unknown path was treated as verified outside");
                Press(Footer.Close); ChooseFile("5"); Shot("outside project patch omitted");
                Press(Footer.Close);
                Press(Footer.Close);
                Press(Footer.Close); Shot("returned original task");
                Debug.Log("Halcyonic: Results retained draft " + name + ": same file=" + (director.Navigator.Beside == retained)
                    + ", instruction equality=" + (retained?.ContinuationDraft()?.Text == instructionBefore?.Text)
                    + ", instruction parts=" + instructionBefore?.Part + "/" + retained?.ContinuationDraft()?.Part
                    + ", request equality=" + (retained?.ContinuationRequest()?.Text == context.Request.Text)
                    + ", pending identity equality=" + (retained?.ContinuationRequest()?.PendingCommandId == context.Request.PendingCommandId));
                if (director.Navigator.Beside != retained || retained?.ContinuationRequest()?.Text != context.Request.Text
                    || retained.ContinuationRequest()?.PendingCommandId != context.Request.PendingCommandId
                    || retained.ContinuationDraft()?.Text != instructionBefore?.Text || retained.ContinuationDraft()?.Spoken != instructionBefore?.Spoken
                    || retained.ContinuationDraft()?.Part != instructionBefore?.Part) failures.Add(name + ": Results changed its original file, either draft or pending identity");
                director.OpenFile(OtherResultsTask); Draw(); director.OpenFile(ResultsTask); Draw();
                if (!ReferenceEquals(director.Navigator.Beside, retained)) failures.Add(name + ": explicit Back to task was undone by reselection");
                // The spatial return is still a hidden ordinary draft. Reopen deliberately before
                // its explicit Discard draft action; a stale hidden Cancel must do nothing.
                if (retained?.Screen.Instruction.Hidden == true && !Press(FileScreens.TellIt)) return failures;
                if (!Press(FileScreens.CancelInstruction)) return failures;
                if (!TaskRoute(FileScreens.OpenWorkspaceRequest)) return failures;
                Shot("request before task switch");
                var retainedRequest = director.Navigator.Beside as WorkspaceRequestColumn;
                var requestDraft = retainedRequest?.Draft;
                director.OpenFile(OtherResultsTask); Draw(); director.OpenFile(ResultsTask); Shot("task switch restored request");
                if (retainedRequest == null || !ReferenceEquals(director.Navigator.Beside, retainedRequest)
                    || retainedRequest.Draft.Text != requestDraft?.Text || retainedRequest.Draft.PendingCommandId != requestDraft?.PendingCommandId
                    || retainedRequest.Draft.Part != requestDraft?.Part)
                    failures.Add(name + ": task switch lost request surface, draft, page or pending identity");
                director.OpenFile(OtherResultsTask); Draw();
                inbox.Enqueue(new ConnectionStatus(ConnectionPhase.WaitingToRetry)); session.Pump(); Draw();
                var readsBeforeOfflineRequest = handler.Reads.Count;
                director.OpenFile(ResultsTask); Shot("offline return retains request draft");
                if (retainedRequest == null || !ReferenceEquals(director.Navigator.Beside, retainedRequest)
                    || retainedRequest.Draft.Text != requestDraft?.Text || retainedRequest.Draft.PendingCommandId != requestDraft?.PendingCommandId
                    || handler.Reads.Count != readsBeforeOfflineRequest)
                    failures.Add(name + ": offline request selection lost its draft/identity or fetched while disconnected");
                inbox.Enqueue(new ConnectionStatus(ConnectionPhase.Synchronizing)); session.Pump(); Draw();
                if (handler.Reads.Count != readsBeforeOfflineRequest) failures.Add(name + ": request read before a current snapshot");
                inbox.Enqueue(new WelcomeMessage { Journal = snapshot.Journal, Head = snapshot.Position, ServerTime = Time });
                inbox.Enqueue(new SnapshotMessage { Snapshot = snapshot }); inbox.Enqueue(new ConnectionStatus(ConnectionPhase.Live)); session.Pump();
                Shot("reconnected request keeps original pending identity");
                if (retainedRequest == null || !ReferenceEquals(director.Navigator.Beside, retainedRequest)
                    || retainedRequest.Draft.Text != requestDraft?.Text || retainedRequest.Draft.PendingCommandId != requestDraft?.PendingCommandId)
                    failures.Add(name + ": reconnect changed the retained request or pending identity");
                Press(Footer.Close); TaskRoute(FileScreens.OpenResults); Press(TaskResultsColumn.Read, ResultsEvent);
                var closingReader = director.Navigator.Beside;
                if (!director.CloseFile(ResultsTask)) failures.Add(name + ": retained task did not accept its explicit close");
                Draw();
                if (ReferenceEquals(director.Navigator.Beside, closingReader)) failures.Add(name + ": closing a task left its result reader visible");
                director.OpenFile(ResultsTask); Draw();
                if (director.Navigator.Beside is not FileColumn || ReferenceEquals(director.Navigator.Beside, retained))
                    failures.Add(name + ": a closed task resurrected its discarded local view");
                TaskRoute(FileScreens.OpenResults); Press(TaskResultsColumn.Read, ResultsEvent);
                var priorExecutionReader = director.Navigator.Beside;
                var priorExecutionFrame = director.Plane.Showing(MenuColumn.File);
                director.OpenFile(OtherResultsTask); Draw();
                var nextExecution = HalcyonicJson.Deserialize<ExecutionView>(HalcyonicJson.Serialize(execution));
                nextExecution.ExecutionId = "0192f1a0-0000-7000-8000-000000000109";
                work.CurrentExecutionId = nextExecution.ExecutionId; work.ExecutionIds.Add(nextExecution.ExecutionId);
                snapshot.Executions.Add(nextExecution); snapshot.Position++;
                inbox.Enqueue(new SnapshotMessage { Snapshot = snapshot }); session.Pump(); Draw();
                var readsBeforeNewExecution = handler.Reads.Count;
                director.OpenFile(ResultsTask); Shot("new execution opens fresh task");
                if (director.Navigator.Beside is not FileColumn || ReferenceEquals(director.Navigator.Beside, priorExecutionReader)
                    || handler.Reads.Count != readsBeforeNewExecution)
                    failures.Add(name + ": new execution restored or read the previous execution's result");
                if (priorExecutionFrame != null && director.PressForRender(MenuColumn.File, TaskResultsColumn.Next, null, priorExecutionFrame, null))
                    failures.Add(name + ": prior execution result kept action authority");
                if (sends != 0) failures.Add(name + ": read-only Results submitted a command");
                if (handler.Reads.Count(uri => uri.AbsolutePath.EndsWith("/" + ResultsEvent)) < 2) failures.Add(name + ": restore did not freshly read its original event");
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(root); }
            return failures;
        }
    }
}

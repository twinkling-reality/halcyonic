#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>Actual request-column views on the dedicated console, using synthetic metadata/results only.</summary>
        public static void RequestCheck()
        {
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "RequestRenders", variant));
                Directory.CreateDirectory(folder);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                ViewField.Current = FieldChecks.Quest3S;
                var found = RenderWorkspaceRequests("workspace", folder).ToList();
                ViewField.Current = null;
                KeepFontAssetsAsCommitted();
                foreach (var failure in found) Debug.LogError("Halcyonic: request render: " + failure);
                if (found.Count == 0) Debug.Log("Halcyonic: request render: every check passed; " + folder);
                return found;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static IEnumerable<string> RenderWorkspaceRequests(string name, string folder)
        {
            var failures = new List<string>();
            var root = new GameObject("Synthetic request layout witness");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
            using var session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                var work = Work.Running();
                var commands = new CommandFactory(client);
                var sent = new List<CommandEnvelope>();
                WorkspaceRequestColumn? request = null;
                Action<string>? done = null;
                var context = new WorkspaceContinuation
                {
                    HandoffId = Guid.NewGuid().ToString("D"), ConnectionKey = new string('a', 64), JournalId = work.State.Journal!.JournalId,
                    ProjectId = work.Workstream.ProjectId, WorkstreamId = work.Workstream.WorkstreamId, ExecutionId = work.Execution.ExecutionId,
                    Surface = WorkspaceSurface.Request,
                };
                // The read/current-state seams are explicitly synthetic. This checks real views and
                // director input, not a live session watermark, private persistence or model inference.
                MenuDirector Make(bool unavailable = false) => MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    Commands = commands, Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    State = () => work.State, Session = () => session, Connected = () => true,
                    Overview = () => WorkOverview.Of(work.State, new StageVisibility(), _ => true),
                    Bar = place => TasksColumn.Bar(place, work.State),
                    KeyboardOffered = () => true, Keyboard = (_, _, accept, _) => done = accept,
                    Submit = (_, command) => { sent.Add(command); return Task.FromResult(new CommandAckMessage { CommandId = command.CommandId, Disposition = CommandAckDisposition.Accepted }); },
                    File = (host, _) => request = new WorkspaceRequestColumn(host, commands, context, () => true,
                        _ => unavailable ? Task.FromException<WorkspaceAgentStatus>(new InvalidOperationException())
                            : Task.FromResult<WorkspaceAgentStatus>(new AvailableWorkspaceAgent { Model = "Simulated assistant" }),
                        (id, _) => Task.FromResult(new WorkspaceRequestResponse
                        {
                            Request = new WorkspaceRequestView
                            {
                                RequestId = id, JournalId = context.JournalId!, ProjectId = context.ProjectId!, WorkstreamId = context.WorkstreamId!, ExecutionId = context.ExecutionId!,
                                OriginalText = "Inspect this task and explain what remains to be checked.", Status = new JValue("responded"), CreatedAt = Time, UpdatedAt = Time,
                                Reply = new WorkspaceRequestReply { Model = "Simulated assistant", Text = string.Join(" ", Enumerable.Repeat("This simulated reply demonstrates reported text and full readable paging; it does not establish a real task outcome.", 8)) },
                                Steps = new List<WorkspaceRequestStep>
                                {
                                    new() { StepId = "0192f1a0-0000-7000-8000-000000000105", ToolName = "task.read", Status = new JValue("confirmed"), ObservationPosition = 3, Summary = "The simulated task state was read." },
                                    new() { StepId = "0192f1a0-0000-7000-8000-000000000106", ToolName = "send_instruction", Status = new JValue("unknown"), CommandId = Guid.NewGuid().ToString("D"), ObservationPosition = 4, Summary = "The simulated instruction delivery is unknown. No retry or task completion is claimed." },
                                },
                            },
                        })) { BeforeSubmit = _ => true },
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, Array.Empty<CharacterTarget>(), null, false),
                });
                var director = Make();
                void Draw()
                { for (var draw = 0; draw < 3; draw++) { director.UpdateForRender(); director.DrawNow(); director.Plane.Advance(MenuPlane.OpeningSeconds); } }
                void Shot(string step)
                {
                    Draw();
                    var shown = director.Plane.Showing(MenuColumn.File);
                    if (shown != null && (shown.Subject != LabelText.Name(work.Workstream.Title) || !shown.SubjectRuns.AllTheirs))
                        failures.Add(name + ": request " + step + " did not identify its actual task as outside text");
                    failures.AddRange(PlaneState(name + " request " + step, folder, camera, texture, director.Plane,
                        new List<(CharacterView, CharacterTarget)>(), eyes, null, lightLine: false));
                }
                bool Press(string action, string? key = null)
                {
                    Draw();
                    var frame = director.Plane.Showing(MenuColumn.File);
                    if (frame == null || !(frame.Footer.All.Any(each => each.Prompt.Id == action && each.Prompt.Available)
                        || frame.Lines.Any(line => line.Action == action && line.Available)
                        || action == MenuFrame.ChooseSection && frame.Sections.Any(section => section.Key == key && section.Reached)))
                    { failures.Add(name + ": no visible request control " + action); return false; }
                    var taken = director.PressForRender(MenuColumn.File, action, key, frame, null);
                    if (!taken) failures.Add(name + ": visible request control refused its drawn frame " + action);
                    Draw(); return taken;
                }
                void ReadDraft(string step)
                {
                    for (var part = 0; part < 100; part++)
                    {
                        Shot(step + " part " + (part + 1));
                        if (director.Plane.Showing(MenuColumn.File)!.Lines.Any(line => line.Action == WorkspaceRequestColumn.Next && line.Available))
                            Press(WorkspaceRequestColumn.Next);
                        else return;
                    }
                    failures.Add(name + ": full request did not finish its offered pager");
                }
                director.OpenFile(work.Workstream.WorkstreamId);
                Shot("empty draft");
                Press(WorkspaceRequestColumn.Edit);
                done?.Invoke(string.Join(" ", Enumerable.Repeat("Inspect the current task, explain the open risks and improve its regression coverage.", 4)));
                ReadDraft("typed draft");
                if (sent.Count != 0) failures.Add(name + ": typing or reading submitted a request");
                Press(WorkspaceRequestColumn.Start);
                Shot("accepted waiting");
                if (sent.Count != 1 || sent[0] is not WorkspaceRequestSubmitCommand) failures.Add(name + ": explicit typed Start did not emit one scoped request");
                // A fresh fixture models the separate GET of an existing exact request. It is not
                // falsely derived from that acknowledgement, which proved no recorded result above.
                UnityEngine.Object.DestroyImmediate(director.gameObject);
                context.Request = new WorkspaceRequestContinuation { RequestId = "0192f1a0-0000-7000-8000-000000000104", Text = "Explain the next step before proceeding", Spoken = true };
                director = Make(); director.OpenFile(work.Workstream.WorkstreamId);
                Shot("reported reply");
                Press(MenuFrame.ChooseSection, "receipts");
                Shot("recorded actions");
                Press(MenuFrame.ChooseSection, "draft");
                ReadDraft("retained spoken draft");
                Press(WorkspaceRequestColumn.Resume);
                Shot("spoken review");
                if (sent.Count != 1 || !director.Plane.Showing(MenuColumn.File)!.Footer.Confirming) failures.Add(name + ": spoken continuation submitted before its explicit review");
                void ChromePress(GlazeButton button, int pointer)
                {
                    button.SettleForRender();
                    button.Target.PointerForRender(pointer, Oculus.Interaction.PointerEventType.Select, button.transform.position);
                    button.Target.PointerForRender(pointer, Oculus.Interaction.PointerEventType.Unselect, button.transform.position);
                }
                ChromePress(director.Plane.Chrome.Targets.Single(button => button.Label.text == WorkspaceText.Minimize), 841);
                Shot("minimized request");
                if (director.Plane.Bar is MenuBarView dock) ChromePress(dock.Target, 842);
                else failures.Add(name + ": minimized request offered no actual restore dock");
                Shot("restored request");
                if (request?.Draft.Text != context.Request.Text || request.Draft.RequestId != context.Request.RequestId)
                    failures.Add(name + ": global Minimize lost the request's reference or local words");
                UnityEngine.Object.DestroyImmediate(director.gameObject);
                context.Request = new WorkspaceRequestContinuation { Text = "Keep this request when the assistant is unavailable" };
                director = Make(unavailable: true); director.OpenFile(work.Workstream.WorkstreamId);
                Shot("unavailable draft");
                if (sent.Count != 1 || request?.Draft.Text != context.Request.Text) failures.Add(name + ": unavailable assistant lost or sent the local draft");
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(root); }
            return failures;
        }
    }
}

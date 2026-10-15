#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>Three actual production routes in the candidate spatial composition, at both reading sizes.</summary>
        public static void SpatialWorkspaceCheck()
        {
            InstrumentFontAtlas.ValidateSaved();
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "SpatialWorkspaceRenders", variant));
                Directory.CreateDirectory(folder);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                ViewField.Current = FieldChecks.Quest3S;
                var found = new List<string>();
                try
                {
                    found.AddRange(RenderSpatialWorkspace(folder, Work.Running(), false));
                    found.AddRange(RenderSpatialWorkspace(folder, Work.Approval("Run make migrate"), true));
                }
                finally { ViewField.Current = null; KeepFontAssetsAsCommitted(); }
                foreach (var failure in found) Debug.LogError("Halcyonic: spatial composition: " + failure);
                if (found.Count == 0) Debug.Log("Halcyonic: spatial composition: every check passed; " + folder);
                return found;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static IEnumerable<string> RenderSpatialWorkspace(string folder, Work work, bool waiting)
        {
            var failures = new List<string>();
            var root = new GameObject("Actual spatial workspace composition witness");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
            using var session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
            try
            {
                // Explicit synthetic transport; production Pump establishes the real current-watermark guard.
                var journal = new JournalInfo { JournalId = "render-journal", Origin = JournalOrigin.Live };
                var snapshot = new Snapshot { Journal = journal, Position = 1, Projects = work.State.Projects.Values.ToList(),
                    Workstreams = new List<WorkstreamView> { work.Workstream }, Executions = new List<ExecutionView> { work.Execution },
                    Runtimes = work.State.Runtimes.ToList() };
                var inbox = (ConcurrentQueue<object>)typeof(RealtimeSession).GetField("inbox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
                inbox.Enqueue(new WelcomeMessage { Journal = journal, Head = 1, ServerTime = Time,
                    CommandPolicies = new List<CommandPolicy> {
                        new() { CommandType = CommandType.ExecutionRespondToApproval, Policy = PolicyCategory.ReviewRequired },
                        new() { CommandType = CommandType.ExecutionSendInstruction, Policy = PolicyCategory.LowConsequence } } });
                inbox.Enqueue(new SnapshotMessage { Snapshot = snapshot }); inbox.Enqueue(new ConnectionStatus(ConnectionPhase.Live)); session.Pump();
                if (!session.SnapshotCurrent) failures.Add("spatial fixture did not establish current session authority");
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                var commands = new CommandFactory(client);
                Action<string>? done = null;
                var sends = 0;
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup {
                    SpatialWorkspace = true, Commands = commands, Presentation = new WorkspacePresentationState(),
                    Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    ConnectionTarget = () => ControlPlaneTarget.Local(new Uri("ws://127.0.0.1:9/realtime"), "render"),
                    State = () => session.State, Session = () => session, Connected = () => true,
                    Overview = () => WorkOverview.Of(session.State, new StageVisibility(), _ => true), Bar = place => TasksColumn.Bar(place, session.State),
                    ReadLocations = _ => Task.FromResult(new LocationsResponse { Roots = new List<LocationRoot>() }),
                    KeyboardOffered = () => true, Keyboard = (_, _, accept, _) => done = accept,
                    Submit = (_, command) => { sends++; return Task.FromResult(new CommandAckMessage { CommandId = command.CommandId, Disposition = CommandAckDisposition.Accepted }); },
                    File = (host, _) => new FileColumn(host, () => work.Present(), commands, () => null, _ => null, () => ""),
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, Array.Empty<CharacterTarget>(), null, false),
                });
                void Draw()
                { for (var turn = 0; turn < 4; turn++) { director.UpdateForRender(); director.DrawNow(); director.Plane.Advance(MenuPlane.OpeningSeconds); } }
                void Press(string action, string? key = null)
                {
                    Draw();
                    var visible = director.Plane.Shown.LastOrDefault(each => each.View.Frame is MenuFrame frame &&
                        (frame.Lines.Any(line => line.Action == action && line.Key == key && line.Available) ||
                         frame.Footer.All.Any(slot => slot.Prompt.Id == action && slot.Prompt.Available)));
                    if (visible.View == null || !director.PressForRender(visible.Kind, action, key, visible.View.Frame, null))
                        failures.Add("spatial: actual visible route refused " + action);
                }
                void Shot(string name)
                {
                    Draw(); var plane = director.Plane; ForceMeshes(root);
                    if (plane.Composition == null || plane.Shown.Count != 1) { failures.Add(name + ": expected one focused reading surface"); return; }
                    var view = plane.Shown[0].View;
                    var body = plane.Composition.Parts.First(part => part.Column == 1 && part.Index == 0);
                    var shape = new GlazeChecks.PlaneShape("work reading surface", view.Parts[0], new Vector2(body.Width, body.Height) * PlaneComposition.Distance);
                    var chrome = new GlazeChecks.PlaneShape("peripheral controls", plane.Chrome.transform, plane.Chrome.Size * plane.Chrome.transform.localScale.x);
                    // The chrome bounds deliberately enclose the work, so the old separated-rectangle
                    // gap invariant is inapplicable. Text, targets, field and reading orientation retain their thresholds.
                    if (Quaternion.Angle(shape.Root.rotation, chrome.Root.rotation) > 0.05f || Mathf.Abs(Vector3.Dot(shape.Root.position - chrome.Root.position, chrome.Root.forward)) > 0.0005f)
                        failures.Add(name + ": peripheral controls and reading face do not share their physical plane");
                    failures.AddRange(GlazeChecks.TypeStepsDown(new[] { (IReadOnlyList<GlazeChecks.PlaneShape>)new[] { shape } }, eyes, name));
                    failures.AddRange(GlazeChecks.TextAsSeen(root, eyes, name));
                    failures.AddRange(GlazeChecks.TargetsLargeEnough(view.Targets.Concat(plane.Chrome.Targets), eyes, name));
                    failures.AddRange(GlazeChecks.OneSelectionTreatment(plane.GetComponentsInChildren<Surface>(false), eyes, name));
                    if (!view.Footer.Fits) failures.Add(name + ": current frame actions do not fit");
                    if (ViewField.Current is ViewField field) {
                        var corners = new[] { -0.5f, 0.5f }.SelectMany(x => new[] { -0.5f, 0.5f }.Select(y => chrome.Root.position + chrome.Root.right * x * chrome.Size.x + chrome.Root.up * y * chrome.Size.y));
                        failures.AddRange(GlazeChecks.InsideField(name, corners, eyes, chrome.Root.position, WorkspacePlacement.ReadingPitch(plane.Composition.Size, plane.Direction.Elevation, field), field));
                    }
                    foreach (var (_, prompt) in plane.Front!.Footer.All)
                        if (prompt.Kind != PromptKind.Close && !(view.Footer.Showing?.All.Any(slot => slot.Prompt.Id == prompt.Id) ?? false)) failures.Add(name + ": offered action is absent: " + prompt.Id);
                    camera.transform.rotation = PlaneLayout.Facing(plane.Direction);
                    var image = Render(camera, texture); File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
                }
                if (!waiting) Shot("spatial-projects");
                Press(ProjectsScreens.ChooseProject, work.Workstream.ProjectId);
                Press(TasksColumn.OpenTask, work.Workstream.WorkstreamId);
                if (!waiting) { Press(FileScreens.TellIt); done?.Invoke("Add a regression test for the rate limit."); }
                Shot(waiting ? "spatial-waiting-approval" : "spatial-working-direction");
                if (!director.Plane.SelectedMarks.Any(mark => mark.Word == StateLanguage.Practice)) failures.Add("spatial: synthetic work lost Practice provenance");
                if (sends != 0) failures.Add("spatial: navigation or drafting submitted a command");
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(root); }
            return failures;
        }
    }
}

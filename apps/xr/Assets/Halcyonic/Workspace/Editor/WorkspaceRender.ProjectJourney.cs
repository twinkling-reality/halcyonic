#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
        public static void SpatialProjectJourneyCheck()
        {
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "SpatialProjectJourneyRenders", variant));
                Directory.CreateDirectory(folder);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                ViewField.Current = FieldChecks.Quest3S;
                var found = new List<string>();
                try { found.AddRange(RenderSpatialProjectJourney(folder)); }
                catch (Exception error) { found.Add("project journey threw " + error.GetBaseException().Message); }
                finally { ViewField.Current = null; KeepFontAssetsAsCommitted(); }
                foreach (var failure in found) Debug.LogError("Halcyonic: spatial project journey: " + failure);
                if (found.Count == 0) Debug.Log("Halcyonic: spatial project journey: every check passed; " + folder);
                return found;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static IEnumerable<string> RenderSpatialProjectJourney(string folder)
        {
            var failures = new List<string>();
            var root = new GameObject("Actual spatial project and creation journey");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var client = new ClientInfo { Name = "render", Version = "render", DeviceLabel = "render" };
            using var session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
            try
            {
                var work = Work.Running();
                var runtime = RenderRuntime("review-render", ModelChoice.None); runtime.UsesProjectLocation = false;
                var journal = new JournalInfo { JournalId = "render-project-journey", Origin = JournalOrigin.Live };
                var snapshot = new Snapshot { Journal = journal, Position = 1, Projects = work.State.Projects.Values.ToList(),
                    Workstreams = new List<WorkstreamView> { work.Workstream }, Executions = new List<ExecutionView> { work.Execution }, Runtimes = new List<RuntimeDescriptor> { runtime } };
                var inbox = (ConcurrentQueue<object>)typeof(RealtimeSession).GetField("inbox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
                inbox.Enqueue(new WelcomeMessage { Journal = journal, Head = 1, ServerTime = Time });
                inbox.Enqueue(new SnapshotMessage { Snapshot = snapshot }); inbox.Enqueue(new ConnectionStatus(ConnectionPhase.Live)); session.Pump();
                if (!session.SnapshotCurrent) throw new InvalidOperationException("synthetic project source is not current");
                var eyes = new Vector3(0f, EyeHeight, 0f); var camera = MakeCamera(root.transform, eyes, texture);
                var characters = new List<(CharacterView View, CharacterTarget Target)>();
                var commands = new CommandFactory(client); NewProjectFlow? flow = null; Action<string>? typed = null; var sends = 0;
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup {
                    SpatialWorkspace = true, InitialPlacementReady = () => true, Commands = commands,
                    Presentation = new WorkspacePresentationState(), Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    ConnectionTarget = () => ControlPlaneTarget.Local(new Uri("ws://127.0.0.1:9/realtime"), "render"),
                    State = () => session.State, Session = () => session, Connected = () => true,
                    Overview = () => WorkOverview.Of(session.State, new StageVisibility(), _ => true), Bar = place => TasksColumn.Bar(place, session.State),
                    ReadLocations = _ => Task.FromResult(new LocationsResponse { Roots = new List<LocationRoot>() }),
                    MakeNewProject = host => flow = new NewProjectFlow(host, commands, new KeptInMemory()),
                    KeyboardOffered = () => true, Keyboard = (_, _, accept, _) => typed = accept,
                    Submit = (_, _) => { sends++; throw new InvalidOperationException("The route witness cannot submit work."); },
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, Array.Empty<CharacterTarget>(), null, false),
                });
                void Draw() { for (var tick = 0; tick < 3; tick++) { director.UpdateForRender(); director.DrawNow(); director.Plane.Advance(MenuPlane.OpeningSeconds); } }
                void Shot(string step) { Draw(); failures.AddRange(SpatialState(step, folder, root, camera, texture, director, characters, eyes)); }
                void Press(string action, string? key = null)
                {
                    Draw();
                    var target = director.Plane.Shown.LastOrDefault(item => item.View.Frame is MenuFrame frame &&
                        (frame.Lines.Any(line => line.Action == action && line.Key == key && line.Available)
                        || frame.Footer.All.Any(slot => slot.Prompt.Id == action && slot.Prompt.Available)));
                    if (target.View == null || !director.PressForRender(target.Kind, action, key, target.View.Frame, null))
                        throw new InvalidOperationException("No current visible route for " + action);
                    Draw();
                }
                Shot("01-projects"); Press(ProjectsScreens.ChooseProject, work.Workstream.ProjectId); Shot("02-project-tasks");
                Press(TasksColumn.NewTask, work.Workstream.ProjectId); Shot("03-new-task-idea");
                if (flow?.Idea?.ExistingProjectId != work.Workstream.ProjectId) failures.Add("New task did not bind the selected existing project");
                Press(NewProjectScreens.TypeIdea);
                if (typed == null) throw new InvalidOperationException("The actual keyboard callback was not offered");
                typed(string.Join("\n\n", Enumerable.Range(1, 8).Select(index => "Part " + index + ": validate the current source, keep the existing behavior, and preserve the final report for careful review."))); typed = null; Draw();
                Shot("04-task-local-draft"); Press(NewProjectScreens.UseIdea); Shot("05-task-recap");
                Press(NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
                Press(NewProjectScreens.MoreOptions); Press(NewProjectScreens.ChooseRuntime, runtime.RuntimeId); Press(NewProjectScreens.Done);
                Shot("06-task-ready"); Press(NewProjectScreens.StartBuilding);
                var review = flow?.Review ?? throw new InvalidOperationException("The actual mandatory review did not open");
                if (review.PageCount < 2) failures.Add("The long task did not exercise multiple mandatory review pages");
                for (var page = 0; page < review.PageCount; page++)
                {
                    Shot("07-task-review-" + page);
                    if (page < review.PageCount - 1)
                    {
                        if (review.CanConfirm) failures.Add("Review offered confirmation before every part was read");
                        Press(NewProjectScreens.NextPart);
                    }
                }
                if (!review.CanConfirm) failures.Add("The last actual review page never enabled explicit confirmation");
                Press(NewProjectScreens.Cancel); Shot("08-task-review-cancelled");
                if (sends != 0 || flow?.Idea?.ExistingProjectId != work.Workstream.ProjectId) failures.Add("Review cancellation submitted work or lost selected project identity");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
            return failures;
        }
    }
}

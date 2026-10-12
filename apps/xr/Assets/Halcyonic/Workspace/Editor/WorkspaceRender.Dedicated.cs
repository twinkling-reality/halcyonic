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
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>The dedicated, character-free production menu and global dock at both actual font sizes.</summary>
        public static void DedicatedWorkspaceCheck()
        {
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "DedicatedWorkspaceRenders", variant));
                Directory.CreateDirectory(folder);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                ViewField.Current = FieldChecks.Quest3S;
                var result = RenderDedicatedWorkspace("workspace", folder).ToList();
                ViewField.Current = null;
                KeepFontAssetsAsCommitted();
                foreach (var failure in result) Debug.LogError("Halcyonic: dedicated workspace: " + failure);
                if (result.Count == 0) Debug.Log("Halcyonic: dedicated workspace: every check passed; " + folder);
                return result;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static IEnumerable<string> RenderDedicatedWorkspace(string name, string folder)
        {
            var failures = new List<string>();
            var root = new GameObject("Character-free dedicated workspace witness");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                var work = Work.Running(); // Explicitly synthetic execution in a live-origin journal.
                var state = (ClientProjection?)null;
                var connected = false;
                var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
                var commands = new CommandFactory(client);
                var session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
                var presentation = new WorkspacePresentationState();
                FileColumn? file = null;
                Action<string>? done = null;
                var sends = 0;
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    Presentation = presentation,
                    Commands = commands,
                    Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    State = () => state,
                    Session = () => session,
                    Connected = () => connected,
                    Overview = () => state == null ? null : WorkOverview.Of(state, new StageVisibility(), _ => true),
                    Bar = place => TasksColumn.Bar(place, state),
                    ReadLocations = _ => Task.FromResult(new LocationsResponse { Roots = new List<LocationRoot>() }),
                    KeyboardOffered = () => true,
                    Keyboard = (_, _, accept, _) => done = accept,
                    Submit = (_, command) => { sends++; return Task.FromResult(new CommandAckMessage { CommandId = command.CommandId, Disposition = CommandAckDisposition.Accepted }); },
                    File = (host, _) => file = new FileColumn(host, () => work.Present(live: connected), commands, () => null, _ => null, () => ""),
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, Array.Empty<CharacterTarget>(), null, false),
                });

                void Shot(string suffix)
                {
                    director.UpdateForRender();
                    director.DrawNow();
                    failures.AddRange(PlaneState(name + " " + suffix, folder, camera, texture, director.Plane,
                        new List<(CharacterView, CharacterTarget)>(), eyes, null, lightLine: false));
                }
                void Press(string action, string? key = null)
                {
                    var visible = director.Plane.Shown.Where(each => each.View.Frame is MenuFrame frame &&
                        (frame.Lines.Any(line => line.Action == action && line.Key == key)
                            || frame.Footer.All.Any(prompt => prompt.Prompt.Id == action && prompt.Prompt.Available)
                            || action == MenuFrame.ChooseSection && frame.Sections.Any(section => section.Key == key))).ToList();
                    if (visible.Count == 0) { failures.Add(name + ": no actually visible " + action + " target was offered"); return; }
                    var target = visible.Last();
                    if (!director.PressForRender(target.Kind, action, key, target.View.Frame, null))
                        failures.Add(name + ": the visible " + action + " route refused the current drawn frame");
                }
                void ChromePress(GlazeButton target, int pointer)
                {
                    target.SettleForRender();
                    target.Target.PointerForRender(pointer, Oculus.Interaction.PointerEventType.Select, target.transform.position);
                    target.Target.PointerForRender(pointer, Oculus.Interaction.PointerEventType.Unselect, target.transform.position);
                }

                Shot("fresh projects");
                if (!director.Navigator.IsOpen || director.Navigator.Place != MenuPlace.Projects || director.Navigator.Asking)
                    failures.Add(name + ": fresh startup did not expose normal Projects navigation");
                if (root.GetComponentsInChildren<CharacterTarget>(true).Length != 0 || presentation.AgentsVisible)
                    failures.Add(name + ": default workspace requires a character");
                var divider = director.Plane.Chrome.GetComponentsInChildren<Surface>(false).Single(shape => shape.name == "Header rule");
                divider.Selection = SurfaceSelection.None;
                if (!GlazeChecks.OneSelectionTreatment(new[] { divider }, eyes, "untyped divider negative control").Any(problem => problem.Contains("is a bar")))
                    failures.Add(name + ": an arbitrary untyped bar escaped the selection invariant");
                divider.Selection = SurfaceSelection.CompositionDivider;
                var columns = director.Plane.Shown.Select(each => (IReadOnlyList<GlazeChecks.PlaneShape>)each.View.Parts.Select((part, index) =>
                    new GlazeChecks.PlaneShape(part.name, part, new Vector2(each.View.Width, each.View.Heights[index]) * part.localScale.x)).ToList()).ToList();
                var header = new GlazeChecks.PlaneShape("workspace header", director.Plane.Chrome.transform, director.Plane.Chrome.Size * director.Plane.Chrome.transform.localScale.x);
                var headerRotation = director.Plane.Chrome.transform.rotation;
                director.Plane.Chrome.transform.rotation *= Quaternion.Euler(0f, 1f, 0f);
                if (!GlazeChecks.OnePlane(columns, eyes, "turned header negative control", header).Any(problem => problem.Contains("is turned")))
                    failures.Add(name + ": a header rotated against its body escaped coplanarity checks");
                director.Plane.Chrome.transform.rotation = headerRotation;
                state = work.State;
                connected = true;
                director.Navigator.Renew();
                Shot("connected projects");
                Press(ProjectsScreens.ChooseProject, work.Workstream.ProjectId);
                Shot("project tasks");
                if (!director.Plane.SelectionContext.Text.Contains(work.State.Projects[work.Workstream.ProjectId].Name))
                    failures.Add(name + ": filtered Tasks header lost the selected project");
                Press(TasksColumn.ProjectDetails);
                Shot("project details");
                if (director.Navigator.Place != MenuPlace.Projects || director.Plane.Front?.Side == null)
                    failures.Add(name + ": Project details lost the existing project actions");
                var projectDetails = director.Plane.Shown.FirstOrDefault(each => each.Kind == MenuColumn.Side).View;
                if (projectDetails?.Side == null || !director.PressForRender(MenuColumn.Side, SidePanel.Close, null, projectDetails.InPlaceOf, projectDetails.Side))
                    failures.Add(name + ": visible Close details did not restore the project list");
                Shot("returned projects from details");
                Press(ProjectsScreens.ChooseProject, work.Workstream.ProjectId);
                Shot("returned project tasks");
                Press(TasksColumn.OpenTask, work.Workstream.WorkstreamId);
                Shot("selected practice task");
                if (!director.Plane.SelectedMarks.Any(mark => mark.Word == StateLanguage.Practice)) failures.Add(name + ": selected synthetic task is unmarked inside a live journal");
                Press(FileScreens.TellIt);
                done?.Invoke("Add a regression test for the rate limit.");
                Shot("local instruction");
                if (director.Plane.Shown.SelectMany(each => each.View.GetComponentsInChildren<StateBadgeView>(false)).Any())
                    failures.Add(name + ": selected task state is repeated above its body title");
                var draftFile = file;
                ChromePress(director.Plane.Chrome.Targets.Single(button => button.Label.text == "Projects"), 730);
                Shot("projects beside retained instruction");
                if (director.Navigator.Place != MenuPlace.Projects || draftFile?.Screen.Instruction.Words != "Add a regression test for the rate limit.")
                    failures.Add(name + ": Projects navigation lost the retained instruction draft");
                Press(ProjectsScreens.ChooseProject, work.Workstream.ProjectId);
                Shot("tasks beside retained instruction");
                Press(TasksColumn.OpenTask, work.Workstream.WorkstreamId);
                Shot("reopened retained instruction");
                if (director.Navigator.Beside != draftFile || draftFile?.Screen.Instruction.Words != "Add a regression test for the rate limit.")
                    failures.Add(name + ": reopening the selected task replaced its draft view");
                var move = director.Plane.Chrome.Targets.Single(button => button.Label.text == WorkspaceText.MoveWorkspace);
                var grab = move.transform.position;
                director.HoldSubjectForRender(grab);
                if (!director.Dragging) failures.Add(name + ": the current visible workspace Move did not begin a real hand drag");
                var offset = grab - eyes;
                director.DragSubjectForRender(eyes + Quaternion.Euler(0f, 4f, 0f) * offset);
                director.LetGoForRender();
                Shot("hand placed instruction");
                var retainedDirection = director.Plane.Direction;
                var retainedOffset = director.Plane.Moved;
                if (retainedOffset == default) failures.Add(name + ": the real Move pointer left no retained hand offset");
                var fullFrame = director.Plane.Chrome.FrameSurface;
                var retainedCorners = Enumerable.Range(0, 4).Select(corner => fullFrame.transform.TransformPoint(new Vector3(
                    corner % 2 == 0 ? -0.5f : 0.5f, corner < 2 ? -0.5f : 0.5f, 0f))).ToArray();
                if (!director.OpenLocal(new WorkspaceNoticeColumn(director, "Move agents clear of the workspace before applying their placement.")))
                    failures.Add(name + ": a local placement explanation could not open");
                Shot("placement overlay");
                if (director.ClearsShelf(retainedCorners, eyes)) failures.Add(name + ": shelf placement forgot the full retained workspace under its smaller overlay");
                Press(Footer.Close);
                Shot("returned hand placed instruction");
                if (director.Plane.Direction.Yaw != retainedDirection.Yaw || director.Plane.Direction.Elevation != retainedDirection.Elevation
                    || director.Plane.Moved != retainedOffset) failures.Add(name + ": overlay return changed the hand-placed console position");

                var kept = file;
                ChromePress(director.Plane.Chrome.Targets.Single(button => button.Label.text == WorkspaceText.Minimize), 731);
                Shot("minimized dock");
                if (!presentation.Minimized || director.Plane.Shown.Count != 0 || director.Plane.Chrome.Visible || director.Plane.Bar?.Controls.Move.gameObject.activeSelf == true)
                    failures.Add(name + ": Minimize did not leave only the global restore dock");
                if (director.Plane.Bar is not MenuBarView dock) failures.Add(name + ": minimized workspace has no restore target");
                else ChromePress(dock.Target, 732);
                Shot("restored instruction");
                if (director.Plane.Direction.Yaw != retainedDirection.Yaw || director.Plane.Direction.Elevation != retainedDirection.Elevation
                    || director.Plane.Moved != retainedOffset) failures.Add(name + ": global Minimize/restore changed the hand-placed console position");
                if (presentation.Minimized || file != kept || file?.Screen.Instruction.Words != "Add a regression test for the rate limit.")
                    failures.Add(name + ": global restore lost selected task or local draft");
                connected = false;
                Shot("disconnected draft");
                if (sends != 0) failures.Add(name + ": navigation, keyboard completion or restore sent a command");
                session.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(root);
            }
            return failures;
        }
    }
}

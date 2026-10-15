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
        public static void ResponsiveWorkspaceCheck()
        {
            var field = ViewField.Current; var palette = Glaze.Palette; var scale = GlazeText.Scale;
            var active = RenderTexture.active; var failures = new List<string>();
            try
            {
                failures.AddRange(GlazeChecks.AtEachTextSize(variant =>
                {
                    var found = new List<string>();
                    foreach (var still in new[] { false, true })
                    {
                        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                        ViewField.Current = FieldChecks.Quest3S;
                        var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "ResponsiveWorkspaceRenders", variant, still ? "still" : "slide"));
                        Directory.CreateDirectory(folder);
                        found.AddRange(RenderWorkspaceFollow(folder, still));
                    }
                    return found;
                }));
            }
            catch (Exception error) { failures.Add("responsive fixture threw " + error.GetBaseException().GetType().Name); }
            finally { RenderTexture.active = active; ViewField.Current = field; Glaze.SetPalette(palette); GlazeText.SetScale(scale); KeepFontAssetsAsCommitted(); }
            foreach (var failure in failures) Debug.LogError("Halcyonic: responsive workspace: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: responsive workspace: every check passed.");
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static List<string> RenderWorkspaceFollow(string folder, bool still)
        {
            var failures = new List<string>(); var root = new GameObject("Synthetic moving head with production workspace");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var client = new ClientInfo { Name = "render", Version = "render", DeviceLabel = "render" };
            using var session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
            MenuDirector? director = null;
            try
            {
                var work = Work.Running(); var snapshot = new Snapshot { Journal = new JournalInfo { JournalId = "render-follow", Origin = JournalOrigin.Live }, Position = 1,
                    Projects = work.State.Projects.Values.ToList(), Workstreams = new List<WorkstreamView> { work.Workstream },
                    Executions = new List<ExecutionView> { work.Execution }, Runtimes = work.State.Runtimes.ToList() };
                var inbox = (ConcurrentQueue<object>)typeof(RealtimeSession).GetField("inbox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
                inbox.Enqueue(new WelcomeMessage { Journal = snapshot.Journal, Head = 1, ServerTime = Time });
                inbox.Enqueue(new SnapshotMessage { Snapshot = snapshot }); inbox.Enqueue(new ConnectionStatus(ConnectionPhase.Live)); session.Pump();
                if (!session.SnapshotCurrent) failures.Add("synthetic moving-head source is not current");
                var eyes = new Vector3(0, EyeHeight, 0); var looking = Vector3.forward; var ready = true;
                var camera = MakeCamera(root.transform, eyes, texture); camera.transform.rotation = Quaternion.identity;
                var commands = new CommandFactory(client); FileColumn? file = null; Action<string>? typed = null; var sends = 0;
                var targets = new List<CharacterTarget>(); var presentation = new WorkspacePresentationState();
                director = MenuDirector.Create(root.transform, new MenuDirector.Setup {
                    SpatialWorkspace = true, ResponsiveWorkspace = true, InitialPlacementReady = () => ready, Presentation = presentation,
                    Commands = commands, Comfort = new Comfort { Text = GlazeText.Scale > 1 ? TextSize.Larger : TextSize.Standard, Still = still },
                    Session = () => session, State = () => session.State, Connected = () => true,
                    Overview = () => WorkOverview.Of(session.State, new StageVisibility(), _ => true), Bar = place => TasksColumn.Bar(place, session.State),
                    ReadLocations = _ => Task.FromResult(new LocationsResponse { Roots = new List<LocationRoot>() }),
                    KeyboardOffered = () => true, Keyboard = (_, _, accept, _) => typed = accept,
                    Submit = (_, command) => { sends++; return Task.FromResult(new CommandAckMessage { CommandId = command.CommandId, Disposition = CommandAckDisposition.Accepted }); },
                    File = (host, _) => file = new FileColumn(host, () => work.Present(live: true), commands, () => null, _ => null, () => ""),
                    CharacterOf = id => presentation.AgentsVisible ? targets.FirstOrDefault(target => target.WorkstreamId == id) : null,
                    StageNow = () => new MenuDirector.Stage(eyes, looking, presentation.AgentsVisible ? targets : new List<CharacterTarget>(), null, false),
                });
                double now = 0;
                void Tick(bool redraw = false)
                {
                    now += 0.05; camera.transform.SetPositionAndRotation(eyes, Quaternion.LookRotation(looking, Vector3.up));
                    if (redraw) director.Redraw();
                    director.FollowFrameForRender(now); director.Plane.FrameForRender(0.05f);
                }
                void Rest(int frames = 40, bool redraw = false) { for (var frame = 0; frame < frames; frame++) Tick(redraw); }
                void Shot(string name)
                {
                    var view = director.Plane.Shown.Single().View;
                    failures.AddRange(GlazeChecks.TextAsSeen(root, eyes, name));
                    failures.AddRange(GlazeChecks.TargetsLargeEnough(view.Targets.Concat(director.Plane.Chrome.Targets), eyes, name));
                    var field = ViewField.Current!.Value;
                    foreach (var x in new[] { -0.5f, 0.5f }) foreach (var y in new[] { -0.5f, 0.5f })
                    {
                        var world = director.Plane.Chrome.transform.TransformPoint(new Vector3(x * director.Plane.Chrome.Size.x, y * director.Plane.Chrome.Size.y, 0));
                        var relative = camera.transform.InverseTransformPoint(world);
                        if (relative.z <= 0 || !field.Shows(Mathf.Atan2(relative.x, relative.z) * Mathf.Rad2Deg,
                            Mathf.Atan2(relative.y, new Vector2(relative.x, relative.z).magnitude) * Mathf.Rad2Deg))
                            failures.Add(name + ": actual settled workspace corner is outside the current head's front field");
                    }
                    var image = Render(camera, texture); File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
                }
                director.DrawNow(); Rest(); director.OpenFile(work.Workstream.WorkstreamId); Rest();
                var frame = director.Plane.Showing(MenuColumn.File)!;
                if (!director.PressForRender(MenuColumn.File, FileScreens.TellIt, null, frame, null) || typed == null)
                    throw new InvalidOperationException("actual task instruction input did not open");
                const string draft = "Keep this synthetic local draft through head movement.";
                var keyboardAnchor = director.Plane.Chrome.transform.position;
                looking = Quaternion.Euler(0, 80, 0) * Vector3.forward; Rest();
                if (director.FollowingForRender || Vector3.Distance(keyboardAnchor, director.Plane.Chrome.transform.position) > 0.0001f)
                    failures.Add("pending keyboard input was interrupted by automatic recovery");
                typed(draft); typed = null; Rest();
                if (file!.Screen.Instruction.Words != draft) failures.Add("the exact pending keyboard words were lost");
                looking = Vector3.forward; eyes = new Vector3(0, EyeHeight, 0); director.ResetPosition(); Rest(); Shot("01-reading");
                var initial = director.Plane.Chrome.transform.position; var initialRotation = director.Plane.Chrome.transform.rotation;
                for (var step = 0; step < 20; step++)
                {
                    looking = Quaternion.Euler(0, (float)Math.Sin(step * 0.1) * 6, 0) * Vector3.forward;
                    eyes = new Vector3((float)Math.Sin(step * 0.1) * 0.03f, EyeHeight, 0); Tick();
                }
                if (Vector3.Distance(initial, director.Plane.Chrome.transform.position) > 0.0001f
                    || Quaternion.Angle(initialRotation, director.Plane.Chrome.transform.rotation) > 0.01f)
                    failures.Add("ordinary small head turns/leans chased the reading object");
                var stale = director.Plane.Showing(MenuColumn.File)!;
                var send = director.Plane.GetComponentsInChildren<GlazeButton>(false).Single(button => button.Label.text == "Send instruction");
                send.SettleForRender(); send.Target.PointerForRender(612, Oculus.Interaction.PointerEventType.Select, send.transform.position);
                for (var step = 1; step <= 8; step++) { looking = Quaternion.Euler(0, step * 10, 0) * Vector3.forward; Tick(); }
                var began = false;
                for (var step = 0; step < 40; step++)
                {
                    Tick(redraw: true); // Identical passive redraws must not keep restarting the slide.
                    if (!director.FollowingForRender) continue;
                    began = true;
                    if (director.PressForRender(MenuColumn.File, FileScreens.SendInstruction, null, stale, null)
                        || director.PressForRender(MenuColumn.File, FileScreens.SendInstruction, null, director.Plane.Showing(MenuColumn.File), null)
                        || director.Plane.Shown.Any(shown => shown.View.Targets.Any(button => button.Accepting()))
                        || director.Plane.Chrome.Targets.Any(button => button.Accepting()))
                        failures.Add("moving or settling workspace accepted stale task input");
                }
                if (!began || director.FollowingForRender || director.Plane.Repositioning || director.Plane.Sliding)
                    failures.Add("yaw recovery never began or failed to settle under repeated passive redraws");
                send.Target.PointerForRender(612, Oculus.Interaction.PointerEventType.Unselect, send.transform.position);
                if (sends != 0 || file!.Steering.Armed != null || file.Screen.Instruction.Words != draft)
                    failures.Add("a stale press survived movement, or the exact retained draft was lost");
                Shot("02-yaw-recovered");
                var beforeTranslation = director.Plane.Eyes;
                for (var step = 1; step <= 8; step++) { eyes = new Vector3(0.03f + step * 0.05f, EyeHeight, 0); Tick(); }
                Rest();
                if (Vector3.Distance(director.Plane.Eyes, beforeTranslation) < 0.15f || Vector3.Distance(director.Plane.Eyes, eyes) > 0.001f
                    || director.FollowingForRender) failures.Add("translation recovery did not establish and settle at the current head position");
                if (file.Screen.Instruction.Words != draft || sends != 0) failures.Add("translation recovery changed the retained draft or submitted work");
                Shot("03-translation-recovered");
                var settled = director.Plane.Chrome.transform.position;
                ready = false; looking = Quaternion.Euler(0, 160, 0) * Vector3.forward; Rest();
                if (director.PressForRender(MenuColumn.File, FileScreens.SendInstruction, null, director.Plane.Showing(MenuColumn.File), null))
                    failures.Add("not-ready head pose accepted task input");
                if (Vector3.Distance(settled, director.Plane.Chrome.transform.position) > 0.0001f) failures.Add("untracked/not-ready pose moved the content anchor");
                ready = true; Rest(); Shot("04-tracking-restored");
                looking = Quaternion.Euler(20, 220, 0) * Vector3.forward; Rest(); Shot("05-moderate-pitch-recovered");
                if (Mathf.Abs(director.Plane.Direction.Elevation + 20) > 0.1f || director.FollowingForRender)
                    failures.Add("moderate pitch recovery failed to settle at its eligible head-relative pose");
                var beforeExtreme = director.Plane.Chrome.transform.position;
                looking = Quaternion.Euler(80, 220, 0) * Vector3.forward;
                var extremeMoved = false;
                for (var frameIndex = 0; frameIndex < 80; frameIndex++) { Tick(redraw: true); extremeMoved |= director.FollowingForRender; }
                if (extremeMoved || Vector3.Distance(beforeExtreme, director.Plane.Chrome.transform.position) > 0.0001f)
                    failures.Add("unsatisfiable extreme pitch repeatedly recovered or moved the reading object");
                // This pose intentionally cannot fit: do not call Shot's front-field assertion or claim it does.
                var extremeImage = Render(camera, texture); File.WriteAllBytes(Path.Combine(folder, "06-extreme-pitch-deferred.png"), extremeImage.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(extremeImage);
                looking = Quaternion.Euler(20, 220, 0) * Vector3.forward; Rest();
                var future = Quaternion.Euler(0, 280, 0) * Vector3.forward;
                var fixedView = CharacterView.Create(root.transform, work.Workstream.WorkstreamId); fixedView.Show(work.Present(live: true).Character);
                var fixedTarget = CharacterTarget.Attach(fixedView, work.Workstream.WorkstreamId);
                fixedView.transform.position += eyes + future * PlaneComposition.Distance - fixedTarget.BodyPosition;
                targets.Add(fixedTarget); presentation.SetAgentsEnabled(true); Rest();
                var placedBody = fixedTarget.BodyPosition; var placedRoot = fixedView.transform.position;
                var beforeBlocked = director.Plane.Chrome.transform.position;
                if (director.Plane.CanRecoverAt(eyes, future, targets)) failures.Add("prospective recovery ignored a deliberately placed real character at its destination");
                looking = future; var collisionMoved = false;
                for (var frameIndex = 0; frameIndex < 80; frameIndex++) { Tick(); collisionMoved |= director.FollowingForRender; }
                if (collisionMoved || Vector3.Distance(beforeBlocked, director.Plane.Chrome.transform.position) > 0.0001f
                    || fixedTarget.BodyPosition != placedBody || fixedView.transform.position != placedRoot)
                    failures.Add("blocked recovery moved content into a character or changed the explicit character placement");
                presentation.SetAgentsEnabled(false); fixedView.gameObject.SetActive(false); Rest(); Shot("07-clear-anchor-after-character-hidden");
                if (fixedView.transform.position != placedRoot || file.Screen.Instruction.Words != draft || sends != 0)
                    failures.Add("safe recovery changed the placed character or retained draft, or sent work");
                // Deliberately place the selected task's real character safely beside the next anchor.
                fixedView.gameObject.SetActive(true); fixedView.Grounded = true;
                fixedView.transform.localScale = Vector3.one * AgentShelfDraft.Scale;
                fixedView.transform.SetPositionAndRotation(eyes + Quaternion.Euler(0, 15, 0) * Vector3.forward * 2f - Vector3.up * 0.6f,
                    Quaternion.Euler(0, 195, 0));
                fixedView.PlaceShelfLabel(eyes); fixedView.AdvanceForRender(0);
                presentation.SetAgentsEnabled(true); Rest();
                var positiveRoot = fixedView.transform.position; var positiveBody = fixedTarget.BodyPosition;
                var positiveLooking = Quaternion.Euler(0, 340, 0) * Vector3.forward;
                if (!fixedView.Label.IdentityInWorkspace || !director.Plane.CanRecoverAt(eyes, positiveLooking, targets))
                    failures.Add("safe offset selected-character anchor/heading connector was falsely rejected");
                looking = positiveLooking; var positiveMoved = false;
                for (var frameIndex = 0; frameIndex < 60; frameIndex++) { Tick(); positiveMoved |= director.FollowingForRender; }
                if (!positiveMoved || director.FollowingForRender || fixedView.transform.position != positiveRoot || fixedTarget.BodyPosition != positiveBody)
                    failures.Add("safe selected-character recovery failed or changed explicit character placement");
                Shot("08-selected-character-recovered");
                if (!fixedView.Label.IdentityInWorkspace || director.Plane.LightLine == null)
                    failures.Add("selected-character recovery lost the shared task heading or real connector");
                if (director.Plane.LightLine is (Vector3 lineFrom, Vector3 lineTo))
                {
                    var outline = MenuPlane.LabelOutline(fixedView); var extent = BodyExtent(fixedView, eyes);
                    for (var sample = 1; sample < 40; sample++)
                    {
                        var point = Vector3.Lerp(lineFrom, lineTo, sample / 40f); var relative = point - eyes;
                        var yaw = Mathf.Atan2(relative.x, relative.z) * Mathf.Rad2Deg;
                        var elevation = Mathf.Atan2(relative.y, new Vector2(relative.x, relative.z).magnitude) * Mathf.Rad2Deg;
                        if (MenuPlane.OnLabel(outline, eyes, point)
                            || yaw >= extent.Left && yaw <= extent.Right && elevation >= extent.Bottom && elevation <= extent.Top)
                        { failures.Add("actual selected-character connector crosses its body or label after recovery"); break; }
                    }
                }
                if (!director.PressForRender(MenuColumn.File, FileScreens.Cancel, null, director.Plane.Showing(MenuColumn.File), null))
                    failures.Add("fresh settled workspace never returned ordinary task input authority");
            }
            finally
            {
                if (director != null) typeof(MenuDirector).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(director, null);
                UnityEngine.Object.DestroyImmediate(root); texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }
    }
}

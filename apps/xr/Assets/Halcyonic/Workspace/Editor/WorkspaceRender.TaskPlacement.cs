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
using Halcyonic.XR.Room;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>Actual task row, RoomControls, RoomPlacement and CharacterStage, with isolated synthetic transport/settings.</summary>
        public static void ShowTaskHereCheck()
        {
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "TaskPlacementRenders", variant));
                Directory.CreateDirectory(folder);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                ViewField.Current = FieldChecks.Quest3S;
                List<string> found;
                try { found = RenderTaskPlacement(folder); }
                catch (Exception error) { found = new List<string> { "actual placement fixture threw " + error.GetBaseException().GetType().Name + ": " + error.GetBaseException().Message }; }
                finally { ViewField.Current = null; KeepFontAssetsAsCommitted(); }
                foreach (var failure in found) Debug.LogError("Halcyonic: task placement: " + failure);
                if (found.Count == 0) Debug.Log("Halcyonic: task placement: every check passed; " + folder);
                return found;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private sealed class PlacementPairing : IPairingStore
        {
            public readonly PairedControlPlane Value = new PairedControlPlane("127.0.0.1", 9, new string('1', 64), "render", PairingCrypto.CredentialPrefix + "render-only-no-network");
            public PairedControlPlane Load() => Value;
            public void Save(PairedControlPlane pairing) => throw new InvalidOperationException("The fixture cannot persist pairing.");
            public void Forget() => throw new InvalidOperationException("The fixture cannot forget pairing.");
        }
        private sealed class PlacementHistory : HttpMessageHandler
        {
            public JournalInfo Journal = null!;
            public int Reads;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            {
                if (request.Method != HttpMethod.Get || request.RequestUri?.AbsolutePath != "/api/events")
                    throw new InvalidOperationException("The placement witness permits only synthetic event-history GET.");
                Reads++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(HalcyonicJson.Serialize(new EventsResponse { Journal = Journal, Head = 1 })) });
            }
        }
        private static FieldInfo PlacementField(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, name);
        private static object? PlacementCall(object target, string name, params object?[] args) => target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(target, args);
        private static void PlacementPump(RealtimeSession session, params object[] messages)
        {
            var inbox = (ConcurrentQueue<object>)PlacementField(typeof(RealtimeSession), "inbox").GetValue(session)!;
            foreach (var message in messages) inbox.Enqueue(message);
            session.Pump();
        }

        private static List<string> RenderTaskPlacement(string folder)
        {
            var failures = new List<string>();
            // These are the exact typed preferences Awake/Learned may read or change. Their original
            // presence and values are restored even if any assertion, component or HTTP witness throws.
            var stringKeys = new[] { "Halcyonic.RoomSpace", "Halcyonic.RoomPlacements", "halcyonic.stage.visibility" };
            var intKeys = new[] { "Halcyonic.RoomAccessDeclined", "Halcyonic.WorkspaceOpened" };
            var strings = stringKeys.Select(key => (Key: key, Had: PlayerPrefs.HasKey(key), Value: PlayerPrefs.GetString(key, ""))).ToList();
            var integers = intKeys.Select(key => (Key: key, Had: PlayerPrefs.HasKey(key), Value: PlayerPrefs.GetInt(key, 0))).ToList();
            var staticNames = new[] { "pairingStore", "tokenMigrated", "api", "apiTarget" };
            var settings = staticNames.ToDictionary(name => name, name => PlacementField(typeof(ControlPlaneSettings), name).GetValue(null));
            var root = new GameObject("Actual task placement witness");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            RoomPlacement? placement = null;
            GameObject? roomRoot = null;
            var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
            using var session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
            using var replacement = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
            var pairing = new PlacementPairing();
            var work = Work.Running();
            var journal = new JournalInfo { JournalId = "render-journal", Origin = JournalOrigin.Live };
            using var handler = new PlacementHistory { Journal = journal };
            using var api = new ControlPlaneApi(new Uri("http://127.0.0.1:9/"), "render", handler);
            try
            {
                foreach (var key in stringKeys) PlayerPrefs.DeleteKey(key);
                foreach (var key in intKeys) PlayerPrefs.DeleteKey(key);
                PlayerPrefs.SetInt("Halcyonic.WorkspaceOpened", 1);
                PlacementField(typeof(ControlPlaneSettings), "pairingStore").SetValue(null, pairing);
                PlacementField(typeof(ControlPlaneSettings), "tokenMigrated").SetValue(null, true);
                PlacementField(typeof(ControlPlaneSettings), "api").SetValue(null, api);
                PlacementField(typeof(ControlPlaneSettings), "apiTarget").SetValue(null, ControlPlaneTarget.Paired(pairing.Value));
                var prior = HalcyonicJson.Deserialize<WorkstreamView>(HalcyonicJson.Serialize(work.Workstream));
                prior.WorkstreamId = "render-prior"; prior.Title = "Previously shown task";
                var priorExecution = HalcyonicJson.Deserialize<ExecutionView>(HalcyonicJson.Serialize(work.Execution));
                priorExecution.ExecutionId = "render-prior-execution"; priorExecution.WorkstreamId = prior.WorkstreamId;
                prior.CurrentExecutionId = priorExecution.ExecutionId; prior.ExecutionIds = new List<string> { priorExecution.ExecutionId };
                var snapshot = new Snapshot { Journal = journal, Position = 1, Projects = work.State.Projects.Values.ToList(),
                    Workstreams = new List<WorkstreamView> { prior, work.Workstream }, Executions = new List<ExecutionView> { work.Execution, priorExecution }, Runtimes = work.State.Runtimes.ToList() };
                PlacementPump(session, new WelcomeMessage { Journal = journal, Head = 1, ServerTime = Time }, new SnapshotMessage { Snapshot = snapshot }, new ConnectionStatus(ConnectionPhase.Live));
                if (!session.SnapshotCurrent) throw new InvalidOperationException("The synthetic session is not current.");
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture); camera.transform.rotation = Quaternion.identity;
                var host = new GameObject("Actual workspace and room components"); host.SetActive(false); host.transform.SetParent(root.transform, false);
                var connection = host.AddComponent<ControlPlaneConnection>(); connection.enabled = false; // Never invoke transport OnEnable/Start.
                PlacementField(typeof(ControlPlaneConnection), "sessions").SetValue(connection, new DemonstrationFallback(session, () => null));
                var workspace = host.AddComponent<WorkspaceDirector>(); workspace.enabled = false;
                host.SetActive(true);
                if (PlacementField(typeof(WorkspaceDirector), "connection").GetValue(workspace) == null) PlacementCall(workspace, "Awake");
                placement = host.AddComponent<RoomPlacement>(); placement.enabled = false;
                if (PlacementField(typeof(RoomPlacement), "root").GetValue(placement) == null) PlacementCall(placement, "Awake");
                roomRoot = (GameObject)PlacementField(typeof(RoomPlacement), "root").GetValue(placement)!;
                var controlsType = typeof(RoomPlacement).Assembly.GetType("Halcyonic.XR.Room.RoomControls", throwOnError: true)!;
                controlsType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { roomRoot.transform, placement });
                var stage = workspace.EnsureAgentStage(); stage.enabled = false;
                if (PlacementField(typeof(CharacterStage), "arc").GetValue(stage) == null) PlacementCall(stage, "Awake");
                PlacementCall(stage, "FindPlacementSource");
                stage.Request(prior.WorkstreamId); // Explicitly retain a different selection before the requested task.
                var sends = 0;
                FileColumn? file = null;
                var setup = new MenuDirector.Setup {
                    Presentation = workspace.Presentation, Commands = new CommandFactory(client), InitialPlacementReady = () => true,
                    State = () => connection.Session?.State, Session = () => connection.Session, Connected = () => connection.Session?.Status.IsLive == true,
                    ConnectionTarget = () => ControlPlaneTarget.Paired(pairing.Value), Api = () => api,
                    Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    Overview = () => WorkOverview.Of(connection.Session!.State, new StageVisibility(), _ => true), Bar = place => TasksColumn.Bar(place, connection.Session?.State),
                    File = (bound, id) => file = (FileColumn?)PlacementCall(workspace, "FileFor", bound, id),
                    Submit = (_, _) => { sends++; throw new InvalidOperationException("Placement must not submit a command."); },
                    CharacterOf = id => workspace.Targets.FirstOrDefault(target => target.View.WorkstreamId == id && target.gameObject.activeInHierarchy),
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, workspace.Targets.Where(target => target.gameObject.activeInHierarchy).ToList(), null, false),
                };
                setup.SpatialWorkspace = true; // Require the actual candidate layout at compile time.
                var menu = MenuDirector.Create(host.transform, setup);
                PlacementField(typeof(WorkspaceDirector), "menu").SetValue(workspace, menu);
                void Draw()
                { for (var step = 0; step < 4; step++) { menu.UpdateForRender(); menu.DrawNow(); menu.Plane.Advance(MenuPlane.OpeningSeconds); } }
                void Shot(string name)
                {
                    Draw(); ForceMeshes(root);
                    failures.AddRange(GlazeChecks.TextAsSeen(root, eyes, name));
                    failures.AddRange(GlazeChecks.TargetsLargeEnough(menu.Plane.Shown.SelectMany(item => item.View.Targets).Concat(menu.Plane.Chrome.Targets), eyes, name));
                    if (ViewField.Current is ViewField field && menu.Plane.Composition is PlaneComposition composition)
                    {
                        var surface = menu.Plane.Chrome.FrameSurface.transform;
                        var corners = new[] { -0.5f, 0.5f }.SelectMany(x => new[] { -0.5f, 0.5f }.Select(y => surface.TransformPoint(new Vector3(x, y, 0f))));
                        failures.AddRange(GlazeChecks.InsideField(name, corners, eyes, menu.Plane.Chrome.transform.position,
                            WorkspacePlacement.ReadingPitch(composition.Size, menu.Plane.Direction.Elevation, field), field));
                    }
                    camera.transform.rotation = PlaneLayout.Facing(menu.Plane.Direction);
                    var image = Render(camera, texture); File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
                }
                var openStep = 0; string openingTask = "";
                void Press(string action)
                {
                    Draw(); var shown = menu.Plane.Shown.Single();
                    if (!(shown.View.Frame?.Lines.Any(line => line.Action == action && line.Available) == true || shown.View.Footer.Showing?.All.Any(slot => slot.Prompt.Id == action && slot.Prompt.Available) == true)
                        || !menu.PressForRender(shown.Kind, action, null, shown.View.Frame, null)) failures.Add("actual placement refused visible " + action + "; Open #" + openStep + " task=" + openingTask + "; frame=" + shown.View.Frame?.Subject + "; actions=" + string.Join(",", shown.View.Frame?.Lines.Where(line => line.Action != null).Select(line => line.Action + ":" + line.Available) ?? Enumerable.Empty<string>()));
                    Draw();
                }
                void Open(string id) { openStep++; openingTask = id; menu.OpenFile(id); Draw(); Press(FileScreens.ShowTaskHere); }
                bool SamePose(Pose? a, Pose? b) => a.HasValue == b.HasValue && (!a.HasValue || Vector3.Distance(a.Value.position, b!.Value.position) < 0.00001f && Quaternion.Angle(a.Value.rotation, b.Value.rotation) < 0.001f);
                var originalSelection = stage.ShelfPage.Title;
                Open(work.Workstream.WorkstreamId); Shot("task-placement-first-preview");
                var previewObject = (Transform)PlacementField(typeof(CharacterStage), "preview").GetValue(stage)!;
                if (!previewObject.gameObject.activeInHierarchy) failures.Add("the real CharacterStage shelf preview was not visible");
                if (placement.Preview == null || placement.Preferred != null || workspace.Presentation.AgentsEnabled || stage.ShelfPage.Title != originalSelection)
                    failures.Add("opening the real placement row changed placement/selection/enabled state before Apply");
                if (placement.Preview is Pose unturned)
                {
                    // Turn Left through the column's own row, as a person presses it: the preview turns by its 5 degree step
                    // (the turn rows' words are the direction RoomPlacement turns by; a rename once left them unturnable).
                    var turned = false;
                    for (var shelfPage = 0; shelfPage < 5; shelfPage++)
                    {
                        Draw(); var shelfShown = menu.Plane.Shown.Single(); var shelfFrame = shelfShown.View.Frame;
                        var turnRow = shelfFrame?.Lines.FirstOrDefault(line => line.Action == AgentShelfColumn.Move && line.Key == AgentShelfColumn.TurnLeft);
                        if (turnRow != null) { turned = menu.PressForRender(shelfShown.Kind, AgentShelfColumn.Move, turnRow.Key, shelfFrame, null); break; }
                        if (!menu.PressForRender(shelfShown.Kind, Footer.NextPage, null, shelfFrame, null)) break;
                    }
                    Draw();
                    if (!turned) failures.Add("the placement's Turn Left row was not offered, or refused its press");
                    else if (placement.Preview is not Pose turnedPose
                        || Mathf.Abs(Mathf.DeltaAngle(unturned.rotation.eulerAngles.y, turnedPose.rotation.eulerAngles.y) + 5f) > 0.5f)
                        failures.Add("Turn Left did not turn the shelf preview 5 degrees left");
                }
                Press(Footer.Close);
                if (previewObject.gameObject.activeInHierarchy || placement.Preview != null || placement.Preferred != null || workspace.Presentation.AgentsEnabled || stage.ShelfPage.Title != originalSelection)
                    failures.Add("initial Cancel did not preserve the previous empty shelf and selection");
                Open(work.Workstream.WorkstreamId); Press(AgentShelfColumn.Apply); PlacementCall(stage, "LateUpdate"); stage.Refresh();
                if (placement.Preferred == null || !workspace.Presentation.AgentsEnabled || !stage.TryGetCharacter(work.Workstream.WorkstreamId, out var selected) || !selected.gameObject.activeInHierarchy
                    || stage.ShelfPage.Title != work.Workstream.Title || stage.TryGetCharacter(prior.WorkstreamId, out _))
                    failures.Add("actual Apply did not show exactly the requested task's real character");
                var shelfCorners = new Vector3[8];
                if (placement.Preferred is Pose confirmed)
                {
                    CharacterStage.WriteShelfCorners(confirmed, stage.ShelfWidth, eyes, shelfCorners);
                    if (!workspace.ClearsShelf(shelfCorners, eyes)) failures.Add("the actual applied character shelf overlaps the complete current workspace");
                }
                Shot("task-placement-applied-selected-character");
                var kept = placement.Preferred; var keptSelection = stage.ShelfPage.Title;
                Open(prior.WorkstreamId); Press(Footer.Close);
                if (!SamePose(kept, placement.Preferred) || stage.ShelfPage.Title != keptSelection || !workspace.Presentation.AgentsEnabled)
                    failures.Add("Cancel while another task was previewed changed the prior pose/selection/enabled state");
                Open(prior.WorkstreamId);
                var stale = menu.Plane.Shown.Single().View.Frame;
                PlacementPump(session, new WelcomeMessage { Journal = journal, Head = 2, ServerTime = Time }); Draw();
                if (session.SnapshotCurrent || placement.Preview != null || !SamePose(kept, placement.Preferred) || stage.ShelfPage.Title != keptSelection)
                    failures.Add("a stale snapshot did not cancel preview while preserving the confirmed shelf");
                if (stale != null && menu.PressForRender(MenuColumn.File, AgentShelfColumn.Apply, null, stale, null)) failures.Add("a stale placement frame retained Apply authority");
                PlacementPump(session, new SnapshotMessage { Snapshot = new Snapshot { Journal = journal, Position = 2, Projects = snapshot.Projects, Workstreams = snapshot.Workstreams, Executions = snapshot.Executions, Runtimes = snapshot.Runtimes } });
                if (!session.SnapshotCurrent) failures.Add("the restored snapshot did not satisfy the current watermark");
                // The real file refresh uses a 0.5 second monotonic deadline; immediate edit-mode
                // Draw calls do not advance it. Let actual Tick reoffer the local action, without
                // changing its predicate or granting authority to the previously drawn frame.
                var refreshDeadline = System.Diagnostics.Stopwatch.StartNew();
                do { Draw(); if (file?.Frame?.Lines.Any(line => line.Action == FileScreens.ShowTaskHere && line.Available) == true) break;
                    Thread.Sleep(10); } while (refreshDeadline.Elapsed.TotalSeconds < FileColumn.RefreshSeconds + 0.5);
                Open(prior.WorkstreamId); stale = menu.Plane.Shown.Single().View.Frame;
                PlacementPump(replacement, new WelcomeMessage { Journal = journal, Head = 1, ServerTime = Time }, new SnapshotMessage { Snapshot = snapshot }, new ConnectionStatus(ConnectionPhase.Live));
                PlacementField(typeof(ControlPlaneConnection), "sessions").SetValue(connection, new DemonstrationFallback(replacement, () => null)); Draw();
                if (placement.Preview != null || !SamePose(kept, placement.Preferred) || stage.ShelfPage.Title != keptSelection)
                    failures.Add("a replacement session did not cancel preview while preserving the confirmed shelf");
                if (stale != null && menu.PressForRender(MenuColumn.File, AgentShelfColumn.Apply, null, stale, null)) failures.Add("the old session's placement frame retained Apply authority");
                if (sends != 0 || handler.Reads == 0) failures.Add("actual placement sent a command or did not exercise production FileFor/history binding");
            }
            finally
            {
                try
                {
                    if (roomRoot != null) UnityEngine.Object.DestroyImmediate(roomRoot);
                    if (placement != null) PlacementField(typeof(RoomPlacement), "root").SetValue(placement, null);
                    UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(texture);
                }
                finally
                {
                    foreach (var name in staticNames) PlacementField(typeof(ControlPlaneSettings), name).SetValue(null, settings[name]);
                    foreach (var item in strings) { if (item.Had) PlayerPrefs.SetString(item.Key, item.Value); else PlayerPrefs.DeleteKey(item.Key); }
                    foreach (var item in integers) { if (item.Had) PlayerPrefs.SetInt(item.Key, item.Value); else PlayerPrefs.DeleteKey(item.Key); }
                    PlayerPrefs.Save();
                }
            }
            return failures;
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
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
        /// <summary>
        /// The flight on the shipped scene (the lead's 2026-11-17T18:32Z): the release WorkspaceDirector, MenuDirector, RoomPlacement and
        /// CharacterStage on a synthetic session that reaches no network (the shelf walk's harness), a task whose agent asks, its shelf
        /// placed through Place Agents. Opening the task at its question calls its character; 41 frames at 0.1 s on fixed clocks, the
        /// page closed at 2 s. It fails where the question calls no character, where the bot leaves the frame, where it has not landed
        /// beside the panel or the panel arrives before it lands, where the amber floor ring flies or stands past the lift-off, and where
        /// the ring is not back under it at home while the task still waits. Renders land in Builds/FlightWalkRenders.
        /// </summary>
        public static void FlightWalkCheck()
        {
            InstrumentFontAtlas.ValidateSaved();
            var designWas = RoomDesign.Enabled;
            RoomDesign.Enabled = true;
            List<string> failures;
            try
            {
                failures = GlazeChecks.AtEachTextSize(variant =>
                {
                    var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "FlightWalkRenders", variant));
                    Directory.CreateDirectory(folder);
                    foreach (var old in Directory.GetFiles(folder)) File.Delete(old);
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    var asyncShadersWere = ShaderUtil.allowAsyncCompilation;
                    ShaderUtil.allowAsyncCompilation = false;
                    ViewField.Current = FieldChecks.Quest3S;
                    List<string> found;
                    try { found = RenderFlightWalk(folder); }
                    catch (Exception error) { found = new List<string> { "the flight walk threw " + error.GetBaseException().GetType().Name + ": " + error.GetBaseException().Message }; }
                    finally { ShaderUtil.allowAsyncCompilation = asyncShadersWere; ViewField.Current = null; KeepFontAssetsAsCommitted(); }
                    foreach (var failure in found) Debug.LogError("Halcyonic: flight walk: " + failure);
                    if (found.Count == 0) Debug.Log("Halcyonic: flight walk: every check passed; " + folder);
                    return found;
                });
            }
            finally { RoomDesign.Enabled = designWas; }
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static List<string> RenderFlightWalk(string folder)
        {
            var failures = new List<string>();
            var stringKeys = new[] { "Halcyonic.RoomSpace", "Halcyonic.RoomPlacements", "halcyonic.stage.visibility" };
            var intKeys = new[] { "Halcyonic.RoomAccessDeclined", "Halcyonic.WorkspaceOpened" };
            var strings = stringKeys.Select(key => (Key: key, Had: PlayerPrefs.HasKey(key), Value: PlayerPrefs.GetString(key, ""))).ToList();
            var integers = intKeys.Select(key => (Key: key, Had: PlayerPrefs.HasKey(key), Value: PlayerPrefs.GetInt(key, 0))).ToList();
            var root = new GameObject("Flight walk");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            RoomPlacement? placement = null;
            GameObject? roomRoot = null;
            var stillWas = GlazeMotion.Still;
            var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
            using var session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
            // The task runs while its shelf is placed, then its agent asks: the moment the flight is for.
            var running = Work.Running();
            var work = Work.Asking(Scripted());
            var workstreamId = work.Workstream.WorkstreamId;
            var journal = new JournalInfo { JournalId = "render-journal", Origin = JournalOrigin.Live };
            using var handler = new PlacementHistory { Journal = journal };
            using var api = new ControlPlaneApi(new Uri("http://127.0.0.1:9/"), "render", handler);
            try
            {
                GlazeMotion.Still = false;
                foreach (var key in stringKeys) PlayerPrefs.DeleteKey(key);
                foreach (var key in intKeys) PlayerPrefs.DeleteKey(key);
                PlayerPrefs.SetInt("Halcyonic.WorkspaceOpened", 1);
                var snapshot = new Snapshot { Journal = journal, Position = 1, Projects = running.State.Projects.Values.ToList(),
                    Workstreams = new List<WorkstreamView> { running.Workstream }, Executions = new List<ExecutionView> { running.Execution }, Runtimes = running.State.Runtimes.ToList() };
                PlacementPump(session, new WelcomeMessage { Journal = journal, Head = 1, ServerTime = Time }, new SnapshotMessage { Snapshot = snapshot }, new ConnectionStatus(ConnectionPhase.Live));
                if (!session.SnapshotCurrent) throw new InvalidOperationException("The synthetic session is not current.");
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                camera.transform.rotation = Quaternion.identity;
                var host = new GameObject("Workspace and room"); host.SetActive(false); host.transform.SetParent(root.transform, false);
                var connection = host.AddComponent<ControlPlaneConnection>(); connection.enabled = false; // Never transport.
                PlacementField(typeof(ControlPlaneConnection), "sessions").SetValue(connection, new DemonstrationFallback(session, () => null));
                var workspace = host.AddComponent<WorkspaceDirector>(); workspace.enabled = false;
                host.SetActive(true);
                if (PlacementField(typeof(WorkspaceDirector), "connection").GetValue(workspace) == null) PlacementCall(workspace, "Awake");
                workspace.TargetOf = () => ControlPlaneTarget.Paired(PlacementPaired); workspace.ApiOf = () => api;
                placement = host.AddComponent<RoomPlacement>(); placement.enabled = false;
                if (PlacementField(typeof(RoomPlacement), "root").GetValue(placement) == null) PlacementCall(placement, "Awake");
                roomRoot = (GameObject)PlacementField(typeof(RoomPlacement), "root").GetValue(placement)!;
                var controlsType = typeof(RoomPlacement).Assembly.GetType("Halcyonic.XR.Room.RoomControls", throwOnError: true)!;
                var controls = controlsType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { roomRoot.transform, placement })!;
                var stage = workspace.EnsureAgentStage(); stage.enabled = false;
                if (PlacementField(typeof(CharacterStage), "arc").GetValue(stage) == null) PlacementCall(stage, "Awake");
                PlacementCall(stage, "FindPlacementSource");
                // Every clock the flight reads, fixed: the stage's and the panel's.
                var clock = 100f;
                stage.FlightClock = () => clock;
                // The stage as WorkspaceDirector.StageNow gives it, with the flight's call, release and who may fly, as it hands them.
                MenuDirector.Stage StageNow()
                {
                    var standing = workspace.Targets.Where(target => target.gameObject.activeInHierarchy).ToList();
                    Vector3[]? shelf = null;
                    if (stage.Visible)
                    {
                        shelf = new Vector3[8];
                        CharacterStage.WriteShelfCorners(stage.OwnShelf, stage.ShelfWidth, eyes, shelf, CharacterStage.ShelfPlaces);
                    }
                    return new MenuDirector.Stage(eyes, Vector3.forward, standing, null, false, shelf, degrees => stage.ShelfTurnDegrees = degrees,
                        call: stage.Call, release: stage.Release, canFly: task => stage.Visible && stage.Holds(task));
                }
                var setup = new MenuDirector.Setup
                {
                    Presentation = workspace.Presentation, Commands = new CommandFactory(client), InitialPlacementReady = () => true,
                    State = () => connection.Session?.State, Session = () => connection.Session, Connected = () => connection.Session?.Status.IsLive == true,
                    ConnectionTarget = () => ControlPlaneTarget.Paired(PlacementPaired), Api = () => api,
                    Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    Overview = () => WorkOverview.Of(connection.Session!.State, new StageVisibility(), _ => true), Bar = place => TasksColumn.Bar(place, connection.Session?.State),
                    File = (bound, id) => (FileColumn?)PlacementCall(workspace, "FileFor", bound, id),
                    Submit = (_, _) => throw new InvalidOperationException("The flight walk must not submit a command."),
                    CharacterOf = id => workspace.Targets.FirstOrDefault(target => target.View.WorkstreamId == id && target.gameObject.activeInHierarchy),
                    StageNow = StageNow,
                };
                setup.SpatialWorkspace = true;
                var menu = MenuDirector.Create(host.transform, setup);
                PlacementField(typeof(WorkspaceDirector), "menu").SetValue(workspace, menu);
                var plane = menu.Plane;
                var bar = plane.Kit.Bar;
                bar.PanelClock = () => clock;
                // When the panel's moment arrived, kept so the render's settling never moves it (the product's frames run no settle).
                float? momentAt = null;
                void Draw()
                {
                    for (var step = 0; step < 4; step++)
                    {
                        menu.UpdateForRender(); menu.DrawNow(); plane.Advance(MenuPlane.OpeningSeconds);
                        if (plane.KitShows) { plane.Kit.SettleForRender(); plane.Kit.DrawnForRender(); }
                    }
                    if (momentAt is float at)
                    {
                        PlacementField(typeof(RoomBarView), "arrivedAt").SetValue(bar, at);
                        PlacementCall(bar, "FollowArrival");
                    }
                    PlacementCall(stage, "LateUpdate");
                }
                bool Press(string action, string? key = null)
                {
                    Draw();
                    if (plane.KitShows && plane.Kit.Bar.Button(action, key) is RoomButton button && button.gameObject.activeInHierarchy)
                    {
                        button.Press.SettleForRender();
                        button.Press.PressForRender(985);
                        Draw();
                        return true;
                    }
                    // Else through the frame the plane shows that offers it, as the task placement walk presses it.
                    foreach (var (kind, shownView) in plane.Shown)
                    {
                        if (shownView.Frame is not MenuFrame frame || !(frame.Lines.Any(line => line.Action == action && line.Key == key && line.Available)
                            || frame.Footer.All.Any(slot => slot.Prompt.Id == action && slot.Prompt.Available))) continue;
                        if (!menu.PressForRender(kind, action, key, frame, null)) continue;
                        Draw();
                        return true;
                    }
                    return false;
                }

                // The shelf, placed as a person places it: the task's Show Task Here, then Apply (the task placement walk's way).
                // The waiting task's page may open on its own; the shelf is placed with nothing beside the menu.
                Draw();
                for (var close = 0; close < 3 && plane.Shown.Any(each => each.Kind != MenuColumn.Menu); close++)
                {
                    Debug.Log("Halcyonic: flight walk: before placing, the plane shows " + string.Join(", ", plane.Shown.Select(each => each.Kind.ToString())) + "; Close");
                    if (!Press(Footer.Close)) { menu.Navigator.CloseBeside(); Draw(); }
                }
                // The head faces the plane, as the shelf walk's shots turn it, so the shelf's search measures from the view a person has.
                camera.transform.rotation = PlaneLayout.Facing(plane.Direction);
                Draw();
                // A placing cancelled first, as the shelf walk begins: the plane decides its width again before the real one.
                controlsType.GetMethod("PlaceAgents", BindingFlags.Public | BindingFlags.Instance)!.Invoke(controls, null);
                Draw();
                if (!Press(Footer.Close)) failures.Add("nothing drawn closes the shelf's placing");
                Draw(); Draw();
                controlsType.GetMethod("PlaceAgents", BindingFlags.Public | BindingFlags.Instance)!.Invoke(controls, null);
                Draw();
                Debug.Log("Halcyonic: flight walk: placing: preview " + (placement.Preview != null) + ", menu host " + (workspace.MenuHost != null) + ", minimized "
                    + workspace.Presentation.Minimized + ", input suspended " + FocusGuard.InputSuspended + ", the plane shows " + string.Join(", ", plane.Shown.Select(each => each.Kind.ToString())));
                camera.transform.rotation = PlaneLayout.Facing(plane.Direction);
                Draw();
                if (!Press(AgentShelfColumn.Apply)) failures.Add("nothing drawn applies the shelf's place");
                // The first Apply of an Editor run can land before the shelf's column has laid its own frame: once more, said so.
                if (placement.Preferred == null && Press(AgentShelfColumn.Apply)) Debug.Log("Halcyonic: flight walk: Apply pressed a second time, placed " + (placement.Preferred != null));
                Debug.Log("Halcyonic: flight walk: applied: placed " + (placement.Preferred != null) + ", the plane shows " + string.Join(", ", plane.Shown.Select(each => each.Kind.ToString())));
                PlacementCall(stage, "LateUpdate"); stage.Refresh(); Draw(); Draw();
                if (menu.Navigator.Beside != null && !Press(Footer.Close)) menu.Navigator.CloseBeside();
                Draw();
                // Its agent asks.
                PlacementPump(session, new WelcomeMessage { Journal = journal, Head = 2, ServerTime = Time }, new SnapshotMessage { Snapshot = new Snapshot { Journal = journal, Position = 2,
                    Projects = work.State.Projects.Values.ToList(), Workstreams = new List<WorkstreamView> { work.Workstream }, Executions = new List<ExecutionView> { work.Execution },
                    Runtimes = work.State.Runtimes.ToList() } });
                stage.Refresh(); Draw(); Draw();
                if (connection.Session?.State.Workstreams.TryGetValue(workstreamId, out var asking) != true || asking.Attention.Level != AttentionLevel.ActionRequired)
                    failures.Add("the task's agent asked and the session does not say it needs the person");
                var target = workspace.Targets.FirstOrDefault(each => each.View.WorkstreamId == workstreamId && each.gameObject.activeInHierarchy);
                if (target == null)
                {
                    failures.Add("the asking task's character does not stand on the placed shelf (placed " + (placement.Preferred != null) + ", stage visible " + stage.Visible
                        + ", agents shown " + workspace.Presentation.AgentsVisible + ", " + workspace.Targets.Count() + " targets, " + (connection.Session?.State.Workstreams.Count ?? -1) + " tasks)");
                    return failures;
                }
                var view = target.View;
                void Advance(float seconds)
                {
                    foreach (var each in workspace.Targets.Where(each => each.gameObject.activeInHierarchy)) each.View.AdvanceForRender(seconds);
                }
                // At home before the call: the task waits for the person, so its ring stands under it.
                for (var settle = 0; settle < 20; settle++) Advance(0.1f);
                Draw();
                Advance(0.1f);
                if (view.NeedsYouRing is not MeshRenderer homeRing) { failures.Add("before the call, the asking task's character has no amber ring under it"); return failures; }
                var ringHome = homeRing.transform.position;
                var bodyHome = view.transform.position;

                // The question opens, and its moment calls the character.
                // Each new moment's arrival from the clock it came at: the question's at the call, the one after the page closes at the close.
                bar.MomentArrived += _ => momentAt = clock;
                menu.OpenFile(workstreamId, FileSection.Waiting);
                var refresh = System.Diagnostics.Stopwatch.StartNew();
                do { Draw(); Thread.Sleep(50); } while (refresh.Elapsed.TotalSeconds < FileColumn.RefreshSeconds + 0.5 && stage.Flying == null);
                Draw();
                if (stage.Flying != workstreamId) { failures.Add("the question opened and no character was called (flying: " + (stage.Flying ?? "none") + ", bar " + (bar.Shown?.State.ToString() ?? "none") + ")"); return failures; }
                var calledAt = clock;
                momentAt ??= calledAt;
                var landed = RoomMotion.FlightBreathSeconds + RoomMotion.FlightSeconds;
                const float ReleasedAt = 2f;
                // The person's view of it all: from the eyes toward the middle of the shelf and the panel.
                var beside = MenuDirector.BesidePanel(bar.Panel.transform, bar.Panel.Size);
                // Where it lands as the stage holds it: at or below the eyes, never within half a metre of the head.
                var lands = beside;
                lands.y = Mathf.Min(lands.y, eyes.y);
                if ((lands - eyes).magnitude < CharacterStage.FlightClearOfHeadM) lands = eyes + (lands - eyes).normalized * CharacterStage.FlightClearOfHeadM;
                Debug.Log("Halcyonic: flight walk: home " + bodyHome.ToString("F3") + " (" + (bodyHome - eyes).magnitude.ToString("0.00") + " m from the eyes), beside the panel "
                    + beside.ToString("F3") + " (" + (beside - eyes).magnitude.ToString("0.00") + " m), landing " + lands.ToString("F3") + ", the panel " + bar.Panel.transform.position.ToString("F3")
                    + " (" + (bar.Panel.transform.position - eyes).magnitude.ToString("0.00") + " m)");
                // Home stands far to one side and the landing beside the panel near the eyes, further apart than one look takes in, so the
                // clip is filmed from just behind the person and above, as a film camera over the shoulder, holding both in a 100 degree view.
                var across = ((bodyHome - eyes).normalized + (lands - eyes).normalized).normalized;
                across.y = 0f;
                across = across.sqrMagnitude > 1e-6f ? across.normalized : Vector3.forward;
                camera.transform.position = eyes - across * 0.9f + Vector3.up * 0.35f;
                camera.fieldOfView = 100f;
                camera.transform.rotation = Quaternion.LookRotation((bodyHome + lands) / 2f - camera.transform.position);
                var released = false;
                var pinchedBuilding = false;
                var ringMoved = 0f;
                string? ringPast = null;
                for (var frame = 0; frame <= 40; frame++)
                {
                    var seconds = frame * 0.1f;
                    clock = calledAt + seconds;
                    if (!released && seconds >= ReleasedAt - 1e-4f)
                    {
                        released = true;
                        if (!Press(Footer.Close)) { menu.Navigator.CloseBeside(); Debug.Log("Halcyonic: flight walk: no drawn Close, the beside closed by its navigator"); }
                        if (stage.FlightNow.Along >= 1f && Mathf.Approximately(stage.FlightNow.PanelShown, 1f) && stage.Flying != null)
                            Debug.Log("Halcyonic: flight walk: the page closed at " + seconds.ToString("0.0") + " s");
                    }
                    Draw();
                    Advance(0.1f);
                    // Approve only what is shown (AUDIT3's FL1): a pinch on the question's first answer while the panel is held for the
                    // flight, and while its last line still builds, takes nothing; one after it stands whole takes.
                    var at = "flight-" + frame.ToString("00", CultureInfo.InvariantCulture);
                    bool Pinch()
                    {
                        if (bar.Button(FileScreens.Choose, "0") is not RoomButton answer || !answer.gameObject.activeInHierarchy) return false;
                        var took = false;
                        System.Action counted = () => took = true;
                        answer.Press.Pressed += counted;
                        answer.Press.SettleForRender();
                        answer.Press.PressForRender(985);
                        answer.Press.Pressed -= counted;
                        return took;
                    }
                    if (frame == 3 && Pinch()) failures.Add(at + ": the question's answer took a pinch while its panel was held for the flight");
                    if (!pinchedBuilding && seconds > landed && !bar.PanelBuilt)
                    {
                        pinchedBuilding = true;
                        if (Pinch()) failures.Add(at + ": the question's answer took a pinch while its panel's lines still built");
                        else Debug.Log("Halcyonic: flight walk: " + at + ": a pinch while the panel built took nothing");
                    }
                    if (frame == 19)
                    {
                        if (!bar.PanelBuilt) failures.Add(at + ": the question's panel is not whole a second after the landing");
                        else if (!Pinch()) failures.Add(at + ": the question's answer took no pinch once its panel stood whole");
                    }
                    ForceMeshes(root);
                    var image = Render(camera, texture);
                    var name = "flight-" + frame.ToString("00", CultureInfo.InvariantCulture);
                    File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                    var flight = stage.FlightNow;
                    // The bot in the frame, whole, the whole way.
                    var parts = view.GetComponentsInChildren<Renderer>(false).Where(each => each.enabled).ToList();
                    if (parts.Count == 0) failures.Add(name + ": the character draws nothing");
                    else
                    {
                        var box = parts.Select(each => each.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
                        var seen = camera.WorldToViewportPoint(box.center);
                        if (seen.z <= 0f || seen.x < 0.02f || seen.x > 0.98f || seen.y < 0.02f || seen.y > 0.98f)
                            failures.Add(name + ": the character stands out of the frame, at " + seen.ToString("F2"));
                    }
                    // The ring stays on the floor where it stood, never flying, and is gone once the lift-off breath is over.
                    var ring = view.NeedsYouRing;
                    if (ring != null && (seconds > 0f && seconds < 4f - 1e-4f || stage.Flying != null)) ringMoved = Mathf.Max(ringMoved, Vector3.Distance(ring.transform.position, ringHome));
                    if (ring != null && seconds >= RoomMotion.FlightBreathSeconds + 0.05f && stage.Flying != null) ringPast ??= name + " (" + seconds.ToString("0.0") + " s)";
                    // Landed beside the panel, and the panel waits for it.
                    if (!released && seconds > landed + 0.05f && Vector3.Distance(view.transform.position, lands) > 0.05f)
                        failures.Add(name + ": the character stands " + Vector3.Distance(view.transform.position, lands).ToString("0.00") + " m from its landing beside the panel");
                    if (seconds < landed - 0.05f && bar.PanelArrival.Shown > 0.001f)
                        failures.Add(name + ": the panel stands " + bar.PanelArrival.Shown.ToString("0.00") + " in before the character lands");
                    Debug.Log("Halcyonic: flight walk: " + name + ": " + seconds.ToString("0.0") + " s, along " + flight.Along.ToString("0.00") + ", over " + flight.OverM.ToString("0.000")
                        + " m, the bot at " + view.transform.position.ToString("F3") + ", the panel " + bar.PanelArrival.Shown.ToString("0.00") + " in, the ring "
                        + (ring == null ? "gone" : "at " + ring.transform.position.ToString("F3")) + ", the tag " + (view.Labels.gameObject.activeInHierarchy ? "shown" : "home") + ", flying " + (stage.Flying ?? "none") + ".");
                }
                if (ringMoved > 0.01f) failures.Add("the amber ring moved " + ringMoved.ToString("0.000") + " m from where it stood: it flies with the character");
                if (ringPast != null) failures.Add("the amber ring still stands at " + ringPast + ", past the lift-off");
                // Home again, the task still waiting: its ring under it once more.
                for (var settle = 0; settle < 10; settle++) { clock += 0.1f; Draw(); Advance(0.1f); }
                if (stage.Flying != null) failures.Add("two seconds after the page closed, the character is not home (" + stage.FlightNow.Along.ToString("0.00") + " along)");
                if (view.NeedsYouRing is not MeshRenderer backRing) failures.Add("home again while the task still waits, the amber ring is not back under the character");
                else if (Vector3.Distance(backRing.transform.position, ringHome) > 0.01f) failures.Add("home again, the amber ring stands " + Vector3.Distance(backRing.transform.position, ringHome).ToString("0.000") + " m from its place");
                Debug.Log("Halcyonic: flight walk: the ring moved at most " + ringMoved.ToString("0.000") + " m while the character flew; home again, it is " + (view.NeedsYouRing != null ? "back" : "gone") + ".");
            }
            finally
            {
                GlazeMotion.Still = stillWas;
                try
                {
                    if (roomRoot != null) UnityEngine.Object.DestroyImmediate(roomRoot);
                    if (placement != null) PlacementField(typeof(RoomPlacement), "root").SetValue(placement, null);
                    UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(texture);
                }
                finally
                {
                    foreach (var item in strings) { if (item.Had) PlayerPrefs.SetString(item.Key, item.Value); else PlayerPrefs.DeleteKey(item.Key); }
                    foreach (var item in integers) { if (item.Had) PlayerPrefs.SetInt(item.Key, item.Value); else PlayerPrefs.DeleteKey(item.Key); }
                    PlayerPrefs.Save();
                }
            }
            return failures;
        }
    }
}

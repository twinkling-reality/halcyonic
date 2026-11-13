#nullable enable
using System;
using System.Collections.Generic;
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
        /// The shelf walk (lead, 2026-11-13T19:34Z): the proof that the plane is for reading and the room keeps clear of it. Under the
        /// redesign, at each text size, a person places the agent shelf through the room's own Place Agents and its Apply, opens a
        /// task's page over it, then closes the page. It fails where the shelf stands under the plane while the page is open (seen
        /// from the eyes, a degree at least, and by the product's own ClearsShelf), where a row does not lay whole or a part is lost,
        /// where the shelf's own place is rewritten, and where the shelf does not come back once the page is closed. The release
        /// WorkspaceDirector, RoomPlacement, RoomControls and CharacterStage are the real ones, on a synthetic session that reaches
        /// no network (WorkspaceRender.TaskPlacement's harness). Renders land in Builds/ShelfWalkRenders.
        /// </summary>
        public static void ShelfWalkCheck()
        {
            InstrumentFontAtlas.ValidateSaved();
            var designWas = RoomDesign.Enabled;
            RoomDesign.Enabled = true;
            List<string> failures;
            try
            {
                failures = GlazeChecks.AtEachTextSize(variant =>
                {
                    var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "ShelfWalkRenders", variant));
                    Directory.CreateDirectory(folder);
                    foreach (var old in Directory.GetFiles(folder)) File.Delete(old);
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    ViewField.Current = FieldChecks.Quest3S;
                    List<string> found;
                    try { found = RenderShelfWalk(folder); }
                    catch (Exception error) { found = new List<string> { "the shelf walk threw " + error.GetBaseException().GetType().Name + ": " + error.GetBaseException().Message }; }
                    finally { ViewField.Current = null; KeepFontAssetsAsCommitted(); }
                    foreach (var failure in found) Debug.LogError("Halcyonic: shelf walk: " + failure);
                    if (found.Count == 0) Debug.Log("Halcyonic: shelf walk: every check passed; " + folder);
                    return found;
                });
            }
            finally { RoomDesign.Enabled = designWas; }
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static List<string> RenderShelfWalk(string folder)
        {
            var failures = new List<string>();
            var stringKeys = new[] { "Halcyonic.RoomSpace", "Halcyonic.RoomPlacements", "halcyonic.stage.visibility" };
            var intKeys = new[] { "Halcyonic.RoomAccessDeclined", "Halcyonic.WorkspaceOpened" };
            var strings = stringKeys.Select(key => (Key: key, Had: PlayerPrefs.HasKey(key), Value: PlayerPrefs.GetString(key, ""))).ToList();
            var integers = intKeys.Select(key => (Key: key, Had: PlayerPrefs.HasKey(key), Value: PlayerPrefs.GetInt(key, 0))).ToList();
            var root = new GameObject("Shelf walk");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            RoomPlacement? placement = null;
            GameObject? roomRoot = null;
            var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
            using var session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
            var work = Work.Running();
            var journal = new JournalInfo { JournalId = "render-journal", Origin = JournalOrigin.Live };
            using var handler = new PlacementHistory { Journal = journal };
            using var api = new ControlPlaneApi(new Uri("http://127.0.0.1:9/"), "render", handler);
            // The plane's own lines: a row not laid whole, a bar past its footprint, a part lost, the older view, and each form change.
            var heard = new List<string>();
            var widths = new List<string>();
            Application.LogCallback listen = (message, _, _) =>
            {
                if (message.StartsWith("Halcyonic: the bar did not lay whole", StringComparison.Ordinal)
                    || message.StartsWith("Halcyonic: the bar laid past its largest footprint", StringComparison.Ordinal)
                    || message.StartsWith("Halcyonic: the general reader lost a part", StringComparison.Ordinal)
                    || message.StartsWith("Halcyonic: older view drew a whole screen", StringComparison.Ordinal)) lock (heard) heard.Add(message);
                if (message.StartsWith("Halcyonic: the plane lays its panel", StringComparison.Ordinal)) lock (widths) widths.Add(message);
            };
            Application.logMessageReceived += listen;
            try
            {
                foreach (var key in stringKeys) PlayerPrefs.DeleteKey(key);
                foreach (var key in intKeys) PlayerPrefs.DeleteKey(key);
                PlayerPrefs.SetInt("Halcyonic.WorkspaceOpened", 1);
                var snapshot = new Snapshot { Journal = journal, Position = 1, Projects = work.State.Projects.Values.ToList(),
                    Workstreams = new List<WorkstreamView> { work.Workstream }, Executions = new List<ExecutionView> { work.Execution }, Runtimes = work.State.Runtimes.ToList() };
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
                // The stage as WorkspaceDirector.StageNow gives it: the standing characters, and the placed shelf's envelope where the
                // director takes one (through Stage's widest constructor, so this walk also compiles where it does not yet).
                var stageMade = typeof(MenuDirector.Stage).GetConstructors().OrderByDescending(each => each.GetParameters().Length).First();
                MenuDirector.Stage StageNow()
                {
                    var standing = workspace.Targets.Where(target => target.gameObject.activeInHierarchy).ToList();
                    Vector3[]? shelf = null;
                    if (stage.Visible)
                    {
                        shelf = new Vector3[8];
                        // The shelf's own place where the stage keeps one (CharacterStage.OwnShelf: never where it stands aside), as
                        // WorkspaceDirector writes it; else where it stands.
                        var own = typeof(CharacterStage).GetProperty("OwnShelf")?.GetValue(stage) is Pose kept ? kept : new Pose(stage.Shelf.position, stage.Shelf.rotation);
                        CharacterStage.WriteShelfCorners(own, stage.ShelfWidth, eyes, shelf);
                    }
                    // And the plane's word to stand the shelf aside, as WorkspaceDirector hands it (CharacterStage.ShelfTurnDegrees).
                    var turn = typeof(CharacterStage).GetProperty("ShelfTurnDegrees");
                    Action<float> standAside = degrees => turn?.SetValue(stage, degrees);
                    var given = new object?[] { eyes, Vector3.forward, standing, null, false, shelf, standAside };
                    return (MenuDirector.Stage)stageMade.Invoke(stageMade.GetParameters()
                        .Select((parameter, index) => index < given.Length ? given[index] : parameter.HasDefaultValue ? parameter.DefaultValue : null).ToArray());
                }
                var setup = new MenuDirector.Setup
                {
                    Presentation = workspace.Presentation, Commands = new CommandFactory(client), InitialPlacementReady = () => true,
                    State = () => connection.Session?.State, Session = () => connection.Session, Connected = () => connection.Session?.Status.IsLive == true,
                    ConnectionTarget = () => ControlPlaneTarget.Paired(PlacementPaired), Api = () => api,
                    Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    Overview = () => WorkOverview.Of(connection.Session!.State, new StageVisibility(), _ => true), Bar = place => TasksColumn.Bar(place, connection.Session?.State),
                    File = (bound, id) => (FileColumn?)PlacementCall(workspace, "FileFor", bound, id),
                    Submit = (_, _) => throw new InvalidOperationException("The shelf walk must not submit a command."),
                    CharacterOf = id => workspace.Targets.FirstOrDefault(target => target.View.WorkstreamId == id && target.gameObject.activeInHierarchy),
                    StageNow = StageNow,
                };
                setup.SpatialWorkspace = true;
                var menu = MenuDirector.Create(host.transform, setup);
                PlacementField(typeof(WorkspaceDirector), "menu").SetValue(workspace, menu);
                var plane = menu.Plane;

                void Draw()
                {
                    for (var step = 0; step < 4; step++)
                    {
                        menu.UpdateForRender(); menu.DrawNow(); plane.Advance(MenuPlane.OpeningSeconds);
                        if (plane.KitShows) { plane.Kit.SettleForRender(); plane.Kit.DrawnForRender(); }
                    }
                    PlacementCall(stage, "LateUpdate");
                }
                // The shelf as it stands now, its envelope as the eyes see it.
                GlazeChecks.Extent ShelfSeen()
                {
                    var corners = new Vector3[8];
                    CharacterStage.WriteShelfCorners(new Pose(stage.Shelf.position, stage.Shelf.rotation), stage.ShelfWidth, eyes, corners);
                    float left = float.MaxValue, right = float.MinValue, bottom = float.MaxValue, top = float.MinValue;
                    foreach (var corner in corners)
                    {
                        var toward = corner - eyes;
                        var yaw = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
                        var elevation = Mathf.Atan2(toward.y, Mathf.Max(new Vector2(toward.x, toward.z).magnitude, 1e-4f)) * Mathf.Rad2Deg;
                        left = Mathf.Min(left, yaw); right = Mathf.Max(right, yaw); bottom = Mathf.Min(bottom, elevation); top = Mathf.Max(top, elevation);
                    }
                    return new GlazeChecks.Extent("the shelf", left, right, bottom, top);
                }
                var shots = 0;
                void Shot(string moment)
                {
                    Draw();
                    foreach (var button in root.GetComponentsInChildren<RoomButton>(false)) button.Settle();
                    foreach (var band in root.GetComponentsInChildren<RoomBand>(false)) band.Settle();
                    ForceMeshes(root);
                    camera.transform.rotation = PlaneLayout.Facing(plane.Direction);
                    var image = Render(camera, texture);
                    var name = "shelf-" + (++shots).ToString("00") + "-" + moment;
                    File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                    lock (heard)
                    {
                        failures.AddRange(heard.Distinct().Select(line => name + ": the plane said: " + line));
                        heard.Clear();
                    }
                    if (plane.KitShows && plane.Kit.Bar.Shown is RoomBar laid && typeof(RoomBar).GetProperty("LostPart")?.GetValue(laid) is true)
                        failures.Add(name + ": a part of the frame is not on screen (the bar's LostPart)");
                    Debug.Log("Halcyonic: shelf walk: " + name + ": the shelf stands " + ShelfSeen());
                }
                // The shelf travels aside and back as the eye follows it (XR, 2026-11-13T21:06Z): three seconds of drawn frames, so a
                // moment is taken where it has arrived.
                void Travel()
                {
                    var until = DateTime.UtcNow.AddSeconds(3);
                    while (DateTime.UtcNow < until) { Draw(); Thread.Sleep(50); }
                }
                // A press through the redesign's drawn control, else through the frame the plane shows that offers it, said so.
                bool Press(string action, string? key = null)
                {
                    Draw();
                    if (plane.KitShows && plane.Kit.Bar.Button(action, key) is RoomButton button && button.gameObject.activeInHierarchy)
                    {
                        // A placement's press is taken by the director (MenuDirector.OnActed), not the navigator, so whether it took is
                        // judged by what it changed: the shelf placed, the page closed (each step below says so where it did not).
                        button.Press.SettleForRender();
                        button.Press.PressForRender(985);
                        Draw();
                        return true;
                    }
                    foreach (var (kind, view) in plane.Shown)
                    {
                        if (view.Frame is not MenuFrame frame || !(frame.Lines.Any(line => line.Action == action && line.Key == key && line.Available)
                            || frame.Footer.All.Any(slot => slot.Prompt.Id == action && slot.Prompt.Available))) continue;
                        if (!menu.PressForRender(kind, action, key, frame, null)) continue;
                        Debug.Log("Halcyonic: shelf walk: no drawn control for " + action + ": the frame of record took it");
                        Draw();
                        return true;
                    }
                    return false;
                }

                // 0. A placing cancelled (AUDIT, 2026-11-13T22:31Z): Place Agents, then its Close, with no shelf placed; the plane must decide
                // its width again within two draws (it held the floor's width while placing), never wait for another change.
                controlsType.GetMethod("PlaceAgents", BindingFlags.Public | BindingFlags.Instance)!.Invoke(controls, null);
                Draw();
                bool decidesWidth;
                lock (widths) decidesWidth = widths.Count > 0;
                if (Press(Footer.Close))
                {
                    lock (widths) widths.Clear();
                    Draw();
                    Draw();
                    lock (widths)
                    {
                        // Only where the plane decides a width at all (a tip with the plane's width rule says so while placing).
                        if (!decidesWidth) Debug.Log("Halcyonic: shelf walk: the plane says no width while placing here: the cancelled placing's width is not measured");
                        else if (widths.Count == 0) failures.Add("a cancelled placing: no width line within two draws of its Close (the plane waits for another change)");
                        else Debug.Log("Halcyonic: shelf walk: a cancelled placing, then: " + widths[0]);
                    }
                }
                else failures.Add("nothing drawn closes the shelf's placing");
                // 1. Place Agents, as the room's control offers it, and Apply.
                controlsType.GetMethod("PlaceAgents", BindingFlags.Public | BindingFlags.Instance)!.Invoke(controls, null);
                Draw();
                Shot("placing");
                // What the room had to give at the moment of placing (AUDIT, 2026-11-13T22:01Z): how wide the plane stands as the eyes see
                // it, and the free room the measured field leaves beside it each side (the plane's width step is the plane's own line).
                var planeSeen = GlazeChecks.Of("the plane", eyes, root.GetComponentsInChildren<Renderer>(false)
                    .Where(renderer => renderer.enabled && !renderer.transform.IsChildOf(stage.Shelf) && OwnerOf(renderer.transform)?.StartsWith("Room", StringComparison.Ordinal) == true));
                if (!planeSeen.IsEmpty && ViewField.Current is ViewField seenField)
                    Debug.Log("Halcyonic: shelf walk: placing, the plane stands " + GlazeChecks.Degrees(planeSeen.Left) + " to " + GlazeChecks.Degrees(planeSeen.Right)
                        + " across; the field leaves " + GlazeChecks.Degrees(planeSeen.Left + seenField.Left) + " degrees free on the left and "
                        + GlazeChecks.Degrees(seenField.Right - planeSeen.Right) + " on the right");
                if (placement.Preview == null) failures.Add("Place Agents offered no place for the shelf in the field (the room must yield, never refuse for the text size)");
                if (!Press(AgentShelfColumn.Apply)) failures.Add("nothing drawn applies the shelf's place");
                PlacementCall(stage, "LateUpdate"); stage.Refresh(); Draw();
                if (placement.Preferred is not Pose placed)
                {
                    failures.Add("Apply placed no shelf");
                    return failures;
                }
                var home = (stage.Shelf.position, stage.Shelf.rotation);
                Shot("placed");

                // 2. The task's page opened over it: the shelf stands clear of the plane, its own place kept.
                menu.OpenFile(work.Workstream.WorkstreamId);
                var refresh = System.Diagnostics.Stopwatch.StartNew();
                do { Draw(); Thread.Sleep(50); } while (refresh.Elapsed.TotalSeconds < FileColumn.RefreshSeconds + 0.5);
                Travel();
                Shot("page-open");
                var shelf = ShelfSeen();
                var drawn = root.GetComponentsInChildren<Renderer>(false)
                    .Where(renderer => renderer.enabled && !renderer.transform.IsChildOf(stage.Shelf) && OwnerOf(renderer.transform)?.StartsWith("Room", StringComparison.Ordinal) == true)
                    .Select(renderer => GlazeChecks.Of(OwnerOf(renderer.transform) + "/" + renderer.name, eyes, new[] { renderer }))
                    .Where(extent => !extent.IsEmpty).ToList();
                foreach (var near in drawn.Where(part => part.Apart(shelf) < GlazeChecks.GapDegrees - 1e-3f).Take(3))
                    failures.Add("page open: " + shelf + " is " + GlazeChecks.Degrees(near.Apart(shelf)) + " degrees from " + near + ", under a degree: the shelf stands under the plane");
                var envelope = new Vector3[8];
                CharacterStage.WriteShelfCorners(new Pose(stage.Shelf.position, stage.Shelf.rotation), stage.ShelfWidth, eyes, envelope);
                if (!workspace.ClearsShelf(envelope, eyes)) failures.Add("page open: the product's own ClearsShelf says the plane stands over the shelf");
                if (placement.Preferred is not Pose kept || Vector3.Distance(kept.position, placed.position) > 0.001f || Quaternion.Angle(kept.rotation, placed.rotation) > 0.1f)
                    failures.Add("page open: the shelf's own place was rewritten");
                var aside = Vector3.Distance(stage.Shelf.position, home.position);
                Debug.Log("Halcyonic: shelf walk: page open, the shelf stands " + aside.ToString("0.000") + " m from its place");

                // 3. The page closed: the shelf back in its own place.
                if (!Press(Footer.Close)) { failures.Add("nothing drawn closes the task's page"); menu.Navigator.CloseBeside(); }
                Draw();
                if (menu.Navigator.Beside != null) failures.Add("page closed: Close was pressed and the page stayed");
                Travel();
                Shot("page-closed");
                var back = Vector3.Distance(stage.Shelf.position, home.position);
                if (back > 0.01f || Quaternion.Angle(stage.Shelf.rotation, home.rotation) > 0.5f)
                    failures.Add("page closed: the shelf did not come back to its place (" + back.ToString("0.000") + " m away)");
            }
            finally
            {
                Application.logMessageReceived -= listen;
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

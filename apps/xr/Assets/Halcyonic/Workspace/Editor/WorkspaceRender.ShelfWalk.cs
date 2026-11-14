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
                    // Shaders compile before they draw (XR's stage mend, 2026-11-14): no stand-in colour reaches a pixel check.
                    var asyncShadersWere = ShaderUtil.allowAsyncCompilation;
                    ShaderUtil.allowAsyncCompilation = false;
                    ViewField.Current = FieldChecks.Quest3S;
                    List<string> found;
                    try { found = RenderShelfWalk(folder); }
                    catch (Exception error) { found = new List<string> { "the shelf walk threw " + error.GetBaseException().GetType().Name + ": " + error.GetBaseException().Message }; }
                    finally { ShaderUtil.allowAsyncCompilation = asyncShadersWere; ViewField.Current = null; KeepFontAssetsAsCommitted(); }
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
            string? reasons = null;
            string? stoppedShort = null;
            Application.LogCallback listen = (message, _, _) =>
            {
                if (message.StartsWith("Halcyonic: the plane reasons", StringComparison.Ordinal)) lock (widths) reasons = message;
                if (message.StartsWith("Halcyonic: the drag stopped short", StringComparison.Ordinal)) lock (widths) stoppedShort = message;
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
                string? lastWidth;
                // Cleared before the Close, since its own width is laid within the press's draw (XR, 2026-11-14T00:47Z).
                lock (widths) { decidesWidth = widths.Count > 0; lastWidth = widths.LastOrDefault(); widths.Clear(); }
                if (Press(Footer.Close))
                {
                    Draw();
                    Draw();
                    lock (widths)
                    {
                        // Only where the plane decides a width at all (a tip with the plane's width rule says so while placing). Where the
                        // width it last laid was not a placing's (its line names no ", placing"), the plane already stands as it does with the
                        // placing over, so no new line is owed (charU, 2026-11-14).
                        if (!decidesWidth) Debug.Log("Halcyonic: shelf walk: the plane says no width while placing here: the cancelled placing's width is not measured");
                        else if (widths.Count > 0) Debug.Log("Halcyonic: shelf walk: a cancelled placing, then: " + widths[0]);
                        else if (lastWidth != null && lastWidth.Contains(", placing")) failures.Add("a cancelled placing: no width line within two draws of its Close, the last laid a placing's: " + lastWidth);
                        else Debug.Log("Halcyonic: shelf walk: a cancelled placing: the plane already stood as with no placing: " + lastWidth);
                    }
                }
                else failures.Add("nothing drawn closes the shelf's placing");
                // 1. Place Agents, as the room's control offers it, and Apply.
                lock (widths) widths.Clear();
                controlsType.GetMethod("PlaceAgents", BindingFlags.Public | BindingFlags.Instance)!.Invoke(controls, null);
                Draw();
                Shot("placing");
                // What the room had to give at the moment of placing (AUDIT, 2026-11-13T22:01Z): how wide the plane stands as the eyes see
                // it, and the free room the measured field leaves beside it each side (the plane's width step is the plane's own line).
                var planeSeen = GlazeChecks.Of("the plane", eyes, root.GetComponentsInChildren<Renderer>(false)
                    .Where(renderer => renderer.enabled && !renderer.transform.IsChildOf(stage.Shelf) && OwnerOf(renderer.transform)?.StartsWith("Room", StringComparison.Ordinal) == true));
                if (!planeSeen.IsEmpty && ViewField.Current is ViewField seenField)
                    Debug.Log("Halcyonic: shelf walk: placing, the plane stands " + GlazeChecks.Degrees(planeSeen.Left) + " to " + GlazeChecks.Degrees(planeSeen.Right)
                        + " across; the field leaves " + GlazeChecks.Degrees((float)(planeSeen.Left + seenField.Left)) + " degrees free on the left and "
                        + GlazeChecks.Degrees((float)(seenField.Right - planeSeen.Right)) + " on the right");
                // What the product's own shelf search keeps clear of at this moment (the lead's 2026-11-14T07:12Z ruling 1): the
                // console it asks the plane for, from where the plane faces, and how far the plane stands, so the colour journey's
                // fixture can be held to what a person meets.
                var flatToPlane = plane.transform.position - eyes;
                flatToPlane.y = 0f;
                if (plane.ConsoleExtentFrom(eyes) is ConsoleExtent keptClear)
                    Debug.Log("Halcyonic: shelf walk: placing at text size " + GlazeText.Scale.ToString("0.##") + ", the search keeps clear of the console "
                        + GlazeChecks.Degrees(Mathf.DeltaAngle(plane.Direction.Yaw, keptClear.LeftYaw)) + " to " + GlazeChecks.Degrees(Mathf.DeltaAngle(plane.Direction.Yaw, keptClear.RightYaw))
                        + " across from where the plane faces, foot " + GlazeChecks.Degrees(keptClear.BottomElevation) + " up; the plane stands "
                        + flatToPlane.magnitude.ToString("0.00") + " m from the eyes across the floor, at " + plane.transform.position.ToString("F3")
                        + ", facing " + GlazeChecks.Degrees(plane.Direction.Yaw) + " degrees at " + GlazeChecks.Degrees(plane.Direction.Elevation) + " up");
                else Debug.Log("Halcyonic: shelf walk: placing, the plane gives no console to keep clear of");
                if (placement.Preview == null) failures.Add("Place Agents offered no place for the shelf in the field (the room must yield, never refuse for the text size)");
                // One placing, one width (AUDIT, 2026-11-14T01:01Z): from the placing's start to its Apply the panel keeps the floor's
                // width, so at most one width line, never the panel widening and narrowing under the person while they place.
                // Its Apply then lays one width, the placed shelf's, never a wider one first while the shelf is not yet staged (charV: 440
                // within Apply's press, then 360 a draw later).
                string Widths() => string.Join(" then ", widths.Select(line => line.Replace("Halcyonic: the plane lays its panel ", "")));
                lock (widths)
                {
                    if (widths.Count > 1) failures.Add("a placing laid " + widths.Count + " widths before its Apply: " + Widths());
                    widths.Clear();
                }
                if (!Press(AgentShelfColumn.Apply)) failures.Add("nothing drawn applies the shelf's place");
                PlacementCall(stage, "LateUpdate"); stage.Refresh(); Draw(); Draw();
                lock (widths)
                {
                    if (widths.Count > 1) failures.Add("Apply laid " + widths.Count + " widths: " + Widths());
                    else Debug.Log("Halcyonic: shelf walk: Apply laid " + (widths.Count == 0 ? "no new width" : Widths()));
                }
                if (placement.Preferred is not Pose placed)
                {
                    failures.Add("Apply placed no shelf");
                    return failures;
                }
                var home = (stage.Shelf.position, stage.Shelf.rotation);
                Shot("placed");
                // The first shelf a person places stands where they look (lead, 2026-11-14T01:13Z, ruling 1): whole inside what the field check
                // says a person sees at head level without turning the head. Its degrees out are said either way.
                var firstShelf = new Vector3[8];
                CharacterStage.WriteShelfCorners(new Pose(stage.Shelf.position, stage.Shelf.rotation), stage.ShelfWidth, eyes, firstShelf);
                Debug.Log("Halcyonic: shelf walk: the first shelf placed stands " + ShelfSeen() + ", seen with the head level and facing ahead");
                if (ViewField.Current is ViewField headLevel)
                {
                    failures.AddRange(GlazeChecks.InsideField("the first shelf placed, the head level and facing ahead", firstShelf, eyes, eyes + Vector3.forward, 0f, headLevel));
                    // The ruling's fallback reading, measured beside it (lead, 2026-11-14T02:21Z): every character and its label inside the field,
                    // only the shelf's empty end allowed past. Said, not failed, until the batch says it landed with the fallback.
                    var onShelf = workspace.Targets.Where(target => target.gameObject.activeInHierarchy).SelectMany(target => target.GetComponentsInChildren<Renderer>(false))
                        .Where(renderer => renderer.enabled).SelectMany(renderer =>
                        {
                            var box = renderer.bounds;
                            return new[] { -1f, 1f }.SelectMany(x => new[] { -1f, 1f }.SelectMany(y => new[] { -1f, 1f }.Select(z => box.center + Vector3.Scale(box.extents, new Vector3(x, y, z)))));
                        }).ToArray();
                    if (onShelf.Length == 0) Debug.Log("Halcyonic: shelf walk: no character stands on the first shelf to hold to the field");
                    else foreach (var outside in GlazeChecks.InsideField("the first shelf's characters and their labels", onShelf, eyes, eyes + Vector3.forward, 0f, headLevel))
                            Debug.Log("Halcyonic: shelf walk: by the fallback reading, " + outside);
                    // Checked, not assumed (lead, 02:21Z; XR 02:23Z): the shelf's characters never touch, each as the eyes see it with its label,
                    // and no label is cut.
                    var figures = workspace.Targets.Where(target => target.gameObject.activeInHierarchy)
                        .Select(target => GlazeChecks.Of(OwnerOf(target.transform) ?? target.name, eyes, target.GetComponentsInChildren<Renderer>(false).Where(renderer => renderer.enabled)))
                        .Where(extent => !extent.IsEmpty).ToList();
                    for (var one = 0; one < figures.Count; one++)
                        for (var other = one + 1; other < figures.Count; other++)
                            if (figures[one].Apart(figures[other]) <= 0f) failures.Add("the first shelf: " + figures[one] + " touches " + figures[other]);
                    foreach (var label in workspace.Targets.Where(target => target.gameObject.activeInHierarchy).SelectMany(target => target.GetComponentsInChildren<TMPro.TMP_Text>(false)))
                        if (label.enabled && label.isTextTruncated) failures.Add("the first shelf: a character's label is cut: \"" + label.text + "\"");
                    Debug.Log("Halcyonic: shelf walk: the first shelf's characters: " + figures.Count + " standing, " + string.Join("; ", figures.Select(figure => figure.ToString())));
                }
                else failures.Add("the first shelf placed: no field to hold it to");

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
                // Beside the plane's own "the plane reasons" line (XR, 2026-11-14T00:59Z): the bar's foot line and the band's place as drawn,
                // each as the eyes see it, so the part the plane reasons apart from what it draws can be named.
                void ReasonedAgainstDrawn(string when)
                {
                    if (!plane.KitShows) return;
                    float Elevation(Vector3 point)
                    {
                        var toward = point - eyes;
                        return Mathf.Atan2(toward.y, Mathf.Max(new Vector2(toward.x, toward.z).magnitude, 1e-4f)) * Mathf.Rad2Deg;
                    }
                    var kitNow = plane.Kit;
                    // The bar as drawn, every redesign part but the band's, the handle's (reasoned as a part of its own) and the shelf's as one
                    // (AUDIT, 2026-11-14T01:02Z), beside the plane's reasoned bar.
                    var barHandle = Handle();
                    var barDrawn = GlazeChecks.Of("the bar", eyes, root.GetComponentsInChildren<Renderer>(false)
                        .Where(renderer => renderer.enabled && !renderer.transform.IsChildOf(stage.Shelf) && !renderer.transform.IsChildOf(kitNow.Band.transform)
                            && (barHandle == null || !renderer.transform.IsChildOf(barHandle.transform))
                            && OwnerOf(renderer.transform)?.StartsWith("Room", StringComparison.Ordinal) == true));
                    // Reasoned equals drawn (XR and AUDIT, 2026-11-14T01:06Z): nothing the bar or the band draws stands outside the shape the plane
                    // reasons for it by more than a tenth of a degree, and the band's top is where the plane reasons it. The reasoned shapes are
                    // the largest the plane keeps clear (the bar's largest footprint, the band at its tallest), so a part drawn smaller is not a fault.
                    var bandDrawn = GlazeChecks.Of("the band", eyes, kitNow.Band.GetComponentsInChildren<Renderer>(false).Where(renderer => renderer.enabled));
                    string? said;
                    lock (widths) said = reasons;
                    var shapes = said == null ? null : System.Text.RegularExpressions.Regex.Match(said,
                        @"the bar \((-?[\d.]+) to (-?[\d.]+) across, (-?[\d.]+) to (-?[\d.]+) up\) and the band \((-?[\d.]+) to (-?[\d.]+) across, (-?[\d.]+) to (-?[\d.]+) up\)");
                    if (shapes == null || !shapes.Success) Debug.Log("Halcyonic: shelf walk: " + when + ": the plane said no reasoned bar and band to set beside the drawn ones");
                    else
                    {
                        float At(int group) => float.Parse(shapes.Groups[group].Value, System.Globalization.CultureInfo.InvariantCulture);
                        void Within(GlazeChecks.Extent drawnPart, float left, float right, float bottom, float top, string what)
                        {
                            if (drawnPart.IsEmpty) return;
                            var past = Mathf.Max(Mathf.Max(left - drawnPart.Left, drawnPart.Right - right), Mathf.Max(bottom - drawnPart.Bottom, drawnPart.Top - top));
                            if (past > 0.1f) failures.Add(when + ": " + drawnPart + " draws " + GlazeChecks.Degrees(past) + " degrees past " + what + " as the plane reasons it ("
                                + GlazeChecks.Degrees(left) + " to " + GlazeChecks.Degrees(right) + " across, " + GlazeChecks.Degrees(bottom) + " to " + GlazeChecks.Degrees(top) + " up)");
                            else Debug.Log("Halcyonic: shelf walk: " + when + ": " + drawnPart + " stands within " + what + " as the plane reasons it, reserving "
                                + GlazeChecks.Degrees(drawnPart.Left - left) + " left, " + GlazeChecks.Degrees(right - drawnPart.Right) + " right, "
                                + GlazeChecks.Degrees(drawnPart.Bottom - bottom) + " below and " + GlazeChecks.Degrees(top - drawnPart.Top) + " above what it draws");
                        }
                        Within(barDrawn, At(1), At(2), At(3), At(4), "the bar");
                        if (kitNow.Band.Words != null)
                        {
                            Within(bandDrawn, At(5), At(6), At(7), At(8), "the band");
                            // Containment holds the drawn top under the reasoned one (above); the reasoned top is rounded out a tenth by design
                            // (XR's BandRoundOutDegrees, 01:36Z), so it may stand up to two tenths above what is drawn, never more.
                            if (!bandDrawn.IsEmpty && At(8) - bandDrawn.Top > 0.2f)
                                failures.Add(when + ": the band's top as the plane reasons it stands " + GlazeChecks.Degrees(At(8) - bandDrawn.Top) + " degrees above its drawn top");
                        }
                    }
                    Debug.Log("Halcyonic: shelf walk: " + when + ", as drawn: " + barDrawn + "; the bar's foot line " + GlazeChecks.Degrees(Elevation(kitNow.transform.TransformPoint(0f, kitNow.FootLine, 0f)))
                        + " up, the band's laid place " + GlazeChecks.Degrees(Elevation(kitNow.Band.transform.position)) + " up, " + (kitNow.Band.Words != null ? "words on the band" : "no words on the band")
                        + "; the band's parts: " + string.Join("; ", kitNow.Band.GetComponentsInChildren<Renderer>(false).Where(renderer => renderer.enabled)
                            .Select(renderer => GlazeChecks.Of(OwnerOf(renderer.transform) + "/" + renderer.name, eyes, new[] { renderer }).ToString())));
                }
                ReasonedAgainstDrawn("page open");
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
                // The held aside lets go at rest, no page on the plane (XR, 2026-11-14T02:50Z: the menu's own page, where Place Agents
                // stands, keeps the plane open): Close as a person does, three at most, until the host says it rests (read by name, so a tip
                // without KitWorkspaceHost.Resting still compiles).
                var restsProperty = typeof(KitWorkspaceHost).GetProperty("Resting");
                for (var close = 0; close < 3 && restsProperty?.GetValue(plane.Kit) is false; close++)
                {
                    if (!Press(Footer.Close)) break;
                    Draw();
                }
                Debug.Log("Halcyonic: shelf walk: page closed, the plane " + (restsProperty == null ? "says nothing of rest" : restsProperty.GetValue(plane.Kit) is true ? "at rest" : "still open"));
                Travel();
                Shot("page-closed");
                var back = Vector3.Distance(stage.Shelf.position, home.position);
                if (back > 0.01f || Quaternion.Angle(stage.Shelf.rotation, home.rotation) > 0.5f)
                    failures.Add("page closed: the shelf did not come back to its place (" + back.ToString("0.000") + " m away)");

                // 4. The person's drag (AUDIT and XR, 2026-11-14T01:07Z): the page open again, the plane held by the director's own hold and
                // raised four degrees, then lowered eight, and after each the drawn bar and band held to the shapes the plane reasons for them.
                menu.OpenFile(work.Workstream.WorkstreamId);
                Draw();
                Travel();
                // The plane's handle (FIELD's RoomHandle, the lead's ruling 3), read by name so a tip without it still compiles: its pointer
                // target, the point a hand takes hold of, and its hold area's corners for the field check (AUDIT, 01:30Z).
                Component? Handle() => typeof(KitWorkspaceHost).GetProperty("Handle")?.GetValue(plane.Kit) is Component drawnHandle
                    && drawnHandle.gameObject.activeInHierarchy ? drawnHandle : null;
                void HandleInField(string when)
                {
                    if (Handle() is not Component held || held.GetType().GetProperty("Target")?.GetValue(held) is not PointerTarget holdArea) return;
                    var area = new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(-0.5f, 0.5f), new Vector2(0.5f, 0.5f) }
                        .Select(corner => holdArea.transform.TransformPoint(new Vector3(corner.x * holdArea.Size.x, corner.y * holdArea.Size.y, 0f))).ToArray();
                    if (ViewField.Current is ViewField handleField)
                        failures.AddRange(GlazeChecks.InsideField("the plane's handle, its hold area, " + when, area, eyes, eyes + Vector3.forward, 0f, handleField));
                }
                HandleInField("at rest");
                // Where the plane stands as the eyes see it, from its kit's middle: a drag moves the plane's place, whatever field holds it.
                (float Yaw, float Up) PlaneSeen()
                {
                    var toward = plane.Kit.transform.position - eyes;
                    return (Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg, Mathf.Atan2(toward.y, Mathf.Max(new Vector2(toward.x, toward.z).magnitude, 1e-4f)) * Mathf.Rad2Deg);
                }
                const int HandPointer = 735;
                void DragBy(float degrees, string when)
                {
                    var grab = plane.Kit.transform.position;
                    var side = Vector3.Cross(Vector3.up, grab - eyes).normalized;
                    lock (widths) { reasons = null; stoppedShort = null; }
                    // By the handle as a person takes it where one is drawn: pointed, pressed at its hold point, moved, let go.
                    if (Handle() is Component handleNow && handleNow.GetType().GetProperty("Target")?.GetValue(handleNow) is PointerTarget handleTarget
                        && handleNow.GetType().GetProperty("HoldPoint")?.GetValue(handleNow) is Vector3 holdAt)
                    {
                        handleNow.GetType().GetMethod("SettleForRender")?.Invoke(handleNow, null);
                        var before = PlaneSeen();
                        var toward = Quaternion.AngleAxis(-degrees, Vector3.Cross(Vector3.up, holdAt - eyes).normalized) * (holdAt - eyes);
                        handleTarget.PointerForRender(HandPointer, Oculus.Interaction.PointerEventType.Hover, holdAt);
                        handleTarget.PointerForRender(HandPointer, Oculus.Interaction.PointerEventType.Select, holdAt);
                        Draw();
                        for (var step = 1; step <= 4; step++)
                        {
                            handleTarget.PointerForRender(HandPointer, Oculus.Interaction.PointerEventType.Move, eyes + Vector3.Slerp(holdAt - eyes, toward, step / 4f));
                            Draw();
                        }
                        handleTarget.PointerForRender(HandPointer, Oculus.Interaction.PointerEventType.Unselect, eyes + toward);
                        handleTarget.PointerForRender(HandPointer, Oculus.Interaction.PointerEventType.Unhover, eyes + toward);
                        Draw();
                        Travel();
                        var moved = PlaneSeen().Up - before.Up;
                        string? heldShort;
                        lock (widths) { heldShort = stoppedShort; stoppedShort = null; }
                        // A drag the plane holds short stops at the furthest place that holds and says which rule held it (AUDIT, 02:09Z;
                        // XR's 'the drag stopped short where <rule> held it'): moving less than asked with no such line, or not at all, fails.
                        if (Mathf.Abs(moved) < 0.5f) { failures.Add(when + ": taken by its handle and dragged, the plane did not move (" + moved.ToString("0.0") + " degrees)" + (heldShort == null ? "" : "; " + heldShort)); return; }
                        if (Mathf.Abs(moved) < Mathf.Abs(degrees) * 0.5f && heldShort == null)
                            failures.Add(when + ": by its handle the plane moved " + moved.ToString("0.0") + " of " + degrees.ToString("0") + " degrees and nothing said what held it");
                        Debug.Log("Halcyonic: shelf walk: " + when + ": by its handle, the plane moved " + moved.ToString("0.0") + " degrees up" + (heldShort == null ? "" : "; the plane said: " + heldShort));
                        // Where it is let go is its place (ruling 3): a head turned 30 degrees and back leaves it there.
                        var letGo = PlaneSeen();
                        var head = Camera.main;
                        if (head != null)
                        {
                            var facing = head.transform.rotation;
                            head.transform.rotation = Quaternion.Euler(0f, 30f, 0f) * facing;
                            Travel();
                            head.transform.rotation = facing;
                            Travel();
                            if (Mathf.Abs(Mathf.DeltaAngle(PlaneSeen().Yaw, letGo.Yaw)) > 0.5f || Mathf.Abs(PlaneSeen().Up - letGo.Up) > 0.5f)
                                failures.Add(when + ": a head turned 30 degrees and back moved the plane from where it was let go");
                        }
                        ReasonedAgainstDrawn(when);
                        HandleInField(when);
                        return;
                    }
                    menu.MatureCompactMoveForRender(grab);
                    // The redesign draws no handle yet (lead, 2026-11-14T01:26Z, ruling 3: a handle is being built, and the batch does not
                    // wait for it): said, not failed, while no handle is drawn; once a part named for a handle stands on the plane, a hold
                    // that does not take it fails (AUDIT, 01:27Z), keyed on the handle's presence, never on a flag in the walk.
                    if (!menu.Dragging)
                    {
                        var handle = plane.Kit.GetComponentsInChildren<Transform>(false)
                            .FirstOrDefault(part => part.name.IndexOf("Handle", StringComparison.OrdinalIgnoreCase) >= 0 || OwnerOf(part)?.IndexOf("Handle", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (handle != null) failures.Add(when + ": the plane's handle (" + PathOf(handle) + ") is drawn and holding it took no hold of the plane");
                        else Debug.Log("Halcyonic: shelf walk: " + when + ": not walked: nothing drawn takes hold of the plane yet (its handle, ruling 3)");
                        return;
                    }
                    menu.DragSubjectForRender(eyes + Quaternion.AngleAxis(-degrees, side) * (grab - eyes));
                    menu.LetGoForRender();
                    Draw();
                    Travel();
                    ReasonedAgainstDrawn(when);
                }
                DragBy(4f, "dragged up 4 degrees");
                DragBy(-8f, "dragged down 8 degrees");
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

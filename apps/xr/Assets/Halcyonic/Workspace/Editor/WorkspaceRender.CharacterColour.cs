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
        /// <summary>Real body/material and production colour controls; synthetic journal only, no transport is started.</summary>
        public static void CharacterColourCheck()
        {
            var palette = Glaze.Palette; var scale = GlazeText.Scale; var field = ViewField.Current;
            var active = RenderTexture.active;
            var failures = new List<string>();
            try
            {
                failures.AddRange(GlazeChecks.AtEachTextSize(variant =>
                {
                    ViewField.Current = FieldChecks.Quest3S;
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "CharacterColourRenders", variant));
                    Directory.CreateDirectory(folder);
                    // Ruling (a), 2026-11-16T01:42Z: colour is drawn wholly under the product's design, the redesign with the kit, as
                    // every player build draws it (MenuDirector.Spatial's one rule: a local overlay's frame goes to the kit too).
                    return RenderCharacterColour(folder);
                }));
            }
            catch (Exception error)
            {
                var cause = error.GetBaseException();
                failures.Add("colour fixture threw " + cause.GetType().Name + ": " + cause.Message);
                Debug.LogError("Halcyonic: character colour: where it threw: " + cause.StackTrace);
            }
            finally
            {
                RenderTexture.active = active; Glaze.SetPalette(palette); GlazeText.SetScale(scale); ViewField.Current = field;
                KeepFontAssetsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: character colour: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: character colour: every check passed.");
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static List<string> RenderCharacterColour(string folder)
        {
            var failures = new List<string>();
            // AppearanceControls reads these typed string preferences; only its colour save writes one.
            var preferences = new[] { "halcyonic.character-colours", "halcyonic.appearance" }
                .Select(key => (Key: key, Had: PlayerPrefs.HasKey(key), Value: PlayerPrefs.GetString(key, ""))).ToArray();
            var designWas = RoomDesign.Enabled; var kitWas = KitWorkspaceHost.Enabled;
            RoomDesign.Enabled = true; KitWorkspaceHost.Enabled = true;
            var root = new GameObject("Synthetic colour flow with real character");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var images = new List<Texture2D>();
            var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
            using var session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
            try
            {
                PlayerPrefs.DeleteKey("halcyonic.character-colours");
                var controlsType = typeof(WorkspaceDirector).Assembly.GetType("Halcyonic.XR.Workspace.AppearanceControls", true)!;
                var controls = root.AddComponent(controlsType);
                controlsType.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controls, null);
                var colours = (CharacterColourSettings)controlsType.GetProperty("CharacterColours")!.GetValue(controls)!;
                var keep = controlsType.GetMethod("KeepCharacterColours")!;
                Glaze.SetPalette(GlazePalette.Default);
                var eyes = new Vector3(0f, EyeHeight, 0f); var camera = MakeCamera(root.transform, eyes, texture);
                var prototype = Work.Running();
                var work = HalcyonicJson.Deserialize<WorkstreamView>(HalcyonicJson.Serialize(prototype.Workstream));
                var execution = HalcyonicJson.Deserialize<ExecutionView>(HalcyonicJson.Serialize(prototype.Execution));
                work.WorkstreamId = ResultsTask; work.ProjectId = ResultsProject; work.CurrentExecutionId = ResultsRun; work.ExecutionIds = new List<string> { ResultsRun };
                execution.ExecutionId = ResultsRun; execution.ProjectId = ResultsProject; execution.WorkstreamId = ResultsTask;
                // At rest, where a person opens Character Colour: the redesign offers the task's routes behind More only at rest, never
                // while the agent works (XR's c5802ab49), so the colour panel is reached as a person reaches it.
                work.Status = WorkstreamStatus.Completed; execution.Status = ExecutionStatus.Completed;
                var snapshot = new Snapshot { Journal = new JournalInfo { JournalId = ResultsJournal, Origin = JournalOrigin.Live }, Position = 5,
                    Projects = new List<ProjectView> { new ProjectView { ProjectId = ResultsProject, Name = "Synthetic colour project", CreatedAt = Time, UpdatedAt = Time } },
                    Workstreams = new List<WorkstreamView> { work }, Executions = new List<ExecutionView> { execution }, Runtimes = prototype.State.Runtimes.ToList() };
                var inbox = (ConcurrentQueue<object>)typeof(RealtimeSession).GetField("inbox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
                // Explicit editor-only synthetic transport seam. Never call session.Start or a live API.
                void Pump(params object[] messages) { foreach (var message in messages) inbox.Enqueue(message); session.Pump(); }
                Pump(new WelcomeMessage { Journal = snapshot.Journal, Head = snapshot.Position, ServerTime = Time }, new SnapshotMessage { Snapshot = snapshot }, new ConnectionStatus(ConnectionPhase.Live));
                if (!session.SnapshotCurrent) failures.Add("synthetic welcome/snapshot did not establish the actual watermark");
                RealtimeSession? shownSession = session;
                var source = ControlPlaneTarget.Local(new Uri("ws://127.0.0.1:9/realtime"), "render");
                var configured = source; var sourceKey = WorkspaceContinuity.ConnectionKey(source);
                var scope = new CharacterColourScope(sourceKey, ResultsJournal, ResultsTask);
                var targets = new List<CharacterTarget>(); var characters = new List<(CharacterView View, CharacterTarget Target)>();
                var presentation = new WorkspacePresentationState(); var writes = 0; var sends = 0; FileColumn? file = null;
                var commands = new CommandFactory(client);
                WorkspacePresentation Present() => WorkspacePresenter.Present(work, session.State, new ActivityLog(), session.Status.IsLive);
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    SpatialWorkspace = true, Presentation = presentation, Commands = commands,
                    CharacterColours = colours, CharacterSourceConnectionKey = () => sourceKey,
                    SaveCharacterColours = encoded => { writes++; return (bool)keep.Invoke(controls, new object[] { encoded })!; },
                    ConnectionTarget = () => configured, Session = () => shownSession, State = () => session.State,
                    Connected = () => shownSession?.Status.IsLive == true,
                    Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    Overview = () => WorkOverview.Of(session.State, new StageVisibility(), _ => true), Bar = place => TasksColumn.Bar(place, session.State),
                    Submit = (_, command) => { sends++; return Task.FromResult(new CommandAckMessage { CommandId = command.CommandId, Disposition = CommandAckDisposition.Accepted }); },
                    File = (host, _) => file = new FileColumn(host, Present, commands, () => null, _ => null, () => ""),
                    CharacterOf = id => presentation.AgentsVisible ? targets.FirstOrDefault(target => target.View.WorkstreamId == id) : null,
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, presentation.AgentsVisible ? targets : new List<CharacterTarget>(), null, false),
                });
                void Draw()
                {
                    for (var pass = 0; pass < 4; pass++)
                    {
                        director.UpdateForRender(); director.DrawNow(); director.Plane.Advance(MenuPlane.OpeningSeconds);
                        if (director.Plane.KitShows) { director.Plane.Kit.SettleForRender(); director.Plane.Kit.DrawnForRender(); }
                    }
                }
                // The kit's drawn control for an action: a bar button, else a row of the card that carries the press.
                KitPress? DrawnPress(string action, string? key)
                    => !director.Plane.KitShows ? null : director.Plane.Kit.Bar.Button(action, key)?.Press ?? director.Plane.Kit.Bar.Row(action, key);
                // The way into the panel from the frame of record, a page at a time as the file's own pager turns: only for the
                // one route the redesign does not draw yet (the lead's ruling B, 2026-11-16T03:03Z).
                bool OpenThroughRecord(string action)
                {
                    for (var page = 0; page < 8; page++)
                    {
                        var current = director.Plane.Shown.LastOrDefault(each => each.View.Frame is MenuFrame frame
                            && frame.Lines.Any(line => line.Action == action && line.Available));
                        if (current.View != null) return director.PressForRender(current.Kind, action, null, current.View.Frame, null);
                        if (file?.Frame is MenuFrame record && record.Lines.Any(line => line.Action == action && line.Available))
                            return director.PressForRender(MenuColumn.File, action, null, record, null);
                        var paged = director.Plane.Shown.LastOrDefault(each => each.View.Frame is MenuFrame frame
                            && (frame.Footer.All.Any(slot => slot.Prompt.Id == Footer.NextPage && slot.Prompt.Available)
                                || frame.Lines.Any(line => line.Action == Footer.NextPage && line.Available)));
                        if (paged.View != null && director.PressForRender(paged.Kind, Footer.NextPage, null, paged.View.Frame, null)) { Draw(); continue; }
                        if (file?.Frame is MenuFrame turn && (turn.Footer.All.Any(slot => slot.Prompt.Id == Footer.NextPage && slot.Prompt.Available)
                                || turn.Lines.Any(line => line.Action == Footer.NextPage && line.Available))
                            && director.PressForRender(MenuColumn.File, Footer.NextPage, null, turn, null)) { Draw(); continue; }
                        return false;
                    }
                    return false;
                }
                void Press(string action, string? key = null)
                {
                    Draw();
                    var drawn = DrawnPress(action, key);
                    // Further pages through the kit's own drawn pager, as a person turns them.
                    for (var page = 0; drawn == null && page < 8; page++)
                    {
                        var next = DrawnPress(Footer.NextPage, null) ?? director.Plane.Kit.Bar.BandNextButton?.Press;
                        if (!director.Plane.KitShows || next == null) break;
                        next.SettleForRender(); next.PressForRender(990); Draw();
                        drawn = DrawnPress(action, key);
                    }
                    // The task's routes stand behind the bar's More at rest (XR's c5802ab49, SCREENS's SHAPE 11): press it as a person
                    // does, then look for the route on the More page.
                    if (drawn == null && action == FileScreens.OpenCharacterColour && DrawnPress(RoomBar.More, null) is KitPress more)
                    {
                        more.SettleForRender(); more.PressForRender(990); Draw();
                        drawn = DrawnPress(action, key);
                        // The More page's further pages through its own drawn pager, as a person turns them (at Larger it holds fewer rows).
                        for (var page = 0; drawn == null && page < 4; page++)
                        {
                            var next = DrawnPress(Footer.NextPage, null) ?? director.Plane.Kit.Bar.BandNextButton?.Press;
                            if (next == null) break;
                            next.SettleForRender(); next.PressForRender(990); Draw();
                            drawn = DrawnPress(action, key);
                        }
                        Debug.Log("Halcyonic: character colour: pressed More; the More page " + (drawn != null ? "offers" : "does not offer") + " " + action);
                    }
                    if (drawn == null && action == FileScreens.OpenCharacterColour)
                    {
                        // RULING B: the one named gap. Under the redesign no drawn control opens the colour panel yet (the film batch's
                        // routes); the check says so, opens it through the frame of record, and holds the panel the kit draws to every
                        // check. Once a drawn control exists this branch is never taken, and the press above is the person's.
                        Debug.Log("Halcyonic: character colour: not reached here: no drawn control opens the colour panel under the redesign ("
                            + action + "); opened through the frame of record, every check run on the panel the kit draws");
                        if (!OpenThroughRecord(action)) failures.Add("neither a drawn control nor the frame of record opens " + action);
                        Draw();
                        return;
                    }
                    if (drawn == null)
                    {
                        // Say whether the frame of record offers it, so a control the kit does not draw is told from one not offered.
                        var offered = director.Plane.Shown.Any(each => each.View.Frame is MenuFrame frame
                            && (frame.Footer.All.Any(slot => slot.Prompt.Id == action && slot.Prompt.Available)
                                || frame.Lines.Any(line => line.Action == action && (key == null || line.Key == key) && line.Available)))
                            || file?.Frame is MenuFrame record && record.Lines.Any(line => line.Action == action && (key == null || line.Key == key) && line.Available);
                        var reader = director.Plane.KitShows ? director.Plane.Kit.Bar.Shown?.GetType().Name ?? "no bar" : "no kit";
                        failures.Add("the kit draws no colour control for " + action + (key == null ? "" : " " + key)
                            + " (the frame of record " + (offered ? "offers it" : "does not offer it") + "; the bar shows " + reader + ")");
                        return;
                    }
                    drawn.SettleForRender(); drawn.PressForRender(990);
                    Draw();
                }
                // Each shot is held to the redesign's frame checks, as the judge room's moments are (every word whole, inside the
                // Quest 3S field, at least 4.5:1), and fails where any older view draws: under ruling (a) colour is drawn wholly by
                // the kit, so the older plane's own geometry checks (SpatialState) have nothing of it to measure.
                Texture2D Shot(string step)
                {
                    Draw();
                    foreach (var button in root.GetComponentsInChildren<RoomButton>(false)) button.Settle();
                    foreach (var band in root.GetComponentsInChildren<RoomBand>(false)) band.Settle();
                    ForceMeshes(root);
                    var image = Render(camera, texture); images.Add(image);
                    File.WriteAllBytes(Path.Combine(folder, "colour-" + step.Replace(' ', '-') + ".png"), image.EncodeToPNG());
                    failures.AddRange(RoomFrameChecks(root, camera, image, eyes, director.Plane).Select(each => "colour " + step + ": " + each));
                    var shelf = characters.Count > 0 ? characters[0].View.transform.parent : null;
                    var older = root.GetComponentsInChildren<Renderer>(false)
                        .Where(renderer => renderer.enabled && (shelf == null || !renderer.transform.IsChildOf(shelf)))
                        .Select(renderer => OwnerOf(renderer.transform))
                        .Where(owner => owner != null && !owner.StartsWith("Room", StringComparison.Ordinal) && !owner.StartsWith("Character", StringComparison.Ordinal))
                        .Distinct().OrderBy(owner => owner, StringComparer.Ordinal).ToList();
                    if (older.Count > 0) failures.Add("colour " + step + ": the older view draws " + string.Join(", ", older));
                    Debug.Log("Halcyonic: colour: " + step + ": drawn by the kit" + (older.Count > 0 ? ", and by the older view: " + string.Join(", ", older) : " alone"));
                    return image;
                }
                // Fit the explicit optional body beside the selected task, not the wider project chooser.
                Draw(); director.OpenFile(ResultsTask); Draw();
                var beforePlacement = failures.Count;
                // The shelf is placed as a person places it (the lead's 2026-11-14T07:12Z ruling 1; JUDGE's readE5, XR's and AUDIT's
                // 07:32Z): under the redesign with the kit, so the plane's console and ClearsShelf are the product's keep-out at rest
                // (17.9 degrees either side at Larger), never the older view's widest file column (31.8) that no person meets. The
                // colour panel is drawn by the kit too (ruling (a)).
                (CharacterView View, CharacterTarget Target) agent;
                {
                    if (director.Plane.ConsoleExtentFrom(eyes) is ConsoleExtent keptClear)
                        Debug.Log("Halcyonic: colour: placing at text size " + GlazeText.Scale.ToString("0.##") + ", the search keeps clear of the console "
                            + GlazeChecks.Degrees(Mathf.DeltaAngle(director.Plane.Direction.Yaw, keptClear.LeftYaw)) + " to "
                            + GlazeChecks.Degrees(Mathf.DeltaAngle(director.Plane.Direction.Yaw, keptClear.RightYaw)) + " across from where the plane faces, foot "
                            + GlazeChecks.Degrees(keptClear.BottomElevation) + " up; clearance " + GlazeChecks.Degrees(WorkspacePlacement.ClearanceDegrees) + " degrees");
                    agent = ConfirmedDirectorAgent(root.transform, director, eyes, Present().Character, failures, "colour");
                }
                var placedExtent = GlazeChecks.Of("the colour shelf", eyes, agent.View.transform.parent.GetComponentsInChildren<Renderer>(false));
                Debug.Log("Halcyonic: colour: the shelf placed stands " + GlazeChecks.Degrees(placedExtent.Left) + " to " + GlazeChecks.Degrees(placedExtent.Right)
                    + " across, " + GlazeChecks.Degrees(placedExtent.Bottom) + " to " + GlazeChecks.Degrees(placedExtent.Top) + " up, inside the field");
                // The shared helper emits a diagnostic fallback after failure. It is never valid proof.
                if (failures.Count != beforePlacement) return failures;
                targets.Add(agent.Target); characters.Add(agent); presentation.SetAgentsEnabled(true);
                Draw(); file!.Reflow();
                var original = agent.View.BodyColourForRender; var clock = agent.View.ClockForRender;
                var shell = agent.View.Body.GetComponentsInChildren<Renderer>(true).Single(renderer => renderer.name == "Shell");
                var block = new MaterialPropertyBlock();
                void AssertBody(Vector4 expected, string step)
                {
                    shell.GetPropertyBlock(block);
                    if (agent.View.BodyColourForRender != expected || block.GetVector(Shader.PropertyToID("_BodyColor")) != expected)
                        failures.Add(step + ": real body/material did not match the expected applied or preview hue");
                    if (agent.View.ClockForRender != clock) failures.Add(step + ": colour controls advanced the actual animation clock");
                }
                Press(FileScreens.OpenCharacterColour); var before = Shot("choices");
                var choice = CharacterIdentity.Of(ResultsTask).Hue == 197.0 ? "violet" : "cyan";
                Press(CharacterColourColumn.Choose, choice); AssertBody(original, "choice");
                Press(CharacterColourColumn.Preview); var preview = Shot("preview");
                var previewColour = agent.View.BodyColourForRender;
                if (previewColour == original || writes != 0 || colours.Applied(scope) != "original") failures.Add("Preview failed to change only the actual unsaved body");
                AssertBody(previewColour, "preview");
                var center = camera.WorldToScreenPoint(agent.Target.BodyPosition);
                var edge = camera.WorldToScreenPoint(agent.Target.BodyPosition + camera.transform.up * CharacterView.BodyExtent * agent.Target.Scale);
                var radius = Mathf.CeilToInt(Vector2.Distance(center, edge));
                var rect = new RectInt(Mathf.Clamp((int)center.x - radius, 0, Size - 1), Mathf.Clamp((int)center.y - radius, 0, Size - 1), 2 * radius, 2 * radius);
                rect.width = Math.Min(rect.width, Size - rect.x); rect.height = Math.Min(rect.height, Size - rect.y);
                if (rect.width == 0 || rect.height == 0 || Compare(before, preview, rect).Changed == 0) failures.Add("real body preview produced no rendered pixel change in its measured body region");
                Press(CharacterColourColumn.Cancel); var cancelled = Shot("cancelled"); AssertBody(original, "Cancel");
                if (Compare(before, cancelled, rect).Changed != 0) failures.Add("Cancel did not restore the rendered body region within the existing pixel tolerance");
                if (writes != 0) failures.Add("Cancel persisted a preview");
                Press(CharacterColourColumn.Choose, choice); Press(CharacterColourColumn.Preview); Press(CharacterColourColumn.Apply);
                Shot("applied"); AssertBody(previewColour, "Apply");
                if (writes != 1 || CharacterColourSettings.Load(PlayerPrefs.GetString("halcyonic.character-colours", "")).Applied(scope) != choice)
                    failures.Add("Apply did not persist exactly the selected scoped palette choice through AppearanceControls");
                var alternate = choice == "cyan" ? "violet" : "cyan";
                void BeginAlternate() { Press(CharacterColourColumn.Choose, alternate); Press(CharacterColourColumn.Preview); }
                BeginAlternate(); configured = ControlPlaneTarget.Local(new Uri("ws://127.0.0.1:9/realtime"), "different-synthetic-credential");
                Draw(); Shot("configured source changed"); AssertBody(previewColour, "configured source loss");
                configured = source; director.OpenFile(ResultsTask); Draw(); file!.Reflow(); Press(FileScreens.OpenCharacterColour);
                BeginAlternate(); shownSession = null; Draw(); Shot("session lost"); AssertBody(previewColour, "session loss");
                shownSession = session; Draw(); director.OpenFile(ResultsTask); Draw(); file!.Reflow(); Press(FileScreens.OpenCharacterColour);
                BeginAlternate(); Pump(new ConnectionStatus(ConnectionPhase.Synchronizing)); Draw(); Shot("watermark lost"); AssertBody(previewColour, "watermark loss");
                Pump(new ConnectionStatus(ConnectionPhase.Live)); Draw(); director.OpenFile(ResultsTask); Draw(); file!.Reflow(); Press(FileScreens.OpenCharacterColour);
                BeginAlternate(); targets.Clear(); agent.View.gameObject.SetActive(false); Draw(); Shot("target lost"); AssertBody(previewColour, "target loss");
                agent.View.gameObject.SetActive(true); targets.Add(agent.Target); Draw(); director.OpenFile(ResultsTask); Draw(); file!.Reflow(); Press(FileScreens.OpenCharacterColour);
                BeginAlternate();
                // Non-ExecuteAlways behaviours do not receive play-mode destruction callbacks in this
                // edit-mode witness. Exercise the actual cleanup method explicitly before destruction;
                // this proves its real-body effect, not Unity's play-mode callback delivery.
                typeof(MenuDirector).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(director, null);
                AssertBody(previewColour, "production menu destruction cleanup");
                UnityEngine.Object.DestroyImmediate(director.gameObject); AssertBody(previewColour, "menu destruction");
                var survivingBody = Render(camera, texture); images.Add(survivingBody);
                File.WriteAllBytes(Path.Combine(folder, "menu-destroyed-body.png"), survivingBody.EncodeToPNG());
                if (writes != 1 || sends != 0) failures.Add("lost authority saved a preview or any colour action submitted a runtime command");
            }
            finally
            {
                try
                {
                    foreach (var remaining in root.GetComponentsInChildren<MenuDirector>(true))
                        typeof(MenuDirector).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(remaining, null);
                    UnityEngine.Object.DestroyImmediate(root);
                    foreach (var image in images) UnityEngine.Object.DestroyImmediate(image);
                    texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
                }
                finally
                {
                    RoomDesign.Enabled = designWas; KitWorkspaceHost.Enabled = kitWas;
                    foreach (var preference in preferences)
                        if (preference.Had) PlayerPrefs.SetString(preference.Key, preference.Value); else PlayerPrefs.DeleteKey(preference.Key);
                    PlayerPrefs.Save();
                }
            }
            return failures;
        }
    }
}

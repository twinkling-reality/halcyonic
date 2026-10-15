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
                    return RenderCharacterColour(folder);
                }));
            }
            catch (Exception error) { failures.Add("colour fixture threw " + error.GetBaseException().GetType().Name); }
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
                void Draw() { for (var pass = 0; pass < 4; pass++) { director.UpdateForRender(); director.DrawNow(); director.Plane.Advance(MenuPlane.OpeningSeconds); } }
                void Press(string action, string? key = null)
                {
                    Draw();
                    var shown = director.Plane.Shown.LastOrDefault(each => each.View.Frame is MenuFrame frame
                        && (frame.Footer.All.Any(slot => slot.Prompt.Id == action && slot.Prompt.Available)
                            || frame.Lines.Any(line => line.Action == action && line.Key == key && line.Available)));
                    if (shown.View == null || !director.PressForRender(shown.Kind, action, key, shown.View.Frame, null))
                        failures.Add("no current drawn colour control accepted " + action);
                    Draw();
                }
                Texture2D Shot(string step)
                {
                    Draw(); failures.AddRange(PlaneState("colour " + step, folder, camera, texture, director.Plane, characters, eyes, null, lightLine: false));
                    var image = Render(camera, texture); images.Add(image); return image;
                }
                Draw();
                var agent = ConfirmedDirectorAgent(root.transform, director, eyes, Present().Character, failures, "colour");
                targets.Add(agent.Target); characters.Add(agent); presentation.SetAgentsEnabled(true);
                director.OpenFile(ResultsTask); Draw(); file!.Reflow();
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
                BeginAlternate(); UnityEngine.Object.DestroyImmediate(director.gameObject); AssertBody(previewColour, "menu destruction");
                var survivingBody = Render(camera, texture); images.Add(survivingBody);
                File.WriteAllBytes(Path.Combine(folder, "menu-destroyed-body.png"), survivingBody.EncodeToPNG());
                if (writes != 1 || sends != 0) failures.Add("lost authority saved a preview or any colour action submitted a runtime command");
            }
            finally
            {
                try
                {
                    UnityEngine.Object.DestroyImmediate(root);
                    foreach (var image in images) UnityEngine.Object.DestroyImmediate(image);
                    texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
                }
                finally
                {
                    foreach (var preference in preferences)
                        if (preference.Had) PlayerPrefs.SetString(preference.Key, preference.Value); else PlayerPrefs.DeleteKey(preference.Key);
                    PlayerPrefs.Save();
                }
            }
            return failures;
        }
    }
}

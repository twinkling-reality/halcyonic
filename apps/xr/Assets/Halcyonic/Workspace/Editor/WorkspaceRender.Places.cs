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
        /// <summary>
        /// The places a person reaches from the first screen, walked as the headset runs (the spatial workspace, the redesign
        /// on), each shot and its drawer counted (the census of 2026-11-12): a new person's Folders card; Projects; a project's
        /// tasks; the menu from the Menu button; Compact and back; the Menu button again on the menu, back to Projects. A
        /// moment drawn by an older view, or a press with no drawn control, fails. Graphics needed:
        /// -executeMethod Halcyonic.XR.Workspace.Editor.WorkspaceRender.PlacesCheck; exits 1 when a check fails.
        /// </summary>
        public static void PlacesCheck()
        {
            var previousField = ViewField.Current;
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "PlacesRenders", variant));
                Directory.CreateDirectory(folder);
                ViewField.Current = FieldChecks.Quest3S;
                var found = new List<string>();
                try
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    found.AddRange(RenderPlaces(folder, withProject: false));
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    found.AddRange(RenderPlaces(folder, withProject: true));
                }
                catch (Exception error) { found.Add("places threw " + error.GetBaseException().Message); }
                finally { ViewField.Current = previousField; KeepFontAssetsAsCommitted(); }
                foreach (var failure in found) Debug.LogError("Halcyonic: places: " + failure);
                if (found.Count == 0) Debug.Log("Halcyonic: places: every check passed; " + folder);
                return found;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static IEnumerable<string> RenderPlaces(string folder, bool withProject)
        {
            var failures = new List<string>();
            var root = new GameObject("Places a person reaches");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var client = new ClientInfo { Name = "render", Version = "render", DeviceLabel = "render" };
            using var session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
            try
            {
                var work = Work.Running();
                var runtime = RenderRuntime("review-render", ModelChoice.None); runtime.UsesProjectLocation = false;
                var journal = new JournalInfo { JournalId = "render-places", Origin = JournalOrigin.Live };
                var snapshot = withProject
                    ? new Snapshot { Journal = journal, Position = 1, Projects = work.State.Projects.Values.ToList(), Workstreams = new List<WorkstreamView> { work.Workstream },
                        Executions = new List<ExecutionView> { work.Execution }, Runtimes = new List<RuntimeDescriptor> { runtime } }
                    : new Snapshot { Journal = journal, Position = 1, Projects = new List<ProjectView>(), Workstreams = new List<WorkstreamView>(),
                        Executions = new List<ExecutionView>(), Runtimes = new List<RuntimeDescriptor> { runtime } };
                var inbox = (ConcurrentQueue<object>)typeof(RealtimeSession).GetField("inbox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
                inbox.Enqueue(new WelcomeMessage { Journal = journal, Head = 1, ServerTime = Time });
                inbox.Enqueue(new SnapshotMessage { Snapshot = snapshot }); inbox.Enqueue(new ConnectionStatus(ConnectionPhase.Live)); session.Pump();
                if (!session.SnapshotCurrent) throw new InvalidOperationException("synthetic source is not current");
                var eyes = new Vector3(0f, EyeHeight, 0f); var camera = MakeCamera(root.transform, eyes, texture);
                var place = new LocationRoot
                {
                    Path = "/Users/person/Projects", Name = "Projects", Status = LocationRootStatus.Available, FoldersTruncated = false, Repository = false,
                    ChangedAt = "2026-09-01T00:00:00.000Z", UsedBy = new List<string>(),
                    Folders = new[] { "kite-run", "tail" }.Select(name => new LocationFolder { Name = name, Path = "/Users/person/Projects/" + name, Repository = true,
                        ChangedAt = "2026-11-01T00:00:00.000Z", UsedBy = new List<string>() }).ToList(),
                };
                var presentation = new WorkspacePresentationState();
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup {
                    SpatialWorkspace = true, InitialPlacementReady = () => true, Commands = new CommandFactory(client),
                    Presentation = presentation, Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    ConnectionTarget = () => ControlPlaneTarget.Local(new Uri("ws://127.0.0.1:9/realtime"), "render"),
                    State = () => session.State, Session = () => session, Connected = () => true,
                    Overview = () => WorkOverview.Of(session.State, new StageVisibility(), _ => true), Bar = where => TasksColumn.Bar(where, session.State),
                    ReadLocations = _ => Task.FromResult(new LocationsResponse { Roots = new List<LocationRoot> { place } }),
                    KeyboardOffered = () => true, Keyboard = (_, _, _, _) => { },
                    Submit = (_, _) => throw new InvalidOperationException("The places walk sends nothing."),
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, Array.Empty<CharacterTarget>(), null, false),
                });
                var plane = director.Plane;
                // As the judges' room walk draws: each turn the room host settled and told it was drawn, so a press counts on the frame drawn.
                void Draw()
                {
                    session.Pump();
                    for (var tick = 0; tick < 4; tick++)
                    {
                        director.UpdateForRender(); director.DrawNow(); plane.Advance(MenuPlane.OpeningSeconds);
                        if (plane.KitShows) { plane.Kit.SettleForRender(); plane.Kit.DrawnForRender(); }
                    }
                }
                void Shot(string name, bool drawn = true, bool clear = false)
                {
                    Draw();
                    // As the judges' room walk shoots: every redesign part taken to where it arrives, then the redesign's own frame
                    // checks (every word whole, inside the field, readable), never the older chooser's.
                    foreach (var button in root.GetComponentsInChildren<RoomButton>(false)) button.Settle();
                    foreach (var band in root.GetComponentsInChildren<RoomBand>(false)) band.Settle();
                    ForceMeshes(root);
                    camera.transform.rotation = PlaneLayout.Facing(plane.Direction);
                    var image = Render(camera, texture);
                    File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                    if (drawn) failures.AddRange(RoomFrameChecks(root, camera, image, eyes, plane).Select(each => name + ": " + each));
                    UnityEngine.Object.DestroyImmediate(image);
                    var redesign = new SortedSet<string>(); var older = new SortedSet<string>();
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>(false))
                    {
                        if (!renderer.enabled || OwnerOf(renderer.transform) is not string owner) continue;
                        if (owner.StartsWith("Room", StringComparison.Ordinal)) redesign.Add(owner);
                        else if (!owner.StartsWith("Character", StringComparison.Ordinal)) older.Add(owner);
                    }
                    Debug.Log("Halcyonic: places: " + name + ": place " + director.Navigator.Place + (plane.KitShows ? ", room host" : "") + ": redesign ["
                        + string.Join(", ", redesign) + "], older [" + string.Join(", ", older) + "]");
                    if (drawn && (!plane.KitShows || redesign.Count == 0)) failures.Add(name + ": the redesign draws nothing");
                    if (drawn && older.Count > 0) failures.Add(name + ": older parts draw: " + string.Join(", ", older));
                    // Compact (A15): only the characters and their name tags stand.
                    if (clear && redesign.Count + older.Count > 0) failures.Add(name + ": the room is not clear: " + string.Join(", ", redesign.Concat(older)));
                }
                // As a hand presses: the control drawn and stood a moment first (the judges' walk's 0.45 s), then pressed.
                void Settle() { Draw(); System.Threading.Thread.Sleep(TimeSpan.FromSeconds(0.45)); Draw(); }
                bool PressButton(string action, string? key = null)
                {
                    Settle();
                    if (!plane.KitShows || plane.Kit.Bar.Button(action, key) is not RoomButton button) { failures.Add("no drawn control for " + action + (key == null ? "" : "/" + key)); return false; }
                    button.Press.SettleForRender();
                    button.Press.PressForRender(990);
                    Draw();
                    return true;
                }
                bool PressRow(int index)
                {
                    Settle();
                    var rows = plane.KitShows && plane.Kit.Bar.Card is RoomCardView card ? card.RowTargets.ToList() : new List<(KitPress Press, KitControl Control)>();
                    if (index >= rows.Count) { failures.Add("no drawn row " + index + " on the card (" + rows.Count + " drawn)"); return false; }
                    rows[index].Press.SettleForRender();
                    rows[index].Press.PressForRender(989);
                    Draw();
                    Debug.Log("Halcyonic: places: pressed row " + rows[index].Control.Action + "/" + rows[index].Control.Key + ", now place " + director.Navigator.Place);
                    return true;
                }

                if (!withProject)
                {
                    // A new person's first page: the Folders card.
                    Shot("01-folders");
                    return failures;
                }
                Shot("02-projects");
                if (PressRow(0)) Shot("03-project-tasks");
                director.MenuFromAnywhere();
                Shot("04-menu");
                if (director.Navigator.Place != MenuPlace.Usage) failures.Add("the Menu button opened " + director.Navigator.Place + ", not the menu");
                if (PressButton(UsageColumn.Compact))
                {
                    Debug.Log("Halcyonic: places: Compact pressed, minimized " + presentation.Minimized + ", place " + director.Navigator.Place);
                    Shot("05-compact", drawn: false, clear: true);
                    if (!presentation.Minimized) failures.Add("Compact did not clear the room");
                }
                director.MenuFromAnywhere();
                Shot("06-menu-from-compact");
                if (presentation.Minimized) failures.Add("the Menu button did not bring the workspace back");
                director.MenuFromAnywhere();
                Shot("07-projects-again");
                if (director.Navigator.Place != MenuPlace.Projects) failures.Add("the Menu button on the menu went to " + director.Navigator.Place + ", not Projects");
                if (PressButton(MenuFrame.ChooseSection, nameof(MenuPlace.Usage))) Shot("08-usage-from-projects");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
            return failures;
        }
    }
}

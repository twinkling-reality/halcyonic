#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>
        /// ADR 0029's empty-computer entry through the actual menu director: Projects and full normal
        /// navigation without characters, global Minimize and its visible restore target, then explicit
        /// New project. Every step retains the normal plane geometry, text and provenance checks.
        /// The reusable first-question wizard has separate model tests; it is not automatic entry here.
        /// </summary>
        private static IEnumerable<string> RenderFirstVisit(string name, string folder, float radius, float? surfaceDrop)
        {
            var failures = new List<string>();
            var root = new GameObject("First visit render " + name);
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            RealtimeSession? session = null;
            try
            {
                // Explicitly synthetic empty live-origin state; fixture runtime metadata never starts work.
                var asset = Resources.Load<TextAsset>("HalcyonicDemonstration");
                var recording = DemonstrationRecording.Parse(asset.text);
                Resources.UnloadAsset(asset);
                var state = new ClientProjection();
                state.ApplyWelcome(recording.Welcome);
                state.ApplySnapshot(new Snapshot { Journal = new JournalInfo { JournalId = "0192f1a0-0000-7000-8000-000000000001", Origin = JournalOrigin.Live }, Position = 1, Runtimes = recording.Snapshot.Snapshot.Runtimes }, new StateChanges());

                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                var characters = new List<(CharacterView View, CharacterTarget Target)>();
                var surface = surfaceDrop.HasValue ? EyeHeight - surfaceDrop.Value : (float?)null;
                var comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard };
                var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
                var commands = new CommandFactory(client);
                session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
                var place = new LocationRoot
                {
                    Path = "/Users/person/Projects", Name = "Projects", Status = LocationRootStatus.Available, FoldersTruncated = false,
                    Folders = new List<LocationFolder>
                    {
                        new LocationFolder { Name = "race-times", Path = "/Users/person/Projects/race-times", Repository = true, ChangedAt = "2026-10-05T09:00:00.000Z" },
                        new LocationFolder { Name = "recipes", Path = "/Users/person/Projects/recipes", Repository = false, ChangedAt = "2026-09-28T09:00:00.000Z" },
                    },
                };
                NewProjectFlow? flow = null;
                var presentation = new WorkspacePresentationState();
                var sends = 0;
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    Commands = commands,
                    Presentation = presentation,
                    Overview = () => WorkOverview.Of(state, new StageVisibility(), _ => true),
                    ReadLocations = _ => System.Threading.Tasks.Task.FromResult(new LocationsResponse { Roots = new List<LocationRoot> { place } }),
                    Comfort = comfort,
                    // Your space as a paired computer gives it, in a development build.
                    Space = () => SpaceSettings.Of(() => new SpaceNow(RoomStatus.Initial, RoomOffer.None, StageArrangement.InFront,
                        new PairingNow("192.168.1.23:47801", PairingStep.Idle, true)), _ => { }, _ => { }, pairing: true),
                    MakeNewProject = host => flow = new NewProjectFlow(host, commands, new KeptInMemory()),
                    Connected = () => true,
                    Session = () => session,
                    Submit = (_, _) => { sends++; return null; },
                    State = () => state,
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, Array.Empty<CharacterTarget>(), surface, false),
                    Bar = chosen => TasksColumn.Bar(chosen, state),
                    // Voice is offered to the explicit creation flow; this witness never starts a microphone.
                    Voice = root.AddComponent<HoldToTalk>(),
                });
                var navigator = director.Navigator;
                var plane = director.Plane;
                MenuFrame? Menu() => plane.Showing(MenuColumn.Menu);
                void Press(string action, string? key = null)
                {
                    director.DrawNow();
                    Thread.Sleep(TimeSpan.FromSeconds(0.45));
                    var shown = plane.Shown.LastOrDefault(each => each.View.Frame is MenuFrame frame &&
                        (frame.Footer.All.Any(prompt => prompt.Prompt.Id == action && prompt.Prompt.Available)
                            || frame.Lines.Any(line => line.Action == action && line.Key == key && line.Pressable)
                            || action == MenuFrame.ChooseSection && frame.Sections.Any(section => section.Key == key)));
                    if (shown.View == null || !director.PressForRender(shown.Kind, action, key, shown.View.Frame, null))
                        failures.Add(name + ": no current drawn target accepted " + action + ".");
                    director.UpdateForRender();
                    director.DrawNow();
                }
                void PointerPress(GlazeButton target, int pointer)
                {
                    target.SettleForRender();
                    target.Target.PointerForRender(pointer, Oculus.Interaction.PointerEventType.Select, target.transform.position);
                    target.Target.PointerForRender(pointer, Oculus.Interaction.PointerEventType.Unselect, target.transform.position);
                }
                void Shot(string step, MenuPlace? place = null, string? subject = null)
                {
                    director.UpdateForRender();
                    director.DrawNow();
                    var what = name + " first visit " + step;
                    if (place.HasValue)
                    {
                        var shown = Menu();
                        if (!navigator.IsOpen || navigator.IsCompact || navigator.Asking || navigator.Place != place)
                            failures.Add(what + ": normal " + place + " navigation is not open.");
                        if (subject != null && shown?.Subject != subject) failures.Add(what + ": wrong visible subject for " + place + ".");
                        if (shown == null || !shown.Sections.Select(section => section.Key).SequenceEqual(MenuBar.Places.Select(item => item.ToString()))
                            || shown.Sections.Count(section => section.Chosen) != 1 || shown.Sections.SingleOrDefault(section => section.Chosen)?.Key != place.ToString())
                            failures.Add(what + ": the complete normal navigation row or its current selection is missing.");
                        if (!plane.Chrome.Visible || plane.ConnectionState != WorkspaceText.Connected)
                            failures.Add(what + ": the empty connected workspace lost its current header.");
                    }
                    if (presentation.AgentsVisible || root.GetComponentsInChildren<CharacterTarget>(true).Length != 0
                        || root.GetComponentsInChildren<StageBanner>(true).Length != 0)
                        failures.Add(what + ": the first visit requires characters or a stage banner.");
                    failures.AddRange(PlaneState(what, folder, camera, texture, plane, characters, eyes, null, lightLine: false));
                }
                void MinimizeAndRestore(string step, int pointer)
                {
                    var kept = navigator.Shown;
                    var place = navigator.Place;
                    var previous = Menu();
                    PointerPress(plane.Chrome.Targets.Single(button => button.Label.text == WorkspaceText.Minimize), pointer);
                    Shot(step + " minimized");
                    if (!presentation.Minimized || plane.Shown.Count != 0 || plane.Chrome.Visible || plane.Bar is not MenuBarView dock
                        || !dock.Target.gameObject.activeInHierarchy || dock.Target.Label.text != WorkspaceText.OpenWorkspace
                        || dock.Controls.Move.gameObject.activeSelf)
                        failures.Add(name + ": global Minimize did not leave only its visible restore dock.");
                    if (previous != null && director.PressForRender(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Tasks), previous, null))
                        failures.Add(name + ": the minimized menu retained stale navigation authority.");
                    if (plane.Bar is MenuBarView restore) PointerPress(restore.Target, pointer + 1);
                    Shot(step + " restored", place, place == MenuPlace.Settings ? SettingsText.Subject : ProjectsText.Subject);
                    if (presentation.Minimized || navigator.Shown != kept)
                        failures.Add(name + ": global restore replaced the retained navigation column.");
                }

                Shot("1 projects", MenuPlace.Projects, ProjectsText.Subject);
                if (director.BeforeFirstTask) failures.Add(name + ": empty startup restricted normal navigation to the old first question.");
                for (var tick = 0; tick < 20 && Menu()?.Lines.All(line => line.Action != ProjectsScreens.ChooseFolder) != false; tick++)
                {
                    Thread.Sleep(50);
                    director.UpdateForRender();
                    director.DrawNow();
                }
                if (Menu()?.Lines.Any(line => line.Action == ProjectsScreens.ChooseFolder) != true)
                    failures.Add(name + ": Projects lists none of the explicitly offered computer folders.");
                Shot("2 offered folders", MenuPlace.Projects, ProjectsText.Subject);
                Press(MenuFrame.ChooseSection, nameof(MenuPlace.Tasks));
                Shot("3 empty tasks", MenuPlace.Tasks, "Tasks");
                Press(MenuFrame.ChooseSection, nameof(MenuPlace.Usage));
                Shot("4 usage", MenuPlace.Usage, UsageText.Subject);
                Press(MenuFrame.ChooseSection, nameof(MenuPlace.Settings));
                Shot("5 settings", MenuPlace.Settings, SettingsText.Subject);
                MinimizeAndRestore("6 settings", 910);
                PointerPress(plane.Chrome.Targets.Single(button => button.Label.text == "Projects"), 912);
                Shot("7 projects", MenuPlace.Projects, ProjectsText.Subject);
                Press(ProjectsScreens.NewProject);
                Shot("8 explicit new project");
                var creation = plane.Showing(MenuColumn.File);
                if (flow == null || navigator.Beside != flow || flow.Step != NewProjectStep.YourIdea || creation?.Subject != EntryText.CreateProject)
                    failures.Add(name + ": explicit Projects New project did not open the creation wizard.");
                if (creation?.Footer[PromptSlot.Secondary]?.Words != VoiceText.HoldToTalk)
                    failures.Add(name + ": the explicit creation flow lost its offered held input.");
                if (sends != 0) failures.Add(name + ": navigation, minimize, restore or opening creation submitted a command.");
            }
            finally
            {
                session?.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }
    }
}

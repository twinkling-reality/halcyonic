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
        /// The first visit to a computer with no task yet (ADR 0026), from the eyes, drawn by the menu's own
        /// director on an empty stage: the first question as it opens by itself, A project on your computer
        /// chosen, Projects in its place, Settings chosen from its row, the closed bar, and New project in
        /// the question's place. It fails if the menu doesn't open on the question, if the row of places
        /// holds anything but Settings, unlit but where Settings is chosen, in its slot at the right end, if
        /// a chosen row doesn't set the main action, if Hold to talk isn't beside Start a project alone, if
        /// the banner doesn't step aside while a column shows and come back with the bar, if the closed bar
        /// doesn't say nothing is running yet, or if any of it fails the plane's own checks.
        /// </summary>
        private static IEnumerable<string> RenderFirstVisit(string name, string folder, float radius, float? surfaceDrop)
        {
            var failures = new List<string>();
            var root = new GameObject("First visit render " + name);
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                // A live computer with no project and no task, its journal the recording's, as any journal would be.
                var asset = Resources.Load<TextAsset>("HalcyonicDemonstration");
                var recording = DemonstrationRecording.Parse(asset.text);
                Resources.UnloadAsset(asset);
                var state = new ClientProjection();
                state.ApplyWelcome(recording.Welcome);
                state.ApplySnapshot(new Snapshot { Journal = recording.Snapshot.Snapshot.Journal, Position = 1, Runtimes = recording.Snapshot.Snapshot.Runtimes }, new StateChanges());

                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                var characters = new List<(CharacterView View, CharacterTarget Target)>();
                var surface = surfaceDrop.HasValue ? EyeHeight - surfaceDrop.Value : (float?)null;
                var comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard };
                var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
                var commands = new CommandFactory(client);
                var session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
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
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    Commands = commands,
                    Overview = () => WorkOverview.Of(state, new StageVisibility(), _ => true),
                    ReadLocations = _ => System.Threading.Tasks.Task.FromResult(new LocationsResponse { Roots = new List<LocationRoot> { place } }),
                    Comfort = comfort,
                    // Your space as a paired computer gives it, in a development build.
                    Space = () => SpaceSettings.Of(() => new SpaceNow(RoomStatus.Initial, RoomOffer.None, StageArrangement.InFront,
                        new PairingNow("192.168.1.23:47801", PairingStep.Idle, true)), _ => { }, _ => { }, pairing: true),
                    MakeNewProject = host => flow = new NewProjectFlow(host, commands, new KeptInMemory()),
                    Connected = () => true,
                    Session = () => session,
                    Submit = (_, _) => null,
                    State = () => state,
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, Array.Empty<CharacterTarget>(), surface, false),
                    Bar = chosen => TasksColumn.Bar(chosen, state),
                    // Voice is offered in the editor, so the question carries Hold to talk.
                    Voice = root.AddComponent<HoldToTalk>(),
                });
                var navigator = director.Navigator;
                var plane = director.Plane;
                bool Press(string action, string? key = null)
                {
                    director.DrawNow();
                    // A row or prompt takes a press only once it has stood a moment.
                    Thread.Sleep(TimeSpan.FromSeconds(0.45));
                    var taken = navigator.Act(MenuColumn.Menu, action, key, plane.Showing(MenuColumn.Menu), null);
                    navigator.Tick();
                    director.DrawNow();
                    return taken;
                }
                MenuFrame? Menu() => navigator.Frames(TasksColumn.Bar(navigator.Place, state)).Menu;

                // The first visit, as the workspace's director takes it once the computer's state is known with nothing open.
                var visit = new FirstVisit(visited: false);
                var asks = visit.Asks(live: true, demonstration: false, state.Journal?.JournalId, state.Workstreams.Count > 0);
                if (asks != true) failures.Add(name + ": a computer with no task is not asked the first question.");
                director.BeforeFirstTask = asks == true;
                if (visit.Due(demonstration: false, somethingOpen: false, asks)) director.Open();
                else failures.Add(name + ": the first visit does not open the menu by itself.");
                director.DrawNow();

                void Shot(string step, string? subject, bool settingsLit = false)
                {
                    var what = name + " first visit " + step;
                    var shown = Menu();
                    if (subject != null)
                    {
                        if (shown?.Subject != subject) failures.Add(what + ": the menu shows \"" + shown?.Subject + "\", not \"" + subject + "\".");
                        var row = shown?.Sections.Select(section => (section.Words, section.Chosen)).ToList();
                        if (row == null || row.Count != 1 || row[0] != ("Settings", settingsLit) || shown!.SectionSlots != MenuBar.Places.Count)
                        {
                            failures.Add(what + ": the row of places is not Settings alone " + (settingsLit ? "lit" : "unlit") + " in its slot at the right end.");
                        }
                    }
                    // Where the stage would stand its banner: aside while a column shows, back with the bar alone.
                    var stand = BannerPlace.Of(demonstration: false, AmbientCover.PanelShowing, AmbientCover.PeekShowing);
                    var columnShows = navigator.IsOpen || navigator.Beside != null;
                    if (stand != (columnShows ? BannerStand.Hidden : BannerStand.InPlace))
                    {
                        failures.Add(what + ": the banner " + (columnShows ? "stays under the labels, where the plate stands" : "is missing beside the closed bar") + ".");
                    }
                    failures.AddRange(PlaneState(what, folder, camera, texture, plane, characters, eyes, null, lightLine: false));
                }

                // 1. The question, Something new chosen: Start a project with Hold to talk beside it.
                if (!navigator.Asking) failures.Add(name + ": the first visit opens on " + navigator.Place + ", not the first question.");
                var question = Menu();
                if (question?.Footer[PromptSlot.FarRight]?.Words != FirstQuestionText.StartAProject || question.Footer[PromptSlot.Secondary]?.Words != VoiceText.HoldToTalk)
                {
                    failures.Add(name + ": with Something new chosen, the footer is not Hold to talk beside Start a project.");
                }
                Shot("1 question", FirstQuestionText.Subject);

                // 2. A project on your computer chosen: Show my projects, and Hold to talk gone.
                Press(FirstQuestionScreens.Choose, nameof(FirstAnswer.OnYourComputer));
                question = Menu();
                if (question?.Footer[PromptSlot.FarRight]?.Words != FirstQuestionText.ShowMyProjects || question.Footer[PromptSlot.Secondary] != null)
                {
                    failures.Add(name + ": with A project on your computer chosen, the footer is not Show my projects alone.");
                }
                Shot("2 your computer", FirstQuestionText.Subject);

                // 3. Projects in the question's place, its folders listed, nothing lit.
                Press(FirstQuestionScreens.ShowProjects);
                for (var tick = 0; tick < 20 && Menu()?.Lines.All(line => line.Action != ProjectsScreens.ChooseFolder) != false; tick++)
                {
                    Thread.Sleep(50);
                    navigator.Tick();
                    director.DrawNow();
                }
                if (navigator.Asking || navigator.Place != MenuPlace.Projects) failures.Add(name + ": Show my projects did not open Projects in the question's place.");
                if (Menu()?.Lines.Any(line => line.Action == ProjectsScreens.ChooseFolder) != true) failures.Add(name + ": Projects lists none of the computer's folders.");
                Shot("3 projects", ProjectsText.Subject);

                // 4. Settings, chosen from its row, lit; its Close folds to the bar.
                Press(MenuFrame.ChooseSection, nameof(MenuPlace.Settings));
                if (navigator.Place != MenuPlace.Settings) failures.Add(name + ": choosing Settings from the row left the menu on " + navigator.Place + ".");
                Shot("4 settings", Menu()?.Subject, settingsLit: true);
                Press(Footer.Close);

                // 5. The closed bar: nothing is running yet.
                if (navigator.IsOpen || plane.Bar == null) failures.Add(name + ": Settings' Close did not fold the menu to its bar.");
                var line = TasksColumn.Bar(navigator.Place, state).ClosedLine;
                if (line != TasksText.NothingRunning) failures.Add(name + ": the closed bar says \"" + line + "\", not that nothing is running yet.");
                Shot("5 bar", null);

                // 6. The bar's Open asks again, and Start a project opens New project in the question's place.
                director.Open();
                director.DrawNow();
                if (!navigator.Asking) failures.Add(name + ": with no task, the bar's Open does not bring back the question.");
                Press(FirstQuestionScreens.StartProject);
                if (navigator.IsOpen || flow == null || navigator.Beside != flow) failures.Add(name + ": Start a project did not open New project alone, in the question's place.");
                Shot("6 new project", null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }
    }
}

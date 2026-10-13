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
        /// ADR 0029's recorded entry opens the complete Projects workspace without a character arc
        /// or a separately fabricated banner. Recorded provenance, waiting navigation, task selection
        /// and whole-workspace Minimize/restore use the production director and normal geometry checks.
        /// </summary>
        private static IEnumerable<string> RenderDemoWelcome(string name, string folder, float radius, float? surfaceDrop)
        {
            var failures = new List<string>();
            var root = new GameObject("Demo welcome render " + name);
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            RealtimeSession? session = null;
            try
            {
                var asset = Resources.Load<TextAsset>("HalcyonicDemonstration");
                var recording = DemonstrationRecording.Parse(asset.text);
                Resources.UnloadAsset(asset);
                var (state, activity) = RecordedAt(recording, Array.Empty<int>());
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                var characters = new List<(CharacterView View, CharacterTarget Target)>();
                var surface = surfaceDrop.HasValue ? EyeHeight - surfaceDrop.Value : (float?)null;
                var comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard };
                var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
                var commands = new CommandFactory(client);
                session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
                var presentation = new WorkspacePresentationState();
                var sends = 0;
                FileColumn? selected = null;
                NewProjectFlow? flow = null;
                WorkspacePresentation? Present(string task) => state.Workstreams.TryGetValue(task, out var work)
                    ? WorkspacePresenter.Present(work, state, activity, true) : null;
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    Commands = commands,
                    Presentation = presentation,
                    Overview = () => WorkOverview.Of(state, new StageVisibility(), _ => true),
                    Comfort = comfort,
                    RecordedUsage = at => recording.UsageLimitsAt(at),
                    Space = () => SpaceSettings.Of(() => new SpaceNow(RoomStatus.Initial, RoomOffer.None, StageArrangement.InFront, null), _ => { }, _ => { }),
                    File = (host, task) => selected = new FileColumn(host, () => Present(task), commands, () => null, _ => null, () => ""),
                    Session = () => session,
                    MakeNewProject = host => flow = new NewProjectFlow(host, commands, new KeptInMemory()),
                    Submit = (_, _) => { sends++; return null; },
                    Connected = () => true,
                    Demonstration = () => true,
                    State = () => state,
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, Array.Empty<CharacterTarget>(), surface, false),
                    Bar = place => TasksColumn.Bar(place, state),
                    SomethingWaits = () => state.Workstreams.Values.Any(task => CharacterLineup.TierOf(task) == LineupTier.NeedsYou),
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
                        failures.Add(name + ": no current recorded target accepted " + action + ".");
                    director.UpdateForRender();
                    director.DrawNow();
                }
                void PointerPress(GlazeButton target, int pointer)
                {
                    target.SettleForRender();
                    target.Target.PointerForRender(pointer, Oculus.Interaction.PointerEventType.Select, target.transform.position);
                    target.Target.PointerForRender(pointer, Oculus.Interaction.PointerEventType.Unselect, target.transform.position);
                }
                void Shot(string step, MenuPlace? place = null)
                {
                    director.UpdateForRender();
                    director.DrawNow();
                    var what = name + " demo welcome " + step;
                    if (place.HasValue)
                    {
                        var shown = Menu();
                        if (!navigator.IsOpen || navigator.IsCompact || navigator.Asking || navigator.Place != place || shown == null
                            || !shown.Sections.Select(section => section.Key).SequenceEqual(MenuBar.Places.Select(item => item.ToString()))
                            || shown.Sections.Count(section => section.Chosen) != 1 || shown.Sections.SingleOrDefault(section => section.Chosen)?.Key != place.ToString())
                            failures.Add(what + ": recorded entry lost its complete current navigation.");
                        if (place == MenuPlace.Projects && shown?.Subject != ProjectsText.Subject)
                            failures.Add(what + ": recorded entry did not show Projects.");
                    }
                    if (presentation.AgentsVisible || root.GetComponentsInChildren<CharacterTarget>(true).Length != 0
                        || root.GetComponentsInChildren<StageBanner>(true).Length != 0)
                        failures.Add(what + ": recorded entry requires optional characters or a stage banner.");
                    if (state.Journal?.Origin != JournalOrigin.Fixture || plane.ConnectionState != WorkspaceText.Demo)
                        failures.Add(what + ": recorded work was relabeled as a live session.");
                    if (!presentation.Minimized && (!plane.Chrome.Visible || !plane.Chrome.GetComponentsInChildren<TMPro.TextMeshPro>(false)
                        .Any(words => words.gameObject.name == "Connection state" && words.text == WorkspaceText.Demo)))
                        failures.Add(what + ": the actual visible workspace header lost its recorded provenance.");
                    failures.AddRange(PlaneState(what, folder, camera, texture, plane, characters, eyes, null, lightLine: false));
                }
                void MinimizeAndRestore(string step, int pointer)
                {
                    var retainedMenu = navigator.Shown;
                    var retainedFile = navigator.Beside;
                    var retainedTask = navigator.BesideTask;
                    var prior = plane.Shown.LastOrDefault().View?.Frame;
                    var priorKind = plane.Shown.LastOrDefault().Kind;
                    PointerPress(plane.Chrome.Targets.Single(button => button.Label.text == WorkspaceText.Minimize), pointer);
                    Shot(step + " minimized");
                    if (!presentation.Minimized || plane.Shown.Count != 0 || plane.Chrome.Visible || plane.Bar is not MenuBarView dock
                        || !dock.Target.gameObject.activeInHierarchy || dock.Target.Label.text != WorkspaceText.OpenWorkspace
                        || dock.Controls.Move.gameObject.activeSelf)
                        failures.Add(name + ": recorded Minimize did not leave only the visible global restore dock.");
                    if (prior != null && director.PressForRender(priorKind, Footer.Close, null, prior, null))
                        failures.Add(name + ": minimized recorded content retained old input authority.");
                    if (plane.Bar is MenuBarView restore) PointerPress(restore.Target, pointer + 1);
                    Shot(step + " restored");
                    if (presentation.Minimized || navigator.Shown != retainedMenu || navigator.Beside != retainedFile || navigator.BesideTask != retainedTask)
                        failures.Add(name + ": recorded restore changed the retained menu/task composition.");
                }

                Shot("1 projects", MenuPlace.Projects);
                if (director.BeforeFirstTask || navigator.Asking)
                    failures.Add(name + ": recorded startup opened the obsolete first question.");
                if (Menu()?.Footer.All.Any(prompt => prompt.Prompt.Id == ProjectsScreens.NewProject && prompt.Prompt.Available) != true)
                    failures.Add(name + ": recorded Projects lost its explicit New project entry.");
                MinimizeAndRestore("2 initial projects", 920);
                Shot("3 returned projects", MenuPlace.Projects);

                // Recorded attention updates navigation without opening a task or enabling agents.
                (state, activity) = RecordedAt(recording, new[] { 0 });
                Shot("4 a task waits", MenuPlace.Projects);
                if (Menu()?.Sections.SingleOrDefault(section => section.Waits)?.Key != nameof(MenuPlace.Tasks))
                    failures.Add(name + ": recorded waiting work lost its Tasks attention marker beside Projects.");
                if (navigator.Beside != null || presentation.Minimized)
                    failures.Add(name + ": recorded attention changed the user's current composition by itself.");
                var waiting = state.Workstreams.Values.Single(task => CharacterLineup.TierOf(task) == LineupTier.NeedsYou);
                Press(ProjectsScreens.ChooseProject, waiting.ProjectId);
                Shot("5 project tasks", MenuPlace.Tasks);
                Press(TasksColumn.OpenTask, waiting.WorkstreamId);
                Shot("6 selected recorded task");
                if (selected == null || navigator.Beside != selected || navigator.BesideTask != waiting.WorkstreamId
                    || plane.SelectedMarks.All(mark => mark.Word != StateLanguage.Demo))
                    failures.Add(name + ": character-free task navigation lost its selected recorded identity or provenance.");
                MinimizeAndRestore("7 recorded task", 922);
                if (plane.SelectedMarks.All(mark => mark.Word != StateLanguage.Demo))
                    failures.Add(name + ": restored recorded task lost its provenance.");
                PointerPress(plane.Chrome.Targets.Single(button => button.Label.text == "Projects"), 924);
                Shot("8 projects", MenuPlace.Projects);
                Press(ProjectsScreens.NewProject);
                Shot("9 explicit recorded creation");
                if (flow == null || navigator.Beside != flow || flow.Step != NewProjectStep.YourIdea
                    || plane.Showing(MenuColumn.File)?.Subject != EntryText.CreateProject)
                    failures.Add(name + ": recorded Projects New project did not open its explicit creation wizard.");
                if (plane.Showing(MenuColumn.File)?.Footer.All.Any(prompt => prompt.Prompt.Id == NewProjectScreens.HoldToTalk) == true)
                    failures.Add(name + ": recorded creation offered microphone input.");
                if (sends != 0) failures.Add(name + ": recorded navigation, minimize, restore or opening creation submitted a command.");
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

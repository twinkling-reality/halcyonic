#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI.Editor;
using Halcyonic.XR.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>Both real-font sizes, focused answers plus production organization/placement witnesses.</summary>
        public static void OrganizationCheck()
        {
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "OrganizationRenders", variant));
                Directory.CreateDirectory(folder);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                ViewField.Current = FieldChecks.Quest3S;
                var result = CompactStateSemanticsForRender().ToList();
                result.AddRange(RenderPresetChoices("far-3s", folder));
                result.AddRange(RenderYourAnswers("far-3s", folder, CharacterStage.DefaultDistance, null));
                result.AddRange(RenderMenuDirector("far-3s", folder, CharacterStage.DefaultDistance, null));
                result.AddRange(RenderMenuDirector("desk-3s", folder, 0.55f, 0.46f));
                result.AddRange(RenderMenuDirector("window-3s", folder, CharacterStage.DefaultDistance, null, true));
                ViewField.Current = null;
                KeepFontAssetsAsCommitted();
                foreach (var failure in result) Debug.LogError("Halcyonic: organization render: " + failure);
                if (result.Count == 0) Debug.Log("Halcyonic: organization render: every check passed; " + folder);
                return result;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static IEnumerable<string> CompactStateSemanticsForRender()
        {
            var holder = new GameObject("Compact state semantics witness");
            try
            {
                var badge = StateLanguage.BadgeOf(Presentation("working-compact", 0));
                var view = MenuBarView.Create(holder.transform, "Compact");
                var retained = new object();
                view.Show(new MenuBar(MenuPlace.Tasks, "1 task is waiting for you"), Glaze.Menu.FileColumnDegrees,
                    LineWords.Theirs("Working task"), badge, waitingCount: 1, compactBinding: retained);
                if (view.Badge.Shown?.Tone != badge.Tone || view.Badge.Shown?.Icon != badge.Icon || view.Badge.Shown?.Word != badge.Word)
                    yield return "compact: waiting elsewhere changed the retained Working task's badge semantics";
                if (view.WaitingCount.text != "1 waiting" || view.WaitingCount.color != GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Attention).Foreground))
                    yield return "compact: the separate waiting count lost its words or waiting tone";
                var retainedTitle = LineWords.TheirName("Verified retained task title");
                var director = MenuDirector.Create(holder.transform, new MenuDirector.Setup
                {
                    File = (_, _) => new StubColumn(() => new MenuFrame(retainedTitle, new Footer(new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close)))),
                });
                director.OpenFile("known-task-missing-from-latest-state");
                director.Compact();
                director.DrawNow();
                var identity = director.Plane.Bar?.GetComponentsInChildren<TMPro.TMP_Text>(false).FirstOrDefault(label => label.name == "Retained identity");
                if (identity == null || !identity.text.Contains(retainedTitle.Text) || identity.text.Contains(ProjectsText.NewProject))
                    yield return "compact: a missing retained task was mislabeled as a new project instead of retaining its verified title";
            }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
        }

        private static IEnumerable<string> RenderPresetChoices(string name, string folder)
        {
            var failures = new List<string>();
            var root = new GameObject("Measured preset choices witness");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            try
            {
                var work = Work.Running();
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                var view = CharacterView.Create(root.transform, work.Workstream.WorkstreamId);
                view.Show(work.Present().Character);
                var (height, scale) = CharacterStage.Stance(view, CharacterStage.DefaultDistance, 0f, CharacterStage.DefaultHeightFromEyes);
                view.transform.SetPositionAndRotation(eyes + Vector3.forward * CharacterStage.DefaultDistance + Vector3.up * height, Quaternion.LookRotation(Vector3.back));
                view.transform.localScale = Vector3.one * scale;
                var target = CharacterTarget.Attach(view, work.Workstream.WorkstreamId);
                var characters = new List<(CharacterView View, CharacterTarget Target)> { (view, target) };
                var choices = Enumerable.Range(0, 9).Select(index => new PresetInstruction("Choice " + index,
                    "Preserve exact output for choice " + index + " and report every condition before continuing.")).ToArray();
                var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
                var commands = new CommandFactory(client);
                FileColumn? file = null;
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    Commands = commands, Connected = () => true, State = () => work.State,
                    Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    File = (host, _) => file = new FileColumn(host, () => work.Present(), commands, () => null, _ => choices, () => ""),
                    StageNow = () => new MenuDirector.Stage(eyes, target.BodyPosition - eyes, new[] { target }, null, false),
                    CharacterOf = _ => target, Bar = place => TasksColumn.Bar(place, work.State),
                });
                void Capture(string step)
                {
                    director.DrawNow();
                    failures.AddRange(PlaneState(name + " preset " + step, folder, camera, texture, director.Plane, characters, eyes, null));
                }
                bool Press(string action, string? key = null)
                {
                    director.DrawNow();
                    System.Threading.Thread.Sleep(TimeSpan.FromSeconds(0.45));
                    var frame = director.Plane.Showing(MenuColumn.File);
                    if (frame == null || !(frame.Lines.Any(line => line.Action == action && (key == null || line.Key == key))
                        || frame.Footer.All.Any(each => each.Prompt.Id == action)))
                    { failures.Add(name + ": preset witness has no offered control " + action); return false; }
                    var accepted = director.Navigator.Act(MenuColumn.File, action, key, frame, null);
                    director.DrawNow();
                    return accepted;
                }
                director.OpenFile(work.Workstream.WorkstreamId);
                director.DrawNow();
                Press(FileScreens.TellIt);
                Capture("first page");
                if (!file!.Frame!.Focused || file.Screen.PresetPages < 2) failures.Add(name + ": oversized preset chooser was not focused and paged");
                var chosen = file.Frame.Lines.First(line => line.Action == FileScreens.Preset);
                Press(FileScreens.Preset, chosen.Key);
                var candidate = file.Screen.PresetToSend;
                for (var part = 0; part < file.Screen.PresetPages; part++)
                {
                    Capture("choices " + (file.Screen.PresetPage + 1));
                    Press(FileScreens.NextPresetPage);
                    if (file.Screen.PresetToSend != candidate) failures.Add(name + ": browsing preset choices discarded the exact candidate");
                }
                file.Said(string.Join(" ", Enumerable.Range(0, 30).Select(index => "Nothing was sent because condition " + index + " could not be accepted.")));
                Capture("full notice");
                if (!file.Screen.ShowingPresetNotice) failures.Add(name + ": long preset notice had no full explanation route");
                for (var part = 1; part < file.Screen.PresetNoticeParts; part++)
                {
                    Press(FileScreens.NextPresetNotice);
                    Capture("notice " + (file.Screen.PresetNoticePart + 1));
                }
                Press(FileScreens.BackPresetNotice);
                Capture("choices after notice");
                if (file.Screen.PresetToSend != candidate || file.Screen.Notice != null) failures.Add(name + ": notice acknowledgement lost the preset candidate or notice truth");
                Press(FileScreens.Cancel);
                Capture("ordinary file after cancel");
                if (file.Screen.Presets != null || file.Frame!.Focused) failures.Add(name + ": preset Cancel did not return to the ordinary file");
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(root); }
            return failures;
        }

        private static void PressMoveForOrganizationRender(MenuDirector menu, int pointer)
        {
            var move = menu.Plane.Bar?.Controls.Move ?? menu.Plane.Shown.FirstOrDefault(each => each.Kind == MenuColumn.File).View?.Targets.FirstOrDefault(button => button.Label.text == "Move");
            if (move == null) throw new InvalidOperationException("No visible Move target in the organization witness.");
            // Advance only the prompt's settling witness, then use the real pointer/release route.
            typeof(Halcyonic.XR.UI.GlazeButton).GetField("shownAt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(move, float.NegativeInfinity);
            move.Target.PointerForRender(pointer, Oculus.Interaction.PointerEventType.Select, move.transform.position);
            move.Target.PointerForRender(pointer, Oculus.Interaction.PointerEventType.Unselect, move.transform.position);
        }

        /// <summary>Exercises the actual production character handler without starting a session or persisting settings.</summary>
        private static void SelectCharacterThroughWorkspace(MenuDirector menu, CharacterTarget character, RealtimeSession session)
        {
            var holder = new GameObject("Character selection witness");
            holder.SetActive(false);
            try
            {
                // Inactive required components never enable networking. Supply the same unstarted session/state as the file.
                var workspace = holder.AddComponent<WorkspaceDirector>();
                var connection = holder.GetComponent<Halcyonic.XR.ControlPlaneConnection>();
                typeof(Halcyonic.XR.ControlPlaneConnection).GetField("sessions", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(connection,
                    new DemonstrationFallback(session, () => null));
                typeof(WorkspaceDirector).GetField("connection", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(workspace, connection);
                typeof(WorkspaceDirector).GetField("menu", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(workspace, menu);
                typeof(WorkspaceDirector).GetMethod("OnSelected", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, new object[] { character });
            }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
        }
    }
}

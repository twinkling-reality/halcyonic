#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Halcyonic.Client;
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
            }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
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
        private static void SelectCharacterThroughWorkspace(MenuDirector menu, CharacterTarget character)
        {
            var holder = new GameObject("Character selection witness");
            holder.SetActive(false);
            try
            {
                // Awake is delayed while inactive; this handler's retained route only consumes its menu.
                var workspace = holder.AddComponent<WorkspaceDirector>();
                typeof(WorkspaceDirector).GetField("menu", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(workspace, menu);
                typeof(WorkspaceDirector).GetMethod("OnSelected", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, new object[] { character });
            }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
        }
    }
}

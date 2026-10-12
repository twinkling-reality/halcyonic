#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Halcyonic.Client;
using Halcyonic.XR.UI.Editor;
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
                var result = RenderYourAnswers("far-3s", folder, CharacterStage.DefaultDistance, null).ToList();
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

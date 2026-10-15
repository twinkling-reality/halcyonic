#nullable enable
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>Explicit scene references only in transient development build scenes. Never saves
    /// the source scene or adds shaders to Resources/Always Included. Release artifact exclusion
    /// must still be verified on the built release, rather than inferred from source stripping.</summary>
    public sealed class ReadingVisibilityInDevelopmentBuilds : IProcessSceneWithReport
    {
        public int callbackOrder => 100;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null) return;
            var development = (report.summary.options & BuildOptions.Development) != 0;
            foreach (var old in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ReadingVisibilityAssets>(true)).ToArray())
                Object.DestroyImmediate(old.gameObject);
            if (!development) return;
            AddTo(scene);
        }
        public static ReadingVisibilityAssets AddTo(Scene scene)
        {
            var root = new GameObject("Development reading visibility assets");
            SceneManager.MoveGameObjectToScene(root, scene);
            var assets = root.AddComponent<ReadingVisibilityAssets>();
            assets.Opaque = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Halcyonic/Workspace/Development/ReadingOpaque.shader");
            assets.Mask = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Halcyonic/Workspace/Development/ReadingMask.shader");
            if (assets.Opaque == null || assets.Mask == null) throw new BuildFailedException("Reading comparison shaders unavailable; no fallback is permitted.");
            return assets;
        }
    }
}

#nullable enable
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Halcyonic.XR.Editor
{
    /// <summary>
    /// Builds the development APK for a Meta Quest into Builds/, which git ignores. From the editor:
    /// Halcyonic > Build Quest APK. In batch mode, with the editor closed, see
    /// docs/internal/runbooks/XR_DEVELOPMENT.md.
    /// </summary>
    public static class QuestBuild
    {
        public const string ApkPath = "Builds/Halcyonic.apk";

        [MenuItem("Halcyonic/Build Quest APK")]
        public static void BuildDevelopmentApk()
        {
            // An APK, not an app bundle: adb installs APKs.
            EditorUserBuildSettings.buildAppBundle = false;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development,
            });
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"Halcyonic: built {summary.outputPath} ({summary.totalSize} bytes) in {summary.totalTime}.");
                return;
            }
            var message = $"Halcyonic: the Quest build ended {summary.result} with {summary.totalErrors} errors.";
            if (!Application.isBatchMode) throw new BuildFailedException(message);
            Debug.LogError(message);
            EditorApplication.Exit(1);
        }
    }
}

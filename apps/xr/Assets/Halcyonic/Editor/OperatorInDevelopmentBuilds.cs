#nullable enable
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Halcyonic.XR.Editor
{
    /// <summary>
    /// Keeps Meta XR Operator out of every build unless a journey build asks for it. Meta's own build
    /// step (MetaXROperatorBuildProcessor, callback order 0) puts the Operator's Android library into
    /// every development build. While an app built with it runs, the library serves agents over MCP on
    /// port 8720 on every address of the headset, with no authentication and any web origin allowed
    /// (validation/quest-headset-in-the-loop.md): anything on the headset or its network, and any
    /// browser page that can reach it, can read the scene and capture what the wearer sees. This runs
    /// after Meta's step and leaves the whole library out, its layer and its manifest file included,
    /// unless <see cref="Include"/> is set: only a development build made by <see cref="QuestBuild"/>
    /// with <c>HALCYONIC_OPERATOR=1</c> in its environment, a journey build. QuestBuild refuses an APK
    /// that carries the layer without it, or lacks it with it (<see cref="FindIn"/>).
    /// </summary>
    internal sealed class OperatorInDevelopmentBuilds : IPreprocessBuildWithReport
    {
        /// <summary>Meta's library: the layer's .so for each ABI and its implicit layer manifest.</summary>
        internal const string Library = "XrApiLayer_METAX_operator_unity_android";

        /// <summary>The environment variable that asks for the Operator in a development build.</summary>
        internal const string Variable = "HALCYONIC_OPERATOR";

        /// <summary>What an APK carries of the Operator's layer: its native library and its layer manifest.</summary>
        internal static readonly string[] LayerFiles =
        {
            "lib/arm64-v8a/libXrApiLayer_METAX_operator.so",
            "assets/openxr/1/api_layers/implicit.d/XrApiLayer_METAX_operator.json",
        };

        /// <summary>Set by QuestBuild for a development build whose environment asks for the Operator, and cleared after it.</summary>
        internal static bool Include;

        /// <summary>Whether this process's environment asks for the Operator: exactly <c>1</c>.</summary>
        internal static bool Asked() => System.Environment.GetEnvironmentVariable(Variable) == "1";

        // After Meta's MetaXROperatorBuildProcessor (0), whose include delegate this replaces.
        public int callbackOrder => 1;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            var include = Include && (report.summary.options & BuildOptions.Development) != 0;
            var found = false;
            foreach (var importer in PluginImporter.GetAllImporters())
            {
                if (!importer.assetPath.Contains(Library)) continue;
                importer.SetIncludeInBuildDelegate(_ => include);
                found = true;
            }
            if (!found) return;
            Debug.Log(include
                ? "Halcyonic: Meta XR Operator is in this journey build; its server on 8720 has no authentication."
                : "Halcyonic: Meta XR Operator is left out of this build.");
        }

        /// <summary>The Operator's layer files the APK carries; empty when it carries none.</summary>
        internal static List<string> FindIn(string apkPath)
        {
            using var apk = ZipFile.OpenRead(apkPath);
            var names = new HashSet<string>(apk.Entries.Select(entry => entry.FullName));
            return LayerFiles.Where(names.Contains).ToList();
        }
    }
}

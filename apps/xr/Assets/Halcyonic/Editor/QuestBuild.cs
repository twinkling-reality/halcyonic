#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Halcyonic.XR.Editor
{
    /// <summary>
    /// Builds APKs for a Meta Quest into Builds/, which git ignores: a development APK for the owner's
    /// headset, and a release APK for the Meta Horizon Store that leaves Meta's development tools out.
    /// From the editor: Halcyonic > Build Quest APK and Halcyonic > Build Quest Release APK. In batch
    /// mode, with the editor closed, see docs/internal/runbooks/XR_DEVELOPMENT.md.
    /// </summary>
    public static class QuestBuild
    {
        public const string DevelopmentApkPath = "Builds/Halcyonic.apk";
        public const string ReleaseApkPath = "Builds/Halcyonic-release.apk";

        [MenuItem("Halcyonic/Build Quest APK")]
        public static void BuildDevelopmentApk()
        {
            Finish(Build(DevelopmentApkPath, BuildOptions.Development).summary);
        }

        /// <summary>
        /// The microphone permission. Only development builds may ask for it, for hold to talk (ADR
        /// 0021); a release or demonstration APK carries no voice.
        /// </summary>
        public const string MicrophonePermission = "android.permission.RECORD_AUDIO";

        /// <summary>
        /// The identifier Meta's build step writes into <c>com.oculus.supportedDevices</c> for Meta VR
        /// Glasses when the project targets them. The release build never declares them: nobody has
        /// run Halcyonic on them (horizon-store-release.md).
        /// </summary>
        public const string GlassesDevice = "stanley";

        /// <summary>
        /// Builds without the development option, with DevAgentSettings.asset moved out of Resources
        /// and the version code from <see cref="ReleaseVersionCode.Variable"/> when it is set, then
        /// checks the APK for Meta's development tools, the microphone permission, the glance spike and
        /// the Glasses device identifier, and deletes it if any remains.
        /// </summary>
        [MenuItem("Halcyonic/Build Quest Release APK")]
        public static void BuildReleaseApk()
        {
            if (!ReleaseVersionCode.TryRead(out var versionCode, out var problem))
            {
                Fail(problem);
                return;
            }
            BuildReport report;
            try
            {
                using (DevAgentSettingsAside.Begin())
                using (ReleaseVersionCode.Apply(versionCode))
                {
                    report = Build(ReleaseApkPath, BuildOptions.None);
                }
            }
            catch (BuildFailedException e)
            {
                Fail(e.Message);
                return;
            }
            if (report.summary.result == BuildResult.Succeeded)
            {
                var found = MetaDevelopmentTools.FindIn(ReleaseApkPath);
                if (found.Count > 0)
                {
                    File.Delete(ReleaseApkPath);
                    Fail($"Halcyonic: deleted {ReleaseApkPath}, which carries Meta's development tools: {string.Join("; ", found)}.");
                    return;
                }
                if (MetaDevelopmentTools.ManifestAsks(ReleaseApkPath, MicrophonePermission))
                {
                    File.Delete(ReleaseApkPath);
                    Fail($"Halcyonic: deleted {ReleaseApkPath}, which asks for {MicrophonePermission}: only development builds may use the microphone (ADR 0021).");
                    return;
                }
                var glance = GlanceInDevelopmentBuilds.FindIn(ReleaseApkPath);
                if (glance.Count > 0)
                {
                    File.Delete(ReleaseApkPath);
                    Fail($"Halcyonic: deleted {ReleaseApkPath}, which carries the glance spike, for development builds only: {string.Join("; ", glance)}.");
                    return;
                }
                if (MetaDevelopmentTools.ManifestAsks(ReleaseApkPath, GlassesDevice))
                {
                    File.Delete(ReleaseApkPath);
                    Fail($"Halcyonic: deleted {ReleaseApkPath}, which declares Meta VR Glasses ({GlassesDevice}): the store build claims only devices it has run on.");
                    return;
                }
            }
            Finish(report.summary);
        }

        private static BuildReport Build(string apkPath, BuildOptions options)
        {
            // An APK, not an app bundle: adb installs APKs, and the Horizon Store takes APKs.
            EditorUserBuildSettings.buildAppBundle = false;
            // The glance spike goes only into development builds made here (GlanceInDevelopmentBuilds).
            GlanceInDevelopmentBuilds.Include = (options & BuildOptions.Development) != 0;
            try
            {
                return BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                    locationPathName = apkPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = options,
                });
            }
            finally
            {
                GlanceInDevelopmentBuilds.Include = false;
            }
        }

        private static void Finish(BuildSummary summary)
        {
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"Halcyonic: built {summary.outputPath} ({new FileInfo(summary.outputPath).Length} bytes) in {summary.totalTime}.");
                return;
            }
            Fail($"Halcyonic: the Quest build ended {summary.result} with {summary.totalErrors} errors.");
        }

        private static void Fail(string message)
        {
            if (!Application.isBatchMode) throw new BuildFailedException(message);
            Debug.LogError(message);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>
    /// Meta's development tools, which a release APK must not carry:
    /// <list type="bullet">
    /// <item>Meta XR Operator's Android library: the agentic media projection activity and service,
    /// the FOREGROUND_SERVICE_MEDIA_PROJECTION permission, and an OpenXR API layer that serves agents
    /// from inside the app. Meta's own build step leaves it out of every non-development build.</item>
    /// <item>The Immersive Debugger's dev agent, and the agent bridge through which it reaches the
    /// editor, which <see cref="LeaveOutMetaDevelopmentAssemblies"/> leaves out. The debugger's
    /// runtime assembly stays: the MR Utility Kit's <c>MRUK.Awake</c> reads its settings, and the
    /// linker cannot process a method whose assembly is missing. The committed settings keep the
    /// debugger disabled, and its runtime has no networking; see horizon-store-release.md.</item>
    /// <item>DevAgentSettings.asset, into which Meta's build step writes this Mac's LAN address and
    /// the token of the editor's remote agent server for every build, and which ships because it
    /// sits in Resources. <see cref="DevAgentSettingsAside"/> keeps it out.</item>
    /// </list>
    /// </summary>
    internal static class MetaDevelopmentTools
    {
        // Meta.XR.ImmersiveDebugger.Interface stays: Meta's building blocks reference its attributes.
        // Meta.XR.ImmersiveDebugger stays: the MR Utility Kit's MRUK.Awake reads its RuntimeSettings.
        internal static readonly string[] Assemblies =
        {
            "Meta.XR.ImmersiveDebugger.DevAgent",
            "meta.xr.ai.agentbridge",
            "meta.xr.ai.agentbridge.telemetry",
        };

        internal const string DevAgentSettingsPath = "Assets/Resources/DevAgentSettings.asset";

        private static readonly string[] OperatorManifestEntries =
        {
            "com.meta.agenticxr",
            "FOREGROUND_SERVICE_MEDIA_PROJECTION",
        };

        /// <summary>What the APK still carries of Meta's development tools; empty when it is clean.</summary>
        internal static List<string> FindIn(string apkPath)
        {
            var found = new List<string>();
            using var apk = ZipFile.OpenRead(apkPath);
            found.AddRange(apk.Entries.Select(entry => entry.FullName).Where(name => name.Contains("METAX_operator")));

            var manifest = Read(apk, "AndroidManifest.xml", found);
            found.AddRange(OperatorManifestEntries
                .Where(entry => Contains(manifest, entry))
                .Select(entry => $"{entry} in the manifest"));

            var assemblies = Encoding.UTF8.GetString(Read(apk, "assets/bin/Data/ScriptingAssemblies.json", found));
            found.AddRange(Assemblies.Select(name => $"{name}.dll").Where(file => assemblies.Contains($"\"{file}\"")));

            // The Resources container names each asset in Resources by its lowercase path.
            if (Contains(Read(apk, "assets/bin/Data/globalgamemanagers", found), "devagentsettings"))
                found.Add("DevAgentSettings in Resources");
            return found;
        }

        /// <summary>Whether the APK's manifest names <paramref name="text"/>, such as a permission; true when there is no manifest to read.</summary>
        internal static bool ManifestAsks(string apkPath, string text)
        {
            using var apk = ZipFile.OpenRead(apkPath);
            var missing = new List<string>();
            var manifest = Read(apk, "AndroidManifest.xml", missing);
            return missing.Count > 0 || Contains(manifest, text);
        }

        private static byte[] Read(ZipArchive apk, string entryName, List<string> found)
        {
            var entry = apk.GetEntry(entryName);
            if (entry == null)
            {
                found.Add($"no {entryName} to check");
                return Array.Empty<byte>();
            }
            using var stream = entry.Open();
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            return bytes.ToArray();
        }

        // Both encodings: a compiled manifest keeps its strings in UTF-8 or UTF-16.
        private static bool Contains(byte[] data, string text) =>
            IndexOf(data, Encoding.UTF8.GetBytes(text)) >= 0 || IndexOf(data, Encoding.Unicode.GetBytes(text)) >= 0;

        private static int IndexOf(byte[] data, byte[] pattern)
        {
            for (var i = 0; i <= data.Length - pattern.Length; i++)
            {
                var j = 0;
                while (j < pattern.Length && data[i + j] == pattern[j]) j++;
                if (j == pattern.Length) return i;
            }
            return -1;
        }
    }

    /// <summary>
    /// Leaves <see cref="MetaDevelopmentTools.Assemblies"/> out of every non-development player build,
    /// as Unity's Test Framework leaves its own assemblies out.
    /// </summary>
    internal sealed class LeaveOutMetaDevelopmentAssemblies : IFilterBuildAssemblies
    {
        public int callbackOrder => 0;

        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if ((buildOptions & BuildOptions.Development) != 0) return assemblies;
            return assemblies
                .Where(path => !MetaDevelopmentTools.Assemblies.Contains(Path.GetFileNameWithoutExtension(path)))
                .ToArray();
        }
    }

    /// <summary>
    /// Moves DevAgentSettings.asset out of Resources until disposed. Meta never recreates it while a
    /// player builds, so the build leaves it out.
    /// </summary>
    internal sealed class DevAgentSettingsAside : IDisposable
    {
        // Outside every Resources folder; git ignores it in case a build is interrupted.
        private const string AsidePath = "Assets/DevAgentSettings.asset";

        private readonly bool moved;

        private DevAgentSettingsAside(bool moved)
        {
            this.moved = moved;
        }

        internal static DevAgentSettingsAside Begin()
        {
            const string settingsPath = MetaDevelopmentTools.DevAgentSettingsPath;
            if (!File.Exists(settingsPath)) return new DevAgentSettingsAside(false);
            if (File.Exists(AsidePath))
            {
                throw new BuildFailedException(
                    $"Halcyonic: {AsidePath} exists, left by an interrupted release build. Move it back to {settingsPath}, or delete it.");
            }
            var error = AssetDatabase.MoveAsset(settingsPath, AsidePath);
            if (error.Length > 0) throw new BuildFailedException($"Halcyonic: could not move {settingsPath} aside: {error}");
            return new DevAgentSettingsAside(true);
        }

        public void Dispose()
        {
            if (!moved) return;
            const string settingsPath = MetaDevelopmentTools.DevAgentSettingsPath;
            var error = AssetDatabase.MoveAsset(AsidePath, settingsPath);
            if (error.Length > 0) Debug.LogError($"Halcyonic: could not move {AsidePath} back to {settingsPath}: {error}");
        }
    }

    /// <summary>
    /// Fails a non-development build that would ship DevAgentSettings.asset: one started anywhere but
    /// <see cref="QuestBuild.BuildReleaseApk"/>, such as the Build Profiles window.
    /// </summary>
    internal sealed class RefuseDevAgentSettingsInRelease : IPreprocessBuildWithReport
    {
        // After Meta's build processors, which write into the asset.
        public int callbackOrder => int.MaxValue;

        public void OnPreprocessBuild(BuildReport report)
        {
            if ((report.summary.options & BuildOptions.Development) != 0) return;
            if (!File.Exists(MetaDevelopmentTools.DevAgentSettingsPath)) return;
            throw new BuildFailedException(
                $"Halcyonic: a release build would ship {MetaDevelopmentTools.DevAgentSettingsPath}, which holds this Mac's LAN address "
                + "and an access token. Build it with Halcyonic > Build Quest Release APK.");
        }
    }

    /// <summary>
    /// The version code of a release APK. Every upload needs one above every earlier upload's
    /// (developers report the store refuses a repeated one), so an upload's comes from the environment variable
    /// <see cref="Variable"/>, in the form YYMMDDNN (the date and that day's build number, for
    /// example 26111701), set for the build and put back afterwards, so ProjectSettings never
    /// changes. Without it the project's own code (1) is kept, and the log says it is not for an
    /// upload. See docs/internal/runbooks/XR_DEVELOPMENT.md, "Before an upload".
    /// </summary>
    internal sealed class ReleaseVersionCode : IDisposable
    {
        internal const string Variable = "HALCYONIC_VERSION_CODE";

        /// <summary>The largest version code Android accepts.</summary>
        internal const int Largest = 2100000000;

        private readonly int previous;

        private ReleaseVersionCode(int previous)
        {
            this.previous = previous;
        }

        /// <summary>The version code to build with, or 0 to keep the project's; false with a reason when the variable is malformed.</summary>
        internal static bool TryRead(out int versionCode, out string problem)
        {
            versionCode = 0;
            problem = "";
            var text = Environment.GetEnvironmentVariable(Variable);
            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.Log($"Halcyonic: the release APK keeps version code {PlayerSettings.Android.bundleVersionCode}; set {Variable} for an upload.");
                return true;
            }
            if (!int.TryParse(text.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out versionCode)
                || versionCode < 1 || versionCode > Largest)
            {
                versionCode = 0;
                problem = $"Halcyonic: {Variable} must be a whole number from 1 to {Largest}.";
                return false;
            }
            return true;
        }

        /// <summary>Sets <paramref name="versionCode"/> for the build, unless it is 0, and puts the project's back when disposed.</summary>
        internal static ReleaseVersionCode Apply(int versionCode)
        {
            var applied = new ReleaseVersionCode(PlayerSettings.Android.bundleVersionCode);
            if (versionCode > 0)
            {
                PlayerSettings.Android.bundleVersionCode = versionCode;
                Debug.Log($"Halcyonic: the release APK carries version code {versionCode}.");
            }
            return applied;
        }

        public void Dispose()
        {
            PlayerSettings.Android.bundleVersionCode = previous;
        }
    }
}

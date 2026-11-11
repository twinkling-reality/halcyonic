#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;

namespace Halcyonic.XR.Editor
{
    /// <summary>
    /// Every Android permission a release APK asks for is one the judge build's record names
    /// (docs/internal/validation/competition-judge-build.md, "The release APK"). Unity writes
    /// <c>MODIFY_AUDIO_SETTINGS</c> and <c>BLUETOOTH</c> into its generated manifest beside
    /// <c>RECORD_AUDIO</c> whenever the Microphone class is used; hold to talk records the headset's own
    /// microphone and changes no audio setting, so this takes both out of the library's manifest in every build,
    /// and <see cref="Unchosen"/> lets <see cref="QuestBuild"/> refuse a release APK that asks for anything else.
    /// </summary>
    internal sealed class ChosenPermissions : IPostGenerateGradleAndroidProject
    {
        private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";

        /// <summary>Unity's additions for the microphone that hold to talk does not use.</summary>
        internal static readonly string[] Removed = { "android.permission.MODIFY_AUDIO_SETTINGS", "android.permission.BLUETOOTH" };

        /// <summary>
        /// The permissions the record names: hands, the room's anchors and scene, the network, the microphone for hold to
        /// talk, and AndroidX's own receiver permission, which is named after the package.
        /// </summary>
        internal static readonly string[] Named =
        {
            "com.oculus.permission.HAND_TRACKING", "com.oculus.permission.USE_ANCHOR_API", "com.oculus.permission.USE_SCENE",
            "android.permission.INTERNET", "android.permission.RECORD_AUDIO",
        };

        private const string ReceiverSuffix = ".DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION";

        // After the boundary's (100_002), so nothing later adds them back.
        public int callbackOrder => 100_003;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath)) return;
            var document = new XmlDocument();
            document.Load(manifestPath);
            var manifest = document.DocumentElement!;
            var removed = new List<string>();
            foreach (var element in manifest.SelectNodes("uses-permission")!.OfType<XmlElement>().ToList())
            {
                var name = element.GetAttribute("name", AndroidNamespace);
                if (!Removed.Contains(name)) continue;
                manifest.RemoveChild(element);
                removed.Add(name);
            }
            if (removed.Count == 0) return;
            document.Save(manifestPath);
            UnityEngine.Debug.Log("Halcyonic: left out " + string.Join(" and ", removed) + ", which hold to talk does not use.");
        }

        /// <summary>
        /// The permissions <paramref name="apkPath"/> asks for that the record does not name, read with aapt2 from
        /// Unity's own Android SDK; a single entry saying why when they cannot be read, so a check never passes unread.
        /// </summary>
        internal static List<string> Unchosen(string apkPath)
        {
            var aapt2 = Aapt2();
            if (aapt2 == null) return new List<string> { "no aapt2 in Unity's Android SDK to read the permissions" };
            var start = new ProcessStartInfo(aapt2, $"dump permissions \"{apkPath}\"")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            };
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) return new List<string> { "aapt2 could not read the permissions (exit " + process.ExitCode + ")" };
            // The first line names the package: package: com.halcyonic.xr
            var package = output.Split('\n').FirstOrDefault(line => line.StartsWith("package:", StringComparison.Ordinal))?.Substring("package:".Length).Trim();
            if (string.IsNullOrEmpty(package)) return new List<string> { "aapt2 named no package, so its own receiver permission cannot be told apart" };
            // Lines such as: uses-permission: name='android.permission.INTERNET'
            var asked = output.Split('\n')
                .Where(line => line.StartsWith("uses-permission", StringComparison.Ordinal))
                .Select(line => line.Split('\'').ElementAtOrDefault(1) ?? line.Trim())
                .ToList();
            // AndroidX's receiver permission only under the APK's own package, never any name that merely ends the same way (AUDIT).
            return asked.Where(name => !Named.Contains(name) && name != package + ReceiverSuffix).ToList();
        }

        private static string? Aapt2()
        {
            var sdk = AndroidExternalToolsSettings.sdkRootPath;
            var tools = Path.Combine(sdk ?? "", "build-tools");
            if (string.IsNullOrEmpty(sdk) || !Directory.Exists(tools)) return null;
            return Directory.GetDirectories(tools).OrderBy(each => each, StringComparer.Ordinal)
                .Select(each => Path.Combine(each, "aapt2")).LastOrDefault(File.Exists);
        }
    }
}

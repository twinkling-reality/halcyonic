#nullable enable
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace Halcyonic.XR.Editor
{
    /// <summary>
    /// Adds the glance (apps/xr/Android/glance), a small 2D window for use inside another immersive
    /// app, to development builds made by <see cref="QuestBuild"/>, and to nothing else: its Java
    /// sources live outside Assets, so Unity never builds them on its own, and this step copies them
    /// into the generated Gradle project and adds its activity and the notification permission to
    /// the library's manifest only while <see cref="Include"/> is set. A spike: the release build
    /// refuses an APK that carries any of it (<see cref="FindIn"/>).
    /// </summary>
    internal sealed class GlanceInDevelopmentBuilds : IPostGenerateGradleAndroidProject
    {
        private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";

        /// <summary>The glance's package: its classes, and the name in the manifest.</summary>
        internal const string Package = "com.halcyonic.glance";

        internal const string Activity = Package + ".GlanceActivity";

        internal const string NotificationPermission = "android.permission.POST_NOTIFICATIONS";

        /// <summary>Set by QuestBuild for a development build, and cleared after it.</summary>
        internal static bool Include;

        // After Meta's OVRGradleGeneration (99999), which writes the network security configuration this changes.
        public int callbackOrder => 100_000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var java = Path.Combine(path, "src", "main", "java");
            // Unity reuses the Gradle project between builds and leaves files it did not write, so a
            // development build's copy would otherwise reach the next release build.
            var copied = Path.Combine(java, "com", "halcyonic", "glance");
            if (Directory.Exists(copied)) Directory.Delete(copied, recursive: true);
            // Earlier builds of the spike allowed cleartext to 127.0.0.1; its own sockets need no exception.
            RemoveLoopbackCleartext(Path.Combine(path, "src", "main", "res", "xml", NetworkConfigFile));
            if (!Include) return;
            var sources = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Android", "glance", "src"));
            foreach (var source in Directory.GetFiles(sources, "*.java", SearchOption.AllDirectories))
            {
                var target = Path.Combine(java, Path.GetRelativePath(sources, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: true);
            }
            AddToManifest(Path.Combine(path, "src", "main", "AndroidManifest.xml"));
            Debug.Log("Halcyonic: the glance spike is in this development build.");
        }

        private static void AddToManifest(string manifestPath)
        {
            var document = new XmlDocument();
            document.Load(manifestPath);
            var manifest = document.DocumentElement!;
            var application = (XmlElement?)manifest.SelectSingleNode("application")
                ?? throw new InvalidDataException("The library manifest has no application.");

            var permission = document.CreateElement("uses-permission");
            permission.SetAttribute("name", AndroidNamespace, NotificationPermission);
            manifest.InsertBefore(permission, application);

            var activity = document.CreateElement("activity");
            activity.SetAttribute("name", AndroidNamespace, Activity);
            activity.SetAttribute("label", AndroidNamespace, "Halcyonic");
            activity.SetAttribute("exported", AndroidNamespace, "true");
            // Its window shows task titles: keep it out of the recent apps' thumbnails.
            activity.SetAttribute("excludeFromRecents", AndroidNamespace, "true");
            activity.SetAttribute("launchMode", AndroidNamespace, "singleTask");
            activity.SetAttribute("taskAffinity", AndroidNamespace, "com.halcyonic.xr.glance");
            activity.SetAttribute("theme", AndroidNamespace, "@android:style/Theme.DeviceDefault.NoActionBar");
            var filter = document.CreateElement("intent-filter");
            filter.AppendChild(Named(document, "action", "android.intent.action.MAIN"));
            filter.AppendChild(Named(document, "category", "android.intent.category.DEFAULT"));
            // A hybrid app's 2D activity, and the one launched from inside another immersive app.
            filter.AppendChild(Named(document, "category", "com.oculus.intent.category.2D"));
            filter.AppendChild(Named(document, "category", "com.oculus.intent.category.OVERLAY_LAUNCHER"));
            activity.AppendChild(filter);
            var layout = document.CreateElement("layout");
            layout.SetAttribute("defaultWidth", AndroidNamespace, "640dp");
            layout.SetAttribute("defaultHeight", AndroidNamespace, "720dp");
            layout.SetAttribute("minWidth", AndroidNamespace, "480dp");
            layout.SetAttribute("minHeight", AndroidNamespace, "480dp");
            activity.AppendChild(layout);
            application.AppendChild(activity);
            document.Save(manifestPath);
        }

        /// <summary>The network security configuration Meta's build step writes, which refuses all cleartext.</summary>
        internal const string NetworkConfigFile = "network_sec_config.xml";

        /// <summary>The control plane through adb reverse, which a stale cleartext exception would name.</summary>
        internal const string LoopbackHost = "127.0.0.1";

        /// <summary>
        /// Takes out a domain configuration allowing cleartext to 127.0.0.1, which builds of the spike
        /// before it read over its own socket added, should the Gradle project keep one. Android's
        /// cleartext policy binds its HTTP stacks, not a plain socket such as GlancePoll's or the C#
        /// client's.
        /// </summary>
        private static void RemoveLoopbackCleartext(string configPath)
        {
            if (!File.Exists(configPath)) return;
            var document = new XmlDocument();
            document.Load(configPath);
            var added = document.DocumentElement!.SelectNodes("domain-config[domain='" + LoopbackHost + "']");
            if (added == null || added.Count == 0) return;
            foreach (XmlNode node in added) node.ParentNode!.RemoveChild(node);
            document.Save(configPath);
        }

        private static XmlElement Named(XmlDocument document, string element, string name)
        {
            var named = document.CreateElement(element);
            named.SetAttribute("name", AndroidNamespace, name);
            return named;
        }

        /// <summary>
        /// What of the glance an APK carries: its activity or the notification permission in the
        /// manifest, its classes in any dex file, or cleartext to 127.0.0.1 in a resource. Empty for a
        /// build without it.
        /// </summary>
        internal static List<string> FindIn(string apkPath)
        {
            var found = new List<string>();
            using var apk = ZipFile.OpenRead(apkPath);
            foreach (var entry in apk.Entries)
            {
                var manifest = entry.FullName == "AndroidManifest.xml";
                var dex = entry.FullName.StartsWith("classes", System.StringComparison.Ordinal) && entry.FullName.EndsWith(".dex", System.StringComparison.Ordinal);
                var resource = entry.FullName.StartsWith("res/", System.StringComparison.Ordinal) && entry.FullName.EndsWith(".xml", System.StringComparison.Ordinal);
                if (!manifest && !dex && !resource) continue;
                using var stream = entry.Open();
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                var data = copy.ToArray();
                if (manifest && Contains(data, Activity)) found.Add("the glance's activity in the manifest");
                if (manifest && Contains(data, NotificationPermission)) found.Add(NotificationPermission + " in the manifest");
                if (dex && Contains(data, "Lcom/halcyonic/glance/")) found.Add("the glance's classes in " + entry.FullName);
                if (resource && Contains(data, "domain-config") && Contains(data, LoopbackHost)) found.Add("cleartext to " + LoopbackHost + " in " + entry.FullName);
            }
            return found;
        }

        /// <summary>
        /// The kinds of finding <see cref="FindIn"/> makes in an APK that carries the whole glance and
        /// are not among <paramref name="found"/>: none for a development build, or the check is blind.
        /// </summary>
        internal static List<string> MissingFrom(IReadOnlyList<string> found)
        {
            var kinds = new[] { "the glance's activity in the manifest", NotificationPermission + " in the manifest", "the glance's classes in " };
            return kinds.Where(kind => !found.Any(finding => finding.StartsWith(kind, System.StringComparison.Ordinal))).ToList();
        }

        private static bool Contains(byte[] data, string text) =>
            IndexOf(data, Encoding.UTF8.GetBytes(text)) >= 0 || IndexOf(data, Encoding.Unicode.GetBytes(text)) >= 0;

        private static int IndexOf(byte[] data, byte[] pattern)
        {
            for (var start = 0; start <= data.Length - pattern.Length; start++)
            {
                var match = true;
                for (var index = 0; index < pattern.Length; index++)
                {
                    if (data[start + index] == pattern[index]) continue;
                    match = false;
                    break;
                }
                if (match) return start;
            }
            return -1;
        }
    }
}

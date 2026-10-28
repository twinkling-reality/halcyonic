#nullable enable
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace Halcyonic.XR.Editor
{
    /// <summary>Packages the production panel in development and release APKs, apart from the glance spike.</summary>
    internal sealed class PanelInBuilds : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 100_001;
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var java = Path.Combine(path, "src", "main", "java");
            var copied = Path.Combine(java, "com", "halcyonic", "panel");
            if (Directory.Exists(copied)) Directory.Delete(copied, recursive: true);
            var sources = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Android", "panel", "src"));
            foreach (var source in Directory.GetFiles(sources, "*.java", SearchOption.AllDirectories))
            {
                var target = Path.Combine(java, Path.GetRelativePath(sources, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: true);
            }
            // Deliberate offline visual proof only. Remove a stale development asset before a
            // release reuses this Gradle directory; the release activity also refuses preview.
            var preview = Path.Combine(path, "src", "main", "assets", "halcyonic-native-panel-preview.json");
            if (File.Exists(preview)) File.Delete(preview);
            if (GlanceInDevelopmentBuilds.Include)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
                var fixture = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "fixtures", "workspace", "native-panel.json"));
                File.Copy(fixture, preview, overwrite: true);
                var development = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Android", "panel", "development"));
                foreach (var source in Directory.GetFiles(development, "*.java", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(java, Path.GetRelativePath(development, source));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(source, target, overwrite: true);
                }
            }
            PreviewManifest(Path.Combine(path, "src", "main", "AndroidManifest.xml"), GlanceInDevelopmentBuilds.Include);
        }

        private static void PreviewManifest(string path, bool include)
        {
            const string android = "http://schemas.android.com/apk/res/android";
            const string name = "com.halcyonic.panel.PanelPreviewActivity";
            var document = new XmlDocument(); document.Load(path);
            var application = (XmlElement?)document.DocumentElement!.SelectSingleNode("application")
                ?? throw new InvalidDataException("The library manifest has no application.");
            foreach (XmlNode node in application.SelectNodes("activity")!)
                if (node is XmlElement activity && activity.GetAttribute("name", android) == name) application.RemoveChild(node);
            if (include)
            {
                var activity = document.CreateElement("activity");
                activity.SetAttribute("name", android, name);
                activity.SetAttribute("exported", android, "false");
                activity.SetAttribute("excludeFromRecents", android, "true");
                activity.SetAttribute("launchMode", android, "singleTask");
                activity.SetAttribute("taskAffinity", android, "${applicationId}.panel.preview");
                activity.SetAttribute("theme", android, "@android:style/Theme.DeviceDefault.NoActionBar");
                activity.SetAttribute("resizeableActivity", android, "true");
                var filter = document.CreateElement("intent-filter");
                foreach (var category in new[] { "android.intent.category.DEFAULT", "com.oculus.intent.category.2D", "com.oculus.intent.category.OVERLAY_LAUNCHER" })
                {
                    var item = document.CreateElement("category"); item.SetAttribute("name", android, category); filter.AppendChild(item);
                }
                var action = document.CreateElement("action"); action.SetAttribute("name", android, "android.intent.action.MAIN"); filter.AppendChild(action);
                activity.AppendChild(filter);
                var layout = document.CreateElement("layout");
                layout.SetAttribute("defaultWidth", android, "640dp"); layout.SetAttribute("defaultHeight", android, "720dp");
                layout.SetAttribute("minWidth", android, "480dp"); layout.SetAttribute("minHeight", android, "480dp");
                activity.AppendChild(layout);
                application.AppendChild(activity);
            }
            document.Save(path);
        }
    }
}

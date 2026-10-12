#nullable enable
using System.IO;
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
            }
        }
    }
}

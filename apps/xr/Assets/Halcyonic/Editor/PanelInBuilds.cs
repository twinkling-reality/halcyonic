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
        }
    }
}

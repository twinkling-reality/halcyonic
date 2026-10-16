#nullable enable
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace Halcyonic.XR.Editor
{
    /// <summary>
    /// Lets development builds made by <see cref="QuestBuild"/>, and nothing else, hide the headset's
    /// boundary while the room shows: adds exactly the permission Meta's Boundary Visibility Support
    /// would add, <see cref="Permission"/>, to the library's manifest only while <see cref="Include"/>
    /// is set. <c>OculusProjectConfig</c> keeps that support at None, so no other build carries it, and
    /// the release build refuses an APK that asks for it. Never the boundaryless app feature.
    /// </summary>
    internal sealed class BoundaryInDevelopmentBuilds : IPostGenerateGradleAndroidProject
    {
        private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";

        internal const string Permission = "com.oculus.permission.BOUNDARY_VISIBILITY";

        /// <summary>The feature for apps with no immersive part, which Halcyonic never declares.</summary>
        internal const string BoundarylessFeature = "com.oculus.feature.BOUNDARYLESS_APP";

        /// <summary>Set by QuestBuild for a development build, and cleared after it.</summary>
        internal static bool Include;

        // After Meta's OVRGradleGeneration (99999), the glance (100_000) and the panel (100_001).
        public int callbackOrder => 100_002;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // Unity reuses the Gradle project between builds, so a development build's permission is
            // taken out again before a build that must not carry it.
            var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            var document = new XmlDocument();
            document.Load(manifestPath);
            var manifest = document.DocumentElement!;
            var existing = manifest.SelectNodes("uses-permission");
            XmlElement? found = null;
            foreach (XmlElement element in existing!)
            {
                if (element.GetAttribute("name", AndroidNamespace) == Permission) found = element;
            }
            if (Include && found == null)
            {
                var permission = document.CreateElement("uses-permission");
                permission.SetAttribute("name", AndroidNamespace, Permission);
                manifest.InsertBefore(permission, manifest.SelectSingleNode("application"));
                Debug.Log("Halcyonic: this development build may hide the boundary while the room shows.");
            }
            else if (!Include && found != null)
            {
                manifest.RemoveChild(found);
            }
            document.Save(manifestPath);
        }
    }
}

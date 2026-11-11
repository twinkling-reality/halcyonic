#nullable enable
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace Halcyonic.XR.Editor
{
    /// <summary>
    /// Lets development builds made by <see cref="QuestBuild"/>, and nothing else, play a project's preview in the headset's
    /// browser over the pairing (ADR 0052): writes the relay's service and its two permissions into the library's manifest
    /// only while <see cref="Include"/> is set (<see cref="RelayManifest"/>), and takes them out of any other build. A
    /// release build carries none of them, and its permission check (<see cref="ChosenPermissions"/>) does not name them.
    /// </summary>
    internal sealed class RelayInDevelopmentBuilds : IPostGenerateGradleAndroidProject
    {
        /// <summary>Set by QuestBuild for a development build, and cleared after it.</summary>
        internal static bool Include;

        // After the boundary's (100_002).
        public int callbackOrder => 100_003;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            var document = new XmlDocument();
            document.Load(manifestPath);
            RelayManifest.Apply(document, Include);
            document.Save(manifestPath);
            if (Include) Debug.Log("Halcyonic: this development build may play a preview in the headset's browser over the pairing.");
        }
    }
}

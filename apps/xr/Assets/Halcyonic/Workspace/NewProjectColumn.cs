#nullable enable
using System;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// New project for the menu's director (ADR 0026): <see cref="NewProjectFlow"/> with what this
    /// device keeps for it. Its drafts go to a file in the app's private storage, under Android's
    /// getFilesDir and never Unity's persistentDataPath; the id of a start whose outcome is unknown is
    /// kept for the journal it was sent to, where the entry panel reads it too (<see cref="KeptUnknownStart"/>);
    /// and the demonstration plays the companion's recorded exchange, keeping no draft.
    /// </summary>
    public static class NewProjectColumn
    {
        private const string DraftsFile = "creation-drafts.json";

        /// <summary>
        /// New Project over <paramref name="host"/>, its drafts in <paramref name="drafts"/>' store, else the device's
        /// (<see cref="DeviceDrafts"/>); the demonstration keeps none.
        /// </summary>
        public static NewProjectFlow Create(IMenuHost host, CommandFactory commands, Func<ICreationDraftStore>? drafts = null)
        {
            var demonstration = host.Demonstration;
            var store = demonstration ? null : drafts != null ? drafts() : DeviceDrafts();
            return new NewProjectFlow(host, commands, new KeptUnknownStart(new PlayerPreferences(), () => host.State), store, demonstration ? Recording() : null);
        }

        /// <summary>The drafts' file in the app's private storage; refused in batch mode, where a check hands in its own.</summary>
        public static ICreationDraftStore DeviceDrafts() => new FileCreationDraftStore(ControlPlaneSettings.PrivateFile(DraftsFile));

        /// <summary>The demonstration's recorded exchange with the companion, or none when it can't be read: then the fixed questions stand in.</summary>
        private static CompanionRecording? Recording()
        {
            var asset = Resources.Load<TextAsset>(CompanionRecording.ResourceName);
            if (asset == null) return null;
            try
            {
                return CompanionRecording.Parse(asset.text);
            }
            catch (System.FormatException error)
            {
                Debug.LogWarning("Halcyonic: the companion's recorded exchange couldn't be read (" + error.GetType().Name + ").");
                return null;
            }
        }
    }
}

#nullable enable
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

        public static NewProjectFlow Create(IMenuHost host, CommandFactory commands)
        {
            var demonstration = host.Demonstration;
            var drafts = demonstration ? null : new FileCreationDraftStore(ControlPlaneSettings.PrivateFile(DraftsFile));
            return new NewProjectFlow(host, commands, new KeptUnknownStart(new PlayerPreferences(), () => host.State), drafts, demonstration ? Recording() : null);
        }

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

#nullable enable
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// New project for the menu's director (ADR 0026): <see cref="NewProjectFlow"/> with what this
    /// device keeps for it. Its drafts go to a file in the app's private storage, under Android's
    /// getFilesDir and never Unity's persistentDataPath; the id of a start whose outcome is unknown is
    /// kept where the entry panel kept it, so one it left carries over; and the demonstration plays the
    /// companion's recorded exchange, keeping no draft.
    /// </summary>
    public static class NewProjectColumn
    {
        private const string DraftsFile = "creation-drafts.json";

        /// <summary>The entry panel's own key, so a start it left with an unknown outcome still comes first.</summary>
        private const string UnknownCommandPreference = "halcyonic.new-work.unresolved-command-id";

        public static NewProjectFlow Create(IMenuHost host, CommandFactory commands)
        {
            var demonstration = host.Demonstration;
            var drafts = demonstration ? null : new FileCreationDraftStore(ControlPlaneSettings.PrivateFile(DraftsFile));
            return new NewProjectFlow(host, commands, new KeptInPreferences(), drafts, demonstration ? Recording() : null);
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

        private sealed class KeptInPreferences : IKeptCommand
        {
            public string? Id
            {
                get
                {
                    var id = PlayerPrefs.GetString(UnknownCommandPreference, "");
                    return id.Length == 0 ? null : id;
                }
                set
                {
                    if (value == null) PlayerPrefs.DeleteKey(UnknownCommandPreference);
                    else PlayerPrefs.SetString(UnknownCommandPreference, value);
                    PlayerPrefs.Save();
                }
            }
        }
    }
}

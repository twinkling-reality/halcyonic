#nullable enable
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The person's comfort settings (<see cref="Comfort"/>, ADR 0023), changed in the menu's Settings
    /// under Comfort (<see cref="ComfortSettings"/>) and kept on the device: reading text a step
    /// larger, Keep things still, and sounds quieter or off. They take effect as the stage starts and
    /// the moment one changes: every label takes the text's size (<see cref="GlazeText.SetScale"/>),
    /// everything that keeps moving on its own stops or starts again, badges, waits and characters alike
    /// (<see cref="StateBadgeView.Still"/>, the one switch, <see cref="GlazeMotion.Still"/>), and every
    /// sound Halcyonic plays takes the volume.
    /// </summary>
    public sealed class ComfortControls : MonoBehaviour
    {
        private const string Preference = "halcyonic.comfort";

        private Comfort comfort = new Comfort();

        /// <summary>The settings as they stand.</summary>
        public Comfort Settings => comfort;

        private void Awake()
        {
            comfort = Comfort.Load(PlayerPrefs.GetString(Preference, ""));
            Apply();
        }

        /// <summary>
        /// Keeps the settings as they stand on the device and lets them take effect now, as after the
        /// menu's Settings changed them.
        /// </summary>
        public void Keep()
        {
            PlayerPrefs.SetString(Preference, comfort.Save());
            PlayerPrefs.Save();
            Apply();
        }

        private void Apply()
        {
            GlazeText.SetScale(comfort.TextScale);
            StateBadgeView.Still = comfort.Still;
            // Halcyonic's sounds are the only sounds the app plays.
            AudioListener.volume = comfort.Volume;
        }
    }
}

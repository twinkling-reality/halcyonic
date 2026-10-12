#nullable enable
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>Owns the persisted applied appearance. A column's temporary preview never writes this key.</summary>
    public sealed class AppearanceControls : MonoBehaviour
    {
        private const string Preference = "halcyonic.appearance";
        private AppearanceSettings settings = new AppearanceSettings();
        public AppearanceSettings Settings => settings;

        private void Awake()
        {
            settings = AppearanceSettings.Load(PlayerPrefs.GetString(Preference, ""));
            Glaze.SetPalette(settings.Applied);
        }

        public void Keep()
        {
            PlayerPrefs.SetString(Preference, settings.Save());
            PlayerPrefs.Save();
        }
    }
}

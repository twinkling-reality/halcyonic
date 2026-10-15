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
        private const string CharacterPreference = "halcyonic.character-colours";
        public CharacterColourSettings CharacterColours { get; private set; } = new CharacterColourSettings();

        private void Awake()
        {
            CharacterColours = CharacterColourSettings.Load(PlayerPrefs.GetString(CharacterPreference, ""));
            settings = AppearanceSettings.Load(PlayerPrefs.GetString(Preference, ""));
            Glaze.SetPalette(settings.Applied);
        }

        public bool KeepCharacterColours(string encoded)
        {
            var previous = PlayerPrefs.GetString(CharacterPreference, "");
            try { PlayerPrefs.SetString(CharacterPreference, encoded); PlayerPrefs.Save(); return true; }
            catch (System.Exception)
            {
                try { PlayerPrefs.SetString(CharacterPreference, previous); } catch (System.Exception) { }
                return false;
            }
        }

        public void Keep()
        {
            PlayerPrefs.SetString(Preference, settings.Save());
            PlayerPrefs.Save();
        }
    }
}

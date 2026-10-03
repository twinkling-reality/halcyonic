#nullable enable
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>The device's preferences as Unity keeps them, saved at once, so a start's unknown outcome survives a crash (<see cref="KeptUnknownStart"/>).</summary>
    public sealed class PlayerPreferences : IDevicePreferences
    {
        public string? Get(string key) => PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key, "") : null;

        public void Set(string key, string? value)
        {
            if (value == null) PlayerPrefs.DeleteKey(key);
            else PlayerPrefs.SetString(key, value);
            PlayerPrefs.Save();
        }
    }
}

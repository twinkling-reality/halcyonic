#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>
    /// The redesign's panel glass (the lead's 13:33Z, from the owner's ask panel): Dark, the owner's reference and the default, or Light, our own
    /// inverse. The person's choice on Settings › Appearance, kept on the device beside the appearance; it changes no work, so no journal event.
    /// </summary>
    public enum PanelTheme
    {
        Dark,
        Light,
    }

    /// <summary>The panel theme in force, read by the kit's glass and its words; set at start from the kept choice and the moment a person chooses.</summary>
    public static class PanelThemes
    {
        /// <summary>The two choices' one name each (AUDIT2's 13:34Z): never "Dark Mode", "Night" or another.</summary>
        public const string DarkWords = "Dark";

        public const string LightWords = "Light";

        public static PanelTheme Current { get; private set; } = PanelTheme.Dark;

        /// <summary>Raised once per change, never for the theme already in force: the kit eases its faces from one to the other.</summary>
        public static event Action<PanelTheme>? Changed;

        public static void Set(PanelTheme theme)
        {
            if (Current == theme) return;
            Current = theme;
            Changed?.Invoke(theme);
        }

        public static string Words(PanelTheme theme) => theme == PanelTheme.Light ? LightWords : DarkWords;

        /// <summary>The kept key, exact: "dark" or "light".</summary>
        public static string Key(PanelTheme theme) => theme == PanelTheme.Light ? "light" : "dark";

        /// <summary>The theme a kept key names; null for any other text, which the caller reads as the default.</summary>
        public static PanelTheme? Find(string? key) => key switch
        {
            "dark" => PanelTheme.Dark,
            "light" => PanelTheme.Light,
            _ => null,
        };
    }
}

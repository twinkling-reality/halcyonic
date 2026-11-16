#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>Device appearance only, separate from comfort, projects, pairing and runtime settings.</summary>
    public sealed class AppearanceSettings
    {
        public GlazePalette Applied { get; private set; } = GlazePalette.Default;

        /// <summary>The redesign's panel glass the person chose (<see cref="PanelTheme"/>), Dark by default.</summary>
        public PanelTheme Panels { get; private set; } = PanelTheme.Dark;

        public void Apply(GlazePalette palette) => Applied = palette ?? throw new ArgumentNullException(nameof(palette));

        public void ApplyPanels(PanelTheme panels) => Panels = panels;

        public string Save() => "version=2;theme=" + Applied.Key + ";panels=" + PanelThemes.Key(Panels);

        /// <summary>
        /// An unknown version, incomplete value or damaged choice returns the current default appearance. A first version's value
        /// (before the panel theme) keeps its palette and reads Dark; a second version's unknown panel theme reads Dark too.
        /// </summary>
        public static AppearanceSettings Load(string? saved)
        {
            var appearance = new AppearanceSettings();
            if (saved == null) return appearance;
            var parts = saved.Split(';');
            var first = parts.Length == 2 && parts[0] == "version=1";
            var second = parts.Length == 3 && parts[0] == "version=2" && parts[2].StartsWith("panels=", StringComparison.Ordinal);
            if (!(first || second) || !parts[1].StartsWith("theme=", StringComparison.Ordinal)) return appearance;
            var palette = GlazePalette.Find(parts[1].Substring(6));
            if (palette != null) appearance.Apply(palette);
            if (second && PanelThemes.Find(parts[2].Substring(7)) is PanelTheme panels) appearance.ApplyPanels(panels);
            return appearance;
        }
    }
}

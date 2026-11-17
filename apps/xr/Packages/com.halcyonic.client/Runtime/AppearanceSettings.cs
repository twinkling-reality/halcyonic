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

        /// <summary>How code is coloured (<see cref="CodeTheme"/>), Halora by default.</summary>
        public CodeTheme Code { get; private set; } = CodeTheme.Halora;

        public void Apply(GlazePalette palette) => Applied = palette ?? throw new ArgumentNullException(nameof(palette));

        public void ApplyPanels(PanelTheme panels) => Panels = panels;

        public void ApplyCode(CodeTheme code) => Code = code;

        public string Save() => "version=3;theme=" + Applied.Key + ";panels=" + PanelThemes.Key(Panels) + ";code=" + CodeThemes.Key(Code);

        /// <summary>
        /// An unknown version, incomplete value or damaged choice returns the current default appearance. A first version's value
        /// (before the panel theme) keeps its palette and reads Dark; a second version's unknown panel theme reads Dark too. A first or
        /// second version's value (before Code Colours) reads Halora, and so does a third version's unknown code theme.
        /// </summary>
        public static AppearanceSettings Load(string? saved)
        {
            var appearance = new AppearanceSettings();
            if (saved == null) return appearance;
            var parts = saved.Split(';');
            var first = parts.Length == 2 && parts[0] == "version=1";
            var second = parts.Length == 3 && parts[0] == "version=2" && parts[2].StartsWith("panels=", StringComparison.Ordinal);
            var third = parts.Length == 4 && parts[0] == "version=3" && parts[2].StartsWith("panels=", StringComparison.Ordinal)
                && parts[3].StartsWith("code=", StringComparison.Ordinal);
            if (!(first || second || third) || !parts[1].StartsWith("theme=", StringComparison.Ordinal)) return appearance;
            var palette = GlazePalette.Find(parts[1].Substring(6));
            if (palette != null) appearance.Apply(palette);
            if ((second || third) && PanelThemes.Find(parts[2].Substring(7)) is PanelTheme panels) appearance.ApplyPanels(panels);
            if (third && CodeThemes.Find(parts[3].Substring(5)) is CodeTheme code) appearance.ApplyCode(code);
            return appearance;
        }
    }
}

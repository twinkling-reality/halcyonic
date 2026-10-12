#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>Device appearance only, separate from comfort, projects, pairing and runtime settings.</summary>
    public sealed class AppearanceSettings
    {
        public GlazePalette Applied { get; private set; } = GlazePalette.Default;

        public void Apply(GlazePalette palette) => Applied = palette ?? throw new ArgumentNullException(nameof(palette));

        public string Save() => "version=1;theme=" + Applied.Key;

        /// <summary>An unknown version, incomplete value or damaged choice returns the original appearance.</summary>
        public static AppearanceSettings Load(string? saved)
        {
            var appearance = new AppearanceSettings();
            if (saved == null) return appearance;
            var parts = saved.Split(';');
            if (parts.Length != 2 || parts[0] != "version=1" || !parts[1].StartsWith("theme=", StringComparison.Ordinal)) return appearance;
            var palette = GlazePalette.Find(parts[1].Substring(6));
            if (palette != null) appearance.Apply(palette);
            return appearance;
        }
    }
}

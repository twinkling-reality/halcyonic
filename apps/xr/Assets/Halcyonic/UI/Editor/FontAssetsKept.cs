#nullable enable
using System;
using System.Linq;
using UnityEditor;

namespace Halcyonic.XR.UI.Editor
{
    /// <summary>
    /// A run never writes the kit's font assets (the lead, 2026-11-12: every landing left GlazeIcons, KitMono, KitLabel and
    /// KitReading changed, their scale ratios 0.9 or 0.875 by which text a run drew, and the fallback atlas grown). TextMesh Pro
    /// recomputes a font material's ratios whenever it measures text, from what that text draws, so the values are a run's,
    /// not the font's; and a dynamic fallback atlas grows with what a run draws. Every save drops those assets, except inside
    /// the atlas tools' own builds (<see cref="Save"/>), which are the only writes of them that mean to change them.
    /// </summary>
    public sealed class FontAssetsKept : AssetModificationProcessor
    {
        private const string KitFonts = "Assets/Halcyonic/UI/Resources/HalcyonicUI/";
        private const string Fallback = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        private static int building;

        /// <summary>Whether a save would drop <paramref name="path"/>: a kit font asset or the fallback atlas, outside a build.</summary>
        public static bool Kept(string path) =>
            building == 0 && ((path.StartsWith(KitFonts, StringComparison.Ordinal) && path.EndsWith(".asset", StringComparison.Ordinal)) || path == Fallback);

        /// <summary>The atlas tools' one save: their built fonts are written as built.</summary>
        public static void Save()
        {
            building++;
            try
            {
                AssetDatabase.SaveAssets();
            }
            finally
            {
                building--;
            }
        }

        private static string[] OnWillSaveAssets(string[] paths) => paths.Where(path => !Kept(path)).ToArray();
    }
}

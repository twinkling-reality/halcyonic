#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Halcyonic.XR.UI.Editor
{
    /// <summary>
    /// Builds the icon atlas (ADR 0023): a static TextMeshPro SDF font asset of every glyph of the
    /// Glaze icon font (<see cref="GlazeIconGlyphs.All"/>), which apps/xr/tools/glaze_icons.py makes
    /// from Material Symbols Rounded. Static, so it never grows at run time and the player carries no
    /// font file; one sampling size, in the smallest square atlas of 256, 512 or 1024 pixels that holds
    /// every glyph. A rebuild keeps the asset's identity. In the editor: Halcyonic > Build the Icon
    /// Atlas. In batch mode, see docs/internal/runbooks/XR_DEVELOPMENT.md; it exits with 1 when a
    /// glyph is missing.
    /// </summary>
    public static class GlazeIconAtlas
    {
        public const string FontPath = "Assets/Halcyonic/UI/Icons/MaterialSymbolsRounded-Glaze.ttf";

        /// <summary>Where the atlas is kept, under a Resources folder, so the interface loads it by name.</summary>
        public const string AssetPath = "Assets/Halcyonic/UI/Resources/HalcyonicUI/GlazeIcons.asset";

        private const string BuildingPath = "Assets/Halcyonic/UI/Resources/HalcyonicUI/GlazeIcons building.asset";

        /// <summary>The atlas's name, its file's.</summary>
        private const string Name = "GlazeIcons";

        /// <summary>The pixels a glyph's em is drawn at, and the reach of its distance field around it.</summary>
        private const int SamplingSize = 56;

        private const int Padding = 7;

        private static readonly int[] AtlasSizes = { 256, 512, 1024 };

        [MenuItem("Halcyonic/Build the Icon Atlas")]
        public static void Menu()
        {
            var error = Build();
            EditorUtility.DisplayDialog("Icon atlas", error ?? "Built " + AssetPath + ".", "OK");
        }

        /// <summary>The batch entry point: exits with 0 when every glyph is in the atlas, 1 otherwise.</summary>
        public static void Check()
        {
            var error = Build();
            if (error != null) Debug.LogError("Halcyonic: icon atlas: " + error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }

        /// <summary>Builds the atlas, or says why it could not.</summary>
        public static string? Build()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null) return "there is no icon font at " + FontPath + "; run apps/xr/tools/glaze_icons.py first.";
            foreach (var size in AtlasSizes)
            {
                var atlas = TMP_FontAsset.CreateFontAsset(font, SamplingSize, Padding, GlyphRenderMode.SDFAA, size, size,
                    AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: false);
                // The glyphs are all in the font, so any that do not go in found no room in this atlas.
                if (!atlas.TryAddCharacters(GlazeIconGlyphs.All, out _))
                {
                    Discard(atlas);
                    continue;
                }
                // Static from here: the asset keeps no reference to the font, so a player carries none.
                atlas.atlasPopulationMode = AtlasPopulationMode.Static;
                atlas.name = Name;
                atlas.atlasTexture.name = "GlazeIcons Atlas";
                atlas.material.name = "GlazeIcons Material";
                Save(atlas);
                var saved = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
                if (saved == null || !saved.HasCharacters(GlazeIconGlyphs.All, out List<char> missing) || saved.sourceFontFile != null)
                {
                    return "the saved atlas lacks glyphs or still refers to its font.";
                }
                if (saved.name != Name) return "the saved atlas is named " + saved.name + ", not " + Name + ".";
                Debug.Log("Halcyonic: icon atlas: " + GlazeIconGlyphs.All.Length + " glyphs in " + size + " by " + size + " pixels, sampled at "
                    + SamplingSize.ToString(CultureInfo.InvariantCulture) + " with a padding of " + Padding.ToString(CultureInfo.InvariantCulture) + ", in " + AssetPath + ".");
                return null;
            }
            return "the glyphs do not fit a " + AtlasSizes[AtlasSizes.Length - 1] + " pixel atlas at a sampling size of " + SamplingSize + ".";
        }

        /// <summary>
        /// Saves the atlas with its texture and material in one asset. A rebuild is written in place of
        /// the asset before it, so its identity, and its meta file, stay as they were.
        /// </summary>
        private static void Save(TMP_FontAsset atlas)
        {
            AssetDatabase.DeleteAsset(BuildingPath);
            AssetDatabase.CreateAsset(atlas, BuildingPath);
            AssetDatabase.AddObjectToAsset(atlas.atlasTexture, atlas);
            AssetDatabase.AddObjectToAsset(atlas.material, atlas);
            AssetDatabase.SaveAssets();
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath) == null)
            {
                var error = AssetDatabase.MoveAsset(BuildingPath, AssetPath);
                if (!string.IsNullOrEmpty(error)) throw new IOException(error);
                return;
            }
            var project = Path.GetDirectoryName(Application.dataPath)!;
            File.Copy(Path.Combine(project, BuildingPath), Path.Combine(project, AssetPath), overwrite: true);
            AssetDatabase.DeleteAsset(BuildingPath);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            // The copy keeps the name the building file gave it; the asset is named for its own file.
            var saved = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
            if (saved == null || saved.name == Name) return;
            saved.name = Name;
            EditorUtility.SetDirty(saved);
            AssetDatabase.SaveAssets();
        }

        private static void Discard(TMP_FontAsset atlas)
        {
            Object.DestroyImmediate(atlas.atlasTexture);
            Object.DestroyImmediate(atlas.material);
            Object.DestroyImmediate(atlas);
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Halcyonic.XR.UI.Editor
{
    /// <summary>Builds the shipped, static title face; body and uncommon glyphs keep the existing reading face.</summary>
    public static class InstrumentFontAtlas
    {
        public const string AssetPath = "Assets/Halcyonic/UI/Resources/HalcyonicUI/InstrumentType.asset";
        private const string BuildingPath = "Assets/Halcyonic/UI/Resources/HalcyonicUI/InstrumentType building.asset";
        private const string FontPath = "Assets/Halcyonic/UI/Fonts/Rajdhani/Rajdhani-Medium.ttf";
        private static readonly string Characters = new string(Enumerable.Range(32, 95).Select(value => (char)value).ToArray()) + "…–—‘’“”·";

        public static void Check()
        {
            try
            {
                if (!File.ReadAllBytes("Assets/Halcyonic/UI/Fonts/Rajdhani/OFL.txt").SequenceEqual(
                    File.ReadAllBytes("Assets/StreamingAssets/Halcyonic-Licenses/Rajdhani-OFL.txt")))
                    throw new InvalidOperationException("The shipped title-font license differs from its source license.");
                Build();
                var saved = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
                if (saved == null || saved.atlasPopulationMode != AtlasPopulationMode.Static || saved.sourceFontFile != null
                    || !saved.HasCharacters(Characters) || saved.fallbackFontAssetTable.Count != 1
                    || saved.fallbackFontAssetTable[0] != TMP_Settings.defaultFontAsset)
                    throw new InvalidOperationException("The static title face or its reading fallback is incomplete.");
                var root = new GameObject("Instrument type check");
                try
                {
                    var title = GlazeText.Create(root.transform, "Selected work", GlazeType.Hero, Color.white, TextAlignmentOptions.TopLeft, 1);
                    if (title.font != saved || title.richText || title.fontSize != GlazeTokens.FontSize(GlazeTokens.Units(Halcyonic.Client.Glaze.HeroDegrees)))
                        throw new InvalidOperationException("The production title does not consume the bundled face and literal text policy.");
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
                Debug.Log("Halcyonic: instrument type: static atlas, required glyphs, fallback and production title passed.");
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogError("Halcyonic: instrument type: " + error.Message); EditorApplication.Exit(1); }
        }

        private static void Build()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null) throw new InvalidOperationException("The bundled OFL title font is missing.");
            var atlas = TMP_FontAsset.CreateFontAsset(font, 72, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: false);
            if (!atlas.TryAddCharacters(Characters, out _))
            {
                UnityEngine.Object.DestroyImmediate(atlas.atlasTexture);
                UnityEngine.Object.DestroyImmediate(atlas.material);
                UnityEngine.Object.DestroyImmediate(atlas);
                throw new InvalidOperationException("Required title glyphs do not fit the static atlas.");
            }
            atlas.atlasPopulationMode = AtlasPopulationMode.Static;
            atlas.name = "InstrumentType";
            atlas.atlasTexture.name = "InstrumentType Atlas";
            atlas.material.name = "InstrumentType Material";
            atlas.fallbackFontAssetTable = new List<TMP_FontAsset> { TMP_Settings.defaultFontAsset };
            AssetDatabase.DeleteAsset(BuildingPath);
            AssetDatabase.CreateAsset(atlas, BuildingPath);
            AssetDatabase.AddObjectToAsset(atlas.atlasTexture, atlas);
            AssetDatabase.AddObjectToAsset(atlas.material, atlas);
            AssetDatabase.SaveAssets();
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath) == null)
            {
                var error = AssetDatabase.MoveAsset(BuildingPath, AssetPath);
                if (!string.IsNullOrEmpty(error)) throw new IOException(error);
            }
            else
            {
                File.Copy(BuildingPath, AssetPath, overwrite: true);
                AssetDatabase.DeleteAsset(BuildingPath);
                AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            }
        }
    }
}

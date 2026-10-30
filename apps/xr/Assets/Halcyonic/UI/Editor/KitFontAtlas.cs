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
    /// <summary>
    /// Builds the room's six static faces (the owner's yes, 2026-10-30; ART's cuts): Archivo Expanded Regular for display and
    /// SemiBold for titles, Inter Regular for reading, Medium for labels and SemiBold for strong words, JetBrains Mono for
    /// commands and code. Each is a static SDF atlas of the glyphs the kit draws, its
    /// licence shipped beside the app byte for byte, Liberation Sans its fallback for anything outside the atlas.
    /// </summary>
    public static class KitFontAtlas
    {
        private const string Resources = "Assets/Halcyonic/UI/Resources/HalcyonicUI/";
        private const string Licenses = "Assets/StreamingAssets/Halcyonic-Licenses/";

        /// <summary>Every face: its asset name, its source file, its licence and the licence's shipped copy.</summary>
        public static readonly (string Name, string Font, string License, string Shipped)[] Faces =
        {
            ("KitDisplay", "Assets/Halcyonic/UI/Fonts/Archivo/ArchivoExpanded-Regular.ttf", "Assets/Halcyonic/UI/Fonts/Archivo/OFL.txt", Licenses + "Archivo-OFL.txt"),
            ("KitTitle", "Assets/Halcyonic/UI/Fonts/Archivo/ArchivoExpanded-SemiBold.ttf", "Assets/Halcyonic/UI/Fonts/Archivo/OFL.txt", Licenses + "Archivo-OFL.txt"),
            ("KitReading", "Assets/Halcyonic/UI/Fonts/Inter/Inter-Regular.ttf", "Assets/Halcyonic/UI/Fonts/Inter/OFL.txt", Licenses + "Inter-OFL.txt"),
            ("KitReadingStrong", "Assets/Halcyonic/UI/Fonts/Inter/Inter-SemiBold.ttf", "Assets/Halcyonic/UI/Fonts/Inter/OFL.txt", Licenses + "Inter-OFL.txt"),
            ("KitLabel", "Assets/Halcyonic/UI/Fonts/Inter/Inter-Medium.ttf", "Assets/Halcyonic/UI/Fonts/Inter/OFL.txt", Licenses + "Inter-OFL.txt"),
            ("KitMono", "Assets/Halcyonic/UI/Fonts/JetBrainsMono/JetBrainsMono-Regular.ttf", "Assets/Halcyonic/UI/Fonts/JetBrainsMono/OFL.txt", Licenses + "JetBrainsMono-OFL.txt"),
        };

        /// <summary>The glyphs every face holds: printable ASCII and the punctuation the kit's words and others' text use.</summary>
        public static readonly string Characters = new string(Enumerable.Range(32, 95).Select(value => (char)value).ToArray()) + "\u2026\u2013\u2014\u2018\u2019\u201C\u201D\u00B7\u2212";

        public static string AssetPath(string name) => Resources + name + ".asset";

        /// <summary>Builds every face and checks it; for a batch run.</summary>
        public static void Check()
        {
            try
            {
                foreach (var face in Faces) Build(face.Name, face.Font);
                ValidateSaved();
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogError("Halcyonic: kit faces: " + error.Message); EditorApplication.Exit(1); }
        }

        /// <summary>Checks the saved faces without rebuilding them or exiting the caller.</summary>
        public static void ValidateSaved()
        {
            foreach (var (name, _, license, shipped) in Faces)
            {
                if (!File.ReadAllBytes(license).SequenceEqual(File.ReadAllBytes(shipped)))
                    throw new InvalidOperationException(name + ": the shipped licence differs from its source licence.");
                var saved = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath(name));
                if (saved == null || saved.atlasPopulationMode != AtlasPopulationMode.Static || saved.sourceFontFile != null
                    || !saved.HasCharacters(Characters, out List<char> missing) || saved.fallbackFontAssetTable.Count != 1
                    || saved.fallbackFontAssetTable[0] != TMP_Settings.defaultFontAsset)
                    throw new InvalidOperationException(name + ": the static face, its glyphs or its reading fallback is incomplete.");
            }
            Debug.Log("Halcyonic: kit faces: " + Faces.Length + " static atlases, required glyphs, fallbacks and licences passed.");
        }

        private static void Build(string name, string fontPath)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (font == null) throw new InvalidOperationException(name + ": the bundled OFL font is missing.");
            var atlas = TMP_FontAsset.CreateFontAsset(font, 72, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: false);
            if (!atlas.TryAddCharacters(Characters, out _))
            {
                UnityEngine.Object.DestroyImmediate(atlas.atlasTexture);
                UnityEngine.Object.DestroyImmediate(atlas.material);
                UnityEngine.Object.DestroyImmediate(atlas);
                throw new InvalidOperationException(name + ": required glyphs do not fit the static atlas.");
            }
            atlas.atlasPopulationMode = AtlasPopulationMode.Static;
            atlas.name = name;
            atlas.atlasTexture.name = name + " Atlas";
            atlas.material.name = name + " Material";
            atlas.fallbackFontAssetTable = new List<TMP_FontAsset> { TMP_Settings.defaultFontAsset };
            var path = AssetPath(name);
            var building = Resources + name + " building.asset";
            AssetDatabase.DeleteAsset(building);
            AssetDatabase.CreateAsset(atlas, building);
            AssetDatabase.AddObjectToAsset(atlas.atlasTexture, atlas);
            AssetDatabase.AddObjectToAsset(atlas.material, atlas);
            AssetDatabase.SaveAssets();
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) == null)
            {
                var error = AssetDatabase.MoveAsset(building, path);
                if (!string.IsNullOrEmpty(error)) throw new IOException(error);
            }
            else
            {
                File.Copy(building, path, overwrite: true);
                AssetDatabase.DeleteAsset(building);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Halcyonic.XR.UI.Editor
{
    /// <summary>
    /// Builds the room's six static faces (the owner's yes, 2026-10-30; ART's cuts; ART8's one family, 2026-10-31): Inter
    /// SemiBold for display, titles and strong words, each its own asset so a role is still found by its face, Inter Regular
    /// for reading and Medium for labels, JetBrains Mono for commands and code. Each is a static SDF atlas of the glyphs the kit draws, its
    /// licence shipped beside the app byte for byte, Liberation Sans its fallback for anything outside the atlas.
    /// </summary>
    public static class KitFontAtlas
    {
        private const string Resources = "Assets/Halcyonic/UI/Resources/HalcyonicUI/";
        private const string Licenses = "Assets/StreamingAssets/Halcyonic-Licenses/";

        /// <summary>Every face: its asset name, its source file, its licence and the licence's shipped copy.</summary>
        public static readonly (string Name, string Font, string License, string Shipped)[] Faces =
        {
            ("KitDisplay", "Assets/Halcyonic/UI/Fonts/Inter/Inter-SemiBold.ttf", "Assets/Halcyonic/UI/Fonts/Inter/OFL.txt", Licenses + "Inter-OFL.txt"),
            ("KitTitle", "Assets/Halcyonic/UI/Fonts/Inter/Inter-SemiBold.ttf", "Assets/Halcyonic/UI/Fonts/Inter/OFL.txt", Licenses + "Inter-OFL.txt"),
            ("KitReading", "Assets/Halcyonic/UI/Fonts/Inter/Inter-Regular.ttf", "Assets/Halcyonic/UI/Fonts/Inter/OFL.txt", Licenses + "Inter-OFL.txt"),
            ("KitReadingStrong", "Assets/Halcyonic/UI/Fonts/Inter/Inter-SemiBold.ttf", "Assets/Halcyonic/UI/Fonts/Inter/OFL.txt", Licenses + "Inter-OFL.txt"),
            ("KitLabel", "Assets/Halcyonic/UI/Fonts/Inter/Inter-Medium.ttf", "Assets/Halcyonic/UI/Fonts/Inter/OFL.txt", Licenses + "Inter-OFL.txt"),
            ("KitMono", "Assets/Halcyonic/UI/Fonts/JetBrainsMono/JetBrainsMono-Regular.ttf", "Assets/Halcyonic/UI/Fonts/JetBrainsMono/OFL.txt", Licenses + "JetBrainsMono-OFL.txt"),
        };

        /// <summary>
        /// The glyphs every face holds: printable ASCII, the punctuation the kit's words and others' text use, the well's marks
        /// (LineWords.Literal: the code point's angle quotes and the tab's arrow; AUDIT2's 21:48Z, AUDIT3's LI4), and the accented
        /// Latin letters of <see cref="KitGlyphs.Latin"/> (the lead, 2026-11-17T22:10Z).
        /// </summary>
        public static readonly string Characters = new string(Enumerable.Range(32, 95).Select(value => (char)value).ToArray()) + "\u2026\u2013\u2014\u2018\u2019\u201C\u201D\u2212"
            + "\u2039\u203A\u21E5" + KitGlyphs.Latin;

        /// <summary>
        /// The atlas sizes a face is tried at, smallest first: the 1024 square every face had, then twice as wide, then the 2048
        /// square, so an atlas grows only as far as its glyphs need at the same 72 pt and padding 9.
        /// </summary>
        public static readonly (int Width, int Height)[] AtlasSizes = { (1024, 1024), (2048, 1024), (2048, 2048) };

        /// <summary>
        /// A static SDF atlas of <paramref name="characters"/> from <paramref name="font"/>, at 72 pt, padding 9 and SDFAA, the
        /// smallest of <see cref="AtlasSizes"/> they fit; it names a character the font itself lacks rather than growing for it.
        /// </summary>
        public static TMP_FontAsset StaticAtlas(Font font, string characters, string name)
        {
            var missing = new List<string>();
            if (FontEngine.LoadFontFace(font, 72) != FontEngineError.Success) throw new InvalidOperationException(name + ": its font does not load.");
            foreach (var character in characters.Distinct())
                if (!FontEngine.TryGetGlyphIndex(character, out var index) || index == 0) missing.Add("U+" + ((int)character).ToString("X4"));
            if (missing.Count > 0) throw new InvalidOperationException(name + ": its font draws no " + string.Join(", ", missing) + ".");
            foreach (var (width, height) in AtlasSizes)
            {
                var atlas = TMP_FontAsset.CreateFontAsset(font, 72, 9, GlyphRenderMode.SDFAA, width, height,
                    AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: false);
                if (atlas.TryAddCharacters(characters, out _))
                {
                    atlas.atlasPopulationMode = AtlasPopulationMode.Static;
                    Debug.Log("Halcyonic: atlas: " + name + " holds " + atlas.characterTable.Count + " characters in a " + width + "x" + height + " atlas.");
                    return atlas;
                }
                UnityEngine.Object.DestroyImmediate(atlas.atlasTexture);
                UnityEngine.Object.DestroyImmediate(atlas.material);
                UnityEngine.Object.DestroyImmediate(atlas);
            }
            throw new InvalidOperationException(name + ": required glyphs do not fit the largest static atlas.");
        }

        /// <summary>
        /// The line break's mark, which JetBrains Mono does not draw: every Inter face holds it, and the mono face reaches it through
        /// <see cref="MarksFace"/>, its first fallback.
        /// </summary>
        public const string LineMark = "\u21B5";

        /// <summary>The face the mono face falls back to for <see cref="LineMark"/>: the reading face, Inter Regular.</summary>
        public const string MarksFace = "KitReading";

        /// <summary>The mono face, the only face without <see cref="LineMark"/> of its own.</summary>
        public const string MonoFace = "KitMono";

        /// <summary>
        /// The Latin letters JetBrains Mono does not draw (the trial rebuild, 2026-11-17T22:25Z): Ĳ and ĳ. The mono face reaches them
        /// through <see cref="MarksFace"/>, as it reaches <see cref="LineMark"/>.
        /// </summary>
        public const string MonoLacks = "\u0132\u0133";

        /// <summary>What a face holds: <see cref="Characters"/> and <see cref="LineMark"/>; the mono face neither the mark nor <see cref="MonoLacks"/>.</summary>
        public static string CharactersOf(string name) => name == MonoFace
            ? new string(Characters.Where(character => MonoLacks.IndexOf(character) < 0).ToArray())
            : Characters + LineMark;

        public static string AssetPath(string name) => Resources + name + ".asset";

        /// <summary>Builds every face and checks it; for a batch run.</summary>
        public static void Check()
        {
            try
            {
                // Every face is tried, so one run names every glyph a font lacks.
                var errors = new List<string>();
                foreach (var face in Faces)
                {
                    try { Build(face.Name, face.Font); }
                    catch (InvalidOperationException error) { errors.Add(error.Message); }
                }
                if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
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
                    || !saved.HasCharacters(CharactersOf(name), out List<char> missing) || !FallbacksHold(name, saved.fallbackFontAssetTable))
                    throw new InvalidOperationException(name + ": the static face, its glyphs or its reading fallback is incomplete.");
            }
            Debug.Log("Halcyonic: kit faces: " + Faces.Length + " static atlases, required glyphs, fallbacks and licences passed.");
        }

        /// <summary>Every face falls back to the reading default alone; the mono face first to <see cref="MarksFace"/> for its line mark.</summary>
        private static bool FallbacksHold(string name, List<TMP_FontAsset> table) => name == MonoFace
            ? table.Count == 2 && table[0] == AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath(MarksFace)) && table[1] == TMP_Settings.defaultFontAsset
                && table[0].HasCharacters(LineMark + MonoLacks)
            : table.Count == 1 && table[0] == TMP_Settings.defaultFontAsset;

        /// <summary>The fallback table a face is built with (<see cref="FallbacksHold"/>).</summary>
        private static List<TMP_FontAsset> FallbacksOf(string name) => name == MonoFace
            ? new List<TMP_FontAsset> { AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath(MarksFace)), TMP_Settings.defaultFontAsset }
            : new List<TMP_FontAsset> { TMP_Settings.defaultFontAsset };

        private static void Build(string name, string fontPath)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (font == null) throw new InvalidOperationException(name + ": the bundled OFL font is missing.");
            var atlas = StaticAtlas(font, CharactersOf(name), name);
            atlas.name = name;
            atlas.atlasTexture.name = name + " Atlas";
            atlas.material.name = name + " Material";
            atlas.fallbackFontAssetTable = FallbacksOf(name);
            Keep(atlas, AssetPath(name));
        }

        /// <summary>
        /// Saves a built face at <paramref name="path"/>: in place over a face built before, so it keeps its asset and its .meta,
        /// or as a new asset. The title face is saved the same way (InstrumentFontAtlas), since a file copied over it read back stale.
        /// </summary>
        public static void Keep(TMP_FontAsset atlas, string path)
        {
            var name = atlas.name;
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null)
            {
                // A face built before is rebuilt in place, so it keeps its asset and its .meta: its old atlas and material
                // leave it and the new ones join it. Copying a building file over it left the face half loaded (2026-10-31).
                foreach (var part in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                {
                    AssetDatabase.RemoveObjectFromAsset(part);
                    UnityEngine.Object.DestroyImmediate(part, true);
                }
                EditorUtility.CopySerialized(atlas, existing);
                existing.name = name;
                AssetDatabase.AddObjectToAsset(atlas.atlasTexture, existing);
                AssetDatabase.AddObjectToAsset(atlas.material, existing);
                EditorUtility.SetDirty(existing);
                FontAssetsKept.Save();
                existing.ReadFontAssetDefinition();
                UnityEngine.Object.DestroyImmediate(atlas);
                return;
            }
            var building = path.Substring(0, path.Length - ".asset".Length) + " building.asset";
            AssetDatabase.DeleteAsset(building);
            AssetDatabase.CreateAsset(atlas, building);
            AssetDatabase.AddObjectToAsset(atlas.atlasTexture, atlas);
            AssetDatabase.AddObjectToAsset(atlas.material, atlas);
            FontAssetsKept.Save();
            var error = AssetDatabase.MoveAsset(building, path);
            if (!string.IsNullOrEmpty(error)) throw new IOException(error);
        }
    }
}

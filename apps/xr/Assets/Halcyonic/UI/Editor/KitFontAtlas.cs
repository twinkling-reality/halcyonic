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
        /// <summary>
        /// Checks every committed face against what a rebuild makes, writing nothing (the lead's MUST, 2026-11-17T23:0xZ): each face
        /// is built in memory from its font and settings and compared with the committed one, glyph by glyph and byte by byte, then
        /// <see cref="ValidateSaved"/>; for a batch run. A face that differs fails with the difference; <see cref="Rebuild"/> writes.
        /// </summary>
        public static void Check()
        {
            try
            {
                // Every face is tried, so one run names every difference and every glyph a font lacks.
                var errors = new List<string>();
                foreach (var (name, font, _, _) in Faces)
                {
                    try { errors.AddRange(Differences(name, Built(name, font, CharactersOf(name), FallbacksOf(name)), AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath(name)))); }
                    catch (InvalidOperationException error) { errors.Add(error.Message); }
                }
                if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
                ValidateSaved();
                Debug.Log("Halcyonic: kit faces: every committed face is what a rebuild makes.");
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogError("Halcyonic: kit faces: " + error.Message); EditorApplication.Exit(1); }
        }

        /// <summary>Rebuilds every face in place from its font and settings, keeping each asset's file IDs, then checks them; for a batch run.</summary>
        public static void Rebuild()
        {
            try
            {
                var errors = new List<string>();
                foreach (var (name, font, _, _) in Faces)
                {
                    try { Keep(Built(name, font, CharactersOf(name), FallbacksOf(name)), AssetPath(name)); }
                    catch (InvalidOperationException error) { errors.Add(error.Message); }
                }
                if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
                ValidateSaved();
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogError("Halcyonic: kit faces: " + error.Message); EditorApplication.Exit(1); }
        }

        /// <summary>A face built in memory, named and with its fallbacks, from the font at <paramref name="fontPath"/>; nothing is written.</summary>
        public static TMP_FontAsset Built(string name, string fontPath, string characters, List<TMP_FontAsset> fallbacks)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (font == null) throw new InvalidOperationException(name + ": the bundled OFL font is missing.");
            var atlas = StaticAtlas(font, characters, name);
            atlas.name = name;
            atlas.atlasTexture.name = name + " Atlas";
            atlas.material.name = name + " Material";
            atlas.fallbackFontAssetTable = fallbacks;
            return atlas;
        }

        /// <summary>
        /// How a committed face differs from <paramref name="built"/>, the same face built in memory: its atlas, settings, characters,
        /// glyphs, fallbacks and atlas pixels. <paramref name="built"/> is destroyed after.
        /// </summary>
        public static List<string> Differences(string name, TMP_FontAsset built, TMP_FontAsset? saved)
        {
            var found = new List<string>();
            try
            {
                if (saved == null) { found.Add(name + ": no committed face."); return found; }
                void Same<T>(string what, T committed, T rebuilt)
                {
                    if (!EqualityComparer<T>.Default.Equals(committed, rebuilt)) found.Add(name + ": " + what + " is " + committed + " committed, " + rebuilt + " rebuilt.");
                }
                Same("the atlas", (saved.atlasWidth, saved.atlasHeight, saved.atlasPadding, saved.atlasRenderMode), (built.atlasWidth, built.atlasHeight, built.atlasPadding, built.atlasRenderMode));
                Same("the point size", saved.faceInfo.pointSize, built.faceInfo.pointSize);
                Same("the population mode", saved.atlasPopulationMode, built.atlasPopulationMode);
                Same("the fallbacks", string.Join(",", saved.fallbackFontAssetTable.Select(each => each == null ? "none" : each.name)),
                    string.Join(",", built.fallbackFontAssetTable.Select(each => each == null ? "none" : each.name)));
                static string CharacterTable(TMP_FontAsset face) => string.Join(",", face.characterTable.Select(each => each.unicode + ">" + each.glyphIndex).OrderBy(each => each, StringComparer.Ordinal));
                Same("the character table", CharacterTable(saved), CharacterTable(built));
                static string GlyphTable(TMP_FontAsset face) => string.Join(";", face.glyphTable.OrderBy(glyph => glyph.index).Select(glyph =>
                    glyph.index + ":" + glyph.glyphRect.x + "," + glyph.glyphRect.y + "," + glyph.glyphRect.width + "," + glyph.glyphRect.height + ","
                    + glyph.metrics.width + "," + glyph.metrics.height + "," + glyph.metrics.horizontalBearingX + "," + glyph.metrics.horizontalBearingY + ","
                    + glyph.metrics.horizontalAdvance + "," + glyph.scale + "," + glyph.atlasIndex));
                if (GlyphTable(saved) != GlyphTable(built)) found.Add(name + ": its glyphs' places or metrics differ from a rebuild.");
                var committedPixels = saved.atlasTexture.GetRawTextureData<byte>();
                var rebuiltPixels = built.atlasTexture.GetRawTextureData<byte>();
                if (committedPixels.Length != rebuiltPixels.Length) found.Add(name + ": its atlas holds " + committedPixels.Length + " bytes committed, " + rebuiltPixels.Length + " rebuilt.");
                else
                {
                    var differing = 0;
                    for (var index = 0; index < committedPixels.Length; index++) if (committedPixels[index] != rebuiltPixels[index]) differing++;
                    if (differing > 0) found.Add(name + ": " + differing + " of its atlas's " + committedPixels.Length + " bytes differ from a rebuild.");
                }
                return found;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(built.atlasTexture);
                UnityEngine.Object.DestroyImmediate(built.material);
                UnityEngine.Object.DestroyImmediate(built);
            }
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
                // Every material named for the face (its underlays) draws from the face's own atlas, by the file ID it names.
                foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Resources.TrimEnd('/') }))
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (material != null && material.name.StartsWith(name + " ", StringComparison.Ordinal) && material.HasProperty(ShaderUtilities.ID_MainTex)
                        && material.GetTexture(ShaderUtilities.ID_MainTex) != saved.atlasTexture)
                        throw new InvalidOperationException(material.name + " does not draw from " + name + "'s atlas.");
                }
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

        /// <summary>
        /// Saves a built face at <paramref name="path"/>: over a face built before, its atlas's pixels and its material's properties are
        /// copied into the face's own texture and material, so the asset, its .meta and every sub-object's file ID stay as they were
        /// (materials such as KitReading Underlay name the atlas by its file ID; 2026-11-17T23:0xZ); otherwise as a new asset. The title
        /// face is saved the same way (InstrumentFontAtlas), since a file copied over it read back stale.
        /// </summary>
        public static void Keep(TMP_FontAsset atlas, string path)
        {
            var name = atlas.name;
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null)
            {
                // A face built before is rebuilt in place: copying a building file over it left the face half loaded (2026-10-31),
                // and replacing its texture and material gave them new file IDs, which left the underlay materials naming none.
                var parts = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
                var texture = parts.OfType<Texture2D>().SingleOrDefault();
                var material = parts.OfType<Material>().SingleOrDefault();
                if (texture == null || material == null) throw new InvalidOperationException(name + ": the committed face has no single atlas and material to keep.");
                var source = atlas.atlasTexture;
                if (!texture.Reinitialize(source.width, source.height, source.format, false))
                    throw new InvalidOperationException(name + ": its atlas could not take the rebuilt size.");
                texture.LoadRawTextureData(source.GetRawTextureData<byte>());
                texture.Apply(false, false);
                texture.name = name + " Atlas";
                material.shader = atlas.material.shader;
                material.CopyPropertiesFromMaterial(atlas.material);
                material.SetTexture(ShaderUtilities.ID_MainTex, texture);
                material.name = name + " Material";
                EditorUtility.CopySerialized(atlas, existing);
                existing.name = name;
                existing.atlasTextures = new[] { texture };
                existing.material = material;
                EditorUtility.SetDirty(texture);
                EditorUtility.SetDirty(material);
                EditorUtility.SetDirty(existing);
                FontAssetsKept.Save();
                existing.ReadFontAssetDefinition();
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(atlas.material);
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

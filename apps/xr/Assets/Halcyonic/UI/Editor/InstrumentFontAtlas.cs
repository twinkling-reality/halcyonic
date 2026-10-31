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
        private static readonly string Characters = new string(Enumerable.Range(32, 95).Select(value => (char)value).ToArray()) + "…–\u2014‘’“”·";

        public static void Check()
        {
            try
            {
                Build();
                ValidateSaved();
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogError("Halcyonic: instrument type: " + error.Message); EditorApplication.Exit(1); }
        }

        /// <summary>Checks the saved production resource without rebuilding it or exiting the caller.</summary>
        public static void ValidateSaved()
        {
            if (!File.ReadAllBytes("Assets/Halcyonic/UI/Fonts/Rajdhani/OFL.txt").SequenceEqual(
                File.ReadAllBytes("Assets/StreamingAssets/Halcyonic-Licenses/Rajdhani-OFL.txt")))
                throw new InvalidOperationException("The shipped title-font license differs from its source license.");
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
                ValidateIdentity(title);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            Debug.Log("Halcyonic: instrument type: static atlas, required glyphs, fallback and production title passed.");
        }

        private static void ValidateIdentity(TMP_Text title)
        {
            const string name = "<b>Approved</b>\\nTask";
            var runs = GlazeRuns.Of(title);
            var expected = Halcyonic.Client.LineWords.TheirName(name).ForTextMeshPro();
            runs.SetIdentity(name);
            if (title.richText || !title.parseCtrlCharacters || title.text != expected.Text
                || !runs.Theirs.SequenceEqual(expected.Item2) || runs.Theirs.Count == 0)
                throw new InvalidOperationException("Named identity lost its literal text or outside provenance.");
            CheckSlant(false);
            runs.Set(Halcyonic.Client.LineWords.TheirName(name));
            CheckSlant(true);
            runs.SetIdentity(name);
            CheckSlant(false);

            void CheckSlant(bool italic)
            {
                title.ForceMeshUpdate(true);
                var checkedLetters = 0;
                foreach (var character in title.textInfo.characterInfo.Take(title.textInfo.characterCount))
                {
                    if (!character.isVisible) continue;
                    var vertices = title.textInfo.meshInfo[character.materialReferenceIndex].vertices;
                    var bottom = vertices[character.vertexIndex];
                    var top = vertices[character.vertexIndex + 1];
                    if (top.y <= bottom.y) continue;
                    var actual = (top.x - bottom.x) / (top.y - bottom.y);
                    var wanted = italic ? character.fontAsset.italicStyle * 0.01f : 0f;
                    if (Mathf.Abs(actual - wanted) > 0.02f)
                        throw new InvalidOperationException("Identity/prose reuse did not restore the correct mesh treatment.");
                    checkedLetters++;
                }
                if (checkedLetters == 0) throw new InvalidOperationException("Identity check drew no letters.");
            }
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

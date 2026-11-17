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
        private const string FontPath = "Assets/Halcyonic/UI/Fonts/Rajdhani/Rajdhani-Medium.ttf";
        private static readonly string Characters = new string(Enumerable.Range(32, 95).Select(value => (char)value).ToArray()) + "…–\u2014‘’“”"
            + new string(Halcyonic.Client.KitGlyphs.Latin.Where(character => Lacks.IndexOf(character) < 0).ToArray());

        /// <summary>
        /// The accented Latin characters Rajdhani Medium does not draw (the trial rebuild, 2026-11-17T22:20Z): ¤ ª ¬ ² ³ µ ¹ º ¼ ½ ¾
        /// Ĉ ĉ Ĥ ĸ ſ. They draw in <see cref="LacksFace"/>, the title face's first fallback, never as a missing glyph's box.
        /// </summary>
        public const string Lacks = "\u00A4\u00AA\u00AC\u00B2\u00B3\u00B5\u00B9\u00BA\u00BC\u00BD\u00BE\u0108\u0109\u0124\u0138\u017F";

        /// <summary>The kit's title face, Inter SemiBold, which the title face falls back to for <see cref="Lacks"/>.</summary>
        public const string LacksFace = "KitTitle";

        private static List<TMP_FontAsset> Fallbacks() =>
            new List<TMP_FontAsset> { AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KitFontAtlas.AssetPath(LacksFace)), TMP_Settings.defaultFontAsset };

        /// <summary>
        /// Checks the committed title face against what a rebuild makes, writing nothing (the lead's MUST, 2026-11-17T23:0xZ), then
        /// <see cref="ValidateSaved"/>; for a batch run. <see cref="Rebuild"/> writes.
        /// </summary>
        public static void Check()
        {
            try
            {
                var found = KitFontAtlas.Differences("InstrumentType", Built(), AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath));
                if (found.Count > 0) throw new InvalidOperationException(string.Join(" ", found));
                ValidateSaved();
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogError("Halcyonic: instrument type: " + error.Message); EditorApplication.Exit(1); }
        }

        /// <summary>Rebuilds the title face in place, keeping its file IDs, then checks it; for a batch run.</summary>
        public static void Rebuild()
        {
            try
            {
                // In place, as the kit's faces are: a building file copied over the face read back without its new glyphs (2026-11-17T22:31Z).
                KitFontAtlas.Keep(Built(), AssetPath);
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
            if (saved == null || saved.atlasPopulationMode != AtlasPopulationMode.Static || saved.sourceFontFile != null)
                throw new InvalidOperationException("The title face is not a static atlas of its own.");
            if (!saved.HasCharacters(Characters, out List<char> missing))
                throw new InvalidOperationException("The title face lacks " + string.Join(", ", missing.Select(character => "U+" + ((int)character).ToString("X4"))) + ".");
            if (!saved.fallbackFontAssetTable.SequenceEqual(Fallbacks()) || !saved.fallbackFontAssetTable[0].HasCharacters(Lacks))
                throw new InvalidOperationException("The title face's fallbacks are not " + LacksFace + " holding what Rajdhani lacks, then the reading default.");
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

        /// <summary>The title face built in memory with the kit's sizes, settings and font check (KitFontAtlas.Built); nothing is written.</summary>
        private static TMP_FontAsset Built() => KitFontAtlas.Built("InstrumentType", FontPath, Characters, Fallbacks());
    }
}

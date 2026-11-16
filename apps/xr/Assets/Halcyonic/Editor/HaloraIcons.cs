#nullable enable
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace Halcyonic.XR.Editor
{
    /// <summary>
    /// The Quest launcher's icon, Halora's (the owner's yes to the kingfisher, 2026-11-16 14:3xZ; docs BRAND): the approved dawn-over-the-sea
    /// ground cropped square with the cream kingfisher centred, one PNG per Android density, made by <c>tools/halora-icon/icon.html</c> from the
    /// ground raster the board was drawn with (never re-rendered: the ground's noise differs by GPU). Run in batch mode: it sets the importer of
    /// each PNG, puts each on its own density of the legacy, round and adaptive kinds (the bird inside the adaptive safe zone), reads every
    /// density back, and quits non-zero if any is missing or of another size.
    /// </summary>
    public static class HaloraIcons
    {
        private const string Folder = "Assets/Halcyonic/Brand/Icon/";

        public static void Apply()
        {
            var failures = new List<string>();
            Texture2D? Load(string name)
            {
                var path = Folder + name + ".png";
                if (AssetImporter.GetAtPath(path) is TextureImporter importer)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.mipmapEnabled = false;
                    importer.alphaIsTransparency = true;
                    importer.sRGBTexture = true;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.isReadable = false;
                    importer.SaveAndReimport();
                }
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null) failures.Add("no texture at " + path);
                return texture;
            }

            void Set(PlatformIconKind kind, string front, string? back)
            {
                var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                foreach (var icon in icons)
                {
                    var size = icon.width;
                    if (back == null) icon.SetTexture(Load(front + "-" + size));
                    else icon.SetTextures(Load(back + "-" + size), Load(front + "-" + size));
                }
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
            }

            AssetDatabase.Refresh();
            Set(AndroidPlatformIconKind.Legacy, "legacy", null);
            Set(AndroidPlatformIconKind.Round, "round", null);
            Set(AndroidPlatformIconKind.Adaptive, "fore", "back");
            AssetDatabase.SaveAssets();

            // Every density of every kind reads back with its own PNG, the size it was made at.
            foreach (var kind in new PlatformIconKind[] { AndroidPlatformIconKind.Legacy, AndroidPlatformIconKind.Round, AndroidPlatformIconKind.Adaptive })
            {
                foreach (var icon in PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind))
                {
                    var textures = icon.GetTextures();
                    var wanted = kind == AndroidPlatformIconKind.Adaptive ? 2 : 1;
                    if (textures.Length < wanted || textures.Take(wanted).Any(texture => texture == null))
                        failures.Add(kind + " " + icon.width + " has no texture");
                    else if (textures.Take(wanted).Any(texture => texture.width != icon.width || texture.height != icon.height))
                        failures.Add(kind + " " + icon.width + " carries a texture of another size");
                    else Debug.Log("Halcyonic: Halora icon: " + kind + " " + icon.width + " px set from " + string.Join(", ", textures.Take(wanted).Select(texture => texture.name)));
                }
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: Halora icon: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: Halora icon: every density set.");
            if (Application.isBatchMode) EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }
    }
}

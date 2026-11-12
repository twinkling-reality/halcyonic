#nullable enable
using System.IO;
using UnityEditor;

namespace Halcyonic.XR.World.Editor
{
    /// <summary>
    /// Imports a scene's pictures (under <c>Resources/HalcyonicWorld/Scenes/&lt;scene&gt;</c>, ADR 0049 decision 9) so the headset
    /// mixes them sharp and in budget. Each light's layer, the places' and the air: full 8192 width (about 22 pixels per
    /// degree, near the panel's 25), mipmaps for the far land, wrapping round but not over the poles, read as stored (a
    /// log curve, decoded by the mix), ASTC 6x6 on Android. The view transform: a small table in sRGB, read exactly,
    /// uncompressed, no mipmaps, clamped. The depth beside them is a plain bytes file.
    /// </summary>
    public sealed class WorldPictureImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/HalcyonicWorld/Scenes/")) return;
            var name = Path.GetFileNameWithoutExtension(assetPath);
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            if (name == "view")
            {
                importer.sRGBTexture = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
                importer.filterMode = UnityEngine.FilterMode.Bilinear;
                importer.anisoLevel = 0;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                {
                    name = "Android",
                    overridden = true,
                    maxTextureSize = 2048,
                    format = TextureImporterFormat.RGBA32,
                });
                return;
            }
            importer.sRGBTexture = false;
            importer.mipmapEnabled = true;
            importer.wrapModeU = UnityEngine.TextureWrapMode.Repeat;
            importer.wrapModeV = UnityEngine.TextureWrapMode.Clamp;
            importer.filterMode = UnityEngine.FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 8192;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android",
                overridden = true,
                maxTextureSize = 8192,
                format = TextureImporterFormat.ASTC_6x6,
            });
        }
    }
}

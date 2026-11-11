#nullable enable
using UnityEditor;

namespace Halcyonic.XR.World.Editor
{
    /// <summary>
    /// Imports a person's world pictures (under <c>Resources/HalcyonicWorld/Pictures</c>) so the headset shows them
    /// sharp and in budget: full 8192 width (about 22 pixels per degree, near the panel's 25), mipmaps for the far
    /// land, wrapping round but not over the poles, sRGB, and ASTC 6x6 on Android (about 15 MB each with mips, against
    /// 128 MB uncompressed). The depth beside them is a plain bytes file.
    /// </summary>
    public sealed class WorldPictureImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/HalcyonicWorld/Pictures/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapModeU = UnityEngine.TextureWrapMode.Repeat;
            importer.wrapModeV = UnityEngine.TextureWrapMode.Clamp;
            importer.filterMode = UnityEngine.FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 8192;
            importer.npotScale = TextureImporterNPOTScale.None;
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

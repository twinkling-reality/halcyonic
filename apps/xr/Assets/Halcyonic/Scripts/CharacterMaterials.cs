#nullable enable
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>
    /// The materials characters render with, and the shader globals they share.
    /// </summary>
    /// <remarks>
    /// A player build leaves out every shader that nothing in the build references, and the first
    /// device build rendered characters magenta for that reason. The character shaders therefore
    /// ship through materials in a Resources folder (Assets/Halcyonic/Characters/Resources):
    /// everything under Resources is in the build, the materials reference their shaders, and the
    /// build compiles exactly the variants those materials use. That keeps the project's Always
    /// Included Shaders list, which compiles every variant for every build, unchanged.
    /// </remarks>
    internal static class CharacterMaterials
    {
        private const string Folder = "HalcyonicCharacters/";

        public static readonly int NoiseId = Shader.PropertyToID("_HalcyonicNoise");
        public static readonly int KeyLightId = Shader.PropertyToID("_HalcyonicKeyLight");

        private static Material? body;
        private static Material? halo;
        private static Material? ring;
        private static Texture2D? noise;

        /// <summary>The glossy body with its eyes and surface effects (Halcyonic/Character Body).</summary>
        public static Material Body => Load(ref body, "Body");

        /// <summary>The glow behind a character (Halcyonic/Soft Shape, disc profile).</summary>
        public static Material Halo => Load(ref halo, "Halo");

        /// <summary>The ring that sweeps around a character running tests (Halcyonic/Soft Shape, band profile).</summary>
        public static Material Ring => Load(ref ring, "Ring");

        /// <summary>Bakes the noise texture and publishes the shader globals, once. Takes a few milliseconds.</summary>
        public static void Prepare()
        {
            if (noise != null) return;
            noise = CharacterNoise.Create();
            Shader.SetGlobalTexture(NoiseId, noise);
            // Until the stage is placed: from above, to the left and behind the person.
            SetKeyLight(Quaternion.identity);
        }

        /// <summary>
        /// Points the key light from above the person's left shoulder, in the frame the stage faces,
        /// so every character is lit from the same side as the person sees it.
        /// </summary>
        public static void SetKeyLight(Quaternion stage)
        {
            Shader.SetGlobalVector(KeyLightId, stage * new Vector3(-0.45f, 0.75f, -0.55f).normalized);
        }

        private static Material Load(ref Material? cache, string name)
        {
            if (cache != null) return cache;
            cache = Resources.Load<Material>(Folder + name);
            if (cache == null)
            {
                // Loudly wrong rather than invisible: Unity's error shader draws magenta.
                Debug.LogError("Halcyonic: the material Resources/" + Folder + name + " is missing, so characters cannot render. It ships the character shaders into player builds.");
                var fallback = Shader.Find("Hidden/InternalErrorShader");
                if (fallback == null) fallback = Shader.Find("Legacy Shaders/Diffuse");
                cache = new Material(fallback);
            }
            return cache;
        }

        /// <summary>Forgets cached objects when play mode starts without a domain reload.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            body = null;
            halo = null;
            ring = null;
            noise = null;
        }
    }
}

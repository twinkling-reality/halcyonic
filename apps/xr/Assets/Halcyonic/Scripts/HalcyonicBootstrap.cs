#nullable enable
using UnityEngine;

namespace Halcyonic.XR
{
    internal static class HalcyonicBootstrap
    {
        /// <summary>Adds the stage to any scene that lacks one, so no scene file has to carry it.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateStage()
        {
            if (Object.FindAnyObjectByType<CharacterStage>() != null) return;
            var root = new GameObject("Halcyonic");
            root.AddComponent<ControlPlaneConnection>();
            root.AddComponent<CharacterStage>();
            root.AddComponent<FocusGuard>();
            Object.DontDestroyOnLoad(root);
        }
    }
}

#nullable enable
using UnityEngine;

namespace Halcyonic.XR.World
{
    /// <summary>
    /// Adds the person's world to the scene at runtime, from this assembly, so that neither the stage,
    /// the room nor the scene has to know about it: <see cref="WorldView"/> finds the room's placement
    /// by itself and shows only while the virtual space does.
    /// </summary>
    public static class WorldBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin()
        {
            // Until the owner has judged it on the headset, only development builds and the editor draw it:
            // a release build's Virtual Space stays as it was.
            if (!Debug.isDebugBuild && !Application.isEditor) return;
            var world = new GameObject("Halcyonic world");
            Object.DontDestroyOnLoad(world);
            world.AddComponent<WorldView>();
        }
    }
}

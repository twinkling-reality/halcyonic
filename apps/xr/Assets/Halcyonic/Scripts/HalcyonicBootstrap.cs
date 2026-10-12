#nullable enable
using UnityEngine;

namespace Halcyonic.XR
{
    public static class HalcyonicBootstrap
    {
        /// <summary>Adds the connection, focus and field measures without requiring optional agents.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateStage()
        {
            if (EnsureStage() is GameObject made) Object.DontDestroyOnLoad(made);
        }

        /// <summary>Ensures one runtime root. Workspace bootstrap attaches the normal workspace independently.</summary>
        /// <returns>The root made, or null where a connection already exists.</returns>
        public static GameObject? EnsureStage()
        {
            var stage = Object.FindAnyObjectByType<ControlPlaneConnection>();
            if (stage == null)
            {
                var root = new GameObject("Halcyonic");
                root.AddComponent<ControlPlaneConnection>();
                root.AddComponent<FocusGuard>();
                root.AddComponent<DeviceMeasures>();
                return root;
            }
            if (Object.FindAnyObjectByType<DeviceMeasures>() == null) stage.gameObject.AddComponent<DeviceMeasures>();
            return null;
        }
    }
}

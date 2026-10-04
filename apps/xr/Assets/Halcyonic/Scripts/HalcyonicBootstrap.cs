#nullable enable
using UnityEngine;

namespace Halcyonic.XR
{
    public static class HalcyonicBootstrap
    {
        /// <summary>Adds the stage to any scene that lacks one, so no scene file has to carry it.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateStage()
        {
            if (EnsureStage() is GameObject made) Object.DontDestroyOnLoad(made);
        }

        /// <summary>
        /// The stage and what runs beside it, exactly once whatever the scene carries: in a scene with no
        /// stage, a stage of its own with the control plane's connection, the focus guard and the device's
        /// measures, which it returns; in one that carries the stage, as Stage.unity carries the stage,
        /// its connection and focus guard, the device's measures added beside the stage, as no scene file
        /// carries them. Without them the headset's field of view is never measured, so the layout never
        /// keeps to it (<see cref="DeviceMeasures"/>).
        /// </summary>
        /// <returns>The stage made, or null where the scene carried one.</returns>
        public static GameObject? EnsureStage()
        {
            var stage = Object.FindAnyObjectByType<CharacterStage>();
            if (stage == null)
            {
                var root = new GameObject("Halcyonic");
                root.AddComponent<ControlPlaneConnection>();
                root.AddComponent<CharacterStage>();
                root.AddComponent<FocusGuard>();
                root.AddComponent<DeviceMeasures>();
                return root;
            }
            if (Object.FindAnyObjectByType<DeviceMeasures>() == null) stage.gameObject.AddComponent<DeviceMeasures>();
            return null;
        }
    }
}

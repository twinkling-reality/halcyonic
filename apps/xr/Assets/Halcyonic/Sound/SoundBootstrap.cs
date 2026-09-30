#nullable enable
using UnityEngine;

namespace Halcyonic.XR.Sound
{
    /// <summary>
    /// Adds <see cref="StageSound"/> to the stage object at runtime, from this assembly, so that neither
    /// the stage nor the scene has to know about sound. A scene without an audio listener has no one
    /// to hear it, so it gets none.
    /// </summary>
    /// <remarks>
    /// <see cref="HalcyonicBootstrap"/> creates a stage in scenes that lack one, after scene load like
    /// this, in an order Unity does not define; so this waits a few frames for a stage to exist.
    /// </remarks>
    public sealed class SoundBootstrap : MonoBehaviour
    {
        private const int MaxFrames = 120;

        private int frames;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin()
        {
            new GameObject("Halcyonic sound bootstrap").AddComponent<SoundBootstrap>();
        }

        private void Update()
        {
            var stage = FindAnyObjectByType<CharacterStage>();
            if (stage == null && ++frames < MaxFrames) return;
            if (stage != null && stage.GetComponent<StageSound>() == null)
            {
                if (FindAnyObjectByType<AudioListener>() != null) stage.gameObject.AddComponent<StageSound>();
                else Debug.Log("Halcyonic: sound off, because the scene has no audio listener.");
            }
            Destroy(gameObject);
        }
    }
}

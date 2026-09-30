#nullable enable
using UnityEngine;

namespace Halcyonic.XR.Room
{
    /// <summary>
    /// Adds <see cref="RoomPlacement"/> to the stage object at runtime, from this assembly, so that
    /// neither the stage nor the scene has to know about the room: the stage looks for an
    /// <see cref="IStagePlacementSource"/> on itself once a second. A scene without Meta's camera rig
    /// has no passthrough or scene model to use, so it gets none.
    /// </summary>
    /// <remarks>
    /// <see cref="HalcyonicBootstrap"/> creates a stage in scenes that lack one, after scene load
    /// like this, in an order Unity does not define; so this waits a few frames for a stage to exist.
    /// </remarks>
    public sealed class RoomBootstrap : MonoBehaviour
    {
        private const int MaxFrames = 120;

        private int frames;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin()
        {
            new GameObject("Halcyonic room bootstrap").AddComponent<RoomBootstrap>();
        }

        private void Update()
        {
            var stage = FindAnyObjectByType<CharacterStage>();
            if (stage == null && ++frames < MaxFrames) return;
            if (stage != null && stage.GetComponent<RoomPlacement>() == null)
            {
                if (FindAnyObjectByType<OVRCameraRig>() != null) stage.gameObject.AddComponent<RoomPlacement>();
                else Debug.Log("Halcyonic: room placement off, because the scene has no camera rig for passthrough and the scene model.");
            }
            Destroy(gameObject);
        }
    }
}

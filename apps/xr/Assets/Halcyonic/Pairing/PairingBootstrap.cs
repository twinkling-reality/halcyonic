#nullable enable
using UnityEngine;

namespace Halcyonic.XR.Pairing
{
    /// <summary>
    /// Adds <see cref="PairingPanel"/> to the stage object at runtime, from this assembly, so that
    /// neither the stage nor the scene has to know about pairing (ADR 0017). Every build gets it, a
    /// release build too: without pairing a release build can only show the demonstration.
    /// </summary>
    /// <remarks>
    /// <see cref="HalcyonicBootstrap"/> creates a stage in scenes that lack one, after scene load like
    /// this, in an order Unity does not define; so this waits a few frames for a stage to exist.
    /// </remarks>
    public sealed class PairingBootstrap : MonoBehaviour
    {
        private const int MaxFrames = 120;

        private int frames;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin()
        {
            new GameObject("Halcyonic pairing bootstrap").AddComponent<PairingBootstrap>();
        }

        private void Update()
        {
            var connection = FindAnyObjectByType<ControlPlaneConnection>();
            if (connection == null && ++frames < MaxFrames) return;
            if (connection != null && connection.GetComponent<PairingPanel>() == null)
            {
                connection.gameObject.AddComponent<PairingPanel>();
            }
            Destroy(gameObject);
        }
    }
}

#nullable enable
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// One eased move of the spatial kit between two laid-out states, as the switch's Compact or a task's row asks
    /// and the owner confirms: what the person leaves travels to where the next state stands and takes its size, over
    /// one slide, and the next state stands whole at the end. Nothing in between is read. While things are kept still
    /// the move is over before it starts: the end state, at once.
    /// </summary>
    public static class KitTransition
    {
        /// <summary>How far through a move that began <paramref name="elapsed"/> seconds ago, eased; 1 while things are kept still.</summary>
        public static float Progress(float elapsed) => GlazeMotion.Still ? 1f : Glaze.EaseInOut(Mathf.Clamp01(elapsed / Glaze.SlideSeconds));

        /// <summary>
        /// Stands <paramref name="moving"/> <paramref name="t"/> of the way from where it stood (<paramref name="from"/>)
        /// to the next state's pose (<paramref name="to"/>), its scale uniform, so its shapes keep their own proportions.
        /// </summary>
        public static void Pose(Transform moving, Pose from, float fromScale, Pose to, float toScale, float t)
        {
            moving.SetPositionAndRotation(Vector3.Lerp(from.position, to.position, t), Quaternion.Slerp(from.rotation, to.rotation, t));
            moving.localScale = Vector3.one * Mathf.Lerp(fromScale, toScale, t);
        }
    }
}

#nullable enable
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>
    /// Focus awareness, required by Meta (VRC.Quest.Input.4): when the app loses input focus it keeps
    /// rendering, hides hand and controller visuals, and ignores their input. Hand visuals are
    /// assigned in the editor once the scene has a rig; interactive components check
    /// <see cref="InputSuspended"/>. Whether Unity reports the Quest system menu as a focus change
    /// under OpenXR must be verified in the Simulator and on a device.
    /// </summary>
    public sealed class FocusGuard : MonoBehaviour
    {
        [SerializeField] private GameObject[] handVisuals = new GameObject[0];

        public static bool InputSuspended { get; private set; }

        private void OnApplicationFocus(bool hasFocus)
        {
            InputSuspended = !hasFocus;
            foreach (var visual in handVisuals)
            {
                if (visual != null) visual.SetActive(hasFocus);
            }
        }
    }
}

#nullable enable
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The redesign draws in every build, release included (ON-1b, 2026-11-11, once every moment of the judges' walk had its
    /// reader), and in the editor, its render checks and journeys included. It lives apart from <see cref="CapturePresets"/>,
    /// which compiles only into development builds and the editor. A frame no reader of the redesign takes still falls back to
    /// the older views until ON-2 removes them; the old kit's own render states turn it off inside themselves.
    /// </summary>
    internal static class RoomDesignDefault
    {
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Enable() => RoomDesign.Enabled = true;
    }
}

#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.Room
{
    /// <summary>
    /// The headset's side of the person's real room (<see cref="RoomPassthrough"/>): the Meta XR Core
    /// SDK's passthrough, started through <see cref="OVRManager.isInsightPassthroughEnabled"/>, drawn by
    /// an <see cref="OVRPassthroughLayer"/> underlay, with the rig's cameras cleared to transparent black
    /// while it runs so the room shows wherever nothing is drawn. Off, the cameras get their own
    /// background back. It asks for the boundary hidden or shown only in development builds and the
    /// editor, and keeps <see cref="OVRManager.shouldBoundaryVisibilityBeSuppressed"/> in step, since
    /// OVRManager otherwise asks the other way on its next frame.
    /// </summary>
    /// <remarks>
    /// The project declares passthrough supported, not required (<c>OculusProjectConfig</c>), so the app
    /// also installs where it is missing. It declares no boundary visibility support: only development
    /// APKs carry the permission, added by <c>BoundaryInDevelopmentBuilds</c>.
    /// </remarks>
    internal sealed class PassthroughView : IPassthroughDevice
    {
        private readonly GameObject host;
        private readonly List<(Camera Camera, CameraClearFlags Flags, Color Background)> cleared =
            new List<(Camera, CameraClearFlags, Color)>();

        private OVRPassthroughLayer? layer;

        public PassthroughView(GameObject host)
        {
            this.host = host;
        }

        public bool Initialized => OVRManager.IsInsightPassthroughInitialized();

        public bool InitFailed => OVRManager.HasInsightPassthroughInitFailed();

        public bool LayerShown => layer != null && layer.enabled && !layer.hidden;

        public string? CannotStart()
        {
            if (OVRManager.instance == null) return "the scene has no OVRManager";
            if (!OVRManager.IsInsightPassthroughSupported()) return "this headset does not support it";
            if (OVRManager.HasInsightPassthroughInitFailed()) return "it failed to start";
            return null;
        }

        public void TurnOn()
        {
            OVRManager.instance.isInsightPassthroughEnabled = true;
            if (layer == null) layer = host.AddComponent<OVRPassthroughLayer>();
            layer.hidden = false;
            layer.enabled = true;
            ClearCameras();
        }

        public void TurnOff()
        {
            var manager = OVRManager.instance;
            if (manager != null) manager.isInsightPassthroughEnabled = false;
            if (layer != null) layer.hidden = true;
            RestoreCameras();
        }

        public BoundaryAnswer AskBoundary(bool hidden)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var manager = OVRManager.instance;
            if (manager != null) manager.shouldBoundaryVisibilityBeSuppressed = hidden;
            var result = OVRPlugin.RequestBoundaryVisibility(hidden ? OVRPlugin.BoundaryVisibility.Suppressed : OVRPlugin.BoundaryVisibility.NotSuppressed);
            return result switch
            {
                OVRPlugin.Result.Success => BoundaryAnswer.Done,
                OVRPlugin.Result.Warning_BoundaryVisibilitySuppressionNotAllowed => BoundaryAnswer.NotAllowed,
                OVRPlugin.Result.Failure_Unsupported or OVRPlugin.Result.Failure_NotYetImplemented => BoundaryAnswer.Unsupported,
                _ => BoundaryAnswer.Failed,
            };
#else
            return BoundaryAnswer.Unsupported;
#endif
        }

        /// <summary>
        /// Clears the rig's cameras to transparent black, which the compositor fills with passthrough.
        /// With one camera for both eyes, as the rig here has, the eye cameras are the center camera.
        /// </summary>
        private void ClearCameras()
        {
            if (cleared.Count > 0) return;
            var cameras = new HashSet<Camera>();
            var rig = Object.FindAnyObjectByType<OVRCameraRig>();
            if (rig != null)
            {
                if (rig.leftEyeCamera != null) cameras.Add(rig.leftEyeCamera);
                if (rig.rightEyeCamera != null) cameras.Add(rig.rightEyeCamera);
            }
            if (Camera.main != null) cameras.Add(Camera.main);
            foreach (var camera in cameras)
            {
                cleared.Add((camera, camera.clearFlags, camera.backgroundColor));
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
            }
        }

        private void RestoreCameras()
        {
            foreach (var (camera, flags, background) in cleared)
            {
                if (camera == null) continue;
                camera.clearFlags = flags;
                camera.backgroundColor = background;
            }
            cleared.Clear();
        }
    }
}

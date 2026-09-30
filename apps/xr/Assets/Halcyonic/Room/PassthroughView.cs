#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.Room
{
    /// <summary>
    /// The person's real room behind the scene: the Meta XR Core SDK's passthrough, started through
    /// <see cref="OVRManager.isInsightPassthroughEnabled"/>, drawn by an <see cref="OVRPassthroughLayer"/>
    /// underlay, with the rig's cameras cleared to transparent black while it runs so the room shows
    /// wherever nothing is drawn. Off, the cameras get their own background back.
    /// </summary>
    /// <remarks>
    /// Passthrough is unavailable when the headset does not support it, when it fails to start, or
    /// when it has not started within <see cref="StartSeconds"/>; the room placement then shows the
    /// virtual space. The project declares passthrough supported, not required
    /// (<c>OculusProjectConfig</c>), so the app also installs where it is missing.
    /// </remarks>
    internal sealed class PassthroughView
    {
        private const float StartSeconds = 6f;

        private readonly GameObject host;
        private readonly List<(Camera Camera, CameraClearFlags Flags, Color Background)> cleared =
            new List<(Camera, CameraClearFlags, Color)>();

        private OVRPassthroughLayer? layer;
        private float startedAt;

        public PassthroughView(GameObject host)
        {
            this.host = host;
        }

        public PassthroughState State { get; private set; } = PassthroughState.Off;

        /// <summary>Why passthrough is unavailable, for the log.</summary>
        public string? Problem { get; private set; }

        /// <summary>Starts passthrough, unless it is unavailable.</summary>
        public void Start(float now)
        {
            if (State is PassthroughState.Starting or PassthroughState.Running or PassthroughState.Unavailable) return;
            var manager = OVRManager.instance;
            if (manager == null)
            {
                MarkUnavailable("the scene has no OVRManager");
                return;
            }
            if (!OVRManager.IsInsightPassthroughSupported())
            {
                MarkUnavailable("this headset does not support it");
                return;
            }
            if (OVRManager.HasInsightPassthroughInitFailed())
            {
                MarkUnavailable("it failed to start");
                return;
            }
            manager.isInsightPassthroughEnabled = true;
            if (layer == null) layer = host.AddComponent<OVRPassthroughLayer>();
            layer.hidden = false;
            layer.enabled = true;
            ClearCameras();
            startedAt = now;
            State = PassthroughState.Starting;
        }

        /// <summary>Stops passthrough and gives the cameras their background back.</summary>
        public void Stop()
        {
            if (State is PassthroughState.Off or PassthroughState.Unavailable) return;
            Shutdown();
            State = PassthroughState.Off;
        }

        /// <summary>Follows passthrough's start once a frame; returns the state.</summary>
        public PassthroughState Poll(float now)
        {
            switch (State)
            {
                case PassthroughState.Starting when OVRManager.IsInsightPassthroughInitialized():
                    State = PassthroughState.Running;
                    break;
                case PassthroughState.Starting when OVRManager.HasInsightPassthroughInitFailed():
                    MarkUnavailable("it failed to start");
                    break;
                case PassthroughState.Starting when now - startedAt > StartSeconds:
                    MarkUnavailable("it did not start within " + StartSeconds + " seconds");
                    break;
                case PassthroughState.Running when OVRManager.HasInsightPassthroughInitFailed():
                    MarkUnavailable("it stopped working");
                    break;
            }
            return State;
        }

        private void MarkUnavailable(string problem)
        {
            Shutdown();
            Problem = problem;
            State = PassthroughState.Unavailable;
        }

        private void Shutdown()
        {
            var manager = OVRManager.instance;
            if (manager != null) manager.isInsightPassthroughEnabled = false;
            if (layer != null) layer.hidden = true;
            RestoreCameras();
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

#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Halcyonic.XR
{
    /// <summary>
    /// Decides when the stage should be placed in front of the person again: when the session
    /// starts, when the tracking origin changes (a recenter or a new boundary), when the app
    /// resumes, and when the head moves farther in one frame than a person can, which only a
    /// changed tracking space explains. Uses only Unity's XR input API.
    /// </summary>
    /// <remarks>
    /// Unity's OpenXR package forwards the runtime's reference space changes, a recenter above all,
    /// to <see cref="XRInputSubsystem.trackingOriginUpdated"/>. One recenter can raise several of
    /// them over a few frames, and poses are not final when the first arrives, so a request waits
    /// until requests have stopped for a moment and the head is tracked.
    /// </remarks>
    internal sealed class PersonPlacement
    {
        /// <summary>How long requests must have stopped before placing, so repeated events coalesce.</summary>
        private const float SettleSeconds = 0.3f;

        /// <summary>How long to wait for head tracking before placing from whatever pose there is.</summary>
        private const float TrackingTimeoutSeconds = 3f;

        private const float JumpMeters = 0.5f;
        private const float JumpDegrees = 45f;

        private readonly List<XRInputSubsystem> found = new List<XRInputSubsystem>();
        private readonly List<XRInputSubsystem> subscribed = new List<XRInputSubsystem>();
        private string? reason = "the session started";
        private float requestedAt = float.NegativeInfinity;
        private float lastRequest = float.NegativeInfinity;
        private float nextSubsystemSearch;
        private volatile bool originChanged;
        private bool headSeen;
        private Vector3 lastHeadPosition;
        private float lastHeadYaw;

        /// <summary>Asks for the stage to be placed again, for the reason given.</summary>
        public void Request(string why, float now)
        {
            if (reason == null)
            {
                reason = why;
                requestedAt = now;
            }
            lastRequest = now;
        }

        /// <summary>
        /// Called once a frame after the head has moved. Returns why the stage should be placed now,
        /// or null.
        /// </summary>
        public string? Poll(Transform? head, float now, float deltaTime)
        {
            if (float.IsNegativeInfinity(requestedAt)) requestedAt = now;
            if (float.IsNegativeInfinity(lastRequest)) lastRequest = now;
            if (now >= nextSubsystemSearch) Subscribe(now);
            if (originChanged)
            {
                originChanged = false;
                Request("the tracking origin changed", now);
            }
            WatchForJumps(head, now, deltaTime);

            if (reason == null || now - lastRequest < SettleSeconds) return null;
            if (!HeadTracked() && now - requestedAt < TrackingTimeoutSeconds) return null;
            var why = reason;
            reason = null;
            return why;
        }

        public void Stop()
        {
            foreach (var subsystem in subscribed) subsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
            subscribed.Clear();
            nextSubsystemSearch = 0f;
        }

        private void Subscribe(float now)
        {
            // XR subsystems can start after the scene, so keep looking, once a second.
            nextSubsystemSearch = now + 1f;
            SubsystemManager.GetSubsystems(found);
            foreach (var subsystem in found)
            {
                if (subscribed.Contains(subsystem)) continue;
                subsystem.trackingOriginUpdated += OnTrackingOriginUpdated;
                subscribed.Add(subsystem);
            }
        }

        // Only sets a flag: the event's thread is not documented, and Poll acts on it.
        private void OnTrackingOriginUpdated(XRInputSubsystem subsystem) => originChanged = true;

        private void WatchForJumps(Transform? head, float now, float deltaTime)
        {
            if (head == null)
            {
                headSeen = false;
                return;
            }
            var position = head.position;
            var yaw = head.eulerAngles.y;
            // A long frame, such as after a pause, can hold real movement; only judge short ones.
            if (headSeen && deltaTime > 0f && deltaTime < 0.1f)
            {
                var moved = Vector3.Distance(position, lastHeadPosition);
                var turned = Mathf.Abs(Mathf.DeltaAngle(lastHeadYaw, yaw));
                if (moved > JumpMeters || turned > JumpDegrees) Request("the tracking space moved", now);
            }
            headSeen = true;
            lastHeadPosition = position;
            lastHeadYaw = yaw;
        }

        private static bool HeadTracked()
        {
            // Without a headset the camera's pose is all there is.
            if (!XRSettings.isDeviceActive) return true;
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid) return false;
            // A headset that does not report whether it is tracked is taken as tracked.
            return !head.TryGetFeatureValue(CommonUsages.isTracked, out var tracked) || tracked;
        }
    }
}

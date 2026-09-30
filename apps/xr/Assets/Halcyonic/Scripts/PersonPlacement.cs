#nullable enable
using System.Collections.Generic;
using System.Threading;
using Halcyonic.Client;
using UnityEngine;
using UnityEngine.XR;

namespace Halcyonic.XR
{
    /// <summary>
    /// Feeds the stage's placement decisions (<see cref="InFrontPlacement"/> in the client core) from
    /// Unity: the runtime's reference space changes, which Unity's OpenXR package forwards to
    /// <see cref="XRInputSubsystem.trackingOriginUpdated"/>, the head's pose each frame, and whether
    /// the head is tracked. Uses only Unity's XR input API.
    /// </summary>
    internal sealed class PersonPlacement
    {
        private readonly InFrontPlacement rules = new InFrontPlacement();
        private readonly List<XRInputSubsystem> found = new List<XRInputSubsystem>();
        private readonly List<XRInputSubsystem> subscribed = new List<XRInputSubsystem>();
        private float nextSubsystemSearch;
        private int originChanges;

        /// <summary>The app gained or lost input focus.</summary>
        public void FocusChanged(float now) => rules.FocusChanged(now);

        /// <summary>The app paused or resumed.</summary>
        public void Paused(bool paused, float now) => rules.Paused(paused, now);

        /// <summary>Called once a frame after the head has moved. Returns what the stage should do now.</summary>
        public PlacementDecision Poll(Transform? head, float now, float deltaTime)
        {
            if (now >= nextSubsystemSearch) Subscribe(now);
            if (Interlocked.Exchange(ref originChanges, 0) > 0) rules.OriginChanged(now);
            HeadSample? sample = null;
            if (head != null)
            {
                var position = head.position;
                sample = new HeadSample(new System.Numerics.Vector3(position.x, position.y, position.z), head.eulerAngles.y);
            }
            return rules.Poll(now, deltaTime, sample, HeadTracked());
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

        // Only counts: the event's thread is not documented, and Poll acts on it.
        private void OnTrackingOriginUpdated(XRInputSubsystem subsystem) => Interlocked.Increment(ref originChanges);

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

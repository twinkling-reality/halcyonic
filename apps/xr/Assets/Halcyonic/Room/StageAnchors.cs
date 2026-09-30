#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Halcyonic.XR.Room
{
    /// <summary>
    /// The stage's spatial anchor, through the Meta XR Core SDK's <see cref="OVRSpatialAnchor"/>:
    /// created where the stage stands and saved to the headset, loaded and localized by its UUID in a
    /// later session, and erased when it no longer keeps a place. Every operation reports what
    /// happened in words for the log, and none throws: anchors are an improvement, never a
    /// requirement.
    /// </summary>
    /// <remarks>
    /// An anchor saved in one room does not localize in another, and one that the headset lost,
    /// for example after its spaces were cleared, is not found. Both simply fail here, and the room
    /// placement chooses again.
    /// </remarks>
    internal static class StageAnchors
    {
        /// <summary>
        /// Creates an anchor at <paramref name="pose"/>, waits until it is created and localized, and
        /// saves it. Returns the anchor, whether it was saved, and what happened. Without an anchor
        /// the placement holds for this session only.
        /// </summary>
        public static async Task<(OVRSpatialAnchor? Anchor, bool Saved, string Detail)> CreateAsync(Transform parent, Pose pose, float timeoutSeconds)
        {
            var host = new GameObject("Stage anchor");
            host.transform.SetParent(parent, false);
            host.transform.SetPositionAndRotation(pose.position, pose.rotation);
            OVRSpatialAnchor anchor;
            try
            {
                // Creation starts in the component's Start, on the next frame.
                anchor = host.AddComponent<OVRSpatialAnchor>();
            }
            catch (Exception error)
            {
                UnityEngine.Object.Destroy(host);
                return (null, false, "could not create one: " + error.GetType().Name);
            }
            var deadline = Time.unscaledTime + timeoutSeconds;
            // The component destroys itself when creation fails.
            while (anchor != null && !anchor.Created && Time.unscaledTime < deadline) await Task.Yield();
            if (anchor == null)
            {
                if (host != null) UnityEngine.Object.Destroy(host);
                return (null, false, "the headset did not create it");
            }
            if (!anchor.Created)
            {
                UnityEngine.Object.Destroy(host);
                return (null, false, "it was not created within " + timeoutSeconds + " seconds");
            }
            while (anchor != null && !anchor.Localized && Time.unscaledTime < deadline) await Task.Yield();
            if (anchor == null || !anchor.Localized)
            {
                if (host != null) UnityEngine.Object.Destroy(host);
                return (null, false, "it was not localized within " + timeoutSeconds + " seconds");
            }
            try
            {
                var saved = await anchor.SaveAnchorAsync();
                return saved.Success
                    ? (anchor, true, "saved")
                    : (anchor, false, "not saved: " + saved.Status);
            }
            catch (Exception error)
            {
                return (anchor, false, "not saved: " + error.GetType().Name);
            }
        }

        /// <summary>
        /// Loads a saved anchor by its UUID and localizes it, within <paramref name="timeoutSeconds"/>.
        /// Returns the anchor, bound to a new component under <paramref name="parent"/>, or null, and
        /// whether the headset still holds it at all.
        /// </summary>
        public static async Task<(OVRSpatialAnchor? Anchor, bool Found, string Detail)> RestoreAsync(Transform parent, Guid uuid, float timeoutSeconds)
        {
            var unbound = new List<OVRSpatialAnchor.UnboundAnchor>();
            try
            {
                var loaded = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[] { uuid }, unbound);
                if (!loaded.Success) return (null, true, "it could not be loaded: " + loaded.Status);
                if (unbound.Count == 0) return (null, false, "the headset no longer holds it");
                var candidate = unbound[0];
                var localized = candidate.Localized || await candidate.LocalizeAsync(timeoutSeconds);
                if (!localized) return (null, true, "it was not localized within " + timeoutSeconds + " seconds, so it may belong to another room");
                // Bound in the same frame the component is added, before its Start would create a new anchor.
                var host = new GameObject("Stage anchor");
                host.transform.SetParent(parent, false);
                var anchor = host.AddComponent<OVRSpatialAnchor>();
                candidate.BindTo(anchor);
                return (anchor, true, "restored");
            }
            catch (Exception error)
            {
                return (null, true, "it could not be restored: " + error.GetType().Name);
            }
        }

        /// <summary>Erases saved anchors that no longer keep a place, and says how that went.</summary>
        public static async Task<string> EraseAsync(IEnumerable<Guid> uuids)
        {
            var erase = uuids.Where(uuid => uuid != Guid.Empty).Distinct().ToList();
            if (erase.Count == 0) return "nothing to erase";
            try
            {
                var result = await OVRSpatialAnchor.EraseAnchorsAsync(Array.Empty<OVRSpatialAnchor>(), erase);
                return result.Success ? $"erased {erase.Count} saved anchors" : "could not erase saved anchors: " + result.Status;
            }
            catch (Exception error)
            {
                return "could not erase saved anchors: " + error.GetType().Name;
            }
        }

        /// <summary>The anchor's pose in the world now.</summary>
        public static Pose PoseOf(OVRSpatialAnchor anchor) => new Pose(anchor.transform.position, anchor.transform.rotation);
    }
}

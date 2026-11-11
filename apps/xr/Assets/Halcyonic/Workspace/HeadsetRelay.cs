#nullable enable
using System;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Play in Headset over the pairing (ADR 0052): the app's one <see cref="PreviewRelay"/>, which serves the paired
    /// computer's preview of one project on the headset's loopback, so Quest Browser opens it as a secure context with no
    /// cable, held in the foreground by <c>com.halcyonic.relay.PreviewRelayService</c> while the browser is in front. It
    /// starts only when Play in Headset opens a preview (<see cref="HeadsetPlay"/> decides, so never in a demonstration and
    /// never for a check that did not pass with every read confined), one at a time; it stops when Halcyonic comes back to
    /// the front after the browser, when another project's preview opens, when the pairing changes and when the app quits.
    /// While it runs, any app on the headset could read that one preview through it (AUDIT2 S2 of ADR 0052), and nothing
    /// else of the computer's.
    /// </summary>
    public static class HeadsetRelay
    {
        private static PreviewRelay? relay;
        private static string? relayedProject;
        private static ControlPlaneTarget? relayedTarget;
        private static bool wentAway;

        /// <summary>
        /// Whether this build carries the relay: a development build only (ADR 0052, the lead's decision of 2026-11-11), as
        /// its service and permissions are only in a development build's manifest (RelayInDevelopmentBuilds).
        /// </summary>
        public static bool Built => Debug.isDebugBuild;

        /// <summary>Whether the app can relay a preview to <paramref name="target"/>: a paired computer, on the headset, in a build that carries it.</summary>
        public static bool Serves(ControlPlaneTarget? target)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Built && target?.Pairing != null;
#else
            return false;
#endif
        }

        /// <summary>
        /// The loopback port that serves <paramref name="projectId"/>'s preview from <paramref name="target"/>, starting the
        /// relay where none serves it now; null where the app cannot relay to that computer or the relay could not start.
        /// </summary>
        public static int? PortFor(ControlPlaneTarget? target, string projectId)
        {
            if (!Serves(target)) return null;
            if (relay != null && relayedProject == projectId && relayedTarget != null && relayedTarget.SameAs(target!)) return relay.Port;
            Stop();
            try
            {
                relay = PreviewRelay.Start(target!, projectId);
            }
            catch (Exception error)
            {
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "Halcyonic: the preview relay did not start: {0}", error.GetType().Name);
                return null;
            }
            relayedProject = projectId;
            relayedTarget = target;
            wentAway = false;
            Hold(true);
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "Halcyonic: the preview relay serves 127.0.0.1:{0}", relay.Port);
            return relay.Port;
        }

        /// <summary>
        /// The app went to the background (<paramref name="paused"/>, as the browser comes to the front) or came back: back
        /// in front after being away, the browser has left the preview, so the relay stops.
        /// </summary>
        public static void Paused(bool paused)
        {
            if (relay == null) return;
            if (paused) wentAway = true;
            else if (wentAway) Stop();
        }

        /// <summary>Stops the relay and lets the process leave the foreground; nothing where none runs.</summary>
        public static void Stop()
        {
            if (relay == null) return;
            Hold(false);
            relay.Dispose();
            relay = null;
            relayedProject = null;
            relayedTarget = null;
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "Halcyonic: the preview relay stopped");
        }

        /// <summary>Holds the process in the foreground while a relay is open, or lets it go; a refusal leaves the relay serving.</summary>
        private static void Hold(bool relaying)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var service = new AndroidJavaClass("com.halcyonic.relay.PreviewRelayService");
                service.CallStatic("hold", UnityEngine.Android.AndroidApplication.currentActivity, relaying);
            }
            catch (Exception error)
            {
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "Halcyonic: the preview relay's service did not {0}: {1}", relaying ? "start" : "stop", error.GetType().Name);
            }
#endif
        }
    }
}

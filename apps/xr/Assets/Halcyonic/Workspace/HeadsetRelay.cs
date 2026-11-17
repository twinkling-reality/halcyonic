#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Play in Headset over the pairing (ADR 0052): the app's <see cref="PreviewRelay"/>s, each serving the paired computer's
    /// preview of one project on the headset's loopback, so Quest Browser opens it as a secure context with no cable, held
    /// in the foreground by <c>com.halcyonic.relay.PreviewRelayService</c> while any runs. One starts only when Play in Headset
    /// opens a preview (<see cref="HeadsetPlay"/> decides, so never in a demonstration and never for a check that did not pass
    /// with every read confined). One per open project screen (the limits audit's #9, <see cref="PreviewRelays"/>): a screen's
    /// relay lives until that screen closes, so several projects' previews stand live at once; a Play with no screen open
    /// keeps the one-relay rule, ending when Halcyonic comes back to the front after the browser. All stop when the pairing
    /// changes and when the app quits. While one runs, any app on the headset could read that preview through it (AUDIT2 S2
    /// of ADR 0052), and nothing else of the computer's.
    /// </summary>
    public static class HeadsetRelay
    {
        private static readonly PreviewRelays relays = new PreviewRelays(StartOne);
        private static bool held;

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
        /// The loopback port that serves <paramref name="projectId"/>'s preview from <paramref name="target"/>, starting its relay
        /// where none serves it now; <paramref name="open"/> are the projects whose screens stand open. Null where the app cannot
        /// relay to that computer or the relay could not start.
        /// </summary>
        public static int? PortFor(ControlPlaneTarget? target, string projectId, IReadOnlyCollection<string> open)
        {
            if (!Serves(target)) return null;
            var port = relays.PortFor(target!, projectId, open);
            Follow();
            return port;
        }

        /// <summary>The open project screens changed: each closed screen's relay stops, and only its own.</summary>
        public static void Keep(IReadOnlyCollection<string> open)
        {
            if (!relays.Any) return;
            relays.Keep(open);
            Follow();
        }

        /// <summary>
        /// The app went to the background (<paramref name="paused"/>, as the browser comes to the front) or came back: back in
        /// front after being away, a Play with no screen open is left by the browser and stops; an open screen's stays live.
        /// </summary>
        public static void Paused(bool paused, IReadOnlyCollection<string> open)
        {
            relays.Paused(paused, open);
            Follow();
        }

        /// <summary>Stops every relay and lets the process leave the foreground; nothing where none runs.</summary>
        public static void Stop()
        {
            relays.StopAll();
            Follow();
        }

        private static PreviewRelays.Relayed StartOne(ControlPlaneTarget target, string projectId)
        {
            try
            {
                var relay = PreviewRelay.Start(target, projectId);
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "Halcyonic: a preview relay serves 127.0.0.1:{0}", relay.Port);
                return new PreviewRelays.Relayed(relay.Port, new Stopping(relay));
            }
            catch (Exception error)
            {
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "Halcyonic: the preview relay did not start: {0}", error.GetType().Name);
                throw;
            }
        }

        /// <summary>A relay's stop, said once in the log as it stops.</summary>
        private sealed class Stopping : IDisposable
        {
            private readonly PreviewRelay relay;
            public Stopping(PreviewRelay relay) => this.relay = relay;

            public void Dispose()
            {
                relay.Dispose();
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "Halcyonic: a preview relay stopped");
            }
        }

        /// <summary>Holds the process in the foreground while any relay runs, and lets it go once none does.</summary>
        private static void Follow()
        {
            if (relays.Any == held) return;
            held = relays.Any;
            Hold(held);
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

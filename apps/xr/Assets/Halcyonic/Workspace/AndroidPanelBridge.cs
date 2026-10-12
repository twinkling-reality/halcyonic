#nullable enable
using System;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>The production native agent panel. Only ordinary context crosses app-private storage.</summary>
    public static class AndroidPanelBridge
    {
        public readonly struct LaunchResult
        {
            public bool Succeeded { get; }
            public string Reason { get; }
            public LaunchResult(bool succeeded, string reason) { Succeeded = succeeded; Reason = reason; }
        }

        public static LaunchResult OpenPanel(string continuationJson, bool inHome)
        {
            if (!WorkspaceContinuity.TryParse(continuationJson, out _))
                return new LaunchResult(false, "Workspace context could not be saved.");
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // Unity owns currentActivity; never dispose that reference.
                var activity = UnityEngine.Android.AndroidApplication.currentActivity;
                using var bridge = new AndroidJavaClass("com.halcyonic.panel.PanelBridge");
                var reason = bridge.CallStatic<string>("open", activity, continuationJson, inHome);
                return new LaunchResult(string.IsNullOrEmpty(reason), reason ?? "");
            }
            catch (Exception)
            {
                return new LaunchResult(false, "The panel could not open. Your work is still in Workspace.");
            }
#else
            return new LaunchResult(false, "The native panel opens on a headset.");
#endif
        }

        /// <summary>Consumes only an explicit native return, once. Outbound state is a different file.</summary>
        public static bool TryTakePendingReturn(out string continuationJson)
        {
            continuationJson = "";
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var activity = UnityEngine.Android.AndroidApplication.currentActivity;
                using var bridge = new AndroidJavaClass("com.halcyonic.panel.PanelBridge");
                var json = bridge.CallStatic<string>("takePendingReturn", activity);
                if (!WorkspaceContinuity.TryParse(json, out _)) return false;
                continuationJson = json;
                return true;
            }
            catch (Exception) { return false; }
#else
            return false;
#endif
        }

        /// <summary>Preserves disk state through reconnect and process death until fresh validation.</summary>
        public static bool TryPeekPendingReturn(out string continuationJson)
        {
            continuationJson = "";
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var activity = UnityEngine.Android.AndroidApplication.currentActivity;
                using var bridge = new AndroidJavaClass("com.halcyonic.panel.PanelBridge");
                var json = bridge.CallStatic<string>("peekPendingReturn", activity);
                if (!WorkspaceContinuity.TryParse(json, out _)) return false;
                continuationJson = json;
                return true;
            }
            catch (Exception) { return false; }
#else
            return false;
#endif
        }

        /// <summary>Removes only the return actually validated and applied by the caller.</summary>
        public static bool AcknowledgePendingReturn(string handoffId)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var activity = UnityEngine.Android.AndroidApplication.currentActivity;
                using var bridge = new AndroidJavaClass("com.halcyonic.panel.PanelBridge");
                return bridge.CallStatic<bool>("acknowledgePendingReturn", activity, handoffId);
            }
            catch (Exception) { return false; }
#else
            return false;
#endif
        }
    }
}

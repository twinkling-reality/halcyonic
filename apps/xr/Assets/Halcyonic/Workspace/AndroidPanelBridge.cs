#nullable enable
using System;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>The production native agent panel. Only ordinary context crosses app-private storage.</summary>
    public static class AndroidPanelBridge
    {
        /// <summary>Mirrors only the app's reduced-motion choice; native waits fail safe to still.</summary>
        public static void SetMotionStill(bool still)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var bridge = new AndroidJavaClass("com.halcyonic.panel.PanelBridge");
                bridge.CallStatic("setMotionStill", UnityEngine.Android.AndroidApplication.currentActivity, still);
            }
            catch (Exception) { /* Native unknown preference remains still. */ }
#endif
        }

        public enum WorkspaceViewsStatus { Available, Absent, Failed }
        public readonly struct WorkspaceViewsRead
        {
            public WorkspaceViewsStatus Status { get; }
            public string? Json { get; }
            public string Reason { get; }
            public WorkspaceViewsRead(WorkspaceViewsStatus status, string? json, string reason)
            { Status = status; Json = json; Reason = reason; }
        }
        public readonly struct StorageResult
        {
            public bool Succeeded { get; }
            public string Reason { get; }
            public StorageResult(bool succeeded, string reason) { Succeeded = succeeded; Reason = reason; }
        }

        /// <summary>Failed private IO never masquerades as a missing record. Codec validation remains the caller's.</summary>
        public static WorkspaceViewsRead ReadWorkspaceViews()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var activity = UnityEngine.Android.AndroidApplication.currentActivity;
                using var bridge = new AndroidJavaClass("com.halcyonic.panel.PanelBridge");
                var json = bridge.CallStatic<string>("readWorkspaceViews", activity);
                return json == null
                    ? new WorkspaceViewsRead(WorkspaceViewsStatus.Absent, null, "")
                    : new WorkspaceViewsRead(WorkspaceViewsStatus.Available, json, "");
            }
            catch (Exception) { return new WorkspaceViewsRead(WorkspaceViewsStatus.Failed, null, "Saved work could not be read. Keep this view open."); }
#else
            return new WorkspaceViewsRead(WorkspaceViewsStatus.Failed, null, "Private headset storage is unavailable here.");
#endif
        }

        /// <summary>Writes only the fixed app-private Unity record, after strict bounded validation.</summary>
        public static StorageResult WriteWorkspaceViews(string json)
        {
            if (!WorkspaceViewStorage.TryParse(json, out _))
                return new StorageResult(false, "This saved work is invalid or too large. Your current work remains open.");
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var activity = UnityEngine.Android.AndroidApplication.currentActivity;
                using var bridge = new AndroidJavaClass("com.halcyonic.panel.PanelBridge");
                return bridge.CallStatic<bool>("writeWorkspaceViews", activity, json)
                    ? new StorageResult(true, "")
                    : new StorageResult(false, "Saved work could not be written. Your current work remains open.");
            }
            catch (Exception) { return new StorageResult(false, "Saved work could not be written. Your current work remains open."); }
#else
            return new StorageResult(false, "Private headset storage is unavailable here.");
#endif
        }

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

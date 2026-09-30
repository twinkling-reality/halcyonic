#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>
    /// Where the control plane is and how to authenticate. In the editor and the XR Simulator the
    /// control plane runs on the same machine. On a headset, `adb reverse tcp:47800 tcp:47800`
    /// makes the same loopback address reach it over USB, and the token is pushed to the app's
    /// persistent data directory (docs/internal/runbooks/XR_DEVELOPMENT.md). A headset paired with a
    /// control plane over the network (ADR 0017) reaches that one instead, over pinned TLS. Without
    /// either no control plane is configured, and the stage shows the recorded demonstration instead.
    /// </summary>
    public static class ControlPlaneSettings
    {
        public const string DefaultEndpoint = "ws://127.0.0.1:47800/realtime";
        private const string TokenFileName = "access-token";
        private const string PairingFileName = "halcyonic-pairing.json";

        private static IPairingStore? pairingStore;
        private static ControlPlaneApi? api;
        private static ControlPlaneTarget? apiTarget;

        public static Uri Endpoint => new Uri(Environment.GetEnvironmentVariable("HALCYONIC_ENDPOINT") ?? DefaultEndpoint);

        /// <summary>
        /// The control plane to reach: the one this device paired with, else the one the access token
        /// is for, else none.
        /// </summary>
        public static ControlPlaneTarget? Target()
        {
            var pairing = ReadPairing();
            if (pairing != null) return ControlPlaneTarget.Paired(pairing);
            var token = ReadAccessToken();
            return token == null ? null : ControlPlaneTarget.Local(Endpoint, token);
        }

        /// <summary>
        /// The REST client for the control plane <see cref="Target"/> names now, shared, and made again
        /// once pairing, forgetting or a new token changes it; null when there is none. Call it on the
        /// main thread.
        /// </summary>
        public static ControlPlaneApi? Api()
        {
            var target = Target();
            if (target != null && apiTarget != null && target.SameAs(apiTarget)) return api;
            api?.Dispose();
            apiTarget = target;
            api = target?.CreateApi();
            return api;
        }

        /// <summary>
        /// Where the pairing and its credential are kept: app-internal storage on Android, which only
        /// this app can read, and the persistent data directory elsewhere. Call it on the main thread.
        /// </summary>
        public static IPairingStore PairingStore => pairingStore ??= new FilePairingStore(PairingPath());

        /// <summary>The pairing, or null when there is none or it cannot be read.</summary>
        public static PairedControlPlane? ReadPairing()
        {
            try
            {
                return PairingStore.Load();
            }
            catch (InvalidDataException error)
            {
                Debug.LogWarning("Halcyonic: the pairing on this device cannot be read, so it is ignored: " + error.Message);
                return null;
            }
        }

        /// <summary>The first access token found, or null when none is provisioned.</summary>
        public static string? ReadAccessToken()
        {
            foreach (var path in TokenPaths())
            {
                if (!File.Exists(path)) continue;
                var token = File.ReadAllText(path).Trim();
                if (token.Length > 0) return token;
            }
            return null;
        }

        /// <summary>Where the token is looked for, in order.</summary>
        public static IEnumerable<string> TokenPaths()
        {
            var configured = Environment.GetEnvironmentVariable("HALCYONIC_TOKEN_FILE");
            if (!string.IsNullOrEmpty(configured)) yield return configured;
            yield return Path.Combine(Application.persistentDataPath, TokenFileName);
#if UNITY_EDITOR || UNITY_STANDALONE
            var dataDir = Environment.GetEnvironmentVariable("HALCYONIC_DATA_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".halcyonic");
            yield return Path.Combine(dataDir, TokenFileName);
#endif
        }

        private static string PairingPath()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Context.getFilesDir(): internal storage, which no other app can read and adb reaches only
            // through run-as on a debuggable build, unlike persistentDataPath on shared storage.
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var files = activity.Call<AndroidJavaObject>("getFilesDir");
            return Path.Combine(files.Call<string>("getAbsolutePath"), PairingFileName);
#else
            return Path.Combine(Application.persistentDataPath, PairingFileName);
#endif
        }
    }
}

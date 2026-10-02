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
    /// makes the same loopback address reach it over USB, and the token is written into the app's
    /// private storage with `run-as` (docs/internal/runbooks/XR_DEVELOPMENT.md). A headset paired with a
    /// control plane over the network (ADR 0017) reaches that one instead, over pinned TLS. Without
    /// either no control plane is configured, and the stage shows the recorded demonstration instead.
    /// </summary>
    public static class ControlPlaneSettings
    {
        public const string DefaultEndpoint = "ws://127.0.0.1:47800/realtime";
        private const string TokenFileName = "access-token";
        private const string PairingFileName = "halcyonic-pairing.json";

        private static IPairingStore? pairingStore;
        private static bool tokenMigrated;
        private static ControlPlaneApi? api;
        private static ControlPlaneTarget? apiTarget;

        public static Uri Endpoint => new Uri(Environment.GetEnvironmentVariable("HALCYONIC_ENDPOINT") ?? DefaultEndpoint);

        /// <summary>
        /// The control plane to reach: the one this device paired with, else the one the access token
        /// is for, else none.
        /// </summary>
        public static ControlPlaneTarget? Target()
        {
            // Before the pairing, which would otherwise leave a token on shared storage as long as it lasts.
            MigrateAccessToken();
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

        /// <summary>The first access token found, or null when none is provisioned. Call it on the main thread.</summary>
        public static string? ReadAccessToken()
        {
            MigrateAccessToken();
            return AccessTokenFile.Read(TokenPaths());
        }

        /// <summary>Where the token is looked for, in order.</summary>
        public static IEnumerable<string> TokenPaths()
        {
            var configured = Environment.GetEnvironmentVariable("HALCYONIC_TOKEN_FILE");
            if (!string.IsNullOrEmpty(configured)) yield return configured;
            yield return PrivatePath(TokenFileName);
#if UNITY_EDITOR || UNITY_STANDALONE
            var dataDir = Environment.GetEnvironmentVariable("HALCYONIC_DATA_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".halcyonic");
            yield return Path.Combine(dataDir, TokenFileName);
#endif
        }

        private static string PairingPath() => PrivatePath(PairingFileName);

        /// <summary>
        /// A file in the app's private storage: on Android Context.getFilesDir(), internal storage,
        /// which no other app can read and adb reaches only through run-as on a debuggable build, unlike
        /// persistentDataPath on shared storage; elsewhere the persistent data directory.
        /// </summary>
        private static string PrivatePath(string name)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var files = activity.Call<AndroidJavaObject>("getFilesDir");
            return Path.Combine(files.Call<string>("getAbsolutePath"), name);
#else
            return Path.Combine(Application.persistentDataPath, name);
#endif
        }

        /// <summary>
        /// Moves a token an earlier build read from shared storage into private storage, once a run,
        /// and removes the old copy. Only Android kept it on shared storage, and only development
        /// builds, which reach the computer over USB, take it from there: a release build, which may
        /// have replaced one without clearing its data, removes it without reading it. The token never
        /// reaches the log.
        /// </summary>
        private static void MigrateAccessToken()
        {
            if (tokenMigrated) return;
            tokenMigrated = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (!Debug.isDebugBuild)
                {
                    var left = Path.Combine(Application.persistentDataPath, TokenFileName);
                    if (File.Exists(left))
                    {
                        File.Delete(left);
                        Debug.Log("Halcyonic: removed an access token from shared storage without reading it; a release build does not use one there.");
                    }
                    return;
                }
                // Where earlier builds kept it. Should Unity ever put persistentDataPath in private
                // storage, the two name one file, which the move keeps.
                var legacy = Path.Combine(Application.persistentDataPath, TokenFileName);
                var outcome = AccessTokenFile.Migrate(legacy, PrivatePath(TokenFileName), OwnerOnly);
                if (outcome == AccessTokenMigration.Moved) Debug.Log("Halcyonic: moved the access token from shared storage into app-private storage.");
                else if (outcome == AccessTokenMigration.MovedUnrestricted) Debug.LogWarning("Halcyonic: moved the access token from shared storage into app-private storage, but could not set its mode to 600.");
                else if (outcome == AccessTokenMigration.RemovedStaleCopy) Debug.Log("Halcyonic: removed the access token from shared storage; the one in app-private storage is used.");
                else if (outcome == AccessTokenMigration.RemovedUnusableCopy) Debug.Log("Halcyonic: removed an access token file from shared storage that held no token.");
            }
            catch (Exception error)
            {
                Debug.LogWarning("Halcyonic: the access token in shared storage could not be moved: " + error.GetType().Name);
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>Mode 600: only this app's own user may read or write the file.</summary>
        private static void OwnerOnly(string path)
        {
            using var os = new AndroidJavaClass("android.system.Os");
            os.CallStatic("chmod", path, 0x180);
        }
#endif
    }
}

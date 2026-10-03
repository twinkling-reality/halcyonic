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
        private static string? computer;
        private static bool computerRead;

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
        /// The computer <see cref="Target"/> reaches, as <see cref="ControlPlaneTarget.Computer"/> names
        /// it, or null when none is configured: read once, and again only after a pairing or forgetting
        /// one, so it costs nothing to ask every frame. What the device keeps for one computer alone,
        /// such as a start whose outcome is unknown, is kept under it. Call it on the main thread.
        /// </summary>
        public static string? Computer()
        {
            if (!computerRead)
            {
                computer = Target()?.Computer;
                computerRead = true;
            }
            return computer;
        }

        /// <summary>
        /// Where the pairing and its credential are kept: app-internal storage on Android, which only
        /// this app can read, and the persistent data directory elsewhere. Saving or forgetting one
        /// changes the computer reached. Call it on the main thread.
        /// </summary>
        public static IPairingStore PairingStore => pairingStore ??= new ChangesComputer(new FilePairingStore(PairingPath()));

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
        /// A file of the app's own in its private storage (<see cref="PrivatePath"/>), as New project
        /// keeps its drafts: <paramref name="name"/> is one plain file name, never a path, so nothing
        /// reaches outside that directory.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="name"/> is empty, a path, or names a directory.</exception>
        public static string PrivateFile(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name == "." || name == ".." || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0
                || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetFileName(name) != name)
            {
                throw new ArgumentException("A private file is named by one plain file name.", nameof(name));
            }
            return PrivatePath(name);
        }

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
        /// Moves a token an earlier build kept on shared storage into private storage, once a run,
        /// and removes the old copy. Only Android kept it on shared storage, and only development
        /// builds, which reach the computer over USB, take it from there: a release build, which may
        /// have replaced one without clearing its data, removes it unread. Nothing there is followed
        /// through a link or waited on as a pipe (<see cref="AndroidTokenStorage"/>). The token never
        /// reaches the log.
        /// </summary>
        private static void MigrateAccessToken()
        {
            if (tokenMigrated) return;
            tokenMigrated = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var storage = new AndroidTokenStorage();
                // Where earlier builds kept it. Should Unity ever put persistentDataPath in private
                // storage, the two name one file, which the move keeps.
                var legacy = Path.Combine(Application.persistentDataPath, TokenFileName);
                var move = Debug.isDebugBuild
                    ? AccessTokenFile.Migrate(legacy, PrivatePath(TokenFileName), storage)
                    : AccessTokenFile.Discard(legacy, storage);
                switch (move.Outcome)
                {
                    case AccessTokenMigration.Moved:
                        Debug.Log("Halcyonic: moved the access token from shared storage into app-private storage.");
                        if (!move.Restricted) Debug.LogWarning("Halcyonic: could not set the moved access token's mode to 600; app-private storage still keeps other apps out.");
                        break;
                    case AccessTokenMigration.KeptPrivateToken:
                        Debug.Log("Halcyonic: an access token is in app-private storage, so the one on shared storage is not used.");
                        break;
                    case AccessTokenMigration.Unusable:
                        Debug.Log("Halcyonic: what was at the access token's old place on shared storage was not a token in its own form, so it was not used.");
                        break;
                    case AccessTokenMigration.Discarded:
                        Debug.Log("Halcyonic: a release build found an access token on shared storage and did not read it.");
                        break;
                    case AccessTokenMigration.LeftInLinkedFolder:
                        Debug.LogWarning("Halcyonic: the folder on shared storage where the access token was kept is a link, so nothing in it was read or removed.");
                        break;
                }
                if (move.SharedCopyRemains)
                {
                    Debug.LogWarning("Halcyonic: a copy of the access token is still on shared storage, and this app could not remove it. From the computer: adb shell rm -f /sdcard/Android/data/com.halcyonic.xr/files/access-token");
                }
            }
            catch (Exception error)
            {
                // A storage failure's message names only the call and its errno, never the token.
                var reason = error is TokenStorageException ? error.Message : error.GetType().Name;
                Debug.LogWarning("Halcyonic: could not deal with the access token's old place on shared storage, so a copy may still be there (" + reason + "). From the computer: adb shell ls /sdcard/Android/data/com.halcyonic.xr/files/");
            }
#endif
        }

        /// <summary>The pairing's store, after whose save or forget the computer reached is read again.</summary>
        private sealed class ChangesComputer : IPairingStore
        {
            private readonly IPairingStore store;

            public ChangesComputer(IPairingStore store)
            {
                this.store = store;
            }

            public PairedControlPlane? Load() => store.Load();

            public void Save(PairedControlPlane pairing)
            {
                store.Save(pairing);
                computerRead = false;
            }

            public void Forget()
            {
                store.Forget();
                computerRead = false;
            }
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>
    /// Where the control plane is and how to authenticate. In the editor and the XR Simulator the
    /// control plane runs on the same machine. On a headset, `adb reverse tcp:47800 tcp:47800`
    /// makes the same loopback address reach it over USB, and the token is pushed to the app's
    /// persistent data directory (docs/internal/runbooks/XR_DEVELOPMENT.md).
    /// </summary>
    public static class ControlPlaneSettings
    {
        public const string DefaultEndpoint = "ws://127.0.0.1:47800/realtime";
        private const string TokenFileName = "access-token";

        public static Uri Endpoint => new Uri(Environment.GetEnvironmentVariable("HALCYONIC_ENDPOINT") ?? DefaultEndpoint);

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
    }
}

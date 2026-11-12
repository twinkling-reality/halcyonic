#nullable enable
using System;
using System.IO;

namespace Halcyonic.Client
{
    /// <summary>
    /// What a run with nobody at it may reach: Unity's batch mode, where every journey, render, walk and check runs.
    /// Such a run takes a control plane only from an endpoint and a token file named for it on purpose, a scratch
    /// plane's, and never falls back to this device's pairing, its stored token or the account's data folder, so no
    /// check can ever read from, send to or show a developer's own computer (AUDIT3's alert, 2026-11-12).
    /// </summary>
    public static class UnattendedReach
    {
        /// <summary>The refusal's words, when nothing names a scratch plane.</summary>
        public const string Refused = "An Editor check may not reach a real control plane. Name a scratch plane with HALCYONIC_ENDPOINT and HALCYONIC_TOKEN_FILE.";

        /// <summary>The refusal's words, when such a run reaches for what this device keeps, such as New Project's drafts.</summary>
        public const string KeptRefused = "An Editor check may not read or write what this device keeps. Hand it a store of its own.";

        /// <summary>The refusal's words, when the endpoint named is not a realtime address.</summary>
        public const string NotAnEndpoint = "The scratch plane's endpoint is not a realtime address (ws or wss).";

        /// <summary>
        /// The scratch plane at <paramref name="endpoint"/>, with the token in <paramref name="tokenFile"/>.
        /// </summary>
        /// <exception cref="UnattendedReachRefusedException">Either is missing, or the file holds no token.</exception>
        public static ControlPlaneTarget Target(string? endpoint, string? tokenFile)
        {
            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(tokenFile)) throw new UnattendedReachRefusedException(Refused);
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "ws" && uri.Scheme != "wss")
                throw new UnattendedReachRefusedException(NotAnEndpoint);
            var token = AccessTokenFile.Read(new[] { tokenFile! });
            if (token == null) throw new UnattendedReachRefusedException("The token file named for the scratch plane holds no token.");
            return ControlPlaneTarget.Local(uri, token);
        }
    }

    /// <summary>A run with nobody at it asked for a control plane that nothing named on purpose.</summary>
    public sealed class UnattendedReachRefusedException : InvalidOperationException
    {
        public UnattendedReachRefusedException(string message) : base(message) { }
    }
}

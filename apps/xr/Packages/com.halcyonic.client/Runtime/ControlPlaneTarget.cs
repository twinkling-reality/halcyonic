#nullable enable
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// A control plane this device paired with over the network (ADR 0017): where it is, the
    /// certificate it presented when the device paired, and the credential it issued. The credential
    /// is a secret: it is kept only through an <see cref="IPairingStore"/> and never logged.
    /// </summary>
    public sealed class PairedControlPlane
    {
        public PairedControlPlane(string host, int port, string certificateSha256, string deviceId, string credential)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("A host is required.", nameof(host));
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            if (!credential.StartsWith(PairingCrypto.CredentialPrefix, StringComparison.Ordinal))
            {
                throw new ArgumentException("A device credential starts with " + PairingCrypto.CredentialPrefix + ".", nameof(credential));
            }
            Host = host;
            Port = port;
            CertificateSha256 = ControlPlaneTarget.CheckPin(certificateSha256);
            DeviceId = deviceId;
            Credential = credential;
        }

        /// <summary>The address or name the person typed.</summary>
        public string Host { get; }

        public int Port { get; }

        /// <summary>What every connection's certificate must hash to.</summary>
        public string CertificateSha256 { get; }

        public string DeviceId { get; }

        public string Credential { get; }

        public Uri RealtimeEndpoint => new UriBuilder("wss", UriHost(Host), Port, "/realtime").Uri;

        /// <summary>The address as a person reads it, such as 192.168.1.23:47801.</summary>
        public string Address => UriHost(Host) + ":" + Port.ToString(CultureInfo.InvariantCulture);

        /// <summary>A copy for another address of the same control plane, whose pin still holds.</summary>
        public PairedControlPlane At(string host, int port) => new PairedControlPlane(host, port, CertificateSha256, DeviceId, Credential);

        private static string UriHost(string host) => host.Contains(":") && !host.StartsWith("[", StringComparison.Ordinal) ? "[" + host + "]" : host;
    }

    /// <summary>
    /// Where a session connects, and how it proves who it is: the local access token over the
    /// unpinned transport, as over USB, or a paired device's credential over the pinned ones.
    /// </summary>
    public sealed class ControlPlaneTarget
    {
        private static readonly Regex Sha256 = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

        private ControlPlaneTarget(Uri endpoint, string credential, PairedControlPlane? pairing)
        {
            Endpoint = endpoint;
            Credential = credential;
            Pairing = pairing;
        }

        /// <summary>The realtime endpoint.</summary>
        public Uri Endpoint { get; }

        /// <summary>The bearer credential: the access token, or the device credential.</summary>
        public string Credential { get; }

        /// <summary>The pairing, when this device reaches its control plane over the network.</summary>
        public PairedControlPlane? Pairing { get; }

        /// <summary>A control plane reached with the access token, such as through <c>adb reverse</c>.</summary>
        public static ControlPlaneTarget Local(Uri endpoint, string accessToken) => new ControlPlaneTarget(endpoint, accessToken, null);

        /// <summary>A paired control plane, over pinned TLS.</summary>
        public static ControlPlaneTarget Paired(PairedControlPlane pairing) =>
            new ControlPlaneTarget(pairing.RealtimeEndpoint, pairing.Credential, pairing);

        public IRealtimeTransport CreateTransport() =>
            Pairing == null ? (IRealtimeTransport)new ClientWebSocketTransport() : new PinnedWebSocketTransport(Pairing.CertificateSha256);

        public RealtimeSession CreateSession(ClientInfo client) =>
            new RealtimeSession(
                new RealtimeSessionOptions(Endpoint, Credential, client)
                {
                    AccessRefused = Pairing == null ? ConnectionText.AccessTokenRefused : ConnectionText.PairingRefused,
                },
                CreateTransport);

        public ControlPlaneApi CreateApi() =>
            Pairing == null
                ? new ControlPlaneApi(ControlPlaneApi.BaseUriFor(Endpoint), Credential)
                : new ControlPlaneApi(ControlPlaneApi.BaseUriFor(Endpoint), Credential, new PinnedHttpHandler(Pairing.CertificateSha256));

        /// <summary>
        /// Whether another target reaches the same endpoint with the same credential and pin, so a
        /// client made for one serves the other; pairing, forgetting or a new token changes it.
        /// </summary>
        public bool SameAs(ControlPlaneTarget other) =>
            Endpoint == other.Endpoint
            && string.Equals(Credential, other.Credential, StringComparison.Ordinal)
            && string.Equals(Pairing?.CertificateSha256, other.Pairing?.CertificateSha256, StringComparison.Ordinal);

        /// <summary>A certificate pin: a SHA-256 in lowercase hex.</summary>
        public static string CheckPin(string pin) =>
            Sha256.IsMatch(pin) ? pin : throw new ArgumentException("A certificate pin is a SHA-256 in lowercase hex.", nameof(pin));
    }
}

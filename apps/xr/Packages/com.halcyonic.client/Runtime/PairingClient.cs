#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using Newtonsoft.Json;

namespace Halcyonic.Client
{
    /// <summary>
    /// Pairs this device with a control plane over the network (ADR 0017): the person types the
    /// control plane's address and the eight-digit code <c>pnpm pair</c> shows. The exchange runs on
    /// the network listener's <c>/pair</c> WebSocket: SRP-6a proves both sides used the same code, and
    /// both proofs bind the TLS certificate this connection presented, which becomes the pin for
    /// every later connection. The code never crosses the network.
    /// </summary>
    public static class PairingClient
    {
        /// <summary>How long an exchange may take in all.</summary>
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        /// <exception cref="PairingException">The control plane refused, or could not be reached.</exception>
        public static async Task<PairedControlPlane> PairAsync(
            string host,
            int port,
            string typedCode,
            string deviceLabel,
            CancellationToken cancellationToken = default)
        {
            var code = PairingCrypto.NormalizeCode(typedCode)
                ?? throw new PairingException("invalid_code", "The code is the eight digits pnpm pair shows.", null);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            var token = timeout.Token;
            var endpoint = new UriBuilder("wss", host.Contains(":") ? "[" + host.Trim('[', ']') + "]" : host, port, "/pair").Uri;
            PinnedConnection connection;
            try
            {
                connection = await PinnedConnection.OpenAsync(WebSocketUpgrade.Host(endpoint), port, null, token).ConfigureAwait(false);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                throw new PairingException("unreachable", "Nothing answered at " + endpoint.Authority + ". Check the address, and that " + HostText.Your + " and this device share a network.", null, error);
            }
            using (connection)
            using (connection.CloseOn(token))
            {
                try
                {
                    return await ExchangeAsync(connection, endpoint, code, deviceLabel, token).ConfigureAwait(false);
                }
                catch (Exception error) when (!(error is PairingException) && !cancellationToken.IsCancellationRequested)
                {
                    throw new PairingException(
                        timeout.IsCancellationRequested ? "timeout" : "protocol_error",
                        timeout.IsCancellationRequested ? "Pairing took too long; try again." : "Pairing failed: " + error.Message,
                        null,
                        error);
                }
            }
        }

        /// <summary>
        /// Asks the control plane to stop accepting this device's credential, as forgetting it does.
        /// Returns false when the control plane could not be reached, so its owner can revoke it there.
        /// </summary>
        public static async Task<bool> RevokeAsync(PairedControlPlane pairing, CancellationToken cancellationToken = default)
        {
            using var http = new HttpClient(new PinnedHttpHandler(pairing.CertificateSha256)) { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ControlPlaneApi.BaseUriFor(pairing.RealtimeEndpoint), "api/device/revoke"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pairing.Credential);
            try
            {
                using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                // Already revoked on the control plane counts as done.
                return response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.Unauthorized;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        private static async Task<PairedControlPlane> ExchangeAsync(
            PinnedConnection connection,
            Uri endpoint,
            string code,
            string deviceLabel,
            CancellationToken cancellationToken)
        {
            WebSocket socket;
            try
            {
                socket = await WebSocketUpgrade.ConnectAsync(connection.Stream, endpoint, new KeyValuePair<string, string>[0], cancellationToken).ConfigureAwait(false);
            }
            catch (UpgradeRefusedException refused)
            {
                throw new PairingException(refused.Code ?? "refused", Explain(refused.Code, refused.Message), null, refused);
            }
            using (socket)
            {
                var buffer = new byte[16 * 1024];
                await SendAsync(socket, new PairRequestMessage { DeviceLabel = deviceLabel }, cancellationToken).ConfigureAwait(false);
                var challenge = Expect<PairChallengeMessage>(await ReceiveAsync(socket, buffer, cancellationToken).ConfigureAwait(false));

                var salt = Convert.FromBase64String(challenge.Salt);
                var serverPublic = Srp6a.FromBytes(Convert.FromBase64String(challenge.ServerPublic));
                var client = SrpClient.Create(SrpGroup.Pairing, Srp6a.PairingIdentity, PairingCrypto.CodePassword(code));
                var key = client.SessionKey(salt, serverPublic)
                    ?? throw new PairingException("protocol_error", "The control plane sent an invalid challenge.", null);
                var certificate = PairingCrypto.FromHex(connection.CertificateSha256);
                var transcript = PairingCrypto.Transcript(deviceLabel, salt, client.A, serverPublic, certificate);
                var proof = PairingCrypto.ClientProof(key, transcript);
                await SendAsync(socket, new PairProofMessage
                {
                    ClientPublic = Convert.ToBase64String(Srp6a.Pad(SrpGroup.Pairing, client.A)),
                    Proof = Convert.ToBase64String(proof),
                }, cancellationToken).ConfigureAwait(false);

                var accepted = Expect<PairAcceptedMessage>(await ReceiveAsync(socket, buffer, cancellationToken).ConfigureAwait(false));
                var sealedCredential = Convert.FromBase64String(accepted.Credential);
                var expected = PairingCrypto.ServerProof(key, transcript, proof, accepted.DeviceId, sealedCredential);
                if (!PairingCrypto.SameMac(expected, Convert.FromBase64String(accepted.Proof)))
                {
                    // Only a control plane that knew the code can prove it; nothing is kept.
                    throw new PairingException("server_not_proven", "The control plane could not prove it knew the code; nothing was paired.", null);
                }
                var credential = PairingCrypto.CredentialFromBytes(PairingCrypto.Seal(sealedCredential, key, transcript));
                return new PairedControlPlane(WebSocketUpgrade.Host(endpoint), endpoint.Port, connection.CertificateSha256, accepted.DeviceId, credential);
            }
        }

        private static T Expect<T>(PairingServerMessage message) where T : PairingServerMessage
        {
            if (message is T expected) return expected;
            if (message is PairRefusedMessage refused)
            {
                throw new PairingException(refused.Error.Code, Explain(refused.Error.Code, refused.Error.Message), refused.AttemptsLeft);
            }
            throw new PairingException("protocol_error", "The control plane answered out of order.", null);
        }

        private static async Task SendAsync(WebSocket socket, PairingClientMessage message, CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(HalcyonicJson.Serialize(message));
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<PairingServerMessage> ReceiveAsync(WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
        {
            var text = await WebSocketUpgrade.ReceiveTextAsync(socket, buffer, 64 * 1024, cancellationToken).ConfigureAwait(false)
                ?? throw new PairingException("protocol_error", "The control plane closed the connection before answering.", null);
            try
            {
                return HalcyonicJson.Deserialize<PairingServerMessage>(text);
            }
            catch (JsonException error)
            {
                throw new PairingException("protocol_error", "The control plane's answer does not match the pairing contract.", null, error);
            }
        }

        /// <summary>The control plane's words, with what the person can do where it helps.</summary>
        private static string Explain(string? code, string message) => code switch
        {
            "pairing_closed" => "Pairing is not open on " + HostText.Your + ". Run pnpm pair there, then try again.",
            "wrong_code" => "The code was not accepted. Check it on " + HostText.Your + " and type it again.",
            _ => message,
        };
    }

    /// <summary>Pairing did not complete. Nothing was stored.</summary>
    public sealed class PairingException : Exception
    {
        public PairingException(string code, string message, long? attemptsLeft, Exception? inner = null)
            : base(message, inner)
        {
            Code = code;
            AttemptsLeft = attemptsLeft;
        }

        /// <summary>
        /// Why: <c>wrong_code</c>, <c>pairing_closed</c>, <c>too_many_requests</c>, <c>busy</c>,
        /// <c>timeout</c>, <c>unreachable</c>, <c>invalid_code</c>, <c>server_not_proven</c> or
        /// <c>protocol_error</c>, among the control plane's codes.
        /// </summary>
        public string Code { get; }

        /// <summary>How many more wrong codes the Mac's pairing window allows, once one was refused.</summary>
        public long? AttemptsLeft { get; }
    }
}

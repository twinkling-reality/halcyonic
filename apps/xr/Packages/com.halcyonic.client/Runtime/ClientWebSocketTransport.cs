#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// <see cref="IRealtimeTransport"/> over <see cref="ClientWebSocket"/>. Whether ClientWebSocket
    /// works under IL2CPP on Quest is not verified yet; the transport interface is the seam for a
    /// native replacement if it does not.
    /// </summary>
    public sealed class ClientWebSocketTransport : IRealtimeTransport
    {
        /// <summary>A snapshot of a large project can be big; a message beyond this is refused.</summary>
        public const int DefaultMaxMessageBytes = 16 * 1024 * 1024;

        private readonly ClientWebSocket socket = new ClientWebSocket();
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private readonly byte[] buffer = new byte[16 * 1024];
        private readonly int maxMessageBytes;

        public ClientWebSocketTransport(int maxMessageBytes = DefaultMaxMessageBytes)
        {
            this.maxMessageBytes = maxMessageBytes;
        }

        public string? CloseDescription { get; private set; }

        public async Task ConnectAsync(Uri endpoint, string accessToken, CancellationToken cancellationToken)
        {
            socket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);
            try
            {
                await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            }
            catch (WebSocketException) when (!cancellationToken.IsCancellationRequested)
            {
                // ClientWebSocket says only that it could not connect, whether nothing answered or the
                // control plane refused the token (HTTP 401). One authenticated REST read tells them apart.
                if (await RefusesToken(endpoint, accessToken, cancellationToken).ConfigureAwait(false) is UpgradeRefusedException refused)
                {
                    throw refused;
                }
                throw;
            }
        }

        /// <summary>
        /// Whether the control plane at <paramref name="endpoint"/> answers the access token with 401,
        /// as the refusal it would have given the upgrade; null when it does not, or does not answer.
        /// </summary>
        internal static async Task<UpgradeRefusedException?> RefusesToken(Uri endpoint, string accessToken, CancellationToken cancellationToken)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ControlPlaneApi.BaseUriFor(endpoint), "api/runtimes"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.Unauthorized) return null;
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                try
                {
                    var error = HalcyonicJson.Deserialize<ErrorResponse>(body).Error;
                    return new UpgradeRefusedException(401, error.Code, error.Message);
                }
                catch (Exception)
                {
                    return new UpgradeRefusedException(401, null, "no explanation was given.");
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }

        public async Task SendAsync(string message, CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await socket
                    .SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                sendLock.Release();
            }
        }

        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
        {
            using var message = new MemoryStream();
            while (true)
            {
                var result = await socket
                    .ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                    .ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    CloseDescription = result.CloseStatus == null
                        ? "closed without a status"
                        : (int)result.CloseStatus + " " + result.CloseStatusDescription;
                    return null;
                }
                if (result.MessageType != WebSocketMessageType.Text)
                {
                    throw new InvalidDataException("The control plane sent a binary message; the protocol is JSON text.");
                }
                if (message.Length + result.Count > maxMessageBytes)
                {
                    throw new InvalidDataException("A message from the control plane exceeds " + maxMessageBytes + " bytes.");
                }
                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                }
            }
        }

        public void Dispose()
        {
            socket.Dispose();
            sendLock.Dispose();
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Halcyonic.Client
{
    /// <summary>
    /// <see cref="IRealtimeTransport"/> to a control plane on loopback with the access token, as over
    /// USB through <c>adb reverse</c>. It opens a connection to the literal address, through no proxy,
    /// asks the control plane on it to prove it holds the token (<see cref="LoopbackProof"/>), then
    /// performs the WebSocket upgrade with the token on that same connection and hands the stream to
    /// <see cref="WebSocket.CreateFromStream"/>, as <see cref="PinnedWebSocketTransport"/> does over
    /// TLS, so only what just proved itself receives the token. The upgrade's own answer tells a
    /// refused token (401) from a control plane that does not answer.
    /// </summary>
    public sealed class LoopbackWebSocketTransport : IRealtimeTransport
    {
        /// <summary>A snapshot of a large project can be big; a message beyond this is refused.</summary>
        public const int DefaultMaxMessageBytes = 16 * 1024 * 1024;

        private readonly int maxMessageBytes;
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private readonly byte[] buffer = new byte[16 * 1024];
        private TcpClient? connection;
        private WebSocket? socket;

        public LoopbackWebSocketTransport(int maxMessageBytes = DefaultMaxMessageBytes)
        {
            this.maxMessageBytes = maxMessageBytes;
        }

        public string? CloseDescription { get; private set; }

        public async Task ConnectAsync(Uri endpoint, string accessToken, CancellationToken cancellationToken)
        {
            if (endpoint.Scheme != "ws") throw new ArgumentException("A loopback connection is ws://.", nameof(endpoint));
            var baseUri = ControlPlaneApi.BaseUriFor(endpoint);
            var address = LoopbackProof.AddressOf(baseUri) ?? throw new TokenNotSentException(LoopbackProofOutcome.NotLoopback, baseUri);
            var proved = await LoopbackProof.ProveAsync(baseUri, address, accessToken, cancellationToken).ConfigureAwait(false);
            if (proved.Outcome != LoopbackProofOutcome.Proved || proved.Connection == null) throw new TokenNotSentException(proved.Outcome, baseUri);
            var open = proved.Connection;
            connection = open;
            using (cancellationToken.Register(() => open.Dispose()))
            {
                socket = await WebSocketUpgrade.ConnectAsync(
                    open.GetStream(),
                    endpoint,
                    new[] { new KeyValuePair<string, string>("Authorization", "Bearer " + accessToken) },
                    cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task SendAsync(string message, CancellationToken cancellationToken)
        {
            var open = socket ?? throw new InvalidOperationException("Not connected.");
            var bytes = Encoding.UTF8.GetBytes(message);
            await sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await open.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                sendLock.Release();
            }
        }

        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
        {
            var open = socket ?? throw new InvalidOperationException("Not connected.");
            var text = await WebSocketUpgrade.ReceiveTextAsync(open, buffer, maxMessageBytes, cancellationToken).ConfigureAwait(false);
            if (text == null) CloseDescription = WebSocketUpgrade.Describe(open);
            return text;
        }

        public void Dispose()
        {
            socket?.Dispose();
            connection?.Dispose();
            sendLock.Dispose();
        }
    }
}

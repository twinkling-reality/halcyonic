#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using Newtonsoft.Json;

namespace Halcyonic.Client
{
    /// <summary>
    /// <see cref="IRealtimeTransport"/> to a paired control plane over <c>wss://</c>, pinned to the
    /// certificate the device saw when it paired (ADR 0017). It opens a <see cref="PinnedConnection"/>,
    /// performs the WebSocket upgrade itself with the device credential, and hands the stream to
    /// <see cref="WebSocket.CreateFromStream"/>, the framing ClientWebSocket uses too.
    /// ClientWebSocket itself cannot pin on the headset: it ignores its options' validation callback.
    /// </summary>
    public sealed class PinnedWebSocketTransport : IRealtimeTransport
    {
        private readonly string pin;
        private readonly int maxMessageBytes;
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private readonly byte[] buffer = new byte[16 * 1024];
        private PinnedConnection? connection;
        private WebSocket? socket;

        /// <param name="pin">The SHA-256 of the control plane's certificate, in lowercase hex.</param>
        public PinnedWebSocketTransport(string pin, int maxMessageBytes = LoopbackWebSocketTransport.DefaultMaxMessageBytes)
        {
            this.pin = ControlPlaneTarget.CheckPin(pin);
            this.maxMessageBytes = maxMessageBytes;
        }

        public string? CloseDescription { get; private set; }

        public async Task ConnectAsync(Uri endpoint, string accessToken, CancellationToken cancellationToken)
        {
            if (endpoint.Scheme != "wss") throw new ArgumentException("A pinned connection is wss://.", nameof(endpoint));
            connection = await PinnedConnection.OpenAsync(WebSocketUpgrade.Host(endpoint), endpoint.Port, pin, cancellationToken).ConfigureAwait(false);
            using (connection.CloseOn(cancellationToken))
            {
                socket = await WebSocketUpgrade.ConnectAsync(
                    connection.Stream,
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

    /// <summary>The control plane answered a WebSocket upgrade with something other than 101.</summary>
    public sealed class UpgradeRefusedException : IOException
    {
        public UpgradeRefusedException(int status, string? code, string message)
            : base("The control plane refused the connection (" + status + "): " + message)
        {
            Status = status;
            Code = code;
        }

        public int Status { get; }

        /// <summary>The error code the control plane gave, such as <c>device_revoked</c>, if any.</summary>
        public string? Code { get; }
    }

    /// <summary>The client side of RFC 6455's opening handshake, over a stream that is already open.</summary>
    internal static class WebSocketUpgrade
    {
        private const string AcceptGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        public static async Task<WebSocket> ConnectAsync(
            Stream stream,
            Uri endpoint,
            IReadOnlyList<KeyValuePair<string, string>> headers,
            CancellationToken cancellationToken)
        {
            var nonce = new byte[16];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(nonce);
            }
            var key = Convert.ToBase64String(nonce);
            var request = new List<KeyValuePair<string, string>>
            {
                // The port always, because the control plane checks Host against its own.
                new KeyValuePair<string, string>("Host", HostHeader(endpoint)),
                new KeyValuePair<string, string>("Upgrade", "websocket"),
                new KeyValuePair<string, string>("Connection", "Upgrade"),
                new KeyValuePair<string, string>("Sec-WebSocket-Key", key),
                new KeyValuePair<string, string>("Sec-WebSocket-Version", "13"),
            };
            request.AddRange(headers);
            await Http1.WriteRequestAsync(stream, "GET", endpoint.PathAndQuery, request, null, cancellationToken).ConfigureAwait(false);
            var head = await Http1.ReadHeadAsync(stream, cancellationToken).ConfigureAwait(false);
            if (head.Status != 101)
            {
                var body = await Http1.ReadBodyAsync(stream, head, 64 * 1024, cancellationToken).ConfigureAwait(false);
                throw Refusal(head.Status, body);
            }
            string expected;
            using (var sha1 = SHA1.Create())
            {
                expected = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(key + AcceptGuid)));
            }
            if (!string.Equals(head.Header("Upgrade"), "websocket", StringComparison.OrdinalIgnoreCase)
                || !Lists(head.Header("Connection"), "upgrade")
                || head.Header("Sec-WebSocket-Accept") != expected)
            {
                throw new InvalidDataException("The control plane answered the WebSocket upgrade incorrectly.");
            }
            return WebSocket.CreateFromStream(stream, false, null, WebSocket.DefaultKeepAliveInterval);
        }

        /// <summary>Whether a comma-separated header value names <paramref name="token"/>, in any case (RFC 6455 4.1).</summary>
        private static bool Lists(string? value, string token)
        {
            if (value == null) return false;
            foreach (var part in value.Split(','))
            {
                if (string.Equals(part.Trim(), token, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>The host to connect to: a name, or an address without the brackets a URI puts around IPv6.</summary>
        public static string Host(Uri endpoint) => endpoint.IdnHost.Trim('[', ']');

        public static string HostHeader(Uri endpoint) =>
            (endpoint.HostNameType == UriHostNameType.IPv6 ? "[" + Host(endpoint) + "]" : Host(endpoint)) + ":" + endpoint.Port;

        /// <summary>The next complete text message, or null once the peer closed the connection.</summary>
        public static async Task<string?> ReceiveTextAsync(WebSocket socket, byte[] buffer, int maxMessageBytes, CancellationToken cancellationToken)
        {
            using var message = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                if (result.MessageType != WebSocketMessageType.Text)
                {
                    throw new InvalidDataException("The control plane sent a binary message; the protocol is JSON text.");
                }
                if (message.Length + result.Count > maxMessageBytes)
                {
                    throw new InvalidDataException("A message from the control plane exceeds " + maxMessageBytes + " bytes.");
                }
                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage) return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            }
        }

        public static string Describe(WebSocket socket) =>
            socket.CloseStatus == null ? "closed without a status" : (int)socket.CloseStatus + " " + socket.CloseStatusDescription;

        private static UpgradeRefusedException Refusal(int status, byte[] body)
        {
            try
            {
                var error = HalcyonicJson.Deserialize<ErrorResponse>(Encoding.UTF8.GetString(body)).Error;
                return new UpgradeRefusedException(status, error.Code, error.Message);
            }
            catch (JsonException)
            {
                return new UpgradeRefusedException(status, null, "no explanation was given.");
            }
        }
    }
}

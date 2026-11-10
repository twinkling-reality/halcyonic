#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Halcyonic.Client
{
    /// <summary>
    /// An <see cref="HttpMessageHandler"/> for a paired control plane's REST API over <c>https://</c>,
    /// pinned to the certificate the device saw when it paired (ADR 0017). Each request gets its own
    /// <see cref="PinnedConnection"/> and is sent as HTTP/1.1 with <c>Connection: close</c>; the REST
    /// API is read a few pages at a time, so connections are not reused. On the headset,
    /// HttpClientHandler's certificate callback throws, which is why this exists.
    /// </summary>
    public sealed class PinnedHttpHandler : HttpMessageHandler
    {
        private readonly string pin;
        private readonly PairedReach? reach;
        private readonly int maxResponseBytes;

        /// <param name="pin">The SHA-256 of the control plane's certificate, in lowercase hex.</param>
        public PinnedHttpHandler(string pin, int maxResponseBytes = 16 * 1024 * 1024)
        {
            this.pin = ControlPlaneTarget.CheckPin(pin);
            this.maxResponseBytes = maxResponseBytes;
        }

        /// <summary>
        /// To a pairing's control plane wherever it answers (<see cref="PairedReach"/>): each request's
        /// path, at the first of its addresses that completes the handshake with the pin.
        /// </summary>
        public PinnedHttpHandler(PairedReach reach, int maxResponseBytes = 16 * 1024 * 1024)
            : this(reach.Pin, maxResponseBytes)
        {
            this.reach = reach;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new ArgumentException("The request has no URI.", nameof(request));
            if (uri.Scheme != "https") throw new ArgumentException("A pinned request is https://.", nameof(request));
            var body = request.Content == null ? null : await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

            Http1Head head;
            byte[] content;
            try
            {
                PinnedConnection connection;
                var at = uri;
                if (reach == null)
                {
                    connection = await PinnedConnection.OpenAsync(WebSocketUpgrade.Host(uri), uri.Port, pin, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var (opened, host) = await reach.OpenAsync(cancellationToken).ConfigureAwait(false);
                    connection = opened;
                    at = PairedReach.At(uri, host);
                }
                using var open = connection;
                var headers = Http1.RequestHeaders(request, at, body);
                using var closing = connection.CloseOn(cancellationToken);
                await Http1.WriteRequestAsync(connection.Stream, request.Method.Method, uri.PathAndQuery, headers, body, cancellationToken).ConfigureAwait(false);
                head = await Http1.ReadHeadAsync(connection.Stream, cancellationToken).ConfigureAwait(false);
                content = await Http1.ReadBodyAsync(connection.Stream, head, maxResponseBytes, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (Reported(error, cancellationToken) is Exception reported)
            {
                throw reported;
            }
            return Http1.Response(request, head, content);
        }

        /// <summary>
        /// How a failed exchange is reported, as <see cref="LoopbackProofHandler"/> reports one: cancelled once
        /// its token is, whatever the runtime threw on the way, since closing the connection is how a cancelled
        /// read ends and Mono may fault rather than cancel; else, as other handlers report them, a connection that
        /// failed, a certificate mismatch included, or an answer too large or not HTTP; anything else, null, as
        /// it was thrown.
        /// </summary>
        public static Exception? Reported(Exception error, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return new OperationCanceledException(cancellationToken);
            return error is IOException || error is InvalidDataException || error is FormatException || error is OverflowException
                ? new HttpRequestException(error.Message, error)
                : null;
        }
    }
}

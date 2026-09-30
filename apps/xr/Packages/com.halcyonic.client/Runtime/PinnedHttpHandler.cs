#nullable enable
using System;
using System.Collections.Generic;
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
        private readonly int maxResponseBytes;

        /// <param name="pin">The SHA-256 of the control plane's certificate, in lowercase hex.</param>
        public PinnedHttpHandler(string pin, int maxResponseBytes = 16 * 1024 * 1024)
        {
            this.pin = ControlPlaneTarget.CheckPin(pin);
            this.maxResponseBytes = maxResponseBytes;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new ArgumentException("The request has no URI.", nameof(request));
            if (uri.Scheme != "https") throw new ArgumentException("A pinned request is https://.", nameof(request));
            var body = request.Content == null ? null : await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            var headers = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Host", WebSocketUpgrade.HostHeader(uri)),
                new KeyValuePair<string, string>("Connection", "close"),
            };
            foreach (var header in request.Headers)
            {
                if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(header.Key, "Connection", StringComparison.OrdinalIgnoreCase)) continue;
                headers.Add(new KeyValuePair<string, string>(header.Key, string.Join(", ", header.Value)));
            }
            if (request.Content != null)
            {
                foreach (var header in request.Content.Headers)
                {
                    if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                    headers.Add(new KeyValuePair<string, string>(header.Key, string.Join(", ", header.Value)));
                }
            }
            if (body != null || request.Method == HttpMethod.Post)
            {
                headers.Add(new KeyValuePair<string, string>("Content-Length", (body?.Length ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            Http1Head head;
            byte[] content;
            try
            {
                using var connection = await PinnedConnection.OpenAsync(WebSocketUpgrade.Host(uri), uri.Port, pin, cancellationToken).ConfigureAwait(false);
                using var closing = connection.CloseOn(cancellationToken);
                await Http1.WriteRequestAsync(connection.Stream, request.Method.Method, uri.PathAndQuery, headers, body, cancellationToken).ConfigureAwait(false);
                head = await Http1.ReadHeadAsync(connection.Stream, cancellationToken).ConfigureAwait(false);
                content = await Http1.ReadBodyAsync(connection.Stream, head, maxResponseBytes, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException error) when (!cancellationToken.IsCancellationRequested)
            {
                // As other handlers report a connection that failed, a certificate mismatch included.
                throw new HttpRequestException(error.Message, error);
            }
            var response = new HttpResponseMessage((HttpStatusCode)head.Status)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(content),
            };
            foreach (var header in head.Headers)
            {
                if (!response.Headers.TryAddWithoutValidation(header.Key, header.Value))
                {
                    response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
            return response;
        }
    }
}

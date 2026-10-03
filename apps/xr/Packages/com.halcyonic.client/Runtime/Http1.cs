#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Halcyonic.Client
{
    /// <summary>The status line and headers of an HTTP/1.1 response.</summary>
    internal sealed class Http1Head
    {
        public Http1Head(int status, IReadOnlyList<KeyValuePair<string, string>> headers)
        {
            Status = status;
            Headers = headers;
        }

        public int Status { get; }

        public IReadOnlyList<KeyValuePair<string, string>> Headers { get; }

        public string? Header(string name)
        {
            foreach (var header in Headers)
            {
                if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)) return header.Value;
            }
            return null;
        }
    }

    /// <summary>
    /// Just enough HTTP/1.1 for the pinned transports and the loopback proof: a request the
    /// connection's last, a response read by Content-Length, chunks or to the end of the stream. The
    /// head is read a byte at a time, so a WebSocket upgrade never reads into the frames that follow it.
    /// </summary>
    internal static class Http1
    {
        private const int MaxHeadBytes = 32 * 1024;

        /// <summary>
        /// The headers that send <paramref name="request"/> as its connection's last: Host with the
        /// port, Connection: close, the request's own and its content's, and Content-Length, leaving
        /// out any header named in <paramref name="dropped"/>.
        /// </summary>
        public static List<KeyValuePair<string, string>> RequestHeaders(HttpRequestMessage request, Uri uri, byte[]? body, params string[] dropped)
        {
            var headers = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Host", WebSocketUpgrade.HostHeader(uri)),
                new KeyValuePair<string, string>("Connection", "close"),
            };
            foreach (var header in request.Headers)
            {
                if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(header.Key, "Connection", StringComparison.OrdinalIgnoreCase)
                    || Array.Exists(dropped, name => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))) continue;
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
                headers.Add(new KeyValuePair<string, string>("Content-Length", (body?.Length ?? 0).ToString(CultureInfo.InvariantCulture)));
            }
            return headers;
        }

        /// <summary>The response to <paramref name="request"/>, its body read whole.</summary>
        public static HttpResponseMessage Response(HttpRequestMessage request, Http1Head head, byte[] content)
        {
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

        public static async Task WriteRequestAsync(
            Stream stream,
            string method,
            string target,
            IEnumerable<KeyValuePair<string, string>> headers,
            byte[]? body,
            CancellationToken cancellationToken)
        {
            var head = new StringBuilder();
            head.Append(method).Append(' ').Append(target).Append(" HTTP/1.1\r\n");
            foreach (var header in headers)
            {
                if (header.Key.IndexOfAny(new[] { '\r', '\n', ':' }) >= 0 || header.Value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                {
                    throw new ArgumentException("A header name or value holds a line break.", nameof(headers));
                }
                head.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
            }
            head.Append("\r\n");
            var bytes = Encoding.ASCII.GetBytes(head.ToString());
            await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
            if (body != null && body.Length > 0) await stream.WriteAsync(body, 0, body.Length, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public static async Task<Http1Head> ReadHeadAsync(Stream stream, CancellationToken cancellationToken)
        {
            var received = new List<byte>(512);
            var one = new byte[1];
            while (!EndsWithBlankLine(received))
            {
                if (received.Count >= MaxHeadBytes) throw new InvalidDataException("The response head is too long.");
                var read = await stream.ReadAsync(one, 0, 1, cancellationToken).ConfigureAwait(false);
                if (read == 0) throw new IOException("The connection closed before the response arrived.");
                received.Add(one[0]);
            }
            var text = Encoding.ASCII.GetString(received.ToArray(), 0, received.Count - 4);
            var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                if (line.IndexOf('\r') >= 0 || line.IndexOf('\n') >= 0) throw new InvalidDataException("The response head holds a bare line break.");
                if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t')) throw new InvalidDataException("The response head folds a header across lines.");
            }
            var status = lines[0].Split(' ');
            if (status.Length < 2 || !status[0].StartsWith("HTTP/1.", StringComparison.Ordinal)
                || !int.TryParse(status[1], NumberStyles.None, CultureInfo.InvariantCulture, out var code))
            {
                throw new InvalidDataException("The response does not start with an HTTP/1.1 status line.");
            }
            var headers = new List<KeyValuePair<string, string>>();
            for (var i = 1; i < lines.Length; i++)
            {
                var colon = lines[i].IndexOf(':');
                if (colon <= 0) continue;
                headers.Add(new KeyValuePair<string, string>(lines[i].Substring(0, colon).Trim(), lines[i].Substring(colon + 1).Trim()));
            }
            return new Http1Head(code, headers);
        }

        public static async Task<byte[]> ReadBodyAsync(Stream stream, Http1Head head, int maxBytes, CancellationToken cancellationToken)
        {
            var body = new MemoryStream();
            var lengths = 0;
            var encodings = 0;
            foreach (var header in head.Headers)
            {
                if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)) lengths++;
                if (string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) encodings++;
            }
            // Framing two ways, or a coding this does not read, could make the next response start
            // anywhere: refused rather than guessed.
            if (lengths > 1 || encodings > 1 || (lengths == 1 && encodings == 1))
            {
                throw new InvalidDataException("The response frames its body more than one way.");
            }
            var length = head.Header("Content-Length");
            if (encodings == 1 && !string.Equals(head.Header("Transfer-Encoding"), "chunked", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The response's transfer coding is not chunked.");
            }
            if (length != null)
            {
                if (!long.TryParse(length, NumberStyles.None, CultureInfo.InvariantCulture, out var expected) || expected > maxBytes)
                {
                    throw new InvalidDataException("The response is larger than " + maxBytes + " bytes.");
                }
                await CopyAsync(stream, body, expected, maxBytes, cancellationToken).ConfigureAwait(false);
                if (body.Length != expected) throw new IOException("The connection closed before the whole response arrived.");
            }
            else if (string.Equals(head.Header("Transfer-Encoding"), "chunked", StringComparison.OrdinalIgnoreCase))
            {
                while (true)
                {
                    var sizeLine = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
                    var semicolon = sizeLine.IndexOf(';');
                    var digits = (semicolon < 0 ? sizeLine : sizeLine.Substring(0, semicolon)).Trim();
                    // At most 8 hex digits, so a size can't overflow or read as negative.
                    if (digits.Length == 0 || digits.Length > 8
                        || !long.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var size))
                    {
                        throw new InvalidDataException("A chunk's size is not a size.");
                    }
                    if (size == 0) break;
                    if (body.Length + size > maxBytes) throw new InvalidDataException("The response is larger than " + maxBytes + " bytes.");
                    await CopyAsync(stream, body, size, maxBytes, cancellationToken).ConfigureAwait(false);
                    if ((await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false)).Length != 0)
                    {
                        throw new InvalidDataException("A chunk is longer than its size.");
                    }
                }
                // The trailer: header lines after the last chunk, up to the blank line that ends the response.
                var trailer = 0;
                while ((await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false)).Length != 0)
                {
                    if (++trailer > 64) throw new InvalidDataException("The response's trailer is too long.");
                }
            }
            else
            {
                await CopyAsync(stream, body, long.MaxValue, maxBytes, cancellationToken).ConfigureAwait(false);
            }
            return body.ToArray();
        }

        private static async Task CopyAsync(Stream from, MemoryStream to, long count, int maxBytes, CancellationToken cancellationToken)
        {
            var buffer = new byte[16 * 1024];
            var remaining = count;
            while (remaining > 0)
            {
                var read = await from.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, remaining), cancellationToken).ConfigureAwait(false);
                if (read == 0) return;
                if (to.Length + read > maxBytes) throw new InvalidDataException("The response is larger than " + maxBytes + " bytes.");
                to.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        private static async Task<string> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
        {
            var line = new StringBuilder();
            var one = new byte[1];
            while (true)
            {
                var read = await stream.ReadAsync(one, 0, 1, cancellationToken).ConfigureAwait(false);
                if (read == 0) throw new IOException("The connection closed inside a chunked response.");
                if (one[0] == '\n') return line.ToString().TrimEnd('\r');
                if (line.Length > 1024) throw new InvalidDataException("A chunk line is too long.");
                line.Append((char)one[0]);
            }
        }

        private static bool EndsWithBlankLine(List<byte> received)
        {
            var n = received.Count;
            return n >= 4 && received[n - 4] == '\r' && received[n - 3] == '\n' && received[n - 2] == '\r' && received[n - 1] == '\n';
        }
    }
}

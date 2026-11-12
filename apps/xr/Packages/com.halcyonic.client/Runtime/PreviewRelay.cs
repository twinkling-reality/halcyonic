#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Halcyonic.Client
{
    /// <summary>
    /// A project's preview, shown in the headset's browser with no cable (ADR 0052, amended 2026-11-12): this answers the
    /// browser on the headset's own loopback address, <c>http://127.0.0.1:&lt;Port&gt;/</c>, which a browser treats as a
    /// secure context, so WebXR is offered, and carries each browser connection whole to the paired computer over one
    /// pinned WebSocket (<c>/api/projects/&lt;id&gt;/preview-stream</c>), where the computer pipes it to that project's
    /// preview only when every agent's reads were confined. So the app plays as at the desk: any method, its own
    /// WebSockets, live reload. Each connection's first request must name this address as its Host and must not be a
    /// service worker's; it goes on with <c>Connection: close</c>, so the browser's next request comes on a new
    /// connection and meets both checks again (a WebSocket upgrade keeps its connection). The device credential is on
    /// the WebSocket's upgrade only, never in the browser's bytes. The headset's app must keep running while the browser
    /// is in front: the Unity layer holds an Android foreground service while a relay is open (ADR 0052).
    /// </summary>
    public sealed class PreviewRelay : IDisposable
    {
        private const int MaxHeadBytes = 16 * 1024;

        /// <summary>The computer's close codes for a stream its gate stopped (4409) or whose preview did not answer (4502).</summary>
        private const int GateClosed = 4409;
        private const int PreviewUnreachable = 4502;

        private readonly TcpListener listener;
        private readonly PairedReach reach;
        private readonly string credential;
        private readonly Uri stream;
        private readonly CancellationTokenSource stopping = new CancellationTokenSource();
        private readonly Task accepting;

        private PreviewRelay(TcpListener listener, PairedReach reach, string credential, Uri stream)
        {
            this.listener = listener;
            this.reach = reach;
            this.credential = credential;
            this.stream = stream;
            accepting = Task.Run(AcceptAsync);
        }

        /// <summary>The port on the headset's loopback address the browser opens.</summary>
        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        /// <summary>What the headset's browser opens: by address, never <c>localhost</c>.</summary>
        public Uri Url => new Uri("http://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture) + "/");

        /// <summary>
        /// Starts relaying <paramref name="projectId"/>'s preview from the computer this device paired with,
        /// on <paramref name="port"/> of the headset's loopback address (0 for any free one).
        /// </summary>
        public static PreviewRelay Start(ControlPlaneTarget target, string projectId, int port = 0)
        {
            var pairing = target.Pairing ?? throw new InvalidOperationException("A preview is relayed only from a paired computer.");
            if (!Guid.TryParse(projectId, out _)) throw new ArgumentException("A project id is required.", nameof(projectId));
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            var reach = PairedReach.For(pairing);
            var stream = new UriBuilder("wss", reach.Typed.Contains(":") ? "[" + reach.Typed.Trim('[', ']') + "]" : reach.Typed, reach.Port, "/api/projects/" + projectId + "/preview-stream").Uri;
            return new PreviewRelay(listener, reach, pairing.Credential, stream);
        }

        public void Dispose()
        {
            stopping.Cancel();
            listener.Stop();
            try
            {
                accepting.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // Stopping ends the accept loop by throwing.
            }
        }

        private async Task AcceptAsync()
        {
            while (!stopping.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception) when (stopping.IsCancellationRequested)
                {
                    return;
                }
                catch (SocketException)
                {
                    continue;
                }
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                var browser = client.GetStream();
                try
                {
                    RequestHead? head;
                    using (var waiting = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token))
                    {
                        waiting.CancelAfter(TimeSpan.FromSeconds(30));
                        head = await ReadRequestHeadAsync(browser, waiting.Token).ConfigureAwait(false);
                    }
                    if (head == null) return;
                    // A page elsewhere that renamed itself to this address cannot read through it.
                    if (!string.Equals(head.Header("Host"), "127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                    {
                        await WriteAsync(browser, 403, "Forbidden", Plain("Open the preview at its address."), head.Method, stopping.Token).ConfigureAwait(false);
                        return;
                    }
                    if (head.Header("Service-Worker") != null)
                    {
                        await WriteAsync(browser, 403, "Forbidden", Plain("A preview is not relayed as a service worker."), head.Method, stopping.Token).ConfigureAwait(false);
                        return;
                    }
                    await CarryAsync(browser, head).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // A browser that went away, or a computer that did not answer: this connection ends.
                }
            }
        }

        /// <summary>The browser's connection, from its first request on, carried both ways until either side ends it.</summary>
        private async Task CarryAsync(NetworkStream browser, RequestHead head)
        {
            PinnedConnection connection;
            WebSocket tunnel;
            try
            {
                var (opened, host) = await reach.OpenAsync(stopping.Token).ConfigureAwait(false);
                connection = opened;
                try
                {
                    using (connection.CloseOn(stopping.Token))
                    {
                        tunnel = await WebSocketUpgrade.ConnectAsync(
                            connection.Stream,
                            PairedReach.At(stream, host),
                            new[] { new KeyValuePair<string, string>("Authorization", "Bearer " + credential) },
                            stopping.Token).ConfigureAwait(false);
                    }
                }
                catch
                {
                    connection.Dispose();
                    throw;
                }
            }
            catch (UpgradeRefusedException refused)
            {
                // The computer's refusal, said in the product's words by its code alone, never its message.
                var code = refused.Status == 401 || refused.Status == 403 ? "not_paired" : refused.Code;
                await WriteAsync(browser, refused.Status, "Refused", Plain(WhyNot(code)), head.Method, stopping.Token).ConfigureAwait(false);
                return;
            }
            catch (Exception) when (!stopping.IsCancellationRequested)
            {
                await WriteAsync(browser, 502, "Bad Gateway", Plain("Your computer isn't answering."), head.Method, stopping.Token).ConfigureAwait(false);
                return;
            }
            using (connection)
            using (tunnel)
            using (var ending = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token))
            {
                var first = Encoding.ASCII.GetBytes(head.Forwarded(closeAfter: !head.IsWebSocketUpgrade));
                await tunnel.SendAsync(new ArraySegment<byte>(first), WebSocketMessageType.Binary, true, ending.Token).ConfigureAwait(false);
                var answered = false;
                var up = Task.Run(async () =>
                {
                    var buffer = new byte[16 * 1024];
                    while (true)
                    {
                        var read = await browser.ReadAsync(buffer, 0, buffer.Length, ending.Token).ConfigureAwait(false);
                        if (read == 0) return;
                        await tunnel.SendAsync(new ArraySegment<byte>(buffer, 0, read), WebSocketMessageType.Binary, true, ending.Token).ConfigureAwait(false);
                    }
                });
                var down = Task.Run(async () =>
                {
                    var buffer = new byte[16 * 1024];
                    while (true)
                    {
                        var received = await tunnel.ReceiveAsync(new ArraySegment<byte>(buffer), ending.Token).ConfigureAwait(false);
                        if (received.MessageType == WebSocketMessageType.Close) return;
                        answered = true;
                        await browser.WriteAsync(buffer, 0, received.Count, ending.Token).ConfigureAwait(false);
                    }
                });
                // The stream ends when the computer's side ends, or soon after the browser's: a browser that has sent all
                // it will gets two seconds for an answer still coming, and one that closed frees its stream at once
                // after, so a page that reloads never holds streams it no longer uses.
                try
                {
                    var over = await Task.WhenAny(down, up).ConfigureAwait(false);
                    if (over == up) await Task.WhenAny(down, Task.Delay(TimeSpan.FromSeconds(2), ending.Token)).ConfigureAwait(false);
                    if (down.IsFaulted) await down.ConfigureAwait(false);
                }
                catch (Exception) when (!stopping.IsCancellationRequested)
                {
                    // The browser went away mid-answer, or the computer dropped the stream.
                }
                finally
                {
                    ending.Cancel();
                }
                try
                {
                    await up.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Cancelled as the stream ended.
                }
                // A stream the computer ended before any byte came back: why, in the product's words.
                var closed = (int?)tunnel.CloseStatus;
                if (!answered && (closed == GateClosed || closed == PreviewUnreachable))
                {
                    var code = closed == PreviewUnreachable ? "preview_unreachable" : tunnel.CloseStatusDescription;
                    await WriteAsync(browser, closed == PreviewUnreachable ? 502 : 409, "Refused", Plain(WhyNot(code)), head.Method, stopping.Token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>Why the computer shows no preview here, by its refusal's code, in the product's words.</summary>
        public static string WhyNot(string? code) => code switch
        {
            "preview_not_confined" => "This app stays on your computer. It may read outside its folder.",
            "preview_not_passed" => "This preview isn't shown. Its check did not pass.",
            "preview_not_latest" => "This preview is out of date. Check the app again on your computer.",
            "preview_not_running" => "No preview is running. Start it on your computer, then open it again.",
            "preview_too_large" => "That file is too large to show.",
            "not_paired" => "This headset isn't paired with your computer any more. Pair it again.",
            "preview_unreachable" => "The preview isn't answering. Try again.",
            "too_many_streams" => "This app opened too many connections at once. Reload it.",
            // A code with no words here: unknown, said plainly, never another case's words (AUDIT's census, 2026-11-12).
            _ => "Your computer didn't show this preview. Reason unknown.",
        };

        private static (List<KeyValuePair<string, string>> Headers, byte[] Body) Plain(string text) =>
            (new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("Content-Type", "text/plain; charset=utf-8") }, Encoding.UTF8.GetBytes(text));

        private static async Task WriteAsync(
            Stream stream,
            int status,
            string reason,
            (List<KeyValuePair<string, string>> Headers, byte[] Body) answer,
            string method,
            CancellationToken cancellationToken)
        {
            var text = new StringBuilder();
            text.Append("HTTP/1.1 ").Append(status.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(Clean(reason)).Append("\r\n");
            foreach (var header in answer.Headers) text.Append(header.Key).Append(": ").Append(Clean(header.Value)).Append("\r\n");
            text.Append("Content-Length: ").Append(answer.Body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            // The headset's browser keeps nothing across previews that share this address.
            text.Append("Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n");
            var bytes = Encoding.ASCII.GetBytes(text.ToString());
            await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
            if (method != "HEAD") await stream.WriteAsync(answer.Body, 0, answer.Body.Length, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>A header value as written: no line breaks, so nothing can add a header of its own.</summary>
        private static string Clean(string value) => new string(value.Where(c => c >= ' ' && c != '\u007f').ToArray());

        private static async Task<RequestHead?> ReadRequestHeadAsync(Stream stream, CancellationToken cancellationToken)
        {
            var received = new List<byte>(512);
            var one = new byte[1];
            while (received.Count < MaxHeadBytes)
            {
                var read = await stream.ReadAsync(one, 0, 1, cancellationToken).ConfigureAwait(false);
                if (read == 0) return null;
                received.Add(one[0]);
                var n = received.Count;
                if (n >= 4 && received[n - 4] == '\r' && received[n - 3] == '\n' && received[n - 2] == '\r' && received[n - 1] == '\n')
                {
                    return RequestHead.Parse(Encoding.ASCII.GetString(received.ToArray(), 0, n - 4));
                }
            }
            return null;
        }

        private sealed class RequestHead
        {
            private readonly Dictionary<string, string> headers;
            private readonly string requestLine;
            private readonly IReadOnlyList<string> lines;

            private RequestHead(string method, string target, Dictionary<string, string> headers, string requestLine, IReadOnlyList<string> lines)
            {
                Method = method;
                Target = target;
                this.headers = headers;
                this.requestLine = requestLine;
                this.lines = lines;
            }

            public string Method { get; }

            public string Target { get; }

            public string? Header(string name) => headers.TryGetValue(name, out var value) ? value : null;

            /// <summary>A WebSocket's opening request, which keeps its connection for the socket's life.</summary>
            public bool IsWebSocketUpgrade => string.Equals(Header("Upgrade"), "websocket", StringComparison.OrdinalIgnoreCase);

            /// <summary>
            /// The head as it goes on, with <c>Connection: close</c> in place of the browser's own where
            /// <paramref name="closeAfter"/>, so the app's server answers once and the browser's next request comes on a
            /// connection of its own.
            /// </summary>
            public string Forwarded(bool closeAfter)
            {
                var text = new StringBuilder(requestLine).Append("\r\n");
                foreach (var line in lines)
                {
                    var name = line.Substring(0, line.IndexOf(':')).Trim();
                    if (closeAfter && (name.Equals("Connection", StringComparison.OrdinalIgnoreCase) || name.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)))
                        continue;
                    text.Append(line).Append("\r\n");
                }
                if (closeAfter) text.Append("Connection: close\r\n");
                return text.Append("\r\n").ToString();
            }

            public static RequestHead? Parse(string text)
            {
                var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
                var start = lines[0].Split(' ');
                if (start.Length != 3 || !start[2].StartsWith("HTTP/1.", StringComparison.Ordinal)) return null;
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in lines.Skip(1))
                {
                    var colon = line.IndexOf(':');
                    if (colon <= 0) return null;
                    headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
                }
                return new RequestHead(start[0], start[1], headers, lines[0], lines.Skip(1).ToList());
            }
        }
    }
}

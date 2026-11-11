#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Halcyonic.Client
{
    /// <summary>
    /// A project's preview, shown in the headset's browser with no cable (ADR 0052): this answers the
    /// browser on the headset's own loopback address, <c>http://127.0.0.1:&lt;Port&gt;/</c>, which a browser
    /// treats as a secure context, so WebXR is offered, and asks the paired computer for each file over
    /// the pinned connection (<c>/api/projects/&lt;id&gt;/preview/...</c>), where the computer relays it from
    /// that project's preview only when every agent's reads were confined. Only GET and HEAD, one request
    /// a connection; the device credential never reaches the browser, and nothing but the file's own
    /// description comes back. The headset's app must keep running while the browser is in front: the
    /// Unity layer holds an Android foreground service while a relay is open (ADR 0052).
    /// </summary>
    public sealed class PreviewRelay : IDisposable
    {
        /// <summary>The largest file relayed, as the computer's relay allows.</summary>
        public const int MaxFileBytes = 32 * 1024 * 1024;

        private const int MaxHeadBytes = 16 * 1024;

        private static readonly string[] PassedRequestHeaders = { "Accept", "Accept-Language", "Range", "If-None-Match", "If-Modified-Since" };

        private static readonly string[] PassedResponseHeaders = { "Content-Type", "Content-Range", "Accept-Ranges", "ETag", "Last-Modified" };

        private readonly TcpListener listener;
        private readonly HttpMessageInvoker invoker;
        private readonly string credential;
        private readonly Uri preview;
        private readonly CancellationTokenSource stopping = new CancellationTokenSource();
        private readonly Task accepting;

        private PreviewRelay(TcpListener listener, HttpMessageHandler handler, string credential, Uri preview)
        {
            this.listener = listener;
            invoker = new HttpMessageInvoker(handler, disposeHandler: true);
            this.credential = credential;
            this.preview = preview;
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
            var preview = new UriBuilder("https", reach.Typed.Contains(":") ? "[" + reach.Typed.Trim('[', ']') + "]" : reach.Typed, reach.Port, "/api/projects/" + projectId + "/preview").Uri;
            return new PreviewRelay(listener, new PinnedHttpHandler(reach, MaxFileBytes), pairing.Credential, preview);
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
            invoker.Dispose();
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
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var stream = client.GetStream();
                try
                {
                    var head = await ReadRequestHeadAsync(stream, timeout.Token).ConfigureAwait(false);
                    if (head == null) return;
                    await RespondAsync(stream, head, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // A browser that went away, or a computer that did not answer: this connection ends.
                }
            }
        }

        private async Task RespondAsync(Stream stream, RequestHead head, CancellationToken cancellationToken)
        {
            if (head.Method != "GET" && head.Method != "HEAD")
            {
                await WriteAsync(stream, 405, "Method Not Allowed", Plain("A preview is only read."), head.Method, cancellationToken).ConfigureAwait(false);
                return;
            }
            // A page elsewhere that renamed itself to this address cannot read through it.
            if (!string.Equals(head.Header("Host"), "127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                await WriteAsync(stream, 403, "Forbidden", Plain("Open the preview at its address."), head.Method, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (head.Header("Service-Worker") != null)
            {
                await WriteAsync(stream, 403, "Forbidden", Plain("A preview is not relayed as a service worker."), head.Method, cancellationToken).ConfigureAwait(false);
                return;
            }
            // Only a path inside the preview: a URI collapses dot segments, so a target climbing out of it,
            // in any spelling, would reach the computer's other routes with this device's credential (AUDIT2).
            var target = InsidePreview(head.Target);
            if (target == null)
            {
                await WriteAsync(stream, 400, "Bad Request", Plain("That is not a path in this preview."), head.Method, cancellationToken).ConfigureAwait(false);
                return;
            }
            using var request = new HttpRequestMessage(new HttpMethod(head.Method), target);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + credential);
            foreach (var name in PassedRequestHeaders)
            {
                var value = head.Header(name);
                if (value != null) request.Headers.TryAddWithoutValidation(name, value);
            }
            HttpResponseMessage answer;
            try
            {
                answer = await invoker.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                await WriteAsync(stream, 502, "Bad Gateway", Plain("Your computer isn't answering."), head.Method, cancellationToken).ConfigureAwait(false);
                return;
            }
            using (answer)
            {
                var body = await answer.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                // The computer's refusal, said in the product's words by its code alone, never its message.
                var status = (int)answer.StatusCode;
                if (status == 401 || status == 403 || status == 409 || status == 502)
                {
                    var code = status == 401 || status == 403 ? "not_paired" : CodeOf(body);
                    await WriteAsync(stream, status, answer.ReasonPhrase ?? "", Plain(WhyNot(code)), head.Method, cancellationToken).ConfigureAwait(false);
                    return;
                }
                var headers = new List<KeyValuePair<string, string>>();
                foreach (var name in PassedResponseHeaders)
                {
                    if (answer.Headers.TryGetValues(name, out var values) || answer.Content.Headers.TryGetValues(name, out values))
                    {
                        headers.Add(new KeyValuePair<string, string>(name, string.Join(", ", values)));
                    }
                }
                await WriteAsync(stream, (int)answer.StatusCode, answer.ReasonPhrase ?? "", (headers, body), head.Method, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The preview's URI for a request target, or null unless its path stays inside the preview: no <c>.</c>
        /// or <c>..</c> segment, no backslash and no escaped dot, slash or backslash in the path, and, once built,
        /// a path that is the preview's own or below it.
        /// </summary>
        public Uri? InsidePreview(string requestTarget)
        {
            if (!requestTarget.StartsWith("/", StringComparison.Ordinal) || requestTarget.Length > 2048) return null;
            var question = requestTarget.IndexOf('?');
            var path = question < 0 ? requestTarget : requestTarget.Substring(0, question);
            if (path.IndexOf('\\') >= 0) return null;
            foreach (var escaped in new[] { "%2e", "%2f", "%5c" })
            {
                if (path.IndexOf(escaped, StringComparison.OrdinalIgnoreCase) >= 0) return null;
            }
            if (path.Split('/').Any(segment => segment == "." || segment == "..")) return null;
            var uri = new Uri(preview + requestTarget);
            var root = preview.AbsolutePath;
            return uri.AbsolutePath == root || uri.AbsolutePath.StartsWith(root + "/", StringComparison.Ordinal) ? uri : null;
        }

        /// <summary>Why the computer shows no preview here, by its refusal's code, in the product's words.</summary>
        public static string WhyNot(string? code) => code switch
        {
            "preview_not_confined" => "This app stays on your computer. Its agents could read outside its folder.",
            "preview_not_passed" => "This preview isn't shown. Its check did not pass.",
            "preview_not_latest" => "This preview is out of date. Check the app again on your computer.",
            "preview_not_running" => "No preview is running. Start it on your computer, then open it again.",
            "preview_too_large" => "That file is too large to show.",
            "not_paired" => "This headset isn't paired with your computer any more. Pair it again.",
            _ => "The preview isn't answering. Try again.",
        };

        private static string? CodeOf(byte[] body)
        {
            try
            {
                return Newtonsoft.Json.Linq.JObject.Parse(Encoding.UTF8.GetString(body))["error"]?.Value<string>("code");
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return null;
            }
        }

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

            private RequestHead(string method, string target, Dictionary<string, string> headers)
            {
                Method = method;
                Target = target;
                this.headers = headers;
            }

            public string Method { get; }

            public string Target { get; }

            public string? Header(string name) => headers.TryGetValue(name, out var value) ? value : null;

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
                return new RequestHead(start[0], start[1], headers);
            }
        }
    }
}

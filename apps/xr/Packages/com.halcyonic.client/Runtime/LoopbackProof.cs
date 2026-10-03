#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Halcyonic.Client
{
    /// <summary>What asking a control plane on loopback for its proof found.</summary>
    public enum LoopbackProofOutcome
    {
        /// <summary>It proved it holds the access token, for the address and port that were dialled.</summary>
        Proved,

        /// <summary>Something answered and did not prove it.</summary>
        Unproved,

        /// <summary>Nothing answered in time.</summary>
        Unreachable,

        /// <summary>The address is not a literal loopback one, so nothing was asked.</summary>
        NotLoopback,
    }

    /// <summary>
    /// The control plane's proof that it holds the access token, asked before this device sends the
    /// token on loopback, as over USB through <c>adb reverse</c> (docs/internal/architecture/SECURITY.md).
    /// While the control plane is stopped another program may listen on its port, on the computer or
    /// on the headset; it can't make the proof, so it gets no token. The client sends a fresh 32-byte
    /// challenge to the public health check, and only the loopback listener answers, with an
    /// HMAC-SHA256 under the token of a fixed label, the address and port the connection reached, and
    /// the challenge; the client checks it against the address and port it dialled. The control plane's
    /// own command-line clients ask the same (apps/control-plane/src/http/security.ts). Connections
    /// are opened here, to the literal address, through no proxy and never from a pool, so a
    /// connection another program kept open is never reused.
    /// </summary>
    public static class LoopbackProof
    {
        public const string ChallengeHeader = "x-halcyonic-challenge";
        public const string ProofHeader = "x-halcyonic-proof";

        /// <summary>How long connecting and the proof may take, as the command-line clients allow.</summary>
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

        private const string Label = "halcyonic loopback proof v2\n";
        private const int MaxHealthBytes = 64 * 1024;
        private static readonly Regex Sha256Hex = new Regex(@"^[0-9a-f]{64}\z", RegexOptions.CultureInvariant);

        /// <summary>
        /// The address and port <paramref name="uri"/> dials as the proof names them, 127.0.0.1:47800 or
        /// [::1]:47800; null unless its host is exactly 127.0.0.1 or ::1, the only addresses the token
        /// goes to. Another address in 127.0.0.0/8, or 127.0.0.1 written as IPv6, is refused.
        /// </summary>
        public static string? AddressOf(Uri uri)
        {
            if (!IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address)
                || !(address.Equals(IPAddress.Loopback) || address.Equals(IPAddress.IPv6Loopback))) return null;
            var port = uri.Port.ToString(CultureInfo.InvariantCulture);
            return address.AddressFamily == AddressFamily.InterNetworkV6 ? "[" + address + "]:" + port : address + ":" + port;
        }

        /// <summary>The proof for <paramref name="challenge"/> at <paramref name="address"/>, in lowercase hex.</summary>
        public static string Compute(string token, string address, string challenge)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(token));
            return Hex(hmac.ComputeHash(Encoding.UTF8.GetBytes(Label + address + "\n" + challenge)));
        }

        /// <summary>
        /// Whether the control plane at <paramref name="uri"/>'s address and port proves it holds
        /// <paramref name="token"/>, asked on a connection of its own that is closed afterwards, as
        /// before a WebSocket upgrade, which opens its own. The token is not sent.
        /// </summary>
        public static async Task<LoopbackProofOutcome> AskAsync(Uri uri, string token, CancellationToken cancellationToken)
        {
            var address = AddressOf(uri);
            if (address == null) return LoopbackProofOutcome.NotLoopback;
            var proved = await ProveAsync(uri, address, token, cancellationToken).ConfigureAwait(false);
            proved.Connection?.Dispose();
            return proved.Outcome;
        }

        /// <summary>
        /// Opens a connection to <paramref name="uri"/>'s literal address and asks for the proof on it,
        /// within <see cref="Timeout"/>. When it is proved and the server keeps the connection open, the
        /// connection is returned for the request that follows; otherwise it is closed.
        /// </summary>
        internal static async Task<(LoopbackProofOutcome Outcome, TcpClient? Connection)> ProveAsync(
            Uri uri,
            string address,
            string token,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TcpClient? client = null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(Timeout);
                var target = IPAddress.Parse(uri.Host.Trim('[', ']'));
                client = new TcpClient(target.AddressFamily) { NoDelay = true };
                var opened = client;
                using (timeout.Token.Register(() => opened.Dispose()))
                {
                    await client.ConnectAsync(target, uri.Port).ConfigureAwait(false);
                    var stream = client.GetStream();
                    var challenge = NewChallenge();
                    var headers = new[]
                    {
                        new KeyValuePair<string, string>("Host", WebSocketUpgrade.HostHeader(uri)),
                        new KeyValuePair<string, string>(ChallengeHeader, challenge),
                    };
                    await Http1.WriteRequestAsync(stream, "GET", "/api/health", headers, null, timeout.Token).ConfigureAwait(false);
                    var head = await Http1.ReadHeadAsync(stream, timeout.Token).ConfigureAwait(false);
                    var keptOpen = !string.Equals(head.Header("Connection"), "close", StringComparison.OrdinalIgnoreCase)
                        && (head.Header("Content-Length") != null
                            || string.Equals(head.Header("Transfer-Encoding"), "chunked", StringComparison.OrdinalIgnoreCase));
                    await Http1.ReadBodyAsync(stream, head, MaxHealthBytes, timeout.Token).ConfigureAwait(false);
                    var proofs = head.Headers
                        .Where(header => string.Equals(header.Key, ProofHeader, StringComparison.OrdinalIgnoreCase))
                        .Select(header => header.Value)
                        .ToArray();
                    if (proofs.Length != 1 || !Sha256Hex.IsMatch(proofs[0])) return Closed(LoopbackProofOutcome.Unproved, client);
                    var expected = Encoding.ASCII.GetBytes(Compute(token, address, challenge));
                    if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(proofs[0]), expected))
                    {
                        return Closed(LoopbackProofOutcome.Unproved, client);
                    }
                    // Proved, but closing: the request can't follow on this connection, and no other carries it.
                    if (!keptOpen) return Closed(LoopbackProofOutcome.Unreachable, client);
                }
                return (LoopbackProofOutcome.Proved, client);
            }
            catch (InvalidDataException) when (!cancellationToken.IsCancellationRequested)
            {
                // Something answered, but not with HTTP a control plane would send.
                return Closed(LoopbackProofOutcome.Unproved, client);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return Closed(LoopbackProofOutcome.Unreachable, client);
            }
            catch
            {
                // The caller cancelled; closing the connection is how that ends a read.
                client?.Dispose();
                throw new OperationCanceledException(cancellationToken);
            }
        }

        private static (LoopbackProofOutcome Outcome, TcpClient? Connection) Closed(LoopbackProofOutcome outcome, TcpClient? client)
        {
            client?.Dispose();
            return (outcome, null);
        }

        /// <summary>A fresh challenge: 32 random bytes in lowercase hex.</summary>
        private static string NewChallenge()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Hex(bytes);
        }

        private static string Hex(IEnumerable<byte> bytes)
        {
            var text = new StringBuilder();
            foreach (var value in bytes) text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }

    /// <summary>
    /// The access token was not sent: what answered did not prove it holds it, nothing answered, or the
    /// address is not a literal loopback one. An <see cref="HttpRequestException"/>, so it reads as a
    /// control plane that could not be reached, which it is, for this token.
    /// </summary>
    public sealed class TokenNotSentException : HttpRequestException
    {
        public TokenNotSentException(LoopbackProofOutcome outcome, Uri uri)
            : base(Describe(outcome, uri))
        {
            Outcome = outcome;
        }

        public LoopbackProofOutcome Outcome { get; }

        /// <summary>
        /// Why, as the detail a person may read after "Can't reach your computer", so in their words:
        /// the access token is the access code (WORDS.md).
        /// </summary>
        private static string Describe(LoopbackProofOutcome outcome, Uri uri)
        {
            var at = LoopbackProof.AddressOf(uri) ?? uri.Authority;
            switch (outcome)
            {
                case LoopbackProofOutcome.Unproved:
                    return "Something answers at " + at + " but can't prove it holds the access code, so the code was not sent. It may be another program listening while Halcyonic is stopped.";
                case LoopbackProofOutcome.NotLoopback:
                    return "The access code goes only to 127.0.0.1 or [::1], so it was not sent to " + at + ". Name one of those instead.";
                default:
                    return "Nothing answered at " + at + ", so the access code was not sent.";
            }
        }
    }

    /// <summary>
    /// The REST API of a control plane on loopback with the access token: each request gets a
    /// connection of its own to the literal address, through no proxy, on which the control plane
    /// first proves it holds the token (<see cref="LoopbackProof"/>); then the request, with the token,
    /// follows on that same connection as its last, so only what just proved itself receives the token,
    /// and no connection is reused. A redirect comes back as it is, never followed. Throws
    /// <see cref="TokenNotSentException"/> instead of sending the token. A paired control plane is
    /// proved by its pinned certificate instead (<see cref="PinnedHttpHandler"/>).
    /// </summary>
    public sealed class LoopbackProofHandler : HttpMessageHandler
    {
        private readonly string accessToken;
        private readonly int maxResponseBytes;

        public LoopbackProofHandler(string accessToken, int maxResponseBytes = 16 * 1024 * 1024)
        {
            this.accessToken = accessToken;
            this.maxResponseBytes = maxResponseBytes;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new ArgumentException("The request has no URI.", nameof(request));
            if (uri.Scheme != "http") throw new ArgumentException("A loopback request is http://.", nameof(request));
            var address = LoopbackProof.AddressOf(uri) ?? throw new TokenNotSentException(LoopbackProofOutcome.NotLoopback, uri);
            var body = request.Content == null ? null : await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            // Whatever the caller set as authorization is left out; the token goes only after the proof.
            var headers = Http1.RequestHeaders(request, uri, body, "Authorization");
            headers.Add(new KeyValuePair<string, string>("Authorization", "Bearer " + accessToken));

            var proved = await LoopbackProof.ProveAsync(uri, address, accessToken, cancellationToken).ConfigureAwait(false);
            if (proved.Outcome != LoopbackProofOutcome.Proved || proved.Connection == null) throw new TokenNotSentException(proved.Outcome, uri);
            Http1Head head;
            byte[] content;
            try
            {
                using var connection = proved.Connection;
                using var closing = cancellationToken.Register(() => connection.Dispose());
                var stream = connection.GetStream();
                await Http1.WriteRequestAsync(stream, request.Method.Method, uri.PathAndQuery, headers, body, cancellationToken).ConfigureAwait(false);
                head = await Http1.ReadHeadAsync(stream, cancellationToken).ConfigureAwait(false);
                content = await Http1.ReadBodyAsync(stream, head, maxResponseBytes, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                // Closing the connection is how a cancelled read ends; say it was cancelled.
                throw new OperationCanceledException(cancellationToken);
            }
            catch (Exception error) when (error is IOException || error is SocketException || error is ObjectDisposedException || error is InvalidDataException || error is FormatException || error is OverflowException)
            {
                // A connection that failed, or an answer too large or not HTTP: one that could not be read.
                throw new HttpRequestException(error.Message, error);
            }
            return Http1.Response(request, head, content);
        }
    }
}

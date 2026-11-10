#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// Where a paired control plane can be reached (ADR 0017): the address the person typed, the
    /// addresses the control plane last said it has, its <c>.local</c> name first among them, and the
    /// one that answered last. A connection tries them in that order, each under the same pin, so the
    /// computer found at a new address on the network is used with nothing to do, while anything else
    /// there gets no more than a TLS handshake it cannot complete. One per pairing, shared by the
    /// realtime session and the REST client.
    /// </summary>
    public sealed class PairedReach
    {
        /// <summary>The most addresses kept from what a control plane says.</summary>
        public const int MaxAddresses = 8;

        private static readonly ConcurrentDictionary<string, PairedReach> Known = new ConcurrentDictionary<string, PairedReach>(StringComparer.Ordinal);
        private static readonly Regex LocalName = new Regex("^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\\.)+local$", RegexOptions.CultureInvariant);

        private readonly object gate = new object();
        private readonly string credential;
        private IReadOnlyList<string> addresses;
        private FilePairingStore? store;
        private string? answered;
        private DateTime learnedAt = DateTime.MinValue;
        private Task? learning;

        private PairedReach(PairedControlPlane pairing)
        {
            Typed = pairing.Host;
            Port = pairing.Port;
            Pin = pairing.CertificateSha256;
            DeviceId = pairing.DeviceId;
            credential = pairing.Credential;
            addresses = pairing.Addresses;
            store = pairing.Source;
        }

        /// <summary>The address the person typed when pairing.</summary>
        public string Typed { get; }

        public int Port { get; }

        /// <summary>What every connection's certificate must hash to.</summary>
        public string Pin { get; }

        public string DeviceId { get; }

        /// <summary>How long one address may take to connect while another is left to try.</summary>
        public TimeSpan AddressTimeout { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>How long after asking the control plane where it is before asking again.</summary>
        public TimeSpan LearnEvery { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>The address that answered last, while this app runs; null before.</summary>
        public string? Answered
        {
            get
            {
                lock (gate) return answered;
            }
        }

        /// <summary>The asking under way or last finished, for tests; null before any.</summary>
        public Task? Learning
        {
            get
            {
                lock (gate) return learning;
            }
        }

        /// <summary>
        /// The reach of this pairing, shared by everything this app opens to it, with what the pairing
        /// knows now merged in. Another pairing, even of the same device, gets its own.
        /// </summary>
        public static PairedReach For(PairedControlPlane pairing)
        {
            var key = pairing.DeviceId + "\n" + pairing.CertificateSha256 + "\n" + pairing.Host + "\n" + pairing.Port + "\n" + pairing.Credential;
            var reach = Known.GetOrAdd(key, _ => new PairedReach(pairing));
            reach.Merge(pairing);
            return reach;
        }

        /// <summary>
        /// What to try, in order: the address that answered last, the typed one, then what the control
        /// plane said, each once.
        /// </summary>
        public IReadOnlyList<string> Candidates()
        {
            lock (gate)
            {
                var all = new List<string>();
                if (answered != null) all.Add(answered);
                all.Add(Typed);
                all.AddRange(addresses);
                return all.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        /// <summary>
        /// Opens a pinned connection to the first candidate that completes the handshake with the pin,
        /// and says which it was. When none does, the most telling failure: a certificate other than the
        /// pin, else something that answered without TLS, else the typed address's failure.
        /// </summary>
        public async Task<(PinnedConnection Connection, string Host)> OpenAsync(CancellationToken cancellationToken)
        {
            var candidates = Candidates();
            Exception? first = null;
            CertificateMismatchException? mismatch = null;
            HandshakeFailedException? handshake = null;
            for (var index = 0; index < candidates.Count; index++)
            {
                var host = candidates[index];
                using var slice = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (index < candidates.Count - 1) slice.CancelAfter(AddressTimeout);
                try
                {
                    var connection = await PinnedConnection.OpenAsync(host.Trim('[', ']'), Port, Pin, slice.Token).ConfigureAwait(false);
                    lock (gate) answered = host;
                    return (connection, host);
                }
                catch (Exception error) when (!cancellationToken.IsCancellationRequested)
                {
                    if (error is CertificateMismatchException seen) mismatch ??= seen;
                    else if (error is HandshakeFailedException failed) handshake ??= failed;
                    else if (error is OperationCanceledException) error = new System.IO.IOException("Nothing answered at " + UriHost(host) + ":" + Port + " in time.", error);
                    first ??= error;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            throw (Exception?)mismatch ?? (Exception?)handshake ?? first ?? new System.IO.IOException("No address to try.");
        }

        /// <summary>The same URI at the address that answered, for the Host header a request must carry.</summary>
        public static Uri At(Uri uri, string host) => new UriBuilder(uri) { Host = UriHost(host) }.Uri;

        /// <summary>
        /// Asks the control plane, in the background, where else it can be reached, unless it was asked
        /// lately or is being asked now; what it says is kept with the pairing for the next start.
        /// </summary>
        public void LearnSoon()
        {
            lock (gate)
            {
                if (learning != null && !learning.IsCompleted) return;
                if (DateTime.UtcNow - learnedAt < LearnEvery) return;
                learnedAt = DateTime.UtcNow;
                learning = Task.Run(LearnAsync);
            }
        }

        /// <summary>
        /// Takes what a control plane said as the addresses to try: IP addresses and <c>.local</c> names
        /// only, at most <see cref="MaxAddresses"/>, in its order, and keeps them with the pairing.
        /// </summary>
        public void Learned(IEnumerable<string> said)
        {
            var usable = Usable(said);
            FilePairingStore? keep;
            lock (gate)
            {
                if (usable.SequenceEqual(addresses, StringComparer.Ordinal)) return;
                addresses = usable;
                keep = store;
            }
            keep?.Remember(DeviceId, credential, Pin, usable);
        }

        /// <summary>What may be kept of addresses said or stored: IPs and <c>.local</c> names, each once.</summary>
        public static IReadOnlyList<string> Usable(IEnumerable<string>? said)
        {
            if (said == null) return Array.Empty<string>();
            return said
                .Where(address => address != null)
                .Select(address => address.Trim().Trim('[', ']').ToLowerInvariant())
                .Where(address => address.Length > 0 && address.Length <= 253 && (IsAddress(address) || LocalName.IsMatch(address)))
                .Distinct(StringComparer.Ordinal)
                .Take(MaxAddresses)
                .ToList();
        }

        /// <summary>
        /// An IP address in a form a computer reports it: IPv6 with its colons, or IPv4 as four dotted parts,
        /// never the short forms .NET also parses, such as "10.1" for 10.0.0.1.
        /// </summary>
        private static bool IsAddress(string address) =>
            IPAddress.TryParse(address, out _) && (address.Contains(":") || address.Split('.').Length == 4);

        private async Task LearnAsync()
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var invoker = new HttpMessageInvoker(new PinnedHttpHandler(this, 64 * 1024), disposeHandler: true);
                var uri = new UriBuilder("https", UriHost(Typed), Port, "/api/device/listener").Uri;
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + credential);
                using var response = await invoker.SendAsync(request, timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return;
                var listener = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                // Another port is another listener: what it says is not where this pairing reaches.
                if (listener.Value<int?>("port") != Port || !(listener["addresses"] is JArray said)) return;
                Learned(said.Values<string>().Where(address => address != null).Select(address => address!));
            }
            catch (Exception)
            {
                // Asked again at the next connection after LearnEvery; nothing here is the person's to know.
            }
        }

        private void Merge(PairedControlPlane pairing)
        {
            lock (gate)
            {
                if (pairing.Source != null) store = pairing.Source;
                if (pairing.Addresses.Count > 0) addresses = pairing.Addresses;
            }
        }

        private static string UriHost(string host) => host.Contains(":") && !host.StartsWith("[", StringComparison.Ordinal) ? "[" + host + "]" : host;
    }
}

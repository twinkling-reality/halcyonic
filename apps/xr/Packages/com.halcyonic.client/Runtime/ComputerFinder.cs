#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Halcyonic.Client
{
    /// <summary>A computer that answered on the Wi-Fi while its pairing was open (ADR 0017, amendment of 2026-11-11).</summary>
    public sealed class FoundComputer
    {
        public FoundComputer(string name, string host, int port, IReadOnlyList<string> addresses, string? pinPrefix, bool sharesItsName = false)
        {
            Name = name;
            Host = host;
            Port = port;
            Addresses = addresses;
            PinPrefix = pinPrefix;
            SharesItsName = sharesItsName;
        }

        /// <summary>The computer's name as it announces itself, for the person to choose by.</summary>
        public string Name { get; }

        /// <summary>Its <c>.local</c> name, the address pairing types for the person.</summary>
        public string Host { get; }

        public int Port { get; }

        /// <summary>The IPv4 addresses it answered with, kept with the pairing for finding it again.</summary>
        public IReadOnlyList<string> Addresses { get; }

        /// <summary>
        /// The first 16 hex of its certificate's SHA-256, as it announced it: lets the panel drop an answer whose
        /// certificate will not match before it asks for a code. Never trusted: the pairing binds the certificate.
        /// </summary>
        public string? PinPrefix { get; }

        /// <summary>
        /// Another answer came with the same name but another host, port or pin: any device can answer under
        /// any name, so the panel shows both and says so rather than letting one replace the other.
        /// </summary>
        public bool SharesItsName { get; }

        /// <summary>What pairing types: <c>name.local:port</c>.</summary>
        public string Address => Host + ":" + Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Finds computers announcing Halcyonic pairing on the Wi-Fi (ADR 0017, amendment of 2026-11-11): one
    /// legacy unicast mDNS query (RFC 6762 section 6.7) for <c>_halcyonic._tcp.local</c>, sent from an ordinary
    /// port to 224.0.0.251:5353, whose answers come back by unicast to that port, so the headset needs no
    /// multicast lock and no permission beyond the network it has. A computer announces only while a person
    /// has a pairing window open on it. Nothing found is no failure: the person types the name.
    /// </summary>
    public static class ComputerFinder
    {
        public const string Service = "_halcyonic._tcp.local";

        private static readonly IPEndPoint Mdns = new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353);

        /// <summary>
        /// Asks once and listens for <paramref name="wait"/>; every distinct answer (name, host, port and pin)
        /// once, by name, those sharing a name marked.
        /// </summary>
        public static Task<IReadOnlyList<FoundComputer>> FindAsync(TimeSpan wait, CancellationToken cancellationToken = default) =>
            FindAsync(wait, Mdns, cancellationToken);

        /// <summary>As <see cref="FindAsync(TimeSpan, CancellationToken)"/>, asking <paramref name="responder"/>: tests only.</summary>
        public static async Task<IReadOnlyList<FoundComputer>> FindAsync(TimeSpan wait, IPEndPoint responder, CancellationToken cancellationToken)
        {
            using var socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            var query = Query(Service);
            await socket.SendAsync(query, query.Length, responder).ConfigureAwait(false);
            var found = new Dictionary<string, FoundComputer>(StringComparer.OrdinalIgnoreCase);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(wait);
            using (timeout.Token.Register(() => socket.Dispose()))
            {
                while (!timeout.IsCancellationRequested)
                {
                    UdpReceiveResult received;
                    try
                    {
                        received = await socket.ReceiveAsync().ConfigureAwait(false);
                    }
                    catch (Exception) when (timeout.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (SocketException)
                    {
                        continue;
                    }
                    foreach (var computer in Parse(received.Buffer)) found[Key(computer)] = computer;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return Distinct(found.Values);
        }

        /// <summary>One answer's identity: an answer equal to an earlier one adds nothing.</summary>
        private static string Key(FoundComputer computer) =>
            computer.Name.ToLowerInvariant() + "\n" + computer.Host + "\n" + computer.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + computer.PinPrefix;

        /// <summary>Each distinct answer once, by name then host, a name that came more than once marked.</summary>
        private static IReadOnlyList<FoundComputer> Distinct(IEnumerable<FoundComputer> answers)
        {
            var distinct = answers.GroupBy(Key).Select(same => same.First()).ToList();
            var shared = new HashSet<string>(
                distinct.GroupBy(computer => computer.Name, StringComparer.OrdinalIgnoreCase).Where(named => named.Count() > 1).Select(named => named.Key),
                StringComparer.OrdinalIgnoreCase);
            return distinct
                .Select(computer => shared.Contains(computer.Name)
                    ? new FoundComputer(computer.Name, computer.Host, computer.Port, computer.Addresses, computer.PinPrefix, sharesItsName: true)
                    : computer)
                .OrderBy(computer => computer.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(computer => computer.Host, StringComparer.Ordinal)
                .ThenBy(computer => computer.Port)
                .ToList();
        }

        /// <summary>A DNS query for the PTR records of <paramref name="service"/>, as one message.</summary>
        public static byte[] Query(string service)
        {
            var bytes = new List<byte> { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
            bytes.AddRange(Name(service));
            bytes.AddRange(new byte[] { 0, 12, 0, 1 });
            return bytes.ToArray();
        }

        /// <summary>
        /// The computers one answer names: each PTR for the service, completed by its SRV (host and port),
        /// TXT (<c>pin=</c>) and the host's A records, wherever in the message they are. Anything malformed is
        /// dropped whole: an answer is never trusted, only offered.
        /// </summary>
        public static IReadOnlyList<FoundComputer> Parse(byte[] message)
        {
            try
            {
                return ParseRecords(message);
            }
            catch (Exception error) when (error is IndexOutOfRangeException || error is ArgumentException || error is FormatException)
            {
                return Array.Empty<FoundComputer>();
            }
        }

        private static IReadOnlyList<FoundComputer> ParseRecords(byte[] message)
        {
            if (message.Length < 12) return Array.Empty<FoundComputer>();
            var questions = (message[4] << 8) | message[5];
            var records = ((message[6] << 8) | message[7]) + ((message[8] << 8) | message[9]) + ((message[10] << 8) | message[11]);
            var offset = 12;
            for (var q = 0; q < questions; q++)
            {
                ReadName(message, ref offset);
                offset += 4;
            }
            var instances = new List<string>();
            var services = new Dictionary<string, (string Host, int Port)>(StringComparer.OrdinalIgnoreCase);
            var pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var addresses = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            for (var r = 0; r < records && offset < message.Length; r++)
            {
                var owner = ReadName(message, ref offset);
                var type = (message[offset] << 8) | message[offset + 1];
                var length = (message[offset + 8] << 8) | message[offset + 9];
                var data = offset + 10;
                if (data + length > message.Length) throw new ArgumentException("A record runs past the message.");
                switch (type)
                {
                    case 12 when string.Equals(owner, Service, StringComparison.OrdinalIgnoreCase):
                        var at = data;
                        instances.Add(ReadName(message, ref at));
                        break;
                    case 33:
                        var port = (message[data + 4] << 8) | message[data + 5];
                        var targetAt = data + 6;
                        services[owner] = (ReadName(message, ref targetAt), port);
                        break;
                    case 16:
                        for (var t = data; t < data + length;)
                        {
                            var size = message[t];
                            var text = Encoding.UTF8.GetString(message, t + 1, size);
                            if (text.StartsWith("pin=", StringComparison.Ordinal)) pins[owner] = text.Substring(4);
                            t += 1 + size;
                        }
                        break;
                    case 1 when length == 4:
                        if (!addresses.TryGetValue(owner, out var list)) addresses[owner] = list = new List<string>();
                        list.Add(new IPAddress(new[] { message[data], message[data + 1], message[data + 2], message[data + 3] }).ToString());
                        break;
                }
                offset = data + length;
            }
            var found = new List<FoundComputer>();
            foreach (var instance in instances.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!services.TryGetValue(instance, out var service)) continue;
                if (!service.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || service.Port < 1) continue;
                var label = instance.EndsWith("." + Service, StringComparison.OrdinalIgnoreCase)
                    ? instance.Substring(0, instance.Length - Service.Length - 1)
                    : instance;
                pins.TryGetValue(instance, out var pin);
                if (pin != null && !System.Text.RegularExpressions.Regex.IsMatch(pin, "^[0-9a-f]{16}$")) pin = null;
                found.Add(new FoundComputer(
                    label,
                    service.Host.ToLowerInvariant(),
                    service.Port,
                    addresses.TryGetValue(service.Host, out var hosts) ? hosts.Distinct().ToList() : (IReadOnlyList<string>)Array.Empty<string>(),
                    pin));
            }
            return found;
        }

        private static IEnumerable<byte> Name(string name)
        {
            foreach (var label in name.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var bytes = Encoding.UTF8.GetBytes(label);
                yield return (byte)bytes.Length;
                foreach (var b in bytes) yield return b;
            }
            yield return 0;
        }

        /// <summary>A name, following compression pointers forward and back, at most 16 of them.</summary>
        private static string ReadName(byte[] message, ref int offset)
        {
            var labels = new List<string>();
            var at = offset;
            var jumped = false;
            for (var jumps = 0; ; )
            {
                var length = message[at];
                if (length == 0)
                {
                    if (!jumped) offset = at + 1;
                    break;
                }
                if ((length & 0xC0) == 0xC0)
                {
                    if (++jumps > 16) throw new FormatException("Too many name pointers.");
                    var target = ((length & 0x3F) << 8) | message[at + 1];
                    if (!jumped) offset = at + 2;
                    jumped = true;
                    at = target;
                    continue;
                }
                labels.Add(Encoding.UTF8.GetString(message, at + 1, length));
                at += 1 + length;
            }
            return string.Join(".", labels);
        }
    }
}

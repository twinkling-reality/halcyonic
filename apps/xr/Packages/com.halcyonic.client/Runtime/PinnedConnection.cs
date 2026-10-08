#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Halcyonic.Client
{
    /// <summary>
    /// A TLS connection to a paired control plane (ADR 0017). The certificate is checked inside
    /// SslStream's own validation callback, so a certificate other than the pinned one ends the
    /// handshake before anything is sent. Before pairing there is no pin yet: the certificate is
    /// accepted and recorded, and the pairing proof binds it.
    /// </summary>
    /// <remarks>
    /// The callback given to SslStream's constructor is the only one Unity's Android class libraries
    /// honor: ClientWebSocket ignores its options' callback, and HttpClientHandler's callback throws
    /// there (docs/internal/validation/network-pairing.md). So the pinned transports speak WebSocket
    /// and HTTP/1.1 over this stream themselves.
    /// </remarks>
    public sealed class PinnedConnection : IDisposable
    {
        private readonly TcpClient client;

        private PinnedConnection(TcpClient client, SslStream stream, string certificateSha256)
        {
            this.client = client;
            Stream = stream;
            CertificateSha256 = certificateSha256;
        }

        public Stream Stream { get; }

        /// <summary>The SHA-256 of the certificate the server presented, in lowercase hex.</summary>
        public string CertificateSha256 { get; }

        /// <summary>
        /// Connects and completes the TLS handshake. With a pin, any other certificate fails the
        /// handshake with <see cref="CertificateMismatchException"/>; without one, any certificate is
        /// accepted and recorded, which only pairing may do.
        /// </summary>
        public static async Task<PinnedConnection> OpenAsync(string host, int port, string? pin, CancellationToken cancellationToken)
        {
            var client = await ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
            string? seen = null;
            var stream = new SslStream(client.GetStream(), false, (sender, certificate, chain, errors) =>
            {
                if (certificate == null) return false;
                seen = PairingCrypto.Sha256Hex(certificate.GetRawCertData());
                // A certificate's hash is public, so an ordinary comparison reveals nothing.
                return pin == null || string.Equals(seen, pin, StringComparison.Ordinal);
            });
            try
            {
                using (cancellationToken.Register(() => client.Dispose()))
                {
                    await stream.AuthenticateAsClientAsync(host).ConfigureAwait(false);
                }
            }
            catch (Exception error)
            {
                stream.Dispose();
                client.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
                if (pin != null && seen != null && seen != pin) throw new CertificateMismatchException(seen);
                throw new HandshakeFailedException("The TLS handshake failed: " + error.Message, error);
            }
            try
            {
                return new PinnedConnection(client, stream, Presented(seen));
            }
            catch (HandshakeFailedException)
            {
                stream.Dispose();
                client.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Closes the connection if the token is cancelled while the registration lasts, so a read that
        /// does not watch its token, as on some platforms, still ends.
        /// </summary>
        public CancellationTokenRegistration CloseOn(CancellationToken cancellationToken) => cancellationToken.Register(Dispose);

        public void Dispose()
        {
            Stream.Dispose();
            client.Dispose();
        }

        /// <summary>
        /// The certificate the handshake saw, or a handshake that failed: one that ended without a certificate set
        /// up no secure connection, though something took the connection.
        /// </summary>
        public static string Presented(string? seen) => seen ?? throw new HandshakeFailedException("The server presented no certificate.", null);

        private static async Task<TcpClient> ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            IPAddress[] addresses;
            try
            {
                addresses = IPAddress.TryParse(host, out var literal)
                    ? new[] { literal }
                    : await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
            }
            catch (SocketException error)
            {
                throw new IOException("The name " + host + " does not resolve: " + error.Message, error);
            }
            Exception? last = null;
            foreach (var address in addresses)
            {
                var client = new TcpClient(address.AddressFamily) { NoDelay = true };
                try
                {
                    using (cancellationToken.Register(() => client.Dispose()))
                    {
                        await client.ConnectAsync(address, port).ConfigureAwait(false);
                    }
                    return client;
                }
                catch (Exception error)
                {
                    client.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                    last = error;
                }
            }
            throw new IOException("Could not connect to " + host + ":" + port + ": " + (last?.Message ?? "the name has no address") + ".", last);
        }
    }

    /// <summary>
    /// Something answered at the address and port, so the connection was made, and then no secure connection
    /// was set up: what answered doesn't speak TLS, closed it, or showed no certificate. Told apart from a name
    /// that doesn't resolve or a connection nothing took, which stay plain <see cref="IOException"/>s.
    /// </summary>
    public sealed class HandshakeFailedException : IOException
    {
        public HandshakeFailedException(string message, Exception? inner)
            : base(message, inner)
        {
        }
    }

    /// <summary>The server presented a certificate other than the one pinned when this device paired.</summary>
    public sealed class CertificateMismatchException : IOException
    {
        public CertificateMismatchException(string seen)
            : base("The control plane presented a certificate other than the one this device paired with. Pair again if the control plane's identity changed.")
        {
            Seen = seen;
        }

        /// <summary>The SHA-256 of the certificate presented instead.</summary>
        public string Seen { get; }
    }
}

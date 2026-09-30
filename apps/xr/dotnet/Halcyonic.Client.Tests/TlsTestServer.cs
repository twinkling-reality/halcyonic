using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Halcyonic.Client.Tests;

/// <summary>
/// A TLS server on a loopback port with a self-signed certificate of its own, for testing the pinned
/// transports against servers that answer exactly as a test needs, including impostors.
/// </summary>
internal sealed class TlsTestServer : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly X509Certificate2 certificate;
    private readonly Func<Stream, Task> serve;
    private readonly CancellationTokenSource stopping = new();
    private readonly Task accepting;
    private readonly List<Task> connections = new();
    private int bytesReceived;

    private TlsTestServer(X509Certificate2 certificate, Func<Stream, Task> serve)
    {
        this.certificate = certificate;
        this.serve = serve;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        accepting = AcceptAsync();
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public string CertificateSha256 => PairingCrypto.Sha256Hex(certificate.RawData);

    /// <summary>Application bytes the server read after the handshake, over every connection.</summary>
    public int BytesReceived => Volatile.Read(ref bytesReceived);

    public static TlsTestServer Start(Func<Stream, Task> serve) => new(CreateCertificate(), serve);

    /// <summary>A server that answers every connection with this HTTP response, then closes.</summary>
    public static TlsTestServer Answering(string response) =>
        Start(async stream =>
        {
            await ReadHeadAsync(stream);
            var bytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(bytes);
        });

    /// <summary>Reads until the client closes the connection.</summary>
    public static async Task DrainAsync(Stream stream)
    {
        var buffer = new byte[1024];
        while (await stream.ReadAsync(buffer) > 0)
        {
        }
    }

    /// <summary>Reads an HTTP request head, a byte at a time.</summary>
    public static async Task<string> ReadHeadAsync(Stream stream)
    {
        var head = new StringBuilder();
        var one = new byte[1];
        while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            if (await stream.ReadAsync(one) == 0) break;
            head.Append((char)one[0]);
        }
        return head.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        listener.Stop();
        try
        {
            await accepting;
        }
        catch (Exception)
        {
            // Stopped.
        }
        foreach (var connection in connections.ToArray())
        {
            try
            {
                await connection.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
                // A test may leave a connection failing or hanging; it is dropped with the listener.
            }
        }
        certificate.Dispose();
        stopping.Dispose();
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Test control plane", key, HashAlgorithmName.SHA256);
        using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // SslStream on macOS serves only a certificate whose key it can reach through an import.
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), null);
    }

    private async Task AcceptAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(stopping.Token);
            lock (connections) connections.Add(ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        await using (var tls = new SslStream(client.GetStream(), false))
        {
            try
            {
                await tls.AuthenticateAsServerAsync(certificate, false, false);
            }
            catch (Exception)
            {
                // A client that refuses the certificate ends the handshake; nothing was received.
                return;
            }
            await serve(new CountingStream(tls, count => Interlocked.Add(ref bytesReceived, count)));
        }
    }

    /// <summary>Counts what the server reads, so a test can tell that a client sent nothing.</summary>
    private sealed class CountingStream : Stream
    {
        private readonly Stream inner;
        private readonly Action<int> counted;

        public CountingStream(Stream inner, Action<int> counted)
        {
            this.inner = inner;
            this.counted = counted;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            counted(read);
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            counted(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            counted(read);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.WriteAsync(buffer, offset, count, cancellationToken);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);
    }
}

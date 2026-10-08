using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The pinned transports (ADR 0017) against TLS servers that answer as each test needs: the pin
/// decides the handshake before anything is sent, and HTTP and the WebSocket upgrade are read
/// exactly.
/// </summary>
public class PinnedTransportTests
{
    private static readonly string OtherPin = new('a', 64);

    [Test]
    public async Task APinnedConnectionOpensOnlyWithThePinnedCertificate()
    {
        await using var server = TlsTestServer.Start(TlsTestServer.DrainAsync);
        using (var connection = await PinnedConnection.OpenAsync("127.0.0.1", server.Port, server.CertificateSha256, CancellationToken.None))
        {
            Assert.That(connection.CertificateSha256, Is.EqualTo(server.CertificateSha256));
        }
        var mismatch = Assert.ThrowsAsync<CertificateMismatchException>(
            () => PinnedConnection.OpenAsync("127.0.0.1", server.Port, OtherPin, CancellationToken.None));
        Assert.That(mismatch!.Seen, Is.EqualTo(server.CertificateSha256));
    }

    [Test]
    public async Task WithoutAPinTheCertificateIsAcceptedAndRecorded()
    {
        await using var server = TlsTestServer.Start(TlsTestServer.DrainAsync);
        using var connection = await PinnedConnection.OpenAsync("127.0.0.1", server.Port, null, CancellationToken.None);
        Assert.That(connection.CertificateSha256, Is.EqualTo(server.CertificateSha256));
    }

    [Test]
    public async Task NothingReachesAServerWithAnotherCertificate()
    {
        await using var impostor = TlsTestServer.Start(async stream => await TlsTestServer.ReadHeadAsync(stream));
        using var http = new HttpClient(new PinnedHttpHandler(OtherPin));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "hlcd_" + new string('A', 43));
        var failed = Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync($"https://127.0.0.1:{impostor.Port}/api/snapshot"));
        Assert.That(failed!.InnerException, Is.TypeOf<CertificateMismatchException>());

        var transport = new PinnedWebSocketTransport(OtherPin);
        Assert.ThrowsAsync<CertificateMismatchException>(
            () => transport.ConnectAsync(new Uri($"wss://127.0.0.1:{impostor.Port}/realtime"), "hlcd_" + new string('A', 43), CancellationToken.None));
        transport.Dispose();
        await Task.Delay(100);
        Assert.That(impostor.BytesReceived, Is.Zero, "the credential never reached the impostor");
    }

    [Test]
    public async Task SendsARequestWithItsHeadersAndBody()
    {
        string? head = null;
        string? body = null;
        await using var server = TlsTestServer.Start(async stream =>
        {
            head = await TlsTestServer.ReadHeadAsync(stream);
            var buffer = new byte[2];
            var read = await stream.ReadAsync(buffer);
            body = Encoding.UTF8.GetString(buffer, 0, read);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\n\r\n"));
        });
        using var http = new HttpClient(new PinnedHttpHandler(server.CertificateSha256));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://127.0.0.1:{server.Port}/api/commands?x=1")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "hlcd_token");
        using var response = await http.SendAsync(request);
        Assert.That((int)response.StatusCode, Is.EqualTo(204));
        Assert.That(head, Does.StartWith("POST /api/commands?x=1 HTTP/1.1\r\n"));
        Assert.That(head, Does.Contain($"Host: 127.0.0.1:{server.Port}\r\n"));
        Assert.That(head, Does.Contain("Authorization: Bearer hlcd_token\r\n"));
        Assert.That(head, Does.Contain("Connection: close\r\n"));
        Assert.That(head, Does.Contain("Content-Type: application/json; charset=utf-8\r\n"));
        Assert.That(head, Does.Contain("Content-Length: 2\r\n"));
        Assert.That(body, Is.EqualTo("{}"));
    }

    [TestCase("Content-Length: 9\r\n\r\n{\"a\":\"b\"}", TestName = "ByLength")]
    [TestCase("Transfer-Encoding: chunked\r\n\r\n4\r\n{\"a\"\r\n5\r\n:\"b\"}\r\n0\r\n\r\n", TestName = "ByChunks")]
    [TestCase("Connection: close\r\n\r\n{\"a\":\"b\"}", TestName = "ToTheEnd")]
    public async Task ReadsAResponse(string rest)
    {
        await using var server = TlsTestServer.Answering("HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\n" + rest);
        using var http = new HttpClient(new PinnedHttpHandler(server.CertificateSha256));
        var text = await http.GetStringAsync($"https://127.0.0.1:{server.Port}/api/health");
        Assert.That(text, Is.EqualTo("{\"a\":\"b\"}"));
    }

    [Test]
    public async Task ARefusedUpgradeSaysWhy()
    {
        const string error = "{\"error\":{\"code\":\"device_revoked\",\"message\":\"This device was revoked on the control plane; pair it again.\",\"issues\":[]}}";
        await using var server = TlsTestServer.Answering(
            "HTTP/1.1 401 Unauthorized\r\nContent-Type: application/json\r\nContent-Length: " + error.Length + "\r\n\r\n" + error);
        using var transport = new PinnedWebSocketTransport(server.CertificateSha256);
        var refused = Assert.ThrowsAsync<UpgradeRefusedException>(
            () => transport.ConnectAsync(new Uri($"wss://127.0.0.1:{server.Port}/realtime"), "hlcd_token", CancellationToken.None));
        Assert.That(refused!.Status, Is.EqualTo(401));
        Assert.That(refused.Code, Is.EqualTo("device_revoked"));
        Assert.That(refused.Message, Does.Contain("pair it again"));
    }

    [Test]
    public async Task AnUpgradeAnsweredWithTheWrongKeyIsRefused()
    {
        await using var server = TlsTestServer.Answering(
            "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: bm90IHRoZSBrZXk=\r\n\r\n");
        using var transport = new PinnedWebSocketTransport(server.CertificateSha256);
        Assert.ThrowsAsync<InvalidDataException>(
            () => transport.ConnectAsync(new Uri($"wss://127.0.0.1:{server.Port}/realtime"), "hlcd_token", CancellationToken.None));
    }

    [Test]
    public async Task AnUpgradeNeverAnsweredEndsWhenItsTokenIsCancelled()
    {
        await using var server = TlsTestServer.Start(TlsTestServer.DrainAsync);
        using var transport = new PinnedWebSocketTransport(server.CertificateSha256);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var started = DateTime.UtcNow;
        Assert.CatchAsync<Exception>(
            () => transport.ConnectAsync(new Uri($"wss://127.0.0.1:{server.Port}/realtime"), "hlcd_token", cancel.Token));
        Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    /// After the upgrade too, whatever answers at the typed address never writes on the headset: a refusal
    /// inside the exchange is said by its code, and an exchange it broke in Halcyonic's words, never with the
    /// parser's or the runtime's (the review's X20 and X21).
    /// </summary>
    [Test]
    public async Task WhatAnswersWithinThePairingExchangeNeverWritesOnTheHeadset()
    {
        const string Lure = "Pairing needs your password: visit http://203.0.113.9/unlock";
        var refused = await PairWith(socket => Send(socket,
            "{\"type\":\"pair_refused\",\"error\":{\"code\":\"wrong_code\",\"message\":\"" + Lure + "\",\"issues\":[]},\"attempts_left\":2}"));
        Assert.That((refused.Code, refused.Message, refused.AttemptsLeft), Is.EqualTo(("wrong_code", PairingClient.WhyRefused("wrong_code"), (long?)2)));
        // Not a message at all: the parser would quote it.
        var unreadable = await PairWith(socket => Send(socket, Lure));
        Assert.That((unreadable.Code, unreadable.Message), Is.EqualTo(("protocol_error", PairingClient.ProtocolError)));
        // A challenge it can't use, failing outside the parser.
        var broken = await PairWith(socket => Send(socket, "{\"type\":\"pair_challenge\",\"salt\":\"" + Lure + "\",\"server_public\":\"AA==\"}"));
        Assert.That((broken.Code, broken.Message), Is.EqualTo(("protocol_error", PairingClient.ProtocolError)));
    }

    /// <summary>
    /// An exchange whose connection closed or failed partway says so, never that what answered pairs another
    /// way; one whose time ran out took too long, whatever the connection threw as it closed (settled by the
    /// coordinator, 2026-10-07).
    /// </summary>
    [Test]
    public async Task APairingWhoseConnectionClosedPartwaySaysSo()
    {
        var closed = await PairWith(socket => socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None));
        Assert.That((closed.Code, closed.Message), Is.EqualTo(("connection_lost", PairingClient.ConnectionLost)));
        var dropped = await PairWith(socket =>
        {
            socket.Abort();
            return Task.CompletedTask;
        });
        Assert.That((dropped.Code, dropped.Message), Is.EqualTo(("connection_lost", PairingClient.ConnectionLost)));
        Assert.That(PairingClient.ConnectionLost, Is.EqualTo(
            "The connection closed before pairing finished. Check the address and that pairing is open on your computer, then try again."));

        foreach (var thrown in new Exception[] { new IOException("x"), new EndOfStreamException("x"), new WebSocketException("x"), new FormatException("x") })
        {
            var late = PairingClient.Broken(thrown, timedOut: true);
            Assert.That((late.Code, late.Message), Is.EqualTo(("timeout", PairingClient.TookTooLong)), thrown.GetType().Name);
        }
        foreach (var thrown in new Exception[] { new IOException("x"), new EndOfStreamException("x"), new WebSocketException("x"), new ObjectDisposedException("x") })
        {
            Assert.That(PairingClient.Broken(thrown, timedOut: false).Code, Is.EqualTo("connection_lost"), thrown.GetType().Name);
        }
        Assert.That(PairingClient.Broken(new FormatException("x"), timedOut: false).Code, Is.EqualTo("protocol_error"));
        Assert.That(PairingClient.Broken(new InvalidDataException("x"), timedOut: false).Code, Is.EqualTo("protocol_error"));
    }

    /// <summary>Pairs with a server at the typed address that takes the upgrade and the device's first message, then does <paramref name="then"/>.</summary>
    private static async Task<PairingException> PairWith(Func<WebSocket, Task> then)
    {
        await using var server = TlsTestServer.Start(async stream =>
        {
            var head = await TlsTestServer.ReadHeadAsync(stream);
            var key = Regex.Match(head, "^Sec-WebSocket-Key: *(\\S+)\r$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Groups[1].Value;
            var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n"));
            using var socket = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: Timeout.InfiniteTimeSpan);
            await socket.ReceiveAsync(new ArraySegment<byte>(new byte[16 * 1024]), CancellationToken.None);
            await then(socket);
        });
        return Assert.ThrowsAsync<PairingException>(() => PairingClient.PairAsync("127.0.0.1", server.Port, "12345678", "Quest 3"))!;
    }

    private static Task Send(WebSocket socket, string text) =>
        socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, CancellationToken.None);

    /// <summary>
    /// A request whose token was cancelled ends cancelled, whatever the runtime threw as its connection closed,
    /// so a timeout reads as one on the headset too, where Mono may fault rather than cancel (the review's R6).
    /// </summary>
    [Test]
    public void ARequestCancelledWhileItsConnectionFailedIsReportedAsCancelled()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        foreach (var thrown in new Exception[] { new IOException("closed"), new ObjectDisposedException("SslStream"), new InvalidDataException("cut"), new System.Net.Sockets.SocketException(), new OperationCanceledException() })
        {
            var reported = PinnedHttpHandler.Reported(thrown, cancelled.Token);
            Assert.That(reported, Is.InstanceOf<OperationCanceledException>(), thrown.GetType().Name);
            Assert.That(((OperationCanceledException)reported!).CancellationToken, Is.EqualTo(cancelled.Token));
        }
        // While its token stands: a connection that failed, or an answer not HTTP, as other handlers report them.
        Assert.That(PinnedHttpHandler.Reported(new IOException("closed"), CancellationToken.None), Is.InstanceOf<HttpRequestException>());
        Assert.That(PinnedHttpHandler.Reported(new InvalidDataException("cut"), CancellationToken.None), Is.InstanceOf<HttpRequestException>());
        Assert.That(PinnedHttpHandler.Reported(new ArgumentException("bug"), CancellationToken.None), Is.Null, "anything else as it was thrown");
    }

    /// <summary>
    /// A request whose token is cancelled ends cancelled even where the runtime throws something else on the
    /// way: here the name lookup, which takes no token, fails for a name that never resolves (RFC 6761), as
    /// Mono may fail a closed connection rather than cancel it (the review's R6).
    /// </summary>
    [Test]
    public void ARequestCancelledWhileItsNameFailedToResolveEndsCancelled()
    {
        using var invoker = new HttpMessageInvoker(new PinnedHttpHandler(new string('a', 64)));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.That(async () => await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://halcyonic-test.invalid/api/locations"), cancelled.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(async () => await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://halcyonic-test.invalid/api/locations"), CancellationToken.None),
            Throws.InstanceOf<HttpRequestException>(), "while its token stands, a connection that failed");
    }

    /// <summary>
    /// Pairing tells a connection something took from one nothing took: what answered without setting up a secure
    /// connection, as a service that doesn't speak TLS or a listener turning away too many tries, says so; a name
    /// that doesn't resolve, or a port nothing listens on, says nothing answered (settled by the coordinator, 2026-10-07).
    /// </summary>
    [Test]
    public async Task APairingThatConnectedButSetUpNoSecureConnectionSaysSomethingAnswered()
    {
        foreach (var answer in new[] { "HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n", "" })
        {
            var plain = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            plain.Start();
            var port = ((System.Net.IPEndPoint)plain.LocalEndpoint).Port;
            var serving = Task.Run(async () =>
            {
                using var client = await plain.AcceptTcpClientAsync();
                var stream = client.GetStream();
                if (answer.Length > 0) await stream.WriteAsync(Encoding.ASCII.GetBytes(answer));
            });
            try
            {
                var refused = Assert.ThrowsAsync<PairingException>(() => PairingClient.PairAsync("127.0.0.1", port, "12345678", "Quest 3"));
                Assert.That((refused!.Code, refused.Message), Is.EqualTo(("not_secure",
                    "Something at 127.0.0.1:" + port + " answered but didn't set up a secure connection. Check the address and port; if they're right, try again in a minute.")),
                    answer.Length > 0 ? "not TLS" : "closed at once");
                await serving;
            }
            finally
            {
                plain.Stop();
            }
        }

        var free = ControlPlaneProcess.FreePort();
        var nothing = Assert.ThrowsAsync<PairingException>(() => PairingClient.PairAsync("127.0.0.1", free, "12345678", "Quest 3"));
        Assert.That((nothing!.Code, nothing.Message), Is.EqualTo(("unreachable",
            "Nothing answered at 127.0.0.1:" + free + ". Check the address, and that your computer and this device share a network.")));
        var unnamed = Assert.ThrowsAsync<PairingException>(() => PairingClient.PairAsync("halcyonic-test.invalid", 47801, "12345678", "Quest 3"));
        Assert.That(unnamed!.Code, Is.EqualTo("unreachable"), "a name that doesn't resolve");
        // A handshake that ended with no certificate set up no secure connection either.
        Assert.That(() => PinnedConnection.Presented(null), Throws.InstanceOf<HandshakeFailedException>());
        Assert.That(PinnedConnection.Presented("ab"), Is.EqualTo("ab"));
    }

    [Test]
    public void TargetsUseThePinnedTransportsOnlyForAPairing()
    {
        var local = ControlPlaneTarget.Local(new Uri("ws://127.0.0.1:47800/realtime"), "token");
        using (var transport = local.CreateTransport()) Assert.That(transport, Is.TypeOf<LoopbackWebSocketTransport>());
        var pairing = new PairedControlPlane("192.168.1.23", 47801, new string('c', 64), "01a0f0a0-0000-7000-8000-000000000001", "hlcd_" + new string('A', 43));
        var paired = ControlPlaneTarget.Paired(pairing);
        using (var transport = paired.CreateTransport()) Assert.That(transport, Is.TypeOf<PinnedWebSocketTransport>());
        Assert.That(paired.Endpoint, Is.EqualTo(new Uri("wss://192.168.1.23:47801/realtime")));
        Assert.That(paired.Credential, Is.EqualTo(pairing.Credential));
        Assert.That(ControlPlaneApi.BaseUriFor(paired.Endpoint), Is.EqualTo(new Uri("https://192.168.1.23:47801/")));
        Assert.That(pairing.At("fe80::1", 47801).RealtimeEndpoint, Is.EqualTo(new Uri("wss://[fe80::1]:47801/realtime")));
        using (var api = local.CreateApi()) Assert.That(api.AccessRefused, Is.EqualTo(ConnectionText.AccessTokenRefused), "a refused request says what the connection says");
        using (var api = paired.CreateApi()) Assert.That(api.AccessRefused, Is.EqualTo(ConnectionText.PairingRefused));
        Assert.That((local.TurnedAway, paired.TurnedAway), Is.EqualTo((
            "Your computer turned the connection away. The headset tries again by itself; if this keeps happening, restart this app on your computer.",
            "Your computer turned the connection away. The headset tries again by itself; if this keeps happening, pair it again in Settings.")),
            "a turned-away connection's way on is true for how this headset reaches the computer");
        var client = new Halcyonic.Contracts.ClientInfo { Name = "halcyonic-xr", Version = "test", DeviceLabel = "Quest" };
        foreach (var target in new[] { local, paired })
        {
            var options = target.SessionOptions(client);
            Assert.That((options.AccessRefused, options.TurnedAway), Is.EqualTo((target.AccessRefused, target.TurnedAway)), "the session says what the target does");
        }
        Assert.Throws<ArgumentException>(() => ControlPlaneTarget.CheckPin("ABC"));
        Assert.Throws<ArgumentException>(() => ControlPlaneTarget.CheckPin(new string('C', 64)));
    }

    [Test]
    public void ATargetIsTheSameOnlyWithTheSameEndpointCredentialAndPin()
    {
        var endpoint = new Uri("ws://127.0.0.1:47800/realtime");
        var local = ControlPlaneTarget.Local(endpoint, "token");
        Assert.That(local.SameAs(ControlPlaneTarget.Local(new Uri("ws://127.0.0.1:47800/realtime"), "token")), Is.True);
        Assert.That(local.SameAs(ControlPlaneTarget.Local(endpoint, "another token")), Is.False);
        Assert.That(local.SameAs(ControlPlaneTarget.Local(new Uri("ws://127.0.0.1:47810/realtime"), "token")), Is.False);

        var pairing = new PairedControlPlane("192.168.1.23", 47801, new string('c', 64), "01a0f0a0-0000-7000-8000-000000000001", "hlcd_" + new string('A', 43));
        var paired = ControlPlaneTarget.Paired(pairing);
        Assert.That(paired.SameAs(ControlPlaneTarget.Paired(pairing.At("192.168.1.23", 47801))), Is.True);
        Assert.That(paired.SameAs(local), Is.False);
        Assert.That(paired.SameAs(ControlPlaneTarget.Paired(pairing.At("192.168.1.24", 47801))), Is.False, "another address");
        var repaired = new PairedControlPlane("192.168.1.23", 47801, new string('c', 64), "01a0f0a0-0000-7000-8000-000000000002", "hlcd_" + new string('B', 43));
        Assert.That(paired.SameAs(ControlPlaneTarget.Paired(repaired)), Is.False, "another credential");
        var anotherCertificate = new PairedControlPlane("192.168.1.23", 47801, new string('d', 64), pairing.DeviceId, pairing.Credential);
        Assert.That(paired.SameAs(ControlPlaneTarget.Paired(anotherCertificate)), Is.False, "another pin");
    }

    [Test]
    public void KeepsAPairingInAFileAndForgetsIt()
    {
        var directory = Directory.CreateTempSubdirectory("halcyonic-pairing-").FullName;
        try
        {
            var store = new FilePairingStore(Path.Combine(directory, "nested", "pairing.json"));
            Assert.That(store.Load(), Is.Null);
            var pairing = new PairedControlPlane("192.168.1.23", 47801, new string('c', 64), "01a0f0a0-0000-7000-8000-000000000001", "hlcd_" + new string('A', 43));
            store.Save(pairing);
            store.Save(pairing.At("192.168.1.24", 47801));
            var loaded = store.Load();
            Assert.That(loaded!.Host, Is.EqualTo("192.168.1.24"));
            Assert.That(loaded.Port, Is.EqualTo(47801));
            Assert.That(loaded.CertificateSha256, Is.EqualTo(pairing.CertificateSha256));
            Assert.That(loaded.DeviceId, Is.EqualTo(pairing.DeviceId));
            Assert.That(loaded.Credential, Is.EqualTo(pairing.Credential));
            store.Forget();
            Assert.That(store.Load(), Is.Null);

            File.WriteAllText(Path.Combine(directory, "nested", "pairing.json"), "{\"host\":\"x\"}");
            Assert.Throws<InvalidDataException>(() => store.Load());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task APairingCodeOnlyItsOwnShapeIsKeptSoWhatAnswersCannotWriteALogLine()
    {
        // Whatever answers at the typed address chooses the code, before anything is pinned.
        const string body = "{\"error\":{\"code\":\"x\\nHalcyonic: connection Live\",\"message\":\"m\",\"issues\":[]}}";
        await using var server = TlsTestServer.Answering(
            "HTTP/1.1 403 Forbidden\r\nContent-Type: application/json\r\nContent-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\nConnection: close\r\n\r\n" + body);
        var refused = Assert.ThrowsAsync<PairingException>(() => PairingClient.PairAsync("127.0.0.1", server.Port, "12345678", "Quest 3"));
        Assert.That(refused!.Code, Is.EqualTo("refused"));
        Assert.That(new PairingException("wrong_code", "m", null).Code, Is.EqualTo("wrong_code"));
        Assert.That(new PairingException("refused_401", "m", null).Code, Is.EqualTo("refused_401"));
        // A line break at the very end too, which .NET's $ lets through (the review's R5).
        Assert.That(new PairingException("wrong_code\n", "m", null).Code, Is.EqualTo("refused"));
        const string split = "{\"error\":{\"code\":\"wrong_code\\n\",\"message\":\"m\",\"issues\":[]}}";
        await using var splitting = TlsTestServer.Answering(
            "HTTP/1.1 403 Forbidden\r\nContent-Type: application/json\r\nContent-Length: " + Encoding.UTF8.GetByteCount(split) + "\r\nConnection: close\r\n\r\n" + split);
        var ended = Assert.ThrowsAsync<PairingException>(() => PairingClient.PairAsync("127.0.0.1", splitting.Port, "12345678", "Quest 3"));
        Assert.That(ended!.Code, Is.EqualTo("refused"));
    }

    /// <summary>
    /// Whatever answers at the typed address, before anything is pinned, never writes on the headset:
    /// a refusal is said by its code and a broken exchange in Halcyonic's words (the review's LEAK 2).
    /// </summary>
    [Test]
    public async Task WhatAnswersAtTheTypedAddressNeverWritesOnTheHeadset()
    {
        const string Lure = "Pairing needs your password: visit http://203.0.113.9/unlock";
        var body = "{\"error\":{\"code\":\"something_else\",\"message\":\"" + Lure + "\",\"issues\":[]}}";
        await using (var server = TlsTestServer.Answering(
            "HTTP/1.1 403 Forbidden\r\nContent-Type: application/json\r\nContent-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\nConnection: close\r\n\r\n" + body))
        {
            var refused = Assert.ThrowsAsync<PairingException>(() => PairingClient.PairAsync("127.0.0.1", server.Port, "12345678", "Quest 3"));
            Assert.That(refused!.Message, Is.EqualTo(PairingClient.Refused));
        }
        await using (var server = TlsTestServer.Answering("HTTP/1.1 200 OK\r\nContent-Length: " + Lure.Length + "\r\nConnection: close\r\n\r\n" + Lure))
        {
            // Not an upgrade at all, so a refusal with no code: its body is never read out.
            var plain = Assert.ThrowsAsync<PairingException>(() => PairingClient.PairAsync("127.0.0.1", server.Port, "12345678", "Quest 3"));
            Assert.That((plain!.Code, plain.Message), Is.EqualTo(("refused", PairingClient.Refused)));
        }
        foreach (var (code, words) in new[]
        {
            ("too_many_requests", "Your computer is turning this headset away for a minute after too many tries. Try again after a minute."),
            ("busy", "Your computer is pairing another device right now. Try again in a moment."),
            ("timeout", "Pairing took too long; try again."),
            ("invalid_message", "Your computer refused to pair this headset. Open pairing there again, then try again."),
            ("internal_error", "Your computer refused to pair this headset. Open pairing there again, then try again."),
            ((string?)null, "Your computer refused to pair this headset. Open pairing there again, then try again."),
        })
        {
            Assert.That(PairingClient.WhyRefused(code), Is.EqualTo(words), code ?? "no code");
        }
        Assert.That(new[] { PairingClient.ProtocolError, PairingClient.NotProven, PairingClient.Failed, PairingClient.Refused }, Has.None.Contains("control plane"));
    }
}

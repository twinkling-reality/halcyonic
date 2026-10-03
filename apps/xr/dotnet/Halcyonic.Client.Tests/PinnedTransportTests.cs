using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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
    }
}

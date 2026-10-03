using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// A plain HTTP server on a loopback port that keeps each connection open for as long as the client
/// does, records every request on it, with its connection, and answers as a test needs.
/// </summary>
internal sealed class RecordingServer : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly Func<string, int, string> answer;
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentQueue<(int Connection, string Head)> requests = new();
    private readonly List<TcpClient> clients = new();
    private readonly Task accepting;
    private int connections;

    public RecordingServer(Func<string, int, string> answer, int port = 0)
    {
        this.answer = answer;
        listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        accepting = AcceptAsync();
    }

    public int Port { get; }

    public int Connections => Volatile.Read(ref connections);

    /// <summary>Each request's connection, numbered from 1, and its head.</summary>
    public (int Connection, string Head)[] Requests => requests.ToArray();

    public string[] Paths => Requests.Select(request => request.Head.Split(' ')[1]).ToArray();

    /// <summary>A request head's proof challenge, or null.</summary>
    public static string? ChallengeIn(string head) =>
        Regex.Match(head, "^" + LoopbackProof.ChallengeHeader + ": *([0-9a-f]{64})\r$", RegexOptions.Multiline | RegexOptions.IgnoreCase) is { Success: true } match
            ? match.Groups[1].Value
            : null;

    /// <summary>A response that keeps the connection open, or closes it.</summary>
    public static string Http(string status, string headers = "", string body = "{}", bool close = false) =>
        "HTTP/1.1 " + status + "\r\nContent-Type: application/json\r\nContent-Length: " + Encoding.UTF8.GetByteCount(body)
        + (close ? "\r\nConnection: close" : "\r\nConnection: keep-alive") + "\r\n" + headers + "\r\n" + body;

    /// <summary>Answers a proof's challenge as a control plane holding <paramref name="token"/> at <paramref name="address"/> would.</summary>
    public static string Proof(string token, string address, string head) =>
        LoopbackProof.ProofHeader + ": " + LoopbackProof.Compute(token, address, ChallengeIn(head)!) + "\r\n";

    /// <summary>Stops listening and keeps every connection it accepted open.</summary>
    public void StopListening() => listener.Stop();

    private async Task AcceptAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stopping.Token);
            }
            catch (Exception)
            {
                return;
            }
            lock (clients) clients.Add(client);
            _ = ServeAsync(client, Interlocked.Increment(ref connections));
        }
    }

    private async Task ServeAsync(TcpClient client, int connection)
    {
        try
        {
            var stream = client.GetStream();
            while (true)
            {
                var head = await TlsTestServer.ReadHeadAsync(stream);
                if (head.Length == 0) return;
                var length = Regex.Match(head, "^Content-Length: *(\\d+)\r$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                if (length.Success)
                {
                    var body = new byte[int.Parse(length.Groups[1].Value, CultureInfo.InvariantCulture)];
                    var read = 0;
                    while (read < body.Length)
                    {
                        var count = await stream.ReadAsync(body.AsMemory(read));
                        if (count == 0) return;
                        read += count;
                    }
                }
                requests.Enqueue((connection, head));
                var response = answer(head, Port);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
                if (response.Contains("\r\nConnection: close\r\n", StringComparison.Ordinal)) return;
            }
        }
        catch (Exception)
        {
            // The client went away.
        }
    }

    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        listener.Stop();
        await accepting;
        lock (clients)
        {
            foreach (var client in clients) client.Dispose();
        }
        stopping.Dispose();
    }
}

public class LoopbackProofTests
{
    /// <summary>A token in its own form: 43 characters of base64url.</summary>
    private static readonly string Token = new string('a', 40) + "_-Z";

    private const string Challenge = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

    [Test]
    public void TheProofIsTheControlPlanesOwn()
    {
        // From apps/control-plane/src/http/security.ts: loopbackProof(token, address, challenge).
        Assert.That(LoopbackProof.Compute(Token, "127.0.0.1:47800", Challenge),
            Is.EqualTo("36b6ac9395c974c2f272ff9389c115755a26c9c2c8fc0120f6165dc0d53aa455"));
        Assert.That(LoopbackProof.Compute(Token, "[::1]:47800", Challenge),
            Is.EqualTo("764fe8845252a97c1ebcccd3adfea278bc73d2c7b159548a77455c170edb1d91"));
    }

    [Test]
    public void OnlyALiteralLoopbackAddressIsNamed()
    {
        Assert.That(LoopbackProof.AddressOf(new Uri("http://127.0.0.1:47800/")), Is.EqualTo("127.0.0.1:47800"));
        Assert.That(LoopbackProof.AddressOf(new Uri("ws://[::1]:47800/realtime")), Is.EqualTo("[::1]:47800"));
        Assert.That(LoopbackProof.AddressOf(new Uri("http://[::ffff:127.0.0.1]:47800/")), Is.Null, "the control plane's host check refuses it");
        Assert.That(LoopbackProof.AddressOf(new Uri("http://localhost:47800/")), Is.Null, "a name another listener could answer to");
        Assert.That(LoopbackProof.AddressOf(new Uri("http://192.168.1.23:47800/")), Is.Null);
        Assert.That(LoopbackProof.AddressOf(new Uri("http://0.0.0.0:47800/")), Is.Null);
        Assert.That(LoopbackProof.AddressOf(new Uri("http://127.0.0.2:47800/")), Is.Null, "only 127.0.0.1 of 127.0.0.0/8");
        Assert.That(LoopbackProof.AddressOf(new Uri("http://127.1:47800/")), Is.Null.Or.EqualTo("127.0.0.1:47800"), "written short, it is 127.0.0.1 or nothing");
    }

    private static HttpClient Client() => new(new LoopbackProofHandler(Token));

    private static Uri At(RecordingServer server, string path = "api/runtimes") => new($"http://127.0.0.1:{server.Port}/{path}");

    [Test]
    public async Task TheTokenGoesOnlyOnTheConnectionThatJustProvedItself()
    {
        await using var controlPlane = new RecordingServer((head, port) =>
            RecordingServer.ChallengeIn(head) != null
                ? RecordingServer.Http("200 OK", RecordingServer.Proof(Token, "127.0.0.1:" + port, head))
                : RecordingServer.Http("200 OK", close: true));
        using var http = Client();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "something-the-caller-set");
        (await http.GetAsync(At(controlPlane))).Dispose();
        (await http.GetAsync(At(controlPlane, "api/locations"))).Dispose();

        var requests = controlPlane.Requests;
        Assert.That(requests.Select(request => (request.Connection, request.Head.Split(' ')[1])), Is.EqualTo(new[]
        {
            (1, "/api/health"), (1, "/api/runtimes"), (2, "/api/health"), (2, "/api/locations"),
        }), "each request has a connection of its own, and follows the proof on it");
        foreach (var (_, head) in requests.Where(request => request.Head.StartsWith("GET /api/health ", StringComparison.Ordinal)))
        {
            Assert.That(head, Does.Not.Contain("uthorization"), "the proof is asked without the token");
        }
        foreach (var (_, head) in requests.Where(request => !request.Head.StartsWith("GET /api/health ", StringComparison.Ordinal)))
        {
            Assert.That(Regex.Matches(head, "^Authorization: ", RegexOptions.Multiline), Has.Count.EqualTo(1));
            Assert.That(head, Does.Contain("Authorization: Bearer " + Token + "\r\n"));
            Assert.That(head, Does.Not.Contain("something-the-caller-set"));
        }
    }

    [Test]
    public async Task WhatCanNotProveItGetsNoToken()
    {
        var cases = new Dictionary<string, Func<string, int, string>>
        {
            ["no proof, as an old server or another program"] = (_, _) => RecordingServer.Http("200 OK"),
            ["another token, as a control plane with a newer one"] = (head, port) =>
                RecordingServer.Http("200 OK", RecordingServer.Proof(new string('b', 43), "127.0.0.1:" + port, head)),
            ["the real proof for another port, as a relay passes it on"] = (head, port) =>
                RecordingServer.Http("200 OK", RecordingServer.Proof(Token, "127.0.0.1:" + (port + 1), head)),
            ["the real proof for another address"] = (head, port) =>
                RecordingServer.Http("200 OK", RecordingServer.Proof(Token, "[::1]:" + port, head)),
            ["two proofs"] = (head, port) =>
                RecordingServer.Http("200 OK", RecordingServer.Proof(Token, "127.0.0.1:" + port, head) + LoopbackProof.ProofHeader + ": 0\r\n"),
            ["a proof in capitals"] = (head, port) =>
                RecordingServer.Http("200 OK", LoopbackProof.ProofHeader + ": "
                    + LoopbackProof.Compute(Token, "127.0.0.1:" + port, RecordingServer.ChallengeIn(head)!).ToUpperInvariant() + "\r\n"),
            ["something that is not HTTP"] = (_, _) => "SSH-2.0-OpenSSH_9.9\r\n\r\n",
        };
        foreach (var (name, answer) in cases)
        {
            await using var impostor = new RecordingServer(answer);
            using var http = Client();
            var error = Assert.ThrowsAsync<TokenNotSentException>(() => http.GetAsync(At(impostor)), name);
            Assert.That(error!.Outcome, Is.EqualTo(LoopbackProofOutcome.Unproved), name);
            Assert.That(impostor.Paths, Is.EqualTo(new[] { "/api/health" }), name);
            Assert.That(impostor.Requests.All(request => !request.Head.Contains("uthorization", StringComparison.Ordinal)), Is.True, name);
        }
    }

    [Test]
    public async Task NothingAnsweringAndANameGetNoToken()
    {
        using var http = Client();
        var unreachable = Assert.ThrowsAsync<TokenNotSentException>(() => http.GetAsync($"http://127.0.0.1:{ControlPlaneProcess.FreePort()}/api/runtimes"));
        Assert.That(unreachable!.Outcome, Is.EqualTo(LoopbackProofOutcome.Unreachable));

        await using var named = new RecordingServer((head, port) => RecordingServer.Http("200 OK", RecordingServer.Proof(Token, "127.0.0.1:" + port, head)));
        var notLoopback = Assert.ThrowsAsync<TokenNotSentException>(() => http.GetAsync($"http://localhost:{named.Port}/api/runtimes"));
        Assert.That(notLoopback!.Outcome, Is.EqualTo(LoopbackProofOutcome.NotLoopback));
        Assert.That(named.Connections, Is.Zero, "nothing is asked of a name");

        // Proved, but closing the connection: the request can't follow on it, so it is not sent.
        await using var closing = new RecordingServer((head, port) =>
            RecordingServer.Http("200 OK", RecordingServer.Proof(Token, "127.0.0.1:" + port, head), close: true));
        Assert.That(Assert.ThrowsAsync<TokenNotSentException>(() => http.GetAsync(At(closing)))!.Outcome, Is.EqualTo(LoopbackProofOutcome.Unreachable));
        Assert.That(closing.Paths, Is.EqualTo(new[] { "/api/health" }));
    }

    [Test]
    public async Task AControlPlaneThatStoppedProvingGetsNoTokenAndNoConnectionIsReused()
    {
        var proving = true;
        await using var server = new RecordingServer((head, port) =>
            RecordingServer.ChallengeIn(head) == null ? RecordingServer.Http("200 OK", body: "{\"roots\":[]}")
            : proving ? RecordingServer.Http("200 OK", RecordingServer.Proof(Token, "127.0.0.1:" + port, head))
            : RecordingServer.Http("200 OK"));
        using var api = new ControlPlaneApi(new Uri($"http://127.0.0.1:{server.Port}/"), Token);
        await api.GetLocationsAsync();
        // The control plane stops and something else takes its port, keeping its connections open.
        proving = false;
        Assert.ThrowsAsync<ControlPlaneRequestException>(() => api.GetLocationsAsync());
        Assert.ThrowsAsync<ControlPlaneRequestException>(() => api.GetLocationsAsync());
        Assert.That(server.Requests.Select(request => (request.Connection, request.Head.Split(' ')[1])), Is.EqualTo(new[]
        {
            (1, "/api/health"), (1, "/api/locations"), (2, "/api/health"), (3, "/api/health"),
        }), "every proof is asked on a new connection, and none carries a token after it fails");
    }

    [Test]
    public async Task AnotherProgramOnThePortGetsNoTokenFromAnyClient()
    {
        // While the control plane is stopped another program listens on its port and answers everything.
        await using var impostor = new RecordingServer((_, _) => RecordingServer.Http("200 OK", body: "{\"status\":\"ok\"}"));
        var endpoint = new Uri($"ws://127.0.0.1:{impostor.Port}/realtime");

        using (var api = new ControlPlaneApi(ControlPlaneApi.BaseUriFor(endpoint), Token))
        {
            var error = Assert.ThrowsAsync<ControlPlaneRequestException>(() => api.GetLocationsAsync());
            Assert.That(error!.InnerException, Is.TypeOf<TokenNotSentException>());
        }
        using (var transport = new LoopbackWebSocketTransport())
        {
            var error = Assert.ThrowsAsync<TokenNotSentException>(() => transport.ConnectAsync(endpoint, Token, CancellationToken.None));
            Assert.That(error!.Outcome, Is.EqualTo(LoopbackProofOutcome.Unproved));
        }
        await RunUntilEnded(ControlPlaneTarget.Local(endpoint, Token), ConnectionText.AccessTokenUnproved);

        Assert.That(impostor.Requests, Is.Not.Empty);
        foreach (var (_, head) in impostor.Requests)
        {
            Assert.That(head, Does.StartWith("GET /api/health "), "only the proof is asked");
            Assert.That(head, Does.Not.Contain(Token));
            Assert.That(head, Does.Not.Contain("uthorization"));
        }
    }

    [Test]
    public async Task AnEndpointByNameEndsTheSessionWithoutAsking()
    {
        await using var named = new RecordingServer((_, _) => RecordingServer.Http("200 OK"));
        var line = await RunUntilEnded(ControlPlaneTarget.Local(new Uri($"ws://localhost:{named.Port}/realtime"), Token), null);
        Assert.That(line, Does.StartWith("The access code goes only to 127.0.0.1 or [::1]"));
        Assert.That(named.Connections, Is.Zero);
    }

    /// <summary>Runs a session until it ends, and returns the line it ends with.</summary>
    private static async Task<string> RunUntilEnded(ControlPlaneTarget target, string? line)
    {
        var session = target.CreateSession(Samples.Client);
        session.Start();
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (session.Status.Phase != ConnectionPhase.Refused && DateTime.UtcNow < deadline)
            {
                session.Pump();
                await Task.Delay(20);
            }
            Assert.That(session.Status.Phase, Is.EqualTo(ConnectionPhase.Refused), "trying again would not change it");
            Assert.That(session.Status.AccessRefused, Is.True);
            if (line != null) Assert.That(ConnectionText.WhyNotLive(session.Status), Is.EqualTo(line));
            return ConnectionText.WhyNotLive(session.Status);
        }
        finally
        {
            await session.StopAsync();
        }
    }

    [Test]
    public async Task AControlPlaneThatProvesItselfAndRefusesTheUpgradeIsARefusal()
    {
        // Only the control plane holds the token, so this happens when it is replaced between the proof
        // and the upgrade; the upgrade's own answer says so, on the connection that proved itself.
        await using var controlPlane = new RecordingServer((head, port) =>
            RecordingServer.ChallengeIn(head) != null
                ? RecordingServer.Http("200 OK", RecordingServer.Proof(Token, "127.0.0.1:" + port, head))
                : RecordingServer.Http("401 Unauthorized", body: "{\"error\":{\"code\":\"unauthorized\",\"message\":\"A valid access token is required.\"}}", close: true));
        using var transport = new LoopbackWebSocketTransport();
        var refused = Assert.ThrowsAsync<UpgradeRefusedException>(() =>
            transport.ConnectAsync(new Uri($"ws://127.0.0.1:{controlPlane.Port}/realtime"), Token, CancellationToken.None));
        Assert.That(refused!.Status, Is.EqualTo(401));
        Assert.That(controlPlane.Requests.Select(request => (request.Connection, request.Head.Split(' ')[1])),
            Is.EqualTo(new[] { (1, "/api/health"), (1, "/realtime") }), "the upgrade follows the proof on its connection");
    }

    [Test]
    public async Task FailuresReadAsTheKindsCallersExpect()
    {
        // A caller that cancelled, before or during the proof, hears that it cancelled.
        await using var silent = new RecordingServer((_, _) => "");
        using (var invoker = new HttpMessageInvoker(new LoopbackProofHandler(Token)))
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.That(() => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, At(silent)), cancelled.Token),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(() => LoopbackProof.AskAsync(At(silent), Token, cancelled.Token), Throws.InstanceOf<OperationCanceledException>());
            using var soon = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            Assert.That(() => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, At(silent)), soon.Token),
                Throws.InstanceOf<OperationCanceledException>(), "not taken for a control plane that does not answer");
        }

        // After the proof, an answer that can't be read is a request that failed, as from any handler.
        var answers = new[]
        {
            RecordingServer.Http("200 OK", body: new string('x', 64)),
            "NOT HTTP\r\n\r\n",
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\nzz\r\n",
        };
        foreach (var answer in answers)
        {
            await using var server = new RecordingServer((head, port) =>
                RecordingServer.ChallengeIn(head) != null ? RecordingServer.Http("200 OK", RecordingServer.Proof(Token, "127.0.0.1:" + port, head)) : answer);
            using var invoker = new HttpMessageInvoker(new LoopbackProofHandler(Token, maxResponseBytes: 16));
            Assert.That(() => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, At(server)), CancellationToken.None),
                Throws.InstanceOf<HttpRequestException>(), answer);
        }
    }

    [Test]
    public async Task AResponseFramedAmbiguouslyIsRefusedAndATrailerIsReadWhole()
    {
        var refused = new Dictionary<string, string>
        {
            ["two lengths"] = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nContent-Length: 3\r\nConnection: close\r\n\r\n{}",
            ["a length and chunks"] = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n2\r\n{}\r\n0\r\n\r\n",
            ["a coding it does not read"] = "HTTP/1.1 200 OK\r\nTransfer-Encoding: gzip\r\nConnection: close\r\n\r\n{}",
            ["a folded header"] = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}",
            ["a bare carriage return"] = "HTTP/1.1 200 OK\r\nX-Split: a\rb\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}",
            ["a chunk size of nine digits"] = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n000000002\r\n{}\r\n0\r\n\r\n",
            ["a chunk size that reads negative"] = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\nffffffffffffffff\r\n{}\r\n0\r\n\r\n",
            ["a chunk longer than its size"] = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n1\r\n{}\r\n0\r\n\r\n",
        };
        foreach (var (name, answer) in refused)
        {
            await using var server = new RecordingServer((head, port) =>
                RecordingServer.ChallengeIn(head) != null ? RecordingServer.Http("200 OK", RecordingServer.Proof(Token, "127.0.0.1:" + port, head)) : answer);
            using var invoker = new HttpMessageInvoker(new LoopbackProofHandler(Token));
            Assert.That(() => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, At(server)), CancellationToken.None),
                Throws.InstanceOf<HttpRequestException>(), name);
        }

        // A proof answered in chunks with a trailer: the trailer is read to its end, so the request
        // that follows on the connection reads its own answer.
        await using var chunked = new RecordingServer((head, port) =>
        {
            var challenge = RecordingServer.ChallengeIn(head);
            if (challenge == null)
            {
                return "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n2;name=value\r\n{}\r\n0\r\nX-Trailer: done\r\n\r\n";
            }
            return "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n" + RecordingServer.Proof(Token, "127.0.0.1:" + port, head)
                + "\r\n2\r\n{}\r\n0\r\nX-Trailer: done\r\n\r\n";
        });
        using (var invoker = new HttpMessageInvoker(new LoopbackProofHandler(Token)))
        {
            using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, At(chunked)), CancellationToken.None);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("{}"));
        }
        Assert.That(chunked.Requests.Select(request => (request.Connection, request.Head.Split(' ')[1])),
            Is.EqualTo(new[] { (1, "/api/health"), (1, "/api/runtimes") }));
    }

    [Test]
    public async Task ARedirectIsNeverFollowedWithTheToken()
    {
        await using var elsewhere = new RecordingServer((_, _) => RecordingServer.Http("200 OK"));
        // It holds the token, as only the control plane does, and redirects what follows the proof.
        await using var redirecting = new RecordingServer((head, port) =>
            RecordingServer.ChallengeIn(head) != null
                ? RecordingServer.Http("200 OK", RecordingServer.Proof(Token, "127.0.0.1:" + port, head))
                : RecordingServer.Http("307 Temporary Redirect", "Location: http://127.0.0.1:" + elsewhere.Port + "/api/locations\r\n", close: true));
        using var api = new ControlPlaneApi(new Uri($"http://127.0.0.1:{redirecting.Port}/"), Token);
        Assert.ThrowsAsync<ControlPlaneRequestException>(() => api.GetLocationsAsync());
        Assert.That(redirecting.Paths, Is.EqualTo(new[] { "/api/health", "/api/locations" }));
        Assert.That(elsewhere.Connections, Is.Zero, "the redirect is not followed");
    }
}

/// <summary>The proof against a real control plane, behind a relay, and after an impostor kept a connection.</summary>
[Category("ControlPlane")]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class LiveLoopbackProofTests
{
    private string dataDir = null!;
    private ControlPlaneProcess? controlPlane;

    [SetUp]
    public void CreateDataDirectory() => dataDir = Directory.CreateTempSubdirectory("halcyonic-proof-tests-").FullName;

    [TearDown]
    public void CleanUp()
    {
        controlPlane?.Dispose();
        Directory.Delete(dataDir, recursive: true);
    }

    [Test]
    public async Task TheControlPlaneProvesItsTokenAndARelayCanNotPassItOn()
    {
        controlPlane = await ControlPlaneProcess.StartAsync(dataDir, 0);
        var baseUri = ControlPlaneApi.BaseUriFor(controlPlane.RealtimeEndpoint);
        Assert.That(await LoopbackProof.AskAsync(baseUri, controlPlane.AccessToken, CancellationToken.None), Is.EqualTo(LoopbackProofOutcome.Proved));
        Assert.That(await LoopbackProof.AskAsync(baseUri, new string('b', 43), CancellationToken.None), Is.EqualTo(LoopbackProofOutcome.Unproved),
            "a stale token is not proved, so it is never sent");
        using (var api = new ControlPlaneApi(baseUri, controlPlane.AccessToken))
        {
            Assert.That((await api.GetLocationsAsync()).Roots, Is.Not.Null);
        }

        // A relay on another port that passes everything to the control plane, naming the control
        // plane's own port as the host, so its host check passes.
        var relayed = new ConcurrentQueue<string>();
        var answers = new ConcurrentQueue<string>();
        var relay = new TcpListener(IPAddress.Loopback, 0);
        relay.Start();
        var relayPort = ((IPEndPoint)relay.LocalEndpoint).Port;
        using var stopping = new CancellationTokenSource();
        var relaying = RelayAsync(relay, controlPlane.Port, relayPort, relayed, answers, stopping.Token);
        try
        {
            var throughRelay = new Uri($"http://127.0.0.1:{relayPort}/");
            Assert.That(await LoopbackProof.AskAsync(throughRelay, controlPlane.AccessToken, CancellationToken.None), Is.EqualTo(LoopbackProofOutcome.Unproved),
                "the control plane proves the address it was reached at, not the relay's");
            using var api = new ControlPlaneApi(throughRelay, controlPlane.AccessToken);
            Assert.ThrowsAsync<ControlPlaneRequestException>(() => api.GetLocationsAsync());
            Assert.That(relayed, Is.Not.Empty);
            Assert.That(answers.Any(answer => answer.Contains(LoopbackProof.ProofHeader + ":", StringComparison.OrdinalIgnoreCase)), Is.True,
                "the control plane's own proof came back through the relay, for its own port");
            Assert.That(relayed.All(head => !head.Contains(controlPlane.AccessToken, StringComparison.Ordinal)), Is.True, "no token passed the relay");
        }
        finally
        {
            stopping.Cancel();
            relay.Stop();
            await relaying;
        }
    }

    [Test]
    public async Task AConnectionAnImpostorKeptOpenIsNeverUsedOnceTheControlPlaneIsBack()
    {
        // The review's case: while the control plane is stopped an impostor answers with keep-alive,
        // then lets go of the port and keeps the connection, waiting for what comes on it.
        if (OperatingSystem.IsWindows()) Assert.Ignore("needs Unix file modes for the access token");
        var token = new string('c', 43);
        var file = Path.Combine(dataDir, "access-token");
        File.WriteAllText(file, token + "\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        // The impostor takes a port of its own, which the control plane then takes over.
        await using var impostor = new RecordingServer((_, _) => RecordingServer.Http("200 OK"));
        var port = impostor.Port;
        using var api = new ControlPlaneApi(new Uri($"http://127.0.0.1:{port}/"), token);
        Assert.ThrowsAsync<ControlPlaneRequestException>(() => api.GetLocationsAsync());
        impostor.StopListening();

        controlPlane = await ControlPlaneProcess.StartAsync(dataDir, port);
        Assert.That(controlPlane.AccessToken, Is.EqualTo(token));
        Assert.That((await api.GetLocationsAsync()).Roots, Is.Not.Null, "the control plane, on a connection of its own, proves itself");
        Assert.That(impostor.Paths, Is.EqualTo(new[] { "/api/health" }), "the kept connection carried nothing more");
    }

    /// <summary>
    /// Passes each connection to the control plane in both directions, naming the control plane's
    /// own port as the host in the first request, until either side closes.
    /// </summary>
    private static async Task RelayAsync(
        TcpListener relay,
        int target,
        int own,
        ConcurrentQueue<string> relayed,
        ConcurrentQueue<string> answers,
        CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await relay.AcceptTcpClientAsync(stopping);
            }
            catch (Exception)
            {
                return;
            }
            using (client)
            using (var upstream = new TcpClient())
            {
                await upstream.ConnectAsync(IPAddress.Loopback, target, stopping);
                var head = await TlsTestServer.ReadHeadAsync(client.GetStream());
                relayed.Enqueue(head);
                var rewritten = head.Replace("127.0.0.1:" + own, "127.0.0.1:" + target, StringComparison.Ordinal);
                await upstream.GetStream().WriteAsync(Encoding.ASCII.GetBytes(rewritten), stopping);
                using var either = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                var back = PipeAsync(upstream.GetStream(), client.GetStream(), answers, either.Token);
                var on = PipeAsync(client.GetStream(), upstream.GetStream(), relayed, either.Token);
                await Task.WhenAny(back, on);
                either.Cancel();
                await Task.WhenAll(back, on);
            }
        }
    }

    private static async Task PipeAsync(Stream from, Stream to, ConcurrentQueue<string> seen, CancellationToken stopping)
    {
        var buffer = new byte[8192];
        try
        {
            int count;
            while ((count = await from.ReadAsync(buffer, stopping)) > 0)
            {
                seen.Enqueue(Encoding.ASCII.GetString(buffer, 0, count));
                await to.WriteAsync(buffer.AsMemory(0, count), stopping);
            }
        }
        catch (Exception)
        {
            // One side closed, or the relay stopped.
        }
    }
}

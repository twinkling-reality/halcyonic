using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The preview relay on the headset (ADR 0052, amended 2026-11-12): the browser asks the headset's loopback address, the
/// relay carries each connection whole over one pinned WebSocket to the paired computer, here a stand-in that answers as
/// the computer's preview stream does.
/// </summary>
public class PreviewRelayTests
{
    private const string Project = "01920000-0000-7000-8000-000000000001";

    /// <summary>What the stand-in computer saw: the upgrade's head, and every byte the stream carried to it.</summary>
    private sealed class Seen
    {
        public string? Upgrade;
        public readonly ConcurrentQueue<string> Carried = new();
        public string CarriedText => string.Concat(Carried);
    }

    /// <summary>
    /// A stand-in computer. <paramref name="refuse"/> answers the upgrade itself (a status line and a body); otherwise it
    /// opens the WebSocket and hands it to <paramref name="serve"/>.
    /// </summary>
    private static (PreviewRelay Relay, TlsTestServer Mac, Seen Seen) Start(Func<WebSocket, Seen, Task>? serve = null, string? refuse = null)
    {
        var seen = new Seen();
        var mac = TlsTestServer.Start(async stream =>
        {
            var head = await TlsTestServer.ReadHeadAsync(stream);
            seen.Upgrade = head;
            if (refuse != null)
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes(refuse));
                return;
            }
            var key = Line(head, "Sec-WebSocket-Key");
            var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n"));
            using var socket = WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(30));
            await (serve ?? AnswerOnce("HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nhello"))(socket, seen);
        });
        var pairing = new PairedControlPlane("127.0.0.1", mac.Port, mac.CertificateSha256, Guid.NewGuid().ToString(), "hlcd_" + new string('A', 43));
        return (PreviewRelay.Start(ControlPlaneTarget.Paired(pairing), Project), mac, seen);
    }

    private static string Line(string head, string name)
    {
        foreach (var line in head.Split("\r\n"))
            if (line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase)) return line.Substring(name.Length + 1).Trim();
        return "";
    }

    /// <summary>Takes the first message the stream carries, then answers as the app's server would and ends.</summary>
    private static Func<WebSocket, Seen, Task> AnswerOnce(string answer) => async (socket, seen) =>
    {
        seen.Carried.Enqueue(await ReceiveTextAsync(socket));
        await socket.SendAsync(Encoding.ASCII.GetBytes(answer), WebSocketMessageType.Binary, true, CancellationToken.None);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "ended", CancellationToken.None);
    };

    private static async Task<string> ReceiveTextAsync(WebSocket socket)
    {
        var buffer = new byte[64 * 1024];
        var received = await socket.ReceiveAsync(buffer, CancellationToken.None);
        return Encoding.ASCII.GetString(buffer, 0, received.Count);
    }

    [Test]
    public async Task TheBrowsersRequestGoesWholeWithAnyMethodAndTheCredentialOnlyOnTheUpgrade()
    {
        var (relay, mac, seen) = Start();
        await using var _ = mac;
        using var __ = relay;
        Assert.That(relay.Url.ToString(), Is.EqualTo("http://127.0.0.1:" + relay.Port + "/"));
        using var browser = new HttpClient();
        var posted = await browser.PostAsync(new Uri(relay.Url, "api/score?v=2"), new StringContent("{\"score\":3}"));
        Assert.That(posted.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await posted.Content.ReadAsStringAsync(), Is.EqualTo("hello"));
        Assert.That(seen.Upgrade, Does.StartWith("GET /api/projects/" + Project + "/preview-stream HTTP/1.1"));
        Assert.That(seen.Upgrade, Does.Contain("Authorization: Bearer hlcd_"), "the credential is on the upgrade");
        var carried = seen.CarriedText;
        Assert.That(carried, Does.StartWith("POST /api/score?v=2 HTTP/1.1\r\n"), "any method, the path as the browser asked");
        Assert.That(carried, Does.Contain("\r\nConnection: close\r\n"), "one request a connection, so each meets the checks");
        Assert.That(carried, Does.Not.Contain("Authorization"), "never in the browser's bytes");
    }

    [Test]
    public async Task AWebSocketUpgradeKeepsItsConnectionBothWays()
    {
        var (relay, mac, seen) = Start(async (socket, seen) =>
        {
            seen.Carried.Enqueue(await ReceiveTextAsync(socket));
            await socket.SendAsync(Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n\r\nreload"), WebSocketMessageType.Binary, true, CancellationToken.None);
            seen.Carried.Enqueue(await ReceiveTextAsync(socket));
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "ended", CancellationToken.None);
        });
        await using var _ = mac;
        using var __ = relay;
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, relay.Port);
        var page = client.GetStream();
        await page.WriteAsync(Encoding.ASCII.GetBytes("GET /hmr HTTP/1.1\r\nHost: 127.0.0.1:" + relay.Port + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n\r\n"));
        var buffer = new byte[4096];
        var got = "";
        while (!got.Contains("reload")) got += Encoding.ASCII.GetString(buffer, 0, await page.ReadAsync(buffer));
        await page.WriteAsync(Encoding.ASCII.GetBytes("ack"));
        for (var wait = 0; wait < 100 && seen.Carried.Count < 2; wait++) await Task.Delay(20);
        var carried = seen.CarriedText;
        Assert.That(carried, Does.Contain("Connection: Upgrade"), "an upgrade keeps its own Connection");
        Assert.That(carried, Does.Not.Contain("Connection: close"));
        Assert.That(carried, Does.EndWith("ack"), "the page's bytes after the upgrade reach the app");
    }

    [Test]
    public async Task ABrowserThatGoesAwayEndsItsStreamSoItHoldsNone()
    {
        var ended = new TaskCompletionSource<WebSocketCloseStatus?>();
        var (relay, mac, _) = Start(async (socket, seen) =>
        {
            // The app's server never answers: only the browser leaving can end this stream.
            var buffer = new byte[1024];
            try
            {
                while (true)
                {
                    var received = await socket.ReceiveAsync(buffer, CancellationToken.None);
                    if (received.MessageType == WebSocketMessageType.Close) break;
                }
            }
            catch (WebSocketException)
            {
            }
            ended.TrySetResult(socket.CloseStatus);
        });
        await using var _ = mac;
        using var __ = relay;
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, relay.Port);
            await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: 127.0.0.1:" + relay.Port + "\r\n\r\n"));
            await Task.Delay(300);
        }
        var finished = await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(6)));
        Assert.That(finished, Is.SameAs(ended.Task), "the stream to the computer ended once the browser went away");
    }

    [Test]
    public async Task OnlyARequestNamingItsOwnAddressAndNoServiceWorkerIsCarried()
    {
        var (relay, mac, seen) = Start();
        await using var _ = mac;
        using var __ = relay;
        using var browser = new HttpClient();
        using var renamed = new HttpRequestMessage(HttpMethod.Get, relay.Url);
        renamed.Headers.Host = "attacker.example:" + relay.Port;
        Assert.That((await browser.SendAsync(renamed)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "a page renamed to this address reads nothing");
        using var worker = new HttpRequestMessage(HttpMethod.Get, new Uri(relay.Url, "sw.js"));
        worker.Headers.Add("Service-Worker", "script");
        Assert.That((await browser.SendAsync(worker)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(seen.Upgrade, Is.Null, "neither reached the computer");
    }

    [Test]
    public async Task AComputerThatDoesNotAnswerIsSaidPlainly()
    {
        var (relay, mac, _) = Start();
        await mac.DisposeAsync();
        using var __ = relay;
        using var browser = new HttpClient();
        var page = await browser.GetAsync(relay.Url);
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
        Assert.That(await page.Content.ReadAsStringAsync(), Is.EqualTo("Your computer isn't answering."));
    }

    [TestCase(409, "preview_not_confined", "preview_not_confined")]
    [TestCase(429, "too_many_streams", "too_many_streams")]
    [TestCase(401, "device_revoked", "not_paired")]
    public async Task ARefusedStreamIsSaidInTheProductsWordsNeverItsMessage(int status, string code, string said)
    {
        var body = "{\"error\":{\"code\":\"" + code + "\",\"message\":\"developer words\",\"issues\":[]}}";
        var (relay, mac, _) = Start(refuse: "HTTP/1.1 " + status + " Refused\r\nContent-Type: application/json\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n" + body);
        await using var __ = mac;
        using var ___ = relay;
        using var browser = new HttpClient();
        var page = await browser.GetAsync(relay.Url);
        Assert.That((int)page.StatusCode, Is.EqualTo(status));
        var words = await page.Content.ReadAsStringAsync();
        Assert.That(words, Is.EqualTo(PreviewRelay.WhyNot(said)));
        Assert.That(words, Does.Not.Contain("developer"));
    }

    [Test]
    public async Task AStreamTheComputerEndsBeforeAnyAnswerSaysWhy()
    {
        var (relay, mac, _) = Start(async (socket, seen) =>
        {
            seen.Carried.Enqueue(await ReceiveTextAsync(socket));
            await socket.CloseAsync((WebSocketCloseStatus)4409, "preview_not_latest", CancellationToken.None);
        });
        await using var __ = mac;
        using var ___ = relay;
        using var browser = new HttpClient();
        var page = await browser.GetAsync(relay.Url);
        Assert.That((int)page.StatusCode, Is.EqualTo(409));
        Assert.That(await page.Content.ReadAsStringAsync(), Is.EqualTo(PreviewRelay.WhyNot("preview_not_latest")));
    }

    [Test]
    public void EachCodeTheComputerSendsHasItsOwnWords()
    {
        Assert.That(PreviewRelay.WhyNot("preview_not_confined"), Is.EqualTo("This app stays on your computer. It may read outside its folder."));
        Assert.That(PreviewRelay.WhyNot("preview_unreachable"), Is.EqualTo("The preview isn't answering. Try again."));
        Assert.That(PreviewRelay.WhyNot("something_new"), Is.EqualTo("Your computer didn't show this preview. Reason unknown."));
        Assert.That(PreviewRelay.WhyNot(null), Is.EqualTo(PreviewRelay.WhyNot("something_new")));
    }
}

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The preview relay on the headset (ADR 0052): the browser asks the headset's loopback address, the
/// relay asks the paired computer over pinned TLS, here a stand-in that answers as the computer's relay.
/// </summary>
public class PreviewRelayTests
{
    private const string Project = "01920000-0000-7000-8000-000000000001";

    private static async Task<(PreviewRelay Relay, TlsTestServer Mac, Func<string?> Asked)> StartAsync(string answer)
    {
        string? asked = null;
        var mac = TlsTestServer.Start(async stream =>
        {
            asked = await TlsTestServer.ReadHeadAsync(stream);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(answer));
        });
        var pairing = new PairedControlPlane("127.0.0.1", mac.Port, mac.CertificateSha256, Guid.NewGuid().ToString(), "hlcd_" + new string('A', 43));
        var relay = PreviewRelay.Start(ControlPlaneTarget.Paired(pairing), Project);
        return (relay, mac, () => asked);
    }

    [Test]
    public async Task TheBrowserGetsThePreviewsFileAndNeverTheCredentialOrTheComputersOtherHeaders()
    {
        var (relay, mac, asked) = await StartAsync(
            "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nSet-Cookie: tracking=1\r\nAccess-Control-Allow-Origin: *\r\nContent-Length: 20\r\nConnection: close\r\n\r\n<h1>Star garden</h1>");
        await using var _ = mac;
        using var __ = relay;
        Assert.That(relay.Url.ToString(), Is.EqualTo("http://127.0.0.1:" + relay.Port + "/"));
        using var browser = new HttpClient();
        var page = await browser.GetAsync(new Uri(relay.Url, "assets/app.js?v=2"));
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await page.Content.ReadAsStringAsync(), Is.EqualTo("<h1>Star garden</h1>"));
        Assert.That(page.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        Assert.That(page.Headers.CacheControl?.NoStore, Is.True, "the browser keeps nothing across previews");
        Assert.That(page.Headers.Contains("Set-Cookie"), Is.False);
        Assert.That(page.Headers.Contains("Access-Control-Allow-Origin"), Is.False);
        var head = asked();
        Assert.That(head, Does.StartWith("GET /api/projects/" + Project + "/preview/assets/app.js?v=2 HTTP/1.1"));
        Assert.That(head, Does.Contain("Authorization: Bearer hlcd_"), "the credential goes to the computer");
    }

    [Test]
    public async Task OnlyAReadOfItsOwnAddressIsRelayed()
    {
        var (relay, mac, asked) = await StartAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
        await using var _ = mac;
        using var __ = relay;
        using var browser = new HttpClient();
        var posted = await browser.PostAsync(relay.Url, new StringContent("x"));
        Assert.That(posted.StatusCode, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
        using var renamed = new HttpRequestMessage(HttpMethod.Get, relay.Url);
        renamed.Headers.Host = "attacker.example:" + relay.Port;
        Assert.That((await browser.SendAsync(renamed)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "a page renamed to this address reads nothing");
        using var worker = new HttpRequestMessage(HttpMethod.Get, new Uri(relay.Url, "sw.js"));
        worker.Headers.Add("Service-Worker", "script");
        Assert.That((await browser.SendAsync(worker)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(asked(), Is.Null, "none of them reached the computer");
    }

    [Test]
    public async Task AComputerThatDoesNotAnswerIsSaidPlainly()
    {
        var (relay, mac, _) = await StartAsync("");
        await mac.DisposeAsync();
        using var __ = relay;
        using var browser = new HttpClient();
        var page = await browser.GetAsync(relay.Url);
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
        Assert.That(await page.Content.ReadAsStringAsync(), Is.EqualTo("Your computer isn't answering."));
    }

    [Test]
    public async Task APreviewTheComputerKeepsIsSaidInTheProductsWordsNeverItsMessage()
    {
        const string refusal = "{\"error\":{\"code\":\"preview_not_confined\",\"message\":\"developer words\"}}";
        var (relay, mac, _) = await StartAsync(
            "HTTP/1.1 409 Conflict\r\nContent-Type: application/json\r\nContent-Length: " + refusal.Length + "\r\nConnection: close\r\n\r\n" + refusal);
        await using var __ = mac;
        using var ___ = relay;
        using var browser = new HttpClient();
        var page = await browser.GetAsync(relay.Url);
        Assert.That((int)page.StatusCode, Is.EqualTo(409));
        Assert.That(await page.Content.ReadAsStringAsync(), Is.EqualTo(PreviewRelay.WhyNot("preview_not_confined")));
        Assert.That(PreviewRelay.WhyNot("preview_not_confined"), Does.Not.Contain("developer"));
    }

    /// <summary>A request written exactly, as another app on the headset could send it; the status line back.</summary>
    private static async Task<string> RawAsync(PreviewRelay relay, string target)
    {
        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, relay.Port);
        var stream = client.GetStream();
        var request = Encoding.ASCII.GetBytes("GET " + target + " HTTP/1.1\r\nHost: 127.0.0.1:" + relay.Port + "\r\n\r\n");
        await stream.WriteAsync(request);
        using var reader = new System.IO.StreamReader(stream, Encoding.ASCII);
        return await reader.ReadLineAsync() ?? "";
    }

    [TestCase("/../../../../api/events")]
    [TestCase("/%2e%2e/%2e%2e/%2e%2e/%2e%2e/api/events")]
    [TestCase("/%2E%2e/api/events")]
    [TestCase("/..%2f..%2fapi/events")]
    [TestCase("/assets/./../../api/events")]
    [TestCase("/a\\..\\..\\api/events")]
    public async Task NothingClimbsOutOfThePreviewToTheComputersOtherRoutes(string target)
    {
        var (relay, mac, asked) = await StartAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
        await using var _ = mac;
        using var __ = relay;
        Assert.That(await RawAsync(relay, target), Does.StartWith("HTTP/1.1 400"));
        Assert.That(asked(), Is.Null, "the computer was asked nothing, so the credential went nowhere");
        Assert.That(relay.InsidePreview("/assets/app.js?v=../x")?.AbsolutePath, Is.EqualTo("/api/projects/" + Project + "/preview/assets/app.js"), "a query may hold dots");
    }

    [Test]
    public async Task AHeadsetTheComputerNoLongerAcceptsIsToldToPairAgain()
    {
        const string refusal = "{\"error\":{\"code\":\"device_revoked\",\"message\":\"developer words\"}}";
        var (relay, mac, _) = await StartAsync(
            "HTTP/1.1 401 Unauthorized\r\nContent-Type: application/json\r\nContent-Length: " + refusal.Length + "\r\nConnection: close\r\n\r\n" + refusal);
        await using var __ = mac;
        using var ___ = relay;
        using var browser = new HttpClient();
        var page = await browser.GetAsync(relay.Url);
        Assert.That((int)page.StatusCode, Is.EqualTo(401));
        Assert.That(await page.Content.ReadAsStringAsync(), Is.EqualTo(PreviewRelay.WhyNot("not_paired")));
    }
}

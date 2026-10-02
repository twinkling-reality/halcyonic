using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The device's side of pairing (ADR 0017) against a real control plane serving its network
/// listener on a loopback address: pairing with the code, working over the pinned transports as the
/// device, and what a wrong code, a relay in the middle, a revocation and another identity do.
/// </summary>
[Category("ControlPlane")]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class PairingLiveTests
{
    private readonly List<ControlPlaneProcess> processes = new();
    private readonly List<string> directories = new();
    private readonly List<RecordingTransport> connections = new();
    private readonly CommandFactory commands = new(Samples.Client);
    private RealtimeSession? session;

    [TearDown]
    public async Task CleanUp()
    {
        if (session != null) await session.StopAsync();
        foreach (var process in processes) process.Dispose();
        foreach (var directory in directories) Directory.Delete(directory, recursive: true);
    }

    private string TemporaryDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("halcyonic-pairing-tests-").FullName;
        directories.Add(directory);
        return directory;
    }

    private async Task<ControlPlaneProcess> StartAsync(int networkPort, string? dataDir = null)
    {
        var process = await ControlPlaneProcess.StartAsync(dataDir ?? TemporaryDirectory(), networkPort: networkPort);
        processes.Add(process);
        return process;
    }

    /// <summary>A request to the control plane's loopback API, with its access token, as the owner's Mac makes.</summary>
    private static async Task<JToken?> LoopbackAsync(ControlPlaneProcess controlPlane, HttpMethod method, string path)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(method, $"http://127.0.0.1:{controlPlane.Port}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", controlPlane.AccessToken);
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.IsSuccessStatusCode, Is.True, method + " " + path + ": " + text);
        return text.Length == 0 ? null : Json.Parse(text);
    }

    private static async Task<(string Code, string Pin)> OpenPairingAsync(ControlPlaneProcess controlPlane)
    {
        var opened = (await LoopbackAsync(controlPlane, HttpMethod.Post, "/api/pairing"))!;
        return ((string)opened["code"]!, (string)opened["listener"]!["certificate_sha256"]!);
    }

    private static string Wrong(string code) => ((int.Parse(code) + 1) % 100_000_000).ToString("D8");

    private RealtimeSession Connect(ControlPlaneTarget target)
    {
        var options = new RealtimeSessionOptions(target.Endpoint, target.Credential, Samples.Client)
        {
            InitialRetryDelay = TimeSpan.FromMilliseconds(100),
            MaxRetryDelay = TimeSpan.FromMilliseconds(500),
        };
        session = new RealtimeSession(options, () =>
        {
            var transport = new RecordingTransport(target.CreateTransport());
            lock (connections) connections.Add(transport);
            return transport;
        });
        session.Start();
        return session;
    }

    private Task<StateChanges> Until(Func<RealtimeSession, bool> condition, string description, int seconds = 15) =>
        Pumping.Until(session!, condition, description, TimeSpan.FromSeconds(seconds), () => string.Join("\n", processes.Select(p => p.Output)));

    private async Task<CommandResult> RunAsync(CommandEnvelope command)
    {
        var ack = await session!.SubmitAsync(command);
        Assert.That(ack.Disposition, Is.EqualTo(CommandAckDisposition.Accepted), command.CommandType);
        await Until(
            s => s.State.Commands.TryGetValue(command.CommandId, out var view) && view.Status == CommandStatus.Completed,
            command.CommandType + " completes");
        return session.State.Commands[command.CommandId].Result!;
    }

    [Test]
    public async Task PairsWithTheCodeThenDirectsWorkOverPinnedTlsAsTheDevice()
    {
        var networkPort = ControlPlaneProcess.FreePort();
        var controlPlane = await StartAsync(networkPort);
        var (code, pin) = await OpenPairingAsync(controlPlane);

        var paired = await PairingClient.PairAsync("127.0.0.1", networkPort, code[..4] + " " + code[4..], "Quest 3");
        Assert.That(paired.CertificateSha256, Is.EqualTo(pin), "the device pins the certificate its pairing connection saw");
        Assert.That(paired.Address, Is.EqualTo("127.0.0.1:" + networkPort));
        Assert.That(paired.Credential, Does.Match("^hlcd_[A-Za-z0-9_-]{43}$"));
        var status = (await LoopbackAsync(controlPlane, HttpMethod.Get, "/api/pairing"))!;
        Assert.That((string)status["state"]!, Is.EqualTo("paired"));
        Assert.That((string)status["device"]!["device_id"]!, Is.EqualTo(paired.DeviceId));
        Assert.That((string)status["device"]!["label"]!, Is.EqualTo("Quest 3"));

        var store = new FilePairingStore(Path.Combine(TemporaryDirectory(), "pairing.json"));
        store.Save(paired);
        var target = ControlPlaneTarget.Paired(store.Load()!);
        Connect(target);
        await Until(s => s.Status.IsLive, "the session is live over pinned TLS");

        var project = (ProjectCreatedResult)await RunAsync(commands.CreateProject("From the headset"));
        var workstream = (WorkstreamCreatedResult)await RunAsync(commands.CreateWorkstream(project.ProjectId, "Paginate the order history", null));
        var execution = (ExecutionCreatedResult)await RunAsync(commands.StartExecution(
            workstream.WorkstreamId, "mock", "Add paging.", new Dictionary<string, JToken> { ["scenario"] = "approval_required" }, modelRef: "mock/fast"));
        await Until(s => s.State.Executions[execution.ExecutionId].Status == ExecutionStatus.WaitingForHuman, "the execution waits for approval");
        var approval = session!.State.Executions[execution.ExecutionId].PendingApprovals.Single();
        await RunAsync(commands.RespondToApproval(execution.ExecutionId, approval.ApprovalId, ApprovalDecision.Approve));
        await Until(s => s.State.Executions[execution.ExecutionId].Status == ExecutionStatus.Completed, "the turn finishes", seconds: 20);

        using (var api = target.CreateApi())
        {
            var history = new ActivityLog();
            history.Record(await api.ReadAllAsync(workstream.WorkstreamId, session.State.Journal!.JournalId));
            Assert.That(history.For(execution.ExecutionId).Select(entry => entry.Text), Does.Contain("Approved"));
        }

        var events = HalcyonicJson.Deserialize<EventsResponse>((await LoopbackAsync(controlPlane, HttpMethod.Get, "/api/events?after=0&limit=1000"))!.ToString());
        var principals = events.Events.Select(stored => stored.Event).OfType<CommandAcceptedEvent>().Select(accepted => accepted.Payload.Principal).ToList();
        Assert.That(principals, Has.Count.EqualTo(4));
        Assert.That(principals.All(principal => principal is DevicePrincipal device && device.DeviceId == paired.DeviceId), Is.True,
            "the journal names the device as the sender of every command it sent");
        Assert.That(events.Events.Select(stored => stored.Event).OfType<DevicePairedEvent>().Single().Payload.DeviceId, Is.EqualTo(paired.DeviceId));
        var received = connections.SelectMany(connection => connection.Received).ToList();
        Assert.That(received.Any(message => message.Contains("\"device.paired\"")), Is.False, "device events never reach the realtime stream");
        foreach (var message in received) Json.AssertRoundTrips<ServerMessage>(message);
    }

    [Test]
    public async Task RefusesWrongCodesAndClosesPairingAfterThree()
    {
        var networkPort = ControlPlaneProcess.FreePort();
        var controlPlane = await StartAsync(networkPort);
        var (code, _) = await OpenPairingAsync(controlPlane);
        foreach (var left in new long[] { 2, 1, 0 })
        {
            var refused = Assert.ThrowsAsync<PairingException>(() => PairingClient.PairAsync("127.0.0.1", networkPort, Wrong(code), "Quest 3"));
            Assert.That(refused!.Code, Is.EqualTo("wrong_code"));
            Assert.That(refused.AttemptsLeft, Is.EqualTo(left));
        }
        var closed = Assert.ThrowsAsync<PairingException>(() => PairingClient.PairAsync("127.0.0.1", networkPort, code, "Quest 3"));
        Assert.That(closed!.Code, Is.EqualTo("pairing_closed"));
        Assert.That((string)(await LoopbackAsync(controlPlane, HttpMethod.Get, "/api/pairing"))!["state"]!, Is.EqualTo("locked"));
        var invalid = Assert.ThrowsAsync<PairingException>(() => PairingClient.PairAsync("127.0.0.1", networkPort, "1234", "Quest 3"));
        Assert.That(invalid!.Code, Is.EqualTo("invalid_code"));
    }

    [Test]
    public async Task ARelayInTheMiddleCannotPairEvenWithTheCode()
    {
        var networkPort = ControlPlaneProcess.FreePort();
        var controlPlane = await StartAsync(networkPort);
        var (code, _) = await OpenPairingAsync(controlPlane);
        await using var relay = TlsRelay.Start(networkPort);

        var refused = Assert.ThrowsAsync<PairingException>(() => PairingClient.PairAsync("127.0.0.1", relay.Port, code, "Quest 3"));
        Assert.That(refused!.Code, Is.EqualTo("wrong_code"), "the device bound the relay's certificate into its proof");
        Assert.That(relay.Relayed, Is.GreaterThan(0), "the relay passed the exchange on");
        var status = (await LoopbackAsync(controlPlane, HttpMethod.Get, "/api/pairing"))!;
        Assert.That((int)status["failed_attempts"]!, Is.EqualTo(1));

        var paired = await PairingClient.PairAsync("127.0.0.1", networkPort, code, "Quest 3");
        Assert.That(paired.CertificateSha256, Is.Not.EqualTo(relay.CertificateSha256));
    }

    [Test]
    public async Task RevokingTheDeviceEndsItsSessionAndItsCredential()
    {
        var networkPort = ControlPlaneProcess.FreePort();
        var controlPlane = await StartAsync(networkPort);
        var (code, _) = await OpenPairingAsync(controlPlane);
        var paired = await PairingClient.PairAsync("127.0.0.1", networkPort, code, "Quest 3");
        var target = ControlPlaneTarget.Paired(paired);
        Connect(target);
        await Until(s => s.Status.IsLive, "the session is live");

        await LoopbackAsync(controlPlane, HttpMethod.Post, "/api/devices/" + paired.DeviceId + "/revoke");
        await Until(s => !s.Status.IsLive && (s.Status.Detail ?? "").Contains("revoked"), "the session learns the device was revoked");
        using var api = target.CreateApi();
        var refused = Assert.ThrowsAsync<ControlPlaneRequestException>(() => api.ReadAllAsync("01a0f0a0-0000-7000-8000-000000000001", session!.State.Journal!.JournalId));
        Assert.That(refused!.Message, Does.Contain("revoked"));
    }

    [Test]
    public async Task ForgettingTheControlPlaneRevokesTheCredentialThere()
    {
        var networkPort = ControlPlaneProcess.FreePort();
        var controlPlane = await StartAsync(networkPort);
        var (code, _) = await OpenPairingAsync(controlPlane);
        var paired = await PairingClient.PairAsync("127.0.0.1", networkPort, code, "Quest 3");

        Assert.That(await PairingClient.RevokeAsync(paired), Is.True);
        var devices = (await LoopbackAsync(controlPlane, HttpMethod.Get, "/api/devices"))!;
        Assert.That((string?)devices["devices"]![0]!["revoked_at"], Is.Not.Null);
        Assert.That(await PairingClient.RevokeAsync(paired), Is.True, "revoking again is already done");
    }

    [Test]
    public async Task APairedDeviceRefusesAControlPlaneWithAnotherIdentity()
    {
        var networkPort = ControlPlaneProcess.FreePort();
        var first = await StartAsync(networkPort);
        var (code, _) = await OpenPairingAsync(first);
        var paired = await PairingClient.PairAsync("127.0.0.1", networkPort, code, "Quest 3");
        first.Dispose();

        // Another control plane, with an identity of its own, where the paired one was.
        await StartAsync(networkPort);
        var target = ControlPlaneTarget.Paired(paired);
        Connect(target);
        await Until(s => (s.Status.Detail ?? "").Contains("certificate other than the one this device paired with"), "the session refuses the certificate");
        Assert.That(session!.Status.IsLive, Is.False);
        Assert.That(connections.SelectMany(connection => connection.Sent), Is.Empty, "nothing was sent to it");
    }
}

/// <summary>
/// A device in the middle of the network: it terminates TLS with a certificate of its own, names
/// the real listener in the Host header, and passes everything else on unchanged both ways.
/// </summary>
internal sealed class TlsRelay : IAsyncDisposable
{
    private readonly TlsTestServer server;
    private int relayed;

    private TlsRelay(int targetPort)
    {
        server = TlsTestServer.Start(inbound => RelayAsync(inbound, targetPort));
    }

    public int Port => server.Port;

    public string CertificateSha256 => server.CertificateSha256;

    /// <summary>Connections passed on to the real listener.</summary>
    public int Relayed => relayed;

    public static TlsRelay Start(int targetPort) => new(targetPort);

    public ValueTask DisposeAsync() => server.DisposeAsync();

    private async Task RelayAsync(Stream inbound, int targetPort)
    {
        var head = await TlsTestServer.ReadHeadAsync(inbound);
        head = head.Replace("Host: 127.0.0.1:" + Port, "Host: 127.0.0.1:" + targetPort, StringComparison.Ordinal);
        using var outbound = await PinnedConnection.OpenAsync("127.0.0.1", targetPort, null, default);
        System.Threading.Interlocked.Increment(ref relayed);
        await outbound.Stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        await Task.WhenAny(inbound.CopyToAsync(outbound.Stream), outbound.Stream.CopyToAsync(inbound));
    }
}

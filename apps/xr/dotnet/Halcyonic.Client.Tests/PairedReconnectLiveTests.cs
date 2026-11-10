using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// A paired headset's session over the pinned path against a real control plane, through what a
/// headset meets on Wi-Fi: the network going silent and coming back, the app paused for a sleep
/// while work goes on, and the Mac's control plane restarting.
/// </summary>
public class PairedReconnectLiveTests
{
    private readonly List<ControlPlaneProcess> processes = new();
    private readonly List<string> directories = new();
    private readonly List<RecordingTransport> connections = new();
    private RealtimeSession? session;

    [TearDown]
    public async Task CleanUp()
    {
        // One instance serves every test of the fixture, so each test's leftovers go with it.
        if (session != null) await session.StopAsync();
        session = null;
        foreach (var process in processes) process.Dispose();
        foreach (var directory in directories) Directory.Delete(directory, recursive: true);
        processes.Clear();
        directories.Clear();
        lock (connections) connections.Clear();
    }

    private string TemporaryDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("halcyonic-reconnect-tests-").FullName;
        directories.Add(directory);
        return directory;
    }

    private async Task<ControlPlaneProcess> StartAsync(string dataDir, int networkPort = 0)
    {
        var process = await ControlPlaneProcess.StartAsync(dataDir, networkPort: networkPort);
        processes.Add(process);
        return process;
    }

    private static async Task LoopbackAsync(ControlPlaneProcess controlPlane, HttpMethod method, string path, string? body = null)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(method, $"http://127.0.0.1:{controlPlane.Port}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", controlPlane.AccessToken);
        if (body != null) request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request);
        Assert.That(response.IsSuccessStatusCode, Is.True, method + " " + path + ": " + await response.Content.ReadAsStringAsync());
    }

    private static async Task<PairedControlPlane> PairAsync(ControlPlaneProcess controlPlane)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{controlPlane.Port}/api/pairing");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", controlPlane.AccessToken);
        using var response = await http.SendAsync(request);
        var code = (string)Json.Parse(await response.Content.ReadAsStringAsync())!["code"]!;
        return await PairingClient.PairAsync("127.0.0.1", controlPlane.NetworkPort!.Value, code, "Quest 3");
    }

    /// <summary>A project made from the Mac, as work going on while the headset is away.</summary>
    private static Task CreateProjectFromTheMacAsync(ControlPlaneProcess controlPlane, string name) =>
        LoopbackAsync(controlPlane, HttpMethod.Post, "/api/commands", HalcyonicJson.Serialize(new CommandFactory(Samples.Client).CreateProject(name)));

    private RealtimeSession Connect(ControlPlaneTarget target)
    {
        var options = target.SessionOptions(Samples.Client);
        options.InitialRetryDelay = TimeSpan.FromMilliseconds(100);
        options.MaxRetryDelay = TimeSpan.FromMilliseconds(500);
        options.PingInterval = TimeSpan.FromMilliseconds(300);
        options.IdleTimeout = TimeSpan.FromSeconds(2);
        options.ConnectTimeout = TimeSpan.FromSeconds(2);
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

    private static bool Holds(RealtimeSession s, string projectName) => s.State.Projects.Values.Any(project => project.Name == projectName);

    private RecordingTransport Latest()
    {
        lock (connections) return connections[^1];
    }

    [Test]
    public async Task ASilentNetworkIsNoticedAndTheSessionComesBackWithWhatHappenedMeanwhile()
    {
        var controlPlane = await StartAsync(TemporaryDirectory());
        var networkPort = controlPlane.NetworkPort!.Value;
        var paired = await PairAsync(controlPlane);
        // The headset reaches the listener through Wi-Fi, here a relay that can go silent as Wi-Fi does.
        await using var wifi = SilentRelay.Start(IPAddress.IPv6Loopback, networkPort, new IPEndPoint(IPAddress.Loopback, networkPort));
        Connect(ControlPlaneTarget.Paired(paired.At("::1", networkPort)));
        await Until(s => s.Status.IsLive, "the session is live through the network");
        int before;
        lock (connections) before = connections.Count;

        wifi.GoSilent();
        await Until(s => !s.Status.IsLive, "the silent network is noticed without TCP telling", seconds: 10);
        await CreateProjectFromTheMacAsync(controlPlane, "Made while away");

        wifi.ComeBack();
        await Until(s => s.Status.IsLive && Holds(s, "Made while away"), "the session is live again and holds what happened meanwhile", seconds: 20);
        Assert.That(connections.Count, Is.GreaterThan(before), "a new connection, not the old one revived");
        Assert.That(Latest().Received.Any(message => message.Contains("\"type\":\"snapshot\"")), Is.True,
            "behind the head, the new connection gets a snapshot");
    }

    [Test]
    public async Task ASleepWithNothingNewResumesWithoutASnapshotAndOneWithWorkDoneCatchesUp()
    {
        var controlPlane = await StartAsync(TemporaryDirectory());
        var paired = await PairAsync(controlPlane);
        Connect(ControlPlaneTarget.Paired(paired));
        await Until(s => s.Status.IsLive, "the session is live over pinned TLS");

        await session!.SetPausedAsync(true);
        await session.SetPausedAsync(false);
        await Until(s => s.Status.IsLive, "awake again with nothing new");
        Assert.That(Latest().Received.First(message => message.Contains("\"type\":\"welcome\"")), Does.Contain("\"resumed\":true"));
        Assert.That(Latest().Received.Any(message => message.Contains("\"type\":\"snapshot\"")), Is.False, "at the head, no snapshot");

        await session.SetPausedAsync(true);
        await CreateProjectFromTheMacAsync(controlPlane, "Made during the sleep");
        await session.SetPausedAsync(false);
        await Until(s => s.Status.IsLive && Holds(s, "Made during the sleep"), "awake again with the work done meanwhile");
    }

    [Test]
    public async Task AControlPlaneRestartedOnTheMacIsFoundAgainWithTheSamePairing()
    {
        var dataDir = TemporaryDirectory();
        var first = await StartAsync(dataDir);
        var networkPort = first.NetworkPort!.Value;
        var paired = await PairAsync(first);
        Connect(ControlPlaneTarget.Paired(paired));
        await Until(s => s.Status.IsLive, "the session is live over pinned TLS");

        first.Kill();
        await Until(s => !s.Status.IsLive, "the control plane gone is noticed");
        var again = await StartAsync(dataDir, networkPort);
        await CreateProjectFromTheMacAsync(again, "After the restart");
        await Until(s => s.Status.IsLive && Holds(s, "After the restart"), "the same pairing reaches the restarted control plane", seconds: 30);
    }
}

/// <summary>
/// The network between a headset and the Mac: passes TCP on unchanged, so the pin still holds, and
/// can go silent, as Wi-Fi does in a sleep or out of range, without closing anything: bytes stop
/// moving both ways and new connections are taken but never passed on.
/// </summary>
internal sealed class SilentRelay : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly IPEndPoint target;
    private readonly CancellationTokenSource stopping = new();
    private readonly List<TcpClient> open = new();
    private readonly Task accepting;
    private volatile TaskCompletionSource flowing = Flowing();

    private SilentRelay(IPAddress address, int port, IPEndPoint target)
    {
        this.target = target;
        listener = new TcpListener(address, port);
        listener.Start();
        accepting = AcceptAsync();
    }

    public static SilentRelay Start(IPAddress address, int port, IPEndPoint target) => new(address, port, target);

    public void GoSilent() => flowing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void ComeBack() => flowing.TrySetResult();

    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        ComeBack();
        listener.Stop();
        lock (open) foreach (var client in open) client.Dispose();
        try
        {
            await accepting;
        }
        catch (Exception)
        {
            // Stopping ends the accept loop by throwing.
        }
    }

    private static TaskCompletionSource Flowing()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        done.SetResult();
        return done;
    }

    private async Task AcceptAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            var inbound = await listener.AcceptTcpClientAsync(stopping.Token);
            lock (open) open.Add(inbound);
            _ = RelayAsync(inbound);
        }
    }

    private async Task RelayAsync(TcpClient inbound)
    {
        try
        {
            await flowing.Task;
            var outbound = new TcpClient(target.AddressFamily);
            lock (open) open.Add(outbound);
            await outbound.ConnectAsync(target.Address, target.Port);
            await Task.WhenAny(
                PumpAsync(inbound.GetStream(), outbound.GetStream()),
                PumpAsync(outbound.GetStream(), inbound.GetStream()));
        }
        catch (Exception)
        {
            // A connection ends however the test leaves it.
        }
        finally
        {
            inbound.Dispose();
        }
    }

    /// <summary>Copies one way, holding every chunk while the network is silent, so nothing closes.</summary>
    private async Task PumpAsync(Stream from, Stream to)
    {
        var buffer = new byte[16 * 1024];
        while (!stopping.IsCancellationRequested)
        {
            var read = await from.ReadAsync(buffer, stopping.Token);
            if (read == 0) return;
            await flowing.Task;
            await to.WriteAsync(buffer.AsMemory(0, read), stopping.Token);
        }
    }
}

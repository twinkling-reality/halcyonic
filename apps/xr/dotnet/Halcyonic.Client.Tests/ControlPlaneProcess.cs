using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Halcyonic.Client.Tests;

/// <summary>
/// A real control plane (`node apps/control-plane/src/main.ts`) on a private port and data directory.
/// </summary>
/// <remarks>
/// Its standard input is a pipe that only this test host holds and never writes to, and it runs
/// with HALCYONIC_EXIT_ON_STDIN_END=1. <see cref="Dispose"/> kills it, but a test host that dies
/// first (killed, crashed, or ended by a runner's timeout) never disposes it: the operating system
/// then closes the pipe, and the control plane shuts itself down instead of running on as an
/// orphan. The pipe's writer belongs to the <see cref="Process"/> this object keeps, so the pipe
/// stays open for this object's lifetime; only <see cref="CloseStandardInput"/> closes it early.
/// </remarks>
internal sealed class ControlPlaneProcess : IDisposable
{
    private readonly Process process;
    private readonly StringBuilder output = new();
    private bool disposed;

    private ControlPlaneProcess(Process process, string dataDir, int port)
    {
        this.process = process;
        DataDir = dataDir;
        Port = port;
        process.OutputDataReceived += (_, line) => Append(line.Data);
        process.ErrorDataReceived += (_, line) => Append(line.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    public string DataDir { get; }

    public int Port { get; }

    public Uri RealtimeEndpoint => new($"ws://127.0.0.1:{Port}/realtime");

    public string AccessToken => File.ReadAllText(Path.Combine(DataDir, "access-token")).Trim();

    public string Output
    {
        get
        {
            lock (output) return output.ToString();
        }
    }

    /// <param name="networkPort">With a port, the network listener for paired devices serves on 127.0.0.1 there.</param>
    public static async Task<ControlPlaneProcess> StartAsync(string dataDir, int port, int? networkPort = null)
    {
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = Repository.Root,
            UseShellExecute = false,
            // Held open and never written to; see the class remarks.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("apps/control-plane/src/main.ts");
        start.Environment["HALCYONIC_DATA_DIR"] = dataDir;
        start.Environment["HALCYONIC_PORT"] = port.ToString(CultureInfo.InvariantCulture);
        start.Environment["HALCYONIC_LOG_LEVEL"] = "warn";
        start.Environment["HALCYONIC_EXIT_ON_STDIN_END"] = "1";
        if (networkPort != null)
        {
            start.Environment["HALCYONIC_NETWORK_HOST"] = "127.0.0.1";
            start.Environment["HALCYONIC_NETWORK_PORT"] = networkPort.Value.ToString(CultureInfo.InvariantCulture);
        }
        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("node did not start.");
        }
        catch (Win32Exception error)
        {
            throw new InvalidOperationException("The control plane tests need Node.js 24 on PATH.", error);
        }
        var controlPlane = new ControlPlaneProcess(process, dataDir, port);
        try
        {
            await controlPlane.WaitUntilHealthyAsync();
        }
        catch
        {
            controlPlane.Dispose();
            throw;
        }
        return controlPlane;
    }

    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Ends the process abruptly, as a crash would.</summary>
    public void Kill()
    {
        if (disposed || process.HasExited) return;
        process.Kill(entireProcessTree: true);
        process.WaitForExit();
    }

    /// <summary>
    /// Closes the control plane's standard input, as the operating system does when the test host
    /// dies, so that the control plane shuts itself down.
    /// </summary>
    public void CloseStandardInput() => process.StandardInput.Close();

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for the process to exit by itself. Returns its exit
    /// code, or null while it still runs.
    /// </summary>
    public int? WaitForExit(TimeSpan timeout)
    {
        if (!process.WaitForExit(timeout)) return null;
        // Also waits for the output handlers, so that Output is complete.
        process.WaitForExit();
        return process.ExitCode;
    }

    public void Dispose()
    {
        Kill();
        disposed = true;
        process.Dispose();
    }

    private async Task WaitUntilHealthyAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            if (process.HasExited) throw new InvalidOperationException("The control plane exited:\n" + Output);
            try
            {
                using var response = await http.GetAsync($"http://127.0.0.1:{Port}/api/health");
                if (response.IsSuccessStatusCode) return;
            }
            catch (Exception error) when (error is HttpRequestException || error is TaskCanceledException)
            {
                // Not listening yet.
            }
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The control plane did not become healthy:\n" + Output);
            await Task.Delay(100);
        }
    }

    private void Append(string? line)
    {
        if (line == null) return;
        lock (output) output.AppendLine(line);
    }
}

/// <summary>Wraps a transport and keeps every message, so tests can inspect exactly what crossed the wire.</summary>
internal sealed class RecordingTransport : IRealtimeTransport
{
    private readonly IRealtimeTransport inner;
    private readonly List<string> received = new();
    private readonly List<string> sent = new();

    public RecordingTransport(IRealtimeTransport inner)
    {
        this.inner = inner;
    }

    public string? CloseDescription => inner.CloseDescription;

    public IReadOnlyList<string> Received
    {
        get
        {
            lock (received) return received.ToArray();
        }
    }

    public IReadOnlyList<string> Sent
    {
        get
        {
            lock (sent) return sent.ToArray();
        }
    }

    public Task ConnectAsync(Uri endpoint, string accessToken, CancellationToken cancellationToken) =>
        inner.ConnectAsync(endpoint, accessToken, cancellationToken);

    public Task SendAsync(string message, CancellationToken cancellationToken)
    {
        lock (sent) sent.Add(message);
        return inner.SendAsync(message, cancellationToken);
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var message = await inner.ReceiveAsync(cancellationToken);
        if (message != null)
        {
            lock (received) received.Add(message);
        }
        return message;
    }

    /// <summary>Drops the connection without a close handshake, as a network failure would.</summary>
    public void Abort() => inner.Dispose();

    public void Dispose() => inner.Dispose();
}

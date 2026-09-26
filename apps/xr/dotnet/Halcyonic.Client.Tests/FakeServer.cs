using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using Newtonsoft.Json.Linq;

namespace Halcyonic.Client.Tests;

/// <summary>The server end of in-memory connections, for testing the session without a network.</summary>
internal sealed class FakeServer
{
    private readonly Channel<FakeConnection> opened = Channel.CreateUnbounded<FakeConnection>();
    private int attempts;

    /// <summary>When set, connection attempts fail with this exception.</summary>
    public Exception? ConnectFailure { get; set; }

    public int Attempts => Volatile.Read(ref attempts);

    public IRealtimeTransport CreateTransport()
    {
        Interlocked.Increment(ref attempts);
        return new FakeConnection(this);
    }

    internal void Opened(FakeConnection connection) => opened.Writer.TryWrite(connection);

    public async Task<FakeConnection> AcceptAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await opened.Reader.ReadAsync(timeout.Token);
    }
}

internal sealed class FakeConnection : IRealtimeTransport
{
    private readonly FakeServer server;
    private readonly Channel<string?> toClient = Channel.CreateUnbounded<string?>();
    private readonly Channel<string> fromClient = Channel.CreateUnbounded<string>();

    public FakeConnection(FakeServer server)
    {
        this.server = server;
    }

    public string? CloseDescription { get; private set; }

    public string? AccessToken { get; private set; }

    public bool Disposed { get; private set; }

    public Task ConnectAsync(Uri endpoint, string accessToken, CancellationToken cancellationToken)
    {
        if (server.ConnectFailure != null) return Task.FromException(server.ConnectFailure);
        AccessToken = accessToken;
        server.Opened(this);
        return Task.CompletedTask;
    }

    public Task SendAsync(string message, CancellationToken cancellationToken)
    {
        if (Disposed) throw new ObjectDisposedException(nameof(FakeConnection));
        fromClient.Writer.TryWrite(message);
        return Task.CompletedTask;
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken) =>
        await toClient.Reader.ReadAsync(cancellationToken);

    public void Dispose()
    {
        Disposed = true;
        toClient.Writer.TryComplete();
    }

    public void Send(ServerMessage message) => toClient.Writer.TryWrite(HalcyonicJson.Serialize(message));

    public void SendRaw(string text) => toClient.Writer.TryWrite(text);

    public void Close(string description)
    {
        CloseDescription = description;
        toClient.Writer.TryWrite(null);
    }

    /// <summary>The next message the client sent, skipping pings unless asked for them.</summary>
    public async Task<JObject> ReceiveFromClientAsync(bool includePings = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var message = (JObject)Json.Parse(await fromClient.Reader.ReadAsync(timeout.Token));
            if (includePings || (string?)message["type"] != "ping") return message;
        }
    }

    /// <summary>Completes the handshake the way the control plane does for a fresh client.</summary>
    public async Task<JObject> WelcomeWithSnapshotAsync(Snapshot snapshot)
    {
        var hello = await ReceiveFromClientAsync();
        Send(Samples.Welcome(resumed: false, head: snapshot.Position, journalId: snapshot.Journal.JournalId));
        Send(new SnapshotMessage { Snapshot = snapshot });
        return hello;
    }
}

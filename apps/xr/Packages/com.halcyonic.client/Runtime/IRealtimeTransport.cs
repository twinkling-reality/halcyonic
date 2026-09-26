#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Halcyonic.Client
{
    /// <summary>
    /// A text message connection to the control plane's realtime endpoint. The session creates one
    /// per connection attempt and disposes it when the attempt ends. Implementations must allow a
    /// receive to run while sends happen, and must serialize concurrent sends.
    /// </summary>
    public interface IRealtimeTransport : IDisposable
    {
        /// <summary>Why the server closed the connection, once <see cref="ReceiveAsync"/> has returned null.</summary>
        string? CloseDescription { get; }

        /// <summary>Opens the connection, sending the bearer token on the upgrade request.</summary>
        Task ConnectAsync(Uri endpoint, string accessToken, CancellationToken cancellationToken);

        Task SendAsync(string message, CancellationToken cancellationToken);

        /// <summary>The next complete text message, or null once the server has closed the connection.</summary>
        Task<string?> ReceiveAsync(CancellationToken cancellationToken);
    }
}

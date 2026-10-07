#nullable enable
using System;

namespace Halcyonic.Client
{
    public enum ConnectionPhase
    {
        /// <summary>Not started, or stopped by the application.</summary>
        Stopped,

        Connecting,

        /// <summary>Connected and waiting for a snapshot; the local state may be out of date.</summary>
        Synchronizing,

        /// <summary>Connected and current; events arrive as the control plane journals them.</summary>
        Live,

        /// <summary>The last connection failed or ended; the next attempt starts after a delay.</summary>
        WaitingToRetry,

        /// <summary>The control plane will not serve this client, for example an unsupported protocol version.</summary>
        Refused,
    }

    public sealed class ConnectionStatus
    {
        public static readonly ConnectionStatus Stopped = new ConnectionStatus(ConnectionPhase.Stopped);

        public ConnectionStatus(ConnectionPhase phase, string? detail = null, TimeSpan? retryIn = null, bool accessRefused = false, bool answered = false, string? diagnostic = null)
        {
            Diagnostic = diagnostic;
            Phase = phase;
            Detail = detail;
            RetryIn = retryIn;
            AccessRefused = accessRefused;
            Answered = answered;
        }

        public ConnectionPhase Phase { get; }

        /// <summary>Why the last connection ended or was refused, for display.</summary>
        public string? Detail { get; }

        /// <summary>
        /// The control plane answered and refused this device's credential (HTTP 401): it is reachable,
        /// and trying again with the same credential cannot help. <see cref="Detail"/> then says, in
        /// plain words, what to do next.
        /// </summary>
        public bool AccessRefused { get; }

        /// <summary>
        /// Your computer answered before the last connection ended, as by turning it away or closing it, so
        /// <see cref="Detail"/> says why on its own, never after "Can't reach your computer".
        /// </summary>
        public bool Answered { get; }

        /// <summary>
        /// What went wrong as the device log needs it: an exception's own message, or a refused upgrade's
        /// status and its code by its shape. Never drawn: it can hold an address or the control plane's
        /// terms (the review's L4).
        /// </summary>
        public string? Diagnostic { get; }

        /// <summary>The device log's line: the phase, then the diagnostic where there is one, else the detail.</summary>
        public string ForLog => (Diagnostic ?? Detail) is string why ? Phase + ": " + why : Phase.ToString();

        /// <summary>How long until the next attempt, while waiting to retry.</summary>
        public TimeSpan? RetryIn { get; }

        /// <summary>Only a live session's state is current; anything else shows the last known state.</summary>
        public bool IsLive => Phase == ConnectionPhase.Live;

        public override string ToString() => Detail == null ? Phase.ToString() : Phase + ": " + Detail;
    }
}

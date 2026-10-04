#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>
    /// What the person is told when this device is not connected to its Mac, in plain words, with what
    /// to do next. A refused credential and an unreachable Mac read differently, because they are fixed
    /// differently: the first needs a new credential, the second only the Mac and the connection.
    /// </summary>
    public static class ConnectionText
    {
        /// <summary>The credential was refused, when how this device reaches the Mac is not known.</summary>
        public const string AccessRefused = HostText.YourStart + " refused this headset's credential.";

        /// <summary>
        /// A development build that reaches the Mac with the access token, as over USB; the person
        /// reads "access code" (WORDS.md).
        /// </summary>
        public const string AccessTokenRefused =
            HostText.YourStart + " refused this headset's access code: it doesn't match " + HostText.Your + "'s. "
            + "Put " + HostText.Your + "'s current access code on the headset, then restart the app.";

        /// <summary>
        /// A development build whose access token the Mac did not prove it holds (<see cref="LoopbackProof"/>):
        /// the Mac's token is another, or something else answers in its place, so the token was not sent.
        /// </summary>
        public const string AccessTokenUnproved =
            "This headset's access code doesn't match " + HostText.Your + "'s, or something else is answering in its place, so the headset didn't send it. "
            + "Put " + HostText.Your + "'s current access code on the headset, check that Halcyonic is running there, and restart the app.";

        /// <summary>A paired headset whose pairing the Mac no longer accepts, as after it was revoked.</summary>
        public const string PairingRefused = HostText.YourStart + " no longer accepts this headset's pairing. Forget the " + HostText.Noun + " on the headset and pair again.";

        /// <summary>The connection's phase in plain words, never the phase's own name.</summary>
        public static string Phase(ConnectionStatus status) => status.Phase switch
        {
            ConnectionPhase.Live => "Live",
            ConnectionPhase.Connecting => "Connecting to " + HostText.Your + "…",
            ConnectionPhase.Synchronizing => "Catching up with " + HostText.Your + "…",
            ConnectionPhase.WaitingToRetry => Unreachable,
            ConnectionPhase.Refused => WhyNotLive(status),
            _ => "Not connected",
        };

        /// <summary>
        /// Why the connection ended, by the code your computer gave when it turned the connection away,
        /// never its message, which is written for developers in the control plane's terms.
        /// </summary>
        public static string Ended(string? code) => code switch
        {
            "unsupported_protocol" => OtherVersion,
            "device_revoked" => PairingRefused,
            "too_many_connections" => "This headset already has too many connections open to " + HostText.Your + ". Close the app, then open it again.",
            "invalid_message" or "hello_required" => HostText.YourStart + " couldn't read what this app sent. " + SameVersion,
            _ => HostText.YourStart + " ended the connection.",
        };

        /// <summary>Your computer speaks another version of the connection than this app.</summary>
        public const string OtherVersion = HostText.YourStart + " runs another version of this app. " + SameVersion;

        private const string SameVersion = "Install the same version on both.";

        /// <summary>Your computer sent what this app can't read; the message itself is never shown, since it may hold anything.</summary>
        public const string Unreadable = HostText.YourStart + " sent something this app can't read. " + SameVersion;

        /// <summary>Your computer closed the connection.</summary>
        public const string Closed = HostText.YourStart + " closed the connection.";

        /// <summary>
        /// Why your computer closed the connection, from the close it sent ("1008 device revoked", as the
        /// control plane closes a revoked device's connection): a revoked pairing in the pairing's own words,
        /// any other close as <see cref="Closed"/>, never the close's text.
        /// </summary>
        public static string ClosedWith(string? close) =>
            close != null && close.EndsWith(" device revoked", StringComparison.Ordinal) ? PairingRefused : Closed;

        /// <summary>This headset fell behind what your computer sent, so it reads everything again.</summary>
        public const string FellBehind = "This headset fell behind, so it's catching up.";

        /// <summary>Your computer didn't answer the connection in time.</summary>
        public static string NoAnswer(TimeSpan within) => HostText.YourStart + " didn't answer within " + Seconds(within) + ".";

        /// <summary>Your computer sent nothing on a live connection for this long.</summary>
        public static string Silent(TimeSpan quiet) => HostText.YourStart + " sent nothing for " + Seconds(quiet) + ".";

        private static string Seconds(TimeSpan span)
        {
            var seconds = Math.Max(1, (int)Math.Round(span.TotalSeconds));
            return seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + (seconds == 1 ? " second" : " seconds");
        }

        public const string Unreachable = "Can't reach " + HostText.Your + "; trying again. Check that Halcyonic is running there and this headset can reach it.";

        /// <summary>
        /// Why the control plane is not shown: refused, with the next step; refused for another reason,
        /// such as a protocol this app does not speak; or not reachable, with the technical detail after.
        /// </summary>
        public static string WhyNotLive(ConnectionStatus? status)
        {
            var detail = status?.Detail;
            if (status != null && status.Phase == ConnectionPhase.Refused)
            {
                if (status.AccessRefused) return detail ?? AccessRefused;
                return HostText.YourStart + " refused this app." + (detail == null ? "" : " " + detail);
            }
            return Unreachable + (detail == null ? "" : " (" + detail + ")");
        }
    }
}

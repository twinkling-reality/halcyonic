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
        public const string AccessRefused = "Credential Refused";

        /// <summary>
        /// A development build that reaches the Mac with the access token, as over USB; the person
        /// reads "access code" (WORDS.md).
        /// </summary>
        public const string AccessTokenRefused =
            "Access Code Refused · Mismatch · Copy Current Code · Restart App";

        /// <summary>
        /// A development build whose access token the Mac did not prove it holds (<see cref="LoopbackProof"/>):
        /// the Mac's token is another, or something else answers in its place, so the token was not sent.
        /// </summary>
        public const string AccessTokenUnproved =
            "Code Withheld · Host Unverified · Copy Current Code · Check App · Restart App";

        /// <summary>A paired headset whose pairing the Mac no longer accepts, as after it was revoked.</summary>
        public const string PairingRefused = "Pairing Revoked · Forget Computer · Pair Again";

        /// <summary>The connection's phase in plain words, never the phase's own name.</summary>
        public static string Phase(ConnectionStatus status) => status.Phase switch
        {
            ConnectionPhase.Live => "Live",
            ConnectionPhase.Connecting => "Connecting to Computer",
            ConnectionPhase.Synchronizing => "Catching Up",
            ConnectionPhase.WaitingToRetry => Unreachable,
            ConnectionPhase.Refused => WhyNotLive(status),
            _ => "Not Connected",
        };

        /// <summary>
        /// Why the connection ended, by the code your computer gave when it turned the connection away,
        /// never its message, which is written for developers in the control plane's terms.
        /// </summary>
        public static string Ended(string? code) => code switch
        {
            "unsupported_protocol" => OtherVersion,
            "device_revoked" => PairingRefused,
            "too_many_connections" => "Too Many Connections · Restart App",
            "invalid_message" or "hello_required" => "Message Unreadable · " + SameVersion,
            _ => "Connection Ended · " + TriesAgain,
        };

        /// <summary>
        /// Why your computer refused to open the connection, other than a refused credential, by the code it
        /// gave: never the refusal's message, which is the control plane's own and can name a header, a
        /// server or a credential.
        /// </summary>
        /// <param name="turnedAway">Any other refusal's words, with the way on for how this headset reaches the computer.</param>
        public static string UpgradeRefused(string? code, string turnedAway) => code switch
        {
            // Only the network listener refuses a Host name, and a stored pairing passed it once, so the
            // address may have changed there (settled by the coordinator, 2026-10-07).
            "host_not_allowed" => "Host Not Allowed · " + TriesAgain + " · Pair Again if Repeated",
            "too_many_requests" => "Computer Busy · Too Many Tries · " + TriesAgain,
            _ => turnedAway,
        };

        private const string TriesAgain = "Retrying";

        /// <summary>
        /// Something answered with a certificate other than the one pinned when this headset paired, which
        /// can mean something is answering in your computer's place (settled by the coordinator, 2026-10-07).
        /// </summary>
        public const string NotThePairedComputer = "Unknown Host · Not Your Computer · Nothing Sent";

        /// <summary>A code for the device log, kept only by its shape, so what answered can't write a line of its own.</summary>
        public static string CodeForLog(string? code) =>
            code != null && System.Text.RegularExpressions.Regex.IsMatch(code, "^[a-z][a-z0-9_]{0,63}\\z") ? code : "no code";

        /// <summary>
        /// The stage's line while the headset waits to try again: "Last known: can't reach your computer"
        /// only where nothing answered, else why your computer turned it away, closed it or ended it, alone.
        /// </summary>
        public static string Retrying(ConnectionStatus status) => status.Answered && status.Detail != null
            ? status.Detail
            : "Last Known · Computer Unreachable · Retrying" + (string.IsNullOrEmpty(status.Detail) ? "" : " · " + status.Detail);

        /// <summary>A connection turned away for no reason a code names, where how this headset reaches the computer is not known.</summary>
        public const string TurnedAway = "Connection Refused · " + TriesAgain;

        /// <summary>The same, for a paired headset (settled by the coordinator, 2026-10-07).</summary>
        public const string TurnedAwayPaired = "Connection Refused · " + TriesAgain + " · Pair Again if Repeated";

        /// <summary>The same, over USB with the access code.</summary>
        public const string TurnedAwayUsb = "Connection Refused · " + TriesAgain + " · Restart Computer App if Repeated";

        /// <summary>Your computer speaks another version of the connection than this app.</summary>
        public const string OtherVersion = "Version Mismatch · " + SameVersion;

        private const string SameVersion = "Install Same Version";

        /// <summary>Your computer sent what this app can't read; the message itself is never shown, since it may hold anything.</summary>
        public const string Unreadable = "Message Unreadable · " + SameVersion;

        /// <summary>Your computer closed the connection.</summary>
        public const string Closed = "Connection Closed · " + TriesAgain;

        /// <summary>
        /// Why your computer closed the connection, from the close it sent ("1008 device revoked", as the
        /// control plane closes a revoked device's connection): a revoked pairing in the pairing's own words,
        /// any other close as <see cref="Closed"/>, never the close's text.
        /// </summary>
        public static string ClosedWith(string? close) =>
            close != null && close.EndsWith(" device revoked", StringComparison.Ordinal) ? PairingRefused : Closed;

        /// <summary>This headset fell behind what your computer sent, so it reads everything again.</summary>
        public const string FellBehind = "Catching Up";

        /// <summary>Your computer didn't answer the connection in time.</summary>
        public static string NoAnswer(TimeSpan within) => "No Answer in " + Seconds(within);

        /// <summary>
        /// Your computer sent nothing on a live connection for this long. It answered before going quiet, so
        /// the line stands alone, with its way on (settled by the coordinator, 2026-10-07).
        /// </summary>
        public static string Silent(TimeSpan quiet) => "Computer Silent · " + Seconds(quiet) + " · " + TriesAgain;

        private static string Seconds(TimeSpan span)
        {
            var seconds = Math.Max(1, (int)Math.Round(span.TotalSeconds));
            return seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " s";
        }

        public const string Unreachable = "Computer Unreachable · Retrying · Check App and Network";

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
                return "App Refused" + (detail == null ? "" : " · " + detail);
            }
            // Your computer answered and turned it away, closed it or ended it: that is why on its own, since
            // "Can't reach" would be untrue and a second way on (the review's L3).
            if (status != null && status.Answered && detail != null) return detail;
            return Unreachable + (detail == null ? "" : " · " + detail);
        }
    }
}

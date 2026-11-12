#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>
    /// What the person is told when this device is not connected to its Mac, in plain words, with what
    /// to do next. A refused credential and an unreachable Mac read differently, because they are fixed
    /// differently: the first needs a new credential, the second only the Mac and the connection. Each is a title and its line
    /// as one phrase (FIELD's words, 2026-11-12; <see cref="Notice.Phrase"/>), never facts joined on a dot.
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
            "Access Code Refused, copy the current code, then restart the app";

        /// <summary>
        /// A development build whose access token the Mac did not prove it holds (<see cref="LoopbackProof"/>):
        /// the Mac's token is another, or something else answers in its place, so the token was not sent.
        /// </summary>
        public const string AccessTokenUnproved =
            "Code Withheld, the computer wasn't verified; copy the current code, then restart the app";

        /// <summary>A paired headset whose pairing the Mac no longer accepts, as after it was revoked.</summary>
        public const string PairingRefused = "Pairing Revoked, forget this computer, then pair again";

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
            "too_many_connections" => "Too Many Connections, restart the app to connect again",
            "invalid_message" or "hello_required" => Unreadable,
            _ => "Connection Ended, " + TriesAgain,
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
            "host_not_allowed" => "Address Not Allowed, " + TriesAgain + "; pair again if this repeats",
            "too_many_requests" => "Computer Busy, too many tries, so trying again in a minute",
            _ => turnedAway,
        };

        private const string TriesAgain = "trying again";

        /// <summary>
        /// Something answered with a certificate other than the one pinned when this headset paired, which
        /// can mean something is answering in your computer's place (settled by the coordinator, 2026-10-07).
        /// </summary>
        public const string NotThePairedComputer = "Not Your Computer, something else answered, so nothing was sent";

        /// <summary>A code for the device log, kept only by its shape, so what answered can't write a line of its own.</summary>
        public static string CodeForLog(string? code) =>
            code != null && System.Text.RegularExpressions.Regex.IsMatch(code, "^[a-z][a-z0-9_]{0,63}\\z") ? code : "no code";

        /// <summary>
        /// The stage's line while the headset waits to try again: "Last known: can't reach your computer"
        /// only where nothing answered, else why your computer turned it away, closed it or ended it, alone.
        /// </summary>
        public static string Retrying(ConnectionStatus status) => status.Answered && status.Detail != null
            ? status.Detail
            : "Computer Unreachable, " + TriesAgain + (string.IsNullOrEmpty(status.Detail) ? "" : ". " + status.Detail);

        /// <summary>A connection turned away for no reason a code names, where how this headset reaches the computer is not known.</summary>
        public const string TurnedAway = "Connection Refused, " + TriesAgain;

        /// <summary>The same, for a paired headset (settled by the coordinator, 2026-10-07).</summary>
        public const string TurnedAwayPaired = "Connection Refused, " + TriesAgain + "; pair again if this repeats";

        /// <summary>The same, over USB with the access code.</summary>
        public const string TurnedAwayUsb = "Connection Refused, " + TriesAgain + "; restart the app on the computer if this repeats";

        /// <summary>Your computer speaks another version of the connection than this app.</summary>
        public const string OtherVersion = "Different Versions, install the same version on the headset and the computer";

        /// <summary>Your computer sent what this app can't read; the message itself is never shown, since it may hold anything.</summary>
        public const string Unreadable = "Message Unreadable, the headset and the computer may run different versions";

        /// <summary>Your computer closed the connection.</summary>
        public const string Closed = "Connection Closed, " + TriesAgain;

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
        public static string NoAnswer(TimeSpan within) => "No Answer, nothing in " + Seconds(within);

        /// <summary>
        /// Your computer sent nothing on a live connection for this long. It answered before going quiet, so
        /// the line stands alone, with its way on (settled by the coordinator, 2026-10-07).
        /// </summary>
        public static string Silent(TimeSpan quiet) => "Computer Silent, nothing for " + Seconds(quiet) + ", so " + TriesAgain;

        private static string Seconds(TimeSpan span)
        {
            var seconds = Math.Max(1, (int)Math.Round(span.TotalSeconds));
            return seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " s";
        }

        public const string Unreachable = "Computer Unreachable, " + TriesAgain + "; check the app and the network";

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
                return detail ?? "App Refused";
            }
            // Your computer answered and turned it away, closed it or ended it: that is why on its own, since
            // "Can't reach" would be untrue and a second way on (the review's L3).
            if (status != null && status.Answered && detail != null) return detail;
            return detail == null ? Unreachable : Unreachable + ". " + detail;
        }
    }
}

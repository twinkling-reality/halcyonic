#nullable enable

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

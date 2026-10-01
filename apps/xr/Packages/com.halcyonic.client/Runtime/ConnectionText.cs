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
        public const string AccessRefused = "Your Mac refused this headset's credential.";

        /// <summary>A development build that reaches the Mac with the access token, as over USB.</summary>
        public const string AccessTokenRefused =
            "Your Mac refused this headset's access token: it doesn't match the Mac's. "
            + "Put the Mac's current access token on the headset, then restart the app.";

        /// <summary>A paired headset whose pairing the Mac no longer accepts, as after it was revoked.</summary>
        public const string PairingRefused = "Your Mac no longer accepts this headset's pairing. Forget the Mac on the headset and pair again.";

        /// <summary>The connection's phase in plain words, never the phase's own name.</summary>
        public static string Phase(ConnectionStatus status) => status.Phase switch
        {
            ConnectionPhase.Live => "Live",
            ConnectionPhase.Connecting => "Connecting to your Mac…",
            ConnectionPhase.Synchronizing => "Catching up with your Mac…",
            ConnectionPhase.WaitingToRetry => Unreachable,
            ConnectionPhase.Refused => WhyNotLive(status),
            _ => "Not connected",
        };

        public const string Unreachable = "Can't reach your Mac; trying again. Check that the control plane is running and this headset can reach it.";

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
                return "Your Mac refused this app." + (detail == null ? "" : " " + detail);
            }
            return Unreachable + (detail == null ? "" : " (" + detail + ")");
        }
    }
}

#nullable enable
using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using Halcyonic.XR.Workspace;
using UnityEngine;

namespace Halcyonic.XR.Pairing
{
    /// <summary>
    /// Pairs this headset with a control plane over the network (ADR 0017): one button and a line in
    /// the Your computer section of the Settings sheet (ADR 0023). "Pair with a computer" asks on the system
    /// keyboard for the address and the eight-digit code <c>pnpm pair</c> shows on the Mac, pairs in
    /// the background, keeps the pairing through <see cref="ControlPlaneSettings.PairingStore"/>, and
    /// connects again. Once paired, the same button forgets the Mac, after a second, deliberate
    /// press, and asks the Mac to revoke this headset's credential. What it says also shows on the
    /// stage's banner as a short notice, so a result reached while the sheet is closed is not missed.
    /// </summary>
    /// <remarks>
    /// Moving into Settings changed only where the button and line show and how they look; the
    /// pairing itself, the code entry, the confirmation and every message are as they were. The
    /// button ignores input while <see cref="FocusGuard.InputSuspended"/> or the sheet is closed. The
    /// keyboard's answer counts anyway, since focus returns only after the keyboard closes. The code
    /// is passed to the pairing and kept nowhere, and neither it nor the credential is logged.
    /// </remarks>
    internal sealed class PairingPanel : MonoBehaviour
    {
        /// <summary>How long the line shows after it changes, once nothing is in progress.</summary>
        private const float LineSeconds = 10f;

        /// <summary>How long the second press that forgets the Mac is waited for.</summary>
        private const float ConfirmSeconds = 6f;

        /// <summary>The network listener's port when the typed address names none.</summary>
        private const int DefaultPort = 47801;

        /// <summary>The address typed last, so pairing again starts from it. Not a secret.</summary>
        private const string AddressPreference = "halcyonic.pairing.address";

        private enum Step
        {
            Idle,
            Address,
            Code,
            Pairing,
            Forgetting,
        }

        private ControlPlaneConnection connection = null!;
        private CharacterStage? stage;
        private SettingsSection section = null!;
        private GlazeButton button = null!;
        private Step step;
        private TouchScreenKeyboard? keyboard;
        private string host = "";
        private int port;
        private Task<Outcome>? pairing;
        private Task<bool>? forgetting;
        private PairedControlPlane? paired;
        private string shownLine = "";
        private float lineUntil;
        private float confirmUntil;
        private bool lineShown;

        private void Awake()
        {
            connection = GetComponent<ControlPlaneConnection>();
            stage = GetComponent<CharacterStage>();
            paired = ControlPlaneSettings.ReadPairing();
            section = SettingsSheet.On(gameObject).Section(SettingsText.YourMac, 1);
            button = section.Button("Pairing", ButtonRole.Secondary);
            button.Pressed += OnPressed;
            Layout();
        }

        private void OnEnable() => FocusGuard.Left += OnFocusLeft;

        private void OnDisable() => FocusGuard.Left -= OnFocusLeft;

        /// <summary>A Forget half confirmed when focus went to another window is asked again once back.</summary>
        private void OnFocusLeft()
        {
            if (confirmUntil <= 0f) return;
            confirmUntil = 0f;
            Layout();
        }

        private void Update()
        {
            PollKeyboard();
            PollWork();
            var now = Time.unscaledTime;
            if (confirmUntil > 0f && now >= confirmUntil)
            {
                confirmUntil = 0f;
                Layout();
            }
            if (lineShown != LineShown(now)) Layout();
        }

        private void OnPressed()
        {
            if (step != Step.Idle) return;
            if (paired == null)
            {
                BeginPairing();
                return;
            }
            if (confirmUntil <= 0f)
            {
                confirmUntil = Time.unscaledTime + ConfirmSeconds;
                Say("Forget the " + HostText.Noun + " at " + paired.Address + "? This headset then needs pairing again to reach it.");
                return;
            }
            confirmUntil = 0f;
            step = Step.Forgetting;
            var forgotten = paired;
            forgetting = Task.Run(() => PairingClient.RevokeAsync(forgotten));
            Say("Forgetting the " + HostText.Noun + "…");
        }

        private void BeginPairing()
        {
            if (!TouchScreenKeyboard.isSupported)
            {
                Say("Pairing needs the headset's system keyboard; pair from the headset.");
                return;
            }
            step = Step.Address;
            keyboard = FocusGuard.Track(TouchScreenKeyboard.Open(PlayerPrefs.GetString(AddressPreference, ""), TouchScreenKeyboardType.URL,
                false, false, false, false, HostText.YourStart + "'s address, as pnpm pair shows it"));
            Say("Type " + HostText.Your + "'s address, as pnpm pair shows it, such as 192.168.1.23:47801.");
        }

        /// <summary>Reads the keyboard once it closes: first the address, then the code.</summary>
        private void PollKeyboard()
        {
            var open = keyboard;
            if (open == null || open.status == TouchScreenKeyboard.Status.Visible) return;
            keyboard = null;
            if (open.status != TouchScreenKeyboard.Status.Done)
            {
                step = Step.Idle;
                Say("Pairing canceled; nothing changed.");
                return;
            }
            var typed = open.text ?? "";
            if (step == Step.Address)
            {
                if (!TryParseAddress(typed, out host, out port))
                {
                    step = Step.Idle;
                    Say("That is not an address like 192.168.1.23:47801. Pairing canceled.");
                    return;
                }
                PlayerPrefs.SetString(AddressPreference, typed.Trim());
                PlayerPrefs.Save();
                step = Step.Code;
                keyboard = FocusGuard.Track(TouchScreenKeyboard.Open("", TouchScreenKeyboardType.NumberPad, false, false, false, false,
                    "The eight-digit code pnpm pair shows"));
                Say("Type the eight-digit code pnpm pair shows on " + HostText.Your + ".");
                return;
            }
            if (step != Step.Code) return;
            step = Step.Pairing;
            var (toHost, toPort, label) = (host, port, DeviceLabel());
            pairing = Task.Run(() => PairAsync(toHost, toPort, typed, label));
            Say("Pairing with " + Address(host, port) + "…");
            Log("pairing with the control plane at " + Address(host, port));
        }

        /// <summary>Applies what the background work found, on the main thread.</summary>
        private void PollWork()
        {
            if (pairing != null && pairing.IsCompleted)
            {
                var outcome = pairing.Result;
                pairing = null;
                step = Step.Idle;
                if (outcome.Paired == null)
                {
                    Say(outcome.Message);
                    Log("pairing refused: " + outcome.Code + (outcome.AttemptsLeft == null ? "" : ", " + outcome.AttemptsLeft + " attempts left"));
                    return;
                }
                ControlPlaneSettings.PairingStore.Save(outcome.Paired);
                paired = outcome.Paired;
                Say("Paired with " + HostText.Your + " at " + paired.Address + ". Connecting over Wi-Fi.");
                Log("paired; connecting over the network");
                Reconnect();
            }
            if (forgetting != null && forgetting.IsCompleted)
            {
                var revoked = !forgetting.IsFaulted && forgetting.Result;
                forgetting = null;
                step = Step.Idle;
                ControlPlaneSettings.PairingStore.Forget();
                paired = null;
                Say(revoked
                    ? "Forgot the " + HostText.Noun + ", which no longer accepts this headset."
                    : "Forgot the " + HostText.Noun + " on this headset. It could not be reached, so revoke this headset there with pnpm devices.");
                Log(revoked ? "forgot the control plane, which revoked this headset" : "forgot the control plane, which could not be reached to revoke this headset");
                Reconnect();
            }
        }

        /// <summary>Connects again with the settings as they now are, as at startup.</summary>
        private void Reconnect()
        {
            connection.enabled = false;
            connection.enabled = true;
        }

        private static async Task<Outcome> PairAsync(string host, int port, string code, string label)
        {
            try
            {
                return new Outcome(await PairingClient.PairAsync(host, port, code, label), "paired", "", null);
            }
            catch (PairingException error)
            {
                var left = error.AttemptsLeft is long attempts && attempts > 0 ? " " + attempts + (attempts == 1 ? " attempt" : " attempts") + " left." : "";
                return new Outcome(null, error.Code, error.Message + left, error.AttemptsLeft);
            }
            catch (Exception error)
            {
                return new Outcome(null, "error", "Pairing failed: " + error.Message, null);
            }
        }

        private void Say(string text)
        {
            shownLine = text;
            lineUntil = Time.unscaledTime + LineSeconds;
            // The banner says it too, for a result reached with the sheet closed.
            if (stage != null) stage.ShowNotice(text);
            Layout();
        }

        private bool LineShown(float now) => shownLine.Length > 0 && (now < lineUntil || step != Step.Idle);

        /// <summary>
        /// The section's button, Forget and its confirmation outlined in red, and, while it shows, the
        /// line above it. A refusal can carry the words of whatever answered at the typed address, and
        /// a failure an exception's: the section shows them by the one rule for text Halcyonic did not write.
        /// </summary>
        private void Layout()
        {
            if (step == Step.Idle)
            {
                var confirming = paired != null && confirmUntil > 0f;
                var text = paired == null ? "Pair with a " + HostText.Noun : confirming ? "Yes, forget this " + HostText.Noun : "Forget this " + HostText.Noun;
                button.Role = paired == null ? ButtonRole.Secondary : ButtonRole.Destructive;
                section.Offer(button, text);
            }
            else
            {
                section.Offer(button, null);
            }
            lineShown = LineShown(Time.unscaledTime);
            section.Say(lineShown ? shownLine : "");
        }

        /// <summary>
        /// An address as <c>pnpm pair</c> prints it: <c>192.168.1.23:47801</c>, a name such as
        /// <c>my-mac.local:47801</c>, or <c>[fe80::1]:47801</c>; the port may be left out.
        /// </summary>
        internal static bool TryParseAddress(string typed, out string host, out int port)
        {
            var text = typed.Trim();
            host = text;
            port = DefaultPort;
            string? portText = null;
            if (text.StartsWith("[", StringComparison.Ordinal))
            {
                var close = text.IndexOf(']');
                if (close < 0) return false;
                host = text.Substring(1, close - 1);
                var rest = text.Substring(close + 1);
                if (rest.Length > 0)
                {
                    if (!rest.StartsWith(":", StringComparison.Ordinal)) return false;
                    portText = rest.Substring(1);
                }
            }
            else if (text.IndexOf(':') == text.LastIndexOf(':') && text.IndexOf(':') >= 0)
            {
                host = text.Substring(0, text.IndexOf(':'));
                portText = text.Substring(text.IndexOf(':') + 1);
            }
            if (portText != null && (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535))
            {
                return false;
            }
            if (host.Length == 0 || host.Length > 253) return false;
            foreach (var character in host)
            {
                if (!(char.IsLetterOrDigit(character) || character == '.' || character == '-' || character == ':' || character == '%')) return false;
            }
            return true;
        }

        private static string Address(string host, int port) =>
            (host.Contains(":") ? "[" + host + "]" : host) + ":" + port.ToString(CultureInfo.InvariantCulture);

        /// <summary>What the Mac lists this headset as: its model, without control characters.</summary>
        private static string DeviceLabel()
        {
            var label = new StringBuilder();
            foreach (var character in SystemInfo.deviceModel ?? "")
            {
                if (character >= ' ' && character != '\u007f') label.Append(character);
            }
            var text = label.ToString().Trim();
            if (text.Length > 120) text = text.Substring(0, 120).Trim();
            return text.Length == 0 ? "Headset" : text;
        }

        private void Log(string message) =>
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this, "Halcyonic: {0}", message);

        private sealed class Outcome
        {
            public Outcome(PairedControlPlane? paired, string code, string message, long? attemptsLeft)
            {
                Paired = paired;
                Code = code;
                Message = message;
                AttemptsLeft = attemptsLeft;
            }

            public PairedControlPlane? Paired { get; }

            public string Code { get; }

            public string Message { get; }

            public long? AttemptsLeft { get; }
        }
    }
}

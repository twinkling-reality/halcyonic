#nullable enable
using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.XR.Workspace;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Pairing
{
    /// <summary>
    /// Pairs this headset with a control plane over the network (ADR 0017): one small button, low and
    /// to the person's left, and a line above it. "Pair with a Mac" asks on the system keyboard for
    /// the address and the eight-digit code <c>pnpm pair</c> shows on the Mac, pairs in the
    /// background, keeps the pairing through <see cref="ControlPlaneSettings.PairingStore"/>, and
    /// connects again. Once paired, the same button forgets the Mac, after a second, deliberate
    /// press, and asks the Mac to revoke this headset's credential.
    /// </summary>
    /// <remarks>
    /// The button is the workspace's <see cref="PanelButton"/>, pointed at and pinched or poked, which
    /// ignores input while <see cref="FocusGuard.InputSuspended"/>. The keyboard's answer counts
    /// anyway, since focus returns only after the keyboard closes. The code is passed to the pairing
    /// and kept nowhere, and neither it nor the credential is logged.
    /// </remarks>
    internal sealed class PairingPanel : MonoBehaviour
    {
        /// <summary>Where the controls rest, relative to where the person faces: to the left, near, and low.</summary>
        private const float RestTurnDegrees = -26f;
        private const float RestReach = 0.40f;
        private const float RestBelowEyes = 0.40f;

        /// <summary>Where they come up while pairing: ahead, a little below the eyes.</summary>
        private const float PresentReach = 0.50f;
        private const float PresentBelowEyes = 0.10f;

        /// <summary>How long the line shows after it changes, once nothing is in progress.</summary>
        private const float LineSeconds = 10f;

        /// <summary>How long the second press that forgets the Mac is waited for.</summary>
        private const float ConfirmSeconds = 6f;

        /// <summary>Out of the comfortable zone for this long, the controls return to their resting place.</summary>
        private const float AwaySeconds = 1.5f;
        private const float AwayDegrees = 50f;

        private const float Gap = 0.02f;
        private const float LineWidth = 0.56f;
        private const float LinePadding = 0.02f;
        private const float MinButtonWidth = 0.24f;

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
        private Transform root = null!;
        private PanelButton button = null!;
        private Transform lineRoot = null!;
        private SpriteRenderer linePlate = null!;
        private TextMeshPro line = null!;
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
        private bool placed;
        private bool presenting;
        private float awayFor;

        private void Awake()
        {
            connection = GetComponent<ControlPlaneConnection>();
            paired = ControlPlaneSettings.ReadPairing();
            root = new GameObject("Pairing controls").transform;
            lineRoot = new GameObject("Line").transform;
            lineRoot.SetParent(root, false);
            linePlate = PairingVisuals.Plate(lineRoot, "Plate");
            line = PairingVisuals.Text(lineRoot, "Text", new Vector2(LineWidth, 0.2f));
            button = PanelButton.Create(root, "Pairing");
            button.Pressed += OnPressed;
            Layout();
        }

        private void OnDestroy()
        {
            if (root != null) Destroy(root.gameObject);
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
            if (presenting && step == Step.Idle && now >= lineUntil) Place(presentingNow: false);
            if (!placed) Place(presentingNow: false);
            if (lineRoot.gameObject.activeSelf != LineShown(now)) Layout();
            FollowPerson(Time.unscaledDeltaTime);
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
                Say("Forget the Mac at " + paired.Address + "? This headset then needs pairing again to reach it.");
                return;
            }
            confirmUntil = 0f;
            step = Step.Forgetting;
            var forgotten = paired;
            forgetting = Task.Run(() => PairingClient.RevokeAsync(forgotten));
            Say("Forgetting the Mac…");
        }

        private void BeginPairing()
        {
            if (!TouchScreenKeyboard.isSupported)
            {
                Say("Pairing needs the headset's system keyboard; pair from the headset.");
                return;
            }
            step = Step.Address;
            keyboard = TouchScreenKeyboard.Open(PlayerPrefs.GetString(AddressPreference, ""), TouchScreenKeyboardType.URL,
                false, false, false, false, "The Mac's address, as pnpm pair shows it");
            Say("Type the Mac's address, as pnpm pair shows it, such as 192.168.1.23:47801.");
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
                keyboard = TouchScreenKeyboard.Open("", TouchScreenKeyboardType.NumberPad, false, false, false, false,
                    "The eight-digit code pnpm pair shows");
                Say("Type the eight-digit code pnpm pair shows on the Mac.");
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
                Say("Paired with the Mac at " + paired.Address + ". Connecting over Wi-Fi.");
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
                    ? "Forgot the Mac, which no longer accepts this headset."
                    : "Forgot the Mac on this headset. It could not be reached, so revoke this headset there with pnpm devices.");
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
            if (!presenting || !placed) Place(presentingNow: true);
            Layout();
        }

        private bool LineShown(float now) => shownLine.Length > 0 && (now < lineUntil || step != Step.Idle);

        /// <summary>The button and, above it while it shows, the line, centered on the controls' origin.</summary>
        private void Layout()
        {
            if (step == Step.Idle)
            {
                var confirming = paired != null && confirmUntil > 0f;
                var text = paired == null ? "Pair with a Mac" : confirming ? "Yes, forget this Mac" : "Forget this Mac";
                button.Show(text, Vector2.zero, button.Measure(text, MinButtonWidth), confirm: confirming);
            }
            else
            {
                button.Hide();
            }
            var shown = LineShown(Time.unscaledTime);
            lineRoot.gameObject.SetActive(shown);
            if (!shown) return;
            // A refusal can carry the words of whatever answered at the typed address, and a failure an
            // exception's: shown by the one rule for text Halcyonic did not write.
            line.text = LabelText.ForTextMeshPro(shownLine);
            var size = line.GetPreferredValues(line.text, LineWidth, 0f);
            var textHeight = Mathf.Min(size.y, 0.2f);
            line.rectTransform.sizeDelta = new Vector2(LineWidth, textHeight);
            var plateSize = new Vector2(Mathf.Min(size.x, LineWidth) + 2f * LinePadding, textHeight + 2f * LinePadding);
            linePlate.size = plateSize;
            linePlate.transform.localPosition = new Vector3(0f, 0f, 0.002f);
            var buttonTop = step == Step.Idle ? PanelButton.Height / 2f + Gap : 0f;
            lineRoot.localPosition = new Vector3(0f, buttonTop + plateSize.y / 2f, 0f);
        }

        /// <summary>
        /// Stands the controls where they rest, or up in front of the person, facing the eyes and
        /// scaled to keep the button's designed angular size.
        /// </summary>
        private void Place(bool presentingNow)
        {
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head == null) return;
            presenting = presentingNow;
            placed = true;
            awayFor = 0f;
            var eyes = head.position;
            var direction = Quaternion.AngleAxis(presentingNow ? 0f : RestTurnDegrees, Vector3.up) * Level(head);
            var position = eyes + direction * (presentingNow ? PresentReach : RestReach)
                + Vector3.down * (presentingNow ? PresentBelowEyes : RestBelowEyes);
            var toPanel = position - eyes;
            root.SetPositionAndRotation(position, Quaternion.LookRotation(toPanel.normalized, Vector3.up));
            root.localScale = Vector3.one * (toPanel.magnitude / PairingVisuals.DesignDistance);
        }

        /// <summary>Returns the controls to rest when the person has faced well away from them for a moment.</summary>
        private void FollowPerson(float deltaTime)
        {
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head == null || presenting) return;
            var toPanel = root.position - head.position;
            var level = new Vector3(toPanel.x, 0f, toPanel.z);
            var away = level.sqrMagnitude < 1e-4f || Vector3.Angle(Level(head), level) > AwayDegrees || level.magnitude > 1.0f;
            awayFor = away ? awayFor + Mathf.Min(deltaTime, 0.1f) : 0f;
            if (awayFor > AwaySeconds) Place(presentingNow: false);
        }

        /// <summary>Where the head faces on the level, even looking straight down.</summary>
        private static Vector3 Level(Transform head)
        {
            var forward = head.forward;
            var level = new Vector3(forward.x, 0f, forward.z);
            if (level.sqrMagnitude < 0.01f)
            {
                var up = head.up;
                level = new Vector3(up.x, 0f, up.z) * (forward.y < 0f ? 1f : -1f);
            }
            return level.sqrMagnitude < 1e-6f ? Vector3.forward : level.normalized;
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

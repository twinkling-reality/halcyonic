#nullable enable
using System;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>What a button does, which sets its look (ADR 0023): roles, never screens, choose colours.</summary>
    public enum ButtonRole
    {
        /// <summary>The one action a screen leads to, filled with the accent.</summary>
        Primary,

        /// <summary>Any other action.</summary>
        Secondary,

        /// <summary>An action that cannot be taken back: outlined in red, never filled.</summary>
        Destructive,

        /// <summary>A choice that shows or hides something, as a project on the rail: outlined in the accent while on.</summary>
        Filter,
    }

    /// <summary>
    /// A button of the interface (ADR 0023): pinched from afar with a hand ray, or poked. Its label
    /// always says what it does; a second, smaller line under it can say more, such as a project's
    /// counts. It is 60 dp tall, or 48 dp when compact, never less, and it shows every state: at rest,
    /// pointed at, pressed, unavailable, set aside while the app lacks focus, and done. Built in units
    /// of its distance from the eyes, under a parent scaled by that distance.
    /// </summary>
    /// <remarks>
    /// Its presses follow the workspace's button (<c>PanelButton</c>), whose API it keeps: a press
    /// within <see cref="SettleSeconds"/> of the button taking a new role is not a press of it, so a
    /// confirmation needs a separate, deliberate gesture, and a hold button starts its hold after
    /// <see cref="HoldSeconds"/> and ends it when let go, dropped or taken away, as hold to talk needs.
    /// </remarks>
    public sealed class GlazeButton : MonoBehaviour
    {
        public const float HeightDegrees = Glaze.TargetDegrees;

        public const float CompactHeightDegrees = Glaze.MinimumTargetDegrees;

        /// <summary>A pinch or poke that began before the button took its current role is not a press of it.</summary>
        public const float SettleSeconds = 0.35f;

        /// <summary>How long a hold button must be held before its hold starts (ADR 0021).</summary>
        public const float HoldSeconds = 0.3f;

        private const float PaddingDegrees = 1.25f;
        private const float CompactPaddingDegrees = 0.9f;
        private const float EdgeDegrees = 0.12f;
        private const float HoverEdgeDegrees = 0.15f;

        /// <summary>How much smaller a press makes the button, for the time a press shows.</summary>
        private const float PressedScale = 0.97f;

        /// <summary>Set aside while the app lacks focus: this visible.</summary>
        private const float AwayOpacity = 0.45f;

        private Surface plate = null!;
        private TextMeshPro label = null!;
        private TextMeshPro detail = null!;
        private PointerTarget target = null!;
        private ButtonRole role;
        private bool compact;
        private bool on;
        private bool available = true;
        private bool done;
        private GlazeTone? detailTone;
        private Vector2 size;
        private float shownAt;
        private float flash;
        private float pressedAt = -1f;
        private bool holding;
        private int paintedState = -1;

        /// <summary>A press; on a hold button, a press let go before its hold started.</summary>
        public event Action? Pressed;

        /// <summary>A hold button was held for <see cref="HoldSeconds"/>.</summary>
        public event Action? HoldStarted;

        /// <summary>A hold that started has ended: let go on the button (true), or dropped (false).</summary>
        public event Action<bool>? HoldEnded;

        /// <summary>A hold button: it starts something while held, as hold to talk does, instead of acting on a press.</summary>
        public bool Holds { get; set; }

        /// <summary>A hold started and has not ended.</summary>
        public bool Holding => holding;

        /// <summary>Presses are ignored while false, as while a panel grows or the app lacks focus.</summary>
        public Func<bool> Accepting { get; set; } = () => true;

        /// <summary>The button's size, in its parent's units.</summary>
        public Vector2 Size => size;

        public float Width => size.x;

        public TextMeshPro Label => label;

        /// <summary>The second line, while one shows.</summary>
        public TextMeshPro? Detail => detail.gameObject.activeSelf ? detail : null;

        public PointerTarget Target => target;

        public ButtonRole Role => role;

        public static float HeightOf(bool compact) => GlazeTokens.Units(compact ? CompactHeightDegrees : HeightDegrees);

        public static GlazeButton Create(Transform parent, string name, ButtonRole role, bool compact = false, int order = 11)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var button = go.AddComponent<GlazeButton>();
            button.role = role;
            button.compact = compact;
            button.plate = Surface.Create(go.transform, "Plate", order);
            button.label = GlazeText.Create(go.transform, "Label", compact ? GlazeType.Badge : GlazeType.Body, GlazeTokens.Text,
                TextAlignmentOptions.Center, order + 1, strong: true);
            button.label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            button.label.textWrappingMode = TextWrappingModes.NoWrap;
            button.detail = GlazeText.Create(go.transform, "Detail", GlazeType.Caption, GlazeTokens.TextSecondary, TextAlignmentOptions.Center, order + 1);
            button.detail.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            button.detail.textWrappingMode = TextWrappingModes.NoWrap;
            button.detail.gameObject.SetActive(false);
            var height = HeightOf(compact);
            button.target = PointerTarget.Rectangle(go, new Vector2(height, height), ray: true, poke: true);
            button.target.Selected += button.OnSelected;
            button.target.Released += button.OnReleased;
            go.SetActive(false);
            return button;
        }

        /// <summary>How wide the button is with this label and second line: the wider and the padding, never narrower than tall.</summary>
        public float Measure(string text, string? detailText = null)
        {
            var padding = GlazeTokens.Units(compact ? CompactPaddingDegrees : PaddingDegrees);
            var widest = label.GetPreferredValues(LabelText.ForTextMeshPro(text)).x;
            if (detailText != null) widest = Mathf.Max(widest, detail.GetPreferredValues(LabelText.ForTextMeshPro(detailText)).x);
            return Mathf.Max(HeightOf(compact), widest + 2f * padding);
        }

        /// <summary>
        /// Shows the button centred at <paramref name="center"/>, in its parent's units, as wide as
        /// <paramref name="width"/>, its label and second line as written, the line in
        /// <paramref name="tone"/>'s colour when given.
        /// </summary>
        public void Show(string text, Vector2 center, float width, string? detailText = null, GlazeTone? tone = null)
        {
            var changed = !gameObject.activeSelf || label.text != LabelText.ForTextMeshPro(text)
                || (detailText == null ? detail.gameObject.activeSelf : !detail.gameObject.activeSelf || detail.text != LabelText.ForTextMeshPro(detailText));
            GlazeText.SetLiteral(label, text);
            detailTone = tone;
            var height = HeightOf(compact);
            size = new Vector2(Mathf.Max(width, height), height);
            var padding = GlazeTokens.Units(compact ? CompactPaddingDegrees : PaddingDegrees);
            var inner = size.x - 2f * padding;
            if (detailText == null)
            {
                detail.gameObject.SetActive(false);
                GlazeText.Lay(label, inner, 1);
                label.transform.localPosition = new Vector3(0f, 0f, -0.0005f);
            }
            else
            {
                GlazeText.SetLiteral(detail, detailText);
                detail.gameObject.SetActive(true);
                GlazeText.Lay(label, inner, 1);
                GlazeText.Lay(detail, inner, 1);
                // The label over the line, the pair centred in the button.
                var labelLine = GlazeText.LineHeight(label);
                var detailLine = GlazeText.LineHeight(detail);
                var top = (labelLine + detailLine) / 2f;
                label.transform.localPosition = new Vector3(0f, top - labelLine / 2f, -0.0005f);
                detail.transform.localPosition = new Vector3(0f, top - labelLine - detailLine / 2f, -0.0005f);
            }
            target.Resize(size);
            // In front of whatever it sits on, so a ray finds the button first.
            transform.localPosition = new Vector3(center.x, center.y, -GlazeTokens.Units(0.1f));
            if (changed) shownAt = Time.unscaledTime;
            paintedState = -1;
            gameObject.SetActive(true);
            Paint();
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>For a filter: whether what it shows or hides is shown now, outlined in the accent.</summary>
        public bool On
        {
            get => on;
            set
            {
                if (on == value) return;
                on = value;
                paintedState = -1;
            }
        }

        /// <summary>Whether it can be pressed at all; an unavailable button is outlined, quiet and takes no press.</summary>
        public bool Available
        {
            get => available;
            set
            {
                if (available == value) return;
                available = value;
                paintedState = -1;
            }
        }

        /// <summary>Whether what it asked for is done, as the runtime confirmed: shown in the success colours.</summary>
        public bool Done
        {
            get => done;
            set
            {
                if (done == value) return;
                done = value;
                paintedState = -1;
            }
        }

        private void OnSelected()
        {
            if (!available || !Accepting() || Time.unscaledTime - shownAt < SettleSeconds) return;
            if (Holds)
            {
                pressedAt = Time.unscaledTime;
                return;
            }
            flash = Glaze.PressSeconds + Glaze.ReleaseSeconds;
            Pressed?.Invoke();
        }

        private void OnReleased(bool cancelled)
        {
            if (pressedAt < 0f) return;
            var started = holding;
            EndPress();
            if (started) HoldEnded?.Invoke(!cancelled);
            else if (!cancelled) Pressed?.Invoke();
        }

        private void EndPress()
        {
            pressedAt = -1f;
            holding = false;
        }

        private void OnDisable()
        {
            if (pressedAt < 0f) return;
            var started = holding;
            EndPress();
            if (started) HoldEnded?.Invoke(false);
        }

        private void Update()
        {
            flash = Mathf.Max(0f, flash - Time.unscaledDeltaTime);
            if (pressedAt >= 0f)
            {
                if (!Accepting() || !target.HandHovered)
                {
                    var started = holding;
                    EndPress();
                    if (started) HoldEnded?.Invoke(false);
                }
                else if (!holding && Time.unscaledTime - pressedAt >= HoldSeconds)
                {
                    holding = true;
                    HoldStarted?.Invoke();
                }
            }
            Paint();
        }

        /// <summary>
        /// For the editor's renders, which have no hands: paints the button as pointed at or pressed.
        /// The next frame paints it as it really is.
        /// </summary>
        public void PaintForRender(bool hoveredNow, bool pressedNow)
        {
            paintedState = -1;
            Paint(hoveredNow, pressedNow);
        }

        /// <summary>Paints the state it is in now, only when that changed.</summary>
        private void Paint() => Paint(target.Hovered, flash > 0f || holding);

        private void Paint(bool pointed, bool pressed)
        {
            var hovered = pointed && available;
            var away = FocusGuard.InputSuspended;
            var state = (pressed ? 1 : 0) | (hovered ? 2 : 0) | (away ? 4 : 0);
            if (state == paintedState) return;
            paintedState = state;

            Color fill, text, edge = Color.clear;
            var edgeWidth = 0f;
            var accent = Glaze.Tone(GlazeTone.Accent);
            if (!available)
            {
                fill = Color.clear;
                text = GlazeTokens.ColorOf(Glaze.TextDisabled);
                edge = GlazeTokens.ColorOf(GlazeColor.Hex(0x3A4658));
                edgeWidth = GlazeTokens.Units(0.1f);
            }
            else if (done)
            {
                var success = Glaze.Tone(GlazeTone.Success);
                fill = GlazeTokens.ColorOf(success.Container);
                text = GlazeTokens.ColorOf(success.Foreground);
                edge = GlazeTokens.ColorOf(success.Strong, 0.5f);
                edgeWidth = GlazeTokens.Units(0.1f);
            }
            else
            {
                switch (role)
                {
                    case ButtonRole.Primary:
                        fill = GlazeTokens.ColorOf(pressed ? GlazeColor.Hex(0x6E93D8) : hovered ? GlazeColor.Hex(0x98BAFA) : accent.Strong);
                        text = GlazeTokens.ColorOf(accent.OnStrong);
                        break;
                    case ButtonRole.Destructive:
                        var failure = Glaze.Tone(GlazeTone.Failure);
                        fill = pressed || hovered ? GlazeTokens.ColorOf(failure.Container) : Color.clear;
                        text = GlazeTokens.ColorOf(failure.Foreground);
                        edge = GlazeTokens.ColorOf(failure.Strong);
                        edgeWidth = GlazeTokens.Units(EdgeDegrees);
                        break;
                    case ButtonRole.Filter when !on:
                        // Quiet while off; pointed at or pressed, its fill lightens and so must its word.
                        fill = GlazeTokens.ColorOf(pressed ? Glaze.ControlPressed : hovered ? Glaze.ControlHover : Glaze.Raised);
                        text = pressed || hovered ? GlazeTokens.Text : GlazeTokens.TextSecondary;
                        break;
                    default:
                        fill = GlazeTokens.ColorOf(pressed ? Glaze.ControlPressed : hovered ? Glaze.ControlHover : Glaze.Control);
                        text = GlazeTokens.Text;
                        if (role == ButtonRole.Filter)
                        {
                            edge = GlazeTokens.ColorOf(accent.Strong);
                            edgeWidth = GlazeTokens.Units(EdgeDegrees);
                        }
                        break;
                }
                if (hovered && edgeWidth == 0f)
                {
                    // Pointed at: a ring of the accent inside the outline, besides the lighter fill.
                    edge = GlazeTokens.ColorOf(accent.Strong, 0.55f);
                    edgeWidth = GlazeTokens.Units(HoverEdgeDegrees);
                }
            }
            plate.Draw(size, GlazeTokens.Units(Glaze.ButtonRadiusDegrees), fill, edge, edgeWidth);
            var opacity = away ? AwayOpacity : 1f;
            plate.Fade(opacity);
            label.color = new Color(text.r, text.g, text.b, opacity);
            var line = detailTone.HasValue && available && !done ? GlazeTokens.ColorOf(Glaze.Tone(detailTone.Value).Foreground) : role == ButtonRole.Primary ? text : GlazeTokens.TextSecondary;
            detail.color = new Color(line.r, line.g, line.b, opacity);
            transform.localScale = Vector3.one * (pressed ? PressedScale : 1f);
        }
    }
}

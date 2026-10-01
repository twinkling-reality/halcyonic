#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// A button in the workspace: pinch it from afar with a hand ray, or poke it. It brightens while
    /// pointed at and flashes when pressed, and its label always says what it does. A button can
    /// carry a second, smaller line under its label, such as a project's counts on a rail chip, or
    /// show its label in two lines, such as a question on a tab.
    /// </summary>
    public sealed class PanelButton : MonoBehaviour
    {
        public const float Height = 0.072f;

        /// <summary>
        /// A pinch or poke that began before the button took its current role is not a press of it:
        /// the second step of a confirmation must be a separate, deliberate gesture.
        /// </summary>
        private const float SettleSeconds = 0.35f;

        private const float FlashSeconds = 0.18f;

        /// <summary>The detail line's size relative to the label's.</summary>
        private const float DetailScale = 0.78f;

        /// <summary>How long a hold button must be held before its hold starts (ADR 0021).</summary>
        public const float HoldSeconds = 0.3f;

        private float height = Height;
        private float textSize = WorkspaceVisuals.BodySize;
        private SpriteRenderer plate = null!;
        private TextMeshPro label = null!;
        private TextMeshPro? detail;
        private PointerTarget target = null!;
        private Color normal;
        private Color hover;
        private float shownAt;
        private float flash;
        private float pressedAt = -1f;
        private bool holding;

        /// <summary>A press; on a hold button, a press let go before its hold started.</summary>
        public event Action? Pressed;

        /// <summary>A hold button was held for <see cref="HoldSeconds"/>.</summary>
        public event Action? HoldStarted;

        /// <summary>
        /// A hold that started has ended: let go on the button (true), or dropped (false) because the
        /// hand left it, input was suspended, or the button went away.
        /// </summary>
        public event Action<bool>? HoldEnded;

        /// <summary>A hold button: it starts something while held, as hold to talk does, instead of acting on a press.</summary>
        public bool Holds { get; set; }

        /// <summary>A hold started and has not ended.</summary>
        public bool Holding => holding;

        /// <summary>Presses are ignored while false, for example while the workspace grows or shrinks.</summary>
        public Func<bool> Accepting { get; set; } = () => true;

        public float Width { get; private set; }

        /// <summary>The label, for the editor's checks that it shows what it was given, whole.</summary>
        public TextMeshPro Label => label;

        /// <summary>The detail line, while one shows.</summary>
        public TextMeshPro? Detail => detail != null && detail.gameObject.activeSelf ? detail : null;

        /// <param name="height">The button's height; a smaller one, such as a tab's, suits a secondary control.</param>
        /// <param name="textSize">The label's size, <see cref="WorkspaceVisuals.BodySize"/> unless given.</param>
        public static PanelButton Create(Transform parent, string name, float height = Height, float textSize = WorkspaceVisuals.BodySize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var button = go.AddComponent<PanelButton>();
            button.height = height;
            button.textSize = textSize;
            button.plate = WorkspaceVisuals.Plate(go.transform, "Plate", new Vector2(0.1f, height), WorkspaceVisuals.ButtonColor, WorkspaceVisuals.PanelControlOrder);
            button.label = WorkspaceVisuals.Text(go.transform, "Label", textSize, WorkspaceVisuals.TextColor,
                new Vector2(0.1f, height), TextAlignmentOptions.Center, order: WorkspaceVisuals.PanelTextOrder);
            button.target = PointerTarget.Rectangle(go, new Vector2(0.1f, height), ray: true, poke: true);
            button.target.Selected += button.OnSelected;
            button.target.Released += button.OnReleased;
            go.SetActive(false);
            return button;
        }

        /// <summary>How wide the button is with this label: the label and a margin, at least <paramref name="minWidth"/>.</summary>
        public float Measure(string text, float minWidth, float margin = 0.05f) =>
            Mathf.Max(minWidth, label.GetPreferredValues(LabelText.ForTextMeshPro(text)).x + margin);

        /// <summary>How wide the button is with a label in lines, the widest line deciding.</summary>
        public float MeasureLines(IReadOnlyList<string> lines, float minWidth, float margin = 0.05f)
        {
            var widest = 0f;
            foreach (var line in lines) widest = Mathf.Max(widest, label.GetPreferredValues(LabelText.ForTextMeshPro(line)).x);
            return Mathf.Max(minWidth, widest + margin);
        }

        /// <summary>
        /// Shows the button centered at <paramref name="center"/>, in the panel's coordinates. Its
        /// label can come from outside, as a recorded instruction's does, so it shows as written.
        /// A <paramref name="detail"/> line, smaller and under the label, shows as written too.
        /// </summary>
        public void Show(string text, Vector2 center, float width, bool confirm = false, string? detail = null, Color? detailColor = null)
        {
            var changed = !gameObject.activeSelf || label.text != LabelText.ForTextMeshPro(text) || DetailChanged(detail);
            WorkspaceVisuals.SetLiteral(label, text);
            Place(center, width, confirm, changed, detail, detailColor);
        }

        /// <summary>Shows the label in lines, one under the other, each as written.</summary>
        public void ShowLines(IReadOnlyList<string> lines, Vector2 center, float width, bool confirm = false)
        {
            var text = new System.Text.StringBuilder();
            foreach (var line in lines)
            {
                if (text.Length > 0) text.Append('\n');
                text.Append(LabelText.ForTextMeshPro(line));
            }
            var changed = !gameObject.activeSelf || label.text != text.ToString() || DetailChanged(null);
            WorkspaceVisuals.SetLiteralLines(label, lines);
            Place(center, width, confirm, changed, null, null);
        }

        public void Hide() => gameObject.SetActive(false);

        private bool DetailChanged(string? text) =>
            text == null ? detail != null && detail.gameObject.activeSelf : detail == null || !detail.gameObject.activeSelf || detail.text != LabelText.ForTextMeshPro(text);

        private void Place(Vector2 center, float width, bool confirm, bool changed, string? detailText, Color? detailColor)
        {
            Width = width;
            var size = new Vector2(Width, height);
            plate.size = size;
            target.Resize(size);
            normal = confirm ? WorkspaceVisuals.ConfirmColor : WorkspaceVisuals.ButtonColor;
            hover = confirm ? WorkspaceVisuals.ConfirmHoverColor : WorkspaceVisuals.ButtonHoverColor;
            label.color = confirm ? WorkspaceVisuals.ConfirmTextColor : WorkspaceVisuals.TextColor;
            if (detailText == null)
            {
                if (detail != null) detail.gameObject.SetActive(false);
                label.alignment = TextAlignmentOptions.Center;
                label.rectTransform.sizeDelta = size;
                label.rectTransform.localPosition = new Vector3(-Width / 2f, height / 2f, -0.001f);
            }
            else
            {
                // The label in the upper part, the detail under it, both inside the plate's margin.
                if (detail == null)
                {
                    detail = WorkspaceVisuals.Text(transform, "Detail", textSize * DetailScale, WorkspaceVisuals.SecondaryColor,
                        new Vector2(0.1f, height), TextAlignmentOptions.Center, order: WorkspaceVisuals.PanelTextOrder);
                }
                var inner = new Vector2(Width - 0.03f, height / 2f);
                label.alignment = TextAlignmentOptions.Bottom;
                label.rectTransform.sizeDelta = inner;
                label.rectTransform.localPosition = new Vector3(-inner.x / 2f, height / 2f + 0.002f, -0.001f);
                WorkspaceVisuals.SetLiteral(detail, detailText);
                detail.color = confirm ? WorkspaceVisuals.ConfirmTextColor : detailColor ?? WorkspaceVisuals.SecondaryColor;
                detail.alignment = TextAlignmentOptions.Top;
                detail.rectTransform.sizeDelta = inner;
                detail.rectTransform.localPosition = new Vector3(-inner.x / 2f, -0.002f, -0.001f);
                detail.gameObject.SetActive(true);
            }
            // Slightly in front of the panel, so a ray finds the button before the panel behind it.
            transform.localPosition = new Vector3(center.x, center.y, -0.004f);
            if (changed) shownAt = Time.unscaledTime;
            // Painted at once, so a confirmation never shows a frame in the ordinary button's color.
            Paint();
            gameObject.SetActive(true);
        }

        private void OnSelected()
        {
            if (!Accepting() || Time.unscaledTime - shownAt < SettleSeconds) return;
            if (Holds)
            {
                pressedAt = Time.unscaledTime;
                return;
            }
            flash = FlashSeconds;
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

        private void Paint() =>
            plate.color = flash > 0f || holding ? WorkspaceVisuals.ButtonPressColor : target.Hovered ? hover : normal;
    }
}

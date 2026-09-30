#nullable enable
using System;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// A button in the workspace: pinch it from afar with a hand ray, or poke it. It brightens while
    /// pointed at and flashes when pressed, and its label always says what it does.
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

        private float height = Height;
        private SpriteRenderer plate = null!;
        private TextMeshPro label = null!;
        private PointerTarget target = null!;
        private Color normal;
        private Color hover;
        private float shownAt;
        private float flash;

        public event Action? Pressed;

        /// <summary>Presses are ignored while false, for example while the workspace grows or shrinks.</summary>
        public Func<bool> Accepting { get; set; } = () => true;

        public float Width { get; private set; }

        /// <param name="height">The button's height; a smaller one, such as a tab's, suits a secondary control.</param>
        /// <param name="textSize">The label's size, <see cref="WorkspaceVisuals.BodySize"/> unless given.</param>
        public static PanelButton Create(Transform parent, string name, float height = Height, float textSize = WorkspaceVisuals.BodySize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var button = go.AddComponent<PanelButton>();
            button.height = height;
            button.plate = WorkspaceVisuals.Plate(go.transform, "Plate", new Vector2(0.1f, height), WorkspaceVisuals.ButtonColor, WorkspaceVisuals.PanelControlOrder);
            button.label = WorkspaceVisuals.Text(go.transform, "Label", textSize, WorkspaceVisuals.TextColor,
                new Vector2(0.1f, height), TextAlignmentOptions.Center, order: WorkspaceVisuals.PanelTextOrder);
            button.target = PointerTarget.Rectangle(go, new Vector2(0.1f, height), ray: true, poke: true);
            button.target.Selected += button.OnSelected;
            go.SetActive(false);
            return button;
        }

        /// <summary>How wide the button is with this label: the label and a margin, at least <paramref name="minWidth"/>.</summary>
        public float Measure(string text, float minWidth) =>
            Mathf.Max(minWidth, label.GetPreferredValues(LabelText.ForTextMeshPro(text)).x + 0.05f);

        /// <summary>
        /// Shows the button centered at <paramref name="center"/>, in the panel's coordinates. Its
        /// label can come from outside, as a recorded instruction's does, so it shows as written.
        /// </summary>
        public void Show(string text, Vector2 center, float width, bool confirm = false)
        {
            var changed = !gameObject.activeSelf || label.text != LabelText.ForTextMeshPro(text);
            WorkspaceVisuals.SetLiteral(label, text);
            Width = width;
            var size = new Vector2(Width, height);
            plate.size = size;
            label.rectTransform.sizeDelta = size;
            label.rectTransform.localPosition = new Vector3(-Width / 2f, height / 2f, -0.001f);
            target.Resize(size);
            normal = confirm ? WorkspaceVisuals.ConfirmColor : WorkspaceVisuals.ButtonColor;
            hover = confirm ? WorkspaceVisuals.ConfirmHoverColor : WorkspaceVisuals.ButtonHoverColor;
            label.color = confirm ? WorkspaceVisuals.ConfirmTextColor : WorkspaceVisuals.TextColor;
            // Slightly in front of the panel, so a ray finds the button before the panel behind it.
            transform.localPosition = new Vector3(center.x, center.y, -0.004f);
            if (changed) shownAt = Time.unscaledTime;
            // Painted at once, so a confirmation never shows a frame in the ordinary button's color.
            Paint();
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);

        private void OnSelected()
        {
            if (!Accepting() || Time.unscaledTime - shownAt < SettleSeconds) return;
            flash = FlashSeconds;
            Pressed?.Invoke();
        }

        private void Update()
        {
            flash = Mathf.Max(0f, flash - Time.unscaledDeltaTime);
            Paint();
        }

        private void Paint() => plate.color = flash > 0f ? WorkspaceVisuals.ButtonPressColor : target.Hovered ? hover : normal;
    }
}

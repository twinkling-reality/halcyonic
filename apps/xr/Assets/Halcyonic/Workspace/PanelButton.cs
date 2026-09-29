#nullable enable
using System;
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

        public static PanelButton Create(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var button = go.AddComponent<PanelButton>();
            button.plate = WorkspaceVisuals.Plate(go.transform, "Plate", new Vector2(0.1f, Height), WorkspaceVisuals.ButtonColor, WorkspaceVisuals.ControlOrder);
            button.label = WorkspaceVisuals.Text(go.transform, "Label", WorkspaceVisuals.BodySize, WorkspaceVisuals.TextColor,
                new Vector2(0.1f, Height), TextAlignmentOptions.Center);
            button.target = PointerTarget.Rectangle(go, new Vector2(0.1f, Height), ray: true, poke: true);
            button.target.Selected += button.OnSelected;
            go.SetActive(false);
            return button;
        }

        /// <summary>How wide the button is with this label: the label and a margin, at least <paramref name="minWidth"/>.</summary>
        public float Measure(string text, float minWidth) => Mathf.Max(minWidth, label.GetPreferredValues(text).x + 0.05f);

        /// <summary>Shows the button centered at <paramref name="center"/>, in the panel's coordinates.</summary>
        public void Show(string text, Vector2 center, float width, bool confirm = false)
        {
            var changed = !gameObject.activeSelf || label.text != text;
            label.text = text;
            Width = width;
            var size = new Vector2(Width, Height);
            plate.size = size;
            label.rectTransform.sizeDelta = size;
            label.rectTransform.localPosition = new Vector3(-Width / 2f, Height / 2f, -0.001f);
            target.Resize(size);
            normal = confirm ? WorkspaceVisuals.ConfirmColor : WorkspaceVisuals.ButtonColor;
            hover = confirm ? WorkspaceVisuals.ConfirmHoverColor : WorkspaceVisuals.ButtonHoverColor;
            label.color = confirm ? WorkspaceVisuals.ConfirmTextColor : WorkspaceVisuals.TextColor;
            // Slightly in front of the panel, so a ray finds the button before the panel behind it.
            transform.localPosition = new Vector3(center.x, center.y, -0.004f);
            if (changed) shownAt = Time.unscaledTime;
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
            plate.color = flash > 0f ? WorkspaceVisuals.ButtonPressColor : target.Hovered ? hover : normal;
        }
    }
}

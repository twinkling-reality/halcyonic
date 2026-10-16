#nullable enable
using System;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// How a kit control takes a press, by <see cref="GlazeButton"/>'s rules: one whole hand target on the plane
    /// (<see cref="PointerTarget"/>, ray and poke); a press counts only once the control has shown its current words,
    /// role, place and availability for <see cref="GlazeButton.SettleSeconds"/>, only while its host accepts presses
    /// and only while the app has input focus; an unavailable control refuses the press instead of acting. A press is
    /// a request to the control's owner, never a state change by itself.
    /// </summary>
    public sealed class KitPress : MonoBehaviour
    {
        private PointerTarget target = null!;
        private Vector2 size;
        private float shownAt;
        private bool available = true;
        private float flash;

        /// <summary>A press of an available control, after it settled.</summary>
        public event Action? Pressed;

        /// <summary>A press of an unavailable control while presses are otherwise taken.</summary>
        public event Action? Refused;

        /// <summary>Presses are ignored while false, as while the instrument moves or a view opens.</summary>
        public Func<bool> Accepting { get; set; } = () => true;

        public PointerTarget Target => target;

        /// <summary>The target's size, in its parent's units.</summary>
        public Vector2 Size => size;

        public bool Hovered => target.Hovered;

        /// <summary>A press shows, for its short flash.</summary>
        public bool Pressing => flash > 0f;

        public bool Available
        {
            get => available;
            set
            {
                if (available == value) return;
                available = value;
                // Becoming available is a new action: a press already under way is not one of it.
                if (value) Unsettle();
            }
        }

        public static KitPress Create(GameObject host, Vector2 size)
        {
            var press = host.AddComponent<KitPress>();
            press.size = size;
            press.target = PointerTarget.Rectangle(host, size, ray: true, poke: true);
            press.target.Selected += press.OnSelected;
            press.shownAt = Time.unscaledTime;
            return press;
        }

        public void Resize(Vector2 size)
        {
            if (this.size == size) return;
            this.size = size;
            target.Resize(size);
        }

        /// <summary>The control took new words, a new role or a new place: a press already under way is not one of it.</summary>
        public void Unsettle() => shownAt = Time.unscaledTime;

        /// <summary>For renders: advances the settling clock only; focus, availability and the host's acceptance still decide.</summary>
        public void SettleForRender() => shownAt = float.NegativeInfinity;

        /// <summary>For renders, which have no hands: a finger's press and release at the target's centre, as the SDK raises them.</summary>
        public void PressForRender(int pointer)
        {
            target.PointerForRender(pointer, Oculus.Interaction.PointerEventType.Select, transform.position);
            target.PointerForRender(pointer, Oculus.Interaction.PointerEventType.Unselect, transform.position);
        }

        private void OnSelected()
        {
            if (!Accepting() || FocusGuard.InputSuspended || Time.unscaledTime - shownAt < GlazeButton.SettleSeconds) return;
            if (!available)
            {
                Refused?.Invoke();
                return;
            }
            flash = Glaze.PressSeconds + Glaze.ReleaseSeconds;
            Pressed?.Invoke();
        }

        private void Update()
        {
            if (flash > 0f) flash = Mathf.Max(0f, flash - Time.unscaledDeltaTime);
        }
    }
}

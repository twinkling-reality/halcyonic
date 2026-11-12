#nullable enable
using System;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// How a kit control takes a press: ONE press (the lead's decision R1, FIELD's design of 2026-11-12), one whole hand
    /// target on the plane (<see cref="PointerTarget"/>, ray and poke). The press acts on its RELEASE over the control, by
    /// <see cref="PressGuard"/>: it counts only where the pinch began after the control arrived (its current words, role,
    /// place and availability) and, for a ray, after the ray rested on it; a pinch that slides off or drags does nothing.
    /// Only while its host accepts presses and the app has input focus; an unavailable control refuses the press instead
    /// of acting. A press is a request to the control's owner, never a state change by itself.
    /// </summary>
    public sealed class KitPress : MonoBehaviour
    {
        private PointerTarget target = null!;
        private Vector2 size;
        private PressGuard guard = null!;
        private bool byRay;
        private bool counted;
        private bool available = true;
        private float flash;

        /// <summary>A press of an available control, after it settled.</summary>
        public event Action? Pressed;

        /// <summary>A press of an unavailable control while presses are otherwise taken.</summary>
        public event Action? Refused;

        /// <summary>
        /// A control that is held (Hold to Talk) acts at its counted pinch, not its release, and its release ends the hold;
        /// the same guard decides whether the pinch counts.
        /// </summary>
        public bool ActsOnPinch { get; set; }

        /// <summary>The seconds every press is timed by: Unity's own, or a check's, which drives it exactly.</summary>
        public Func<float> Clock { get; set; } = () => Time.unscaledTime;

        /// <summary>Presses are ignored while false, as while the instrument moves or a view opens.</summary>
        public Func<bool> Accepting { get; set; } = () => true;

        public PointerTarget Target => target;

        /// <summary>The target's size, in its parent's units.</summary>
        public Vector2 Size => size;

        /// <summary>Lit means it will take: hovered, and a pinch made now would count (<see cref="PressGuard.Lit"/>).</summary>
        public bool Hovered => target.Hovered && guard.Lit(Clock(), target.RayHovered);

        /// <summary>A press shows: a counted pinch held, then its short flash once it acted.</summary>
        public bool Pressing => (counted && guard.Armed) || flash > 0f;

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
            press.guard = new PressGuard(press.Clock());
            press.target.SelectedBy += press.OnDown;
            press.target.PressMoved += press.OnMoved;
            press.target.Released += press.OnUp;
            press.target.HoverChanged += press.OnHover;
            return press;
        }

        public void Resize(Vector2 size)
        {
            if (this.size == size) return;
            this.size = size;
            target.Resize(size);
        }

        /// <summary>The control took new words, a new role or a new place: a press already under way is not one of it.</summary>
        public void Unsettle()
        {
            guard.Arrive(Clock());
            counted = false;
        }

        /// <summary>For renders: advances the settling clock only; focus, availability and the host's acceptance still decide.</summary>
        public void SettleForRender() => guard.SettleForRender();

        /// <summary>For renders, which have no hands: a finger's press and release at the target's centre, as the SDK raises them.</summary>
        public void PressForRender(int pointer)
        {
            target.PointerForRender(pointer, Oculus.Interaction.PointerEventType.Select, transform.position);
            target.PointerForRender(pointer, Oculus.Interaction.PointerEventType.Unselect, transform.position);
        }

        private void OnHover()
        {
            if (target.RayHovered) guard.RayOn(Clock());
            else guard.RayOff(Clock());
        }

        private void OnDown(bool ray)
        {
            byRay = ray;
            // A refused pinch sinks nothing: only a counted one shows its press.
            counted = Accepting() && !FocusGuard.InputSuspended && guard.Down(Clock(), ray);
            if (counted && ActsOnPinch) Act();
        }

        private void OnMoved(float degrees, float metres, bool rayMeets)
        {
            guard.Moved(degrees, metres);
            // While a ray holds its pinch the interactor keeps its hover, so whether it still meets the control is read here.
            if (!byRay) return;
            if (rayMeets) guard.RayOn(Clock());
            else guard.RayOff(Clock());
        }

        private void OnUp(bool cancelled)
        {
            // The guard hears every release, so no pinch stays armed past its own.
            var released = guard.Up(Clock(), byRay, cancelled);
            var acts = counted && released && Accepting() && !FocusGuard.InputSuspended && !ActsOnPinch;
            counted = false;
            if (!target.RayHovered) guard.RayOff(Clock());
            if (acts) Act();
        }

        private void Act()
        {
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

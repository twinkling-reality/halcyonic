#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// A frame's footer of prompts on the menu (<see cref="Footer"/>, ADR 0026), drawn along its last
    /// row: Close far left and the rare action beside it, the far right's prompt at the right end and
    /// the secondary beside it, and a confirmation's Yes in the middle between them, 12 mm apart and
    /// reaching <see cref="Glaze.Menu.PromptMarginDegrees"/> past each content line. What limits it is
    /// the prompts' width: <see cref="Fits"/> says whether they stand apart within the column.
    /// </summary>
    public sealed class FooterView : MonoBehaviour
    {
        private const int Slots = 5;

        private readonly GlazeButton?[] buttons = new GlazeButton?[Slots];
        private readonly Prompt?[] shown = new Prompt?[Slots];
        private readonly GlazeShimmer?[] shimmers = new GlazeShimmer?[Slots];
        private int order;

        /// <summary>A prompt was pressed: its id says what to do.</summary>
        public event Action<Prompt>? Pressed;

        /// <summary>A held prompt, as Hold to talk, was held long enough to start.</summary>
        public event Action<Prompt>? HoldStarted;

        /// <summary>A held prompt's hold ended: let go on it (true), or dropped (false).</summary>
        public event Action<Prompt, bool>? HoldEnded;

        /// <summary>The prompts stand apart within the column, 12 mm between each.</summary>
        public bool Fits { get; private set; } = true;

        /// <summary>How much room the prompts and the gaps between them need, and how much the row has, in its parent's units.</summary>
        public (float Needed, float Room) Measure { get; private set; }

        /// <summary>A footer's height: a target's.</summary>
        public static float Height => GlazeButton.HeightOf(false);

        /// <summary>The button showing the prompt in <paramref name="slot"/>, or null.</summary>
        public GlazeButton? this[PromptSlot slot] => shown[(int)slot] != null ? buttons[(int)slot] : null;

        /// <summary>The shimmer on the words of the prompt in <paramref name="slot"/>, or null: lit while that prompt waits (ADR 0027).</summary>
        public GlazeShimmer? ShimmerOf(PromptSlot slot) => shown[(int)slot] != null ? shimmers[(int)slot] : null;

        /// <summary>Every prompt showing, left to right.</summary>
        public IEnumerable<(PromptSlot Slot, GlazeButton Button)> Shown
        {
            get
            {
                for (var index = 0; index < Slots; index++)
                {
                    if (shown[index] != null && buttons[index] is GlazeButton button) yield return ((PromptSlot)index, button);
                }
            }
        }

        public static FooterView Create(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<FooterView>();
            view.order = order;
            return view;
        }

        /// <summary>The footer drawn last, for the renders' check that every prompt a frame offers is drawn.</summary>
        public Footer? Showing { get; private set; }

        /// <summary>
        /// Shows <paramref name="footer"/> on the row at <paramref name="middle"/>, between the content
        /// lines at <paramref name="left"/> and <paramref name="right"/>, in its parent's units.
        /// </summary>
        public void Show(Footer footer, float left, float right, float middle)
        {
            Showing = footer;
            var margin = GlazeTokens.Units(Glaze.Menu.PromptMarginDegrees);
            var gap = Glaze.TargetGapMeters / Glaze.Menu.PlaneMeters;
            var widths = new float[Slots];
            for (var index = 0; index < Slots; index++)
            {
                var prompt = footer[(PromptSlot)index];
                shown[index] = prompt;
                if (prompt == null)
                {
                    buttons[index]?.Hide();
                    if (shimmers[index] is GlazeShimmer still) still.Waits = false;
                    continue;
                }
                var button = Button(index);
                // A prompt whose words say what is under way, as Sent… or Hold to talk writing down, shimmers.
                shimmers[index]!.Waits = prompt.Waits;
                button.Holds = prompt.Holds;
                button.Available = prompt.Available;
                // As wide as the widest words it may show, so a hold that changes them never moves its cap.
                widths[index] = button.MeasurePrompt(prompt.Words, prompt.DrawnAsMain);
                foreach (var words in prompt.AlsoReads) widths[index] = Mathf.Max(widths[index], button.MeasurePrompt(words, prompt.DrawnAsMain));
            }

            // From the left: Close, then the rare action. From the right: the far right, then the secondary.
            var start = left - margin;
            var end = right + margin;
            var x = start;
            foreach (var slot in new[] { PromptSlot.Close, PromptSlot.Rare })
            {
                if (shown[(int)slot] == null) continue;
                Stand(slot, x + widths[(int)slot] / 2f, middle, widths[(int)slot]);
                x += widths[(int)slot] + gap;
            }
            var leftEnd = x;
            x = end;
            foreach (var slot in new[] { PromptSlot.FarRight, PromptSlot.Secondary })
            {
                if (shown[(int)slot] == null) continue;
                Stand(slot, x - widths[(int)slot] / 2f, middle, widths[(int)slot]);
                x -= widths[(int)slot] + gap;
            }
            var rightStart = x;
            var free = widths[(int)PromptSlot.Free];
            if (shown[(int)PromptSlot.Free] != null) Stand(PromptSlot.Free, (leftEnd + rightStart) / 2f, middle, free);

            var count = 0;
            var needed = 0f;
            foreach (var width in widths)
            {
                if (width <= 0f) continue;
                needed += width;
                count++;
            }
            needed += gap * Mathf.Max(0, count - 1);
            Measure = (needed, end - start);
            Fits = needed <= end - start + 1e-5f;
        }

        public void Hide()
        {
            for (var index = 0; index < Slots; index++)
            {
                shown[index] = null;
                buttons[index]?.Hide();
                if (shimmers[index] is GlazeShimmer still) still.Waits = false;
            }
        }

        private void Stand(PromptSlot slot, float center, float middle, float width)
        {
            var prompt = shown[(int)slot]!;
            Button((int)slot).ShowPrompt(prompt.Words, prompt.Icon, new Vector2(center, middle), width, prompt.DrawnAsMain);
        }

        private GlazeButton Button(int index)
        {
            if (buttons[index] is GlazeButton made) return made;
            var button = GlazeButton.Create(transform, "Prompt " + (PromptSlot)index, ButtonRole.Prompt, order: order);
            button.Pressed += () =>
            {
                if (shown[index] is Prompt prompt) Pressed?.Invoke(prompt);
            };
            button.HoldStarted += () =>
            {
                if (shown[index] is Prompt prompt) HoldStarted?.Invoke(prompt);
            };
            button.HoldEnded += letGo =>
            {
                if (shown[index] is Prompt prompt) HoldEnded?.Invoke(prompt, letGo);
            };
            buttons[index] = button;
            shimmers[index] = GlazeShimmer.On(button.Label);
            return button;
        }
    }
}

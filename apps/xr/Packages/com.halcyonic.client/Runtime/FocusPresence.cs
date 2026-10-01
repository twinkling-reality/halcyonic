#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>What changed at one <see cref="FocusPresence.Tick"/>.</summary>
    public readonly struct FocusChange
    {
        public FocusChange(bool left, bool foldChanged)
        {
            Left = left;
            FoldChanged = foldChanged;
        }

        /// <summary>
        /// Focus went to something else since the last tick, not the app's own keyboard: a half-done
        /// confirmation must be confirmed afresh.
        /// </summary>
        public bool Left { get; }

        /// <summary><see cref="FocusPresence.Folded"/> changed: large panels fold or come back.</summary>
        public bool FoldChanged { get; }
    }

    /// <summary>
    /// Whether the person is using Halcyonic or another window over it (a Mac's Virtual Display, a
    /// browser video, the system keyboard, the Meta menu), and what the app does about it. Input is
    /// suspended at once when focus goes, and stays suspended for <see cref="ReturnGrace"/> after it
    /// comes back, so the pinch that returns focus never presses a control. Large panels fold only
    /// after focus has stayed away for <see cref="FoldAfter"/>, so focus that flaps, as the Quest's
    /// system windows make it, never rearranges the stage, and they never fold for the app's own
    /// keyboard. They come back as they were once input is ready again. Work keeps running and
    /// updating throughout: losing focus is not a pause.
    /// </summary>
    /// <remarks>
    /// Time is the caller's monotonic clock, such as Unity's unscaled time; nothing here reads a
    /// clock, so it is tested without one.
    /// </remarks>
    public sealed class FocusPresence
    {
        /// <summary>How long focus stays away before large panels fold.</summary>
        public static readonly TimeSpan FoldAfter = TimeSpan.FromSeconds(3);

        /// <summary>How long input stays suspended after focus comes back.</summary>
        public static readonly TimeSpan ReturnGrace = TimeSpan.FromSeconds(0.5);

        private TimeSpan since;
        private int keyboards;
        private bool leftUnseen;

        /// <param name="now">When the app started, focused.</param>
        public FocusPresence(TimeSpan now)
        {
            HasFocus = true;
            since = now - ReturnGrace;
        }

        /// <summary>The app has input focus now, as the system last reported it.</summary>
        public bool HasFocus { get; private set; }

        /// <summary>Controls take no input: focus is away, or came back less than <see cref="ReturnGrace"/> ago.</summary>
        public bool InputSuspended { get; private set; }

        /// <summary>Large panels are folded out of the way of another window; their content is kept.</summary>
        public bool Folded { get; private set; }

        /// <summary>The app's own system keyboard is open, so focus away is expected and folds nothing.</summary>
        public bool KeyboardOpen => keyboards > 0;

        /// <summary>How many times focus went to something other than the app's own keyboard.</summary>
        public int Epoch { get; private set; }

        /// <summary>The system reported a focus change. Repeats of the same state change nothing.</summary>
        public void Report(bool hasFocus, TimeSpan now)
        {
            if (hasFocus == HasFocus) return;
            HasFocus = hasFocus;
            since = now;
            InputSuspended = true;
            if (!hasFocus && !KeyboardOpen)
            {
                Epoch++;
                leftUnseen = true;
            }
        }

        /// <summary>The app opened its system keyboard; until <see cref="KeyboardClosed"/>, focus away folds nothing.</summary>
        public void KeyboardOpened() => keyboards++;

        public void KeyboardClosed() => keyboards = Math.Max(0, keyboards - 1);

        /// <summary>Brings the state up to <paramref name="now"/>; call it every frame.</summary>
        public FocusChange Tick(TimeSpan now)
        {
            var held = now - since;
            InputSuspended = !HasFocus || held < ReturnGrace;
            var folded = HasFocus ? Folded && held < ReturnGrace : Folded || (!KeyboardOpen && held >= FoldAfter);
            var change = new FocusChange(leftUnseen, folded != Folded);
            Folded = folded;
            leftUnseen = false;
            return change;
        }
    }
}

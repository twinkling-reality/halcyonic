#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>
    /// Focus awareness, required by Meta (VRC.Quest.Input.4), and the ambient presence beside another
    /// window (<see cref="FocusPresence"/>). When the app loses input focus, to a Mac's Virtual Display,
    /// a browser video, the system keyboard or the Meta menu, it keeps rendering and updating, hides
    /// hand and controller visuals, and ignores their input; interactive components check
    /// <see cref="InputSuspended"/>. Input stays suspended for half a second after focus returns, so
    /// the gesture that brings it back never presses a control. Large panels fold out of the way once
    /// focus has stayed away for three seconds (<see cref="Folded"/>), never for the app's own keyboard
    /// (<see cref="Track"/>), and come back as they were. Components that hold a confirmation half done
    /// drop it on <see cref="Left"/>. Whether Unity reports the Quest system menu as a focus change
    /// under OpenXR is verified on the device (quest-3-device.md).
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class FocusGuard : MonoBehaviour
    {
        [SerializeField] private GameObject[] handVisuals = new GameObject[0];

        private static readonly List<TouchScreenKeyboard> keyboards = new List<TouchScreenKeyboard>();
        private static FocusPresence? presence;

        /// <summary>Controls take no hand input: focus is away, or came back less than half a second ago.</summary>
        public static bool InputSuspended => presence?.InputSuspended ?? false;

        /// <summary>Focus has stayed away, so large panels are folded; their content is kept.</summary>
        public static bool Folded => foldedForRender ?? presence?.Folded ?? false;

        private static bool? foldedForRender;

        /// <summary>Folds or restores as focus would, for the editor's renders; null follows focus again.</summary>
        public static void FoldForRender(bool? folded) => foldedForRender = folded;

        /// <summary>Focus went to another window, not the app's own keyboard: drop any confirmation half done.</summary>
        public static event Action? Left;

        /// <summary><see cref="Folded"/> changed.</summary>
        public static event Action? FoldChanged;

        /// <summary>
        /// Notes a system keyboard the app opened, so the focus it takes folds nothing and drops no
        /// confirmation, until it closes. Returns it, so it wraps <c>TouchScreenKeyboard.Open</c>.
        /// </summary>
        public static TouchScreenKeyboard? Track(TouchScreenKeyboard? keyboard)
        {
            if (keyboard == null || presence == null) return keyboard;
            keyboards.Add(keyboard);
            presence.KeyboardOpened();
            return keyboard;
        }

        private static TimeSpan Now => TimeSpan.FromSeconds(Time.unscaledTimeAsDouble);

        private void Awake()
        {
            presence = new FocusPresence(Now);
            keyboards.Clear();
        }

        private void OnDestroy()
        {
            presence = null;
            keyboards.Clear();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            presence?.Report(hasFocus, Now);
            foreach (var visual in handVisuals)
            {
                if (visual != null) visual.SetActive(hasFocus);
            }
        }

        private void Update()
        {
            if (presence == null) return;
            for (var index = keyboards.Count - 1; index >= 0; index--)
            {
                if (keyboards[index].status == TouchScreenKeyboard.Status.Visible) continue;
                keyboards.RemoveAt(index);
                presence.KeyboardClosed();
            }
            var change = presence.Tick(Now);
            if (change.Left)
            {
                Debug.Log("Halcyonic: focus went to another window; controls take no input until it is back.");
                Left?.Invoke();
            }
            if (change.FoldChanged)
            {
                Debug.Log(presence.Folded ? "Halcyonic: focus stayed away, so large panels are folded." : "Halcyonic: focus is back; panels are restored.");
                FoldChanged?.Invoke();
            }
        }
    }
}

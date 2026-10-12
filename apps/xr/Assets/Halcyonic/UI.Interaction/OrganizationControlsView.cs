#nullable enable
using System;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>Named workspace controls, separate from the title and the decision footer.</summary>
    public sealed class OrganizationControlsView : MonoBehaviour
    {
        private static GlazeButton? measurer;
        private GlazeButton move = null!;
        private GlazeButton toggle = null!;
        private Surface moveBoundary = null!;
        private Surface toggleBoundary = null!;
        private object? binding;
        private int generation;
        private int pressedGeneration = -1;
        private bool held;
        public void Bind(object? key)
        {
            if (ReferenceEquals(binding, key)) return;
            var hadPress = pressedGeneration >= 0 || held;
            held = false;
            binding = key;
            generation++;
            pressedGeneration = -1;
            move.EndPressSince(float.MinValue);
            move.Unsettle();
            toggle.EndPressSince(float.MinValue);
            toggle.Unsettle();
            if (hadPress) LetGo?.Invoke();
        }
        public event Action? MovePressed;
        public event Action? PressStarted;
        public event Action<Vector3>? Held;
        public event Action<Vector3>? Dragged;
        public event Action? LetGo;
        public event Action? Toggled;
        public Func<bool> Accepting { get; set; } = () => true;
        public GlazeButton Move => move;
        public GlazeButton Toggle => toggle;

        public static float Width(bool compact = false) => Measure("Move") + MenuPage.TargetGap + Measure(compact ? "Expand" : "Compact");
        public static float Measure(string words)
        {
            if (measurer == null)
            {
                var host = new GameObject("Organization measure") { hideFlags = HideFlags.HideAndDontSave };
                measurer = GlazeButton.Create(host.transform, "Measure", ButtonRole.Prompt);
                host.SetActive(false);
            }
            return measurer.MeasurePrompt(words, false);
        }

        public static OrganizationControlsView Create(Transform parent)
        {
            var go = new GameObject("Workspace controls");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<OrganizationControlsView>();
            view.moveBoundary = Surface.Create(go.transform, "Move boundary", 21);
            view.toggleBoundary = Surface.Create(go.transform, "Toggle boundary", 21);
            view.move = GlazeButton.Create(go.transform, "Move", ButtonRole.Prompt, order: 22);
            view.toggle = GlazeButton.Create(go.transform, "Compact or expand", ButtonRole.Prompt, order: 22);
            view.move.Holds = true;
            view.move.Accepting = view.toggle.Accepting = () => view.Accepting();
            view.move.Target.EnableDrag();
            view.move.Target.Selected += () =>
            {
                view.pressedGeneration = view.Accepting() ? view.generation : -1;
                if (view.pressedGeneration >= 0) view.PressStarted?.Invoke();
            };
            view.move.Pressed += () => { if (view.pressedGeneration == view.generation && view.Accepting()) view.MovePressed?.Invoke(); };
            view.move.HoldStarted += () =>
            {
                if (view.pressedGeneration != view.generation || !view.Accepting() || !(view.move.Target.HeldPoint is Vector3 point)) return;
                view.held = true;
                view.Held?.Invoke(point);
            };
            view.move.Target.Dragged += point => { if (view.held && view.pressedGeneration == view.generation) view.Dragged?.Invoke(point); };
            view.move.Target.Released += _ => { view.held = false; view.LetGo?.Invoke(); };
            view.toggle.Pressed += () => view.Toggled?.Invoke();
            return view;
        }

        public void Show(float right, float middle, bool compact)
        {
            gameObject.SetActive(true);
            var toggleWords = compact ? "Expand" : "Compact";
            var toggleWidth = Measure(toggleWords);
            var moveWidth = Measure("Move");
            Stand(toggle, toggleBoundary, toggleWords, GlazeIcon.Next, right - toggleWidth / 2f, middle, toggleWidth);
            Stand(move, moveBoundary, "Move", GlazeIcon.Move, right - toggleWidth - MenuPage.TargetGap - moveWidth / 2f, middle, moveWidth);
        }

        private static void Stand(GlazeButton button, Surface boundary, string words, GlazeIcon icon, float x, float y, float width)
        {
            button.ShowPrompt(words, icon, new Vector2(x, y), width, false);
            boundary.Draw(new Vector2(width, GlazeButton.HeightOf(false)), GlazeTokens.Units(Glaze.Menu.RadiusDegrees), Color.clear,
                new Color(1f, 1f, 1f, Glaze.Menu.PointedFrameOpacity), GlazeTokens.Units(Glaze.Menu.PointedFrameDegrees));
            boundary.transform.localPosition = new Vector3(x, y, -0.0001f);
        }
    }
}

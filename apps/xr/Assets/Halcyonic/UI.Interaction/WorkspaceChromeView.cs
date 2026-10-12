#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>The connected workspace frame and its fixed global controls, independent of task decisions.</summary>
    public sealed class WorkspaceChromeView : MonoBehaviour
    {
        private Surface frame = null!;
        private Surface heading = null!;
        private Surface rule = null!;
        private TextMeshPro product = null!;
        private TextMeshPro connection = null!;
        private TextMeshPro context = null!;
        private readonly List<Surface> grounds = new List<Surface>();
        private GlazeButton move = null!;
        private GlazeButton apps = null!;
        private GlazeButton minimize = null!;
        public Func<bool> Accepting { get; set; } = () => false;
        public event Action? OtherAppsPressed;
        public event Action? MinimizePressed;
        public event Action? MovePressed;
        public event Action? PressStarted;
        public event Action<Vector3>? Held;
        public event Action<Vector3>? Dragged;
        public event Action? LetGo;
        public IReadOnlyList<GlazeButton> Targets { get; private set; } = Array.Empty<GlazeButton>();
        public static float Height => MenuPage.Words(2) + MenuPage.Target() + 3f * MenuPage.TargetGap;
        public bool Visible => gameObject.activeSelf;

        public static WorkspaceChromeView Create(Transform parent)
        {
            var go = new GameObject("Halcyonic workspace frame");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<WorkspaceChromeView>();
            view.frame = Surface.Create(go.transform, "Connected frame", 16);
            view.heading = Surface.Create(go.transform, "Header", 19);
            view.rule = Surface.Create(go.transform, "Header rule", 20);
            view.product = GlazeText.Create(go.transform, "Halcyonic", GlazeType.Body, GlazeTokens.Text, TextAlignmentOptions.Left, 23, strong: true);
            view.connection = GlazeText.Create(go.transform, "Connection state", GlazeType.Body, GlazeTokens.TextSecondary, TextAlignmentOptions.Right, 23);
            view.context = GlazeText.Create(go.transform, "Project and task", GlazeType.Body, GlazeTokens.TextSecondary, TextAlignmentOptions.Left, 23);
            view.product.rectTransform.pivot = view.context.rectTransform.pivot = new Vector2(0f, 0.5f);
            view.connection.rectTransform.pivot = new Vector2(1f, 0.5f);
            view.move = GlazeButton.Create(go.transform, "Move workspace", ButtonRole.Navigation, order: 23);
            view.apps = GlazeButton.Create(go.transform, "Other apps", ButtonRole.Navigation, order: 23);
            view.minimize = GlazeButton.Create(go.transform, "Minimize workspace", ButtonRole.Navigation, order: 23);
            view.Targets = new[] { view.move, view.apps, view.minimize };
            foreach (var button in view.Targets) button.Accepting = () => view.Accepting();
            view.move.Holds = true;
            view.move.Target.EnableDrag();
            view.move.Pressed += () => view.MovePressed?.Invoke();
            view.move.Target.Selected += () => { if (view.Accepting()) view.PressStarted?.Invoke(); };
            view.move.HoldStarted += () => { if (view.Accepting() && view.move.Target.HeldPoint is Vector3 point) view.Held?.Invoke(point); };
            view.move.Target.Dragged += point => { if (view.move.Holding && view.Accepting()) view.Dragged?.Invoke(point); };
            view.move.Target.Released += _ => view.LetGo?.Invoke();
            view.apps.Pressed += () => view.OtherAppsPressed?.Invoke();
            view.minimize.Pressed += () => view.MinimizePressed?.Invoke();
            go.SetActive(false);
            return view;
        }

        public void Show(float width, float totalHeight, string connectionState, LineWords selection, bool appsAvailable)
        {
            gameObject.SetActive(true);
            var padding = GlazeTokens.Units(Glaze.Menu.PaddingDegrees);
            var left = -width / 2f + padding;
            var right = width / 2f - padding;
            frame.DrawGlass(new Vector2(width, totalHeight), radius: 0f);
            frame.transform.localPosition = new Vector3(0f, -(totalHeight - Height) / 2f, 0.001f);
            heading.DrawGlass(new Vector2(width, Height), radius: 0f);
            var top = Height / 2f - MenuPage.TargetGap;
            var line = MenuPage.Words(1);
            GlazeText.SetLiteral(product, WorkspaceText.Product);
            GlazeText.Lay(product, (right - left) / 2f, 1);
            product.transform.localPosition = new Vector3(left, top - line / 2f, -0.001f);
            GlazeText.SetLiteral(connection, connectionState);
            GlazeText.Lay(connection, (right - left) / 2f, 1);
            connection.transform.localPosition = new Vector3(right, top - line / 2f, -0.001f);
            var theirs = GlazeRuns.Of(context).Set(selection, _ => true);
            GlazeText.Lay(context, right - left, 1);
            context.transform.localPosition = new Vector3(left, top - 1.5f * line, -0.001f);
            GlazeRuns.TokenGrounds(context, theirs, grounds, transform, 21);
            var y = -Height / 2f + MenuPage.TargetGap + MenuPage.Target() / 2f;
            var gap = MenuPage.TargetGap;
            var buttonWidth = (right - left - 2f * gap) / 3f;
            move.Show(WorkspaceText.MoveWorkspace, new Vector2(left + buttonWidth / 2f, y), buttonWidth);
            apps.Available = appsAvailable;
            apps.Show(WorkspaceText.OtherApps, new Vector2(left + 1.5f * buttonWidth + gap, y), buttonWidth);
            minimize.Show(WorkspaceText.Minimize, new Vector2(right - buttonWidth / 2f, y), buttonWidth);
            rule.Draw(new Vector2(width, GlazeTokens.Units(0.08f)), 0f, GlazeTokens.ColorOf(Glaze.Palette.EdgeLight));
            rule.transform.localPosition = new Vector3(0f, -Height / 2f, -0.001f);
            foreach (var button in Targets) { button.EndPressSince(float.MinValue); button.Unsettle(); }
        }

        public void Hide()
        {
            foreach (var button in Targets) button.EndPressSince(float.MinValue);
            gameObject.SetActive(false);
        }
    }
}

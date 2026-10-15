#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>The connected workspace frame and its fixed global controls, independent of task decisions.</summary>
    public sealed class WorkspaceChromeView : MonoBehaviour
    {
        private Surface frame = null!;
        private Surface identityShoulder = null!;
        private Surface heading = null!;
        private Surface rule = null!;
        private TextMeshPro product = null!;
        private TextMeshPro connection = null!;
        private TextMeshPro context = null!;
        private TextMeshPro provenance = null!;
        private StateBadgeView status = null!;
        private readonly List<Surface> grounds = new List<Surface>();
        private GlazeButton move = null!;
        private GlazeButton apps = null!;
        private GlazeButton minimize = null!;
        private GlazeButton projects = null!;
        private GlazeButton tasks = null!, usage = null!, settings = null!;
        public event Action<MenuPlace>? PlacePressed;
        public static float SpatialTop => MenuPage.Target() + 2f * MenuPage.TargetGap;
        public static float SpatialBase => MenuPage.Target() + MenuPage.Words(1) + 3f * MenuPage.TargetGap;
        public static float SpatialRail => PlaneComposition.Units(12f);
        public Func<bool> Accepting { get; set; } = () => false;
        public event Action? OtherAppsPressed;
        public event Action? MinimizePressed;
        public event Action? ProjectsPressed;
        public event Action? MovePressed;
        public event Action? PressStarted;
        public event Action<Vector3>? Held;
        public event Action<Vector3>? Dragged;
        public event Action? LetGo;
        public IReadOnlyList<GlazeButton> Targets { get; private set; } = Array.Empty<GlazeButton>();
        public static float Height => MenuPage.Words(1) + 2f * MenuPage.Target() + 4f * MenuPage.TargetGap;
        public bool Visible => gameObject.activeSelf;
        public Vector2 Size { get; private set; }
        public Surface FrameSurface => frame;

        public static WorkspaceChromeView Create(Transform parent)
        {
            var go = new GameObject("Halcyonic workspace frame");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<WorkspaceChromeView>();
            view.frame = Surface.Create(go.transform, "Connected frame", 16);
            view.identityShoulder = Surface.Create(go.transform, "Work identity shoulder", 19);
            view.heading = Surface.Create(go.transform, "Header", 19);
            view.rule = Surface.Create(go.transform, "Header rule", 20);
            view.product = GlazeText.Create(go.transform, "Halcyonic", GlazeType.Body, GlazeTokens.Text, TextAlignmentOptions.Left, 23, strong: true);
            view.connection = GlazeText.Create(go.transform, "Connection state", GlazeType.Body, GlazeTokens.TextSecondary, TextAlignmentOptions.Right, 23);
            view.context = GlazeText.Create(go.transform, "Project and task", GlazeType.Body, GlazeTokens.TextSecondary, TextAlignmentOptions.Left, 23);
            view.status = StateBadgeView.Create(go.transform, "Selected task state", 22, pill: true);
            view.provenance = GlazeText.Create(go.transform, "Selected task provenance", GlazeType.Body, GlazeTokens.TextSecondary, TextAlignmentOptions.Right, 23);
            view.provenance.rectTransform.pivot = new Vector2(1f, 0.5f);
            view.product.rectTransform.pivot = view.context.rectTransform.pivot = new Vector2(0f, 0.5f);
            view.connection.rectTransform.pivot = new Vector2(1f, 0.5f);
            view.move = GlazeButton.Create(go.transform, "Move workspace", ButtonRole.Navigation, order: 23);
            view.apps = GlazeButton.Create(go.transform, "Other apps", ButtonRole.Navigation, order: 23);
            view.minimize = GlazeButton.Create(go.transform, "Minimize workspace", ButtonRole.Navigation, order: 23);
            view.projects = GlazeButton.Create(go.transform, "Projects navigation", ButtonRole.Navigation, order: 23);
            view.tasks = GlazeButton.Create(go.transform, "Tasks navigation", ButtonRole.Navigation, order: 23);
            view.usage = GlazeButton.Create(go.transform, "Usage navigation", ButtonRole.Navigation, order: 23);
            view.settings = GlazeButton.Create(go.transform, "Settings navigation", ButtonRole.Navigation, order: 23);
            view.Targets = new[] { view.move, view.apps, view.minimize, view.projects, view.tasks, view.usage, view.settings };
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
            view.projects.Pressed += () => view.ProjectsPressed?.Invoke();
            view.tasks.Pressed += () => view.PlacePressed?.Invoke(MenuPlace.Tasks);
            view.usage.Pressed += () => view.PlacePressed?.Invoke(MenuPlace.Usage);
            view.settings.Pressed += () => view.PlacePressed?.Invoke(MenuPlace.Settings);
            go.SetActive(false);
            return view;
        }

        public void Show(float width, float totalHeight, string connectionState, LineWords selection, bool appsAvailable, StateBadge? selectedState, IReadOnlyList<WorkMark> marks, bool preserveMovePress = false)
        {
            foreach (var button in Targets) { button.Role = ButtonRole.Navigation; button.Label.alignment = TextAlignmentOptions.Center; button.Label.color = GlazeTokens.Text; }
            identityShoulder.gameObject.SetActive(false);
            product.gameObject.SetActive(true);
            context.gameObject.SetActive(true);
            tasks.gameObject.SetActive(false); usage.gameObject.SetActive(false); settings.gameObject.SetActive(false);
            preserveMovePress &= gameObject.activeSelf && connection.text == connectionState;
            gameObject.SetActive(true);
            var padding = GlazeTokens.Units(Glaze.Menu.PaddingDegrees);
            var left = -width / 2f + padding;
            var right = width / 2f - padding;
            Size = new Vector2(width, Height);
            frame.Draw(new Vector2(width, totalHeight), 0f, Color.clear,
                GlazeTokens.ColorOf(Glaze.Palette.EdgeLight, Glaze.Menu.HairlineOpacity), GlazeTokens.Units(Glaze.Menu.HairlineDegrees));
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
            var contextY = top - line - MenuPage.TargetGap - MenuPage.Target() / 2f;
            GlazeText.SetLiteral(projects.Label, "Projects");
            var projectsWidth = Mathf.Max(MenuPage.Target(), projects.Label.GetPreferredValues().x + 2f * MenuPage.TargetGap);
            projects.Show("Projects", new Vector2(left + projectsWidth / 2f, contextY), projectsWidth);
            var contextLeft = left + projectsWidth + MenuPage.TargetGap;
            var contextRight = right;
            provenance.gameObject.SetActive(marks.Count > 0);
            if (marks.Count > 0)
            {
                GlazeText.SetLiteral(provenance, string.Join(" · ", marks.Select(mark => mark.Word)));
                var needed = provenance.GetPreferredValues().x;
                GlazeText.Lay(provenance, needed, 1);
                provenance.transform.localPosition = new Vector3(right, contextY, -0.001f);
                contextRight -= needed + MenuPage.TargetGap;
            }
            status.gameObject.SetActive(selectedState != null);
            if (selectedState != null)
            {
                status.Show(selectedState);
                status.transform.localPosition = new Vector3(contextRight - status.Width / 2f, contextY, -0.001f);
                contextRight -= status.Width + MenuPage.TargetGap;
            }
            var contextWords = selection.Text == "Projects" ? LineWords.None : selection;
            var theirs = GlazeRuns.Of(context).Set(contextWords, tried => context.GetPreferredValues(tried).x <= Mathf.Max(0f, contextRight - contextLeft));
            GlazeText.Lay(context, Mathf.Max(0f, contextRight - contextLeft), 1);
            context.transform.localPosition = new Vector3(contextLeft, contextY, -0.001f);
            GlazeRuns.TokenGrounds(context, theirs, grounds, transform, 21);
            var y = -Height / 2f + MenuPage.TargetGap + MenuPage.Target() / 2f;
            var gap = MenuPage.TargetGap;
            var buttonWidth = (right - left - 2f * gap) / 3f;
            move.Show(WorkspaceText.MoveWorkspace, new Vector2(left + buttonWidth / 2f, y), buttonWidth);
            apps.Available = appsAvailable;
            apps.Show(WorkspaceText.OtherApps, new Vector2(left + 1.5f * buttonWidth + gap, y), buttonWidth);
            minimize.Show(WorkspaceText.Minimize, new Vector2(right - buttonWidth / 2f, y), buttonWidth);
            rule.Selection = SurfaceSelection.CompositionDivider;
            rule.Draw(new Vector2(width, GlazeTokens.Units(0.08f)), 0f, GlazeTokens.ColorOf(Glaze.Palette.EdgeLight));
            rule.transform.localPosition = new Vector3(0f, -Height / 2f, -0.001f);
            // A stable redraw must not restart a deliberate Move hold. The plane proves the
            // same frame, owner and physical hit area; all other controls require a fresh press.
            foreach (var button in Targets)
                if (button != move || !preserveMovePress) { button.EndPressSince(float.MinValue); button.Unsettle(); }
        }

        public void ShowSpatial(float width, float totalHeight, float contentLeft, float contentWidth, string connectionState,
            LineWords selection, bool appsAvailable, IReadOnlyList<WorkMark> marks, MenuPlace place, bool preserveMovePress = false, bool focusedWork = false)
        {
            preserveMovePress &= gameObject.activeSelf && connection.text == connectionState;
            gameObject.SetActive(true);
            Size = new Vector2(width, totalHeight);
            frame.gameObject.SetActive(true);
            frame.Draw(new Vector2(width, totalHeight), 0f, Color.clear);
            frame.transform.localPosition = Vector3.zero;
            heading.gameObject.SetActive(false); rule.gameObject.SetActive(false);
            status.gameObject.SetActive(false);
            var gap = MenuPage.TargetGap;
            var top = totalHeight / 2f;
            var left = -width / 2f;
            var labelY = top - MenuPage.Words(1) / 2f;
            product.gameObject.SetActive(!focusedWork);
            GlazeText.SetLiteral(product, WorkspaceText.Product);
            GlazeText.Lay(product, SpatialRail, 1);
            product.transform.localPosition = new Vector3(left, labelY, -0.001f);
            GlazeText.SetLiteral(connection, connectionState);
            GlazeText.Lay(connection, contentWidth / 2f, 1);
            connection.transform.localPosition = new Vector3(contentLeft + contentWidth, labelY, -0.001f);
            var contextStart = focusedWork ? left + SpatialRail + gap : contentLeft;
            var contextRoom = focusedWork ? Mathf.Max(0f, width - SpatialRail - gap - contentWidth * 0.28f) : contentWidth * 0.62f;
            var hasIdentity = selection.Text != "Projects" && !string.IsNullOrWhiteSpace(selection.Text);
            context.gameObject.SetActive(hasIdentity);
            identityShoulder.gameObject.SetActive(false);
            if (hasIdentity)
            {
                GlazeRuns.Of(context).Set(selection, tried => context.GetPreferredValues(tried).x <= contextRoom - 2f * gap);
                GlazeText.Lay(context, contextRoom - 2f * gap, 1);
                context.transform.localPosition = new Vector3(contextStart + gap, labelY, -0.001f);
            }
            provenance.gameObject.SetActive(marks.Count > 0);
            if (marks.Count > 0)
            {
                GlazeText.SetLiteral(provenance, string.Join(" · ", marks.Select(mark => mark.Word)));
                GlazeText.Lay(provenance, SpatialRail, 1);
                provenance.transform.localPosition = new Vector3(contentLeft + contentWidth, -totalHeight / 2f + MenuPage.Words(1) / 2f, -0.001f);
            }
            var entries = new[] { (projects, MenuPlace.Projects, "Projects", GlazeIcon.Folder), (tasks, MenuPlace.Tasks, "Tasks", GlazeIcon.ShowAll), (usage, MenuPlace.Usage, "Usage", GlazeIcon.UsageLeft), (settings, MenuPlace.Settings, "Settings", GlazeIcon.Settings) };
            var y = top - SpatialTop - MenuPage.Target() / 2f;
            foreach (var (button, destination, words, icon) in entries)
            {
                var visible = !focusedWork || destination == MenuPlace.Projects;
                button.gameObject.SetActive(visible);
                if (!visible) continue;
                var at = focusedWork ? top - MenuPage.Target() / 2f : y;
                button.ShowPrompt(words, icon, new Vector2(left + SpatialRail / 2f, at), SpatialRail, !focusedWork && destination == place);
                if (!button.On) button.Label.color = GlazeTokens.TextSecondary;
                y -= MenuPage.Target() + gap;
            }
            move.Role = apps.Role = minimize.Role = ButtonRole.Prompt;
            var buttonWidth = (contentWidth - 2f * gap) / 3f;
            var baseY = -totalHeight / 2f + SpatialBase - gap - MenuPage.Target() / 2f;
            move.Show(WorkspaceText.MoveWorkspace, new Vector2(contentLeft + buttonWidth / 2f, baseY), buttonWidth);
            apps.Available = appsAvailable;
            apps.Show(WorkspaceText.OtherApps, new Vector2(contentLeft + contentWidth / 2f, baseY), buttonWidth);
            minimize.Show(WorkspaceText.Minimize, new Vector2(contentLeft + contentWidth - buttonWidth / 2f, baseY), buttonWidth);
            move.Label.color = apps.Label.color = minimize.Label.color = GlazeTokens.TextSecondary;
            foreach (var button in Targets)
                if (button != move || !preserveMovePress) { button.EndPressSince(float.MinValue); button.Unsettle(); }
        }

        public void Hide()
        {
            foreach (var button in Targets) button.EndPressSince(float.MinValue);
            gameObject.SetActive(false);
        }
    }
}

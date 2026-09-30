#nullable enable
using System;
using Halcyonic.Client;
using Halcyonic.Contracts;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The workspace's details, chosen with tabs under its actions: Activity, the requests and recent
    /// activity the panel shows; Understanding, what the understanding source concluded about the
    /// execution; and Evaluation, what the evaluation source measured about it. The tabs are the
    /// workspace's buttons, pointed at and pinched, or poked, and ignore input while the app lacks
    /// focus. A section reads when it is shown for an execution it holds nothing about, and again
    /// when the person presses Refresh. Understanding also follows the execution as it changes, at
    /// most every two seconds; Evaluation, whose reads spend the evaluation source's budget, never
    /// reads by itself again. Reads go to the control plane, or to the recorded demonstration while
    /// it plays, through <see cref="IIntelligenceReader"/>; the client core writes every word. Acting
    /// from the workspace returns the details to Activity, where the request's result shows.
    /// </summary>
    public sealed class WorkspaceSections : MonoBehaviour
    {
        private const float TabGap = 0.016f;
        private const float FollowSeconds = 2f;

        /// <summary>How often a section is written again anyway, since "2 minutes ago" and staleness move with time.</summary>
        private const float RedrawSeconds = 1f;

        /// <summary>Lines under the provenance that fit the details area (<see cref="SectionView"/>).</summary>
        public const int UnderstandingLines = 7;

        private static readonly TimeZoneInfo Zone = LocalZone();

        private WorkspacePanel panel = null!;
        private Func<WorkspacePresentation?> presentation = () => null;
        private Func<IIntelligenceReader?> reader = () => null;
        private PanelButton activityTab = null!;
        private PanelButton understandingTab = null!;
        private PanelButton evaluationTab = null!;
        private PanelButton refresh = null!;
        private SpriteRenderer chosenMark = null!;
        private SectionView view = null!;
        private IntelligenceFeed<UnderstandingResponse> understanding = null!;
        private IntelligenceFeed<EvaluationResponse> evaluation = null!;
        private SectionKind? shown;
        private int drawn = -1;
        private float redrawAt;
        private bool fixedContent;

        /// <summary>The section shown, or null while the details show the activity.</summary>
        public SectionKind? Shown => shown;

        /// <summary>
        /// Adds the tabs and sections to a workspace panel. <paramref name="presentation"/> is the open
        /// workstream as last presented; <paramref name="reader"/> is where to read, null when there is
        /// nowhere to, as without a control plane or demonstration.
        /// </summary>
        public static WorkspaceSections Attach(WorkspacePanel panel, Func<WorkspacePresentation?> presentation, Func<IIntelligenceReader?> reader)
        {
            var sections = panel.gameObject.AddComponent<WorkspaceSections>();
            sections.panel = panel;
            sections.presentation = presentation;
            sections.reader = reader;
            sections.Build();
            return sections;
        }

        /// <summary>Shows the requests and recent activity under the tabs.</summary>
        public void ShowActivity() => Choose(null);

        /// <summary>Shows a section, reading it if it holds nothing about the execution yet.</summary>
        public void Show(SectionKind kind) => Choose(kind);

        /// <summary>Shows a section with content given as it is, reading nothing: for the editor's renders.</summary>
        public void ShowFixed(SectionPresentation section)
        {
            fixedContent = true;
            Choose(section.Kind);
            view.Show(section);
        }

        /// <summary>Every label the sections draw, for the editor's check that none interprets what it shows.</summary>
        public SectionView View => view;

        private void Build()
        {
            understanding = new IntelligenceFeed<UnderstandingResponse>((id, cancel) => Reader().ReadUnderstandingAsync(id, cancel));
            evaluation = new IntelligenceFeed<EvaluationResponse>((id, cancel) => Reader().ReadEvaluationAsync(id, cancel));
            activityTab = Tab("Activity tab", () => Choose(null));
            understandingTab = Tab("Understanding tab", () => Choose(SectionKind.Understanding));
            evaluationTab = Tab("Evaluation tab", () => Choose(SectionKind.Evaluation));
            refresh = Tab("Refresh", Refresh);
            // A shape as well as a color marks the tab chosen.
            chosenMark = WorkspaceVisuals.Plate(transform, "Chosen tab", new Vector2(0.1f, 0.004f), WorkspaceVisuals.TextColor, WorkspaceVisuals.PanelControlOrder);
            view = SectionView.Create(transform);
            panel.ActionPressed += _ => Choose(null);
            panel.ConfirmPressed += () => Choose(null);
            panel.PresetPressed += _ => Choose(null);
            Choose(null);
        }

        private IIntelligenceReader Reader() =>
            reader() ?? throw new ControlPlaneRequestException("There is no control plane or demonstration to ask.");

        private PanelButton Tab(string name, Action pressed)
        {
            var button = PanelButton.Create(transform, name, WorkspacePanel.TabsHeight, WorkspaceVisuals.DetailSize);
            button.Accepting = () => panel.Accepting();
            button.Pressed += pressed;
            return button;
        }

        private void Choose(SectionKind? kind)
        {
            shown = kind;
            panel.ShowActivity(kind == null);
            view.gameObject.SetActive(kind != null);
            drawn = -1;
            var x = WorkspacePanel.DetailsLeft;
            var center = WorkspacePanel.TabsTop - WorkspacePanel.TabsHeight / 2f;
            foreach (var (button, label, tab) in new[]
            {
                (activityTab, "Activity", (SectionKind?)null),
                (understandingTab, IntelligenceText.UnderstandingTitle, SectionKind.Understanding),
                (evaluationTab, IntelligenceText.EvaluationTitle, SectionKind.Evaluation),
            })
            {
                var width = button.Measure(label, 0.12f);
                button.Show(label, new Vector2(x + width / 2f, center), width);
                if (tab == kind)
                {
                    chosenMark.size = new Vector2(width - 0.02f, 0.004f);
                    chosenMark.transform.localPosition = new Vector3(x + width / 2f, WorkspacePanel.TabsTop - WorkspacePanel.TabsHeight - 0.005f, -0.004f);
                }
                x += width + TabGap;
            }
            if (kind == null)
            {
                refresh.Hide();
                return;
            }
            var refreshWidth = refresh.Measure("Refresh", 0.12f);
            refresh.Show("Refresh", new Vector2(WorkspacePanel.DetailsLeft + WorkspacePanel.DetailsWidth - refreshWidth / 2f, center), refreshWidth);
        }

        private void Refresh()
        {
            var execution = presentation()?.Execution;
            var now = DateTimeOffset.UtcNow;
            if (shown == SectionKind.Understanding) understanding.Refresh(execution?.UpdatedAt, now);
            else if (shown == SectionKind.Evaluation) evaluation.Refresh(execution?.UpdatedAt, now);
        }

        private void Update()
        {
            if (fixedContent) return;
            var now = DateTimeOffset.UtcNow;
            var execution = presentation()?.Execution;
            if (shown == SectionKind.Understanding)
            {
                understanding.Show(execution?.ExecutionId, execution?.UpdatedAt, now, TimeSpan.FromSeconds(FollowSeconds));
            }
            else if (shown == SectionKind.Evaluation)
            {
                evaluation.Show(execution?.ExecutionId, execution?.UpdatedAt, now);
            }
            understanding.Poll();
            evaluation.Poll();
            if (shown is not SectionKind kind) return;
            var version = kind == SectionKind.Understanding ? understanding.Version : evaluation.Version;
            if (version == drawn && Time.unscaledTime < redrawAt) return;
            drawn = version;
            redrawAt = Time.unscaledTime + RedrawSeconds;
            view.Show(kind == SectionKind.Understanding
                ? UnderstandingPresenter.Present(understanding, now, Zone, UnderstandingLines)
                : EvaluationPresenter.Present(evaluation, now, Zone));
        }

        private void OnDestroy()
        {
            understanding?.Clear();
            evaluation?.Clear();
        }

        private static TimeZoneInfo LocalZone()
        {
            try
            {
                return TimeZoneInfo.Local;
            }
            catch (Exception)
            {
                return TimeZoneInfo.Utc;
            }
        }
    }
}

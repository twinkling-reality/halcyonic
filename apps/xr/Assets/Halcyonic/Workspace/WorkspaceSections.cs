#nullable enable
using System;
using Halcyonic.Client;
using Halcyonic.Contracts;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// What the workspace's two sections read for the tab chosen: Help me understand, what the
    /// understanding source concluded about the execution, and What was checked?, what the evaluation
    /// source measured about it. The source's name stays in the answer's provenance line, never on a
    /// tab. A section reads when it is shown for an execution it holds nothing about, and again when
    /// the person presses Refresh. Understanding also follows the execution as it changes, at most
    /// every two seconds; Evaluation, whose reads spend the evaluation source's budget, never reads by
    /// itself again. Reads go to the control plane, or to the recorded demonstration while it plays,
    /// through <see cref="IIntelligenceReader"/>; the client core writes every word.
    /// </summary>
    public sealed class WorkspaceSections : MonoBehaviour
    {
        private const float FollowSeconds = 2f;

        /// <summary>Lines under the provenance that fit the space under a section's heading (<see cref="SectionView"/>).</summary>
        public const int UnderstandingLines = 6;

        private static readonly TimeZoneInfo Zone = LocalZone();

        private Func<WorkspacePresentation?> presentation = () => null;
        private Func<IIntelligenceReader?> reader = () => null;
        private IntelligenceFeed<UnderstandingResponse> understanding = null!;
        private IntelligenceFeed<EvaluationResponse> evaluation = null!;
        private WorkspaceQuestion question;
        private SectionPresentation? fixedSection;

        /// <summary>
        /// Reads for a workspace. <paramref name="presentation"/> is the open workstream as last
        /// presented; <paramref name="reader"/> is where to read, null when there is nowhere to, as
        /// without a control plane or demonstration.
        /// </summary>
        public static WorkspaceSections Attach(GameObject host, Func<WorkspacePresentation?> presentation, Func<IIntelligenceReader?> reader)
        {
            var sections = host.AddComponent<WorkspaceSections>();
            sections.presentation = presentation;
            sections.reader = reader;
            sections.understanding = new IntelligenceFeed<UnderstandingResponse>((id, cancel) => sections.Reader().ReadUnderstandingAsync(id, cancel));
            sections.evaluation = new IntelligenceFeed<EvaluationResponse>((id, cancel) => sections.Reader().ReadEvaluationAsync(id, cancel));
            return sections;
        }

        /// <summary>The tab chosen, whose section reads.</summary>
        public WorkspaceQuestion Question => question;

        /// <summary>Changes whenever a read's answer changes, so the workspace knows to draw it again.</summary>
        public int Version => understanding.Version * 397 + evaluation.Version;

        /// <summary>Reads for the tab chosen from now on.</summary>
        public void Show(WorkspaceQuestion shown) => question = shown;

        /// <summary>A section given as it is, read from nowhere: for the editor's renders.</summary>
        public void ShowFixed(SectionPresentation section)
        {
            fixedSection = section;
            question = section.Kind == SectionKind.Understanding ? WorkspaceQuestion.Understand : WorkspaceQuestion.Checked;
        }

        /// <summary>The chosen section as it reads now, or null while the tab chosen is not a section.</summary>
        public SectionPresentation? Section
        {
            get
            {
                if (fixedSection != null) return fixedSection;
                var now = DateTimeOffset.UtcNow;
                return question switch
                {
                    WorkspaceQuestion.Understand => UnderstandingPresenter.Present(understanding, now, Zone, UnderstandingLines),
                    WorkspaceQuestion.Checked => EvaluationPresenter.Present(evaluation, now, Zone),
                    _ => null,
                };
            }
        }

        /// <summary>Reads the chosen section again, as Refresh asks.</summary>
        public void Refresh()
        {
            var execution = presentation()?.Execution;
            var now = DateTimeOffset.UtcNow;
            if (question == WorkspaceQuestion.Understand) understanding.Refresh(execution?.UpdatedAt, now);
            else if (question == WorkspaceQuestion.Checked) evaluation.Refresh(execution?.UpdatedAt, now);
        }

        private IIntelligenceReader Reader() =>
            reader() ?? throw new ControlPlaneRequestException("There is no control plane or demonstration to ask.");

        private void Update()
        {
            if (fixedSection != null) return;
            var now = DateTimeOffset.UtcNow;
            var execution = presentation()?.Execution;
            if (question == WorkspaceQuestion.Understand)
            {
                understanding.Show(execution?.ExecutionId, execution?.UpdatedAt, now, TimeSpan.FromSeconds(FollowSeconds));
            }
            else if (question == WorkspaceQuestion.Checked)
            {
                evaluation.Show(execution?.ExecutionId, execution?.UpdatedAt, now);
            }
            understanding.Poll();
            evaluation.Poll();
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

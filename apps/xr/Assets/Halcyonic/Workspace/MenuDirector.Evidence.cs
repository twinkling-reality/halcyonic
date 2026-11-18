#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The evidence view in the room (plan v2, AUDIT's 9; the lead's go at 02:4xZ): the project screen's Outcome and Reasoning doors
    /// open <see cref="EvidenceFlowView"/> around the person, Seorak's strip over Salidium's places, for the project's newest task and
    /// its run, read live through that task's own file (its understanding and evaluation feeds), never the recorded run. The ask is
    /// the run's own instruction from the projection. The same door again, Close, or the screen closing puts it away. Presentation only.
    /// </summary>
    public sealed partial class MenuDirector
    {
        private EvidenceFlowView? evidenceFlow;
        private string? evidenceWork;
        private (int Understanding, int Evaluation, string? Execution) evidenceShown = (-1, -1, null);

        /// <summary>Whether the evidence view stands now, and for which task.</summary>
        public string? EvidenceWorkstream => evidenceFlow != null && evidenceFlow.gameObject.activeSelf ? evidenceWork : null;

        /// <summary>The evidence view as drawn, for checks and renders; null before it first opens.</summary>
        public EvidenceFlowView? EvidenceFlow => evidenceFlow;

        /// <summary>Opens the evidence view for <paramref name="work"/> around where the person stands and looks, or puts it away when it already stands for that task.</summary>
        private void ToggleEvidence(WorkstreamView work)
        {
            if (EvidenceWorkstream == work.WorkstreamId)
            {
                CloseEvidence();
                return;
            }
            // The task's file reads its run's understanding and evaluation while the view stands (FileColumn.EvidenceFlowShown).
            OpenFile(work.WorkstreamId, FileSection.Activity);
            var stage = setup.StageNow();
            if (evidenceFlow == null)
            {
                var holder = new GameObject("Evidence").transform;
                holder.SetParent(transform, false);
                evidenceFlow = EvidenceFlowView.Create(holder);
                // The person's chosen stats, kept on this headset (FIELD's EvidenceChoice), seeded once and kept on each pick.
                chosenStats ??= EvidenceChoice.Load(preferences);
                evidenceFlow.Chose += choice =>
                {
                    EvidenceChoice.Save(preferences, choice);
                    ChosenStats = choice;
                    TickEvidence();
                };
            }
            var looking = new Vector3(stage.Looking.x, 0f, stage.Looking.z);
            evidenceFlow.transform.parent.SetPositionAndRotation(stage.Eyes, Quaternion.LookRotation(looking.sqrMagnitude > 1e-6f ? looking.normalized : Vector3.forward));
            evidenceFlow.gameObject.SetActive(true);
            // The room makes way: the task's file the door opened (it reads the run for the view) and the rest of the plane stand
            // hidden while the view is open, so nothing of the room stands in its clear middle (AUDIT's 03:19Z); Close brings it back.
            plane.transform.localScale = Vector3.one * 1e-4f;
            evidenceWork = work.WorkstreamId;
            evidenceShown = (-1, -1, null);
            TickEvidence();
            dirty = true;
        }

        private void CloseEvidence()
        {
            if (evidenceFlow != null) evidenceFlow.gameObject.SetActive(false);
            if (evidenceWork != null) plane.transform.localScale = Vector3.one;
            evidenceWork = null;
        }

        /// <summary>Each frame: the view drawn again only when a feed read something new, so it grows with the reads and never on a timer.</summary>
        private void TickEvidence()
        {
            if (evidenceFlow == null || EvidenceWorkstream is not string workstreamId) return;
            // It stands until its own door, Close, or its task going: the door opens the task's file, which moves the shown screen on, so
            // the view never hangs on a screen being shown (SCREENS' 03:1xZ finding: it closed in the frame it opened).
            if (State?.Workstreams.TryGetValue(workstreamId, out var work) != true)
            {
                CloseEvidence();
                return;
            }
            var file = navigator.Beside is FileColumn open && open.Now?.Character.WorkstreamId == workstreamId ? open : null;
            if (file == null) return;
            file.EvidenceFlowShown = () => EvidenceWorkstream == workstreamId;
            var execution = work.CurrentExecutionId is string id && State.Executions.TryGetValue(id, out var run) ? run : null;
            var versions = (file.UnderstandingVersion, file.EvaluationVersion, execution?.ExecutionId + "|" + execution?.ClosingWords?.At + "|" + execution?.Status);
            if (versions == evidenceShown) return;
            evidenceShown = versions;
            // The story anyone can follow (the owner's 2026-11-18 direction), from the run's own reads.
            // The agent's own plain sentences, asked of it at the run's end (CORE's closing words), said as the agent's on the face.
            var closing = execution?.ClosingWords is ClosingWordsView words ? new EvidenceStory.ClosingWords(words.Did, words.Why, words.How, words.Left) : null;
            var story = EvidenceStory.Of(execution?.Instruction, file.Understanding, closing);
            var strip = EvidenceStrip.Of((file.Evaluation?.Result as AvailableEvaluation)?.Evaluation);
            evidenceFlow.Tell(story, strip?.Shown(ChosenStats) ?? new EvidenceStrip.Stat[0], strip?.Notes ?? new string[0], strip, ChosenStats, StatusWords(execution?.Status));
        }

        /// <summary>The run's state as the story's status pill says it, plain; null where it is not known, and no pill stands.</summary>
        private static string? StatusWords(ExecutionStatus? status) => status switch
        {
            ExecutionStatus.Completed => EvidenceStory.Words.Done,
            ExecutionStatus.Running => EvidenceStory.Words.Working,
            ExecutionStatus.Verifying => EvidenceStory.Words.Checking,
            ExecutionStatus.WaitingForHuman => EvidenceStory.Words.NeedsYou,
            ExecutionStatus.Starting => EvidenceStory.Words.Starting,
            ExecutionStatus.Failed or ExecutionStatus.Interrupted => EvidenceStory.Words.Stopped,
            ExecutionStatus.Unknown or null => null,
            _ => null,
        };

        /// <summary>The person's chosen strip stats in their order; null until they choose (FIELD's Choose sets it). Setting it redraws the strip.</summary>
        public IReadOnlyList<string>? ChosenStats
        {
            get => chosenStats;
            set
            {
                chosenStats = value;
                evidenceShown = (-1, -1, null);
            }
        }

        private IReadOnlyList<string>? chosenStats;
        private readonly IDevicePreferences preferences = new PlayerPreferences();

        /// <summary>
        /// Presses <paramref name="action"/> on <paramref name="projectId"/>'s screen exactly as its bar does (the Reasoning door
        /// KitScreenAction.Why, the Outcome door KitScreenAction.HowItWent, or Close): the one way a journey opens the evidence view.
        /// </summary>
        public void PressScreenDoor(KitScreenAction action, string projectId) => OnScreenActed(action, projectId);
    }
}

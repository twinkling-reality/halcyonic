#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>The local workspace presentation, shared by the menu and optional spatial agents.</summary>
    public sealed class WorkspacePresentationState
    {
        private bool changing;
        private readonly HashSet<string> dismissedMomentSignatures = new HashSet<string>(StringComparer.Ordinal);
        private string? inspectedWaitingSignature;

        public WorkspacePresentationState(bool keepAgentsWhenMinimized = false)
        {
            KeepAgentsWhenMinimized = keepAgentsWhenMinimized;
        }

        public bool Minimized { get; private set; }
        public bool AgentsEnabled { get; private set; }

        /// <summary>
        /// Whether placed ambient characters stay visible on their shelf when the console is minimized
        /// (Rule 18, DESIGN-SETTLED.md).
        /// </summary>
        public bool KeepAgentsWhenMinimized { get; set; }

        /// <summary>
        /// Whether contextual moment cards automatically surface beside placed characters when a workstream
        /// reaches <see cref="AttentionLevel.ActionRequired"/> while the console is minimized or a project screen is open.
        /// </summary>
        public bool ContextualMomentsEnabled { get; set; }

        public bool AgentsVisible => AgentsEnabled && (!Minimized || KeepAgentsWhenMinimized);

        /// <summary>The workstream whose contextual moment card was explicitly inspected beside its avatar, if any.</summary>
        public string? InspectedWorkstreamId { get; private set; }

        /// <summary>The project whose on-demand screen and command bar are open in the room, if any.</summary>
        public string? ScreenProjectId { get; private set; }

        /// <summary>Whether the on-demand project screen's command bar is open rather than folded to its handle.</summary>
        public bool ScreenBarOpen { get; private set; }

        /// <summary>Revokes retained input and command authority before any presentation disappears.</summary>
        public event Action? Suspending;
        public event Action? Changed;

        public bool Minimize()
        {
            if (Minimized || changing) return false;
            changing = true;
            try
            {
                Suspending?.Invoke();
                Minimized = true;
                Changed?.Invoke();
                return true;
            }
            finally { changing = false; }
        }

        public bool Restore()
        {
            if (!Minimized || changing) return false;
            changing = true;
            try { Minimized = false; Changed?.Invoke(); return true; }
            finally { changing = false; }
        }

        public bool SetAgentsEnabled(bool enabled)
        {
            if (AgentsEnabled == enabled || changing) return false;
            changing = true;
            try
            {
                AgentsEnabled = enabled;
                if (!enabled)
                {
                    InspectedWorkstreamId = null;
                    inspectedWaitingSignature = null;
                    dismissedMomentSignatures.Clear();
                }
                Changed?.Invoke();
                return true;
            }
            finally { changing = false; }
        }

        public bool InspectMoment(string workstreamId)
        {
            if (string.IsNullOrEmpty(workstreamId) || changing) return false;
            var hadDismissed = dismissedMomentSignatures.Any(sig => sig.StartsWith(workstreamId + ":", StringComparison.Ordinal));
            if (InspectedWorkstreamId == workstreamId && !hadDismissed) return false;
            changing = true;
            try
            {
                InspectedWorkstreamId = workstreamId;
                inspectedWaitingSignature = null;
                dismissedMomentSignatures.RemoveWhere(sig => sig.StartsWith(workstreamId + ":", StringComparison.Ordinal));
                Changed?.Invoke();
                return true;
            }
            finally { changing = false; }
        }

        public bool CloseMoment(ClientProjection? state = null)
        {
            if (changing) return false;
            var currentSig = CurrentWaitingSignature(state);
            if (InspectedWorkstreamId == null && currentSig == null) return false;
            changing = true;
            try
            {
                InspectedWorkstreamId = null;
                inspectedWaitingSignature = null;
                if (currentSig != null) dismissedMomentSignatures.Add(currentSig);
                Changed?.Invoke();
                return true;
            }
            finally { changing = false; }
        }

        public bool ShowProjectScreen(string projectId, bool barOpen = false)
        {
            if (string.IsNullOrEmpty(projectId) || changing) return false;
            if (ScreenProjectId == projectId && ScreenBarOpen == barOpen) return false;
            changing = true;
            try
            {
                ScreenProjectId = projectId;
                ScreenBarOpen = barOpen;
                Changed?.Invoke();
                return true;
            }
            finally { changing = false; }
        }

        public bool SetScreenBarOpen(bool open)
        {
            if (ScreenProjectId == null || ScreenBarOpen == open || changing) return false;
            changing = true;
            try
            {
                ScreenBarOpen = open;
                Changed?.Invoke();
                return true;
            }
            finally { changing = false; }
        }

        public bool CloseProjectScreen()
        {
            if (ScreenProjectId == null || changing) return false;
            changing = true;
            try
            {
                ScreenProjectId = null;
                ScreenBarOpen = false;
                Changed?.Invoke();
                return true;
            }
            finally { changing = false; }
        }

        /// <summary>
        /// The active contextual moment card to stand beside a visible character: opened when a character's
        /// workstream reaches <see cref="AttentionLevel.ActionRequired"/> or is explicitly inspected, and
        /// collapsing to null once answered or closed.
        /// </summary>
        public (string WorkstreamId, KitMomentSlot Slot)? ActiveMoment(
            ClientProjection? state,
            Func<string, bool>? characterVisible = null)
        {
            if (!AgentsVisible || state == null) return null;

            if (InspectedWorkstreamId is string inspected)
            {
                if (!state.Workstreams.TryGetValue(inspected, out var inspectedWork)
                    || (characterVisible != null && !characterVisible(inspected)))
                {
                    return null;
                }
                var waitingSig = WaitingSignature(inspectedWork, state);
                if (inspectedWaitingSignature == null && waitingSig != null)
                {
                    inspectedWaitingSignature = waitingSig;
                }
                else if (inspectedWaitingSignature != null && waitingSig == null)
                {
                    // A pending question or approval that was open when inspected has been answered: collapse back to the avatar.
                    InspectedWorkstreamId = null;
                    inspectedWaitingSignature = null;
                    return null;
                }
                var inspectedSlot = MomentSlotOf(inspectedWork, state, includeIdleFallback: true);
                return inspectedSlot != null ? (inspected, inspectedSlot) : null;
            }

            foreach (var work in OrderWorkstreams(state.Workstreams.Values))
            {
                if (work.Attention.Level != AttentionLevel.ActionRequired) continue;
                if (characterVisible != null && !characterVisible(work.WorkstreamId)) continue;
                var sig = WaitingSignature(work, state);
                if (sig != null && dismissedMomentSignatures.Contains(sig)) continue;
                var slot = MomentSlotOf(work, state, includeIdleFallback: false);
                if (slot != null) return (work.WorkstreamId, slot);
            }
            return null;
        }

        /// <summary>
        /// The on-demand project screen model for <see cref="ScreenProjectId"/>, or null while no project screen is open.
        /// </summary>
        public KitScreenModel? ActiveScreen(ClientProjection? state, bool isLive = true)
        {
            if (ScreenProjectId is not string projectId || state == null) return null;
            if (!state.Projects.TryGetValue(projectId, out var project)) return null;
            return ScreenOf(project, state, ScreenBarOpen, isLive);
        }

        public static KitMomentSlot? MomentSlotOf(
            WorkstreamView work,
            ClientProjection state,
            bool includeIdleFallback = false)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            if (state == null) throw new ArgumentNullException(nameof(state));

            var execution = state.CurrentExecution(work);
            var runtimeDescriptor = execution != null ? state.RuntimeOf(execution) : null;
            var runtime = runtimeDescriptor?.DisplayName ?? execution?.Runtime.DisplayName ?? "Agent";
            var working = work.Status == WorkstreamStatus.Starting
                || work.Status == WorkstreamStatus.Running
                || work.Status == WorkstreamStatus.Verifying
                || work.Status == WorkstreamStatus.WaitingForHuman;
            var moments = new List<KitMoment>();

            if (work.Status == WorkstreamStatus.Starting)
            {
                moments.Add(KitMoment.Sent(runtime));
            }

            if (execution != null)
            {
                if (execution.LastTestRun is TestRunResultView testRun)
                {
                    moments.Add(KitMoment.AgentCheck(
                        runtime,
                        testRun.Label ?? "Test run",
                        testRun.Summary ?? testRun.Outcome.ToString(),
                        ParseTime(testRun.CompletedAt)));
                }

                var sandbox = runtimeDescriptor?.ApprovalMayLeaveSandbox switch
                {
                    true => KitSandbox.MayLeave,
                    false => KitSandbox.StaysInside,
                    _ => KitSandbox.NotDeclared,
                };
                foreach (var approval in execution.PendingApprovals.OrderBy(item => item.RequestedAt, StringComparer.Ordinal))
                {
                    var command = approval.Subject is ToolUseSubject tool
                        ? tool.Summary
                        : WorkspaceText.ApprovalWithoutTool;
                    var complete = approval.Approvable && WorkspaceText.OnlyDeny(approval) == null;
                    moments.Add(KitMoment.Question(runtime, command, sandbox, complete, ParseTime(approval.RequestedAt)));
                }

                foreach (var question in execution.PendingQuestions)
                {
                    if (question.Prompts.Count == 0) continue;
                    var first = question.Prompts[0];
                    var promptText = string.IsNullOrEmpty(first.Header)
                        ? first.Text
                        : first.Header + " · " + first.Text;
                    moments.Add(KitMoment.AgentSays(runtime, promptText, ParseTime(question.AskedAt)));
                }
            }

            if (moments.Count == 0 && includeIdleFallback)
            {
                moments.Add(KitMoment.AgentSays(runtime, work.Title, ParseTime(work.UpdatedAt)));
            }

            return moments.Count > 0 ? new KitMomentSlot(moments, working) : null;
        }

        public static KitScreenModel ScreenOf(
            ProjectView project,
            ClientProjection state,
            bool barOpen,
            bool isLive = true)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (state == null) throw new ArgumentNullException(nameof(state));

            var work = OrderWorkstreams(state.Workstreams.Values.Where(item => item.ProjectId == project.ProjectId)).FirstOrDefault();
            var execution = work != null ? state.CurrentExecution(work) : null;
            var runtime = execution != null
                ? (state.RuntimeOf(execution)?.DisplayName ?? execution.Runtime.DisplayName)
                : (state.Runtimes.FirstOrDefault()?.DisplayName ?? "Agent");
            var stateWords = work != null
                ? StateLanguage.LineWordOf(CharacterPresenter.Present(work, state, isLive))
                : StateLanguage.WordOf(WorkState.NotStarted);
            var working = work != null && (work.Status == WorkstreamStatus.Starting
                || work.Status == WorkstreamStatus.Running
                || work.Status == WorkstreamStatus.Verifying
                || work.Status == WorkstreamStatus.WaitingForHuman);

            KitMoment? lastCheck = null;
            if (project.Check is ProjectViewCheck hostCheck)
            {
                var checkedAt = ParseTime(hostCheck.RecordedAt);
                lastCheck = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: hostCheck.Findings.Passed, checkedAt);
            }
            else if (execution?.LastTestRun is TestRunResultView agentTest)
            {
                lastCheck = KitMoment.AgentCheck(
                    runtime,
                    agentTest.Label ?? "Test run",
                    agentTest.Summary ?? agentTest.Outcome.ToString(),
                    ParseTime(agentTest.CompletedAt));
            }

            var isVr = project.Starter != null;
            var tryRoute = ProjectPlayFacts.PlayOnLaptopFor(project) == PlayOnLaptop.Offered;
            return new KitScreenModel(
                project.Name,
                runtime,
                stateWords,
                isVr,
                lastCheck,
                tryRoute,
                pictureTakenAt: null,
                barOpen,
                working,
                newestChangeAt: null);
        }

        private string? CurrentWaitingSignature(ClientProjection? state)
        {
            if (state == null) return null;
            if (InspectedWorkstreamId is string inspected && state.Workstreams.TryGetValue(inspected, out var inspectedWork))
            {
                return WaitingSignature(inspectedWork, state);
            }
            foreach (var work in OrderWorkstreams(state.Workstreams.Values))
            {
                if (work.Attention.Level != AttentionLevel.ActionRequired) continue;
                var sig = WaitingSignature(work, state);
                if (sig != null && !dismissedMomentSignatures.Contains(sig)) return sig;
            }
            return null;
        }

        private static List<WorkstreamView> OrderWorkstreams(IEnumerable<WorkstreamView> workstreams)
        {
            var ordered = new List<WorkstreamView>(workstreams);
            ordered.Sort(CharacterLineup.Compare);
            return ordered;
        }

        private static string? WaitingSignature(WorkstreamView work, ClientProjection state)
        {
            var execution = state.CurrentExecution(work);
            if (execution != null)
            {
                var approval = execution.PendingApprovals
                    .OrderBy(item => item.RequestedAt, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (approval != null) return work.WorkstreamId + ":approval:" + approval.ApprovalId;
                var question = execution.PendingQuestions.FirstOrDefault();
                if (question != null) return work.WorkstreamId + ":question:" + question.QuestionId;
            }
            if (work.Attention.Level == AttentionLevel.ActionRequired && work.Attention.Reasons.Count > 0)
            {
                return work.WorkstreamId + ":attention:" + work.UpdatedAt;
            }
            return null;
        }

        private static DateTimeOffset? ParseTime(string? timestamp) =>
            timestamp != null && DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : null;
    }
}

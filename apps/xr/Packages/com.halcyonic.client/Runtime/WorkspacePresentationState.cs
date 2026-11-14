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
        private readonly Dictionary<string, IReadOnlyList<RuntimeModel>> runtimeModels = new Dictionary<string, IReadOnlyList<RuntimeModel>>(StringComparer.Ordinal);
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

        /// <summary>Whether showing the screen put the console aside, so that closing the screen brings it back.</summary>
        private bool consoleAsideForScreen;

        /// <summary>The project waiting to open on the Mac once its newly started preview is ready.</summary>
        public string? PendingOpenOnMacProjectId { get; private set; }

        /// <summary>The source project waiting for its <c>project.make_vr_version</c> VR project to appear in the projection.</summary>
        public string? PendingVrConversionSourceProjectId { get; private set; }

        /// <summary>The active two-step build sequence (<c>workstream.create</c> then <c>execution.start</c>) for a newly created VR project.</summary>
        public BuildSequence? PendingVrConversionSequence { get; private set; }

        /// <summary>The runtime selected through the composer's environment or agent selector pill, if any.</summary>
        public string? SelectedRuntimeId { get; private set; }

        /// <summary>The model reference selected for a <see cref="ModelChoice.Listed"/> runtime through the composer's agent selector pill, if any.</summary>
        public string? SelectedModelRef { get; private set; }

        private (string Task, string? Run, RoomResultPage Page, int Part)? resultPage;

        /// <summary>
        /// Which of the redesign's pages over a task at rest shows (<see cref="RoomResult"/>): the result, unless the person
        /// opened Changes or Checks for this very task and run. A new run, or another task, starts at the result again.
        /// </summary>
        public (RoomResultPage Page, int Part) ResultPageFor(string? workstreamId, string? executionId) =>
            resultPage is (var task, var run, var page, var part) && task == workstreamId && run == executionId
                ? (page, part) : (RoomResultPage.Result, 0);

        /// <summary>Shows <paramref name="page"/> of the task's result, at <paramref name="part"/> of its rows; true where that changed what shows.</summary>
        public bool ShowResultPage(string workstreamId, string? executionId, RoomResultPage page, int part = 0)
        {
            var next = (workstreamId, executionId, page, Math.Max(0, part));
            if (changing || ResultPageFor(workstreamId, executionId) == (page, Math.Max(0, part))) return false;
            changing = true;
            try
            {
                resultPage = page == RoomResultPage.Result ? ((string, string?, RoomResultPage, int)?)null : next;
                Changed?.Invoke();
                return true;
            }
            finally { changing = false; }
        }

        /// <summary>Where the device keeps the person's choice per project across restarts of the headset app; none, and it is kept for this run only.</summary>
        public IDevicePreferences? Preferences { get; set; }

        /// <summary>The device preference that keeps every project's choice (<see cref="ProjectChoices"/>).</summary>
        public const string ProjectChoicesKey = "halcyonic.project-choices";

        private ProjectChoices? projectChoices;

        private ProjectChoices Choices => projectChoices ??= ProjectChoices.Read(Preferences?.Get(ProjectChoicesKey));

        /// <summary>The project whose kept choice is the chosen runtime and model now (<see cref="UseProject"/>); null with none.</summary>
        public string? ChoiceProjectId { get; private set; }

        /// <summary>
        /// Whether a task's next start is never ask by default (ADR 0043's amendment of 2026-11-10): only where the level is
        /// shown before the start goes, which the redesign's row does at rest (AUDIT, 2026-11-11); Ask First anywhere else.
        /// </summary>
        public Func<bool> NeverAskByDefault { get; set; } = () => RoomDesign.Enabled;

        /// <summary>
        /// The level for the next start in <paramref name="projectId"/>: the one the person chose for that project, else the
        /// default (<see cref="NeverAskByDefault"/>). The choice alone starts nothing and is not the level a start uses: that
        /// also needs the runtime the start uses to offer it (<see cref="KitPresenter.RunChoiceOf"/>,
        /// <see cref="NewWorkDraft.StartsNeverAsk"/>). No project, no choice.
        /// </summary>
        public bool NeverAskFor(string? projectId) => projectId != null && (Choices.Of(projectId)?.NeverAsk ?? NeverAskByDefault());

        /// <summary>The level the person chose for <paramref name="projectId"/> themselves, never the default: never ask only where they chose it.</summary>
        public bool NeverAskChosenFor(string? projectId) => projectId != null && Choices.Of(projectId)?.NeverAsk == true;

        /// <summary>Keeps the level the person chose for the next start in <paramref name="projectId"/>, across restarts; true where it changed.</summary>
        public bool ChooseNeverAsk(string projectId, bool neverAsk)
        {
            if (changing || NeverAskFor(projectId) == neverAsk) return false;
            changing = true;
            try
            {
                Choices.Keep(projectId, Choices.Of(projectId) is ProjectChoice kept ? kept.WithLevel(neverAsk) : new ProjectChoice(null, null, neverAsk));
                Save();
                Changed?.Invoke();
                return true;
            }
            finally { changing = false; }
        }

        /// <summary>
        /// The person's context is <paramref name="projectId"/> now: its kept agent app and model become the chosen runtime
        /// and model (none kept, none chosen, so the run's own stand). Called while the view is laid out, it raises nothing;
        /// true where the chosen runtime or model changed.
        /// </summary>
        public bool UseProject(string? projectId)
        {
            if (changing || projectId == ChoiceProjectId) return false;
            ChoiceProjectId = projectId;
            var kept = projectId != null ? Choices.Of(projectId) : null;
            var (runtimeId, modelRef) = (kept?.RuntimeId, kept?.ModelRef);
            if (SelectedRuntimeId == runtimeId && SelectedModelRef == modelRef) return false;
            (SelectedRuntimeId, SelectedModelRef) = (runtimeId, modelRef);
            return true;
        }

        /// <summary>The chosen runtime and model are kept for the project in use, across restarts.</summary>
        private void RememberRuntime()
        {
            if (ChoiceProjectId is not string projectId) return;
            var level = Choices.Of(projectId)?.NeverAsk;
            Choices.Keep(projectId, new ProjectChoice(SelectedRuntimeId, SelectedModelRef, level));
            Save();
        }

        private void Save() => Preferences?.Set(ProjectChoicesKey, Choices.Write());

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

        /// <summary>
        /// Brings the console back. A project's screen standing in the room goes away with it: the screen and the console
        /// never stand in front of each other.
        /// </summary>
        public bool Restore()
        {
            if (!Minimized || changing) return false;
            changing = true;
            try
            {
                Minimized = false;
                (ScreenProjectId, ScreenBarOpen, consoleAsideForScreen) = (null, false, false);
                Changed?.Invoke();
                return true;
            }
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

        /// <summary>
        /// Brings a picked project's screen forward (ADR 0040). The console steps aside to its compact presence rather than
        /// stand in front of the screen, revoking its authority as minimizing does, and comes back when the screen closes.
        /// </summary>
        public bool ShowProjectScreen(string projectId, bool barOpen = false)
        {
            if (string.IsNullOrEmpty(projectId) || changing) return false;
            if (ScreenProjectId == projectId && ScreenBarOpen == barOpen) return false;
            changing = true;
            try
            {
                if (!Minimized)
                {
                    Suspending?.Invoke();
                    Minimized = true;
                    consoleAsideForScreen = true;
                }
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
                // The console the screen put aside comes back; one the person had put aside stays so.
                if (consoleAsideForScreen) Minimized = false;
                consoleAsideForScreen = false;
                Changed?.Invoke();
                return true;
            }
            finally { changing = false; }
        }

        public bool SelectRuntime(string? runtimeId, string? modelRef = null)
        {
            var normalizedModel = string.IsNullOrWhiteSpace(modelRef) ? null : modelRef;
            if (SelectedRuntimeId == runtimeId && SelectedModelRef == normalizedModel || changing) return false;
            changing = true;
            try
            {
                SelectedRuntimeId = runtimeId;
                SelectedModelRef = normalizedModel;
                RememberRuntime();
                Changed?.Invoke();
                return true;
            }
            finally { changing = false; }
        }

        public bool SelectModel(string? modelRef)
        {
            var normalizedModel = string.IsNullOrWhiteSpace(modelRef) ? null : modelRef;
            if (SelectedModelRef == normalizedModel || changing) return false;
            changing = true;
            try
            {
                SelectedModelRef = normalizedModel;
                RememberRuntime();
                Changed?.Invoke();
                return true;
            }
            finally { changing = false; }
        }

        /// <summary>
        /// Stores the listed models for <paramref name="runtimeId"/> with local Mac models (<see cref="NewWorkDraft.RunsHere"/>)
        /// ordered before remote models, matching <see cref="NewWorkDraft"/>.
        /// </summary>
        public void SetRuntimeModels(string runtimeId, IReadOnlyList<RuntimeModel>? models)
        {
            if (string.IsNullOrEmpty(runtimeId)) return;
            if (models == null || models.Count == 0)
            {
                runtimeModels.Remove(runtimeId);
                return;
            }
            var ordered = new List<RuntimeModel>(models.Count);
            foreach (var model in models)
            {
                if (model != null && NewWorkDraft.RunsHere(model)) ordered.Add(model);
            }
            foreach (var model in models)
            {
                if (model != null && !NewWorkDraft.RunsHere(model)) ordered.Add(model);
            }
            runtimeModels[runtimeId] = ordered;
        }

        /// <summary>Stores the models returned by <paramref name="response"/> when available.</summary>
        public void SetRuntimeModels(RuntimeModelsResponse? response)
        {
            if (response == null || string.IsNullOrEmpty(response.RuntimeId)) return;
            if (response.Result is AvailableModels available)
            {
                SetRuntimeModels(response.RuntimeId, available.Models);
                if (SelectedRuntimeId == response.RuntimeId)
                {
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>The ordered models cached for <paramref name="runtimeId"/>, or empty when none are cached.</summary>
        public IReadOnlyList<RuntimeModel> ModelsForRuntime(string? runtimeId) =>
            runtimeId != null && runtimeModels.TryGetValue(runtimeId, out var models)
                ? models
                : Array.Empty<RuntimeModel>();

        /// <summary>
        /// The display name, or the reference, of the model <paramref name="execution"/> runs on <paramref name="runtime"/>
        /// as the models listed for it name it, whatever is selected; null where it runs none by name.
        /// </summary>
        public string? ExecutionModelDisplay(RuntimeDescriptor? runtime, ExecutionView? execution)
        {
            if (runtime == null || runtime.ModelChoice != ModelChoice.Listed || execution == null
                || execution.Runtime.RuntimeId != runtime.RuntimeId || string.IsNullOrWhiteSpace(execution.ModelRef)) return null;
            return CaptureNames.ModelName(ModelsForRuntime(runtime.RuntimeId).FirstOrDefault(item => item.ModelRef == execution.ModelRef)?.DisplayName ?? execution.ModelRef);
        }

        /// <summary>
        /// Returns the display name or reference of the currently selected model for <paramref name="runtime"/>,
        /// or null when no specific model is selected.
        /// </summary>
        public string? SelectedModelDisplay(RuntimeDescriptor? runtime, ExecutionView? execution = null)
        {
            if (runtime == null || runtime.ModelChoice != ModelChoice.Listed) return null;
            var modelRef = SelectedRuntimeId == runtime.RuntimeId
                ? SelectedModelRef
                : execution != null && execution.Runtime.RuntimeId == runtime.RuntimeId
                    ? execution.ModelRef
                    : null;
            if (string.IsNullOrWhiteSpace(modelRef)) return null;
            var models = ModelsForRuntime(runtime.RuntimeId);
            var matched = models.FirstOrDefault(item => item.ModelRef == modelRef);
            return CaptureNames.ModelName(matched?.DisplayName ?? modelRef);
        }

        /// <summary>
        /// Switches between This Mac and Sandbox by selecting a runtime from <paramref name="runtimes"/> that declares the
        /// other environment (ADR 0033): the environment is what the runtime that would run declares, never a choice of its
        /// own, since no command carries one. Returns the runtime chosen and whether it stays inside a sandbox; null, with
        /// nothing changed, where the current runtime declares nothing or no registered runtime declares the other.
        /// </summary>
        public (string RuntimeId, bool Sandbox)? CycleEnvironment(IReadOnlyList<RuntimeDescriptor>? runtimes, string? currentRuntimeId = null)
        {
            var effectiveRuntimeId = currentRuntimeId ?? SelectedRuntimeId;
            var current = runtimes?.FirstOrDefault(item => item != null && item.RuntimeId == effectiveRuntimeId);
            if (current?.ApprovalMayLeaveSandbox is not bool mayLeave || changing) return null;
            // From This Mac (may leave) to Sandbox, and back.
            if (SelectRuntimeForEnvironment(runtimes, effectiveRuntimeId, sandbox: mayLeave) is not RuntimeDescriptor next) return null;
            changing = true;
            try
            {
                SelectedRuntimeId = next.RuntimeId;
                SelectedModelRef = null;
                RememberRuntime();
                Changed?.Invoke();
            }
            finally { changing = false; }
            return (next.RuntimeId, mayLeave);
        }

        /// <summary>
        /// Whether pressing the composer's Host chip can change anything: <paramref name="current"/> declares its environment
        /// and some registered runtime that can start work declares the other one.
        /// </summary>
        public static bool EnvironmentCanChange(IReadOnlyList<RuntimeDescriptor>? runtimes, RuntimeDescriptor? current) =>
            current?.ApprovalMayLeaveSandbox is bool mayLeave && SelectRuntimeForEnvironment(runtimes, current.RuntimeId, sandbox: mayLeave) != null;

        /// <summary>
        /// Selects an available runtime matching the requested execution environment (<paramref name="sandbox"/>:
        /// <c>true</c> for <c>ApprovalMayLeaveSandbox == false</c>, <c>false</c> for <c>ApprovalMayLeaveSandbox == true</c>),
        /// preferring one different from <paramref name="currentRuntimeId"/> when available, or null when no runtime declares that environment.
        /// </summary>
        public static RuntimeDescriptor? SelectRuntimeForEnvironment(
            IReadOnlyList<RuntimeDescriptor>? runtimes,
            string? currentRuntimeId,
            bool sandbox)
        {
            if (runtimes == null || runtimes.Count == 0) return null;
            var mayLeave = !sandbox;
            var matching = runtimes
                .Where(item => item != null && item.Capabilities.StartExecution && item.ApprovalMayLeaveSandbox == mayLeave)
                .ToList();
            if (matching.Count == 0) return null;
            return matching.FirstOrDefault(item => item.RuntimeId != currentRuntimeId && !item.Synthetic)
                ?? matching.FirstOrDefault(item => item.RuntimeId != currentRuntimeId)
                ?? matching[0];
        }

        /// <summary>
        /// Cycles to the next available runtime with <c>StartExecution</c> after <paramref name="currentRuntimeId"/>,
        /// or the first available runtime when <paramref name="currentRuntimeId"/> is absent.
        /// </summary>
        public static RuntimeDescriptor? NextRuntime(
            IReadOnlyList<RuntimeDescriptor>? runtimes,
            string? currentRuntimeId)
        {
            if (runtimes == null || runtimes.Count == 0) return null;
            var eligible = runtimes.Where(item => item != null && item.Capabilities.StartExecution).ToList();
            if (eligible.Count == 0) return null;
            var index = currentRuntimeId != null
                ? eligible.FindIndex(item => item.RuntimeId == currentRuntimeId)
                : -1;
            return index < 0 ? eligible[0] : eligible[(index + 1) % eligible.Count];
        }

        /// <summary>
        /// Cycles across available runtimes with <c>StartExecution</c> and any listed models provided by
        /// <paramref name="modelsForRuntime"/> (including Codex, OpenCode, Claude Agent, and local Meta/Ollama models).
        /// </summary>
        public static (RuntimeDescriptor Runtime, RuntimeModel? Model)? NextAgent(
            IReadOnlyList<RuntimeDescriptor>? runtimes,
            string? currentRuntimeId,
            string? currentModelRef = null,
            Func<string, IReadOnlyList<RuntimeModel>>? modelsForRuntime = null)
        {
            if (runtimes == null || runtimes.Count == 0) return null;
            var choices = new List<(RuntimeDescriptor Runtime, RuntimeModel? Model)>();
            foreach (var runtime in runtimes)
            {
                if (runtime == null || !runtime.Capabilities.StartExecution) continue;
                choices.Add((runtime, null));
                if (runtime.ModelChoice == ModelChoice.Listed && modelsForRuntime != null)
                {
                    var models = modelsForRuntime(runtime.RuntimeId);
                    if (models != null)
                    {
                        foreach (var model in models)
                        {
                            if (model != null) choices.Add((runtime, model));
                        }
                    }
                }
            }
            if (choices.Count == 0) return null;
            var normalizedModel = string.IsNullOrWhiteSpace(currentModelRef) ? null : currentModelRef;
            var index = -1;
            if (currentRuntimeId != null)
            {
                index = choices.FindIndex(item =>
                    item.Runtime.RuntimeId == currentRuntimeId
                    && item.Model?.ModelRef == normalizedModel);
                if (index < 0)
                {
                    index = choices.FindIndex(item => item.Runtime.RuntimeId == currentRuntimeId);
                }
            }
            return index < 0 ? choices[0] : choices[(index + 1) % choices.Count];
        }

        /// <summary>
        /// Advances the selected runtime and model to the next choice in <see cref="NextAgent"/> and notifies listeners.
        /// </summary>
        public (RuntimeDescriptor Runtime, RuntimeModel? Model)? CycleAgent(
            IReadOnlyList<RuntimeDescriptor>? runtimes,
            string? currentRuntimeId = null,
            string? currentModelRef = null)
        {
            var effectiveRuntimeId = currentRuntimeId ?? SelectedRuntimeId;
            var effectiveModelRef = currentModelRef ?? (effectiveRuntimeId == SelectedRuntimeId ? SelectedModelRef : null);
            var next = NextAgent(runtimes, effectiveRuntimeId, effectiveModelRef, ModelsForRuntime);
            if (next is var (runtime, model) && !changing)
            {
                changing = true;
                try
                {
                    SelectedRuntimeId = runtime.RuntimeId;
                    SelectedModelRef = model?.ModelRef;
                    RememberRuntime();
                    Changed?.Invoke();
                }
                finally { changing = false; }
            }
            return next;
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
            var runtime = CaptureNames.AgentName(runtimeDescriptor?.DisplayName ?? execution?.Runtime.DisplayName ?? "Agent");
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
                        ? CaptureNames.Request(tool.Summary)
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
                        : first.Header + ": " + first.Text;
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
            var runtime = CaptureNames.AgentName(execution != null
                ? (state.RuntimeOf(execution)?.DisplayName ?? execution.Runtime.DisplayName)
                : (state.Runtimes.FirstOrDefault()?.DisplayName ?? "Agent"));
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
                // A check whose verdict is not known is Partly Checked, never failed (VRIFY, AUDIT2's words): the host's finding under it.
                lastCheck = CheckSteps.PartlyChecked(hostCheck.Findings)
                    ? KitMoment.PartlyCheckedHost(KitCheckScope.Builds, hostCheck.Findings.Unknown, checkedAt, CheckSteps.Of(hostCheck.Findings))
                    : KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: hostCheck.Findings.Passed, checkedAt, CheckSteps.Of(hostCheck.Findings));
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
            var play = ProjectPlayFacts.PlayOnLaptopFor(project);
            var tryRoute = play == PlayOnLaptop.Offered || play == PlayOnLaptop.NoPreviewOfCheck;
            var newestChangeAt = project.Check != null && project.Check.Files != ProjectViewCheckFiles.Current
                ? DateTimeOffset.MaxValue
                : (DateTimeOffset?)null;
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
                newestChangeAt: newestChangeAt);
        }

        /// <summary>Returns the primary workstream for <paramref name="projectId"/> in <paramref name="state"/>, ordered by priority, or null.</summary>
        public static WorkstreamView? PrimaryWorkstreamOf(string projectId, ClientProjection? state) =>
            state == null ? null : OrderWorkstreams(state.Workstreams.Values.Where(item => item.ProjectId == projectId)).FirstOrDefault();

        /// <summary>
        /// Maps a room screen inspection action (<see cref="KitScreenAction.Steps"/>, <see cref="KitScreenAction.Why"/>,
        /// <see cref="KitScreenAction.HowItWent"/>) to the corresponding task <see cref="FileSection"/>, or null for
        /// non-section actions.
        /// </summary>
        public static FileSection? SectionForScreenAction(KitScreenAction action) => action switch
        {
            KitScreenAction.Steps => FileSection.Activity,
            KitScreenAction.Why => FileSection.Changes,
            KitScreenAction.HowItWent => FileSection.Checks,
            _ => null,
        };

        /// <summary>
        /// Where the redesign shows a task's section instead (SCREENS's one Changes, the lead's decision of 2026-11-12T1115Z):
        /// what changed is the files, so the file's Changes opens the result's page of changed files, whose rows open the file
        /// reading, never the understanding's section with its source band. Null where the section shows as itself (the older
        /// view, and every other section).
        /// </summary>
        public static RoomResultPage? ResultPageOf(FileSection section, bool roomDesign) =>
            roomDesign && section == FileSection.Changes ? RoomResultPage.Changes : (RoomResultPage?)null;

        /// <summary>
        /// Builds the control-plane command for a direct room screen action (<see cref="KitScreenAction.MakeAVrVersion"/>
        /// or <see cref="KitScreenAction.TryIt"/>) when the project state supports it, or null when the action opens
        /// a workspace flow instead.
        /// </summary>
        public static CommandEnvelope? CommandForScreenAction(ProjectView? project, KitScreenAction action, CommandFactory commands)
        {
            if (project == null || commands == null) return null;
            switch (action)
            {
                case KitScreenAction.MakeAVrVersion:
                    if (project.Starter != null) return null;
                    return ProjectPlayFacts.TryVrVersionTarget(project, out var root, out var folderName)
                        ? commands.MakeVrVersion(project.ProjectId, root, folderName)
                        : null;
                case KitScreenAction.TryIt:
                    var play = ProjectPlayFacts.PlayOnLaptopFor(project);
                    return play switch
                    {
                        PlayOnLaptop.Offered => commands.OpenOnMac(project.ProjectId),
                        PlayOnLaptop.NoPreviewOfCheck => commands.StartPreview(project.ProjectId),
                        _ => null,
                    };
                default:
                    return null;
            }
        }

        /// <summary>
        /// Maps a tethered moment card action (<see cref="KitMomentAction"/>) to the task <see cref="FileSection"/>
        /// that handles or inspects it in the full task console, or null when the action triggers project preview/try
        /// directly (<see cref="KitMomentAction.TryIt"/>).
        /// </summary>
        public static FileSection? SectionForMomentAction(KitMomentAction action) => action switch
        {
            KitMomentAction.YesRunIt or KitMomentAction.ReadIt or KitMomentAction.No or KitMomentAction.SaySomethingElse => FileSection.Waiting,
            KitMomentAction.SeeWhatChanged => FileSection.Changes,
            KitMomentAction.SeeTheOutput => FileSection.Checks,
            KitMomentAction.Stop => FileSection.Activity,
            _ => null,
        };

        /// <summary>
        /// Records a command dispatched from a spatial room screen or moment card so follow-up steps
        /// (<c>project.open_on_mac</c> after <c>project.preview.start</c>, or starting the conversion
        /// workstream after <c>project.make_vr_version</c>) can advance when the projection updates.
        /// </summary>
        public void RecordCommandDispatched(CommandEnvelope? command)
        {
            switch (command)
            {
                case ProjectPreviewStartCommand startPreview:
                    PendingOpenOnMacProjectId = startPreview.Payload.ProjectId;
                    break;
                case ProjectOpenOnMacCommand:
                    PendingOpenOnMacProjectId = null;
                    break;
                case ProjectMakeVrVersionCommand makeVr:
                    PendingVrConversionSourceProjectId = makeVr.Payload.OriginalProjectId;
                    PendingVrConversionSequence = null;
                    break;
            }
        }

        public void ResetPendingCommands()
        {
            PendingOpenOnMacProjectId = null;
            PendingVrConversionSourceProjectId = null;
            PendingVrConversionSequence = null;
            SelectedRuntimeId = null;
            SelectedModelRef = null;
            // What each project keeps stays kept; the next context loads it again.
            ChoiceProjectId = null;
            resultPage = null;
            runtimeModels.Clear();
        }

        /// <summary>
        /// Advances any pending spatial follow-up flow against <paramref name="state"/>:
        /// dispatches <c>project.open_on_mac</c> once a preview started by <see cref="KitScreenAction.TryIt"/>
        /// reaches <see cref="PlayOnLaptop.Offered"/>, and selects + starts the conversion workstream
        /// (<c>workstream.create</c> then <c>execution.start</c>) once a VR project created by
        /// <see cref="KitScreenAction.MakeAVrVersion"/> appears with its <see cref="ProjectMadeFromInfo"/>.
        /// </summary>
        public CommandEnvelope? AdvancePendingCommands(
            ClientProjection? state,
            CommandFactory? commands,
            out string? convertedProjectId)
        {
            convertedProjectId = null;
            if (state == null || commands == null) return null;

            if (PendingOpenOnMacProjectId is string previewProjectId)
            {
                if (!state.Projects.TryGetValue(previewProjectId, out var previewProject))
                {
                    PendingOpenOnMacProjectId = null;
                }
                else
                {
                    var play = ProjectPlayFacts.PlayOnLaptopFor(previewProject);
                    if (play == PlayOnLaptop.Offered)
                    {
                        PendingOpenOnMacProjectId = null;
                        return commands.OpenOnMac(previewProjectId);
                    }
                    if (play != PlayOnLaptop.NoPreviewOfCheck)
                    {
                        PendingOpenOnMacProjectId = null;
                    }
                }
            }

            if (PendingVrConversionSourceProjectId is string sourceProjectId)
            {
                ProjectView? newVrProject = null;
                ProjectMadeFromInfo? madeFrom = null;
                foreach (var candidate in state.Projects.Values
                    .OrderByDescending(item => item.CreatedAt, StringComparer.Ordinal)
                    .ThenBy(item => item.ProjectId, StringComparer.Ordinal))
                {
                    if (state.MadeFromOf(candidate.ProjectId) is ProjectMadeFromInfo info
                        && info.OriginalProjectId == sourceProjectId
                        && !string.IsNullOrWhiteSpace(info.Instruction)
                        && !state.Workstreams.Values.Any(work => work.ProjectId == candidate.ProjectId))
                    {
                        newVrProject = candidate;
                        madeFrom = info;
                        break;
                    }
                }

                if (newVrProject != null && madeFrom != null)
                {
                    PendingVrConversionSourceProjectId = null;
                    convertedProjectId = newVrProject.ProjectId;
                    if (ScreenProjectId == sourceProjectId)
                    {
                        ShowProjectScreen(newVrProject.ProjectId, ScreenBarOpen);
                    }
                    var runtime = (SelectedRuntimeId != null
                            ? state.Runtimes.FirstOrDefault(item =>
                                item.RuntimeId == SelectedRuntimeId
                                && item.Capabilities.StartExecution
                                && (item.ModelChoice != ModelChoice.Listed || !string.IsNullOrWhiteSpace(SelectedModelRef)))
                            : null)
                        ?? state.Runtimes.FirstOrDefault(item => item.Capabilities.StartExecution && item.ModelChoice != ModelChoice.Listed && !item.Synthetic)
                        ?? state.Runtimes.FirstOrDefault(item => item.Capabilities.StartExecution && item.ModelChoice != ModelChoice.Listed);
                    if (runtime != null)
                    {
                        var modelRef = runtime.ModelChoice == ModelChoice.Listed ? SelectedModelRef : null;
                        PendingVrConversionSequence = BuildSequence.ForPrompt(
                            newVrProject.ProjectId,
                            madeFrom.Instruction,
                            runtime,
                            commands,
                            out var firstCommand,
                            modelRef,
                            // The VR copy starts at the level the person chose for its source project; nothing shows a default for it.
                            neverAsk: NeverAskChosenFor(sourceProjectId));
                        return firstCommand;
                    }
                }
            }

            if (PendingVrConversionSequence is BuildSequence sequence)
            {
                var next = sequence.Advance(state);
                if (sequence.Started || sequence.Stopped)
                {
                    PendingVrConversionSequence = null;
                }
                if (next != null) return next;
            }

            return null;
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

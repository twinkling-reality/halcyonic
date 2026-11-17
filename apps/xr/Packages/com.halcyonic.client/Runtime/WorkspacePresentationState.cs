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

        /// <summary>
        /// The project whose screen the person last brought forward, which holds the command bar, if any. Other projects' screens may stand open
        /// beside it (<see cref="ScreenProjectIds"/>).
        /// </summary>
        public string? ScreenProjectId { get; private set; }

        private readonly List<string> openScreens = new List<string>();

        /// <summary>
        /// Every project screen open in the room, in the order they were opened (the lead, 2026-11-17T18:55Z (d)): up to <see cref="ScreensMost"/>,
        /// each standing at its own place. Another past that is refused, never silently swapped for one already open (AUDIT3's SC1): the person
        /// reads <see cref="CloseAScreen"/> and closes one first.
        /// </summary>
        public IReadOnlyList<string> ScreenProjectIds => openScreens;

        /// <summary>How many project screens stand open at once (SHOW's RoomLayout.Screens fits three in the seated reach).</summary>
        public const int ScreensMost = 3;

        /// <summary>The words a refused open shows (AUDIT2's): the person closes a screen, then opens the next.</summary>
        public const string CloseAScreen = "Close a screen to open another";

        /// <summary>True from a refused open until the next screen opens or closes: the room says <see cref="CloseAScreen"/>.</summary>
        public bool ScreenRefused { get; private set; }

        /// <summary>Whether the on-demand project screen's command bar is open rather than folded to its handle.</summary>
        public bool ScreenBarOpen { get; private set; }

        /// <summary>Whether showing the screen put the console aside, so that closing the screen brings it back.</summary>
        private bool consoleAsideForScreen;

        private readonly List<string> pendingOpenOnMac = new List<string>();
        private readonly List<string> pendingVrSources = new List<string>();

        /// <summary>
        /// The projects waiting to open on the Mac once their newly started previews are ready, in the order pressed: each project its
        /// own wait, side by side, never one press in another's place (limit #15, the lead's 19:17Z).
        /// </summary>
        public IReadOnlyList<string> PendingOpenOnMacProjectIds => pendingOpenOnMac;

        /// <summary>The first project waiting to open on the Mac, or null.</summary>
        public string? PendingOpenOnMacProjectId => pendingOpenOnMac.Count > 0 ? pendingOpenOnMac[0] : null;

        /// <summary>
        /// The source projects waiting for their <c>project.make_vr_version</c> VR projects, in the order pressed (limit #15): each
        /// conversion starts in its turn, one at a time, and a later press never replaces an earlier one.
        /// </summary>
        public IReadOnlyList<string> PendingVrConversionSourceProjectIds => pendingVrSources;

        /// <summary>The first source project waiting for its VR project, or null.</summary>
        public string? PendingVrConversionSourceProjectId => pendingVrSources.Count > 0 ? pendingVrSources[0] : null;

        /// <summary>A VR version waiting its turn (AUDIT2's words): its state, always this, whatever its place in the queue.</summary>
        public const string QueueWaiting = "Waiting";

        /// <summary>
        /// A project's place in the Make VR Version queue as the kit's two parts, never joined (AUDIT2 19:26Z): the state
        /// <see cref="QueueWaiting"/>, and under it "{n} Ahead" where n conversions go before it (one running counts), null where
        /// none does. Null where the project is not queued.
        /// </summary>
        public (string State, string? Ahead)? VrQueuePlace(string projectId)
        {
            var at = pendingVrSources.IndexOf(projectId);
            if (at < 0) return null;
            var ahead = at + (PendingVrConversionSequence != null ? 1 : 0);
            return (QueueWaiting, ahead == 0 ? null : ahead.ToString(System.Globalization.CultureInfo.InvariantCulture) + " Ahead");
        }

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
        /// shown before the start goes, which the redesign's row does at rest (AUDIT, 2026-11-11); Ask Me First anywhere else.
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
                openScreens.Clear();
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
            if (!openScreens.Contains(projectId) && openScreens.Count >= ScreensMost)
            {
                if (!ScreenRefused) { ScreenRefused = true; Changed?.Invoke(); }
                return false;
            }
            ScreenRefused = false;
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
                if (!openScreens.Contains(projectId)) openScreens.Add(projectId);
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

        /// <summary>Closes the screen brought forward; the others stay open (the lead's walk: open A, B and C, close B, and A and C stay).</summary>
        public bool CloseProjectScreen() => ScreenProjectId != null && CloseProjectScreen(ScreenProjectId);

        /// <summary>
        /// Closes <paramref name="projectId"/>'s screen alone. Where it held the bar, the last opened of the rest takes it, folded. The console the
        /// screens put aside comes back once none stands; one the person had put aside stays so.
        /// </summary>
        public bool CloseProjectScreen(string projectId)
        {
            if (string.IsNullOrEmpty(projectId) || !openScreens.Contains(projectId) || changing) return false;
            ScreenRefused = false;
            changing = true;
            try
            {
                openScreens.Remove(projectId);
                if (ScreenProjectId == projectId)
                {
                    ScreenProjectId = openScreens.Count > 0 ? openScreens[openScreens.Count - 1] : null;
                    ScreenBarOpen = false;
                }
                if (openScreens.Count == 0)
                {
                    if (consoleAsideForScreen) Minimized = false;
                    consoleAsideForScreen = false;
                }
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
        /// Every project's own Needs You card (the lead's 2026-11-17T19:17Z limit #2): one card at each place whose tasks wait on the
        /// person, for the first of them in the shelf's order (the inspected task where it is the place's), never only one card for the
        /// whole room; and under it the count of the others waiting at that same place (<see cref="PlaceMoment.MoreWaitingWords"/>), so
        /// none waits silent. A closed card's signature stays closed, as <see cref="ActiveMoment"/>'s does.
        /// </summary>
        public IReadOnlyList<PlaceMoment> ActiveMoments(ClientProjection? state, Func<string, bool>? characterVisible = null)
        {
            var moments = new List<PlaceMoment>();
            if (!AgentsVisible || state == null) return moments;
            var inspected = InspectedWorkstreamId != null ? ActiveMoment(state, characterVisible) : null;
            var waitingBy = new Dictionary<string, List<(WorkstreamView Work, KitMomentSlot Slot)>>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var work in OrderWorkstreams(state.Workstreams.Values))
            {
                if (work.Attention.Level != AttentionLevel.ActionRequired) continue;
                if (characterVisible != null && !characterVisible(work.WorkstreamId)) continue;
                var sig = WaitingSignature(work, state);
                if (sig != null && dismissedMomentSignatures.Contains(sig)) continue;
                if (MomentSlotOf(work, state, includeIdleFallback: false) is not KitMomentSlot slot) continue;
                var project = work.ProjectId;
                if (!waitingBy.TryGetValue(project, out var waiting))
                {
                    waitingBy[project] = waiting = new List<(WorkstreamView, KitMomentSlot)>();
                    order.Add(project);
                }
                waiting.Add((work, slot));
            }
            // The inspected task's place shows the inspected task's card first, the rest of that place's waiting counted under it.
            if (inspected is var (inspectedId, inspectedSlot) && state.Workstreams.TryGetValue(inspectedId, out var inspectedWork))
            {
                var project = inspectedWork.ProjectId;
                if (!waitingBy.TryGetValue(project, out var waiting))
                {
                    waitingBy[project] = waiting = new List<(WorkstreamView, KitMomentSlot)>();
                    order.Insert(0, project);
                }
                waiting.RemoveAll(each => each.Work.WorkstreamId == inspectedId);
                waiting.Insert(0, (inspectedWork, inspectedSlot));
            }
            foreach (var project in order)
            {
                var waiting = waitingBy[project];
                moments.Add(new PlaceMoment(project, waiting[0].Work.WorkstreamId, waiting[0].Slot, waiting.Count - 1));
            }
            return moments;
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

        /// <summary>
        /// Lets go of every open screen whose project the projection no longer holds (AUDIT3's SC2), so a gone project never keeps a slot that
        /// refuses a real open. Where the one brought forward went, the last opened of the rest takes the bar, folded.
        /// </summary>
        public void ForgetScreensOfGoneProjects(ClientProjection state)
        {
            if (openScreens.RemoveAll(id => !state.Projects.ContainsKey(id)) == 0) return;
            if (ScreenProjectId != null && !openScreens.Contains(ScreenProjectId))
            {
                ScreenProjectId = openScreens.Count > 0 ? openScreens[openScreens.Count - 1] : null;
                ScreenBarOpen = false;
            }
            ScreenRefused = false;
        }

        /// <summary>
        /// Every open screen's model, in <see cref="ScreenProjectIds"/>' order: the one brought forward with its bar as set, the rest folded.
        /// A project the projection no longer holds stands no screen.
        /// </summary>
        public IReadOnlyList<(string ProjectId, KitScreenModel Screen)> OpenScreens(ClientProjection? state, bool isLive = true)
        {
            var screens = new List<(string, KitScreenModel)>();
            if (state == null) return screens;
            ForgetScreensOfGoneProjects(state);
            foreach (var id in openScreens)
                if (state.Projects.TryGetValue(id, out var project))
                    screens.Add((id, ScreenOf(project, state, id == ScreenProjectId && ScreenBarOpen, isLive)));
            return screens;
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
            // The primary task's own run's app, or none (AUDIT2's 19:23Z MUST): only a check the run itself reported is credited to it.
            var runtime = execution != null ? CaptureNames.AgentName(state.RuntimeOf(execution)?.DisplayName ?? execution.Runtime.DisplayName) : null;
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
                    // Nothing new drawn yet on the starter: Starter Only, never failed (the lead's 14:11Z).
                    : CheckSteps.StarterOnly(hostCheck.Findings)
                    ? KitMoment.StarterOnlyHost(KitCheckScope.Builds, checkedAt, CheckSteps.Of(hostCheck.Findings))
                    : KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: hostCheck.Findings.Passed, checkedAt, CheckSteps.Of(hostCheck.Findings));
            }
            else if (execution?.LastTestRun is TestRunResultView agentTest && runtime != null)
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
                stateWords,
                isVr,
                lastCheck,
                tryRoute,
                pictureTakenAt: null,
                barOpen,
                working,
                newestChangeAt: newestChangeAt).WithAgents(SwarmOf(project, state, isLive));
        }

        /// <summary>
        /// The project's agents as rows where more than one task works on it (limit #14), in the shelf's order: each its task's name, its
        /// agent app, its own state (the tag's word, so screen, tag and card say one thing), and whether it needs the person. Empty for one
        /// task or none.
        /// </summary>
        public static IReadOnlyList<KitScreenAgent> SwarmOf(ProjectView project, ClientProjection state, bool isLive = true)
        {
            var tasks = OrderWorkstreams(state.Workstreams.Values.Where(item => item.ProjectId == project.ProjectId));
            if (tasks.Count < 2) return Array.Empty<KitScreenAgent>();
            return tasks.Select(task =>
            {
                var execution = state.CurrentExecution(task);
                // The task's own run's app, or none: never another runtime's name or a stand-in for a task with no run (AUDIT2's 19:23Z MUST).
                var agent = execution != null ? CaptureNames.AgentName(state.RuntimeOf(execution)?.DisplayName ?? execution.Runtime.DisplayName) : null;
                var presented = CharacterPresenter.Present(task, state, isLive);
                return new KitScreenAgent(task.WorkstreamId, LineWords.TheirName(task.Title), agent, CharacterPresenter.TagWord(presented),
                    task.Attention.Level == AttentionLevel.ActionRequired);
            }).ToList();
        }

        /// <summary>Returns the primary workstream for <paramref name="projectId"/> in <paramref name="state"/>, ordered by priority, or null.</summary>
        public static WorkstreamView? PrimaryWorkstreamOf(string projectId, ClientProjection? state) =>
            state == null ? null : OrderWorkstreams(state.Workstreams.Values.Where(item => item.ProjectId == projectId)).FirstOrDefault();

        /// <summary>
        /// The redesign's page an evidence door opens (the lead's 2026-11-17 20:50Z): Outcome, Seorak's measure of the run, and
        /// Reasoning, Salidium's understanding of it; null for every other action and in the older view, where the doors open sections.
        /// </summary>
        public static RoomResultPage? EvidencePageFor(KitScreenAction action, bool roomDesign) => !roomDesign ? (RoomResultPage?)null : action switch
        {
            KitScreenAction.HowItWent => RoomResultPage.Outcome,
            KitScreenAction.Why => RoomResultPage.Reasoning,
            _ => null,
        };

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
                    if (!pendingOpenOnMac.Contains(startPreview.Payload.ProjectId)) pendingOpenOnMac.Add(startPreview.Payload.ProjectId);
                    break;
                case ProjectOpenOnMacCommand openOnMac:
                    pendingOpenOnMac.Remove(openOnMac.Payload.ProjectId);
                    break;
                case ProjectMakeVrVersionCommand makeVr:
                    // Queued behind any conversion under way, which carries on (limit #15: never overwritten).
                    if (!pendingVrSources.Contains(makeVr.Payload.OriginalProjectId)) pendingVrSources.Add(makeVr.Payload.OriginalProjectId);
                    break;
            }
        }

        public void ResetPendingCommands()
        {
            pendingOpenOnMac.Clear();
            pendingVrSources.Clear();
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

            // Every project waiting on its preview, each in its own right (limit #15): the first ready opens now, the rest next frame.
            foreach (var previewProjectId in pendingOpenOnMac.ToList())
            {
                if (!state.Projects.TryGetValue(previewProjectId, out var previewProject))
                {
                    pendingOpenOnMac.Remove(previewProjectId);
                    continue;
                }
                var play = ProjectPlayFacts.PlayOnLaptopFor(previewProject);
                if (play == PlayOnLaptop.Offered)
                {
                    pendingOpenOnMac.Remove(previewProjectId);
                    return commands.OpenOnMac(previewProjectId);
                }
                if (play != PlayOnLaptop.NoPreviewOfCheck) pendingOpenOnMac.Remove(previewProjectId);
            }

            // One conversion at a time, the queue in the order pressed (limit #15): the next starts once the one under way ends.
            foreach (var sourceProjectId in PendingVrConversionSequence == null ? pendingVrSources.ToList() : new List<string>())
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
                    pendingVrSources.Remove(sourceProjectId);
                    convertedProjectId = newVrProject.ProjectId;
                    var stood = openScreens.IndexOf(sourceProjectId);
                    if (stood >= 0 && !openScreens.Contains(newVrProject.ProjectId)) openScreens[stood] = newVrProject.ProjectId;
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
                    // No runtime can start it: this one ends here, as before; the next in line waits for the next frame.
                    return null;
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

    /// <summary>A place's Needs You card: its project, the task it shows, its moment, and how many more wait at that place.</summary>
    public sealed class PlaceMoment
    {
        public PlaceMoment(string projectId, string workstreamId, KitMomentSlot slot, int moreWaiting)
        {
            ProjectId = projectId;
            WorkstreamId = workstreamId;
            Slot = slot;
            MoreWaiting = moreWaiting < 0 ? 0 : moreWaiting;
        }

        public string ProjectId { get; }

        public string WorkstreamId { get; }

        public KitMomentSlot Slot { get; }

        /// <summary>The others waiting at this place, never counting the one shown.</summary>
        public int MoreWaiting { get; }

        /// <summary>"{n} More Waiting" under the card's content (AUDIT2's 2026-11-17T19:18Z); null where none other waits.</summary>
        public string? MoreWaitingWords => MoreWaiting >= 1 ? MoreWaiting.ToString(CultureInfo.InvariantCulture) + " More Waiting" : null;
    }
}

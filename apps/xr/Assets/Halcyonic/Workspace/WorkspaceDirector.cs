#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Three levels of detail for the same work, all in place. Ambient: the characters as the stage
    /// shows them. Peek: once the person's gaze rests on a character, or at once while a hand points
    /// at it, one line beside it (<see cref="PeekChoice"/>). Open: a pinch on the ray, a poke, or a
    /// pinch of either hand while the gaze peek shows (look and pinch) opens the workspace next to
    /// that character, within reach and clear of the others; collapsing returns to ambient.
    /// Everything shown comes from the client core (WorkspacePresenter, WorkspaceText) and the
    /// session; commands go through WorkspaceSteering and RealtimeSession.SubmitAsync, and a result
    /// is shown as done only when the control plane's record says the runtime confirmed it. No peek,
    /// hint or input while the app lacks focus (<see cref="FocusGuard"/>).
    /// </summary>
    [RequireComponent(typeof(ControlPlaneConnection), typeof(CharacterStage))]
    public sealed class WorkspaceDirector : MonoBehaviour
    {
        /// <summary>Farther than a character's own motion, in its units: the stage moved it.</summary>
        private const float MovedFar = 1.5f * CharacterView.BodyRadius;

        private const float NoticeSeconds = 8f;
        private const float RefreshSeconds = 0.5f;

        /// <summary>Activity times are local; if the platform cannot tell the zone, they are UTC and say so.</summary>
        private static readonly (TimeZoneInfo Zone, string Note) Clock = LocalClock();

        private readonly ActivityLog activity = new ActivityLog();
        private readonly CommandSubmissions submissions = new CommandSubmissions();
        private readonly Dictionary<string, CharacterTarget> targets = new Dictionary<string, CharacterTarget>();
        private ControlPlaneConnection connection = null!;
        private CharacterStage stage = null!;
        private CommandFactory commands = null!;
        private PeekLabel peek = null!;
        private OnboardingHint hint = null!;
        private readonly PeekChoice peekChoice = new PeekChoice();
        private readonly List<BodyInView> bodies = new List<BodyInView>();
        private GazeHover? gaze;
        private CharacterTarget? pointed;
        private CharacterTarget? facing;
        private string? peekLineFor;
        private string peekLine = "";
        private Opened? opened;
        private string? journalId;
        private int shownSubmissions = -1;
        private float nextRefresh;

        /// <summary>
        /// Raised with the workstream when the person opens its workspace, collapses it, or sends a
        /// command from it; the stage's sound follows it. A command raises it once handed to the
        /// session: sent, not confirmed, since the runtime's answer arrives later in the state.
        /// </summary>
        public event Action<string, WorkspaceAct>? Acted;

        private void Awake()
        {
            connection = GetComponent<ControlPlaneConnection>();
            stage = GetComponent<CharacterStage>();
            // The same client the session introduces itself as (ControlPlaneConnection).
            commands = new CommandFactory(new ClientInfo
            {
                Name = "halcyonic-xr",
                Version = Application.version,
                DeviceLabel = SystemInfo.deviceModel,
            });
            peek = PeekLabel.Create(transform);
            hint = OnboardingHint.Create(transform);
        }

        private void OnEnable()
        {
            connection.Changed += OnChanged;
            stage.CharacterCreated += Attach;
        }

        private void OnDisable()
        {
            connection.Changed -= OnChanged;
            stage.CharacterCreated -= Attach;
        }

        private void Start()
        {
            gaze = GazeHover.Create(transform, () => peekChoice.PinchTarget);
            if (gaze == null)
            {
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this, "Halcyonic: {0}", "no gaze in this scene, so only hands peek");
            }
            else if (gaze.Hands == 0)
            {
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this, "Halcyonic: {0}", "no seated hand rays in this scene, so the gaze peeks but a pinch does not open");
            }
            // Characters the stage created before this component subscribed.
            var session = connection.Session;
            if (session == null) return;
            foreach (var workstreamId in session.State.Workstreams.Keys)
            {
                if (stage.TryGetCharacter(workstreamId, out var view)) Attach(workstreamId, view);
            }
        }

        private void Attach(string workstreamId, CharacterView view)
        {
            if (targets.TryGetValue(workstreamId, out var existing) && existing != null && existing.View == view) return;
            var target = CharacterTarget.Attach(view, workstreamId);
            targets[workstreamId] = target;
            target.Ray.Selected += () => OnSelected(target);
            target.Poke.Selected += () => OnSelected(target);
            target.Ray.GazeSelected += () => OnLookAndPinch(target);
        }

        private void OnChanged(StateChanges changes)
        {
            var session = connection.Session;
            if (session == null) return;
            if (changes.Resynchronized)
            {
                var current = session.State.Journal?.JournalId;
                if (current != journalId || changes.Rewound)
                {
                    // Positions and commands from another journal, or from a demonstration that
                    // started again, mean nothing here.
                    activity.Clear();
                    submissions.Clear();
                    journalId = current;
                }
                // A snapshot carries state but no history: read the open workstream's history again.
                if (opened != null) ReadHistory(opened);
            }
            activity.Record(changes.Events);
            Refresh();
        }

        private void Update()
        {
            if (opened != null && opened.Character == null) Close(immediately: true);
            if (opened != null && opened.Transition != null
                && Vector3.Distance(opened.Transition.PlacedBeside, opened.Character!.BodyPosition) > MovedFar * opened.Character.Scale)
            {
                // The stage moved the character, for example after a recenter: the workspace follows.
                var (place, scale) = PlaceBeside(opened.Character);
                opened.Transition.MoveTo(place, scale);
            }
            PollKeyboard();
            if (submissions.Version != shownSubmissions || Time.unscaledTime >= nextRefresh) Refresh();
            UpdatePeek();
        }

        private void Refresh()
        {
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            shownSubmissions = submissions.Version;
            // The peek's line is written again on its next frame.
            peekLineFor = null;
            RefreshHint();
            RefreshPanel();
        }

        private WorkspacePresentation? Present(string workstreamId)
        {
            var session = connection.Session;
            if (session == null || !session.State.Workstreams.TryGetValue(workstreamId, out var workstream)) return null;
            return WorkspacePresenter.Present(workstream, session.State, activity, session.Status.IsLive, submissions);
        }

        /// <summary>
        /// Every frame, since peeks fade: tells <see cref="PeekChoice"/> what the hands and the gaze
        /// are on, shows the peek it chooses, and turns that character, and an open one, to the
        /// person. Allocates nothing unless the peek's line changes.
        /// </summary>
        private void UpdatePeek()
        {
            var head = WorkspaceVisuals.Head;
            var headForward = head != null ? head.forward : Vector3.forward;
            // A hand keeps the character it points at until it leaves, then takes any other.
            if (pointed == null || !pointed.HandHovered) pointed = null;
            CharacterTarget? gazed = null;
            foreach (var target in targets.Values)
            {
                if (target == null) continue;
                if (pointed == null && target.HandHovered) pointed = target;
                if (gazed == null && target.GazeHovered) gazed = target;
            }
            var input = new PeekInput
            {
                Suspended = FocusGuard.InputSuspended,
                Open = opened?.Character.WorkstreamId,
                Pointed = pointed?.WorkstreamId,
                HandOnTarget = PointerTarget.AnyHandOnTarget,
                Gazed = gazed?.WorkstreamId,
                GazedOffCenter = gazed != null ? Vector3.Angle(headForward, gazed.BodyPosition - WorkspaceVisuals.HeadPosition) : 0f,
                HeadForward = new System.Numerics.Vector3(headForward.x, headForward.y, headForward.z),
            };
            peekChoice.Update(input, Time.unscaledTime);

            var shown = peekChoice.Shown != null && targets.TryGetValue(peekChoice.Shown, out var peeked) ? peeked : null;
            if (shown != null && peekLineFor != shown.WorkstreamId)
            {
                var presentation = Present(shown.WorkstreamId);
                peekLineFor = shown.WorkstreamId;
                peekLine = presentation == null ? "" : WorkspaceText.Peek(presentation);
            }
            if (shown == null || peekLine.Length == 0) peek.Hide();
            else peek.Show(shown, peekLine, peekChoice.Opacity);

            // The character whose peek is wanted looks at the person, as does the open one.
            var wanted = peekChoice.Wanted != null && targets.TryGetValue(peekChoice.Wanted, out var looking) ? looking : null;
            if (wanted == facing) return;
            if (facing != null) FacePerson(facing, opened?.Character == facing);
            facing = wanted;
            if (facing != null) FacePerson(facing, true);
        }

        /// <summary>A look and pinch: the gaze interactor selected a character on a pinch, while its gaze peek showed.</summary>
        private void OnLookAndPinch(CharacterTarget target)
        {
            if (opened != null || gaze == null || gaze.Armed != target.WorkstreamId) return;
            Open(target);
        }

        /// <summary>
        /// Until the person first opens a workspace, a small pinch cue above the first character that
        /// needs them; nothing while a workspace is open or the app lacks focus.
        /// </summary>
        private void RefreshHint()
        {
            var session = connection.Session;
            if (!OnboardingHint.Needed || opened != null || FocusGuard.InputSuspended || session == null)
            {
                hint.Hide();
                return;
            }
            var needing = session.State.Workstreams.Values
                .Where(workstream => workstream.Attention.Level == AttentionLevel.ActionRequired)
                .OrderBy(workstream => workstream.CreatedAt, StringComparer.Ordinal)
                .Select(workstream => targets.TryGetValue(workstream.WorkstreamId, out var target) ? target : null)
                .FirstOrDefault(target => target != null);
            if (needing == null) hint.Hide();
            else hint.Show(needing);
        }

        private void OnSelected(CharacterTarget target)
        {
            var reopen = opened?.Character != target;
            Close(immediately: false);
            if (reopen) Open(target);
            else Acted?.Invoke(target.WorkstreamId, WorkspaceAct.Collapse);
        }

        private void Open(CharacterTarget target)
        {
            var presentation = Present(target.WorkstreamId);
            if (presentation == null) return;
            OnboardingHint.Learned();
            hint.Hide();

            var root = new GameObject("Workspace " + target.WorkstreamId);
            root.transform.SetParent(transform, false);
            var panel = WorkspacePanel.Create(root.transform);
            var (place, scale) = PlaceBeside(target);
            var transition = WorkspaceTransition.Begin(root, target, place, scale);
            var workspace = new Opened(target, panel, transition, new WorkspaceSteering(commands));
            opened = workspace;
            Acted?.Invoke(target.WorkstreamId, WorkspaceAct.Open);
            workspace.Sections = WorkspaceSections.Attach(panel, () => workspace.Now, IntelligenceReader);
            workspace.Sections.RequestTurned += () =>
            {
                if (opened == workspace) RefreshPanel();
            };
            panel.Accepting = () => opened == workspace && transition.Open;
            panel.ActionPressed += action => Steer(workspace, s => s.Press(action, workspace.Now!));
            panel.ConfirmPressed += () => Steer(workspace, s => s.Confirm(workspace.Now!));
            panel.PresetPressed += preset =>
            {
                workspace.Presets = false;
                Steer(workspace, s => s.Typed(preset.Text, workspace.Now!));
            };
            panel.CancelPressed += () =>
            {
                CloseKeyboard(workspace);
                workspace.Steering.StopTyping();
                workspace.Steering.Cancel();
                workspace.Presets = false;
                RefreshPanel();
            };
            panel.CollapsePressed += () =>
            {
                Acted?.Invoke(target.WorkstreamId, WorkspaceAct.Collapse);
                Close(immediately: false);
            };
            FacePerson(target, true);
            ReadHistory(workspace);
            RefreshPanel();
        }

        /// <summary>
        /// Within reach, toward the character, facing the eyes, scaled to its designed angular size,
        /// and clear of every character's body, below the ones it passes or above them, in the
        /// comfortable band and never into the surface they stand on (<see cref="WorkspaceLayout"/>).
        /// </summary>
        private (Pose Place, float Scale) PlaceBeside(CharacterTarget target)
        {
            var eyes = WorkspaceVisuals.HeadPosition;
            var looking = WorkspaceVisuals.Head != null ? WorkspaceVisuals.Head.forward : target.BodyPosition - eyes;
            var (pose, _) = WorkspaceLayout.Place(target, targets.Values, eyes, looking, stage.SurfaceHeight, bodies);
            return (pose, WorkspaceLayout.Scale);
        }

        private void Close(bool immediately)
        {
            var closing = opened;
            if (closing == null) return;
            opened = null;
            CloseKeyboard(closing);
            if (closing.Character != null) FacePerson(closing.Character, closing.Character == facing);
            // Gone already when its character left the stage.
            if (closing.Transition != null) closing.Transition.Collapse(immediately || closing.Character == null);
        }

        private void RefreshPanel()
        {
            var workspace = opened;
            if (workspace == null) return;
            var presentation = Present(workspace.Character.WorkstreamId);
            if (presentation == null)
            {
                // The workstream is gone from the state, for example in another journal.
                Close(immediately: false);
                return;
            }
            workspace.Now = presentation;
            var lapse = workspace.Steering.Refresh(presentation);
            if (lapse != null) Notify(workspace, lapse);
            ShowRequest(workspace, presentation);
            if (workspace.Notice != null && Time.unscaledTime > workspace.NoticeUntil) workspace.Notice = null;
            workspace.Panel.Show(Content(workspace, presentation));
        }

        /// <summary>
        /// While an approval or denial waits for its confirmation, the whole request it answers shows
        /// under the actions, in parts when it is long, and the steering learns which part shows: an
        /// approval is confirmed only once the last part has shown.
        /// </summary>
        private static void ShowRequest(Opened workspace, WorkspacePresentation presentation)
        {
            var request = workspace.Steering.Request(presentation);
            if (request == null)
            {
                workspace.Sections.EndRequest();
                return;
            }
            workspace.Sections.ShowRequest(request);
            workspace.Steering.RequestShown(workspace.Sections.Request.Part, workspace.Sections.Request.Parts);
        }

        private PanelContent Content(Opened workspace, WorkspacePresentation presentation)
        {
            var character = presentation.Character;
            var steering = workspace.Steering;
            var content = new PanelContent
            {
                Title = character.Title,
                Status = WorkspaceText.StatusLine(character),
                StatusColor = ToneOf(character.Activity),
                Execution = WorkspaceText.Execution(presentation),
                Objective = "Objective: " + WorkspaceText.Objective(presentation),
                Attention = WorkspaceText.Attention(presentation),
                AttentionColor = character.Attention == AttentionLevel.ActionRequired ? WorkspaceVisuals.AttentionColor : ToneOf(character.Activity),
                Actions = presentation.Actions.ToList(),
                WhyNoActions = WorkspaceText.WhyNoActions(presentation),
                Notice = workspace.Notice,
                Feedback = presentation.Commands.Select(command => command.Text).ToList(),
                Activity = presentation.Activity.Select(entry => (WorkspaceText.Activity(entry, Clock.Zone), entry.Reported)).ToList(),
                ActivityCaption = "Recent activity" + Clock.Note + workspace.HistoryNote,
            };
            if (steering.Armed != null)
            {
                content.Mode = ControlsMode.Confirm;
                content.Prompt = steering.Prompt(presentation);
                content.ConfirmLabel = WorkspaceText.ConfirmLabel(steering.Armed.Value);
                content.CanConfirm = steering.CanConfirm;
            }
            else if (workspace.Presets)
            {
                content.Mode = ControlsMode.Presets;
                content.Presets = workspace.Recorded?.Count > 0 ? workspace.Recorded : WorkspaceText.PresetInstructions;
            }
            else if (steering.Typing)
            {
                content.Mode = ControlsMode.Typing;
                content.Prompt = WorkspaceText.TypingPrompt;
            }
            return content;
        }

        private static Color ToneOf(CharacterActivity activity) => activity switch
        {
            CharacterActivity.WaitingForHuman => WorkspaceVisuals.AttentionColor,
            CharacterActivity.Failed => WorkspaceVisuals.ProblemColor,
            CharacterActivity.Unknown => WorkspaceVisuals.ProblemColor,
            _ => WorkspaceVisuals.TextColor,
        };

        /// <param name="byHand">
        /// The step comes from the person's hands, which count only while the app has focus. The
        /// keyboard's result does not: focus may return a frame after the keyboard closes.
        /// </param>
        private void Steer(Opened workspace, Func<WorkspaceSteering, SteeringOutcome> step, bool byHand = true)
        {
            if (opened != workspace || workspace.Now == null || (byHand && FocusGuard.InputSuspended)) return;
            var outcome = step(workspace.Steering);
            switch (outcome.Step)
            {
                case SteeringStep.Send:
                    Submit(workspace, outcome.Command!);
                    break;
                case SteeringStep.Explain:
                    Notify(workspace, outcome.Message!);
                    break;
                case SteeringStep.Type:
                    OpenKeyboard(workspace);
                    break;
            }
            RefreshPanel();
        }

        private void Submit(Opened workspace, CommandEnvelope command)
        {
            var session = connection.Session;
            var execution = workspace.Now?.Execution;
            if (session == null || execution == null)
            {
                Notify(workspace, "Not sent: " + (connection.SetupProblem ?? "there is no session with the control plane."));
                return;
            }
            workspace.Notice = null;
            Report(submissions.SubmitAsync(sent => session.SubmitAsync(sent), command, execution.ExecutionId));
            if (WorkspaceActs.Of(command) is WorkspaceAct act) Acted?.Invoke(workspace.Character.WorkstreamId, act);
        }

        private static void Notify(Opened workspace, string notice)
        {
            workspace.Notice = notice;
            workspace.NoticeUntil = Time.unscaledTime + NoticeSeconds;
        }

        /// <summary>
        /// The Quest system keyboard (TouchScreenKeyboard with Require System Keyboard on). While it
        /// is open the app loses input focus, so the hands and the panel pause until it closes.
        /// Where no keyboard is supported, such as the editor, a few preset instructions stand in.
        /// </summary>
        private void OpenKeyboard(Opened workspace)
        {
            // The recorded demonstration follows only the instructions it recorded, so it offers those.
            var execution = workspace.Now?.Execution?.ExecutionId;
            workspace.Recorded = execution == null ? null : connection.DemonstrationInstructions(execution);
            if (workspace.Recorded?.Count > 0)
            {
                workspace.Steering.StopTyping();
                workspace.Presets = true;
                return;
            }
            if (TouchScreenKeyboard.isSupported)
            {
                workspace.Keyboard = TouchScreenKeyboard.Open("", TouchScreenKeyboardType.Default, true, false, false, false,
                    "Instruction for " + workspace.Now?.Character.Title);
            }
            if (workspace.Keyboard == null)
            {
                workspace.Steering.StopTyping();
                workspace.Presets = true;
            }
        }

        private void PollKeyboard()
        {
            var workspace = opened;
            var keyboard = workspace?.Keyboard;
            if (workspace == null || keyboard == null || keyboard.status == TouchScreenKeyboard.Status.Visible) return;
            workspace.Keyboard = null;
            if (keyboard.status == TouchScreenKeyboard.Status.Done)
            {
                var typed = keyboard.text;
                Steer(workspace, s => s.Typed(typed, workspace.Now!), byHand: false);
                return;
            }
            workspace.Steering.StopTyping();
            Notify(workspace, "The keyboard closed, so nothing was sent.");
            RefreshPanel();
        }

        private static void CloseKeyboard(Opened workspace)
        {
            if (workspace.Keyboard == null) return;
            workspace.Keyboard.active = false;
            workspace.Keyboard = null;
        }

        /// <summary>Where the workspace's sections read: the recorded demonstration while it is shown, else the control plane.</summary>
        private IIntelligenceReader? IntelligenceReader()
        {
            if (connection.DemonstrationReads is DemonstrationReads recorded) return recorded;
            return ControlPlaneSettings.Api();
        }

        /// <summary>Reads the workstream's history, which a snapshot does not carry, into the activity.</summary>
        private void ReadHistory(Opened workspace) => Report(ReadHistoryAsync(workspace));

        private async Task ReadHistoryAsync(Opened workspace)
        {
            var journal = connection.Session?.State.Journal?.JournalId;
            if (journal == null) return;
            if (connection.DemonstrationLine != null)
            {
                // The recorded demonstration plays its whole history through the session, and no
                // control plane serves its journal.
                workspace.HistoryNote = "";
                return;
            }
            var workstreamId = workspace.Character.WorkstreamId;
            var request = ++workspace.HistoryRequests;
            try
            {
                // The control plane configured now, over the pinned transport when paired (ADR 0017).
                var api = ControlPlaneSettings.Api() ?? throw new ControlPlaneRequestException("no control plane is configured.");
                workspace.HistoryNote = " · reading history…";
                RefreshPanel();
                var events = await api.ReadAllAsync(workstreamId, journal);
                if (request != workspace.HistoryRequests || journal != journalId) return;
                activity.Record(events);
                workspace.HistoryNote = "";
            }
            catch (Exception error)
            {
                if (request != workspace.HistoryRequests) return;
                // Live activity still arrives; say that the older part is missing, and why.
                workspace.HistoryNote = " · history unavailable: " + error.Message;
            }
            RefreshPanel();
        }

        /// <summary>The character turns to the person and looks at them while it is peeked at or open.</summary>
        private static void FacePerson(CharacterTarget target, bool facing)
        {
            if (target.View != null) target.View.LookAtPerson(facing);
        }

        private static (TimeZoneInfo, string) LocalClock()
        {
            try
            {
                return (TimeZoneInfo.Local, "");
            }
            catch (Exception)
            {
                return (TimeZoneInfo.Utc, " (times in UTC)");
            }
        }

        private static async void Report(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }

        /// <summary>The open workspace and what it is in the middle of.</summary>
        private sealed class Opened
        {
            public Opened(CharacterTarget character, WorkspacePanel panel, WorkspaceTransition transition, WorkspaceSteering steering)
            {
                Character = character;
                Panel = panel;
                Transition = transition;
                Steering = steering;
            }

            public CharacterTarget Character { get; }

            public WorkspacePanel Panel { get; }

            public WorkspaceTransition Transition { get; }

            public WorkspaceSteering Steering { get; }

            /// <summary>The tabs and details under the actions, and the whole request while a confirmation asks about one.</summary>
            public WorkspaceSections Sections { get; set; } = null!;

            /// <summary>The presentation last shown; presses are judged against it.</summary>
            public WorkspacePresentation? Now { get; set; }

            public TouchScreenKeyboard? Keyboard { get; set; }

            public bool Presets { get; set; }

            /// <summary>The instructions the recorded demonstration offers here, shown as the presets.</summary>
            public IReadOnlyList<PresetInstruction>? Recorded { get; set; }

            public string? Notice { get; set; }

            public float NoticeUntil { get; set; }

            public string HistoryNote { get; set; } = "";

            public int HistoryRequests { get; set; }
        }
    }
}

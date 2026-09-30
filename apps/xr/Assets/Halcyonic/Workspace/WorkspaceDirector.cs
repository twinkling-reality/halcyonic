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
    /// shows them. Peek: while the person looks at a character, or a hand points at it, one line
    /// beside it. Open: a pinch on the ray, or a poke, opens the workspace next to that character,
    /// within reach; collapsing returns to ambient. Everything shown comes from the client core
    /// (WorkspacePresenter, WorkspaceText) and the session; commands go through WorkspaceSteering
    /// and RealtimeSession.SubmitAsync, and a result is shown as done only when the control plane's
    /// record says the runtime confirmed it. No peek, hint or input while the app lacks focus
    /// (<see cref="FocusGuard"/>).
    /// </summary>
    [RequireComponent(typeof(ControlPlaneConnection), typeof(CharacterStage))]
    public sealed class WorkspaceDirector : MonoBehaviour
    {
        /// <summary>
        /// About two feet from the eyes: within a seated person's reach, so the workspace's buttons
        /// can be poked without leaning or standing, and near enough to read at its scaled size.
        /// </summary>
        private const float Reach = 0.6f;

        /// <summary>The angle between the character and the workspace's nearest edge.</summary>
        private const float ClearanceDegrees = 3f;

        /// <summary>The workspace opens no farther than this to the side of where the person looks.</summary>
        private const float MaxSideDegrees = 15f;

        /// <summary>Its center stays between these heights, in degrees from the eyes' level: the comfortable middle.</summary>
        private const float LowestDegrees = -24f;

        private const float HighestDegrees = 2f;

        /// <summary>Farther than a character's own motion: the stage moved it.</summary>
        private const float MovedFar = 0.3f;

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
        private CharacterTarget? peeked;
        private Opened? opened;
        private string? journalId;
        private ControlPlaneApi? api;
        private int shownSubmissions = -1;
        private float nextRefresh;

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
            if (GazeHover.Create(transform) == null)
            {
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this, "Halcyonic: {0}", "no gaze in this scene, so only hands peek");
            }
            // Characters the stage created before this component subscribed.
            var session = connection.Session;
            if (session == null) return;
            foreach (var workstreamId in session.State.Workstreams.Keys)
            {
                if (stage.TryGetCharacter(workstreamId, out var view)) Attach(workstreamId, view);
            }
        }

        private void OnDestroy() => api?.Dispose();

        private void Attach(string workstreamId, CharacterView view)
        {
            if (targets.TryGetValue(workstreamId, out var existing) && existing != null && existing.View == view) return;
            var target = CharacterTarget.Attach(view, workstreamId);
            targets[workstreamId] = target;
            target.Ray.HoverChanged += () => OnHoverChanged(target);
            target.Poke.HoverChanged += () => OnHoverChanged(target);
            target.Ray.Selected += () => OnSelected(target);
            target.Poke.Selected += () => OnSelected(target);
        }

        private void OnChanged(StateChanges changes)
        {
            var session = connection.Session;
            if (session == null) return;
            if (changes.Resynchronized)
            {
                var current = session.State.Journal?.JournalId;
                if (current != journalId)
                {
                    // Positions and commands from another journal mean nothing here.
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
                && Vector3.Distance(opened.Transition.PlacedBeside, opened.Character!.BodyPosition) > MovedFar)
            {
                // The stage moved the character, for example after a recenter: the workspace follows.
                var (place, scale) = PlaceBeside(opened.Character);
                opened.Transition.MoveTo(place, scale);
            }
            PollKeyboard();
            if (submissions.Version != shownSubmissions || Time.unscaledTime >= nextRefresh) Refresh();
        }

        private void Refresh()
        {
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            shownSubmissions = submissions.Version;
            RefreshPeek();
            RefreshHint();
            RefreshPanel();
        }

        private WorkspacePresentation? Present(string workstreamId)
        {
            var session = connection.Session;
            if (session == null || !session.State.Workstreams.TryGetValue(workstreamId, out var workstream)) return null;
            return WorkspacePresenter.Present(workstream, session.State, activity, session.Status.IsLive, submissions);
        }

        private void OnHoverChanged(CharacterTarget target)
        {
            FacePerson(target, target.Hovered || opened?.Character == target);
            RefreshPeek();
        }

        /// <summary>
        /// Which character to peek at: one a hand points at wins; otherwise, in the ambient view, the
        /// one the person looks at. While a workspace is open the gaze reads it, not what is behind it.
        /// </summary>
        private CharacterTarget? ChoosePeek()
        {
            if (peeked != null && peeked.HandHovered) return peeked;
            var pointed = targets.Values.FirstOrDefault(target => target != null && target.HandHovered);
            if (pointed != null || opened != null) return pointed;
            if (peeked != null && peeked.GazeHovered) return peeked;
            return targets.Values.FirstOrDefault(target => target != null && target.GazeHovered);
        }

        private void RefreshPeek()
        {
            peeked = FocusGuard.InputSuspended ? null : ChoosePeek();
            var presentation = peeked == null || opened?.Character == peeked ? null : Present(peeked.WorkstreamId);
            if (presentation == null || peeked == null)
            {
                peek.Hide();
                return;
            }
            peek.Show(peeked, WorkspaceText.Peek(presentation));
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
            RefreshPeek();
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
                Close(immediately: false);
                RefreshPeek();
            };
            FacePerson(target, true);
            ReadHistory(workspace);
            RefreshPanel();
        }

        /// <summary>
        /// Within reach, in the direction of the character and next to it in view, clear of its body:
        /// below it, or above it where below would leave the comfortable band (<see cref="Nearer"/>).
        /// The workspace keeps near the middle of the view (at most <see cref="MaxSideDegrees"/> to
        /// the side of where the person looks, and between <see cref="LowestDegrees"/> and
        /// <see cref="HighestDegrees"/>), faces the eyes, and is scaled to its designed angular size.
        /// </summary>
        private static (Pose Place, float Scale) PlaceBeside(CharacterTarget target)
        {
            var head = WorkspaceVisuals.HeadPosition;
            var toCharacter = target.BodyPosition - head;
            var flat = new Vector3(toCharacter.x, 0f, toCharacter.z);
            var characterSide = flat.sqrMagnitude > 1e-6f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : 0f;
            var characterHeight = Mathf.Atan2(toCharacter.y, Mathf.Max(flat.magnitude, 0.01f)) * Mathf.Rad2Deg;

            var looking = WorkspaceVisuals.Head != null ? WorkspaceVisuals.Head.forward : flat;
            var lookingSide = Mathf.Atan2(looking.x, looking.z) * Mathf.Rad2Deg;
            var side = lookingSide + Mathf.Clamp(Mathf.DeltaAngle(lookingSide, characterSide), -MaxSideDegrees, MaxSideDegrees);

            var scale = Reach / WorkspaceVisuals.PanelDistance;
            var halfHeight = Mathf.Atan2(WorkspacePanel.Height / 2f, WorkspaceVisuals.PanelDistance) * Mathf.Rad2Deg;
            // Clear of the character's body, whatever size the stage gives it.
            var bodyDegrees = Mathf.Atan2(CharacterView.BodyRadius * target.Scale, Mathf.Max(toCharacter.magnitude, 0.1f)) * Mathf.Rad2Deg;
            var offset = bodyDegrees + ClearanceDegrees + halfHeight;
            var height = Nearer(characterHeight - offset, characterHeight + offset);

            var direction = Quaternion.Euler(-height, side, 0f) * Vector3.forward;
            var center = head + direction * Reach;
            return (new Pose(center, Quaternion.LookRotation(direction, Vector3.up)), scale);
        }

        /// <summary>
        /// Below or above the character: whichever stays in the comfortable band, the lower one when
        /// both do; when neither does, the one that needs less moving, moved into the band, which may
        /// then cover part of the character.
        /// </summary>
        private static float Nearer(float below, float above)
        {
            bool Fits(float height) => height >= LowestDegrees && height <= HighestDegrees;
            if (Fits(below)) return below;
            if (Fits(above)) return above;
            float Moving(float height) => Mathf.Abs(height - Mathf.Clamp(height, LowestDegrees, HighestDegrees));
            return Mathf.Clamp(Moving(below) <= Moving(above) ? below : above, LowestDegrees, HighestDegrees);
        }

        private void Close(bool immediately)
        {
            var closing = opened;
            if (closing == null) return;
            opened = null;
            CloseKeyboard(closing);
            if (closing.Character != null) FacePerson(closing.Character, closing.Character.Hovered);
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
            if (workspace.Notice != null && Time.unscaledTime > workspace.NoticeUntil) workspace.Notice = null;
            workspace.Panel.Show(Content(workspace, presentation));
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
            }
            else if (workspace.Presets)
            {
                content.Mode = ControlsMode.Presets;
                content.Presets = WorkspaceText.PresetInstructions;
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
                if (api == null)
                {
                    var token = ControlPlaneSettings.ReadAccessToken()
                        ?? throw new ControlPlaneRequestException("no access token.");
                    api = new ControlPlaneApi(ControlPlaneApi.BaseUriFor(ControlPlaneSettings.Endpoint), token);
                }
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

            /// <summary>The presentation last shown; presses are judged against it.</summary>
            public WorkspacePresentation? Now { get; set; }

            public TouchScreenKeyboard? Keyboard { get; set; }

            public bool Presets { get; set; }

            public string? Notice { get; set; }

            public float NoticeUntil { get; set; }

            public string HistoryNote { get; set; } = "";

            public int HistoryRequests { get; set; }
        }
    }
}

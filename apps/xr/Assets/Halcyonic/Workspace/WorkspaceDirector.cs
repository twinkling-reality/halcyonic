#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Three levels of detail for the same work, all in place. Ambient: the characters as the stage
    /// shows them. Peek: once the person's gaze rests on a character, or at once while a hand points
    /// at it, a card under its label (<see cref="PeekChoice"/>, <see cref="PeekLabel"/>). Open: a
    /// pinch on the ray, a poke, or a pinch of either hand while the gaze peek shows (look and pinch)
    /// opens the workspace next to that character, within reach and clear of the others and their
    /// labels; collapsing returns to ambient.
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
        private SpriteRenderer reticle = null!;
        private OnboardingHint hint = null!;
        private readonly PeekChoice peekChoice = new PeekChoice();
        private readonly List<BodyInView> bodies = new List<BodyInView>();
        private GazeHover? gaze;
        private CharacterTarget? pointed;
        private string? loggedPointed;
        private string? loggedPeek;
        private CharacterTarget? facing;
        private string? peekFor;
        private PeekCard? peekCard;
        private Opened? opened;
        private HoldToTalk voice = null!;
        private HoldToTalk answerVoice = null!;
        private string? journalId;
        private int shownSubmissions = -1;
        private float nextRefresh;
        private string? pendingOpen;

        /// <summary>
        /// Raised with the workstream when the person opens its workspace, collapses it, or sends a
        /// command from it; the stage's sound follows it. A command raises it once handed to the
        /// session: sent, not confirmed, since the runtime's answer arrives later in the state.
        /// </summary>
        public event Action<string, WorkspaceAct>? Acted;

        /// <summary>Raised with the workstream when its workspace opens, however it was opened.</summary>
        public event Action<string>? WorkOpened;

        /// <summary>Raised with the workstream when its workspace closes, however it was closed.</summary>
        public event Action<string>? WorkClosed;

        /// <summary>The workstream whose workspace is open, or null.</summary>
        public string? OpenWorkstream => opened?.WorkstreamId;

        /// <summary>Every character's target, for placing other panels clear of them.</summary>
        public IEnumerable<CharacterTarget> Targets => targets.Values;

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
            reticle = WorkspaceVisuals.Plate(transform, "Head gaze reticle", Vector2.one * 0.008f,
                new Color(0.93f, 0.95f, 0.96f, 0.8f), WorkspaceVisuals.ControlOrder);
            reticle.gameObject.SetActive(false);
            hint = OnboardingHint.Create(transform);
            // Hold to talk for an instruction, in development builds: always confirmed as heard before it is sent.
            voice = gameObject.AddComponent<HoldToTalk>();
            voice.Said += words =>
            {
                if (opened == null) return;
                Notify(opened, words);
                RefreshPanel();
            };
            voice.Heard += text =>
            {
                if (opened != null) Steer(opened, s => s.Spoken(text, opened.Now!), byHand: false);
            };
            // Hold to talk for a typed answer: what the Mac heard becomes the draft, sent only by Send answer.
            answerVoice = gameObject.AddComponent<HoldToTalk>();
            answerVoice.Said += words =>
            {
                if (opened == null) return;
                Notify(opened, words);
                RefreshPanel();
            };
            answerVoice.Heard += text =>
            {
                var workspace = opened;
                if (workspace?.Draft == null) return;
                Notify(workspace, workspace.Draft.Type(workspace.AnswerPrompt, text) ?? VoiceText.HeardAnswer);
                RefreshPanel();
            };
        }

        private void OnEnable()
        {
            connection.Changed += OnChanged;
            stage.CharacterCreated += Attach;
            FocusGuard.Left += OnFocusLeft;
        }

        private void OnDisable()
        {
            connection.Changed -= OnChanged;
            stage.CharacterCreated -= Attach;
            FocusGuard.Left -= OnFocusLeft;
        }

        /// <summary>
        /// Focus went to another window: a confirmation half done is dropped and said so, to be given
        /// afresh once back. The runtime's request stays pending; nothing is sent.
        /// </summary>
        private void OnFocusLeft()
        {
            var workspace = opened;
            if (workspace == null) return;
            var outcome = workspace.Steering.FocusLeft();
            if (outcome.Step != SteeringStep.Explain) return;
            Notify(workspace, outcome.Message!);
            RefreshPanel();
        }

        private void Start()
        {
            if (GetComponent<ProjectRail>() == null) gameObject.AddComponent<ProjectRail>();
            if (GetComponent<UsageLeftGlance>() == null) gameObject.AddComponent<UsageLeftGlance>();
            gaze = GazeHover.Create(transform, () => (peekChoice.PinchTarget, peekChoice.PinchBlock));
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

        /// <summary>
        /// Opens a workstream's workspace whether or not it has a character now, as More work and
        /// Open now ask: the stage gives it one first (<see cref="CharacterStage.Request"/>), and the
        /// workspace opens beside it on the next frame, once it stands in its slot. Nothing is sent.
        /// </summary>
        public void OpenWork(string workstreamId)
        {
            if (FocusGuard.InputSuspended) return;
            if (opened?.WorkstreamId == workstreamId) return;
            Close(immediately: false);
            pendingOpen = workstreamId;
            stage.Request(workstreamId);
        }

        /// <summary>Opens the work <see cref="OpenWork"/> asked for once its character stands on the stage, or forgets it once it is gone.</summary>
        private void OpenPending()
        {
            var id = pendingOpen;
            if (id == null) return;
            if (connection.Session?.State.Workstreams.ContainsKey(id) != true)
            {
                pendingOpen = null;
                return;
            }
            if (!targets.TryGetValue(id, out var target) || target == null) return;
            pendingOpen = null;
            if (opened?.Character != target) Open(target);
        }

        /// <summary>Collapses the open workspace, if one is open, as the entry panel does before it opens in the same place.</summary>
        public void CloseWork()
        {
            if (opened == null) return;
            Acted?.Invoke(opened.WorkstreamId, WorkspaceAct.Collapse);
            Close(immediately: false);
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
            // Folded while another window keeps focus; back exactly as it was when focus returns.
            var shown = opened?.Transition;
            if (shown != null && shown.gameObject.activeSelf == FocusGuard.Folded) shown.gameObject.SetActive(!FocusGuard.Folded);
            if (opened != null && opened.Transition != null
                && Vector3.Distance(opened.Transition.PlacedBeside, opened.Character!.BodyPosition) > MovedFar * opened.Character.Scale)
            {
                // The stage moved the character, for example after a recenter: the workspace follows.
                var (place, scale) = PlaceBeside(opened.Character);
                opened.Transition.MoveTo(place, scale);
            }
            PollKeyboard();
            OpenPending();
            if (submissions.Version != shownSubmissions || Time.unscaledTime >= nextRefresh) Refresh();
            UpdatePeek();
        }

        private void Refresh()
        {
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            shownSubmissions = submissions.Version;
            // The peek's card is written again on its next frame.
            peekFor = null;
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
        /// person. Allocates nothing unless the peek's card changes.
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
            var pointedId = pointed?.WorkstreamId;
            if (pointedId != loggedPointed)
            {
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this,
                    "Halcyonic interaction: hand character target {0}", pointedId ?? "none");
                loggedPointed = pointedId;
            }
            if (head != null && !FocusGuard.InputSuspended && opened == null)
            {
                var depth = gazed == null ? 1f : Mathf.Clamp(
                    Vector3.Distance(head.position, gazed.BodyPosition) - CharacterView.BodyRadius * gazed.Scale - 0.03f,
                    0.4f, 2f);
                reticle.transform.SetPositionAndRotation(head.position + headForward * depth, head.rotation);
                reticle.gameObject.SetActive(true);
            }
            else reticle.gameObject.SetActive(false);
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
            if (shown != null && peekFor != shown.WorkstreamId)
            {
                var presentation = Present(shown.WorkstreamId);
                peekFor = shown.WorkstreamId;
                peekCard = presentation == null ? null : PeekCard.Of(presentation);
            }
            if (shown == null || peekCard == null || peekChoice.Opacity <= 0f)
            {
                peek.Hide();
                loggedPeek = null;
            }
            else
            {
                // Under the label, unless a panel is open under the labels or the labels rest on a surface.
                peek.Show(shown, targets.Values, peekCard, peekChoice.Opacity, opened != null || AmbientCover.PanelShowing || stage.SurfaceHeight.HasValue);
                if (shown.WorkstreamId != loggedPeek)
                {
                    Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this,
                        "Halcyonic interaction: peek shown {0} source {1}", shown.WorkstreamId, peekChoice.Source);
                    loggedPeek = shown.WorkstreamId;
                }
            }

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
            if (opened != null || gaze == null || gaze.Armed != target.WorkstreamId)
            {
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this,
                    "Halcyonic interaction: look pinch refused for {0}: selection changed", target.WorkstreamId);
                return;
            }
            if (!Open(target))
            {
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this,
                    "Halcyonic interaction: look pinch refused for {0}: no presentation", target.WorkstreamId);
                return;
            }
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this,
                "Halcyonic interaction: look pinch accepted for {0}", target.WorkstreamId);
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

        private bool Open(CharacterTarget target)
        {
            var presentation = Present(target.WorkstreamId);
            if (presentation == null) return false;
            OnboardingHint.Learned();
            hint.Hide();

            var root = new GameObject("Workspace " + target.WorkstreamId);
            root.transform.SetParent(transform, false);
            AmbientCover.Add(root, panel: true);
            var panel = WorkspacePanel.Create(root.transform);
            var (place, scale) = PlaceBeside(target);
            var transition = WorkspaceTransition.Begin(root, target, place, scale);
            var workspace = new Opened(target, panel, transition, new WorkspaceSteering(commands));
            opened = workspace;
            // Work the person just opened keeps its character a while after closing (CharacterLineup.KeepFor).
            stage.Keep(target.WorkstreamId);
            Acted?.Invoke(target.WorkstreamId, WorkspaceAct.Open);
            workspace.Sections = WorkspaceSections.Attach(panel, () => workspace.Now, IntelligenceReader, WorkspaceText.FirstQuestion(presentation),
                () => workspace.Draft, () => HoldToTalk.Offered && connection.DemonstrationReads == null);
            workspace.Sections.TypeAnswer += prompt => OpenAnswerKeyboard(workspace, prompt);
            workspace.Sections.Asking.SpeakStarted += prompt =>
            {
                if (opened != workspace) return;
                workspace.AnswerPrompt = prompt;
                answerVoice.Begin();
            };
            workspace.Sections.Asking.SpeakEnded += answerVoice.End;
            workspace.Sections.Asking.SpeakTapped += () =>
            {
                Notify(workspace, VoiceText.TooShort);
                RefreshPanel();
            };
            workspace.Sections.RequestTurned += () =>
            {
                if (opened == workspace) RefreshPanel();
            };
            panel.Accepting = () => opened == workspace && transition.Open;
            // Only Send answer sends an answer, with the answers chosen; no other gesture does.
            panel.ActionPressed += action => Steer(workspace, s => action == WorkspaceAction.Answer && workspace.Draft != null
                ? s.SendAnswer(workspace.Draft, workspace.Now!)
                : s.Press(action, workspace.Now!));
            panel.ConfirmPressed += () => Steer(workspace, s => s.Confirm(workspace.Now!));
            panel.PresetPressed += preset =>
            {
                workspace.Presets = false;
                Steer(workspace, s => s.Typed(preset.Text, workspace.Now!));
            };
            panel.SpeakStarted += () =>
            {
                if (opened == workspace) voice.Begin();
            };
            panel.SpeakEnded += voice.End;
            panel.SpeakTapped += () =>
            {
                Notify(workspace, VoiceText.TooShort);
                RefreshPanel();
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
            if (opened == workspace) WorkOpened?.Invoke(target.WorkstreamId);
            return opened == workspace;
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
            voice.Drop();
            answerVoice.Drop();
            if (closing.Character != null) FacePerson(closing.Character, closing.Character == facing);
            // Gone already when its character left the stage.
            if (closing.Transition != null) closing.Transition.Collapse(immediately || closing.Character == null);
            WorkClosed?.Invoke(closing.WorkstreamId);
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
            // The answers chosen belong to one question; another question starts afresh.
            var asked = presentation.QuestionToAnswer;
            var execution = presentation.Execution?.ExecutionId;
            if (asked == null || execution == null) workspace.Draft = null;
            else if (workspace.Draft == null || !workspace.Draft.Answers(execution, asked)) workspace.Draft = new QuestionDraft(execution, asked);
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
                Goal = WorkspaceText.Goal(presentation),
                Answer = WorkspaceText.Answer(presentation),
                AnswerColor = character.AttentionNotes.Count == 0 ? WorkspaceVisuals.SecondaryColor
                    : character.Attention == AttentionLevel.ActionRequired ? WorkspaceVisuals.AttentionColor : ToneOf(character.Activity),
                Actions = presentation.Actions.ToList(),
                Speak = HoldToTalk.Offered && connection.DemonstrationReads == null && presentation.Actions.Contains(WorkspaceAction.Instruct),
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
            // An answer sent shows how it goes with the activity: sent, then taken, refused or not confirmed.
            if (command is ExecutionAnswerQuestionCommand) workspace.Sections.Show(WorkspaceQuestion.Doing);
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
                workspace.Keyboard = FocusGuard.Track(TouchScreenKeyboard.Open("", TouchScreenKeyboardType.Default, true, false, false, false,
                    "Instruction for " + workspace.Now?.Character.Title));
            }
            if (workspace.Keyboard == null)
            {
                workspace.Steering.StopTyping();
                workspace.Presets = true;
            }
        }

        /// <summary>The system keyboard for a typed answer to a prompt; the text becomes part of the draft, never sent by itself.</summary>
        private void OpenAnswerKeyboard(Opened workspace, int prompt)
        {
            if (opened != workspace || FocusGuard.InputSuspended || workspace.Draft == null) return;
            if (!TouchScreenKeyboard.isSupported)
            {
                Notify(workspace, "Typing an answer needs the headset's system keyboard.");
                RefreshPanel();
                return;
            }
            workspace.AnswerPrompt = prompt;
            workspace.AnswerKeyboard = FocusGuard.Track(TouchScreenKeyboard.Open(workspace.Draft.Typed(prompt) ?? "", TouchScreenKeyboardType.Default,
                true, false, false, false, "Your answer"));
        }

        private void PollAnswerKeyboard()
        {
            var workspace = opened;
            var keyboard = workspace?.AnswerKeyboard;
            if (workspace == null || keyboard == null || keyboard.status == TouchScreenKeyboard.Status.Visible) return;
            workspace.AnswerKeyboard = null;
            if (keyboard.status != TouchScreenKeyboard.Status.Done || workspace.Draft == null) return;
            if (workspace.Draft.Type(workspace.AnswerPrompt, keyboard.text) is string problem) Notify(workspace, problem);
            RefreshPanel();
        }

        private void PollKeyboard()
        {
            PollAnswerKeyboard();
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
            if (workspace.AnswerKeyboard != null)
            {
                workspace.AnswerKeyboard.active = false;
                workspace.AnswerKeyboard = null;
            }
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
                WorkstreamId = character.WorkstreamId;
                Panel = panel;
                Transition = transition;
                Steering = steering;
            }

            public CharacterTarget Character { get; }

            /// <summary>Kept apart from the character, which the stage may destroy while it is open.</summary>
            public string WorkstreamId { get; }

            public WorkspacePanel Panel { get; }

            public WorkspaceTransition Transition { get; }

            public WorkspaceSteering Steering { get; }

            /// <summary>The tabs and details under the actions, and the whole request while a confirmation asks about one.</summary>
            public WorkspaceSections Sections { get; set; } = null!;

            /// <summary>The presentation last shown; presses are judged against it.</summary>
            public WorkspacePresentation? Now { get; set; }

            public TouchScreenKeyboard? Keyboard { get; set; }

            /// <summary>The person's answers to the agent's question shown, kept until another question shows.</summary>
            public QuestionDraft? Draft { get; set; }

            /// <summary>The system keyboard open for a typed answer, and the prompt it answers.</summary>
            public TouchScreenKeyboard? AnswerKeyboard { get; set; }

            public int AnswerPrompt { get; set; }

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

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
    /// opens the task's file beside the menu (ADR 0026); collapsing returns to ambient.
    /// It keeps the realtime session, what the journal says and the one way anything is sent, and hosts
    /// the menu's director (<see cref="MenuDirector"/>), which draws every column on one plane. A task's
    /// file is a <see cref="FileColumn"/>: its rules decide every send, which goes through
    /// <see cref="CommandSubmissions"/> here, so what is in flight shows as sent. No peek, hint or
    /// input while the app lacks focus (<see cref="FocusGuard"/>).
    /// </summary>
    [RequireComponent(typeof(ControlPlaneConnection), typeof(CharacterStage))]
    public sealed class WorkspaceDirector : MonoBehaviour
    {
        /// <summary>Farther than a character's own motion, in its units: the stage moved it.</summary>
        private const float MovedFar = 1.5f * CharacterView.BodyRadius;

        private const float RefreshSeconds = 0.5f;

        /// <summary>Activity times are local; if the platform cannot tell the zone, they are UTC and say so.</summary>
        private static readonly (TimeZoneInfo Zone, string Note) Clock = LocalClock();

        private readonly ActivityLog activity = new ActivityLog();
        private readonly CommandSubmissions submissions = new CommandSubmissions();
        private readonly Dictionary<string, CharacterTarget> targets = new Dictionary<string, CharacterTarget>();

        /// <summary>The characters standing now, gathered every frame for the peek into one list, so no frame allocates.</summary>
        private readonly List<CharacterTarget> standing = new List<CharacterTarget>();
        private ControlPlaneConnection connection = null!;
        private CharacterStage stage = null!;
        private CommandFactory commands = null!;
        private PeekLabel peek = null!;
        private SpriteRenderer reticle = null!;
        private OnboardingHint hint = null!;
        private readonly PeekChoice peekChoice = new PeekChoice();
        private GazeHover? gaze;
        private CharacterTarget? pointed;
        private string? loggedPointed;
        private string? loggedPeek;
        private CharacterTarget? facing;
        private string? peekFor;
        private PeekCard? peekCard;
        private HoldToTalk voice = null!;
        private MenuDirector menu = null!;
        private string? journalId;
        private int shownSubmissions = -1;
        private float nextRefresh;
        private string? pendingOpen;
        private string? shownBeside;
        private Vector3 besidePlacedAt;
        private string historyNote = "";
        private int historyRequests;
        private TouchScreenKeyboard? keyboard;
        private Action<string>? keyboardDone;
        private WorkOverview? overview;
        private (long Position, int Visibility) overviewOf = (-1, -1);

        /// <summary>
        /// Raised with the workstream when the person opens its file, collapses it, or sends a command
        /// from it; the stage's sound follows it. A command raises it once handed to the session: sent,
        /// not confirmed, since the runtime's answer arrives later in the state.
        /// </summary>
        public event Action<string, WorkspaceAct>? Acted;

        /// <summary>Raised with the workstream when its file opens, however it was opened.</summary>
        public event Action<string>? WorkOpened;

        /// <summary>Raised with the workstream when its file closes, however it was closed.</summary>
        public event Action<string>? WorkClosed;

        /// <summary>The workstream whose file is open beside the menu, or null.</summary>
        public string? OpenWorkstream => menu != null ? menu.Navigator.BesideTask : null;

        /// <summary>Every character's target, for placing other panels clear of them.</summary>
        public IEnumerable<CharacterTarget> Targets => targets.Values;

        /// <summary>The menu's director this hosts.</summary>
        public MenuDirector Menu => menu;

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
            // Hold to talk's one voice, in development builds; the menu tells the held column what it heard.
            voice = gameObject.AddComponent<HoldToTalk>();
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
            if (GetComponent<ProjectRail>() == null) gameObject.AddComponent<ProjectRail>();
            if (GetComponent<UsageLeftGlance>() == null) gameObject.AddComponent<UsageLeftGlance>();
            var comfort = GetComponent<ComfortControls>() ?? gameObject.AddComponent<ComfortControls>();
            menu = MenuDirector.Create(transform, new MenuDirector.Setup
            {
                State = () => connection.Session?.State,
                Connected = () => connection.Session?.Status.IsLive == true,
                Session = () => connection.Session,
                Submit = Submit,
                Api = () => ControlPlaneSettings.Api(),
                Demonstration = () => connection.DemonstrationLine != null,
                Keyboard = OpenKeyboard,
                KeyboardOffered = () => TouchScreenKeyboard.isSupported,
                Voice = voice,
                StageNow = StageNow,
                CharacterOf = task => targets.TryGetValue(task, out var target) && target != null ? target : null,
                Bar = place => TasksColumn.Bar(place, connection.Session?.State),
                SomethingWaits = SomethingWaits,
                Comfort = comfort.Settings,
                ComfortSaved = comfort.Keep,
                Commands = commands,
                Overview = Overview,
                ShowProject = ShowProject,
                RecordedUsage = now => connection.DemonstrationUsageLimits(now),
                MakeNewProject = host => NewProjectColumn.Create(host, commands),
                File = FileFor,
            });
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
        /// Opens a workstream's file whether or not it has a character now, as More work and Open now
        /// ask: the stage gives it one first (<see cref="CharacterStage.Request"/>), and the file opens
        /// beside the menu once it stands in its slot. Nothing is sent.
        /// </summary>
        public void OpenWork(string workstreamId)
        {
            if (FocusGuard.InputSuspended) return;
            if (OpenWorkstream == workstreamId) return;
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
            Open(target);
        }

        /// <summary>Collapses the open file, if one is open, as the entry panel does before it opens in the same place.</summary>
        public void CloseWork()
        {
            if (OpenWorkstream is not string open) return;
            Acted?.Invoke(open, WorkspaceAct.Collapse);
            menu.Navigator.CloseBeside();
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
                if (OpenWorkstream is string open) ReadHistory(open);
            }
            activity.Record(changes.Events);
            Refresh();
        }

        private void Update()
        {
            PollKeyboard();
            OpenPending();
            FollowBeside();
            if (submissions.Version != shownSubmissions || Time.unscaledTime >= nextRefresh) Refresh();
            UpdatePeek();
        }

        /// <summary>
        /// What opens or closes beside the menu: its character faces the person while it shows, is kept
        /// on the stage a while after, and the others learn of it. When the stage moves the character,
        /// the plane re-centres on it.
        /// </summary>
        private void FollowBeside()
        {
            var beside = OpenWorkstream;
            if (beside != shownBeside)
            {
                if (shownBeside is string closed)
                {
                    if (targets.TryGetValue(closed, out var was) && was != null) FacePerson(was, was == facing);
                    WorkClosed?.Invoke(closed);
                }
                shownBeside = beside;
                if (beside != null)
                {
                    if (targets.TryGetValue(beside, out var now) && now != null)
                    {
                        FacePerson(now, true);
                        besidePlacedAt = now.BodyPosition;
                    }
                    WorkOpened?.Invoke(beside);
                }
            }
            if (beside != null && targets.TryGetValue(beside, out var character) && character != null
                && Vector3.Distance(besidePlacedAt, character.BodyPosition) > MovedFar * character.Scale)
            {
                besidePlacedAt = character.BodyPosition;
                menu.Redraw();
            }
        }

        private void Refresh()
        {
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            shownSubmissions = submissions.Version;
            // The peek's card is written again on its next frame.
            peekFor = null;
            RefreshHint();
        }

        private WorkspacePresentation? Present(string workstreamId)
        {
            var session = connection.Session;
            if (session == null || !session.State.Workstreams.TryGetValue(workstreamId, out var workstream)) return null;
            return WorkspacePresenter.Present(workstream, session.State, activity, session.Status.IsLive, submissions);
        }

        /// <summary>
        /// A task's file as a column beside the menu: its rules decide every send, which goes through
        /// <see cref="Submit"/>; its words come from the presentation of the work as it stands, its own
        /// commands in flight included.
        /// </summary>
        private IMenuColumn? FileFor(IMenuHost host, string workstreamId)
        {
            if (Present(workstreamId) == null) return null;
            OnboardingHint.Learned();
            hint.Hide();
            // Work the person just opened keeps its character a while after closing (CharacterLineup.KeepFor).
            stage.Keep(workstreamId);
            Acted?.Invoke(workstreamId, WorkspaceAct.Open);
            ReadHistory(workstreamId);
            return new FileColumn(host, () => Present(workstreamId), commands, IntelligenceReader,
                execution => connection.DemonstrationInstructions(execution), () => Clock.Note + historyNote,
                act => Acted?.Invoke(workstreamId, act));
        }

        /// <summary>
        /// The one way anything is sent: to <paramref name="madeIn"/>, the session the column was made
        /// in, and only while it is still the session shown and live, so a column made in the
        /// demonstration never reaches a real control plane, nor one made before a re-pairing the new
        /// one. It goes through <see cref="CommandSubmissions"/>, so a command still on its way shows as
        /// sent and a second decision waits for the first. Nothing is queued to send later. Null, sending
        /// nothing, otherwise.
        /// </summary>
        private Task<CommandAckMessage>? Submit(RealtimeSession madeIn, CommandEnvelope command)
        {
            if (!ReferenceEquals(madeIn, connection.Session) || !madeIn.Status.IsLive) return null;
            var acknowledged = new TaskCompletionSource<CommandAckMessage>();
            Report(SubmitAsync(madeIn, command, acknowledged));
            Refresh();
            return acknowledged.Task;
        }

        private async Task SubmitAsync(RealtimeSession session, CommandEnvelope command, TaskCompletionSource<CommandAckMessage> acknowledged)
        {
            // The session's send is called within this call, before anything is awaited: it refuses a
            // connection welcomed to another journal than the one shown, and only checks that now.
            await submissions.SubmitAsync(async sent =>
            {
                var ack = await session.SubmitAsync(sent);
                acknowledged.TrySetResult(ack);
                return ack;
            }, command, ExecutionOf(command));
            // Not sent, or sent with an outcome nobody knows: the submissions say so in the activity.
            acknowledged.TrySetCanceled();
        }

        /// <summary>The execution a command acts on, for the submissions' record; empty for one that acts on no execution.</summary>
        private static string ExecutionOf(CommandEnvelope command) => command switch
        {
            ExecutionRespondToApprovalCommand approval => approval.Payload.ExecutionId,
            ExecutionAnswerQuestionCommand answer => answer.Payload.ExecutionId,
            ExecutionInterruptCommand interrupt => interrupt.Payload.ExecutionId,
            ExecutionSendInstructionCommand instruction => instruction.Payload.ExecutionId,
            _ => "",
        };

        private MenuDirector.Stage StageNow()
        {
            var head = WorkspaceVisuals.Head;
            standing.Clear();
            foreach (var target in targets.Values)
            {
                if (target != null) standing.Add(target);
            }
            return new MenuDirector.Stage(WorkspaceVisuals.HeadPosition, head != null ? head.forward : Vector3.forward, standing.ToList(),
                stage.SurfaceHeight, stage.BesideAWindow);
        }

        private bool SomethingWaits() =>
            connection.Session?.State.Workstreams.Values.Any(workstream => workstream.Attention.Level == AttentionLevel.ActionRequired) == true;

        /// <summary>Every project and its work as the stage counts it, the same object until the state or what the stage shows changes.</summary>
        private WorkOverview? Overview()
        {
            var state = connection.Session?.State;
            var rail = GetComponent<ProjectRail>();
            if (state == null || rail == null) return null;
            var key = (state.Position, rail.Visibility.Version);
            if (overview == null || key != overviewOf)
            {
                overview = WorkOverview.Of(state, rail.Visibility, id => stage.SlotOf(id) >= 0);
                overviewOf = key;
            }
            return overview;
        }

        /// <summary>Shows or hides a project's work on the stage, kept on this device, as the rail's Show and Hide do.</summary>
        private void ShowProject(string projectId, bool shown)
        {
            var rail = GetComponent<ProjectRail>();
            if (rail == null) return;
            if (rail.Visibility.Shows(projectId) != shown) rail.ToggleProject(projectId);
        }

        /// <summary>
        /// The Quest system keyboard (TouchScreenKeyboard with Require System Keyboard on), holding
        /// <paramref name="text"/>. While it is open the app loses input focus, so the hands and the
        /// plane pause until it closes; <paramref name="done"/> only when the person finishes.
        /// </summary>
        private void OpenKeyboard(string text, string prompt, Action<string> done)
        {
            if (!TouchScreenKeyboard.isSupported || FocusGuard.InputSuspended) return;
            if (keyboard != null) keyboard.active = false;
            keyboardDone = done;
            keyboard = FocusGuard.Track(TouchScreenKeyboard.Open(text, TouchScreenKeyboardType.Default, true, false, false, false, prompt));
        }

        private void PollKeyboard()
        {
            var open = keyboard;
            if (open == null || open.status == TouchScreenKeyboard.Status.Visible) return;
            keyboard = null;
            var done = keyboardDone;
            keyboardDone = null;
            if (open.status == TouchScreenKeyboard.Status.Done) done?.Invoke(open.text);
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
            var open = OpenWorkstream;
            // A hand keeps the character it points at until it leaves, then takes any other.
            if (pointed == null || !pointed.HandHovered) pointed = null;
            CharacterTarget? gazed = null;
            standing.Clear();
            foreach (var target in targets.Values)
            {
                if (target == null) continue;
                standing.Add(target);
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
            if (head != null && !FocusGuard.InputSuspended && open == null)
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
                Open = open,
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
                peek.Show(shown, standing, peekCard, peekChoice.Opacity, open != null || AmbientCover.PanelShowing || stage.SurfaceHeight.HasValue,
                    stage.BesideAWindow);
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
            if (facing != null) FacePerson(facing, open != null && facing.WorkstreamId == open);
            facing = wanted;
            if (facing != null) FacePerson(facing, true);
        }

        /// <summary>A look and pinch: the gaze interactor selected a character on a pinch, while its gaze peek showed.</summary>
        private void OnLookAndPinch(CharacterTarget target)
        {
            if (OpenWorkstream != null || gaze == null || gaze.Armed != target.WorkstreamId)
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
        /// Until the person first opens a file, a small pinch cue above the first character that needs
        /// them; nothing while a file is open or the app lacks focus.
        /// </summary>
        private void RefreshHint()
        {
            var session = connection.Session;
            if (!OnboardingHint.Needed || OpenWorkstream != null || FocusGuard.InputSuspended || session == null)
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

        /// <summary>A character chosen: its file opens beside the menu, or, already open, closes.</summary>
        private void OnSelected(CharacterTarget target)
        {
            if (FocusGuard.InputSuspended) return;
            if (OpenWorkstream == target.WorkstreamId)
            {
                Acted?.Invoke(target.WorkstreamId, WorkspaceAct.Collapse);
                menu.Navigator.CloseBeside();
                return;
            }
            Open(target);
        }

        private bool Open(CharacterTarget target)
        {
            if (Present(target.WorkstreamId) == null) return false;
            menu.OpenFile(target.WorkstreamId);
            return OpenWorkstream == target.WorkstreamId;
        }

        /// <summary>Where the file's sections read: the recorded demonstration while it is shown, else the control plane.</summary>
        private IIntelligenceReader? IntelligenceReader()
        {
            if (connection.DemonstrationReads is DemonstrationReads recorded) return recorded;
            return ControlPlaneSettings.Api();
        }

        /// <summary>Reads the workstream's history, which a snapshot does not carry, into the activity.</summary>
        private void ReadHistory(string workstreamId) => Report(ReadHistoryAsync(workstreamId));

        private async Task ReadHistoryAsync(string workstreamId)
        {
            var journal = connection.Session?.State.Journal?.JournalId;
            if (journal == null) return;
            if (connection.DemonstrationLine != null)
            {
                // The recorded demonstration plays its whole history through the session, and no
                // control plane serves its journal.
                historyNote = "";
                return;
            }
            var request = ++historyRequests;
            try
            {
                // The control plane configured now, over the pinned transport when paired (ADR 0017).
                var api = ControlPlaneSettings.Api() ?? throw new ControlPlaneRequestException("no control plane is configured.");
                historyNote = " · reading earlier activity…";
                var events = await api.ReadAllAsync(workstreamId, journal);
                if (request != historyRequests || journal != journalId) return;
                activity.Record(events);
                historyNote = "";
            }
            catch (Exception error)
            {
                if (request != historyRequests) return;
                // Live activity still arrives; say that the older part is missing, and how to read it again.
                historyNote = " · earlier activity unavailable, reopen to try again";
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this, "Halcyonic: {0}", "earlier activity could not be read: " + error.GetType().Name);
            }
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
    }
}

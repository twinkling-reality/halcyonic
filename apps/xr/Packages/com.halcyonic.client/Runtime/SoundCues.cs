#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The stage's sounds, in the soundbook's order, named by the words the person reads: a state's
    /// cue by its state's word, an act's by its button's. Each has a visual twin on the stage, so
    /// sound is never the only signal (docs/internal/architecture/XR_CLIENT.md, "Sound").
    /// </summary>
    public enum SoundCue
    {
        /// <summary>A round starts: a soft double tap, like the hop.</summary>
        Working,

        /// <summary>A test run starts: four muted taps, up and back.</summary>
        CheckingItsWork,

        /// <summary>It rises and looks at the person: two strikes rising; the second rings on.</summary>
        WaitingForYou,

        /// <summary>It settles: the pair falling onto its own note. A finished round proves nothing, so no celebration.</summary>
        FinishedThisRound,

        /// <summary>The round failed: a dull, cracked strike, then a lower one.</summary>
        CouldNotFinish,

        /// <summary>Halcyonic cannot see the work right now: a strike whose pitch will not settle.</summary>
        CantTellYet,

        /// <summary>Interrupted, as the runtime confirmed: a strike caught by a hand.</summary>
        Stopped,

        /// <summary>The connection dropped: all six notes through a wall, fading; one cue for the whole room.</summary>
        LastKnown,

        /// <summary>The person expands a bot into its workspace: a chord unfolding toward them.</summary>
        Open,

        /// <summary>The person closes the workspace back into the bot: the chord folding back.</summary>
        Close,

        /// <summary>Sent, not yet confirmed: two notes struck together, open and warm.</summary>
        Approve,

        /// <summary>A decision, not an error: a short step down, damped.</summary>
        Deny,

        /// <summary>The person's words were sent: three light taps.</summary>
        TellIt,

        /// <summary>The stop was sent; the bot confirms later: a hand pressed flat on it.</summary>
        Stop,

        /// <summary>A control took a press that has no cue of its own: one soft felt tap.</summary>
        Touch,

        /// <summary>A control refused a press, as one unavailable now does: Deny's damped step, a note lower and quieter.</summary>
        NotNow,
    }

    /// <summary>Where a cue sounds from.</summary>
    public enum CuePlace
    {
        /// <summary>The character's body, where it stands on the stage.</summary>
        Character,

        /// <summary>In front of the person, from the workspace: the person's own actions.</summary>
        Workspace,

        /// <summary>The whole stage at once: the room's one cue when the connection drops.</summary>
        Stage,

        /// <summary>The control the person pressed, where it stands: its tap, or its refusal.</summary>
        Control,
    }

    /// <summary>What the person did with a workstream's workspace.</summary>
    public enum WorkspaceAct
    {
        /// <summary>Opened it from its character.</summary>
        Open,

        /// <summary>Collapsed it back into its character.</summary>
        Collapse,

        /// <summary>Sent an approval.</summary>
        Approve,

        /// <summary>Sent a denial.</summary>
        Deny,

        /// <summary>Sent an instruction.</summary>
        Instruct,

        /// <summary>Sent a request to stop the turn.</summary>
        Interrupt,
    }

    public static class WorkspaceActs
    {
        /// <summary>The act a command sent from a workspace stands for, or null for any other command.</summary>
        public static WorkspaceAct? Of(CommandEnvelope command) => command switch
        {
            ExecutionRespondToApprovalCommand answer =>
                answer.Payload.Decision == ApprovalDecision.Approve ? WorkspaceAct.Approve : WorkspaceAct.Deny,
            ExecutionSendInstructionCommand _ => WorkspaceAct.Instruct,
            ExecutionInterruptCommand _ => WorkspaceAct.Interrupt,
            _ => null,
        };
    }

    /// <summary>A cue to play: which, for whom, from where and when.</summary>
    public sealed class CueOnset
    {
        public CueOnset(SoundCue cue, string? workstreamId, int bot, CuePlace place, double at)
        {
            Cue = cue;
            WorkstreamId = workstreamId;
            Bot = bot;
            Place = place;
            At = at;
        }

        public SoundCue Cue { get; }

        /// <summary>
        /// The workstream whose character sounds it, or in whose workspace the person acted; null for
        /// the stage's cue and a control's.
        /// </summary>
        public string? WorkstreamId { get; }

        /// <summary>
        /// The bot whose note it plays, 0 to 5 into <see cref="GlazeSynthesizer.Homes"/>; -1 for a cue
        /// with one render: the stage's, which plays every bot's note, and a control's, on the key's.
        /// </summary>
        public int Bot { get; }

        public CuePlace Place { get; }

        /// <summary>When it starts, in seconds on the clock the selector was given.</summary>
        public double At { get; }
    }

    /// <summary>
    /// Chooses what the stage sounds, following the soundbook's rules. A character sounds when its
    /// state changes to one the person might act on, or when work starts, from its own place and on
    /// its own note. The person's own actions sound in front of them and say only that a command was
    /// sent: the result arrives later as the character's own cue, because an accepted command is not
    /// success. A dropped connection is one cue for the whole room, once per loss. Cues start at least
    /// <see cref="MinimumGap"/> apart, and the same cue from the same character within
    /// <see cref="RepeatWindow"/> is dropped, unless the person acted on that character in between,
    /// so the result of their act is always heard. A snapshot, a resynchronization or a rewind only sets
    /// what later changes are compared with, and makes no sound. Nothing is chosen while the caller
    /// says the stage cannot be heard, and nothing it missed then plays later, except the person's
    /// own act just before it can be heard again, as the system keyboard's result comes just before
    /// focus returns.
    /// </summary>
    /// <remarks>
    /// Each character on the stage keeps one of <see cref="GlazeSynthesizer.Bots"/> notes for as long
    /// as it stays shown: when it appears, the note of the slot it stands in, numbered from the
    /// person's left like the notes, or the free note nearest to it. Its note stays when the lineup
    /// moves it, so a bot stays recognizable by ear. Not thread safe; use it from one thread.
    /// </remarks>
    public sealed class SoundCueSelector
    {
        /// <summary>The least time between two onsets, in seconds.</summary>
        public const double MinimumGap = 0.3;

        /// <summary>A character's cue repeated within this many seconds is dropped.</summary>
        public const double RepeatWindow = 10;

        /// <summary>
        /// How long the person's act waits for the stage to be heard again, in seconds. The system
        /// keyboard's result arrives as the keyboard closes, just before the app has focus again.
        /// </summary>
        public const double ActWaitsForFocus = 2;

        /// <summary>The note of a workspace whose character has none, which should not happen: the page's middle bot.</summary>
        private const int MiddleBot = 2;

        private static readonly CueOnset[] Nothing = new CueOnset[0];
        private static readonly Comparison<(int Slot, string Id)> BySlot = (a, b) =>
            a.Slot != b.Slot ? a.Slot.CompareTo(b.Slot) : string.CompareOrdinal(a.Id, b.Id);
        private static readonly Comparison<Heard> ByImportance = (a, b) =>
        {
            var byRank = Rank(a.Cue).CompareTo(Rank(b.Cue));
            if (byRank != 0) return byRank;
            return a.Bot != b.Bot ? a.Bot.CompareTo(b.Bot) : string.CompareOrdinal(a.WorkstreamId, b.WorkstreamId);
        };

        private readonly Dictionary<string, CharacterActivity> seen = new Dictionary<string, CharacterActivity>();
        private readonly Dictionary<string, int> bots = new Dictionary<string, int>();
        private readonly Dictionary<(SoundCue, string?), double> heard = new Dictionary<(SoundCue, string?), double>();
        private readonly List<string> leaving = new List<string>();
        private readonly List<(int Slot, string Id)> arriving = new List<(int Slot, string Id)>();
        private readonly List<(SoundCue, string?)> forgotten = new List<(SoundCue, string?)>();
        private readonly List<Heard> changed = new List<Heard>();
        private bool primed;
        private bool live;
        private double lastOnset = double.NegativeInfinity;
        private (WorkspaceAct Act, string WorkstreamId, double At)? waiting;

        /// <summary>The bot whose note a workstream's character plays, while it stands on the stage.</summary>
        public int? BotOf(string workstreamId) => bots.TryGetValue(workstreamId, out var bot) ? bot : (int?)null;

        /// <summary>Takes the state as it is now as what later changes are compared with. Makes no sound.</summary>
        /// <param name="slotOf">The slot a workstream's character stands in, from the person's left, or -1 without one.</param>
        public void Reset(ClientProjection state, ConnectionStatus status, Func<string, int> slotOf)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (status == null) throw new ArgumentNullException(nameof(status));
            if (slotOf == null) throw new ArgumentNullException(nameof(slotOf));
            seen.Clear();
            foreach (var pair in state.Workstreams) seen[pair.Key] = CharacterPresenter.ActivityOf(pair.Value.Status);
            live = status.IsLive;
            AssignBots(state, slotOf);
            ForgetBefore(lastOnset - RepeatWindow);
            primed = true;
        }

        /// <summary>
        /// Follows what one pump of the session shown changed, after the stage has drawn it. Returns the
        /// cues to play, in order, each at least <see cref="MinimumGap"/> after the one before.
        /// </summary>
        /// <param name="changes">What the pump changed.</param>
        /// <param name="state">The session's state after the pump.</param>
        /// <param name="status">The session's connection status after the pump.</param>
        /// <param name="slotOf">The slot a workstream's character stands in, from the person's left, or -1 without one.</param>
        /// <param name="now">The current time in seconds, on a clock that only moves forward.</param>
        /// <param name="audible">Whether cues can be heard now; while not, nothing is chosen, except as <paramref name="waitingForYouWhileAway"/> allows.</param>
        /// <param name="waitingForYouWhileAway">
        /// The person chose to hear work that comes to wait for them while another window has focus:
        /// while not <paramref name="audible"/>, a character's Waiting for you still sounds, once, and
        /// nothing else does. Off by default; whether it helps or interrupts is for the headset to show.
        /// </param>
        public IReadOnlyList<CueOnset> Observe(
            StateChanges changes,
            ClientProjection state,
            ConnectionStatus status,
            Func<string, int> slotOf,
            double now,
            bool audible,
            bool waitingForYouWhileAway = false)
        {
            if (changes == null) throw new ArgumentNullException(nameof(changes));
            if (!primed || changes.Resynchronized)
            {
                // A snapshot, another session or a rewind: what changed in between is not known.
                Reset(state, status, slotOf);
                return Nothing;
            }
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (status == null) throw new ArgumentNullException(nameof(status));
            if (slotOf == null) throw new ArgumentNullException(nameof(slotOf));

            AssignBots(state, slotOf);
            changed.Clear();
            foreach (var id in changes.Workstreams)
            {
                if (!state.Workstreams.TryGetValue(id, out var workstream)) continue;
                var activity = CharacterPresenter.ActivityOf(workstream.Status);
                var before = seen.TryGetValue(id, out var was) ? was : (CharacterActivity?)null;
                seen[id] = activity;
                // A workstream without a character on the stage has no place to sound from.
                if (CueFor(before, activity) is SoundCue cue && bots.TryGetValue(id, out var bot))
                {
                    changed.Add(new Heard(cue, id, bot));
                }
            }
            // Lost: the connection ended or was refused. A session the application stopped, as when
            // the headset sleeps, was not lost.
            var lost = live && !status.IsLive
                && (status.Phase == ConnectionPhase.WaitingToRetry || status.Phase == ConnectionPhase.Refused);
            live = status.IsLive;
            if (!audible && waitingForYouWhileAway)
            {
                // One gentle cue for each character that came to wait for the person; never an alarm.
                var away = new List<CueOnset>();
                changed.Sort(ByImportance);
                foreach (var cue in changed)
                {
                    if (cue.Cue == SoundCue.WaitingForYou && !Repeated(cue.Cue, cue.WorkstreamId, now)) away.Add(Schedule(cue.Cue, cue.WorkstreamId, cue.Bot, now));
                }
                return away;
            }
            if (!audible) return Nothing;

            var onsets = new List<CueOnset>();
            // Together, the most pressing first; what happened before the connection dropped comes
            // before the room's cue.
            changed.Sort(ByImportance);
            foreach (var cue in changed)
            {
                if (!Repeated(cue.Cue, cue.WorkstreamId, now)) onsets.Add(Schedule(cue.Cue, cue.WorkstreamId, cue.Bot, now));
            }
            if (lost && !Repeated(SoundCue.LastKnown, null, now)) onsets.Add(Schedule(SoundCue.LastKnown, null, -1, now));
            return onsets;
        }

        /// <summary>
        /// The person did something with a workstream's workspace: the cue for it, in front of them. A
        /// command's cue says it was sent, never that it succeeded. While cues cannot be heard the act
        /// waits, for <see cref="ActWaitsForFocus"/> at most, and sounds only through
        /// <see cref="HeardAgain"/>. Acting on a character also clears what it sounded recently, so the
        /// result of the act, which arrives later as the character's own cue, is never dropped as a
        /// repeat.
        /// </summary>
        public CueOnset? Act(WorkspaceAct act, string workstreamId, double now, bool audible)
        {
            if (workstreamId == null) throw new ArgumentNullException(nameof(workstreamId));
            forgotten.Clear();
            foreach (var key in heard.Keys)
            {
                if (key.Item2 == workstreamId) forgotten.Add(key);
            }
            foreach (var key in forgotten) heard.Remove(key);
            waiting = audible ? ((WorkspaceAct, string, double)?)null : (act, workstreamId, now);
            if (!audible) return null;
            return Schedule(CueOf(act), workstreamId, bots.TryGetValue(workstreamId, out var bot) ? bot : MiddleBot, now);
        }

        /// <summary>
        /// A control took a press that has no cue of its own: one soft tap from it, at once and with no
        /// gap kept, since it answers the hand. A press that sends an act is answered by the act's own
        /// cue (<see cref="Act"/>) instead, so it gets no tap. Nothing while cues cannot be heard, and
        /// nothing later.
        /// </summary>
        public CueOnset? Touch(double now, bool audible) => audible ? Answer(SoundCue.Touch, now) : null;

        /// <summary>
        /// A control refused a press, being unavailable now: Not now from it, at once and with no gap
        /// kept. Never for a press refused because cues cannot be heard, as while another window has
        /// focus or in the moment after it returns: that press reached nothing.
        /// </summary>
        public CueOnset? NotNow(double now, bool audible) => audible ? Answer(SoundCue.NotNow, now) : null;

        /// <summary>
        /// Cues can be heard again, as when the app has focus again: the person's act that waited for
        /// it sounds now, if it came within <see cref="ActWaitsForFocus"/>; nothing else missed does.
        /// </summary>
        public CueOnset? HeardAgain(double now)
        {
            var act = waiting;
            waiting = null;
            if (act == null || now - act.Value.At > ActWaitsForFocus) return null;
            var (what, workstreamId, _) = act.Value;
            return Schedule(CueOf(what), workstreamId, bots.TryGetValue(workstreamId, out var bot) ? bot : MiddleBot, now);
        }

        /// <summary>
        /// The cue a character sounds when its activity changes, or null for none: arriving at a state
        /// the person might act on, a test run starting, or work starting or resuming. A test run
        /// ending, or a start completing, makes no sound; nor does a workstream that has not started.
        /// </summary>
        public static SoundCue? CueFor(CharacterActivity? before, CharacterActivity after)
        {
            if (before == after) return null;
            switch (after)
            {
                case CharacterActivity.WaitingForHuman:
                    return SoundCue.WaitingForYou;
                case CharacterActivity.TurnFinished:
                    return SoundCue.FinishedThisRound;
                case CharacterActivity.Failed:
                    return SoundCue.CouldNotFinish;
                case CharacterActivity.Unknown:
                    return SoundCue.CantTellYet;
                case CharacterActivity.Interrupted:
                    return SoundCue.Stopped;
                case CharacterActivity.Verifying:
                    return SoundCue.CheckingItsWork;
                case CharacterActivity.Starting:
                case CharacterActivity.Working:
                    // Work starts, or resumes after the person answered: the result of their answer
                    // arriving as the character's own cue.
                    return before == CharacterActivity.Starting
                        || before == CharacterActivity.Working
                        || before == CharacterActivity.Verifying
                        ? (SoundCue?)null
                        : SoundCue.Working;
                default:
                    return null;
            }
        }

        public static SoundCue CueOf(WorkspaceAct act) => act switch
        {
            WorkspaceAct.Open => SoundCue.Open,
            WorkspaceAct.Collapse => SoundCue.Close,
            WorkspaceAct.Approve => SoundCue.Approve,
            WorkspaceAct.Deny => SoundCue.Deny,
            WorkspaceAct.Instruct => SoundCue.TellIt,
            WorkspaceAct.Interrupt => SoundCue.Stop,
            _ => throw new ArgumentOutOfRangeException(nameof(act), act, "Unhandled act."),
        };

        public static CuePlace PlaceOf(SoundCue cue)
        {
            switch (cue)
            {
                case SoundCue.LastKnown:
                    return CuePlace.Stage;
                case SoundCue.Open:
                case SoundCue.Close:
                case SoundCue.Approve:
                case SoundCue.Deny:
                case SoundCue.TellIt:
                case SoundCue.Stop:
                    return CuePlace.Workspace;
                case SoundCue.Touch:
                case SoundCue.NotNow:
                    return CuePlace.Control;
                default:
                    return CuePlace.Character;
            }
        }

        /// <summary>Of cues that arrive together, which starts first: what needs the person most.</summary>
        private static int Rank(SoundCue cue) => cue switch
        {
            SoundCue.WaitingForYou => 0,
            SoundCue.CouldNotFinish => 1,
            SoundCue.CantTellYet => 2,
            SoundCue.FinishedThisRound => 3,
            SoundCue.Stopped => 4,
            SoundCue.CheckingItsWork => 5,
            _ => 6,
        };

        private CueOnset Schedule(SoundCue cue, string? workstreamId, int bot, double now)
        {
            var at = Math.Max(now, lastOnset + MinimumGap);
            lastOnset = at;
            var place = PlaceOf(cue);
            // The person's own actions always answer them; only characters' cues can repeat too often.
            if (place != CuePlace.Workspace) heard[(cue, workstreamId)] = at;
            return new CueOnset(cue, workstreamId, bot, place, at);
        }

        /// <summary>A control's answer to the hand: at once, on the key's note, keeping no gap and holding up no other cue.</summary>
        private static CueOnset Answer(SoundCue cue, double now) => new CueOnset(cue, null, -1, PlaceOf(cue), now);

        private bool Repeated(SoundCue cue, string? workstreamId, double now) =>
            heard.TryGetValue((cue, workstreamId), out var last) && now - last < RepeatWindow;

        private void ForgetBefore(double time)
        {
            forgotten.Clear();
            foreach (var pair in heard)
            {
                if (pair.Value < time) forgotten.Add(pair.Key);
            }
            foreach (var key in forgotten) heard.Remove(key);
        }

        /// <summary>
        /// Characters that left the stage give their notes back; newcomers, from the person's left,
        /// take the note of their slot, or the free note nearest to it.
        /// </summary>
        private void AssignBots(ClientProjection state, Func<string, int> slotOf)
        {
            leaving.Clear();
            foreach (var pair in bots)
            {
                if (!state.Workstreams.ContainsKey(pair.Key) || slotOf(pair.Key) < 0) leaving.Add(pair.Key);
            }
            foreach (var id in leaving) bots.Remove(id);

            arriving.Clear();
            foreach (var id in state.Workstreams.Keys)
            {
                if (bots.ContainsKey(id)) continue;
                var slot = slotOf(id);
                if (slot >= 0) arriving.Add((slot, id));
            }
            arriving.Sort(BySlot);
            foreach (var (slot, id) in arriving) bots[id] = FreeBotNear(Math.Min(slot, GlazeSynthesizer.Bots - 1));
        }

        /// <summary>The free note nearest the preferred one, the lower of two as near; shared only when every note is taken.</summary>
        private int FreeBotNear(int preferred)
        {
            for (var distance = 0; distance < GlazeSynthesizer.Bots; distance++)
            {
                var lower = preferred - distance;
                if (lower >= 0 && !bots.ContainsValue(lower)) return lower;
                var upper = preferred + distance;
                if (distance > 0 && upper < GlazeSynthesizer.Bots && !bots.ContainsValue(upper)) return upper;
            }
            return preferred;
        }

        private readonly struct Heard
        {
            public Heard(SoundCue cue, string workstreamId, int bot)
            {
                Cue = cue;
                WorkstreamId = workstreamId;
                Bot = bot;
            }

            public SoundCue Cue { get; }

            public string WorkstreamId { get; }

            public int Bot { get; }
        }
    }
}

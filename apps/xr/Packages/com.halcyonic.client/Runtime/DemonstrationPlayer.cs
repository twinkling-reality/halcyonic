#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    public sealed class DemonstrationOptions
    {
        /// <summary>How fast to play: 1 is the recorded pace, 2 twice as fast. Recorded holds scale with it.</summary>
        public double Speed { get; set; } = 1;

        /// <summary>
        /// When set, every hold the recording times lasts this long instead, at the recorded pace;
        /// <see cref="Timeout.InfiniteTimeSpan"/> holds until an answer. A hold that waits for an
        /// answer, as at an approval, always does.
        /// </summary>
        public TimeSpan? Hold { get; set; }
    }

    /// <summary>
    /// Plays a <see cref="DemonstrationRecording"/> as a realtime session, entirely on this device:
    /// each connection the session makes gets a <see cref="DemonstrationTransport"/> that plays the
    /// recording from its beginning and follows the answers a person gives. Where the playback stands
    /// is shared, so the stage can offer the instructions recorded there and say when the recording
    /// has ended. Safe to read from any thread.
    /// </summary>
    public sealed class DemonstrationPlayer
    {
        private static readonly PresetInstruction[] NoInstructions = new PresetInstruction[0];
        private Place place = new Place(0, 0, false);
        private int plays;

        public DemonstrationPlayer(DemonstrationRecording recording, ClientInfo client, DemonstrationOptions? options = null)
            : this(Task.FromResult(recording), client, options)
        {
        }

        /// <param name="recording">
        /// The recording, possibly still being read on another thread, so a large one never holds up a
        /// frame. The session connects once it is read; if it cannot be read, each connection attempt
        /// fails with the reason.
        /// </param>
        public DemonstrationPlayer(Task<DemonstrationRecording> recording, ClientInfo client, DemonstrationOptions? options = null)
        {
            Loading = recording;
            Options = options ?? new DemonstrationOptions();
            if (!(Options.Speed > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(options), Options.Speed, "The speed must be positive.");
            }
            Session = new RealtimeSession(
                new RealtimeSessionOptions(DemonstrationTransport.Endpoint, string.Empty, client),
                () => new DemonstrationTransport(this));
            Reads = new DemonstrationReads(this);
        }

        /// <summary>
        /// The recording's answers about each execution's understanding and evaluation, where the
        /// playback stands, for what a client would read over REST.
        /// </summary>
        public DemonstrationReads Reads { get; }

        /// <summary>The recording once it has been read; null until then, or if it cannot be.</summary>
        public DemonstrationRecording? Recording => Loading.Status == TaskStatus.RanToCompletion ? Loading.Result : null;

        /// <summary>Reading the recording.</summary>
        public Task<DemonstrationRecording> Loading { get; }

        public DemonstrationOptions Options { get; }

        /// <summary>The session that plays the recording; nothing it opens leaves this device.</summary>
        public RealtimeSession Session { get; }

        /// <summary>
        /// The recording has reached one of its ends: its final state holds with no action offered,
        /// then the demonstration starts again.
        /// </summary>
        public bool Ended => Volatile.Read(ref place).Ended;

        /// <summary>How many times the recording has started from its beginning, in any connection.</summary>
        public int Plays => Volatile.Read(ref plays);

        /// <summary>
        /// The instructions the recording offers for this execution where it stands now, as buttons that
        /// send exactly the recorded text; empty where it offers none. Any other text is answered with
        /// one of these, and says so.
        /// </summary>
        public IReadOnlyList<PresetInstruction> InstructionsFor(string executionId)
        {
            var recording = Recording;
            if (recording == null) return NoInstructions;
            var now = Volatile.Read(ref place);
            List<PresetInstruction>? found = null;
            foreach (var branch in recording.Nodes[now.Node].BranchesAfter(now.Played))
            {
                var answer = branch.Answer;
                if (answer.Kind != DemonstrationAnswerKind.Instruct || answer.ExecutionId != executionId) continue;
                (found ??= new List<PresetInstruction>()).Add(new PresetInstruction(answer.Label!, answer.Text!));
            }
            return found ?? (IReadOnlyList<PresetInstruction>)NoInstructions;
        }

        /// <summary>Where the transport playing now stands.</summary>
        internal void Report(int node, int played, bool ended) => Volatile.Write(ref place, new Place(node, played, ended));

        /// <summary>The node playing now, and how many of its events have played.</summary>
        internal (int Node, int Played) Where
        {
            get
            {
                var now = Volatile.Read(ref place);
                return (now.Node, now.Played);
            }
        }

        /// <summary>The transport playing now started the recording from its beginning.</summary>
        internal void Began() => Interlocked.Increment(ref plays);

        private sealed class Place
        {
            public Place(int node, int played, bool ended)
            {
                Node = node;
                Played = played;
                Ended = ended;
            }

            public int Node { get; }

            public int Played { get; }

            public bool Ended { get; }
        }
    }
}

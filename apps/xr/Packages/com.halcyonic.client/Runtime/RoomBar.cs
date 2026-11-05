#nullable enable

namespace Halcyonic.Client
{
    /// <summary>What the bar shows: one control, Type or Talk, talking to the task of the character picked on the shelf.</summary>
    public enum RoomBarState
    {
        /// <summary>"Type or Talk": a press opens it to write.</summary>
        Rest,

        /// <summary>Recording what the person says.</summary>
        Listening,

        /// <summary>The computer writes down what was said.</summary>
        Transcribing,

        /// <summary>Open to write: the draft, the mic and Send.</summary>
        Open,

        /// <summary>"Sent · Awaiting Start": the instruction went, and the run has not started yet.</summary>
        Sent,
    }

    /// <summary>
    /// The bar of the redesign (DESIGN's SYSTEM-v4, 2026-11-05): one capsule, Type or Talk, that opens to write and folds
    /// when sent. A view of the open task's existing instruction, never a second one: a press is the file's own action
    /// (<see cref="FileScreens.TellIt"/>, <see cref="FileScreens.EditInstruction"/>, <see cref="FileScreens.HoldToTalk"/>,
    /// <see cref="FileScreens.SendInstruction"/>), so sending, reading before Send and voice keep their rules.
    /// </summary>
    public sealed class RoomBar
    {
        /// <summary>The bar at rest. The redesign's own words; every other word here is the product's.</summary>
        public const string TypeOrTalk = "Type or Talk";

        /// <summary>The bar once the instruction went and the run has not started.</summary>
        public const string SentWords = KitMomentWords.SentTitle + KitMomentWords.Dot + KitMomentWords.WaitingToStart;

        private RoomBar(RoomBarState state, LineWords words, bool micShown, bool sendShown, bool sendAvailable, string? whyNotSend)
        {
            State = state;
            Words = words;
            MicShown = micShown;
            SendShown = sendShown;
            SendAvailable = sendAvailable;
            WhyNotSend = whyNotSend;
        }

        public RoomBarState State { get; }

        /// <summary>What the capsule says: Halcyonic's words, or the person's draft, theirs.</summary>
        public LineWords Words { get; }

        public bool MicShown { get; }

        public bool SendShown { get; }

        public bool SendAvailable { get; }

        /// <summary>Why Send waits, in the product's words; null while it is offered or not shown.</summary>
        public string? WhyNotSend { get; }

        /// <summary>The capsule's width in dp: narrow at rest, wider while it listens or says it sent, the pair's open.</summary>
        public float WidthDp => State switch
        {
            RoomBarState.Rest => RoomGeometry.BarRestDp,
            RoomBarState.Open => RoomGeometry.BarOpenDp,
            _ => RoomGeometry.BarWideDp,
        };

        /// <summary>The file's action a press on the capsule's words takes: open to write at rest, the keyboard open.</summary>
        public string? FieldAction => State switch
        {
            RoomBarState.Rest => FileScreens.TellIt,
            RoomBarState.Open => FileScreens.EditInstruction,
            _ => null,
        };

        /// <summary>The file's action the mic takes, held to talk.</summary>
        public const string MicAction = FileScreens.HoldToTalk;

        /// <summary>The file's action Send takes.</summary>
        public const string SendAction = FileScreens.SendInstruction;

        /// <summary>
        /// The bar for the open task's composer: what voice is doing first, then the draft while the composer is open, then
        /// Sent while the run it started has not, else at rest.
        /// </summary>
        /// <param name="composing">The task's instruction is open to write.</param>
        /// <param name="draft">Its words so far.</param>
        /// <param name="voiceOffered">Hold to talk is offered here.</param>
        /// <param name="sendable">Send is offered now.</param>
        /// <param name="whyNotSend">Why Send waits, where it does.</param>
        /// <param name="sentAwaitingStart">The task's run was asked for and has not started.</param>
        public static RoomBar Of(bool composing, string draft, bool voiceOffered, bool sendable, string? whyNotSend, VoiceStage voice, bool sentAwaitingStart)
        {
            if (voice == VoiceStage.Listening)
                return new RoomBar(RoomBarState.Listening, LineWords.Ours(VoiceText.ListeningWords), micShown: true, sendShown: false, sendAvailable: false, null);
            if (voice == VoiceStage.WritingDown)
                return new RoomBar(RoomBarState.Transcribing, LineWords.Ours(VoiceText.WritingDownWords), micShown: true, sendShown: false, sendAvailable: false, null);
            if (composing)
                return new RoomBar(RoomBarState.Open, draft.Length == 0 ? LineWords.Ours(TypeOrTalk) : LineWords.Theirs(draft), voiceOffered,
                    sendShown: true, sendable, sendable ? null : whyNotSend);
            if (sentAwaitingStart)
                return new RoomBar(RoomBarState.Sent, LineWords.Ours(SentWords), voiceOffered, sendShown: false, sendAvailable: false, null);
            return new RoomBar(RoomBarState.Rest, LineWords.Ours(TypeOrTalk), voiceOffered, sendShown: false, sendAvailable: false, null);
        }
    }
}

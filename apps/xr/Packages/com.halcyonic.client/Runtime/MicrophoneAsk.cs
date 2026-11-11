#nullable enable

namespace Halcyonic.Client
{
    /// <summary>The microphone as the headset's system reports it, with whether Halcyonic asked before (kept on the headset).</summary>
    public enum MicrophoneAccess
    {
        /// <summary>The person allowed it.</summary>
        Allowed,
        /// <summary>Not allowed, and Halcyonic has not asked, or the system's question was dismissed unanswered.</summary>
        NeverAsked,
        /// <summary>Refused once: the system will ask once more.</summary>
        RefusedOnce,
        /// <summary>Refused so the system no longer asks: only Settings can allow it.</summary>
        RefusedForGood,
    }

    /// <summary>How the system answered its microphone question.</summary>
    public enum MicrophoneAnswer
    {
        Granted,
        Denied,
        DeniedForGood,
        Dismissed,
    }

    /// <summary>What a hold to talk does about the microphone before it records.</summary>
    public enum MicrophoneAtHold
    {
        /// <summary>Record.</summary>
        Record,
        /// <summary>Ask the system now and record nothing; the words say why.</summary>
        Ask,
        /// <summary>Record nothing and ask nothing: the words say how to allow it.</summary>
        Explain,
    }

    /// <summary>
    /// When Halcyonic asks for the microphone (ADR 0021, amended 2026-11-11): once, at the first open, after one line
    /// that says why, so the first hold to talk records; never again at an open. A hold asks only where no answer was
    /// ever given, or where the person refused once (the system asks once more); where the system no longer asks, the
    /// hold's line says how to allow it. Nothing is asked while no computer is configured or the recorded demonstration
    /// shows, and launches that drive the app by settings skip the open's question, since nothing may press a system
    /// dialog there.
    /// </summary>
    public static class MicrophoneAsk
    {
        /// <summary>The line before the open's question, and at a hold that asks again. Placeholder words: FIELD words it.</summary>
        public const string Why = "Hold to Talk Uses the Microphone";

        /// <summary>Reads the access from the system's facts and whether an answer was recorded before.</summary>
        public static MicrophoneAccess Read(bool allowed, bool answeredBefore, bool systemWouldExplain) =>
            allowed ? MicrophoneAccess.Allowed
            : !answeredBefore ? MicrophoneAccess.NeverAsked
            : systemWouldExplain ? MicrophoneAccess.RefusedOnce
            : MicrophoneAccess.RefusedForGood;

        /// <summary>Whether this open may come to ask at all: only where it was never answered, and not in a launch driven by settings.</summary>
        public static bool WaitsAtOpen(MicrophoneAccess access, bool drivenLaunch) =>
            access == MicrophoneAccess.NeverAsked && !drivenLaunch;

        /// <summary>
        /// Whether the open asks now: where it may (<see cref="WaitsAtOpen"/>), a computer is configured to take the voice
        /// (<paramref name="computerConfigured"/>), and the recorded demonstration is not showing
        /// (<paramref name="recordedRunShowing"/>; unknown counts as showing). Until then nothing is asked or kept.
        /// </summary>
        public static bool AskAtOpen(MicrophoneAccess access, bool drivenLaunch, bool computerConfigured, bool recordedRunShowing) =>
            WaitsAtOpen(access, drivenLaunch) && computerConfigured && !recordedRunShowing;

        /// <summary>Whether an answer is kept as answered: a dismissed question is not, so the next hold asks.</summary>
        public static bool CountsAsAnswer(MicrophoneAnswer answer) => answer != MicrophoneAnswer.Dismissed;

        /// <summary>The access after the system's <paramref name="answer"/>.</summary>
        public static MicrophoneAccess After(MicrophoneAnswer answer) => answer switch
        {
            MicrophoneAnswer.Granted => MicrophoneAccess.Allowed,
            MicrophoneAnswer.Denied => MicrophoneAccess.RefusedOnce,
            MicrophoneAnswer.DeniedForGood => MicrophoneAccess.RefusedForGood,
            _ => MicrophoneAccess.NeverAsked,
        };

        /// <summary>What a hold does with <paramref name="access"/>.</summary>
        public static MicrophoneAtHold AtHold(MicrophoneAccess access) => access switch
        {
            MicrophoneAccess.Allowed => MicrophoneAtHold.Record,
            MicrophoneAccess.RefusedForGood => MicrophoneAtHold.Explain,
            _ => MicrophoneAtHold.Ask,
        };

        /// <summary>The words at a hold that does not record, or null where it records.</summary>
        public static string? WordsAtHold(MicrophoneAccess access) => access switch
        {
            MicrophoneAccess.Allowed => null,
            MicrophoneAccess.NeverAsked => VoiceText.AllowMicrophone,
            MicrophoneAccess.RefusedOnce => Why,
            _ => VoiceText.MicrophoneRefused,
        };

        /// <summary>The words once the system answered a hold's question.</summary>
        public static string WordsAfter(MicrophoneAnswer answer) => answer switch
        {
            MicrophoneAnswer.Granted => VoiceText.MicrophoneAllowed,
            MicrophoneAnswer.Dismissed => VoiceText.AllowMicrophone,
            MicrophoneAnswer.Denied => Why,
            _ => VoiceText.MicrophoneRefused,
        };
    }
}

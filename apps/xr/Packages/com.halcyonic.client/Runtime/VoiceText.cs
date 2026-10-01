#nullable enable

namespace Halcyonic.Client
{
    /// <summary>
    /// The words of hold to talk (ADR 0021), so the XR layer only lays them out. What was heard is a
    /// draft the person keeps, fixes or speaks again, and says so; nothing claims the Mac understood
    /// more than its transcript. Refusals say what to do next, and typing is always offered.
    /// </summary>
    public static class VoiceText
    {
        public const string HoldToTalk = "Hold to talk";
        public const string Listening = "Listening. Let go when you're done.";
        public const string Hearing = "Your Mac is turning that into text.";
        public const string HeardOnYourMac = "Heard on your Mac.";
        public const string HeardNote = "Heard on your Mac. Check it before you go on.";

        public const string NothingHeard = "Nothing was heard. Hold, speak, then let go, or type instead.";
        public const string TooShort = "That was too short to hear. Hold while you speak.";
        public const string Stopped = "Stopped listening, so nothing was sent.";
        public const string AllowMicrophone = "Allow the microphone when the headset asks, then hold again.";
        public const string MicrophoneAllowed = "The microphone is allowed. Hold again to talk.";
        public const string NoMicrophone = "Allow the microphone in the headset's settings, or type instead.";
        public const string Unreachable = "Your Mac can't be reached right now. Type instead, or try again.";

        /// <summary>Every word shown beside hold to talk, for the editor's check that the longest fits.</summary>
        public static readonly string[] Shown =
        {
            Listening, Hearing, NothingHeard, TooShort, Stopped, AllowMicrophone, MicrophoneAllowed, NoMicrophone, Unreachable,
            Refusal("transcription_unavailable"), Refusal("transcription_busy"), Refusal("transcription_busy_on_mac"),
            Refusal("rate_limited"), Refusal("transcription_failed"), Refusal("invalid_audio"), Refusal("something_new"),
        };

        /// <summary>Why the Mac turned a clip away, from the control plane's code.</summary>
        public static string Refusal(string? code) => code switch
        {
            "transcription_unavailable" => "Voice isn't set up on your Mac. Type instead.",
            "transcription_busy" => "Your Mac is still hearing your last clip. Try again in a moment.",
            "transcription_busy_on_mac" => "Your Mac is hearing another clip. Try again in a moment.",
            "rate_limited" => "That's a lot of clips at once. Wait a moment, or type instead.",
            "transcription_failed" => "Your Mac couldn't turn that into text. Try again, or type instead.",
            "invalid_audio" or "audio_too_short" or "audio_too_long" or "payload_too_large" =>
                "That recording didn't work. Try again, or type instead.",
            null => Unreachable,
            _ => "Your Mac turned that clip away. Type instead.",
        };
    }
}

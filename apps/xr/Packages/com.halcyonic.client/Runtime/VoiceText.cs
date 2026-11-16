#nullable enable

using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>
    /// The words of hold to talk (ADR 0021), so the XR layer only lays them out. What was heard is a
    /// draft the person keeps, fixes or speaks again, and says so; nothing claims the Mac understood
    /// more than its transcript. Refusals say what to do next, and typing is always offered.
    /// </summary>
    public static class VoiceText
    {
        public const string HoldToTalk = "Hold to Talk";
        public const string Listening = "Listening";
        public const string Hearing = "Typing It Out";

        /// <summary>Hold to talk's own words while it records, in its place on the footer, so the page never grows for it.</summary>
        public const string ListeningWords = "Listening";

        /// <summary>Hold to talk's own words while the computer writes down what was said.</summary>
        public const string WritingDownWords = "Typing It Out";

        /// <summary>The words Hold to talk shows in place of its own, laid at the widest so it never changes width under the hand.</summary>
        public static readonly IReadOnlyList<string> TalkReads = new[] { ListeningWords, WritingDownWords };
        public const string HeardNote = "Heard, not typed: check the words";
        public const string HeardAnswer = "Transcribed. Check it before sending.";

        /// <summary>The first question's idea, heard as New project opened on a later step of a draft kept from before: dropped (settled by the coordinator, 2026-10-08).</summary>
        public const string NotOnYourIdea = "Nothing was typed: the step changed. Choose your idea.";

        /// <summary>A voice answer heard after the question it was spoken for gave way to another, or to another prompt: dropped (settled by the coordinator, 2026-10-04).</summary>
        public const string QuestionChangedWhileSpeaking = "Nothing was typed: the question changed. Read it again.";

        public const string NothingHeard = "Nothing was heard. Hold while you talk.";
        public const string TooShort = "Too short. Hold while you talk.";
        public const string Stopped = "Nothing was sent: listening stopped.";
        public const string AllowMicrophone = "Allow the microphone, then hold again.";
        public const string MicrophoneAllowed = "The microphone is allowed. Hold to talk.";
        public const string NoMicrophone = "No microphone. Type instead.";
        public const string MicrophoneRefused = "The microphone was refused. Allow it in settings or type.";
        public const string Unreachable = "Your computer can't be reached. Type, or try again.";

        /// <summary>Every word shown beside hold to talk, for the editor's check that the longest fits.</summary>
        public static readonly string[] Shown =
        {
            Listening, Hearing, NothingHeard, TooShort, Stopped, AllowMicrophone, MicrophoneAllowed, NoMicrophone, MicrophoneRefused, Unreachable,
            Refusal("transcription_unavailable"), Refusal("transcription_busy"), Refusal("transcription_busy_on_mac"),
            Refusal("rate_limited"), Refusal("transcription_failed"), Refusal("invalid_audio"), Refusal("something_new"),
        };

        /// <summary>The question before a spoken instruction is sent: what the Mac heard, then whether to send it.</summary>
        public static string SendHeard(string instruction) => "Send This? " + LabelText.Plain(instruction);

        /// <summary>Why the Mac turned a clip away, from the control plane's code.</summary>
        public static string Refusal(string? code) => code switch
        {
            "transcription_unavailable" => "Voice isn't set up. Type instead.",
            "transcription_busy" => "Still transcribing the last clip. Try again soon.",
            "transcription_busy_on_mac" => "Your computer is busy. Try again soon.",
            "rate_limited" => "Too many tries. Try again soon or type.",
            "transcription_failed" => "That couldn't be transcribed. Try again or type.",
            "invalid_audio" or "audio_too_short" or "audio_too_long" or "payload_too_large" =>
                "That recording can't be used. Try again or type.",
            null => Unreachable,
            _ => "Voice didn't work. Type instead.",
        };
    }
}

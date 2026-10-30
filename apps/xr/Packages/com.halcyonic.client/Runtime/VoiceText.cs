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
        public const string Hearing = "Transcribing";

        /// <summary>Hold to talk's own words while it records, in its place on the footer, so the page never grows for it.</summary>
        public const string ListeningWords = "Listening";

        /// <summary>Hold to talk's own words while the computer writes down what was said.</summary>
        public const string WritingDownWords = "Transcribing";

        /// <summary>The words Hold to talk shows in place of its own, laid at the widest so it never changes width under the hand.</summary>
        public static readonly IReadOnlyList<string> TalkReads = new[] { ListeningWords, WritingDownWords };
        public const string HeardNote = "Transcribed · Check Before Continuing";
        public const string HeardAnswer = "Transcribed · Check Before Sending";

        /// <summary>The first question's idea, heard as New project opened on a later step of a draft kept from before: dropped (settled by the coordinator, 2026-10-08).</summary>
        public const string NotOnYourIdea = "Not Typed · Step Changed · Choose Your Idea";

        /// <summary>A voice answer heard after the question it was spoken for gave way to another, or to another prompt: dropped (settled by the coordinator, 2026-10-04).</summary>
        public const string QuestionChangedWhileSpeaking = "Not Typed · Question Changed · Reread";

        public const string NothingHeard = "Nothing Heard · Hold While Talking";
        public const string TooShort = "Too Short · Hold While Talking";
        public const string Stopped = "Not Sent · Listening Stopped";
        public const string AllowMicrophone = "Allow Microphone · Hold Again";
        public const string MicrophoneAllowed = "Microphone Allowed · Hold to Talk";
        public const string NoMicrophone = "No Microphone · Type Instead";
        public const string MicrophoneRefused = "Microphone Refused · Allow in Settings or Type";
        public const string Unreachable = "Computer Unreachable · Type or Retry";

        /// <summary>Every word shown beside hold to talk, for the editor's check that the longest fits.</summary>
        public static readonly string[] Shown =
        {
            Listening, Hearing, NothingHeard, TooShort, Stopped, AllowMicrophone, MicrophoneAllowed, NoMicrophone, MicrophoneRefused, Unreachable,
            Refusal("transcription_unavailable"), Refusal("transcription_busy"), Refusal("transcription_busy_on_mac"),
            Refusal("rate_limited"), Refusal("transcription_failed"), Refusal("invalid_audio"), Refusal("something_new"),
        };

        /// <summary>The question before a spoken instruction is sent: what the Mac heard, then whether to send it.</summary>
        public static string SendHeard(string instruction) => "Send Heard Instruction? · " + LabelText.Plain(instruction);

        /// <summary>Why the Mac turned a clip away, from the control plane's code.</summary>
        public static string Refusal(string? code) => code switch
        {
            "transcription_unavailable" => "Voice Not Set Up · Type Instead",
            "transcription_busy" => "Transcribing Last Clip · Retry Soon",
            "transcription_busy_on_mac" => "Computer Busy · Retry Soon",
            "rate_limited" => "Too Many Tries · Retry Soon or Type",
            "transcription_failed" => "Transcription Failed · Retry or Type",
            "invalid_audio" or "audio_too_short" or "audio_too_long" or "payload_too_large" =>
                "Recording Unusable · Retry or Type",
            null => Unreachable,
            _ => "Voice Failed · Type Instead",
        };
    }
}

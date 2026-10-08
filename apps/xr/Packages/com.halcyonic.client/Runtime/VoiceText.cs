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
        public const string HoldToTalk = "Hold to talk";
        public const string Listening = "Listening. Let go when you're done.";
        public const string Hearing = "Writing down what you said.";

        /// <summary>Hold to talk's own words while it records, in its place on the footer, so the page never grows for it.</summary>
        public const string ListeningWords = "Listening";

        /// <summary>Hold to talk's own words while the computer writes down what was said.</summary>
        public const string WritingDownWords = "Writing down";

        /// <summary>The words Hold to talk shows in place of its own, laid at the widest so it never changes width under the hand.</summary>
        public static readonly IReadOnlyList<string> TalkReads = new[] { ListeningWords, WritingDownWords };
        public const string HeardNote = "This is what " + HostText.Your + " heard. Check it before you go on.";
        public const string HeardAnswer = "This is what " + HostText.Your + " heard. Check it, then press Send answer.";

        /// <summary>The first question's idea, heard as New project opened on a later step of a draft kept from before: dropped (settled by the coordinator, 2026-10-08).</summary>
        public const string NotOnYourIdea = "Nothing was typed: this project has moved on from Your idea. Choose Your idea above, then hold to talk again.";

        /// <summary>A voice answer heard after the question it was spoken for gave way to another, or to another prompt: dropped (settled by the coordinator, 2026-10-04).</summary>
        public const string QuestionChangedWhileSpeaking = "Nothing was typed: the question changed while you spoke. Read it again.";

        public const string NothingHeard = "I didn't catch anything. Hold the button while you talk, or type instead.";
        public const string TooShort = "That was too quick. Keep holding while you talk.";
        public const string Stopped = "Stopped listening, so nothing was sent.";
        public const string AllowMicrophone = "Allow the microphone when the headset asks, then hold again.";
        public const string MicrophoneAllowed = "The microphone is allowed. Hold again to talk.";
        public const string NoMicrophone = "There's no microphone this app can listen with. Type instead.";
        public const string MicrophoneRefused = "Allow the microphone in the headset's settings, or type instead.";
        public const string Unreachable = HostText.YourStart + " can't be reached right now. Type instead, or try again.";

        /// <summary>Every word shown beside hold to talk, for the editor's check that the longest fits.</summary>
        public static readonly string[] Shown =
        {
            Listening, Hearing, NothingHeard, TooShort, Stopped, AllowMicrophone, MicrophoneAllowed, NoMicrophone, MicrophoneRefused, Unreachable,
            Refusal("transcription_unavailable"), Refusal("transcription_busy"), Refusal("transcription_busy_on_mac"),
            Refusal("rate_limited"), Refusal("transcription_failed"), Refusal("invalid_audio"), Refusal("something_new"),
        };

        /// <summary>The question before a spoken instruction is sent: what the Mac heard, then whether to send it.</summary>
        public static string SendHeard(string instruction) => HostText.YourStart + " heard: \u201C" + LabelText.Plain(instruction) + "\u201D Send it?";

        /// <summary>Why the Mac turned a clip away, from the control plane's code.</summary>
        public static string Refusal(string? code) => code switch
        {
            "transcription_unavailable" => "Voice isn't set up on " + HostText.Your + ". Type instead.",
            "transcription_busy" => "Still working on your last one. Try again in a moment.",
            "transcription_busy_on_mac" => HostText.YourStart + " is hearing another clip. Try again in a moment.",
            "rate_limited" => "Give it a moment, then try again, or type instead.",
            "transcription_failed" => HostText.YourStart + " couldn't turn that into text. Try again, or type instead.",
            "invalid_audio" or "audio_too_short" or "audio_too_long" or "payload_too_large" =>
                "That recording didn't work. Try again, or type instead.",
            null => Unreachable,
            _ => "That didn't work. Try typing instead.",
        };
    }
}

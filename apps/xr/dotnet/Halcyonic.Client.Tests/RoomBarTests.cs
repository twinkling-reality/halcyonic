using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The redesign's bar (DESIGN's SYSTEM-v4, 2026-11-05): one capsule, Type or Talk, a view of the open task's own
/// instruction, so every press is the file's existing action.
/// </summary>
public class RoomBarTests
{
    private static RoomBar Bar(bool composing = false, string draft = "", bool sendable = false, string? why = null,
        VoiceStage voice = VoiceStage.Idle, bool sent = false) =>
        RoomBar.Of(composing, draft, voiceOffered: true, sendable, why, voice, sent);

    [Test]
    public void AtRestItSaysTypeOrTalkAndAPressOpensItToWrite()
    {
        var bar = Bar();
        Assert.That((bar.State, bar.Words.Text, bar.WidthDp, bar.FieldAction), Is.EqualTo((RoomBarState.Rest, "Type or Talk", 230f, FileScreens.TellIt)));
        Assert.That((bar.MicShown, bar.SendShown), Is.EqualTo((true, false)));
    }

    [Test]
    public void OpenItHoldsTheDraftAsTheirWordsAndSendWaitsWithTheProductsReason()
    {
        var waiting = Bar(composing: true, draft: "Add a golden balloon", sendable: false, why: "Read This Part");
        Assert.That((waiting.State, waiting.Words.Text, waiting.WidthDp, waiting.FieldAction), Is.EqualTo((RoomBarState.Open, "Add a golden balloon", 696f, FileScreens.EditInstruction)));
        Assert.That(waiting.Words.Runs[0].Theirs, Is.True, "the person's words");
        Assert.That((waiting.SendShown, waiting.SendAvailable, waiting.WhyNotSend), Is.EqualTo((true, false, "Read This Part")));

        var ready = Bar(composing: true, draft: "Add a golden balloon", sendable: true, why: "Read This Part");
        Assert.That((ready.SendAvailable, ready.WhyNotSend), Is.EqualTo((true, (string?)null)));
        Assert.That((RoomBar.SendAction, RoomBar.MicAction), Is.EqualTo((FileScreens.SendInstruction, FileScreens.HoldToTalk)));
    }

    [Test]
    public void VoiceShowsFirstThenTheComposerThenSent()
    {
        Assert.That(Bar(composing: true, draft: "x", voice: VoiceStage.Listening, sent: true).State, Is.EqualTo(RoomBarState.Listening));
        Assert.That(Bar(voice: VoiceStage.Listening).Words.Text, Is.EqualTo("Listening"));
        Assert.That(Bar(voice: VoiceStage.WritingDown).Words.Text, Is.EqualTo("Transcribing"));
        Assert.That(Bar(composing: true, sent: true).State, Is.EqualTo(RoomBarState.Open), "a new draft while the last waits to start");
        var sent = Bar(sent: true);
        Assert.That((sent.State, sent.Words.Text, sent.WidthDp, sent.SendShown, sent.FieldAction),
            Is.EqualTo((RoomBarState.Sent, "Sent · Awaiting Start", 340f, false, (string?)null)));
    }
}

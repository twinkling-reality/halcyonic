using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The microphone's question (ADR 0021, amended 2026-11-11): asked once at the first open after a line that says why,
/// so the first hold records; a hold asks only where no answer was given or the person refused once; where the system
/// no longer asks, the hold says how to allow it; a driven launch never asks at the open.
/// </summary>
public class MicrophoneAskTests
{
    [Test]
    public void TheSystemsFactsAndTheKeptAnswerReadAsOneAccess()
    {
        Assert.That(MicrophoneAsk.Read(allowed: true, answeredBefore: false, systemWouldExplain: false), Is.EqualTo(MicrophoneAccess.Allowed));
        Assert.That(MicrophoneAsk.Read(allowed: false, answeredBefore: false, systemWouldExplain: false), Is.EqualTo(MicrophoneAccess.NeverAsked));
        Assert.That(MicrophoneAsk.Read(allowed: false, answeredBefore: true, systemWouldExplain: true), Is.EqualTo(MicrophoneAccess.RefusedOnce));
        Assert.That(MicrophoneAsk.Read(allowed: false, answeredBefore: true, systemWouldExplain: false), Is.EqualTo(MicrophoneAccess.RefusedForGood));
    }

    [Test]
    public void OnlyTheFirstOpenAsksAndNeverADrivenLaunch()
    {
        Assert.That(MicrophoneAsk.WaitsAtOpen(MicrophoneAccess.NeverAsked, drivenLaunch: false), Is.True);
        Assert.That(MicrophoneAsk.AskAtOpen(MicrophoneAccess.NeverAsked, drivenLaunch: false, computerConfigured: true, recordedRunShowing: false), Is.True);
        Assert.That(MicrophoneAsk.WaitsAtOpen(MicrophoneAccess.NeverAsked, drivenLaunch: true), Is.False);
        Assert.That(MicrophoneAsk.AskAtOpen(MicrophoneAccess.NeverAsked, drivenLaunch: true, computerConfigured: true, recordedRunShowing: false), Is.False);
        foreach (var later in new[] { MicrophoneAccess.Allowed, MicrophoneAccess.RefusedOnce, MicrophoneAccess.RefusedForGood })
        {
            Assert.That(MicrophoneAsk.WaitsAtOpen(later, drivenLaunch: false), Is.False, later.ToString());
            Assert.That(MicrophoneAsk.AskAtOpen(later, drivenLaunch: false, computerConfigured: true, recordedRunShowing: false), Is.False, later.ToString());
        }
    }

    [Test]
    public void NothingIsAskedWithoutAComputerOrOverTheRecordedDemonstration()
    {
        Assert.That(MicrophoneAsk.AskAtOpen(MicrophoneAccess.NeverAsked, drivenLaunch: false, computerConfigured: false, recordedRunShowing: false), Is.False, "no computer to take the voice");
        Assert.That(MicrophoneAsk.AskAtOpen(MicrophoneAccess.NeverAsked, drivenLaunch: false, computerConfigured: true, recordedRunShowing: true), Is.False, "the recorded demonstration shows");
        Assert.That(MicrophoneAsk.AskAtOpen(MicrophoneAccess.NeverAsked, drivenLaunch: false, computerConfigured: false, recordedRunShowing: true), Is.False);
        Assert.That(MicrophoneAsk.WaitsAtOpen(MicrophoneAccess.NeverAsked, drivenLaunch: false), Is.True, "it keeps waiting: nothing is kept, a later moment may ask");
    }

    [Test]
    public void OnceAllowedAtTheOpenTheFirstHoldRecords()
    {
        var access = MicrophoneAsk.After(MicrophoneAnswer.Granted);
        Assert.That(MicrophoneAsk.AtHold(access), Is.EqualTo(MicrophoneAtHold.Record));
        Assert.That(MicrophoneAsk.WordsAtHold(access), Is.Null);
    }

    [Test]
    public void ADismissedQuestionIsAskedAgainAtTheHold()
    {
        Assert.That(MicrophoneAsk.CountsAsAnswer(MicrophoneAnswer.Dismissed), Is.False);
        var access = MicrophoneAsk.After(MicrophoneAnswer.Dismissed);
        Assert.That(MicrophoneAsk.AtHold(access), Is.EqualTo(MicrophoneAtHold.Ask));
        Assert.That(MicrophoneAsk.WordsAtHold(access), Is.EqualTo(VoiceText.AllowMicrophone));
    }

    [Test]
    public void ARefusalOnceAsksAgainWithTheWhyAndARefusalForGoodSaysHowToAllowIt()
    {
        var once = MicrophoneAsk.After(MicrophoneAnswer.Denied);
        Assert.That(MicrophoneAsk.AtHold(once), Is.EqualTo(MicrophoneAtHold.Ask));
        Assert.That(MicrophoneAsk.WordsAtHold(once), Is.EqualTo(MicrophoneAsk.Why));

        var forGood = MicrophoneAsk.After(MicrophoneAnswer.DeniedForGood);
        Assert.That(MicrophoneAsk.AtHold(forGood), Is.EqualTo(MicrophoneAtHold.Explain), "the system no longer asks: nothing is requested");
        Assert.That(MicrophoneAsk.WordsAtHold(forGood), Is.EqualTo(VoiceText.MicrophoneRefused));
        Assert.That(MicrophoneAsk.WordsAfter(MicrophoneAnswer.DeniedForGood), Is.EqualTo(VoiceText.MicrophoneRefused));
    }

    [Test]
    public void EveryAnswerButADismissalIsKept()
    {
        foreach (var answer in new[] { MicrophoneAnswer.Granted, MicrophoneAnswer.Denied, MicrophoneAnswer.DeniedForGood })
            Assert.That(MicrophoneAsk.CountsAsAnswer(answer), Is.True, answer.ToString());
        Assert.That(MicrophoneAsk.WordsAfter(MicrophoneAnswer.Granted), Is.EqualTo(VoiceText.MicrophoneAllowed));
    }
}

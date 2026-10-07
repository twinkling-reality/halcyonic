using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>Hold to talk's one voice for the menu's columns: its words only for the column that held (ADR 0021, ADR 0026).</summary>
[TestFixture]
public class MenuVoiceTests
{
    /// <summary>A column that records what the voice gives it.</summary>
    private sealed class Column : IMenuColumn
    {
        public List<string> Got { get; } = new();

        public MenuFrame? Frame => null;

#pragma warning disable CS0067
        public event Action? Changed;

        public event Action? Closed;
#pragma warning restore CS0067

        public void Act(string id, string? key)
        {
        }

        public void Drawn(MenuFrame drawn, Footer? sidePanel)
        {
        }

        public void HoldStarted(string id) => Got.Add("hold " + id);

        public void HoldEnded(string id, bool letGo) => Got.Add("ended " + id + " " + letGo);

        public void Heard(string text) => Got.Add("heard " + text);

        public void Said(string words) => Got.Add("said " + words);

        public void Tick()
        {
        }

        public void FocusLeft()
        {
        }
    }

    /// <summary>Hold to talk as the director has it: recording, then waiting for the computer's answer, until it comes or is dropped.</summary>
    private sealed class Voice
    {
        public bool Recording { get; set; }

        public bool Waiting { get; set; }

        /// <summary>Begin can't record, as with no microphone, and says why.</summary>
        public bool Refuses { get; set; }

        public int Dropped { get; private set; }

        public MenuVoice Menu { get; }

        public Voice() => Menu = new MenuVoice(() => Recording || Waiting, Begin, Send, Drop);

        private void Begin()
        {
            if (Refuses)
            {
                Menu.Said("No microphone");
                return;
            }
            Recording = true;
            Menu.Said("Listening");
        }

        private void Send()
        {
            Recording = false;
            Waiting = true;
            Menu.Said("Hearing");
        }

        /// <summary>The computer's answer can't be called back: dropping stops a recording, but the words already sent are still worked on.</summary>
        public bool KeepsWorking { get; set; }

        private void Drop()
        {
            Recording = false;
            Waiting &= KeepsWorking;
            Dropped++;
        }

        /// <summary>The computer answers what it heard.</summary>
        public void Answer(string text)
        {
            Waiting = false;
            Menu.Heard(text);
        }
    }

    [Test]
    public void TheVoiceSaysItListensWhileHeldThenWritesDownUntilTheWordsComeOrAreDropped()
    {
        var voice = new Voice();
        var file = new Column();
        Assert.That(voice.Menu.Stage, Is.EqualTo(VoiceStage.Idle));
        voice.Menu.Hold(file, "speak");
        Assert.That((voice.Menu.Stage, voice.Menu.Holding?.Id), Is.EqualTo((VoiceStage.Listening, (string?)"speak")));
        voice.Menu.Ended(file, "speak", letGo: true);
        Assert.That((voice.Menu.Stage, voice.Menu.Holding?.Id), Is.EqualTo((VoiceStage.WritingDown, (string?)"speak")), "let go, the computer writes it down");
        voice.Answer("ship it");
        Assert.That((voice.Menu.Stage, voice.Menu.Holding), Is.EqualTo((VoiceStage.Idle, ((IMenuColumn, string)?)null)), "the words came, so no prompt shows the voice");

        // Let go off the prompt, or refused for want of a microphone: nothing to write down.
        voice.Menu.Hold(file, "speak");
        voice.Menu.Ended(file, "speak", letGo: false);
        Assert.That(voice.Menu.Stage, Is.EqualTo(VoiceStage.Idle));
        voice.Refuses = true;
        voice.Menu.Hold(file, "speak");
        Assert.That(voice.Menu.Stage, Is.EqualTo(VoiceStage.Idle));
    }

    [Test]
    public void FocusLeavingWhileTheWordsAreWrittenDownLeavesTheVoiceIdle()
    {
        var voice = new Voice();
        var file = new Column();
        voice.Menu.Hold(file, "speak");
        voice.Menu.Ended(file, "speak", letGo: true);
        voice.Menu.FocusLeft();
        Assert.That(voice.Menu.Stage, Is.EqualTo(VoiceStage.Idle));
    }

    [Test]
    public void WordsStillToComeForFileAReachOnlyFileAAndAHoldOnFileBStartsNothing()
    {
        var voice = new Voice();
        var a = new Column();
        var b = new Column();
        Assert.That(voice.Menu.Hold(a, "speak"), Is.True);
        voice.Menu.Ended(a, "speak", letGo: true);
        Assert.That(voice.Menu.Hold(b, "speak"), Is.False, "the voice still waits for A's words");
        voice.Menu.Ended(b, "speak", letGo: true);
        voice.Answer("ship it");
        Assert.That(a.Got, Is.EqualTo(new[] { "said Listening", "hold speak", "ended speak True", "said Hearing", "heard ship it" }));
        Assert.That(b.Got, Is.Empty, "B learns of no hold and hears none of A's words");

        Assert.That(voice.Menu.Hold(b, "speak"), Is.True, "once A's words came, B may hold");
        Assert.That(b.Got, Is.EqualTo(new[] { "said Listening", "hold speak" }));
    }

    [Test]
    public void AHoldTheVoiceCouldNotStartIsNoHoldButItsColumnHearsWhy()
    {
        var voice = new Voice { Refuses = true };
        var a = new Column();
        Assert.That(voice.Menu.Hold(a, "speak"), Is.False);
        voice.Menu.Ended(a, "speak", letGo: true);
        Assert.That(a.Got, Is.EqualTo(new[] { "said No microphone" }), "no hold started, so none ends, and nothing is sent");
        Assert.That(voice.Waiting, Is.False);
    }

    [Test]
    public void OnlyTheHoldThatStartedTheRecordingEndsIt()
    {
        var voice = new Voice();
        var a = new Column();
        var b = new Column();
        voice.Menu.Hold(a, "speak");
        voice.Menu.Ended(b, "speak", letGo: true);
        voice.Menu.Ended(a, "other", letGo: true);
        Assert.That(voice.Recording, Is.True, "another column's, or another prompt's, let go ends nothing");
        voice.Menu.Ended(a, "speak", letGo: false);
        Assert.That((voice.Recording, voice.Waiting, voice.Dropped), Is.EqualTo((false, false, 1)), "dropped, not sent");
        Assert.That(a.Got.Last(), Is.EqualTo("ended speak False"));
    }

    [Test]
    public void AColumnThatLeftWhileItsWordsWereWrittenDownShowsTheVoiceNowhere()
    {
        // Another file opened while the computer still works on what was said: the file that held left the plane,
        // so no Hold to talk shows Writing down for words spoken elsewhere, even while the computer still works.
        var voice = new Voice { KeepsWorking = true };
        var a = new Column();
        voice.Menu.Hold(a, "speak");
        voice.Menu.Ended(a, "speak", letGo: true);
        Assert.That(voice.Menu.Stage, Is.EqualTo(VoiceStage.WritingDown));
        voice.Menu.Left(a);
        Assert.That(voice.Waiting, Is.True, "the computer still works on the words");
        Assert.That((voice.Menu.Stage, voice.Menu.Holding), Is.EqualTo((VoiceStage.Idle, ((IMenuColumn, string)?)null)));
    }

    [Test]
    public void TheColumnThatHeldLeavingThePlaneStopsTheVoiceAndHearsNoMore()
    {
        var voice = new Voice();
        var a = new Column();
        var b = new Column();
        voice.Menu.Hold(a, "speak");
        voice.Menu.Left(b);
        Assert.That(voice.Recording, Is.True, "another column leaving changes nothing");
        voice.Menu.Left(a);
        Assert.That((voice.Recording, voice.Dropped, voice.Menu.Speaking), Is.EqualTo((false, 1, (IMenuColumn?)null)));
        Assert.That(a.Got.Last(), Is.EqualTo("ended speak False"), "its hold ends as dropped");
        voice.Menu.Heard("late words");
        Assert.That(a.Got.Concat(b.Got).Any(got => got.StartsWith("heard")), Is.False, "words that come late reach no one");
    }

    [Test]
    public void LosingFocusDropsWhatTheVoiceRecordsOrAwaits()
    {
        var voice = new Voice();
        var a = new Column();
        voice.Menu.Hold(a, "speak");
        voice.Menu.Ended(a, "speak", letGo: true);
        voice.Menu.FocusLeft();
        Assert.That((voice.Waiting, voice.Dropped), Is.EqualTo((false, 1)));
        Assert.That(voice.Menu.Hold(a, "speak"), Is.True, "back, a new hold records");
        voice.Menu.FocusLeft();
        Assert.That(a.Got.Last(), Is.EqualTo("ended speak False"));
    }
}

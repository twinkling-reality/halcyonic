using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The first question's plate, until the computer's first task (ADR 0026).</summary>
[TestFixture]
public class FirstQuestionTests
{
    private sealed class Opens
    {
        public List<string> Got { get; } = new();

        public FirstQuestionColumn Column(FakeMenuHost host) =>
            new(host, heard => Got.Add("start " + (heard ?? "nothing heard")), () => Got.Add("projects"));
    }

    private static FakeMenuHost Host(bool voice = true) => new() { VoiceOffered = voice };

    [Test]
    public void ItAsksWhatYouWouldLikeToWorkOnWithSomethingNewChosenAndHoldToTalkBesideStartAProject()
    {
        var frame = FirstQuestionScreens.Question(FirstAnswer.SomethingNew, voice: true, said: null);
        Assert.That(frame.Subject, Is.EqualTo("What would you like to work on?"));
        Assert.That(frame.Sections, Is.Empty, "the navigator gives the row of places");
        Assert.That(frame.Lines.Select(line => (line.Words, line.Chosen, line.Opens, line.Choice)), Is.EqualTo(new[]
        {
            ("Something new", true, false, false),
            ("A project on your computer", false, false, false),
        }), "both rows choose and neither opens a side panel, so neither shows a chevron");
        Assert.That(frame.Lines.Select(line => line.Icon), Is.EqualTo(new GlazeIcon?[] { GlazeIcon.CreateProject, GlazeIcon.Folder }));
        Assert.That(frame.Footer[PromptSlot.Close]!.Words, Is.EqualTo("Close"));
        Assert.That((frame.Footer[PromptSlot.Secondary]!.Words, frame.Footer[PromptSlot.Secondary]!.Holds), Is.EqualTo(("Hold to talk", true)));
        Assert.That((frame.Footer[PromptSlot.FarRight]!.Words, frame.Footer[PromptSlot.FarRight]!.DrawnAsMain), Is.EqualTo(("Start a project", true)));
        Assert.That(frame.Reason, Is.Null, "nothing is drawn quiet");
    }

    [Test]
    public void ChoosingAProjectOnYourComputerSetsShowMyProjectsAndTakesHoldToTalkAway()
    {
        var frame = FirstQuestionScreens.Question(FirstAnswer.OnYourComputer, voice: true, said: null);
        Assert.That(frame.Lines.Single(line => line.Chosen).Words, Is.EqualTo("A project on your computer"));
        Assert.That(frame.Footer[PromptSlot.Secondary], Is.Null, "gone, not drawn quiet");
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Show my projects"));
        Assert.That(frame.Reason, Is.Null);
    }

    [Test]
    public void WithoutAVoiceThereIsNoHoldToTalk()
    {
        Assert.That(FirstQuestionScreens.Question(FirstAnswer.SomethingNew, voice: false, said: null).Footer[PromptSlot.Secondary], Is.Null);
    }

    [Test]
    public void ItsMainActionOpensNewProjectOrProjectsInItsPlaceAndCloseClosesIt()
    {
        var opens = new Opens();
        var column = opens.Column(Host());
        var closed = 0;
        column.Closed += () => closed++;
        column.Act(FirstQuestionScreens.ShowProjects, null);
        Assert.That(opens.Got, Is.Empty, "Show my projects isn't offered while Something new is chosen");
        column.Act(FirstQuestionScreens.StartProject, null);
        Assert.That(opens.Got, Is.EqualTo(new[] { "start nothing heard" }));

        var changed = 0;
        column.Changed += () => changed++;
        column.Act(FirstQuestionScreens.Choose, nameof(FirstAnswer.OnYourComputer));
        Assert.That((column.Chosen, changed), Is.EqualTo((FirstAnswer.OnYourComputer, 1)));
        column.Act(FirstQuestionScreens.Choose, nameof(FirstAnswer.OnYourComputer));
        Assert.That(changed, Is.EqualTo(1), "choosing the chosen row changes nothing");
        column.Act(FirstQuestionScreens.StartProject, null);
        column.Act(FirstQuestionScreens.ShowProjects, null);
        Assert.That(opens.Got, Is.EqualTo(new[] { "start nothing heard", "projects" }));

        column.Act(Footer.Close, null);
        column.Act(FirstQuestionScreens.ShowProjects, null);
        Assert.That((closed, opens.Got.Count), Is.EqualTo((1, 2)), "once closed, a press still queued does nothing");
    }

    [Test]
    public void TheIdeaHeardOpensNewProjectWithItOnlyWhileSomethingNewIsChosen()
    {
        var opens = new Opens();
        var column = opens.Column(Host());
        column.Heard("  ");
        Assert.That(opens.Got, Is.Empty, "nothing heard opens nothing");
        column.Heard("a bird feeder that counts visits");
        Assert.That(opens.Got, Is.EqualTo(new[] { "start a bird feeder that counts visits" }));

        column.Act(FirstQuestionScreens.Choose, nameof(FirstAnswer.OnYourComputer));
        column.Heard("late words");
        Assert.That(opens.Got, Has.Count.EqualTo(1), "words heard for a Hold to talk no longer shown are dropped");
    }

    [Test]
    public void WhyNothingCameOfHoldToTalkIsALineUntilAnotherRowIsChosen()
    {
        var column = new Opens().Column(Host());
        column.Said(VoiceText.Listening);
        Assert.That(column.Frame!.Lines, Has.Count.EqualTo(2), "listening shows on Hold to talk itself");
        column.Said(VoiceText.NothingHeard);
        Assert.That(column.Frame!.Lines.Last().Words, Is.EqualTo(VoiceText.NothingHeard));
        column.Act(FirstQuestionScreens.Choose, nameof(FirstAnswer.OnYourComputer));
        Assert.That(column.Frame!.Lines, Has.Count.EqualTo(2));
    }

    [Test]
    public void HoldToTalkComesAndGoesWithTheVoice()
    {
        var host = Host(voice: false);
        var column = new Opens().Column(host);
        Assert.That(column.Frame!.Footer[PromptSlot.Secondary], Is.Null);
        host.VoiceOffered = true;
        var changed = 0;
        column.Changed += () => changed++;
        column.Tick();
        column.Tick();
        Assert.That((changed, column.Frame!.Footer[PromptSlot.Secondary]?.Words), Is.EqualTo((1, "Hold to talk")));
    }
}

#nullable enable
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The information panel as a card (the addendum's A17): a name as the title, facts as slots, a status over its line, a track of tries, a list.</summary>
public sealed class RoomCardTests
{
    [Test]
    public void ACardReadsItsWordsInTheOrderItShowsThem()
    {
        var card = new RoomCard(LineWords.TheirName("Kite Run"), LineWords.Ours("Make a kite"), RoomCardHead.Project, new RoomCardStatus("Fixed", "The build passed", RoomTone.Good),
            new[] { new RoomSlot(RoomKind.Starter, LineWords.Ours("VR Game")) }, new[] { new RoomCardStep("Build", RoomTone.Good) },
            list: new[] { new RoomListRow(RoomKind.Changed, LineWords.TheirName("kite.js"), "Changed") }, more: LineWords.Ours("As the provider reports it"));

        Assert.That(card.Words().ToList(), Is.EqualTo(new[] { "Fixed", "The build passed", "Kite Run", "Make a kite", "VR Game", "Build", "kite.js", "Changed", "As the provider reports it" }));
        Assert.That(card.AnyDot, Is.False);
    }

    [Test]
    public void AFactStrungOnTheSeparatorIsCaught()
    {
        var card = new RoomCard(LineWords.Ours("Model" + KitMomentWords.Dot + "Glimmer"));

        Assert.That(card.AnyDot, Is.True, "the card's language never joins two facts on a dot");
    }

    [Test]
    public void ATrackNeedsTriesAndAGaugeStaysInItsRange()
    {
        Assert.That(new RoomCard(trackAt: 1, trackOf: 0).TrackOf, Is.Null);
        Assert.That(new RoomCard(trackAt: 1, trackOf: 0).TrackAt, Is.Null, "no track, no try under way");
        Assert.That(new RoomCard(trackAt: 3, trackOf: 2).TrackAt, Is.EqualTo(3), "past the last, every try is done");
        Assert.That(new RoomGauge(LineWords.Ours("This Week"), 140, null).PercentLeft, Is.EqualTo(100));
        Assert.That(new RoomGauge(LineWords.Ours("This Week"), -5, null).PercentLeft, Is.EqualTo(0));
    }

    [Test]
    public void OutsideWordsOnACardStayTheirs()
    {
        // AUDIT's F1: a companion's proposal on the recap's foot line, and a provider's label on a gauge, keep their outside runs.
        var idea = new ProjectIdea();
        idea.UseIdea("A page of race times for my running club");
        var draft = new NewWorkDraft(new CommandFactory(Samples.Client));
        var proposal = LineWords.Ours("Suggested ") + LineWords.TheirName("Race Times");
        var card = NewProjectScreens.RecapCard(idea, draft, null, live: true, proposal);
        Assert.That(card.More, Is.Not.Null);
        Assert.That(card.More!.Runs.Any(run => run.Theirs && run.Text.Contains("Race Times")), Is.True, "the companion's words stay its own on the card");
        Assert.That(card.More.Runs.Any(run => !run.Theirs), Is.True, "and the product's own words stay upright beside them");
        var gauge = new RoomGauge(LineWords.TheirName("Pro Window"), 50, null);
        Assert.That(gauge.Label.Runs.All(run => run.Theirs), Is.True, "a provider's label leans as theirs");
    }

    [Test]
    public void AnEmptyCardHoldsNothing()
    {
        var card = new RoomCard();

        Assert.That(card.Words(), Is.Empty);
        Assert.That(card.Slots, Is.Empty);
        Assert.That(card.List, Is.Empty);
        Assert.That(card.Head, Is.EqualTo(RoomCardHead.None));
    }
}

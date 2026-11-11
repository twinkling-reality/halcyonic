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
    public void ACodeToScanKeepsTheHostsCellsAndHoldsNoWords()
    {
        var dark = Enumerable.Range(0, 21 * 21).Select(index => index % 3 == 0).ToArray();
        var code = new RoomCode(21, dark);
        var card = new RoomCard(status: new RoomCardStatus("Shared", "Scan the code to play", RoomTone.Good), code: code);

        Assert.That(card.Code, Is.SameAs(code));
        Assert.That(code.Side, Is.EqualTo(21));
        Assert.That(code.IsDark(0, 0), Is.True);
        Assert.That(code.IsDark(1, 0), Is.False);
        Assert.That(code.IsDark(0, 1), Is.EqualTo(21 % 3 == 0), "row by row from the top left");
        dark[1] = true;
        Assert.That(code.IsDark(1, 0), Is.False, "the code keeps its own copy of the host's cells");
        Assert.That(card.Words().ToList(), Is.EqualTo(new[] { "Shared", "Scan the code to play" }), "a code holds no words");
        Assert.That(new RoomCard().Code, Is.Null);
    }

    [Test]
    public void ACodeRefusesASideOutOfRangeOrTheWrongCount()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new RoomCode(20, new bool[400]));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new RoomCode(178, new bool[178 * 178]));
        Assert.Throws<System.ArgumentException>(() => new RoomCode(21, new bool[21 * 21 - 1]));
        Assert.Throws<System.ArgumentNullException>(() => new RoomCode(21, null!));
        Assert.That(new RoomCode(177, new bool[177 * 177]).Side, Is.EqualTo(177));
    }

    [Test]
    public void APageOfReadingIsTheFilesOwnWordsAndNeverReadForTheSeparator()
    {
        var reading = new[]
        {
            new RoomReadingRow(RoomLineKind.Same, 11, LineWords.TheirName("const tail = [];")),
            new RoomReadingRow(RoomLineKind.Added, 12, LineWords.TheirName("label = a" + KitMomentWords.Dot.Trim() + "b;")),
            new RoomReadingRow(RoomLineKind.Added, 99, LineWords.TheirName(".join();"), continued: true, indent: 2),
            RoomReadingRow.GapRow,
        };
        var card = new RoomCard(LineWords.TheirName("kite.js"), slots: new[] { new RoomSlot(RoomKind.Changed, LineWords.Ours("Edited")) }, reading: reading);

        Assert.That(card.Words().ToList(), Is.EqualTo(new[] { "kite.js", "Edited", "const tail = [];", "label = a" + KitMomentWords.Dot.Trim() + "b;", ".join();" }));
        Assert.That(card.AnyDot, Is.False, "a file's line may hold any character; only the card's own facts are read for the separator");
        Assert.That(reading[2].Number, Is.Null, "a continued row carries no number");
        Assert.That(reading[2].Indent, Is.EqualTo(2), "a line's leading space is kept as a count, since outside words lose it");
        Assert.That(reading[3].Text, Is.Null);
        Assert.That(new RoomReadingRow(RoomLineKind.Same, 1, LineWords.TheirName("x"), indent: -3).Indent, Is.EqualTo(0));
        Assert.That(new RoomCard().Reading, Is.Empty);
    }

    [Test]
    public void AScreenWrapsAFileToTheColumnsTheCardDraws()
    {
        // 384 dp of card less 60 of gutter and mark and 12 kept clear at the right, at 9 dp a character; at larger text
        // 375.6 less 69 and 13.8, at 10.35.
        Assert.That(RoomReading.Columns(1f), Is.EqualTo(34));
        Assert.That(RoomReading.Columns(1f, continued: true), Is.EqualTo(32));
        Assert.That(RoomReading.Columns(Comfort.LargerTextScale), Is.EqualTo(28));
        Assert.That(RoomReading.Columns(Comfort.LargerTextScale, continued: true), Is.EqualTo(26));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => RoomReading.Columns(0f));
    }

    [Test]
    public void AStepMayBeNamedByTheProjectAndLeansAsItsWords()
    {
        var theirs = new RoomCardStep(LineWords.TheirName("typecheck"), RoomTone.Good);
        var ours = new RoomCardStep("Build", RoomTone.Bad, 1, 2);

        Assert.That(theirs.Name, Is.EqualTo("typecheck"));
        Assert.That(theirs.Words.Runs.Single().Theirs, Is.True, "a script's name is the project's words");
        Assert.That(ours.Words.Runs.Single().Theirs, Is.False);
        Assert.That((ours.TriesUsed, ours.TriesOf), Is.EqualTo((1, 2)));
        Assert.That(new RoomCard(steps: new[] { theirs, ours }).Words().ToList(), Is.EqualTo(new[] { "typecheck", "Build" }));
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

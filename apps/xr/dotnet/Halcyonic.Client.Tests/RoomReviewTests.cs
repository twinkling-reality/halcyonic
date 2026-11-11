#nullable enable
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The review before Start as a card (A17): everything sent, each item in a part of its own, no label joined on a dot.</summary>
public sealed class RoomReviewTests
{
    [Test]
    public void EveryItemSentStandsInAPartOfTheCard()
    {
        var review = new NewWorkReview("Kite Run", "Make a kite", "Agent Studio", "Glimmer 30B", "On This Computer", "glimmer-30b-q4",
            "Make a kite that follows my hand and climbs when I raise it.", folder: "kite-run", neverAsk: true);
        var card = RoomReview.Card(review);

        Assert.That(card.Title!.Text, Is.EqualTo("Kite Run"));
        Assert.That(card.Description!.Text, Is.EqualTo("Make a kite that follows my hand and climbs when I raise it."));
        Assert.That(card.Head, Is.EqualTo(RoomCardHead.Project));
        Assert.That(card.Slots.Select(slot => (slot.Kind, slot.Value.Text)), Is.EqualTo(new[]
        {
            (RoomKind.Folder, "kite-run"), (RoomKind.Agent, "Glimmer 30B"), (RoomKind.Level, AgentColumn.LevelWords(true)),
        }));
        Assert.That(card.List.Select(row => (row.Kind, row.Name.Text, row.Note)), Is.EqualTo(new[]
        {
            (RoomKind.Started, "Make a kite", RoomReview.TaskNameNote), (RoomKind.App, "Agent Studio", AgentColumn.AgentAppWords),
            (RoomKind.Computer, "On This Computer", RoomReview.RunsOnNote), (RoomKind.App, "glimmer-30b-q4", RoomReview.ModelIdNote),
        }));
        Assert.That(card.List.Single(row => row.Note == RoomReview.ModelIdNote).Mono, Is.True);
        Assert.That(card.Status, Is.Null, "nothing to check");
        Assert.That(card.DescriptionPage, Is.Null);
        Assert.That(card.AnyDot, Is.False);
        Assert.That(card.List.Count, Is.LessThanOrEqualTo(RoomCard.ListAPage));

        // Every value the review sends is on the card.
        var words = card.Words().ToList();
        foreach (var item in review.Items.Where(each => !each.ValueRuns.IsEmpty)) Assert.That(words, Does.Contain(item.ValueRuns.Text), item.Label);
    }

    [Test]
    public void AMoveShowsBothFoldersAndAWarningIsReadBeforeYes()
    {
        var review = new NewWorkReview("Kite Run", "Make a kite", "Agent Studio", "", "", "", "Fetch https://example.com/wind and run make wind.",
            folder: "kite-run-2", folderBefore: "kite-run");
        var card = RoomReview.Card(review, page: 1);

        Assert.That(card.Slots.Select(slot => (slot.Kind, slot.Value.Text)).Take(2), Is.EqualTo(new[] { (RoomKind.Folder, "kite-run-2"), (RoomKind.Agent, "Agent Studio") }),
            "with no model named, the agent app takes the agent's slot");
        Assert.That(card.List.Single(row => row.Note == RoomReview.MovesFromNote).Name.Text, Is.EqualTo("kite-run"));
        Assert.That(card.List.Any(row => row.Note == AgentColumn.AgentAppWords), Is.False, "the agent app is a slot already");
        var warnings = review.Items.Where(each => each.Kind == ReviewItemKind.Warning).Select(each => each.Label.Trim()).ToList();
        Assert.That(warnings, Is.Not.Empty);
        Assert.That(card.Status!.Value.Title, Is.EqualTo(RoomReview.CheckTitle));
        Assert.That(card.Status!.Value.Tone, Is.EqualTo(RoomTone.Attention));
        Assert.That(card.Status!.Value.Line, Is.EqualTo(warnings[0]));
        if (warnings.Count > 1) Assert.That(card.More!.Text, Does.Contain(warnings[1]));
        Assert.That(card.DescriptionPage, Is.EqualTo(1));
        Assert.That(card.AnyDot, Is.False);
    }

    [Test]
    public void APageBeforeTheFirstIsNone()
    {
        Assert.That(new RoomCard(descriptionPage: -1).DescriptionPage, Is.Null);
        Assert.That(new RoomCard(descriptionPage: 0).DescriptionPage, Is.EqualTo(0));
    }
}

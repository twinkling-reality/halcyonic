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
        var task = RoomReview.Card(review, part: 0, taskPages: 1);
        var card = RoomReview.Card(review, part: 1, taskPages: 1);

        Assert.That(RoomReview.Parts(1), Is.EqualTo(2));
        Assert.That(task.Title!.Text, Is.EqualTo("Kite Run"));
        Assert.That(task.Description!.Text, Is.EqualTo("Make a kite that follows my hand and climbs when I raise it."));
        Assert.That(task.DescriptionPage, Is.EqualTo(0), "the task's part pages, so its view measures how many pages it makes");
        Assert.That(task.More!.Text, Is.EqualTo("Part 1 of 2"));
        Assert.That(task.Slots, Is.Empty);
        Assert.That(card.Title!.Text, Is.EqualTo("Kite Run"));
        Assert.That(card.Description, Is.Null, "the last part is what else is sent");
        Assert.That(card.Slots.Select(slot => (slot.Kind, slot.Value.Text)), Is.EqualTo(new[]
        {
            (RoomKind.Folder, "kite-run"), (RoomKind.Agent, "Glimmer 30B"), (RoomKind.Level, AgentColumn.LevelWords(true)),
        }));
        Assert.That(card.List.Select(row => (row.Kind, row.Name.Text)), Is.EqualTo(new[]
        {
            (RoomKind.Task, "Make a kite"), (RoomKind.App, "Agent Studio"), (RoomKind.Computer, "On This Computer"), (RoomKind.Agent, "glimmer-30b-q4"),
        }));
        Assert.That(card.List.All(row => row.Note == null), Is.True, "a row's mark says what it is; no note names a field");
        Assert.That(card.More!.Text, Is.EqualTo("Part 2 of 2"));
        Assert.That(card.Status, Is.Null, "nothing to check");
        Assert.That(card.DescriptionPage, Is.Null);
        Assert.That(card.AnyDot, Is.False);
        Assert.That(card.List.Count, Is.LessThanOrEqualTo(RoomCard.ListAPage));

        // Every value the review sends is on one of its parts.
        var words = task.Words().Concat(card.Words()).ToList();
        foreach (var item in review.Items.Where(each => !each.ValueRuns.IsEmpty)) Assert.That(words, Does.Contain(item.ValueRuns.Text), item.Label);
    }

    [Test]
    public void AMoveShowsBothFoldersAndAWarningIsReadBeforeYes()
    {
        var review = new NewWorkReview("Kite Run", "Make a kite", "Agent Studio", "", "", "", "Fetch https://example.com/wind and run make wind.",
            folder: "kite-run-2", folderBefore: "kite-run");
        var card = RoomReview.Card(review, part: 2, taskPages: 2);

        Assert.That(card.Slots.Select(slot => (slot.Kind, slot.Value.Text)).Take(2), Is.EqualTo(new[] { (RoomKind.Folder, "kite-run-2"), (RoomKind.Agent, "Agent Studio") }),
            "with no model named, the agent app takes the agent's slot");
        Assert.That(card.List.Single(row => row.Kind == RoomKind.Folder).Name.Text, Is.EqualTo(RoomReview.MovesFrom + "kite-run"));
        Assert.That(card.List.Any(row => row.Kind == RoomKind.App), Is.False, "the agent app is a slot already");
        var warnings = review.Items.Where(each => each.Kind == ReviewItemKind.Warning).Select(each => each.Label.Trim()).ToList();
        Assert.That(warnings, Is.Not.Empty);
        Assert.That(card.Status!.Value.Title, Is.EqualTo(RoomReview.CheckTitle));
        Assert.That(card.Status!.Value.Tone, Is.EqualTo(RoomTone.Attention));
        Assert.That(card.Status!.Value.Line, Is.EqualTo(string.Join(" ", warnings)), "every warning is read before Yes, in the status's line");
        Assert.That(card.More!.Text, Is.EqualTo("Part 3 of 3"));
        var second = RoomReview.Card(review, part: 1, taskPages: 2);
        Assert.That((second.DescriptionPage, second.More!.Text, second.Status), Is.EqualTo(((int?)1, "Part 2 of 3", (RoomCardStatus?)null)), "a page of the task, no warning yet");
        Assert.That(RoomReview.Card(review, part: 9, taskPages: 2).More!.Text, Is.EqualTo("Part 3 of 3"), "past the last part is the last");
        Assert.That(card.AnyDot, Is.False);
    }

    [Test]
    public void APageBeforeTheFirstIsNone()
    {
        Assert.That(new RoomCard(descriptionPage: -1).DescriptionPage, Is.Null);
        Assert.That(new RoomCard(descriptionPage: 0).DescriptionPage, Is.EqualTo(0));
    }
}

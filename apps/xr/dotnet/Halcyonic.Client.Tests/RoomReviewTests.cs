#nullable enable
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The review before Start as cards in parts (A17): everything sent, each item in a part of a card, no label joined on a dot.</summary>
public sealed class RoomReviewTests
{
    [Test]
    public void TheTaskThenHowItRunsAndEveryItemSentStandsInAPart()
    {
        var review = new NewWorkReview("Kite Run", "Make a kite", "Agent Studio", "Glimmer 30B", "On This Computer", "glimmer-30b-q4",
            "Make a kite that follows my hand and climbs when I raise it.", folder: "kite-run", neverAsk: true);
        var task = RoomReview.Card(review, part: 0, taskPages: 1);
        var runs = RoomReview.Card(review, part: 1, taskPages: 1);

        Assert.That(RoomReview.Parts(review, 1), Is.EqualTo(2), "nothing to check, so no part for it");
        Assert.That(task.Title!.Text, Is.EqualTo("Kite Run"));
        Assert.That(task.Head, Is.EqualTo(RoomCardHead.Project), "the project reads as the same thing on every card");
        Assert.That(task.Description!.Text, Is.EqualTo("Make a kite that follows my hand and climbs when I raise it."));
        Assert.That(task.DescriptionPage, Is.EqualTo(0), "the task's part pages, so its view measures how many pages it makes");
        Assert.That(task.More!.Text, Is.EqualTo("Part 1 of 2"));
        Assert.That(task.Slots, Is.Empty);

        Assert.That(runs.Title!.Text, Is.EqualTo("Kite Run"));
        Assert.That(runs.Description, Is.Null);
        Assert.That(runs.Slots.Select(slot => (slot.Kind, slot.Value.Text)), Is.EqualTo(new[]
        {
            (RoomKind.Folder, "kite-run"), (RoomKind.Agent, "glimmer-30b-q4"), (RoomKind.Level, AgentColumn.LevelWords(true)),
        }), "the model by the exact id the start sends");
        Assert.That(runs.Slots.Select(slot => slot.Mono), Is.EqualTo(new[] { true, true, false }), "names to type in mono");
        Assert.That(runs.List.Select(row => (row.Kind, row.Name.Text)), Is.EqualTo(new[]
        {
            (RoomKind.Task, "Make a kite"), (RoomKind.App, "Agent Studio"), (RoomKind.Computer, "On This Computer"),
        }));
        Assert.That(runs.List.All(row => row.Note == null), Is.True, "a row's mark says what it is; no note names a field");
        Assert.That(runs.More!.Text, Is.EqualTo("Part 2 of 2"));
        Assert.That(runs.Status, Is.Null);
        Assert.That(task.AnyDot || runs.AnyDot, Is.False);

        // Every value the start sends is on one of its parts; the model's catalog name stands for its id only where no id is named.
        var words = task.Words().Concat(runs.Words()).ToList();
        foreach (var item in review.Items.Where(each => !each.ValueRuns.IsEmpty && each.Kind != ReviewItemKind.Model))
            Assert.That(words, Does.Contain(item.ValueRuns.Text), item.Label);
        var unnamed = new NewWorkReview("Kite Run", "Make a kite", "Agent Studio", "Glimmer 30B", "", "", "Make a kite.");
        Assert.That(RoomReview.Card(unnamed, part: 1).Slots.Single(slot => slot.Kind == RoomKind.Agent).Value.Text, Is.EqualTo("Glimmer 30B"));
    }

    [Test]
    public void AMoveShowsBothFoldersAndANoteToCheckIsTheLastPartBeforeYes()
    {
        var review = new NewWorkReview("Kite Run", "Make a kite", "Agent Studio", "", "", "", "Fetch https://example.com/wind and run npm install.",
            folder: "kite-run-2", folderBefore: "kite-run");
        var warnings = review.Items.Where(each => each.Kind == ReviewItemKind.Warning).Select(each => each.Label.Trim()).ToList();
        Assert.That(warnings, Is.Not.Empty);
        Assert.That(RoomReview.Parts(review, 2), Is.EqualTo(4));

        var second = RoomReview.Card(review, part: 1, taskPages: 2);
        Assert.That((second.DescriptionPage, second.More!.Text, second.Status), Is.EqualTo(((int?)1, "Part 2 of 4", (RoomCardStatus?)null)), "a page of the task, no warning yet");

        var runs = RoomReview.Card(review, part: 2, taskPages: 2);
        Assert.That(runs.Slots.Select(slot => (slot.Kind, slot.Value.Text)).Take(2), Is.EqualTo(new[] { (RoomKind.Folder, "kite-run-2"), (RoomKind.Agent, "Agent Studio") }),
            "with no model named, the agent app takes the agent's slot");
        Assert.That(runs.Slots.Select(slot => slot.Mono).Take(2), Is.EqualTo(new[] { true, false }), "a folder's name is set in mono, an agent app's name is not");
        Assert.That(runs.List.Single(row => row.Kind == RoomKind.Folder).Name.Text, Is.EqualTo(RoomReview.MovesFrom + "kite-run"));
        Assert.That(runs.List.Any(row => row.Kind == RoomKind.App), Is.False, "the agent app is a slot already");

        var check = RoomReview.Card(review, part: 3, taskPages: 2);
        Assert.That(check.Title!.Text, Is.EqualTo("Kite Run"));
        Assert.That((check.Status!.Value.Title, check.Status!.Value.Tone, check.Status!.Value.Mark), Is.EqualTo((RoomReview.CheckTitle, RoomTone.Attention, RoomKind.Failed)));
        Assert.That(check.Status!.Value.Line, Is.EqualTo(RoomReview.CheckBothLine), "an address and something to run, in one short line");
        Assert.That(check.Description!.Text, Is.EqualTo("Fetch https://example.com/wind and run npm install."), "what to check is in view, the person's own sentence");
        var onlyAddress = new NewWorkReview("Kite Run", "Make a kite", "Agent Studio", "", "", "", "Make a kite. Use the wind from https://example.com/wind to pull it. Make it red.");
        var addressPart = RoomReview.Card(onlyAddress, part: 2, taskPages: 1);
        Assert.That(addressPart.Status!.Value.Line, Is.EqualTo(RoomReview.CheckAddressLine));
        Assert.That(addressPart.Description!.Text, Is.EqualTo("Use the wind from https://example.com/wind to pull it."), "only the sentence that holds it");
        Assert.That(check.More!.Text, Is.EqualTo("Part 4 of 4"));
        Assert.That(RoomReview.Card(review, part: 9, taskPages: 2).More!.Text, Is.EqualTo("Part 4 of 4"), "past the last part is the last");
        Assert.That(check.AnyDot, Is.False);
    }

    [Test]
    public void APageBeforeTheFirstIsNoneAndAGapMaySayWhatItSkips()
    {
        Assert.That(new RoomCard(descriptionPage: -1).DescriptionPage, Is.Null);
        Assert.That(new RoomCard(descriptionPage: 0).DescriptionPage, Is.EqualTo(0));
        Assert.That(RoomReadingRow.Gap(23).Number, Is.EqualTo(23));
        Assert.That(RoomReadingRow.GapRow.Number, Is.Null);
        Assert.That(RoomReadingRow.Gap(0).Number, Is.Null);
        Assert.That((RoomReading.Skipped(1), RoomReading.Skipped(23)), Is.EqualTo(("1 Line", "23 Lines")));
    }
}

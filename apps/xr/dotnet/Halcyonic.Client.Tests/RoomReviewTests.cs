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
        Assert.That((second.DescriptionPage, second.More!.Text), Is.EqualTo(((int?)1, "Part 2 of 4")), "a page of the task");
        // Confirm stands on every part (R1: paging is never required), so the warning does too: none is confirmed unseen.
        Assert.That(second.Status!.Value.Title, Is.EqualTo(RoomReview.CheckTitle), "the warning on a page of the task too");

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
        Assert.That(check.Description!.Text, Is.EqualTo("Fetch https://example.com/wind and run npm install."), "what it runs is in view, the person's own sentence");
        var onlyAddress = new NewWorkReview("Kite Run", "Make a kite", "Agent Studio", "", "", "", "Make a kite. Use the wind from https://example.com/wind to pull it. Make it red.");
        var addressPart = RoomReview.Card(onlyAddress, part: 2, taskPages: 1);
        Assert.That(addressPart.Status!.Value.Line, Is.EqualTo(RoomReview.CheckAddressLine));
        Assert.That(addressPart.Description, Is.Null, "an address alone stands in its slot, never twice");
        Assert.That(addressPart.Slots.Select(slot => (slot.Kind, slot.Value.Text, slot.Mono)), Is.EqualTo(new[] { (RoomKind.Link, "https://example.com/wind", true) }),
            "the address itself, whole, in mono, its sentence's end left off");
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

    [Test]
    public void AnyCommandsSheetReadsOnTheSameCardWithNothingKnowingWhichCommandItIs()
    {
        // A review with no words of its own (as sending work), two short facts, one longer one, and an address to check.
        var sheet = new ReviewSheet(LineWords.TheirName("Kite Run"), RoomCardHead.Project, facts: new[]
        {
            new ReviewFact(RoomKind.Folder, LineWords.TheirName("kite-run"), mono: true, slot: true),
            new ReviewFact(RoomKind.Link, LineWords.TheirName("main"), mono: true, slot: true),
            new ReviewFact(RoomKind.Task, LineWords.TheirName("Make the kite fly in the wind")),
        }, checks: new[] { new ReviewCheck(ReviewCheckKind.WebAddress, LineWords.TheirName("https://example.com/wind.json")) });
        Assert.That(RoomReview.Parts(sheet, 1), Is.EqualTo(2), "no words: the facts, then what to check");
        var facts = RoomReview.Card(sheet, part: 0);
        Assert.That(facts.Slots.Select(slot => (slot.Kind, slot.Value.Text)), Is.EqualTo(new[] { (RoomKind.Folder, "kite-run"), (RoomKind.Link, "main") }));
        Assert.That(facts.List.Single().Name.Text, Is.EqualTo("Make the kite fly in the wind"));
        Assert.That(facts.More!.Text, Is.EqualTo("Part 1 of 2"));
        Assert.That(facts.AnyDot, Is.False);
        var check = RoomReview.Card(sheet, part: 1);
        Assert.That(check.Status!.Value.Line, Is.EqualTo("It names a web address"));
        Assert.That(check.Slots.Single().Value.Text, Is.EqualTo("https://example.com/wind.json"));
        // A kind this client has no words for is said as unknown in plain words, never as another kind's name (R3).
        var other = new ReviewSheet(null, facts: new[] { new ReviewFact(RoomKind.Folder, LineWords.TheirName("kite-run")) }, checks: new[] { new ReviewCheck(ReviewCheckKind.Other) });
        Assert.That(RoomReview.Card(other, part: 1).Status!.Value.Line, Is.EqualTo("It names something to check"));
        // What happens in the host's words under the title; the items a page at a time after the facts; the host's own warning line.
        var items = Enumerable.Range(1, 7).Select(index => new RoomListRow(RoomKind.Changed, LineWords.TheirName("file" + index + ".js"), "Edited", mono: true)).ToArray();
        var send = new ReviewSheet(LineWords.TheirName("Kite Run"), RoomCardHead.Project, facts: new[] { new ReviewFact(RoomKind.Folder, LineWords.TheirName("kite-run"), mono: true, slot: true) },
            checks: new[] { new ReviewCheck(ReviewCheckKind.Other, line: "This cannot be undone") }, line: LineWords.Ours("Your files go to the project's repository as a new branch"), items: items);
        Assert.That(RoomReview.Parts(send, 1), Is.EqualTo(3), "seven items: five with the facts, two after, then what to check");
        var first = RoomReview.Card(send, part: 0);
        Assert.That((first.Description!.Text, first.List.Count, first.Status!.Value.Line), Is.EqualTo(("Your files go to the project's repository as a new branch", 5, "This cannot be undone")));
        Assert.That(RoomReview.Card(send, part: 1).List.Select(row => row.Name.Text), Is.EqualTo(new[] { "file6.js", "file7.js" }));
        var alone = new ReviewSheet(LineWords.TheirName("Kite Run"));
        Assert.That((RoomReview.Parts(alone, 1), RoomReview.Card(alone).More), Is.EqualTo((1, (LineWords?)null)), "one part says no part number");
    }
}

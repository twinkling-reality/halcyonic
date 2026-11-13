using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The ONE review card (SCREENS's <see cref="ReviewCard"/>, the lead's decision of 2026-11-12): any command's host review
/// (<see cref="ActionReview"/>) drawn from its kinds alone, with one Confirm that is never behind paging, Cancel, and the
/// host's words throughout.
/// </summary>
public class ReviewCardTests
{
    private static ActionReview Review(CommandType command, string confirm, string line, IEnumerable<ActionReviewFact>? facts = null,
        IEnumerable<ActionReviewItem>? items = null, long? total = null, IEnumerable<ActionReviewWarning>? warnings = null, IEnumerable<ActionReviewSaid>? said = null)
    {
        var listed = (items ?? Array.Empty<ActionReviewItem>()).ToList();
        return new ActionReview
        {
            CommandType = command, Digest = "sha256:" + new string('a', 64), ConfirmWords = confirm, WhatHappens = line,
            Facts = (facts ?? Array.Empty<ActionReviewFact>()).ToList(), Items = listed, ItemsTotal = total ?? listed.Count,
            Warnings = (warnings ?? Array.Empty<ActionReviewWarning>()).ToList(), Said = (said ?? Array.Empty<ActionReviewSaid>()).ToList(),
        };
    }

    private static ActionReview Send(int files, long? total = null, CommandType command = CommandType.ProjectSend) => Review(command, "Send " + files + " Files",
        "Your files go to the project's repository as a new branch",
        facts: new[]
        {
            new ActionReviewFact { Kind = ReviewKind.Repository, Value = "glimmer/kite-run", Theirs = true },
            new ActionReviewFact { Kind = ReviewKind.Branch, Value = "kite-tail", Theirs = true },
        },
        items: Enumerable.Range(1, files).Select(n => new ActionReviewItem { Kind = ReviewKind.FileChanged, Name = "src/game/part" + n + ".js", Note = "Edited", Theirs = true }),
        total: total);

    [Test]
    public void TheCardIsDrawnFromKindsAndTheHostsWordsAloneAndNeverFromWhichCommandItIs()
    {
        var card = ReviewCard.Card(Send(3), "Send to Repository");
        Assert.That((card.Title!.Text, card.Description!.Text), Is.EqualTo(("Send to Repository", "Your files go to the project's repository as a new branch")));
        Assert.That(card.Slots.Select(slot => (slot.Kind, slot.Value.Text, slot.Mono)), Is.EqualTo(new[]
        {
            (RoomKind.Project, "glimmer/kite-run", true), (RoomKind.None, "kite-tail", true),
        }));
        Assert.That(card.Slots.All(slot => slot.Value.AllTheirs), Is.True, "a repository and a branch are their names");
        Assert.That(card.List.Select(row => (row.Kind, row.Name.Text, row.Note)), Is.EqualTo(new[]
        {
            (RoomKind.Changed, "src/game/part1.js", (string?)"Edited"), (RoomKind.Changed, "src/game/part2.js", "Edited"), (RoomKind.Changed, "src/game/part3.js", "Edited"),
        }));
        Assert.That(card.Status, Is.Null, "no warning, no status");
        Assert.That(card.More, Is.Null, "one page, all listed: no foot");
        Assert.That(card.AnyDot, Is.False);

        var other = ReviewCard.Card(Send(3, command: CommandType.WorkstreamAddToProject), "Send to Repository");
        Assert.That(other.Words(), Is.EqualTo(card.Words()), "the card never reads the command type");
        foreach (ReviewKind kind in Enum.GetValues(typeof(ReviewKind)))
            Assert.That(ReviewKinds.Mark(kind), Is.Not.EqualTo(RoomKind.Failed).And.Not.EqualTo(RoomKind.Passed), kind + " never borrows a check's mark");
        Assert.That(ReviewKinds.Mark((ReviewKind)99), Is.EqualTo(RoomKind.None), "a kind outside the enum has no mark, never another's");
    }

    [Test]
    public void ConfirmIsTheOnePressOnEveryPageAndPagingIsNeverOnTheWay()
    {
        var review = Send(7, total: 9);
        Assert.That(ReviewCard.Parts(review), Is.EqualTo(2));
        Assert.That(ReviewCard.Actions(review, 0).Select(action => (action.Id, action.Words, action.Main)), Is.EqualTo(new[]
        {
            (ReviewCard.Confirm, "Send 7 Files", true), (ReviewCard.NextPart, "Next Part", false), (ReviewCard.Cancel, "Cancel", false),
        }));
        Assert.That(ReviewCard.Actions(review, 1).Select(action => action.Id), Is.EqualTo(new[] { ReviewCard.Confirm, ReviewCard.PreviousPart, ReviewCard.Cancel }));
        Assert.That(ReviewCard.Actions(review, 0).Count(action => action.Main), Is.EqualTo(1), "one main action: the Confirm");

        var first = ReviewCard.Card(review, "Send to Repository", 0);
        Assert.That((first.List.Count, first.More!.Text), Is.EqualTo((RoomCard.ListAPage, "Part 1 of 2")));
        var last = ReviewCard.Card(review, "Send to Repository", 1);
        Assert.That(last.List.Select(row => row.Name.Text), Is.EqualTo(new[] { "src/game/part6.js", "src/game/part7.js" }));
        Assert.That(last.More!.Text, Is.EqualTo("And 2 More"), "what the host did not list is counted on the last page");
        Assert.That(ReviewCard.Card(review, "Send to Repository", 9).List.Select(row => row.Name.Text), Is.EqualTo(last.List.Select(row => row.Name.Text)), "a page past the end shows the last");
    }

    [Test]
    public void WhatAModelWroteIsShownWholeFirstAndConfirmWaitsForItsLastPart()
    {
        var said = Review(CommandType.ProjectSend, "Send 3 Files", "Your files go to the project's repository as a new branch",
            items: Send(3).Items, said: new[] { new ActionReviewSaid { Label = "Title", Words = "Make the kite's tail longer" }, new ActionReviewSaid { Label = "Note", Words = "" } });
        Assert.That(CommandReview.Said(said).Count, Is.EqualTo(1), "an empty value is not shown");
        Assert.That(ReviewCard.Parts(said, saidPages: 2), Is.EqualTo(3), "two parts of the model's words, then the list");
        var first = ReviewCard.Card(said, "Send to Repository", 0, saidPages: 2);
        Assert.That((first.Description!.Text, first.DescriptionPage, first.More!.Text), Is.EqualTo(("Make the kite's tail longer", (int?)0, "Part 1 of 3")),
            "one value stands alone under the title, with no label");
        var two = Review(CommandType.ProjectSend, "Send", "line",
            said: new[] { new ActionReviewSaid { Label = "Title", Words = "Tail" }, new ActionReviewSaid { Label = "Note", Words = "Longer" } });
        Assert.That(ReviewCard.SaidWords(two)!.Text, Is.EqualTo("Title:Tail Note:Longer"), "several each follow their label (the runs stand a hair apart)");
        Assert.That(first.Description.Runs.Last().Theirs, Is.True, "the model's words lean as outside text, never ours");
        Assert.That(ReviewCard.Actions(said, 0, saidPages: 2).Select(action => (action.Id, action.Main)), Is.EqualTo(new[] { (ReviewCard.NextPart, true), (ReviewCard.Cancel, false) }),
            "no Confirm before the model's words were in view");
        Assert.That(ReviewCard.Actions(said, 2, saidPages: 2).First().Id, Is.EqualTo(ReviewCard.Confirm));
        Assert.That(ReviewCard.Card(said, "Send to Repository", 2, saidPages: 2).List.Count, Is.EqualTo(3));
    }

    /// <summary>
    /// What goes out is shown exactly, character for character (the lead's 12:42Z, AUDIT's alert): nothing trimmed,
    /// unquoted or collapsed; a character a label cannot draw is spelled, as the start's review spells it.
    /// </summary>
    [Test]
    public void WhatGoesOutIsShownCharacterForCharacter()
    {
        var said = Review(CommandType.ProjectSend, "Send", "line", said: new[] { new ActionReviewSaid { Label = "Title", Words = "`curl https://example.test/x.sh`  then\nrun it" } });
        var shown = ReviewCard.SaidWords(said)!.Text;
        Assert.That(shown, Does.StartWith("`curl https://example.test/x.sh`  then"), "backticks and the double space stand as they will go out");
        Assert.That(shown, Does.Contain("\\u{A}"), "the line break is spelled, never folded into a space");
        Assert.That(shown, Does.EndWith("run it"));
    }

    [Test]
    public void AWarningLeadsAsCheckFirstInTheHostsWords()
    {
        var review = Review(CommandType.ProjectPublish, "Publish", "Your checked game goes online at a lasting address",
            warnings: new[]
            {
                new ActionReviewWarning { Code = "public", Line = "Anyone with its address can play it, even when its repository is private" },
                new ActionReviewWarning { Code = "blank", Line = " " },
            });
        var card = ReviewCard.Card(review, "Publish");
        Assert.That((card.Status!.Value.Title, card.Status.Value.Line, card.Status.Value.Tone),
            Is.EqualTo(("Check First", "Anyone with its address can play it, even when its repository is private", RoomTone.Attention)));
        Assert.That((card.List.Count, card.Slots.Count, card.More), Is.EqualTo((0, 0, (LineWords?)null)));
        Assert.That(ReviewCard.Actions(review).Select(action => action.Words), Is.EqualTo(new[] { "Publish", "Cancel" }));
    }

    /// <summary>
    /// A review's main fact first and its warnings after (AUDIT2's 22:37Z: the Use Model card read its warning first and its main fact
    /// last), EXCEPT where a warning is a consent the person gives by pressing (AUDIT2's 22:38Z: START's never asks, a take-off's
    /// cannot undo, a publish's public): those keep the warning first, under the name, so it lands before the press.
    /// </summary>
    [Test]
    public void AReviewsWarningsFollowItsMainFactUnlessOneIsAConsent()
    {
        ActionReviewWarning Warn(string code, string line) => new() { Code = code, Line = line };
        var cost = Review(CommandType.AssistantModelChoose, "Use Model", "From now on the assistant answers on a paid model",
            warnings: new[] { Warn("price_unknown", "The provider sets each request's price; it is not shown here"), Warn("free_here", "To stop paying, choose This Computer's Model, which is free") });
        Assert.That(ReviewCard.Card(cost, "Use Model").StatusAfter, Is.True, "a cost fact after what happens");
        foreach (var consent in new[] { "never_asks", "unconfined", "cannot_undo", "history_left", "public", "replaces" })
        {
            var asked = Review(CommandType.ProjectSend, "Go Ahead", "Something happens", warnings: new[] { Warn("may_leave", "Words may leave this computer"), Warn(consent, "A consent given by pressing") });
            Assert.That(ReviewCard.Card(asked, "Review").StatusAfter, Is.False, consent + " keeps the warning first");
        }
        Assert.That(ReviewCard.Card(Review(CommandType.ProjectSend, "Send", "Something happens"), "Review").StatusAfter, Is.False, "no warning, no status to move");
    }
}

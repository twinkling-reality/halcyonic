#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>Add to Project as the headset says it (ADR 0045; the addendum's A13 in A17's language): waiting, the review, added, changed in both, refused.</summary>
public sealed class RoomAddTests
{
    private static OwnCopy Copy(PathList? toAdd = null, string? lastAdded = null, ConflictedFiles? conflict = null) => new()
    {
        Directory = "/copies/w1", MadeAt = "2026-11-11T03:00:00.000Z", BaseTree = new string('a', 64), ToAdd = toAdd, LastAddedAt = lastAdded, Conflict = conflict,
    };

    private static PathList Paths(params string[] paths) => new() { Paths = paths.ToList(), Total = paths.Length };

    [Test]
    public void Work_waiting_is_a_slot_of_its_count_and_the_review_names_the_task_and_each_file_as_theirs()
    {
        var add = RoomAdd.Of(Copy(Paths("src/kite.js", "src/tail.js", "src/score.js", "assets/tail.png")), working: false);
        Assert.That((add.State, add.Count), Is.EqualTo((RoomAddState.Ready, 4L)));
        Assert.That((add.Slot!.Value.Kind, add.Slot.Value.Value.Text), Is.EqualTo((RoomKind.ToAdd, "4 Files")));
        var review = add.ReviewCard(LineWords.TheirName("Tail"), "Kite Run");
        Assert.That((review.Title!.Text, review.Description!.Text), Is.EqualTo(("Tail", "Into Kite Run. You can undo this.")));
        Assert.That(review.List.Select(row => row.Name.Text), Is.EqualTo(new[] { "src/kite.js", "src/tail.js", "src/score.js", "assets/tail.png" }));
        Assert.That(review.List.All(row => row.Name.Runs.All(run => run.Theirs) && row.Mono), "a file's name is an agent's words, never ours");
        Assert.That(review.AnyDot, Is.False, "no fact joined on a dot");
        Assert.That((add.ConfirmWords, add.SendingWords), Is.EqualTo(("Add to Project", "Adding 4 Files")));
        Assert.That(review.Title.Text, Is.Not.EqualTo(add.ConfirmWords), "the title names the thing, the button the act");
        Assert.That(RoomAdd.Of(Copy(Paths("src/kite.js")), false).Slot!.Value.Value.Text, Is.EqualTo("1 File"));
    }

    [Test]
    public void The_count_is_the_whole_count_even_where_the_list_holds_fewer()
    {
        var waiting = new PathList { Paths = new List<string> { "a.js", "b.js" }, Total = 140 };
        var add = RoomAdd.Of(Copy(waiting), false);
        Assert.That((add.Count, add.Slot!.Value.Value.Text), Is.EqualTo((140L, "140 Files")));
        var review = add.ReviewCard(LineWords.TheirName("Tail"), null);
        Assert.That((review.List.Count, review.More!.Text, review.Description!.Text), Is.EqualTo((2, "And 138 Files More", "You can undo this.")));
    }

    [Test]
    public void Added_with_nothing_newer_says_so_and_a_newer_wait_wins()
    {
        var added = RoomAdd.Of(Copy(lastAdded: "2026-11-11T03:10:00.000Z"), false);
        Assert.That((added.AddedCard!.Status!.Value.Title, added.AddedCard.Status.Value.Tone), Is.EqualTo(("Added to Project", RoomTone.Good)));
        Assert.That(added.Slot, Is.Null);
        Assert.That(RoomAdd.Of(Copy(Paths("x.js"), lastAdded: "2026-11-11T03:10:00.000Z"), false).State, Is.EqualTo(RoomAddState.Ready));
    }

    [Test]
    public void Files_changed_in_both_are_a_status_over_their_list_their_kind_the_quiet_note()
    {
        var both = new ConflictedFiles
        {
            Files = new List<ConflictedFile>
            {
                new() { Path = "src/kite.js", Kind = ConflictKind.Text }, new() { Path = "assets/kite.png", Kind = ConflictKind.Binary },
                new() { Path = "notes/plan.md", Kind = ConflictKind.Deleted }, new() { Path = "lib/x", Kind = ConflictKind.Other },
            },
            Total = 4,
        };
        var add = RoomAdd.Of(Copy(Paths("src/kite.js"), conflict: both), false);
        var card = add.ConflictCard!;
        Assert.That((add.State, card.Status!.Value.Title, card.Status.Value.Line, card.Status.Value.Tone),
            Is.EqualTo((RoomAddState.Conflict, "Not Added", "The project changed 4 files too", RoomTone.Bad)));
        Assert.That(card.List.Select(row => (row.Name.Text, row.Note, row.Kind)), Is.EqualTo(new[]
        {
            ("src/kite.js", (string?)"Edited in Both", RoomKind.Conflict), ("assets/kite.png", "Picture or Data File", RoomKind.Conflict),
            ("notes/plan.md", "Removed in Project", RoomKind.Removed), ("lib/x", null, RoomKind.Conflict),
        }));
        Assert.That(card.List.All(row => row.Name.Runs.All(run => run.Theirs)), "the names theirs; the notes are ours, apart");
        Assert.That(card.AnyDot, Is.False);
        Assert.That(RoomAdd.Of(Copy(conflict: new ConflictedFiles { Files = new List<ConflictedFile> { new() { Path = "a", Kind = ConflictKind.Text } }, Total = 1 }), false)
            .ConflictCard!.Status!.Value.Line, Is.EqualTo("The project changed this file too"));
    }

    [Test]
    public void Nothing_is_said_while_the_agent_works_or_without_a_copy_of_its_own()
    {
        Assert.That(RoomAdd.Of(Copy(Paths("x.js")), working: true).State, Is.EqualTo(RoomAddState.None));
        Assert.That(RoomAdd.Of(null, false).State, Is.EqualTo(RoomAddState.None));
        Assert.That(RoomAdd.Of(new RefusedCopy { Code = CopyRefusalCode.NestedRepository, RefusedAt = "2026-11-11T03:00:00.000Z" }, false).State, Is.EqualTo(RoomAddState.None));
        var nothing = RoomAdd.Of(Copy(), false);
        Assert.That((nothing.Slot, nothing.AddedCard, nothing.ConflictCard), Is.EqualTo(((RoomSlot?)null, (RoomCard?)null, (RoomCard?)null)), "a copy with nothing to add says nothing");
    }

    [Test]
    public void A_refused_add_says_why_as_a_status_in_the_products_words()
    {
        Assert.That(RoomAdd.RefusedCard("nothing_to_add")!.Status!.Value.Line, Is.EqualTo("Nothing new to add"));
        Assert.That((RoomAdd.RefusedCard("folder_changed")!.Status!.Value.Title, RoomAdd.RefusedCard("folder_changed")!.Status!.Value.Line), Is.EqualTo(("Not Added", "The project just changed")));
        Assert.That(RoomAdd.RefusedCard("changed_in_both"), Is.Null, "the conflict is the copy's own state");
        Assert.That(RoomAdd.RefusedCard(null), Is.Null);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// A place per project, every agent standing (the lead, 2026-11-17T18:56Z): no project shares a place, no task is paged away, the one
/// that needs the person stands first, and a crowd's "{n} More" never counts one that needs the person (AUDIT2, 18:57Z).
/// </summary>
public class RoomPlacesTests
{
    private static WorkstreamView Work(string id, string project, int minute, bool needsYou = false) => new()
    {
        WorkstreamId = id, ProjectId = project, Title = id,
        CreatedAt = "2026-11-17T10:" + minute.ToString("00") + ":00Z", UpdatedAt = "2026-11-17T10:" + minute.ToString("00") + ":00Z",
        Status = needsYou ? WorkstreamStatus.WaitingForHuman : WorkstreamStatus.Running,
        Attention = new Attention { Level = needsYou ? AttentionLevel.ActionRequired : AttentionLevel.None },
    };

    private static readonly Dictionary<string, string> Names = new() { ["A"] = "Kite Run", ["B"] = "Star Catcher", ["C"] = "Tide Pool" };

    [Test]
    public void EveryTaskOfEveryProjectStandsInItsOwnProjectsPlace()
    {
        var work = new[] { Work("a1", "A", 1), Work("b1", "B", 2), Work("a2", "A", 3), Work("c1", "C", 4), Work("b2", "B", 5), Work("b3", "B", 6) };
        var places = new RoomPlaces().Update("j", work, id => Names[id]);
        Assert.That(places.Select(place => place.ProjectId), Is.EqualTo(new[] { "A", "B", "C" }), "one place per project, the oldest work first");
        Assert.That(places.Select(place => place.Name), Is.EqualTo(new[] { "Kite Run", "Star Catcher", "Tide Pool" }));
        Assert.That(places.SelectMany(place => place.Shown).OrderBy(id => id), Is.EqualTo(work.Select(each => each.WorkstreamId).OrderBy(id => id)),
            "every task stands, in exactly one place: nothing is paged away");
        foreach (var place in places) Assert.That(place.Tasks.All(id => work.Single(each => each.WorkstreamId == id).ProjectId == place.ProjectId), Is.True);
        Assert.That(places.All(place => place.More == 0), Is.True);
    }

    [Test]
    public void TheOneThatNeedsYouStandsFirstInItsPlace()
    {
        var places = new RoomPlaces().Update("j", new[] { Work("b1", "B", 9), Work("b2", "B", 1, needsYou: true), Work("b3", "B", 5) }, id => Names[id]);
        Assert.That(places.Single().Tasks.First(), Is.EqualTo("b2"));
        Assert.That(places.Single().NeedsYou, Is.EqualTo(1));
    }

    [Test]
    public void APlaceKeepsItsOrderAsOthersComeAndGo()
    {
        var room = new RoomPlaces();
        room.Update("j", new[] { Work("a1", "A", 1), Work("b1", "B", 2) }, id => Names[id]);
        var later = room.Update("j", new[] { Work("c1", "C", 0), Work("b1", "B", 2), Work("a1", "A", 1) }, id => Names[id]);
        Assert.That(later.Select(place => place.ProjectId), Is.EqualTo(new[] { "A", "B", "C" }), "a new project stands after the rest, though its work is older");
        var gone = room.Update("j", new[] { Work("c1", "C", 0), Work("b1", "B", 2) }, id => Names[id]);
        Assert.That(gone.Select(place => place.ProjectId), Is.EqualTo(new[] { "B", "C" }), "a project with no work stands no place, and the rest keep their order");
        var other = room.Update("k", new[] { Work("c1", "C", 0), Work("b1", "B", 2) }, id => Names[id]);
        Assert.That(other.Select(place => place.ProjectId), Is.EqualTo(new[] { "C", "B" }), "another journal starts afresh, the oldest work first");
    }

    [Test]
    public void APlaceTakesItsFormByCountAndACrowdNeverHidesOneThatNeedsYou()
    {
        var rule = new PlaceRule(3, 6, 10, 6);
        Assert.That(new[] { 1, 3, 4, 6, 7, 10, 11, 16 }.Select(rule.FormOf),
            Is.EqualTo(new[] { PlaceForm.Full, PlaceForm.Full, PlaceForm.TwoRows, PlaceForm.TwoRows, PlaceForm.Small, PlaceForm.Small, PlaceForm.Crowd, PlaceForm.Crowd }));
        var room = new RoomPlaces { Rule = rule };
        var many = Enumerable.Range(1, 16).Select(minute => Work("b" + minute, "B", minute, needsYou: minute is 2 or 4)).ToList();
        var place = room.Update("j", many, id => Names[id]).Single();
        Assert.That(place.Form, Is.EqualTo(PlaceForm.Crowd));
        Assert.That((place.Standing, place.More), Is.EqualTo((6, 10)), "{n} More counts exactly the characters not shown");
        Assert.That(place.Shown.Take(2), Is.EquivalentTo(new[] { "b2", "b4" }), "both that need the person stand in front");
        Assert.That(place.Tasks.Skip(place.Standing), Has.None.AnyOf("b2", "b4"), "never one that needs you inside the n");
        room.Spread.Add("B");
        var spread = room.Update("j", many, id => Names[id]).Single();
        Assert.That((spread.Standing, spread.More), Is.EqualTo((16, 0)), "spread open, every one stands");
        var calling = Enumerable.Range(1, 12).Select(minute => Work("c" + minute, "C", minute, needsYou: minute <= 8)).ToList();
        var full = new RoomPlaces { Rule = rule }.Update("j", calling, id => Names[id]).Single();
        Assert.That((full.Standing, full.More), Is.EqualTo((8, 4)), "a crowd stands every one that needs you, past its front of six");
    }

    /// <summary>AUDIT3's PL1: the task whose bar is open stands though it falls past a crowd's front, and the front grows by it alone.</summary>
    [Test]
    public void TheSelectedTaskStandsPastACrowdsFront()
    {
        var room = new RoomPlaces { Rule = new PlaceRule(3, 6, 10, 6) };
        var crowd = Enumerable.Range(1, 12).Select(minute => Work("t" + minute, "B", minute)).ToList();
        var unselected = room.Update("j", crowd, id => Names[id]).Single();
        var eleventh = unselected.Tasks[10];
        Assert.That(unselected.Shown, Has.No.Member(eleventh), "past the front of six, unselected");
        var place = room.Update("j", crowd, id => Names[id], selectedId: eleventh).Single();
        Assert.That(place.Shown, Has.Member(eleventh), "the selected task stands");
        Assert.That((place.Standing, place.More), Is.EqualTo((7, 5)));
        Assert.That(place.Tasks.OrderBy(id => id), Is.EqualTo(crowd.Select(each => each.WorkstreamId).OrderBy(id => id)), "every task still in its place, once");
    }

    [Test]
    public void APlaceRuleRisesFromOneRowToTheCrowd()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaceRule(4, 3, 10, 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaceRule(0, 3, 10, 6));
    }
}

using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// A place's own words (AUDIT2, 2026-11-17T18:57Z): its project's name once, theirs; "{n} More" only where n is at least 1, n exactly
/// the characters not shown and never one that needs the person; "Show Fewer" once spread; no press where nothing is gathered.
/// </summary>
public class RoomPlaceSignTests
{
    private static WorkstreamView Work(string id, int minute, bool needsYou = false) => new()
    {
        WorkstreamId = id, ProjectId = "A", Title = id,
        CreatedAt = "2026-11-17T10:" + minute.ToString("00") + ":00Z", UpdatedAt = "2026-11-17T10:" + minute.ToString("00") + ":00Z",
        Status = needsYou ? WorkstreamStatus.WaitingForHuman : WorkstreamStatus.Running,
        Attention = new Attention { Level = needsYou ? AttentionLevel.ActionRequired : AttentionLevel.None },
    };

    private static IReadOnlyList<WorkstreamView> Tasks(int count, params int[] needingYou) =>
        Enumerable.Range(0, count).Select(index => Work("t" + index, index, needingYou.Contains(index))).ToList();

    [Test]
    public void ThePlaceIsNamedOnceByItsProjectsOwnName()
    {
        var places = new RoomPlaces();
        var place = places.Update("j", Tasks(2), _ => "kite run (Beta)").Single();
        var name = RoomPlaceSign.NameOf(place)!;
        Assert.That((name.Text, name.AllTheirs), Is.EqualTo(("kite run (Beta)", true)), "the person's own words, in their own case");
        Assert.That(RoomPlaceSign.NameOf(places.Update("j", Tasks(2), _ => null).Single()), Is.Null, "no name the host knows, none drawn");
    }

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(6)]
    [TestCase(10)]
    public void NoPressWhereThePlaceStandsEveryCharacter(int count)
    {
        var places = new RoomPlaces();
        var place = places.Update("j", Tasks(count), _ => "Kite Run").Single();
        Assert.That(place.Form, Is.Not.EqualTo(PlaceForm.Crowd));
        Assert.That((place.More, RoomPlaceSign.PressOf(place, places)), Is.EqualTo((0, ((string, string)?)null)));
    }

    [TestCase(11, new int[0])]
    [TestCase(16, new[] { 3, 12 })]
    [TestCase(16, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 })]
    public void MoreCountsExactlyTheCharactersNotShownAndNeverOneThatNeedsYou(int count, int[] needingYou)
    {
        var places = new RoomPlaces();
        var tasks = Tasks(count, needingYou);
        var place = places.Update("j", tasks, _ => "Kite Run").Single();
        var notShown = place.Tasks.Except(place.Shown).ToList();
        Assert.That(place.More, Is.EqualTo(notShown.Count), "n is exactly the characters not shown");
        Assert.That(notShown.Any(id => tasks.Single(each => each.WorkstreamId == id).Status == WorkstreamStatus.WaitingForHuman), Is.False,
            "one that needs the person is never inside the n");
        Assert.That(RoomPlaceSign.PressOf(place, places),
            Is.EqualTo(place.More >= 1 ? ((string, string)?)(place.More + " More", RoomPlaceSign.MoreAction) : null), "\"{n} More\" only where n is at least 1");
    }

    [Test]
    public void MoreSpreadsThePlaceAndShowFewerGathersItAgain()
    {
        var places = new RoomPlaces();
        var tasks = Tasks(14);
        var place = places.Update("j", tasks, _ => "Kite Run").Single();
        var (words, action) = RoomPlaceSign.PressOf(place, places)!.Value;
        Assert.That(words, Is.EqualTo(place.More + " More"));
        Assert.That(RoomPlaceSign.Act(places, action, "A"), Is.True);
        place = places.Update("j", tasks, _ => "Kite Run").Single();
        Assert.That((place.More, place.Shown.Count()), Is.EqualTo((0, 14)), "spread open, every character stands");
        Assert.That(RoomPlaceSign.PressOf(place, places), Is.EqualTo(((string, string)?)(RoomPlaceSign.FewerWords, RoomPlaceSign.FewerAction)));
        Assert.That(RoomPlaceSign.Act(places, RoomPlaceSign.FewerAction, "A"), Is.True);
        place = places.Update("j", tasks, _ => "Kite Run").Single();
        Assert.That(RoomPlaceSign.PressOf(place, places)?.Words, Is.EqualTo(place.More + " More"), "gathered again");
        Assert.That(RoomPlaceSign.Act(places, "something-else", "A"), Is.False);
    }

    [Test]
    public void MoreNeverReadsWithoutACount()
    {
        Assert.That(RoomPlaceSign.MoreWords(7), Is.EqualTo("7 More"));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => RoomPlaceSign.MoreWords(0));
    }
}

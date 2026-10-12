using System;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class AgentShelfPagesTests
{
    private static WorkstreamView Work(string id, string title, string updated) => new()
    { WorkstreamId = id, Title = title, UpdatedAt = updated, Status = WorkstreamStatus.Running, Attention = new Attention { Level = AttentionLevel.None } };

    [Test]
    public void ExplicitSelectionSurvivesReorderRankAndNewWork()
    {
        var pages = new AgentShelfPages();
        var a = Work("a", "First task", "2026-10-12T10:00:00Z");
        var b = Work("b", "Second task", "2026-10-12T09:00:00Z");
        pages.Update("journal", new[] { b, a });
        Assert.That((pages.ShownId, pages.Info.Summary), Is.EqualTo(("a", "Showing 1 of 2")));
        Assert.That(pages.Move(1), Is.True);
        Assert.That(pages.ShownId, Is.EqualTo("b"));
        b.Attention.Level = AttentionLevel.ActionRequired;
        pages.Update("journal", new[] { a, Work("c", "New task", "2026-10-12T11:00:00Z"), b });
        Assert.That((pages.ShownId, pages.Info.Title, pages.Info.Count), Is.EqualTo(("b", "Second task", 3)));
        Assert.That(pages.Info.Index, Is.EqualTo(0));
        Assert.That(pages.Move(-1), Is.False);
        Assert.That(pages.Select("absent"), Is.False);
        Assert.That(pages.Move(2), Is.False);
    }

    [Test]
    public void MissingSelectionAndChangedJournalRequireAnotherExplicitChoice()
    {
        var pages = new AgentShelfPages();
        var a = Work("a", "First", "2026-10-12T10:00:00Z"); var b = Work("b", "Second", "2026-10-12T09:00:00Z");
        pages.Update("one", new[] { a, b });
        pages.Update("one", new[] { b });
        Assert.That((pages.SelectedId, pages.ShownId, pages.Info.Index), Is.EqualTo(("a", (string?)null, -1)));
        Assert.That(pages.Info.Summary, Is.EqualTo("Choose an agent · 1 available"));
        Assert.That(pages.Move(1), Is.True);
        pages.Update("two", new[] { a, b });
        Assert.That(pages.ShownId, Is.Null, "another journal cannot retarget an old selection");
        Assert.That(pages.Move(-1), Is.True);
        Assert.That(pages.ShownId, Is.EqualTo("b"));
        pages.Update("two", Array.Empty<WorkstreamView>());
        Assert.That(pages.Info.Count, Is.Zero);
        Assert.That(pages.Move(1), Is.False);
    }

    [Test]
    public void ShelfRowsReadFreshPageCountsAndRaiseOnlyTheExplicitDirection()
    {
        var pages = new AgentShelfPages();
        pages.Update("one", new[] { Work("a", "First", "2026-10-12T10:00:00Z"), Work("b", "Second", "2026-10-12T09:00:00Z") });
        var raised = new System.Collections.Generic.List<string>();
        var rows = SpaceSettings.Of(() => new SpaceNow(null, RoomOffer.None, null, null, agentsEnabled: true, shelfPlaced: true, shelfPage: pages.Info), raised.Add, _ => { });
        var previous = rows.Single(row => row.Key == SpaceSettings.PreviousAgent); var next = rows.Single(row => row.Key == SpaceSettings.NextAgent);
        Assert.That(next.Read().Value, Is.EqualTo("Showing 1 of 2"));
        Assert.That(previous.Read().Reason, Is.Not.Null);
        Assert.That(next.Read().Reason, Is.Null);
        next.Change(); Assert.That(raised, Is.EqualTo(new[] { SpaceSettings.NextAgent }));
        pages.Move(1);
        Assert.That(previous.Read().Value, Is.EqualTo("Showing 2 of 2"));
        Assert.That(previous.Read().Reason, Is.Null);
        Assert.That(next.Read().Reason, Is.Not.Null);
        previous.Change(); Assert.That(raised.Last(), Is.EqualTo(SpaceSettings.PreviousAgent));
    }
    [TestCase("2")]
    [TestCase("<b>Run this</b>\u200B task")]
    [TestCase("The complete task title remains readable in current shelf details")]
    public void PageTaskIdentityUsesOutsideRunsIndependentlyOfTheCount(string title)
    {
        var rows = SpaceSettings.Of(() => new SpaceNow(null, RoomOffer.None, null, null,
            agentsEnabled: true, shelfPlaced: true, shelfPage: new AgentShelfPageInfo(0, 2, title)), _ => { }, _ => { });
        var now = rows.Single(row => row.Key == SpaceSettings.NextAgent).Read();
        Assert.That(now.ValueRuns.AnyTheirs, Is.False, "numeric count words belong to Halcyonic even when a title matches their suffix");
        Assert.That(now.NowRuns.AllTheirs, Is.True);
        Assert.That(now.NowRuns.Text, Is.EqualTo(LabelText.Name(title)));
        Assert.That(now.NowRuns.Runs.Single().Text.IndexOf('\u200B'), Is.EqualTo(-1), "the literal control code point must be spelled out");
        Assert.That(now.ValueRuns.Text, Is.EqualTo("Showing 1 of 2"));
    }

}

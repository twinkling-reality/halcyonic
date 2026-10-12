using System;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

[TestFixture]
public class PlacementColumnTests
{
    [Test]
    public void ChoosingDoesNotApplyAndApplyUsesTheActualSelectedArrangement()
    {
        var host = new FakeMenuHost { Height = MenuPage.Rows(5) };
        var current = StageArrangement.InFront;
        var changes = 0;
        var resets = 0;
        var column = new PlacementColumn(host, () => current, choice => { current = choice; changes++; }, () => resets++, () => false);
        var frame = column.Frame!;
        column.Act(PlacementColumn.Choose, nameof(StageArrangement.BesideAWindow));
        Assert.That(changes, Is.Zero, "undrawn controls have no authorization");
        column.Drawn(frame, null);
        column.Act(PlacementColumn.Choose, nameof(StageArrangement.BesideAWindow));
        Assert.That(changes, Is.Zero, "choosing only selects");
        var selected = column.Frame!;
        Assert.That(selected.Lines.Single(line => line.Chosen).Key, Is.EqualTo(nameof(StageArrangement.BesideAWindow)));
        Assert.That(selected.Footer.All.Count(), Is.EqualTo(3));
        column.Act(PlacementColumn.Apply, null);
        Assert.That(changes, Is.Zero, "the selected frame must draw");
        column.Drawn(frame, null);
        column.Act(PlacementColumn.Apply, null);
        Assert.That(changes, Is.Zero, "a stale draw cannot authorize Apply");
        column.Drawn(selected, null);
        column.Act(PlacementColumn.Apply, null);
        Assert.That((changes, current), Is.EqualTo((1, StageArrangement.BesideAWindow)));
        var applied = column.Frame!;
        column.Drawn(applied, null);
        column.Act(PlacementColumn.Reset, null);
        Assert.That(resets, Is.EqualTo(1));
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void WindowExplainsTheBoundaryAndOffersEveryActualPlacementAcrossPages()
    {
        var host = new FakeMenuHost { Height = MenuPage.Rows(2) };
        var column = new PlacementColumn(host, () => StageArrangement.BesideAWindow, _ => { }, () => { }, () => true);
        var seen = new System.Collections.Generic.List<string?>();
        for (var index = 0; index < 3; index++)
        {
            var frame = column.Frame!;
            Assert.That(frame.Focused, Is.True);
            Assert.That(frame.Lines[0].Words, Is.EqualTo(PlacementColumn.WindowInstruction));
            seen.AddRange(frame.Lines.Where(line => line.Action == PlacementColumn.Choose).Select(line => line.Key));
            column.Drawn(frame, null);
            column.Act(Footer.NextPage, null);
        }
        Assert.That(seen, Is.EquivalentTo(Enum.GetNames<StageArrangement>()));
    }

    [Test]
    public void PagingKeepsTheNamedCandidateUntilAnotherPlacementIsChosen()
    {
        var host = new FakeMenuHost { Height = MenuPage.Rows(2) };
        StageArrangement? applied = null;
        var column = new PlacementColumn(host, () => StageArrangement.InFront, choice => applied = choice, () => { }, () => false);
        var frame = column.Frame!;
        column.Drawn(frame, null);
        column.Act(PlacementColumn.Choose, nameof(StageArrangement.InFront));
        frame = column.Frame!;
        Assert.That(frame.Lines[0].Words, Is.EqualTo("Chosen: In front of you"));
        Assert.That(frame.Lines.Any(line => line.Action == Footer.NextPage && line.Control == LineControl.Action), Is.True);
        column.Drawn(frame, null);
        column.Act(Footer.NextPage, null);
        frame = column.Frame!;
        Assert.That(frame.Lines[0].Words, Is.EqualTo("Chosen: In front of you"));
        Assert.That(frame.Lines.Where(line => line.Action == PlacementColumn.Choose).All(line => line.Choice && line.AnswerMark == AnswerMark.Single), Is.True);
        column.Drawn(frame, null);
        column.Act(Footer.NextPage, null);
        frame = column.Frame!;
        column.Drawn(frame, null);
        column.Act(Footer.NextPage, null);
        frame = column.Frame!;
        Assert.That(frame.Lines.Single(line => line.Chosen).Key, Is.EqualTo(nameof(StageArrangement.InFront)));
        column.Drawn(frame, null);
        column.Act(PlacementColumn.Apply, null);
        Assert.That(applied, Is.EqualTo(StageArrangement.InFront));
    }

    [Test]
    public void DeskOffersResetAndFocusRevokesTheOldSelectionAndActionFrame()
    {
        var resets = 0;
        var host = new FakeMenuHost();
        var column = new PlacementColumn(host, () => null, _ => Assert.Fail("a desk cannot change arrangement"), () => resets++, () => false);
        var frame = column.Frame!;
        Assert.That(frame.Lines.Single().Words, Is.EqualTo(PlacementColumn.DeskInstruction));
        Assert.That(frame.Footer.All.Count(), Is.EqualTo(2));
        column.Drawn(frame, null);
        column.FocusLeft();
        column.Act(PlacementColumn.Reset, null);
        Assert.That(resets, Is.Zero);
        frame = column.Frame!;
        column.Drawn(frame, null);
        column.Act(PlacementColumn.Reset, null);
        Assert.That(resets, Is.EqualTo(1));
    }

    [Test]
    public void FullRetainedTitleHasAnExplicitPagedReadRouteAndBackPreservesPlacementPage()
    {
        var host = new FakeMenuHost { Height = MenuPage.Words(2) + MenuPage.Target() + MenuPage.TargetGap };
        var column = new PlacementColumn(host, () => StageArrangement.InFront, _ => { }, () => { }, () => false, "An outside task title");
        var frame = column.Frame!;
        Assert.That(frame.Lines.Any(line => line.Action == PlacementColumn.ReadTitle), Is.True);
        column.Drawn(frame, null);
        column.Act(PlacementColumn.ReadTitle, null);
        var title = column.Frame!;
        Assert.That(title.Subject, Is.EqualTo("Task title"));
        Assert.That(title.Lines.Single().Runs.AllTheirs, Is.True);
        column.Drawn(title, null);
        column.Act(PlacementColumn.Back, null);
        Assert.That(column.Frame!.Subject, Is.EqualTo(PlacementColumn.Subject));
    }
}

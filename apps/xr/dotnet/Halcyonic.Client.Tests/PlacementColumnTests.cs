using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

[TestFixture]
public class PlacementColumnTests
{
    [Test]
    public void ResetRequiresExactCurrentDrawAndQueuedControlsLoseAuthority()
    {
        var host = new FakeMenuHost(); var resets = 0;
        var column = new PlacementColumn(host, () => resets++);
        var original = column.Frame!;
        Assert.That(original.Lines.Single().Action, Is.EqualTo(PlacementColumn.Reset));
        column.Act(PlacementColumn.Reset, null); Assert.That(resets, Is.Zero);
        column.Drawn(original, null); column.FocusLeft();
        column.Act(PlacementColumn.Reset, null); Assert.That(resets, Is.Zero);
        var current = column.Frame!; column.Drawn(original, null);
        column.Act(PlacementColumn.Reset, null); Assert.That(resets, Is.Zero);
        column.Drawn(current, null); column.Act(PlacementColumn.Reset, null);
        Assert.That(resets, Is.EqualTo(1)); Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void FullTaskTitleRetainsItsExplicitReadRouteAndBackReturnsToWorkspace()
    {
        var host = new FakeMenuHost { Height = MenuPage.Words(2) + MenuPage.Target() + MenuPage.TargetGap };
        var column = new PlacementColumn(host, () => { }, "An outside task title");
        ActVisible(column, PlacementColumn.ReadTitle);
        var title = column.Frame!;
        Assert.That(title.Subject, Is.EqualTo("Task title")); Assert.That(title.Lines.Single().Runs.AllTheirs, Is.True);
        column.Drawn(title, null); column.Act(PlacementColumn.Back, null);
        Assert.That(column.Frame!.Subject, Is.EqualTo(PlacementColumn.Subject));
    }

    [Test]
    public void TaskPortraitChoiceIsLocalReversibleAndRequiresCurrentPage()
    {
        var host = new FakeMenuHost(); var visible = true; var writes = 0;
        var column = new PlacementColumn(host, () => { }, "A task", () => visible, value => { visible = value; writes++; });
        column.Act(PlacementColumn.TogglePortraits, null); Assert.That(writes, Is.Zero);
        ActVisible(column, PlacementColumn.TogglePortraits); Assert.That(visible, Is.False);
        ActVisible(column, PlacementColumn.TogglePortraits); Assert.That(visible, Is.True);
        Assert.That(writes, Is.EqualTo(2)); Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void EveryWorkspaceRoutePagesWithinBudgetAndHiddenActionsCannotAct()
    {
        var host = new FakeMenuHost { Height = MenuPage.Target() };
        var moving = false; var minimized = 0; var openedApps = 0; var canApps = false;
        var destinations = new List<MenuPlace>();
        var column = new PlacementColumn(host, () => { }, "Task", () => true, _ => { }, value => moving = value,
            () => minimized++, () => openedApps++, () => canApps, destinations.Add);
        var first = column.Frame!; column.Drawn(first, null);
        column.Act(PlacementColumn.Minimize, null); Assert.That(minimized, Is.Zero, "off-page action has no input authority");
        var seen = new HashSet<string>();
        for (var i = 0; i < 20; i++)
        {
            var frame = column.Frame!;
            Assert.That(MenuPage.Rows(frame.Lines.Count), Is.LessThanOrEqualTo(host.Height + .0001));
            foreach (var line in frame.Lines) seen.Add(line.Action + ":" + line.Key);
            foreach (var line in frame.Lines.Where(line => line.Action == PlacementColumn.OtherApps))
                Assert.That(line.Words, Is.EqualTo("Open work window"));
            column.Drawn(frame, null);
            if (frame.Footer[PromptSlot.FarRight]?.Words == "First Page") break;
            column.Act(Footer.NextPage, null);
        }
        Assert.That(seen, Does.Contain(PlacementColumn.BeginMove + ":"));
        Assert.That(seen, Does.Contain(PlacementColumn.Minimize + ":"));
        foreach (var place in Enum.GetValues<MenuPlace>()) Assert.That(seen, Does.Contain(PlacementColumn.Navigate + ":" + place));
        ActVisible(column, PlacementColumn.OtherApps); Assert.That(openedApps, Is.Zero);
        canApps = true; column.FocusLeft(); ActVisible(column, PlacementColumn.OtherApps); Assert.That(openedApps, Is.EqualTo(1));
        ActVisible(column, PlacementColumn.BeginMove); Assert.That(moving, Is.True); Assert.That(column.Moving, Is.True);
        var movingFrame = column.Frame!;
        Assert.That(movingFrame.Footer[PromptSlot.Close]!.Words, Is.EqualTo("Done moving"));
        column.Drawn(movingFrame, null); column.Act(PlacementColumn.OtherApps, null);
        Assert.That(openedApps, Is.EqualTo(1), "the explicit moving page cannot authorize an app switch");
        column.FocusLeft(); column.Act(PlacementColumn.Minimize, null);
        Assert.That(minimized, Is.Zero, "leaving the local controls revokes queued organization actions");
        Assert.That(host.Sent, Is.Empty);
    }

    private static void ActVisible(PlacementColumn column, string id)
    {
        for (var i = 0; i < 30; i++)
        {
            var frame = column.Frame!; column.Drawn(frame, null);
            var line = frame.Lines.FirstOrDefault(line => line.Action == id);
            if (line != null) { column.Act(id, line.Key); return; }
            column.Act(Footer.NextPage, null);
        }
        Assert.Fail("Local action was unreachable: " + id);
    }
}

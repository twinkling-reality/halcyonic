using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

[TestFixture]
public class PlacementColumnTests
{
    [Test]
    public void WorkspaceMovementOffersOnlyRealResetAndRequiresTheExactCurrentDraw()
    {
        var host = new FakeMenuHost();
        var resets = 0;
        var column = new PlacementColumn(host, () => resets++);
        var original = column.Frame!;
        Assert.That(original.Lines.Single().Words, Is.EqualTo(PlacementColumn.HoldInstruction));
        Assert.That(original.Footer.All.Select(each => each.Prompt.Id), Is.EquivalentTo(new[] { Footer.Close, PlacementColumn.Reset }));
        column.Act(PlacementColumn.Reset, null);
        Assert.That(resets, Is.Zero, "an undrawn control cannot move the workspace");
        column.Drawn(original, null);
        column.FocusLeft();
        column.Act(PlacementColumn.Reset, null);
        Assert.That(resets, Is.Zero, "focus revoked the earlier control");
        var current = column.Frame!;
        column.Drawn(original, null);
        column.Act(PlacementColumn.Reset, null);
        Assert.That(resets, Is.Zero, "a stale draw cannot grant Reset");
        column.Drawn(current, null);
        column.Act(PlacementColumn.Reset, null);
        Assert.That(resets, Is.EqualTo(1));
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void FullTaskTitleRetainsItsExplicitReadRouteAndBackReturnsToMovementHelp()
    {
        var host = new FakeMenuHost { Height = MenuPage.Words(2) + MenuPage.Target() + MenuPage.TargetGap };
        var column = new PlacementColumn(host, () => { }, "An outside task title");
        var movement = column.Frame!;
        Assert.That(movement.Lines.Any(line => line.Action == PlacementColumn.ReadTitle), Is.True);
        column.Drawn(movement, null);
        column.Act(PlacementColumn.ReadTitle, null);
        var title = column.Frame!;
        Assert.That(title.Subject, Is.EqualTo("Task title"));
        Assert.That(title.Lines.Single().Runs.AllTheirs, Is.True);
        column.Drawn(title, null);
        column.Act(PlacementColumn.Back, null);
        Assert.That(column.Frame!.Subject, Is.EqualTo(PlacementColumn.Subject));
    }
}

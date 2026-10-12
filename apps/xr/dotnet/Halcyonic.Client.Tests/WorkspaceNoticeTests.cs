using System.Collections.Generic;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class WorkspaceNoticeTests
{
    [Test]
    public void LongLocalExplanationKeepsEveryWrappedRowAndRequiresCurrentDrawAndPagerDwell()
    {
        var host = new FileMenuHost { Height = _ => MenuPage.Words(2) };
        var column = new WorkspaceNoticeColumn(host, new string('a', 241));
        var seen = new HashSet<int>();
        var closed = 0;
        column.Closed += () => closed++;
        column.Act(Footer.Close, null);
        Assert.That(closed, Is.Zero);
        for (var page = 0; page < 4; page++)
        {
            var frame = column.Frame!;
            var line = frame.Lines[0];
            for (var row = 0; row < line.Rows; row++) seen.Add(line.FromRow!.Value + row);
            column.Drawn(frame, null);
            column.Act(Footer.NextPage, null);
            Assert.That(column.Frame!.Lines[0].FromRow, Is.EqualTo(line.FromRow), "double presses cannot skip unseen parts");
            frame = column.Frame!;
            column.Drawn(frame, null);
            host.Wait(0.5);
            column.Act(Footer.NextPage, null);
        }
        Assert.That(seen, Is.EquivalentTo(new[] { 0, 1, 2, 3, 4, 5, 6 }));
        var current = column.Frame!;
        column.Drawn(current, null);
        column.FocusLeft();
        column.Act(Footer.Close, null);
        Assert.That(closed, Is.Zero);
        current = column.Frame!;
        column.Drawn(current, null);
        column.Act(Footer.Close, null);
        Assert.That(closed, Is.EqualTo(1));
        Assert.That(host.Sent, Is.Empty);
    }
}

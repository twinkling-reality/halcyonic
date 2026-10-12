using System;
using System.IO;
using System.Linq;
using Halcyonic.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class WorkspaceRecoveryColumnTests
{
    private static WorkspaceContinuation Record()
    {
        var fixture = JObject.Parse(File.ReadAllText(Repository.PathTo("fixtures/workspace/continuation.json")));
        WorkspaceContinuity.TryParse(fixture["cases"]![0]!["value"]!.ToString(Formatting.None), out var record);
        return record!;
    }
    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void EarlierDraftCanBeReadInFullButNeverRetargetedOrSent(TextSize size)
    {
        var record = Record(); record.Instruction!.Text = string.Join(" ", Enumerable.Repeat("Review these earlier ordinary words.", 100));
        var host = new FileMenuHost { TextSize = size, Height = _ => MenuPage.Words(3) };
        var restores = 0;
        var column = new WorkspaceRecoveryColumn(host, new[] { record }, _ => false, _ => { restores++; return true; });
        var list = column.Frame!; column.Drawn(list, null);
        column.Act(WorkspaceRecoveryColumn.OpenDraft, "0");
        var rows = 0;
        for (var part = 0; part < 100; part++)
        {
            var frame = column.Frame!;
            column.Drawn(frame, null); host.Wait(0.5);
            Assert.That(frame.Footer.All.Any(prompt => prompt.Prompt.Id == WorkspaceRecoveryColumn.RestoreDraft), Is.False);
            column.Act(WorkspaceRecoveryColumn.RestoreDraft, null);
            rows += frame.Lines.Single().Rows;
            if (frame.Footer[PromptSlot.FarRight] == null) break;
            var prompt = frame.Footer[PromptSlot.FarRight]!;
            if (prompt.Words.StartsWith("First", StringComparison.Ordinal)) break;
            column.Act(prompt.Id, null);
        }
        Assert.That(rows, Is.GreaterThan(host.RowsOf(record.Instruction!.Text, Glaze.Menu.FileColumnDegrees)), "complete draft plus earlier-run context remains reachable");
        Assert.That(restores, Is.Zero);
        Assert.That(host.Sent, Is.Empty);
    }
    [Test]
    public void OnlyThePendingReturnOffersExplicitKeepAndFocusLossRevokesItsDrawnTarget()
    {
        var record = Record(); var host = new FileMenuHost(); var kept = 0;
        var column = new WorkspaceRecoveryColumn(host, new[] { record }, _ => false, _ => false, _ => { kept++; return true; }, record.HandoffId);
        column.Drawn(column.Frame!, null); column.Act(WorkspaceRecoveryColumn.OpenDraft, "0");
        var detail = column.Frame!; column.Drawn(detail, null); host.Wait(0.5);
        Assert.That(detail.Footer.All.Any(prompt => prompt.Prompt.Id == WorkspaceRecoveryColumn.KeepReturn), Is.True);
        column.FocusLeft(); column.Act(WorkspaceRecoveryColumn.KeepReturn, null);
        Assert.That(kept, Is.Zero);
        detail = column.Frame!; column.Drawn(detail, null); host.Wait(0.5);
        column.Act(WorkspaceRecoveryColumn.KeepReturn, null);
        Assert.That(kept, Is.EqualTo(1));
        Assert.That(host.Sent, Is.Empty);
    }
}

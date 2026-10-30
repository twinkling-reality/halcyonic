using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class PresetPagingTests
{
    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void OversizedPresetChoicesFitTheMeasuredFocusedPageAndKeepTheExactCandidateAcrossEveryPage(TextSize size)
    {
        var host = new FileMenuHost { TextSize = size };
        var work = new WaitingWork();
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Running; }, WorkstreamStatus.Running);
        var choices = Enumerable.Range(0, 9).Select(index => new PresetInstruction("Choice " + index,
            "Keep exact output for choice " + index + " and report every condition before continuing.")).ToArray();
        var column = new FileColumn(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Instruct), new CommandFactory(Samples.Client),
            () => null, _ => choices, () => "");
        column.Act(FileScreens.TellIt, null);
        Assert.That(column.Frame!.Focused, Is.True);
        Assert.That(column.Frame.Sections, Is.Empty);
        Assert.That(column.Screen.PresetPages, Is.GreaterThan(1));
        var first = column.Frame.Lines.First(line => line.Action == FileScreens.Preset);
        column.Drawn(column.Frame!, null);
        column.Act(FileScreens.Preset, first.Key);
        var candidate = column.Screen.PresetToSend;
        var seen = new HashSet<string>();
        for (var pageIndex = 0; pageIndex < column.Screen.PresetPages; pageIndex++)
        {
            var frame = column.Frame!;
            Assert.That(frame.Footer.All.Count(), Is.LessThanOrEqualTo(3));
            Assert.That(frame.Lines.Any(line => line.Action == FileScreens.NextPresetPage), Is.True);
            foreach (var line in frame.Lines.Where(line => line.Action == FileScreens.Preset)) seen.Add(line.Words);
            var budget = new HeightBudget(host.PageHeight(1, false, focused: true));
            var height = frame.Lines.Sum(line => budget.Target(line.Rows)) + (frame.Lines.Count - 1) * budget.TargetGap
                + budget.GroupGap + budget.Words(1) + budget.Reason;
            Assert.That(height, Is.LessThanOrEqualTo(budget.Room + 1e-6f), "all actual choice/source/reason/pager heights are reserved");
            column.Drawn(frame, null); host.Wait(0.41);
            column.Act(FileScreens.NextPresetPage, null);
            Assert.That(column.Screen.PresetToSend, Is.EqualTo(candidate), "inspecting another page must not clear the chosen candidate");
            Assert.That(host.Sent, Is.Empty);
        }
        Assert.That(seen, Is.EquivalentTo(choices.Select(choice => choice.Text)));
        Assert.That(column.Screen.PresetPage, Is.Zero);
        Assert.That(column.Frame!.Lines.Single(line => line.Key == first.Key).Chosen, Is.True);
    }

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void PresetPagingRequiresTheCurrentDrawAndExistingTurnGuardIncludingAfterCompact(TextSize size)
    {
        var host = new FileMenuHost { TextSize = size };
        var work = new WaitingWork();
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Running; }, WorkstreamStatus.Running);
        var choices = Enumerable.Range(0, 12).Select(index => new PresetInstruction("Choice " + index, "Keep exact choice " + index)).ToArray();
        var column = new FileColumn(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Instruct), new CommandFactory(Samples.Client),
            () => null, _ => choices, () => "");
        column.Act(FileScreens.TellIt, null);
        column.Act(FileScreens.NextPresetPage, null);
        Assert.That(column.Screen.PresetPage, Is.Zero);
        column.Drawn(column.Frame!, null); column.Act(FileScreens.NextPresetPage, null);
        Assert.That(column.Screen.PresetPage, Is.Zero);
        column.Drawn(column.Frame!, null); host.Wait(0.41); column.Act(FileScreens.NextPresetPage, null);
        Assert.That(column.Screen.PresetPage, Is.EqualTo(1));
        column.Compact(); column.Act(FileScreens.NextPresetPage, null);
        Assert.That(column.Screen.PresetPage, Is.EqualTo(1));
        column.Drawn(column.Frame!, null); column.Act(FileScreens.NextPresetPage, null);
        Assert.That(column.Screen.PresetPage, Is.EqualTo(1));
        column.Drawn(column.Frame!, null); host.Wait(0.41); column.Act(FileScreens.NextPresetPage, null);
        Assert.That(column.Screen.PresetPage, Is.EqualTo(2));
        Assert.That(host.Sent, Is.Empty);
    }
    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void LongPresetNoticeIsReadOnItsOwnPagesAndRepeatedOccurrencesRemainVisible(TextSize size)
    {
        var host = new FileMenuHost { TextSize = size };
        var work = new WaitingWork();
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Running; }, WorkstreamStatus.Running);
        var choices = Enumerable.Range(0, 9).Select(index => new PresetInstruction("Choice " + index, "Keep exact choice " + index)).ToArray();
        var column = new FileColumn(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Instruct), new CommandFactory(Samples.Client),
            () => null, _ => choices, () => "");
        column.Act(FileScreens.TellIt, null);
        column.Drawn(column.Frame!, null);
        column.Act(FileScreens.Preset, "0");
        var candidate = column.Screen.PresetToSend;
        var notice = string.Join(" ", Enumerable.Range(0, 80).Select(index => "Nothing was sent because condition " + index + " failed."));
        column.Said(notice);
        Assert.That(column.Screen.ShowingPresetNotice, Is.True);
        Assert.That(column.Frame!.Lines.First().Words, Is.EqualTo("What Happened"));
        Assert.That(column.Frame.Lines.Last().Words, Is.EqualTo(notice));
        Assert.That(column.Frame.Lines.Last().Runs.AnyTheirs, Is.False);
        var budget = new HeightBudget(host.PageHeight(1, false, focused: true));
        Assert.That(column.Frame.Lines.Sum(line => budget.Words(line.Rows)) + budget.LineGap, Is.LessThanOrEqualTo(budget.Room + 1e-6f));
        Assert.That(column.Frame.Lines.Any(line => line.Action == FileScreens.Preset), Is.False);
        column.Drawn(column.Frame, null); host.Wait(0.41); column.Act(FileScreens.NextPresetNotice, null);
        Assert.That(column.Screen.PresetNoticePart, Is.EqualTo(1));
        column.Drawn(column.Frame!, null);
        Assert.That(column.Screen.PresetNoticePart, Is.EqualTo(1));
        column.Act(FileScreens.BackPresetNotice, null);
        Assert.That((column.Screen.ShowingPresetNotice, column.Screen.Notice, column.Screen.PresetToSend), Is.EqualTo((false, (string?)null, candidate)));
        column.Said(notice);
        Assert.That(column.Screen.ShowingPresetNotice, Is.True, "a new identical occurrence must not be silently acknowledged");
        Assert.That(host.Sent, Is.Empty);
    }

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void OnlyAChoiceOnTheCurrentDrawnPresetPageMayReplaceTheRetainedCandidate(TextSize size)
    {
        var host = new FileMenuHost { TextSize = size };
        var work = new WaitingWork();
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Running; }, WorkstreamStatus.Running);
        var choices = Enumerable.Range(0, 12).Select(index => new PresetInstruction("Choice " + index, "Keep exact choice " + index)).ToArray();
        var column = new FileColumn(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Instruct), new CommandFactory(Samples.Client),
            () => null, _ => choices, () => "");
        column.Act(FileScreens.TellIt, null);
        var oldFrame = column.Frame!;
        var first = oldFrame.Lines.First(line => line.Action == FileScreens.Preset).Key!;
        column.Act(FileScreens.Preset, first);
        Assert.That(column.Screen.ChosenPreset, Is.Null, "a never-drawn page grants no choice authority");
        column.Drawn(oldFrame, null);
        column.Act(FileScreens.Preset, first);
        var candidate = column.Screen.PresetToSend;
        Assert.That(candidate, Is.EqualTo(choices[int.Parse(first)].Text));
        column.Drawn(column.Frame!, null); host.Wait(0.41);
        column.Act(FileScreens.NextPresetPage, null);
        Assert.That(column.Screen.PresetToSend, Is.EqualTo(candidate));
        var currentKey = column.Frame!.Lines.First(line => line.Action == FileScreens.Preset).Key!;
        Assert.That(currentKey, Is.Not.EqualTo(first));
        column.Drawn(oldFrame, null);
        column.Act(FileScreens.Preset, currentKey);
        Assert.That(column.Screen.PresetToSend, Is.EqualTo(candidate), "an old draw cannot authorize the replacement page");
        column.Drawn(column.Frame, null);
        column.Act(FileScreens.Preset, first);
        Assert.That(column.Screen.PresetToSend, Is.EqualTo(candidate), "an off-page key must not replace the retained candidate");
        column.Act(FileScreens.Preset, currentKey);
        Assert.That(column.Screen.PresetToSend, Is.EqualTo(choices[int.Parse(currentKey)].Text));
        Assert.That(host.Sent, Is.Empty);
    }

}

using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The redesign at work (DESIGN's BUILD-SPEC-v4 2.3) through the file's own frame: no panel, Stop alone in the column, the
/// row only where the file still offers an instruction, the step the runtime reports for the cursor, and Stop's own
/// confirmation, which sends the one interrupt.
/// </summary>
public class RoomWorkingTests
{
    private readonly CommandFactory factory = new(Samples.Client);

    private static WorkspacePresentation Present(WaitingWork work, params WorkspaceAction[] offered)
    {
        var now = work.Present();
        return new WorkspacePresentation(now.Character, now.Objective, now.Execution, now.Runtime, offered,
            offered.Where(now.RequiresConfirmation).ToList(), now.Commands, new ActivityEntry[0]);
    }

    private static WaitingWork Work(ExecutionStatus status, params ToolActivityView[] tools)
    {
        var work = new WaitingWork();
        work.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.Status = status;
            execution.ActiveTools = new List<ToolActivityView>(tools);
            execution.ToolActivity = tools.Length > 0 ? ExecutionViewToolActivity.Running : ExecutionViewToolActivity.None;
        }, status == ExecutionStatus.Completed ? WorkstreamStatus.Completed : WorkstreamStatus.Running);
        return work;
    }

    private static RoomBar Bar(FileColumn column) => RoomBar.Of(KitFileBinding.Of(column.Frame!, column.Steering.Armed, column.Now!, "", new ConnectionStatus(ConnectionPhase.Live), "Storefront",
        new[] { new KitSpineSection(new KitRowModel[0]) }, roomDesign: true)!, VoiceStage.Idle, false);

    private (FileMenuHost Host, FileColumn Column) Open(Func<WorkspacePresentation?> present)
    {
        var host = new FileMenuHost();
        var column = new FileColumn(host, present, factory, () => null, _ => null, () => "");
        column.Reflow();
        Draw(host, column);
        return (host, column);
    }

    private static void Draw(FileMenuHost host, FileColumn column)
    {
        column.Drawn(column.Frame!, sidePanel: null);
        host.Wait(1);
    }

    [Test]
    public void AtWorkStopStandsAloneWithNoPanelAndTheCursorHasTheStepTheRuntimeReports()
    {
        var work = Work(ExecutionStatus.Running, new ToolActivityView { ToolCallId = "t1", ToolName = "Edit", Title = "src/score.js", StartedAt = "2026-11-10T09:00:00.000Z" });
        var (_, column) = Open(() => Present(work, WorkspaceAction.Interrupt));
        var bar = Bar(column);
        Assert.That(bar.Working, Is.True);
        Assert.That((bar.Pair, bar.Row.Count, bar.Band, bar.Body.Text, bar.Sentence, bar.Words.Text), Is.EqualTo((false, 0, (string?)null, "", (string?)null, "")),
            "no panel, no row, nothing on the band and no reason in words");
        Assert.That(bar.Column.Select(action => (action.Place, action.Main)), Is.EqualTo(new[] { (KitPlace.Stop, false) }), "Stop alone, a rest action");
        Assert.That(bar.Step?.Words(_ => true), Is.EqualTo("Editing score.js"));
    }

    [Test]
    public void AtWorkTheRowStandsOnlyWhereTheFileStillOffersAnInstruction()
    {
        var work = Work(ExecutionStatus.Verifying);
        var (_, column) = Open(() => Present(work, WorkspaceAction.Instruct, WorkspaceAction.Interrupt));
        var bar = Bar(column);
        Assert.That((bar.Working, bar.State, bar.Step), Is.EqualTo((true, RoomBarState.Rest, (RoomWorkStep?)null)), "checks running are work too; no step is reported, so none is shown");
        Assert.That(bar.Row.Select(action => action.Place), Does.Contain(KitPlace.Field), "the runtime takes an instruction while it works, so the row stands");
        Assert.That(bar.Column.Select(action => action.Place), Is.EqualTo(new[] { KitPlace.Stop }));

        var unstoppable = Open(() => Present(work, WorkspaceAction.Instruct)).Column;
        Assert.That((Bar(unstoppable).Working, Bar(unstoppable).Column.Count), Is.EqualTo((true, 0)), "a runtime that cannot be stopped shows no Stop");
    }

    [Test]
    public void AtRestNothingIsAtWork()
    {
        var work = Work(ExecutionStatus.Completed);
        var (_, column) = Open(() => Present(work, WorkspaceAction.Instruct));
        var bar = Bar(column);
        Assert.That((bar.Working, bar.State, bar.Column.Count, bar.Step), Is.EqualTo((false, RoomBarState.Rest, 0, (RoomWorkStep?)null)));
    }

    [Test]
    public void StopAsksItsOwnConfirmationAndOnlyItsConfirmSendsTheOneInterrupt()
    {
        var work = Work(ExecutionStatus.Running);
        var (host, column) = Open(() => Present(work, WorkspaceAction.Interrupt));
        var stop = Bar(column).Column.Single();
        column.Act(stop.Control.Action, stop.Control.Key);
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Interrupt));
        Assert.That(host.Sent, Is.Empty, "the press only asks");

        var asked = Bar(column);
        Assert.That((asked.State, asked.Working), Is.EqualTo((RoomBarState.Confirm, false)));
        Assert.That(asked.Sentence + KitMomentWords.Dot + asked.Body.Text, Is.EqualTo(column.Steering.Prompt(column.Now!)), "the file's own words: its question in the sentence bar, what stopping does in the panel");
        Assert.That(asked.Sentence, Does.EndWith("?"));
        Assert.That(asked.Column.Any(action => action.Place == KitPlace.Cancel), Is.True);

        // Cancel: back at work, nothing sent.
        Draw(host, column);
        var cancel = Bar(column).Column.Single(action => action.Place == KitPlace.Cancel);
        column.Act(cancel.Control.Action, cancel.Control.Key);
        Assert.That((column.Steering.Armed, Bar(column).Working, host.Sent.Count), Is.EqualTo(((WorkspaceAction?)null, true, 0)));

        // Stop again, read, Confirm: one interrupt.
        Draw(host, column);
        stop = Bar(column).Column.Single();
        column.Act(stop.Control.Action, stop.Control.Key);
        Draw(host, column);
        var confirm = Bar(column).Column.Single(action => action.Place == KitPlace.Yes);
        Assert.That((confirm.Main, confirm.Locks), Is.EqualTo((true, true)), "Confirm stands behind its lock");
        column.Act(confirm.Control.Action, confirm.Control.Key);
        Assert.That(host.Sent.Single(), Is.InstanceOf<ExecutionInterruptCommand>());
        column.Act(confirm.Control.Action, confirm.Control.Key);
        Assert.That(host.Sent.Count, Is.EqualTo(1), "a second press sends nothing");
    }
}

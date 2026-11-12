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

    /// <summary>
    /// The lead's honesty rule (2026-11-12): a recorded or demonstration task's bar carries the product's own line, word for word, in every
    /// state it stands in (at work, at rest, asking); a live task's carries none.
    /// </summary>
    [TestCase(ExecutionStatus.Running)]
    [TestCase(ExecutionStatus.Completed)]
    [TestCase(ExecutionStatus.WaitingForHuman)]
    public void ARecordedTasksBarCarriesTheNoticeInEveryStateAndALiveOneNone(ExecutionStatus status)
    {
        var live = status == ExecutionStatus.WaitingForHuman ? new WaitingWork() : Work(status);
        var (_, liveColumn) = Open(() => Present(live, WorkspaceAction.Instruct, WorkspaceAction.Approve, WorkspaceAction.Deny));
        Assert.That(Bar(liveColumn).Notice, Is.Null, "live work carries no notice");

        var recorded = status == ExecutionStatus.WaitingForHuman ? new WaitingWork() : Work(status);
        recorded.State.ApplySnapshot(Samples.Snapshot(2, new[] { recorded.Workstream }, new[] { recorded.Execution },
            new JournalInfo { JournalId = "recorded-journal", Origin = JournalOrigin.Fixture }), new StateChanges());
        var shown = Present(recorded, WorkspaceAction.Instruct, WorkspaceAction.Approve, WorkspaceAction.Deny);
        var (_, column) = Open(() => shown);
        var bar = Bar(column);
        var expected = DemonstrationFallback.Notice(recordedRun: !shown.Character.Synthetic);
        Assert.That(bar.Notice?.Text, Is.EqualTo(expected), "the product's own line, in state " + bar.State);
        Assert.That(expected, Does.Contain("Nothing here is live"));
    }

    /// <summary>
    /// AUDIT's MUST 2 (2026-11-12): a plain frame (Projects, the menu, New Project...) carries no task's marks, so its notice comes from what the
    /// connection says it shows, by the same one rule: recorded or the demonstration, the product's line whole; live, none.
    /// </summary>
    [TestCase(WorkMarkKind.Recorded, true)]
    [TestCase(WorkMarkKind.Demo, false)]
    public void APlainFrameCarriesTheNoticeWhileARecordingPlaysAndNoneWhileLive(WorkMarkKind shown, bool recordedRun)
    {
        var frame = new MenuFrame("Projects", new Footer(new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close)),
            lines: new[] { new PageLine("Storefront", action: "projects-choose-project", key: "p1") });
        var plain = KitFileBinding.PlainOf(frame);
        Assert.That(RoomBar.Of(plain, VoiceStage.Idle, false, shown).Notice?.Text, Is.EqualTo(DemonstrationFallback.Notice(recordedRun)));
        Assert.That(RoomBar.Of(plain, VoiceStage.Idle, false, null).Notice, Is.Null, "live, none");
        Assert.That(RoomBar.Of(plain, VoiceStage.Idle, false, WorkMarkKind.Practice).Notice, Is.Null, "a practice run is live");
    }

    /// <summary>
    /// The result's Changes page carries the understanding's account only once it is read (SCREENS's seam): nothing before any read, and
    /// a read that could not answer gives no part, never guessed words.
    /// </summary>
    [Test]
    public void TheAccountForTheResultsChangesIsNothingBeforeTheUnderstandingAnswers()
    {
        var work = Work(ExecutionStatus.Completed);
        var (_, column) = Open(() => Present(work, WorkspaceAction.Instruct));
        Assert.That(column.Account(), Is.Null, "nothing before any read");
        column.ResultChangesShown = () => true;
        column.Tick();
        Assert.That(column.Account()?.Parts ?? 0, Is.EqualTo(0), "a read with nothing to read gives no part");
    }

    [Test]
    public void AtRestNothingIsAtWork()
    {
        var work = Work(ExecutionStatus.Completed);
        var (_, column) = Open(() => Present(work, WorkspaceAction.Instruct));
        var bar = Bar(column);
        // At rest: nothing at work; how the run stands is its card (B, 2026-11-12), with the one Changes (the lead's 11:15Z) and the file's
        // own Close in the column.
        Assert.That((bar.Working, bar.State, bar.Step, bar.Card?.Status?.Title), Is.EqualTo((false, RoomBarState.Rest, (RoomWorkStep?)null, (string?)RoomStanding.Finished)));
        // With no check of the computer's own, Checks opens the file's Checks section (AUDIT's census: every page a drawn press away).
        Assert.That(bar.Column.Select(action => (action.Control.Action, action.Control.Key)),
            Is.EqualTo(new[] { (RoomResult.ShowChanges, (string?)null), (MenuFrame.ChooseSection, (string?)FileScreens.Key(FileSection.Checks)), (Footer.Close, (string?)null) }));
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

    [Test]
    public void AnArmedStopLapsesWhenTheTaskRunsAnotherRunByThen()
    {
        var work = Work(ExecutionStatus.Running);
        var steering = new WorkspaceSteering(factory);
        var pressedOn = Present(work, WorkspaceAction.Interrupt);
        Assert.That(steering.Press(WorkspaceAction.Interrupt, pressedOn).Step, Is.EqualTo(SteeringStep.Confirm));

        // The task's run is another by the time Confirm is pressed: nothing is stopped, and the person is told.
        var other = Present(work, WorkspaceAction.Interrupt);
        other.Execution!.ExecutionId = "another-run";
        var outcome = steering.Confirm(other);
        Assert.That((outcome.Step, outcome.Command, outcome.Message), Is.EqualTo((SteeringStep.Explain, (CommandEnvelope?)null, "Not Sent · Task Changed · Check and Retry")));
        Assert.That(steering.Armed, Is.Null, "the confirmation is gone with it");

        // The same run: Confirm stops it, by its own id.
        var same = Work(ExecutionStatus.Running);
        var again = new WorkspaceSteering(factory);
        var shown = Present(same, WorkspaceAction.Interrupt);
        again.Press(WorkspaceAction.Interrupt, shown);
        var sent = again.Confirm(Present(same, WorkspaceAction.Interrupt));
        Assert.That(sent.Step, Is.EqualTo(SteeringStep.Send));
        Assert.That(((ExecutionInterruptCommand)sent.Command!).Payload.ExecutionId, Is.EqualTo(shown.Execution!.ExecutionId));
    }
}

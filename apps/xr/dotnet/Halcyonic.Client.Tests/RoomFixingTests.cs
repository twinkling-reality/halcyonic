#nullable enable
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>A failed check back with its agent on its own (ADR 0044; the addendum's A7): the line while a try runs, and the line a result adds once the tries end.</summary>
public sealed class RoomFixingTests
{
    private static RoomBar AtWork(AutoFixView? fix, ExecutionStatus status = ExecutionStatus.Running)
    {
        var work = new WaitingWork();
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = status; execution.AutoFix = fix; },
            status == ExecutionStatus.Completed ? WorkstreamStatus.Completed : WorkstreamStatus.Running);
        var now = work.Present();
        var offered = new[] { WorkspaceAction.Instruct, WorkspaceAction.Interrupt };
        var workspace = new WorkspacePresentation(now.Character, now.Objective, now.Execution, now.Runtime, offered,
            offered.Where(now.RequiresConfirmation).ToList(), now.Commands, new ActivityEntry[0]);
        var steering = new WorkspaceSteering(new CommandFactory(Samples.Client));
        var frame = FileScreens.Screen(workspace, steering, new FileScreen { Section = FileSection.Activity, Speak = true }, new AnswerRoom(4));
        var binding = KitFileBinding.Of(frame, steering.Armed, workspace, "", new ConnectionStatus(ConnectionPhase.Live), "Storefront",
            new[] { new KitSpineSection(new KitRowModel[0]) }, roomDesign: true);
        Assert.That(binding, Is.Not.Null);
        Assert.That(binding!.Model.AutoFix, Is.SameAs(fix), "the model carries the task's own chain");
        return RoomBar.Of(binding, VoiceStage.Idle, false);
    }

    private static AutoFixView Fix(AutoFixViewState state, long attempt, long cap = 2, AutoFixEndReason? reason = null) =>
        new() { State = state, CheckId = "0193b3a0-0000-7000-8000-000000000001", Attempt = attempt, Cap = cap, Reason = reason };

    [Test]
    public void While_a_try_runs_the_bar_at_work_says_which_try_under_the_failure()
    {
        var bar = AtWork(Fix(AutoFixViewState.Fixing, 1));
        Assert.That(bar.Working, Is.True);
        Assert.That(bar.Fixing, Is.EqualTo("Fixing · Try 1 of 2"));
        Assert.That(bar.Facts!.Select(row => (row.Words.Text, row.Mark)), Is.EqualTo(new[] { (RoomBar.FixingFailed, RoomRowMark.Cross) }));
        Assert.That(bar.FromMoment, Is.False, "the frame stays the task's own");
        Assert.That(bar.Column.Select(action => action.Place), Does.Contain(KitPlace.Stop), "Stop stands as at any work");
        Assert.That(AtWork(Fix(AutoFixViewState.Fixing, 2)).Fixing, Is.EqualTo("Fixing · Try 2 of 2"));
    }

    [Test]
    public void No_try_under_way_gives_no_fixing_line()
    {
        Assert.That(AtWork(null).Fixing, Is.Null);
        Assert.That(AtWork(Fix(AutoFixViewState.Ended, 1, reason: AutoFixEndReason.Passed)).Fixing, Is.Null, "an ended chain is the result's to say");
        Assert.That(AtWork(Fix(AutoFixViewState.Fixing, 0)).Fixing, Is.Null, "no try sent yet");
        Assert.That(AtWork(Fix(AutoFixViewState.Fixing, 1), ExecutionStatus.Completed).Fixing, Is.Null, "not at work");
    }

    [Test]
    public void An_ended_chain_gives_the_result_its_line_only_where_tries_were_made_and_passed_or_ran_out()
    {
        Assert.That(RoomBar.FixedWords(Fix(AutoFixViewState.Ended, 2, reason: AutoFixEndReason.Passed)), Is.EqualTo("Fixed · Try 2 of 2"));
        Assert.That(RoomBar.FixedWords(Fix(AutoFixViewState.Ended, 1, cap: 3, reason: AutoFixEndReason.Passed)), Is.EqualTo("Fixed · Try 1 of 3"));
        Assert.That(RoomBar.FixedWords(Fix(AutoFixViewState.Ended, 2, reason: AutoFixEndReason.CapReached)), Is.EqualTo("Stopped After 2 Tries"));
        Assert.That(RoomBar.FixedWords(Fix(AutoFixViewState.Ended, 1, reason: AutoFixEndReason.PersonStopped)), Is.Null);
        Assert.That(RoomBar.FixedWords(Fix(AutoFixViewState.Ended, 0, reason: AutoFixEndReason.NoChange)), Is.Null, "no try was sent");
        Assert.That(RoomBar.FixedWords(Fix(AutoFixViewState.Fixing, 1)), Is.Null);
        Assert.That(RoomBar.FixedWords(null), Is.Null);
    }
}

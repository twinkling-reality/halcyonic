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
    public void While_a_try_runs_the_bar_at_work_shows_a_status_card_with_its_track()
    {
        var bar = AtWork(Fix(AutoFixViewState.Fixing, 1));
        Assert.That(bar.Working, Is.True);
        Assert.That(bar.Card!.Status!.Value.Title, Is.EqualTo(RoomBar.FixingTitle));
        Assert.That(bar.Card.Status!.Value.Tone, Is.EqualTo(RoomTone.Working));
        Assert.That((bar.Card.TrackAt, bar.Card.TrackOf), Is.EqualTo(((int?)1, (int?)2)), "the try is a track of its own, never a suffix");
        Assert.That(bar.Card.AnyDot, Is.False);
        Assert.That(bar.FromMoment, Is.False, "the frame stays the task's own");
        Assert.That(bar.Column.Select(action => action.Place), Does.Contain(KitPlace.Stop), "Stop stands as at any work");
        Assert.That(AtWork(Fix(AutoFixViewState.Fixing, 2)).Card!.TrackAt, Is.EqualTo(2));
    }

    [Test]
    public void A_try_on_named_steps_names_them_in_its_line_and_keeps_its_track()
    {
        var fix = Fix(AutoFixViewState.Fixing, 1);
        fix.FailedSteps = new System.Collections.Generic.List<AutoFixViewFailedStepsItem> { AutoFixViewFailedStepsItem.Build, AutoFixViewFailedStepsItem.Build };
        var card = RoomBar.FixingCard(fix)!;
        Assert.That(card.Status!.Value.Title, Is.EqualTo(RoomBar.FixingTitle));
        Assert.That(card.Status!.Value.Line, Is.EqualTo("Working on Build by itself"), "what is being fixed, named");
        Assert.That((card.TrackAt, card.TrackOf), Is.EqualTo(((int?)1, (int?)2)));
        Assert.That(card.Steps, Is.Empty, "no node repeating the mark");
        fix.FailedSteps.Add(AutoFixViewFailedStepsItem.Typecheck);
        Assert.That(RoomBar.FixingCard(fix)!.Status!.Value.Line, Is.EqualTo("Working on Build and " + RoomResult.StepWords(CheckStepKind.TypeCheck) + " by itself"));
        Assert.That(RoomBar.FixingCard(Fix(AutoFixViewState.Fixing, 1))!.Status!.Value.Line, Is.EqualTo(RoomBar.FixingLine), "no step named, the line as before");
    }

    [Test]
    public void No_try_under_way_gives_no_fixing_card()
    {
        Assert.That(AtWork(null).Card, Is.Null);
        Assert.That(AtWork(Fix(AutoFixViewState.Ended, 1, reason: AutoFixEndReason.Passed)).Card, Is.Null, "an ended chain is the result's to say");
        Assert.That(AtWork(Fix(AutoFixViewState.Fixing, 0)).Card, Is.Null, "no try sent yet");
        Assert.That(AtWork(Fix(AutoFixViewState.Fixing, 1), ExecutionStatus.Completed).Card, Is.Null, "not at work");
    }

    [Test]
    public void An_ended_chain_gives_a_card_only_where_tries_were_made_and_passed_or_ran_out()
    {
        var fixedCard = RoomBar.FixedCard(Fix(AutoFixViewState.Ended, 2, reason: AutoFixEndReason.Passed))!;
        Assert.That((fixedCard.Status!.Value.Title, fixedCard.Status.Value.Tone), Is.EqualTo((RoomBar.FixedTitle, RoomTone.Good)));
        Assert.That((fixedCard.TrackAt, fixedCard.TrackOf), Is.EqualTo(((int?)2, (int?)2)), "the tries it took");
        Assert.That(RoomBar.FixedCard(Fix(AutoFixViewState.Ended, 1, reason: AutoFixEndReason.Passed))!.TrackAt, Is.EqualTo(1), "passed on the first of two: one used, one left");
        var stopped = RoomBar.FixedCard(Fix(AutoFixViewState.Ended, 2, reason: AutoFixEndReason.CapReached))!;
        Assert.That((stopped.Status!.Value.Title, stopped.Status.Value.Tone), Is.EqualTo((RoomBar.StoppedTitle, RoomTone.Bad)));
        Assert.That(RoomBar.FixedCard(Fix(AutoFixViewState.Ended, 1, reason: AutoFixEndReason.PersonStopped)), Is.Null);
        Assert.That(RoomBar.FixedCard(Fix(AutoFixViewState.Ended, 0, reason: AutoFixEndReason.NoChange)), Is.Null, "no try was sent");
        Assert.That(RoomBar.FixedCard(Fix(AutoFixViewState.Fixing, 1)), Is.Null);
        Assert.That(RoomBar.FixedCard(null), Is.Null);
    }
}

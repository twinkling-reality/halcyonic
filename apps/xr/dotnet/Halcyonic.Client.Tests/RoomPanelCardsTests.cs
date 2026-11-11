#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The result, Changes, Checks and usage as cards (the addendum's A17), from the facts XR gives apart.</summary>
public sealed class RoomPanelCardsTests
{
    private const string Observed =
        "{\"turn_id\":\"t1\"," +
        "\"created\":{\"files\":[{\"path\":\"src/stars.ts\",\"lines\":42,\"bytes\":1300}],\"total\":3}," +
        "\"changed\":{\"files\":[{\"path\":\"src/game.ts\",\"lines_before\":120,\"lines_after\":131,\"bytes\":4100}],\"total\":1}," +
        "\"deleted\":{\"files\":[{\"path\":\"assets/old.png\",\"lines_before\":null}],\"total\":1}," +
        "\"not_read\":2,\"skipped\":null}";

    private const string Skipped =
        "{\"turn_id\":\"t2\",\"created\":{\"files\":[],\"total\":0},\"changed\":{\"files\":[],\"total\":0},\"deleted\":{\"files\":[],\"total\":0}," +
        "\"not_read\":0,\"skipped\":{\"code\":\"no_baseline\",\"message\":\"No earlier reading of the folder.\"}}";

    private static ObservedTurn Turn(string json)
    {
        var log = new ActivityLog();
        log.Record(new[] { ActivityLogBoundTests.Journaled(5, "e1", "execution.files.observed", json) });
        return log.ObservedFor("e1").Single();
    }

    private static IReadOnlyList<CheckStepOutcome> Steps(bool buildFails = false) => new[]
    {
        new CheckStepOutcome(CheckStepKind.TypeCheck, CheckStepResult.Passed),
        new CheckStepOutcome(CheckStepKind.Build, buildFails ? CheckStepResult.Failed : CheckStepResult.Passed),
        new CheckStepOutcome(CheckStepKind.PageDrawn, buildFails ? CheckStepResult.NotRun : CheckStepResult.Passed),
        new CheckStepOutcome(CheckStepKind.VrOffered, buildFails ? CheckStepResult.NotRun : CheckStepResult.Passed),
    };

    private static readonly LineWords Task = LineWords.TheirName("Golden Balloon");

    private static AutoFixView Fix(AutoFixViewState state, long attempt, AutoFixEndReason? reason = null) =>
        new() { State = state, CheckId = "0193b3a0-0000-7000-8000-000000000001", Attempt = attempt, Cap = 2, Reason = reason };

    [Test]
    public void The_result_is_the_tasks_name_over_its_check_and_its_count_as_slots()
    {
        var card = RoomPanelCards.Result(new RoomResult(false, true, false, true, Turn(Observed), Steps()), Task)!;
        Assert.That((card.Title!.Text, card.Head), Is.EqualTo(("Golden Balloon", RoomCardHead.Picture)));
        Assert.That(card.Title.Runs.All(run => run.Theirs), "the task's name is theirs");
        Assert.That(card.Slots.Select(slot => (slot.Kind, slot.Value.Text)), Is.EqualTo(new[] { (RoomKind.Passed, "Passed"), (RoomKind.ToAdd, "5 Files") }));
        Assert.That((card.Status, card.Description), Is.EqualTo(((RoomCardStatus?)null, (LineWords?)null)), "the agent's words stand on the band, never here");
        Assert.That(card.AnyDot, Is.False);
        var waiting = RoomPanelCards.Result(new RoomResult(false, true, false, true, Turn(Observed), Steps()), Task, toAdd: new RoomSlot(RoomKind.ToAdd, LineWords.Ours("4 Files")))!;
        Assert.That(waiting.Slots[1].Value.Text, Is.EqualTo("4 Files"), "the files waiting to be added stand in the count's place");
        Assert.That(RoomPanelCards.Result(new RoomResult(false, false, false, null, null, null), Task), Is.Null, "nothing known: no card");
    }

    [Test]
    public void An_ended_chain_heads_the_result_and_All_Passing_stands_only_with_the_check()
    {
        var fixedCard = RoomPanelCards.Result(new RoomResult(false, true, false, true, Turn(Observed), Steps()), Task, Fix(AutoFixViewState.Ended, 2, AutoFixEndReason.Passed))!;
        Assert.That((fixedCard.Status!.Value.Title, fixedCard.TrackAt, fixedCard.TrackOf), Is.EqualTo((RoomBar.FixedTitle, (int?)2, (int?)2)));
        var since = RoomPanelCards.Result(new RoomResult(false, true, false, null, Turn(Observed), Steps()), Task, Fix(AutoFixViewState.Ended, 2, AutoFixEndReason.Passed))!;
        Assert.That(since.Status, Is.Null, "the check is gone with the folder's change: All Passing does not speak for these files");
        var ranOut = RoomPanelCards.Result(new RoomResult(false, true, false, false, Turn(Observed), Steps(buildFails: true)), Task, Fix(AutoFixViewState.Ended, 2, AutoFixEndReason.CapReached))!;
        Assert.That((ranOut.Status!.Value.Title, ranOut.Status.Value.Tone), Is.EqualTo((RoomBar.StoppedTitle, RoomTone.Bad)));
    }

    [Test]
    public void Changes_are_a_list_of_paths_with_their_kind_and_a_quiet_note()
    {
        var card = RoomPanelCards.Changes(new RoomResult(false, true, false, true, Turn(Observed), Steps()), Task);
        Assert.That(card.Title!.Text, Is.EqualTo(RoomPanelCards.ChangesTitle));
        Assert.That(card.Description!.Text, Is.EqualTo("What Golden Balloon did to the project."));
        Assert.That(card.Description.Runs.Any(run => run.Theirs), "the task's name leans inside our sentence");
        Assert.That(card.List.Select(row => (row.Kind, row.Name.Text, row.Note)), Is.EquivalentTo(new[]
        {
            (RoomKind.Added, "src/stars.ts", (string?)"New"), (RoomKind.Changed, "src/game.ts", "Edited"), (RoomKind.Removed, "assets/old.png", "Removed"),
        }));
        Assert.That(card.List.All(row => row.Mono && row.Name.Runs.All(run => run.Theirs)), "a path is theirs, in mono");
        Assert.That(card.AnyDot, Is.False);
        var unseen = RoomPanelCards.Changes(new RoomResult(false, true, false, true, Turn(Skipped), Steps()), Task);
        Assert.That((unseen.Status!.Value.Title, unseen.List.Count), Is.EqualTo((RoomPanelCards.NotSeen, 0)));
    }

    [Test]
    public void Checks_name_the_outcome_and_lay_the_steps_dim_once_the_run_ended()
    {
        var passing = RoomPanelCards.Checks(new RoomResult(false, true, false, true, Turn(Observed), Steps()));
        Assert.That(passing.Status!.Value.Title, Is.EqualTo(RoomPanelCards.AllPassing));
        Assert.That(passing.Steps.All(step => step.Tone == RoomTone.Good), Is.True);
        var failing = RoomPanelCards.Checks(new RoomResult(false, true, false, false, Turn(Observed), Steps(buildFails: true)), Fix(AutoFixViewState.Ended, 2, AutoFixEndReason.CapReached));
        Assert.That(failing.Status!.Value.Title, Is.EqualTo(RoomPanelCards.ChecksFailed));
        Assert.That(failing.Steps.Select(step => step.Tone), Is.EqualTo(new[] { RoomTone.Good, RoomTone.Bad, RoomTone.Skipped, RoomTone.Skipped }), "never reached: dim, the run ended");
        Assert.That((failing.Steps[1].TriesUsed, failing.Steps[1].TriesOf), Is.EqualTo((2, 2)), "the tries hang on the failed step");
        Assert.That(failing.Steps.Where((_, index) => index != 1).All(step => step.TriesOf == 0), Is.True);
        var fixing = RoomPanelCards.Checks(new RoomResult(false, true, false, false, Turn(Observed), Steps(buildFails: true)), Fix(AutoFixViewState.Fixing, 1));
        Assert.That((fixing.Status!.Value.Title, fixing.Steps[1].Tone, fixing.Steps[2].Tone), Is.EqualTo((RoomBar.FixingTitle, RoomTone.Working, RoomTone.Quiet)), "while it is fixed, the rest wait");
        Assert.That(failing.AnyDot || fixing.AnyDot || passing.AnyDot, Is.False);
        Assert.That(failing.Status!.Value.Title, Does.Not.Contain("Build"), "the outcome, never the node's own word");
    }

    [Test]
    public void Usage_is_a_gauge_a_window_said_to_be_at_most_what_is_left()
    {
        var rows = new[]
        {
            new UsageLeftRow(LineWords.TheirName("Pro") + LineWords.Ours(" · " + UsageLeftPresenter.FiveHourWindow), "At most 62% left", 62, resets: "today at 18:40",
                label: LineWords.TheirName("Pro"), window: UsageLeftPresenter.FiveHourWindow),
            new UsageLeftRow(LineWords.TheirName("Pro") + LineWords.Ours(" · " + UsageLeftPresenter.Weekly), "At most 38% left", 38, resets: "Thursday at 09:00",
                label: LineWords.TheirName("Pro"), window: UsageLeftPresenter.Weekly),
        };
        var card = RoomPanelCards.Usage(rows);
        Assume.That(card, Is.Not.Null);
        Assert.That(card!.Description!.Text, Is.EqualTo(RoomPanelCards.UsageLine), "the numbers are at most what is left, as reported");
        Assert.That(card.Gauges.Select(gauge => gauge.PercentLeft), Is.EqualTo(new[] { 62, 38 }));
        Assert.That(card.Gauges.All(gauge => !gauge.Label.Text.Contains("·")), Is.True, "a window's word alone, never joined on a dot");
        Assert.That(card.Gauges.Select(gauge => gauge.Kind), Is.EqualTo(new[] { RoomKind.Session, RoomKind.Week }));
        Assert.That(card.Gauges.Select(gauge => gauge.Note), Is.EqualTo(new[] { "Resets today at 18:40", "Resets Thursday at 09:00" }));
        Assert.That(RoomPanelCards.Usage(new UsageLeftRow[0]), Is.Null);
    }
}

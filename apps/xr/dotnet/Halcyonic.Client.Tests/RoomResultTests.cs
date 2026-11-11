using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The redesign's result at rest (DESIGN's BUILD-SPEC-v4 2.5 to 2.8): only what the computer itself knows, over the row,
/// with the agent's newest message on the band; Changes a row a file, Checks a row a step; and nothing where nothing
/// is known.
/// </summary>
public class RoomResultTests
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

    private const string Passing =
        "{\"tsc\":{\"exit\":0,\"seconds\":1.5},\"build\":{\"exit\":0,\"seconds\":4.25}," +
        "\"page\":{\"exceptions\":[],\"exception_count\":0,\"console_errors\":0,\"canvases\":1,\"edges_at_3s\":900,\"edges_at_10s\":950,\"drawn\":true,\"pictures\":[]}," +
        "\"unknown\":null,\"passed\":true,\"reads_confined\":true,\"vr\":{\"offered\":\"offered\",\"how\":\"offer_session\",\"exceptions\":0,\"ready\":true}}";

    private static ObservedTurn Turn(string json)
    {
        var log = new ActivityLog();
        log.Record(new[] { ActivityLogBoundTests.Journaled(5, "e1", "execution.files.observed", json) });
        return log.ObservedFor("e1").Single();
    }

    private static IReadOnlyList<CheckStepOutcome> Steps(bool buildFails = false)
    {
        var steps = CheckSteps.Of(HalcyonicJson.Deserialize<CheckFindings>(Passing, strict: true)).ToList();
        if (buildFails) steps[1] = new CheckStepOutcome(CheckStepKind.Build, CheckStepResult.Failed);
        return steps;
    }

    /// <summary>The redesign's view of a task at rest holding <c>result</c>, its agent having said <c>says</c>.</summary>
    private static RoomBar Bar(RoomResult? result, string? says = "I added the golden balloon.", ExecutionStatus status = ExecutionStatus.Completed, Action<FileScreen>? prepare = null)
    {
        var work = new WaitingWork();
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = status; }, status == ExecutionStatus.Completed ? WorkstreamStatus.Completed : WorkstreamStatus.Running);
        var now = work.Present();
        var offered = new[] { WorkspaceAction.Instruct };
        var activity = says != null ? new[] { new ActivityEntry(1, "2026-11-10T09:00:01.000Z", ActivityKind.Message, says, true) } : new ActivityEntry[0];
        var workspace = new WorkspacePresentation(now.Character, now.Objective, now.Execution, now.Runtime, offered, offered.Where(now.RequiresConfirmation).ToList(), now.Commands, activity);
        var steering = new WorkspaceSteering(new CommandFactory(Samples.Client));
        var screen = new FileScreen { Section = FileSection.Activity, Speak = true };
        prepare?.Invoke(screen);
        var frame = FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4));
        var binding = KitFileBinding.Of(frame, steering.Armed, workspace, screen.Instruction.Open ? screen.Instruction.Words : "", new ConnectionStatus(ConnectionPhase.Live), "Storefront",
            new[] { new KitSpineSection(new KitRowModel[0]) }, roomDesign: true, result: result);
        Assert.That(binding, Is.Not.Null, "the redesign binds the frame");
        return RoomBar.Of(binding!, VoiceStage.Idle, false);
    }

    private static string Show(RoomBar bar) => bar.State + " | panel: " + bar.Body.Text + " | says: " + bar.Sentence + " | band: " + bar.Band + " | row " + bar.Row.Count + " | " +
        string.Join(", ", bar.Column.Select(action => action.Words + (action.Main ? "*" : "")));

    [Test]
    public void TheResultStandsOverTheRowWithOnlyWhatTheComputerKnows()
    {
        var bar = Bar(new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: true, Turn(Observed), Steps()));
        Assert.That((bar.Result, bar.Pair, bar.State, bar.Wide), Is.EqualTo((true, true, RoomBarState.Rest, false)), Show(bar));
        Assert.That(bar.Row.Count, Is.GreaterThan(0), "the row still stands under the pair");
        Assert.That((bar.Body.Text, bar.Sentence), Is.EqualTo(("5 Files Changed", (string?)"Build Passed")), "the totals the computer counted, and its own check");
        Assert.That(bar.Band, Is.EqualTo("I added the golden balloon."), "the agent's newest message, on the band and nowhere in the panel");
        Assert.That(bar.BandIsRead, Is.False, "a message is not something to read before the row takes a press");
        Assert.That(bar.Column.Select(action => (action.Control.Action, action.Words, action.Main)), Is.EqualTo(new[]
        {
            (RoomResult.MakeVrVersion, "Make VR Version", true), (RoomResult.ShowChanges, "Changes", false), (RoomResult.ShowChecks, "Checks", false),
        }));

        // One file, and a VR project: the file's own way to an instruction leads, as Request Change.
        var one = "{\"turn_id\":\"t1\",\"created\":{\"files\":[{\"path\":\"a.ts\",\"lines\":1,\"bytes\":1}],\"total\":1},\"changed\":{\"files\":[],\"total\":0},\"deleted\":{\"files\":[],\"total\":0},\"not_read\":0,\"skipped\":null}";
        var vr = Bar(new RoomResult(isVr: true, canMakeVr: false, canTry: true, passed: true, Turn(one), Steps()));
        Assert.That(vr.Body.Text, Is.EqualTo("1 File Changed"));
        Assert.That(vr.Column.Select(action => (action.Words, action.Main, action.Place)), Is.EqualTo(new[]
        {
            ("Request Change", true, KitPlace.Field), ("Try Build", false, KitPlace.Step), ("Changes", false, KitPlace.Step), ("Checks", false, KitPlace.Step),
        }));
    }

    [Test]
    public void WhatTheComputerDoesNotKnowIsLeftOut()
    {
        // No observation, no check: the loop's own view, the row alone.
        var nothing = Bar(new RoomResult(false, false, false, null, null, null));
        Assert.That((nothing.Result, nothing.Pair, nothing.State, nothing.Band), Is.EqualTo((false, false, RoomBarState.Rest, (string?)null)), Show(nothing));
        Assert.That(Bar(null).Result, Is.False);

        // A check and no observation: the check is the panel's one line; no count is guessed, and no Changes.
        var checkedOnly = Bar(new RoomResult(false, false, false, false, null, Steps(buildFails: true)));
        Assert.That((checkedOnly.Body.Text, checkedOnly.Sentence), Is.EqualTo(("Build Failed", (string?)null)));
        Assert.That(checkedOnly.Column.Select(action => action.Words), Is.EqualTo(new[] { "Checks" }));

        // A turn the computer could not read: no count, and Changes says so honestly.
        var skipped = new RoomResult(false, false, false, null, Turn(Skipped), null);
        Assert.That((skipped.FilesChanged, skipped.CountLine), Is.EqualTo(((long?)null, (string?)null)));
        var page = Bar(new RoomResult(false, false, false, null, Turn(Skipped), null, RoomResultPage.Changes));
        Assert.That(page.Facts!.Select(row => row.Words.Text), Is.EqualTo(new[] { "Not Observed" }), "never an empty list read as no changes");

        // The agent at work, or a draft standing: no result.
        Assert.That(Bar(new RoomResult(false, true, false, true, Turn(Observed), Steps()), status: ExecutionStatus.Running).Result, Is.False);
    }

    [Test]
    public void ChangesIsARowAFileEightAPageAndChecksARowAStep()
    {
        var changes = Bar(new RoomResult(false, true, false, true, Turn(Observed), Steps(), RoomResultPage.Changes));
        Assert.That(changes.Facts!.Select(row => (row.Words.Text, row.Mark)), Is.EqualTo(new[]
        {
            ("Made · src/stars.ts", RoomRowMark.None), ("Changed · 120 to 131 Lines · src/game.ts", RoomRowMark.None), ("Removed · assets/old.png", RoomRowMark.None),
        }));
        Assert.That(changes.Facts![0].Words.AnyTheirs && !changes.Facts[0].Words.AllTheirs, Is.True, "the path is the folder's own name inside Halcyonic's line");
        Assert.That(changes.Facts.All(row => row.Words.Runs.Last().Theirs && !row.Words.OursAfterTheirs), Is.True, "the name stands last: none of Halcyonic's words follow it");
        Assert.That((changes.Band, changes.Sentence), Is.EqualTo(((string?)null, (string?)null)), "no band and no sentence on Changes");
        Assert.That(changes.Column.Select(action => (action.Control.Action, action.Main)), Is.EqualTo(new[] { (RoomResult.Back, false) }));
        Assert.That(changes.Row.Count, Is.GreaterThan(0));

        // More than eight files: Next Page is the main action, and the last page offers Back alone.
        var many = "{\"turn_id\":\"t1\",\"created\":{\"files\":[" + string.Join(",", Enumerable.Range(1, 11).Select(index => "{\"path\":\"src/f" + index + ".ts\",\"lines\":1,\"bytes\":1}")) +
            "],\"total\":11},\"changed\":{\"files\":[],\"total\":0},\"deleted\":{\"files\":[],\"total\":0},\"not_read\":0,\"skipped\":null}";
        var first = Bar(new RoomResult(false, true, false, null, Turn(many), null, RoomResultPage.Changes));
        Assert.That((first.Facts!.Count, first.Column[0].Control.Action, first.Column[0].Main, first.Column[0].Words), Is.EqualTo((8, RoomResult.NextPage, true, "Next Page")), "the product's own pager words, from the first page");
        var last = Bar(new RoomResult(false, true, false, null, Turn(many), null, RoomResultPage.Changes, part: 1));
        Assert.That((last.Facts!.Count, last.Column.Count), Is.EqualTo((3, 1)));

        // Checks: each step with its mark; failed, the way to an instruction leads; passed with a build to try, Try Build.
        var failed = Bar(new RoomResult(true, false, false, false, null, Steps(buildFails: true), RoomResultPage.Checks));
        Assert.That(failed.Facts!.Select(row => (row.Words.Text, row.Mark)), Is.EqualTo(new[]
        {
            ("Type Check", RoomRowMark.Check), ("Build", RoomRowMark.Cross), ("Page Drawn", RoomRowMark.Check), ("VR Offered", RoomRowMark.Check),
        }));
        Assert.That(failed.Column.Select(action => (action.Words, action.Main, action.Place)), Is.EqualTo(new[] { ("Write Instruction", true, KitPlace.Field), ("Back", false, KitPlace.Step) }),
            "the file's own way to an instruction, in the product's words");
        // A step the check never reached has no row.
        var stopped = new RoomResult(true, false, false, false, null, new[]
        {
            new CheckStepOutcome(CheckStepKind.TypeCheck, CheckStepResult.Passed), new CheckStepOutcome(CheckStepKind.Build, CheckStepResult.Failed),
            new CheckStepOutcome(CheckStepKind.PageDrawn, CheckStepResult.NotRun), new CheckStepOutcome(CheckStepKind.VrOffered, CheckStepResult.NotRun),
        }, RoomResultPage.Checks);
        Assert.That(stopped.Rows.Select(row => row.Words.Text), Is.EqualTo(new[] { "Type Check", "Build" }));
        var passed = Bar(new RoomResult(true, false, true, true, null, Steps(), RoomResultPage.Checks));
        Assert.That(passed.Column.Select(action => (action.Control.Action, action.Main)), Is.EqualTo(new[] { (RoomResult.TryBuild, true), (RoomResult.Back, false) }));
    }

    [Test]
    public void ThePageShownIsKeptForOneTaskAndRunAndStartsAtTheResultAgain()
    {
        var state = new WorkspacePresentationState();
        var changes = 0;
        state.Changed += () => changes++;
        Assert.That(state.ResultPageFor("w1", "e1"), Is.EqualTo((RoomResultPage.Result, 0)));
        Assert.That(state.ShowResultPage("w1", "e1", RoomResultPage.Changes), Is.True);
        Assert.That(state.ShowResultPage("w1", "e1", RoomResultPage.Changes), Is.False, "the same page again changes nothing");
        Assert.That(state.ShowResultPage("w1", "e1", RoomResultPage.Changes, part: 1), Is.True);
        Assert.That((state.ResultPageFor("w1", "e1"), changes), Is.EqualTo(((RoomResultPage.Changes, 1), 2)));
        Assert.That(state.ResultPageFor("w1", "e2"), Is.EqualTo((RoomResultPage.Result, 0)), "a new run starts at its result");
        Assert.That(state.ResultPageFor("w2", "e1"), Is.EqualTo((RoomResultPage.Result, 0)), "another task has its own");
        Assert.That(state.ShowResultPage("w1", "e1", RoomResultPage.Result), Is.True, "Back");
        Assert.That(state.ResultPageFor("w1", "e1"), Is.EqualTo((RoomResultPage.Result, 0)));
        // A run with no id yet keeps its page too.
        Assert.That(state.ShowResultPage("w3", null, RoomResultPage.Checks), Is.True);
        Assert.That(state.ResultPageFor("w3", null), Is.EqualTo((RoomResultPage.Checks, 0)));
        state.ResetPendingCommands();
        Assert.That(state.ResultPageFor("w3", null), Is.EqualTo((RoomResultPage.Result, 0)));
        // A page past the rows' last shows the last.
        Assert.That(new RoomResult(false, false, false, null, Turn(Observed), null, RoomResultPage.Changes, part: 9).Part, Is.EqualTo(0));
    }
}

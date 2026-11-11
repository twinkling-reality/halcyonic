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
    private static RoomBar Bar(RoomResult? result, string? says = "I added the golden balloon.", ExecutionStatus status = ExecutionStatus.Completed, Action<FileScreen>? prepare = null,
        KitSelectors? selectors = null, AutoFixView? fix = null)
    {
        var work = new WaitingWork();
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = status; execution.AutoFix = fix; }, status == ExecutionStatus.Completed ? WorkstreamStatus.Completed : WorkstreamStatus.Running);
        var now = work.Present();
        var offered = new[] { WorkspaceAction.Instruct };
        var activity = says != null ? new[] { new ActivityEntry(1, "2026-11-10T09:00:01.000Z", ActivityKind.Message, says, true) } : new ActivityEntry[0];
        var workspace = new WorkspacePresentation(now.Character, now.Objective, now.Execution, now.Runtime, offered, offered.Where(now.RequiresConfirmation).ToList(), now.Commands, activity);
        var steering = new WorkspaceSteering(new CommandFactory(Samples.Client));
        var screen = new FileScreen { Section = FileSection.Activity, Speak = true };
        prepare?.Invoke(screen);
        var frame = FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4));
        var binding = KitFileBinding.Of(frame, steering.Armed, workspace, screen.Instruction.Open ? screen.Instruction.Words : "", new ConnectionStatus(ConnectionPhase.Live), "Storefront",
            new[] { new KitSpineSection(new KitRowModel[0]) }, selectors: selectors, roomDesign: true, result: result);
        Assert.That(binding, Is.Not.Null, "the redesign binds the frame");
        return RoomBar.Of(binding!, VoiceStage.Idle, false);
    }

    /// <summary>The card's slots as the person reads them, apart (A17); the card never joins facts on a dot.</summary>
    private static string[] Slots(RoomBar bar)
    {
        Assert.That(bar.Card, Is.Not.Null, "the panel is a card");
        Assert.That(bar.Card!.AnyDot, Is.False, "no fact joined to another on a dot");
        return bar.Card.Slots.Select(slot => slot.Value.Text).ToArray();
    }

    /// <summary>Every action but the file's own Close, which the result always ends with (FIELD's parts note).</summary>
    private static bool NotClose(RoomAction action) => action.Control.Place != KitPlace.Close;

    private static string Show(RoomBar bar) => bar.State + " | panel: " + bar.Body.Text + " | says: " + bar.Sentence + " | band: " + bar.Band + " | row " + bar.Row.Count + " | " +
        string.Join(", ", bar.Column.Where(NotClose).Select(action => action.Words + (action.Main ? "*" : "")));

    [Test]
    public void TheResultStandsOverTheRowWithOnlyWhatTheComputerKnows()
    {
        var bar = Bar(new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: true, Turn(Observed), Steps()));
        Assert.That((bar.Result, bar.Pair, bar.State, bar.Wide), Is.EqualTo((true, true, RoomBarState.Rest, false)), Show(bar));
        Assert.That(bar.Row.Count, Is.GreaterThan(0), "the row still stands under the pair");
        Assert.That((Slots(bar), bar.Sentence), Is.EqualTo((new[] { "Passed", "5 Files" }, (string?)null)), "its own check and the totals the computer counted, as slots");
        Assert.That(bar.Band, Is.EqualTo("I added the golden balloon."), "the agent's newest message, on the band and nowhere in the panel");
        Assert.That(bar.BandIsRead, Is.False, "a message is not something to read before the row takes a press");
        Assert.That(bar.Column.Where(NotClose).Select(action => (action.Control.Action, action.Words, action.Main)), Is.EqualTo(new[]
        {
            (RoomResult.MakeVrVersion, "Make VR Version", true), (RoomResult.ShowChanges, "Changes", false), (RoomResult.ShowChecks, "Checks", false),
        }));

        // One file, and a VR project: the file's own way to an instruction leads, as Request Change.
        var one = "{\"turn_id\":\"t1\",\"created\":{\"files\":[{\"path\":\"a.ts\",\"lines\":1,\"bytes\":1}],\"total\":1},\"changed\":{\"files\":[],\"total\":0},\"deleted\":{\"files\":[],\"total\":0},\"not_read\":0,\"skipped\":null}";
        var vr = Bar(new RoomResult(isVr: true, canMakeVr: false, canTry: true, passed: true, Turn(one), Steps()));
        Assert.That(Slots(vr), Is.EqualTo(new[] { "Passed", "1 File" }));
        Assert.That(vr.Column.Where(NotClose).Select(action => (action.Words, action.Main, action.Place)), Is.EqualTo(new[]
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
        Assert.That((Slots(checkedOnly), checkedOnly.Sentence), Is.EqualTo((new[] { "Failed" }, (string?)null)));
        Assert.That(checkedOnly.Column.Where(NotClose).Select(action => action.Words), Is.EqualTo(new[] { "Checks" }));

        // A turn the computer could not read: no count, and Changes says so honestly.
        var skipped = new RoomResult(false, false, false, null, Turn(Skipped), null);
        Assert.That((skipped.FilesChanged, skipped.CountLine), Is.EqualTo(((long?)null, (string?)null)));
        var page = Bar(new RoomResult(false, false, false, null, Turn(Skipped), null, RoomResultPage.Changes));
        Assert.That((page.Card?.Status?.Title, page.Card?.List.Count), Is.EqualTo(((string?)RoomPanelCards.NotSeen, (int?)0)), "never an empty list read as no changes");

        // The agent at work, or a draft standing: no result.
        Assert.That(Bar(new RoomResult(false, true, false, true, Turn(Observed), Steps()), status: ExecutionStatus.Running).Result, Is.False);
    }

    [Test]
    public void ChangesIsAListAFileFiveAPageAndChecksANodeAStep()
    {
        var changes = Bar(new RoomResult(false, true, false, true, Turn(Observed), Steps(), RoomResultPage.Changes));
        Slots(changes);
        Assert.That(changes.Card!.List.Select(row => (row.Kind, row.Name.Text, row.Note)), Is.EqualTo(new[]
        {
            (RoomKind.Added, "src/stars.ts", (string?)RoomPanelCards.NewNote), (RoomKind.Changed, "src/game.ts", (string?)RoomPanelCards.EditedNote),
            (RoomKind.Removed, "assets/old.png", (string?)RoomPanelCards.RemovedNote),
        }));
        Assert.That(changes.Card.List.All(row => row.Name.AllTheirs), Is.True, "each path is the folder's own name, apart from Halcyonic's words");
        Assert.That((changes.Band, changes.Sentence), Is.EqualTo(((string?)null, (string?)null)), "no band and no sentence on Changes");
        // DEVICE's History is Changes' second action (lead, 2026-11-11), then Back.
        Assert.That(changes.Column.Where(NotClose).Select(action => (action.Control.Action, action.Main)), Is.EqualTo(new[] { (RoomResult.ShowHistory, false), (RoomResult.Back, false) }));
        Assert.That(changes.Row.Count, Is.GreaterThan(0));

        // More than five files: Next Page is the main action, and the last page offers History and Back.
        var many = "{\"turn_id\":\"t1\",\"created\":{\"files\":[" + string.Join(",", Enumerable.Range(1, 11).Select(index => "{\"path\":\"src/f" + index + ".ts\",\"lines\":1,\"bytes\":1}")) +
            "],\"total\":11},\"changed\":{\"files\":[],\"total\":0},\"deleted\":{\"files\":[],\"total\":0},\"not_read\":0,\"skipped\":null}";
        var first = Bar(new RoomResult(false, true, false, null, Turn(many), null, RoomResultPage.Changes));
        Assert.That((first.Card!.List.Count, first.Column[0].Control.Action, first.Column[0].Main, first.Column[0].Words), Is.EqualTo((RoomCard.ListAPage, RoomResult.NextPage, true, "Next Page")), "the product's own pager words, from the first page");
        var last = Bar(new RoomResult(false, true, false, null, Turn(many), null, RoomResultPage.Changes, part: 2));
        Assert.That((last.Card!.List.Count, last.Card.List[0].Name.Text, last.Column.Count(NotClose)), Is.EqualTo((1, "src/f11.ts", 2)));

        // Checks: each step with its mark; failed, the way to an instruction leads; passed with a build to try, Try Build.
        var failed = Bar(new RoomResult(true, false, false, false, null, Steps(buildFails: true), RoomResultPage.Checks));
        Slots(failed);
        Assert.That(failed.Card!.Status?.Title, Is.EqualTo(RoomPanelCards.ChecksFailed));
        Assert.That(failed.Card.Steps.Select(step => (step.Name, step.Tone)), Is.EqualTo(new[]
        {
            ("Type Check", RoomTone.Good), ("Build", RoomTone.Bad), ("Page Drawn", RoomTone.Good), ("VR Offered", RoomTone.Good),
        }));
        Assert.That(failed.Column.Where(NotClose).Select(action => (action.Words, action.Main, action.Place)), Is.EqualTo(new[] { ("Write Instruction", true, KitPlace.Field), ("Back", false, KitPlace.Step) }),
            "the file's own way to an instruction, in the product's words");
        // A step the check never reached has no row.
        var stopped = new RoomResult(true, false, false, false, null, new[]
        {
            new CheckStepOutcome(CheckStepKind.TypeCheck, CheckStepResult.Passed), new CheckStepOutcome(CheckStepKind.Build, CheckStepResult.Failed),
            new CheckStepOutcome(CheckStepKind.PageDrawn, CheckStepResult.NotRun), new CheckStepOutcome(CheckStepKind.VrOffered, CheckStepResult.NotRun),
        }, RoomResultPage.Checks);
        Assert.That(stopped.Rows.Select(row => row.Words.Text), Is.EqualTo(new[] { "Type Check", "Build" }));
        var passed = Bar(new RoomResult(true, false, true, true, null, Steps(), RoomResultPage.Checks));
        Assert.That(passed.Column.Where(NotClose).Select(action => (action.Control.Action, action.Main)), Is.EqualTo(new[] { (RoomResult.TryBuild, true), (RoomResult.Back, false) }));
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

    [TestCase(ProjectViewCheckFiles.Current, true)] [TestCase(ProjectViewCheckFiles.Changed, false)] [TestCase(ProjectViewCheckFiles.Unknown, false)]
    public void TheComputersCheckStandsOnlyForTheFilesItChecked(ProjectViewCheckFiles files, bool stands)
    {
        // A passed check, then the folder changed after it: the check says nothing of the files it never saw (AUDIT2).
        var project = new ProjectView { ProjectId = "p1", Name = "Balloon Pop", CreatedAt = Samples.Time, UpdatedAt = Samples.Time,
            Check = new ProjectViewCheck { CheckId = "c1", RecordedAt = Samples.Time, Position = 1, Files = files, Findings = HalcyonicJson.Deserialize<CheckFindings>(Passing, strict: true) } };
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(1, new WorkstreamView[0], new ExecutionView[0]);
        snapshot.Projects.Add(project);
        state.ApplySnapshot(snapshot, new StateChanges());
        var result = RoomResult.Of(project, state, Turn(Observed))!;
        Assert.That((result.CheckLine, result.Steps.Count > 0), Is.EqualTo((stands ? "Build Passed" : null, stands)));
        var bar = Bar(result);
        Assert.That(bar.Column.Where(NotClose).Any(action => action.Control.Action == RoomResult.ShowChecks), Is.EqualTo(stands), "no Checks page for a check of other files");
        Assert.That(Slots(bar), Does.Contain("5 Files"), "what the computer observed still stands");
    }

    [Test]
    public void TheResultStandsBesideTheAgentsActionWhereTheAgentOffersNeverAsk()
    {
        // Codex, the main path, offers never ask: the row names the agent and its level, and the result still stands over it.
        var was = RoomDesign.Enabled;
        RoomDesign.Enabled = true;
        try
        {
            var selectors = new KitSelectors("This Mac", false, "Codex", agentChangeable: true, offersNeverAsk: true, neverAsk: true);
            var bar = Bar(new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: true, Turn(Observed), Steps()), selectors: selectors);
            Assert.That((bar.Result, Slots(bar), bar.Band), Is.EqualTo((true, new[] { "Passed", "5 Files" }, (string?)"I added the golden balloon.")), Show(bar));
            Assert.That(bar.Column.Where(NotClose).Select(action => action.Control.Action), Is.EqualTo(new[] { RoomResult.MakeVrVersion, RoomResult.ShowChanges, RoomResult.ShowChecks }));
            var agent = bar.Row.Single(action => action.Control.Action == AgentColumn.Open);
            Assert.That(agent.Chip, Is.EqualTo("Never Ask"), "the level the next start will use stays shown beside the result, as the action's chip apart from its words");
            // The agent's moment adds its action to the loop's own bar and answers nothing, so the loop's bar is what stands.
            Assert.That(bar.FromMoment, Is.False);
        }
        finally { RoomDesign.Enabled = was; }
    }

    [Test]
    public void AnEndedChainOfTriesStandsFirstInTheResultAndWhereTheyRanOutAnInstructionLeads()
    {
        AutoFixView Ended(long attempt, AutoFixEndReason reason) =>
            new() { State = AutoFixViewState.Ended, CheckId = "0193b3a0-0000-7000-8000-000000000001", Attempt = attempt, Cap = 2, Reason = reason };
        var was = RoomDesign.Enabled;
        RoomDesign.Enabled = true;
        try
        {
            var fixedBar = Bar(new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: true, Turn(Observed), Steps()), fix: Ended(2, AutoFixEndReason.Passed));
            Assert.That((fixedBar.Card?.Status?.Title, Slots(fixedBar)), Is.EqualTo(((string?)RoomBar.FixedTitle, new[] { "Passed", "5 Files" })), Show(fixedBar));
            Assert.That((fixedBar.Card!.TrackAt, fixedBar.Card.TrackOf), Is.EqualTo(((int?)2, (int?)2)), "the tries as pips, apart from the words");
            Assert.That(fixedBar.Column[0].Control.Action, Is.EqualTo(RoomResult.MakeVrVersion), "a fixed check leaves the result's own way on");
            Assert.That(fixedBar.Column[0].Main, Is.True);

            var stopped = Bar(new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: false, Turn(Observed), Steps(buildFails: true)), fix: Ended(2, AutoFixEndReason.CapReached));
            Assert.That((stopped.Card?.Status?.Title, Slots(stopped)), Is.EqualTo(((string?)RoomBar.StoppedTitle, new[] { "Failed", "5 Files" })), Show(stopped));
            Assert.That((stopped.Column[0].Place, stopped.Column[0].Main), Is.EqualTo((KitPlace.Field, true)), "where the tries ran out, an instruction leads");
            Assert.That(stopped.Column.Count(action => action.Main), Is.EqualTo(1));
            Assert.That(stopped.Column.Where(NotClose).Select(action => action.Control.Action), Does.Contain(RoomResult.MakeVrVersion));

            // A check alone, no observed turn: the chain's status, the check its one slot.
            var alone = Bar(new RoomResult(false, true, false, false, null, Steps(buildFails: true)), fix: Ended(2, AutoFixEndReason.CapReached));
            Assert.That((alone.Card?.Status?.Title, Slots(alone)), Is.EqualTo(((string?)RoomBar.StoppedTitle, new[] { "Failed" })), Show(alone));

            // A fix the check passed, then the folder changed: the check no longer speaks for these files, so neither does
            // "Fixed" (AUDIT's probe, 2026-11-11); the result stands as before any try.
            var changedSince = Bar(new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: null, Turn(Observed), null), fix: Ended(1, AutoFixEndReason.Passed));
            Assert.That((changedSince.Card?.Status, Slots(changedSince)), Is.EqualTo(((RoomCardStatus?)null, new[] { "5 Files" })), Show(changedSince));
            Assert.That(changedSince.Column[0].Control.Action, Is.EqualTo(RoomResult.MakeVrVersion));
            // That the tries ran out stays true after the folder changed.
            var ranOutSince = Bar(new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: null, Turn(Observed), null), fix: Ended(2, AutoFixEndReason.CapReached));
            Assert.That((ranOutSince.Card?.Status?.Title, Slots(ranOutSince)), Is.EqualTo(((string?)RoomBar.StoppedTitle, new[] { "5 Files" })), Show(ranOutSince));

            // Any other end, or none: the result as before any try.
            var other = Bar(new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: true, Turn(Observed), Steps()), fix: Ended(1, AutoFixEndReason.PersonStopped));
            Assert.That((other.Card?.Status, Slots(other)), Is.EqualTo(((RoomCardStatus?)null, new[] { "Passed", "5 Files" })));
        }
        finally { RoomDesign.Enabled = was; }
    }

    [Test]
    public void PlayInHeadsetStandsBesideTryBuildOnlyWhereItCanAct()
    {
        var was = RoomDesign.Enabled;
        RoomDesign.Enabled = true;
        try
        {
            RoomBar With(PlayInHeadset? play) => Bar(new RoomResult(isVr: false, canMakeVr: true, canTry: true, passed: true, Turn(Observed), Steps(), play: play));
            var offered = With(PlayInHeadset.Offered);
            Assert.That(offered.Column.Where(NotClose).Select(action => action.Control.Action), Is.EqualTo(new[] { RoomResult.MakeVrVersion, RoomResult.TryBuild, RoomResult.PlayInHeadset, RoomResult.ShowChanges, RoomResult.ShowChecks }), Show(offered));
            var play = offered.Column.Single(action => action.Control.Action == RoomResult.PlayInHeadset);
            Assert.That((play.Words, play.Main, play.Control.Available), Is.EqualTo((HeadsetPlay.ActionWords, false, true)));
            // A press that starts the preview first stands as well.
            Assert.That(With(PlayInHeadset.NoPreviewOfCheck).Column.Where(NotClose).Any(action => action.Control.Action == RoomResult.PlayInHeadset && action.Control.Available), Is.True);
            // Where it cannot act it is not drawn, as Try Build is not: an action has no disabled look (BUILD-SPEC-v4 1.4).
            foreach (var none in new PlayInHeadset?[] { null, PlayInHeadset.NotConfined, PlayInHeadset.NoWayToHeadset, PlayInHeadset.NoCheck,
                PlayInHeadset.FilesNotChecked, PlayInHeadset.CheckNotPassed, PlayInHeadset.NoControlPlane })
                Assert.That(With(none).Column.Where(NotClose).Any(action => action.Control.Action == RoomResult.PlayInHeadset), Is.False, none?.ToString() ?? "unknown");
        }
        finally { RoomDesign.Enabled = was; }
    }

    [Test]
    public void TheResultGivesItsFactsApartForACard()
    {
        // A17: the count, the check, each change and each step apart, never joined on a separator.
        var result = new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: false, Turn(Observed), Steps(buildFails: true), RoomResultPage.Changes);
        Assert.That(result.Changes.Select(change => (change.Kind, change.KindWords, change.Path, change.Note)), Is.EqualTo(new[]
        {
            (RoomChangeKind.Made, RoomResult.Made, "src/stars.ts", (string?)null),
            (RoomChangeKind.Changed, RoomResult.Changed, "src/game.ts", (string?)"120 to 131 Lines"),
            (RoomChangeKind.Removed, RoomResult.Removed, "assets/old.png", (string?)null),
        }));
        Assert.That(result.NotObserved, Is.False);
        Assert.That(result.StepFacts.Select(step => step.Tone), Does.Contain(RoomStepTone.Failed));
        Assert.That(result.StepFacts.Single(step => step.Tone == RoomStepTone.Failed).Name, Is.EqualTo(RoomResult.StepWords(result.StepFacts.Single(step => step.Tone == RoomStepTone.Failed).Step)));
        Assert.That(result.StepFacts.Count, Is.EqualTo(result.Steps.Count), "steps never reached are named too, so marks hang on the one that failed");
        var skipped = new RoomResult(false, false, false, null, Turn(Skipped), null);
        Assert.That((skipped.Changes.Count, skipped.NotObserved), Is.EqualTo((0, true)));

        var was = RoomDesign.Enabled;
        RoomDesign.Enabled = true;
        try
        {
            var bar = Bar(new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: true, Turn(Observed), Steps()));
            Assert.That((bar.Known?.FilesChanged, bar.Known?.Passed, bar.Ended), Is.EqualTo(((long?)5, (bool?)true, (string?)null)), "the bar hands the card its result");
            var ended = Bar(new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: true, Turn(Observed), Steps()),
                fix: new AutoFixView { State = AutoFixViewState.Ended, CheckId = "0193b3a0-0000-7000-8000-000000000001", Attempt = 2, Cap = 2, Reason = AutoFixEndReason.Passed });
            Assert.That(ended.Ended, Is.EqualTo("Fixed · Try 2 of 2"), "the tries' line apart from the result's own");
        }
        finally { RoomDesign.Enabled = was; }
    }

    private static RoomAdd Copy(WorkstreamCopy? copy) => RoomAdd.Of(copy, working: false);

    private static OwnCopy Own(int files, ConflictedFiles? conflict = null) => new OwnCopy
    {
        Directory = "/copies/w1", MadeAt = "2026-11-11T05:00:00.000Z", BaseTree = "base",
        ToAdd = new PathList { Paths = Enumerable.Range(1, files).Select(index => "src/file" + index + ".ts").ToList(), Total = files },
        ToAddReadAt = "2026-11-11T05:01:00.000Z", Conflict = conflict, ToAddReview = Review,
    };

    /// <summary>The digest of the copy's reading, as the host gives it with its files (ADR 0051, amended).</summary>
    private const string Review = "5f7a0c3e9b1d2f4a6c8e0b2d4f6a8c0e2b4d6f8a0c2e4b6d8f0a2c4e6b8d0f2a";

    [Test]
    public void AddToProjectLeadsAndItsReviewCardConfirmsBehindItsLock()
    {
        var was = RoomDesign.Enabled;
        RoomDesign.Enabled = true;
        try
        {
            RoomBar At(RoomResultPage page, RoomAdd add, int part = 0, string? review = Review) =>
                Bar(new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: true, Turn(Observed), Steps(), page, part, add: add, addReview: review));
            var result = At(RoomResultPage.Result, Copy(Own(10)));
            Assert.That((result.Column[0].Control.Action, result.Column[0].Words, result.Column[0].Main), Is.EqualTo((RoomResult.ShowAdd, RoomAdd.AddToProject, true)), Show(result));
            Assert.That(result.Column.Count(action => action.Main), Is.EqualTo(1));
            // The review is a card (the task as its title, where its work goes, the files as its list), five files a page; it says
            // nothing of undoing, since nothing can undo an add yet (AUDIT, 2026-11-11).
            var review = At(RoomResultPage.Add, Copy(Own(10)));
            Assert.That(review.Card, Is.Not.Null, "the review stands as its card");
            Assert.That((review.Card!.List.Count, review.Card.AnyDot, review.Card.Words().Any(words => words.Contains("undo"))), Is.EqualTo((RoomCard.ListAPage, false, false)));
            // Every file is shown before the add: the first page offers Next Page, the last page Add to Project behind its lock.
            Assert.That(review.Column.Where(NotClose).Select(action => action.Control.Action), Is.EqualTo(new[] { RoomResult.NextPage, RoomResult.Back }), Show(review));
            var last = At(RoomResultPage.Add, Copy(Own(10)), part: 1);
            Assert.That(last.Card!.List.Select(row => row.Name.Text), Is.EqualTo(Enumerable.Range(6, 5).Select(index => "src/file" + index + ".ts")));
            var confirm = last.Column[0];
            Assert.That((confirm.Control.Action, confirm.Words, confirm.Main, confirm.Locks), Is.EqualTo((RoomResult.AddConfirm, RoomAdd.AddToProject, true, true)), Show(last));
            // The add carries the digest of the reading these files came from, as its own key (TASKS's call note); with none, no add.
            Assert.That(confirm.Control.Key, Is.EqualTo(Review), "the confirm carries the review it showed");
            Assert.That(At(RoomResultPage.Add, Copy(Own(10)), part: 1, review: null).Column.Where(NotClose).Select(action => action.Control.Action), Is.EqualTo(new[] { RoomResult.Back }),
                "no review, no add: nothing left, no reading yet, or a reading from before");
            Assert.That(last.Column.Where(NotClose).Select(action => action.Control.Action), Is.EqualTo(new[] { RoomResult.AddConfirm, RoomResult.Back }));
            // Where the host listed fewer files than the add brings, the add is never confirmed here: not every file can be shown.
            var partial = Copy(new OwnCopy { Directory = "/c", MadeAt = "2026-11-11T05:00:00.000Z", BaseTree = "b",
                ToAdd = new PathList { Paths = new List<string> { "src/a.ts" }, Total = 3 }, ToAddReadAt = "2026-11-11T05:01:00.000Z" });
            Assert.That(At(RoomResultPage.Add, partial).Column.Where(NotClose).Select(action => action.Control.Action), Is.EqualTo(new[] { RoomResult.Back }));
            // Nothing to add: no action, and its review confirms nothing.
            Assert.That(At(RoomResultPage.Result, Copy(Own(0))).Column.Where(NotClose).Any(action => action.Control.Action is RoomResult.ShowAdd or RoomResult.ShowConflict), Is.False);
            Assert.That(At(RoomResultPage.Add, Copy(Own(0))).Column.Where(NotClose).Select(action => action.Control.Action), Is.EqualTo(new[] { RoomResult.Back }));
            // The project changed the same files: Let Agent Fix leads; its page is FIELD's card naming them.
            var conflict = Copy(Own(2, new ConflictedFiles { Files = new List<ConflictedFile> { new() { Path = "src/game.ts", Kind = ConflictKind.Text } }, Total = 1 }));
            Assert.That(At(RoomResultPage.Result, conflict).Column[0].Control.Action, Is.EqualTo(RoomResult.ShowConflict));
            var fitting = At(RoomResultPage.Conflict, conflict);
            Assert.That((fitting.Card != null, fitting.Column[0].Control.Action, fitting.Column[0].Words), Is.EqualTo((true, RoomResult.Fit, RoomAdd.LetAgentFix)));
        }
        finally { RoomDesign.Enabled = was; }
    }

    [Test]
    public void TheResultAndItsPagesEndWithTheFilesOwnCloseAndTheRowAtRestWithProjects()
    {
        // The census (JUDGE, 2026-11-11): a task's file had no drawn way back to the Projects place.
        var result = Bar(new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: true, Turn(Observed), Steps()));
        Assert.That((result.Column.Last().Control.Place, result.Column.Last().Main, result.Column.Count(action => action.Control.Place == KitPlace.Close)), Is.EqualTo((KitPlace.Close, false, 1)), Show(result));
        var changes = Bar(new RoomResult(false, true, false, true, Turn(Observed), Steps(), RoomResultPage.Changes));
        Assert.That(changes.Column.Last().Control.Place, Is.EqualTo(KitPlace.Close));
        // At rest with nothing known, no pair: "Projects" last in the row, never main, raising the same Close.
        var nothing = Bar(new RoomResult(false, false, false, null, null, null));
        var back = nothing.Row.Last();
        Assert.That((back.Words, back.Main, back.Control.Place), Is.EqualTo((RoomBar.ProjectsWords, false, KitPlace.Close)), Show(nothing));
    }

    [Test]
    public void HistoryShowsItsCardItsRowsPressesAndRestoreBehindItsLock()
    {
        // DEVICE's History on the result's own page: the sentence while it reads, the versions as rows picked from by
        // their own controls (the version's id as the key), and on a review Restore behind its lock, then Cancel.
        var history = new HistoryColumn("p1", "Kite Run");
        var reading = Bar(new RoomResult(false, true, false, null, null, null, RoomResultPage.History, history: history));
        Assert.That(reading.Sentence, Is.EqualTo("Reading History"));
        Assert.That(reading.Card, Is.Null);

        var version = new CheckpointView
        {
            CheckpointId = "01a19000-0000-7000-8000-000000000011", ProjectId = "p1", Reason = CheckpointReason.BeforeTask, ExecutionId = null,
            RecordedAt = "2026-11-11T08:00:00.000Z", Tree = new string('b', 64), Files = 3,
        };
        history.Listed(new CheckpointsResponse { ProjectId = "p1", Result = new AvailableCheckpoints { Checkpoints = new List<CheckpointView> { version } } },
            null, DateTimeOffset.Parse("2026-11-11T09:00:00Z"), TimeZoneInfo.Utc);
        var listed = Bar(new RoomResult(false, true, false, null, null, null, RoomResultPage.History, history: history));
        var row = listed.Card!.List.Single();
        Assert.That((row.Press!.Action, row.Press.Key), Is.EqualTo((RoomResult.HistoryVersion, version.CheckpointId)));
        Assert.That(listed.Column.Where(NotClose).Select(action => action.Control.Action), Is.EqualTo(new[] { RoomResult.HistoryBack }));

        history.Choose(version.CheckpointId);
        history.Previewed(new RestorePreviewResponse
        {
            ProjectId = "p1",
            Result = new AvailableRestorePreview
            {
                Preview = new RestorePreview
                {
                    CheckpointId = version.CheckpointId, ChangedBack = new PathList { Paths = new List<string> { "a.ts" }, Total = 1 },
                    Removed = new PathList(), Returned = new PathList(), ProjectHeadMoved = false,
                },
                Review = new string('a', 64),
            },
        });
        var review = Bar(new RoomResult(false, true, false, null, null, null, RoomResultPage.History, history: history));
        Assert.That(review.Column.Where(NotClose).Select(action => (action.Control.Action, action.Main)), Is.EqualTo(new[] { (RoomResult.HistoryRestore, true), (RoomResult.HistoryCancel, false) }));
        Assert.That(review.Column[0].Locks, Is.True, "Restore stands behind its lock");
        Assert.That(review.Card!.AnyDot, Is.False);
    }
}

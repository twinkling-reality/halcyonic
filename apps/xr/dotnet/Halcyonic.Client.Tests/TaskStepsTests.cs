using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The plain "what is happening" list (the lead's ruling of 2026-11-16 02:01Z; AUDIT2's words of 02:02Z): each step from
/// a fact the journal holds, Planning only in plan mode, the computer's own check counted only while the files are as it
/// checked them, Ready only after it passed, and a status the computer can't tell never read as failed.
/// </summary>
public class TaskStepsTests
{
    private const string Request = "Make a page that says hello and counts my visits";

    private static ExecutionView Execution(ExecutionStatus status, bool? plan = null, bool? planLeft = null, params ToolActivityView[] tools) => new()
    {
        ExecutionId = "execution",
        Instruction = Request,
        Status = status,
        Plan = plan,
        PlanLeft = planLeft,
        TurnCount = status == ExecutionStatus.Starting ? 0 : 1,
        ActiveTools = new List<ToolActivityView>(tools),
        ToolActivity = tools.Length > 0 ? ExecutionViewToolActivity.Running : ExecutionViewToolActivity.None,
    };

    private static ToolActivityView Tool(string name, string? title) =>
        new() { ToolCallId = "call-1", ToolName = name, Title = title, StartedAt = "2026-11-16T03:00:00.000Z" };

    private static ObservedTurn Turn(long created, long changed, long deleted, FilesObservedSkipped? skipped = null) =>
        new(7, "2026-11-16T03:04:00.000Z", new FilesObserved
        {
            TurnId = "turn-1",
            Created = new FilesObservedCreated { Total = created },
            Changed = new FilesObservedChanged { Total = changed },
            Deleted = new FilesObservedDeleted { Total = deleted },
            Skipped = skipped,
        });

    private static ProjectViewCheck HostCheck(bool passed, ProjectViewCheckFiles files, CheckFindingsVerdict? verdict = null) => new()
    {
        CheckId = "check-1",
        RecordedAt = "2026-11-16T03:05:00.000Z",
        Files = files,
        Findings = new CheckFindings { Passed = passed, Verdict = verdict ?? (passed ? CheckFindingsVerdict.Passed : CheckFindingsVerdict.Failed) },
    };

    /// <summary>A starter's app, whose check is the computer's own build and open.</summary>
    private static IReadOnlyList<TaskStep> Of(ExecutionView? execution, ProjectViewCheck? check, bool checkRunning, ObservedTurn? lastTurn) =>
        TaskSteps.Of(execution, TaskCheckWay.StarterApp, TaskCheckSeen.Of(check), checkRunning, lastTurn);

    private static string[] Read(IReadOnlyList<TaskStep> steps) => steps.Select(step => step.ToString()).ToArray();

    [Test]
    public void NoTaskHasNoSteps()
    {
        Assert.That(Of(null, null, false, null), Is.Empty);
    }

    [Test]
    public void AStartingTaskShowsTheRequestDoneAndWorkingNow()
    {
        Assert.That(Read(Of(Execution(ExecutionStatus.Starting), null, false, null)), Is.EqualTo(new[]
        {
            "Done Your Request: " + Request,
            "Now Working",
            "NotYet Check",
            "NotYet Ready",
        }));
    }

    [Test]
    public void WorkingSaysTheStepUnderWayAndNothingWhereNoneIsReported()
    {
        var editing = Of(Execution(ExecutionStatus.Running, tools: Tool("Edit", "src/index.html")), null, false, null);
        Assert.That(editing[1].ToString(), Is.EqualTo("Now Working: Editing index.html"));
        var silent = Of(Execution(ExecutionStatus.Running), null, false, null);
        Assert.That(silent[1].ToString(), Is.EqualTo("Now Working"), "no step reported, no line");
    }

    [Test]
    public void TheAgentsOwnTestRunIsStillWorkingNeverTheComputersCheck()
    {
        var steps = Of(Execution(ExecutionStatus.Verifying), null, false, null);
        Assert.That(steps[1].Word, Is.EqualTo(TaskSteps.Working));
        Assert.That(steps[1].Kind, Is.EqualTo(TaskStepKind.Now));
        Assert.That(steps.Any(step => step.Word == TaskSteps.Checking), Is.False, "the agent's report is not this list's check");
    }

    [Test]
    public void AWaitingApprovalMarksTheStepThatIsNowAndKeepsItsWord()
    {
        var execution = Execution(ExecutionStatus.WaitingForHuman);
        execution.PendingApprovals.Add(new ApprovalView { ApprovalId = "a", Subject = new ToolUseSubject { ToolName = "Bash" } });
        var steps = Of(execution, null, false, null);
        Assert.That(steps[1].ToString(), Is.EqualTo("NeedsYou Working: Needs You: Run Command?"));
        Assert.That(steps[1].Mark, Is.EqualTo(GlazeIcon.WaitingForYou));
        Assert.That(steps.Skip(2).Select(step => step.Kind), Is.All.EqualTo(TaskStepKind.NotYet));
    }

    [Test]
    public void AQuestionOrAnotherRequestSaysOnlyThatItNeedsYou()
    {
        var execution = Execution(ExecutionStatus.WaitingForHuman);
        execution.PendingApprovals.Add(new ApprovalView { ApprovalId = "a", Subject = new ToolUseSubject { ToolName = "apply_patch" } });
        Assert.That(Of(execution, null, false, null)[1].Line!.Text, Is.EqualTo("Needs You"), "never a raw tool name");
    }

    [Test]
    public void PlanningStandsOnlyInPlanMode()
    {
        Assert.That(Of(Execution(ExecutionStatus.Running), null, false, null).Select(step => step.Word), Has.None.EqualTo(TaskSteps.Planning));
        Assert.That(Read(Of(Execution(ExecutionStatus.Running, plan: true), null, false, null)), Is.EqualTo(new[]
        {
            "Done Your Request: " + Request,
            "Now Planning",
            "NotYet Working",
            "NotYet Check",
            "NotYet Ready",
        }));
        Assert.That(Read(Of(Execution(ExecutionStatus.Completed, plan: true), null, false, Turn(0, 0, 0))), Is.EqualTo(new[]
        {
            "Done Your Request: " + Request,
            "Done Planning",
            "NotYet Working: " + RunStart.CarryOutPlan,
            "NotYet Check",
            "NotYet Ready",
        }), "a plan made is not work done: the person carries it out next, nothing to check yet");
        Assert.That(Of(Execution(ExecutionStatus.Running, plan: true, planLeft: true), null, false, null).Select(step => step.ToString()).Take(3),
            Is.EqualTo(new[] { "Done Your Request: " + Request, "Done Planning", "Now Working" }));
    }

    [Test]
    public void AFinishedTurnSaysTheFilesTheComputerSawChangeAndOffersTheCheck()
    {
        Assert.That(Read(Of(Execution(ExecutionStatus.Completed), null, false, Turn(1, 2, 0))), Is.EqualTo(new[]
        {
            "Done Your Request: " + Request,
            "Done Working: Changed 3 Files",
            "NotYet Check: " + TaskSteps.CheckDoes,
            "NotYet Ready",
        }));
        Assert.That(Of(Execution(ExecutionStatus.Completed), null, false, Turn(1, 0, 0))[1].Line!.Text, Is.EqualTo("Changed 1 File"));
    }

    [Test]
    public void FilesTheComputerDidNotReadAreNeverSaidAsNone()
    {
        var skipped = Turn(0, 0, 0, new FilesObservedSkipped { Code = FilesObservedSkippedCode.TooLarge, Message = "too large" });
        Assert.That(Of(Execution(ExecutionStatus.Completed), null, false, skipped)[1].Line, Is.Null);
        Assert.That(Of(Execution(ExecutionStatus.Completed), null, false, null)[1].Line, Is.Null);
    }

    [Test]
    public void ACheckUnderWayIsCheckingNow()
    {
        var steps = Of(Execution(ExecutionStatus.Completed), null, true, Turn(1, 0, 0));
        Assert.That(steps[2].ToString(), Is.EqualTo("Now Checking"));
        Assert.That(steps[3].ToString(), Is.EqualTo("NotYet Ready"));
    }

    [Test]
    public void ReadyOnlyAfterTheComputersOwnCheckPassedOnTheFilesAsTheyAre()
    {
        Assert.That(Read(Of(Execution(ExecutionStatus.Completed), HostCheck(true, ProjectViewCheckFiles.Current), false, Turn(1, 0, 0))).Skip(2),
            Is.EqualTo(new[] { "Done Check", "Done Ready" }));
        foreach (var files in new[] { ProjectViewCheckFiles.Changed, ProjectViewCheckFiles.Unknown })
        {
            Assert.That(Read(Of(Execution(ExecutionStatus.Completed), HostCheck(true, files), false, Turn(1, 0, 0))).Skip(2),
                Is.EqualTo(new[] { "NotYet Check: " + TaskSteps.CheckDoes, "NotYet Ready" }), files + ": a check of other files does not count");
        }
    }

    [Test]
    public void AFailedCheckEndsTheListAndOneNotKnownIsPartlyChecked()
    {
        var failed = Of(Execution(ExecutionStatus.Completed), HostCheck(false, ProjectViewCheckFiles.Current), false, Turn(1, 0, 0));
        Assert.That(failed.Last().ToString(), Is.EqualTo("End Checks Failed"));
        Assert.That(failed.Select(step => step.Word), Has.None.EqualTo(TaskSteps.Ready));
        var partly = Of(Execution(ExecutionStatus.Completed), HostCheck(false, ProjectViewCheckFiles.Current, CheckFindingsVerdict.NotKnown), false, Turn(1, 0, 0));
        Assert.That(partly.Last().ToString(), Is.EqualTo("End Partly Checked"), "not known is never failed");
    }

    [Test]
    public void AStarterOnlyCheckEndsAsStarterOnlyWithItsLineNeverFailed()
    {
        var starter = Of(Execution(ExecutionStatus.Completed), HostCheck(false, ProjectViewCheckFiles.Current, CheckFindingsVerdict.StarterOnly), false, Turn(1, 0, 0));
        var last = starter.Last();
        Assert.That((last.Kind, last.Word, last.Line?.Text), Is.EqualTo((TaskStepKind.End, "Starter Only", (string?)"Nothing new is drawn yet. Say what you want to see.")));
        Assert.That(starter.Select(step => step.Word), Has.None.EqualTo(TaskSteps.ChecksFailed), "a fresh app never reads as broken");
        Assert.That(last.Mark, Is.Not.EqualTo(GlazeIcon.ChecksFailed), "never the failed mark");
        Assert.That(CheckSteps.StarterOnly(new CheckFindings { Passed = false, Verdict = CheckFindingsVerdict.StarterOnly }), Is.True);
        Assert.That(CheckSteps.StarterOnly(new CheckFindings { Passed = false, Verdict = CheckFindingsVerdict.Failed }), Is.False);
    }

    [Test]
    public void StoppedFailedAndUnknownEndWhereTheWorkStood()
    {
        Assert.That(Read(Of(Execution(ExecutionStatus.Interrupted), null, false, null)), Is.EqualTo(new[] { "Done Your Request: " + Request, "End Stopped" }));
        Assert.That(Of(Execution(ExecutionStatus.Failed), null, false, null).Last().ToString(), Is.EqualTo("End Failed"));
        var unknown = Execution(ExecutionStatus.Unknown);
        unknown.StatusReason = new ErrorInfo { Code = "runtime_connection_lost", Message = "lost" };
        var steps = Of(unknown, null, false, null);
        Assert.That(steps.Last().Word, Is.EqualTo(TaskSteps.CantTell));
        Assert.That(steps.Last().Line!.Text, Is.EqualTo(StateLanguage.LostTouch));
        Assert.That(steps.Select(step => step.Word), Has.None.EqualTo(TaskSteps.Failed), "can't tell is never read as failed");
    }

    [Test]
    public void TheStepsWordsAreShortTitleCaseAndCarryNoDot()
    {
        var words = new[] { TaskSteps.YourRequest, TaskSteps.Planning, TaskSteps.Working, TaskSteps.Check, TaskSteps.Checking, TaskSteps.Ready,
            TaskSteps.ChecksFailed, TaskSteps.PartlyChecked, TaskSteps.Stopped, TaskSteps.Failed, TaskSteps.CantTell, TaskSteps.NeedsYou };
        foreach (var word in words)
        {
            var parts = word.Split(' ');
            Assert.That(parts.Length, Is.InRange(1, 3), word);
            Assert.That(parts.All(part => char.IsUpper(part[0])), Is.True, word);
            Assert.That(word, Does.Not.Contain("·"), word);
        }
        Assert.That(TaskSteps.CheckDoes, Does.Not.Contain("·"));
    }

    [Test]
    public void AProjectOfItsOwnSaysItsOwnChecksOrThatOneMustBeChosen()
    {
        var own = TaskSteps.Of(Execution(ExecutionStatus.Completed), TaskCheckWay.OwnCheck, null, false, Turn(1, 0, 0));
        Assert.That(own[2].ToString(), Is.EqualTo("NotYet Check: " + TaskSteps.OwnCheckDoes));
        var choose = TaskSteps.Of(Execution(ExecutionStatus.Completed), TaskCheckWay.ChooseOwnCheck, null, false, Turn(1, 0, 0));
        Assert.That(choose[2].ToString(), Is.EqualTo("NotYet Check: " + TaskSteps.ChooseCheck));
        var ran = new ProjectOwnCheckRunView
        {
            CheckId = "own-1",
            RecordedAt = "2026-11-16T03:05:00.000Z",
            Files = ProjectOwnCheckRunViewFiles.Current,
            Findings = new OwnCheckFindings { Outcome = OwnCheckFindingsOutcome.Ran, Passed = true },
        };
        Assert.That(Read(TaskSteps.Of(Execution(ExecutionStatus.Completed), TaskCheckWay.OwnCheck, TaskCheckSeen.Of(ran), false, Turn(1, 0, 0))).Skip(2),
            Is.EqualTo(new[] { "Done Check", "Done Ready" }));
        var changed = new ProjectOwnCheckRunView
        {
            CheckId = "own-2",
            RecordedAt = "2026-11-16T03:05:00.000Z",
            Files = ProjectOwnCheckRunViewFiles.Current,
            Findings = new OwnCheckFindings { Outcome = OwnCheckFindingsOutcome.CommandChanged, Passed = false },
        };
        Assert.That(TaskSteps.Of(Execution(ExecutionStatus.Completed), TaskCheckWay.OwnCheck, TaskCheckSeen.Of(changed), false, Turn(1, 0, 0)).Last().ToString(),
            Is.EqualTo("End Partly Checked"), "a check that could not run its commands is never failed");
    }

    [Test]
    public void WhereNoCheckExistsTheListEndsAtWorking()
    {
        Assert.That(Read(TaskSteps.Of(Execution(ExecutionStatus.Completed), TaskCheckWay.None, null, false, Turn(0, 2, 0))), Is.EqualTo(new[]
        {
            "Done Your Request: " + Request,
            "Done Working: Changed 2 Files",
        }), "never a step nobody can reach");
        Assert.That(TaskSteps.Of(Execution(ExecutionStatus.Running), TaskCheckWay.None, null, false, null).Select(step => step.Word),
            Is.EqualTo(new[] { TaskSteps.YourRequest, TaskSteps.Working }));
    }

    [Test]
    public void TheWayFollowsTheHostsRule()
    {
        Assert.That(TaskSteps.WayOf(new ProjectView { ProjectId = "p", Starter = "web-game", World = new ProjectWorldView() }, false), Is.EqualTo(TaskCheckWay.None), "a world has no build");
        Assert.That(TaskSteps.WayOf(new ProjectView { ProjectId = "p", Starter = "web-game" }, true), Is.EqualTo(TaskCheckWay.None), "a starter that declares no build");
        Assert.That(TaskSteps.WayOf(new ProjectView { ProjectId = "p", Starter = "web-game" }, false), Is.EqualTo(TaskCheckWay.StarterApp));
        Assert.That(TaskSteps.WayOf(new ProjectView { ProjectId = "p" }, false), Is.EqualTo(TaskCheckWay.ChooseOwnCheck));
        Assert.That(TaskSteps.WayOf(new ProjectView { ProjectId = "p", OwnCheck = new ProjectOwnCheckView() }, false), Is.EqualTo(TaskCheckWay.OwnCheck));
    }

    [Test]
    public void ALineKeepsWhoseWordsItHolds()
    {
        var steps = Of(Execution(ExecutionStatus.Running, tools: Tool("Edit", "src/index.html")), null, false, null);
        Assert.That(steps[0].Line!.Runs.Select(run => run.Theirs), Is.All.True, "the request is the person's");
        Assert.That(steps[1].Line!.Runs.Select(run => (run.Text, run.Theirs)), Is.EqualTo(new[] { ("Editing ", false), ("index.html", true) }), "our verb, the file's own name theirs");
    }
}

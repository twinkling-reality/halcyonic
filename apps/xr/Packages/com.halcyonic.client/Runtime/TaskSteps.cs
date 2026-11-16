#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>Where a step of a task stands.</summary>
    public enum TaskStepKind
    {
        Done,
        Now,
        NotYet,

        /// <summary>The step that is now waits for the person: an approval or a question.</summary>
        NeedsYou,

        /// <summary>The task ended here, short of Ready: checks failed, stopped, failed or can't tell. No step follows it.</summary>
        End,
    }

    /// <summary>One step of the plain "what is happening" list: its mark, its word and at most one line under it.</summary>
    public sealed class TaskStep
    {
        public TaskStep(TaskStepKind kind, GlazeIcon mark, string word, string? line)
        {
            Kind = kind;
            Mark = mark;
            Word = word;
            Line = line;
        }

        public TaskStepKind Kind { get; }
        public GlazeIcon Mark { get; }

        /// <summary>The step's own word, Halcyonic's.</summary>
        public string Word { get; }

        /// <summary>
        /// One line under the step, or null: the person's request as they gave it (theirs), the step under way
        /// (<see cref="RoomWork"/>), the files the computer saw change, what waits for the person, or what Check Project does.
        /// </summary>
        public string? Line { get; }

        public override string ToString() => Kind + " " + Word + (Line == null ? "" : ": " + Line);
    }

    /// <summary>
    /// The plain "what is happening" list for a task (the film's clarity pass; the lead's ruling of 2026-11-16 02:01Z):
    /// Your Request, Planning only in plan mode, Working, Check, Ready, each from a fact the journal holds and nothing
    /// else. The request is the execution's instruction; planning is plan mode until the plan is left; working is the
    /// agent's turn; a waiting approval or question marks the step that is now; the check is the computer's own Check
    /// Project, counted only while the files are as it checked them; Ready only after that check passed. The agent's own
    /// test run is its report, never this list's check, and a status the computer can't tell is never read as failed.
    /// </summary>
    public static class TaskSteps
    {
        public const string YourRequest = "Your Request";
        public const string Planning = "Planning";
        public const string Working = "Working";
        public const string Check = "Check";
        public const string Checking = "Checking";
        public const string Ready = "Ready";
        public const string ChecksFailed = "Checks Failed";
        public const string PartlyChecked = "Partly Checked";
        public const string Stopped = "Stopped";
        public const string Failed = "Failed";
        public const string CantTell = "Can't Tell";
        public const string NeedsYou = "Needs You";

        /// <summary>What Check Project does, under the check's step until it has run (the lead's L1, 2026-11-16 02:05Z).</summary>
        public const string CheckDoes = "Builds the app and opens it.";

        /// <summary>The files the computer saw change over the turn.</summary>
        public static string Changed(long files) =>
            "Changed " + files.ToString(System.Globalization.CultureInfo.InvariantCulture) + (files == 1 ? " File" : " Files");

        /// <summary>What waits for the person, under the step that is now.</summary>
        public static string Waits(ExecutionView execution)
        {
            var approval = execution.PendingApprovals.FirstOrDefault();
            if (approval?.Subject is ToolUseSubject tool && WorkspaceText.RunsCommand(tool.ToolName)) return NeedsYou + ": " + KitMomentWords.RunThisCommand;
            return NeedsYou;
        }

        /// <summary>
        /// The steps of <paramref name="execution"/>: none without one. <paramref name="check"/> is its project's last check
        /// of the computer's own; <paramref name="checkRunning"/> says a Check Project the person sent is under way;
        /// <paramref name="lastTurn"/> is what the computer saw of the folder over the last turn, or null.
        /// </summary>
        public static IReadOnlyList<TaskStep> Of(ExecutionView? execution, ProjectViewCheck? check, bool checkRunning, ObservedTurn? lastTurn)
        {
            if (execution == null) return Array.Empty<TaskStep>();
            var steps = new List<TaskStep>
            {
                new TaskStep(TaskStepKind.Done, GlazeIcon.FinishedThisRound, YourRequest, string.IsNullOrWhiteSpace(execution.Instruction) ? null : execution.Instruction),
            };

            var status = execution.Status;
            var planOnly = execution.Plan == true && execution.PlanLeft != true;
            var working = status == ExecutionStatus.Starting || status == ExecutionStatus.Running || status == ExecutionStatus.Verifying;
            var waiting = status == ExecutionStatus.WaitingForHuman;
            var ended = status == ExecutionStatus.Failed || status == ExecutionStatus.Interrupted || status == ExecutionStatus.Unknown;
            var finished = status == ExecutionStatus.Completed;

            // The step that is now is Planning while the plan is not left, else Working.
            if (execution.Plan == true)
            {
                if (!planOnly) steps.Add(Step(TaskStepKind.Done, Planning, null));
                else if (finished) steps.Add(Step(TaskStepKind.Done, Planning, null));
                else if (!AddNow(steps, Planning, execution, working, waiting, ended, line: null)) return steps;
            }

            if (planOnly)
            {
                steps.Add(Step(TaskStepKind.NotYet, Working, null));
            }
            else if (finished)
            {
                steps.Add(Step(TaskStepKind.Done, Working, FilesLine(lastTurn)));
            }
            else
            {
                var under = working ? RoomWork.Of(execution)?.Words(_ => true) : null;
                if (!AddNow(steps, Working, execution, working, waiting, ended, under)) return steps;
                steps.Add(Step(TaskStepKind.NotYet, Check, null));
                steps.Add(Step(TaskStepKind.NotYet, Ready, null));
                return steps;
            }

            if (planOnly)
            {
                steps.Add(Step(TaskStepKind.NotYet, Check, null));
                steps.Add(Step(TaskStepKind.NotYet, Ready, null));
                return steps;
            }

            // The work is done: the computer's own check, counted only while the files are as it checked them.
            var covers = check != null && check.Files == ProjectViewCheckFiles.Current;
            if (checkRunning)
            {
                steps.Add(new TaskStep(TaskStepKind.Now, GlazeIcon.CheckingItsWork, Checking, null));
                steps.Add(Step(TaskStepKind.NotYet, Ready, null));
                return steps;
            }
            if (!covers)
            {
                steps.Add(Step(TaskStepKind.NotYet, Check, CheckDoes));
                steps.Add(Step(TaskStepKind.NotYet, Ready, null));
                return steps;
            }
            var findings = check!.Findings;
            if (findings.Passed)
            {
                steps.Add(Step(TaskStepKind.Done, Check, null));
                steps.Add(Step(TaskStepKind.Done, Ready, null));
                return steps;
            }
            steps.Add(CheckSteps.PartlyChecked(findings)
                ? new TaskStep(TaskStepKind.End, GlazeIcon.CantTellYet, PartlyChecked, null)
                : new TaskStep(TaskStepKind.End, GlazeIcon.ChecksFailed, ChecksFailed, null));
            return steps;
        }

        /// <summary>
        /// Adds the step that is now, or the end in its place; false when the task ended there, so no step follows.
        /// </summary>
        private static bool AddNow(List<TaskStep> steps, string word, ExecutionView execution, bool working, bool waiting, bool ended, string? line)
        {
            if (waiting)
            {
                steps.Add(new TaskStep(TaskStepKind.NeedsYou, GlazeIcon.WaitingForYou, word, Waits(execution)));
                return true;
            }
            if (ended)
            {
                steps.Add(End(execution));
                return false;
            }
            steps.Add(new TaskStep(working ? TaskStepKind.Now : TaskStepKind.NotYet, working ? GlazeIcon.Working : GlazeIcon.NotStarted, word, line));
            return true;
        }

        private static TaskStep End(ExecutionView execution) => execution.Status switch
        {
            ExecutionStatus.Interrupted => new TaskStep(TaskStepKind.End, GlazeIcon.Stopped, Stopped, null),
            ExecutionStatus.Failed => new TaskStep(TaskStepKind.End, GlazeIcon.CouldNotFinish, Failed, null),
            ExecutionStatus.Unknown => new TaskStep(TaskStepKind.End, GlazeIcon.CantTellYet, CantTell, StateLanguage.CantTellWhy(execution.StatusReason?.Code)),
            _ => throw new ArgumentOutOfRangeException(nameof(execution), execution.Status, "Only a task that ended short of done has an end."),
        };

        private static TaskStep Step(TaskStepKind kind, string word, string? line) => new TaskStep(kind, kind switch
        {
            TaskStepKind.Done => GlazeIcon.FinishedThisRound,
            TaskStepKind.Now => GlazeIcon.Working,
            TaskStepKind.NotYet => GlazeIcon.NotStarted,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A waiting or ending step carries its own mark."),
        }, word, line);

        /// <summary>The files the computer saw change over the turn, or null where it did not read them or saw none.</summary>
        private static string? FilesLine(ObservedTurn? turn)
        {
            if (turn == null || turn.Skipped != null) return null;
            var total = turn.CreatedTotal + turn.ChangedTotal + turn.DeletedTotal;
            return total > 0 ? Changed(total) : null;
        }
    }
}

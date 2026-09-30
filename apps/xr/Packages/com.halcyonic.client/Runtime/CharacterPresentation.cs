#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>What a character is doing. The XR layer chooses the pose, motion and sound for each.</summary>
    public enum CharacterActivity
    {
        /// <summary>The workstream has no execution yet.</summary>
        Idle,
        Starting,
        Working,
        /// <summary>Working, with a test run in progress.</summary>
        Verifying,
        WaitingForHuman,
        /// <summary>The last turn ended. This says nothing about whether the work is correct.</summary>
        TurnFinished,
        Failed,
        Interrupted,
        /// <summary>Halcyonic cannot currently observe the work; shown instead of a guess.</summary>
        Unknown,
    }

    /// <summary>
    /// What a workstream's character conveys. The control plane decides status and attention; this
    /// maps them to cues the XR layer renders. Every cue has a text form, so no state is conveyed by
    /// color alone (docs/internal/product/PRODUCT.md).
    /// </summary>
    public sealed class CharacterPresentation
    {
        public CharacterPresentation(
            string workstreamId,
            string title,
            CharacterActivity activity,
            string statusLabel,
            AttentionLevel attention,
            IReadOnlyList<string> attentionNotes,
            int pendingApprovals,
            bool synthetic,
            bool recorded,
            bool stale)
        {
            WorkstreamId = workstreamId;
            Title = title;
            Activity = activity;
            StatusLabel = statusLabel;
            Attention = attention;
            AttentionNotes = attentionNotes;
            PendingApprovals = pendingApprovals;
            Synthetic = synthetic;
            Recorded = recorded;
            Stale = stale;
        }

        public string WorkstreamId { get; }

        /// <summary>The workstream's title, as <see cref="LabelText.Plain"/> shows it.</summary>
        public string Title { get; }

        public CharacterActivity Activity { get; }

        public string StatusLabel { get; }

        public AttentionLevel Attention { get; }

        /// <summary>Why the character needs attention, one line per reason, as <see cref="LabelText.Plain"/> shows it.</summary>
        public IReadOnlyList<string> AttentionNotes { get; }

        public int PendingApprovals { get; }

        /// <summary>The work is simulated (for example the mock runtime) and must be labeled as such.</summary>
        public bool Synthetic { get; }

        /// <summary>The state comes from a recorded fixture journal and must be labeled as development data.</summary>
        public bool Recorded { get; }

        /// <summary>The session is not live, so this is the last known state.</summary>
        public bool Stale { get; }
    }

    public static class CharacterPresenter
    {
        public static CharacterPresentation Present(WorkstreamView workstream, ClientProjection state, bool live)
        {
            var execution = state.CurrentExecution(workstream);
            var activity = ActivityOf(workstream.Status);
            var notes = new List<string>();
            foreach (var reason in workstream.Attention.Reasons) notes.Add(LabelText.Plain(Explain(reason, state)));
            return new CharacterPresentation(
                workstream.WorkstreamId,
                LabelText.Plain(workstream.Title),
                activity,
                LabelOf(activity),
                workstream.Attention.Level,
                notes,
                execution?.PendingApprovals.Count ?? 0,
                execution?.Runtime.Synthetic ?? false,
                state.Journal?.Origin == JournalOrigin.Fixture,
                !live);
        }

        public static CharacterActivity ActivityOf(WorkstreamStatus status) => status switch
        {
            WorkstreamStatus.Created => CharacterActivity.Idle,
            WorkstreamStatus.Starting => CharacterActivity.Starting,
            WorkstreamStatus.Running => CharacterActivity.Working,
            WorkstreamStatus.Verifying => CharacterActivity.Verifying,
            WorkstreamStatus.WaitingForHuman => CharacterActivity.WaitingForHuman,
            WorkstreamStatus.Completed => CharacterActivity.TurnFinished,
            WorkstreamStatus.Failed => CharacterActivity.Failed,
            WorkstreamStatus.Interrupted => CharacterActivity.Interrupted,
            WorkstreamStatus.Unknown => CharacterActivity.Unknown,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unhandled workstream status."),
        };

        public static string LabelOf(CharacterActivity activity) => activity switch
        {
            CharacterActivity.Idle => "Not started",
            CharacterActivity.Starting => "Starting",
            CharacterActivity.Working => "Working",
            CharacterActivity.Verifying => "Running tests",
            CharacterActivity.WaitingForHuman => "Needs you",
            CharacterActivity.TurnFinished => "Turn finished",
            CharacterActivity.Failed => "Failed",
            CharacterActivity.Interrupted => "Stopped",
            CharacterActivity.Unknown => "State unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(activity), activity, "Unhandled activity."),
        };

        private static string Explain(AttentionReason reason, ClientProjection state)
        {
            state.Executions.TryGetValue(reason.ExecutionId, out var execution);
            switch (reason)
            {
                case ApprovalPendingReason approval:
                    foreach (var pending in execution?.PendingApprovals ?? new List<ApprovalView>())
                    {
                        if (pending.ApprovalId == approval.ApprovalId && pending.Subject is ToolUseSubject tool)
                        {
                            return "Approval needed to use " + tool.ToolName + ": " + tool.Summary;
                        }
                    }
                    return "Approval needed.";
                case ExecutionFailedReason _:
                    return execution?.StatusReason is { } failure ? "Failed: " + failure.Message : "The execution failed.";
                case ExecutionStateUnknownReason _:
                    return execution?.StatusReason is { } unknown
                        ? "State unknown: " + unknown.Message
                        : "Halcyonic cannot currently observe this execution.";
                case VerificationFailedReason tests:
                    var run = execution?.LastTestRun;
                    return run != null && run.TestRunId == tests.TestRunId && run.Summary != null
                        ? "Tests failed: " + run.Summary
                        : "Tests failed.";
                default:
                    return "Needs attention.";
            }
        }
    }
}

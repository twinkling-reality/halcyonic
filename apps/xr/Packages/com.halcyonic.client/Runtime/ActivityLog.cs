#nullable enable
using System.Collections.Generic;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    public enum ActivityKind
    {
        Lifecycle,
        Turn,
        Message,
        Tool,
        TestRun,
        Approval,
        Command,
        Connection,
    }

    /// <summary>One line of an execution's activity, derived from a journaled event.</summary>
    public sealed class ActivityEntry
    {
        public ActivityEntry(long position, string occurredAt, ActivityKind kind, string text, bool reported)
        {
            Position = position;
            OccurredAt = occurredAt;
            Kind = kind;
            Text = text;
            Reported = reported;
        }

        public long Position { get; }

        public string OccurredAt { get; }

        public ActivityKind Kind { get; }

        public string Text { get; }

        /// <summary>The text is a claim made in agent output, not an observed fact.</summary>
        public bool Reported { get; }
    }

    /// <summary>
    /// Recent activity per execution, derived from journaled events. Live events come from
    /// <see cref="StateChanges.Events"/>; a snapshot carries current state but no history, so after
    /// a resynchronization the history of an execution being looked at is read again with
    /// <see cref="IEventHistory"/>. Not thread safe; use it from one thread.
    /// </summary>
    public sealed class ActivityLog
    {
        private readonly int capacity;
        private readonly Dictionary<string, List<ActivityEntry>> entries = new Dictionary<string, List<ActivityEntry>>();
        private readonly Dictionary<string, string> toolNames = new Dictionary<string, string>();
        private readonly Dictionary<string, string> runtimeNames = new Dictionary<string, string>();

        public ActivityLog(int capacityPerExecution = 200)
        {
            capacity = capacityPerExecution;
        }

        /// <summary>The execution's activity, oldest first.</summary>
        public IReadOnlyList<ActivityEntry> For(string executionId) =>
            entries.TryGetValue(executionId, out var list) ? list : (IReadOnlyList<ActivityEntry>)new ActivityEntry[0];

        /// <summary>Adds events in journal order, skipping ones already recorded. Returns the executions that changed.</summary>
        public ISet<string> Record(IEnumerable<StoredEvent> events)
        {
            var changed = new HashSet<string>();
            foreach (var stored in events)
            {
                var executionId = stored.Event.ExecutionId;
                if (executionId == null) continue;
                if (!entries.TryGetValue(executionId, out var list))
                {
                    list = new List<ActivityEntry>();
                    entries[executionId] = list;
                }
                if (list.Count > 0 && stored.Position <= list[list.Count - 1].Position)
                {
                    if (Contains(list, stored.Position)) continue;
                }
                var entry = Describe(stored);
                if (entry == null) continue;
                Insert(list, entry);
                if (list.Count > capacity) list.RemoveAt(0);
                changed.Add(executionId);
            }
            return changed;
        }

        /// <summary>Forgets everything, for example when the session moves to another journal.</summary>
        public void Clear()
        {
            entries.Clear();
            toolNames.Clear();
            runtimeNames.Clear();
        }

        private ActivityEntry? Describe(StoredEvent stored)
        {
            var e = stored.Event;
            ActivityEntry Entry(ActivityKind kind, string text, bool reported = false) =>
                new ActivityEntry(stored.Position, e.OccurredAt, kind, text, reported);

            switch (e)
            {
                case ExecutionCreatedEvent created:
                    // Recorded when the control plane accepts the start, before the runtime has started anything.
                    if (e.ExecutionId != null) runtimeNames[e.ExecutionId] = created.Payload.Runtime.DisplayName;
                    return Entry(ActivityKind.Lifecycle, "Asked " + created.Payload.Runtime.DisplayName + " to start");
                case RuntimeExecutionStartedEvent _:
                    // Started only once the runtime says so.
                    return Entry(ActivityKind.Lifecycle, e.ExecutionId != null && runtimeNames.TryGetValue(e.ExecutionId, out var runtime)
                        ? "Started on " + runtime
                        : "Started");
                case ExecutionStartFailedEvent failed:
                    return Entry(ActivityKind.Lifecycle, "Could not start: " + failed.Payload.Error.Message);
                case ExecutionStateUnknownEvent unknown:
                    return Entry(ActivityKind.Connection, "State unknown: " + unknown.Payload.Message);
                case RuntimeTurnStartedEvent _:
                    return Entry(ActivityKind.Turn, "Turn started");
                case RuntimeTurnCompletedEvent _:
                    return Entry(ActivityKind.Turn, "Turn finished");
                case RuntimeTurnFailedEvent turnFailed:
                    return Entry(ActivityKind.Turn, "Turn failed: " + turnFailed.Payload.Error.Message);
                case RuntimeTurnInterruptedEvent _:
                    return Entry(ActivityKind.Turn, "Turn stopped");
                case RuntimeAgentMessageEvent message:
                    return Entry(ActivityKind.Message, message.Payload.Text, reported: true);
                case RuntimeToolStartedEvent tool:
                    toolNames[ToolKey(e, tool.Payload.ToolCallId)] = tool.Payload.ToolName;
                    return Entry(ActivityKind.Tool, tool.Payload.Title == null ? tool.Payload.ToolName : tool.Payload.ToolName + ": " + tool.Payload.Title);
                case RuntimeToolCompletedEvent toolDone:
                    var name = toolNames.TryGetValue(ToolKey(e, toolDone.Payload.ToolCallId), out var known) ? known : "A tool";
                    return Entry(ActivityKind.Tool, name + (toolDone.Payload.Outcome == ToolOutcome.Succeeded ? " succeeded" : " failed"));
                case RuntimeTestRunStartedEvent tests:
                    return Entry(ActivityKind.TestRun, tests.Payload.Label == null ? "Tests started" : "Tests started: " + tests.Payload.Label);
                case RuntimeTestRunCompletedEvent testsDone:
                    return Entry(ActivityKind.TestRun, "Tests " + TestWord(testsDone.Payload.Outcome) + (testsDone.Payload.Summary == null ? "" : ": " + testsDone.Payload.Summary));
                case RuntimeApprovalRequestedEvent approval:
                    return Entry(ActivityKind.Approval, approval.Payload.Subject is ToolUseSubject subject
                        ? "Approval requested to use " + subject.ToolName + ": " + subject.Summary
                        : "Approval requested");
                case RuntimeApprovalResolvedEvent resolved:
                    return Entry(ActivityKind.Approval, resolved.Payload.Decision == ApprovalResolution.Approved ? "Approved" : "Denied");
                case RuntimeConnectionLostEvent lost:
                    return Entry(ActivityKind.Connection, "Lost contact with the runtime: " + lost.Payload.Reason);
                case CommandAcceptedEvent accepted:
                    return Entry(ActivityKind.Command, accepted.Payload.Command.Client.Name + " asked to " + Verb(accepted.Payload.Command));
                case CommandRejectedEvent rejected:
                    return Entry(ActivityKind.Command, "Refused to " + Verb(rejected.Payload.Command) + ": " + rejected.Payload.Rejection.Message);
                case CommandFailedEvent commandFailed:
                    return Entry(ActivityKind.Command, "A request failed: " + commandFailed.Payload.Failure.Message
                        + (commandFailed.Payload.Failure.Effect == FailureEffect.Unknown ? " It may have taken effect anyway." : ""));
                default:
                    // Command completion adds nothing a person needs to read here,
                    // and the model a runtime reports using is on the execution itself.
                    return null;
            }
        }

        /// <summary>Tool call ids are native and only unique within one execution.</summary>
        private static string ToolKey(EventEnvelope e, string toolCallId) => e.ExecutionId + "\n" + toolCallId;

        private static string Verb(CommandEnvelope command) => command switch
        {
            ExecutionRespondToApprovalCommand respond => respond.Payload.Decision == ApprovalDecision.Approve ? "approve" : "deny",
            ExecutionInterruptCommand _ => "stop the turn",
            ExecutionSendInstructionCommand _ => "send an instruction",
            ExecutionStartCommand _ => "start work",
            _ => command.CommandType,
        };

        private static string TestWord(TestOutcome outcome) => outcome switch
        {
            TestOutcome.Passed => "passed",
            TestOutcome.Failed => "failed",
            _ => "errored",
        };

        private static bool Contains(List<ActivityEntry> list, long position)
        {
            foreach (var entry in list)
            {
                if (entry.Position == position) return true;
            }
            return false;
        }

        private static void Insert(List<ActivityEntry> list, ActivityEntry entry)
        {
            var index = list.Count;
            while (index > 0 && list[index - 1].Position > entry.Position) index--;
            list.Insert(index, entry);
        }
    }
}

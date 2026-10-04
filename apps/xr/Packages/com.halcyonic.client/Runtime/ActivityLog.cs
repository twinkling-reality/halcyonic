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
                    // The message is the agent app's or the control plane's own; a folder's problem is said by its code.
                    return Entry(ActivityKind.Lifecycle, EntryText.FolderProblem(null, failed.Payload.Error.Code) is string folder
                        ? "Couldn't start: " + char.ToLowerInvariant(folder[0]) + folder.Substring(1)
                        : "Couldn't start");
                case ExecutionStateUnknownEvent unknown:
                    // Why, by its code alone, as the character says it: the message is a diagnostic.
                    return Entry(ActivityKind.Connection, StateLanguage.CantTell(StateLanguage.CantTellWhy(StateUnknownCodeOf(unknown.Payload.Code))));
                case RuntimeTurnStartedEvent _:
                    return Entry(ActivityKind.Turn, "Round started");
                case RuntimeTurnCompletedEvent _:
                    return Entry(ActivityKind.Turn, "Round finished");
                case RuntimeTurnFailedEvent _:
                    // The error is the agent app's own, naming it, a request or a path: never shown.
                    return Entry(ActivityKind.Turn, "Couldn't finish this round");
                case RuntimeTurnInterruptedEvent _:
                    return Entry(ActivityKind.Turn, "Round stopped");
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
                    // The reason is the agent app's diagnostic, naming it, a request or a path: never shown.
                    return Entry(ActivityKind.Connection, StateLanguage.LostTouch);
                case CommandAcceptedEvent accepted:
                    // A client's name is not a person's, so it is not shown as one; a command without plain words is left out.
                    return Verb(accepted.Payload.Command) is string asked ? Entry(ActivityKind.Command, "Asked to " + asked) : null;
                case CommandRejectedEvent rejected:
                    // Why, by the refusal's code: its message is the control plane's own, for developers.
                    return Entry(ActivityKind.Command, WorkspaceText.Couldnt(Verb(rejected.Payload.Command) is string refused ? "Couldn't " + refused : "Your computer refused a request",
                        WorkspaceText.WhyRefused(rejected.Payload.Rejection.Code)));
                case CommandFailedEvent commandFailed:
                    // The failure's message is the agent app's or the control plane's own: what failed, and
                    // whether it may have happened anyway, are what a person can act on. An effect that can't
                    // be ruled out is never said as "Couldn't", which says nothing happened.
                    if (commandFailed.Payload.Failure.Effect == FailureEffect.Unknown) return Entry(ActivityKind.Command, NotSureItHappened);
                    return Entry(ActivityKind.Command, VerbOf(commandFailed.Payload.CommandType) is string verb ? "Couldn't " + verb : "A request failed");
                default:
                    // Command completion adds nothing a person needs to read here,
                    // and the model a runtime reports using is on the execution itself.
                    return null;
            }
        }

        /// <summary>Tool call ids are native and only unique within one execution.</summary>
        private static string ToolKey(EventEnvelope e, string toolCallId) => e.ExecutionId + "\n" + toolCallId;

        /// <summary>A command whose effect can't be ruled out, in WORDS.md's unknown-effect pattern (settled by the coordinator, 2026-10-04).</summary>
        public const string NotSureItHappened = "Not sure it happened. Check its activity before you try again.";

        /// <summary>A state-unknown event's code as the control plane writes it, so the log and the character word it alike.</summary>
        private static string StateUnknownCodeOf(StateUnknownCode code) => code switch
        {
            StateUnknownCode.ControlPlaneRestarted => "control_plane_restarted",
            StateUnknownCode.StartOutcomeUnknown => "start_outcome_unknown",
            _ => "",
        };

        /// <summary>What a command asks for, in plain words, or null for one that has none yet.</summary>
        private static string? Verb(CommandEnvelope command) => command switch
        {
            ExecutionRespondToApprovalCommand respond => respond.Payload.Decision == ApprovalDecision.Approve ? "approve" : "deny",
            ExecutionInterruptCommand _ => "stop it",
            ExecutionSendInstructionCommand _ => "send an instruction",
            ExecutionStartCommand _ => "start work",
            ExecutionAnswerQuestionCommand _ => "answer the agent's question",
            _ => null,
        };

        /// <summary>What a command of this type asks for, in plain words, or null for one that has none yet.</summary>
        private static string? VerbOf(CommandType type) => type switch
        {
            CommandType.ExecutionRespondToApproval => "answer the request",
            CommandType.ExecutionInterrupt => "stop it",
            CommandType.ExecutionSendInstruction => "send an instruction",
            CommandType.ExecutionStart => "start work",
            CommandType.ExecutionAnswerQuestion => "answer the agent's question",
            _ => null,
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

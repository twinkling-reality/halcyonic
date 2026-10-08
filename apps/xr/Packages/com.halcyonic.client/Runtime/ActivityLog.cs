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
            : this(position, occurredAt, kind, reported ? LineWords.Theirs(text) : LineWords.Written(text), reported)
        {
        }

        /// <param name="words">What it says as runs: Halcyonic's words upright, a name, a title or the agent's message from outside leaning.</param>
        public ActivityEntry(long position, string occurredAt, ActivityKind kind, LineWords words, bool reported)
        {
            Position = position;
            OccurredAt = occurredAt;
            Kind = kind;
            Words = words;
            Text = words.Text;
            Reported = reported;
        }

        public long Position { get; }

        public string OccurredAt { get; }

        public ActivityKind Kind { get; }

        public string Text { get; }

        /// <summary>What it says as runs, outside text shown by <see cref="LabelText"/>'s rule.</summary>
        public LineWords Words { get; }

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
            ActivityEntry Entry(ActivityKind kind, string text) => Said(kind, LineWords.Written(text));
            ActivityEntry Said(ActivityKind kind, LineWords words, bool reported = false) =>
                new ActivityEntry(stored.Position, e.OccurredAt, kind, words, reported);
            LineWords Ours(string words) => LineWords.Written(words);

            switch (e)
            {
                case ExecutionCreatedEvent created:
                    // Recorded when the control plane accepts the start, before the runtime has started anything.
                    if (e.ExecutionId != null) runtimeNames[e.ExecutionId] = created.Payload.Runtime.DisplayName;
                    // Theirs last, so a name never stands where Halcyonic's words do (settled 2026-10-08).
                    return Said(ActivityKind.Lifecycle, Ours("Asked to start on ") + LineWords.TheirName(created.Payload.Runtime.DisplayName));
                case RuntimeExecutionStartedEvent _:
                    // Started only once the runtime says so.
                    return Said(ActivityKind.Lifecycle, e.ExecutionId != null && runtimeNames.TryGetValue(e.ExecutionId, out var runtime)
                        ? Ours("Started on ") + LineWords.TheirName(runtime)
                        : Ours("Started"));
                case ExecutionStartFailedEvent failed:
                    // The message is the agent app's or the control plane's own; a folder's problem is said by its code.
                    return Entry(ActivityKind.Lifecycle, WorkspaceText.WhyFailed(failed.Payload.Error.Code, running: false) is string why
                        ? StateLanguage.Lead("Couldn't start", why)
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
                    return Said(ActivityKind.Message, LineWords.Theirs(message.Payload.Text), reported: true);
                case RuntimeToolStartedEvent tool:
                    toolNames[ToolKey(e, tool.Payload.ToolCallId)] = tool.Payload.ToolName;
                    // Halcyonic's word first, so a tool named "Approved" never reads as Halcyonic's own entry (settled 2026-10-08).
                    var used = Ours("Used ") + LineWords.TheirName(tool.Payload.ToolName);
                    return Said(ActivityKind.Tool, LineWords.Theirs(tool.Payload.Title) is { IsEmpty: false } title ? used + Ours(": ") + title : used);
                case RuntimeToolCompletedEvent toolDone:
                    var succeeded = toolDone.Payload.Outcome == ToolOutcome.Succeeded;
                    return Said(ActivityKind.Tool, toolNames.TryGetValue(ToolKey(e, toolDone.Payload.ToolCallId), out var known)
                        ? Ours(succeeded ? "Succeeded: " : "Failed: ") + LineWords.TheirName(known)
                        : Ours(succeeded ? "A tool succeeded" : "A tool failed"));
                case RuntimeTestRunStartedEvent tests:
                    return Said(ActivityKind.TestRun, LineWords.Theirs(tests.Payload.Label) is { IsEmpty: false } label ? Ours("Tests started: ") + label : Ours("Tests started"));
                case RuntimeTestRunCompletedEvent testsDone:
                    var ran = Ours("Tests " + TestWord(testsDone.Payload.Outcome));
                    return Said(ActivityKind.TestRun, LineWords.Theirs(testsDone.Payload.Summary) is { IsEmpty: false } summary ? ran + Ours(": ") + summary : ran);
                case RuntimeApprovalRequestedEvent approval:
                    return Said(ActivityKind.Approval, approval.Payload.Subject is ToolUseSubject subject
                        ? Ours("Approval requested to use ") + LineWords.TheirName(subject.ToolName) + Ours(": ") + LineWords.Theirs(subject.Summary)
                        : Ours("Approval requested"));
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
                    if (commandFailed.Payload.Failure.Effect == FailureEffect.Unknown) return Entry(ActivityKind.Command, WorkspaceText.NotSure(commandFailed.Payload.CommandType));
                    var failedTo = VerbOf(commandFailed.Payload.CommandType) is string verb ? "Couldn't " + verb : "A request failed";
                    // A start that failed never ran, so there is no work to look at.
                    var running = commandFailed.Payload.CommandType != CommandType.ExecutionStart;
                    return Entry(ActivityKind.Command, WorkspaceText.WhyFailed(commandFailed.Payload.Failure.Code, running) is string because ? StateLanguage.Lead(failedTo, because) : failedTo);
                default:
                    // Command completion adds nothing a person needs to read here,
                    // and the model a runtime reports using is on the execution itself.
                    return null;
            }
        }

        /// <summary>Tool call ids are native and only unique within one execution.</summary>
        private static string ToolKey(EventEnvelope e, string toolCallId) => e.ExecutionId + "\n" + toolCallId;

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

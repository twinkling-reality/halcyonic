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
        /// <summary>An entry given as a string: the agent's words by LabelText.Name's rule, or Halcyonic's, spelled by LabelText.Plain first.</summary>
        public ActivityEntry(long position, string occurredAt, ActivityKind kind, string text, bool reported)
            : this(position, occurredAt, kind, reported ? LineWords.TheirName(text) : LineWords.Written(LabelText.Plain(text)), reported)
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
    /// <see cref="IEventHistory"/>. It keeps every line, up to the capacity, of the executions most
    /// recently recorded or read, and of the rest only the last few and the last one that is not a
    /// round's, which a peek shows; opening a file reads its history again (U6). Not thread safe; use
    /// it from one thread.
    /// </summary>
    public sealed class ActivityLog
    {
        /// <summary>The entry a turn's start makes: where one turn's words begin (<see cref="KitPresenter.TurnReportOf"/>).</summary>
        public const string RoundStarted = "Round Started";

        private readonly int capacity;
        private readonly int keptWhole;
        private readonly int tail;
        private readonly Dictionary<string, List<ActivityEntry>> entries = new Dictionary<string, List<ActivityEntry>>();
        private readonly Dictionary<string, string> toolNames = new Dictionary<string, string>();
        private readonly Dictionary<string, string> runtimeNames = new Dictionary<string, string>();
        // What the computer observed of each execution's folder, turn by turn, in journal order.
        private readonly Dictionary<string, List<ObservedTurn>> observed = new Dictionary<string, List<ObservedTurn>>();
        private const int ObservedKept = 16;
        // The executions whose lines are all kept, the least recently recorded or read first.
        private readonly LinkedList<string> recent = new LinkedList<string>();
        private readonly Dictionary<string, LinkedListNode<string>> recentNodes = new Dictionary<string, LinkedListNode<string>>();

        public ActivityLog(int capacityPerExecution = 200, int executionsKeptWhole = 32, int linesKeptOtherwise = 3)
        {
            capacity = capacityPerExecution;
            keptWhole = executionsKeptWhole;
            tail = linesKeptOtherwise;
        }

        /// <summary>The execution's activity, oldest first. Reading it keeps all of it.</summary>
        public IReadOnlyList<ActivityEntry> For(string executionId)
        {
            if (!entries.TryGetValue(executionId, out var list)) return new ActivityEntry[0];
            Touch(executionId);
            return list;
        }

        /// <summary>
        /// What the computer observed of the execution's folder (<c>execution.files.observed</c>), a turn each, oldest first:
        /// the last <see cref="ObservedKept"/> of the executions kept whole, and of the rest the last.
        /// </summary>
        public IReadOnlyList<ObservedTurn> ObservedFor(string executionId) =>
            observed.TryGetValue(executionId, out var turns) ? turns : (IReadOnlyList<ObservedTurn>)new ObservedTurn[0];

        /// <summary>Tool calls whose names wait for their completion line: started and not yet completed.</summary>
        public int OpenToolCalls => toolNames.Count;

        /// <summary>Adds events in journal order, skipping ones already recorded. Returns the executions that changed.</summary>
        public ISet<string> Record(IEnumerable<StoredEvent> events)
        {
            var changed = new HashSet<string>();
            foreach (var stored in events)
            {
                var executionId = stored.Event.ExecutionId;
                if (executionId == null) continue;
                if (stored.Event is ExecutionFilesObservedEvent files)
                {
                    if (Observe(executionId, stored, files.Payload)) { changed.Add(executionId); Touch(executionId); }
                    continue;
                }
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
                Touch(executionId);
            }
            KeepWithinBound();
            return changed;
        }

        /// <summary>Forgets everything, for example when the session moves to another journal.</summary>
        public void Clear()
        {
            entries.Clear();
            toolNames.Clear();
            runtimeNames.Clear();
            observed.Clear();
            recent.Clear();
            recentNodes.Clear();
        }

        private void Touch(string executionId)
        {
            if (recentNodes.TryGetValue(executionId, out var node))
            {
                recent.Remove(node);
            }
            else
            {
                node = new LinkedListNode<string>(executionId);
                recentNodes[executionId] = node;
            }
            recent.AddLast(node);
        }

        /// <summary>Past the executions kept whole, the least recently recorded or read keeps only its tail.</summary>
        private void KeepWithinBound()
        {
            while (recent.Count > keptWhole)
            {
                var oldest = recent.First!.Value;
                recent.RemoveFirst();
                recentNodes.Remove(oldest);
                if (entries.TryGetValue(oldest, out var list)) KeepTail(list);
                if (observed.TryGetValue(oldest, out var turns) && turns.Count > 1) turns.RemoveRange(0, turns.Count - 1);
            }
        }

        /// <summary>Keeps an observed turn in journal order, once. Returns whether it was new.</summary>
        private bool Observe(string executionId, StoredEvent stored, FilesObserved files)
        {
            if (!observed.TryGetValue(executionId, out var turns))
            {
                turns = new List<ObservedTurn>();
                observed[executionId] = turns;
            }
            var at = turns.Count;
            while (at > 0 && turns[at - 1].Position >= stored.Position)
            {
                if (turns[at - 1].Position == stored.Position) return false;
                at--;
            }
            turns.Insert(at, new ObservedTurn(stored.Position, stored.Event.OccurredAt, files));
            if (turns.Count > ObservedKept) turns.RemoveAt(0);
            return true;
        }

        /// <summary>The last lines, and before them the last one that is not a round's when they are all rounds'.</summary>
        private void KeepTail(List<ActivityEntry> list)
        {
            if (list.Count <= tail) return;
            var start = list.Count - tail;
            var said = list.FindLastIndex(entry => entry.Kind != ActivityKind.Turn);
            var kept = said >= 0 && said < start ? list[said] : null;
            list.RemoveRange(0, start);
            if (kept != null) list.Insert(0, kept);
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
                    if (e.ExecutionId != null) runtimeNames[e.ExecutionId] = CaptureNames.AgentName(created.Payload.Runtime.DisplayName);
                    // Theirs last, so a name never stands where Halcyonic's words do (settled 2026-10-08).
                    return Said(ActivityKind.Lifecycle, Ours("Start Requested · ") + LineWords.TheirName(CaptureNames.AgentName(created.Payload.Runtime.DisplayName)));
                case RuntimeExecutionStartedEvent _:
                    // Started only once the runtime says so.
                    return Said(ActivityKind.Lifecycle, e.ExecutionId != null && runtimeNames.TryGetValue(e.ExecutionId, out var runtime)
                        ? Ours("Started · ") + LineWords.TheirName(runtime)
                        : Ours("Started"));
                case ExecutionStartFailedEvent failed:
                    // The message is the agent app's or the control plane's own; a folder's problem is said by its code.
                    return Entry(ActivityKind.Lifecycle, WorkspaceText.WhyFailed(failed.Payload.Error.Code, running: false) is string why
                        ? StateLanguage.Lead(StateLanguage.StartFailed, why)
                        : StateLanguage.StartFailed);
                case ExecutionStateUnknownEvent unknown:
                    // Why, by its code alone, as the character says it: the message is a diagnostic.
                    return Entry(ActivityKind.Connection, StateLanguage.CantTell(StateLanguage.CantTellWhy(StateUnknownCodeOf(unknown.Payload.Code))));
                case RuntimeTurnStartedEvent _:
                    return Entry(ActivityKind.Turn, RoundStarted);
                case RuntimeTurnCompletedEvent _:
                    return Entry(ActivityKind.Turn, "Round Ended");
                case RuntimeTurnFailedEvent _:
                    // The error is the agent app's own, naming it, a request or a path: never shown.
                    return Entry(ActivityKind.Turn, StateLanguage.RoundFailed);
                case RuntimeTurnInterruptedEvent _:
                    return Entry(ActivityKind.Turn, "Round Stopped");
                case RuntimeAgentMessageEvent message:
                    // The agent's prose reads without Markdown's marks (DEVICE's headset frames, 2026-11-04); a message
                    // that is only a fence has no prose, so it reads as written rather than as no words (AUDIT).
                    var prose = LabelText.WithoutMarks(message.Payload.Text);
                    return Said(ActivityKind.Message, LineWords.TheirName(string.IsNullOrWhiteSpace(prose) ? message.Payload.Text : prose), reported: true);
                case RuntimeToolStartedEvent tool:
                    toolNames[ToolKey(e, tool.Payload.ToolCallId)] = tool.Payload.ToolName;
                    // Halcyonic's word first, so a tool named "Approved" never reads as Halcyonic's own entry (settled 2026-10-08).
                    var used = Ours("Tool · ") + ToolWords(tool.Payload.ToolName);
                    return Said(ActivityKind.Tool, LineWords.Theirs(tool.Payload.Title) is { IsEmpty: false } title ? used + Ours(" · ") + title : used);
                case RuntimeToolCompletedEvent toolDone:
                    var succeeded = toolDone.Payload.Outcome == ToolOutcome.Succeeded;
                    // The completion line is the name's last use, so the name goes with it (U6).
                    return Said(ActivityKind.Tool, toolNames.Remove(ToolKey(e, toolDone.Payload.ToolCallId), out var known)
                        ? Ours(succeeded ? "Succeeded · " : "Failed · ") + ToolWords(known) + Ours(" · Reported")
                        : Ours(succeeded ? "Tool Succeeded · Reported" : "Tool Failed · Reported"));
                case RuntimeTestRunStartedEvent tests:
                    return Said(ActivityKind.TestRun, LineWords.Theirs(tests.Payload.Label) is { IsEmpty: false } label ? Ours("Tests Started · ") + label : Ours("Tests Started"));
                case RuntimeTestRunCompletedEvent testsDone:
                    var ran = Ours("Tests " + TestWord(testsDone.Payload.Outcome));
                    return Said(ActivityKind.TestRun, (LineWords.Theirs(testsDone.Payload.Summary) is { IsEmpty: false } summary ? ran + Ours(" · ") + summary : ran) + Ours(" · Reported"));
                case RuntimeApprovalRequestedEvent approval:
                    return Said(ActivityKind.Approval, approval.Payload.Subject is ToolUseSubject subject
                        ? Ours("Approval Requested · ") + ToolWords(subject.ToolName) + Ours(" · ") + LineWords.Theirs(CaptureNames.Request(subject.Summary))
                        : Ours("Approval Requested"));
                case RuntimeApprovalResolvedEvent resolved:
                    return Entry(ActivityKind.Approval, resolved.Payload.Decision == ApprovalResolution.Approved ? "Approved" : "Denied");
                case RuntimeConnectionLostEvent lost:
                    // The reason is the agent app's diagnostic, naming it, a request or a path: never shown.
                    return Entry(ActivityKind.Connection, StateLanguage.LostTouch);
                case CommandAcceptedEvent accepted:
                    // A client's name is not a person's, so it is not shown as one; a command without plain words is left out.
                    return Verb(accepted.Payload.Command) is string asked ? Entry(ActivityKind.Command, asked + " Sent") : null;
                case CommandRejectedEvent rejected:
                    // Why, by the refusal's code: its message is the control plane's own, for developers.
                    return Entry(ActivityKind.Command, WorkspaceText.Couldnt(Verb(rejected.Payload.Command) is string refused ? refused + " Refused" : "Request Refused",
                        WorkspaceText.WhyRefused(rejected.Payload.Rejection.Code)));
                case CommandFailedEvent commandFailed:
                    // The failure's message is the agent app's or the control plane's own: what failed, and
                    // whether it may have happened anyway, are what a person can act on. An effect that can't
                    // be ruled out is never said as "Couldn't", which says nothing happened.
                    if (commandFailed.Payload.Failure.Effect == FailureEffect.Unknown) return Entry(ActivityKind.Command, WorkspaceText.NotSure(commandFailed.Payload.CommandType));
                    var failedTo = VerbOf(commandFailed.Payload.CommandType) is string verb ? verb + " Failed" : "Request Failed";
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
            ExecutionRespondToApprovalCommand respond => respond.Payload.Decision == ApprovalDecision.Approve ? "Approval" : "Denial",
            ExecutionInterruptCommand _ => "Stop",
            ExecutionSendInstructionCommand _ => "Instruction",
            ExecutionStartCommand _ => "Start",
            ExecutionAnswerQuestionCommand _ => "Answer",
            _ => null,
        };

        /// <summary>What a command of this type asks for, in plain words, or null for one that has none yet.</summary>
        private static string? VerbOf(CommandType type) => type switch
        {
            CommandType.ExecutionRespondToApproval => "Decision",
            CommandType.ExecutionInterrupt => "Stop",
            CommandType.ExecutionSendInstruction => "Instruction",
            CommandType.ExecutionStart => "Start",
            CommandType.ExecutionAnswerQuestion => "Answer",
            _ => null,
        };

        /// <summary>
        /// A tool's name as Activity says it: a runtime's internal id for its own kind of item (Codex's) as a person's word,
        /// Halcyonic's; any other name as the agent gave it, theirs (JUDGE's frames, 2026-11-04).
        /// </summary>
        private static LineWords ToolWords(string name) => name switch
        {
            "commandExecution" => LineWords.Written("Command"),
            "fileChange" => LineWords.Written("File Change"),
            _ => LineWords.TheirName(name),
        };

        private static string TestWord(TestOutcome outcome) => outcome switch
        {
            TestOutcome.Passed => "Passed",
            TestOutcome.Failed => "Failed",
            _ => "Errored",
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

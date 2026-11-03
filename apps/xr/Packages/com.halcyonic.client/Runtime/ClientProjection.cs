#nullable enable
using System.Collections.Generic;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The client's copy of control plane state. It changes only by applying a snapshot or the
    /// entity changes an event carries, and never derives state itself
    /// (docs/internal/architecture/REALTIME.md); past the last <see cref="FinishedCommandsKept"/>
    /// finished commands it forgets the one that finished longest ago, as a snapshot leaves it out.
    /// Not thread safe; use it from one thread.
    /// </summary>
    public sealed class ClientProjection
    {
        /// <summary>The finished commands kept, as many as the control plane's snapshot carries.</summary>
        public const int FinishedCommandsKept = 50;

        private readonly Dictionary<string, ProjectView> projects = new Dictionary<string, ProjectView>();
        private readonly Dictionary<string, WorkstreamView> workstreams = new Dictionary<string, WorkstreamView>();
        private readonly Dictionary<string, ExecutionView> executions = new Dictionary<string, ExecutionView>();
        private readonly Dictionary<string, CommandView> commands = new Dictionary<string, CommandView>();
        // Finished commands, the one that finished longest ago first.
        private readonly Queue<string> finished = new Queue<string>();
        private readonly Dictionary<string, ProjectMadeFromInfo> madeFrom = new Dictionary<string, ProjectMadeFromInfo>();
        private readonly List<RuntimeDescriptor> runtimes = new List<RuntimeDescriptor>();
        private readonly Dictionary<CommandType, PolicyCategory> policies = new Dictionary<CommandType, PolicyCategory>();

        /// <summary>The journal the state comes from, or null before the first snapshot.</summary>
        public JournalInfo? Journal { get; private set; }

        /// <summary>The journal position the state reflects.</summary>
        public long Position { get; private set; }

        public IReadOnlyDictionary<string, ProjectView> Projects => projects;

        public IReadOnlyDictionary<string, WorkstreamView> Workstreams => workstreams;

        public IReadOnlyDictionary<string, ExecutionView> Executions => executions;

        /// <summary>
        /// Pending commands and the last <see cref="FinishedCommandsKept"/> to finish, so a command
        /// that has just finished is still here for whatever watches it (U6).
        /// </summary>
        public IReadOnlyDictionary<string, CommandView> Commands => commands;

        /// <summary>Provenance recorded by <c>project.made_from</c> events, keyed by the new VR project's id.</summary>
        public IReadOnlyDictionary<string, ProjectMadeFromInfo> MadeFrom => madeFrom;

        /// <summary>Returns the <c>project.made_from</c> provenance for <paramref name="projectId"/>, or null when none was recorded.</summary>
        public ProjectMadeFromInfo? MadeFromOf(string projectId) =>
            madeFrom.TryGetValue(projectId, out var info) ? info : null;

        public IReadOnlyList<RuntimeDescriptor> Runtimes => runtimes;

        /// <summary>The consequence category of each command type, from the control plane's welcome.</summary>
        public IReadOnlyDictionary<CommandType, PolicyCategory> CommandPolicies => policies;

        /// <summary>
        /// Whether sending this command type needs an explicit, deliberate action. Unknown policies
        /// count as needing one.
        /// </summary>
        public bool RequiresConfirmation(CommandType commandType) =>
            !policies.TryGetValue(commandType, out var policy) || policy != PolicyCategory.LowConsequence;

        /// <summary>Records what the control plane said when this connection was established.</summary>
        public void ApplyWelcome(WelcomeMessage welcome)
        {
            policies.Clear();
            foreach (var entry in welcome.CommandPolicies) policies[entry.CommandType] = entry.Policy;
        }

        /// <summary>The execution a workstream reports, if it has one.</summary>
        public ExecutionView? CurrentExecution(WorkstreamView workstream)
        {
            return workstream.CurrentExecutionId != null
                && executions.TryGetValue(workstream.CurrentExecutionId, out var execution)
                ? execution
                : null;
        }

        /// <summary>The runtime an execution runs on, if the control plane still offers it.</summary>
        public RuntimeDescriptor? RuntimeOf(ExecutionView execution)
        {
            foreach (var runtime in runtimes)
            {
                if (runtime.RuntimeId == execution.Runtime.RuntimeId) return runtime;
            }
            return null;
        }

        /// <summary>Replaces the whole state.</summary>
        public void ApplySnapshot(Snapshot snapshot, StateChanges changes)
        {
            var sameJournalForward = Journal != null && Journal.JournalId == snapshot.Journal.JournalId && snapshot.Position >= Position;
            if (Journal != null && Journal.JournalId == snapshot.Journal.JournalId && snapshot.Position < Position)
            {
                changes.Rewound = true;
            }
            Journal = snapshot.Journal;
            Position = snapshot.Position;
            Replace(projects, snapshot.Projects, project => project.ProjectId);
            if (!sameJournalForward)
            {
                madeFrom.Clear();
            }
            else if (madeFrom.Count > 0)
            {
                var stale = new List<string>();
                foreach (var key in madeFrom.Keys)
                {
                    if (!projects.ContainsKey(key)) stale.Add(key);
                }
                foreach (var key in stale) madeFrom.Remove(key);
            }
            Replace(workstreams, snapshot.Workstreams, workstream => workstream.WorkstreamId);
            Replace(executions, snapshot.Executions, execution => execution.ExecutionId);
            Replace(commands, snapshot.Commands, command => command.CommandId);
            finished.Clear();
            foreach (var command in snapshot.Commands)
            {
                if (command.Status != CommandStatus.Accepted) finished.Enqueue(command.CommandId);
            }
            runtimes.Clear();
            runtimes.AddRange(snapshot.Runtimes);
            changes.Resynchronized = true;
        }

        /// <summary>
        /// Applies the entity changes of one event. Returns false, and changes nothing, for an event
        /// at or before the current position or one that arrives before any snapshot.
        /// </summary>
        public bool ApplyEvent(EventMessage message, StateChanges changes)
        {
            if (Journal == null || message.Position <= Position) return false;
            Position = message.Position;
            foreach (var project in message.Changes.Projects)
            {
                projects[project.ProjectId] = project;
                changes.Projects.Add(project.ProjectId);
            }
            if (message.Event is ProjectMadeFromEvent madeFromEvent
                && !string.IsNullOrEmpty(madeFromEvent.ProjectId)
                && madeFromEvent.Payload != null)
            {
                madeFrom[madeFromEvent.ProjectId] = new ProjectMadeFromInfo(
                    madeFromEvent.ProjectId,
                    madeFromEvent.Payload.OriginalProjectId,
                    madeFromEvent.Payload.Instruction);
                changes.Projects.Add(madeFromEvent.ProjectId);
            }
            foreach (var workstream in message.Changes.Workstreams)
            {
                workstreams[workstream.WorkstreamId] = workstream;
                changes.Workstreams.Add(workstream.WorkstreamId);
            }
            foreach (var execution in message.Changes.Executions)
            {
                executions[execution.ExecutionId] = execution;
                changes.Executions.Add(execution.ExecutionId);
            }
            foreach (var command in message.Changes.Commands)
            {
                var finishedBefore = commands.TryGetValue(command.CommandId, out var before) && before.Status != CommandStatus.Accepted;
                commands[command.CommandId] = command;
                changes.Commands.Add(command.CommandId);
                if (!finishedBefore && command.Status != CommandStatus.Accepted) finished.Enqueue(command.CommandId);
            }
            // A long connection keeps what a snapshot would: pending commands and the last to finish (U6).
            while (finished.Count > FinishedCommandsKept) commands.Remove(finished.Dequeue());
            changes.Events.Add(new StoredEvent { Position = message.Position, Event = message.Event });
            return true;
        }

        private static void Replace<T>(Dictionary<string, T> target, List<T> source, System.Func<T, string> key)
        {
            target.Clear();
            foreach (var item in source) target[key(item)] = item;
        }
    }

    /// <summary>What applying a batch of messages changed, for incremental presentation updates.</summary>
    public sealed class StateChanges
    {
        /// <summary>A snapshot replaced the whole state, so everything should be redrawn.</summary>
        public bool Resynchronized { get; internal set; }

        /// <summary>
        /// A snapshot took the state back to an earlier position of the same journal, as the recorded
        /// demonstration does each time it starts again: activity and commands seen after that
        /// position no longer apply, although the journal is the same. A control plane never does this.
        /// </summary>
        public bool Rewound { get; internal set; }

        /// <summary>The connection status changed.</summary>
        public bool ConnectionChanged { get; internal set; }

        public ISet<string> Projects { get; } = new HashSet<string>();

        public ISet<string> Workstreams { get; } = new HashSet<string>();

        public ISet<string> Executions { get; } = new HashSet<string>();

        public ISet<string> Commands { get; } = new HashSet<string>();

        /// <summary>The events applied, with their journal positions, in order.</summary>
        public IList<StoredEvent> Events { get; } = new List<StoredEvent>();

        /// <summary>Errors the control plane reported about messages this client sent.</summary>
        public IList<ErrorBody> ServerErrors { get; } = new List<ErrorBody>();

        public bool IsEmpty =>
            !Resynchronized && !ConnectionChanged && Events.Count == 0 && ServerErrors.Count == 0;
    }

    /// <summary>
    /// Provenance recorded when a VR starter project is created from an existing project by
    /// <c>project.make_vr_version</c> (<see cref="ProjectMadeFromEvent"/>).
    /// </summary>
    public sealed class ProjectMadeFromInfo
    {
        public ProjectMadeFromInfo(string projectId, string originalProjectId, string instruction)
        {
            ProjectId = projectId;
            OriginalProjectId = originalProjectId;
            Instruction = instruction;
        }

        public string ProjectId { get; }

        public string OriginalProjectId { get; }

        public string Instruction { get; }

        public string MadeFromProjectId => OriginalProjectId;

        public string MadeFromInstruction => Instruction;
    }
}

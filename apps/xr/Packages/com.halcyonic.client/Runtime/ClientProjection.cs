#nullable enable
using System.Collections.Generic;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The client's copy of control plane state. It changes only by applying a snapshot or the
    /// entity changes an event carries, and never derives state itself
    /// (docs/internal/architecture/REALTIME.md). Not thread safe; use it from one thread.
    /// </summary>
    public sealed class ClientProjection
    {
        private readonly Dictionary<string, ProjectView> projects = new Dictionary<string, ProjectView>();
        private readonly Dictionary<string, WorkstreamView> workstreams = new Dictionary<string, WorkstreamView>();
        private readonly Dictionary<string, ExecutionView> executions = new Dictionary<string, ExecutionView>();
        private readonly Dictionary<string, CommandView> commands = new Dictionary<string, CommandView>();
        private readonly List<RuntimeDescriptor> runtimes = new List<RuntimeDescriptor>();

        /// <summary>The journal the state comes from, or null before the first snapshot.</summary>
        public JournalInfo? Journal { get; private set; }

        /// <summary>The journal position the state reflects.</summary>
        public long Position { get; private set; }

        public IReadOnlyDictionary<string, ProjectView> Projects => projects;

        public IReadOnlyDictionary<string, WorkstreamView> Workstreams => workstreams;

        public IReadOnlyDictionary<string, ExecutionView> Executions => executions;

        /// <summary>Pending commands and recently finished ones.</summary>
        public IReadOnlyDictionary<string, CommandView> Commands => commands;

        public IReadOnlyList<RuntimeDescriptor> Runtimes => runtimes;

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
            Journal = snapshot.Journal;
            Position = snapshot.Position;
            Replace(projects, snapshot.Projects, project => project.ProjectId);
            Replace(workstreams, snapshot.Workstreams, workstream => workstream.WorkstreamId);
            Replace(executions, snapshot.Executions, execution => execution.ExecutionId);
            Replace(commands, snapshot.Commands, command => command.CommandId);
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
                commands[command.CommandId] = command;
                changes.Commands.Add(command.CommandId);
            }
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
}

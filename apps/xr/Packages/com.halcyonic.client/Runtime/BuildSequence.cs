#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    public enum BuildStepKind
    {
        CreateProject,
        CreateWorkstream,
        StartWork,
    }

    public enum BuildStepStatus
    {
        /// <summary>Not sent yet: it waits for the step before it.</summary>
        NotYet,

        /// <summary>Sent; the control plane's result has not arrived.</summary>
        Waiting,

        /// <summary>The control plane recorded it completed, as the step expects.</summary>
        Confirmed,

        /// <summary>The control plane refused it; nothing happened.</summary>
        Refused,

        /// <summary>It failed; whether it had an effect is in <see cref="BuildStep.EffectUnknown"/>.</summary>
        Failed,

        /// <summary>Its acknowledgement was lost and no record has arrived: it may have run.</summary>
        Unknown,

        /// <summary>The session was not connected, so it never left the headset.</summary>
        NotSent,

        /// <summary>It completed with a result the step did not expect.</summary>
        Unexpected,
    }

    /// <summary>One step of Start building and how it went.</summary>
    public sealed class BuildStep
    {
        internal BuildStep(BuildStepKind kind) => Kind = kind;

        public BuildStepKind Kind { get; }

        public BuildStepStatus Status { get; internal set; }

        /// <summary>The control plane's reason for a refusal or failure, as it gave it.</summary>
        public string? Reason { get; internal set; }

        /// <summary>A failure whose effect the control plane could not rule out.</summary>
        public bool EffectUnknown { get; internal set; }
    }

    /// <summary>
    /// Start building: the ordinary commands, sent one at a time, each only once the control plane
    /// recorded the one before it completed: <c>project.create</c> for a new project,
    /// <c>workstream.create</c>, then <c>execution.start</c>. A projected record wins over a missing
    /// or lost acknowledgement (<see cref="NewWorkSubmission"/>). A refusal or failure stops the
    /// sequence and says why. While a command's outcome is unknown, or it failed with an effect that
    /// cannot be ruled out, or completed with an unexpected result, its id stays in
    /// <see cref="Unresolved"/>, and nothing more is sent: a blind retry could start the same work
    /// twice. Accepted is not done: a step is confirmed only by its completed record.
    /// </summary>
    public sealed class BuildSequence
    {
        private readonly NewWorkDraft draft;
        private readonly CommandFactory commands;
        private readonly List<BuildStep> steps = new List<BuildStep>();
        private string? newProjectName;
        private int index;

        /// <param name="draft">The runtime, model and first task, with its project unless one is created.</param>
        /// <param name="newProjectName">The project to create first, or null to add the work to the draft's project.</param>
        public BuildSequence(NewWorkDraft draft, CommandFactory commands, string? newProjectName)
        {
            this.draft = draft ?? throw new ArgumentNullException(nameof(draft));
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
            this.newProjectName = newProjectName;
            if (newProjectName != null) steps.Add(new BuildStep(BuildStepKind.CreateProject));
            steps.Add(new BuildStep(BuildStepKind.CreateWorkstream));
            steps.Add(new BuildStep(BuildStepKind.StartWork));
        }

        public IReadOnlyList<BuildStep> Steps => steps;

        /// <summary>The command in flight, or null before the first and after the sequence ends.</summary>
        public NewWorkSubmission? Current { get; private set; }

        /// <summary>The project the work belongs to, once known.</summary>
        public string? ProjectId => draft.ProjectId;

        /// <summary>The workstream created, once confirmed.</summary>
        public string? WorkstreamId { get; private set; }

        /// <summary>Every step was confirmed: the runtime confirmed the start.</summary>
        public bool Started { get; private set; }

        /// <summary>A step was refused, failed, not sent, or ended unexpectedly; nothing more is sent.</summary>
        public bool Stopped { get; private set; }

        /// <summary>
        /// The id of the command that may have run without this headset knowing its result. While
        /// set, the entry panel keeps it on the device and offers no other start.
        /// </summary>
        public string? Unresolved { get; private set; }

        /// <summary>The first command to send.</summary>
        public CommandEnvelope Begin()
        {
            if (index != 0 || Current != null) throw new InvalidOperationException("The sequence has begun.");
            return Send(newProjectName != null ? commands.CreateProject(newProjectName) : draft.CreateWorkstream());
        }

        /// <summary>
        /// Whether the stopped step can be sent again: it was refused, failed with no effect, or was
        /// never sent, so nothing of it can have run.
        /// </summary>
        public bool CanRetry => Stopped && Unresolved == null && index < steps.Count;

        /// <summary>
        /// Sends the stopped step again as a new command built from the draft as it is now, for
        /// example after the person chose another runtime; <paramref name="projectName"/> replaces the
        /// name of a project not created yet.
        /// </summary>
        public CommandEnvelope Retry(string? projectName = null)
        {
            if (!CanRetry) throw new InvalidOperationException("Only a step that cannot have run is sent again.");
            if (projectName != null) newProjectName = projectName;
            Stopped = false;
            var step = steps[index];
            step.Reason = null;
            step.EffectUnknown = false;
            return Send(step.Kind switch
            {
                BuildStepKind.CreateProject => commands.CreateProject(newProjectName!),
                BuildStepKind.CreateWorkstream => draft.CreateWorkstream(),
                _ => draft.StartExecution(WorkstreamId!),
            });
        }

        /// <summary>The acknowledgement of the command in flight.</summary>
        public void Acknowledged(CommandAckMessage ack) => Current?.Acknowledge(ack);

        /// <summary>The command in flight got no acknowledgement: not sent, or with an unknown outcome.</summary>
        public void AcknowledgementLost(Exception error) => Current?.LostAcknowledgement(error);

        /// <summary>
        /// Brings the step in flight up to date with the projected state and what the session
        /// reported. Returns the next command to send once the step was confirmed, else null.
        /// </summary>
        public CommandEnvelope? Advance(ClientProjection? state)
        {
            var current = Current;
            if (current == null) return null;
            if (state != null && state.Commands.TryGetValue(current.Command.CommandId, out var projected)) current.Observe(projected);
            var step = steps[index];
            switch (current.State)
            {
                case NewWorkSubmissionState.Waiting:
                    step.Status = BuildStepStatus.Waiting;
                    return null;
                case NewWorkSubmissionState.OutcomeUnknown:
                    step.Status = BuildStepStatus.Unknown;
                    return null;
                case NewWorkSubmissionState.NotSent:
                    step.Status = BuildStepStatus.NotSent;
                    Unresolved = null;
                    return Stop();
                case NewWorkSubmissionState.Rejected:
                    step.Status = BuildStepStatus.Refused;
                    step.Reason = current.EffectiveRecord?.Rejection?.Message;
                    Unresolved = null;
                    return Stop();
                case NewWorkSubmissionState.Failed:
                    var failure = current.EffectiveRecord?.Failure;
                    step.Status = BuildStepStatus.Failed;
                    step.Reason = failure?.Message;
                    step.EffectUnknown = failure?.Effect == FailureEffect.Unknown;
                    if (!step.EffectUnknown) Unresolved = null;
                    return Stop();
            }
            if (!current.HasExpectedResult)
            {
                step.Status = BuildStepStatus.Unexpected;
                return Stop();
            }
            step.Status = BuildStepStatus.Confirmed;
            Unresolved = null;
            index++;
            switch (current.EffectiveRecord!.Result)
            {
                case ProjectCreatedResult project:
                    draft.ProjectId = project.ProjectId;
                    return Send(draft.CreateWorkstream());
                case WorkstreamCreatedResult workstream:
                    WorkstreamId = workstream.WorkstreamId;
                    return Send(draft.StartExecution(workstream.WorkstreamId));
                default:
                    Current = null;
                    Started = true;
                    return null;
            }
        }

        private CommandEnvelope Send(CommandEnvelope command)
        {
            Current = new NewWorkSubmission(command);
            Unresolved = command.CommandId;
            steps[index].Status = BuildStepStatus.Waiting;
            return command;
        }

        private CommandEnvelope? Stop()
        {
            Current = null;
            Stopped = true;
            return null;
        }
    }
}

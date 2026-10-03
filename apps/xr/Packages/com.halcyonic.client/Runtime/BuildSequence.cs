#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    public enum BuildStepKind
    {
        CreateProject,

        /// <summary><c>project.set_location</c>: an existing project bound to the folder the person chose.</summary>
        BindFolder,
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

        /// <summary>The control plane's reason for a refusal or failure, as it gave it: text from outside.</summary>
        public string? Reason { get; internal set; }

        /// <summary>The refusal's code, which decides the words and the next action offered.</summary>
        public RejectionCode? Refusal { get; internal set; }

        /// <summary>The failure's code, as the control plane gave it, such as <c>location_not_created</c>.</summary>
        public string? Failure { get; internal set; }

        /// <summary>A failure whose effect the control plane could not rule out.</summary>
        public bool EffectUnknown { get; internal set; }
    }

    /// <summary>
    /// Start building: the ordinary commands, sent one at a time, each only once the control plane
    /// recorded the one before it completed: <c>project.create</c> for a new project, with the folder
    /// the person chose, or <c>project.set_location</c> first when an existing project is to work in
    /// another folder; then <c>workstream.create</c> and <c>execution.start</c>. A projected record wins
    /// over a missing or lost acknowledgement (<see cref="NewWorkSubmission"/>). A refusal or failure
    /// stops the sequence and says why, keeping its code. While a command's outcome is unknown, or it
    /// failed with an effect that cannot be ruled out, or completed with an unexpected result, its id
    /// stays in <see cref="Unresolved"/>, and nothing more is sent: a blind retry could start the same
    /// work twice. Accepted is not done: a step is confirmed only by its completed record.
    /// </summary>
    public sealed class BuildSequence
    {
        private readonly NewWorkDraft draft;
        private readonly CommandFactory commands;
        private readonly List<BuildStep> steps = new List<BuildStep>();
        private string? newProjectName;
        private ProjectLocationChoice? location;
        private int index;

        /// <param name="draft">The runtime, model and first task, with its project unless one is created.</param>
        /// <param name="newProjectName">The project to create first, or null to add the work to the draft's project.</param>
        /// <param name="folder">
        /// Where the project's files live: sent with <c>project.create</c> for a new project, or bound
        /// first with <c>project.set_location</c> for an existing one; null to leave it as it is.
        /// </param>
        public BuildSequence(NewWorkDraft draft, CommandFactory commands, string? newProjectName, ProjectLocationChoice? folder = null)
        {
            this.draft = draft ?? throw new ArgumentNullException(nameof(draft));
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
            this.newProjectName = newProjectName;
            location = folder;
            if (newProjectName != null) steps.Add(new BuildStep(BuildStepKind.CreateProject));
            else if (folder != null) steps.Add(new BuildStep(BuildStepKind.BindFolder));
            steps.Add(new BuildStep(BuildStepKind.CreateWorkstream));
            steps.Add(new BuildStep(BuildStepKind.StartWork));
        }

        public IReadOnlyList<BuildStep> Steps => steps;

        /// <summary>The draft it sends from: the runtime, model and first task as they are now.</summary>
        public NewWorkDraft Draft => draft;

        /// <summary>The project it creates first, or null when the work goes to the draft's project.</summary>
        public string? NewProjectName => newProjectName;

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

        /// <summary>The step that stopped the sequence, or null while it runs or once it started the work.</summary>
        public BuildStep? StoppedAt => Stopped && index < steps.Count ? steps[index] : null;

        /// <summary>
        /// The id of the command that may have run without this headset knowing its result. While
        /// set, the entry panel keeps it on the device and offers no other start.
        /// </summary>
        public string? Unresolved { get; private set; }

        /// <summary>
        /// This sequence has sent a command. One resumed after a restart has not, so it says nothing
        /// about a command whose outcome the device still keeps as unknown, and must never clear it.
        /// </summary>
        public bool Sent { get; private set; }

        /// <summary>
        /// A sequence whose project, and perhaps its task, the Mac already made before the app restarted
        /// (<see cref="CreationDraft"/>): stopped where it was, so Start building sends only what is left
        /// and never makes either twice. Without <paramref name="workstreamId"/> it creates the task and
        /// starts it; with it, it only starts it.
        /// </summary>
        public static BuildSequence Resume(NewWorkDraft draft, CommandFactory commands, string projectId, string? workstreamId)
        {
            draft.ProjectId = projectId ?? throw new ArgumentNullException(nameof(projectId));
            var sequence = new BuildSequence(draft, commands, null);
            if (workstreamId != null)
            {
                sequence.steps[0].Status = BuildStepStatus.Confirmed;
                sequence.WorkstreamId = workstreamId;
                sequence.index = 1;
            }
            sequence.Stopped = true;
            return sequence;
        }

        /// <summary>
        /// The first command to send, on <paramref name="reviewed"/>, read to its end and confirmed:
        /// it must show the project's name, the model and the first task this sends, and is spent.
        /// </summary>
        public CommandEnvelope Begin(NewWorkReview reviewed)
        {
            if (index != 0 || Current != null) throw new InvalidOperationException("The sequence has begun.");
            Confirmed(reviewed, newProjectName);
            return Send(CommandFor(steps[0].Kind));
        }

        /// <summary>
        /// Whether the stopped step can be sent again: it was refused, failed with no effect, or was
        /// never sent, so nothing of it can have run.
        /// </summary>
        public bool CanRetry => Stopped && Unresolved == null && index < steps.Count;

        /// <summary>
        /// Sends the stopped step again as a new command built from the draft as it is now, for
        /// example after the person chose another runtime; <paramref name="projectName"/> replaces the
        /// name of a project not created yet. With <paramref name="folder"/>, a project not created yet
        /// is created there, and a project that exists is bound to it first with
        /// <c>project.set_location</c>, as after <c>location_required</c> or <c>location_missing</c>.
        /// Like the first, it goes only on a fresh <paramref name="reviewed"/>, read to its end and
        /// confirmed, showing what it sends: Try again reads the request again.
        /// </summary>
        public CommandEnvelope Retry(NewWorkReview reviewed, string? projectName = null, ProjectLocationChoice? folder = null)
        {
            if (!CanRetry) throw new InvalidOperationException("Only a step that cannot have run is sent again.");
            Confirmed(reviewed, steps[index].Kind == BuildStepKind.CreateProject ? projectName ?? newProjectName : null);
            if (projectName != null) newProjectName = projectName;
            if (folder != null) location = folder;
            Stopped = false;
            var step = steps[index];
            step.Reason = null;
            step.Refusal = null;
            step.Failure = null;
            step.EffectUnknown = false;
            if (folder != null && step.Kind != BuildStepKind.CreateProject && step.Kind != BuildStepKind.BindFolder)
            {
                // The project exists: bind it to the folder before the step is sent again.
                steps.Insert(index, new BuildStep(BuildStepKind.BindFolder));
            }
            return Send(CommandFor(steps[index].Kind));
        }

        /// <summary>
        /// Holds a send to what the person read: the review shows the new project's name, the model
        /// and the first task as they go, and its final action is taken now, once.
        /// </summary>
        private void Confirmed(NewWorkReview reviewed, string? projectName)
        {
            if (reviewed == null) throw new ArgumentNullException(nameof(reviewed));
            if (projectName != null && !reviewed.Shows(NewWorkReview.ProjectLabel, projectName))
            {
                throw new InvalidOperationException("The project's name is not the one reviewed.");
            }
            if (!reviewed.Shows(NewWorkReview.ModelIdLabel, draft.Model?.ModelRef ?? "none")) throw new InvalidOperationException("The model is not the one reviewed.");
            if (!reviewed.Shows(NewWorkReview.FirstTaskLabel, draft.Objective)) throw new InvalidOperationException("The first task is not the one reviewed.");
            if (!reviewed.Spend()) throw new InvalidOperationException("Only a request read to its end, and confirmed once, is sent.");
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
                    step.Refusal = current.EffectiveRecord?.Rejection?.Code;
                    Unresolved = null;
                    return Stop();
                case NewWorkSubmissionState.Failed:
                    var failure = current.EffectiveRecord?.Failure;
                    step.Status = BuildStepStatus.Failed;
                    step.Reason = failure?.Message;
                    step.Failure = failure?.Code;
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
            switch (current.EffectiveRecord!.Result)
            {
                case ProjectCreatedResult project:
                    draft.ProjectId = project.ProjectId;
                    break;
                case WorkstreamCreatedResult workstream:
                    WorkstreamId = workstream.WorkstreamId;
                    break;
                case ExecutionCreatedResult _:
                    index++;
                    Current = null;
                    Started = true;
                    return null;
            }
            index++;
            return Send(CommandFor(steps[index].Kind));
        }

        private CommandEnvelope CommandFor(BuildStepKind kind) => kind switch
        {
            BuildStepKind.CreateProject => commands.CreateProject(newProjectName!, location),
            BuildStepKind.BindFolder => commands.SetProjectLocation(draft.ProjectId!, location!),
            BuildStepKind.CreateWorkstream => draft.CreateWorkstream(),
            _ => draft.StartExecution(WorkstreamId!),
        };

        private CommandEnvelope Send(CommandEnvelope command)
        {
            Sent = true;
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

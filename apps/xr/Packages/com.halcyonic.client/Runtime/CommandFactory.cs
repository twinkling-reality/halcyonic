#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using Newtonsoft.Json.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// Builds well-formed commands. Each gets a fresh id, so submitting the same object twice is an
    /// idempotent retry, while building a new command is a new request.
    /// </summary>
    public sealed class CommandFactory
    {
        private readonly ClientInfo client;
        private readonly Func<DateTimeOffset> now;

        public CommandFactory(ClientInfo client, Func<DateTimeOffset>? now = null)
        {
            this.client = client;
            this.now = now ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Creates a project in the folder <paramref name="location"/> chooses from what the host lists
        /// (<see cref="ControlPlaneApi.GetLocationsAsync"/>, ADR 0020), or with none: then only a runtime
        /// that does not use the project's location, such as the mock runtime, can start work in it.
        /// </summary>
        public ProjectCreateCommand CreateProject(string name, ProjectLocationChoice? location = null) =>
            Stamp(new ProjectCreateCommand { Payload = new ProjectCreatePayload { Name = name, Location = location } });

        /// <summary>
        /// Binds a project to another folder, for example after its folder was moved or renamed on the
        /// Mac. Work already started keeps the folder it started in.
        /// </summary>
        /// <param name="review">The digest of the location review the person read (ADR 0051), or null for none.</param>
        public ProjectSetLocationCommand SetProjectLocation(string projectId, ProjectLocationChoice location, string? review = null) =>
            Stamp(new ProjectSetLocationCommand
            {
                Payload = new ProjectSetLocationPayload { ProjectId = projectId, Location = location, Review = review },
            });

        /// <summary>
        /// Links the project to one repository of the person's catalog, replacing any link (ADR 0039):
        /// only on the person's own choice, never because the host suggested it.
        /// </summary>
        public ProjectLinkCatalogCommand LinkCatalog(string projectId, long repositoryId, string fullName) =>
            Stamp(new ProjectLinkCatalogCommand
            {
                Payload = new ProjectLinkCatalogPayload
                {
                    ProjectId = projectId,
                    Catalog = new CatalogRepositoryRef { RepositoryId = repositoryId, FullName = fullName },
                },
            });

        public ProjectUnlinkCatalogCommand UnlinkCatalog(string projectId) =>
            Stamp(new ProjectUnlinkCatalogCommand { Payload = new ProjectUnlinkCatalogPayload { ProjectId = projectId } });

        /// <summary>
        /// Makes a VR version of a game (M1.8c-1): a new starter project in <paramref name="folderName"/>
        /// under <paramref name="root"/>, whose agent reads the original's folder and changes nothing
        /// of it. The host refuses it when the original has no folder.
        /// </summary>
        public ProjectMakeVrVersionCommand MakeVrVersion(string originalProjectId, string root, string folderName) =>
            Stamp(new ProjectMakeVrVersionCommand
            {
                Payload = new ProjectMakeVrVersionPayload
                {
                    OriginalProjectId = originalProjectId,
                    Root = root,
                    FolderName = folderName,
                },
            });

        /// <summary>Checks a project made from a starter: type check, build, and the built copy tried (ADR 0037).</summary>
        public ProjectCheckCommand CheckProject(string projectId) =>
            Stamp(new ProjectCheckCommand { Payload = new ProjectCheckPayload { ProjectId = projectId } });

        /// <summary>Serves the built copy of the project's latest passed check on the host's loopback.</summary>
        public ProjectPreviewStartCommand StartPreview(string projectId) =>
            Stamp(new ProjectPreviewStartCommand { Payload = new ProjectPreviewStartPayload { ProjectId = projectId } });

        /// <summary>Adds a task's work, as last read and shown, to its project's folder (ADR 0045; the host refuses a copy changed since).</summary>
        /// <param name="review">The digest of the reading whose files the person was shown (ADR 0051, amended): the host adds only that.</param>
        public WorkstreamAddToProjectCommand AddToProject(string workstreamId, string review) =>
            Stamp(new WorkstreamAddToProjectCommand { Payload = new WorkstreamAddToProjectPayload { WorkstreamId = workstreamId, Review = review } });

        /// <summary>Writes the files both changed, both sides marked, into the task's copy and asks its agent to fit them (ADR 0045).</summary>
        public WorkstreamFitToProjectCommand FitToProject(string workstreamId) =>
            Stamp(new WorkstreamFitToProjectCommand { Payload = new WorkstreamFitToProjectPayload { WorkstreamId = workstreamId } });

        /// <summary>Restores a project's version, carrying the digest of the review the person read (ADR 0051; DEVICE's History).</summary>
        public ProjectRestoreCommand RestoreProject(string projectId, string checkpointId, string review) =>
            Stamp(new ProjectRestoreCommand { Payload = new ProjectRestorePayload { ProjectId = projectId, CheckpointId = checkpointId, Review = review } });

        /// <summary>Shares the project's checked game one way (ADR 0054; DEVICE's Share), asked first behind its lock.</summary>
        public ProjectShareStartCommand StartShare(string projectId, ShareWay way) =>
            Stamp(new ProjectShareStartCommand { Payload = new ProjectShareStartPayload { ProjectId = projectId, Way = way } });

        /// <summary>Stops the project's share at once (ADR 0054); no confirmation.</summary>
        public ProjectShareStopCommand StopShare(string projectId) =>
            Stamp(new ProjectShareStopCommand { Payload = new ProjectShareStopPayload { ProjectId = projectId } });

        public ProjectPreviewStopCommand StopPreview(string projectId) =>
            Stamp(new ProjectPreviewStopCommand { Payload = new ProjectPreviewStopPayload { ProjectId = projectId } });

        /// <summary>
        /// Plays the checked game on the computer, in the host's contained browser, shown by a viewer
        /// the host opens there (P-A). Offer it only as <see cref="ProjectPlayFacts.PlayOnLaptopFor"/>
        /// says; the host may still refuse it. Nothing of the viewer comes back to the client.
        /// </summary>
        public ProjectOpenOnMacCommand OpenOnMac(string projectId) =>
            Stamp(new ProjectOpenOnMacCommand { Payload = new ProjectOpenOnMacPayload { ProjectId = projectId } });

        public WorkstreamCreateCommand CreateWorkstream(string projectId, string title, string? objective) =>
            Stamp(new WorkstreamCreateCommand
            {
                Payload = new WorkstreamCreatePayload { ProjectId = projectId, Title = title, Objective = objective },
            });

        /// <summary>
        /// Starts work on a runtime. <paramref name="modelRef"/> is a model's reference exactly as the
        /// runtime's own list gave it (<see cref="ControlPlaneApi.GetRuntimeModelsAsync"/>), or null to
        /// leave the choice to the runtime; only a runtime whose <c>model_choice</c> is <c>listed</c>
        /// accepts one. <paramref name="approvalLevel"/> is how often the agent stops to ask (ADR 0043): left out it
        /// means ask, as every start before it meant; never ask only for a runtime whose descriptor offers it.
        /// <paramref name="pictures"/> are paths of the project's folder, as its pictures read lists them, that go with
        /// the instruction (ADR 0058): at most four, only for a runtime whose descriptor has <c>takes_pictures</c>;
        /// null or empty sends none.
        /// </summary>
        public ExecutionStartCommand StartExecution(
            string workstreamId,
            string runtimeId,
            string instruction,
            IDictionary<string, JToken>? options = null,
            string? modelRef = null,
            ApprovalLevel? approvalLevel = null,
            IReadOnlyList<string>? pictures = null) =>
            Stamp(new ExecutionStartCommand
            {
                Payload = new ExecutionStartPayload
                {
                    WorkstreamId = workstreamId,
                    RuntimeId = runtimeId,
                    Instruction = instruction,
                    Options = options == null ? new Dictionary<string, JToken>() : new Dictionary<string, JToken>(options),
                    ModelRef = modelRef,
                    ApprovalLevel = approvalLevel,
                    Attachments = AttachmentsOf(pictures),
                },
            });

        /// <summary>
        /// Turns the project's own rules on or off for its tasks (E5). On carries the <paramref name="review"/> the
        /// rules read gave for the text the person read; off needs none.
        /// </summary>
        public ProjectRulesSetCommand SetProjectRules(string projectId, bool on, string? review = null) =>
            Stamp(new ProjectRulesSetCommand
            {
                Payload = new ProjectRulesSetPayload { ProjectId = projectId, On = on, Review = on ? review : null },
            });

        /// <summary>
        /// The person's words for an execution, with any <paramref name="pictures"/> of the project's folder, as on
        /// <see cref="StartExecution"/> (ADR 0058).
        /// </summary>
        public ExecutionSendInstructionCommand SendInstruction(
            string executionId,
            string text,
            IReadOnlyList<string>? pictures = null) =>
            Stamp(new ExecutionSendInstructionCommand
            {
                Payload = new ExecutionSendInstructionPayload
                {
                    ExecutionId = executionId,
                    Text = text,
                    Attachments = AttachmentsOf(pictures),
                },
            });

        /// <summary>Each path as a picture attachment, or null for none, so the field is left out.</summary>
        private static List<InstructionAttachment>? AttachmentsOf(IReadOnlyList<string>? pictures) =>
            pictures == null || pictures.Count == 0
                ? null
                : pictures.Select(path => new InstructionAttachment { Path = path, Kind = "image" }).ToList();

        public ExecutionRespondToApprovalCommand RespondToApproval(
            string executionId,
            string approvalId,
            ApprovalDecision decision,
            string? message = null) =>
            Stamp(new ExecutionRespondToApprovalCommand
            {
                Payload = new ExecutionRespondToApprovalPayload
                {
                    ExecutionId = executionId,
                    ApprovalId = approvalId,
                    Decision = decision,
                    Message = message,
                },
            });

        /// <summary>
        /// Answers a question the agent asked (ADR 0022): one answer per prompt, by the prompt's
        /// <c>Key</c>, with the labels of the options chosen and any typed text. Send it only on a
        /// deliberate press, never on the gesture that returns focus to the app.
        /// </summary>
        public ExecutionAnswerQuestionCommand AnswerQuestion(string executionId, string questionId, IEnumerable<QuestionAnswer> answers) =>
            Stamp(new ExecutionAnswerQuestionCommand
            {
                Payload = new ExecutionAnswerQuestionPayload
                {
                    ExecutionId = executionId,
                    QuestionId = questionId,
                    Answers = new List<QuestionAnswer>(answers),
                },
            });

        public ExecutionInterruptCommand Interrupt(string executionId) =>
            Stamp(new ExecutionInterruptCommand { Payload = new ExecutionInterruptPayload { ExecutionId = executionId } });

        public WorkspaceRequestSubmitCommand Request(string projectId, string workstreamId, string executionId, string text) =>
            Stamp(new WorkspaceRequestSubmitCommand { Payload = new WorkspaceRequestSubmitPayload { ProjectId = projectId, WorkstreamId = workstreamId, ExecutionId = executionId, Text = text } });

        /// <summary>A read-only request about a whole project: no task, no execution (ADR 0030, 2026-11-10).</summary>
        public WorkspaceRequestSubmitCommand RequestAboutProject(string projectId, string text) =>
            Stamp(new WorkspaceRequestSubmitCommand { Payload = new WorkspaceRequestSubmitPayload { ProjectId = projectId, WorkstreamId = null, ExecutionId = null, Text = text } });

        /// <summary>A read-only request about all of the person's work: no project, task or execution.</summary>
        public WorkspaceRequestSubmitCommand RequestAboutAllWork(string text) =>
            Stamp(new WorkspaceRequestSubmitCommand { Payload = new WorkspaceRequestSubmitPayload { ProjectId = null, WorkstreamId = null, ExecutionId = null, Text = text } });

        public WorkspaceRequestResumeCommand ResumeRequest(string requestId, string? text = null) =>
            Stamp(new WorkspaceRequestResumeCommand { Payload = new WorkspaceRequestResumePayload { RequestId = requestId, Text = text } });

        public WorkspaceRequestCancelCommand CancelRequest(string requestId) =>
            Stamp(new WorkspaceRequestCancelCommand { Payload = new WorkspaceRequestCancelPayload { RequestId = requestId } });

        private T Stamp<T>(T command) where T : CommandEnvelope
        {
            command.CommandId = Guid.NewGuid().ToString("D");
            command.IssuedAt = HalcyonicJson.FormatTimestamp(now());
            command.Client = client;
            return command;
        }
    }
}

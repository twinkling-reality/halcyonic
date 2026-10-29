#nullable enable
using System;
using System.Collections.Generic;
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
        public ProjectSetLocationCommand SetProjectLocation(string projectId, ProjectLocationChoice location) =>
            Stamp(new ProjectSetLocationCommand
            {
                Payload = new ProjectSetLocationPayload { ProjectId = projectId, Location = location },
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

        public WorkstreamCreateCommand CreateWorkstream(string projectId, string title, string? objective) =>
            Stamp(new WorkstreamCreateCommand
            {
                Payload = new WorkstreamCreatePayload { ProjectId = projectId, Title = title, Objective = objective },
            });

        /// <summary>
        /// Starts work on a runtime. <paramref name="modelRef"/> is a model's reference exactly as the
        /// runtime's own list gave it (<see cref="ControlPlaneApi.GetRuntimeModelsAsync"/>), or null to
        /// leave the choice to the runtime; only a runtime whose <c>model_choice</c> is <c>listed</c>
        /// accepts one.
        /// </summary>
        public ExecutionStartCommand StartExecution(
            string workstreamId,
            string runtimeId,
            string instruction,
            IDictionary<string, JToken>? options = null,
            string? modelRef = null) =>
            Stamp(new ExecutionStartCommand
            {
                Payload = new ExecutionStartPayload
                {
                    WorkstreamId = workstreamId,
                    RuntimeId = runtimeId,
                    Instruction = instruction,
                    Options = options == null ? new Dictionary<string, JToken>() : new Dictionary<string, JToken>(options),
                    ModelRef = modelRef,
                },
            });

        public ExecutionSendInstructionCommand SendInstruction(string executionId, string text) =>
            Stamp(new ExecutionSendInstructionCommand
            {
                Payload = new ExecutionSendInstructionPayload { ExecutionId = executionId, Text = text },
            });

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

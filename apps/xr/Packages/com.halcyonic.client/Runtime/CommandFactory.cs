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

        public ProjectCreateCommand CreateProject(string name) =>
            Stamp(new ProjectCreateCommand { Payload = new ProjectCreatePayload { Name = name } });

        public WorkstreamCreateCommand CreateWorkstream(string projectId, string title, string? objective) =>
            Stamp(new WorkstreamCreateCommand
            {
                Payload = new WorkstreamCreatePayload { ProjectId = projectId, Title = title, Objective = objective },
            });

        public ExecutionStartCommand StartExecution(
            string workstreamId,
            string runtimeId,
            string instruction,
            IDictionary<string, JToken>? options = null) =>
            Stamp(new ExecutionStartCommand
            {
                Payload = new ExecutionStartPayload
                {
                    WorkstreamId = workstreamId,
                    RuntimeId = runtimeId,
                    Instruction = instruction,
                    Options = options == null ? new Dictionary<string, JToken>() : new Dictionary<string, JToken>(options),
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

        public ExecutionInterruptCommand Interrupt(string executionId) =>
            Stamp(new ExecutionInterruptCommand { Payload = new ExecutionInterruptPayload { ExecutionId = executionId } });

        private T Stamp<T>(T command) where T : CommandEnvelope
        {
            command.CommandId = Guid.NewGuid().ToString("D");
            command.IssuedAt = HalcyonicJson.FormatTimestamp(now());
            command.Client = client;
            return command;
        }
    }
}

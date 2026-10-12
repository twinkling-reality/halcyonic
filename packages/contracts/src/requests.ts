import Type, { type Static } from 'typebox';
import {
  CommandId,
  ErrorInfo,
  ExecutionId,
  JournalId,
  NativeId,
  Nullable,
  Position,
  ProjectId,
  Text,
  Timestamp,
  WorkspaceRequestId,
  WorkstreamId,
} from './primitives.ts';
import { CommandStatus, ExecutionStatus } from './statuses.ts';

const strict = { additionalProperties: false } as const;

export { WorkspaceRequestId } from './primitives.ts';
export const WorkspaceRequestStatus = Type.Enum([
  'running',
  'waiting_for_human',
  'waiting_for_runtime',
  'responded',
  'stopped',
  'interrupted',
  'failed',
]);
export const WorkspaceRequestStepStatus = Type.Enum([
  'prepared',
  'running',
  'waiting',
  'confirmed',
  'rejected',
  'failed',
  'unknown',
]);
export const WorkspaceRequestReply = Type.Object(
  {
    text: Text(4000),
    epistemic: Type.Literal('reported'),
    model: Text(200),
    served: Type.Literal('this_mac'),
  },
  strict,
);
export const WorkspaceRequestStep = Type.Object(
  {
    step_id: WorkspaceRequestId,
    tool_name: Text(64),
    status: WorkspaceRequestStepStatus,
    command_id: Nullable(CommandId),
    command_status: Nullable(CommandStatus),
    observation_position: Position,
    active_turn_id: Nullable(NativeId),
    had_active_turn: Type.Boolean(),
    observation_after: Position,
    observed_turn_id: Nullable(NativeId),
    runtime_status: Nullable(ExecutionStatus),
    /** Host-authored receipt/availability only; model text belongs in reported reply. */
    summary: Text(2000),
  },
  strict,
);
export const WorkspaceRequestView = Type.Object(
  {
    request_id: WorkspaceRequestId,
    journal_id: JournalId,
    project_id: ProjectId,
    workstream_id: WorkstreamId,
    execution_id: ExecutionId,
    original_text: Text(32000),
    status: WorkspaceRequestStatus,
    reply: Nullable(WorkspaceRequestReply),
    steps: Type.Array(WorkspaceRequestStep, { maxItems: 64 }),
    /** Earlier receipts remain in the journal; the view holds the latest 64. */
    earlier_steps: Type.Integer({ minimum: 0 }),
    created_at: Timestamp,
    updated_at: Timestamp,
  },
  strict,
);
export type WorkspaceRequestView = Static<typeof WorkspaceRequestView>;
export type WorkspaceRequestStep = Static<typeof WorkspaceRequestStep>;
export type WorkspaceRequestReply = Static<typeof WorkspaceRequestReply>;
export type WorkspaceRequestStatus = Static<typeof WorkspaceRequestStatus>;
export const WorkspaceRequestResponse = Type.Object({ request: WorkspaceRequestView }, strict);
export type WorkspaceRequestResponse = Static<typeof WorkspaceRequestResponse>;

/** Capability read, never inference or permission to silently choose another model. */
export const WorkspaceAgentStatus = Type.Union([
  Type.Object(
    { availability: Type.Literal('available'), model: Text(200), served: Type.Literal('this_mac') },
    strict,
  ),
  Type.Object({ availability: Type.Literal('unavailable'), reason: ErrorInfo }, strict),
]);
export type WorkspaceAgentStatus = Static<typeof WorkspaceAgentStatus>;

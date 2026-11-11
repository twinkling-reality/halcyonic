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
/** Counts of the person's tasks by where they stand, as the host lists them: no names, no words. */
export const WorkspaceRequestTaskCounts = Type.Object(
  {
    total: Type.Integer({ minimum: 0 }),
    running: Type.Integer({ minimum: 0 }),
    waiting_on_you: Type.Integer({ minimum: 0 }),
    failed: Type.Integer({ minimum: 0 }),
  },
  strict,
);
export type WorkspaceRequestTaskCounts = Static<typeof WorkspaceRequestTaskCounts>;

/**
 * What the computer itself saw, recorded only when a request ends with no new reply (interrupted, failed or
 * stopped), so the person is never left with nothing (the lead, 2026-11-11). Facts, never sentences: the clients
 * choose the words, and draw them apart from the assistant's reply, as the computer's own. No model text, no path,
 * no agent words. For a task: its execution's state, its turns, the files the Mac saw its turns change, the
 * project's latest check, and whether the person's words reached the agent (`sent`). For a project: its tasks by
 * where they stand, and whether its latest check failed. For all work: the projects and the tasks across them.
 */
export const WorkspaceRequestObserved = Type.Object(
  {
    task: Nullable(
      Type.Object(
        {
          status: ExecutionStatus,
          turns: Type.Integer({ minimum: 0 }),
          files: Nullable(
            Type.Object(
              {
                created: Type.Integer({ minimum: 0 }),
                changed: Type.Integer({ minimum: 0 }),
                deleted: Type.Integer({ minimum: 0 }),
              },
              strict,
            ),
          ),
          check: Nullable(
            Type.Object(
              {
                passed: Type.Boolean(),
                files: Type.Union([
                  Type.Literal('unknown'),
                  Type.Literal('current'),
                  Type.Literal('changed'),
                ]),
              },
              strict,
            ),
          ),
          sent: Type.Boolean(),
        },
        strict,
      ),
    ),
    tasks: Nullable(WorkspaceRequestTaskCounts),
    projects: Nullable(
      Type.Object(
        { total: Type.Integer({ minimum: 0 }), checks_failed: Type.Integer({ minimum: 0 }) },
        strict,
      ),
    ),
  },
  strict,
);
export type WorkspaceRequestObserved = Static<typeof WorkspaceRequestObserved>;

export const WorkspaceRequestView = Type.Object(
  {
    request_id: WorkspaceRequestId,
    journal_id: JournalId,
    /** Null, with the task fields, for a request about all of the person's work, read-only. */
    project_id: Nullable(ProjectId),
    /** Null, with `execution_id`, for a request about the whole project, which is read-only. */
    workstream_id: Nullable(WorkstreamId),
    execution_id: Nullable(ExecutionId),
    original_text: Text(32000),
    status: WorkspaceRequestStatus,
    reply: Nullable(WorkspaceRequestReply),
    /**
     * What the computer saw, where the request ended with no new reply; absent otherwise, and never beside a reply
     * that came after it. The computer's own facts, never the assistant's.
     */
    observed: Type.Optional(WorkspaceRequestObserved),
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

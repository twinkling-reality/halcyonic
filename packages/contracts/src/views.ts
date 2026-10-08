import Type, { type Static } from 'typebox';
import {
  CommandFailure,
  CommandId,
  CommandRejection,
  CommandResult,
  CommandType,
} from './commands.ts';
import { ApprovalSubject } from './events.ts';
import { HostPath, ProjectLocation } from './locations.ts';
import {
  ErrorInfo,
  ExecutionId,
  JournalId,
  NativeId,
  Nullable,
  Position,
  ProjectId,
  Text,
  Timestamp,
  WorkstreamId,
} from './primitives.ts';
import { QuestionPrompts } from './questions.ts';
import { ModelRef, RuntimeDescriptor, RuntimeRef } from './runtime.ts';

const strict = { additionalProperties: false } as const;

const EXECUTION_STATUS_LITERALS = [
  /** Accepted by the control plane; the runtime has not started a turn yet. */
  Type.Literal('starting'),
  /** A turn is in progress. */
  Type.Literal('running'),
  /** A turn is in progress and a test run reported by the runtime is in progress. */
  Type.Literal('verifying'),
  /** The runtime is blocked on a human decision, such as an approval. */
  Type.Literal('waiting_for_human'),
  /** The last turn ended successfully. This says nothing about whether the work is correct. */
  Type.Literal('completed'),
  /** The last turn failed, or the execution could not be started. */
  Type.Literal('failed'),
  /** The last turn was stopped on request. */
  Type.Literal('interrupted'),
  /** Halcyonic cannot currently observe the execution. Shown instead of a guess. */
  Type.Literal('unknown'),
] as const;

export const ExecutionStatus = Type.Union([...EXECUTION_STATUS_LITERALS]);
export type ExecutionStatus = Static<typeof ExecutionStatus>;

/** A workstream reports `created` until its first execution, then its current execution's status. */
export const WorkstreamStatus = Type.Union([Type.Literal('created'), ...EXECUTION_STATUS_LITERALS]);
export type WorkstreamStatus = Static<typeof WorkstreamStatus>;

export const AttentionLevel = Type.Union([
  Type.Literal('none'),
  Type.Literal('notice'),
  Type.Literal('action_required'),
]);
export type AttentionLevel = Static<typeof AttentionLevel>;

/** Every attention signal names the facts behind it, so a client can always explain it. */
export const AttentionReason = Type.Union([
  Type.Object(
    { kind: Type.Literal('approval_pending'), execution_id: ExecutionId, approval_id: NativeId },
    strict,
  ),
  Type.Object(
    { kind: Type.Literal('question_pending'), execution_id: ExecutionId, question_id: NativeId },
    strict,
  ),
  Type.Object({ kind: Type.Literal('execution_failed'), execution_id: ExecutionId }, strict),
  Type.Object({ kind: Type.Literal('execution_state_unknown'), execution_id: ExecutionId }, strict),
  Type.Object(
    {
      kind: Type.Literal('verification_failed'),
      execution_id: ExecutionId,
      test_run_id: NativeId,
    },
    strict,
  ),
]);
export type AttentionReason = Static<typeof AttentionReason>;

export const Attention = Type.Object(
  { level: AttentionLevel, reasons: Type.Array(AttentionReason) },
  strict,
);
export type Attention = Static<typeof Attention>;

export const ProjectView = Type.Object(
  {
    project_id: ProjectId,
    name: Text(200),
    /** Where the project's work runs on the host; null when it has no folder. */
    location: Nullable(ProjectLocation),
    created_at: Timestamp,
    updated_at: Timestamp,
  },
  strict,
);
export type ProjectView = Static<typeof ProjectView>;

export const WorkstreamView = Type.Object(
  {
    workstream_id: WorkstreamId,
    project_id: ProjectId,
    title: Text(200),
    objective: Nullable(Text(4000)),
    status: WorkstreamStatus,
    attention: Attention,
    /** The most recently created execution, whose status the workstream reports. */
    current_execution_id: Nullable(ExecutionId),
    /** All executions of the workstream, oldest first. */
    execution_ids: Type.Array(ExecutionId),
    created_at: Timestamp,
    updated_at: Timestamp,
  },
  strict,
);
export type WorkstreamView = Static<typeof WorkstreamView>;

export const ApprovalView = Type.Object(
  {
    approval_id: NativeId,
    subject: ApprovalSubject,
    /** False when the request was not complete as reported or journaled: it can only be denied. */
    approvable: Type.Boolean(),
    requested_at: Timestamp,
  },
  strict,
);
export type ApprovalView = Static<typeof ApprovalView>;

/** A question the agent asked that waits for the person (ADR 0022). */
export const QuestionView = Type.Object(
  {
    question_id: NativeId,
    prompts: QuestionPrompts,
    answerable: Type.Boolean(),
    asked_at: Timestamp,
  },
  strict,
);
export type QuestionView = Static<typeof QuestionView>;

export const ToolActivityView = Type.Object(
  {
    tool_call_id: NativeId,
    tool_name: Text(128),
    title: Nullable(Text(500)),
    started_at: Timestamp,
  },
  strict,
);
export type ToolActivityView = Static<typeof ToolActivityView>;

export const TestRunView = Type.Object(
  { test_run_id: NativeId, label: Nullable(Text(500)), started_at: Timestamp },
  strict,
);
export type TestRunView = Static<typeof TestRunView>;

export const TestRunResultView = Type.Object(
  {
    test_run_id: NativeId,
    label: Nullable(Text(500)),
    outcome: Type.Union([Type.Literal('passed'), Type.Literal('failed'), Type.Literal('errored')]),
    summary: Nullable(Text(2000)),
    completed_at: Timestamp,
  },
  strict,
);
export type TestRunResultView = Static<typeof TestRunResultView>;

export const ExecutionView = Type.Object(
  {
    execution_id: ExecutionId,
    workstream_id: WorkstreamId,
    project_id: ProjectId,
    runtime: RuntimeRef,
    /** The runtime's own session or thread id, kept for correlation with other tools. */
    native_id: Nullable(NativeId),
    /**
     * The model the runtime last reported using, as its model list names it; null until it
     * reports one (ADR 0016).
     */
    model_ref: Nullable(ModelRef),
    instruction: Text(32000),
    /**
     * The folder the runtime was given to work in, as the host resolved the project's location for
     * this start. Null for a runtime that uses none, and for executions journaled before folders
     * were recorded, where it means not recorded.
     */
    directory: Nullable(HostPath),
    status: ExecutionStatus,
    /** Why the execution is `failed`, `interrupted` or `unknown`, when known. */
    status_reason: Nullable(ErrorInfo),
    pending_approvals: Type.Array(ApprovalView),
    pending_questions: Type.Array(QuestionView),
    active_tools: Type.Array(ToolActivityView),
    /**
     * Whether a tool call runs, from the facts alone. `running`: a turn is active, the status is not
     * `unknown`, and a tool call is open (started, not completed) or a test run is. `none`: no turn is active, or one
     * is with no tool call open on a runtime that reports every tool call it makes
     * (`reports_tool_activity`). `unknown`: a turn is active on a runtime that does not, or the
     * status is `unknown`, whose open calls are stale. Silence never reads as `running` or `none`.
     */
    // `unknown` first, so a client's generated default is the one that claims nothing.
    tool_activity: Type.Union([
      Type.Literal('unknown'),
      Type.Literal('running'),
      Type.Literal('none'),
    ]),
    active_test_run: Nullable(TestRunView),
    last_test_run: Nullable(TestRunResultView),
    turn_count: Type.Integer({ minimum: 0 }),
    created_at: Timestamp,
    started_at: Nullable(Timestamp),
    updated_at: Timestamp,
  },
  strict,
);
export type ExecutionView = Static<typeof ExecutionView>;

export const CommandStatus = Type.Union([
  Type.Literal('accepted'),
  Type.Literal('rejected'),
  Type.Literal('completed'),
  Type.Literal('failed'),
]);
export type CommandStatus = Static<typeof CommandStatus>;

export const CommandView = Type.Object(
  {
    command_id: CommandId,
    command_type: CommandType,
    status: CommandStatus,
    project_id: Nullable(ProjectId),
    workstream_id: Nullable(WorkstreamId),
    execution_id: Nullable(ExecutionId),
    issued_at: Timestamp,
    updated_at: Timestamp,
    rejection: Nullable(CommandRejection),
    failure: Nullable(CommandFailure),
    result: Nullable(CommandResult),
  },
  strict,
);
export type CommandView = Static<typeof CommandView>;

/**
 * Identifies the journal behind every response. `fixture` marks a journal loaded from a recorded
 * trace, which clients must label as development data.
 */
export const JournalInfo = Type.Object(
  {
    journal_id: JournalId,
    origin: Type.Union([Type.Literal('live'), Type.Literal('fixture')]),
  },
  strict,
);
export type JournalInfo = Static<typeof JournalInfo>;

/** The authoritative current state at `position`. */
export const Snapshot = Type.Object(
  {
    journal: JournalInfo,
    position: Position,
    projects: Type.Array(ProjectView),
    workstreams: Type.Array(WorkstreamView),
    executions: Type.Array(ExecutionView),
    /** Commands that are still pending, plus the most recent finished ones. */
    commands: Type.Array(CommandView),
    runtimes: Type.Array(RuntimeDescriptor),
  },
  strict,
);
export type Snapshot = Static<typeof Snapshot>;

/** The current state of every entity an event changed. Clients replace their copies wholesale. */
export const EntityChanges = Type.Object(
  {
    projects: Type.Array(ProjectView),
    workstreams: Type.Array(WorkstreamView),
    executions: Type.Array(ExecutionView),
    commands: Type.Array(CommandView),
  },
  strict,
);
export type EntityChanges = Static<typeof EntityChanges>;

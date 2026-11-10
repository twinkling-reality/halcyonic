import Type, { type Static } from 'typebox';
import {
  CommandFailure,
  CommandId,
  CommandRejection,
  CommandResult,
  CommandType,
} from './commands.ts';
import { WorkstreamCopy } from './copies.ts';
import { ApprovalSubject } from './events.ts';
import { HostPath, ProjectLocation } from './locations.ts';
import { AutoFixView, CheckFindings, CheckId } from './previews.ts';
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
import { ApprovalLevel, ModelRef, RuntimeDescriptor, RuntimeRef } from './runtime.ts';
import { StarterId } from './starters.ts';
import { CommandStatus, ExecutionStatus, WorkstreamStatus } from './statuses.ts';
import { ProjectWorldView } from './world.ts';

export { CommandStatus, ExecutionStatus, WorkstreamStatus } from './statuses.ts';

const strict = { additionalProperties: false } as const;

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

/**
 * Whether the project's files are still as they were when a check ran (T2): `current` while no
 * turn in the folder and no restore has changed them since; `changed` once the Mac observed a
 * turn or a restore change a file, or the project moved to another folder; `unknown` while a turn
 * runs in the folder, after one whose files the Mac could not read whole, when the execution's
 * state is unknown, or after an incomplete restore that wrote a file.
 */
// `unknown` first, so a client's generated default is the one that claims nothing.
export const CheckFilesState = Type.Union([
  Type.Literal('unknown'),
  Type.Literal('current'),
  Type.Literal('changed'),
]);
export type CheckFilesState = Static<typeof CheckFilesState>;

/** A project's latest check, as `project.check.recorded` recorded it, and whether its files are still current (M1.8e, T2). */
export const ProjectCheckView = Type.Object(
  {
    check_id: CheckId,
    recorded_at: Timestamp,
    position: Position,
    files: CheckFilesState,
    findings: CheckFindings,
  },
  strict,
);
export type ProjectCheckView = Static<typeof ProjectCheckView>;

/** The preview running now, as `project.preview.started` said: the check it serves, on a loopback port, and whether that check's files are still current (T2). */
export const ProjectPreviewView = Type.Object(
  {
    check_id: CheckId,
    port: Type.Integer({ minimum: 1, maximum: 65535 }),
    files: CheckFilesState,
  },
  strict,
);
export type ProjectPreviewView = Static<typeof ProjectPreviewView>;

/** The check whose game plays on the Mac now, as `project.mac_play.started` said, and whether that check's files are still current (T2). */
export const ProjectMacPlayView = Type.Object(
  {
    check_id: CheckId,
    files: CheckFilesState,
  },
  strict,
);
export type ProjectMacPlayView = Static<typeof ProjectMacPlayView>;

/**
 * `check`, `preview` and `mac_play` hold the facts their events carried, so a client that connects
 * after them still has them; each is absent when there is none, which never reads as false.
 * `world` is there only for a world project whose description file the Mac has read (ADR 0049).
 */
export const ProjectView = Type.Object(
  {
    project_id: ProjectId,
    name: Text(200),
    /** Where the project's work runs on the host; null when it has no folder. */
    location: Nullable(ProjectLocation),
    /** The starter its folder began with (ADR 0037), or null. */
    starter: Nullable(StarterId),
    created_at: Timestamp,
    updated_at: Timestamp,
    check: Type.Optional(ProjectCheckView),
    preview: Type.Optional(ProjectPreviewView),
    mac_play: Type.Optional(ProjectMacPlayView),
    world: Type.Optional(ProjectWorldView),
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
    /**
     * The task's own copy of the project's folder, or why it has none (ADR 0045). Absent when the
     * task has not started in a folder since copies began, as in every journal from before.
     */
    copy: Type.Optional(WorkstreamCopy),
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
    /**
     * The approval level the runtime confirmed the execution started with, from
     * `runtime.execution.started` (ADR 0043), never from the start's choice. Absent where it was not
     * recorded, which never reads as `ask`.
     */
    approval_level: Type.Optional(ApprovalLevel),
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
    /**
     * The project's failed check sent back to this agent on its own (ADR 0044): the latest chain,
     * under way or ended. Absent where none was ever recorded.
     */
    auto_fix: Type.Optional(AutoFixView),
    created_at: Timestamp,
    started_at: Nullable(Timestamp),
    updated_at: Timestamp,
  },
  strict,
);
export type ExecutionView = Static<typeof ExecutionView>;

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

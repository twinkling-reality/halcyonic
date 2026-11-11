import Type, { type Static } from 'typebox';
import { CheckpointId } from './checkpoints.ts';
import {
  CommandFailure,
  CommandId,
  CommandRejection,
  CommandResult,
  CommandType,
} from './commands.ts';
import { WorkstreamCopy } from './copies.ts';
import { ApprovalSubject, QUEUED_INSTRUCTIONS_MAX } from './events.ts';
import { ProjectPublishedView } from './github.ts';
import { HostPath, ProjectLocation } from './locations.ts';
import { OwnCheckFindings, OwnCheckManager, OwnCheckScript } from './own-checks.ts';
import { AutoFixView, CheckFindings, CheckId, ShareAddress, ShareWay } from './previews.ts';
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
import { ProjectRulesOn } from './project-reads.ts';
import { QuestionPrompts } from './questions.ts';
import { ApprovalLevel, ModelRef, RuntimeDescriptor, RuntimeRef } from './runtime.ts';
import { SecretNames } from './secrets.ts';
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
    /** The task whose own copy this check checked (ADR 0045); absent, the project's own folder. */
    workstream_id: Type.Optional(WorkstreamId),
  },
  strict,
);
export type ProjectCheckView = Static<typeof ProjectCheckView>;

/** A project's own check's latest run (ADR 0055), as `project.own_check.recorded` recorded it. */
export const ProjectOwnCheckRunView = Type.Object(
  {
    check_id: CheckId,
    recorded_at: Timestamp,
    position: Position,
    /** Whether the folder changed since: `changed` once a turn changed files in it. */
    files: CheckFilesState,
    /** The run right after the person chose the check. */
    first: Type.Boolean(),
    findings: OwnCheckFindings,
    workstream_id: Type.Optional(WorkstreamId),
  },
  strict,
);
export type ProjectOwnCheckRunView = Static<typeof ProjectOwnCheckRunView>;

/**
 * A project's own check (ADR 0055): what the person chose (each script's command, the project's own
 * words), its latest run, and whether any run of this choice passed on this computer. A failure is sent
 * to an agent on its own only when the run before the agent's turn passed (`passed_before`).
 */
export const ProjectOwnCheckView = Type.Object(
  {
    manager: OwnCheckManager,
    scripts: Type.Array(OwnCheckScript, { minItems: 1, maxItems: 5 }),
    chosen_at: Timestamp,
    latest: Nullable(ProjectOwnCheckRunView),
    ever_passed: Type.Boolean(),
  },
  strict,
);
export type ProjectOwnCheckView = Static<typeof ProjectOwnCheckView>;

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

/** The project's shared link now, as `project.share.started` said, and whether its check's files are still current. */
export const ProjectShareView = Type.Object(
  { check_id: CheckId, url: ShareAddress, way: ShareWay, files: CheckFilesState },
  strict,
);
export type ProjectShareView = Static<typeof ProjectShareView>;

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
    /** A project's own check, once the person chose one (ADR 0055); never on a starter's project. */
    own_check: Type.Optional(ProjectOwnCheckView),
    preview: Type.Optional(ProjectPreviewView),
    mac_play: Type.Optional(ProjectMacPlayView),
    /** The project's shared link, when one is live (F1). */
    share: Type.Optional(ProjectShareView),
    /** The game as last published to the person's own GitHub Pages (ADR 0039, amended). */
    published: Type.Optional(ProjectPublishedView),
    /**
     * The project's secrets by name, sorted (ADR 0057); absent when it has none. Never a value.
     */
    secrets: Type.Optional(SecretNames),
    world: Type.Optional(ProjectWorldView),
    /**
     * The project's own rules, while they are on (E5): the file and the digest of the text the person
     * chose, passed on before each task's first instruction while the file still has that digest.
     * Absent while off.
     */
    rules: Type.Optional(ProjectRulesOn),
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
    /** The project's version the task starts from (ADR 0045); absent for a task from the folder as it is. */
    from_checkpoint: Type.Optional(CheckpointId),
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

/** An instruction waiting for its execution's turn to end: its command and when it was queued. */
export const QueuedInstructionView = Type.Object(
  { command_id: CommandId, queued_at: Timestamp },
  { additionalProperties: false },
);
export type QueuedInstructionView = Static<typeof QueuedInstructionView>;

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
    /** True when its start recorded the runtime's own plan mode (PLAN); absent otherwise. */
    plan: Type.Optional(Type.Boolean()),
    /** True once the runtime left the plan to carry it out (`runtime.plan.left`); absent otherwise. */
    plan_left: Type.Optional(Type.Boolean()),
    /**
     * Why its start, which asked for Halcyonic's sandbox, ran without it or is not counted confined
     * (BOX-4), as recorded; absent where none was.
     */
    unconfined_reason: Type.Optional(Text(2000)),
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
    /**
     * Instructions the person sent while a turn ran, to a runtime that cannot steer, waiting for the
     * turn to end (ADR 0056): oldest first, at most `QUEUED_INSTRUCTIONS_MAX`. Each is sent only after
     * a turn that completed, one per turn; the headset holds their words. Absent when none waits.
     */
    queued_instructions: Type.Optional(
      Type.Array(QueuedInstructionView, { minItems: 1, maxItems: QUEUED_INSTRUCTIONS_MAX }),
    ),
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

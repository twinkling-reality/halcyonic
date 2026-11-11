import Type, { type Static, type TSchema } from 'typebox';
import { CatalogRepositoryRef } from './catalog.ts';
import {
  CheckpointFailure,
  CheckpointId,
  CheckpointReason,
  GitObjectId,
  PathList,
  ReviewDigest,
} from './checkpoints.ts';
import {
  CommandEnvelope,
  CommandFailure,
  CommandId,
  CommandRejection,
  CommandResult,
  CommandType,
  ExecutionSendInstructionCommand,
  PolicyCategory,
  ReceivedVia,
} from './commands.ts';
import { ChangedFiles, ConflictedFiles, CopyRefusalCode } from './copies.ts';
import { DeviceLabel, Principal, Sha256Hex } from './devices.ts';
import { FilesObserved } from './file-changes.ts';
import { HostPath, ProjectLocation } from './locations.ts';
import {
  OWN_CHECK_SCRIPT_NAMES,
  OwnCheckFindings,
  OwnCheckManager,
  OwnCheckScript,
} from './own-checks.ts';
import {
  AUTO_FIX_CAP_MAX,
  AutoFixEndReason,
  CheckFindings,
  CheckId,
  PreviewStopReason,
  ShareAddress,
  ShareStopReason,
  ShareWay,
} from './previews.ts';
import {
  DeviceId,
  ErrorInfo,
  EventId,
  ExecutionId,
  NativeId,
  Nullable,
  ProjectId,
  RuntimeId,
  Text,
  Timestamp,
  Uuid,
  WorkstreamId,
} from './primitives.ts';
import { QuestionPrompts } from './questions.ts';
import {
  WorkspaceRequestId,
  WorkspaceRequestReply,
  WorkspaceRequestStatus,
  WorkspaceRequestStep,
  WorkspaceRequestView,
} from './requests.ts';
import { ApprovalLevel, ModelRef, RuntimeRef } from './runtime.ts';
import { StarterId } from './starters.ts';
import { ToolResult } from './tool-results.ts';
import { EVENT_SCHEMA_VERSION } from './versions.ts';
import { WorldObserved } from './world.ts';

const strict = { additionalProperties: false } as const;

const NativeType = Type.String({ minLength: 1, maxLength: 128 });

/**
 * How the fact in an event is known. The classes match Salidium's epistemic vocabulary:
 * - `observed`: read from structured runtime or control plane state.
 * - `reported`: a claim made in agent or user text. It is never upgraded to `observed`.
 * - `inferred`: derived by a named deterministic rule, which is recorded.
 */
export const Provenance = Type.Union([
  Type.Object({ epistemic: Type.Literal('observed'), native_type: Nullable(NativeType) }, strict),
  Type.Object({ epistemic: Type.Literal('reported'), native_type: Nullable(NativeType) }, strict),
  Type.Object(
    {
      epistemic: Type.Literal('inferred'),
      native_type: Nullable(NativeType),
      rule: Type.String({ pattern: '^[a-z][a-z0-9_.-]{0,127}$' }),
    },
    strict,
  ),
]);
export type Provenance = Static<typeof Provenance>;

export const ControlPlaneSource = Type.Object({ kind: Type.Literal('control_plane') }, strict);
export const RuntimeSource = Type.Object(
  { kind: Type.Literal('runtime'), runtime_id: RuntimeId },
  strict,
);
export const EventSource = Type.Union([ControlPlaneSource, RuntimeSource]);
export type EventSource = Static<typeof EventSource>;

const EnvelopeBase = {
  schema_version: Type.Literal(EVENT_SCHEMA_VERSION),
  event_id: EventId,
  /**
   * Identifier of the native record this event was normalized from. Unique per source, so a
   * runtime record delivered twice is journaled once.
   */
  source_native_id: Nullable(NativeId),
  /** Ordering supplied by the source within one execution. Null when the source has none. */
  sequence: Nullable(Type.Integer({ minimum: 0 })),
  /** When the fact happened, by the clock of the source that observed it. */
  occurred_at: Timestamp,
  /** When the control plane journaled the event. */
  ingested_at: Timestamp,
  /** The command that started the chain of work this event belongs to, if any. */
  correlation_id: Nullable(Uuid),
  /** The command or event that directly caused this event, if any. */
  causation_id: Nullable(Uuid),
  provenance: Provenance,
};

const ProjectScope = {
  project_id: ProjectId,
  workstream_id: Type.Null(),
  execution_id: Type.Null(),
};
const WorkstreamScope = {
  project_id: ProjectId,
  workstream_id: WorkstreamId,
  execution_id: Type.Null(),
};
const ExecutionScope = {
  project_id: ProjectId,
  workstream_id: WorkstreamId,
  execution_id: ExecutionId,
};
/** A command may be rejected before its target resolves, so its scope is only as complete as known. */
const CommandScope = {
  project_id: Nullable(ProjectId),
  workstream_id: Nullable(WorkstreamId),
  execution_id: Nullable(ExecutionId),
};
/** Facts about the control plane itself, such as its paired devices, belong to no project. */
const NoScope = {
  project_id: Type.Null(),
  workstream_id: Type.Null(),
  execution_id: Type.Null(),
};

function projectEvent<const T extends string, P extends TSchema>(eventType: T, payload: P) {
  return Type.Object(
    {
      ...EnvelopeBase,
      ...ProjectScope,
      event_type: Type.Literal(eventType),
      source: ControlPlaneSource,
      payload,
    },
    strict,
  );
}

function workstreamEvent<const T extends string, P extends TSchema>(eventType: T, payload: P) {
  return Type.Object(
    {
      ...EnvelopeBase,
      ...WorkstreamScope,
      event_type: Type.Literal(eventType),
      source: ControlPlaneSource,
      payload,
    },
    strict,
  );
}

function executionEvent<const T extends string, P extends TSchema>(eventType: T, payload: P) {
  return Type.Object(
    {
      ...EnvelopeBase,
      ...ExecutionScope,
      event_type: Type.Literal(eventType),
      source: ControlPlaneSource,
      payload,
    },
    strict,
  );
}

/**
 * A workspace request's scope: its task and execution; neither, for a whole-project request; or no
 * project either, for a request about all of the person's work.
 */
const RequestScope = {
  project_id: Nullable(ProjectId),
  workstream_id: Nullable(WorkstreamId),
  execution_id: Nullable(ExecutionId),
};

function requestEvent<const T extends string, P extends TSchema>(eventType: T, payload: P) {
  return Type.Object(
    {
      ...EnvelopeBase,
      ...RequestScope,
      event_type: Type.Literal(eventType),
      source: ControlPlaneSource,
      payload,
    },
    strict,
  );
}

function commandEvent<const T extends string, P extends TSchema>(eventType: T, payload: P) {
  return Type.Object(
    {
      ...EnvelopeBase,
      ...CommandScope,
      event_type: Type.Literal(eventType),
      source: ControlPlaneSource,
      payload,
    },
    strict,
  );
}

function controlPlaneEvent<const T extends string, P extends TSchema>(eventType: T, payload: P) {
  return Type.Object(
    {
      ...EnvelopeBase,
      ...NoScope,
      event_type: Type.Literal(eventType),
      source: ControlPlaneSource,
      payload,
    },
    strict,
  );
}

function runtimeEvent<const T extends string, P extends TSchema>(eventType: T, payload: P) {
  return Type.Object(
    {
      ...EnvelopeBase,
      ...ExecutionScope,
      event_type: Type.Literal(eventType),
      source: RuntimeSource,
      payload,
    },
    strict,
  );
}

// Control plane facts ------------------------------------------------------------------------

export const ProjectCreated = projectEvent(
  'project.created',
  Type.Object({ name: Text(200), location: Nullable(ProjectLocation) }, strict),
);

/** The project's work runs in another folder from now on. */
export const ProjectLocationSet = projectEvent(
  'project.location_set',
  Type.Object({ location: ProjectLocation }, strict),
);

/**
 * The person linked the project to a repository of their catalog (ADR 0039), replacing `previous`.
 * The link is the person's command, so the event is observed; the repository's `owner/name` in it
 * is as the client sent it, taken from the catalog's answer; checked against the catalog this computer last read, never against GitHub, and it may name a private repository or an organisation.
 */
export const ProjectCatalogLinked = projectEvent(
  'project.catalog_linked',
  Type.Object({ catalog: CatalogRepositoryRef, previous: Nullable(CatalogRepositoryRef) }, strict),
);

/** The person removed the project's link to `previous`, as it was recorded when it was linked. */
export const ProjectCatalogUnlinked = projectEvent(
  'project.catalog_unlinked',
  Type.Object({ previous: CatalogRepositoryRef }, strict),
);

/**
 * The project's new folder began with a starter's files (ADR 0037): which starter, the SHA-256 of
 * the lockfile its shared dependencies were installed from, how many files were copied, and the
 * SHA-256 over their paths and contents.
 */
export const ProjectStarterApplied = projectEvent(
  'project.starter_applied',
  Type.Object(
    {
      starter_id: StarterId,
      lockfile: Sha256Hex,
      files: Type.Integer({ minimum: 0 }),
      template: Sha256Hex,
    },
    strict,
  ),
);

/**
 * The project was made as a VR version of another (M1.8c-1): the original, which its agent may read
 * but not change, and the instruction the host wrote for that agent, naming the original's folder.
 */
export const ProjectMadeFrom = projectEvent(
  'project.made_from',
  Type.Object({ original_project_id: ProjectId, instruction: Text(32000) }, strict),
);

/**
 * The host kept every file of the project's folder as a checkpoint (ADR 0036): hashes and counts
 * only, never contents. `execution_id` names the execution whose start or turn it follows, or is
 * null before a restore.
 */
export const ProjectCheckpointRecorded = projectEvent(
  'project.checkpoint.recorded',
  Type.Object(
    {
      checkpoint_id: CheckpointId,
      reason: CheckpointReason,
      execution_id: Nullable(ExecutionId),
      tree: Sha256Hex,
      files: Type.Integer({ minimum: 0 }),
      project_head: Nullable(GitObjectId),
      /** On `after_turn`: the runtime's id of the turn it follows, absent where it gave none. */
      turn_id: Type.Optional(NativeId),
    },
    strict,
  ),
);

/** The most checkpoints one prune names; a prune that would name more is recorded in parts. */
export const CHECKPOINTS_PRUNED_MAX = 1_000;

/**
 * The host stopped keeping these checkpoints, by the rule of ADR 0036's amendment of 2026-11-11 (the
 * newest ones and every one a restore, an undo, a task's copy or a task still at work needs are
 * kept): they can no longer be put back or compared. `kept` counts the project's checkpoints still
 * kept after it.
 */
export const ProjectCheckpointsPruned = projectEvent(
  'project.checkpoints.pruned',
  Type.Object(
    {
      checkpoint_ids: Type.Array(CheckpointId, { minItems: 1, maxItems: CHECKPOINTS_PRUNED_MAX }),
      kept: Type.Integer({ minimum: 0 }),
    },
    strict,
  ),
);

/** The host could not keep a checkpoint, so nothing from this point can be put back. */
export const ProjectCheckpointFailed = projectEvent(
  'project.checkpoint.failed',
  Type.Object(
    { reason: CheckpointReason, execution_id: Nullable(ExecutionId), failure: CheckpointFailure },
    strict,
  ),
);

/**
 * The host put every file back as the checkpoint holds it and then read every one again: each
 * equal, and nothing else outside what the checkpoint left out. Recorded only after that check.
 */
export const ProjectRestored = projectEvent(
  'project.restored',
  Type.Object(
    {
      checkpoint_id: CheckpointId,
      /** The checkpoint taken just before, which puts back what this restore changed. */
      before_restore_checkpoint_id: CheckpointId,
      tree: Sha256Hex,
      files_compared: Type.Integer({ minimum: 0 }),
      changed_back: PathList,
      removed: PathList,
      returned: PathList,
      /** Whether the project's own repository points at another commit than at the checkpoint. */
      project_head_moved: Nullable(Type.Boolean()),
    },
    strict,
  ),
);

/**
 * A restore that did not end with every file equal to the checkpoint. `written` says whether it
 * had changed any file before it stopped; `not_restored` lists what still differs, as far as known.
 */
export const ProjectRestoreIncomplete = projectEvent(
  'project.restore_incomplete',
  Type.Object(
    {
      checkpoint_id: CheckpointId,
      before_restore_checkpoint_id: Nullable(CheckpointId),
      written: Type.Boolean(),
      not_restored: PathList,
      failure: ErrorInfo,
    },
    strict,
  ),
);

/**
 * The host checked the project (ADR 0037): what it observed of the type check, the build and the
 * built copy in its sandboxed browser. `passed` and `drawn` never say the game works.
 */
export const ProjectCheckRecorded = projectEvent(
  'project.check.recorded',
  Type.Object(
    {
      check_id: CheckId,
      findings: CheckFindings,
      /** The task whose own copy was checked (ADR 0045); absent, the project's own folder. */
      workstream_id: Type.Optional(WorkstreamId),
    },
    strict,
  ),
);

/**
 * The person chose the project's own check (ADR 0055): the package manager, and each script with its
 * command as the project's `package.json` wrote it then, the project's own words, and that command's
 * SHA-256. A later choice replaces it.
 */
export const ProjectOwnCheckChosen = projectEvent(
  'project.own_check.chosen',
  Type.Object(
    {
      manager: OwnCheckManager,
      scripts: Type.Array(OwnCheckScript, { minItems: 1, maxItems: OWN_CHECK_SCRIPT_NAMES.length }),
    },
    strict,
  ),
);

/**
 * The host ran the project's own check (ADR 0055) inside the agents' closed sandbox: what it observed
 * of each chosen script, and what the check itself wrote. `first` marks the run right after the person
 * chose it. Never that the software works.
 */
export const ProjectOwnCheckRecorded = projectEvent(
  'project.own_check.recorded',
  Type.Object(
    {
      check_id: CheckId,
      first: Type.Boolean(),
      findings: OwnCheckFindings,
      /** The task whose own copy was checked (ADR 0045); absent, the project's own folder. */
      workstream_id: Type.Optional(WorkstreamId),
    },
    strict,
  ),
);

/**
 * The host serves the built copy of a passed check on 127.0.0.1 at `port`: recorded only once the
 * server itself listens there and answers with that copy. Clients open `http://127.0.0.1:port`,
 * never `localhost`, which can resolve to ::1, where another process may listen.
 */
export const ProjectPreviewStarted = projectEvent(
  'project.preview.started',
  Type.Object({ check_id: CheckId, port: Type.Integer({ minimum: 1, maximum: 65535 }) }, strict),
);

/** The project's preview stopped, and why. */
export const ProjectPreviewStopped = projectEvent(
  'project.preview.stopped',
  Type.Object({ check_id: CheckId, reason: PreviewStopReason }, strict),
);

/**
 * The checked game the project's preview serves is shared at `url`: by `way` `link` a public address
 * anyone can open, by `wifi` the Mac's address on its local network (F1, ADR 0054). Recorded only
 * once the address answers with that game, a link's over a verified `https://`.
 */
export const ProjectShareStarted = projectEvent(
  'project.share.started',
  Type.Object({ check_id: CheckId, url: ShareAddress, way: ShareWay }, strict),
);

/** The project's shared link stopped answering, and why. */
export const ProjectShareStopped = projectEvent(
  'project.share.stopped',
  Type.Object({ check_id: CheckId, reason: ShareStopReason }, strict),
);

/**
 * The checked game started playing on the Mac (ADR 0037): its page runs in the host's contained
 * headless browser, shown in Halcyonic's viewer. Started means the browser runs; not that anyone played.
 */
export const ProjectMacPlayStarted = projectEvent(
  'project.mac_play.started',
  Type.Object({ check_id: CheckId }, strict),
);

/** The game stopped playing on the Mac, and why. */
export const ProjectMacPlayStopped = projectEvent(
  'project.mac_play.stopped',
  Type.Object(
    {
      check_id: CheckId,
      reason: Type.Enum(['closed', 'preview_stopped', 'newer_check', 'host_stopped', 'failed']),
    },
    strict,
  ),
);

/**
 * The Mac itself read a world project's description file (ADR 0049): when the world starter was
 * applied, and after a turn in the project's folder, whenever what it found differs from what it
 * last recorded. The description is the file's content as the Mac validated it; the agent's own
 * account of its edit is never the source.
 */
export const ProjectWorldObserved = projectEvent('project.world.observed', WorldObserved);

export const WorkstreamCreated = workstreamEvent(
  'workstream.created',
  Type.Object({ title: Text(200), objective: Nullable(Text(4000)) }, strict),
);

/**
 * The task has a copy of its own of the project's folder in `directory`, a folder of Halcyonic's own
 * (ADR 0045), made just before its first start. `base_tree` is the SHA-256 tree of the copy as it
 * was made, in the project's checkpoint store, `files` the files it holds; the task's work is what
 * changes from it.
 */
export const WorkstreamCopyMade = workstreamEvent(
  'workstream.copy_made',
  Type.Object(
    { directory: HostPath, base_tree: Sha256Hex, files: Type.Integer({ minimum: 0 }) },
    strict,
  ),
);

/** The task could not have a copy of its own, so its start works in the project's own folder. */
export const WorkstreamCopyRefused = workstreamEvent(
  'workstream.copy_refused',
  Type.Object({ code: CopyRefusalCode, message: Text(2000) }, strict),
);

/**
 * The host read the task's copy (its tree `tree`) and found `changed` differing from the copy's
 * base: what an add would bring to the project, as far as the copy goes. `files` lists the same
 * files with how each changed, and `review` is the digest of the reading (ADR 0051), which an add
 * from a paired device carries back; a reading recorded before either carried neither.
 */
export const WorkstreamCopyChangesRead = workstreamEvent(
  'workstream.copy_changes_read',
  Type.Object(
    {
      tree: Sha256Hex,
      changed: PathList,
      files: Type.Optional(ChangedFiles),
      review: Type.Optional(ReviewDigest),
    },
    strict,
  ),
);

/**
 * The task's work is in the project's folder (ADR 0045): the host combined the copy (`task_tree`)
 * with the project's folder (`project_tree`) over the copy's base, wrote `merged_tree` into the
 * folder and read it again equal. `checkpoint_id` is the project's checkpoint taken just before;
 * `added` the files the add changed. The copy's base becomes `task_tree`.
 */
export const WorkstreamAdded = workstreamEvent(
  'workstream.added',
  Type.Object(
    {
      checkpoint_id: CheckpointId,
      task_tree: Sha256Hex,
      project_tree: Sha256Hex,
      merged_tree: Sha256Hex,
      added: PathList,
    },
    strict,
  ),
);

/**
 * An add found the task (`task_tree`) and the project (`project_tree`) changed the same places, so
 * nothing was written to the project's folder.
 */
export const WorkstreamAddConflicted = workstreamEvent(
  'workstream.add_conflicted',
  Type.Object(
    { task_tree: Sha256Hex, project_tree: Sha256Hex, conflicted: ConflictedFiles },
    strict,
  ),
);

/**
 * The host wrote the combined files, both sides marked where they met, into the task's copy (its
 * tree now `fitted_tree`), so the copy's base becomes `project_tree`, the project as the add read it.
 * `written` lists the files it changed in the copy.
 */
export const WorkstreamFitted = workstreamEvent(
  'workstream.fitted',
  Type.Object({ project_tree: Sha256Hex, fitted_tree: Sha256Hex, written: PathList }, strict),
);

export const ExecutionCreated = executionEvent(
  'execution.created',
  Type.Object(
    {
      runtime: RuntimeRef,
      instruction: Text(32000),
      /**
       * The project's folder as the host resolved it for this start, which the runtime was given
       * to work in; null for a runtime that uses no folder.
       */
      directory: Nullable(HostPath),
    },
    strict,
  ),
);

/**
 * The Mac read the execution's folder at the end of a turn and compared it with its reading before
 * (ADR 0038): created, changed and deleted files with sizes and line counts, observed by the Mac,
 * never claimed by the agent.
 */
export const ExecutionFilesObserved = executionEvent('execution.files.observed', FilesObserved);

/**
 * The host gave the agent, after the person's words, the first lines of the project's failed check
 * (its type check, its build or both, or a project's own check's failed script), marked as the Mac's
 * own check (FX1). Which check, which
 * steps, how many lines and bytes; never the text, which is neither journaled nor logged. Given
 * once per failed check, with the next instruction to an agent in the project.
 */
export const ExecutionCheckOutputGiven = executionEvent(
  'execution.check_output_given',
  Type.Object(
    {
      check_id: CheckId,
      /** `own_check`: a project's own check's failed script (ADR 0055). */
      parts: Type.Array(Type.Enum(['typecheck', 'build', 'own_check']), {
        minItems: 1,
        maxItems: 3,
      }),
      lines: Type.Integer({ minimum: 0, maximum: 80 }),
      bytes: Type.Integer({ minimum: 0, maximum: 16384 }),
    },
    strict,
  ),
);

/** The most instructions that may wait for one execution's turn to end (ADR 0056). */
export const QUEUED_INSTRUCTIONS_MAX = 3;

/**
 * The person's instruction is waiting for this execution's turn to end, on a runtime that cannot
 * steer (ADR 0056): it is sent after a turn that completed, `position` in line, or its command fails
 * with effect `none`, never sent. The words stay in the command's own `command.accepted`.
 */
export const ExecutionInstructionQueued = executionEvent(
  'execution.instruction.queued',
  Type.Object(
    {
      command_id: CommandId,
      position: Type.Integer({ minimum: 1, maximum: QUEUED_INSTRUCTIONS_MAX }),
    },
    strict,
  ),
);

/**
 * The host began sending a waiting instruction after a turn that completed (ADR 0056): from here it is
 * no longer waiting, so it can no longer be dropped or failed unsent; its command ends as any sent
 * instruction does, completed when the runtime confirms or failed with the runtime's own effect.
 */
export const ExecutionInstructionDequeued = executionEvent(
  'execution.instruction.dequeued',
  Type.Object({ command_id: CommandId }, strict),
);

/**
 * The host, not the person, sent the project's failed check back to this execution's agent as
 * its next instruction (ADR 0044): which check, which try of how many, and the instruction's own
 * command, recorded before that command is submitted. Delivery is that command's own outcome.
 */
export const ExecutionAutoFixSent = executionEvent(
  'execution.auto_fix.sent',
  Type.Object(
    {
      check_id: CheckId,
      attempt: Type.Integer({ minimum: 1, maximum: AUTO_FIX_CAP_MAX }),
      cap: Type.Integer({ minimum: 1, maximum: AUTO_FIX_CAP_MAX }),
      command_id: CommandId,
    },
    strict,
  ),
);

/**
 * Sending a failed check back to this execution's agent on its own ended, or did not begin
 * (`attempts` 0), and why (ADR 0044).
 */
export const ExecutionAutoFixEnded = executionEvent(
  'execution.auto_fix.ended',
  Type.Object(
    {
      check_id: CheckId,
      attempts: Type.Integer({ minimum: 0, maximum: AUTO_FIX_CAP_MAX }),
      cap: Type.Integer({ minimum: 1, maximum: AUTO_FIX_CAP_MAX }),
      reason: AutoFixEndReason,
    },
    strict,
  ),
);

/** The adapter reported that the runtime could not start the execution. */
export const ExecutionStartFailed = executionEvent(
  'execution.start_failed',
  Type.Object({ error: ErrorInfo }, strict),
);

/**
 * The control plane can no longer say what the execution is doing: a restart lost the connection
 * to its runtime (`control_plane_restarted`, until its session is re-attached), a restart's
 * session could not be re-attached and is gone for good (`session_lost`, ADR 0046), or a start
 * request timed out or failed in a way that may still have started the runtime. The execution
 * shows `unknown` until the runtime reports again.
 */
export const ExecutionStateUnknown = executionEvent(
  'execution.state_unknown',
  Type.Object(
    {
      code: Type.Union([
        Type.Literal('control_plane_restarted'),
        Type.Literal('start_outcome_unknown'),
        Type.Literal('session_lost'),
      ]),
      message: Text(2000),
    },
    strict,
  ),
);

/**
 * Who submitted the command, as the control plane authenticated it. Null for a command from inside
 * the control plane, and for commands journaled before principals were recorded, which the journal
 * reads as null.
 */
const CommandPrincipal = Nullable(Principal);

export const CommandAccepted = commandEvent(
  'command.accepted',
  Type.Object(
    {
      command: CommandEnvelope,
      policy: PolicyCategory,
      received_via: ReceivedVia,
      principal: CommandPrincipal,
    },
    strict,
  ),
);

export const CommandRejected = commandEvent(
  'command.rejected',
  Type.Object(
    {
      command: CommandEnvelope,
      rejection: CommandRejection,
      received_via: ReceivedVia,
      principal: CommandPrincipal,
    },
    strict,
  ),
);

/**
 * A device proved it saw the pairing code and received its credential (ADR 0017). Only the
 * credential's SHA-256 is kept; the credential exists only on the device.
 */
export const DevicePaired = controlPlaneEvent(
  'device.paired',
  Type.Object(
    {
      device_id: DeviceId,
      /** Self-declared by the device; recorded for display, never trusted. */
      label: DeviceLabel,
      credential_sha256: Sha256Hex,
      /** The TLS certificate the device saw and pinned. */
      certificate_sha256: Sha256Hex,
    },
    strict,
  ),
);

/** The device's credential is no longer accepted. */
export const DeviceRevoked = controlPlaneEvent(
  'device.revoked',
  Type.Object({ device_id: DeviceId, revoked_by: Principal }, strict),
);

/** The action was carried out, confirmed by the runtime where a runtime was involved. */
export const CommandCompleted = commandEvent(
  'command.completed',
  Type.Object(
    { command_id: CommandId, command_type: CommandType, result: Nullable(CommandResult) },
    strict,
  ),
);

export const CommandFailed = commandEvent(
  'command.failed',
  Type.Object(
    { command_id: CommandId, command_type: CommandType, failure: CommandFailure },
    strict,
  ),
);

export const WorkspaceRequestCreated = requestEvent(
  'workspace.request.created',
  Type.Object({ request: WorkspaceRequestView, principal: Nullable(Principal) }, strict),
);
export const WorkspaceRequestChanged = requestEvent(
  'workspace.request.changed',
  Type.Object(
    {
      request_id: WorkspaceRequestId,
      status: WorkspaceRequestStatus,
      reply: Nullable(WorkspaceRequestReply),
    },
    strict,
  ),
);
export const WorkspaceRequestStepRecorded = requestEvent(
  'workspace.request.step_recorded',
  Type.Object(
    {
      request_id: WorkspaceRequestId,
      step: WorkspaceRequestStep,
      prepared_command: Nullable(ExecutionSendInstructionCommand),
    },
    strict,
  ),
);

// Runtime observations -----------------------------------------------------------------------

export const ApprovalSubject = Type.Union([
  Type.Object(
    { kind: Type.Literal('tool_use'), tool_name: Text(128), summary: Text(2000) },
    strict,
  ),
]);
export type ApprovalSubject = Static<typeof ApprovalSubject>;

const TurnRef = { turn_id: Nullable(NativeId) };

export const RUNTIME_EVENT_PAYLOADS = {
  /**
   * The runtime created the native session or thread backing the execution. `confined` (S2) is
   * observed by the adapter: true when it started the execution inside Halcyonic's confinement of a
   * project (ADR 0041), false when not; absent from runtimes that do not record it and from events
   * before it existed, which read as not confined. `approval_level` (ADR 0043) is observed by the
   * adapter too: the level the runtime confirmed it started with; absent where it was not recorded,
   * which never reads as `ask`. `plan` is observed: true when the runtime reported the session in
   * its own plan mode; absent where it was not recorded, which never reads as a plan.
   * `unconfined_reason` (BOX-4) says why a start that asked for Halcyonic's sandbox ran without it,
   * or is not counted confined, in words the headset can show.
   */
  'runtime.execution.started': Type.Object(
    {
      native_id: Nullable(NativeId),
      confined: Type.Optional(Type.Boolean()),
      unconfined_reason: Type.Optional(Text(2000)),
      approval_level: Type.Optional(ApprovalLevel),
      plan: Type.Optional(Type.Boolean()),
    },
    strict,
  ),
  'runtime.turn.started': Type.Object({ ...TurnRef }, strict),
  'runtime.turn.completed': Type.Object({ ...TurnRef }, strict),
  'runtime.turn.failed': Type.Object({ ...TurnRef, error: ErrorInfo }, strict),
  'runtime.turn.interrupted': Type.Object({ ...TurnRef }, strict),
  'runtime.approval.requested': Type.Object(
    {
      approval_id: NativeId,
      subject: ApprovalSubject,
      /**
       * Whether the summary shows in full every command that would run, with how and where it
       * runs, and names every path a change would write. It does not include a change's content.
       * The adapter says so for what it reports; the control plane makes it false when it cuts the
       * summary to fit. Only a complete request can be approved.
       */
      complete: Type.Boolean(),
    },
    strict,
  ),
  /** Emitted once the runtime has applied the decision, not when a client sends it. */
  'runtime.approval.resolved': Type.Object(
    {
      approval_id: NativeId,
      decision: Type.Union([Type.Literal('approved'), Type.Literal('denied')]),
    },
    strict,
  ),
  /**
   * The agent asked the person something through the runtime's own question surface and waits for
   * the answer (ADR 0022). `answerable` is false when Halcyonic cannot carry an answer back: the
   * runtime offers no way, a question is secret, or the runtime asks in a form Halcyonic cannot
   * express. The person can still stop the execution.
   */
  'runtime.question.asked': Type.Object(
    { question_id: NativeId, prompts: QuestionPrompts, answerable: Type.Boolean() },
    strict,
  ),
  /**
   * The runtime settled the question: `answered` once it applied an answer, `dismissed` when it
   * was withdrawn without one. A turn that ends settles its questions without this event.
   */
  'runtime.question.resolved': Type.Object(
    {
      question_id: NativeId,
      outcome: Type.Union([Type.Literal('answered'), Type.Literal('dismissed')]),
    },
    strict,
  ),
  'runtime.tool.started': Type.Object(
    { tool_call_id: NativeId, tool_name: Text(128), title: Nullable(Text(500)) },
    strict,
  ),
  'runtime.tool.completed': Type.Object(
    {
      tool_call_id: NativeId,
      /** Added for exact live result correlation; older events/adapters may omit it. */
      turn_id: Type.Optional(Nullable(NativeId)),
      outcome: Type.Union([Type.Literal('succeeded'), Type.Literal('failed')]),
    },
    strict,
  ),
  'runtime.tool.result': ToolResult,
  /** Text the agent produced. Its content is a claim, so its provenance is `reported`. */
  'runtime.agent_message': Type.Object({ text: Text(32000) }, strict),
  'runtime.test_run.started': Type.Object(
    { test_run_id: NativeId, label: Nullable(Text(500)) },
    strict,
  ),
  'runtime.test_run.completed': Type.Object(
    {
      test_run_id: NativeId,
      outcome: Type.Union([
        Type.Literal('passed'),
        Type.Literal('failed'),
        Type.Literal('errored'),
      ]),
      summary: Nullable(Text(2000)),
    },
    strict,
  ),
  /** The adapter lost contact with the runtime and can no longer observe the execution. */
  'runtime.connection.lost': Type.Object({ reason: Text(2000) }, strict),
  /**
   * The adapter can observe the execution again after a lost connection, and reports what it found
   * in the events that follow.
   */
  'runtime.connection.restored': Type.Object({ reason: Text(2000) }, strict),
  /**
   * The runtime reported the model it uses for the execution, named as the runtime's model list
   * names it (ADR 0016). Taken from the runtime's own report, never from the model chosen.
   */
  'runtime.model.used': Type.Object({ model_ref: ModelRef }, strict),
  /**
   * The runtime left its plan mode in the execution's session (PLAN), as the adapter saw it confirm
   * before the instruction that carries the plan out was sent. From here the session changes files
   * as a start that was not a plan would, at the level it started with.
   */
  'runtime.plan.left': Type.Object({}, strict),
} as const;

export type RuntimeEventType = keyof typeof RUNTIME_EVENT_PAYLOADS;
export const RUNTIME_EVENT_TYPES = Object.keys(RUNTIME_EVENT_PAYLOADS) as RuntimeEventType[];

const P = RUNTIME_EVENT_PAYLOADS;
export const RuntimeExecutionStarted = runtimeEvent(
  'runtime.execution.started',
  P['runtime.execution.started'],
);
export const RuntimeTurnStarted = runtimeEvent('runtime.turn.started', P['runtime.turn.started']);
export const RuntimeTurnCompleted = runtimeEvent(
  'runtime.turn.completed',
  P['runtime.turn.completed'],
);
export const RuntimeTurnFailed = runtimeEvent('runtime.turn.failed', P['runtime.turn.failed']);
export const RuntimeTurnInterrupted = runtimeEvent(
  'runtime.turn.interrupted',
  P['runtime.turn.interrupted'],
);
export const RuntimeApprovalRequested = runtimeEvent(
  'runtime.approval.requested',
  P['runtime.approval.requested'],
);
export const RuntimeApprovalResolved = runtimeEvent(
  'runtime.approval.resolved',
  P['runtime.approval.resolved'],
);
export const RuntimeQuestionAsked = runtimeEvent(
  'runtime.question.asked',
  P['runtime.question.asked'],
);
export const RuntimeQuestionResolved = runtimeEvent(
  'runtime.question.resolved',
  P['runtime.question.resolved'],
);
export const RuntimeToolStarted = runtimeEvent('runtime.tool.started', P['runtime.tool.started']);
export const RuntimeToolCompleted = runtimeEvent(
  'runtime.tool.completed',
  P['runtime.tool.completed'],
);
export const RuntimeToolResult = runtimeEvent('runtime.tool.result', P['runtime.tool.result']);
export const RuntimeAgentMessage = runtimeEvent(
  'runtime.agent_message',
  P['runtime.agent_message'],
);
export const RuntimeTestRunStarted = runtimeEvent(
  'runtime.test_run.started',
  P['runtime.test_run.started'],
);
export const RuntimeTestRunCompleted = runtimeEvent(
  'runtime.test_run.completed',
  P['runtime.test_run.completed'],
);
export const RuntimeConnectionLost = runtimeEvent(
  'runtime.connection.lost',
  P['runtime.connection.lost'],
);
export const RuntimeConnectionRestored = runtimeEvent(
  'runtime.connection.restored',
  P['runtime.connection.restored'],
);
export const RuntimeModelUsed = runtimeEvent('runtime.model.used', P['runtime.model.used']);
export const RuntimePlanLeft = runtimeEvent('runtime.plan.left', P['runtime.plan.left']);

export const EVENT_VARIANTS = [
  ProjectCreated,
  ProjectLocationSet,
  ProjectStarterApplied,
  ProjectMadeFrom,
  ProjectCatalogLinked,
  ProjectCatalogUnlinked,
  ProjectCheckpointRecorded,
  ProjectCheckpointFailed,
  ProjectCheckpointsPruned,
  ExecutionInstructionQueued,
  ExecutionInstructionDequeued,
  ProjectRestored,
  ProjectRestoreIncomplete,
  ProjectCheckRecorded,
  ProjectOwnCheckChosen,
  ProjectOwnCheckRecorded,
  ProjectPreviewStarted,
  ProjectPreviewStopped,
  ProjectMacPlayStarted,
  ProjectMacPlayStopped,
  ProjectShareStarted,
  ProjectShareStopped,
  ProjectWorldObserved,
  WorkstreamCreated,
  WorkstreamCopyMade,
  WorkstreamCopyRefused,
  WorkstreamCopyChangesRead,
  WorkstreamAdded,
  WorkstreamAddConflicted,
  WorkstreamFitted,
  ExecutionCreated,
  ExecutionFilesObserved,
  ExecutionCheckOutputGiven,
  ExecutionAutoFixSent,
  ExecutionAutoFixEnded,
  ExecutionStartFailed,
  ExecutionStateUnknown,
  CommandAccepted,
  CommandRejected,
  CommandCompleted,
  CommandFailed,
  DevicePaired,
  DeviceRevoked,
  WorkspaceRequestCreated,
  WorkspaceRequestChanged,
  WorkspaceRequestStepRecorded,
  RuntimeExecutionStarted,
  RuntimeTurnStarted,
  RuntimeTurnCompleted,
  RuntimeTurnFailed,
  RuntimeTurnInterrupted,
  RuntimeApprovalRequested,
  RuntimeApprovalResolved,
  RuntimeQuestionAsked,
  RuntimeQuestionResolved,
  RuntimeToolStarted,
  RuntimeToolCompleted,
  RuntimeToolResult,
  RuntimeAgentMessage,
  RuntimeTestRunStarted,
  RuntimeTestRunCompleted,
  RuntimeConnectionLost,
  RuntimeConnectionRestored,
  RuntimeModelUsed,
  RuntimePlanLeft,
] as const;

export const EventEnvelope = Type.Union([...EVENT_VARIANTS]);
export type EventEnvelope = Static<typeof EventEnvelope>;
export type EventType = EventEnvelope['event_type'];
export type EventOf<T extends EventType> = Extract<EventEnvelope, { event_type: T }>;
export type RuntimeEvent = Extract<EventEnvelope, { source: { kind: 'runtime' } }>;
export type ControlPlaneEvent = Exclude<EventEnvelope, RuntimeEvent>;
export type DeviceEvent = Extract<EventEnvelope, { event_type: `device.${string}` }>;

/** Every device event type. */
export const DEVICE_EVENT_TYPES: readonly DeviceEvent['event_type'][] = [
  'device.paired',
  'device.revoked',
];

/**
 * Device events are the control plane's own record of who may reach it. They carry no work:
 * realtime clients never receive them, and paired devices never read them
 * (docs/internal/architecture/REALTIME.md).
 */
export function isDeviceEvent(event: EventEnvelope): event is DeviceEvent {
  return (DEVICE_EVENT_TYPES as readonly string[]).includes(event.event_type);
}

/** An event as stored in the journal, with the position the journal assigned to it. */
export const StoredEvent = Type.Object(
  { position: Type.Integer({ minimum: 1 }), event: EventEnvelope },
  strict,
);
export type StoredEvent = Static<typeof StoredEvent>;

export { Timestamp };

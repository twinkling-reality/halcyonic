import Type, { type Static, type TSchema } from 'typebox';
import {
  CommandEnvelope,
  CommandFailure,
  CommandId,
  CommandRejection,
  CommandResult,
  CommandType,
  PolicyCategory,
  ReceivedVia,
} from './commands.ts';
import { DeviceLabel, Principal, Sha256Hex } from './devices.ts';
import { HostPath, ProjectLocation } from './locations.ts';
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
import { ModelRef, RuntimeRef } from './runtime.ts';
import { EVENT_SCHEMA_VERSION } from './versions.ts';

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

export const WorkstreamCreated = workstreamEvent(
  'workstream.created',
  Type.Object({ title: Text(200), objective: Nullable(Text(4000)) }, strict),
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

/** The adapter reported that the runtime could not start the execution. */
export const ExecutionStartFailed = executionEvent(
  'execution.start_failed',
  Type.Object({ error: ErrorInfo }, strict),
);

/**
 * The control plane can no longer say what the execution is doing: a restart lost the connection
 * to its runtime, or a start request timed out or failed in a way that may still have started
 * the runtime. The execution shows `unknown` until the runtime reports again.
 */
export const ExecutionStateUnknown = executionEvent(
  'execution.state_unknown',
  Type.Object(
    {
      code: Type.Union([
        Type.Literal('control_plane_restarted'),
        Type.Literal('start_outcome_unknown'),
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
  /** The runtime created the native session or thread backing the execution. */
  'runtime.execution.started': Type.Object({ native_id: Nullable(NativeId) }, strict),
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
      outcome: Type.Union([Type.Literal('succeeded'), Type.Literal('failed')]),
    },
    strict,
  ),
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

export const EVENT_VARIANTS = [
  ProjectCreated,
  ProjectLocationSet,
  WorkstreamCreated,
  ExecutionCreated,
  ExecutionStartFailed,
  ExecutionStateUnknown,
  CommandAccepted,
  CommandRejected,
  CommandCompleted,
  CommandFailed,
  DevicePaired,
  DeviceRevoked,
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
  RuntimeAgentMessage,
  RuntimeTestRunStarted,
  RuntimeTestRunCompleted,
  RuntimeConnectionLost,
  RuntimeConnectionRestored,
  RuntimeModelUsed,
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

import type {
  CommandId,
  ControlPlaneEvent,
  EventOf,
  RuntimeId,
  Timestamp,
} from '@halcyonic/contracts';
import type { ExecutionContext, ObservationSink } from '@halcyonic/runtime-core';
import type { Logger } from '../logger.ts';
import type { EventDraft, Recorder } from './recorder.ts';
import { type Redaction, redaction as redactionOf, withinLimit } from './redaction.ts';

type ControlPlaneEventType = ControlPlaneEvent['event_type'];
type ScopeOf<T extends ControlPlaneEventType> = Pick<
  EventOf<T>,
  'project_id' | 'workstream_id' | 'execution_id'
>;

export interface Cause {
  readonly correlation_id: string | null;
  readonly causation_id: string | null;
}

export const NO_CAUSE: Cause = { correlation_id: null, causation_id: null };

export function causedBy(commandId: CommandId): Cause {
  return { correlation_id: commandId, causation_id: commandId };
}

/**
 * Builds a control plane event draft. TypeScript cannot correlate a generic event type with its
 * payload inside the union, so the object is asserted here, in one place, and the recorder
 * validates it against the contract before anything is written.
 */
export function controlPlaneDraft<T extends ControlPlaneEventType>(
  eventType: T,
  scope: ScopeOf<T>,
  payload: EventOf<T>['payload'],
  occurredAt: Timestamp,
  cause: Cause,
): EventDraft {
  return {
    event_type: eventType,
    project_id: scope.project_id,
    workstream_id: scope.workstream_id,
    execution_id: scope.execution_id,
    source: { kind: 'control_plane' },
    source_native_id: null,
    sequence: null,
    occurred_at: occurredAt,
    correlation_id: cause.correlation_id,
    causation_id: cause.causation_id,
    provenance: { epistemic: 'observed', native_type: null },
    payload,
  } as unknown as EventDraft;
}

/**
 * Turns an adapter's observations into journaled runtime events for one execution. An invalid
 * observation is an adapter defect: it is logged with its identifiers and dropped, never
 * repaired into something that looks valid.
 */
export function createObservationSink(
  recorder: Recorder,
  logger: Logger,
  execution: ExecutionContext,
  runtimeId: RuntimeId,
  redaction: Redaction = redactionOf(),
): ObservationSink {
  return (observation) => {
    const draft = {
      event_type: observation.type,
      project_id: execution.project_id,
      workstream_id: execution.workstream_id,
      execution_id: execution.execution_id,
      source: { kind: 'runtime', runtime_id: runtimeId },
      source_native_id: observation.native_event_id,
      sequence: observation.sequence,
      occurred_at: observation.occurred_at,
      correlation_id: null,
      causation_id: null,
      provenance: observation.provenance,
      payload: withoutCredentials(observation, redaction),
    } as unknown as EventDraft;
    try {
      recorder.record(draft);
    } catch (error) {
      logger.error(
        {
          err: error,
          execution_id: execution.execution_id,
          runtime_id: runtimeId,
          observation_type: observation.type,
        },
        'runtime observation rejected',
      );
    }
  };
}

/**
 * An observation's payload with credentials taken out, cut to the journal's limit after, since
 * "[redacted]" can be longer than what it replaced. The runtime's error text, a turn's failure and
 * why the connection was lost or restored, loses what Halcyonic holds and every credential shape.
 * What a person must read as given, a tool's title, what an approval asks for, a test run's label
 * and summary, loses only exact copies of what Halcyonic holds, so the command a person approves
 * is never guessed away. Everything else, the agent's own account included, is reported as given.
 */
function withoutCredentials(
  observation: Parameters<ObservationSink>[0],
  redaction: Redaction,
): unknown {
  const payload = observation.payload as Record<string, unknown>;
  const held = (field: string, limit: number) =>
    typeof payload[field] === 'string'
      ? { ...payload, [field]: redaction.held(payload[field], limit) }
      : payload;
  switch (observation.type) {
    case 'runtime.turn.failed': {
      const error = payload.error as { message?: unknown } | undefined;
      return typeof error?.message === 'string'
        ? {
            ...payload,
            error: { ...error, message: withinLimit(redaction.errorText(error.message)) },
          }
        : payload;
    }
    case 'runtime.connection.lost':
    case 'runtime.connection.restored':
      return typeof payload.reason === 'string'
        ? { ...payload, reason: withinLimit(redaction.errorText(payload.reason)) }
        : payload;
    case 'runtime.approval.requested': {
      const subject = payload.subject as { summary?: unknown } | undefined;
      return typeof subject?.summary === 'string'
        ? { ...payload, subject: { ...subject, summary: redaction.held(subject.summary, 2000) } }
        : payload;
    }
    case 'runtime.tool.started':
      return held('title', 500);
    case 'runtime.test_run.started':
      return held('label', 500);
    case 'runtime.test_run.completed':
      return held('summary', 2000);
    default:
      return payload;
  }
}

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
import { withinLimit } from './redaction.ts';

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
  redact: (text: string) => string = (text) => text,
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
      payload: withoutCredentials(observation, redact),
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
 * An observation's payload with credentials taken out of the runtime's error text, cut to the
 * journal's limit after, since "[redacted]" can be longer than what it replaced: a turn's failure
 * and why the connection was lost or restored. Everything else is the runtime's account as given.
 */
function withoutCredentials(
  observation: Parameters<ObservationSink>[0],
  redact: (text: string) => string,
): unknown {
  const payload = observation.payload as Record<string, unknown>;
  switch (observation.type) {
    case 'runtime.turn.failed': {
      const error = payload.error as { message?: unknown } | undefined;
      return typeof error?.message === 'string'
        ? { ...payload, error: { ...error, message: withinLimit(redact(error.message)) } }
        : payload;
    }
    case 'runtime.connection.lost':
    case 'runtime.connection.restored':
      return typeof payload.reason === 'string'
        ? { ...payload, reason: withinLimit(redact(payload.reason)) }
        : payload;
    default:
      return payload;
  }
}

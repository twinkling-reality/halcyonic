import {
  COMMAND_SCHEMA_VERSION,
  type CommandId,
  type EventEnvelope,
  type Timestamp,
} from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import type { Clock } from '@halcyonic/runtime-core';
import type { IdGenerator } from '../ids.ts';
import type { Logger } from '../logger.ts';
import type { CommandService } from './command-service.ts';
import { APPROVABLE_LIMIT, type Cause, controlPlaneDraft } from './drafts.ts';
import type { EventPublisher } from './publisher.ts';
import type { Recorder } from './recorder.ts';

const CLIENT = { name: 'halcyonic-control-plane', version: null, device_label: null } as const;

/**
 * What the agent is told, where its app carries a denial's words, when its request was too long to show: why, and
 * nothing more. No way round is suggested while a file write's approval shows only its path (the lead, 2026-11-13
 * 19:46Z): "write it as a file" comes back only once a write's card shows what it writes (AUDIT2 W1, AUDIT3).
 */
export function tooLongWords(length: number): string {
  return `This request is ${length} characters long, and a person can be shown at most ${APPROVABLE_LIMIT} whole, so it was not done.`;
}

/** What happens to a request: the host answers it, the host denies it for length, or a person decides. */
export type RequestOutcome = 'answer' | 'deny' | 'person';

/**
 * The ONE decision for each request (AUDIT3 M1, 2026-11-13), which the host's answers and its denials both act on, so
 * neither ordering of their listeners lets both act or neither: a confined app's own work the no-ask rule takes is
 * answered by the host whatever its length, since no person reads it and the closed profile holds it (`answer`); a
 * request that would reach a person and is too long to show whole is denied with its reason (`deny`); anything else
 * reaches the person.
 */
export function requestOutcome(event: EventEnvelope, hostAnswers: boolean): RequestOutcome {
  if (event.event_type !== 'runtime.approval.requested') return 'person';
  if (hostAnswers) return 'answer';
  return event.payload.too_long === undefined ? 'person' : 'deny';
}

export interface HostDenialsDeps {
  readonly projection: Projection;
  readonly recorder: Recorder;
  readonly publisher: EventPublisher;
  readonly commands: Pick<CommandService, 'submit'>;
  /** Whether the runtime declares that a denial's words reach its agent (`delivers_denial_message`). */
  readonly delivers: (runtimeId: string) => boolean;
  /** The one decision for a request (`requestOutcome`); this acts only on `deny`. */
  readonly outcome: (event: EventEnvelope) => RequestOutcome;
  readonly ids: IdGenerator;
  readonly clock: Clock;
  readonly logger: Logger;
  /** Whether the journal is the person's own (`live`); a fixture's journal is never appended to. */
  readonly live: boolean;
}

/**
 * The host's own denial of a request a person cannot be shown whole (`too_long` on the request, set where the control
 * plane fits it), for every run, confined or not (AUDIT3, 2026-11-13): it records `execution.approval.denied_by_host`
 * first, then denies through the same command a press sends, with words the agent can act on where its app carries
 * them. So no card that can only be denied is left waiting. A denial that is not accepted leaves the card, as before.
 */
export class HostDenials {
  readonly #deps: HostDenialsDeps;
  #unsubscribe: (() => void) | null = null;
  #closed = false;

  constructor(deps: HostDenialsDeps) {
    this.#deps = deps;
  }

  attach(): void {
    if (!this.#deps.live) return;
    this.#unsubscribe = this.#deps.publisher.subscribe(({ event }) => this.#on(event));
  }

  close(): void {
    this.#closed = true;
    this.#unsubscribe?.();
    this.#unsubscribe = null;
  }

  #on(event: EventEnvelope): void {
    if (this.#closed || event.event_type !== 'runtime.approval.requested') return;
    const tooLong = event.payload.too_long;
    if (tooLong === undefined || event.execution_id === null) return;
    if (this.#deps.outcome(event) !== 'deny') return;
    const approvalId = event.payload.approval_id;
    // After the request is projected, so the denial finds it pending.
    queueMicrotask(() => {
      if (this.#closed) return;
      try {
        this.#deny(event, approvalId, tooLong.length);
      } catch {
        // Not the error itself: it can hold the request. The card stays for the person.
        this.#deps.logger.error({ execution_id: event.execution_id }, 'host denial failed');
      }
    });
  }

  #deny(event: EventEnvelope, approvalId: string, length: number): void {
    const executionId = event.execution_id;
    if (executionId === null) return;
    const view = this.#deps.projection.execution(executionId);
    if (view?.pending_approvals.some((each) => each.approval_id === approvalId) !== true) return;
    const delivered = this.#deps.delivers(view.runtime.runtime_id);
    const scope = {
      project_id: event.project_id,
      workstream_id: event.workstream_id,
      execution_id: executionId,
    };
    const cause = {
      correlation_id: event.correlation_id,
      causation_id: event.event_id,
    } as unknown as Cause;
    this.#deps.recorder.record(
      controlPlaneDraft(
        'execution.approval.denied_by_host',
        scope as never,
        { approval_id: approvalId, because: 'too_long_to_show', length, delivered },
        this.#now(),
        cause,
      ),
    );
    const commandId = this.#deps.ids.next() as CommandId;
    const outcome = this.#deps.commands.submit(
      {
        schema_version: COMMAND_SCHEMA_VERSION,
        command_id: commandId,
        issued_at: this.#now(),
        client: CLIENT,
        command_type: 'execution.respond_to_approval',
        payload: {
          execution_id: executionId,
          approval_id: approvalId,
          decision: 'deny',
          message: tooLongWords(length),
        },
      },
      'internal',
      null,
    );
    this.#deps.logger.info(
      {
        execution_id: executionId,
        command_id: commandId,
        delivered,
        disposition: outcome.disposition,
      },
      'denied a request too long to show',
    );
  }

  #now(): Timestamp {
    return this.#deps.clock.now().toISOString() as Timestamp;
  }
}

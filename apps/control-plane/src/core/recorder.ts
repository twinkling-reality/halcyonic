import {
  EVENT_SCHEMA_VERSION,
  type EventEnvelope,
  parseEventEnvelope,
  type ValidationIssue,
} from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import type { Clock } from '@halcyonic/runtime-core';
import type { IdGenerator } from '../ids.ts';
import type { EventJournal } from '../journal/journal.ts';
import type { Logger } from '../logger.ts';
import type { EventPublisher } from './publisher.ts';

type DistributiveOmit<T, K extends PropertyKey> = T extends unknown ? Omit<T, K> : never;

/** An event before the recorder assigns its identity and ingestion time. */
export type EventDraft = DistributiveOmit<
  EventEnvelope,
  'schema_version' | 'event_id' | 'ingested_at'
>;

export type RecordResult =
  | { readonly status: 'recorded'; readonly position: number; readonly event: EventEnvelope }
  | { readonly status: 'duplicate'; readonly position: number };

export class InvalidEventError extends Error {
  readonly issues: ValidationIssue[];

  constructor(issues: ValidationIssue[]) {
    super(
      `event does not match the contract: ${issues.map((i) => `${i.path} ${i.message}`).join('; ')}`,
    );
    this.name = 'InvalidEventError';
    this.issues = issues;
  }
}

export interface RecorderDeps {
  readonly journal: EventJournal;
  readonly projection: Projection;
  readonly publisher: EventPublisher;
  readonly ids: IdGenerator;
  readonly clock: Clock;
  readonly logger: Logger;
}

/**
 * The only write path. Every event, whether from the control plane, a runtime adapter or a
 * fixture, is validated, appended, applied to the projection and published, in that order and
 * without yielding, so readers never observe a journal and projection that disagree.
 */
export class Recorder {
  readonly #deps: RecorderDeps;

  constructor(deps: RecorderDeps) {
    this.#deps = deps;
  }

  /** Completes a draft with identity and ingestion time, then records it. */
  record(draft: EventDraft): RecordResult {
    return this.import({
      schema_version: EVENT_SCHEMA_VERSION,
      event_id: this.#deps.ids.next(),
      ...draft,
      ingested_at: this.#deps.clock.now().toISOString(),
    });
  }

  /** Records a complete event, for example one read from a fixture trace. */
  import(candidate: unknown): RecordResult {
    const parsed = parseEventEnvelope(candidate);
    if (!parsed.ok) throw new InvalidEventError(parsed.issues);
    const event = parsed.value;

    const appended = this.#deps.journal.append(event);
    if (appended.status === 'duplicate') {
      this.#deps.logger.debug(
        { event_id: event.event_id, position: appended.position, matched_on: appended.matchedOn },
        'duplicate event ignored',
      );
      return { status: 'duplicate', position: appended.position };
    }

    const { changes, notes } = this.#deps.projection.apply({ position: appended.position, event });
    for (const note of notes) {
      this.#deps.logger.warn(
        {
          note: note.code,
          event_id: note.event_id,
          position: appended.position,
          detail: note.message,
        },
        'projection did not apply part of an event',
      );
    }
    this.#deps.publisher.publish({ position: appended.position, event, changes });
    return { status: 'recorded', position: appended.position, event };
  }
}

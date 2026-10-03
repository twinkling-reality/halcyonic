import {
  EVENT_SCHEMA_VERSION,
  type EventEnvelope,
  parseEventEnvelope,
  type ValidationIssue,
} from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import type { Clock } from '@halcyonic/runtime-core';
import type { IdGenerator } from '../ids.ts';
import type { AppendResult, EventJournal } from '../journal/journal.ts';
import { type Logger, OwnWordsError } from '../logger.ts';
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

export class InvalidEventError extends OwnWordsError {
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
      this.#reportDuplicate(event, appended);
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

  /**
   * A record delivered again, or an event imported again, is expected and ignored quietly. A
   * native id that already names an event of another execution or type is not a re-delivery:
   * the runtime reused the id and this event is lost, so that is a warning. The log carries
   * identifiers only, never payload text.
   */
  #reportDuplicate(
    event: EventEnvelope,
    duplicate: Extract<AppendResult, { status: 'duplicate' }>,
  ): void {
    const { existing } = duplicate;
    const reused =
      duplicate.matchedOn === 'source_native_id' &&
      (existing.executionId !== event.execution_id || existing.eventType !== event.event_type);
    if (!reused) {
      this.#deps.logger.debug(
        { event_id: event.event_id, position: duplicate.position, matched_on: duplicate.matchedOn },
        'duplicate event ignored',
      );
      return;
    }
    this.#deps.logger.warn(
      {
        runtime_id: event.source.kind === 'runtime' ? event.source.runtime_id : null,
        source_native_id: event.source_native_id,
        event_id: event.event_id,
        event_type: event.event_type,
        execution_id: event.execution_id,
        existing_event_id: existing.eventId,
        existing_event_type: existing.eventType,
        existing_execution_id: existing.executionId,
        existing_position: duplicate.position,
      },
      'runtime reused a native event id; the event was not journaled',
    );
  }
}

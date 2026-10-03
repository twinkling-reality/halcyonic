import type { EventEnvelope, JournalInfo, StoredEvent } from '@halcyonic/contracts';
import { OwnWordsError } from '../logger.ts';

export type AppendResult =
  | { readonly status: 'appended'; readonly position: number }
  | {
      readonly status: 'duplicate';
      /** Position of the event already in the journal. */
      readonly position: number;
      /** Which identity matched an event already in the journal. */
      readonly matchedOn: 'event_id' | 'source_native_id';
      /**
       * The event already in the journal, so a caller can tell a record delivered again from an
       * id its source reused for a different record.
       */
      readonly existing: {
        readonly eventId: string;
        readonly eventType: string;
        readonly executionId: string | null;
      };
    };

export interface ReadOptions {
  /** Return events with a position greater than this. */
  readonly after: number;
  readonly limit: number;
  readonly workstreamId?: string | null;
  /** Event types to leave out. `limit` counts only the events returned. */
  readonly excludeEventTypes?: readonly string[];
}

/**
 * The append-only record of everything Halcyonic knows. Events are never updated or deleted;
 * current state is a projection of this log. Implementations are synchronous so that append,
 * projection and publication happen as one uninterrupted step in the single-process control plane.
 */
export interface EventJournal {
  readonly info: JournalInfo;
  /** Position of the last event, 0 when empty. */
  head(): number;
  append(event: EventEnvelope): AppendResult;
  read(options: ReadOptions): StoredEvent[];
  /** Every event in position order, for rebuilding the projection. */
  readAll(): Iterable<StoredEvent>;
  close(): void;
}

export class JournalError extends OwnWordsError {
  constructor(message: string) {
    super(message);
    this.name = 'JournalError';
  }
}

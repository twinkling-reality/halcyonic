import type { EventEnvelope, JournalInfo, StoredEvent } from '@halcyonic/contracts';

export type AppendResult =
  | { readonly status: 'appended'; readonly position: number }
  | {
      readonly status: 'duplicate';
      readonly position: number;
      /** Which identity matched an event already in the journal. */
      readonly matchedOn: 'event_id' | 'source_native_id';
    };

export interface ReadOptions {
  /** Return events with a position greater than this. */
  readonly after: number;
  readonly limit: number;
  readonly workstreamId?: string | null;
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

export class JournalError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'JournalError';
  }
}

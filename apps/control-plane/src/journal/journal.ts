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
  /** True once `close` has run: nothing more can be appended or read. */
  readonly closed: boolean;
  /** Position of the last event, 0 when empty. */
  head(): number;
  append(event: EventEnvelope): AppendResult;
  read(options: ReadOptions): StoredEvent[];
  /** One indexed immutable event, not a history scan. Authorization belongs to the caller. */
  event(eventId: string): StoredEvent | undefined;
  /** Every event in position order, for rebuilding the projection; only those after `after` when given. */
  readAll(after?: number): Iterable<StoredEvent>;
  /**
   * The checkpoint of the projection kept beside the events (ADR 0050), or null when there is
   * none. Only a cache of the events: a reader checks it against them before it trusts it.
   */
  readCheckpoint(): StoredCheckpoint | null;
  /** Replaces the checkpoint, in one transaction. The events are never touched. */
  writeCheckpoint(checkpoint: CheckpointToWrite): void;
  close(): void;
}

export interface CheckpointToWrite {
  /** The position the state was exported at, and the id of the event there. */
  readonly position: number;
  readonly eventId: string;
  /** What wrote the state: the code that folds events and how it serializes them. */
  readonly codeIdentity: string;
  readonly state: Uint8Array;
  readonly writtenAt: string;
}

export interface StoredCheckpoint extends CheckpointToWrite {
  /** The journal it was written for, as its own row says. */
  readonly journalId: string;
  /** The SHA-256 of `state` when it was written, as its row says. */
  readonly stateSha256: string;
}

export class JournalError extends OwnWordsError {
  constructor(message: string) {
    super(message);
    this.name = 'JournalError';
  }
}

import { chmodSync, closeSync, openSync } from 'node:fs';
import { DatabaseSync, type StatementSync } from 'node:sqlite';
import {
  type EventEnvelope,
  type JournalId,
  type JournalInfo,
  parseEventEnvelope,
  type StoredEvent,
} from '@halcyonic/contracts';
import type { IdGenerator } from '../ids.ts';
import { type AppendResult, type EventJournal, JournalError, type ReadOptions } from './journal.ts';

/**
 * Forward-only migrations; `PRAGMA user_version` records how many have run. Never edit a
 * migration that has shipped: add a new one. Each runs in its own transaction.
 */
const MIGRATIONS: readonly string[] = [
  `
  CREATE TABLE journal_meta (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
  ) STRICT;

  CREATE TABLE events (
    position INTEGER PRIMARY KEY AUTOINCREMENT,
    event_id TEXT NOT NULL UNIQUE,
    event_type TEXT NOT NULL,
    project_id TEXT,
    workstream_id TEXT,
    execution_id TEXT,
    source_kind TEXT NOT NULL,
    -- The runtime id for runtime events, '' for the control plane.
    source_id TEXT NOT NULL,
    source_native_id TEXT,
    occurred_at TEXT NOT NULL,
    ingested_at TEXT NOT NULL,
    envelope TEXT NOT NULL
  ) STRICT;

  CREATE UNIQUE INDEX events_source_native_id
    ON events (source_kind, source_id, source_native_id)
    WHERE source_native_id IS NOT NULL;
  CREATE INDEX events_workstream ON events (workstream_id, position);
  CREATE INDEX events_execution ON events (execution_id, position);
  `,
  // ADR 0016: `execution.start` carries a `model_ref`. A command stored before it chose no model,
  // which the contract now says with an explicit null.
  `
  UPDATE events
  SET envelope = json_set(envelope, '$.payload.command.payload.model_ref', json('null'))
  WHERE event_type IN ('command.accepted', 'command.rejected')
    AND json_extract(envelope, '$.payload.command.command_type') = 'execution.start'
    AND json_type(envelope, '$.payload.command.payload.model_ref') IS NULL;
  `,
  // ADR 0017: command events record who sent them. Nobody knows who sent a command stored before,
  // which the contract says with a null principal. Builds of the pairing work from before it met
  // migration 2 stamped version 2 on their journals without running it, so this runs its update
  // again. Both updates change only events without the field, so running one twice is harmless.
  `
  UPDATE events
  SET envelope = json_set(envelope, '$.payload.command.payload.model_ref', json('null'))
  WHERE event_type IN ('command.accepted', 'command.rejected')
    AND json_extract(envelope, '$.payload.command.command_type') = 'execution.start'
    AND json_type(envelope, '$.payload.command.payload.model_ref') IS NULL;
  UPDATE events
  SET envelope = json_set(envelope, '$.payload.principal', json('null'))
  WHERE event_type IN ('command.accepted', 'command.rejected')
    AND json_type(envelope, '$.payload.principal') IS NULL;
  `,
  // ADR 0020: a project is bound to a folder. A project stored before was created without one, and
  // a project.create command stored before asked for none. An execution stored before recorded no
  // folder, so its null means not recorded rather than none.
  `
  UPDATE events
  SET envelope = json_set(envelope, '$.payload.location', json('null'))
  WHERE event_type = 'project.created'
    AND json_type(envelope, '$.payload.location') IS NULL;
  UPDATE events
  SET envelope = json_set(envelope, '$.payload.command.payload.location', json('null'))
  WHERE event_type IN ('command.accepted', 'command.rejected')
    AND json_extract(envelope, '$.payload.command.command_type') = 'project.create'
    AND json_type(envelope, '$.payload.command.payload.location') IS NULL;
  UPDATE events
  SET envelope = json_set(envelope, '$.payload.directory', json('null'))
  WHERE event_type = 'execution.created'
    AND json_type(envelope, '$.payload.directory') IS NULL;
  `,
];

export const JOURNAL_SCHEMA_VERSION = MIGRATIONS.length;

export interface OpenJournalOptions {
  /** A file path, or `:memory:` for a journal that lives only as long as the process. */
  readonly path: string;
  /** Recorded only when the journal is created; an existing journal keeps its origin. */
  readonly originIfNew: JournalInfo['origin'];
  readonly ids: IdGenerator;
}

export function openSqliteJournal(options: OpenJournalOptions): EventJournal {
  if (options.path !== ':memory:') restrictToOwner(options.path);
  const db = new DatabaseSync(options.path);
  try {
    db.exec('PRAGMA journal_mode = WAL');
    // FULL makes every committed event durable across power loss, not only process crashes.
    db.exec('PRAGMA synchronous = FULL');
    db.exec('PRAGMA busy_timeout = 5000');
    migrate(db);
    const info = readOrCreateInfo(db, options);
    return new SqliteJournal(db, info);
  } catch (error) {
    db.close();
    throw error;
  }
}

/**
 * The journal holds instructions and agent output, so it is readable by its owner only. SQLite
 * creates the `-wal` and `-shm` files with the database file's mode, so fixing the main file
 * before opening covers all three.
 */
function restrictToOwner(path: string): void {
  closeSync(openSync(path, 'a', 0o600));
  chmodSync(path, 0o600);
}

function migrate(db: DatabaseSync): void {
  const row = db.prepare('PRAGMA user_version').get() as { user_version: number } | undefined;
  const current = row?.user_version ?? 0;
  if (current > MIGRATIONS.length) {
    throw new JournalError(
      `journal schema version ${current} is newer than this build supports (${MIGRATIONS.length}); refusing to open it`,
    );
  }
  for (let version = current; version < MIGRATIONS.length; version += 1) {
    db.exec('BEGIN IMMEDIATE');
    try {
      db.exec(MIGRATIONS[version] ?? '');
      db.exec(`PRAGMA user_version = ${version + 1}`);
      db.exec('COMMIT');
    } catch (error) {
      db.exec('ROLLBACK');
      throw error;
    }
  }
}

function readOrCreateInfo(db: DatabaseSync, options: OpenJournalOptions): JournalInfo {
  const select = db.prepare('SELECT value FROM journal_meta WHERE key = ?');
  const id = (select.get('journal_id') as { value: string } | undefined)?.value;
  const origin = (select.get('origin') as { value: string } | undefined)?.value;
  if (id !== undefined && (origin === 'live' || origin === 'fixture')) {
    return { journal_id: id as JournalId, origin };
  }
  if (id !== undefined || origin !== undefined) {
    throw new JournalError('journal metadata is incomplete or invalid');
  }
  const info: JournalInfo = {
    journal_id: options.ids.next() as JournalId,
    origin: options.originIfNew,
  };
  const insert = db.prepare('INSERT INTO journal_meta (key, value) VALUES (?, ?)');
  db.exec('BEGIN IMMEDIATE');
  try {
    insert.run('journal_id', info.journal_id);
    insert.run('origin', info.origin);
    db.exec('COMMIT');
  } catch (error) {
    db.exec('ROLLBACK');
    throw error;
  }
  return info;
}

class SqliteJournal implements EventJournal {
  readonly info: JournalInfo;
  readonly #db: DatabaseSync;
  readonly #insert: StatementSync;
  readonly #findByEventId: StatementSync;
  readonly #findByNativeId: StatementSync;
  readonly #head: StatementSync;
  readonly #readAfter: StatementSync;
  readonly #readWorkstreamAfter: StatementSync;
  readonly #readAfterExcluding: StatementSync;
  readonly #readWorkstreamAfterExcluding: StatementSync;
  #closed = false;

  constructor(db: DatabaseSync, info: JournalInfo) {
    this.#db = db;
    this.info = info;
    this.#insert = db.prepare(`
      INSERT INTO events (
        event_id, event_type, project_id, workstream_id, execution_id,
        source_kind, source_id, source_native_id, occurred_at, ingested_at, envelope
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`);
    this.#findByEventId = db.prepare(
      'SELECT position, event_id, event_type, execution_id FROM events WHERE event_id = ?',
    );
    this.#findByNativeId = db.prepare(
      'SELECT position, event_id, event_type, execution_id FROM events WHERE source_kind = ? AND source_id = ? AND source_native_id = ?',
    );
    this.#head = db.prepare('SELECT COALESCE(MAX(position), 0) AS head FROM events');
    this.#readAfter = db.prepare(
      'SELECT position, envelope FROM events WHERE position > ? ORDER BY position LIMIT ?',
    );
    this.#readWorkstreamAfter = db.prepare(
      'SELECT position, envelope FROM events WHERE workstream_id = ? AND position > ? ORDER BY position LIMIT ?',
    );
    // The types to leave out arrive as one JSON array, so one statement serves any list.
    this.#readAfterExcluding = db.prepare(
      'SELECT position, envelope FROM events WHERE position > ? AND event_type NOT IN (SELECT value FROM json_each(?)) ORDER BY position LIMIT ?',
    );
    this.#readWorkstreamAfterExcluding = db.prepare(
      'SELECT position, envelope FROM events WHERE workstream_id = ? AND position > ? AND event_type NOT IN (SELECT value FROM json_each(?)) ORDER BY position LIMIT ?',
    );
  }

  head(): number {
    return (this.#head.get() as { head: number }).head;
  }

  append(event: EventEnvelope): AppendResult {
    const sourceId = event.source.kind === 'runtime' ? event.source.runtime_id : '';
    const byEventId = this.#findByEventId.get(event.event_id) as IdentityRow | undefined;
    if (byEventId !== undefined) return duplicate(byEventId, 'event_id');
    if (event.source_native_id !== null) {
      const byNativeId = this.#findByNativeId.get(
        event.source.kind,
        sourceId,
        event.source_native_id,
      ) as IdentityRow | undefined;
      if (byNativeId !== undefined) return duplicate(byNativeId, 'source_native_id');
    }
    const result = this.#insert.run(
      event.event_id,
      event.event_type,
      event.project_id,
      event.workstream_id,
      event.execution_id,
      event.source.kind,
      sourceId,
      event.source_native_id,
      event.occurred_at,
      event.ingested_at,
      JSON.stringify(event),
    );
    return { status: 'appended', position: Number(result.lastInsertRowid) };
  }

  read(options: ReadOptions): StoredEvent[] {
    const workstreamId = options.workstreamId ?? null;
    const excluded = options.excludeEventTypes ?? [];
    let rows: unknown[];
    if (excluded.length === 0) {
      rows =
        workstreamId === null
          ? this.#readAfter.all(options.after, options.limit)
          : this.#readWorkstreamAfter.all(workstreamId, options.after, options.limit);
    } else {
      const types = JSON.stringify(excluded);
      rows =
        workstreamId === null
          ? this.#readAfterExcluding.all(options.after, types, options.limit)
          : this.#readWorkstreamAfterExcluding.all(
              workstreamId,
              options.after,
              types,
              options.limit,
            );
    }
    return rows.map((row) => toStoredEvent(row as { position: number; envelope: string }));
  }

  *readAll(): Iterable<StoredEvent> {
    const batch = 1000;
    for (let after = 0; ; ) {
      const rows = this.read({ after, limit: batch });
      yield* rows;
      const last = rows.at(-1);
      if (last === undefined || rows.length < batch) return;
      after = last.position;
    }
  }

  close(): void {
    if (this.#closed) return;
    this.#closed = true;
    this.#db.close();
  }
}

/** The columns that say which event a duplicate matched, without reading its envelope. */
interface IdentityRow {
  readonly position: number;
  readonly event_id: string;
  readonly event_type: string;
  readonly execution_id: string | null;
}

function duplicate(row: IdentityRow, matchedOn: 'event_id' | 'source_native_id'): AppendResult {
  return {
    status: 'duplicate',
    position: row.position,
    matchedOn,
    existing: {
      eventId: row.event_id,
      eventType: row.event_type,
      executionId: row.execution_id,
    },
  };
}

/** Stored events are validated on the way out too: the journal is data, not trusted code. */
function toStoredEvent(row: { position: number; envelope: string }): StoredEvent {
  const parsed = parseEventEnvelope(JSON.parse(row.envelope));
  if (!parsed.ok) {
    const details = parsed.issues.map((issue) => `${issue.path} ${issue.message}`).join('; ');
    throw new JournalError(
      `event at position ${row.position} does not match the contract: ${details}`,
    );
  }
  return { position: row.position, event: parsed.value };
}

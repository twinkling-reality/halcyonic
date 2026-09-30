import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { after, describe, test } from 'node:test';
import { type EventEnvelope, parseEventEnvelope } from '@halcyonic/contracts';
import { createUuidV7Generator } from '../ids.ts';
import { JournalError } from './journal.ts';
import { openSqliteJournal } from './sqlite-journal.ts';

const TRACE: EventEnvelope[] = readFileSync(
  new URL('../../../../fixtures/traces/multiple_workstreams.jsonl', import.meta.url),
  'utf8',
)
  .trim()
  .split('\n')
  .map((line) => {
    const parsed = parseEventEnvelope(JSON.parse(line));
    assert.ok(parsed.ok);
    return parsed.value;
  });

const directory = mkdtempSync(join(tmpdir(), 'halcyonic-journal-'));
after(() => rmSync(directory, { recursive: true, force: true }));
let files = 0;
const freshPath = () => {
  files += 1;
  return join(directory, `journal-${files}.db`);
};
const ids = createUuidV7Generator();

describe('SQLite journal', () => {
  test('appends in order and reads back validated events', () => {
    const journal = openSqliteJournal({ path: ':memory:', originIfNew: 'live', ids });
    for (const event of TRACE) assert.equal(journal.append(event).status, 'appended');
    assert.equal(journal.head(), TRACE.length);
    const all = [...journal.readAll()];
    assert.deepEqual(
      all.map((stored) => stored.event.event_id),
      TRACE.map((event) => event.event_id),
    );
    assert.deepEqual(
      all.map((stored) => stored.position),
      TRACE.map((_, index) => index + 1),
    );
    journal.close();
  });

  test('the same event, or the same native record, is journaled once', () => {
    const journal = openSqliteJournal({ path: ':memory:', originIfNew: 'live', ids });
    const runtimeEvent = TRACE.find((event) => event.source_native_id !== null);
    assert.ok(runtimeEvent);
    const first = journal.append(runtimeEvent);
    assert.equal(first.status, 'appended');
    const existing = {
      eventId: runtimeEvent.event_id,
      eventType: runtimeEvent.event_type,
      executionId: runtimeEvent.execution_id,
    };
    assert.deepEqual(journal.append(runtimeEvent), {
      status: 'duplicate',
      position: 1,
      matchedOn: 'event_id',
      existing,
    });
    const redelivered = { ...runtimeEvent, event_id: ids.next() } as EventEnvelope;
    assert.deepEqual(journal.append(redelivered), {
      status: 'duplicate',
      position: 1,
      matchedOn: 'source_native_id',
      existing,
    });
    journal.close();
  });

  test('a duplicate names the event already journaled, not the one it refused', () => {
    const journal = openSqliteJournal({ path: ':memory:', originIfNew: 'live', ids });
    const runtimeEvents = TRACE.filter((event) => event.source_native_id !== null);
    const stored = runtimeEvents[0];
    const other = runtimeEvents.find(
      (event) =>
        event.execution_id !== stored?.execution_id && event.event_type !== stored?.event_type,
    );
    assert.ok(stored && other);
    journal.append(stored);
    const reusing = { ...other, source_native_id: stored.source_native_id } as EventEnvelope;
    assert.deepEqual(journal.append(reusing), {
      status: 'duplicate',
      position: 1,
      matchedOn: 'source_native_id',
      existing: {
        eventId: stored.event_id,
        eventType: stored.event_type,
        executionId: stored.execution_id,
      },
    });
    assert.equal(journal.head(), 1);
    journal.close();
  });

  test('reads page by position and filter by workstream', () => {
    const journal = openSqliteJournal({ path: ':memory:', originIfNew: 'live', ids });
    for (const event of TRACE) journal.append(event);
    const page = journal.read({ after: 10, limit: 5 });
    assert.deepEqual(
      page.map((stored) => stored.position),
      [11, 12, 13, 14, 15],
    );
    const workstreamId = TRACE.find((event) => event.workstream_id !== null)?.workstream_id ?? null;
    const filtered = journal.read({ after: 0, limit: 1000, workstreamId });
    assert.ok(filtered.length > 0);
    assert.ok(filtered.every((stored) => stored.event.workstream_id === workstreamId));
    journal.close();
  });

  test('a file journal uses WAL, survives reopening, and keeps its identity and origin', () => {
    const path = freshPath();
    const first = openSqliteJournal({ path, originIfNew: 'live', ids });
    for (const event of TRACE.slice(0, 5)) first.append(event);
    const identity = first.info;
    for (const file of [path, `${path}-wal`]) {
      assert.equal(statSync(file).mode & 0o777, 0o600, `${file} is readable by its owner only`);
    }
    first.close();

    const probe = new DatabaseSync(path);
    // node:sqlite returns rows as null-prototype objects, so compare the field itself.
    const mode = probe.prepare('PRAGMA journal_mode').get() as { journal_mode: string };
    assert.equal(mode.journal_mode, 'wal');
    probe.close();

    const reopened = openSqliteJournal({ path, originIfNew: 'fixture', ids });
    assert.deepEqual(reopened.info, identity);
    assert.equal(reopened.head(), 5);
    reopened.close();
  });

  test('a journal written by a newer build is refused', () => {
    const path = freshPath();
    openSqliteJournal({ path, originIfNew: 'live', ids }).close();
    const db = new DatabaseSync(path);
    db.exec('PRAGMA user_version = 99');
    db.close();
    assert.throws(() => openSqliteJournal({ path, originIfNew: 'live', ids }), JournalError);
  });

  test('execution.start commands stored before model choice are migrated to choose no model', () => {
    const path = freshPath();
    const journal = openSqliteJournal({ path, originIfNew: 'live', ids });
    for (const event of TRACE) journal.append(event);
    journal.close();
    // What a journal of schema version 1 holds: the command as sent then, without model_ref.
    const db = new DatabaseSync(path);
    const starts = db
      .prepare(
        `SELECT position, envelope FROM events WHERE event_type IN ('command.accepted', 'command.rejected')
           AND json_extract(envelope, '$.payload.command.command_type') = 'execution.start'`,
      )
      .all() as { position: number; envelope: string }[];
    assert.ok(starts.length > 0);
    for (const { position, envelope } of starts) {
      const event = JSON.parse(envelope) as { payload: { command: { payload: object } } };
      const { model_ref: _dropped, ...payload } = event.payload.command.payload as {
        model_ref?: unknown;
      };
      event.payload.command.payload = payload;
      db.prepare('UPDATE events SET envelope = ? WHERE position = ?').run(
        JSON.stringify(event),
        position,
      );
    }
    db.exec('PRAGMA user_version = 1');
    db.close();

    const reopened = openSqliteJournal({ path, originIfNew: 'live', ids });
    const events = [...reopened.readAll()].map((stored) => stored.event);
    assert.deepEqual(
      events.map((event) => event.event_id),
      TRACE.map((event) => event.event_id),
    );
    const migrated = events.flatMap((event) =>
      event.event_type === 'command.accepted' &&
      event.payload.command.command_type === 'execution.start'
        ? [event.payload.command.payload.model_ref]
        : [],
    );
    assert.equal(migrated.length, starts.length);
    assert.ok(migrated.every((modelRef) => modelRef === null));
    reopened.close();
    const check = new DatabaseSync(path);
    const version = check.prepare('PRAGMA user_version').get() as { user_version: number };
    assert.equal(version.user_version, 2);
    check.close();
  });

  test('a stored event that no longer matches the contract is reported, not trusted', () => {
    const path = freshPath();
    const journal = openSqliteJournal({ path, originIfNew: 'live', ids });
    journal.append(TRACE[0] as EventEnvelope);
    journal.close();
    const db = new DatabaseSync(path);
    db.exec(`UPDATE events SET envelope = '{"event_type":"project.created"}' WHERE position = 1`);
    db.close();
    const reopened = openSqliteJournal({ path, originIfNew: 'live', ids });
    assert.throws(() => [...reopened.readAll()], /position 1 does not match the contract/);
    reopened.close();
  });
});

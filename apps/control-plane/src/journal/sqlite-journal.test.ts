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

  test('a read can leave event types out, and its limit counts only what it returns', () => {
    const journal = openSqliteJournal({ path: ':memory:', originIfNew: 'live', ids });
    for (const event of TRACE) journal.append(event);
    const excluded = ['command.accepted', 'command.completed'];
    const kept = TRACE.map((event, index) => ({ type: event.event_type, position: index + 1 }))
      .filter(({ type }) => !excluded.includes(type))
      .map(({ position }) => position);
    const page = journal.read({ after: 0, limit: 5, excludeEventTypes: excluded });
    assert.deepEqual(
      page.map((stored) => stored.position),
      kept.slice(0, 5),
    );
    const all = journal.read({ after: 0, limit: 1000, excludeEventTypes: excluded });
    assert.equal(all.length, kept.length);
    assert.ok(all.every((stored) => !excluded.includes(stored.event.event_type)));

    const workstreamId = TRACE.find((event) => event.workstream_id !== null)?.workstream_id ?? null;
    const scoped = journal.read({
      after: 0,
      limit: 1000,
      workstreamId,
      excludeEventTypes: excluded,
    });
    assert.ok(scoped.length > 0);
    assert.ok(
      scoped.every(
        (stored) =>
          stored.event.workstream_id === workstreamId &&
          !excluded.includes(stored.event.event_type),
      ),
    );
    assert.deepEqual(journal.read({ after: 0, limit: 3, excludeEventTypes: [] }).length, 3);
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

  test('a command event journaled before principals were recorded reads with a null principal', () => {
    const path = freshPath();
    const journal = openSqliteJournal({ path, originIfNew: 'live', ids });
    const accepted = TRACE.find((event) => event.event_type === 'command.accepted');
    assert.ok(accepted && accepted.event_type === 'command.accepted');
    const { principal: _principal, ...older } = accepted.payload;
    journal.append({ ...accepted, payload: older } as unknown as EventEnvelope);
    journal.close();

    const db = new DatabaseSync(path);
    const version = db.prepare('PRAGMA user_version').get() as { user_version: number };
    assert.equal(version.user_version, 2);
    const stored = db.prepare('SELECT envelope FROM events').get() as { envelope: string };
    assert.equal(stored.envelope.includes('principal'), false, 'the stored text is not rewritten');
    db.close();

    const reopened = openSqliteJournal({ path, originIfNew: 'live', ids });
    const [read] = [...reopened.readAll()];
    assert.equal(read?.event.event_type, 'command.accepted');
    assert.equal(
      read?.event.event_type === 'command.accepted' && read.event.payload.principal,
      null,
    );
    reopened.close();
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

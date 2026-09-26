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
    assert.deepEqual(journal.append(runtimeEvent), {
      status: 'duplicate',
      position: 1,
      matchedOn: 'event_id',
    });
    const redelivered = { ...runtimeEvent, event_id: ids.next() } as EventEnvelope;
    assert.deepEqual(journal.append(redelivered), {
      status: 'duplicate',
      position: 1,
      matchedOn: 'source_native_id',
    });
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

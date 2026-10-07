import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import fsPromises from 'node:fs/promises';
import { syncBuiltinESMExports } from 'node:module';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, mock, test } from 'node:test';
import { inHalcyonicsCodexHome } from './codex-home.ts';

const base = mkdtempSync(join(tmpdir(), 'halcyonic-codex-home-'));
after(() => rmSync(base, { recursive: true, force: true }));

const THREAD = '019a1b2c-3d4e-7f00-8000-0000000000a1';
/** Noon on 7 October 2026, by this Mac's local clock, as the journal would hold it. */
const STARTED = new Date(2026, 9, 7, 12, 0, 0).toISOString();

let homes = 0;
/** A Codex home with a `sessions` folder, as Codex makes it. */
function home(): string {
  homes += 1;
  const path = join(base, `home-${homes}`);
  mkdirSync(join(path, 'sessions'), { recursive: true });
  return path;
}

/** Writes a rollout as Codex names it, in the local date folder `day` of October 2026. */
function rollout(codexHome: string, day: number, thread = THREAD): string {
  const folder = join(codexHome, 'sessions', '2026', '10', String(day).padStart(2, '0'));
  mkdirSync(folder, { recursive: true });
  const path = join(
    folder,
    `rollout-2026-10-${String(day).padStart(2, '0')}T12-00-00-${thread}.jsonl`,
  );
  writeFileSync(path, '{"type":"session_meta"}\n');
  return path;
}

describe("Codex threads in Halcyonic's own Codex home", () => {
  test('a rollout on the day the execution started, the day before or the day after, is found', async () => {
    for (const day of [6, 7, 8]) {
      const codexHome = home();
      rollout(codexHome, day);
      assert.equal(
        await inHalcyonicsCodexHome(codexHome, 'codex', THREAD, STARTED),
        true,
        `${day}`,
      );
    }
    const far = home();
    rollout(far, 5);
    rollout(far, 9);
    assert.equal(await inHalcyonicsCodexHome(far, 'codex', THREAD, STARTED), false);
    // Another thread's rollout, or another runtime's session, is not this one.
    const other = home();
    rollout(other, 7, '019a1b2c-3d4e-7f00-8000-0000000000b2');
    assert.equal(await inHalcyonicsCodexHome(other, 'codex', THREAD, STARTED), false);
    const same = home();
    rollout(same, 7);
    assert.equal(await inHalcyonicsCodexHome(same, 'opencode', THREAD, STARTED), false);
  });

  test('without a rollout in the home, as for a thread run in ~/.codex, it is not found', async () => {
    assert.equal(await inHalcyonicsCodexHome(home(), 'codex', THREAD, STARTED), false);
    assert.equal(
      await inHalcyonicsCodexHome(join(base, 'missing'), 'codex', THREAD, STARTED),
      false,
    );
  });

  test('an id that is not a thread id never reaches the filesystem', async (t) => {
    const codexHome = home();
    rollout(codexHome, 7, 'not-a-thread');
    const lstat = mock.method(fsPromises, 'lstat');
    const readdir = mock.method(fsPromises, 'readdir');
    syncBuiltinESMExports();
    t.after(() => {
      mock.restoreAll();
      syncBuiltinESMExports();
    });
    for (const id of ['not-a-thread', '../../outside', `${THREAD}/..`, '', `${THREAD}\n`]) {
      assert.equal(await inHalcyonicsCodexHome(codexHome, 'codex', id, STARTED), false, id);
    }
    assert.equal(lstat.mock.callCount() + readdir.mock.callCount(), 0);
    // The same calls are what a thread id is looked for with.
    rollout(codexHome, 7);
    assert.equal(await inHalcyonicsCodexHome(codexHome, 'codex', THREAD, STARTED), true);
    assert.ok(lstat.mock.callCount() > 0 && readdir.mock.callCount() > 0);
  });

  test('a link is never followed: not for the home, sessions, a date folder or the rollout', async () => {
    const real = home();
    const target = rollout(real, 7);
    const cases: [string, string][] = [];

    const linkedHome = join(base, 'linked-home');
    symlinkSync(real, linkedHome);
    cases.push(['the home', linkedHome]);

    const linkedSessions = join(base, 'linked-sessions');
    mkdirSync(linkedSessions);
    symlinkSync(join(real, 'sessions'), join(linkedSessions, 'sessions'));
    cases.push(['sessions', linkedSessions]);

    for (const level of ['year', 'month', 'day'] as const) {
      const codexHome = home();
      const parts = ['2026', '10', '07'];
      const depth = { year: 1, month: 2, day: 3 }[level];
      const parent = join(codexHome, 'sessions', ...parts.slice(0, depth - 1));
      mkdirSync(parent, { recursive: true });
      symlinkSync(
        join(real, 'sessions', ...parts.slice(0, depth)),
        join(parent, parts[depth - 1] as string),
      );
      cases.push([`the ${level} folder`, codexHome]);
    }

    const linkedRollout = home();
    const folder = join(linkedRollout, 'sessions', '2026', '10', '07');
    mkdirSync(folder, { recursive: true });
    symlinkSync(target, join(folder, `rollout-2026-10-07T12-00-00-${THREAD}.jsonl`));
    cases.push(['the rollout', linkedRollout]);

    for (const [what, codexHome] of cases) {
      assert.equal(await inHalcyonicsCodexHome(codexHome, 'codex', THREAD, STARTED), false, what);
    }
    assert.equal(await inHalcyonicsCodexHome(real, 'codex', THREAD, STARTED), true);
  });

  test("a start time that can't be read finds nothing", async () => {
    const codexHome = home();
    rollout(codexHome, 7);
    assert.equal(await inHalcyonicsCodexHome(codexHome, 'codex', THREAD, 'not a time'), false);
  });
});

import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { setTimeout as sleep } from 'node:timers/promises';
import { FILES_WRITING_LISTED } from '@halcyonic/contracts';
import { WritingWatcher } from './writing-watcher.ts';

// Watch It Work: which files the host saw change while a turn runs, names only, bounded (AUDIT3 H2, H3).

const base = mkdtempSync(join(tmpdir(), 'halcyonic-writing-'));
after(() => rmSync(base, { recursive: true, force: true }));

const scope = {
  project_id: '01920000-0000-7000-8000-000000000001',
  workstream_id: '01920000-0000-7000-8000-000000000002',
  execution_id: '01920000-0000-7000-8000-000000000003',
};

/** Waits until the condition holds: a start decides its folder asynchronously, slower under load. */
async function until(condition: () => boolean, ms = 5_000): Promise<void> {
  const by = Date.now() + ms;
  while (!condition()) {
    if (Date.now() > by) throw new Error('the condition never held');
    await sleep(5);
  }
}

function rig(options: { observes?: boolean; protect?: readonly string[] } = {}) {
  let listener: ((published: { event: unknown }) => void) | null = null;
  const recorded: { paths: string[]; paths_total: number; total_complete: boolean }[] = [];
  let names: ((name: string | null) => void) | null = null;
  let open = 0;
  const watcher = new WritingWatcher({
    publisher: {
      subscribe(next: (published: { event: unknown }) => void) {
        listener = next;
        return () => {
          listener = null;
        };
      },
    } as never,
    recorder: {
      record(draft: { payload: (typeof recorded)[number] }) {
        recorded.push(draft.payload);
        return draft;
      },
    } as never,
    projection: { execution: () => ({ directory: base }) } as never,
    clock: { now: () => new Date('2026-11-16T03:10:00.000Z') } as never,
    logger: { error() {} } as never,
    observes: () => options.observes ?? true,
    protect: options.protect ?? [],
    everyMs: 20,
    watchFolder: (_folder, onName) => {
      names = onName;
      open += 1;
      return {
        close() {
          open -= 1;
        },
      };
    },
  });
  const send = (event_type: string) =>
    listener?.({ event: { event_type, ...scope, payload: { turn_id: 't1' } } });
  return {
    watcher,
    recorded,
    send,
    saw: (name: string | null) => names?.(name),
    open: () => open,
  };
}

describe('the files a running turn changes, as the host sees them (Watch It Work)', () => {
  test('records the newest names first, each once, only when they changed, and stops with the turn', async () => {
    const r = rig();
    r.send('runtime.turn.started');
    await until(() => r.open() === 1);
    r.saw('src/main.ts');
    r.saw('README.md');
    r.saw('src/main.ts');
    await until(() => r.recorded.length > 0);
    assert.deepEqual(r.recorded, [
      { paths: ['src/main.ts', 'README.md'], paths_total: 2, total_complete: true },
    ]);
    // Nothing new: nothing more is recorded.
    await sleep(60);
    assert.equal(r.recorded.length, 1);
    r.saw('index.html');
    r.send('runtime.turn.completed');
    assert.equal(r.open(), 0, 'no watch outlives its turn');
    assert.equal(r.watcher.watching, 0);
    assert.deepEqual(r.recorded.at(-1)?.paths, ['index.html', 'src/main.ts', 'README.md']);
  });

  test('never keeps a name outside the folder, inside .git or node_modules at its top, or past the bound', async () => {
    const r = rig();
    r.send('runtime.turn.started');
    await until(() => r.open() === 1);
    for (const name of [
      '../outside.txt',
      '/etc/passwd',
      '.git/config',
      'node_modules/a/b.js',
      null,
    ])
      r.saw(name);
    r.saw('lib/node_modules/kept.js');
    for (let index = 0; index < FILES_WRITING_LISTED + 5; index += 1) r.saw(`f${index}.js`);
    r.send('runtime.turn.completed');
    const last = r.recorded.at(-1);
    assert.equal(last?.paths.length, FILES_WRITING_LISTED);
    assert.equal(last?.paths[0], `f${FILES_WRITING_LISTED + 4}.js`);
    assert.equal(
      last?.paths_total,
      FILES_WRITING_LISTED + 6,
      'each counted once, the rest only counted',
    );
    assert.ok(!last?.paths.some((path) => path.includes('..') || path.startsWith('/')));
  });

  test("watches only a turn this process started, never a folder of the host's own data, and closes with the plane", async () => {
    const replayed = rig({ observes: false });
    replayed.send('runtime.turn.started');
    await sleep(10);
    assert.equal(replayed.open(), 0, 'a recorded run is history, not the folder now');

    // A run's folder that holds the host's own data is never watched.
    const holds = rig({ protect: [join(base, 'data')] });
    holds.send('runtime.turn.started');
    // Given time to decide, it never opens a watch (a slow decision only makes this pass sooner, never fail).
    await sleep(50);
    assert.equal(holds.open(), 0);

    const own = rig({ protect: [join(tmpdir(), 'halcyonic-writing-elsewhere', 'data')] });
    own.send('runtime.turn.started');
    await until(() => own.open() === 1);
    own.watcher.close();
    assert.equal(own.open(), 0, 'closing the plane ends the watch');
    own.send('runtime.turn.started');
    await sleep(10);
    assert.equal(own.open(), 0, 'nothing after the close');
  });

  test('a turn that ends while its watch is still starting leaves no watch (AUDIT3 WW1)', async () => {
    const r = rig();
    // The end comes before the start has looked at the folder: nothing is watched, then or later.
    r.send('runtime.turn.started');
    r.send('runtime.turn.failed');
    await sleep(40);
    assert.equal(r.open(), 0);
    assert.equal(r.watcher.watching, 0);
    assert.deepEqual(r.recorded, []);
  });
});

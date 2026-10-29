import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import {
  mkdirSync,
  mkdtempSync,
  realpathSync,
  rmSync,
  symlinkSync,
  utimesSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, mock, test } from 'node:test';
import type {
  CommandEnvelope,
  CommandId,
  EventEnvelope,
  ProjectId,
  RuntimeDescriptor,
  RuntimeId,
  WorkstreamId,
} from '@halcyonic/contracts';
import type { ObservationSink, RuntimeAdapter, RuntimeObservation } from '@halcyonic/runtime-core';

/** A control plane with the Mac's file readings on, as its own start has them (ADR 0038). */
function observingPlane(locations: HostLocations, adapters: RuntimeAdapter[]) {
  const time = createVirtualTime(new Date('2026-10-29T10:00:00.000Z'));
  const ids = createUuidV7Generator({ now: () => Date.now() });
  const journal = openSqliteJournal({ path: ':memory:', originIfNew: 'live', ids });
  const controlPlane = new ControlPlane({
    journal,
    adapters,
    ids,
    clock: time,
    scheduler: time as Scheduler,
    logger: capturingLogger().logger,
    commandTimeoutMs: 30_000,
    locations,
    observeFiles: { protect: [] },
  });
  return { controlPlane, journal };
}

import { confirmProjectLocation, createVirtualTime, type Scheduler } from '@halcyonic/runtime-core';
import { ControlPlane } from '../core/control-plane.ts';
import { createUuidV7Generator } from '../ids.ts';
import { openSqliteJournal } from '../journal/sqlite-journal.ts';
import { createHostLocations, type HostLocations } from '../locations.ts';
import { capturingLogger } from '../testing/harness.ts';
import {
  compareReadings,
  FileObserver,
  listFolder,
  readEntry,
  readFolder,
  within,
} from './file-observer.ts';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-observer-')));
after(() => rmSync(base, { recursive: true, force: true }));

function folder(name: string): string {
  const path = join(base, name);
  mkdirSync(path, { recursive: true });
  return path;
}

describe('reading a folder', () => {
  test('counts the lines of text files, gives binary files none, and skips the top-level .git and node_modules and links', async () => {
    const root = folder('read');
    writeFileSync(join(root, 'a.ts'), 'one\ntwo\nthree');
    writeFileSync(join(root, 'empty.txt'), '');
    writeFileSync(join(root, 'image.png'), Buffer.from([0x89, 0x50, 0x00, 0x01, 0x0a]));
    mkdirSync(join(root, '.git'));
    writeFileSync(join(root, '.git', 'HEAD'), 'ref\n');
    mkdirSync(join(root, 'src', 'node_modules'), { recursive: true });
    writeFileSync(join(root, 'src', 'node_modules', 'x.js'), 'x\n');
    mkdirSync(join(root, 'game', '.git'), { recursive: true });
    writeFileSync(join(root, 'game', '.git', 'hidden.js'), 'y\n');
    symlinkSync(join(base), join(root, 'node_modules'));
    symlinkSync('a.ts', join(root, 'link.ts'));
    const read = await readFolder(root);
    assert.ok(read.ok);
    // Below the top level, a folder named node_modules or .git is the agent's own and is read like any other:
    // skipping it would hide code from the record (AUDIT L1 C2).
    assert.deepEqual([...read.reading.keys()].sort(), [
      'a.ts',
      'empty.txt',
      'game/.git/hidden.js',
      'image.png',
      'src/node_modules/x.js',
    ]);
    assert.equal(read.reading.get('a.ts')?.lines, 3);
    assert.equal(read.reading.get('empty.txt')?.lines, 0);
    assert.equal(read.reading.get('image.png')?.lines, null);
    // The record says what it left out (AUDIT L1 C2): the link, not the top-level skips.
    assert.equal(read.notRead, 1);
  });

  test('counts what it found and did not read: links, FIFOs and files swapped for them', async () => {
    const root = folder('not-read');
    writeFileSync(join(root, 'a.ts'), 'a\n');
    symlinkSync('a.ts', join(root, 'link.ts'));
    mkdirSync(join(root, 'src'));
    execFileSync('/usr/bin/mkfifo', [join(root, 'src', 'pipe')]);
    const read = await readFolder(root);
    assert.ok(read.ok);
    assert.deepEqual([...read.reading.keys()], ['a.ts']);
    assert.equal(read.notRead, 2);
  });

  test('a folder with more files than it reads is refused as too large', async () => {
    const root = folder('large');
    for (let i = 0; i < 5; i += 1) writeFileSync(join(root, `f${i}.txt`), `${i}\n`);
    const read = await readFolder(root, undefined, { files: 4, countLinesUpTo: 1024 });
    assert.equal(!read.ok && read.code, 'too_large');
  });

  test('a folder that is not there is said so', async () => {
    const read = await readFolder(join(base, 'gone'));
    assert.equal(!read.ok && read.code, 'location_missing');
  });
});

describe('reading one file, as it is when opened', () => {
  // The folder is listed first and each file opened after, so a file can be swapped in between (AUDIT L1 C1):
  // what is opened must itself be a file, reached without following a link.
  test('a link put where a file was listed is not read through', async () => {
    const root = folder('swap-link');
    writeFileSync(join(base, 'outside.txt'), 'secret\nlines\n');
    symlinkSync(join(base, 'outside.txt'), join(root, 'a.txt'));
    assert.equal(
      await readEntry(join(root, 'a.txt'), undefined, { files: 10, countLinesUpTo: 1024 }),
      null,
    );
  });

  test('a link to an endless device is not read through', { timeout: 5000 }, async () => {
    const root = folder('swap-zero');
    symlinkSync('/dev/zero', join(root, 'a.txt'));
    assert.equal(
      await readEntry(join(root, 'a.txt'), undefined, { files: 10, countLinesUpTo: 1024 }),
      null,
    );
  });

  test('a FIFO put where a file was listed is not waited on', { timeout: 5000 }, async () => {
    const root = folder('swap-fifo');
    execFileSync('/usr/bin/mkfifo', [join(root, 'a.txt')]);
    assert.equal(
      await readEntry(join(root, 'a.txt'), undefined, { files: 10, countLinesUpTo: 1024 }),
      null,
    );
  });

  test('a file larger than the bytes left is refused before it is read', async () => {
    const root = folder('bound-first');
    writeFileSync(join(root, 'big.bin'), Buffer.alloc(2048));
    assert.equal(
      await readEntry(join(root, 'big.bin'), undefined, { files: 10, countLinesUpTo: 1024 }, 1024),
      'too_large',
    );
  });
});

describe('a subfolder swapped for a link while the folder is read', () => {
  // The folder is listed before its subfolders are, so one can be swapped for a link in between (AUDIT L1 C1b).
  test('is not listed through the link', async () => {
    const root = folder('swap-dir');
    const outside = folder('swap-dir-outside');
    writeFileSync(join(outside, 'secret.txt'), 'x\n');
    symlinkSync(outside, join(root, 'src'));
    assert.equal(await listFolder(root, 'src'), null);
  });

  test('nor is a folder below it, reached through the link', async () => {
    const root = folder('swap-parent');
    const outside = folder('swap-parent-outside');
    mkdirSync(join(outside, 'inner'));
    symlinkSync(outside, join(root, 'src'));
    assert.equal(await listFolder(root, 'src/inner'), null);
  });
});

describe('comparing two readings', () => {
  test('lists files created, changed and deleted with their lines, and nothing that kept its bytes', async () => {
    const root = folder('compare');
    writeFileSync(join(root, 'keep.ts'), 'same\n');
    writeFileSync(join(root, 'edit.ts'), 'a\nb\n');
    writeFileSync(join(root, 'old.ts'), 'x\ny\nz\n');
    const before = await readFolder(root);
    assert.ok(before.ok);
    writeFileSync(join(root, 'edit.ts'), 'a\nb\nc\nd\n');
    rmSync(join(root, 'old.ts'));
    writeFileSync(join(root, 'new.ts'), 'hello\n');
    // Touched, same bytes: not a change.
    const later = new Date(Date.now() + 5000);
    utimesSync(join(root, 'keep.ts'), later, later);
    const afterReading = await readFolder(root, before.reading);
    assert.ok(afterReading.ok);
    assert.deepEqual(compareReadings(before.reading, afterReading.reading), {
      created: [{ path: 'new.ts', lines: 1, bytes: 6 }],
      changed: [{ path: 'edit.ts', lines_before: 2, lines_after: 4, bytes: 8 }],
      deleted: [{ path: 'old.ts', lines_before: 3 }],
    });
  });
});

/** A runtime working in the project's folder: each turn writes what the test asks, then ends. */
function writingRuntime(turns: ((directory: string) => void)[]) {
  let sink: ObservationSink | null = null;
  let directory = '';
  let n = 0;
  const observe = (type: RuntimeObservation['type'], payload: unknown) => {
    n += 1;
    sink?.({
      type,
      occurred_at: new Date().toISOString(),
      native_event_id: `writer-${n}`,
      sequence: null,
      provenance: { epistemic: 'observed', native_type: null },
      payload,
    } as RuntimeObservation);
  };
  const runTurn = (index: number) => {
    observe('runtime.turn.started', { turn_id: `turn-${index}` });
    turns[index]?.(directory);
    observe('runtime.turn.completed', { turn_id: `turn-${index}` });
  };
  const descriptor: RuntimeDescriptor = {
    runtime_id: 'writer' as RuntimeId,
    kind: 'stub',
    display_name: 'Stub that writes in its folder',
    synthetic: true,
    capabilities: {
      start_execution: true,
      instruct_at_rest: true,
      instruct_while_running: false,
      respond_to_approval: false,
      answer_question: false,
      interrupt: false,
    },
    model_choice: 'none',
    uses_project_location: true,
    reports_tool_activity: false,
  };
  const adapter: RuntimeAdapter = {
    descriptor,
    validateStartOptions: () => ({ ok: true }),
    startExecution: async (request) => {
      sink = request.emit;
      directory = request.directory ?? '';
      observe('runtime.execution.started', { native_id: 'writer-session' });
      runTurn(0);
      return { native_id: 'writer-session' };
    },
    sendInstruction: async () => {
      runTurn(1);
    },
    close: async () => {},
  };
  return adapter;
}

async function until(check: () => boolean, what: string): Promise<void> {
  for (let tries = 0; tries < 400; tries += 1) {
    if (check()) return;
    await new Promise((resolveWait) => setTimeout(resolveWait, 10));
  }
  assert.fail(`timed out waiting for ${what}`);
}

describe('observing each turn through the control plane', () => {
  test('each turn ends with what changed in the folder since the last reading, paths relative only', async () => {
    const root = folder('plane');
    mkdirSync(join(root, 'app'));
    writeFileSync(join(root, 'app', 'index.ts'), 'start\n');
    const harness = observingPlane(createHostLocations([root]), [
      writingRuntime([
        (dir) => writeFileSync(join(dir, 'game.ts'), 'a\nb\n'),
        (dir) => {
          writeFileSync(join(dir, 'game.ts'), 'a\nb\nc\n');
          rmSync(join(dir, 'index.ts'));
        },
      ]),
    ]);
    const { controlPlane, journal } = harness;
    let ids = 0;
    const envelope = (type: CommandEnvelope['command_type'], payload: unknown): CommandEnvelope => {
      ids += 1;
      return {
        schema_version: 1,
        command_id: `00000000-0000-4000-8000-${String(ids).padStart(12, '0')}` as CommandId,
        issued_at: '2026-10-29T10:00:00.000Z',
        client: { name: 'test', version: null, device_label: null },
        command_type: type,
        payload,
      } as CommandEnvelope;
    };
    const submit = (command: CommandEnvelope) => controlPlane.commands.submit(command, 'internal');
    const project = submit(
      envelope('project.create', {
        name: 'Game',
        location: { kind: 'existing_folder', root, folder_name: 'app' },
      }),
    ).command?.result;
    const projectId = (project?.kind === 'project_created' ? project.project_id : '') as ProjectId;
    const work = submit(
      envelope('workstream.create', { project_id: projectId, title: 'Towers', objective: null }),
    ).command?.result;
    const workstreamId = (
      work?.kind === 'workstream_created' ? work.workstream_id : ''
    ) as WorkstreamId;
    const started = submit(
      envelope('execution.start', {
        workstream_id: workstreamId,
        runtime_id: 'writer',
        instruction: 'Make a game.',
        options: {},
        model_ref: null,
      }),
    );
    const observed = (): EventEnvelope[] =>
      [...journal.readAll()]
        .map((stored) => stored.event)
        .filter((event) => event.event_type === 'execution.files.observed');
    await until(() => observed().length === 1, 'the first turn observed');
    const first = observed()[0];
    assert.equal(first?.event_type, 'execution.files.observed');
    if (first?.event_type !== 'execution.files.observed') return;
    assert.equal(first.provenance.epistemic, 'observed');
    assert.equal(first.payload.turn_id, 'turn-0');
    assert.deepEqual(first.payload.created, {
      files: [{ path: 'game.ts', lines: 2, bytes: 4 }],
      total: 1,
    });
    assert.equal(first.payload.skipped, null);
    assert.equal(first.payload.not_read, 0);

    // The start completes once the runtime confirms it, after the folder's first reading.
    assert.equal(started.disposition, 'accepted');
    const executionId = first.execution_id;
    await until(
      () => controlPlane.projection.execution(executionId)?.status === 'completed',
      'rest',
    );
    submit(envelope('execution.send_instruction', { execution_id: executionId, text: 'More.' }));
    await until(() => observed().length === 2, 'the second turn observed');
    const second = observed()[1];
    if (second?.event_type !== 'execution.files.observed') return assert.fail('no second');
    assert.deepEqual(second.payload.changed.files, [
      { path: 'game.ts', lines_before: 2, lines_after: 3, bytes: 6 },
    ]);
    assert.deepEqual(second.payload.deleted.files, [{ path: 'index.ts', lines_before: 1 }]);
    for (const event of observed())
      assert.equal(JSON.stringify(event.payload).includes(root), false);
    await controlPlane.close();
  });

  test('a turn with no earlier reading says so instead of guessing', async () => {
    const { FileObserver } = await import('./file-observer.ts');
    assert.equal(typeof FileObserver, 'function');
    const root = folder('nobase');
    const observer = new FileObserver();
    const result = await observer.turnEnded('01920000-0000-7000-8000-000000000001', root);
    assert.equal(result.skipped?.code, 'no_baseline');
  });

  test('with readings on, a folder removed after admission still fails the start: location_missing, effect none', async () => {
    const root = folder('removed');
    mkdirSync(join(root, 'app'));
    const locations = createHostLocations([root]);
    // Like the real runtimes, it asks the host's policy about the folder before it starts anything.
    const confirming: RuntimeAdapter = {
      ...writingRuntime([]),
      startExecution: async (request) => {
        confirmProjectLocation(locations.policy, request.directory);
        return { native_id: 'confirming-session' };
      },
    };
    const { controlPlane } = observingPlane(locations, [confirming]);
    let ids = 0;
    const envelope = (type: CommandEnvelope['command_type'], payload: unknown): CommandEnvelope => {
      ids += 1;
      return {
        schema_version: 1,
        command_id: `00000000-0000-4000-8000-${String(ids).padStart(12, '0')}` as CommandId,
        issued_at: '2026-10-29T10:00:00.000Z',
        client: { name: 'test', version: null, device_label: null },
        command_type: type,
        payload,
      } as CommandEnvelope;
    };
    const submit = (command: CommandEnvelope) => controlPlane.commands.submit(command, 'internal');
    const project = submit(
      envelope('project.create', {
        name: 'Gone',
        location: { kind: 'existing_folder', root, folder_name: 'app' },
      }),
    ).command?.result;
    const projectId = project?.kind === 'project_created' ? project.project_id : '';
    const work = submit(
      envelope('workstream.create', { project_id: projectId, title: 'Gone', objective: null }),
    ).command?.result;
    const workstreamId = work?.kind === 'workstream_created' ? work.workstream_id : '';
    const started = submit(
      envelope('execution.start', {
        workstream_id: workstreamId,
        runtime_id: 'writer',
        instruction: 'Go.',
        options: {},
        model_ref: null,
      }),
    );
    assert.equal(started.disposition, 'accepted');
    rmSync(join(root, 'app'), { recursive: true });
    const id = started.command?.command_id ?? '';
    await until(
      () => controlPlane.projection.command(id)?.status !== 'accepted',
      'the start outcome',
    );
    const command = controlPlane.projection.command(id);
    assert.equal(command?.status, 'failed');
    assert.equal(command?.failure?.code, 'location_missing');
    assert.equal(command?.failure?.effect, 'none');
    const [execution] = controlPlane.projection.executions();
    assert.equal(execution?.status, 'failed', 'a start that never happened, not an unknown one');
    await controlPlane.close();
  });
});

/**
 * The start's wait for the folder's first reading ends before the reading does (AUDIT L1 C3): the start
 * goes ahead, the first turn says it has no earlier reading, and the reading is never used, whether it
 * was taken before the wait ended (`finished`) or only begins after (`late`).
 */
async function slowFirstReading(order: 'finished' | 'late'): Promise<void> {
  const root = folder(`slow-${order}`);
  mkdirSync(join(root, 'app'));
  writeFileSync(join(root, 'app', 'index.ts'), 'start\n');
  let release = () => {};
  const gate = new Promise<void>((resolveGate) => {
    release = resolveGate;
  });
  const original = FileObserver.prototype.baseline;
  let reading: Promise<void> = Promise.resolve();
  let begin = () => {};
  const slow = mock.method(
    FileObserver.prototype,
    'baseline',
    function (this: FileObserver, id: string, directory: string) {
      if (order === 'finished') {
        // The reading is taken at once, as in the plane; only the promise the start waits on is held.
        reading = original.call(this, id, directory);
        return gate.then(() => reading);
      }
      // The reading begins only once the start has stopped waiting for it.
      return new Promise<void>((resolveLate) => {
        begin = () => {
          reading = original.call(this, id, directory);
          void reading.then(resolveLate);
        };
      });
    },
  );
  // The start's wait is a real timer; the control plane's own time is virtual.
  mock.timers.enable({ apis: ['setTimeout'] });
  const settle = async (check: () => boolean, what: string) => {
    for (let tries = 0; tries < 2000; tries += 1) {
      if (check()) return;
      await new Promise((resolveTick) => setImmediate(resolveTick));
    }
    assert.fail(`timed out waiting for ${what}`);
  };
  try {
    const { controlPlane, journal } = observingPlane(createHostLocations([root]), [
      writingRuntime([
        (dir) => writeFileSync(join(dir, 'game.ts'), 'a\nb\n'),
        (dir) => writeFileSync(join(dir, 'game.ts'), 'a\nb\nc\n'),
      ]),
    ]);
    let ids = 0;
    const envelope = (type: CommandEnvelope['command_type'], payload: unknown): CommandEnvelope => {
      ids += 1;
      return {
        schema_version: 1,
        command_id: `00000000-0000-4000-8000-${String(ids).padStart(12, '0')}` as CommandId,
        issued_at: '2026-10-29T10:00:00.000Z',
        client: { name: 'test', version: null, device_label: null },
        command_type: type,
        payload,
      } as CommandEnvelope;
    };
    const submit = (command: CommandEnvelope) => controlPlane.commands.submit(command, 'internal');
    const project = submit(
      envelope('project.create', {
        name: 'Slow',
        location: { kind: 'existing_folder', root, folder_name: 'app' },
      }),
    ).command?.result;
    const projectId = project?.kind === 'project_created' ? project.project_id : '';
    const work = submit(
      envelope('workstream.create', { project_id: projectId, title: 'Slow', objective: null }),
    ).command?.result;
    const workstreamId = work?.kind === 'workstream_created' ? work.workstream_id : '';
    submit(
      envelope('execution.start', {
        workstream_id: workstreamId,
        runtime_id: 'writer',
        instruction: 'Make a game.',
        options: {},
        model_ref: null,
      }),
    );
    const observed = (): EventEnvelope[] =>
      [...journal.readAll()]
        .map((stored) => stored.event)
        .filter((event) => event.event_type === 'execution.files.observed');
    await settle(() => slow.mock.callCount() === 1, 'the first reading asked for');
    if (order === 'finished') await reading;
    mock.timers.tick(10_000);
    if (order === 'late') begin();
    await settle(
      () => observed().length === 1,
      'the first turn observed without waiting for the reading',
    );
    const first = observed()[0];
    if (first?.event_type !== 'execution.files.observed') return assert.fail('no first');
    assert.equal(first.payload.skipped?.code, 'no_baseline');
    // The start's wait ends only now, after the first turn: nothing changes, and later turns compare as usual.
    release();
    await reading;
    const executionId = first.execution_id;
    await settle(
      () => controlPlane.projection.execution(executionId)?.status === 'completed',
      'rest',
    );
    submit(envelope('execution.send_instruction', { execution_id: executionId, text: 'More.' }));
    await settle(() => observed().length === 2, 'the second turn observed');
    const second = observed()[1];
    if (second?.event_type !== 'execution.files.observed') return assert.fail('no second');
    assert.equal(second.payload.skipped, null);
    assert.deepEqual(second.payload.changed.files, [
      { path: 'game.ts', lines_before: 2, lines_after: 3, bytes: 6 },
    ]);
    await controlPlane.close();
  } finally {
    mock.timers.reset();
    slow.mock.restore();
  }
}

describe('a first reading slower than the start waits for', () => {
  test('taken before the wait ended: the start goes ahead, the first turn says no_baseline, the reading is dropped', () =>
    slowFirstReading('finished'));

  test('begun after the wait ended: the start goes ahead, the first turn says no_baseline, the reading is never kept', () =>
    slowFirstReading('late'));
});

describe("a folder that holds Halcyonic's own data", () => {
  // As undo refuses to checkpoint it (ADR 0036): its journal and token are never read (AUDIT L1 C4).
  test('is never read, and each turn says why', async () => {
    const root = folder('host-data');
    mkdirSync(join(root, 'data'));
    writeFileSync(join(root, 'data', 'access-token'), 'not read\n');
    const observer = new FileObserver({ protect: [join(root, 'data')] });
    const id = '01920000-0000-7000-8000-000000000002';
    await observer.baseline(id, root);
    const turn = await observer.turnEnded(id, root);
    assert.equal(turn.skipped?.code, 'holds_host_data');
    assert.deepEqual(turn.created, { files: [], total: 0 });
    assert.equal(turn.not_read, 0);
  });

  test('is recognised before the protected folder exists, and through a link to the project', async () => {
    const root = folder('host-data-later');
    symlinkSync(root, join(base, 'host-data-link'));
    const observer = new FileObserver({ protect: [join(root, 'not', 'made', 'yet')] });
    const turn = await observer.turnEnded(
      '01920000-0000-7000-8000-000000000003',
      join(base, 'host-data-link'),
    );
    assert.equal(turn.skipped?.code, 'holds_host_data');
  });

  test('a protected folder outside the project changes nothing', async () => {
    const root = folder('host-data-elsewhere');
    writeFileSync(join(root, 'a.ts'), 'a\n');
    const observer = new FileObserver({ protect: [folder('elsewhere-data')] });
    const id = '01920000-0000-7000-8000-000000000004';
    await observer.baseline(id, root);
    writeFileSync(join(root, 'b.ts'), 'b\n');
    const turn = await observer.turnEnded(id, root);
    assert.equal(turn.skipped, null);
    assert.deepEqual(turn.created.files, [{ path: 'b.ts', lines: 1, bytes: 2 }]);
  });
});

describe('a file changed with its size kept and its time set back', () => {
  // touch -m -t can put a file's modification time back; its change time cannot be (AUDIT L1 C6).
  test('is read again and shows as changed', async () => {
    const root = folder('same-size');
    const path = join(root, 'game.ts');
    writeFileSync(path, 'aaaa\n');
    const then = 1_790_000_000;
    utimesSync(path, then, then);
    const before = await readFolder(root);
    assert.ok(before.ok);
    writeFileSync(path, 'bbbb\n');
    utimesSync(path, then, then);
    const afterReading = await readFolder(root, before.reading);
    assert.ok(afterReading.ok);
    assert.deepEqual(compareReadings(before.reading, afterReading.reading).changed, [
      { path: 'game.ts', lines_before: 1, lines_after: 1, bytes: 5 },
    ]);
  });
});

describe('a project folder at the root of the disk', () => {
  test('holds every protected folder', () => {
    assert.equal(within('/', '/Users/someone/.halcyonic'), true);
    assert.equal(within('/Users/someone', '/Users/someone/.halcyonic'), true);
    assert.equal(within('/Users/some', '/Users/someone/.halcyonic'), false);
  });
});

describe('the bound on a reading', () => {
  test('a folder with more bytes than it reads is refused as too large', async () => {
    const root = folder('bytes');
    writeFileSync(join(root, 'big.bin'), Buffer.alloc(2048));
    const read = await readFolder(root, undefined, {
      files: 100,
      bytes: 1024,
      countLinesUpTo: 1024,
    });
    assert.equal(!read.ok && read.code, 'too_large');
  });
});

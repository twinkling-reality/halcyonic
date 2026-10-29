import assert from 'node:assert/strict';
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
import { after, describe, test } from 'node:test';
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
    observeFiles: true,
  });
  return { controlPlane, journal };
}

import { confirmProjectLocation, createVirtualTime, type Scheduler } from '@halcyonic/runtime-core';
import { ControlPlane } from '../core/control-plane.ts';
import { createUuidV7Generator } from '../ids.ts';
import { openSqliteJournal } from '../journal/sqlite-journal.ts';
import { createHostLocations, type HostLocations } from '../locations.ts';
import { capturingLogger } from '../testing/harness.ts';
import { compareReadings, readFolder } from './file-observer.ts';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-observer-')));
after(() => rmSync(base, { recursive: true, force: true }));

function folder(name: string): string {
  const path = join(base, name);
  mkdirSync(path, { recursive: true });
  return path;
}

describe('reading a folder', () => {
  test('counts the lines of text files, gives binary files none, and skips .git, node_modules and links', async () => {
    const root = folder('read');
    writeFileSync(join(root, 'a.ts'), 'one\ntwo\nthree');
    writeFileSync(join(root, 'empty.txt'), '');
    writeFileSync(join(root, 'image.png'), Buffer.from([0x89, 0x50, 0x00, 0x01, 0x0a]));
    mkdirSync(join(root, '.git'));
    writeFileSync(join(root, '.git', 'HEAD'), 'ref\n');
    mkdirSync(join(root, 'src', 'node_modules'), { recursive: true });
    writeFileSync(join(root, 'src', 'node_modules', 'x.js'), 'x\n');
    symlinkSync(join(base), join(root, 'node_modules'));
    symlinkSync('a.ts', join(root, 'link.ts'));
    const read = await readFolder(root);
    assert.ok(read.ok);
    assert.deepEqual([...read.reading.keys()].sort(), ['a.ts', 'empty.txt', 'image.png']);
    assert.equal(read.reading.get('a.ts')?.lines, 3);
    assert.equal(read.reading.get('empty.txt')?.lines, 0);
    assert.equal(read.reading.get('image.png')?.lines, null);
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

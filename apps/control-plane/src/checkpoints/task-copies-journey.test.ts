import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import type {
  CheckpointId,
  CommandEnvelope,
  CommandId,
  EventEnvelope,
  ProjectId,
  RuntimeDescriptor,
  RuntimeId,
  WorkstreamId,
} from '@halcyonic/contracts';
import {
  createVirtualTime,
  type ObservationSink,
  type RuntimeAdapter,
  type RuntimeObservation,
} from '@halcyonic/runtime-core';
import { ControlPlane } from '../core/control-plane.ts';
import { createUuidV7Generator } from '../ids.ts';
import { openSqliteJournal } from '../journal/sqlite-journal.ts';
import { createHostLocations } from '../locations.ts';
import { capturingLogger } from '../testing/harness.ts';
import { CheckpointStore } from './store.ts';

// Several agents on one project, each in its own copy of the folder (ADR 0045), through a real
// control plane, the system's git and APFS clones.

const GIT = '/usr/bin/git';
const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-task-copies-')));
after(() => rmSync(base, { recursive: true, force: true }));

/**
 * A runtime that works in the folder it is given: on start it writes its instruction into
 * `app.txt`, as an agent would, and keeps its turn open until `finish` is called.
 */
function writingRuntime() {
  let observations = 0;
  const sinks: ObservationSink[] = [];
  const descriptor: RuntimeDescriptor = {
    runtime_id: 'writer' as RuntimeId,
    kind: 'stub',
    display_name: 'Stub that writes in its folder',
    synthetic: true,
    capabilities: {
      start_execution: true,
      instruct_at_rest: false,
      instruct_while_running: false,
      respond_to_approval: false,
      answer_question: false,
      interrupt: false,
    },
    model_choice: 'none',
    uses_project_location: true,
    reports_tool_activity: false,
  };
  const observe = (sink: ObservationSink, type: RuntimeObservation['type'], payload: unknown) => {
    observations += 1;
    sink({
      type,
      occurred_at: new Date().toISOString(),
      native_event_id: `writer-${observations}`,
      sequence: null,
      provenance: { epistemic: 'observed', native_type: null },
      payload,
    } as RuntimeObservation);
  };
  const adapter: RuntimeAdapter = {
    descriptor,
    validateStartOptions: () => ({ ok: true }),
    startExecution: async (request) => {
      sinks.push(request.emit);
      observe(request.emit, 'runtime.execution.started', { native_id: `s-${observations}` });
      observe(request.emit, 'runtime.turn.started', { turn_id: 'turn-1' });
      writeFileSync(join(request.directory ?? '', 'app.txt'), `${request.instruction}\n`);
      return { native_id: `s-${observations}` };
    },
    close: async () => {},
  };
  const finish = () => {
    for (const sink of sinks.splice(0))
      observe(sink, 'runtime.turn.completed', { turn_id: 'turn-1' });
  };
  return { adapter, finish };
}

function setup(name: string, options: { linkedRepository?: boolean } = {}) {
  const root = join(base, name);
  const projects = join(root, 'projects');
  const folder = join(projects, 'app');
  mkdirSync(folder, { recursive: true });
  writeFileSync(join(folder, 'app.txt'), 'person\n');
  if (options.linkedRepository)
    writeFileSync(join(folder, '.git'), 'gitdir: /elsewhere/.git/worktrees/app\n');
  const tasks = join(root, 'tasks');
  const time = createVirtualTime(new Date('2026-11-10T10:00:00.000Z'));
  const ids = createUuidV7Generator({ now: () => Date.now() });
  const journal = openSqliteJournal({ path: ':memory:', originIfNew: 'live', ids });
  const runtime = writingRuntime();
  const controlPlane = new ControlPlane({
    journal,
    adapters: [runtime.adapter],
    ids,
    clock: time,
    scheduler: time,
    logger: capturingLogger().logger,
    commandTimeoutMs: 30_000,
    locations: createHostLocations([projects]),
    checkpoints: {
      store: new CheckpointStore({ git: GIT, directory: join(root, 'checkpoints') }),
      copies: { directory: tasks },
    },
  });
  let commands = 0;
  const submit = <T extends CommandEnvelope['command_type']>(
    commandType: T,
    payload: Extract<CommandEnvelope, { command_type: T }>['payload'],
  ) => {
    commands += 1;
    return controlPlane.commands.submit(
      {
        schema_version: 1,
        command_id: `00000000-0000-4000-8000-${String(commands).padStart(12, '0')}` as CommandId,
        issued_at: '2026-11-10T10:00:00.000Z',
        client: { name: 'test', version: null, device_label: null },
        command_type: commandType,
        payload,
      } as CommandEnvelope,
      'internal',
    );
  };
  const created = submit('project.create', {
    name: 'Garden',
    location: { kind: 'existing_folder', root: projects, folder_name: 'app' },
  });
  const projectId = (
    created.command?.result?.kind === 'project_created' ? created.command.result.project_id : ''
  ) as ProjectId;
  const task = (title: string) => {
    const made = submit('workstream.create', { project_id: projectId, title, objective: null });
    return (
      made.command?.result?.kind === 'workstream_created' ? made.command.result.workstream_id : ''
    ) as WorkstreamId;
  };
  const start = (workstreamId: WorkstreamId, instruction: string) =>
    submit('execution.start', {
      workstream_id: workstreamId,
      runtime_id: 'writer' as RuntimeId,
      instruction,
      options: {},
      model_ref: null,
    });
  const events = (): EventEnvelope[] => [...journal.readAll()].map((stored) => stored.event);
  const of = <T extends EventEnvelope['event_type']>(type: T, workstreamId?: WorkstreamId) =>
    events().filter(
      (event): event is Extract<EventEnvelope, { event_type: T }> =>
        event.event_type === type &&
        (workstreamId === undefined || event.workstream_id === workstreamId),
    );
  return {
    root,
    folder,
    tasks,
    controlPlane,
    projectId,
    task,
    start,
    submit,
    events,
    of,
    ...runtime,
  };
}

/** Waits, on real time, until the journal holds what the test expects. */
async function until(check: () => boolean, what: string): Promise<void> {
  for (let tries = 0; tries < 1000; tries += 1) {
    if (check()) return;
    await new Promise((resolveWait) => setTimeout(resolveWait, 10));
  }
  assert.fail(`timed out waiting for ${what}`);
}

describe('several agents on one project (ADR 0045)', {
  skip: process.platform !== 'darwin',
}, () => {
  test('two tasks started one after the other run at once, each changing only its own copy', async () => {
    const { folder, tasks, controlPlane, task, start, events, of, finish } = setup('two');
    const first = task('Lanterns');
    const second = task('Paths');
    assert.equal(start(first, 'lanterns').disposition, 'accepted');
    assert.equal(start(second, 'paths').disposition, 'accepted', 'no wait for the first copy');
    await until(() => of('runtime.turn.started').length === 2, 'both agents at work');

    const directories = of('execution.created').map((event) => event.payload.directory);
    const copyOf = (id: WorkstreamId) => {
      const copy = controlPlane.projection.workstream(id)?.copy;
      assert.ok(copy?.state === 'own', 'the task has a copy of its own');
      return copy.directory;
    };
    assert.deepEqual(new Set(directories), new Set([copyOf(first), copyOf(second)]));
    for (const id of [first, second]) {
      assert.ok(copyOf(id).startsWith(`${realpathSync(tasks)}/`), 'beside, never in, the project');
      assert.equal(of('workstream.copy_made', id).length, 1);
      // The copy is recorded before the execution that works in it.
      const order = events()
        .filter((event) => event.workstream_id === id)
        .map((event) => event.event_type);
      assert.ok(order.indexOf('workstream.copy_made') < order.indexOf('execution.created'));
    }
    assert.equal(readFileSync(join(copyOf(first), 'app.txt'), 'utf8'), 'lanterns\n');
    assert.equal(readFileSync(join(copyOf(second), 'app.txt'), 'utf8'), 'paths\n');
    assert.equal(readFileSync(join(folder, 'app.txt'), 'utf8'), 'person\n', 'the project waits');

    // The task's next start works in the same copy.
    finish();
    await until(() => of('runtime.turn.completed').length === 2, 'both turns to end');
    assert.equal(start(first, 'lanterns again').disposition, 'accepted');
    await until(() => of('execution.created', first).length === 2, 'the second start');
    assert.equal(of('workstream.copy_made', first).length, 1);
    assert.equal(of('execution.created', first)[1]?.payload.directory, copyOf(first));
  });

  test('a folder that cannot be copied keeps one task at a time, and says why', async () => {
    const { folder, controlPlane, task, start, of } = setup('linked', { linkedRepository: true });
    const first = task('Lanterns');
    const second = task('Paths');
    // Both admitted while neither was refused a copy; the host asks the folder rule again.
    assert.equal(start(first, 'lanterns').disposition, 'accepted');
    assert.equal(start(second, 'paths').disposition, 'accepted');
    await until(() => of('command.failed').length === 1, 'the second start to fail');
    assert.equal(of('workstream.copy_refused').length, 2);
    assert.equal(of('workstream.copy_refused', first)[0]?.payload.code, 'linked_repository');
    const created = of('execution.created');
    assert.equal(created.length, 1, 'one agent in the folder');
    assert.equal(created[0]?.payload.directory, realpathSync(folder));
    const failed = of('command.failed')[0];
    assert.equal(failed?.payload.failure.code, 'folder_in_use');
    assert.equal(failed?.payload.failure.effect, 'none');
    assert.match(
      failed?.payload.failure.message ?? '',
      /Another task is (starting|running) in this folder/,
    );
    assert.equal(controlPlane.projection.workstream(first)?.copy?.state, 'refused');
    // Once refused, a start of another task there is refused at once, in words.
    const third = task('Ponds');
    const refused = start(third, 'ponds');
    assert.equal(refused.disposition, 'rejected');
  });

  test("undoing a task's turn puts its own copy back, never the project's folder", async () => {
    const { folder, controlPlane, projectId, task, start, submit, of, finish } = setup('undo');
    const first = task('Lanterns');
    start(first, 'lanterns');
    await until(() => of('runtime.turn.started').length === 1, 'the agent at work');
    finish();
    await until(
      () =>
        of('project.checkpoint.recorded').some((event) => event.payload.reason === 'after_turn'),
      'the after-turn checkpoint',
    );
    const copy = controlPlane.projection.workstream(first)?.copy;
    assert.ok(copy?.state === 'own');
    writeFileSync(join(folder, 'app.txt'), 'person, later\n');
    const beforeTask = of('project.checkpoint.recorded').find(
      (event) => event.payload.reason === 'before_task',
    );
    assert.ok(beforeTask !== undefined);
    const restore = submit('project.restore', {
      project_id: projectId,
      checkpoint_id: beforeTask.payload.checkpoint_id as CheckpointId,
    });
    assert.equal(restore.disposition, 'accepted');
    await until(() => of('project.restored').length === 1, 'the restore');
    assert.equal(readFileSync(join(copy.directory, 'app.txt'), 'utf8'), 'person\n');
    assert.equal(readFileSync(join(folder, 'app.txt'), 'utf8'), 'person, later\n');
  });
});

import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
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
import { CheckpointStore, type RestoreHooks } from './store.ts';

const GIT = '/usr/bin/git';
const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-checkpointer-')));
after(() => rmSync(base, { recursive: true, force: true }));

/**
 * A runtime that works in the project's folder: on start it writes `app.txt` as an agent would,
 * then ends its turn, unless told to keep the turn open.
 */
function writingRuntime(options: { holdTurn?: boolean } = {}) {
  let session = 0;
  let sink: ObservationSink | null = null;
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
  const observe = (type: RuntimeObservation['type'], payload: unknown) => {
    session += 1;
    sink?.({
      type,
      occurred_at: new Date().toISOString(),
      native_event_id: `writer-${session}`,
      sequence: null,
      provenance: { epistemic: 'observed', native_type: null },
      payload,
    } as RuntimeObservation);
  };
  const adapter: RuntimeAdapter = {
    descriptor,
    validateStartOptions: () => ({ ok: true }),
    startExecution: async (request) => {
      sink = request.emit;
      observe('runtime.execution.started', { native_id: `writer-session-${session}` });
      observe('runtime.turn.started', { turn_id: 'turn-1' });
      writeFileSync(join(request.directory ?? '', 'app.txt'), 'agent\n');
      if (!options.holdTurn) observe('runtime.turn.completed', { turn_id: 'turn-1' });
      return { native_id: `writer-session-${session}` };
    },
    close: async () => {},
  };
  return adapter;
}

function setup(
  name: string,
  options: {
    undo?: boolean;
    holdTurn?: boolean;
    restoreHooks?: RestoreHooks;
    journalPath?: string;
  } = {},
) {
  const root = join(base, name);
  const folder = join(root, 'app');
  mkdirSync(folder, { recursive: true });
  writeFileSync(join(folder, 'app.txt'), 'person\n');
  const time = createVirtualTime(new Date('2026-10-28T10:00:00.000Z'));
  const ids = createUuidV7Generator({ now: () => Date.now() });
  const journal = openSqliteJournal({
    path: options.journalPath ?? ':memory:',
    originIfNew: 'live',
    ids,
  });
  const store = new CheckpointStore({ git: GIT, directory: join(root, 'checkpoints') });
  const controlPlane = new ControlPlane({
    journal,
    adapters: [writingRuntime({ holdTurn: options.holdTurn ?? false })],
    ids,
    clock: time,
    scheduler: time,
    logger: capturingLogger().logger,
    commandTimeoutMs: 30_000,
    locations: createHostLocations([root]),
    ...(options.undo === false
      ? {}
      : {
          checkpoints: {
            store,
            ...(options.restoreHooks === undefined ? {} : { restoreHooks: options.restoreHooks }),
          },
        }),
  });
  let commands = 0;
  const envelope = <T extends CommandEnvelope['command_type']>(
    commandType: T,
    payload: Extract<CommandEnvelope, { command_type: T }>['payload'],
  ): CommandEnvelope => {
    commands += 1;
    return {
      schema_version: 1,
      command_id: `00000000-0000-4000-8000-${String(commands).padStart(12, '0')}` as CommandId,
      issued_at: '2026-10-28T10:00:00.000Z',
      client: { name: 'test', version: null, device_label: null },
      command_type: commandType,
      payload,
    } as CommandEnvelope;
  };
  const submit = (command: CommandEnvelope) => controlPlane.commands.submit(command, 'internal');
  const created = submit(
    envelope('project.create', {
      name: 'Garden',
      location: { kind: 'existing_folder', root, folder_name: 'app' },
    }),
  );
  const projectId = (
    created.command?.result?.kind === 'project_created' ? created.command.result.project_id : ''
  ) as ProjectId;
  const work = submit(
    envelope('workstream.create', { project_id: projectId, title: 'Lanterns', objective: null }),
  );
  const workstreamId = (
    work.command?.result?.kind === 'workstream_created' ? work.command.result.workstream_id : ''
  ) as WorkstreamId;
  const start = () =>
    submit(
      envelope('execution.start', {
        workstream_id: workstreamId,
        runtime_id: 'writer' as RuntimeId,
        instruction: 'Make it glow.',
        options: {},
        model_ref: null,
      }),
    );
  const restore = (checkpointId: string) =>
    submit(
      envelope('project.restore', {
        project_id: projectId,
        checkpoint_id: checkpointId as CheckpointId,
      }),
    );
  const events = (): EventEnvelope[] => [...journal.readAll()].map((stored) => stored.event);
  const types = () => events().map((event) => event.event_type);
  return { root, folder, store, ids, time, controlPlane, projectId, start, restore, events, types };
}

/** Waits, on real time, until the journal holds what the test expects. */
async function until(check: () => boolean, what: string): Promise<void> {
  for (let tries = 0; tries < 400; tries += 1) {
    if (check()) return;
    await new Promise((resolveWait) => setTimeout(resolveWait, 10));
  }
  assert.fail(`timed out waiting for ${what}`);
}

function checkpointOf(events: EventEnvelope[], reason: string): string {
  const found = events.find(
    (event) =>
      event.event_type === 'project.checkpoint.recorded' && event.payload.reason === reason,
  );
  if (found?.event_type !== 'project.checkpoint.recorded')
    throw new Error(`no ${reason} checkpoint`);
  return found.payload.checkpoint_id;
}

describe('undo is off unless the host sets it up', () => {
  test('no checkpoint is taken, and a restore is refused as unsupported', async () => {
    const { start, restore, types, controlPlane } = setup('off', { undo: false });
    start();
    await until(() => types().includes('runtime.turn.completed'), 'the turn to end');
    assert.equal(
      types().some((type) => type.startsWith('project.checkpoint')),
      false,
    );
    const refused = restore('01920000-0000-7000-8000-000000000001');
    assert.equal(refused.disposition, 'rejected');
    assert.equal(refused.command?.rejection?.code, 'capability_unsupported');
    assert.equal(controlPlane.checkpoints, null);
    await controlPlane.close();
  });
});

describe('checkpoints around a task', () => {
  test('one before the runtime is asked, one after the turn it reports ended', async () => {
    const { start, types, events, controlPlane } = setup('around');
    start();
    await until(
      () => types().filter((type) => type === 'project.checkpoint.recorded').length === 2,
      'two checkpoints',
    );
    const order = types();
    assert.ok(
      order.indexOf('project.checkpoint.recorded') < order.indexOf('runtime.execution.started'),
      'the folder is kept before the runtime starts',
    );
    const recorded = events().filter((event) => event.event_type === 'project.checkpoint.recorded');
    assert.deepEqual(
      recorded.map(
        (event) => event.event_type === 'project.checkpoint.recorded' && event.payload.reason,
      ),
      ['before_task', 'after_turn'],
    );
    assert.equal(controlPlane.projection.checkpoints(recorded[0]?.project_id ?? '').length, 2);
    await controlPlane.close();
  });
});

describe('putting a project back', () => {
  test('completes only after the check, journals counts and paths inside the project only', async () => {
    const { root, folder, start, restore, types, events, controlPlane } = setup('restore');
    start();
    await until(
      () => types().filter((t) => t === 'project.checkpoint.recorded').length === 2,
      'checkpoints',
    );
    writeFileSync(join(folder, 'extra.txt'), 'new\n');
    const accepted = restore(checkpointOf(events(), 'before_task'));
    assert.equal(accepted.disposition, 'accepted');
    assert.equal(accepted.command?.status, 'accepted', 'not complete before the check');
    await until(
      () => types().includes('command.completed') && types().includes('project.restored'),
      'the restore',
    );
    assert.equal(readFileSync(join(folder, 'app.txt'), 'utf8'), 'person\n');
    const tail = types().slice(-4);
    assert.deepEqual(tail, [
      'command.accepted',
      'project.checkpoint.recorded',
      'project.restored',
      'command.completed',
    ]);
    const restored = events().find((event) => event.event_type === 'project.restored');
    assert.equal(restored?.event_type, 'project.restored');
    if (restored?.event_type !== 'project.restored') return;
    assert.deepEqual(restored.payload.changed_back, { paths: ['app.txt'], total: 1 });
    assert.deepEqual(restored.payload.removed, { paths: ['extra.txt'], total: 1 });
    assert.equal(restored.payload.files_compared, 1);
    for (const event of events().filter(
      (e) => e.event_type.startsWith('project.') && e.event_type !== 'project.created',
    ))
      assert.equal(JSON.stringify(event.payload).includes(root), false, event.event_type);
    await controlPlane.close();
  });

  test('a byte different after writing: the restore fails as incomplete, effect unknown, never completed', async () => {
    let bump: (() => void) | null = null;
    const { folder, start, restore, types, events, controlPlane } = setup('byte', {
      restoreHooks: { onWritten: () => bump?.() },
    });
    bump = () => writeFileSync(join(folder, 'app.txt'), 'persoN\n');
    start();
    await until(
      () => types().filter((t) => t === 'project.checkpoint.recorded').length === 2,
      'checkpoints',
    );
    restore(checkpointOf(events(), 'before_task'));
    await until(() => types().includes('command.failed'), 'the restore to fail');
    assert.equal(types().includes('project.restored'), false);
    assert.equal(types().filter((type) => type === 'command.completed').length, 3);
    const incomplete = events().find((event) => event.event_type === 'project.restore_incomplete');
    assert.equal(incomplete?.event_type, 'project.restore_incomplete');
    if (incomplete?.event_type !== 'project.restore_incomplete') return;
    assert.equal(incomplete.payload.written, true);
    assert.deepEqual(incomplete.payload.not_restored, { paths: ['app.txt'], total: 1 });
    const failed = events().find((event) => event.event_type === 'command.failed');
    assert.equal(
      failed?.event_type === 'command.failed' && failed.payload.failure.effect,
      'unknown',
    );
    await controlPlane.close();
  });

  test('nothing kept first means nothing is changed: incomplete, effect none', async () => {
    const { folder, start, restore, types, events, controlPlane } = setup('nothing-kept');
    start();
    await until(
      () => types().filter((t) => t === 'project.checkpoint.recorded').length === 2,
      'checkpoints',
    );
    mkdirSync(join(folder, 'nested'));
    execFileSync(GIT, ['init', '-q', join(folder, 'nested')]);
    restore(checkpointOf(events(), 'before_task'));
    await until(() => types().includes('command.failed'), 'the restore to fail');
    assert.equal(readFileSync(join(folder, 'app.txt'), 'utf8'), 'agent\n');
    const failed = events().find((event) => event.event_type === 'command.failed');
    assert.equal(failed?.event_type === 'command.failed' && failed.payload.failure.effect, 'none');
    assert.equal(types().includes('project.checkpoint.failed'), true);
    await controlPlane.close();
  });

  test('a file name the contract cannot list is counted, and the restore still completes', async () => {
    const { folder, start, restore, types, events, controlPlane } = setup('odd-name');
    start();
    await until(
      () => types().filter((t) => t === 'project.checkpoint.recorded').length === 2,
      'checkpoints',
    );
    writeFileSync(join(folder, 'odd\nname.txt'), 'x\n');
    restore(checkpointOf(events(), 'before_task'));
    await until(() => types().includes('project.restored'), 'the restore');
    const restored = events().find((event) => event.event_type === 'project.restored');
    if (restored?.event_type !== 'project.restored') return assert.fail('no restore');
    assert.deepEqual(restored.payload.removed, { paths: [], total: 1 });
    await controlPlane.close();
  });

  test('the undo can be undone: the checkpoint before it brings the agent’s files back', async () => {
    const { folder, start, restore, types, events, controlPlane } = setup('undo-undo');
    start();
    await until(
      () => types().filter((t) => t === 'project.checkpoint.recorded').length === 2,
      'checkpoints',
    );
    restore(checkpointOf(events(), 'before_task'));
    await until(() => types().includes('project.restored'), 'the first restore');
    restore(checkpointOf(events(), 'before_restore'));
    await until(
      () => types().filter((type) => type === 'project.restored').length === 2,
      'the second',
    );
    assert.equal(readFileSync(join(folder, 'app.txt'), 'utf8'), 'agent\n');
    await controlPlane.close();
  });

  test('is refused while the task’s turn is still running', async () => {
    const { start, restore, types, events, controlPlane } = setup('running', { holdTurn: true });
    start();
    await until(() => types().includes('runtime.turn.started'), 'the turn to start');
    await until(() => types().includes('project.checkpoint.recorded'), 'the first checkpoint');
    const refused = restore(checkpointOf(events(), 'before_task'));
    assert.equal(refused.disposition, 'rejected');
    assert.equal(refused.command?.rejection?.code, 'invalid_state');
    await controlPlane.close();
  });

  test('a restore in flight when the control plane stops reads as failed, effect unknown, after it starts again', async () => {
    const journalPath = join(base, 'restart.db');
    let written = false;
    const { root, store, ids, time, start, restore, types, events, controlPlane } = setup(
      'restart',
      {
        journalPath,
        // The restore writes, then never gets to its check, as if the control plane stopped there.
        restoreHooks: {
          onWritten: () => {
            written = true;
            return new Promise<void>(() => {});
          },
        },
      },
    );
    start();
    await until(
      () => types().filter((t) => t === 'project.checkpoint.recorded').length === 2,
      'checkpoints',
    );
    restore(checkpointOf(events(), 'before_task'));
    await until(() => written, 'the write');
    await controlPlane.close();

    const reopened = new ControlPlane({
      journal: openSqliteJournal({ path: journalPath, originIfNew: 'live', ids }),
      adapters: [writingRuntime()],
      ids,
      clock: time,
      scheduler: time,
      logger: capturingLogger().logger,
      commandTimeoutMs: 30_000,
      locations: createHostLocations([root]),
      checkpoints: { store },
    });
    reopened.reconcile();
    const failed = [...reopened.journal.readAll()]
      .map((stored) => stored.event)
      .find(
        (event) =>
          event.event_type === 'command.failed' && event.payload.command_type === 'project.restore',
      );
    assert.equal(
      failed?.event_type === 'command.failed' && failed.payload.failure.effect,
      'unknown',
    );
    await reopened.close();
  });
});

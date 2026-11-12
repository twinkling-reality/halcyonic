import assert from 'node:assert/strict';
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  realpathSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import type {
  CheckpointId,
  CheckpointView,
  CommandEnvelope,
  CommandId,
  DeviceId,
  EventEnvelope,
  Principal,
  ProjectId,
  RuntimeDescriptor,
  RuntimeId,
  Timestamp,
  WorkstreamId,
} from '@halcyonic/contracts';
import {
  createVirtualTime,
  type ObservationSink,
  type RuntimeAdapter,
  type RuntimeObservation,
} from '@halcyonic/runtime-core';
import { ControlPlane } from '../core/control-plane.ts';
import { controlPlaneDraft, NO_CAUSE } from '../core/drafts.ts';
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
function writingRuntime(runtimeId = 'writer') {
  let observations = 0;
  const sinks: ObservationSink[] = [];
  const descriptor: RuntimeDescriptor = {
    runtime_id: runtimeId as RuntimeId,
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

function setup(name: string, options: { linkedRepository?: boolean; copiesOff?: boolean } = {}) {
  const root = join(base, name);
  const projects = join(root, 'projects');
  const folder = join(projects, 'app');
  mkdirSync(folder, { recursive: true });
  writeFileSync(join(folder, 'app.txt'), 'person\n');
  if (options.linkedRepository)
    writeFileSync(join(folder, '.git'), 'gitdir: /elsewhere/.git/worktrees/app\n');
  const tasks = join(root, 'tasks');
  const downloads = join(root, 'Downloads');
  mkdirSync(downloads, { recursive: true });
  const time = createVirtualTime(new Date('2026-11-10T10:00:00.000Z'));
  const ids = createUuidV7Generator({ now: () => Date.now() });
  const journal = openSqliteJournal({ path: ':memory:', originIfNew: 'live', ids });
  const runtime = writingRuntime();
  // A runtime this computer does not hold to one folder: it never works in a copy (ADR 0045).
  const loose = writingRuntime('loose');
  const controlPlane = new ControlPlane({
    journal,
    adapters: [runtime.adapter, loose.adapter],
    ids,
    clock: time,
    scheduler: time,
    logger: capturingLogger().logger,
    commandTimeoutMs: 30_000,
    locations: createHostLocations([projects]),
    checkpoints: {
      store: new CheckpointStore({ git: GIT, directory: join(root, 'checkpoints') }),
      keeping: { downloads, reveal: async () => true },
      ...(options.copiesOff
        ? {}
        : { copies: { directory: tasks, heldToFolder: (runtimeId) => runtimeId === 'writer' } }),
    },
  });
  let commands = 0;
  const submit = <T extends CommandEnvelope['command_type']>(
    commandType: T,
    payload: Extract<CommandEnvelope, { command_type: T }>['payload'],
    principal: Principal | null = null,
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
      principal,
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
  const start = (workstreamId: WorkstreamId, instruction: string, runtimeId = 'writer') =>
    submit('execution.start', {
      workstream_id: workstreamId,
      runtime_id: runtimeId as RuntimeId,
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
    finishLoose: loose.finish,
    downloads,
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

  test('a start removes no copy, and each task whose copy is gone says so, once (ADR 0045, amended)', async () => {
    const { tasks, controlPlane, task, start, of, finish } = setup('unheld');
    const first = task('Lanterns');
    assert.equal(start(first, 'lanterns').disposition, 'accepted');
    await until(() => of('runtime.turn.started').length === 1, 'the agent at work');
    finish();
    await until(() => of('runtime.turn.completed').length === 1, 'the turn to end');
    const copy = controlPlane.projection.workstream(first)?.copy;
    assert.ok(copy?.state === 'own');
    // What another control plane on this Mac keeps here, or a restored older journal no longer names.
    const foreign = join(
      tasks,
      '01920000-0000-7000-8000-00000000f0f0',
      '01920000-0000-7000-8000-00000000f0f1',
      'app',
    );
    mkdirSync(foreign, { recursive: true });
    writeFileSync(join(foreign, 'work.txt'), 'not added yet\n');
    assert.deepEqual(await controlPlane.noteCopies(), {
      missing: 0,
      found: 0,
      unheld: 1,
      other: 0,
    });
    assert.equal(
      readFileSync(join(foreign, 'work.txt'), 'utf8'),
      'not added yet\n',
      'never removed',
    );
    assert.equal(readFileSync(join(copy.directory, 'app.txt'), 'utf8'), 'lanterns\n');
    // The task's own copy gone: recorded once, and the task's view says when.
    rmSync(copy.directory, { recursive: true, force: true });
    assert.deepEqual(await controlPlane.noteCopies(), {
      missing: 1,
      found: 0,
      unheld: 1,
      other: 0,
    });
    assert.deepEqual(await controlPlane.noteCopies(), {
      missing: 0,
      found: 0,
      unheld: 1,
      other: 0,
    });
    assert.equal(of('workstream.copy_missing', first).length, 1);
    assert.equal(of('workstream.copy_missing', first)[0]?.payload.directory, copy.directory);
    const gone = controlPlane.projection.workstream(first)?.copy;
    assert.ok(gone?.state === 'own' && typeof gone.missing_at === 'string');
    // Back again (a restored backup): said too, and the view no longer says it is gone.
    mkdirSync(copy.directory, { recursive: true });
    assert.deepEqual(await controlPlane.noteCopies(), {
      missing: 0,
      found: 1,
      unheld: 1,
      other: 0,
    });
    assert.equal(of('workstream.copy_found', first).length, 1);
    const back = controlPlane.projection.workstream(first)?.copy;
    assert.ok(back?.state === 'own' && back.missing_at === undefined);
    assert.equal(readFileSync(join(foreign, 'work.txt'), 'utf8'), 'not added yet\n');
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

  test('a task from an older version works on that version in its own copy, the project untouched, and its add brings its work beside what the project gained since (ADR 0045)', async () => {
    const { folder, controlPlane, projectId, task, start, submit, of, finish } = setup('version');
    writeFileSync(join(folder, '.gitignore'), 'node_modules\n');
    mkdirSync(join(folder, 'node_modules'), { recursive: true });
    writeFileSync(join(folder, 'node_modules', 'kept.txt'), 'installed\n');
    // A first task changes app.txt and its work is added: its copy's turn is a version with it.
    const first = task('Lanterns');
    start(first, 'lanterns');
    await until(() => of('runtime.turn.started').length === 1, 'the first agent at work');
    finish();
    await until(
      () => of('workstream.copy_changes_read', first).length === 1,
      'the first copy read',
    );
    const turnOf = (workstreamId: WorkstreamId) =>
      controlPlane.projection
        .checkpoints(projectId)
        .find(
          (checkpoint) =>
            checkpoint.reason === 'after_turn' &&
            controlPlane.projection.execution(checkpoint.execution_id ?? '')?.workstream_id ===
              workstreamId,
        );
    await until(() => turnOf(first) !== undefined, "the first turn's version");
    const version = turnOf(first) as CheckpointView;
    submit('workstream.add_to_project', { workstream_id: first });
    await until(() => of('workstream.added', first).length === 1, 'the first add');
    // The project gains a file the version never had.
    writeFileSync(join(folder, 'later.txt'), 'the person, later\n');
    // A version that is not one of this project's is refused.
    const missing = submit('workstream.create', {
      project_id: projectId,
      title: 'Nowhere',
      objective: null,
      from_checkpoint: '01920000-0000-7000-8000-0000000000ff' as CheckpointId,
    });
    assert.equal(missing.command?.rejection?.code, 'checkpoint_not_found');
    const made = submit('workstream.create', {
      project_id: projectId,
      title: 'Paths',
      objective: null,
      from_checkpoint: version.checkpoint_id,
    });
    const second = (
      made.command?.result?.kind === 'workstream_created' ? made.command.result.workstream_id : ''
    ) as WorkstreamId;
    assert.equal(
      controlPlane.projection.workstream(second)?.from_checkpoint,
      version.checkpoint_id,
    );
    start(second, 'paths');
    await until(() => of('workstream.copy_made', second).length === 1, 'the copy from the version');
    const copyMade = of('workstream.copy_made', second)[0];
    assert.equal(copyMade?.payload.base_tree, version.tree, "its base is the version's tree");
    const copy = copyMade?.payload.directory ?? '';
    await until(() => of('runtime.turn.started').length === 2, 'the second agent at work');
    assert.equal(
      existsSync(join(copy, 'later.txt')),
      false,
      'the version, not the folder as it is',
    );
    assert.equal(
      readFileSync(join(copy, 'node_modules', 'kept.txt'), 'utf8'),
      'installed\n',
      'ignored files kept',
    );
    const written = readFileSync(join(copy, 'app.txt'), 'utf8');
    assert.match(
      written,
      /^paths\n\nFrom Halcyonic: this task's folder is a copy of the project as it was/,
    );
    assert.ok(written.includes(version.recorded_at), 'the agent is told which version');
    // The project's own folder is untouched.
    assert.equal(readFileSync(join(folder, 'app.txt'), 'utf8'), 'lanterns\n');
    assert.equal(readFileSync(join(folder, 'later.txt'), 'utf8'), 'the person, later\n');
    finish();
    await until(
      () => of('workstream.copy_changes_read', second).length === 1,
      'the second copy read',
    );
    submit('workstream.add_to_project', { workstream_id: second });
    await until(() => of('workstream.added', second).length === 1, 'the second add');
    assert.equal(readFileSync(join(folder, 'app.txt'), 'utf8'), written, "the task's work added");
    assert.equal(
      readFileSync(join(folder, 'later.txt'), 'utf8'),
      'the person, later\n',
      'beside what the project gained since',
    );
  });

  test('a task from a version whose file the project changed since meets it in a conflict, nothing written (ADR 0045)', async () => {
    const { folder, controlPlane, projectId, task, start, submit, of, finish } =
      setup('version-meet');
    const first = task('Lanterns');
    start(first, 'lanterns');
    await until(() => of('runtime.turn.started').length === 1, 'the first agent at work');
    finish();
    await until(
      () => of('workstream.copy_changes_read', first).length === 1,
      'the first copy read',
    );
    submit('workstream.add_to_project', { workstream_id: first });
    await until(() => of('workstream.added', first).length === 1, 'the first add');
    const before = controlPlane.projection
      .checkpoints(projectId)
      .find((checkpoint) => checkpoint.reason === 'before_add') as CheckpointView;
    const made = submit('workstream.create', {
      project_id: projectId,
      title: 'Paths',
      objective: null,
      from_checkpoint: before.checkpoint_id,
    });
    const second = (
      made.command?.result?.kind === 'workstream_created' ? made.command.result.workstream_id : ''
    ) as WorkstreamId;
    start(second, 'paths');
    await until(() => of('runtime.turn.started').length === 2, 'the second agent at work');
    finish();
    await until(
      () => of('workstream.copy_changes_read', second).length === 1,
      'the second copy read',
    );
    submit('workstream.add_to_project', { workstream_id: second });
    await until(() => of('workstream.add_conflicted', second).length === 1, 'the meeting');
    assert.deepEqual(of('workstream.add_conflicted', second)[0]?.payload.conflicted.files, [
      { path: 'app.txt', kind: 'text' },
    ]);
    assert.equal(readFileSync(join(folder, 'app.txt'), 'utf8'), 'lanterns\n', 'nothing written');
  });

  test('a task from a version never falls back to the folder as it is: an agent not kept to one folder is refused at its start', async () => {
    const { folder, controlPlane, projectId, task, start, submit, of, finish } =
      setup('version-loose');
    const first = task('Lanterns');
    start(first, 'lanterns');
    await until(() => of('runtime.turn.started').length === 1, 'the first agent at work');
    finish();
    await until(
      () => controlPlane.projection.checkpoints(projectId).some((c) => c.reason === 'after_turn'),
      'a version',
    );
    const version = controlPlane.projection
      .checkpoints(projectId)
      .find((checkpoint) => checkpoint.reason === 'after_turn') as CheckpointView;
    // That turn's work is added first: a version of a copy whose work was never added is refused.
    await until(() => of('workstream.copy_changes_read', first).length === 1, 'the copy read');
    submit('workstream.add_to_project', { workstream_id: first });
    await until(() => of('workstream.added', first).length === 1, 'the add');
    const made = submit('workstream.create', {
      project_id: projectId,
      title: 'Loose',
      objective: null,
      from_checkpoint: version.checkpoint_id,
    });
    const loose = (
      made.command?.result?.kind === 'workstream_created' ? made.command.result.workstream_id : ''
    ) as WorkstreamId;
    const started = start(loose, 'loose work', 'loose');
    const id = started.command?.command_id ?? '';
    await until(() => controlPlane.projection.command(id)?.status === 'failed', 'the refusal');
    assert.equal(controlPlane.projection.command(id)?.failure?.code, 'capability_unsupported');
    assert.equal(of('workstream.copy_made', loose).length, 0);
    assert.equal(of('workstream.copy_refused', loose).length, 0, 'no fallback recorded');
    assert.equal(
      readFileSync(join(folder, 'app.txt'), 'utf8'),
      'lanterns\n',
      'the folder as the add left it, untouched by the refused start',
    );
  });

  test('an add from a paired device carries the review of the reading it was shown; a missing or stale one changes nothing (ADR 0051)', async () => {
    const { folder, controlPlane, task, start, submit, of, finish } = setup('review');
    const first = task('Lanterns');
    start(first, 'lanterns');
    await until(() => of('runtime.turn.started').length === 1, 'the agent at work');
    finish();
    await until(() => of('workstream.copy_changes_read', first).length === 1, 'the copy read');
    const recorded = of('workstream.copy_changes_read', first)[0]?.payload.review;
    const copy = controlPlane.projection.workstream(first)?.copy;
    const review = copy?.state === 'own' ? copy.to_add_review : null;
    assert.match(review ?? '', /^[0-9a-f]{64}$/);
    assert.equal(review, recorded, 'the view shows the reading the journal holds');
    // A paired device, as pairing records it.
    const deviceId = '01a19000-0000-7000-8000-00000000dd01' as DeviceId;
    controlPlane.recorder.record(
      controlPlaneDraft(
        'device.paired',
        { project_id: null, workstream_id: null, execution_id: null },
        {
          device_id: deviceId,
          label: 'Headset',
          credential_sha256: 'a'.repeat(64),
          certificate_sha256: 'b'.repeat(64),
        },
        controlPlane.clock.now().toISOString() as Timestamp,
        NO_CAUSE,
      ),
    );
    const device: Principal = { kind: 'device', device_id: deviceId };
    const missing = submit('workstream.add_to_project', { workstream_id: first }, device);
    assert.equal(missing.command?.rejection?.code, 'review_required');
    const stale = submit(
      'workstream.add_to_project',
      { workstream_id: first, review: 'f'.repeat(64) },
      device,
    );
    assert.equal(stale.command?.rejection?.code, 'review_stale');
    // A local sender's stale review is refused too; without one it adds as before (ADR 0051).
    assert.equal(
      submit('workstream.add_to_project', { workstream_id: first, review: 'f'.repeat(64) }).command
        ?.rejection?.code,
      'review_stale',
    );
    assert.equal(readFileSync(join(folder, 'app.txt'), 'utf8'), 'person\n', 'nothing written');
    assert.equal(of('workstream.added', first).length, 0);
    const added = submit(
      'workstream.add_to_project',
      { workstream_id: first, review: review as string },
      device,
    );
    assert.equal(added.disposition, 'accepted');
    await until(() => of('workstream.added', first).length === 1, 'the add');
    assert.equal(readFileSync(join(folder, 'app.txt'), 'utf8'), 'lanterns\n');
    const after = controlPlane.projection.workstream(first)?.copy;
    assert.equal(after?.state === 'own' && after.to_add_review, null, 'nothing left to review');
  });

  test("adding brings the task's work into the project beside the person's own, once, and undoing puts it back", async () => {
    const { folder, controlPlane, projectId, task, start, submit, of, finish } = setup('add');
    const first = task('Lanterns');
    start(first, 'lanterns');
    await until(() => of('runtime.turn.started').length === 1, 'the agent at work');
    finish();
    await until(() => of('workstream.copy_changes_read', first).length === 1, 'the copy read');
    assert.deepEqual(of('workstream.copy_changes_read', first)[0]?.payload.changed, {
      paths: ['app.txt'],
      total: 1,
    });
    // The project had app.txt (the person's), so the agent's changed it.
    assert.deepEqual(of('workstream.copy_changes_read', first)[0]?.payload.files, {
      files: [{ path: 'app.txt', change: 'changed' }],
      total: 1,
    });
    const view = () => controlPlane.projection.workstream(first)?.copy;
    const read = view();
    assert.equal(read?.state === 'own' && read.to_add?.total, 1);
    assert.deepEqual(read?.state === 'own' && read.to_add_files, {
      files: [{ path: 'app.txt', change: 'changed' }],
      total: 1,
    });
    // The person changes another file of the project meanwhile.
    writeFileSync(join(folder, 'notes.txt'), 'mine\n');
    const added = await outcome(submit('workstream.add_to_project', { workstream_id: first }));
    assert.equal(added?.status, 'completed');
    assert.equal(added?.result?.kind === 'workstream_added' && added.result.files, 1);
    assert.equal(readFileSync(join(folder, 'app.txt'), 'utf8'), 'lanterns\n');
    assert.equal(readFileSync(join(folder, 'notes.txt'), 'utf8'), 'mine\n');
    assert.equal(of('workstream.added', first).length, 1);
    const copy = view();
    assert.ok(copy?.state === 'own' && copy.last_added_at !== null);
    assert.deepEqual(copy.to_add, { paths: [], total: 0 });
    // Adding again brings nothing: the base moved to what was added.
    const again = await outcome(submit('workstream.add_to_project', { workstream_id: first }));
    assert.equal(again?.failure?.code, 'nothing_to_add');
    // Undo of the add is the project's own checkpoint from just before it.
    const before = of('project.checkpoint.recorded').find((e) => e.payload.reason === 'before_add');
    assert.ok(before !== undefined);
    const undone = await outcome(
      submit('project.restore', {
        project_id: projectId,
        checkpoint_id: before.payload.checkpoint_id as CheckpointId,
      }),
    );
    assert.equal(undone?.status, 'completed');
    assert.equal(readFileSync(join(folder, 'app.txt'), 'utf8'), 'person\n');
    assert.equal(readFileSync(join(folder, 'notes.txt'), 'utf8'), 'mine\n');

    async function outcome(sent: ReturnType<typeof submit>) {
      const id = sent.command?.command_id;
      await until(
        () => controlPlane.projection.command(id ?? '')?.status !== 'accepted',
        'the outcome',
      );
      return controlPlane.projection.command(id ?? '');
    }
  });

  test('where both changed one file nothing is added, the files are named, and a fit marks both in the copy', async () => {
    const { folder, controlPlane, task, start, submit, of, finish } = setup('conflict');
    const first = task('Lanterns');
    start(first, 'lanterns');
    await until(() => of('runtime.turn.started').length === 1, 'the agent at work');
    finish();
    await until(() => of('workstream.copy_changes_read', first).length === 1, 'the copy read');
    writeFileSync(join(folder, 'app.txt'), 'person, later\n');
    const outcome = async (sent: ReturnType<typeof submit>) => {
      const id = sent.command?.command_id ?? '';
      await until(() => controlPlane.projection.command(id)?.status !== 'accepted', 'the outcome');
      return controlPlane.projection.command(id);
    };
    const met = await outcome(submit('workstream.add_to_project', { workstream_id: first }));
    assert.equal(met?.failure?.code, 'changed_in_both');
    assert.equal(met?.failure?.effect, 'none');
    assert.equal(readFileSync(join(folder, 'app.txt'), 'utf8'), 'person, later\n', 'untouched');
    const copy = controlPlane.projection.workstream(first)?.copy;
    assert.ok(copy?.state === 'own');
    assert.deepEqual(copy.conflict, { files: [{ path: 'app.txt', kind: 'text' }], total: 1 });

    const fitted = await outcome(submit('workstream.fit_to_project', { workstream_id: first }));
    assert.equal(fitted?.status, 'completed');
    const marked = readFileSync(join(copy.directory, 'app.txt'), 'utf8');
    assert.match(
      marked,
      /^<<<<<<< [\s\S]*person, later[\s\S]*=======[\s\S]*lanterns[\s\S]*>>>>>>> /,
    );
    assert.equal(
      readFileSync(join(folder, 'app.txt'), 'utf8'),
      'person, later\n',
      'still untouched',
    );
    const after = controlPlane.projection.workstream(first)?.copy;
    assert.equal(after?.state === 'own' && after.conflict, null);
    // The agent fits its work (here, by hand, so no turn reads it): the first add shows the copy
    // as it is now and adds nothing; after it, the add goes through over the new base.
    writeFileSync(join(copy.directory, 'app.txt'), 'person, later, with lanterns\n');
    const unseen = await outcome(submit('workstream.add_to_project', { workstream_id: first }));
    assert.equal(unseen?.failure?.code, 'changed_since_review');
    assert.equal(readFileSync(join(folder, 'app.txt'), 'utf8'), 'person, later\n');
    const added = await outcome(submit('workstream.add_to_project', { workstream_id: first }));
    assert.equal(added?.status, 'completed');
    assert.equal(readFileSync(join(folder, 'app.txt'), 'utf8'), 'person, later, with lanterns\n');
  });

  test('an ignore rule the agent writes cannot make a file of the project vanish on adding', async () => {
    const { folder, controlPlane, task, start, submit, of, finish } = setup('ignored');
    const first = task('Lanterns');
    start(first, 'lanterns');
    await until(() => of('runtime.turn.started').length === 1, 'the agent at work');
    finish();
    await until(() => of('workstream.copy_changes_read', first).length === 1, 'the copy read');
    const copy = controlPlane.projection.workstream(first)?.copy;
    assert.ok(copy?.state === 'own');
    writeFileSync(join(copy.directory, '.gitignore'), 'app.txt\n');
    const press = async () => {
      const sent = submit('workstream.add_to_project', { workstream_id: first });
      const id = sent.command?.command_id ?? '';
      await until(() => controlPlane.projection.command(id)?.status !== 'accepted', 'the outcome');
      return controlPlane.projection.command(id);
    };
    // Written without a turn: shown first, then added.
    assert.equal((await press())?.failure?.code, 'changed_since_review');
    assert.deepEqual(of('workstream.copy_changes_read', first).at(-1)?.payload.changed, {
      paths: ['.gitignore', 'app.txt'],
      total: 2,
    });
    assert.equal((await press())?.status, 'completed');
    assert.equal(readFileSync(join(folder, 'app.txt'), 'utf8'), 'lanterns\n', 'kept and added');
    assert.equal(readFileSync(join(folder, '.gitignore'), 'utf8'), 'app.txt\n');
  });

  test("a runtime not held to one folder gets no copy: it works in the project's folder, one task at a time, and never in another task's copy", async () => {
    const { folder, controlPlane, task, start, of, finish, finishLoose } = setup('loose');
    const first = task('Lanterns');
    assert.equal(start(first, 'lanterns', 'loose').disposition, 'accepted');
    await until(() => of('execution.created', first).length === 1, 'the start');
    const refused = of('workstream.copy_refused', first)[0];
    assert.equal(refused?.payload.code, 'runtime_unconfined');
    assert.equal(of('execution.created', first)[0]?.payload.directory, realpathSync(folder));
    // Another task's start, held or not, waits for the project's folder.
    const second = task('Paths');
    assert.equal(start(second, 'paths', 'loose').disposition, 'rejected');

    // Once the folder is free, a held runtime's task gets its copy; started with a runtime not held
    // to it, it is refused in words.
    await until(() => of('runtime.turn.started', first).length === 1, 'the loose agent at work');
    finishLoose();
    await until(() => of('runtime.turn.completed', first).length === 1, 'the loose turn to end');
    const third = task('Ponds');
    assert.equal(start(third, 'ponds').disposition, 'accepted');
    await until(() => of('workstream.copy_made', third).length === 1, 'the copy');
    await until(() => of('runtime.turn.started', third).length === 1, 'the held agent at work');
    const copy = controlPlane.projection.workstream(third)?.copy;
    assert.ok(copy?.state === 'own');
    assert.notEqual(copy.directory, realpathSync(folder));
    finish();
    await until(() => of('runtime.turn.completed', third).length === 1, 'the held turn to end');
    const loose = start(third, 'again', 'loose');
    assert.equal(loose.disposition, 'rejected');
    assert.match(
      loose.command?.rejection?.message ?? '',
      /not kept to one folder on this computer/,
    );
  });

  test('a task that has a copy, started where copies are off, is refused in words, never sent to its copy', () => {
    const { controlPlane, projectId, task, start } = setup('off', { copiesOff: true });
    const first = task('Lanterns');
    controlPlane.recorder.record(
      controlPlaneDraft(
        'workstream.copy_made',
        { project_id: projectId, workstream_id: first, execution_id: null },
        { directory: '/elsewhere/copy', base_tree: 'a'.repeat(64), files: 1 },
        '2026-11-10T10:00:00.000Z' as never,
        NO_CAUSE,
      ),
    );
    const refused = start(first, 'lanterns');
    assert.equal(refused.disposition, 'rejected');
    assert.match(
      refused.command?.rejection?.message ?? '',
      /This task's copy is off on this computer/,
    );
  });

  test("Save a Copy puts a zip of the project's files in Downloads, and Show in Finder shows the folder (F3)", async () => {
    const { controlPlane, projectId, submit, downloads } = setup('keep');
    const outcome = async (sent: ReturnType<typeof submit>) => {
      const id = sent.command?.command_id ?? '';
      await until(() => controlPlane.projection.command(id)?.status !== 'accepted', 'the outcome');
      return controlPlane.projection.command(id);
    };
    const saved = await outcome(submit('project.save_copy', { project_id: projectId }));
    assert.equal(saved?.status, 'completed');
    assert.ok(saved?.result?.kind === 'project_saved');
    assert.equal(saved.result.files, 1, 'app.txt');
    assert.ok(existsSync(join(downloads, saved.result.file)));
    const shown = await outcome(submit('project.show_in_finder', { project_id: projectId }));
    assert.equal(shown?.status, 'completed');
  });
});

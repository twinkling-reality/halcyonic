import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type {
  CommandEnvelope,
  CommandId,
  RuntimeDescriptor,
  RuntimeId,
  WorkstreamId,
} from '@halcyonic/contracts';
import { admitCommand, type RuntimeCatalog } from './admission.ts';
import { Projection } from './projection.ts';
import { EventBuilder } from './testing/events.ts';

// A task's own copy of its project's folder, and the one step that adds its work (ADR 0045).

const TREE = (digit: string) => digit.repeat(64);
const COPIES = '/copies';

const inFolder: RuntimeCatalog = {
  get: (id): RuntimeDescriptor | undefined =>
    id === 'mock'
      ? {
          runtime_id: 'mock' as RuntimeId,
          kind: 'mock',
          display_name: 'Mock',
          synthetic: true,
          capabilities: {
            start_execution: true,
            instruct_at_rest: true,
            instruct_while_running: false,
            respond_to_approval: true,
            answer_question: false,
            interrupt: true,
          },
          model_choice: 'none',
          uses_project_location: true,
          reports_tool_activity: false,
        }
      : undefined,
};

function setup() {
  const b = new EventBuilder();
  const projection = new Projection();
  const project = b.project('App', { path: '/work/app', name: 'app', created: false });
  projection.apply(project.event);
  const task = (title: string) => {
    const made = b.workstream(project.projectId, title);
    projection.apply(made.event);
    return made.workstreamId;
  };
  const scopeOf = (workstreamId: WorkstreamId) => ({
    project_id: project.projectId,
    workstream_id: workstreamId,
    execution_id: null,
  });
  const copyMade = (workstreamId: WorkstreamId, base = TREE('a')) =>
    projection.apply(
      b.controlPlane('workstream.copy_made', scopeOf(workstreamId), {
        directory: `${COPIES}/${workstreamId}/app`,
        base_tree: base,
        files: 3,
      }),
    );
  /** An execution of the task in `directory`, running a turn until `rest` is called. */
  const running = (workstreamId: WorkstreamId, directory: string) => {
    const execution = b.execution(project.projectId, workstreamId, directory);
    projection.apply(execution.event);
    const scope = {
      projectId: project.projectId,
      workstreamId,
      executionId: execution.executionId,
    };
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    return {
      rest: () =>
        projection.apply(b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' })),
    };
  };
  let counter = 0;
  const envelope = () => {
    counter += 1;
    return {
      schema_version: 1 as const,
      command_id: `00000000-0000-4000-8000-${String(counter).padStart(12, '0')}` as CommandId,
      issued_at: '2026-11-10T10:00:00.000Z',
      client: { name: 'test', version: null, device_label: null },
    };
  };
  const add = (workstreamId: WorkstreamId): CommandEnvelope => ({
    ...envelope(),
    command_type: 'workstream.add_to_project',
    payload: { workstream_id: workstreamId },
  });
  const fit = (workstreamId: WorkstreamId): CommandEnvelope => ({
    ...envelope(),
    command_type: 'workstream.fit_to_project',
    payload: { workstream_id: workstreamId },
  });
  const start = (workstreamId: WorkstreamId): CommandEnvelope => ({
    ...envelope(),
    command_type: 'execution.start',
    payload: {
      workstream_id: workstreamId,
      runtime_id: 'mock' as RuntimeId,
      instruction: 'Go.',
      options: {},
      model_ref: null,
    },
  });
  const accept = (command: CommandEnvelope, workstreamId: WorkstreamId) =>
    projection.apply(
      b.controlPlane('command.accepted', scopeOf(workstreamId), {
        command,
        policy: 'low_consequence',
        received_via: 'internal',
        principal: null,
      }),
    );
  const admit = (command: CommandEnvelope) => admitCommand(command, projection, inFolder);
  const refusal = (command: CommandEnvelope) => {
    const admission = admit(command);
    return admission.admitted ? null : admission.rejection.message;
  };
  return {
    b,
    projection,
    project,
    task,
    scopeOf,
    copyMade,
    running,
    add,
    fit,
    start,
    accept,
    admit,
    refusal,
  };
}

describe("a task's own copy in the projection", () => {
  test('a task has no copy until one is made: its view carries none, as every journal from before', () => {
    const { projection, task } = setup();
    const id = task('First');
    assert.equal('copy' in (projection.workstream(id) ?? {}), false);
  });

  test('made, read, met in an add, fitted and added: the view follows each, and the base moves', () => {
    const { b, projection, task, scopeOf, copyMade } = setup();
    const id = task('First');
    copyMade(id);
    const view = () => projection.workstream(id)?.copy;
    const made = view();
    assert.ok(made?.state === 'own');
    assert.deepEqual(view(), {
      state: 'own',
      directory: `${COPIES}/${id}/app`,
      made_at: made.made_at,
      base_tree: TREE('a'),
      to_add: null,
      to_add_files: null,
      to_add_read_at: null,
      last_added_at: null,
      conflict: null,
    });
    const scope = scopeOf(id);
    // A reading recorded before readings said how each file changed: the kinds stay unknown.
    projection.apply(
      b.controlPlane('workstream.copy_changes_read', scope, {
        tree: TREE('b'),
        changed: { paths: ['src/a.ts'], total: 1 },
      }),
    );
    const older = view();
    assert.equal(older?.state === 'own' && older.to_add?.total, 1);
    assert.equal(older?.state === 'own' && older.to_add_files, null);
    projection.apply(
      b.controlPlane('workstream.copy_changes_read', scope, {
        tree: TREE('b'),
        changed: { paths: ['src/a.ts', 'src/b.ts', 'old.ts'], total: 3 },
        files: {
          files: [
            { path: 'src/a.ts', change: 'changed' },
            { path: 'src/b.ts', change: 'added' },
            { path: 'old.ts', change: 'removed' },
          ],
          total: 3,
        },
      }),
    );
    const read = view();
    assert.equal(read?.state === 'own' && read.to_add?.total, 3);
    assert.deepEqual(read?.state === 'own' && read.to_add_files, {
      files: [
        { path: 'src/a.ts', change: 'changed' },
        { path: 'src/b.ts', change: 'added' },
        { path: 'old.ts', change: 'removed' },
      ],
      total: 3,
    });
    projection.apply(
      b.controlPlane('workstream.add_conflicted', scope, {
        task_tree: TREE('b'),
        project_tree: TREE('c'),
        conflicted: { files: [{ path: 'src/a.ts', kind: 'text' }], total: 1 },
      }),
    );
    const met = view();
    assert.deepEqual(met?.state === 'own' && met.conflict, {
      files: [{ path: 'src/a.ts', kind: 'text' }],
      total: 1,
    });
    assert.equal(met?.state === 'own' && met.base_tree, TREE('a'), 'a conflict moves no base');
    projection.apply(
      b.controlPlane('workstream.fitted', scope, {
        project_tree: TREE('c'),
        fitted_tree: TREE('d'),
        written: { paths: ['src/a.ts'], total: 1 },
      }),
    );
    const fitted = view();
    assert.equal(fitted?.state === 'own' && fitted.base_tree, TREE('c'));
    assert.equal(fitted?.state === 'own' && fitted.conflict, null);
    assert.equal(fitted?.state === 'own' && fitted.to_add, null, 'unknown until read again');
    assert.equal(fitted?.state === 'own' && fitted.to_add_files, null);
    projection.apply(
      b.controlPlane('workstream.added', scope, {
        checkpoint_id: '01920000-0000-7000-8000-0000000000c1' as never,
        task_tree: TREE('e'),
        project_tree: TREE('c'),
        merged_tree: TREE('f'),
        added: { paths: ['src/a.ts', 'src/b.ts'], total: 2 },
      }),
    );
    const added = view();
    assert.equal(added?.state === 'own' && added.base_tree, TREE('e'));
    assert.deepEqual(added?.state === 'own' && added.to_add, { paths: [], total: 0 });
    assert.deepEqual(added?.state === 'own' && added.to_add_files, { files: [], total: 0 });
    assert.ok(
      added?.state === 'own' && added.last_added_at !== null && added.last_added_at > made.made_at,
    );
  });

  test('a refusal shows why; a copy made later replaces it; a second copy or a stray event changes nothing', () => {
    const { b, projection, task, scopeOf, copyMade } = setup();
    const id = task('First');
    const refused = projection.apply(
      b.controlPlane('workstream.copy_refused', scopeOf(id), {
        code: 'linked_repository',
        message: "The folder's .git is a file.",
      }),
    );
    assert.deepEqual(refused.notes, []);
    assert.equal(projection.workstream(id)?.copy?.state, 'refused');
    copyMade(id);
    assert.equal(projection.workstream(id)?.copy?.state, 'own');
    const again = projection.apply(
      b.controlPlane('workstream.copy_made', scopeOf(id), {
        directory: '/elsewhere',
        base_tree: TREE('9'),
        files: 1,
      }),
    );
    assert.equal(again.notes[0]?.code, 'duplicate_entity');
    const copy = projection.workstream(id)?.copy;
    assert.equal(copy?.state === 'own' && copy.directory, `${COPIES}/${id}/app`);

    const other = task('Second');
    const stray = projection.apply(
      b.controlPlane('workstream.copy_changes_read', scopeOf(other), {
        tree: TREE('b'),
        changed: { paths: [], total: 0 },
      }),
    );
    assert.equal(stray.notes[0]?.code, 'unknown_entity');
    assert.equal('copy' in (projection.workstream(other) ?? {}), false);
  });
});

describe("admitting work on a task's own copy", () => {
  test('two tasks with copies of their own start and run at once; one in the project folder still holds it', () => {
    const { task, copyMade, running, start, refusal, admit } = setup();
    const first = task('First');
    const second = task('Second');
    copyMade(first);
    running(first, `${COPIES}/${first}/app`);
    assert.equal(admit(start(second)).admitted, true, 'the project folder is free');
    copyMade(second);
    running(second, `${COPIES}/${second}/app`);
    const third = task('Third');
    assert.equal(admit(start(third)).admitted, true);
    // A task working in the project's own folder, as one whose copy was refused does.
    running(third, '/work/app');
    assert.equal(
      refusal(start(task('Fourth'))),
      'Another task is running in this folder. Wait for it to finish or stop it, then try again.',
    );
  });

  test("adding is the person's review: only a resting task with a copy of its own, beside no other writer of the folder", () => {
    const { b, projection, task, scopeOf, copyMade, running, add, accept, admit, refusal } =
      setup();
    const first = task('First');
    assert.equal(
      refusal(add(first)),
      'This task has no copy of its own yet: it gets one when it starts.',
    );
    projection.apply(
      b.controlPlane('workstream.copy_refused', scopeOf(first), {
        code: 'too_large',
        message: 'Too many files.',
      }),
    );
    assert.equal(
      refusal(add(first)),
      "This task works in the project's own folder, so its changes are already there.",
    );

    const second = task('Second');
    copyMade(second);
    const work = running(second, `${COPIES}/${second}/app`);
    assert.equal(
      refusal(add(second)),
      'This task is running. Wait for it to finish or stop it, then try again.',
    );
    work.rest();
    const admitted = admit(add(second));
    assert.equal(admitted.admitted && admitted.policy, 'review_required');
    assert.equal(admitted.scope.workstream_id, second);

    // Another task working in its own copy does not hold the project's folder.
    const third = task('Third');
    copyMade(third);
    const elsewhere = running(third, `${COPIES}/${third}/app`);
    assert.equal(admit(add(second)).admitted, true);
    elsewhere.rest();

    // An add of another task under way holds the folder; so does a task working in it directly.
    const pending = add(third);
    accept(pending, third);
    assert.equal(
      refusal(add(second)),
      'Adding to the project in this project is still under way. Wait for its outcome, then try again.',
    );
    projection.apply(
      b.controlPlane('command.failed', scopeOf(third), {
        command_id: pending.command_id,
        command_type: 'workstream.add_to_project',
        failure: { code: 'nothing_to_add', message: 'Nothing to add.', effect: 'none' },
      }),
    );
    assert.equal(admit(add(second)).admitted, true);
    running(first, '/work/app');
    assert.equal(
      refusal(add(second)),
      'Another task is running in this folder. Wait for it to finish or stop it, then try again.',
    );
  });

  test('while its add is under way the task neither starts nor adds again', () => {
    const { task, copyMade, add, start, accept, refusal } = setup();
    const id = task('First');
    copyMade(id);
    accept(add(id), id);
    assert.equal(
      refusal(start(id)),
      'Adding to the project for this task is still under way. Wait for its outcome, or start another task.',
    );
    assert.equal(
      refusal(add(id)),
      'Adding to the project for this task is still under way. Wait for its outcome first.',
    );
  });

  test('fitting is admitted only after an add met the project, for a resting task', () => {
    const { b, projection, task, scopeOf, copyMade, running, add, fit, accept, admit, refusal } =
      setup();
    const id = task('First');
    copyMade(id);
    assert.equal(
      refusal(fit(id)),
      "Nothing to fit: the task's work and the project's did not meet at its last add.",
    );
    projection.apply(
      b.controlPlane('workstream.add_conflicted', scopeOf(id), {
        task_tree: TREE('b'),
        project_tree: TREE('c'),
        conflicted: { files: [{ path: 'img.png', kind: 'binary' }], total: 1 },
      }),
    );
    const admitted = admit(fit(id));
    assert.equal(admitted.admitted && admitted.policy, 'low_consequence');
    // A fit reads the project's folder whole, so it waits for another task's add (AUDIT S1).
    const other = task('Other');
    copyMade(other);
    const pending = add(other);
    accept(pending, other);
    assert.equal(
      refusal(fit(id)),
      'Adding to the project in this project is still under way. Wait for its outcome, then try again.',
    );
    projection.apply(
      b.controlPlane('command.failed', scopeOf(other), {
        command_id: pending.command_id,
        command_type: 'workstream.add_to_project',
        failure: { code: 'nothing_to_add', message: 'Nothing to add.', effect: 'none' },
      }),
    );
    running(id, `${COPIES}/${id}/app`);
    assert.equal(
      refusal(fit(id)),
      'This task is running. Wait for it to finish or stop it, then try again.',
    );
  });
});

describe("checking a task's own copy (ADR 0045)", () => {
  const findings = (passed: boolean) =>
    ({
      tsc: { exit: 0, seconds: 1 },
      build: { exit: 0, seconds: 10 },
      page: null,
      unknown: null,
      passed,
      reads_confined: true,
    }) as never;

  test("a check of one task's copy waits for that task alone, and only work in that copy makes it stale", () => {
    const { b, projection, project, task, scopeOf, copyMade, running, admit, refusal } = setup();
    const projectScope = { project_id: project.projectId, workstream_id: null, execution_id: null };
    projection.apply(
      b.controlPlane('project.starter_applied', projectScope, {
        starter_id: 'iwsdk-vr',
        lockfile: 'a'.repeat(64),
        files: 12,
        template: 'b'.repeat(64),
      } as never),
    );
    let n = 0;
    const check = (workstreamId?: WorkstreamId): CommandEnvelope => {
      n += 1;
      return {
        schema_version: 1,
        command_id: `00000000-0000-4000-8000-${String(900 + n).padStart(12, '0')}` as CommandId,
        issued_at: '2026-11-10T10:00:00.000Z',
        client: { name: 'test', version: null, device_label: null },
        command_type: 'project.check',
        payload: {
          project_id: project.projectId,
          ...(workstreamId === undefined ? {} : { workstream_id: workstreamId }),
        },
      };
    };
    const first = task('First');
    const second = task('Second');
    copyMade(first);
    copyMade(second);
    assert.equal(
      refusal(check(task('Third'))),
      "This task works in the project's own folder: check the project.",
    );
    // The second task works in its own copy; the first's copy can be checked meanwhile.
    const elsewhere = running(second, `${COPIES}/${second}/app`);
    const admitted = admit(check(first));
    assert.equal(admitted.admitted, true);
    assert.equal(admitted.scope.workstream_id, first);
    assert.match(
      refusal(check()) ?? '',
      /Check it once it stops/,
      "the project's own check still waits",
    );
    assert.equal(refusal(check(second)), 'This task is running. Check it once it stops.');

    // A recorded check of the first task's copy: the second task's turns do not reach it.
    projection.apply(
      b.controlPlane('project.check.recorded', projectScope, {
        check_id: '01920000-0000-7000-8000-0000000000e1' as never,
        findings: findings(true),
        workstream_id: first,
      }),
    );
    assert.equal(projection.latestCheck(project.projectId)?.workstream_id, first);
    assert.equal(projection.project(project.projectId)?.check?.workstream_id, first);
    elsewhere.rest();
    assert.equal(projection.latestCheck(project.projectId)?.files, 'current');
    // A turn in the first task's copy does.
    running(first, `${COPIES}/${first}/app`);
    assert.equal(projection.latestCheck(project.projectId)?.files, 'unknown');

    // A check of the project's own folder is changed by an add, not by a copy's turn.
    projection.apply(
      b.controlPlane('project.check.recorded', projectScope, {
        check_id: '01920000-0000-7000-8000-0000000000e2' as never,
        findings: findings(true),
      }),
    );
    assert.equal(projection.project(project.projectId)?.check?.workstream_id, undefined);
    running(second, `${COPIES}/${second}/app`).rest();
    assert.equal(projection.latestCheck(project.projectId)?.files, 'current');
    projection.apply(
      b.controlPlane('workstream.added', scopeOf(second), {
        checkpoint_id: '01920000-0000-7000-8000-0000000000c2' as never,
        task_tree: TREE('b'),
        project_tree: TREE('c'),
        merged_tree: TREE('d'),
        added: { paths: ['src/a.ts'], total: 1 },
      }),
    );
    assert.equal(projection.latestCheck(project.projectId)?.files, 'changed');
  });
});

describe("keeping a project's work (F3)", () => {
  test("showing and saving need the project's folder; a save waits for a turn at work, a restore or an add of it", () => {
    const {
      b,
      projection,
      project,
      task,
      scopeOf,
      copyMade,
      add,
      accept,
      admit,
      refusal,
      running,
    } = setup();
    let n = 0;
    const keep = (
      command_type: 'project.show_in_finder' | 'project.save_copy',
      projectId = project.projectId,
    ) => {
      n += 1;
      return {
        schema_version: 1 as const,
        command_id: `00000000-0000-4000-8000-${String(700 + n).padStart(12, '0')}` as CommandId,
        issued_at: '2026-11-11T10:00:00.000Z',
        client: { name: 'test', version: null, device_label: null },
        command_type,
        payload: { project_id: projectId },
      } as CommandEnvelope;
    };
    const shown = admit(keep('project.show_in_finder'));
    assert.equal(shown.admitted && shown.policy, 'low_consequence');
    const saved = admit(keep('project.save_copy'));
    assert.equal(saved.admitted && saved.policy, 'review_required', 'a deliberate press');
    // A turn at work may leave a file half written in the copy (AUDIT2 S1), as a restore waits.
    const busy = task('Busy');
    const turn = running(busy, '/work/app');
    assert.equal(
      refusal(keep('project.save_copy')),
      'A task in this project is running. Save a copy once it has finished.',
    );
    assert.equal(admit(keep('project.show_in_finder')).admitted, true);
    turn.rest();
    assert.equal(admit(keep('project.save_copy')).admitted, true);
    const elsewhere = b.project('Elsewhere');
    projection.apply(elsewhere.event);
    assert.equal(
      refusal(keep('project.save_copy', elsewhere.projectId)),
      'The project has no folder on this computer.',
    );
    const other = task('Other');
    copyMade(other);
    accept(add(other), other);
    assert.equal(
      refusal(keep('project.save_copy')),
      'Adding to the project in this project is still under way. Save a copy once it has finished.',
    );
    assert.equal(admit(keep('project.show_in_finder')).admitted, true, 'showing changes nothing');
    void scopeOf;
  });
});

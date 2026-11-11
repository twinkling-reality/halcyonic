import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { deserialize, serialize } from 'node:v8';
import type {
  CheckpointId,
  CommandEnvelope,
  ExecutionId,
  WorkstreamId,
} from '@halcyonic/contracts';
import { Projection, type ProjectionState } from './projection.ts';
import { EventBuilder } from './testing/events.ts';

// A project's history (the parity map's gap 2): its versions, the ones not kept, and its restores.

const TREE = (digit: string) => digit.repeat(64);

function setup() {
  const b = new EventBuilder();
  const projection = new Projection();
  const project = b.project('App', { path: '/work/app', name: 'app', created: false });
  projection.apply(project.event);
  const projectScope = { project_id: project.projectId, workstream_id: null, execution_id: null };
  const task = (title: string) => {
    const made = b.workstream(project.projectId, title);
    projection.apply(made.event);
    return made.workstreamId;
  };
  const execution = (workstreamId: WorkstreamId, directory: string) => {
    const made = b.execution(project.projectId, workstreamId, directory);
    projection.apply(made.event);
    return made.executionId;
  };
  const version = (
    reason: 'before_task' | 'after_turn' | 'before_restore' | 'before_add',
    executionId: ExecutionId | null,
    tree: string,
    turnId?: string,
  ): CheckpointId => {
    const checkpointId = b.id() as CheckpointId;
    projection.apply(
      b.controlPlane('project.checkpoint.recorded', projectScope, {
        checkpoint_id: checkpointId,
        reason,
        execution_id: executionId,
        tree,
        files: 3,
        project_head: null,
        ...(turnId === undefined ? {} : { turn_id: turnId }),
      }),
    );
    return checkpointId;
  };
  return { b, projection, project, projectScope, task, execution, version };
}

describe("a project's history", () => {
  test('versions name their folder and the one before it of the same folder, and an after_turn version its turn', () => {
    const { b, projection, project, task, execution, version } = setup();
    const inFolder = task('In the folder');
    const own = task('In its own copy');
    projection.apply(
      b.controlPlane(
        'workstream.copy_made',
        { project_id: project.projectId, workstream_id: own, execution_id: null },
        { directory: `/copies/${own}/app`, base_tree: TREE('a'), files: 3 },
      ),
    );
    const here = execution(inFolder, '/work/app');
    const there = execution(own, `/copies/${own}/app`);
    const first = version('before_task', here, TREE('a'));
    const copyFirst = version('before_task', there, TREE('a'));
    const afterTurn = version('after_turn', here, TREE('b'), 'turn-7');
    const copyAfter = version('after_turn', there, TREE('c'));
    const history = projection.history(project.projectId);
    assert.deepEqual(
      history.map((entry) =>
        entry.kind === 'version'
          ? [entry.checkpoint.checkpoint_id, entry.folder.kind, entry.previous, entry.changed_files]
          : entry.kind,
      ),
      [
        [first, 'project', null, null],
        [copyFirst, 'task_copy', null, null],
        [afterTurn, 'project', first, null],
        [copyAfter, 'task_copy', copyFirst, null],
      ],
    );
    const copied = history[1];
    assert.ok(copied?.kind === 'version' && copied.folder.kind === 'task_copy');
    assert.equal(copied.folder.workstream_id, own);
    const turned = history[2];
    assert.ok(turned?.kind === 'version');
    assert.equal(turned.checkpoint.turn_id, 'turn-7');
    assert.equal('turn_id' in (history[0]?.kind === 'version' ? history[0].checkpoint : {}), false);
    assert.equal(projection.checkpoints(project.projectId)[2]?.turn_id, 'turn-7');
    assert.deepEqual(projection.history(b.id()), [], "another project's history is empty");
  });

  test('a version not kept, a restore and a restore that stopped are kept in order with their totals', () => {
    const { b, projection, project, projectScope, version } = setup();
    const kept = version('before_task', null, TREE('a'));
    projection.apply(
      b.controlPlane('project.checkpoint.failed', projectScope, {
        reason: 'after_turn',
        execution_id: null,
        failure: { code: 'too_large', message: 'The folder holds too many files.' },
      }),
    );
    const before = version('before_restore', null, TREE('b'));
    const paths = (total: number) => ({ paths: [], total });
    projection.apply(
      b.controlPlane('project.restored', projectScope, {
        checkpoint_id: kept,
        before_restore_checkpoint_id: before,
        tree: TREE('a'),
        files_compared: 3,
        changed_back: paths(2),
        removed: paths(1),
        returned: paths(0),
        project_head_moved: false,
      }),
    );
    projection.apply(
      b.controlPlane('project.restore_incomplete', projectScope, {
        checkpoint_id: kept,
        before_restore_checkpoint_id: null,
        written: true,
        not_restored: paths(4),
        failure: { code: 'incomplete', message: 'A file could not be written.' },
      }),
    );
    const history = projection.history(project.projectId);
    assert.deepEqual(
      history.map((entry) => entry.kind),
      ['version', 'not_kept', 'version', 'restored', 'restore_incomplete'],
    );
    const notKept = history[1];
    assert.ok(notKept?.kind === 'not_kept');
    assert.deepEqual([notKept.reason, notKept.failure.code], ['after_turn', 'too_large']);
    const restored = history[3];
    assert.ok(restored?.kind === 'restored');
    assert.deepEqual(
      [
        restored.checkpoint_id,
        restored.before_restore_checkpoint_id,
        restored.changed_back,
        restored.removed,
        restored.returned,
        restored.project_head_moved,
      ],
      [kept, before, 2, 1, 0, false],
    );
    const stopped = history[4];
    assert.ok(stopped?.kind === 'restore_incomplete');
    assert.deepEqual([stopped.written, stopped.not_restored], [true, 4]);
    assert.ok(
      history.every((entry, index) => index === 0 || entry.at >= (history[index - 1]?.at ?? '')),
    );
  });

  test('the history is kept across a start that restores the projection from its checkpoint', () => {
    const { projection, project, version } = setup();
    version('before_task', null, TREE('a'));
    version('after_turn', null, TREE('b'), 'turn-1');
    const state = deserialize(serialize(projection.exportState())) as ProjectionState;
    const restarted = new Projection();
    restarted.restoreState(state);
    assert.deepEqual(restarted.history(project.projectId), projection.history(project.projectId));
    assert.equal(restarted.history(project.projectId).length, 2);
    const older = { ...state, history: undefined } as unknown as ProjectionState;
    assert.throws(() => new Projection().restoreState(older), /not one this projection exported/);
  });

  test('the rule lets go only the oldest checkpoints nothing needs, and a prune is applied', () => {
    const { b, projection, project, projectScope, task, execution, version } = setup();
    const own = task('In its own copy');
    projection.apply(
      b.controlPlane(
        'workstream.copy_made',
        { project_id: project.projectId, workstream_id: own, execution_id: null },
        { directory: `/copies/${own}/app`, base_tree: TREE('a'), files: 3 },
      ),
    );
    const there = execution(own, `/copies/${own}/app`);
    const working = execution(task('Still at work'), '/work/app');
    const old = [0, 1, 2].map(() => version('after_turn', null, TREE('a')));
    const added = version('before_add', null, TREE('b'));
    const copied = version('before_task', there, TREE('c'));
    const atWork = version('before_task', working, TREE('d'));
    const beforeRestore = version('before_restore', null, TREE('e'));
    const paths = { paths: [], total: 0 };
    projection.apply(
      b.controlPlane('project.restored', projectScope, {
        checkpoint_id: old[0] as CheckpointId,
        before_restore_checkpoint_id: beforeRestore,
        tree: TREE('a'),
        files_compared: 3,
        changed_back: paths,
        removed: paths,
        returned: paths,
        project_head_moved: null,
      }),
    );
    const newest = [0, 1].map(() => version('after_turn', null, TREE('f')));
    assert.deepEqual(
      projection.prunableCheckpoints(project.projectId, 2, 20),
      [],
      'within keep and slack',
    );
    assert.deepEqual(
      projection.prunableCheckpoints(project.projectId, 2, 0),
      old,
      "only the oldest, never an add's, a copy's, a working task's or the restore's undo",
    );
    assert.ok(
      ![added, copied, atWork, beforeRestore, ...newest].some((id) =>
        projection.prunableCheckpoints(project.projectId, 2, 0).includes(id),
      ),
    );
    projection.apply(
      b.controlPlane('project.checkpoints.pruned', projectScope, {
        checkpoint_ids: [old[1] as CheckpointId, old[2] as CheckpointId],
        kept: 8,
      }),
    );
    assert.deepEqual(projection.prunableCheckpoints(project.projectId, 2, 0), [old[0]]);
    projection.apply(
      b.command(
        {
          schema_version: 1,
          command_id: '00000000-0000-4000-8000-0000000000a1',
          issued_at: '2026-11-11T09:00:00.000Z',
          client: { name: 'test', version: null, device_label: null },
          command_type: 'project.restore',
          payload: { project_id: project.projectId, checkpoint_id: old[0] as CheckpointId },
        } as CommandEnvelope,
        'accepted',
      ),
    );
    assert.deepEqual(
      projection.prunableCheckpoints(project.projectId, 2, 0),
      [],
      'a checkpoint a restore still waiting to run names is kept',
    );
    assert.equal(projection.checkpoint(project.projectId, old[1] as string), undefined);
    const marked = projection
      .history(project.projectId)
      .filter((entry) => entry.kind === 'version' && entry.pruned_at !== undefined)
      .map((entry) => (entry.kind === 'version' ? entry.checkpoint.checkpoint_id : ''));
    assert.deepEqual(marked, [old[1], old[2]], 'the history still shows them, marked pruned');
  });
});

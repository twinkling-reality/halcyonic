import assert from 'node:assert/strict';
import { test } from 'node:test';
import { Projection } from './projection.ts';
import { EventBuilder } from './testing/events.ts';

test('a run holds the files its turn changed as the host last saw them, until its next turn starts (Watch It Work)', () => {
  const b = new EventBuilder();
  const projection = new Projection();
  const project = b.project('Dunes', { path: '/work/dunes', name: 'dunes', created: true });
  const workstream = b.workstream(project.projectId);
  const execution = b.execution(project.projectId, workstream.workstreamId, '/work/dunes');
  for (const stored of [project.event, workstream.event, execution.event]) projection.apply(stored);
  const scope = {
    projectId: project.projectId,
    workstreamId: workstream.workstreamId,
    executionId: execution.executionId,
  };
  const where = {
    project_id: project.projectId,
    workstream_id: workstream.workstreamId,
    execution_id: execution.executionId,
  };
  assert.equal(projection.execution(execution.executionId)?.writing, undefined);
  projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
  const seen = projection.apply(
    b.controlPlane('execution.files.writing', where, {
      paths: ['src/scene.ts', 'index.html'],
      paths_total: 2,
      total_complete: true,
    }),
  );
  assert.deepEqual(
    seen.changes.executions.map((view) => view.execution_id),
    [execution.executionId],
  );
  const writing = projection.execution(execution.executionId)?.writing;
  assert.deepEqual(writing?.paths, ['src/scene.ts', 'index.html']);
  assert.equal(writing?.paths_total, 2);
  assert.equal(typeof writing?.at, 'string');
  // The turn's end keeps it, so the settle has it; the next turn starts with none.
  projection.apply(b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }));
  assert.deepEqual(projection.execution(execution.executionId)?.writing?.paths, [
    'src/scene.ts',
    'index.html',
  ]);
  projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't2' }));
  assert.equal(projection.execution(execution.executionId)?.writing, undefined);
});

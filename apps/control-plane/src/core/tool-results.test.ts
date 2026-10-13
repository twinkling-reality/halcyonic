import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { ToolResult } from '@halcyonic/contracts';
import { Projection } from '@halcyonic/domain';
import { EventBuilder } from '../../../../packages/domain/src/testing/events.ts';
import { createTestControlPlane } from '../testing/harness.ts';
import { ToolResults } from './tool-results.ts';

test('only accepted fully correlated evidence is readable, including after recent eviction and replay', async (t) => {
  const h = createTestControlPlane();
  t.after(() => h.controlPlane.close());
  const b = new EventBuilder(),
    project = b.project(),
    work = b.workstream(project.projectId),
    run = b.execution(project.projectId, work.workstreamId);
  const put = (event: ReturnType<typeof b.project>['event']) =>
    h.controlPlane.recorder.import(event.event);
  for (const event of [project.event, work.event, run.event]) put(event);
  const scope = {
    projectId: project.projectId,
    workstreamId: work.workstreamId,
    executionId: run.executionId,
  };
  put(b.runtimeEvent(scope, 'runtime.execution.started', { native_id: 'native' }));
  put(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 'turn' }));
  const query = {
    journal_id: h.journal.info.journal_id,
    project_id: scope.projectId,
    workstream_id: scope.workstreamId,
  };
  const reader = new ToolResults(h.controlPlane);
  const result = (id: string): ToolResult => ({
    kind: 'command',
    tool_call_id: id,
    turn_id: 'turn',
    completion: 'completed',
    exit_code: 0,
    redaction: { policy: 'known_held_values', replacements: 0 },
    output: {
      text: 'native output',
      availability: 'available',
      complete: true,
      truncation: 'none',
    },
  });
  const unmatched = b.runtimeEvent(scope, 'runtime.tool.result', result('not-started'));
  put(unmatched);
  assert.equal(
    reader.read(scope.executionId, unmatched.event.event_id, query, { kind: 'local' }),
    undefined,
  );
  const foreign = { ...scope, projectId: b.id() as typeof scope.projectId };
  put(
    b.runtimeEvent(foreign, 'runtime.tool.started', {
      tool_call_id: 'foreign',
      tool_name: 'shell',
      title: null,
    }),
  );
  const untrusted = b.runtimeEvent(scope, 'runtime.tool.result', result('foreign'));
  put(untrusted);
  assert.equal(
    reader.read(scope.executionId, untrusted.event.event_id, query, { kind: 'local' }),
    undefined,
  );
  for (const field of ['workstream', 'runtime']) {
    const id = `foreign-${field}`;
    const started = b.runtimeEvent(scope, 'runtime.tool.started', {
      tool_call_id: id,
      tool_name: 'shell',
      title: null,
    });
    if (field === 'workstream') started.event.workstream_id = b.id() as never;
    else started.event.source = { kind: 'runtime', runtime_id: 'foreign' as never };
    put(started);
    const resultEvent = b.runtimeEvent(scope, 'runtime.tool.result', result(id));
    put(resultEvent);
    assert.equal(
      reader.read(scope.executionId, resultEvent.event.event_id, query, { kind: 'local' }),
      undefined,
    );
  }
  let first = '';
  for (let i = 0; i < 18; i++) {
    const id = `tool${i}`;
    put(
      b.runtimeEvent(scope, 'runtime.tool.started', {
        tool_call_id: id,
        tool_name: 'shell',
        title: 'offline command',
      }),
    );
    // Foreign or stale completion must not erase the valid exact start.
    put(
      b.runtimeEvent(foreign, 'runtime.tool.completed', {
        tool_call_id: id,
        turn_id: 'turn',
        outcome: 'succeeded',
      }),
    );
    for (const field of ['workstream', 'runtime']) {
      const completion = b.runtimeEvent(scope, 'runtime.tool.completed', {
        tool_call_id: id,
        turn_id: 'turn',
        outcome: 'succeeded',
      });
      if (field === 'workstream') completion.event.workstream_id = b.id() as never;
      else completion.event.source = { kind: 'runtime', runtime_id: 'foreign' as never };
      put(completion);
    }
    put(
      b.runtimeEvent(scope, 'runtime.tool.completed', {
        tool_call_id: id,
        turn_id: 'stale',
        outcome: 'succeeded',
      }),
    );
    const event = b.runtimeEvent(scope, 'runtime.tool.result', result(id));
    put(event);
    first ||= event.event.event_id;
    put(
      b.runtimeEvent(scope, 'runtime.tool.completed', {
        tool_call_id: id,
        turn_id: 'turn',
        outcome: 'succeeded',
      }),
    );
  }
  const list = reader.list(scope.executionId, query, { kind: 'local' });
  assert.equal(list?.total, 18);
  assert.equal(list?.earlier, 2);
  assert.equal(list?.results.length, 16);
  assert.equal(
    list?.availability,
    'unsupported',
    'live declaration is independent from durable history',
  );
  assert.ok(
    list?.results.every(
      (item, index, all) => index === 0 || item.position < (all[index - 1]?.position ?? 0),
    ),
  );
  assert.equal(
    reader.read(scope.executionId, first, query, { kind: 'local' })?.result.kind,
    'command',
  );
  assert.equal(
    reader.list(
      scope.executionId,
      { ...query, journal_id: b.id() as typeof query.journal_id },
      { kind: 'local' },
    ),
    undefined,
  );
  assert.equal(reader.list(scope.executionId, query, null), undefined);
  assert.equal(
    reader.list(scope.executionId, query, { kind: 'device', device_id: b.id() as never }),
    undefined,
  );
  assert.equal(h.controlPlane.projection.execution(scope.executionId)?.last_test_run, null);
  const replay = new Projection();
  for (const stored of h.journal.readAll()) replay.apply(stored);
  assert.equal(replay.executionResultSummaries(scope.executionId).total, 18);
  assert.deepEqual(
    replay.executionResultSummary(scope.executionId, first),
    h.controlPlane.projection.executionResultSummary(scope.executionId, first),
  );
});

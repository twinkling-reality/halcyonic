import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import type { HereScope, WorkstreamId } from '@halcyonic/contracts';
import { MOCK_MODELS, MockRuntimeAdapter, parseScenario } from '@halcyonic/integration-mock';
import { CREDENTIAL_REFUSED, createVirtualTime } from '@halcyonic/runtime-core';
import { createTestControlPlane, SCENARIOS } from '../testing/harness.ts';

/** A turn whose provider refuses the runtime's key, as a runtime reports it (the Claude Agent adapter does). */
const REFUSED = parseScenario(
  {
    format: 1,
    id: 'key_refused',
    description: 'The provider refuses the key the runtime holds.',
    steps: [
      {
        after_ms: 300,
        emit: {
          type: 'runtime.turn.failed',
          payload: { error: { code: CREDENTIAL_REFUSED, message: 'Invalid API key.' } },
        },
      },
    ],
  },
  'key_refused',
);

function plane(path: string, at: string) {
  const time = createVirtualTime(new Date(at));
  return createTestControlPlane({
    path,
    time,
    adapters: (clock) => [
      new MockRuntimeAdapter({
        scenarios: new Map([...SCENARIOS, ['key_refused', REFUSED]]),
        models: MOCK_MODELS,
        clock,
        scheduler: clock,
      }),
    ],
  });
}

test('a model whose provider refused the key is shown, never offered as ready; after the host reads its key again, it is unknown and offered, saying so', async (t) => {
  const folder = mkdtempSync(join(tmpdir(), 'halcyonic-credential-models-'));
  t.after(() => rmSync(folder, { recursive: true, force: true }));
  const path = join(folder, 'journal.sqlite');
  const first = plane(path, '2026-11-12T15:00:00.000Z');
  const { controlPlane, commands, time } = first;
  const made = controlPlane.commands.submit(commands.createProject('Keyed'), 'internal');
  const projectId =
    made.command?.result?.kind === 'project_created' ? made.command.result.project_id : '';
  const task = controlPlane.commands.submit(
    commands.createWorkstream(
      projectId as never,
      {
        title: 'Try',
        objective: null,
        instruction: 'Say hello.',
        scenario: 'key_refused',
      } as never,
    ),
    'internal',
  );
  const workstreamId = (
    task.command?.result?.kind === 'workstream_created' ? task.command.result.workstream_id : ''
  ) as WorkstreamId;
  const scopeOf = (cp: typeof controlPlane) =>
    ({
      project_id: projectId,
      workstream_id: workstreamId,
      execution_id: cp.projection.workstream(workstreamId)?.current_execution_id ?? null,
    }) as HereScope;
  const before = await controlPlane.commands.choices(scopeOf(controlPlane), 'models');
  assert.ok(before !== null && before.rows.length === MOCK_MODELS.length);
  assert.ok(before?.rows.every((row) => row.actions.includes('execution.start')));
  const start = commands.startExecution(workstreamId, {
    title: 'Try',
    objective: null,
    instruction: 'Say hello.',
    scenario: 'key_refused',
  } as never);
  controlPlane.commands.submit(
    { ...start, payload: { ...start.payload, model_ref: 'mock/fast' as never } },
    'internal',
  );
  await time.advance(1000);
  const execution = controlPlane.projection.workstream(workstreamId)?.current_execution_id ?? '';
  assert.equal(
    controlPlane.projection.execution(execution)?.status_reason?.code,
    CREDENTIAL_REFUSED,
  );
  const refused = await controlPlane.commands.choices(scopeOf(controlPlane), 'models');
  assert.ok(refused !== null && refused.rows.length > 0);
  const hosted = (rows: readonly { label: string }[]) =>
    rows.filter((row) => row.label.startsWith('Simulated hosted model'));
  const local = (rows: readonly { label: string }[]) =>
    rows.filter((row) => !row.label.startsWith('Simulated hosted model'));
  assert.equal(hosted(refused.rows).length, 1);
  for (const row of local(refused.rows) as typeof refused.rows) {
    assert.deepEqual(row.actions, ['execution.start'], 'a key never serves a model on this Mac');
    assert.equal(row.detail, 'Mock runtime (development fixture)');
  }
  for (const row of hosted(refused.rows) as typeof refused.rows) {
    assert.deepEqual(row.actions, [], 'a refused key offers none of its hosted models');
    assert.equal(
      row.detail,
      'Mock runtime (development fixture): its key was refused. Give it a new key on this Mac.',
    );
  }
  await controlPlane.close();
  // The host starts again, reading its credentials anew: the key may have been replaced, so nobody knows.
  const second = plane(path, '2026-11-12T16:00:00.000Z');
  t.after(() => second.controlPlane.close());
  const again = await second.controlPlane.commands.choices(scopeOf(second.controlPlane), 'models');
  assert.ok(again !== null && again.rows.length > 0);
  assert.ok(again.rows.every((row) => row.actions.includes('execution.start')));
  for (const row of hosted(again.rows) as typeof again.rows) {
    assert.equal(
      row.detail,
      'Mock runtime (development fixture): its key was refused before, and is not tried since.',
    );
  }
});

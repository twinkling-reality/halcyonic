import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import type { HereScope, RuntimeModel, WorkstreamId } from '@halcyonic/contracts';
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

function plane(path: string, at: string, models: readonly RuntimeModel[] = MOCK_MODELS) {
  const time = createVirtualTime(new Date(at));
  return createTestControlPlane({
    path,
    time,
    adapters: (clock) => [
      new MockRuntimeAdapter({
        scenarios: new Map([...SCENARIOS, ['key_refused', REFUSED]]),
        models,
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
    { ...start, payload: { ...start.payload, model_ref: 'mock/hosted' as never } },
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
      'Mock runtime (development fixture): Its provider refused your key. Add a new one on the Mac.',
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
      'Mock runtime (development fixture): Its provider refused your key earlier. Start will recheck it.',
    );
  }
});

test("a refusal counts for the models of the same declared provider only: another provider's and this Mac's stay offered", async () => {
  const hosted = (ref: string, provider: string): RuntimeModel => ({
    model_ref: ref,
    display_name: `Simulated ${ref} (development fixture)`,
    served: 'remote',
    tool_calling: 'declared',
    context_tokens: null,
    provider,
  });
  const models: RuntimeModel[] = [
    hosted('alpha/one', 'alpha'),
    hosted('alpha/two', 'alpha'),
    hosted('beta/one', 'beta'),
    { ...(MOCK_MODELS[0] as RuntimeModel) },
  ];
  const { controlPlane, commands, time } = plane(':memory:', '2026-11-12T15:00:00.000Z', models);
  const made = controlPlane.commands.submit(commands.createProject('Two'), 'internal');
  const projectId =
    made.command?.result?.kind === 'project_created' ? made.command.result.project_id : '';
  const workstream = { title: 'Try', objective: null, instruction: 'Hi.', scenario: 'key_refused' };
  const task = controlPlane.commands.submit(
    commands.createWorkstream(projectId as never, workstream as never),
    'internal',
  );
  const workstreamId = (
    task.command?.result?.kind === 'workstream_created' ? task.command.result.workstream_id : ''
  ) as WorkstreamId;
  const start = commands.startExecution(workstreamId, workstream as never);
  controlPlane.commands.submit(
    { ...start, payload: { ...start.payload, model_ref: 'alpha/one' as never } },
    'internal',
  );
  await time.advance(1000);
  const list = await controlPlane.commands.choices(
    {
      project_id: projectId,
      workstream_id: workstreamId,
      execution_id: controlPlane.projection.workstream(workstreamId)?.current_execution_id ?? null,
    } as HereScope,
    'models',
  );
  assert.ok(list !== null);
  assert.deepEqual(
    list.rows.map((row) => [row.label.split(' ')[1], row.actions.length]),
    [
      ['alpha/one', 0],
      ['alpha/two', 0],
      ['beta/one', 1],
      ['fast', 1],
    ],
  );
  await controlPlane.close();
});

test('the row names the declared provider whose key was refused, by its own declaration', async (t) => {
  const { mkdirSync: mkdir, writeFileSync: write } = await import('node:fs');
  const { HostProviders } = await import('../providers.ts');
  const dir = mkdtempSync(join(tmpdir(), 'halcyonic-credential-providers-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  mkdir(join(dir, 'alpha'));
  write(
    join(dir, 'alpha', 'provider.json'),
    JSON.stringify({
      format: 1,
      id: 'alpha',
      name: 'Alpha',
      upstream: 'http://127.0.0.1:1/v1',
      speaks: ['chat_completions'],
      key: {
        file: 'alpha-api-key',
        variable: 'ALPHA_API_KEY',
        field: 'Alpha API Key',
        header: { name: 'Authorization', scheme: 'Bearer' },
      },
      models: ['one'],
    }),
  );
  const models: RuntimeModel[] = [
    {
      model_ref: 'alpha/one',
      display_name: 'Simulated alpha/one (development fixture)',
      served: 'remote',
      tool_calling: 'declared',
      context_tokens: null,
      provider: 'alpha',
    },
  ];
  const time = createVirtualTime(new Date('2026-11-12T15:00:00.000Z'));
  const { controlPlane, commands } = createTestControlPlane({
    time,
    providers: new HostProviders({ dir, dataDir: dir }),
    adapters: (clock) => [
      new MockRuntimeAdapter({
        scenarios: new Map([...SCENARIOS, ['key_refused', REFUSED]]),
        models,
        clock,
        scheduler: clock,
      }),
    ],
  });
  t.after(() => controlPlane.close());
  const made = controlPlane.commands.submit(commands.createProject('Named'), 'internal');
  const projectId =
    made.command?.result?.kind === 'project_created' ? made.command.result.project_id : '';
  const workstream = { title: 'Try', objective: null, instruction: 'Hi.', scenario: 'key_refused' };
  const task = controlPlane.commands.submit(
    commands.createWorkstream(projectId as never, workstream as never),
    'internal',
  );
  const workstreamId = (
    task.command?.result?.kind === 'workstream_created' ? task.command.result.workstream_id : ''
  ) as WorkstreamId;
  const start = commands.startExecution(workstreamId, workstream as never);
  controlPlane.commands.submit(
    { ...start, payload: { ...start.payload, model_ref: 'alpha/one' as never } },
    'internal',
  );
  await time.advance(1000);
  const list = await controlPlane.commands.choices(
    {
      project_id: projectId,
      workstream_id: workstreamId,
      execution_id: controlPlane.projection.workstream(workstreamId)?.current_execution_id ?? null,
    } as HereScope,
    'models',
  );
  assert.equal(
    list?.rows[0]?.detail,
    'Mock runtime (development fixture): Alpha refused your key. Add a new one on the Mac.',
  );
});

test("a model's row shows its provider's refusal before any start, from the provider's own no-spend answer", async (t) => {
  const models: RuntimeModel[] = [
    {
      model_ref: 'alpha/one',
      display_name: 'Simulated alpha/one (development fixture)',
      served: 'remote',
      tool_calling: 'declared',
      context_tokens: null,
      provider: 'alpha',
    },
    { ...(MOCK_MODELS[0] as RuntimeModel) },
  ];
  let answer: 'taken' | 'refused' = 'refused';
  const time = createVirtualTime(new Date('2026-11-13T01:00:00.000Z'));
  const { controlPlane, commands } = createTestControlPlane({
    time,
    keyChecks: { state: async () => answer } as never,
    adapters: (clock) => [
      new MockRuntimeAdapter({ scenarios: SCENARIOS, models, clock, scheduler: clock }),
    ],
  });
  t.after(() => controlPlane.close());
  const made = controlPlane.commands.submit(commands.createProject('Asked'), 'internal');
  const projectId =
    made.command?.result?.kind === 'project_created' ? made.command.result.project_id : '';
  const scope = { project_id: projectId, workstream_id: null, execution_id: null } as HereScope;
  const refused = await controlPlane.commands.choices(scope, 'models');
  assert.deepEqual(
    refused?.rows.map((row) => row.actions.length),
    [0, 1],
    "only the provider's model waits for a new key; this Mac's is offered",
  );
  assert.match(refused?.rows[0]?.detail ?? '', /refused your key\. Add a new one on the Mac\./);
  answer = 'taken';
  const taken = await controlPlane.commands.choices(scope, 'models');
  assert.deepEqual(
    taken?.rows.map((row) => row.actions.length),
    [1, 1],
  );
});

import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import type { CommandId, CommandOf, DeviceId, ExecutionId, Principal } from '@halcyonic/contracts';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { createTestControlPlane } from '../testing/harness.ts';

test('raw request identity is principal-bound, survives held-secret rotation and refuses collapsed guesses', async () => {
  const scenario = DEMO_WORKSTREAMS[0];
  assert.ok(scenario);
  const directory = mkdtempSync(join(tmpdir(), 'halcyonic-request-dedup-'));
  const path = join(directory, 'journal.db');
  let secret = 'synthetic-secret-one';
  const harness = createTestControlPlane({
    path,
    secrets: () => [
      { what: 'test held value', value: secret },
      { what: 'test held value', value: 'synthetic-secret-two' },
    ],
  });
  const { controlPlane, commands, time } = harness;
  controlPlane.commands.attachWorkspaceRequests({ handle: () => null });
  const p = controlPlane.commands.submit(commands.createProject('Synthetic'), 'internal');
  assert.equal(p.command?.result?.kind, 'project_created');
  if (p.command?.result?.kind !== 'project_created') throw new Error('project missing');
  const w = controlPlane.commands.submit(
    commands.createWorkstream(p.command.result.project_id, scenario),
    'internal',
  );
  if (w.command?.result?.kind !== 'workstream_created') throw new Error('task missing');
  const start = controlPlane.commands.submit(
    commands.startExecution(w.command.result.workstream_id, scenario),
    'internal',
  );
  await time.runUntilIdle();
  const execution = controlPlane.projection.workstream(w.command.result.workstream_id)
    ?.current_execution_id as ExecutionId;
  const raw: CommandOf<'workspace.request.submit'> = {
    schema_version: 1,
    command_id: start.command?.command_id.replace(/.$/, 'a') as CommandId,
    issued_at: time.now().toISOString(),
    client: { name: 'synthetic', version: null, device_label: null },
    command_type: 'workspace.request.submit',
    payload: {
      project_id: p.command.result.project_id,
      workstream_id: w.command.result.workstream_id,
      execution_id: execution,
      text: 'Inspect synthetic-secret-one',
    },
  };
  const local: Principal = { kind: 'local' },
    foreign: Principal = {
      kind: 'device',
      device_id: '019aa123-0000-7000-8000-000000000001' as DeviceId,
    };
  // Use a fresh UUID to avoid accidental overlap with the previous command.
  raw.command_id = '019aa123-0000-7000-8000-000000000002' as CommandId;
  assert.equal(controlPlane.commands.submit(raw, 'http', local).disposition, 'accepted');
  assert.equal(
    controlPlane.projection.commandEnvelope(raw.command_id)?.payload &&
      JSON.stringify(controlPlane.projection.commandEnvelope(raw.command_id)).includes(secret),
    false,
  );
  assert.equal(controlPlane.commands.submit(raw, 'http', foreign).disposition, 'conflict');
  assert.equal(
    controlPlane.commands.submit(
      { ...raw, payload: { ...raw.payload, text: 'Inspect wrong-secret-guess' } },
      'http',
      foreign,
    ).disposition,
    'conflict',
  );
  assert.equal(
    controlPlane.commands.submit(
      { ...raw, payload: { ...raw.payload, text: 'Inspect synthetic-secret-two' } },
      'http',
      local,
    ).disposition,
    'conflict',
  );
  secret = 'rotated-test-held-value';
  assert.equal(controlPlane.commands.submit(raw, 'http', local).disposition, 'duplicate');
  for (let i = 0; i < 4; i++)
    assert.equal(controlPlane.commands.submit(raw, 'http', local).disposition, 'duplicate');
  assert.equal(
    controlPlane.commands.submit(raw, 'http', local).disposition,
    'conflict',
    'resends are rate limited',
  );
  await controlPlane.close();
  const restarted = createTestControlPlane({ path });
  assert.equal(
    restarted.controlPlane.commands.submit(raw, 'http', local).disposition,
    'conflict',
    'no raw digest survives restart',
  );
  await restarted.controlPlane.close();
  rmSync(directory, { recursive: true, force: true });
});

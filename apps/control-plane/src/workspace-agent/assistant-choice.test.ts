import assert from 'node:assert/strict';
import { chmodSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import type { CommandId, HereScope, WorkspaceAgentStatus } from '@halcyonic/contracts';
import { createUuidV7Generator } from '../ids.ts';
import { createTestControlPlane } from '../testing/harness.ts';
import type { Model } from './assistant-model.ts';
import type { ToolTurnResult } from './ollama.ts';
import { assistantModel } from './providers/assistant.ts';
import { type HostedProvider, KEY_REFUSED, type Protocol } from './providers/provider.ts';
import { WorkspaceRequests } from './requests.ts';

// The person's own choice of where the assistant runs, end to end in one plane (the lead's D1 and D2, 2026-11-12).
// A test provider and an in-process protocol: nothing leaves this process, and the key is a fake value.

const FAKE = 'fake-key-choice-0001';
const ALL: HereScope = { project_id: null, workstream_id: null, execution_id: null };
const provider: HostedProvider = {
  id: 'test-hosted',
  name: 'Test Hosted',
  upstream: 'https://hosted.invalid/v1',
  protocol: 'test_protocol',
  key: {
    variable: 'TEST_HOSTED_KEY',
    file: 'test-hosted-key',
    header: { name: 'authorization', scheme: 'Bearer' },
  },
  models: ['test-model-1'],
  effort: null,
};
const settle = async () => {
  for (let i = 0; i < 24; i++) await new Promise<void>((resolve) => setImmediate(resolve));
};

function plane(t: { after: (fn: () => unknown) => void }, refuse: boolean) {
  const dir = mkdtempSync(join(tmpdir(), 'assistant-choice-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  writeFileSync(join(dir, provider.key.file), `${FAKE}\n`);
  chmodSync(join(dir, provider.key.file), 0o600);
  let hostedTurns = 0;
  let localTurns = 0;
  const protocol: Protocol = {
    check: async () => (refuse ? { ok: false, ...KEY_REFUSED } : { ok: true }),
    turn: async () => {
      hostedTurns++;
      return refuse
        ? { kind: 'failed', ...KEY_REFUSED }
        : {
            kind: 'reply',
            content: 'Answered on the hosted model.',
            calls: [],
            model: 'test-model-1',
          };
    },
  };
  const local: Model = {
    status: async (): Promise<WorkspaceAgentStatus> => ({
      availability: 'available',
      model: 'local-model:tag',
      served: 'this_mac',
    }),
    turn: async (): Promise<ToolTurnResult> => {
      localTurns++;
      return {
        kind: 'answered',
        content: 'Answered on this computer.',
        toolCalls: [],
        model: 'local-model:tag',
        served: 'this_mac',
      };
    },
  };
  const h = createTestControlPlane({});
  const ids = createUuidV7Generator({ now: () => h.time.now().getTime() });
  const assistant = assistantModel({
    local,
    dataDir: dir,
    environment: {},
    providers: [provider],
    protocols: new Map([['test_protocol', protocol]]),
    chosen: () => h.controlPlane.projection.assistantModel(),
  });
  h.controlPlane.commands.attachAssistantModels(() => assistant.options());
  const unavailable = { availability: 'unavailable' as const, reason: { code: 'x', message: 'x' } };
  const coordinator = new WorkspaceRequests({
    controlPlane: h.controlPlane,
    ids,
    model: assistant,
    understanding: { understand: async () => unavailable },
    evaluation: { evaluate: async () => unavailable },
    scheduler: h.time,
  });
  t.after(async () => {
    coordinator.close();
    await h.controlPlane.close();
  });
  const envelope = (command_type: string, payload: object) =>
    ({
      schema_version: 1,
      command_id: ids.next() as CommandId,
      issued_at: h.time.now().toISOString(),
      client: { name: 'test', version: null, device_label: null },
      command_type,
      payload,
    }) as never;
  const project = h.controlPlane.commands.submit(h.commands.createProject('Garden'), 'internal');
  if (project.command?.result?.kind !== 'project_created') throw new Error('no project');
  const projectId = project.command.result.project_id;
  const ask = (text: string) => {
    const sent = h.controlPlane.commands.submit(
      envelope('workspace.request.submit', {
        project_id: projectId,
        workstream_id: null,
        execution_id: null,
        text,
      }),
      'http',
      { kind: 'local' },
    );
    if (sent.command?.result?.kind !== 'workspace_request_created') throw new Error('no request');
    return sent.command.result.request_id;
  };
  return {
    h,
    assistant,
    coordinator,
    envelope,
    ask,
    counts: () => ({ hosted: hostedTurns, local: localTurns }),
  };
}

test('a stored key offers its models and moves nothing until the person presses Use Model, remembered', async (t) => {
  const p = plane(t, false);
  // Offered everywhere, with this computer's model first and the stored key's model after it.
  const here = await p.h.controlPlane.commands.here(ALL);
  assert.ok(
    here.some(
      (entry) => entry.kind === 'command' && entry.command_type === 'assistant.model.choose',
    ),
  );
  const list = await p.h.controlPlane.commands.choices(ALL, 'assistant_models');
  assert.deepEqual(
    list?.rows.map((row) => [row.label, row.detail, row.actions]),
    [
      ["This Computer's Model", 'local-model:tag', ['assistant.model.choose']],
      ['test-model-1', 'On Test Hosted', ['assistant.model.choose']],
    ],
  );
  // Nothing chosen: the request runs on this computer, and the stored key spends nothing.
  const first = p.ask('What is in the garden?');
  await settle();
  assert.equal(p.coordinator.read(first, { kind: 'local' })?.reply?.served, 'this_mac');
  assert.deepEqual(p.counts(), { hosted: 0, local: 1 });
  // The person's one press, prepared as any entry's (a row by its index and the list's digest).
  const prepared = await p.h.controlPlane.commands.prepare({
    scope: ALL,
    command_type: 'assistant.model.choose',
    values: { model: 1 },
    read: { assistant_models: list?.digest ?? '' },
    command_id: '0192f1a0-0000-7000-8000-00000000c001' as CommandId,
    issued_at: p.h.time.now().toISOString(),
    client: { name: 'test', version: null, device_label: null },
  });
  assert.ok(prepared.ok);
  if (!prepared.ok) return;
  p.h.controlPlane.commands.submit(prepared.command, 'http', { kind: 'local' });
  await settle();
  assert.deepEqual(p.h.controlPlane.projection.assistantModel(), {
    provider_id: 'test-hosted',
    model: 'test-model-1',
  });
  const status = await p.assistant.status();
  assert.equal(status.availability === 'available' && status.provider, 'Test Hosted');
  const second = p.ask('And now?');
  await settle();
  const reply = p.coordinator.read(second, { kind: 'local' })?.reply;
  assert.equal(reply?.served, 'remote');
  assert.equal(reply?.provider, 'Test Hosted');
  assert.deepEqual(p.counts(), { hosted: 1, local: 1 });
  // Remembered: the journal holds it, so a projection rebuilt from it chooses the same.
  const exported = p.h.controlPlane.projection.exportState();
  assert.deepEqual(exported.assistantModel, { provider_id: 'test-hosted', model: 'test-model-1' });
});

test('a model the list does not offer now is refused in words, and the choice stays', async (t) => {
  const p = plane(t, false);
  const sent = p.h.controlPlane.commands.submit(
    p.envelope('assistant.model.choose', {
      model: { provider_id: 'test-hosted', model: 'not-declared' },
    }),
    'http',
    { kind: 'local' },
  );
  await settle();
  const command = p.h.controlPlane.projection.command(sent.command?.command_id as CommandId);
  assert.equal(command?.status, 'failed');
  assert.equal(p.h.controlPlane.projection.assistantModel(), null);
});

test('a refused key is said in words, never sent again, and this computer is one press away', async (t) => {
  const p = plane(t, true);
  p.h.controlPlane.commands.submit(
    p.envelope('assistant.model.choose', {
      model: { provider_id: 'test-hosted', model: 'test-model-1' },
    }),
    'http',
    { kind: 'local' },
  );
  await settle();
  const id = p.ask('What is in the garden?');
  await settle();
  let request = p.coordinator.read(id, { kind: 'local' });
  assert.equal(
    request?.status,
    'waiting_for_human',
    'no silent fall back and no failure left alone',
  );
  const failed = request?.steps.find((step) => step.tool_name === 'model');
  assert.match(failed?.summary ?? '', /refused the stored key/);
  const offer = request?.steps.find((step) => step.tool_name === 'use_this_computer');
  assert.equal(offer?.status, 'waiting');
  assert.equal(offer?.proposal?.command_type, 'assistant.model.choose');
  assert.equal(p.counts().hosted, 1);
  // The refused key is not sent again: a status read and a new request spend nothing more.
  const status = await p.assistant.status();
  assert.equal(status.availability === 'unavailable' && status.reason.code, 'key_refused');
  assert.equal(p.counts().hosted, 1);
  // The one press: the request goes on, on this computer's model.
  p.h.controlPlane.commands.submit(
    p.envelope('workspace.request.resume', {
      request_id: id,
      text: null,
      confirm: { step_id: offer?.step_id, review: offer?.proposal?.review },
    }),
    'http',
    { kind: 'local' },
  );
  await settle();
  assert.equal(p.h.controlPlane.projection.assistantModel(), null);
  request = p.coordinator.read(id, { kind: 'local' });
  assert.equal(request?.status, 'responded');
  assert.equal(request?.reply?.served, 'this_mac');
  assert.deepEqual(p.counts(), { hosted: 1, local: 1 });
});

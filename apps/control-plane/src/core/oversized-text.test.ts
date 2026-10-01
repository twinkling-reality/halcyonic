import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, realpathSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import type { CommandEnvelope, RuntimeDescriptor, RuntimeId } from '@halcyonic/contracts';
import type { RuntimeAdapter } from '@halcyonic/runtime-core';
import { RealtimeClient } from '../client/realtime-client.ts';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { createHostLocations } from '../locations.ts';
import { createTestControlPlane, startTestServer, TEST_CLIENT } from '../testing/harness.ts';

const FEATURE = DEMO_WORKSTREAMS[0] as (typeof DEMO_WORKSTREAMS)[number];
const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-oversized-')));
after(() => rmSync(base, { recursive: true, force: true }));

/** A root far longer than the 2000 characters a refusal's message may hold. */
const LONG_ROOT = `/${'a'.repeat(2100)}`;

describe('text a client sends cannot take the control plane down', () => {
  test('a refusal quoting a very long root is recorded, over REST and over realtime', async () => {
    const root = join(base, 'rest-and-realtime');
    mkdirSync(root);
    const server = await startTestServer({ locations: createHostLocations([root]) });
    try {
      const command = server.commands.createProject('Long root', {
        kind: 'new_folder',
        root: LONG_ROOT,
        folder_name: 'app',
      });
      const response = await fetch(`${server.baseUrl}/api/commands`, {
        method: 'POST',
        headers: { authorization: `Bearer ${server.token}`, 'content-type': 'application/json' },
        body: JSON.stringify(command),
      });
      assert.equal(response.status, 422);
      const body = (await response.json()) as {
        command: { rejection: { code: string; message: string } };
      };
      assert.equal(body.command.rejection.code, 'location_not_allowed');
      assert.ok(body.command.rejection.message.length <= 2000);

      const client = await RealtimeClient.connect(server.wsUrl, server.token);
      client.hello(TEST_CLIENT);
      await client.waitFor((message) => message.type === 'snapshot');
      const ack = async (sent: CommandEnvelope) => {
        client.command(sent);
        return client.waitFor(
          (message) => message.type === 'command_ack' && message.command_id === sent.command_id,
        );
      };
      const refused = await ack(
        server.commands.createProject('Long root again', {
          kind: 'existing_folder',
          root: LONG_ROOT,
          folder_name: null,
        }),
      );
      assert.equal(refused.type === 'command_ack' && refused.disposition, 'rejected');
      // The control plane is still there, and the same connection still works.
      const next = await ack(server.commands.createProject('Still here'));
      assert.equal(next.type === 'command_ack' && next.disposition, 'accepted');
      await client.close();
    } finally {
      await server.stop();
    }
  });

  test('a very long scenario name is refused by the mock runtime in bounded words', async () => {
    const harness = createTestControlPlane();
    const { controlPlane, commands } = harness;
    const project = controlPlane.commands.submit(commands.createProject('P'), 'internal');
    const projectId =
      project.command?.result?.kind === 'project_created'
        ? project.command.result.project_id
        : null;
    const workstream = controlPlane.commands.submit(
      commands.createWorkstream(projectId as never, FEATURE),
      'internal',
    );
    const workstreamId =
      workstream.command?.result?.kind === 'workstream_created'
        ? workstream.command.result.workstream_id
        : null;
    const refused = controlPlane.commands.submit(
      commands.startExecution(workstreamId as never, { ...FEATURE, scenario: 'x'.repeat(3000) }),
      'internal',
    );
    assert.equal(refused.command?.rejection?.code, 'invalid_runtime_options');
    assert.ok((refused.command?.rejection?.message.length ?? 0) < 400);
    await controlPlane.close();
  });

  test("an adapter's refusal longer than the contract allows is cut with an ellipsis and recorded", async () => {
    const descriptor: RuntimeDescriptor = {
      runtime_id: 'wordy' as RuntimeId,
      kind: 'stub',
      display_name: 'Wordy stub',
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
      uses_project_location: false,
    };
    const wordy: RuntimeAdapter = {
      descriptor,
      validateStartOptions: () => ({ ok: false, message: `Refused: ${'🙂'.repeat(3000)}` }),
      startExecution: async () => ({ native_id: null }),
      close: async () => {},
    };
    const { controlPlane, commands } = createTestControlPlane({ adapters: () => [wordy] });
    const project = controlPlane.commands.submit(commands.createProject('P'), 'internal');
    const projectId =
      project.command?.result?.kind === 'project_created'
        ? project.command.result.project_id
        : null;
    const workstream = controlPlane.commands.submit(
      commands.createWorkstream(projectId as never, FEATURE),
      'internal',
    );
    const workstreamId =
      workstream.command?.result?.kind === 'workstream_created'
        ? workstream.command.result.workstream_id
        : null;
    const refused = controlPlane.commands.submit(
      commands.startExecution(workstreamId as never, FEATURE, 'wordy' as RuntimeId),
      'internal',
    );
    const message = refused.command?.rejection?.message ?? '';
    assert.equal(refused.command?.rejection?.code, 'invalid_runtime_options');
    assert.equal(message.length, 2000);
    assert.ok(message.endsWith('…'));
    assert.ok(!/[\uD800-\uDBFF]…$/.test(message), 'no surrogate pair is split');
    await controlPlane.close();
  });

  test('a command the control plane fails to handle answers an error and keeps the connection', async () => {
    const server = await startTestServer();
    const client = await RealtimeClient.connect(server.wsUrl, server.token);
    try {
      client.hello(TEST_CLIENT);
      await client.waitFor((message) => message.type === 'snapshot');
      const submit = server.controlPlane.commands.submit.bind(server.controlPlane.commands);
      server.controlPlane.commands.submit = () => {
        throw new Error('simulated failure while handling a command');
      };
      const broken = server.commands.createProject('Broken');
      client.command(broken);
      const error = await client.waitFor((message) => message.type === 'error');
      assert.deepEqual(error.type === 'error' && [error.error.code, error.fatal], [
        'command_not_handled',
        false,
      ]);
      server.controlPlane.commands.submit = submit;
      const next = server.commands.createProject('After');
      client.command(next);
      const ack = await client.waitFor(
        (message) => message.type === 'command_ack' && message.command_id === next.command_id,
      );
      assert.equal(ack.type === 'command_ack' && ack.disposition, 'accepted');
    } finally {
      await client.close();
      await server.stop();
    }
  });
});

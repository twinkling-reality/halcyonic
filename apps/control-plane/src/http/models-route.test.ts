import assert from 'node:assert/strict';
import { after, before, describe, test } from 'node:test';
import {
  compileValidator,
  type RuntimeId,
  type RuntimeModel,
  RuntimeModelsResponse,
} from '@halcyonic/contracts';
import { MOCK_MODELS, MockRuntimeAdapter } from '@halcyonic/integration-mock';
import { RuntimeActionError, type RuntimeAdapter } from '@halcyonic/runtime-core';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { SCENARIOS, startTestServer } from '../testing/harness.ts';

const validateResponse = compileValidator(RuntimeModelsResponse);
const FEATURE = DEMO_WORKSTREAMS[0] as (typeof DEMO_WORKSTREAMS)[number];

/** A runtime that lists its models through `listModels`, however that behaves. */
function listingRuntime(
  runtimeId: string,
  listModels: () => Promise<readonly RuntimeModel[]>,
): RuntimeAdapter {
  return {
    descriptor: {
      runtime_id: runtimeId as RuntimeId,
      kind: 'test',
      display_name: `Test ${runtimeId}`,
      synthetic: true,
      capabilities: {
        start_execution: false,
        instruct_at_rest: false,
        instruct_while_running: false,
        respond_to_approval: false,
        answer_question: false,
        interrupt: false,
      },
      model_choice: 'listed',
      uses_project_location: false,
      reports_tool_activity: false,
    },
    validateStartOptions: () => ({ ok: true }),
    listModels,
    close: async () => {},
  };
}

/** A value the control plane holds, which a runtime's failure repeats. */
const HELD = 'gateway-secret-value-7';

let listCalls = 0;
let server: Awaited<ReturnType<typeof startTestServer>>;

before(async () => {
  server = await startTestServer({
    modelListTimeoutMs: 200,
    secrets: () => [{ what: 'GATEWAY_KEY', value: HELD }],
    adapters: (time) => [
      new MockRuntimeAdapter({
        scenarios: SCENARIOS,
        models: MOCK_MODELS,
        clock: time,
        scheduler: time,
      }),
      new MockRuntimeAdapter({
        scenarios: SCENARIOS,
        runtimeId: 'plain' as RuntimeId,
        clock: time,
        scheduler: time,
      }),
      listingRuntime('refusing', async () => {
        listCalls += 1;
        throw new RuntimeActionError('runtime_unavailable', 'The runtime could not be started.');
      }),
      listingRuntime('broken', async () => {
        throw new Error('unexpected');
      }),
      listingRuntime('leaking', async () => {
        throw new RuntimeActionError(
          'provider_error',
          `401 from the gateway: ${HELD} and sk-proj-AbCdEf0123456789xyzQRS were refused`,
        );
      }),
      listingRuntime('stalled', () => new Promise(() => {})),
      listingRuntime('malformed', async () => [
        { model_ref: 'has space', display_name: '', served: 'nowhere' } as unknown as RuntimeModel,
      ]),
    ],
  });
});
after(async () => {
  await server.stop();
});

async function models(runtimeId: string, token: string | null = server.token) {
  const response = await fetch(`${server.baseUrl}/api/runtimes/${runtimeId}/models`, {
    headers: token === null ? {} : { authorization: `Bearer ${token}` },
  });
  return { status: response.status, body: (await response.json()) as Record<string, unknown> };
}

describe('GET /api/runtimes/:runtime_id/models', () => {
  test('answers the list the runtime gives, read through and never journaled', async () => {
    const head = server.controlPlane.journal.head();
    const response = await models('mock');
    assert.equal(response.status, 200);
    assert.ok(validateResponse(response.body).ok);
    assert.deepEqual(response.body, {
      runtime_id: 'mock',
      result: { availability: 'available', models: MOCK_MODELS },
    });
    assert.equal(server.controlPlane.journal.head(), head);
  });

  test('says in words why a runtime could not list its models', async () => {
    const calls = listCalls;
    const refused = await models('refusing');
    assert.equal(refused.status, 200);
    assert.ok(validateResponse(refused.body).ok);
    assert.deepEqual(refused.body.result, {
      availability: 'unavailable',
      reason: { code: 'runtime_unavailable', message: 'The runtime could not be started.' },
    });
    // Nothing is cached: every request asks the runtime again.
    await models('refusing');
    assert.equal(listCalls, calls + 2);
    // Error text, so it loses what Halcyonic holds, named, and every credential shape.
    assert.deepEqual((await models('leaking')).body.result, {
      availability: 'unavailable',
      reason: {
        code: 'provider_error',
        message: '401 from the gateway: [redacted: GATEWAY_KEY] and [redacted] were refused',
      },
    });
    assert.deepEqual((await models('broken')).body.result, {
      availability: 'unavailable',
      reason: { code: 'adapter_error', message: 'The runtime adapter failed to list its models.' },
    });
    assert.deepEqual((await models('stalled')).body.result, {
      availability: 'unavailable',
      reason: { code: 'timeout', message: 'The runtime did not list its models within 200 ms.' },
    });
    // A list outside the contract is not passed on.
    assert.deepEqual((await models('malformed')).body.result, {
      availability: 'unavailable',
      reason: {
        code: 'invalid_models',
        message: 'The runtime listed models outside the contract.',
      },
    });
  });

  test('a runtime without a choice of model, an unknown one and a bad id are refused', async () => {
    const plain = await models('plain');
    assert.equal(plain.status, 404);
    assert.equal((plain.body.error as { code: string }).code, 'models_not_listed');
    const unknown = await models('nope');
    assert.equal(unknown.status, 404);
    assert.equal((unknown.body.error as { code: string }).code, 'runtime_not_found');
    assert.equal((await models('Not-An-Id')).status, 400);
    assert.equal((await models('mock', null)).status, 401);
  });
});

describe('starting work on a chosen model', () => {
  async function workstream(): Promise<string> {
    const { controlPlane, commands } = server;
    const project = controlPlane.commands.submit(commands.createProject('Models'), 'internal');
    if (project.command?.result?.kind !== 'project_created') throw new Error('no project');
    const created = controlPlane.commands.submit(
      commands.createWorkstream(project.command.result.project_id, FEATURE),
      'internal',
    );
    if (created.command?.result?.kind !== 'workstream_created') throw new Error('no workstream');
    return created.command.result.workstream_id;
  }

  async function submit(runtimeId: string, modelRef: string | null) {
    const workstreamId = await workstream();
    const start = server.commands.startExecution(workstreamId as never, FEATURE);
    const command = {
      ...start,
      payload: { ...start.payload, runtime_id: runtimeId as RuntimeId, model_ref: modelRef },
    };
    const response = await fetch(`${server.baseUrl}/api/commands`, {
      method: 'POST',
      headers: { authorization: `Bearer ${server.token}`, 'content-type': 'application/json' },
      body: JSON.stringify(command),
    });
    return {
      status: response.status,
      body: (await response.json()) as { command: { rejection: unknown; execution_id: unknown } },
      workstreamId,
    };
  }

  test('a listed model is started, and the model the runtime reports is recorded as observed', async () => {
    const { status, workstreamId } = await submit('mock', 'mock/fast');
    assert.equal(status, 202);
    const executionId =
      server.controlPlane.projection.workstream(workstreamId)?.current_execution_id;
    assert.ok(typeof executionId === 'string');
    assert.equal(server.controlPlane.projection.execution(executionId)?.model_ref, 'mock/fast');
    const reported = server.controlPlane.journal
      .read({ after: 0, limit: 1000, workstreamId })
      .map((stored) => stored.event)
      .filter((event) => event.event_type === 'runtime.model.used');
    assert.deepEqual(
      reported.map((event) => [event.payload, event.provenance.epistemic]),
      [[{ model_ref: 'mock/fast' }, 'observed']],
    );
  });

  test('a choice for a runtime that offers none, or of a model it does not list, is refused', async () => {
    const plain = await submit('plain', 'mock/fast');
    assert.equal(plain.status, 422);
    assert.deepEqual(plain.body.command.rejection, {
      code: 'capability_unsupported',
      message: 'Runtime plain does not offer a choice of model.',
    });
    const unlisted = await submit('mock', 'mock/gone');
    assert.equal(unlisted.status, 422);
    assert.deepEqual(unlisted.body.command.rejection, {
      code: 'invalid_runtime_options',
      message: 'The mock runtime lists no model mock/gone.',
    });
  });
});

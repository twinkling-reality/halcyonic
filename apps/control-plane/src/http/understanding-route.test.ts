import assert from 'node:assert/strict';
import { after, before, describe, test } from 'node:test';
import {
  compileValidator,
  type ExecutionId,
  type RuntimeId,
  UnderstandingResponse,
  type UnderstandingResult,
} from '@halcyonic/contracts';
import { MockRuntimeAdapter } from '@halcyonic/integration-mock';
import type { RuntimeAdapter } from '@halcyonic/runtime-core';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { SCENARIOS, startTestServer } from '../testing/harness.ts';

const validateResponse = compileValidator(UnderstandingResponse);
const FEATURE = DEMO_WORKSTREAMS[0] as (typeof DEMO_WORKSTREAMS)[number];

/** A runtime that accepts a start and never reports a session, so the execution has no native id. */
const STALLED: RuntimeAdapter = {
  descriptor: {
    runtime_id: 'stalled' as RuntimeId,
    kind: 'stalled',
    display_name: 'Stalled runtime',
    synthetic: true,
    capabilities: {
      start_execution: true,
      instruct_at_rest: false,
      instruct_while_running: false,
      respond_to_approval: false,
      interrupt: false,
    },
    model_choice: 'none',
    uses_project_location: false,
  },
  validateStartOptions: () => ({ ok: true }),
  startExecution: () => new Promise(() => {}),
  close: async () => {},
};

let answer: UnderstandingResult;
const asked: [string, string][] = [];
let server: Awaited<ReturnType<typeof startTestServer>>;

before(async () => {
  server = await startTestServer({
    adapters: (time) => [
      new MockRuntimeAdapter({ scenarios: SCENARIOS, clock: time, scheduler: time }),
      STALLED,
    ],
    understanding: {
      understand: async (runtimeKind, nativeId) => {
        asked.push([runtimeKind, nativeId]);
        return answer;
      },
    },
  });
});
after(async () => {
  await server.stop();
});

async function understanding(executionId: string) {
  const response = await fetch(`${server.baseUrl}/api/executions/${executionId}/understanding`, {
    headers: { authorization: `Bearer ${server.token}` },
  });
  return { status: response.status, body: (await response.json()) as Record<string, unknown> };
}

/** Starts an execution on the mock runtime, or on the stalled one. */
function startExecution(runtimeId = 'mock'): ExecutionId {
  const { controlPlane, commands } = server;
  const project = controlPlane.commands.submit(commands.createProject('Understanding'), 'internal');
  if (project.command?.result?.kind !== 'project_created') throw new Error('no project');
  const workstream = controlPlane.commands.submit(
    commands.createWorkstream(project.command.result.project_id, FEATURE),
    'internal',
  );
  if (workstream.command?.result?.kind !== 'workstream_created') throw new Error('no workstream');
  const workstreamId = workstream.command.result.workstream_id;
  const start = commands.startExecution(workstreamId, FEATURE);
  const options = runtimeId === 'mock' ? start.payload.options : {};
  controlPlane.commands.submit(
    { ...start, payload: { ...start.payload, runtime_id: runtimeId as RuntimeId, options } },
    'internal',
  );
  const executionId = controlPlane.projection.workstream(workstreamId)?.current_execution_id;
  if (executionId === null || executionId === undefined) throw new Error('no execution');
  return executionId;
}

describe('GET /api/executions/:id/understanding', () => {
  test('refuses a malformed id and answers 404 for an unknown execution', async () => {
    assert.equal((await understanding('not-an-id')).status, 400);
    const unknown = await understanding('01a0dcf1-5a80-7000-8000-000000000000');
    assert.equal(unknown.status, 404);
    assert.deepEqual((unknown.body.error as { code: string }).code, 'execution_not_found');
  });

  test('says not found, without asking the provider, until the runtime reports its session', async () => {
    const executionId = startExecution('stalled');
    const askedBefore = asked.length;
    const response = await understanding(executionId);
    assert.equal(response.status, 200);
    assert.ok(validateResponse(response.body).ok);
    assert.deepEqual(response.body.result, {
      availability: 'not_found',
      reason: {
        code: 'native_id_unknown',
        message: 'The runtime has not reported its session id yet.',
      },
    });
    assert.equal(asked.length, askedBefore);
  });

  test('asks the provider by runtime kind and native session id and passes the answer through', async () => {
    const executionId = startExecution();
    await server.time.advance(1);
    const nativeId = server.controlPlane.projection.execution(executionId)?.native_id;
    assert.ok(nativeId);
    answer = {
      availability: 'unavailable',
      reason: { code: 'not_running', message: 'Salidium is not running.' },
    };
    const response = await understanding(executionId);
    assert.deepEqual(asked.at(-1), ['mock', nativeId]);
    assert.deepEqual(response.body, { execution_id: executionId, result: answer });
    assert.ok(validateResponse(response.body).ok);
  });

  test('an answer outside the contract is reported as incompatible rather than passed on', async () => {
    const executionId = startExecution();
    await server.time.advance(1);
    answer = {
      availability: 'not_found',
      reason: { code: 'Not Snake Case', message: 'x' },
    };
    const response = await understanding(executionId);
    assert.deepEqual(response.body.result, {
      availability: 'incompatible',
      reason: {
        code: 'invalid_understanding',
        message: 'The understanding provider returned data outside the contract.',
      },
    });
  });
});

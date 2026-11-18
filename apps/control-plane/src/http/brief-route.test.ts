import assert from 'node:assert/strict';
import { after, before, describe, test } from 'node:test';
import {
  compileValidator,
  type ExecutionId,
  type RuntimeId,
  UnderstandingBriefResponse,
  type UnderstandingBriefResult,
} from '@halcyonic/contracts';
import { MockRuntimeAdapter } from '@halcyonic/integration-mock';
import type { RuntimeAdapter } from '@halcyonic/runtime-core';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import type { UnderstandingSource } from '../intelligence/understanding.ts';
import { SCENARIOS, startTestServer } from '../testing/harness.ts';

const validateResponse = compileValidator(UnderstandingBriefResponse);
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
      answer_question: false,
      interrupt: false,
    },
    model_choice: 'none',
    uses_project_location: false,
    reports_tool_activity: false,
  },
  validateStartOptions: () => ({ ok: true }),
  startExecution: () => new Promise(() => {}),
  close: async () => {},
};

let answer: UnderstandingBriefResult;
const asked: [string, string][] = [];
const source: UnderstandingSource = {
  understand: async () => ({
    availability: 'unavailable',
    reason: { code: 'not_running', message: 'Salidium is not running.' },
  }),
  brief: async (runtimeKind, nativeId) => {
    asked.push([runtimeKind, nativeId]);
    return answer;
  },
};
let server: Awaited<ReturnType<typeof startTestServer>>;

before(async () => {
  server = await startTestServer({
    adapters: (time) => [
      new MockRuntimeAdapter({ scenarios: SCENARIOS, clock: time, scheduler: time }),
      STALLED,
    ],
    understanding: source,
  });
});
after(async () => {
  await server.stop();
});

async function brief(executionId: string) {
  const response = await fetch(`${server.baseUrl}/api/executions/${executionId}/brief`, {
    headers: { authorization: `Bearer ${server.token}` },
  });
  return { status: response.status, body: (await response.json()) as Record<string, unknown> };
}

/** Starts an execution on the mock runtime, or on the stalled one. */
function startExecution(runtimeId = 'mock'): ExecutionId {
  const { controlPlane, commands } = server;
  const project = controlPlane.commands.submit(commands.createProject('Brief'), 'internal');
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

describe('GET /api/executions/:id/brief', () => {
  test('refuses a malformed id and answers 404 for an unknown execution', async () => {
    assert.equal((await brief('not-an-id')).status, 400);
    const unknown = await brief('01a0dcf1-5a80-7000-8000-000000000000');
    assert.equal(unknown.status, 404);
    assert.deepEqual((unknown.body.error as { code: string }).code, 'execution_not_found');
  });

  test('says not found, without asking the provider, until the runtime reports its session', async () => {
    const executionId = startExecution('stalled');
    const askedBefore = asked.length;
    const response = await brief(executionId);
    assert.equal(response.status, 200);
    assert.ok(validateResponse(response.body).ok);
    assert.equal(
      (response.body.result as { reason: { code: string } }).reason.code,
      'native_id_unknown',
    );
    assert.equal(asked.length, askedBefore);
  });

  test('asks the provider by runtime kind and native session id and passes the brief through', async () => {
    const executionId = startExecution();
    await server.time.advance(1);
    const nativeId = server.controlPlane.projection.execution(executionId)?.native_id;
    assert.ok(nativeId);
    answer = {
      availability: 'available',
      brief: {
        source: {
          system: 'salidium',
          synthetic: false,
          version: '0.9.0',
          contract: { name: 'salidium.consumer', major: 1, minor: 2 },
          instance_id: '0123456789abcdef0123456789abcdef',
          generated_at: null,
        },
        archetype: 'verified-edit',
        moves: ['action-scope', 'epistemic-verification'].map((kind) => ({
          kind: kind as 'action-scope',
          epistemic: 'observed' as const,
          plain_text: 'Edited orders.ts',
          segments: [
            {
              kind: 'text' as const,
              text: 'Edited orders.ts',
              token: null,
              label: null,
              detail: null,
              epistemic: null,
              tone: null,
              paths: [],
              lines_added: null,
              lines_removed: null,
              check_outcomes: [],
            },
          ],
        })),
        footprint: null,
      },
    };
    const response = await brief(executionId);
    assert.deepEqual(asked.at(-1), ['mock', nativeId]);
    assert.deepEqual(response.body, { execution_id: executionId, result: answer });
    assert.ok(validateResponse(response.body).ok);
  });

  test('a brief outside the contract is reported as incompatible rather than passed on', async () => {
    const executionId = startExecution();
    await server.time.advance(1);
    answer = { availability: 'not_found', reason: { code: 'Not Snake Case', message: 'x' } };
    const response = await brief(executionId);
    assert.deepEqual(response.body.result, {
      availability: 'incompatible',
      reason: {
        code: 'invalid_brief',
        message: 'The understanding provider returned a brief outside the contract.',
      },
    });
  });

  test('says the brief is not read where the provider composes none', async () => {
    const executionId = startExecution();
    await server.time.advance(1);
    const { brief: composes } = source;
    delete source.brief;
    try {
      const response = await brief(executionId);
      assert.ok(validateResponse(response.body).ok);
      assert.deepEqual(response.body.result, {
        availability: 'unavailable',
        reason: {
          code: 'brief_not_read',
          message: 'This understanding provider composes no brief.',
        },
      });
    } finally {
      if (composes !== undefined) source.brief = composes;
    }
  });
});

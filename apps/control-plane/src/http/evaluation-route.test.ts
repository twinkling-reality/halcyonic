import assert from 'node:assert/strict';
import { after, before, describe, test } from 'node:test';
import {
  compileValidator,
  ESTIMATED_COST_NOTE,
  type Evaluation,
  EvaluationResponse,
  type EvaluationResult,
  type ExecutionId,
  type RuntimeId,
} from '@halcyonic/contracts';
import { MockRuntimeAdapter } from '@halcyonic/integration-mock';
import type { RuntimeAdapter } from '@halcyonic/runtime-core';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { SCENARIOS, startTestServer } from '../testing/harness.ts';

const validateResponse = compileValidator(EvaluationResponse);
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
  },
  validateStartOptions: () => ({ ok: true }),
  startExecution: () => new Promise(() => {}),
  close: async () => {},
};

const READ: Pick<Evaluation['cost'], 'availability' | 'coverage' | 'freshness'> = {
  availability: { state: 'available', reason: null },
  coverage: {
    requested: { from: '2026-06-28', through: '2026-09-26' },
    observed: { from: '2026-09-26', through: '2026-09-26' },
    matched_sessions: 1,
    included_sessions: 1,
    complete: true,
    omissions: [],
  },
  freshness: {
    state: 'fresh',
    generated_at: '2026-09-26T18:00:00.000Z',
    data_through: '2026-09-26T17:58:12.000Z',
    stale_at: '2026-09-26T18:05:00.000Z',
  },
};

/** An evaluation of a session that has not matured: unknowns stay null, and zero is measured. */
const EVALUATION: Evaluation = {
  source: { system: 'seorak', synthetic: false, api_version: 'v1' },
  cost: { ...READ, estimated_usd: 1.37, note: ESTIMATED_COST_NOTE },
  outcome: {
    ...READ,
    measure: {
      commits_landed: null,
      uncommitted: null,
      line_survival: null,
      error_count: 0,
      first_error_at: null,
      end_reason: 'prompt_input_exit',
    },
  },
  verification: {
    ...READ,
    lens: { by_kind: [], empty_reason: 'No verification result was captured.' },
  },
};
const AVAILABLE: EvaluationResult = { availability: 'available', evaluation: EVALUATION };

let answer: EvaluationResult;
const asked: [string, string][] = [];
let server: Awaited<ReturnType<typeof startTestServer>>;

before(async () => {
  server = await startTestServer({
    adapters: (time) => [
      new MockRuntimeAdapter({ scenarios: SCENARIOS, clock: time, scheduler: time }),
      STALLED,
    ],
    evaluation: {
      evaluate: async (runtimeKind, nativeId) => {
        asked.push([runtimeKind, nativeId]);
        return answer;
      },
    },
  });
});
after(async () => {
  await server.stop();
});

async function evaluation(executionId: string) {
  const response = await fetch(`${server.baseUrl}/api/executions/${executionId}/evaluation`, {
    headers: { authorization: `Bearer ${server.token}` },
  });
  return { status: response.status, body: (await response.json()) as Record<string, unknown> };
}

/** Starts an execution on the mock runtime, or on the stalled one. */
function startExecution(runtimeId = 'mock'): ExecutionId {
  const { controlPlane, commands } = server;
  const project = controlPlane.commands.submit(commands.createProject('Evaluation'), 'internal');
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

/** Starts an execution on the mock runtime and lets it report its session. */
async function startReportedExecution(): Promise<{ executionId: ExecutionId; nativeId: string }> {
  const executionId = startExecution();
  await server.time.advance(1);
  const nativeId = server.controlPlane.projection.execution(executionId)?.native_id;
  assert.ok(nativeId);
  return { executionId, nativeId };
}

describe('GET /api/executions/:id/evaluation', () => {
  test('refuses a malformed id and answers 404 for an unknown execution', async () => {
    assert.equal((await evaluation('not-an-id')).status, 400);
    const unknown = await evaluation('01a0dcf1-5a80-7000-8000-000000000000');
    assert.equal(unknown.status, 404);
    assert.deepEqual((unknown.body.error as { code: string }).code, 'execution_not_found');
  });

  test('says not found, without asking the provider, until the runtime reports its session', async () => {
    const executionId = startExecution('stalled');
    const askedBefore = asked.length;
    const response = await evaluation(executionId);
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
    const { executionId, nativeId } = await startReportedExecution();
    answer = {
      availability: 'unauthorized',
      reason: { code: 'credential_missing', message: 'No Seorak credential is configured.' },
    };
    const refused = await evaluation(executionId);
    assert.deepEqual(asked.at(-1), ['mock', nativeId]);
    assert.deepEqual(refused.body, { execution_id: executionId, result: answer });
    assert.ok(validateResponse(refused.body).ok);

    answer = AVAILABLE;
    const measured = await evaluation(executionId);
    assert.equal(measured.status, 200);
    assert.deepEqual(measured.body, { execution_id: executionId, result: AVAILABLE });
    assert.ok(validateResponse(measured.body).ok);
  });

  test('an answer outside the contract is reported as incompatible rather than passed on', async () => {
    const { executionId } = await startReportedExecution();
    const scored = { ...EVALUATION, score: 0.9 };
    for (const outside of [
      { availability: 'not_found', reason: { code: 'Not Snake Case', message: 'x' } },
      { availability: 'available', evaluation: scored },
    ]) {
      answer = outside as EvaluationResult;
      const response = await evaluation(executionId);
      assert.deepEqual(response.body.result, {
        availability: 'incompatible',
        reason: {
          code: 'invalid_evaluation',
          message: 'The evaluation provider returned data outside the contract.',
        },
      });
    }
  });
});

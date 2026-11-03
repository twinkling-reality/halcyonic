/**
 * Opt-in and live: a real session's understanding and evaluation, and the usage limits, read
 * through the control plane's own routes from the Salidium and Seorak running on this Mac
 * (salidium-consumer-contract.md and seorak-integration-api.md, 2026-11-03). The execution is a
 * synthetic stand-in's, which runs nothing: only its native id, a Claude Code session the person
 * names, is real. It prints shapes and counts, never a session's words or paths, nor a credential.
 *
 *   HALCYONIC_LIVE_SESSION_ID=<a Claude Code session id> \
 *   HALCYONIC_SALIDIUM_CREDENTIAL_FILE=<file> HALCYONIC_SEORAK_CREDENTIAL_FILE=<file> \
 *   node --test apps/control-plane/src/intelligence/live-routes.test.ts
 *
 * Six of Seorak's 60 requests a minute and three Salidium reads.
 */
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, describe, test } from 'node:test';
import type {
  EvaluationResponse,
  ExecutionId,
  RuntimeId,
  UnderstandingResponse,
  UsageLimitsResponse,
} from '@halcyonic/contracts';
import { defaultSalidiumHome } from '@halcyonic/integration-salidium';
import type { RuntimeAdapter, StartExecutionRequest } from '@halcyonic/runtime-core';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { startTestServer } from '../testing/harness.ts';
import { seorakEvaluation } from './evaluation.ts';
import { salidiumUnderstanding } from './understanding.ts';

const session = process.env.HALCYONIC_LIVE_SESSION_ID;
const salidiumCredential = process.env.HALCYONIC_SALIDIUM_CREDENTIAL_FILE;
const seorakCredential = process.env.HALCYONIC_SEORAK_CREDENTIAL_FILE;
const skip =
  session === undefined || salidiumCredential === undefined || seorakCredential === undefined
    ? 'set HALCYONIC_LIVE_SESSION_ID, HALCYONIC_SALIDIUM_CREDENTIAL_FILE and HALCYONIC_SEORAK_CREDENTIAL_FILE'
    : false;
const WORK = DEMO_WORKSTREAMS[0] as (typeof DEMO_WORKSTREAMS)[number];

/**
 * A synthetic stand-in for Claude Agent that runs nothing: it reports the session its instruction
 * names as its own, so the routes ask the real sources about a real session.
 */
function standIn(now: () => Date): RuntimeAdapter {
  return {
    descriptor: {
      runtime_id: 'claude-agent' as RuntimeId,
      kind: 'claude-agent',
      display_name: 'Claude Agent (synthetic stand-in for a live read)',
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
    async startExecution(request: StartExecutionRequest) {
      const nativeId = request.instruction;
      request.emit({
        native_event_id: `${nativeId}:started`,
        sequence: 1,
        occurred_at: now().toISOString() as never,
        type: 'runtime.execution.started',
        payload: { native_id: nativeId },
        provenance: { epistemic: 'observed', native_type: 'stand-in/started' },
      });
      return { native_id: nativeId };
    },
    close: async () => {},
  };
}

describe('live reads through the routes (opt-in)', { skip }, () => {
  let scratch: string;
  let server: Awaited<ReturnType<typeof startTestServer>>;
  before(async () => {
    scratch = mkdtempSync(join(tmpdir(), 'halcyonic-live-routes-'));
    server = await startTestServer({
      adapters: (time) => [standIn(() => time.now())],
      understanding: salidiumUnderstanding({
        home: defaultSalidiumHome(),
        credentialPath: salidiumCredential as string,
        codexHome: join(scratch, 'codex-home'),
      }),
      evaluation: seorakEvaluation({
        credentialPath: seorakCredential as string,
        codexHome: join(scratch, 'codex-home'),
      }),
    });
  });
  after(async () => {
    await server?.stop();
    if (scratch !== undefined) rmSync(scratch, { recursive: true, force: true });
  });

  /** Starts the stand-in's execution for one session, by its id. */
  function execution(nativeId: string): ExecutionId {
    const { controlPlane, commands } = server;
    const project = controlPlane.commands.submit(commands.createProject('Live read'), 'internal');
    if (project.command?.result?.kind !== 'project_created') throw new Error('no project');
    const workstream = controlPlane.commands.submit(
      commands.createWorkstream(project.command.result.project_id, WORK),
      'internal',
    );
    if (workstream.command?.result?.kind !== 'workstream_created') throw new Error('no workstream');
    const workstreamId = workstream.command.result.workstream_id;
    const start = commands.startExecution(workstreamId, WORK, 'claude-agent' as RuntimeId);
    controlPlane.commands.submit(
      { ...start, payload: { ...start.payload, instruction: nativeId, options: {} } },
      'internal',
    );
    const id = controlPlane.projection.workstream(workstreamId)?.current_execution_id;
    if (id === null || id === undefined) throw new Error('no execution');
    return id;
  }

  async function read<T>(path: string): Promise<T> {
    const response = await fetch(`${server.baseUrl}${path}`, {
      headers: { authorization: `Bearer ${server.token}` },
    });
    assert.equal(response.status, 200, path);
    return (await response.json()) as T;
  }

  test('a real Claude Code session reads available from Salidium and Seorak', async (t) => {
    const id = execution(session as string);
    await t.test('understanding', async () => {
      const { result } = await read<UnderstandingResponse>(`/api/executions/${id}/understanding`);
      assert.equal(result.availability, 'available');
      if (result.availability !== 'available') return;
      const { source, verdict, changes, verification, revision, explanation } =
        result.understanding;
      assert.equal(source.system, 'salidium');
      assert.equal(source.synthetic, false);
      t.diagnostic(
        JSON.stringify({
          version: source.version,
          contract: source.contract,
          verdict: [verdict.tone, verdict.epistemic],
          files: changes.files.length,
          exact: [...new Set(changes.files.map((file) => file.lines_removed_exact))],
          commits: changes.commits.length,
          runs: verification.latest_by_method.map((run) => [run.method, run.outcome, run.scope]),
          anchors: [revision.at_start !== null, revision.at_latest_turn_end !== null],
          explanation: explanation.status,
        }),
      );
    });
    await t.test('evaluation', async () => {
      const { result } = await read<EvaluationResponse>(`/api/executions/${id}/evaluation`);
      assert.equal(result.availability, 'available');
      if (result.availability !== 'available') return;
      const { source, cost, outcome, verification } = result.evaluation;
      assert.equal(source.system, 'seorak');
      assert.equal(source.synthetic, false);
      t.diagnostic(
        JSON.stringify({
          cost: cost.estimated_usd === null ? null : 'estimated',
          outcome: outcome.availability.state,
          errors: outcome.measure?.error_count ?? null,
          lens:
            verification.lens?.by_kind.map((kind) => [
              kind.label,
              kind.runs,
              kind.passed,
              kind.pass_rate,
            ]) ?? null,
        }),
      );
    });
  });

  test('a session neither has seen reads not found from both, and usage limits read as they are', async (t) => {
    const id = execution(randomUUID());
    const understanding = await read<UnderstandingResponse>(`/api/executions/${id}/understanding`);
    assert.equal(understanding.result.availability, 'not_found');
    const evaluation = await read<EvaluationResponse>(`/api/executions/${id}/evaluation`);
    assert.equal(evaluation.result.availability, 'not_found');
    const limits = await read<UsageLimitsResponse>('/api/usage-limits');
    t.diagnostic(
      JSON.stringify(
        limits.availability === 'available'
          ? { availability: 'available', readings: limits.readings.length }
          : { availability: limits.availability, code: limits.reason.code },
      ),
    );
  });
});
